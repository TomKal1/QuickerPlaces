using System.Collections.Generic;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Tests 2 to 5, 19, 20, and D3's guard (plan test 10) from the Phase 1
/// plan's section 6: a failed save is reported, never rolled back, keeps
/// the banner state up until a real successful save, and the whole batch
/// in CommitImport persists exactly once.
/// </summary>
public sealed class PlacesServicePersistenceTests
{
    private static PlacesService NewService(out FakePlacesStorage storage)
    {
        storage = new FakePlacesStorage();
        return new PlacesService(storage);
    }

    /// <summary>Test 2: a save failure is never reported as success — validation and persistence are two separate answers.</summary>
    [Fact]
    public void FailedAdd_ReportsValidationSuccessButPersistenceFailure()
    {
        var service = NewService(out var storage);
        storage.FailNextWrite = true;

        var validation = service.TryAdd("Docs", PlaceType.Folder, @"C:\Docs", out var created, out var persistence);

        Assert.True(validation.Success);
        Assert.NotNull(created);
        Assert.False(persistence.Saved);
        Assert.NotNull(persistence.UserMessage);
    }

    /// <summary>Test 3 (D1): the failed add is still in memory, and HasUnsavedChanges reflects it.</summary>
    [Fact]
    public void FailedAdd_KeepsTheChangeInMemoryAndMarksUnsaved()
    {
        var service = NewService(out var storage);
        storage.FailNextWrite = true;

        service.TryAdd("Docs", PlaceType.Folder, @"C:\Docs", out var created, out _);

        Assert.Contains(created, service.Places);
        Assert.True(service.HasUnsavedChanges);
    }

    /// <summary>Test 4: RetrySave after the fault clears succeeds and clears HasUnsavedChanges.</summary>
    [Fact]
    public void RetrySave_AfterFaultCleared_SucceedsAndClearsUnsavedFlag()
    {
        var service = NewService(out var storage);
        storage.FailNextWrite = true;
        service.TryAdd("Docs", PlaceType.Folder, @"C:\Docs", out _, out _);
        Assert.True(service.HasUnsavedChanges);

        // FailNextWrite already cleared itself after the one failed write.
        var retry = service.RetrySave();

        Assert.True(retry.Saved);
        Assert.Null(retry.UserMessage);
        Assert.False(service.HasUnsavedChanges);
    }

    /// <summary>
    /// Test 5: a second, unrelated command must not clear the banner state
    /// on its own — only a real successful persist may. This is the
    /// requirement most likely to be broken later by a well-meaning reset
    /// on some other path, so it gets its own explicit test rather than
    /// relying on the other tests here to catch a regression incidentally.
    /// </summary>
    [Fact]
    public void UnrelatedCommand_DoesNotClearUnsavedFlagLeftByAFailedSave()
    {
        var service = NewService(out var storage);
        storage.FailEveryWrite = true;

        service.TryAdd("Docs", PlaceType.Folder, @"C:\Docs", out _, out var addPersistence);
        Assert.False(addPersistence.Saved);
        Assert.True(service.HasUnsavedChanges);

        service.TryAdd("Links", PlaceType.Url, "https://example.com", out _, out var secondPersistence);

        Assert.False(secondPersistence.Saved);
        Assert.True(service.HasUnsavedChanges);
    }

    /// <summary>A failed Remove reports not saved and keeps the removal applied in memory (D1).</summary>
    [Fact]
    public void FailedRemove_ReportsNotSavedAndKeepsTheChangeInMemory()
    {
        var service = NewService(out var storage);
        service.TryAdd("Docs", PlaceType.Folder, @"C:\Docs", out var created, out _);
        storage.FailNextWrite = true;

        var persistence = service.Remove(created!);

        Assert.False(persistence.Saved);
        Assert.NotNull(persistence.UserMessage);
        Assert.DoesNotContain(created, service.Places);
        Assert.True(service.HasUnsavedChanges);
    }

    /// <summary>A failed ToggleFavourite reports not saved and keeps the flip applied in memory (D1).</summary>
    [Fact]
    public void FailedToggleFavourite_ReportsNotSavedAndKeepsTheChangeInMemory()
    {
        var service = NewService(out var storage);
        service.TryAdd("Docs", PlaceType.Folder, @"C:\Docs", out var created, out _);
        storage.FailNextWrite = true;

        var persistence = service.ToggleFavourite(created!);

        Assert.False(persistence.Saved);
        Assert.True(created!.IsFavourite);
        Assert.True(service.HasUnsavedChanges);
    }

    /// <summary>A failed SetFavouriteOrder reports not saved and keeps the new order applied in memory (D1).</summary>
    [Fact]
    public void FailedSetFavouriteOrder_ReportsNotSavedAndKeepsTheChangeInMemory()
    {
        var service = NewService(out var storage);
        service.TryAdd("A", PlaceType.Folder, @"C:\A", out var a, out _);
        service.TryAdd("B", PlaceType.Folder, @"C:\B", out var b, out _);
        service.ToggleFavourite(a!);
        service.ToggleFavourite(b!);
        storage.FailNextWrite = true;

        var persistence = service.SetFavouriteOrder(new List<Place> { b!, a! });

        Assert.False(persistence.Saved);
        Assert.Equal(0, b!.FavouriteOrder);
        Assert.Equal(1, a!.FavouriteOrder);
        Assert.True(service.HasUnsavedChanges);
    }

    /// <summary>Test 19: importing several candidates persists exactly once, not once per record.</summary>
    [Fact]
    public void CommitImport_PersistsOnceForTheWholeBatch()
    {
        var service = NewService(out var storage);
        var candidates = new List<Place>
        {
            new() { Alias = "A", Type = PlaceType.Folder, Resource = @"C:\A" },
            new() { Alias = "B", Type = PlaceType.Folder, Resource = @"C:\B" },
            new() { Alias = "C", Type = PlaceType.Folder, Resource = @"C:\C" }
        };

        var writesBefore = storage.WriteCount;
        var (imported, persistence) = service.CommitImport(candidates);

        Assert.Equal(3, imported.Count);
        Assert.True(persistence.Saved);
        Assert.Equal(writesBefore + 1, storage.WriteCount);
    }

    /// <summary>Test 20 (D1 across the batch path): a failed import commit reports not saved and keeps every imported candidate in memory.</summary>
    [Fact]
    public void FailedCommitImport_ReportsNotSavedAndKeepsCandidatesInMemory()
    {
        var service = NewService(out var storage);
        storage.FailNextWrite = true;
        var candidates = new List<Place>
        {
            new() { Alias = "A", Type = PlaceType.Folder, Resource = @"C:\A" },
            new() { Alias = "B", Type = PlaceType.Folder, Resource = @"C:\B" }
        };

        var (imported, persistence) = service.CommitImport(candidates);

        Assert.Equal(2, imported.Count);
        Assert.False(persistence.Saved);
        Assert.NotNull(persistence.UserMessage);
        Assert.All(imported, p => Assert.Contains(p, service.Places));
        Assert.True(service.HasUnsavedChanges);
    }

    /// <summary>
    /// Plan test 10 (D3): while recovery is unresolved, every mutation is
    /// refused outright — no in-memory change, no write attempted at all.
    /// Recovery is put into the unresolved state the same way production
    /// code does it: by loading a store the version gate classifies as
    /// Damaged (see PlacesServiceLoadOutcomeTests for the classification
    /// itself), not through a test-only setter.
    /// </summary>
    [Fact]
    public void MutationsAreRefused_WhileRecoveryIsUnresolved()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = "{ not valid json" };
        var service = new PlacesService(storage);
        Assert.True(service.IsRecoveryUnresolved);

        var addValidation = service.TryAdd("Docs", PlaceType.Folder, @"C:\Docs", out var created, out var addPersistence);
        Assert.False(addValidation.Success);
        Assert.Null(created);
        Assert.False(addPersistence.Saved);
        Assert.Equal(service.RecoveryBlockedMessage, addPersistence.UserMessage);
        Assert.Empty(service.Places);

        var removePersistence = service.Remove(new Place { Alias = "X", Type = PlaceType.Folder, Resource = @"C:\X" });
        Assert.False(removePersistence.Saved);

        var (imported, importPersistence) = service.CommitImport(new List<Place>
        {
            new() { Alias = "Y", Type = PlaceType.Folder, Resource = @"C:\Y" }
        });
        Assert.Empty(imported);
        Assert.False(importPersistence.Saved);

        var retryPersistence = service.RetrySave();
        Assert.False(retryPersistence.Saved);

        Assert.Empty(service.Places);
        Assert.Equal(0, storage.WriteCount);
    }
}
