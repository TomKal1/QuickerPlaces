using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.ViewModels;
using QuickerPlaces.Views.Panels;

namespace QuickerPlaces.Views;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly ActivityStore _activityStore;
    private readonly ActivityTrackingHost _activityHost;
    private readonly SessionStore _sessionStore;
    private readonly WindowsRecentItems _recentItems;
    private readonly PlacesService _placesService;
    private readonly RecentFilesStore _recentFilesStore;
    private readonly RecentFilesHost _recentFilesHost;
    private readonly ThemeManager _themeManager;
    private TrayIcon? _trayIcon;
    private bool _exitRequested;
    private bool _closed;
    private GlobalHotkey? _globalHotkey;
    private string? _globalHotkeyError;
    private WindowState _stateBeforeMinimize = WindowState.Normal;
    private Point _bubbleDragStartPoint;
    private readonly Action _focusSearch;

    public MainWindow(MainViewModel viewModel, AppSettings settings, SettingsService settingsService,
        ActivityStore activityStore, ActivityTrackingHost activityHost, SessionStore sessionStore,
        WindowsRecentItems recentItems, PlacesService placesService, RecentFilesStore recentFilesStore, RecentFilesHost recentFilesHost,
        ThemeManager themeManager)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settings = settings;
        _settingsService = settingsService;
        _activityStore = activityStore;
        _activityHost = activityHost;
        _sessionStore = sessionStore;
        _recentItems = recentItems;
        _placesService = placesService;
        _recentFilesStore = recentFilesStore;
        _recentFilesHost = recentFilesHost;
        _themeManager = themeManager;
        RestoreWindowState(settings);
        UpdateActivityIndicator();

        var places = new PlacesPanel { CollapsesWithWindow = true };
        MainContent.Content = places;
        _focusSearch = places.FocusSearch;
    }

    private void ActivityButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var window = new ActivityWindow(this, _activityStore, _activityHost,
            new NetworkDriveResolver(), UpdateActivityIndicator,
            (folder, owner) => viewModel.AddFolderFromActivity(folder, owner));
        window.ShowDialog();
        UpdateActivityIndicator();
    }

    private void SessionsButton_Click(object sender, RoutedEventArgs e)
        => SessionsWindow.Show(this, _sessionStore, new WindowsShell(), new WindowsOpenDocumentProbe(_recentItems));

    private void LibraryButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var shell = new WindowsShell();
        var library = new LibraryViewModel(_placesService, _sessionStore, _activityStore, _recentFilesStore,
            new PlaceLauncher(_placesService, shell), shell, work: new DispatcherBackgroundWork(Dispatcher));
        LibraryWindow.Show(this, library, _recentFilesHost, _activityHost, viewModel.NotePlaceOpened);
    }

    public void UpdateActivityIndicator()
    {
        var count = _activityStore.EnabledRoots().Count;
        var text = count > 0 && _activityHost.IsPaused
            ? "Recents — tracking paused"
            : count > 0
            ? $"Recents — tracking {count} {(count == 1 ? "folder" : "folders")}"
            : _activityStore.Roots.Count > 0 ? "Recents — paused" : "Recents — no folders tracked";
        ActivityButton.ToolTip = text;
        AutomationProperties.SetName(ActivityButton, text);
        ActivityDot.Visibility = count > 0 && !_activityHost.IsPaused ? Visibility.Visible : Visibility.Collapsed;
        _trayIcon?.Refresh(_settings);
    }

    public AppSettings Settings => _settings;

    public void AttachTrayIcon(TrayIcon trayIcon)
    {
        _trayIcon = trayIcon;
        trayIcon.Refresh(_settings);
    }

    public void ExitFromTray()
    {
        _exitRequested = true;
        Show();
        Close();
        if (IsLoaded) _exitRequested = false;
    }

    public void AllowSessionEnd() => _exitRequested = true;

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

        var saved = SettingsDialog.Show(this, _settings.GlobalHotkey,
            _settings.MinimizeToTray, _settings.StartWithWindows,
            ThemePreference.ParseTheme(_settings.Theme), ThemePreference.ParseHighlight(_settings.Highlight),
            _themeManager.Apply, ApplySettingsChoice);
        if (saved is null)
        {
            // The app is exiting (tray Exit, session end) and closed this
            // window under the dialog: nothing to put back, and registering
            // a hotkey or swapping the palette now would throw.
            if (_closed || Dispatcher.HasShutdownStarted)
                return;

            // Cancelled (Cancel, Esc or the title bar's close button): put
            // back what was there, including any previewed appearance. If
            // the hotkey fails again, it was already failing before (and
            // reported at startup).
            ApplyGlobalHotkey(_settings.GlobalHotkey);
            _themeManager.Apply(ThemePreference.ParseTheme(_settings.Theme), ThemePreference.ParseHighlight(_settings.Highlight));
        }
    }

    private string? ApplySettingsChoice(SettingsChoice choice)
    {
        // Appearance first (already previewed, so usually a no-op): if it
        // fails, nothing else has changed yet.
        try
        {
            _themeManager.Apply(choice.Theme, choice.Highlight);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Could not apply the appearance choice ({ex.GetType().Name}, 0x{ex.HResult:X8}).");
            return "Couldn't change the appearance. Try a different theme or highlight colour.";
        }

        var error = ApplyGlobalHotkey(choice.Hotkey);
        if (error is not null) return error;
        if (!StartupRegistration.TryApply(choice.StartWithWindows, out error))
        {
            // The dialog is still open, so leave the hotkey unregistered;
            // Cancel or a successful Save registers the right one.
            ApplyGlobalHotkey(null);
            return error;
        }
        _settings.GlobalHotkey = choice.Hotkey;
        _settings.MinimizeToTray = choice.MinimizeToTray;
        _settings.StartWithWindows = choice.StartWithWindows;
        _settings.Theme = ThemePreference.Format(choice.Theme);
        _settings.Highlight = ThemePreference.Format(choice.Highlight);
        PersistWindowState(_settings);
        _settingsService.Save(_settings);
        _trayIcon?.Refresh(_settings);
        return null;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_settings.MinimizeToTray && !_exitRequested)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
        if (e.Cancel) _exitRequested = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _globalHotkey?.Dispose();
        _globalHotkey = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
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

        _focusSearch();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Surfaced here (rather than from OnSourceInitialized, where the
        // error is found) so a loaded, on-screen window exists for
        // MessageForm to center on. A problem loading places.json never
        // reaches this far: App.xaml.cs resolves it through the recovery
        // dialog before this window is even created.
        if (_globalHotkeyError is not null)
        {
            MessageForm.Show(
                $"The shortcut for bringing QuickerPlaces to the front isn't active.\n\n{_globalHotkeyError}\n\n" +
                "To choose a different one, or turn it off, open Options next to Hide list and choose Settings.",
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

    /// <summary>Ctrl+F: jump to the search box, selecting any existing query so typing replaces it.</summary>
    private void Find_Executed(object sender, ExecutedRoutedEventArgs e) => _focusSearch();

    // -----------------------------------------------------------------
    // Status bar pause (D19). The bar stays up while the pointer is over
    // it or the keyboard focus is inside it, whichever came first and
    // until both have left; MainViewModel owns the timer.
    // -----------------------------------------------------------------

    // Each handler takes its own state from the event itself and only the
    // other one from the element, so neither depends on the order in which
    // WPF updates IsMouseOver and raises MouseEnter/MouseLeave.

    private void StatusBar_MouseEnter(object sender, MouseEventArgs e) => UpdateStatusPause(hovered: true, focused: StatusBar.IsKeyboardFocusWithin);

    private void StatusBar_MouseLeave(object sender, MouseEventArgs e) => UpdateStatusPause(hovered: false, focused: StatusBar.IsKeyboardFocusWithin);

    private void StatusBar_IsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
        => UpdateStatusPause(hovered: StatusBar.IsMouseOver, focused: (bool)e.NewValue);

    private void UpdateStatusPause(bool hovered, bool focused)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        if (hovered || focused)
            viewModel.PauseStatusTimer();
        else
            viewModel.ResumeStatusTimer();
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
