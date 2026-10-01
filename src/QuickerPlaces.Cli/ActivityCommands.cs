using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Documents;
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

        new CommandSpec("files recent", "PDF, Word and Excel files opened in a period, most recent first (needs Recent Files turned on in the app).", "no arguments", 0, 0,
            Period.Options.Append(new OptionSpec("kind", "pdf, word and/or excel, comma-separated.", ValueName: "kinds", Repeatable: true)).Append(Limit).ToArray(),
            false, RecentFiles, new[] { "qp files recent --period week --kind pdf" }),

        new CommandSpec("folders recent", "Folders you spent time in, in a period, most time first, from the app's opt-in folder tracking (Recents). Each folder names the saved place it is, if any. Folder detail is kept for 62 days.", "no arguments", 0, 0,
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
    {
        var store = context.ReadSessions();
        Available(store.LoadOutcome, "sessions.json");
        var text = args.Positionals[0].Trim();
        var session = store.Find(text)
            ?? store.Sessions.FirstOrDefault(s => string.Equals(s.Name, text, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliError(ErrorCodes.NotFound, $"No session is called \"{text}\".", new JsonObject
            {
                ["suggestions"] = new JsonArray(store.Sessions.Where(s => s.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(5).Select(s => (JsonNode)s.Name).ToArray())
            });

        return new Dictionary<string, object?> { ["session"] = Session(session, context, full: true) };
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

        var files = store.QueryFiles(period.From, period.To, kinds);
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

        var roots = new List<Dictionary<string, object?>>();
        var folders = new List<(Services.Activity.FolderActivity Folder, string RootPath)>();
        foreach (var root in store.Roots)
        {
            // A paused root still reports what it recorded before it was paused.
            var activity = store.QueryPeriod(root.RootId, from, period.To);
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
