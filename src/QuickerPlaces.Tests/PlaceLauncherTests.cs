using System;
using System.ComponentModel;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Tests 22 to 27 from the Phase 3 plan's section 7: PlaceLauncher, the one
/// gateway for every launch (D23), and its definition of a recorded open —
/// the pre-launch check passed and the shell did not throw (D24).
/// </summary>
public sealed class PlaceLauncherTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 30, 0, TimeSpan.Zero);

    private readonly FakePlacesStorage _storage = new();
    private readonly FakeShell _shell = new();
    private readonly PlacesService _service;
    private readonly PlaceLauncher _launcher;

    public PlaceLauncherTests()
    {
        _service = new PlacesService(_storage, new ManualTimeProvider(Now));
        _launcher = new PlaceLauncher(_service, _shell);
    }

    private Place AddFolder(string alias)
    {
        Assert.True(_service.TryAdd(alias, PlaceType.Folder, TestPaths.Folder(alias), out var place, out _).Success);
        return place!;
    }

    private Place AddUrl(string alias)
    {
        Assert.True(_service.TryAdd(alias, PlaceType.Url, $"https://{alias.ToLowerInvariant()}.example.com", out var place, out _).Success);
        return place!;
    }

    /// <summary>Test 22: an existing folder is launched and the open is recorded.</summary>
    [Fact]
    public void ExistingFolder_IsLaunched_AndRecorded()
    {
        var docs = AddFolder("Docs");
        _shell.ExistingDirectories.Add(docs.Resource);

        var outcome = _launcher.Open(docs);

        Assert.Equal(OpenStatus.Launched, outcome.Status);
        Assert.Null(outcome.ErrorMessage);
        Assert.True(outcome.Persistence.Saved);
        Assert.Equal(new[] { docs.Resource }, _shell.Opened);
        Assert.Equal(1, docs.OpenCount);
        Assert.Equal(Now, docs.LastOpenedAt);
    }

    /// <summary>Test 23: a missing folder fails the pre-check; the shell is never asked, and nothing is recorded or written (§4.12).</summary>
    [Fact]
    public void MissingFolder_IsNotLaunched_OrRecorded()
    {
        var docs = AddFolder("Docs");
        var writesBefore = _storage.WriteCount;

        var outcome = _launcher.Open(docs);

        Assert.Equal(OpenStatus.Missing, outcome.Status);
        Assert.Empty(_shell.Opened);
        Assert.Equal(0, docs.OpenCount);
        Assert.Null(docs.LastOpenedAt);
        Assert.Equal(writesBefore, _storage.WriteCount);
    }

    /// <summary>Test 24: a launch Windows refuses is Failed with the shell's message, and nothing is recorded or written.</summary>
    [Fact]
    public void RefusedLaunch_IsFailed_WithTheMessage_AndNotRecorded()
    {
        var wiki = AddUrl("Wiki");
        _shell.ThrowOnOpen = new Win32Exception("The system cannot find the file specified.");
        var writesBefore = _storage.WriteCount;

        var outcome = _launcher.Open(wiki);

        Assert.Equal(OpenStatus.Failed, outcome.Status);
        Assert.Equal("The system cannot find the file specified.", outcome.ErrorMessage);
        Assert.Equal(0, wiki.OpenCount);
        Assert.Equal(writesBefore, _storage.WriteCount);
    }

    /// <summary>Test 25: a URL has no pre-launch check (§4.12); it is launched and recorded.</summary>
    [Fact]
    public void Url_HasNoExistenceCheck_AndIsRecorded()
    {
        var wiki = AddUrl("Wiki");

        var outcome = _launcher.Open(wiki);

        Assert.Equal(OpenStatus.Launched, outcome.Status);
        Assert.Equal(0, _shell.ExistenceChecks);
        Assert.Equal(new[] { wiki.Resource }, _shell.Opened);
        Assert.Equal(1, wiki.OpenCount);
    }

    /// <summary>Test 26: the launch succeeded and the save failed — still Launched, with the failure for the banner, and the usage kept (D26, D1).</summary>
    [Fact]
    public void LaunchedButUnsaved_IsStillLaunched_AndKeepsTheUsage()
    {
        var wiki = AddUrl("Wiki");
        _storage.FailNextWrite = true;

        var outcome = _launcher.Open(wiki);

        Assert.Equal(OpenStatus.Launched, outcome.Status);
        Assert.False(outcome.Persistence.Saved);
        Assert.True(_service.HasUnsavedChanges);
        Assert.Equal(1, wiki.OpenCount);
    }

    /// <summary>Test 27: while recovery is unresolved the place still opens, and nothing is recorded (D26).</summary>
    [Fact]
    public void WhileRecoveryIsUnresolved_ThePlaceStillOpens_ButIsNotRecorded()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = "{ not valid json" };
        var service = new PlacesService(storage, new ManualTimeProvider(Now));
        var launcher = new PlaceLauncher(service, _shell);
        var stray = new Place { Alias = "Wiki", Type = PlaceType.Url, Resource = "https://wiki.example.com" };

        var outcome = launcher.Open(stray);

        Assert.Equal(OpenStatus.Launched, outcome.Status);
        Assert.Equal(new[] { stray.Resource }, _shell.Opened);
        Assert.Equal(0, stray.OpenCount);
        Assert.Equal(0, storage.WriteCount);
    }
}
