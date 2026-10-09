using System;
using System.Linq;
using QuickerPlaces.Models.Activity;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.History;
using QuickerPlaces.Services.Library;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;
using static QuickerPlaces.Tests.Fakes.ActivityFixtures;

namespace QuickerPlaces.Tests;

public sealed class AllFoldersTrackingTests
{
    private readonly ManualTimeProvider _time = new();
    private readonly FakePlacesStorage _storage = new();
    private ActivityStore NewStore() => new(_storage, _time);

    [Fact]
    public void DefaultAndDraft_KeepTargetModeUntilSave_AndReloadKeepsAllFolders()
    {
        var store = NewStore();
        var notified = 0;
        var editor = new FolderTrackingSettingsViewModel(store, () => notified++);
        Assert.True(editor.TargetFoldersOnly);
        Assert.False(editor.CanSave);
        editor.AllFolders = true;
        editor.TargetFoldersOnly = false;
        editor.AllFolders = false;
        Assert.False(editor.AllFolders);
        Assert.False(editor.HasChanges);
        editor.AllFolders = true;
        Assert.False(store.TrackAllFolders);
        Assert.Empty(store.EnabledRoots());
        Assert.Equal(0, _storage.WriteCount);
        Assert.Contains("Unsaved", editor.SaveStatus);

        Assert.True(editor.Save());
        Assert.Equal(1, notified);
        Assert.Equal(1, _storage.WriteCount);
        Assert.False(editor.HasChanges);
        Assert.StartsWith("Saved.", editor.SaveStatus);
        Assert.True(NewStore().TrackAllFolders);
        Assert.True(new FolderTrackingSettingsViewModel(NewStore()).AllFolders);
        Assert.Equal(RollupMode.Exact, Assert.Single(store.EnabledRoots()).Rollup);
    }

    [Fact]
    public void TargetDraft_AddPauseRemoveAndDiscard_DoNotChangeTracking()
    {
        var store = NewStore();
        var first = AddRoot(store);
        var second = AddRoot(store, @"D:\Projects");
        var writes = _storage.WriteCount;
        var editor = new FolderTrackingSettingsViewModel(store);
        editor.Targets[0].Enabled = false;
        editor.RemoveTarget(editor.Targets[1]);
        Assert.True(editor.AddTarget(@"E:\Personal"));
        Assert.True(editor.HasChanges);
        Assert.Equal(writes, _storage.WriteCount);
        Assert.Equal(new[] { first.RootId, second.RootId }, store.EnabledRoots().Select(r => r.RootId));
        var reopened = new FolderTrackingSettingsViewModel(store);
        Assert.False(reopened.HasChanges);
        Assert.Equal(new[] { first.Path, second.Path }, reopened.Targets.Select(t => t.Path));
        Assert.All(reopened.Targets, t => Assert.True(t.Enabled));
    }

    [Fact]
    public void TargetSave_WritesOnce_AndKeepsRemovedHistoryInTheLibrary()
    {
        var store = NewStore();
        var target = AddRoot(store);
        var paused = AddRoot(store, @"D:\Projects");
        store.Record(new[] { Interval(target.RootId, Acme, Today, 30, true) });
        var places = new PlacesService(new FakePlacesStorage(), _time);
        var shell = new FakeShell();
        var library = new LibraryViewModel(places, new SessionStore(new FakePlacesStorage(), _time), store,
            new RecentFilesStore(new FakePlacesStorage(), _time), new PlaceLauncher(places, shell), shell, _time);
        library.RootScope = target.RootId;
        var editor = new FolderTrackingSettingsViewModel(store);
        editor.RemoveTarget(editor.Targets[0]);
        editor.Targets[0].Enabled = false;
        Assert.True(editor.AddTarget(@"E:\Personal"));
        var writes = _storage.WriteCount;
        Assert.True(editor.Save());
        Assert.Equal(writes + 1, _storage.WriteCount);
        Assert.Equal(@"E:\Personal", Assert.Single(store.EnabledRoots()).Path);
        var reloaded = NewStore();
        Assert.DoesNotContain(reloaded.Roots, r => r.RootId == target.RootId);
        Assert.False(reloaded.Roots.Single(r => r.RootId == paused.RootId).Enabled);
        Assert.True(reloaded.AllRoots.Single(r => r.RootId == target.RootId).IsRemoved);
        Assert.Single(reloaded.QueryPeriod(target.RootId, Today, Today)!.Folders);
        library.Reload();
        Assert.Null(library.RootScope);
        Assert.DoesNotContain(library.TrackedRootChips, r => r.RootId == target.RootId);
        Assert.Equal(Acme, Assert.Single(library.Rows).Item.Location);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReaddingRemovedTarget_RestoresItsSettingsIdentityAndHistory(bool useLegacyPicker)
    {
        var store = NewStore();
        var target = AddRoot(store);
        store.TryUpdateRoot(target.Config with { DwellThreshold = TimeSpan.FromSeconds(12),
            EquivalentPrefixes = new[] { @"Z:\Jobs" } }, out _);
        store.Record(new[] { Interval(target.RootId, Acme, Today, 30, true) });
        var editor = new FolderTrackingSettingsViewModel(store);
        editor.RemoveTarget(Assert.Single(editor.Targets));
        Assert.True(editor.Save());
        Assert.Empty(store.EnabledRoots());
        store = NewStore();
        if (useLegacyPicker) AddRoot(store, @"c:\jobs\");
        else
        {
            editor = new FolderTrackingSettingsViewModel(store);
            Assert.True(editor.AddTarget(@"c:\jobs\"));
            Assert.True(editor.Save());
        }
        var restored = Assert.Single(NewStore().Roots);
        Assert.Equal(target.RootId, restored.RootId);
        Assert.Equal(target.TrackingStartedAt, restored.TrackingStartedAt);
        Assert.Equal(TimeSpan.FromSeconds(12), restored.Config.DwellThreshold);
        Assert.Equal(@"Z:\Jobs", Assert.Single(restored.Config.EquivalentPrefixes));
        Assert.Single(NewStore().QueryPeriod(target.RootId, Today, Today)!.Folders);
    }

    [Fact]
    public void TargetSaveFailure_RollsBackScopeRemovalAndAddition_PreservesBufferedHistory()
    {
        var store = NewStore();
        var target = AddRoot(store);
        store.Record(new[] { Interval(target.RootId, Acme, Today, 30, true) });
        var notified = 0;
        var editor = new FolderTrackingSettingsViewModel(store, () => notified++);
        editor.RemoveTarget(Assert.Single(editor.Targets));
        editor.AddTarget(@"D:\Projects");
        editor.AllFolders = true;
        _storage.FailNextWrite = true;
        Assert.False(editor.Save());
        Assert.Equal(0, notified);
        Assert.True(editor.CanSave);
        Assert.False(store.TrackAllFolders);
        Assert.Equal(target.RootId, Assert.Single(store.EnabledRoots()).RootId);
        Assert.Single(store.AllRoots);
        Assert.True(store.HasUnsavedChanges);
        Assert.Single(store.QueryPeriod(target.RootId, Today, Today)!.Folders);
        Assert.True(editor.Save());
        Assert.Equal(1, notified);
        Assert.True(NewStore().TrackAllFolders);
        Assert.Equal(@"D:\Projects", NewStore().Roots.Single(r => !r.Config.IsAllFolders).Path);
        Assert.Single(NewStore().QueryPeriod(target.RootId, Today, Today)!.Folders);
    }

    [Fact]
    public void AllFoldersToggle_DisablesTargetEditing_AndKeepsSelectionsWhenTurnedOff()
    {
        var store = NewStore();
        var first = AddRoot(store);
        var paused = AddRoot(store, @"D:\Projects");
        store.SetEnabled(paused.RootId, false);
        var editor = new FolderTrackingSettingsViewModel(store);
        editor.AllFolders = true;
        Assert.False(editor.CanEditTargets);
        Assert.False(editor.AddTarget(@"E:\Personal"));
        editor.RemoveTarget(editor.Targets[0]);
        Assert.Equal(2, editor.Targets.Count);
        Assert.True(editor.Save());
        editor = new FolderTrackingSettingsViewModel(NewStore());
        editor.AllFolders = false;
        Assert.True(editor.CanEditTargets);
        Assert.True(editor.Targets[0].Enabled);
        Assert.False(editor.Targets[1].Enabled);
        Assert.True(editor.Save());
        Assert.Equal(first.RootId, Assert.Single(NewStore().EnabledRoots()).RootId);
    }

    [Fact]
    public void DuplicateTarget_IsRejected_WithoutChangingTheDraftOrStore()
    {
        var store = NewStore();
        AddRoot(store);
        var editor = new FolderTrackingSettingsViewModel(store);
        Assert.False(editor.AddTarget("c:/jobs/"));
        Assert.False(editor.HasChanges);
        Assert.Contains("already", editor.ErrorMessage);
        Assert.Single(editor.Targets);
        Assert.Equal(1, _storage.WriteCount);
    }

    [Fact]
    public void AllFolders_SupersedesTargets_AndSwitchingBackKeepsTheirSettingsAndHistory()
    {
        var store = NewStore();
        var target = AddRoot(store);
        store.Record(new[] { Interval(target.RootId, Acme, Today, 30, true) });
        Assert.True(store.TrySetTrackAllFolders(true).Saved);
        var all = Assert.Single(store.EnabledRoots());
        Assert.True(all.IsAllFolders);
        // A stale targeted tick must not double count after switching modes.
        store.Record(new[] { Interval(target.RootId, Acme, Today, 40, true), Interval(all.RootId, Acme, Today, 40, true) });
        Assert.Equal(1, store.QueryPeriod(target.RootId, Today, Today)!.Folders.Single().Visits);
        Assert.Equal(1, store.QueryPeriod(all.RootId, Today, Today)!.Folders.Single().Visits);

        Assert.True(store.TrySetTrackAllFolders(false).Saved);
        Assert.Equal(target.Config, Assert.Single(store.EnabledRoots()));
        store.Record(new[] { Interval(all.RootId, @"D:\Personal", Today, 90, true) });
        Assert.Single(store.QueryPeriod(all.RootId, Today, Today)!.Folders);
        var reloaded = NewStore();
        Assert.False(reloaded.TrackAllFolders);
        Assert.Equal(target.Config, Assert.Single(reloaded.EnabledRoots()));
        Assert.Single(reloaded.QueryPeriod(all.RootId, Today, Today)!.Folders);
    }

    [Fact]
    public void DisabledTargets_AreKeptDisabled_WhenAllFoldersIsTurnedOff()
    {
        var store = NewStore();
        var target = AddRoot(store);
        store.SetEnabled(target.RootId, false);
        store.TrySetTrackAllFolders(true);
        store.TrySetTrackAllFolders(false);
        Assert.Empty(store.EnabledRoots());
        Assert.False(store.Roots.Single(r => r.RootId == target.RootId).Enabled);
    }

    [Theory]
    [InlineData(@"C:\Users\Thomas\Documents", @"C:\Users\Thomas\Documents")]
    [InlineData("D:/Personal/Taxes/", @"D:\Personal\Taxes")]
    [InlineData(@"\\server\share\Jobs\Acme", @"\\server\share\Jobs\Acme")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"::{shell-location}", null)]
    [InlineData("https://example.com", null)]
    [InlineData("relative\\folder", null)]
    public void AllFolders_CreditsExactFilesystemFoldersOnly(string path, string? expected)
    {
        var store = NewStore();
        store.TrySetTrackAllFolders(true);
        Assert.Equal(expected, RootPathMatcher.Credit(path, Assert.Single(store.EnabledRoots())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSave_LeavesTheCurrentModeAndBufferedDataIntact_AndCanBeRetried(bool startAll)
    {
        var store = NewStore();
        var target = AddRoot(store);
        if (startAll) store.TrySetTrackAllFolders(true);
        var root = Assert.Single(store.EnabledRoots());
        store.Record(new[] { Interval(root.RootId, Acme, Today, 30, true) });
        var editor = new FolderTrackingSettingsViewModel(store);
        if (startAll) editor.TargetFoldersOnly = true;
        else editor.AllFolders = true;
        _storage.FailNextWrite = true;

        Assert.False(editor.Save());
        Assert.Equal(startAll, store.TrackAllFolders);
        Assert.True(store.HasUnsavedChanges);
        Assert.True(editor.CanSave);
        Assert.Contains("not been applied", editor.ErrorMessage);
        Assert.True(store.Flush().Saved);
        Assert.Equal(startAll, NewStore().TrackAllFolders);
        Assert.True(editor.Save());
        Assert.Equal(!startAll, NewStore().TrackAllFolders);
        Assert.Single(NewStore().QueryPeriod(root.RootId, Today, Today)!.Folders);
    }

    [Fact]
    public void TrackingLoop_RecordsOutsideTargets_AndStillHonoursPauseIdleAndLock()
    {
        var store = NewStore();
        store.TrySetTrackAllFolders(true);
        var root = Assert.Single(store.EnabledRoots());
        var probe = new FakeShellWindowProbe();
        var presence = new FakeUserPresence();
        var clock = new FakeMonotonicClock();
        var tracker = new FolderActivityTracker(probe, presence, clock, _time);
        var loop = new ActivityTrackingLoop(tracker, store, presence, clock);
        probe.ShowForeground(@"D:\Personal\Taxes");
        loop.Start();
        loop.Wake();
        for (var i = 0; i < 8; i++)
        {
            var elapsed = loop.NextWait!.Value;
            clock.Advance(elapsed);
            _time.Advance(elapsed);
            loop.Wake();
        }
        var row = Assert.Single(store.QueryPeriod(root.RootId, Today, Today)!.Folders);
        Assert.Equal(@"D:\Personal\Taxes", row.Folder);
        Assert.Equal(1, row.Visits);
        Assert.Equal(TimeSpan.FromSeconds(7), row.Time);

        var samples = probe.Samples;
        loop.Wake(TrackingSignal.Paused);
        Assert.Null(loop.NextWait);
        Assert.Equal(samples, probe.Samples);
        presence.IdleFor = TimeSpan.FromMinutes(6);
        loop.Wake(TrackingSignal.TrackingResumed);
        Assert.True(loop.IsIdle);
        Assert.Equal(samples, probe.Samples);
        presence.SessionLocked = true;
        loop.Wake(TrackingSignal.Locked);
        Assert.Null(loop.NextWait);
        Assert.Equal(samples, probe.Samples);
    }

    [Fact]
    public void OutsideFolders_AppearInTheLibrary_AndAllFoldersScopeMatchesThem()
    {
        var store = NewStore();
        var target = AddRoot(store);
        store.TrySetTrackAllFolders(true);
        var all = Assert.Single(store.EnabledRoots());
        store.Record(new[] { Interval(all.RootId, @"D:\Personal\Taxes", Today, 30, true) });
        var files = new RecentFilesStore(new FakePlacesStorage(), _time);
        var places = new PlacesService(new FakePlacesStorage(), _time);
        var shell = new FakeShell();
        var library = new LibraryViewModel(places, new SessionStore(new FakePlacesStorage(), _time), store, files,
            new PlaceLauncher(places, shell), shell, _time);
        library.RootScope = all.RootId;
        Assert.Equal(@"D:\Personal\Taxes", Assert.Single(library.Rows).Item.Location);
        var snapshot = LibrarySnapshot.Capture(places, new SessionStore(new FakePlacesStorage(), _time), store, files, _time);
        Assert.False(snapshot.Roots.Single(r => r.RootId == target.RootId).Enabled);
        Assert.True(snapshot.Roots.Single(r => r.RootId == all.RootId).Enabled);
        Assert.False(files.IsTracking);
        Assert.Equal(2, TrackedFolderPaths.LevelBelow(ActivityStore.AllFoldersPath, @"D:\Personal\Taxes"));
        Assert.Equal(2, TrackedFolderPaths.LevelBelow(ActivityStore.AllFoldersPath, @"\\server\share\Personal\Taxes"));
        Assert.Null(TrackedFolderPaths.LevelBelow(ActivityStore.AllFoldersPath, "::{shell}"));
    }

    [Fact]
    public void AllFoldersHistory_SurvivesRetentionAndCanBeReadBack()
    {
        var folder = new FakeHistoryFolder();
        var history = new ActivityHistory(folder, _time, "TEST-PC");
        var store = new ActivityStore(_storage, _time, history);
        store.TrySetTrackAllFolders(true);
        var root = Assert.Single(store.EnabledRoots());
        store.Record(new[] { Interval(root.RootId, @"D:\Personal", Today, 30, true) });
        var date = Today;
        _time.Advance(TimeSpan.FromDays(70));
        Assert.True(store.Flush().Saved);
        Assert.Empty(store.QueryPeriod(root.RootId, date, date)!.Folders);
        var month = history.ReadMonth(date.Year, date.Month)!;
        Assert.Equal(ActivityStore.AllFoldersPath, Assert.Single(month.Roots).Path);
        Assert.Equal(@"D:\Personal", Assert.Single(month.Roots.Single().Days[date].Folders).Key);
    }

    [Fact]
    public void OlderStores_KeepTargetsAndHistory_WhenUpgraded()
    {
        _storage.ContentsToReturn = @"{""schemaVersion"":1,""roots"": [{""rootId"":""target"",""path"":""C:\\Jobs"",""enabled"":true}]}";
        var store = NewStore();
        Assert.True(store.IsAvailable);
        Assert.False(store.TrackAllFolders);
        Assert.Equal("target", Assert.Single(store.EnabledRoots()).RootId);
        Assert.True(store.TrySetTrackAllFolders(true).Saved);
        Assert.Contains("\"schemaVersion\": 2", _storage.LastWritten);
        Assert.Equal("target", NewStore().Roots.Single(r => !r.Config.IsAllFolders).RootId);
    }
}
