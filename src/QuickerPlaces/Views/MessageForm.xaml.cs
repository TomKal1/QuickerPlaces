using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuickerPlaces.Models;

namespace QuickerPlaces.Views;

/// <summary>
/// The app's themed replacement for System.Windows.MessageBox and for any
/// native TaskDialog — every dialog the app shows should go through
/// MessageForm.Show(...) instead of either of those. See README.md
/// "Dialogs" for why: MessageBox/TaskDialog render with the OS's own
/// default light chrome and system font, breaking the dark/violet theme
/// the same way the unstyled title bar and ComboBox popup did earlier.
/// Being a plain Window, MessageForm inherits the app-wide Window style
/// (background, font) from Theme.xaml for free, and builds its buttons
/// from the same Button/Button.Primary styles as the rest of the app.
///
/// This does mean the ViewModel layer calls into a View type directly
/// (MainViewModel.cs calls MessageForm.Show), which isn't strict MVVM —
/// but that's exactly what the MessageBox.Show calls it replaces already
/// did, so this isn't a new compromise, just the same one carried
/// forward. A stricter app would inject an IDialogService interface
/// instead; this template stays with the simpler direct call since
/// nothing here currently needs the extra layer (or its own unit tests).
/// </summary>
public partial class MessageForm : Window
{
    /// <summary>Which button the user pressed. None until a button is clicked.</summary>
    public MessageFormResult Result { get; private set; } = MessageFormResult.None;

    public MessageForm(string message, string title, MessageFormButtons buttons, MessageFormIcon icon)
    {
        InitializeComponent();

        Title = title;
        MessageText.Text = message;

        if (icon == MessageFormIcon.None)
        {
            IconBadge.Visibility = Visibility.Collapsed;
        }
        else
        {
            IconGlyph.Text = GlyphFor(icon);
            IconBadge.Background = BrushFor(icon);
        }

        BuildButtons(buttons);
    }

    /// <summary>
    /// Shows a themed modal dialog and returns which button was pressed.
    /// Mirrors MessageBox.Show's shape so existing call sites are close to
    /// a drop-in swap. Centers on <paramref name="owner"/> if given,
    /// otherwise on Application.Current.MainWindow, otherwise the screen.
    /// </summary>
    public static MessageFormResult Show(
        string message,
        string title,
        MessageFormButtons buttons = MessageFormButtons.OK,
        MessageFormIcon icon = MessageFormIcon.Info,
        Window? owner = null)
    {
        var form = new MessageForm(message, title, buttons, icon);

        var effectiveOwner = owner ?? Application.Current?.MainWindow;
        if (effectiveOwner is not null && effectiveOwner.IsLoaded && !ReferenceEquals(effectiveOwner, form))
        {
            form.Owner = effectiveOwner;
            form.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            form.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        form.ShowDialog();
        return form.Result;
    }

    private void BuildButtons(MessageFormButtons buttons)
    {
        switch (buttons)
        {
            case MessageFormButtons.OK:
                AddButton("OK", MessageFormResult.OK, isDefault: true, isCancel: true);
                break;

            case MessageFormButtons.OKCancel:
                AddButton("Cancel", MessageFormResult.Cancel, isDefault: false, isCancel: true);
                AddButton("OK", MessageFormResult.OK, isDefault: true, isCancel: false);
                break;

            case MessageFormButtons.YesNo:
                AddButton("No", MessageFormResult.No, isDefault: false, isCancel: true);
                AddButton("Yes", MessageFormResult.Yes, isDefault: true, isCancel: false);
                break;

            case MessageFormButtons.YesNoCancel:
                AddButton("Cancel", MessageFormResult.Cancel, isDefault: false, isCancel: true);
                AddButton("No", MessageFormResult.No, isDefault: false, isCancel: false);
                AddButton("Yes", MessageFormResult.Yes, isDefault: true, isCancel: false);
                break;
        }
    }

    private void AddButton(string text, MessageFormResult result, bool isDefault, bool isCancel)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 84,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = isDefault,
            IsCancel = isCancel
        };

        // The one affirmative action (OK / Yes) uses the accent-filled
        // primary style, matching how MainWindow highlights its one
        // primary action (Run Task); every other button stays neutral.
        if (isDefault)
            button.Style = (Style)FindResource("Button.Primary");

        button.Click += (_, _) =>
        {
            Result = result;
            DialogResult = true;
        };

        ButtonPanel.Children.Add(button);
    }

    private static string GlyphFor(MessageFormIcon icon) => icon switch
    {
        MessageFormIcon.Info => "i",
        MessageFormIcon.Warning => "!",
        MessageFormIcon.Error => "X",
        MessageFormIcon.Question => "?",
        _ => string.Empty
    };

    private Brush BrushFor(MessageFormIcon icon) => icon switch
    {
        MessageFormIcon.Info => (Brush)FindResource("Status.Info"),
        MessageFormIcon.Warning => (Brush)FindResource("Status.Warning"),
        MessageFormIcon.Error => (Brush)FindResource("Status.Error"),
        MessageFormIcon.Question => (Brush)FindResource("Accent"),
        _ => Brushes.Transparent
    };
}
