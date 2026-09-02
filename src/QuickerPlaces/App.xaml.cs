using System.Diagnostics;
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

        DiagnosticLog.Info($"{AppInfo.Name} starting.");

        // Local variables rather than fields, deliberately: they're
        // captured by the Closing lambda below, and (unlike fields)
        // Nullable's flow analysis can prove a captured local is never
        // null at the capture site, so this needs no null-forgiving
        // operators to stay warning-clean under <Nullable>enable</Nullable>.
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
            Shutdown();
            return;
        }

        var mainViewModel = new MainViewModel(settings, placesService);

        var mainWindow = new MainWindow(mainViewModel, settings);
        mainWindow.Closing += (_, _) =>
        {
            mainWindow.PersistWindowState(settings);
            settingsService.Save(settings);
            DiagnosticLog.Info($"{AppInfo.Name} exiting cleanly.");
        };

        mainWindow.Show();
    }

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
                    RevealInExplorer(placesService.PlacesFilePath);
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

    /// <summary>
    /// Reveals the store file in Explorer with it pre-selected.
    ///
    /// Windows gotcha, and the reason this uses the legacy Arguments
    /// string rather than the tidier ArgumentList: Explorer's switch is
    /// literally "/select,<path>" — the path must be attached to the
    /// comma with no space between them. ArgumentList joins its entries
    /// with spaces, so passing "/select," and the path as two entries
    /// produces "/select, C:\...\places.json", which Explorer does not
    /// recognize as a selection request; it silently opens the default
    /// Documents view instead, leaving the user staring at the wrong
    /// folder during a recovery prompt. One pre-quoted argument string is
    /// the only shape that works here.
    /// </summary>
    private static void RevealInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""));
        }
        catch
        {
            // Failing to open Explorer must never crash or block the
            // recovery flow — the dialog already shows the path in text,
            // so the user can still navigate there by hand.
        }
    }
}
