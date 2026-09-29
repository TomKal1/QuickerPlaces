using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using QuickerPlaces.Models.Workspace;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.ViewModels;
using QuickerPlaces.Views.Panels;

namespace QuickerPlaces.Views;

/// <summary>
/// The workspace's view (configurable canvas plan M3). Builds each panel
/// once and places it on the twelve-column canvas where
/// <see cref="WorkspaceViewModel.Panels"/> says, so a layout change moves
/// panels without recreating them (their selection and scroll survive).
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

    private readonly Dictionary<string, FrameworkElement> _frames = new();
    private readonly DispatcherTimer _flushTimer;
    private WorkspaceViewModel? _workspace;
    private MainViewModel? _places;
    private SessionStore? _sessions;
    private WindowsOpenDocumentProbe? _probe;
    private RecentFilesHost? _recentFilesHost;
    private LibraryRefresh? _refresh;
    private FileShelfPanel? _shelf;
    private bool _reloadQueued;
    private bool _loadedOnce;

    public WorkspaceView()
    {
        InitializeComponent();
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
        RecentFilesHost recentFilesHost, ActivityTrackingHost activityHost)
    {
        _workspace = workspace;
        _places = places;
        _sessions = sessions;
        _probe = probe;
        _recentFilesHost = recentFilesHost;
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

    /// <summary>At window close: writes a waiting query change and stops listening to the tracking hosts.</summary>
    public void Close()
    {
        _flushTimer.Stop();
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
        // Searching a hidden workspace would be pointless: bring it back, as list mode does.
        if (e.PropertyName == nameof(WorkspaceViewModel.SearchText) && _workspace is { IsSearching: true } && _places is { IsGridExpanded: false } places)
            places.IsGridExpanded = true;
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
        HideButton.Content = expanded ? "Hide panels" : "Show panels";
    }

    // -----------------------------------------------------------------
    // The canvas
    // -----------------------------------------------------------------

    /// <summary>
    /// Places every shown panel: twelve equal columns, one grid row per
    /// canvas row. A row holding only the year strip takes its own height;
    /// any other row shares what is left. Panels are made once and moved.
    /// </summary>
    private void BuildCanvas()
    {
        if (_workspace is null)
            return;

        foreach (var frame in _frames.Values)
            PanelCanvas.Children.Remove(frame);
        PanelCanvas.RowDefinitions.Clear();
        PanelCanvas.ColumnDefinitions.Clear();

        for (var i = 0; i < PanelSpans.Columns; i++)
            PanelCanvas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        foreach (var row in _workspace.Panels.GroupBy(p => p.Row).OrderBy(g => g.Key))
        {
            var fitsContent = row.All(p => p.Type == PanelTypes.Activity);
            PanelCanvas.RowDefinitions.Add(fitsContent
                ? new RowDefinition { Height = GridLength.Auto }
                : new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 220 });
        }

        foreach (var panel in _workspace.Panels)
        {
            if (!_frames.TryGetValue(panel.Id, out var frame))
            {
                frame = CreateFrame(panel);
                _frames[panel.Id] = frame;
            }

            Grid.SetRow(frame, panel.Row);
            Grid.SetColumn(frame, panel.Column);
            Grid.SetColumnSpan(frame, panel.Span);
            PanelCanvas.Children.Add(frame);
        }

        EmptyCanvasText.Visibility = _workspace.HasPanels ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>A panel with its title and Hide button above it.</summary>
    private FrameworkElement CreateFrame(WorkspacePanelViewModel panel)
    {
        var hide = new Button
        {
            Style = (Style)FindResource("Button.IconOnlyCompact"),
            Width = 24,
            Height = 24,
            ToolTip = $"Hide {panel.Title}. Add panel brings it back.",
            Content = new Viewbox
            {
                Width = 10,
                Height = 10,
                Child = new System.Windows.Shapes.Path { Style = (Style)FindResource("Icon"), Data = (System.Windows.Media.Geometry)FindResource("Icon.Close") },
            },
        };
        AutomationProperties.SetName(hide, $"Hide {panel.Title}");
        var id = panel.Id;
        hide.Click += (_, _) => _workspace?.HidePanel(id);

        var title = new TextBlock { Text = panel.Title.ToUpperInvariant(), Style = (Style)FindResource("TextBlock.Caps"), VerticalAlignment = VerticalAlignment.Center };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
        DockPanel.SetDock(hide, Dock.Right);
        header.Children.Add(hide);
        header.Children.Add(title);

        var content = CreateContent(panel);
        var frame = new DockPanel { Margin = new Thickness(6) };
        DockPanel.SetDock(header, Dock.Top);
        frame.Children.Add(header);
        frame.Children.Add(content);
        AutomationProperties.SetName(frame, panel.Title);
        return frame;
    }

    private FrameworkElement CreateContent(WorkspacePanelViewModel panel)
    {
        switch (panel.Type)
        {
            case PanelTypes.Activity:
                return new YearActivityPanel { DataContext = _workspace!.Library };

            case PanelTypes.Shelf:
                _shelf = new FileShelfPanel { DataContext = _workspace!.Library, ShowsSearch = false };
                return _shelf;

            case PanelTypes.Sessions:
                var sessions = new SessionsPanel();
                sessions.Attach(_sessions!, new WindowsShell(), _probe!);
                sessions.SessionsChanged += RequestReload;
                return sessions;

            case PanelTypes.Places:
                return new PlacesPanel { DataContext = _places, CollapsesWithWindow = false };

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
                    _shelf?.FocusList();
                e.Handled = true;
                break;

            case Key.Enter:
                if (_workspace.Library.Rows.FirstOrDefault() is { } top)
                    _workspace.Library.Open(top);
                e.Handled = true;
                break;

            case Key.Down:
                if (_shelf is { IsVisible: true })
                {
                    _shelf.FocusList();
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

    private void Hide_Click(object sender, RoutedEventArgs e) => _places?.ToggleGridCommand.Execute(null);

    private void RetryLayouts_Click(object sender, RoutedEventArgs e) => _workspace?.RetrySave();
}
