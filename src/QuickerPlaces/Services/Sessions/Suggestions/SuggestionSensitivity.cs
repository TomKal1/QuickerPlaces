namespace QuickerPlaces.Services.Sessions.Suggestions;

/// <summary>
/// How readily session suggestions appear (session suggestions plan §6.1):
/// Low suggests only the surest sets, High also the likely ones.
/// </summary>
public enum SuggestionSensitivity
{
    Low,
    Balanced,
    High
}

/// <summary>
/// Reads and writes the sensitivity string in settings.json. Lenient like
/// ThemePreference, but unrecognised text is null rather than a default, so
/// the caller decides whether a per-rule level falls back to the overall one.
/// UI-free and linked into the test project.
/// </summary>
public static class SuggestionSensitivities
{
    public static SuggestionSensitivity? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "low" => SuggestionSensitivity.Low,
        "balanced" => SuggestionSensitivity.Balanced,
        "high" => SuggestionSensitivity.High,
        _ => null
    };

    public static string Format(SuggestionSensitivity sensitivity) => sensitivity switch
    {
        SuggestionSensitivity.Low => "low",
        SuggestionSensitivity.High => "high",
        _ => "balanced"
    };
}
