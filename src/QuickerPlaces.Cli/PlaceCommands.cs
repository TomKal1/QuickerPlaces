using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Remote;

namespace QuickerPlaces.Cli;

/// <summary>The <c>qp places …</c> commands.</summary>
public static class PlaceCommands
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 10000;

    private static readonly OptionSpec Limit = new("limit", $"Return at most N (1-{MaxLimit}; default {DefaultLimit}).", ValueName: "n");

    public static IEnumerable<CommandSpec> All() => new[]
    {
        new CommandSpec("places list", "List saved places, A-Z by alias unless sorted otherwise.", "no arguments", 0, 0, new[]
            {
                new OptionSpec("tag", "Only places with this tag (any case).", ValueName: "tag"),
                new OptionSpec("favourites", "Only favourites.", IsFlag: true),
                new OptionSpec("deleted", "List places in Recently Deleted instead.", IsFlag: true),
                new OptionSpec("sort", "alias, opens, last-opened or added. Opens and dates sort newest/most first.", ValueName: "key"),
                Limit
            }, false, List, new[] { "qp places list --sort opens --limit 10", "qp places list --tag client" }),

        new CommandSpec("places find", "Search places: every word must appear in the alias, folder/link, a tag or the note (any case).", "a search text", 1, null,
            new[] { Limit }, false, Find, new[] { "qp places find tax 2026", "qp places find acme invoices" }),

        new CommandSpec("places get", "One place in full, including its timed opens. <ref> is its alias (any case) or id.", "<ref>", 1, 1,
            new[] { new OptionSpec("deleted", "Also look in Recently Deleted.", IsFlag: true) }, false, Get, new[] { "qp places get Downloads" }),

        new CommandSpec("places top", "The places opened most in a period, from their timed opens.", "no arguments", 0, 0,
            Period.Options.Append(Limit).ToArray(), false, Top, new[] { "qp places top --period week --limit 5", "qp places top --days 3" }),

        new CommandSpec("places open", "Open a place (folder in the file manager, link in the browser) and record the open.", "<ref>", 1, 1,
            new[] { new OptionSpec("dry-run", "Resolve the place and say what would open, without opening it.", IsFlag: true) }, true, Open, new[] { "qp places open \"Tax 2026\"" }),

        new CommandSpec("places add", "Save a new place. Give --folder or --url, not both.", "no arguments", 0, 0, new[]
            {
                new OptionSpec("alias", "The unique name to save it under.", ValueName: "name"),
                new OptionSpec("folder", "An absolute folder path.", ValueName: "path"),
                new OptionSpec("url", "An http(s) link.", ValueName: "url"),
                new OptionSpec("tags", "Tags, comma-separated; may be repeated.", ValueName: "a,b", Repeatable: true),
                new OptionSpec("note", "What the place is for.", ValueName: "text")
            }, true, Add, new[] { "qp places add --alias \"Tax 2026\" --folder \"D:\\Finance\\Tax\\2026\" --tags finance,tax" }),

        new CommandSpec("places tag", "Change a place's tags: --set replaces them all; --add and --remove adjust them.", "<ref>", 1, 1, new[]
            {
                new OptionSpec("set", "The new tags, comma-separated (empty to clear).", ValueName: "a,b", Repeatable: true),
                new OptionSpec("add", "Tags to add, comma-separated.", ValueName: "a,b", Repeatable: true),
                new OptionSpec("remove", "Tags to remove (any case), comma-separated.", ValueName: "a,b", Repeatable: true),
                new OptionSpec("deleted", "Also look in Recently Deleted.", IsFlag: true)
            }, true, Tag, new[] { "qp places tag Downloads --add inbox", "qp places tag Downloads --set \"\"" }),

        new CommandSpec("places note", "Set or clear a place's note.", "<ref>", 1, 1, new[]
            {
                new OptionSpec("text", "The note.", ValueName: "text"),
                new OptionSpec("clear", "Remove the note.", IsFlag: true),
                new OptionSpec("deleted", "Also look in Recently Deleted.", IsFlag: true)
            }, true, Note, new[] { "qp places note Downloads --text \"Clear monthly\"" })
    };

    private static object List(CliContext context, CliArgs args)
    {
        var service = context.ReadPlaces();
        IEnumerable<Place> places = args.Flag("deleted") ? service.RecentlyDeleted : service.Places;

        if (args.Flag("favourites"))
            places = places.Where(p => p.IsFavourite);
        if (args.Value("tag") is { } tag)
            places = places.Where(p => p.Tags.Contains(tag.Trim(), StringComparer.OrdinalIgnoreCase));

        places = (args.Value("sort") ?? "alias").ToLowerInvariant() switch
        {
            "alias" => places.OrderBy(p => p.Alias, StringComparer.CurrentCultureIgnoreCase),
            "opens" => places.OrderByDescending(p => p.OpenCount).ThenBy(p => p.Alias, StringComparer.CurrentCultureIgnoreCase),
            "last-opened" => places.OrderByDescending(p => p.LastOpenedAt ?? DateTimeOffset.MinValue).ThenBy(p => p.Alias, StringComparer.CurrentCultureIgnoreCase),
            "added" => places.OrderByDescending(p => p.DateAdded),
            _ => throw CliError.Usage("--sort must be alias, opens, last-opened or added.")
        };

        return Page(places.ToList(), args, context);
    }

    private static object Find(CliContext context, CliArgs args)
    {
        var query = string.Join(' ', args.Positionals);
        var matches = context.ReadPlaces().Places
            .Where(p => Matches(p, query))
            .OrderBy(p => p.Alias, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var result = Page(matches, args, context);
        result["query"] = query;
        return result;
    }

    private static object Get(CliContext context, CliArgs args)
    {
        var place = Resolve(context.ReadPlaces(), args.Positionals[0], args.Flag("deleted"));
        return new Dictionary<string, object?> { ["place"] = Json.Place(place, context.Shell, includeOpens: true) };
    }

    private static object Top(CliContext context, CliArgs args)
    {
        var period = Period.Parse(args, context.Time);
        var limit = args.Int("limit", 1, MaxLimit) ?? 10;

        var ranked = context.ReadPlaces().Places
            .Select(p => (Place: p, Opens: p.Opens.Where(o => period.Contains(o, context.Time)).ToList()))
            .Where(r => r.Opens.Count > 0)
            .OrderByDescending(r => r.Opens.Count)
            .ThenByDescending(r => r.Opens[^1])
            .ToList();

        return new Dictionary<string, object?>
        {
            ["period"] = period.ToJson(),
            ["total"] = ranked.Count,
            ["places"] = ranked.Take(limit).Select(r =>
            {
                var json = Json.Place(r.Place, context.Shell);
                json["opensInPeriod"] = r.Opens.Count;
                json["lastOpenInPeriod"] = r.Opens[^1];
                return json;
            }).ToList()
        };
    }

    private static object Open(CliContext context, CliArgs args)
    {
        var place = Resolve(context.ReadPlaces(), args.Positionals[0], includeDeleted: false);
        var result = new Dictionary<string, object?> { ["place"] = Json.Place(place, context.Shell) };
        if (args.Flag("dry-run"))
        {
            result["opened"] = false;
            result["recorded"] = false;
            return result;
        }

        // Launched here, recorded through Execute — by the app when it is open.
        var outcome = Launch(place, context.Shell);
        switch (outcome.Status)
        {
            case OpenStatus.Missing:
                throw new CliError(ErrorCodes.OpenFailed, "The place's folder no longer exists.", new JsonObject { ["status"] = "missing", ["resource"] = place.Resource });
            case OpenStatus.Failed:
                throw new CliError(ErrorCodes.OpenFailed, outcome.ErrorMessage ?? "The place couldn't be opened.", new JsonObject { ["status"] = "failed" });
        }

        result["opened"] = true;
        try
        {
            context.Execute(StoreOperations.RecordPlaceOpen, new JsonObject { ["id"] = place.Id.ToString() });
            result["recorded"] = true;
            result["place"] = Json.Place(Resolve(context.ReadPlaces(), place.Id.ToString(), includeDeleted: false), context.Shell);
        }
        catch (CliError error)
        {
            // The place is open either way; only the record of it failed.
            result["recorded"] = false;
            result["notRecordedReason"] = error.Code;
        }
        return result;
    }

    private static object Add(CliContext context, CliArgs args)
    {
        var alias = args.Value("alias") ?? throw CliError.Usage("'places add' needs --alias.");
        var (type, resource) = (args.Value("folder"), args.Value("url")) switch
        {
            ({ } folder, null) => ("folder", folder),
            (null, { } url) => ("url", url),
            _ => throw CliError.Usage("'places add' needs exactly one of --folder or --url.")
        };

        var request = new JsonObject { ["alias"] = alias, ["type"] = type, ["resource"] = resource };
        if (args.Values("tags").Count > 0)
            request["tags"] = Array(args.List("tags"));
        if (args.Value("note") is { } note)
            request["note"] = note;

        return Changed(context, context.Execute(StoreOperations.AddPlace, request));
    }

    private static object Tag(CliContext context, CliArgs args)
    {
        var set = args.Values("set").Count > 0 ? args.List("set") : null;
        var add = args.List("add");
        var remove = args.List("remove");
        if (set is null && add.Count == 0 && remove.Count == 0)
            throw CliError.Usage("'places tag' needs --set, --add or --remove.");

        var place = Resolve(context.ReadPlaces(), args.Positionals[0], args.Flag("deleted"));
        var tags = (set ?? place.Tags).Concat(add).Where(t => !remove.Contains(t.Trim(), StringComparer.OrdinalIgnoreCase));

        return Changed(context, context.Execute(StoreOperations.SetPlaceTags, new JsonObject { ["id"] = place.Id.ToString(), ["tags"] = Array(tags) }));
    }

    private static object Note(CliContext context, CliArgs args)
    {
        var text = args.Value("text");
        if ((text is null) == !args.Flag("clear"))
            throw CliError.Usage("'places note' needs exactly one of --text or --clear.");

        var place = Resolve(context.ReadPlaces(), args.Positionals[0], args.Flag("deleted"));

        return Changed(context, context.Execute(StoreOperations.SetPlaceNote, new JsonObject { ["id"] = place.Id.ToString(), ["note"] = text }));
    }

    /// <summary>
    /// The app's own search rule (PlaceSearch) widened to tags and the note:
    /// every whitespace-separated word must appear, in any case, in the alias,
    /// the folder or link, a tag, or the note.
    /// </summary>
    public static bool Matches(Place place, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        foreach (var term in query.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var found = PlaceSearch.Matches(place, term)
                || place.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase))
                || (place.Note?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!found)
                return false;
        }

        return true;
    }

    /// <summary>
    /// A place by id, else by alias (aliases are unique ignoring case, so at
    /// most one active place matches). With <paramref name="includeDeleted"/>
    /// Recently Deleted is searched after the active places; two deleted
    /// places can share an alias, which is reported as ambiguous with their ids.
    /// </summary>
    public static Place Resolve(PlacesService service, string reference, bool includeDeleted)
    {
        var text = reference.Trim();
        IEnumerable<Place> pool = includeDeleted ? service.Places.Concat(service.RecentlyDeleted) : service.Places;

        if (Guid.TryParse(text, out var id) && pool.FirstOrDefault(p => p.Id == id) is { } byId)
            return byId;

        if (service.Places.FirstOrDefault(p => string.Equals(p.Alias, text, StringComparison.OrdinalIgnoreCase)) is { } active)
            return active;

        if (includeDeleted)
        {
            var deleted = service.RecentlyDeleted.Where(p => string.Equals(p.Alias, text, StringComparison.OrdinalIgnoreCase)).ToList();
            if (deleted.Count == 1)
                return deleted[0];
            if (deleted.Count > 1)
            {
                throw new CliError(ErrorCodes.Ambiguous, $"More than one place in Recently Deleted is called \"{text}\". Use its id.",
                    new JsonObject { ["ids"] = new JsonArray(deleted.Select(p => (JsonNode)p.Id.ToString()).ToArray()) });
            }
        }

        var suggestions = service.Places.Where(p => Matches(p, text)).Take(5).Select(p => (JsonNode)p.Alias).ToArray();
        throw new CliError(ErrorCodes.NotFound, $"No place is called \"{text}\".", new JsonObject { ["suggestions"] = new JsonArray(suggestions) });
    }

    /// <summary>The place an operation changed, read back from disk, where whichever side ran it has saved it.</summary>
    private static object Changed(CliContext context, JsonObject data)
    {
        var id = data["id"]!.GetValue<string>();
        var place = Resolve(context.ReadPlaces(), id, includeDeleted: true);
        return new Dictionary<string, object?> { ["place"] = Json.Place(place, context.Shell) };
    }

    private static JsonArray Array(IEnumerable<string> values) => new(values.Select(v => (JsonNode)v).ToArray());

    private static Dictionary<string, object?> Page(IReadOnlyList<Place> places, CliArgs args, CliContext context)
    {
        var limit = args.Int("limit", 1, MaxLimit) ?? DefaultLimit;
        return new Dictionary<string, object?>
        {
            ["total"] = places.Count,
            ["places"] = Json.Places(places.Take(limit), context.Shell).ToList()
        };
    }

    /// <summary>PlaceLauncher's checks and launch, without its RecordOpen: the open is recorded through Execute instead.</summary>
    private static OpenOutcome Launch(Place place, IShell shell)
    {
        if (place.Type == PlaceType.Folder && !shell.DirectoryExists(place.Resource))
            return new OpenOutcome(OpenStatus.Missing, null, PersistenceResult.Ok());

        try
        {
            shell.Open(place.Resource);
            return new OpenOutcome(OpenStatus.Launched, null, PersistenceResult.Ok());
        }
        catch (Exception ex)
        {
            return new OpenOutcome(OpenStatus.Failed, ex.Message, PersistenceResult.Ok());
        }
    }
}
