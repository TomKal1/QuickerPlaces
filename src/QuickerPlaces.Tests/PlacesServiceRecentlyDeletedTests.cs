using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Tests 47 to 54 from the Phase 2 plan's section 7: what the Recently
/// Deleted dialog and the conflict flow ask of PlacesService (D21) —
/// restore conflicts and the edited restore that resolves them (D15),
/// batch restore, permanent deletion and emptying (D22), all behind D3's
/// guard and D1's no-rollback rule.
///
/// Every service here gets a ManualTimeProvider, so no assertion depends
/// on the clock or zone of the machine running it.
/// </summary>
public sealed class PlacesServiceRecentlyDeletedTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);

    private static PlacesService NewService(out FakePlacesStorage storage, out ManualTimeProvider clock)
    {
        storage = new FakePlacesStorage();
        clock = new ManualTimeProvider(Now);
        return new PlacesService(storage, clock);
    }

    private static Place AddFolder(PlacesService service, string alias, string? folder = null)
    {
        var result = service.TryAdd(alias, PlaceType.Folder, TestPaths.Folder(folder ?? alias), out var created, out var persistence);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(persistence.Saved, persistence.UserMessage);
        return created!;
    }

    private static Place AddUrl(PlacesService service, string alias, string url)
    {
        var result = service.TryAdd(alias, PlaceType.Url, url, out var created, out var persistence);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(persistence.Saved, persistence.UserMessage);
        return created!;
    }

    private static void Remove(PlacesService service, Place place)
    {
        var persistence = service.Remove(place, out var removed);
        Assert.True(removed);
        Assert.True(persistence.Saved, persistence.UserMessage);
    }

    // -----------------------------------------------------------------
    // Restore conflicts and the edited restore (tests 47-49, D15)
    // -----------------------------------------------------------------

    /// <summary>Test 47: no conflict while the alias and destination are free — and none for a place that is not in Recently Deleted.</summary>
    [Fact]
    public void GetRestoreConflict_IsNullWhenNothingStandsInTheWay()
    {
        var service = NewService(out _, out _);
        var docs = AddFolder(service, "Docs");
        var active = AddFolder(service, "Active");
        Remove(service, docs);

        Assert.Null(service.GetRestoreConflict(docs));
        Assert.Null(service.GetRestoreConflict(active));
    }

    /// <summary>Test 47: an active place holding the alias (case-insensitively) is named as the alias holder, and the explanation mentions it.</summary>
    [Fact]
    public void GetRestoreConflict_NamesTheAliasHolder()
    {
        var service = NewService(out _, out _);
        var docs = AddFolder(service, "Docs");
        Remove(service, docs);
        var newDocs = AddFolder(service, "docs", "Elsewhere");

        var conflict = service.GetRestoreConflict(docs);

        Assert.NotNull(conflict);
        Assert.Same(docs, conflict!.Place);
        Assert.Same(newDocs, conflict.AliasHeldBy);
        Assert.Null(conflict.ResourceHeldBy);
        Assert.Equal("\"Docs\" can't be restored as it was: another place is now called \"docs\". Change the alias below, then restore.", conflict.Explanation);
    }

    /// <summary>Test 47: an active place holding the destination is named as the destination holder, for a folder path or a URL; with the alias held by another place as well, both are named.</summary>
    [Fact]
    public void GetRestoreConflict_NamesTheDestinationHolder_AndBoth()
    {
        var service = NewService(out _, out _);
        var docs = AddFolder(service, "Docs");
        var wiki = AddUrl(service, "Wiki", "https://wiki.example.com");
        Remove(service, docs);
        Remove(service, wiki);
        var projects = AddFolder(service, "Projects", "Docs");
        AddUrl(service, "New Wiki", "https://WIKI.example.com");

        var docsConflict = service.GetRestoreConflict(docs)!;
        Assert.Null(docsConflict.AliasHeldBy);
        Assert.Same(projects, docsConflict.ResourceHeldBy);
        Assert.Equal("\"Docs\" can't be restored as it was: \"Projects\" now uses its folder path. Change the folder path below, then restore.", docsConflict.Explanation);
        Assert.Equal("\"Wiki\" can't be restored as it was: \"New Wiki\" now uses its URL. Change the URL below, then restore.", service.GetRestoreConflict(wiki)!.Explanation);

        var docsAlias = AddFolder(service, "DOCS", "Other");

        var both = service.GetRestoreConflict(docs)!;
        Assert.Same(docsAlias, both.AliasHeldBy);
        Assert.Same(projects, both.ResourceHeldBy);
        Assert.Equal("\"Docs\" can't be restored as it was: another place is now called \"DOCS\", and \"Projects\" now uses its folder path. Change them below, then restore.", both.Explanation);
    }

    /// <summary>Test 47: one active place holding both the alias and the destination is named once, as both holders.</summary>
    [Fact]
    public void GetRestoreConflict_OnePlaceHoldingBoth_IsNamedOnce()
    {
        var service = NewService(out _, out _);
        var docs = AddFolder(service, "Docs");
        Remove(service, docs);
        var again = AddFolder(service, "docs", "Docs");

        var conflict = service.GetRestoreConflict(docs)!;

        Assert.Same(again, conflict.AliasHeldBy);
        Assert.Same(again, conflict.ResourceHeldBy);
        Assert.Equal("\"Docs\" can't be restored as it was: another place, \"docs\", now has its alias and its folder path. Change them below, then restore.", conflict.Explanation);
    }

    /// <summary>
    /// Test 48: the edited restore commits the D15 flow — the same record
    /// comes back under the new alias and destination, in its list slot and
    /// bubble slot, in one write.
    /// </summary>
    [Fact]
    public void EditedRestore_RestoresTheRecordWithTheNewValues_InOneWrite()
    {
        var service = NewService(out var storage, out _);
        var first = AddFolder(service, "First");
        var docs = AddFolder(service, "Docs");
        AddFolder(service, "Last");
        service.ToggleFavourite(first);
        service.ToggleFavourite(docs);
        Remove(service, docs);
        AddFolder(service, "docs", "Docs");
        var writes = storage.WriteCount;

        var result = service.TryRestore(docs, "  Docs (old)  ", "  " + TestPaths.Folder("Docs old") + "  ", out var persistence);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(persistence.Saved);
        Assert.Equal(writes + 1, storage.WriteCount);
        Assert.Null(docs.DeletedAt);
        Assert.Equal("Docs (old)", docs.Alias);
        Assert.Equal(TestPaths.Folder("Docs old"), docs.Resource);
        Assert.Equal(new[] { "First", "Docs (old)", "Last", "docs" }, service.Places.Select(p => p.Alias));
        Assert.Equal(1, docs.FavouriteOrder);
        Assert.Contains("Docs (old)", storage.LastWritten);
        Assert.Empty(service.RecentlyDeleted);
    }

    /// <summary>Test 49: edits that still conflict, or a malformed path, fail validation and change nothing — the record stays deleted and nothing is written.</summary>
    [Theory]
    [InlineData("DOCS", "Docs old")]
    [InlineData("Docs (old)", "Docs")]
    [InlineData("Docs (old)", null)]
    public void EditedRestore_ThatStillConflictsOrIsMalformed_ChangesNothing(string alias, string? folder)
    {
        var service = NewService(out var storage, out _);
        var docs = AddFolder(service, "Docs");
        Remove(service, docs);
        AddFolder(service, "docs", "Docs");
        var deletedAt = docs.DeletedAt;
        var writes = storage.WriteCount;
        var resource = folder is null ? "relative\\path" : TestPaths.Folder(folder);

        var result = service.TryRestore(docs, alias, resource, out var persistence);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.True(persistence.Saved);
        Assert.Equal(writes, storage.WriteCount);
        Assert.Equal("Docs", docs.Alias);
        Assert.Equal(TestPaths.Folder("Docs"), docs.Resource);
        Assert.Equal(deletedAt, docs.DeletedAt);
        Assert.Same(docs, Assert.Single(service.RecentlyDeleted));
    }

    // -----------------------------------------------------------------
    // Batch restore, permanent deletion, emptying (tests 50-52, D22)
    // -----------------------------------------------------------------

    /// <summary>
    /// Test 50: RestoreSelected restores the non-conflicting places in one
    /// write, newest deletion first, and returns the conflicts. With two
    /// deleted "Docs" selected, the newer deletion wins and the older comes
    /// back as a conflict with it; a place that conflicts with an active one
    /// is returned too. Nothing that conflicts leaves Recently Deleted.
    /// </summary>
    [Fact]
    public void RestoreSelected_RestoresTheFreeOnesInOneWrite_NewestDeletionFirst()
    {
        var service = NewService(out var storage, out var clock);
        var olderDocs = AddFolder(service, "Docs", "Docs 1");
        var free = AddFolder(service, "Free");
        var blocked = AddFolder(service, "Blocked");
        Remove(service, olderDocs);
        clock.Advance(TimeSpan.FromHours(1));
        Remove(service, free);
        Remove(service, blocked);
        clock.Advance(TimeSpan.FromHours(1));
        var newerDocs = AddFolder(service, "docs", "Docs 2");
        Remove(service, newerDocs);
        var blocker = AddFolder(service, "BLOCKED", "Blocker");
        var writes = storage.WriteCount;

        var (restored, conflicts, persistence) = service.RestoreSelected(new[] { olderDocs, free, blocked, newerDocs, newerDocs, blocker });

        Assert.True(persistence.Saved);
        Assert.Equal(writes + 1, storage.WriteCount);
        Assert.Equal(new[] { newerDocs, free }, restored);
        Assert.Equal(new[] { blocked, olderDocs }, conflicts.Select(c => c.Place));
        Assert.Same(blocker, conflicts[0].AliasHeldBy);
        Assert.Same(newerDocs, conflicts[1].AliasHeldBy);
        Assert.Equal(new[] { blocked, olderDocs }, service.RecentlyDeleted);
        Assert.Equal(new[] { "Free", "docs", "BLOCKED" }, service.Places.Select(p => p.Alias));
    }

    /// <summary>Not a numbered plan test (D22): a batch restore in which nothing can be restored writes nothing.</summary>
    [Fact]
    public void RestoreSelected_WithNothingRestorable_WritesNothing()
    {
        var service = NewService(out var storage, out _);
        var docs = AddFolder(service, "Docs");
        Remove(service, docs);
        var active = AddFolder(service, "docs", "Other");
        var writes = storage.WriteCount;

        var (restored, conflicts, persistence) = service.RestoreSelected(new[] { docs, active });

        Assert.Empty(restored);
        Assert.Single(conflicts);
        Assert.True(persistence.Saved);
        Assert.Equal(writes, storage.WriteCount);
    }

    /// <summary>
    /// Test 51: DeletePermanently removes only the deleted places it is
    /// given, ignores active ones, and they are gone after a reload — real
    /// files, so "gone" means gone from disk. The write count is the next
    /// test's, on the fake.
    /// </summary>
    [Fact]
    public void DeletePermanently_RemovesOnlyTheGivenDeletedPlaces_AndTheyStayGone()
    {
        using var dir = new TempDirectory();
        var clock = new ManualTimeProvider(Now);
        var service = new PlacesService(new FilePlacesStorage(dir.Path, "places.json"), clock);
        var a = AddFolder(service, "A");
        var b = AddFolder(service, "B");
        var c = AddFolder(service, "C");
        var active = AddFolder(service, "Active");
        Remove(service, a);
        Remove(service, b);
        Remove(service, c);

        var persistence = service.DeletePermanently(new[] { a, c, active });

        Assert.True(persistence.Saved);
        Assert.Equal(new[] { b }, service.RecentlyDeleted);
        Assert.Equal(new[] { active }, service.Places);

        var reloaded = new PlacesService(new FilePlacesStorage(dir.Path, "places.json"), clock);
        Assert.Equal(new[] { "B" }, reloaded.RecentlyDeleted.Select(p => p.Alias));
        Assert.Equal(new[] { "Active" }, reloaded.Places.Select(p => p.Alias));
    }

    /// <summary>Test 51, write count: one write for the batch, and none when none of the given places is in Recently Deleted.</summary>
    [Fact]
    public void DeletePermanently_WritesOnce_OrNotAtAll()
    {
        var service = NewService(out var storage, out _);
        var a = AddFolder(service, "A");
        var b = AddFolder(service, "B");
        var active = AddFolder(service, "Active");
        Remove(service, a);
        Remove(service, b);
        var writes = storage.WriteCount;

        Assert.True(service.DeletePermanently(new[] { a, b }).Saved);
        Assert.Equal(writes + 1, storage.WriteCount);

        Assert.True(service.DeletePermanently(new[] { a, active }).Saved);
        Assert.Equal(writes + 1, storage.WriteCount);
        Assert.Same(active, Assert.Single(service.Places));
    }

    /// <summary>Test 52: EmptyRecentlyDeleted removes every deleted place and keeps the active ones, in one write; when it is already empty it writes nothing.</summary>
    [Fact]
    public void EmptyRecentlyDeleted_RemovesEveryDeletedPlace_AndWritesNothingWhenEmpty()
    {
        var service = NewService(out var storage, out _);
        var a = AddFolder(service, "A");
        var b = AddFolder(service, "B");
        AddFolder(service, "Active");
        Remove(service, a);
        Remove(service, b);
        var writes = storage.WriteCount;

        Assert.True(service.EmptyRecentlyDeleted().Saved);

        Assert.Empty(service.RecentlyDeleted);
        Assert.Equal(new[] { "Active" }, service.Places.Select(p => p.Alias));
        Assert.Equal(writes + 1, storage.WriteCount);
        Assert.DoesNotContain("deletedAt", storage.LastWritten);

        Assert.True(service.EmptyRecentlyDeleted().Saved);
        Assert.Equal(writes + 1, storage.WriteCount);
    }

    // -----------------------------------------------------------------
    // D3 and D1 (tests 53-54)
    // -----------------------------------------------------------------

    /// <summary>Test 53: while recovery is unresolved, the batch operations and the edited restore are refused outright — nothing changes, nothing is written (D3).</summary>
    [Fact]
    public void BatchOperationsAndTheEditedRestore_AreRefused_WhileRecoveryIsUnresolved()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = "{ not valid json" };
        var service = new PlacesService(storage, new ManualTimeProvider(Now));
        Assert.True(service.IsRecoveryUnresolved);
        var stranger = new Place { Alias = "X", Type = PlaceType.Folder, Resource = TestPaths.Folder("X"), DeletedAt = Now };

        var (restored, conflicts, restorePersistence) = service.RestoreSelected(new[] { stranger });
        var deletePersistence = service.DeletePermanently(new[] { stranger });
        var emptyPersistence = service.EmptyRecentlyDeleted();
        var editedValidation = service.TryRestore(stranger, "Y", TestPaths.Folder("Y"), out var editedPersistence);

        Assert.Empty(restored);
        Assert.Empty(conflicts);
        foreach (var persistence in new[] { restorePersistence, deletePersistence, emptyPersistence, editedPersistence })
        {
            Assert.False(persistence.Saved);
            Assert.Equal(service.RecoveryBlockedMessage, persistence.UserMessage);
        }
        Assert.False(editedValidation.Success);
        Assert.Equal("X", stranger.Alias);
        Assert.Equal(Now, stranger.DeletedAt);
        Assert.Equal(0, storage.WriteCount);
    }

    /// <summary>
    /// Test 54 (D1): a failed save after DeletePermanently leaves the places
    /// gone from memory — the deletion is not rolled back — and the store
    /// unsaved; RetrySave then writes the store without them.
    /// </summary>
    [Fact]
    public void FailedSaveAfterDeletePermanently_KeepsThemGone_AndRetryWrites()
    {
        var service = NewService(out var storage, out _);
        var a = AddFolder(service, "Doomed");
        AddFolder(service, "Active");
        Remove(service, a);
        storage.FailNextWrite = true;

        var persistence = service.DeletePermanently(new List<Place> { a });

        Assert.False(persistence.Saved);
        Assert.NotNull(persistence.UserMessage);
        Assert.Empty(service.RecentlyDeleted);
        Assert.True(service.HasUnsavedChanges);
        Assert.Contains("Doomed", storage.ContentsToReturn);

        var retry = service.RetrySave();

        Assert.True(retry.Saved);
        Assert.False(service.HasUnsavedChanges);
        Assert.DoesNotContain("Doomed", storage.LastWritten);
    }
}
