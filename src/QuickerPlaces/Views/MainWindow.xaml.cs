using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private GlobalHotkey? _globalHotkey;
    private string? _globalHotkeyError;
    private WindowState _stateBeforeMinimize = WindowState.Normal;
    private Point _bubbleDragStartPoint;

    public MainWindow(MainViewModel viewModel, AppSettings settings, SettingsService settingsService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settings = settings;
        _settingsService = settingsService;
        RestoreWindowState(settings);
    }

    // -----------------------------------------------------------------
    // Global hotkey + bring-to-front. The hotkey needs this window's HWND,
    // which first exists in OnSourceInitialized; any problem registering
    // it is held until Window_Loaded, when a notice can be centered on an
    // on-screen window.
    // -----------------------------------------------------------------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _globalHotkeyError = ApplyGlobalHotkey(_settings.GlobalHotkey);
    }

    /// <summary>
    /// Replaces whatever hotkey is registered with <paramref name="setting"/>
    /// ("Ctrl+Alt+Space"; empty or "None" just unregisters). Returns a
    /// user-readable error, or null on success. On failure no hotkey is
    /// left registered.
    /// </summary>
    private string? ApplyGlobalHotkey(string? setting)
    {
        _globalHotkey?.Dispose();
        _globalHotkey = null;
        SetGlobalHotkeyText(null);

        if (HotkeyGesture.IsDisabled(setting))
            return null;

        if (!HotkeyGesture.TryParse(setting, out var gesture, out var error))
            return error;

        _globalHotkey = GlobalHotkey.TryRegister(this, gesture, out error);
        if (_globalHotkey is null)
            return error;

        _globalHotkey.Pressed += BringToFront;
        SetGlobalHotkeyText(gesture.ToString());
        return null;
    }

    private void SetGlobalHotkeyText(string? text)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.GlobalHotkeyText = text;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        // Paused while the dialog is open: otherwise pressing the current
        // hotkey in the capture box would fire it instead of recording it.
        ApplyGlobalHotkey(null);

        var saved = SettingsDialog.Show(this, _settings.GlobalHotkey, ApplyGlobalHotkey);
        if (saved is null)
        {
            // Cancelled: put back what was there. If that fails again, it
            // was already failing before (and reported at startup).
            ApplyGlobalHotkey(_settings.GlobalHotkey);
            return;
        }

        // Saved now rather than on exit, so a crash can't lose the choice.
        _settings.GlobalHotkey = saved;
        PersistWindowState(_settings);
        _settingsService.Save(_settings);
    }

    protected override void OnClosed(EventArgs e)
    {
        _globalHotkey?.Dispose();
        _globalHotkey = null;
        base.OnClosed(e);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        // Remembered so BringToFront restores a minimized window to
        // maximized if that's how it was before, not always to normal.
        if (WindowState != WindowState.Minimized)
            _stateBeforeMinimize = WindowState;
    }

    /// <summary>
    /// Shows the window in front of everything, restoring it if minimized,
    /// with the search box focused and its text selected — so the global
    /// hotkey (or launching the app again) goes straight to "type to find,
    /// Enter to open".
    /// </summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = _stateBeforeMinimize;

        Show();
        Activate();

        // A modal dialog (Add, Export, a MessageForm...) leaves this window
        // disabled until it closes, so bring that dialog forward instead of
        // trying to focus a search box that can't take input.
        foreach (Window owned in OwnedWindows)
        {
            if (owned.IsVisible)
            {
                owned.Activate();
                return;
            }
        }

        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Surfaced once here (rather than from the constructor) so a
        // loaded, on-screen window exists for MessageForm to center on
        // (SI §5 — a corrupt places.json shouldn't crash the app, but the
        // user should still be told their old data didn't just vanish).
        if (DataContext is MainViewModel { PlacesLoadFailed: true } viewModel)
        {
            // The first change the user makes will overwrite places.json,
            // so point them at the backup copy, which survives that.
            var whereToFind = viewModel.CorruptFileBackupPath is { } backupPath
                ? $"A copy of the unreadable file was saved to:\n{backupPath}"
                : $"QuickerPlaces couldn't make a copy of the file, and your next change will replace it. " +
                  $"To keep it, copy it somewhere safe before adding anything:\n{viewModel.PlacesFilePath}";

            MessageForm.Show(
                $"Your saved places couldn't be read and QuickerPlaces has started with an empty list.\n\n{whereToFind}",
                viewModel.AppName, MessageFormButtons.OK, MessageFormIcon.Warning);
        }

        if (_globalHotkeyError is not null)
        {
            MessageForm.Show(
                $"The shortcut for bringing QuickerPlaces to the front isn't active.\n\n{_globalHotkeyError}\n\n" +
                "To choose a different one, or turn it off, click the Settings (gear) button at the top of the window.",
                AppInfo.Name, MessageFormButtons.OK, MessageFormIcon.Warning);
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

    // -----------------------------------------------------------------
    // Search box + keyboard shortcuts. Window-wide shortcuts are
    // KeyBindings in MainWindow.xaml; these handlers cover the ones that
    // depend on focus (the search box, the grid's selected row).
    // -----------------------------------------------------------------

    /// <summary>Ctrl+F: jump to the search box, selecting any existing query so typing replaces it.</summary>
    private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>
    /// Makes the search box a launcher: Enter opens the top result, Down
    /// moves into the grid to pick a different one, and Esc clears the
    /// search (or, if it's already empty, hands focus to the grid).
    /// </summary>
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        switch (e.Key)
        {
            case Key.Escape:
                if (viewModel.IsSearching)
                    viewModel.SearchText = string.Empty;
                else
                    FocusGridRow(PlacesGrid.SelectedIndex);
                e.Handled = true;
                break;

            case Key.Enter:
                if (PlacesGrid.Items.Count > 0 && PlacesGrid.Items[0] is PlaceViewModel top)
                    viewModel.OpenCommand.Execute(top);
                e.Handled = true;
                break;

            case Key.Down:
                FocusGridRow(0);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Row shortcuts, acting on the selected row: Enter opens, F2 renames, Ctrl+E edits the path/URL, Ctrl+D toggles favourite, Ctrl+C copies the path/URL, Delete removes.</summary>
    private void PlacesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (PlacesGrid.SelectedItem is not PlaceViewModel place || DataContext is not MainViewModel viewModel)
            return;

        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        var none = Keyboard.Modifiers == ModifierKeys.None;

        var command = e.Key switch
        {
            Key.Enter when none => viewModel.OpenCommand,
            Key.F2 when none => viewModel.RenameAliasCommand,
            Key.E when ctrl => viewModel.EditResourceCommand,
            Key.D when ctrl => viewModel.ToggleFavouriteCommand,
            // Replaces DataGrid's own Ctrl+C, which copies every cell of the row.
            Key.C when ctrl => viewModel.CopyResourceCommand,
            Key.Delete when none => viewModel.RemoveCommand,
            _ => null
        };

        if (command is null)
            return;

        // Handled first: DataGrid's own Enter handling would otherwise
        // also move the selection down a row.
        e.Handled = true;
        command.Execute(place);
    }

    /// <summary>Selects and keyboard-focuses the grid row at <paramref name="index"/> (clamped; first row if nothing was selected).</summary>
    private void FocusGridRow(int index)
    {
        if (PlacesGrid.Items.Count == 0)
            return;

        index = System.Math.Clamp(index, 0, PlacesGrid.Items.Count - 1);
        var item = PlacesGrid.Items[index];
        PlacesGrid.SelectedItem = item;
        PlacesGrid.ScrollIntoView(item);

        // Focusing the DataGrid itself only focuses the grid, not a row, so
        // arrow keys wouldn't move from the selection. Focus the row's
        // container once it has been generated.
        PlacesGrid.UpdateLayout();
        if (PlacesGrid.ItemContainerGenerator.ContainerFromIndex(index) is DataGridRow row)
            row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        else
            PlacesGrid.Focus();
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
