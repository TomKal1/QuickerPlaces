using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using QuickerPlaces.Models.Workspace;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

/// <summary>
/// A panel's frame on the workspace canvas (configurable canvas plan M4):
/// title and Hide, and in Arrange mode the handle, Move earlier/later, the
/// width choice and the edge to drag. Made once per panel and kept, so the
/// panel inside keeps its selection, scroll and focus when it moves; the
/// workspace calls <see cref="Update"/> after every change.
/// </summary>
public partial class PanelFrame : UserControl
{
    private bool _updating;

    public PanelFrame(WorkspacePanelViewModel panel, FrameworkElement content)
    {
        InitializeComponent();
        PanelId = panel.Id;
        ContentHost.Child = content;
        Update(panel, arranging: false);
    }

    public string PanelId { get; }

    /// <summary>The panel as last placed.</summary>
    public WorkspacePanelViewModel Panel { get; private set; } = null!;

    public event Action<PanelFrame>? HideRequested;
    public event Action<PanelFrame>? MoveEarlierRequested;
    public event Action<PanelFrame>? MoveLaterRequested;
    public event Action<PanelFrame, int>? SpanRequested;

    /// <summary>The handle: dragging started, moved, and ended (true when cancelled).</summary>
    public event Action<PanelFrame>? MoveDragStarted;
    public event Action<PanelFrame>? MoveDragMoved;
    public event Action<PanelFrame, bool>? MoveDragEnded;

    /// <summary>The edge: dragging started, moved, and ended (true when cancelled).</summary>
    public event Action<PanelFrame>? ResizeDragStarted;
    public event Action<PanelFrame>? ResizeDragMoved;
    public event Action<PanelFrame, bool>? ResizeDragEnded;

    /// <summary>True while the handle or the edge is being dragged.</summary>
    public bool IsDragging => MoveHandle.IsDragging || ResizeHandle.IsDragging;

    /// <summary>Stops a drag in progress; its ended event says it was cancelled.</summary>
    public void CancelDrag()
    {
        MoveHandle.CancelDrag();
        ResizeHandle.CancelDrag();
    }

    /// <summary>Shows the panel's current place and whether Arrange mode's controls are shown.</summary>
    public void Update(WorkspacePanelViewModel panel, bool arranging)
    {
        Panel = panel;
        var title = panel.Title;
        TitleText.Text = title.ToUpperInvariant();
        AutomationProperties.SetName(this, title);

        var arrange = arranging ? Visibility.Visible : Visibility.Collapsed;
        ArrangeControls.Visibility = arrange;
        MoveHandle.Visibility = arrange;
        ResizeHandle.Visibility = arrange;
        ArrangeOutline.Visibility = arrange;

        HideButton.ToolTip = arranging ? $"Hide {title}" : $"Hide {title}. Undo or Add panel brings it back.";
        AutomationProperties.SetName(HideButton, $"Hide {title}");

        // An arrow that a move has just disabled would drop the keyboard focus: hand it to the other one.
        if (EarlierButton.IsKeyboardFocused && !panel.CanMoveEarlier && panel.CanMoveLater)
            LaterButton.Focus();
        else if (LaterButton.IsKeyboardFocused && !panel.CanMoveLater && panel.CanMoveEarlier)
            EarlierButton.Focus();

        EarlierButton.IsEnabled = panel.CanMoveEarlier;
        EarlierButton.ToolTip = $"Move {title} earlier";
        AutomationProperties.SetName(EarlierButton, $"Move {title} earlier");
        LaterButton.IsEnabled = panel.CanMoveLater;
        LaterButton.ToolTip = $"Move {title} later";
        AutomationProperties.SetName(LaterButton, $"Move {title} later");

        MoveHandle.ToolTip = $"Drag to move {title}. Esc cancels.";
        ResizeHandle.ToolTip = $"Drag to change the width of {title}. Esc cancels.";

        // Its own width: in a narrow window it may be shown wider, which the tooltip says.
        _updating = true;
        WidthChoice.SelectedItem = WidthChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, panel.StoredSpan.ToString()));
        _updating = false;
        WidthChoice.ToolTip = panel.IsWidened
            ? $"Width of {title}. Shown {PanelSpans.DisplayName(panel.Span)} while the window is too narrow for {PanelSpans.DisplayName(panel.StoredSpan)}."
            : $"Width of {title}";
        AutomationProperties.SetName(WidthChoice, $"Width of {title}");
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => HideRequested?.Invoke(this);

    private void Earlier_Click(object sender, RoutedEventArgs e) => MoveEarlierRequested?.Invoke(this);

    private void Later_Click(object sender, RoutedEventArgs e) => MoveLaterRequested?.Invoke(this);

    private void WidthChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && WidthChoice.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out var span))
            SpanRequested?.Invoke(this, span);
    }

    private void MoveHandle_DragStarted(object sender, DragStartedEventArgs e) => MoveDragStarted?.Invoke(this);

    private void MoveHandle_DragDelta(object sender, DragDeltaEventArgs e) => MoveDragMoved?.Invoke(this);

    private void MoveHandle_DragCompleted(object sender, DragCompletedEventArgs e) => MoveDragEnded?.Invoke(this, e.Canceled);

    private void ResizeHandle_DragStarted(object sender, DragStartedEventArgs e) => ResizeDragStarted?.Invoke(this);

    private void ResizeHandle_DragDelta(object sender, DragDeltaEventArgs e) => ResizeDragMoved?.Invoke(this);

    private void ResizeHandle_DragCompleted(object sender, DragCompletedEventArgs e) => ResizeDragEnded?.Invoke(this, e.Canceled);
}
