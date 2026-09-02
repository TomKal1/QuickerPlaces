namespace QuickerPlaces.Models;

/// <summary>
/// A save outcome that never throws to the caller — mirrors
/// <c>ValidationResult</c> in <c>Services/PlacesService.cs</c> deliberately:
/// persistence, like validation, never throws across the service boundary,
/// it returns a value the caller inspects. Validation answers "was the
/// input acceptable"; this answers "did the accepted change actually reach
/// disk" — two separate questions a single mutation call can have two
/// separate answers to (see PlacesService.TryAdd's
/// <c>out PersistenceResult</c> parameter).
/// </summary>
public readonly struct PersistenceResult
{
    public bool Saved { get; }

    /// <summary>Short sentence for the banner. Null when Saved is true.</summary>
    public string? UserMessage { get; }

    private PersistenceResult(bool saved, string? userMessage)
    {
        Saved = saved;
        UserMessage = userMessage;
    }

    /// <summary>
    /// A save that succeeded — and also what a call that never attempted a
    /// save reports (a mutation rejected by validation changed nothing, so
    /// nothing failed to reach disk). Callers must therefore never treat a
    /// returned Ok as "the unsaved-changes banner can be cleared": only a
    /// real successful write clears that, and PlacesService.HasUnsavedChanges
    /// — which only Persist() ever touches — is the state to read for it.
    /// </summary>
    public static PersistenceResult Ok() => new(true, null);

    public static PersistenceResult Fail(string userMessage) => new(false, userMessage);
}
