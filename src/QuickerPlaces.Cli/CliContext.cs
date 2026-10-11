using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using QuickerPlaces.Models;
using QuickerPlaces.Models.History;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.History;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Remote;
using QuickerPlaces.Services.Revit.Handlers;
using QuickerPlaces.Services.Revit.Opening;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.Cli;

/// <summary>
/// One run's view of the stores. Reads open each file read-only, so they are
/// safe while the app is running. Every change is a StoreOperations request
/// (<see cref="Execute"/>): run here on the files when the app is closed, or
/// sent to the running app, which runs it on the services it holds and saves
/// it itself. The app keeps the stores in memory and writes all of a store on
/// every change, so writing beside it would be overwritten.
/// </summary>
public sealed class CliContext
{
    public CliContext(CliEnvironment environment, string? dataRoot)
    {
        Environment = environment;
        DataRoot = dataRoot;
        RoamingFolder = AppDataFolders.RoamingFor(dataRoot);
        LocalFolder = AppDataFolders.LocalFor(dataRoot);
        HistoryPath = AppDataFolders.HistoryFor(dataRoot);
    }

    /// <summary>The activity history folder: Documents\QuickerPlaces\History, or "History" under --data-root.</summary>
    public string HistoryPath { get; }

    public CliEnvironment Environment { get; }

    public string? DataRoot { get; }

    public string RoamingFolder { get; }

    public string LocalFolder { get; }

    public TimeProvider Time => Environment.Time;

    public IShell Shell => Environment.Shell;

    public bool AppRunning => Environment.IsAppRunning(AppDataFolders.InstanceScope(DataRoot));

    /// <summary>The Revit handler protocol folder: <c>&lt;data root&gt;\revit</c> under --data-root, else %LocalAppData%\QuickerPlaces\revit.</summary>
    public RevitProtocolFolder RevitFolder => RevitProtocolFolder.ForDataRoot(DataRoot);

    public RevitMachine Revit => Environment.Revit ?? RevitMachine.None();

    /// <summary>settings.json as the app would load it (defaults when there is none), read without creating anything.</summary>
    public AppSettings ReadSettings()
    {
        var path = Path.Combine(LocalFolder, "settings.json");
        return File.Exists(path) ? new SettingsService(path).Load() : new AppSettings();
    }

    public IPlacesStorage PlacesFile => new FilePlacesStorage(RoamingFolder, "places.json");

    public IPlacesStorage SessionsFile => new FilePlacesStorage(RoamingFolder, "sessions.json");

    public IPlacesStorage RecentFilesFile => new FilePlacesStorage(LocalFolder, "recent-files.json");

    public IPlacesStorage ActivityFile => new FilePlacesStorage(LocalFolder, "activity.json");

    public PlacesService ReadPlaces() => Usable(new PlacesService(new ReadOnlyStorage(PlacesFile), Time));

    public SessionStore ReadSessions() => new(new ReadOnlyStorage(SessionsFile), Time);

    public RecentFilesStore ReadRecentFiles() => new(new ReadOnlyStorage(RecentFilesFile), Time);

    public ActivityStore ReadActivity() => new(new ReadOnlyStorage(ActivityFile), Time);

    /// <summary>
    /// The activity history months touching <paramref name="from"/> (null for
    /// all) to <paramref name="to"/> that hold what the stores no longer do,
    /// or another PC's days, with this PC's held days left out (history plan
    /// §5). Read only; nothing in the folder is written.
    /// </summary>
    public IReadOnlyList<HistoryMonthDocument> ReadHistory(DateOnly? from, DateOnly to)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Time.GetUtcNow(), Time.LocalTimeZone).DateTime);
        var cutoffs = HistoryCutoffs.For(today);
        var history = new ActivityHistory(new HistoryFolder(HistoryPath), Time, System.Environment.MachineName);
        return HistoryMonthCache.Needed(history.MonthIndex(), from ?? DateOnly.MinValue, to, cutoffs.DetailFrom)
            .Select(m => history.ReadMonth(m.Year, m.Month, cutoffs))
            .OfType<HistoryMonthDocument>()
            .ToList();
    }

    /// <summary>
    /// Runs one change and returns its data. With the app open the request goes
    /// to it; an app that doesn't answer (closing, or a build from before qp)
    /// is app_running. Afterwards read the stores again for the result: the
    /// change is on disk by then, saved by whichever side ran it.
    /// </summary>
    public JsonObject Execute(string op, JsonObject args)
    {
        var request = new OperationRequest(op, args);
        OperationReply reply;
        if (AppRunning)
        {
            try
            {
                reply = Environment.SendToApp(AppDataFolders.InstanceScope(DataRoot), request);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                throw new CliError(ErrorCodes.AppRunning,
                    "QuickerPlaces is running but didn't take the change. Update QuickerPlaces to the same version as qp, or close it and try again.");
            }
        }
        else
        {
            var places = new Lazy<PlacesService>(() => new PlacesService(PlacesFile, Time));
            var sessions = new Lazy<SessionStore>(() =>
            {
                // Checked read-only first: a damaged sessions.json is set aside
                // when opened for writing, and that is the app's call to make.
                if (ReadSessions().LoadOutcome is not (StoreLoadOutcome.Ok or StoreLoadOutcome.NotPresent))
                    throw new CliError(ErrorCodes.StoreUnavailable, "sessions.json can't be read. Open QuickerPlaces to check it.");
                return new SessionStore(SessionsFile, Time);
            });
            reply = new StoreOperations(() => places.Value, () => sessions.Value).Execute(request, out _);
        }

        if (!reply.Ok)
            throw new CliError(reply.Code ?? ErrorCodes.Internal, reply.Message ?? "The change was refused.", reply.Details);
        return reply.Data ?? new JsonObject();
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

}
