namespace QuickerPlaces.Models;

/// <summary>Which button the user pressed to close a MessageForm.</summary>
public enum MessageFormResult
{
    /// <summary>Closed without pressing a button (e.g. the title bar's close button).</summary>
    None,
    OK,
    Cancel,
    Yes,
    No
}
