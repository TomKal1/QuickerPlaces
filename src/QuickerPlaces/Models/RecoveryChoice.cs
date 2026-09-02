namespace QuickerPlaces.Models;

/// <summary>
/// Which option the user picked in a RecoveryDialog. Mirrors how
/// MessageFormResult sits alongside MessageForm, but RecoveryDialog does
/// not offer a fixed button set the way MessageForm's
/// <c>MessageFormButtons</c> does — the three startup recovery flows in
/// App.xaml.cs each show a different, explicitly-ordered subset of these,
/// built from an ordered list of <see cref="RecoveryOption"/> rather than
/// an enum-selected preset (plan 5.4: "Start with an empty list" must
/// never be a button a user reaches by muscle memory the way a fixed
/// Yes/No set would risk).
/// </summary>
public enum RecoveryChoice
{
    /// <summary>
    /// The dialog was closed without pressing a button — the title bar's
    /// close button, Alt+F4, or the system menu. Treated exactly like
    /// <see cref="Exit"/> by every caller: closing the window is never
    /// equivalent to a choice that writes or quarantines anything.
    /// </summary>
    None,

    /// <summary>Reveal the store file in Explorer, then ask again — never resolves the recovery state on its own.</summary>
    ShowFile,

    /// <summary>Quarantine the damaged file and start with an empty store. Offered only for <c>StoreLoadOutcome.Damaged</c> — never for Unreadable or WrittenByNewerVersion.</summary>
    StartEmpty,

    /// <summary>Re-run the load. Offered only for <c>StoreLoadOutcome.Unreadable</c>, where the file itself was never confirmed to be damaged.</summary>
    TryAgain,

    /// <summary>Close the application without writing anything.</summary>
    Exit
}
