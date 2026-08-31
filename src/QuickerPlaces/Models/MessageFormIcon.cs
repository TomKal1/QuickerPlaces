namespace QuickerPlaces.Models;

/// <summary>
/// Which badge a MessageForm shows next to its message. Each maps to one
/// of Theme.xaml's status brushes rather than an OS icon, so a dialog
/// never breaks the app's own look — see README.md "Dialogs".
/// </summary>
public enum MessageFormIcon
{
    None,
    Info,
    Warning,
    Error,
    Question
}
