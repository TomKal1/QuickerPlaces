using System;
using System.Linq;
using QuickerPlaces.Models.Activity;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Tests.Fakes;
using Xunit;
using static QuickerPlaces.Tests.Fakes.ActivityFixtures;

namespace QuickerPlaces.Tests;

/// <summary>
/// Configuring roots (Phase 9 plan 5.3, D18): adding, editing, disabling
/// and deleting each write at once, a failed write is reported and never
/// swallowed, and deleting a root takes every trace of it in one write.
/// </summary>
public sealed class ActivityStoreRootTests
{
    private readonly FakePlacesStorage _storage = new();
    private readonly ManualTimeProvider _time = new();
    private readonly ActivityStore _store;

    public ActivityStoreRootTests() => _store = new ActivityStore(_storage, _time);

    [Fact]
    public void AddingARoot_TracksItWithTheDefaults_AndSavesAtOnce()
    {
        var root = AddRoot(_store);

        Assert.Matches("^[0-9a-f]{32}$", root.RootId);
        Assert.Equal(@"C:\Jobs", root.Path);
        Assert.True(root.Enabled);
        Assert.Equal(_time.UtcNow, root.TrackingStartedAt);
        Assert.Empty(root.Config.EquivalentPrefixes);
        Assert.Equal(RollupMode.RootChild, root.Config.Rollup);
        Assert.Equal(1, root.Config.Depth);
        Assert.Equal(TimeSpan.FromSeconds(5), root.Config.DwellThreshold);
        Assert.Equal(TimeSpan.FromMinutes(5), root.Config.IdleTimeout);

        Assert.Equal(1, _storage.WriteCount);
        Assert.Equal(root.RootId, Assert.Single(_store.Roots).RootId);
        Assert.Equal(root.RootId, Assert.Single(_store.EnabledRoots()).RootId);
    }

    [Fact]
    public void AddingARoot_KeepsItsNormalizedSpelling()
    {
        Assert.Equal(@"C:\Jobs", AddRoot(_store, "C:/Jobs/").Path);
    }

    [Theory]
    [InlineData(@"Jobs")]
    [InlineData("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    [InlineData("https://example.com")]
    [InlineData("")]
    [InlineData("   ")]
    public void AddingSomethingThatIsNotAFolder_IsRefused(string path)
    {
        var result = _store.TryAddRoot(path, null, out var root, out var persistence);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Null(root);
        Assert.True(persistence.Saved);
        Assert.Empty(_store.Roots);
        Assert.Equal(0, _storage.WriteCount);
    }

    [Fact]
    public void AddingAFolderAlreadyTracked_IsRefused_HoweverItIsWritten()
    {
        AddRoot(_store);

        var result = _store.TryAddRoot(@"c:/jobs/", null, out _, out _);

        Assert.False(result.Success);
        Assert.Contains("already", result.ErrorMessage);
        Assert.Single(_store.Roots);
        Assert.Equal(1, _storage.WriteCount);
    }

    [Fact]
    public void AddingARoot_KeepsItsEquivalentPrefixesNormalized()
    {
        var result = _store.TryAddRoot(@"J:\Jobs", new[] { @"\\fileserver\projects\Jobs\" }, out var root, out _);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(new[] { @"\\fileserver\projects\Jobs" }, root!.Config.EquivalentPrefixes);
    }

    [Fact]
    public void AddingARootWithAnUnusableEquivalent_IsRefused()
    {
        var result = _store.TryAddRoot(@"J:\Jobs", new[] { "relative" }, out _, out _);

        Assert.False(result.Success);
        Assert.Empty(_store.Roots);
    }

    [Fact]
    public void AFailedSaveOfANewRoot_IsReported_AndTheRootIsKeptForTheNextSave()
    {
        _storage.FailNextWrite = true;

        var result = _store.TryAddRoot(@"C:\Jobs", null, out var root, out var persistence);

        Assert.True(result.Success);
        Assert.False(persistence.Saved);
        Assert.Contains("try again", persistence.UserMessage);
        Assert.True(_store.HasUnsavedChanges);
        Assert.Equal(root!.RootId, Assert.Single(_store.Roots).RootId);

        Assert.True(_store.Flush().Saved);
        Assert.False(_store.HasUnsavedChanges);
        Assert.Single(new ActivityStore(_storage, _time).Roots);
    }

    [Fact]
    public void StoppingTracking_SavesAtOnce_AndKeepsTheData()
    {
        var jobs = AddRoot(_store);
        _store.Record(new[] { Interval(jobs.RootId, Acme, Today, 30, true) });

        var persistence = _store.SetEnabled(jobs.RootId, false);

        Assert.True(persistence.Saved);
        Assert.Equal(2, _storage.WriteCount);
        Assert.False(Assert.Single(_store.Roots).Enabled);
        Assert.Empty(_store.EnabledRoots());
        Assert.Single(_store.QueryPeriod(jobs.RootId, Today, Today)!.Folders);

        Assert.True(_store.SetEnabled(jobs.RootId, true).Saved);
        Assert.Single(_store.EnabledRoots());
        Assert.True(new ActivityStore(_storage, _time).Roots.Single().Enabled);
    }

    [Fact]
    public void DeletingARoot_RemovesItsConfigurationAndEveryRecordedDay_InOneWrite()
    {
        var jobs = AddRoot(_store);
        var docs = AddRoot(_store, @"D:\Docs");
        _store.Record(new[]
        {
            Interval(jobs.RootId, Acme, Today, 30, true),
            Interval(jobs.RootId, Beta, Today.AddDays(-3), 30, true),
        });
        Assert.True(_store.Flush().Saved);
        var writes = _storage.WriteCount;

        var persistence = _store.DeleteRoot(jobs.RootId);

        Assert.True(persistence.Saved);
        Assert.Equal(writes + 1, _storage.WriteCount);
        Assert.Equal(docs.RootId, Assert.Single(_store.Roots).RootId);
        Assert.Null(_store.QueryPeriod(jobs.RootId, Today.AddDays(-7), Today));
        Assert.Null(_store.QueryDayTotals(jobs.RootId));
        Assert.DoesNotContain(jobs.RootId, _storage.LastWritten);
        Assert.DoesNotContain("Jobs", _storage.LastWritten);
    }

    [Fact]
    public void AFailedSaveOfADeletion_IsReported_AndTheRootNeverComesBack()
    {
        var jobs = AddRoot(_store);
        _storage.FailNextWrite = true;

        var persistence = _store.DeleteRoot(jobs.RootId);

        Assert.False(persistence.Saved);
        Assert.NotNull(persistence.UserMessage);
        Assert.Empty(_store.Roots);

        Assert.True(_store.Flush().Saved);
        Assert.Empty(new ActivityStore(_storage, _time).Roots);
    }

    [Fact]
    public void AnUnknownRoot_ChangesNothing_AndWritesNothing()
    {
        AddRoot(_store);

        Assert.True(_store.SetEnabled("nope", false).Saved);
        Assert.True(_store.DeleteRoot("nope").Saved);

        Assert.Equal(1, _storage.WriteCount);
        Assert.Null(_store.QueryPeriod("nope", Today, Today));
    }

    [Fact]
    public void EditingARoot_ChangesItsSettings_AndSavesAtOnce()
    {
        var jobs = AddRoot(_store);
        var edited = jobs.Config with
        {
            Rollup = RollupMode.Depth,
            Depth = 2,
            DwellThreshold = TimeSpan.FromSeconds(10),
            IdleTimeout = TimeSpan.FromMinutes(15),
            EquivalentPrefixes = new[] { @"\\fileserver\c$\Jobs" },
        };

        var result = _store.TryUpdateRoot(edited, out var persistence);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(persistence.Saved);
        Assert.Equal(2, _storage.WriteCount);
        foreach (var root in new[] { _store.Roots.Single(), new ActivityStore(_storage, _time).Roots.Single() })
        {
            Assert.Equal(RollupMode.Depth, root.Config.Rollup);
            Assert.Equal(2, root.Config.Depth);
            Assert.Equal(TimeSpan.FromSeconds(10), root.Config.DwellThreshold);
            Assert.Equal(TimeSpan.FromMinutes(15), root.Config.IdleTimeout);
            Assert.Equal(new[] { @"\\fileserver\c$\Jobs" }, root.Config.EquivalentPrefixes);
            Assert.Equal(jobs.TrackingStartedAt, root.TrackingStartedAt);
        }
    }

    public static TheoryData<string> BadEdits => new() { "depth", "dwell", "idle", "path", "equivalent", "unknown" };

    [Theory]
    [MemberData(nameof(BadEdits))]
    public void ABadEdit_IsRefused_AndWritesNothing(string what)
    {
        var jobs = AddRoot(_store);
        var edited = what switch
        {
            "depth" => jobs.Config with { Rollup = RollupMode.Depth, Depth = 0 },
            "dwell" => jobs.Config with { DwellThreshold = TimeSpan.FromSeconds(-1) },
            "idle" => jobs.Config with { IdleTimeout = TimeSpan.Zero },
            "path" => jobs.Config with { Path = @"D:\Elsewhere" },
            "equivalent" => jobs.Config with { EquivalentPrefixes = new[] { "::{GUID}" } },
            _ => jobs.Config with { RootId = "nope" },
        };

        var result = _store.TryUpdateRoot(edited, out _);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(1, _storage.WriteCount);
        Assert.Equal(RollupMode.RootChild, _store.Roots.Single().Config.Rollup);
    }
}
