using System.Windows;
using System.Windows.Input;
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

        DiagnosticLog.Info($"{AppInfo.Name} starting.");

        // Plan 5.6: the single-instance gate runs before any service is
        // constructed — before SettingsService, before PlacesService.
        // That ordering is the entire guarantee that a second launch
        // never reads or writes either store: it is not enforced by
        // anything PlacesService or SettingsService do themselves, only
        // by this method never reaching their constructors when this
        // isn't the first instance.
        var singleInstance = SingleInstance.TryStart();
        if (singleInstance is null)
        {
            // TryStart has already signalled the running instance and
            // logged why. Nothing else to do here —
            // shut down immediately, before touching a single file.
            Shutdown();
            return;
        }

        // Local variables rather than fields, deliberately: they're
        // captured by the Closing lambda below, and (unlike fields)
        // Nullable's flow analysis can prove a captured local is never
        // null at the capture site, so this needs no null-forgiving
        // operators to stay warning-clean under <Nullable>enable</Nullable>.
        RequireAltForAccessKeys();

        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        var placesService = new PlacesService();

        // Plan 5.4: resolve any startup recovery — a damaged store, one
        // that couldn't be opened, or one from a newer version — before
        // the main window is shown at all. This must run ahead of
        // constructing MainViewModel/MainWindow: those assume a resolved,
        // writable PlacesService (or one whose recovery state is at least
        // known to the mutation guard), not a window that might need to
        // show a recovery prompt over itself mid-launch.
        if (!ResolveStoreRecovery(placesService))
        {
            DiagnosticLog.Info($"{AppInfo.Name} exiting from the startup recovery prompt without loading places.");
            singleInstance.Dispose();
            Shutdown();
            return;
        }

        var mainViewModel = new MainViewModel(settings, placesService);

        var mainWindow = new MainWindow(mainViewModel, settings, settingsService);

        mainWindow.Closing += (_, e) =>
        {
            // Last chance for changes the unsaved-changes banner is still
            // offering to retry (file briefly locked, disk since freed up).
            // If it still fails, let the user keep the window open and sort
            // the problem out rather than lose those changes on close.
            if (placesService.HasUnsavedChanges && !placesService.RetrySave().Saved)
            {
                DiagnosticLog.Warn($"Close requested with {placesService.Places.Count} place(s) still unsaved; asking the user.");

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
            DiagnosticLog.Info($"{AppInfo.Name} exiting cleanly.");
            singleInstance.Dispose();
        };

        mainWindow.Show();

        // A second launch attempt signals SingleInstance instead of
        // starting up (above); this is what the running instance does
        // about it. SingleInstance marshals onto the UI thread itself, and
        // the second launch has already granted this process the right to
        // take the foreground (AllowSetForegroundWindow — the documented
        // hand-off, not a SetForegroundWindow workaround), so BringToFront's
        // plain Activate is enough. BringToFront is also what the global
        // hotkey calls: restore, activate, focus the search box.
        singleInstance.ListenForShowRequests(Dispatcher, mainWindow.BringToFront);
    }

    /// <summary>
    /// Access keys ("_Restore selected") fire only with Alt. WPF also fires
    /// one on a plain letter that nothing else took as text, such as R with
    /// the Recently Deleted grid focused, which would restore the selection
    /// on a stray keypress. The letter reaches AccessKeyManager as unhandled
    /// text input, so handling it at the window stops that; a text box has
    /// already taken its own typing by then, and Alt+letter arrives as
    /// SystemText, not Text, so it still works. Menus are unaffected: they
    /// open in a popup, not in a Window.
    /// </summary>
    private static void RequireAltForAccessKeys() =>
        EventManager.RegisterClassHandler(typeof(Window), TextCompositionManager.TextInputEvent,
            new TextCompositionEventHandler((_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Text))
                    e.Handled = true;
            }));

    /// <summary>
    /// Loops the RecoveryDialog until the store's load state is resolved
    /// (Ok/NotPresent, or a failure state the user has genuinely fixed —
    /// a successful retry or a successful quarantine) or the user chooses
    /// to exit. "Show me the file" and a failed retry/quarantine both
    /// return to the dialog rather than leaving the loop — per plan 5.4,
    /// only a resolved state or Exit ends it.
    ///
    /// Returns false if the application should exit without ever showing
    /// MainWindow; true if it's safe to proceed to the normal UI.
    /// </summary>
    private static bool ResolveStoreRecovery(PlacesService placesService)
    {
        while (true)
        {
            var outcome = placesService.LoadOutcome;
            if (outcome is StoreLoadOutcome.Ok or StoreLoadOutcome.NotPresent)
                return true;

            var choice = ShowRecoveryDialog(placesService.PlacesFilePath, outcome);
            DiagnosticLog.Info($"Recovery prompt ({outcome}): user chose {choice}.");

            switch (choice)
            {
                case RecoveryChoice.ShowFile:
                    ExplorerReveal.Reveal(placesService.PlacesFilePath);
                    continue; // Never resolves the state — ask again.

                case RecoveryChoice.StartEmpty:
                    // Offered only for Damaged (see ShowRecoveryDialog) —
                    // QuarantineAndStartEmpty is never called for
                    // Unreadable or WrittenByNewerVersion.
                    var quarantineResult = placesService.QuarantineAndStartEmpty();
                    if (!quarantineResult.Saved)
                    {
                        // The original file was NOT preserved — recovery
                        // stays unresolved (PlacesService guarantees
                        // this), so looping back to the same prompt is
                        // correct, not a bug: proceeding to a writable
                        // state here would risk the next save overwriting
                        // a file that was never actually quarantined.
                        MessageForm.Show(
                            quarantineResult.UserMessage ?? "Couldn't set aside the damaged file.",
                            AppInfo.Name, MessageFormButtons.OK, MessageFormIcon.Error);
                        continue;
                    }
                    return true;

                case RecoveryChoice.TryAgain:
                    // Offered only for Unreadable. Re-runs the whole load;
                    // on success the real data is live and the state is
                    // resolved, on failure the loop asks again.
                    placesService.Reload();
                    if (!placesService.IsRecoveryUnresolved)
                        return true;
                    continue;

                case RecoveryChoice.Exit:
                case RecoveryChoice.None:
                default:
                    // None covers closing the dialog by the title bar,
                    // Alt+F4, or the system menu — RecoveryDialog reports
                    // it identically to an explicit Exit click, never as
                    // a choice that writes or quarantines anything.
                    return false;
            }
        }
    }

    /// <summary>
    /// Builds and shows the recovery prompt for one StoreLoadOutcome.
    /// The three messages are the plan's section 5.4 text verbatim, and
    /// the option sets are not incidental wording — see StoreLoadOutcome's
    /// remarks for exactly what each outcome may and may not do: Damaged
    /// is the only one that ever offers "Start with an empty list", and
    /// neither Unreadable nor WrittenByNewerVersion ever quarantines.
    /// </summary>
    private static RecoveryChoice ShowRecoveryDialog(string path, StoreLoadOutcome outcome) => outcome switch
    {
        StoreLoadOutcome.Damaged => RecoveryDialog.Show(
            "QuickerPlaces couldn't read your saved places. The file appears to be damaged.",
            path,
            AppInfo.Name,
            new[]
            {
                new RecoveryOption("Show me the file", RecoveryChoice.ShowFile),
                new RecoveryOption("Start with an empty list", RecoveryChoice.StartEmpty),
                new RecoveryOption("Exit", RecoveryChoice.Exit, isDefault: true)
            }),

        StoreLoadOutcome.Unreadable => RecoveryDialog.Show(
            "QuickerPlaces couldn't open your saved places. Another program may be using the file, or it may not have permission to read it. Your data is most likely fine.",
            path,
            AppInfo.Name,
            new[]
            {
                new RecoveryOption("Try again", RecoveryChoice.TryAgain, isDefault: true),
                new RecoveryOption("Show me the file", RecoveryChoice.ShowFile),
                new RecoveryOption("Exit", RecoveryChoice.Exit)
            }),

        // WrittenByNewerVersion, and any future outcome this switch
        // doesn't recognize yet — the safe fallback is the same message
        // and options as WrittenByNewerVersion's "let the user act
        // themselves" stance, never a silent proceed.
        _ => RecoveryDialog.Show(
            "These saved places were written by a newer version of QuickerPlaces. Update QuickerPlaces to open them.",
            path,
            AppInfo.Name,
            new[]
            {
                new RecoveryOption("Exit", RecoveryChoice.Exit, isDefault: true),
                new RecoveryOption("Show me the file", RecoveryChoice.ShowFile)
            })
    };

}
