using System;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.Cli;

/// <summary>
/// One run's view of the stores. Reads open each file read-only, so they are
/// safe while the app is running. Writes (places only, for now) open
/// places.json for real, and only when no QuickerPlaces window is running on
/// it: the app holds the store in memory and writes all of it on every
/// change, so anything written beside it would be overwritten.
/// </summary>
public sealed class CliContext
{
    public CliContext(CliEnvironment environment, string? dataRoot)
    {
        Environment = environment;
        DataRoot = dataRoot;
        RoamingFolder = AppDataFolders.RoamingFor(dataRoot);
        LocalFolder = AppDataFolders.LocalFor(dataRoot);
    }

    public CliEnvironment Environment { get; }

    public string? DataRoot { get; }

    public string RoamingFolder { get; }

    public string LocalFolder { get; }

    public TimeProvider Time => Environment.Time;

    public IShell Shell => Environment.Shell;

    public bool AppRunning => Environment.IsAppRunning(AppDataFolders.InstanceScope(DataRoot));

    public IPlacesStorage PlacesFile => new FilePlacesStorage(RoamingFolder, "places.json");

    public IPlacesStorage SessionsFile => new FilePlacesStorage(RoamingFolder, "sessions.json");

    public IPlacesStorage RecentFilesFile => new FilePlacesStorage(LocalFolder, "recent-files.json");

    public IPlacesStorage ActivityFile => new FilePlacesStorage(LocalFolder, "activity.json");

    public PlacesService ReadPlaces() => Usable(new PlacesService(new ReadOnlyStorage(PlacesFile), Time));

    public SessionStore ReadSessions() => new(new ReadOnlyStorage(SessionsFile), Time);

    public RecentFilesStore ReadRecentFiles() => new(new ReadOnlyStorage(RecentFilesFile), Time);

    public ActivityStore ReadActivity() => new(new ReadOnlyStorage(ActivityFile), Time);

    /// <summary>places.json opened to change: refused while the app is running on it, or when it did not load cleanly.</summary>
    public PlacesService WritePlaces()
    {
        if (AppRunning)
        {
            throw new CliError(ErrorCodes.AppRunning,
                "QuickerPlaces is running, so the CLI can't change your places: the app would overwrite the change. Close QuickerPlaces and try again.");
        }

        return Usable(new PlacesService(PlacesFile, Time));
    }

    private static PlacesService Usable(PlacesService service)
    {
        if (service.LoadOutcome is StoreLoadOutcome.Ok or StoreLoadOutcome.NotPresent)
            return service;

        throw new CliError(ErrorCodes.StoreUnavailable, service.LoadOutcome switch
        {
            StoreLoadOutcome.WrittenByNewerVersion => "places.json was written by a newer version of QuickerPlaces. Update QuickerPlaces (and qp) to read it.",
            StoreLoadOutcome.Damaged => "places.json is damaged. Open QuickerPlaces to recover it.",
            _ => "places.json couldn't be opened. Try again, or open QuickerPlaces to check it."
        }, new() { ["outcome"] = Json.Enum(service.LoadOutcome) });
    }

    /// <summary>Turns a failed save into the save_failed error; a saved one passes.</summary>
    public static void EnsureSaved(PersistenceResult result)
    {
        if (!result.Saved)
            throw new CliError(ErrorCodes.SaveFailed, result.UserMessage ?? "The change couldn't be saved.");
    }
}
