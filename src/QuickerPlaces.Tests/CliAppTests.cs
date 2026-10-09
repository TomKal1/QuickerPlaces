using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using QuickerPlaces.Cli;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.History;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Remote;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The qp command line end to end against a temporary data root: one JSON
/// document per run, the envelope and exit codes, the read-only guarantee,
/// and changes routed through the app while it is running. The "app" here
/// is what the real one is to qp: services held in memory over the same
/// files, answering StoreOperations requests.
/// </summary>
public sealed class CliAppTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 30, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();
    private readonly FakeShell _shell = new();
    private readonly ManualTimeProvider _clock = new(Now, TestZones.PlusTen);
    private StoreOperations? _app;
    private PlacesService? _appPlaces;
    private bool _appAnswers = true;
    private OpenDocumentScan? _openDocuments;

    public void Dispose() => _temp.Dispose();

    private string PlacesFile => Path.Combine(_temp.Path, "Roaming", "places.json");

    /// <summary>"Starts" the app: loads the stores into memory, as the app does, and from now on qp sends it its changes.</summary>
    private void StartApp()
    {
        _appPlaces = new PlacesService(new FilePlacesStorage(Path.Combine(_temp.Path, "Roaming"), "places.json"), _clock);
        var sessions = new SessionStore(new FilePlacesStorage(Path.Combine(_temp.Path, "Roaming"), "sessions.json"), _clock);
        _app = new StoreOperations(() => _appPlaces, () => sessions);
    }

    private CliEnvironment Environment() => new()
    {
        Time = _clock,
        Shell = _shell,
        IsAppRunning = _ => _app is not null,
        SendToApp = (scope, request) => _appAnswers ? _app!.Execute(request, out _) : throw new TimeoutException(),
        ScanOpenDocuments = _openDocuments is null ? null : () => _openDocuments
    };

    private (int Exit, JsonObject Json) Run(params string[] args)
    {
        var environment = Environment();
        var output = new StringWriter();
        var exit = CliApp.Run(args.Append("--data-root").Append(_temp.Path).ToArray(), output, environment);

        var text = output.ToString();
        Assert.Single(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        return (exit, JsonNode.Parse(text)!.AsObject());
    }

    private JsonNode Ok(params string[] args)
    {
        var (exit, json) = Run(args);
        Assert.True(json["ok"]!.GetValue<bool>(), json.ToJsonString());
        Assert.Equal(0, exit);
        Assert.Equal(CliApp.ApiVersion, json["apiVersion"]!.GetValue<int>());
        return json["data"]!;
    }

    private string Error(int expectedExit, params string[] args)
    {
        var (exit, json) = Run(args);
        Assert.False(json["ok"]!.GetValue<bool>());
        Assert.Equal(expectedExit, exit);
        return json["error"]!["code"]!.GetValue<string>();
    }

    private void AddWiki() => Ok("places", "add", "--alias", "Wiki", "--url", "https://wiki.example.com", "--tags", "team, docs", "--note", "Team wiki");

    [Fact]
    public void NoArguments_DescribesEveryCommand()
    {
        var data = Ok();

        var names = data["commands"]!.AsArray().Select(c => c!["name"]!.GetValue<string>()).ToList();
        Assert.Contains("places list", names);
        Assert.Contains("folders recent", names);
        Assert.Equal(CliApp.Commands.Count, names.Count);
    }

    [Fact]
    public void Help_AfterACommand_Describes()
    {
        Assert.NotNull(Ok("places", "list", "--help")["commands"]);
    }

    [Fact]
    public void Status_OnAnEmptyRoot_ReportsEveryStoreNotPresent_AndWritable()
    {
        var data = Ok("status");

        Assert.Equal("files", data["changesGoTo"]!.GetValue<string>());
        Assert.All(data["stores"]!.AsArray(), s => Assert.Equal("notPresent", s!["outcome"]!.GetValue<string>()));
    }

    [Fact]
    public void GlobalOptions_WorkBeforeTheCommandToo()
    {
        var environment = Environment();
        var output = new StringWriter();

        var exit = CliApp.Run(new[] { "--data-root", _temp.Path, "--pretty", "status" }, output, environment);

        Assert.Equal(0, exit);
        Assert.Equal("status", JsonNode.Parse(output.ToString())!["command"]!.GetValue<string>());
        Assert.Contains("\n  ", output.ToString());
    }

    [Fact]
    public void Add_SavesThePlaceWithTagsAndNote()
    {
        AddWiki();

        var place = Ok("places", "get", "wiki")["place"]!;
        Assert.Equal("Wiki", place["alias"]!.GetValue<string>());
        Assert.Equal(new[] { "team", "docs" }, place["tags"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.Equal("Team wiki", place["note"]!.GetValue<string>());
        Assert.True(File.Exists(PlacesFile));
    }

    [Fact]
    public void Add_ADuplicateAlias_IsInvalid()
    {
        AddWiki();

        Assert.Equal(ErrorCodes.Invalid, Error(ExitCodes.Invalid, "places", "add", "--alias", "WIKI", "--url", "https://other.example.com"));
    }

    [Fact]
    public void WhileTheAppIsRunning_ChangesGoThroughIt_AndItsNextSaveKeepsThem()
    {
        AddWiki();
        StartApp();

        Ok("places", "tag", "Wiki", "--add", "x");
        Ok("places", "add", "--alias", "B", "--url", "https://b.example.com");

        // The app holds both changes, so its own next save (here, a favourite
        // toggled in its window) writes them back rather than over them.
        Assert.Contains("x", _appPlaces!.Places.Single(p => p.Alias == "Wiki").Tags);
        _appPlaces.ToggleFavourite(_appPlaces.Places.Single(p => p.Alias == "B"));
        Assert.Equal(2, Ok("places", "list")["total"]!.GetValue<int>());
        Assert.Contains("x", Ok("places", "get", "Wiki")["place"]!["tags"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.Equal("app", Ok("status")["changesGoTo"]!.GetValue<string>());
    }

    [Fact]
    public void AnAppThatDoesNotAnswer_IsAppRunning_AndNothingIsWritten()
    {
        AddWiki();
        StartApp();
        _appAnswers = false;
        var before = File.ReadAllText(PlacesFile);

        Assert.Equal(ErrorCodes.AppRunning, Error(ExitCodes.AppRunning, "places", "tag", "Wiki", "--add", "x"));
        Assert.Equal(before, File.ReadAllText(PlacesFile));
    }

    [Fact]
    public void Reads_NeverWrite_EvenWhenTheStoreNeedsMigrating()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PlacesFile)!);
        var v3 = """{ "schemaVersion": 3, "places": [ { "id": "00000000-0000-0000-0000-000000000001", "alias": "Docs", "type": "url", "resource": "https://docs.example.com", "dateAdded": "2026-01-01T00:00:00+00:00", "lastOpenedAt": "2026-09-24T00:00:00+00:00", "openCount": 3 } ] }""";
        File.WriteAllText(PlacesFile, v3);

        Ok("places", "list");
        Ok("places", "top", "--period", "all");
        Ok("status");

        Assert.Equal(v3, File.ReadAllText(PlacesFile));
    }

    [Fact]
    public void Open_LaunchesAndRecords_ThenTopCountsIt()
    {
        AddWiki();

        var data = Ok("places", "open", "Wiki");

        Assert.True(data["recorded"]!.GetValue<bool>());
        Assert.Equal(new[] { "https://wiki.example.com" }, _shell.Opened);
        var top = Ok("places", "top", "--period", "day")["places"]!.AsArray();
        Assert.Equal(1, Assert.Single(top)!["opensInPeriod"]!.GetValue<int>());
    }

    [Fact]
    public void Open_WhileTheAppIsRunning_IsRecordedByTheApp()
    {
        AddWiki();
        StartApp();

        Assert.True(Ok("places", "open", "Wiki")["recorded"]!.GetValue<bool>());
        Assert.Equal(1, _appPlaces!.Places.Single().OpenCount);
    }

    [Fact]
    public void Open_WhenTheAppDoesNotAnswer_StillOpens_ButIsNotRecorded()
    {
        AddWiki();
        StartApp();
        _appAnswers = false;

        var data = Ok("places", "open", "Wiki");

        Assert.False(data["recorded"]!.GetValue<bool>());
        Assert.Equal(ErrorCodes.AppRunning, data["notRecordedReason"]!.GetValue<string>());
        Assert.Single(_shell.Opened);
    }

    [Fact]
    public void Open_AMissingFolder_IsOpenFailed()
    {
        Ok("places", "add", "--alias", "Gone", "--folder", TestPaths.Folder("Gone"));

        Assert.Equal(ErrorCodes.OpenFailed, Error(ExitCodes.OpenFailed, "places", "open", "Gone"));
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public void DryRun_OpensNothing()
    {
        AddWiki();

        Assert.False(Ok("places", "open", "Wiki", "--dry-run")["opened"]!.GetValue<bool>());
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public void Find_MatchesTagsAndNotes()
    {
        AddWiki();

        Assert.Equal(1, Ok("places", "find", "docs")["total"]!.GetValue<int>());
        Assert.Equal(1, Ok("places", "find", "team", "wiki")["total"]!.GetValue<int>());
        Assert.Equal(0, Ok("places", "find", "team", "nothing")["total"]!.GetValue<int>());
    }

    [Fact]
    public void Tag_AddsAndRemoves_IgnoringCase()
    {
        AddWiki();

        var tags = Ok("places", "tag", "Wiki", "--add", "Client,TEAM", "--remove", "DOCS")["place"]!["tags"]!.AsArray();

        Assert.Equal(new[] { "team", "Client" }, tags.Select(t => t!.GetValue<string>()));
    }

    [Fact]
    public void Note_ClearRemovesIt()
    {
        AddWiki();

        Assert.Null(Ok("places", "note", "Wiki", "--clear")["place"]!["note"]);
    }

    [Fact]
    public void Get_AnUnknownPlace_IsNotFound_WithSuggestions()
    {
        AddWiki();

        var (exit, json) = Run("places", "get", "wik");

        Assert.Equal(ExitCodes.NotFound, exit);
        Assert.Equal("Wiki", json["error"]!["details"]!["suggestions"]![0]!.GetValue<string>());
    }

    [Theory]
    [InlineData("places", "lst")]
    [InlineData("nonsense")]
    [InlineData("places", "list", "--bogus")]
    [InlineData("places", "list", "--limit", "0")]
    [InlineData("places", "top", "--period", "fortnight")]
    [InlineData("places", "note", "Wiki")]
    public void BadCalls_AreUsageErrors(params string[] args)
    {
        Assert.Equal(ErrorCodes.Usage, Error(ExitCodes.Usage, args));
    }

    [Fact]
    public void ANewerStore_IsUnavailable_NotEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PlacesFile)!);
        File.WriteAllText(PlacesFile, """{ "schemaVersion": 99, "places": [] }""");

        Assert.Equal(ErrorCodes.StoreUnavailable, Error(ExitCodes.StoreUnavailable, "places", "list"));
    }

    [Fact]
    public void Top_CountsOnlyOpensInThePeriod()
    {
        AddWiki();
        Ok("places", "open", "Wiki");
        _clock.Advance(TimeSpan.FromDays(10));
        Ok("places", "open", "Wiki");

        Assert.Equal(1, Ok("places", "top", "--period", "week")["places"]![0]!["opensInPeriod"]!.GetValue<int>());
        Assert.Equal(2, Ok("places", "top", "--days", "11")["places"]![0]!["opensInPeriod"]!.GetValue<int>());
    }

    private const string Report = @"C:\Clients\Acme\Audit\Report.pdf";
    private const string Figures = @"C:\Clients\Acme\Audit\Figures.xlsx";

    private void SaveAudit() => Ok("sessions", "save", "--name", "Acme audit", "--tags", "acme", "--file", Report, "--file", Figures);

    [Fact]
    public void SessionsSave_ThenGet()
    {
        SaveAudit();

        var session = Ok("sessions", "get", "acme AUDIT")["session"]!;

        Assert.Equal(2, session["files"]!.AsArray().Count);
        Assert.Equal("acme", session["tags"]![0]!.GetValue<string>());
    }

    [Fact]
    public void SessionsSave_ABadFile_IsInvalid()
    {
        Assert.Equal(ErrorCodes.Invalid, Error(ExitCodes.Invalid, "sessions", "save", "--name", "X", "--file", "notes.txt"));
    }

    [Fact]
    public void SessionsSave_WhileTheAppIsRunning_IsSavedByTheApp()
    {
        StartApp();

        SaveAudit();

        Assert.Single(Ok("sessions", "list")["sessions"]!.AsArray());
    }

    [Fact]
    public void SessionsOpen_OpensTheFilesThatExist_ListsTheRest_AndRecordsIt()
    {
        SaveAudit();
        _shell.ExistingFiles.Add(Report);

        var data = Ok("sessions", "open", "Acme audit");

        Assert.Equal(new[] { Report }, _shell.Opened);
        Assert.Equal(Figures, data["missing"]![0]!.GetValue<string>());
        Assert.True(data["recorded"]!.GetValue<bool>());
        Assert.Equal(1, Ok("sessions", "get", "Acme audit")["session"]!["openCount"]!.GetValue<int>());
    }

    [Fact]
    public void SessionsOpen_WithNothingThere_IsOpenFailed_AndNotRecorded()
    {
        SaveAudit();

        Assert.Equal(ErrorCodes.OpenFailed, Error(ExitCodes.OpenFailed, "sessions", "open", "Acme audit"));
        Assert.Equal(0, Ok("sessions", "get", "Acme audit")["session"]!["openCount"]!.GetValue<int>());
    }

    [Fact]
    public void SessionsOpen_OneFile_AndDryRun()
    {
        SaveAudit();
        _shell.ExistingFiles.Add(Report);
        _shell.ExistingFiles.Add(Figures);

        Assert.Equal(2, Ok("sessions", "open", "Acme audit", "--dry-run")["wouldOpen"]!.AsArray().Count);
        Assert.Empty(_shell.Opened);
        Ok("sessions", "open", "Acme audit", "--file", Figures.ToUpperInvariant());
        Assert.Equal(new[] { Figures }, _shell.Opened);
        Assert.Equal(ErrorCodes.Usage, Error(ExitCodes.Usage, "sessions", "open", "Acme audit", "--file", @"C:\Other.pdf"));
    }

    [Fact]
    public void FilesOpen_OffWindows_IsUnsupported()
    {
        Assert.Equal(ErrorCodes.Unsupported, Error(ExitCodes.Unsupported, "files", "open"));
    }

    [Fact]
    public void FilesOpen_GroupsByFolder_NamesTheContainingPlace_AndSuggestsAName()
    {
        var acme = TestPaths.Folder("Acme");
        Ok("places", "add", "--alias", "Acme Client", "--folder", acme);
        var audit = Path.Combine(acme, "Audit");
        _openDocuments = new OpenDocumentScan(new[]
        {
            new DocumentCandidate(Path.Combine(audit, "Report.pdf"), true, "open in Acrobat", null),
            new DocumentCandidate(Path.Combine(audit, "Figures.xlsx"), true, "open in Excel", null),
            new DocumentCandidate(Path.Combine(TestPaths.Folder("Elsewhere"), "Old.docx"), false, "opened recently", Now)
        }, Array.Empty<string>());

        var data = Ok("files", "open");

        Assert.Equal(2, data["files"]!.AsArray().Count);
        Assert.Equal("Acme Client", data["files"]![0]!["place"]!["alias"]!.GetValue<string>());
        var folder = Assert.Single(data["folders"]!.AsArray())!;
        Assert.Equal(2, folder["files"]!.GetValue<int>());
        Assert.Equal("Acme Client 2026-09-25", data["suggestedName"]!.GetValue<string>());
        Assert.Equal(3, Ok("files", "open", "--suggestions")["files"]!.AsArray().Count);
    }

    [Fact]
    public void ActivityDays_ListsEachDaysOpens_NewestFirst()
    {
        AddWiki();
        SaveAudit();
        _shell.ExistingFiles.Add(Report);
        Ok("places", "open", "Wiki");
        _clock.Advance(TimeSpan.FromDays(1));
        Ok("places", "open", "Wiki");
        Ok("places", "open", "Wiki");
        Ok("sessions", "open", "Acme audit");

        var days = Ok("activity", "days", "--period", "week")["days"]!.AsArray();

        Assert.Equal(2, days.Count);
        Assert.Equal("2026-09-26", days[0]!["date"]!.GetValue<string>());
        Assert.Equal(2, days[0]!["places"]![0]!["opens"]!.GetValue<int>());
        Assert.Equal("Acme audit", days[0]!["sessions"]![0]!["name"]!.GetValue<string>());
        Assert.Equal(1, days[1]!["places"]![0]!["opens"]!.GetValue<int>());
        Assert.Empty(days[1]!["sessions"]!.AsArray());
    }

    [Fact]
    public void RecentFilesAndFolders_ReachBackThroughTheActivityHistory()
    {
        var local = Path.Combine(_temp.Path, "Local");
        var activity = new ActivityStore(new FilePlacesStorage(local, "activity.json"), _clock);
        Assert.True(activity.TryAddRoot(@"C:\Jobs", null, out _, out _).Success);
        var files = new RecentFilesStore(new FilePlacesStorage(local, "recent-files.json"), _clock);
        Assert.True(files.SetEnabled(true).Saved);

        // A year and a half ago, saved by this PC before the stores pruned it.
        var old = new DateOnly(2025, 3, 10);
        var history = new ActivityHistory(new HistoryFolder(Path.Combine(_temp.Path, "History")), _clock, System.Environment.MachineName);
        var root = new Models.Activity.TrackedRoot { Path = @"C:\Jobs" };
        root.Days[old] = new Models.Activity.DayActivity();
        root.Days[old].Folders[@"C:\Jobs\Acme"] = new Models.Activity.FolderTotal { Milliseconds = 3_600_000, Visits = 2 };
        Assert.True(history.SaveFolders(new[] { root }));
        Assert.True(history.SaveFiles(new[] { new Models.RecentFiles.RecentFileRecord { Path = @"C:\Jobs\Acme\Old.pdf", Opens = { new DateTimeOffset(2025, 3, 10, 1, 0, 0, TimeSpan.Zero) } } }));

        var recentFiles = Ok("files", "recent", "--days", "900")["files"]!.AsArray();
        Assert.Equal(@"C:\Jobs\Acme\Old.pdf", Assert.Single(recentFiles)!["path"]!.GetValue<string>());
        Assert.Empty(Ok("files", "recent", "--period", "month")["files"]!.AsArray());

        var folder = Assert.Single(Ok("folders", "recent", "--days", "900")["folders"]!.AsArray())!;
        Assert.Equal(@"C:\Jobs\Acme", folder["folder"]!.GetValue<string>());
        Assert.Equal(3600, folder["seconds"]!.GetValue<long>());

        var day = Assert.Single(Ok("activity", "days", "--days", "900")["days"]!.AsArray())!;
        Assert.Equal("2025-03-10", day["date"]!.GetValue<string>());
        Assert.Single(day["files"]!.AsArray());
        Assert.Single(day["folders"]!.AsArray());
    }
}
