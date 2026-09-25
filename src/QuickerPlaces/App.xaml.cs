using System.Windows;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.ViewModels;
using QuickerPlaces.Views;

namespace QuickerPlaces;

/// <summary>
/// Startup/shutdown orchestration. QuickerPlaces has no tray icon and no
/// background/silent run mode (see SI §1/§3) — it is a normal window app:
/// one window shows on launch, and closing it exits the process. Window
/// chrome (bounds, grid-expanded state) is saved once here on clean exit;
/// Places data itself is saved continuously by PlacesService as the user
/// edits it (see Services/PlacesService.cs), independent of this.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Local variables rather than fields, deliberately: they're
        // captured by the Closing lambda below, and (unlike fields)
        // Nullable's flow analysis can prove a captured local is never
        // null at the capture site, so this needs no null-forgiving
        // operators to stay warning-clean under <Nullable>enable</Nullable>.
        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        var placesService = new PlacesService();
        var mainViewModel = new MainViewModel(settings, placesService);

        var mainWindow = new MainWindow(mainViewModel, settings);
        mainWindow.Closing += (_, e) =>
        {
            // Last chance for changes that failed to save earlier (file
            // briefly locked, disk since freed up). If it still fails, let
            // the user keep the window open and sort the problem out
            // rather than lose those changes on close.
            if (!placesService.TrySave())
            {
                var closeAnyway = MessageForm.Show(
                    "Some of your recent changes still couldn't be saved and will be lost if QuickerPlaces closes now.\n\nClose anyway?",
                    AppInfo.Name, MessageFormButtons.YesNo, MessageFormIcon.Warning, mainWindow);

                if (closeAnyway != MessageFormResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            mainWindow.PersistWindowState(settings);
            settingsService.Save(settings);
        };

        mainWindow.Show();
    }
}
