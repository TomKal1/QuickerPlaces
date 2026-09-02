using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using QuickerPlaces.Models;

namespace QuickerPlaces.Views;

/// <summary>
/// The startup recovery prompt for a store that will not load (plan 5.4).
/// Not MessageForm: MessageForm's MessageFormButtons only offers fixed
/// OK/Cancel/Yes/No sets, and the destructive "Start with an empty list"
/// choice must never occupy a slot a user could hit by reflex the way a
/// preset Yes/No pair would risk. RecoveryDialog instead takes an ordered
/// list of <see cref="RecoveryOption"/> — each with its own label — and
/// builds the button row from it, so the three call sites in App.xaml.cs
/// each control their own wording and ordering directly.
///
/// Two rules are enforced here rather than trusted to callers, because
/// getting them wrong would mean losing a user's data:
/// <list type="bullet">
/// <item>The destructive choice (<see cref="RecoveryChoice.StartEmpty"/>)
/// is never allowed to be the dialog's default (Enter-activated) button —
/// see BuildButtons below.</item>
/// <item>Closing the window any way other than clicking a button (the
/// title bar's close button, Alt+F4, the system menu) reports
/// <see cref="RecoveryChoice.None"/>, which every caller in App.xaml.cs
/// treats identically to <see cref="RecoveryChoice.Exit"/> — never as a
/// choice that writes or quarantines anything.</item>
/// </list>
/// </summary>
public partial class RecoveryDialog : Window
{
    /// <summary>Which option the user picked. RecoveryChoice.None until a button is clicked or the dialog is closed some other way.</summary>
    public RecoveryChoice Result { get; private set; } = RecoveryChoice.None;

    /// <summary>
    /// True once a button has actually been clicked. Closing_Closing uses
    /// this to tell "closed via a button, which already set Result and is
    /// about to close the window itself" from "closed via the title bar
    /// or Alt+F4, which never touched Result" — both reach the Closing
    /// event, but only the second should be forced to None (it already is
    /// None by default, but being explicit here means a future change to
    /// the default doesn't silently break the title-bar-equals-Exit rule).
    /// </summary>
    private bool _choiceMade;

    public RecoveryDialog(string message, string path, string title, IReadOnlyList<RecoveryOption> options)
    {
        InitializeComponent();

        Title = title;
        MessageText.Text = message;
        PathText.Text = path;

        BuildButtons(options);
    }

    /// <summary>
    /// Shows the dialog modally and returns the chosen RecoveryChoice.
    /// Centers on <paramref name="owner"/> when one is loaded, falling
    /// back to the screen otherwise — the same fallback MessageForm.Show
    /// uses, needed here because the very first call in App.xaml.cs runs
    /// before MainWindow is shown, so there may be no loaded owner yet.
    /// </summary>
    public static RecoveryChoice Show(
        string message,
        string path,
        string title,
        IReadOnlyList<RecoveryOption> options,
        Window? owner = null)
    {
        var dialog = new RecoveryDialog(message, path, title, options);

        var effectiveOwner = owner ?? Application.Current?.MainWindow;
        if (effectiveOwner is not null && effectiveOwner.IsLoaded && !ReferenceEquals(effectiveOwner, dialog))
        {
            dialog.Owner = effectiveOwner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
        return dialog.Result;
    }

    private void BuildButtons(IReadOnlyList<RecoveryOption> options)
    {
        foreach (var option in options)
        {
            // Defensive, not just documentation: even if a caller ever
            // marks StartEmpty as the default by mistake, this dialog
            // itself refuses to wire it up as Enter-activated. See the
            // class remarks — this is the rule most likely to matter if
            // it's ever broken.
            var isDefault = option.IsDefault && option.Choice != RecoveryChoice.StartEmpty;

            var button = new Button
            {
                Content = option.Label,
                MinWidth = 96,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = isDefault
            };

            if (isDefault)
                button.Style = (Style)FindResource("Button.Primary");

            var choice = option.Choice;
            button.Click += (_, _) =>
            {
                Result = choice;
                _choiceMade = true;
                DialogResult = true;
            };

            ButtonPanel.Children.Add(button);
        }
    }

    /// <summary>
    /// Closing the window any way other than a button click (title bar
    /// close, Alt+F4, the system menu) must never be mistaken for a choice
    /// that writes or quarantines anything. Result already defaults to
    /// RecoveryChoice.None, so this handler doesn't need to change it —
    /// it exists so that guarantee is explicit and tested, not an
    /// accident of field initialization order.
    /// </summary>
    private void RecoveryDialog_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_choiceMade)
            Result = RecoveryChoice.None;
    }
}
