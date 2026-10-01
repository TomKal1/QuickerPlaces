using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using QuickerPlaces.Models.Workspace;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Services.Workspace;
using QuickerPlaces.ViewModels;
using QuickerPlaces.Views.Panels;

namespace QuickerPlaces.Views;

/// <summary>
/// The workspace's view (configurable canvas plan M3, M4). Builds each panel
/// once, in a <see cref="PanelFrame"/>, and places it on the twelve-column
/// canvas where <see cref="WorkspaceViewModel.Panels"/> says; frames stay on
/// the canvas (a hidden panel's is collapsed), so moving, resizing or hiding
/// a panel keeps its selection, scroll and keyboard focus.
///
/// Arrange mode's pointer work is here: dragging a panel's handle shows
/// where it would go and drops it as one move, dragging its edge shows the
/// width it snaps to and sets it once, and Esc cancels either. Nothing is
/// written while dragging. The canvas's width goes to
/// <see cref="WorkspaceViewModel.Reflow"/>, and its height grows past the
/// window, with a scroll bar, when stacked panels need it.
///
/// Also the glue the view model can't hold without WPF: the toolbar search
/// box's keys, the menus, Recent Files read when the workspace opens and
/// kept current through <see cref="LibraryRefresh"/>, the Library read again
/// when places or sessions change, and a pause before a query change is
/// written. <see cref="Close"/> writes anything waiting and lets go of the
/// tracking hosts.
/// </summary>
public partial class WorkspaceView : UserControl
{
    /// <summary>The pause after the last query change before it is written (plan §4: debounce search and filter changes).</summary>
    private static readonly TimeSpan FlushDelay = TimeSpan.FromSeconds(2);

    /// <summary>A canvas row that shares the height is never shorter than this.</summary>
    private const double MinRowHeight = 220;

    /// <summary>How near the canvas's top or bottom edge a drag scrolls it, and by how much per move.</summary>
    private const double AutoScrollMargin = 40;
    private const double AutoScrollStep = 18;

    private readonly Dictionary<string, PanelFrame> _frames = new();

    /// <summary>A columns layout's two stacks, placed on the canvas across their columns (Desk layout design §2).</summary>
    private readonly Grid _leftStack = new();
    private readonly Grid _mainStack = new();
    private readonly DispatcherTimer _flushTimer;
    private WorkspaceViewModel? _workspace;
    private MainViewModel? _places;
    private SessionStore? _sessions;
    private WindowsOpenDocumentProbe? _probe;
    private RecentFilesHost? _recentFilesHost;
    private LibraryRefresh? _refresh;
    /// <summary>The Library grids made so far: a Recents panel's and the File viewer's (File viewer design §7).</summary>
    private readonly List<FileShelfPanel> _shelves = new();
    private FilesPanel? _filesPanel;

    /// <summary>The shelf on screen, if any: the one the search box's Down key moves into.</summary>
    private FileShelfPanel? ShownShelf => _shelves.FirstOrDefault(s => s.IsVisible);
    private SessionsPanel? _sessionsPanel;
    private ActivityStore? _activityStore;
    private ActivityTrackingHost? _activityHost;
    private INetworkDriveResolver? _networkDrives;
    private Action? _trackingChanged;
    private bool _reloadQueued;
    private bool _loadedOnce;
    private PanelFrame? _moveFrame;
    private string? _dropTargetId;
    private bool _dropAfter;
    private PanelFrame? _resizeFrame;
    private int _resizeSpan;
    private Window? _escapeWindow;

    public WorkspaceView()
    {
        InitializeComponent();
        for (var i = 0; i < PanelSpans.Columns; i++)
            PanelCanvas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        PreviewKeyDown += WorkspaceView_PreviewKeyDown;
        _flushTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = FlushDelay };
        _flushTimer.Tick += (_, _) =>
        {
            _flushTimer.Stop();
            _workspace?.FlushPending();
        };
        Loaded += WorkspaceView_Loaded;
    }

    /// <summary>Connects the view to the workspace, the places and the stores the panels need. Call once, before it is shown.</summary>
    public void Attach(WorkspaceViewModel workspace, MainViewModel places, SessionStore sessions, WindowsOpenDocumentProbe probe,
        RecentFilesHost recentFilesHost, ActivityTrackingHost activityHost, ActivityStore activityStore,
        INetworkDriveResolver networkDrives, Action trackingChanged)
    {
        _workspace = workspace;
        _places = places;
        _sessions = sessions;
        _probe = probe;
        _recentFilesHost = recentFilesHost;
        _activityHost = activityHost;
        _activityStore = activityStore;
        _networkDrives = networkDrives;
        _trackingChanged = trackingChanged;
        DataContext = workspace;

        _refresh = new LibraryRefresh(Dispatcher, workspace.Library, recentFilesHost, activityHost);
        workspace.PanelsChanged += BuildCanvas;
        workspace.QueryPending += RestartFlushTimer;
        workspace.Library.PlaceOpened += places.NotePlaceOpened;
        workspace.PropertyChanged += Workspace_PropertyChanged;
        places.PlacesChanged += RequestReload;
        places.PropertyChanged += Places_PropertyChanged;

        ApplyExpanded();
        BuildCanvas();
    }

    /// <summary>Focuses the search box and selects what is in it: Ctrl+F, the global hotkey, a second launch.</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>
    /// Opens a session through the Sessions panel when it is on screen, as its Open
    /// all would, so the panel shows what happened. False when it isn't shown, and
    /// the caller opens the session itself.
    /// </summary>
    public bool TryOpenInSessionsPanel(string sessionId)
    {
        if (_sessionsPanel is not { IsVisible: true })
            return false;

        _sessionsPanel.RunSessionAction(SessionAction.OpenAll, sessionId);
        return true;
    }

    /// <summary>After a session was opened without the Sessions panel: its Last opened, and so its place in the list, changed.</summary>
    public void NoteSessionOpened()
    {
        _sessionsPanel?.Reload();
        RequestReload();
    }

    /// <summary>Reads the Library's sources again soon: after Recents' window closes, or anything else that changed them.</summary>
    public void RequestReload()
    {
        if (_reloadQueued || _workspace is null)
            return;

        // Several changes in one action (an open records a use, then the
        // Library reads itself again) become one read.
        _reloadQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _reloadQueued = false;
            _workspace?.Library.Reload();
        });
    }

    /// <summary>
    /// At window close: keeps an arrangement in progress (as switching
    /// layouts does), writes a waiting query change and stops listening to
    /// the tracking hosts.
    /// </summary>
    public void Close()
    {
        _flushTimer.Stop();
        _moveFrame?.CancelDrag();
        _resizeFrame?.CancelDrag();
        _workspace?.Done();
        if (_workspace is { HasUnsavedChanges: true } workspace)
        {
            var persistence = workspace.FlushPending();
            if (!persistence.Saved)
                DiagnosticLog.Warn($"The workspace layout couldn't be saved at close: {persistence.UserMessage}");
        }

        _refresh?.Dispose();
        _refresh = null;
    }

    private async void WorkspaceView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce || _workspace is null || _recentFilesHost is null)
            return;
        _loadedOnce = true;

        // As the Library window does: what was opened just now is listed.
        try
        {
            await Task.Run(_recentFilesHost.RecordNow);
            _workspace.Library.Reload();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Refreshing Recent Files for the workspace failed ({ex.GetType().Name}).");
        }
    }

    private void RestartFlushTimer()
    {
        _flushTimer.Stop();
        _flushTimer.Start();
    }

    private void Workspace_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // Searching a hidden workspace would be pointless: bring it back, as list mode does.
            case nameof(WorkspaceViewModel.SearchText) when _workspace is { IsSearching: true } && _places is { IsGridExpanded: false } places:
                places.IsGridExpanded = true;
                break;

            case nameof(WorkspaceViewModel.Status):
                // After the binding has updated the text: then a screen reader reads the new line.
                Dispatcher.BeginInvoke(DispatcherPriority.Background, AnnounceStatus);
                break;

            case nameof(WorkspaceViewModel.IsArranging):
                UpdateFrames();
                Dispatcher.BeginInvoke(DispatcherPriority.Background, FitToolbar);
                break;
        }
    }

    private void AnnounceStatus()
    {
        if (string.IsNullOrEmpty(StatusText.Text))
            return;

        var peer = UIElementAutomationPeer.FromElement(StatusText) ?? UIElementAutomationPeer.CreatePeerForElement(StatusText);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void Places_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsGridExpanded))
            ApplyExpanded();
    }

    private void ApplyExpanded()
    {
        var expanded = _places?.IsGridExpanded ?? true;
        CanvasArea.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    // -----------------------------------------------------------------
    // The canvas
    // -----------------------------------------------------------------

    /// <summary>
    /// Places every shown panel. A rows layout: twelve equal columns, one
    /// grid row per canvas row. A columns layout: a stack per column, each a
    /// grid across its column's span, one row per panel. A row holding only
    /// the year strip takes its own height; any other shares what is left,
    /// never below <see cref="MinRowHeight"/>. Panels are made once; a hidden
    /// one's frame is collapsed, not removed, and a frame changes grid only
    /// when it changes column.
    /// </summary>
    private void BuildCanvas()
    {
        if (_workspace is null)
            return;

        var panels = _workspace.Panels;
        var columns = panels.Any(p => p.Dock != PanelDock.None);
        PanelCanvas.RowDefinitions.Clear();
        _leftStack.RowDefinitions.Clear();
        _mainStack.RowDefinitions.Clear();

        if (columns)
        {
            var left = panels.Where(p => p.Dock == PanelDock.Left).ToList();
            var main = panels.Where(p => p.Dock == PanelDock.Main).ToList();
            SetCanvasColumns(left.Count > 0 && main.Count > 0 ? PanelLayoutEngine.LeftColumnWidth(left.Select(p => p.Type)) : null);
            PlaceStack(_leftStack, left, 0);
            PlaceStack(_mainStack, main, left.Count > 0 && main.Count > 0 ? 1 : 0);
        }
        else
        {
            SetCanvasColumns(null, twelve: true);
            _leftStack.Visibility = Visibility.Collapsed;
            _mainStack.Visibility = Visibility.Collapsed;
            foreach (var row in panels.GroupBy(p => p.Row).OrderBy(g => g.Key))
                PanelCanvas.RowDefinitions.Add(RowFor(fitsContent: row.All(p => p.Type == PanelTypes.Activity)));
        }

        var shown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var panel in panels)
        {
            if (!_frames.TryGetValue(panel.Id, out var frame))
            {
                frame = CreateFrame(panel);
                _frames[panel.Id] = frame;
            }

            Reparent(frame, panel.Dock switch
            {
                PanelDock.Left => _leftStack,
                PanelDock.Main => _mainStack,
                _ => PanelCanvas,
            });
            Grid.SetRow(frame, panel.Row);
            Grid.SetColumn(frame, columns ? 0 : panel.Column);
            Grid.SetColumnSpan(frame, columns ? 1 : panel.Span);
            frame.Visibility = Visibility.Visible;
            shown.Add(panel.Id);
        }

        foreach (var (id, frame) in _frames)
        {
            if (shown.Contains(id))
                continue;

            // Keeps whatever the panel holds for when it comes back.
            frame.Visibility = Visibility.Collapsed;
            Grid.SetRow(frame, 0);
            Grid.SetColumn(frame, 0);
            Grid.SetColumnSpan(frame, 1);
        }

        UpdateFrames();
        var noCalendar = _workspace.Panels.All(p => p.Type != PanelTypes.Activity);
        foreach (var shelf in _shelves)
            shelf.ShowsPeriod = noCalendar;
        if (_sessionsPanel is not null)
        {
            // With a File viewer, its Sessions tab lists the files, so the cards need not (File viewer design §5).
            var hasFiles = _workspace.Panels.Any(p => p.Type == PanelTypes.Files);
            _sessionsPanel.ShowsViewFiles = hasFiles;
            _sessionsPanel.CardsOnly = hasFiles;
        }
        if (_filesPanel is not null)
            _filesPanel.OffersSessionActions = _workspace.Panels.Any(p => p.Type == PanelTypes.Sessions);
        EmptyCanvasText.Visibility = _workspace.HasPanels ? Visibility.Collapsed : Visibility.Visible;
        FitCanvasHeight();
    }

    private static RowDefinition RowFor(bool fitsContent) => fitsContent
        ? new RowDefinition { Height = GridLength.Auto }
        : new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = MinRowHeight };

    /// <summary>
    /// The canvas's grid columns: twelve equal ones for a rows layout. A columns
    /// layout with both columns has a left one <paramref name="leftWidth"/> wide
    /// (one card, plus the gap) and a main one with the rest; with one column
    /// shown, that column has the whole canvas.
    /// </summary>
    private void SetCanvasColumns(double? leftWidth, bool twelve = false)
    {
        PanelCanvas.ColumnDefinitions.Clear();
        if (twelve)
        {
            for (var i = 0; i < PanelSpans.Columns; i++)
                PanelCanvas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return;
        }

        if (leftWidth is { } width)
            PanelCanvas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        PanelCanvas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    }

    /// <summary>One column of a columns layout: a grid across the column's span, one row per panel; collapsed when the column is empty.</summary>
    private void PlaceStack(Grid stack, IReadOnlyList<WorkspacePanelViewModel> panels, int column)
    {
        if (panels.Count == 0)
        {
            stack.Visibility = Visibility.Collapsed;
            return;
        }

        if (stack.Parent is null)
            PanelCanvas.Children.Add(stack);
        Grid.SetRow(stack, 0);
        Grid.SetColumn(stack, column);
        Grid.SetColumnSpan(stack, 1);
        stack.Visibility = Visibility.Visible;
        foreach (var panel in panels)
            stack.RowDefinitions.Add(RowFor(fitsContent: panel.Type == PanelTypes.Activity));
    }

    /// <summary>Moves a frame to another grid only when it must: a move within one keeps the panel loaded, with its selection and scroll (M4).</summary>
    private static void Reparent(PanelFrame frame, Grid parent)
    {
        if (ReferenceEquals(frame.Parent, parent))
            return;

        (frame.Parent as Panel)?.Children.Remove(frame);
        parent.Children.Add(frame);
    }

    /// <summary>Each frame's title, arrows and width choice, and Arrange mode's controls on or off.</summary>
    private void UpdateFrames()
    {
        if (_workspace is null)
            return;

        foreach (var panel in _workspace.Panels)
        {
            if (_frames.TryGetValue(panel.Id, out var frame))
                frame.Update(panel, _workspace.IsArranging, _workspace.IsColumns);
        }
    }

    private PanelFrame CreateFrame(WorkspacePanelViewModel panel)
    {
        var frame = new PanelFrame(panel, CreateContent(panel));
        frame.HideRequested += HidePanel;
        frame.MoveEarlierRequested += f => _workspace?.MoveEarlier(f.PanelId);
        frame.MoveLaterRequested += f => _workspace?.MoveLater(f.PanelId);
        frame.SpanRequested += (f, span) => _workspace?.SetSpan(f.PanelId, span);
        frame.DockRequested += (f, dock) => _workspace?.SetDock(f.PanelId, dock);
        frame.MoveDragStarted += MoveDragStarted;
        frame.MoveDragMoved += MoveDragMoved;
        frame.MoveDragEnded += MoveDragEnded;
        frame.ResizeDragStarted += ResizeDragStarted;
        frame.ResizeDragMoved += ResizeDragMoved;
        frame.ResizeDragEnded += ResizeDragEnded;

        // A row sized to the year strip changes height with it (a coverage line, the month view).
        frame.SizeChanged += (_, _) => FitCanvasHeight();
        return frame;
    }

    /// <summary>Hides a panel. Its frame collapses, taking the focus with it, so the focus goes to Undo (or, arranging, to Done).</summary>
    private void HidePanel(PanelFrame frame)
    {
        if (_workspace is null || !_workspace.HidePanel(frame.PanelId))
            return;

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_workspace.IsArranging)
                DoneButton.Focus();
            else if (StatusUndoButton.IsVisible)
                StatusUndoButton.Focus();
            else
                ArrangeButton.Focus();
        });
    }

    private void CanvasScroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The scroll viewer's full width, not its viewport's: a scroll bar
        // appearing must not reflow the panels, or it could come and go forever.
        if (e.WidthChanged)
            _workspace?.Reflow(CanvasScroller.ActualWidth);
        FitCanvasHeight();
    }

    /// <summary>
    /// The canvas's height: the window's, or more when the rows' least
    /// heights add up to more (in a columns layout, the taller column's), and
    /// the canvas then scrolls. Shared rows get a real height to share, so the
    /// lists in them stay virtualized.
    /// </summary>
    private void FitCanvasHeight()
    {
        var needed = Math.Max(Needed(PanelCanvas), Math.Max(Needed(_leftStack), Needed(_mainStack)));
        var height = Math.Max(CanvasScroller.ActualHeight, needed);
        if (double.IsNaN(PanelCanvas.Height) || Math.Abs(PanelCanvas.Height - height) >= 0.5)
            PanelCanvas.Height = height;
    }

    /// <summary>What a grid's rows need at least: a shared row its minimum, a row sized to the year strip its tallest frame.</summary>
    private static double Needed(Grid grid)
    {
        if (grid.Visibility != Visibility.Visible)
            return 0;

        var needed = 0.0;
        for (var row = 0; row < grid.RowDefinitions.Count; row++)
        {
            var definition = grid.RowDefinitions[row];
            if (!definition.Height.IsAuto)
            {
                needed += definition.MinHeight;
                continue;
            }

            needed += grid.Children.OfType<PanelFrame>()
                .Where(f => f.Visibility == Visibility.Visible && Grid.GetRow(f) == row)
                .Select(f => f.DesiredSize.Height)
                .DefaultIfEmpty(0)
                .Max();
        }

        return needed;
    }

    /// <summary>The frames shown, with their places on the canvas.</summary>
    private IEnumerable<(PanelFrame Frame, Rect Bounds)> ShownFrames()
    {
        foreach (var frame in _frames.Values)
        {
            if (frame.Visibility != Visibility.Visible || !frame.IsLoaded)
                continue;
            yield return (frame, frame.TransformToAncestor(PanelCanvas).TransformBounds(new Rect(frame.RenderSize)));
        }
    }

    // -----------------------------------------------------------------
    // Arrange mode: dragging (D1)
    // -----------------------------------------------------------------

    private void MoveDragStarted(PanelFrame frame)
    {
        _moveFrame = frame;
        _dropTargetId = null;
        frame.Opacity = 0.55;
        WatchEscape();
    }

    /// <summary>
    /// Shows where the dragged panel would go: before or after the panel
    /// nearest the pointer — above or below a full-width panel or any panel of
    /// a columns layout, left or right of any other. Nothing is shown where it would not move.
    /// </summary>
    private void MoveDragMoved(PanelFrame frame)
    {
        if (_workspace is null || _moveFrame != frame)
            return;

        ScrollNearEdge();
        var pointer = Mouse.GetPosition(PanelCanvas);
        var nearest = ShownFrames().OrderBy(f => Distance(f.Bounds, pointer)).FirstOrDefault();
        if (nearest.Frame is null)
        {
            HideDropMarker();
            return;
        }

        var bounds = nearest.Bounds;
        var columns = _workspace.IsColumns;

        // In a column, or beside a full-width panel, a drop goes above or below; otherwise left or right.
        var vertical = columns || nearest.Frame.Panel.Span == PanelSpans.Columns;
        var after = vertical ? pointer.Y > bounds.Top + bounds.Height / 2 : pointer.X > bounds.Left + bounds.Width / 2;
        var possible = columns
            ? _workspace.ColumnDrop(frame.PanelId, nearest.Frame.PanelId, after) is not null
            : PanelLayoutEngine.Drop(_workspace.Panels.Select(p => p.Placement).ToList(), frame.PanelId, nearest.Frame.PanelId, after) is not null;
        if (!possible)
        {
            HideDropMarker();
            return;
        }

        _dropTargetId = nearest.Frame.PanelId;
        _dropAfter = after;

        // In the gap between panels: each frame's margin is half of it.
        var inset = PanelLayoutEngine.Gap / 2;
        if (vertical)
        {
            DropMarker.Width = Math.Max(0, bounds.Width - PanelLayoutEngine.Gap);
            DropMarker.Height = 4;
            Canvas.SetLeft(DropMarker, bounds.Left + inset);
            Canvas.SetTop(DropMarker, (after ? bounds.Bottom - inset : bounds.Top + inset) - 2);
        }
        else
        {
            DropMarker.Width = 4;
            DropMarker.Height = Math.Max(0, bounds.Height - PanelLayoutEngine.Gap);
            Canvas.SetLeft(DropMarker, (after ? bounds.Right - inset : bounds.Left + inset) - 2);
            Canvas.SetTop(DropMarker, bounds.Top + inset);
        }

        DropMarker.Visibility = Visibility.Visible;
    }

    private void MoveDragEnded(PanelFrame frame, bool cancelled)
    {
        var target = _dropTargetId;
        frame.Opacity = 1;
        HideDropMarker();
        _moveFrame = null;
        _dropTargetId = null;
        StopWatchingEscape();

        if (cancelled)
            _workspace?.DragCancelled();
        else if (target is not null && _workspace is { } workspace)
        {
            if (workspace.IsColumns)
                workspace.DropInColumn(frame.PanelId, target, _dropAfter);
            else
                workspace.Drop(frame.PanelId, target, _dropAfter);
        }
    }

    private void HideDropMarker()
    {
        _dropTargetId = null;
        DropMarker.Visibility = Visibility.Collapsed;
    }

    private void ResizeDragStarted(PanelFrame frame)
    {
        _resizeFrame = frame;
        _resizeSpan = frame.Panel.StoredSpan;
        WatchEscape();
        ResizeDragMoved(frame);
    }

    /// <summary>Shows the allowed width nearest the pointer, over the panel, named.</summary>
    private void ResizeDragMoved(PanelFrame frame)
    {
        if (_resizeFrame != frame || PanelCanvas.ActualWidth <= 0)
            return;

        var bounds = frame.TransformToAncestor(PanelCanvas).TransformBounds(new Rect(frame.RenderSize));
        var pointer = Mouse.GetPosition(PanelCanvas);
        _resizeSpan = PanelLayoutEngine.SpanForWidth(Math.Max(0, pointer.X - bounds.Left), PanelCanvas.ActualWidth);

        // A width that no longer fits beside the panels before it moves to the
        // next row when kept; the preview stays on this one, up to its end.
        var width = Math.Min(_resizeSpan * PanelCanvas.ActualWidth / PanelSpans.Columns, PanelCanvas.ActualWidth - bounds.Left);
        var inset = PanelLayoutEngine.Gap / 2;
        ResizePreview.Width = Math.Max(0, width - PanelLayoutEngine.Gap);
        ResizePreview.Height = Math.Max(0, bounds.Height - PanelLayoutEngine.Gap);
        Canvas.SetLeft(ResizePreview, bounds.Left + inset);
        Canvas.SetTop(ResizePreview, bounds.Top + inset);
        var name = PanelSpans.DisplayName(_resizeSpan);
        ResizePreviewText.Text = char.ToUpperInvariant(name[0]) + name[1..];
        ResizePreview.Visibility = Visibility.Visible;
    }

    private void ResizeDragEnded(PanelFrame frame, bool cancelled)
    {
        ResizePreview.Visibility = Visibility.Collapsed;
        _resizeFrame = null;
        StopWatchingEscape();

        if (cancelled)
            _workspace?.DragCancelled();
        else if (_resizeSpan != frame.Panel.StoredSpan)
            _workspace?.SetSpan(frame.PanelId, _resizeSpan);
    }

    /// <summary>Esc cancels a drag wherever the keyboard focus is: the handle being dragged never has it.</summary>
    private void WatchEscape()
    {
        if (_escapeWindow is not null || Window.GetWindow(this) is not { } window)
            return;
        _escapeWindow = window;
        window.PreviewKeyDown += Window_PreviewKeyDownWhileDragging;
    }

    private void StopWatchingEscape()
    {
        if (_moveFrame is not null || _resizeFrame is not null || _escapeWindow is null)
            return;
        _escapeWindow.PreviewKeyDown -= Window_PreviewKeyDownWhileDragging;
        _escapeWindow = null;
    }

    private void Window_PreviewKeyDownWhileDragging(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        _moveFrame?.CancelDrag();
        _resizeFrame?.CancelDrag();
        e.Handled = true;
    }

    /// <summary>Scrolls the canvas while a panel is dragged near its top or bottom, so any place can be reached.</summary>
    private void ScrollNearEdge()
    {
        var y = Mouse.GetPosition(CanvasScroller).Y;
        if (y < AutoScrollMargin)
            CanvasScroller.ScrollToVerticalOffset(CanvasScroller.VerticalOffset - AutoScrollStep);
        else if (y > CanvasScroller.ActualHeight - AutoScrollMargin)
            CanvasScroller.ScrollToVerticalOffset(CanvasScroller.VerticalOffset + AutoScrollStep);
    }

    private static double Distance(Rect bounds, Point point)
    {
        var dx = Math.Max(Math.Max(bounds.Left - point.X, 0), point.X - bounds.Right);
        var dy = Math.Max(Math.Max(bounds.Top - point.Y, 0), point.Y - bounds.Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // -----------------------------------------------------------------
    // Arrange mode: the toolbar and bar
    // -----------------------------------------------------------------

    private void Arrange_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is null)
            return;

        if (_places is { IsGridExpanded: false } places)
            places.IsGridExpanded = true;
        _workspace.BeginArrange();

        // The Arrange button is gone now: the keyboard goes to the bar's Done.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => DoneButton.Focus());
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        _workspace?.Done();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => ArrangeButton.Focus());
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        _workspace?.Revert();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => ArrangeButton.Focus());
    }

    private void Restore_Click(object sender, RoutedEventArgs e) => _workspace?.RestoreSaved();

    // -----------------------------------------------------------------
    // My layouts (M5)
    // -----------------------------------------------------------------

    /// <summary>
    /// The Layout menu, made when it opens so it names the layout shown:
    /// save, rename, duplicate, delete, restore, and the startup layout.
    /// </summary>
    private void LayoutMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is not { } workspace)
            return;

        var name = workspace.ActiveLayoutName;
        LayoutMenu.Items.Clear();
        LayoutMenu.Items.Add(LayoutMenuItem($"Save changes to “{name}”", workspace.IsUserLayout, SaveChanges,
            workspace.IsUserLayout ? "Make what is shown this layout's own" : "Built-in layouts stay as they are: save as a new layout instead"));
        LayoutMenu.Items.Add(LayoutMenuItem("Save as a new layout…", true, SaveAsNew, "Keep what is shown as a layout of your own"));
        LayoutMenu.Items.Add(new Separator());
        LayoutMenu.Items.Add(LayoutMenuItem("Rename…", workspace.IsUserLayout, Rename, null));
        LayoutMenu.Items.Add(LayoutMenuItem("Duplicate", true, () => workspace.Duplicate(), $"Copy “{name}” into My layouts"));
        LayoutMenu.Items.Add(LayoutMenuItem($"Delete “{name}”", workspace.IsUserLayout, DeleteLayout,
            "Your places, sessions and files aren't touched. Undo brings it back."));
        LayoutMenu.Items.Add(new Separator());
        LayoutMenu.Items.Add(LayoutMenuItem(workspace.RestoreLabel, workspace.IsModified, () => workspace.RestoreSaved(),
            "Show the layout as it was saved. Undo brings this arrangement back."));
        LayoutMenu.Items.Add(new Separator());

        var startup = new MenuItem { Header = "Start QuickerPlaces with" };
        foreach (var option in workspace.StartupOptions)
        {
            var id = option.PresetId;
            var item = new MenuItem { Header = option.Name, IsCheckable = true, IsChecked = option.IsChosen };
            item.Click += (_, _) => workspace.SetStartup(id);
            startup.Items.Add(item);
        }

        LayoutMenu.Items.Add(startup);
        LayoutMenu.PlacementTarget = LayoutMenuButton;
        LayoutMenu.IsOpen = true;
    }

    private static MenuItem LayoutMenuItem(string header, bool enabled, Action action, string? toolTip)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled, ToolTip = toolTip };
        ToolTipService.SetShowOnDisabled(item, true);
        item.Click += (_, _) => action();
        return item;
    }

    private void SaveAsNew_Click(object sender, RoutedEventArgs e) => SaveAsNew();

    private void SaveChanges_Click(object sender, RoutedEventArgs e) => SaveChanges();

    private void SaveAsNew()
    {
        if (_workspace is { } workspace && Window.GetWindow(this) is { } owner &&
            SaveLayoutDialog.Show(owner, workspace.NewSaveAs(), workspace.SaveAsNew))
            FocusLayoutPicker();
    }

    private void SaveChanges()
    {
        _workspace?.SaveChanges();
        FocusLayoutPicker();
    }

    private void Rename()
    {
        if (_workspace is { } workspace && Window.GetWindow(this) is { } owner)
            SaveLayoutDialog.Show(owner, workspace.NewRename(), workspace.Rename);
    }

    /// <summary>Deletes the layout shown, without asking: Undo, where the focus goes, brings it back.</summary>
    private void DeleteLayout()
    {
        _workspace?.Delete();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (StatusUndoButton.IsVisible)
                StatusUndoButton.Focus();
        });
    }

    /// <summary>After Save as new or Save changes, which leave Arrange mode, the keyboard goes back to the picker.</summary>
    private void FocusLayoutPicker() => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => LayoutPicker.Focus());

    private void RestoreBackup_Click(object sender, RoutedEventArgs e) => _workspace?.RestoreBackup();

    private void Undo_Click(object sender, RoutedEventArgs e) => _workspace?.Undo();

    /// <summary>Ctrl+Z in Arrange mode undoes the last step, unless a text box has the focus: there it undoes typing.</summary>
    private void WorkspaceView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && _workspace is { IsArranging: true } workspace &&
            Keyboard.FocusedElement is not TextBox)
        {
            workspace.Undo();
            e.Handled = true;
        }
    }

    /// <summary>
    /// The search box moves to a row of its own when the layout picker, the
    /// box at its least width and the buttons don't fit on one line — with
    /// large text, or a narrow window — so no button is pushed off the edge.
    /// </summary>
    private void Toolbar_SizeChanged(object sender, SizeChangedEventArgs e) => FitToolbar();

    private void FitToolbar()
    {
        var margins = SearchArea.Margin.Left + SearchArea.Margin.Right;
        var needed = LayoutTools.DesiredSize.Width + SearchArea.MinWidth + margins + PanelTools.DesiredSize.Width;
        var ownRow = Toolbar.ActualWidth > 0 && Toolbar.ActualWidth < needed;
        if (ownRow == (Grid.GetRow(SearchArea) == 1))
            return;

        Grid.SetRow(SearchArea, ownRow ? 1 : 0);
        Grid.SetColumn(SearchArea, ownRow ? 0 : 1);
        Grid.SetColumnSpan(SearchArea, ownRow ? 3 : 1);
        SearchArea.Margin = ownRow ? new Thickness(0, 10, 0, 0) : new Thickness(16, 0, 8, 0);
        SearchArea.MaxWidth = ownRow ? double.PositiveInfinity : 520;
    }

    private FrameworkElement CreateContent(WorkspacePanelViewModel panel)
    {
        switch (panel.Type)
        {
            case PanelTypes.Activity:
                // In a row it shares (Files First), a short window could cut the
                // month view off: it scrolls instead. In its own row it takes the
                // height it needs, and never shows a scroll bar.
                return new ScrollViewer
                {
                    Content = new YearActivityPanel { DataContext = _workspace!.Library },
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Focusable = false,
                };

            case PanelTypes.Shelf:
                return CreateShelf();

            case PanelTypes.Files:
                // The File viewer (File viewer design §3): Saved places and Recents as tabs, with Sessions and All.
                _filesPanel = new FilesPanel(new PlacesPanel { DataContext = _places, CollapsesWithWindow = false }, CreateShelf(), _workspace!.Library);
                _filesPanel.SessionActionRequested += (action, id) => _sessionsPanel?.RunSessionAction(action, id);
                return _filesPanel;

            case PanelTypes.Sessions:
                var sessionsPanel = _sessionsPanel = new SessionsPanel { AllowsNoSelection = true };
                sessionsPanel.Attach(_sessions!, new WindowsShell(), _probe!);
                sessionsPanel.SessionsChanged += RequestReload;
                sessionsPanel.ViewFilesRequested += id => _filesPanel?.ShowSession(id);

                // The chosen card is the session the File viewer shows, and the other way round:
                // the viewer's chip (or leaving its Sessions tab) puts the card down.
                sessionsPanel.SelectedSessionChanged += id => _filesPanel?.ScopeSession(id);
                var library = _workspace!.Library;
                library.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(LibraryViewModel.SessionScope))
                        sessionsPanel.SelectSession(library.SessionScope);
                };
                return sessionsPanel;

            case PanelTypes.Places:
                return new PlacesPanel { DataContext = _places, CollapsesWithWindow = false };

            case PanelTypes.Favourites:
                // Stacked one to a row, so a long list scrolls in its panel.
                return new ScrollViewer
                {
                    Content = new FavouritesPanel { DataContext = _places, ShowsTitle = false, Stacked = true },
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Focusable = false,
                };

            default:
                // A panel from a newer build, or one this build lists but hasn't made yet: kept, never dropped (D6).
                // Resource references, not lookups, so a theme change reaches it.
                var text = new TextBlock
                {
                    Text = "This panel isn't available in this version of QuickerPlaces. It stays in your layout; hide it if you don't need it.",
                    TextWrapping = TextWrapping.Wrap,
                };
                text.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
                var placeholder = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16),
                    Child = text,
                };
                placeholder.SetResourceReference(Border.BorderBrushProperty, "Border.Default");
                return placeholder;
        }
    }

    /// <summary>
    /// A Library grid with Recents' tracked folders and actions (Desk layout
    /// design §4): the Recents panel, or the File viewer's Recent, Sessions and
    /// All tabs.
    /// </summary>
    private FileShelfPanel CreateShelf()
    {
        var shelf = new FileShelfPanel { DataContext = _workspace!.Library, ShowsSearch = false, ShowsSaveAsSession = true };
        shelf.SaveAsSessionRequested += SaveShelfAsSession;
        shelf.AddAsPlaceRequested += AddAsPlace;
        var activity = new ActivityViewModel(_activityStore!, () =>
        {
            _activityHost!.RootsChanged();
            _trackingChanged?.Invoke();
            foreach (var each in _shelves)
                each.UpdateTracking();
            RequestReload();
        });
        shelf.AttachTracking(activity, _networkDrives!, () => ActivityFormat.TrackingSummary(
            _activityStore!.EnabledRoots().Count, _activityStore.Roots.Count, _activityHost!.IsPaused));
        _shelves.Add(shelf);
        return shelf;
    }

    /// <summary>
    /// The shelf's Save as session (M3): its PDF, Word and Excel files, each
    /// once, reviewed in the usual Save Session dialog before anything is
    /// saved. The Sessions panel then shows the new session selected.
    /// </summary>
    private void SaveShelfAsSession()
    {
        if (_workspace is null || _sessions is null || _probe is null || Window.GetWindow(this) is not { } owner)
            return;

        var fileSet = _workspace.ListedFileSet();
        if (fileSet.IsEmpty)
        {
            MessageForm.Show(SessionFileSet.NothingToSave, AppInfo.Name, owner: owner);
            return;
        }

        var editor = SessionEditorViewModel.ForFileSet(_sessions, fileSet);
        if (!SessionEditorDialog.Show(owner, editor, _probe))
            return;

        _sessionsPanel?.NoteSaved(editor.SavedId!, editor.SavePersistenceMessage);
        RequestReload();
    }

    /// <summary>The Recents panel's Add as place: the usual Add folder dialog for that folder, then the Library read again.</summary>
    private void AddAsPlace(string folder)
    {
        if (_places is null || Window.GetWindow(this) is not { } owner)
            return;

        _places.AddFolderFromActivity(folder, owner);
        RequestReload();
    }

    // -----------------------------------------------------------------
    // Search box: a launcher, as the places list's is
    // -----------------------------------------------------------------

    /// <summary>Enter opens the top result, Down moves into the File shelf, Esc clears the search (or, when empty, moves to the shelf).</summary>
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_workspace is null)
            return;

        switch (e.Key)
        {
            case Key.Escape:
                if (_workspace.IsSearching)
                    _workspace.SearchText = "";
                else
                    ShownShelf?.FocusList();
                e.Handled = true;
                break;

            case Key.Enter:
                if (_workspace.Library.Rows.FirstOrDefault() is { } top)
                    _workspace.Library.Open(top);
                e.Handled = true;
                break;

            case Key.Down:
                if (ShownShelf is { } shelf)
                {
                    shelf.FocusList();
                    e.Handled = true;
                }
                break;
        }
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is not null)
            _workspace.SearchText = "";
        SearchBox.Focus();
    }

    // -----------------------------------------------------------------
    // Toolbar buttons and menus
    // -----------------------------------------------------------------

    private void AddPanel_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is null)
            return;

        AddPanelMenu.Items.Clear();
        foreach (var choice in _workspace.AddablePanels)
        {
            var item = new MenuItem { Header = choice.Title };
            var type = choice.Type;
            item.Click += (_, _) => _workspace.AddPanel(type);
            AddPanelMenu.Items.Add(item);
        }

        if (AddPanelMenu.Items.Count == 0)
            return;
        AddPanelMenu.PlacementTarget = AddPanelButton;
        AddPanelMenu.IsOpen = true;
    }

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        if (OptionsButton.ContextMenu is not { } menu) return;
        menu.PlacementTarget = OptionsButton;
        menu.IsOpen = true;
    }

    private void OptionsMenu_Opened(object sender, RoutedEventArgs e)
        => ExportMenuItem.IsEnabled = _places?.ExportCommand.CanExecute(null) ?? false;

    private void RecentFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is not null && Window.GetWindow(this) is { } owner)
            RecentFilesDialog.Show(owner, _workspace.Library);
    }

    private void RecentlyDeleted_Click(object sender, RoutedEventArgs e) => _places?.ShowRecentlyDeletedCommand.Execute(null);

    private void PlacesFile_Click(object sender, RoutedEventArgs e) => _places?.OpenDataFolderCommand.Execute(null);

    private void Import_Click(object sender, RoutedEventArgs e) => _places?.ImportCommand.Execute(null);

    private void Export_Click(object sender, RoutedEventArgs e) => _places?.ExportCommand.Execute(null);

    private void RetryLayouts_Click(object sender, RoutedEventArgs e) => _workspace?.RetrySave();
}
