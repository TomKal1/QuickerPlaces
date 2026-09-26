using System;
using QuickerPlaces.Models;

namespace QuickerPlaces.Services;

/// <summary>
/// The one gateway for every launch (Phase 3 D23): the grid, the favourite
/// bubbles, Ctrl+1–9 and the search box's Enter all reach
/// <see cref="Open"/>. It runs the pre-launch check, asks the shell to open
/// the destination, and records the open only when both succeed — the
/// definition of a recorded open (D24, roadmap §4.12), held here and nowhere
/// else. It is the only caller of PlacesService.RecordOpen.
///
/// That definition proves only that Windows accepted the launch, not that a
/// handler application then opened anything; nothing available to
/// QuickerPlaces can prove more. Phase 4 adds a File pre-check here, and
/// Phase 5 a resolution step (opening policy, per-file choice, Ask each
/// time) and a Cancelled status — callers do not change.
///
/// UI-free and linked into the test project: MainViewModel only turns the
/// returned <see cref="OpenOutcome"/> into messages.
/// </summary>
public sealed class PlaceLauncher
{
    private readonly PlacesService _placesService;
    private readonly IShell _shell;

    public PlaceLauncher(PlacesService placesService, IShell shell)
    {
        _placesService = placesService;
        _shell = shell;
    }

    public OpenOutcome Open(Place place)
    {
        if (place.Type == PlaceType.Folder && !_shell.DirectoryExists(place.Resource))
            return new OpenOutcome(OpenStatus.Missing, null, PersistenceResult.Ok());

        try
        {
            _shell.Open(place.Resource);
        }
        catch (Exception ex)
        {
            // Fail gracefully (SI §6.3): a malformed or no-longer-openable
            // destination must never crash the app. The log gets the type of
            // place and of failure only — never the alias or the destination,
            // which the exception's own message may contain (D26).
            DiagnosticLog.Warn($"Opening a {place.Type} place failed ({ex.GetType().Name}).");
            return new OpenOutcome(OpenStatus.Failed, ex.Message, PersistenceResult.Ok());
        }

        // Windows accepted it: the open counts (D24). A failed or refused
        // save does not undo the launch; the result goes to the banner (D26).
        return new OpenOutcome(OpenStatus.Launched, null, _placesService.RecordOpen(place));
    }
}

public enum OpenStatus
{
    /// <summary>Windows accepted the launch (D24). The outcome's Persistence says whether the usage was saved.</summary>
    Launched,

    /// <summary>The pre-launch check failed: the folder no longer exists. Nothing was launched or recorded.</summary>
    Missing,

    /// <summary>Windows refused the launch. Nothing was recorded.</summary>
    Failed
}

/// <summary>What <see cref="PlaceLauncher.Open"/> did.</summary>
/// <param name="Status">Whether the place was launched, and if not, why.</param>
/// <param name="ErrorMessage">For <see cref="OpenStatus.Failed"/>: the shell's message, for the user. Null otherwise.</param>
/// <param name="Persistence">For <see cref="OpenStatus.Launched"/>: RecordOpen's result, for the unsaved-changes banner. PersistenceResult.Ok() otherwise.</param>
public sealed record OpenOutcome(OpenStatus Status, string? ErrorMessage, PersistenceResult Persistence);
