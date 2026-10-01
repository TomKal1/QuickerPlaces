using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using QuickerPlaces.Cli;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The qp command line end to end against a temporary data root: one JSON
/// document per run, the envelope and exit codes, the read-only guarantee,
/// and the refusal to write while the app is running.
/// </summary>
public sealed class CliAppTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 30, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();
    private readonly FakeShell _shell = new();
    private readonly ManualTimeProvider _clock = new(Now, TestZones.PlusTen);
    private bool _appRunning;

    public void Dispose() => _temp.Dispose();

    private string PlacesFile => Path.Combine(_temp.Path, "Roaming", "places.json");

    private (int Exit, JsonObject Json) Run(params string[] args)
    {
        var environment = new CliEnvironment { Time = _clock, Shell = _shell, IsAppRunning = _ => _appRunning };
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

        Assert.True(data["writable"]!.GetValue<bool>());
        Assert.All(data["stores"]!.AsArray(), s => Assert.Equal("notPresent", s!["outcome"]!.GetValue<string>()));
    }

    [Fact]
    public void GlobalOptions_WorkBeforeTheCommandToo()
    {
        var environment = new CliEnvironment { Time = _clock, Shell = _shell, IsAppRunning = _ => false };
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
    public void Writes_AreRefusedWhileTheAppIsRunning_AndTheFileIsUntouched()
    {
        AddWiki();
        var before = File.ReadAllText(PlacesFile);
        _appRunning = true;

        Assert.Equal(ErrorCodes.AppRunning, Error(ExitCodes.AppRunning, "places", "tag", "Wiki", "--add", "x"));
        Assert.Equal(ErrorCodes.AppRunning, Error(ExitCodes.AppRunning, "places", "add", "--alias", "B", "--url", "https://b.example.com"));
        Assert.Equal(before, File.ReadAllText(PlacesFile));
        Assert.False(Ok("status")["writable"]!.GetValue<bool>());
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
    public void Open_WhileTheAppIsRunning_LaunchesWithoutRecording()
    {
        AddWiki();
        var before = File.ReadAllText(PlacesFile);
        _appRunning = true;

        var data = Ok("places", "open", "Wiki");

        Assert.False(data["recorded"]!.GetValue<bool>());
        Assert.Equal(ErrorCodes.AppRunning, data["notRecordedReason"]!.GetValue<string>());
        Assert.Single(_shell.Opened);
        Assert.Equal(before, File.ReadAllText(PlacesFile));
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
}
