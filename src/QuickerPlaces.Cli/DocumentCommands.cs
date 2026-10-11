using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Remote;

namespace QuickerPlaces.Cli;

/// <summary>
/// <c>qp files open</c> and <c>qp sessions save</c>: together, "save what I
/// have open as a session". qp finds the files and stores the session; naming
/// it is left to the caller (an agent), with a plain suggestion to start from.
/// </summary>
public static class DocumentCommands
{
    public static IEnumerable<CommandSpec> All() => new[]
    {
        new CommandSpec("files open", "The PDF, Office, text, Revit and AutoCAD files open now (Windows only), found as the app's Sessions screen finds them, each with its folder and the saved place it is in. Also groups them by folder and suggests a session name. Detection is a best guess: show the list to the user before saving.", "no arguments", 0, 0,
            new[] { new OptionSpec("suggestions", "Also list recently opened files that may not be open now (likelyOpen: false).", IsFlag: true) },
            false, OpenFiles, new[] { "qp files open", "qp files open --suggestions" }),

        new CommandSpec("sessions save", "Save a new project session from PDF, Office, text, Revit and AutoCAD files.", "no arguments", 0, 0, new[]
            {
                new OptionSpec("name", "The session's name (unique, up to 100 characters).", ValueName: "name"),
                new OptionSpec("tags", "Tags, comma-separated; may be repeated.", ValueName: "a,b", Repeatable: true),
                new OptionSpec("file", "A full path to a file to include; repeat for each file.", ValueName: "path", Repeatable: true)
            }, true, SaveSession, new[] { "qp sessions save --name \"Acme audit 2026-10-01\" --tags acme,audit --file \"D:\\Clients\\Acme\\Audit\\Report.pdf\" --file \"D:\\Clients\\Acme\\Audit\\Figures.xlsx\"" })
    };

    private static object OpenFiles(CliContext context, CliArgs args)
    {
        var scan = context.Environment.ScanOpenDocuments?.Invoke()
            ?? throw new CliError(ErrorCodes.Unsupported, "Finding open files needs Windows.");

        var candidates = scan.Candidates.Where(c => c.IsLikelyOpen || args.Flag("suggestions")).ToList();
        var places = PlacesByFolder(context);

        var files = candidates.Select(c =>
        {
            var folder = Path.GetDirectoryName(c.Path) ?? "";
            var place = Containing(places, folder);
            return new Dictionary<string, object?>
            {
                ["path"] = c.Path,
                ["kind"] = DocumentKinds.FromPath(c.Path) is { } kind ? Json.Enum(kind) : null,
                ["likelyOpen"] = c.IsLikelyOpen,
                ["reason"] = c.Reason,
                ["lastOpenedAt"] = c.LastOpenedAt,
                ["folder"] = folder,
                ["place"] = place is null ? null : new Dictionary<string, object?> { ["id"] = place.Id, ["alias"] = place.Alias }
            };
        }).ToList();

        var open = candidates.Where(c => c.IsLikelyOpen).Select(c => c.Path).ToList();
        var folders = open
            .GroupBy(p => Path.GetDirectoryName(p) ?? "", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => new Dictionary<string, object?>
            {
                ["folder"] = g.Key,
                ["files"] = g.Count(),
                ["place"] = Containing(places, g.Key) is { } place ? new Dictionary<string, object?> { ["id"] = place.Id, ["alias"] = place.Alias } : null
            }).ToList();

        return new Dictionary<string, object?>
        {
            ["files"] = files,
            ["folders"] = folders,
            ["unmatchedTitles"] = scan.UnmatchedTitles,
            ["warning"] = scan.Warning,
            ["suggestedName"] = SuggestName(open, places, context.Time)
        };
    }

    private static object SaveSession(CliContext context, CliArgs args)
    {
        var name = args.Value("name") ?? throw CliError.Usage("'sessions save' needs --name.");
        var files = args.Values("file");
        if (files.Count == 0)
            throw CliError.Usage("'sessions save' needs at least one --file.");

        var data = context.Execute(StoreOperations.CreateSession, new JsonObject
        {
            ["name"] = name,
            ["tags"] = new JsonArray(args.List("tags").Select(t => (JsonNode)t).ToArray()),
            ["files"] = new JsonArray(files.Select(f => (JsonNode)f).ToArray())
        });

        var id = data["id"]!.GetValue<string>();
        var session = context.ReadSessions().Find(id)
            ?? throw new CliError(ErrorCodes.Internal, "The session was saved but can't be read back.");
        return new Dictionary<string, object?>
        {
            ["session"] = new Dictionary<string, object?>
            {
                ["id"] = session.Id,
                ["name"] = session.Name,
                ["tags"] = session.Tags,
                ["files"] = session.Files,
                ["createdAt"] = session.CreatedAt
            }
        };
    }

    /// <summary>
    /// A plain starting point: the saved place (or else the folder) most of
    /// the open files are in, and today's date — "Acme Client 2026-10-01".
    /// An agent is expected to do better from the file names.
    /// </summary>
    public static string? SuggestName(IReadOnlyList<string> openFiles, IReadOnlyDictionary<string, Place> places, TimeProvider time)
    {
        if (openFiles.Count == 0)
            return null;

        var labels = openFiles
            .Select(f => Path.GetDirectoryName(f) ?? "")
            .Select(folder => Containing(places, folder)?.Alias ?? FolderLabel(folder))
            .Where(l => l.Length > 0)
            .GroupBy(l => l, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();

        var date = Period.LocalDate(time.GetUtcNow(), time).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return labels.Count == 0 ? $"Session {date}" : $"{labels[0]} {date}";
    }

    private static string FolderLabel(string folder)
    {
        var trimmed = folder.TrimEnd('\\', '/');
        var cut = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        return cut >= 0 ? trimmed[(cut + 1)..] : trimmed;
    }

    /// <summary>The saved folder place that is <paramref name="folder"/> or the nearest one above it.</summary>
    private static Place? Containing(IReadOnlyDictionary<string, Place> places, string folder)
    {
        var key = ActivityCommands.FolderKey(folder);
        while (key.Length > 0)
        {
            if (places.TryGetValue(key, out var place))
                return place;
            var cut = key.LastIndexOfAny(new[] { '\\', '/' });
            if (cut <= 0)
                break;
            key = key[..cut];
        }
        return null;
    }

    private static IReadOnlyDictionary<string, Place> PlacesByFolder(CliContext context)
    {
        try
        {
            return context.ReadPlaces().Places
                .Where(p => p.Type == PlaceType.Folder)
                .GroupBy(p => ActivityCommands.FolderKey(p.Resource), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch (CliError)
        {
            return new Dictionary<string, Place>();
        }
    }
}
