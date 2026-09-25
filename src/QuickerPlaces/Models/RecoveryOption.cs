namespace QuickerPlaces.Models;

/// <summary>
/// One labelled button in a RecoveryDialog: the text shown, which
/// RecoveryChoice it reports, and whether it is the dialog's default
/// button (activated by Enter). RecoveryDialog builds its button row from
/// an ordered list of these — see Views/RecoveryDialog.xaml.cs — so each
/// of the three startup recovery flows in App.xaml.cs controls its own
/// wording and ordering instead of picking from a fixed preset.
/// </summary>
public readonly struct RecoveryOption
{
    public string Label { get; }

    public RecoveryChoice Choice { get; }

    /// <summary>
    /// True if this is the button Enter activates. At most one option in
    /// a given list should set this, and it must never be the destructive
    /// <see cref="RecoveryChoice.StartEmpty"/> choice (plan 5.4) — that
    /// choice must always require a deliberate click.
    /// </summary>
    public bool IsDefault { get; }

    public RecoveryOption(string label, RecoveryChoice choice, bool isDefault = false)
    {
        Label = label;
        Choice = choice;
        IsDefault = isDefault;
    }
}
