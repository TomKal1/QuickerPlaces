using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

/// <summary>
/// The favourites (SI §6.4; Desk layout design §5): the main window's strip,
/// and in the workspace a panel. DataContext is the <see cref="MainViewModel"/>.
/// </summary>
public partial class FavouritesPanel : UserControl
{
    private Point _bubbleDragStartPoint;

    public FavouritesPanel() => InitializeComponent();

    /// <summary>False in a workspace panel, whose frame already shows the title.</summary>
    public bool ShowsTitle
    {
        get => TitleText.Visibility == Visibility.Visible;
        set => TitleText.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// One favourite to a row, each as wide as the panel: the workspace's left
    /// column is one card wide, and favourites stack down it as they are added.
    /// Off, they wrap side by side, as in the main window's strip.
    /// </summary>
    public bool Stacked
    {
        get => _stacked;
        set
        {
            if (_stacked == value)
                return;
            _stacked = value;
            var panel = new FrameworkElementFactory(_stacked ? typeof(StackPanel) : typeof(WrapPanel));
            panel.SetValue(StackPanel.OrientationProperty, System.Windows.Controls.Orientation.Vertical);
            if (!_stacked)
                panel.SetValue(WrapPanel.OrientationProperty, System.Windows.Controls.Orientation.Horizontal);
            FavouritesItemsControl.ItemsPanel = new ItemsPanelTemplate(panel);
        }
    }

    private bool _stacked;

    // -----------------------------------------------------------------
    // Favourite bubble drag-to-reorder (SI §6.4). A Button already
    // consumes the mouse for its own Click, so reordering is driven from
    // Preview* events: PreviewMouseLeftButtonDown records where the drag
    // could start, PreviewMouseMove checks whether the pointer has moved
    // past the OS drag threshold and — only then — starts a WPF drag/drop
    // operation. A plain click (no meaningful movement) never reaches
    // DoDragDrop, so it still fires the Button's own Click/Open normally.
    // -----------------------------------------------------------------

    private void Bubble_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _bubbleDragStartPoint = e.GetPosition(null);

    private void Bubble_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        if (sender is not Button { DataContext: PlaceViewModel place } button)
            return;

        var current = e.GetPosition(null);
        var movedX = System.Math.Abs(current.X - _bubbleDragStartPoint.X);
        var movedY = System.Math.Abs(current.Y - _bubbleDragStartPoint.Y);

        if (movedX < SystemParameters.MinimumHorizontalDragDistance &&
            movedY < SystemParameters.MinimumVerticalDragDistance)
            return;

        DragDrop.DoDragDrop(button, new DataObject(typeof(PlaceViewModel), place), DragDropEffects.Move);
    }

    private void FavouritesItemsControl_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(PlaceViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void FavouritesItemsControl_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(PlaceViewModel)))
            return;

        if (e.Data.GetData(typeof(PlaceViewModel)) is not PlaceViewModel dragged)
            return;

        if (DataContext is not MainViewModel viewModel)
            return;

        var dropPosition = e.GetPosition(FavouritesItemsControl);
        var targetPlace = FindPlaceUnderPoint(dropPosition);

        // Dropped back onto itself (a short wobble rather than a real
        // move): leave it where it was. Only a drop on empty space — past
        // the last bubble, or in a gap — means "move to the end".
        if (ReferenceEquals(targetPlace, dragged))
            return;

        var items = viewModel.FavouritePlaces;
        var targetIndex = targetPlace is not null
            ? items.IndexOf(targetPlace)
            : items.Count - 1;

        viewModel.MoveFavourite(dragged, targetIndex);
    }

    /// <summary>
    /// Walks up from whatever visual was hit at <paramref name="point"/>
    /// (inside FavouritesItemsControl) until it finds an element whose
    /// DataContext is a PlaceViewModel — i.e. which bubble, if any, the
    /// drop landed on.
    /// </summary>
    private PlaceViewModel? FindPlaceUnderPoint(Point point)
    {
        var hit = VisualTreeHelper.HitTest(FavouritesItemsControl, point)?.VisualHit;

        while (hit is not null)
        {
            if (hit is FrameworkElement { DataContext: PlaceViewModel place })
                return place;

            hit = VisualTreeHelper.GetParent(hit);
        }

        return null;
    }
}
