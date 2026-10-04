using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

/// <summary>
/// The favourites (SI §6.4; Desk layout design §5): the main window's strip,
/// and in the workspace a panel. DataContext is the <see cref="MainViewModel"/>.
/// </summary>
public partial class FavouritesPanel : UserControl
{
    private Point? _bubbleDragStartPoint;
    private readonly CardDropPreview<PlaceViewModel> _dropPreview;

    public FavouritesPanel()
    {
        InitializeComponent();
        _dropPreview = new CardDropPreview<PlaceViewModel>(FavouritesItemsControl);
        Unloaded += (_, _) => _dropPreview.Clear();
    }

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
        if (e.LeftButton != MouseButtonState.Pressed || _bubbleDragStartPoint is not { } start)
            return;

        if (sender is not Button { DataContext: PlaceViewModel place } button)
            return;

        var current = e.GetPosition(null);
        var movedX = System.Math.Abs(current.X - start.X);
        var movedY = System.Math.Abs(current.Y - start.Y);

        if (movedX < SystemParameters.MinimumHorizontalDragDistance &&
            movedY < SystemParameters.MinimumVerticalDragDistance)
            return;

        _bubbleDragStartPoint = null;
        _dropPreview.Drag(button, place);
    }

    private void FavouritesItemsControl_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PlaceViewModel)) is PlaceViewModel dragged &&
            DataContext is MainViewModel vm && vm.FavouritePlaces.Contains(dragged))
        {
            _dropPreview.Update(e.GetPosition(FavouritesItemsControl), dragged, Stacked);
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            _dropPreview.Clear();
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void FavouritesItemsControl_DragLeave(object sender, DragEventArgs e)
    {
        if (!_dropPreview.Contains(e.GetPosition(FavouritesItemsControl)))
            _dropPreview.Clear();
    }

    private void FavouritesItemsControl_Drop(object sender, DragEventArgs e)
    {
        _dropPreview.Clear();
        if (DataContext is MainViewModel viewModel &&
            e.Data.GetData(typeof(PlaceViewModel)) is PlaceViewModel dragged &&
            _dropPreview.FindTarget(e.GetPosition(FavouritesItemsControl), dragged, Stacked) is { } target)
        {
            viewModel.MoveFavourite(dragged, target.TargetIndex);
        }
        e.Handled = true;
    }
}
