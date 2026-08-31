using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuickerPlaces.Models;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

public partial class MainWindow : Window
{
    private Point _bubbleDragStartPoint;

    public MainWindow(MainViewModel viewModel, AppSettings settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        RestoreWindowState(settings);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Surfaced once here (rather than from the constructor) so a
        // loaded, on-screen window exists for MessageForm to center on
        // (SI §5 — a corrupt places.json shouldn't crash the app, but the
        // user should still be told their old data didn't just vanish).
        if (DataContext is MainViewModel { PlacesLoadFailed: true } viewModel)
        {
            MessageForm.Show(
                $"Your saved places couldn't be read and QuickerPlaces has started with an empty list.\n\n" +
                $"The original file was left untouched at:\n{viewModel.PlacesFilePath}",
                viewModel.AppName, MessageFormButtons.OK, MessageFormIcon.Warning);
        }
    }

    private void RestoreWindowState(AppSettings settings)
    {
        if (!double.IsNaN(settings.WindowLeft) && !double.IsNaN(settings.WindowTop))
        {
            var left = settings.WindowLeft;
            var top = settings.WindowTop;

            // Guard against restoring a position from a monitor that's no
            // longer connected — fall back to WPF's default startup
            // location (see WindowStartupLocation in MainWindow.xaml)
            // instead of placing the window off-screen.
            var withinVirtualScreen =
                left >= SystemParameters.VirtualScreenLeft &&
                left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                top >= SystemParameters.VirtualScreenTop &&
                top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;

            if (withinVirtualScreen)
            {
                Left = left;
                Top = top;
            }
        }

        if (settings.WindowWidth > 0)
            Width = settings.WindowWidth;
        if (settings.WindowHeight > 0)
            Height = settings.WindowHeight;

        if (settings.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// Copies the current window bounds (and, via the view model,
    /// non-window UI state) back into <paramref name="settings"/> so
    /// App.xaml.cs can persist them on exit. Places data itself is already
    /// saved continuously by PlacesService — this only covers window
    /// chrome, so it's fine that it's only called once, on clean exit,
    /// rather than on every change.
    /// </summary>
    public void PersistWindowState(AppSettings settings)
    {
        // Capture the restore bounds, not the maximized bounds, so
        // un-maximizing on the next launch doesn't leave the window
        // full-screen with nowhere sensible to shrink back to.
        if (WindowState == WindowState.Normal)
        {
            settings.WindowLeft = Left;
            settings.WindowTop = Top;
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
        }

        settings.WindowMaximized = WindowState == WindowState.Maximized;

        if (DataContext is MainViewModel viewModel)
            viewModel.PersistToSettings();
    }

    /// <summary>Double-click on a grid row = Open (SI §6.3), the same action as the row's top context-menu item.</summary>
    private void Row_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { Item: PlaceViewModel place } && DataContext is MainViewModel viewModel)
            viewModel.OpenCommand.Execute(place);
    }

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

        var items = viewModel.FavouritePlaces;
        var targetIndex = targetPlace is not null && !ReferenceEquals(targetPlace, dragged)
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
