using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.History;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Remote;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.Cli;

/// <summary>The read-only <c>qp sessions …</c>, <c>qp files recent</c> and <c>qp folders recent</c> commands.</summary>
public static class ActivityCommands
{
    private static readonly OptionSpec Limit = new("limit", "Return at most N (1-10000; default 50).", ValueName: "n");

    public static IEnumerable<CommandSpec> All() => new[]
    {
        new CommandSpec("sessions list", "List project sessions (saved sets of PDF, Word and Excel files), most recently used first.", "no arguments", 0, 0,
            new[] { new OptionSpec("tag", "Only sessions with this tag (any case).", ValueName: "tag"), Limit }, false, ListSessions,
            new[] { "qp sessions list --tag client" }),

        new CommandSpec("sessions get", "One session in full, with its files and every reopen. <ref> is its name (any case) or id.", "<ref>", 1, 1,
            Array.Empty<OptionSpec>(), false, GetSession, new[] { "qp sessions get \"Henderson audit\"" }),

        new CommandSpec("sessions open", "Open a session's files (all, or those named with --file) and record the reopen. Files that no longer exist are listed, not opened.", "<ref>", 1, 1,
            new[]
            {
                new OptionSpec("file", "Open only this file of the session; may be repeated.", ValueName: "path", Repeatable: true),
                new OptionSpec("dry-run", "Say what would open, without opening it.", IsFlag: true)
            }, true, OpenSession, new[] { "qp sessions open \"Henderson audit\"" }),

        new CommandSpec("activity days", "Day by day in a period, newest first: the places opened, sessions reopened, files opened and folders spent time in. Days with nothing are left out.", "no arguments", 0, 0,
            Period.Options.Append(new OptionSpec("limit", "At most N of each kind per day (1-100; default 10).", ValueName: "n")).ToArray(),
            false, Days, new[] { "qp activity days --period week", "qp activity days --days 1" }),

        new CommandSpec("files recent", "PDF, Word and Excel files opened in a period, most recent first, from Recent Files and the activity history (needs Recent Files turned on in the app).", "no arguments", 0, 0,
            Period.Options.Append(new OptionSpec("kind", "pdf, word and/or excel, comma-separated.", ValueName: "kinds", Repeatable: true)).Append(Limit).ToArray(),
            false, RecentFiles, new[] { "qp files recent --period week --kind pdf" }),

        new CommandSpec("folders recent", "Folders you spent time in, in a period, most time first, from the app's opt-in folder tracking (Recents). Each folder names the saved place it is, if any. Folder detail is kept for 62 days in the app, and for good in the activity history, which this reads too.", "no arguments", 0, 0,
            Period.Options.Append(Limit).ToArray(), false, RecentFolders, new[] { "qp folders recent --period month --limit 20" })
    };

    private static object ListSessions(CliContext context, CliArgs args)
    {
        var store = context.ReadSessions();
        Available(store.LoadOutcome, "sessions.json");
        IEnumerable<SessionSnapshot> sessions = store.Sessions;
        if (args.Value("tag") is { } tag)
            sessions = sessions.Where(s => s.Tags.Contains(tag.Trim(), StringComparer.OrdinalIgnoreCase));

        var list = sessions.OrderByDescending(s => s.LastUsedAt).ToList();
        return new Dictionary<string, object?>
        {
            ["total"] = list.Count,
            ["sessions"] = list.Take(args.Int("limit", 1, 10000) ?? 50).Select(s => Session(s, context, full: false)).ToList()
        };
    }

    private static object GetSession(CliContext context, CliArgs args)
        => new Dictionary<string, object?> { ["session"] = Session(ResolveSession(context, args.Positionals[0]), context, full: true) };

    private static object OpenSession(CliContext context, CliArgs args)
    {
        var session = ResolveSession(context, args.Positionals[0]);
        var chosen = session.Files.ToList();
        if (args.Values("file").Count > 0)
        {
            var wanted = args.Values("file");
            var unknown = wanted.Where(w => !session.Files.Any(f => DocumentPaths.Same(f, w))).ToList();
            if (unknown.Count > 0)
                throw CliError.Usage($"\"{unknown[0]}\" isn't one of this session's files. 'qp sessions get' lists them.");
            chosen = session.Files.Where(f => wanted.Any(w => DocumentPaths.Same(f, w))).ToList();
        }

        var missing = chosen.Where(f => !context.Shell.FileExists(f)).ToList();
        var result = new Dictionary<string, object?> { ["session"] = Session(session, context, full: false), ["missing"] = missing };
        if (args.Flag("dry-run"))
        {
            result["wouldOpen"] = chosen.Except(missing).ToList();
            result["recorded"] = false;
            return result;
        }

        // SessionLauncher's steps, with the reopen recorded through Execute
        // (by the app while it is open) instead of on a store held here.
        var launched = new List<string>();
        var failed = new List<Dictionary<string, object?>>();
        foreach (var file in chosen.Except(missing))
        {
            try
            {
                context.Shell.Open(file);
                launched.Add(file);
            }
            catch (Exception ex)
            {
                failed.Add(new Dictionary<string, object?> { ["path"] = file, ["message"] = ex.Message });
            }
        }

        result["opened"] = launched;
        result["failed"] = failed;
        if (launched.Count == 0)
            throw new CliError(ErrorCodes.OpenFailed, "None of the session's files could be opened.", new JsonObject
            {
                ["status"] = missing.Count == chosen.Count ? "missing" : "failed",
                ["missing"] = new JsonArray(missing.Select(m => (JsonNode)m).ToArray())
            });

        try
        {
            context.Execute(StoreOperations.MarkSessionOpened, new JsonObject { ["id"] = session.Id });
            result["recorded"] = true;
        }
        catch (CliError error)
        {
            result["recorded"] = false;
            result["notRecordedReason"] = error.Code;
        }
        return result;
    }

    private static SessionSnapshot ResolveSession(CliContext context, string reference)
    {
        var store = context.ReadSessions();
        Available(store.LoadOutcome, "sessions.json");
        var text = reference.Trim();
        return store.Find(text)
            ?? store.Sessions.FirstOrDefault(s => string.Equals(s.Name, text, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliError(ErrorCodes.NotFound, $"No session is called \"{text}\".", new JsonObject
            {
                ["suggestions"] = new JsonArray(store.Sessions.Where(s => s.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(5).Select(s => (JsonNode)s.Name).ToArray())
            });
    }

    /// <summary>
    /// Everything recorded per local day. Folder detail and file opens come
    /// from the stores (62 days) and the activity history (kept for good);
    /// place opens (last 500 per place) and session reopens (365 days) only
    /// from their stores, so an older day may show only some.
    /// </summary>
    private static object Days(CliContext context, CliArgs args)
    {
        var period = Period.Parse(args, context.Time);
        var limit = args.Int("limit", 1, 100) ?? 10;
        var time = context.Time;
        var days = new SortedDictionary<DateOnly, DayBuilder>();
        DayBuilder Day(DateOnly date) => days.TryGetValue(date, out var day) ? day : days[date] = new DayBuilder();

        var placesService = context.ReadPlaces();
        foreach (var place in placesService.Places.Concat(placesService.RecentlyDeleted))
        {
            foreach (var group in place.Opens.GroupBy(o => Period.LocalDate(o, time)).Where(g => period.Contains(g.Key)))
                Day(group.Key).Places.Add((place, group.Count()));
        }

        var sessions = context.ReadSessions();
        if (sessions.LoadOutcome is StoreLoadOutcome.Ok)
        {
            foreach (var session in sessions.Sessions)
            {
                foreach (var group in session.OpenedAt.GroupBy(o => Period.LocalDate(o, time)).Where(g => period.Contains(g.Key)))
                    Day(group.Key).Sessions.Add((session, group.Count()));
            }
        }

        var history = context.ReadHistory(period.From, period.To);
        var files = context.ReadRecentFiles();
        if (files.LoadOutcome is StoreLoadOutcome.Ok)
        {
            var opens = files.QueryHistory().ToDictionary(f => f.Path, f => f.Opens.ToList(), StringComparer.OrdinalIgnoreCase);
            foreach (var (path, older) in HistoryMerge.FileOpens(history))
                (opens.TryGetValue(path, out var list) ? list : opens[path] = new List<DateTimeOffset>()).AddRange(older);

            foreach (var (path, fileOpens) in opens)
            {
                if (DocumentKinds.FromPath(path) is not { } kind)
                    continue;
                foreach (var group in fileOpens.Distinct().GroupBy(o => Period.LocalDate(o, time)).Where(g => period.Contains(g.Key)))
                    Day(group.Key).Files.Add((path, kind, group.Count()));
            }
        }

        var activity = context.ReadActivity();
        var placesByFolder = PlacesByFolder(context);
        if (activity.LoadOutcome is StoreLoadOutcome.Ok)
        {
            foreach (var root in activity.AllRoots)
            {
                var held = activity.QueryFolderDays(root.RootId) ?? Array.Empty<Services.Activity.FolderDay>();
                foreach (var folderDay in held.Concat(HistoryMerge.FolderDays(history, root.Path)))
                {
                    if (period.Contains(folderDay.Date))
                        Day(folderDay.Date).Folders.AddRange(folderDay.Folders);
                }
            }
        }

        return new Dictionary<string, object?>
        {
            ["period"] = period.ToJson(),
            ["days"] = days.Reverse().Select(d => new Dictionary<string, object?>
            {
                ["date"] = Json.Date(d.Key),
                ["places"] = d.Value.Places.OrderByDescending(p => p.Opens).ThenBy(p => p.Place.Alias, StringComparer.CurrentCultureIgnoreCase).Take(limit)
                    .Select(p => new Dictionary<string, object?> { ["id"] = p.Place.Id, ["alias"] = p.Place.Alias, ["opens"] = p.Opens }).ToList(),
                ["sessions"] = d.Value.Sessions.OrderByDescending(s => s.Opens).Take(limit)
                    .Select(s => new Dictionary<string, object?> { ["id"] = s.Session.Id, ["name"] = s.Session.Name, ["opens"] = s.Opens }).ToList(),
                ["files"] = d.Value.Files.OrderByDescending(f => f.Opens).Take(limit)
                    .Select(f => new Dictionary<string, object?> { ["path"] = f.Path, ["kind"] = Json.Enum(f.Kind), ["opens"] = f.Opens }).ToList(),
                ["folders"] = d.Value.Folders.OrderByDescending(f => f.Time).Take(limit).Select(f => new Dictionary<string, object?>
                {
                    ["folder"] = f.Folder,
                    ["seconds"] = (long)f.Time.TotalSeconds,
                    ["visits"] = f.Visits,
                    ["place"] = placesByFolder.TryGetValue(FolderKey(f.Folder), out var place)
                        ? new Dictionary<string, object?> { ["id"] = place.Id, ["alias"] = place.Alias }
                        : null
                }).ToList()
            }).ToList()
        };
    }

    private sealed class DayBuilder
    {
        public List<(Place Place, int Opens)> Places { get; } = new();
        public List<(SessionSnapshot Session, int Opens)> Sessions { get; } = new();
        public List<(string Path, DocumentKind Kind, int Opens)> Files { get; } = new();
        public List<Services.Activity.FolderActivity> Folders { get; } = new();
    }

    private static object RecentFiles(CliContext context, CliArgs args)
    {
        var period = Period.Parse(args, context.Time);
        var store = context.ReadRecentFiles();
        Available(store.LoadOutcome, "recent-files.json");

        List<DocumentKind>? kinds = null;
        if (args.Values("kind").Count > 0)
        {
            kinds = args.List("kind").Select(k => k.ToLowerInvariant() switch
            {
                "pdf" => DocumentKind.Pdf,
                "word" => DocumentKind.Word,
                "excel" => DocumentKind.Excel,
                _ => throw CliError.Usage("--kind must be pdf, word or excel.")
            }).Distinct().ToList();
        }

        // The store holds each open for 62 days; older ones, and other PCs', are in the activity history.
        var opens = store.QueryHistory().ToDictionary(f => f.Path, f => f.Opens.ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var (path, older) in HistoryMerge.FileOpens(context.ReadHistory(period.From, period.To)))
            (opens.TryGetValue(path, out var list) ? list : opens[path] = new List<DateTimeOffset>()).AddRange(older);
        var files = opens
            .Select(f => (Path: f.Key, Kind: DocumentKinds.FromPath(f.Key), Opens: f.Value.Distinct().Where(o => period.Contains(Period.LocalDate(o, context.Time))).OrderBy(o => o).ToList()))
            .Where(f => f.Kind is { } kind && (kinds is null || kinds.Contains(kind)) && f.Opens.Count > 0)
            .Select(f => new RecentFileSummary(f.Path, f.Kind!.Value, f.Opens.Count, f.Opens[^1]))
            .OrderByDescending(f => f.LastOpenedAt)
            .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new Dictionary<string, object?>
        {
            ["enabled"] = store.Settings.Enabled,
            ["period"] = period.ToJson(),
            ["total"] = files.Count,
            ["files"] = files.Take(args.Int("limit", 1, 10000) ?? 50).Select(f => new Dictionary<string, object?>
            {
                ["path"] = f.Path,
                ["kind"] = Json.Enum(f.Kind),
                ["exists"] = context.Shell.FileExists(f.Path),
                ["opensInPeriod"] = f.Opens,
                ["lastOpenedAt"] = f.LastOpenedAt
            }).ToList()
        };
    }

    private static object RecentFolders(CliContext context, CliArgs args)
    {
        var period = Period.Parse(args, context.Time);
        var store = context.ReadActivity();
        Available(store.LoadOutcome, "activity.json");

        // Folder keys are full paths spelled as tracked; a saved place is
        // matched by its folder ignoring case and a trailing separator.
        var places = PlacesByFolder(context);
        var from = period.From ?? DateOnly.MinValue;

        var history = context.ReadHistory(period.From, period.To);
        var roots = new List<Dictionary<string, object?>>();
        var folders = new List<(Services.Activity.FolderActivity Folder, string RootPath)>();
        foreach (var root in store.AllRoots)
        {
            // Paused and removed targets still report their history.
            var activity = store.QueryPeriod(root.RootId, from, period.To) is { } held
                ? WithHistory(held, history, root.Path)
                : null;
            roots.Add(new Dictionary<string, object?>
            {
                ["rootId"] = root.RootId,
                ["path"] = root.Path,
                ["enabled"] = root.Enabled,
                ["trackingStartedAt"] = root.TrackingStartedAt,
                ["detailKeptFrom"] = activity is null ? null : Json.Date(activity.DetailKeptFrom),
                ["periodIsPartial"] = activity is not null && (activity.DetailExpired || activity.StartsBeforeTracking)
            });
            if (activity is not null)
                folders.AddRange(activity.Folders.Select(f => (f, root.Path)));
        }

        var ranked = folders.OrderByDescending(f => f.Folder.Time).ThenBy(f => f.Folder.Folder, StringComparer.OrdinalIgnoreCase).ToList();
        return new Dictionary<string, object?>
        {
            ["period"] = period.ToJson(),
            ["roots"] = roots,
            ["total"] = ranked.Count,
            ["folders"] = ranked.Take(args.Int("limit", 1, 10000) ?? 50).Select(f => new Dictionary<string, object?>
            {
                ["folder"] = f.Folder.Folder,
                ["root"] = f.RootPath,
                ["seconds"] = (long)f.Folder.Time.TotalSeconds,
                ["visits"] = f.Folder.Visits,
                ["lastVisitedAt"] = f.Folder.LastVisited,
                ["place"] = places.TryGetValue(FolderKey(f.Folder.Folder), out var place)
                    ? new Dictionary<string, object?> { ["id"] = place.Id, ["alias"] = place.Alias }
                    : null
            }).ToList()
        };
    }

    /// <summary>
    /// A root's period with the history's folders for it added. Detail is
    /// expired only before the history's first day for the root, since the
    /// history keeps folder detail for every day it saved.
    /// </summary>
    private static Services.Activity.ActivityPeriod WithHistory(Services.Activity.ActivityPeriod held,
        IReadOnlyList<Models.History.HistoryMonthDocument> history, string rootPath)
    {
        var days = HistoryMerge.FolderDays(history, rootPath);
        if (days.Count == 0)
            return held;

        var first = days.Min(d => d.Date);
        return held with
        {
            Folders = HistoryMerge.SumFolders(held.Folders, days, held.From, held.To),
            DetailKeptFrom = first < held.DetailKeptFrom ? first : held.DetailKeptFrom,
            TrackingStartedOn = first < held.TrackingStartedOn ? first : held.TrackingStartedOn,
        };
    }

    private static Dictionary<string, Place> PlacesByFolder(CliContext context)
    {
        var map = new Dictionary<string, Place>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var place in context.ReadPlaces().Places.Where(p => p.Type == PlaceType.Folder))
                map.TryAdd(FolderKey(place.Resource), place);
        }
        catch (CliError)
        {
            // An unreadable places.json only costs the place names.
        }
        return map;
    }

    public static string FolderKey(string path) => path.Trim().TrimEnd('\\', '/');

    private static Dictionary<string, object?> Session(SessionSnapshot session, CliContext context, bool full)
    {
        var json = new Dictionary<string, object?>
        {
            ["id"] = session.Id,
            ["name"] = session.Name,
            ["tags"] = session.Tags,
            ["fileCount"] = session.Files.Count,
            ["createdAt"] = session.CreatedAt,
            ["updatedAt"] = session.UpdatedAt,
            ["lastOpenedAt"] = session.LastOpenedAt,
            ["openCount"] = session.OpenedAt.Count
        };
        if (full)
        {
            json["files"] = session.Files.Select(f => new Dictionary<string, object?>
            {
                ["path"] = f,
                ["kind"] = DocumentKinds.FromPath(f) is { } kind ? Json.Enum(kind) : null,
                ["exists"] = context.Shell.FileExists(f)
            }).ToList();
            json["opens"] = session.OpenedAt;
        }
        return json;
    }

    private static void Available(StoreLoadOutcome outcome, string file)
    {
        if (outcome is StoreLoadOutcome.Ok or StoreLoadOutcome.NotPresent)
            return;

        throw new CliError(ErrorCodes.StoreUnavailable, outcome == StoreLoadOutcome.WrittenByNewerVersion
            ? $"{file} was written by a newer version of QuickerPlaces. Update QuickerPlaces (and qp) to read it."
            : $"{file} couldn't be read. Open QuickerPlaces to check it.", new JsonObject { ["outcome"] = Json.Enum(outcome) });
    }
}
