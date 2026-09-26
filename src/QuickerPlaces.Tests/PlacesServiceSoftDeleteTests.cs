using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Tests 23, 24 and 26 from the Phase 2 plan's section 7: Remove moves a
/// place to Recently Deleted instead of deleting it (§4.8, D7), and a
/// restore brings back the same record to the same list slot and bubble
/// slot, in this session or after a restart (D9). Tests 25 and 27 to 30
/// are the adapted Undo tests in PlacesServiceTests; 31 and 32 extend
/// PlacesServicePersistenceTests.
///
/// Every service here gets a ManualTimeProvider, so no assertion depends
/// on the clock or zone of the machine running it.
/// </summary>
public sealed class PlacesServiceSoftDeleteTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 15, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();
    private readonly ManualTimeProvider _clock = new(Now);

    public void Dispose() => _temp.Dispose();

    private PlacesService NewService() => new(new FilePlacesStorage(_temp.Path, "places.json"), _clock);

    private static Place Add(PlacesService service, string alias)
    {
        var result = service.TryAdd(alias, PlaceType.Folder, TestPaths.Folder(alias), out var created, out var persistence);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(persistence.Saved, persistence.UserMessage);
        return created!;
    }

    private static void Remove(PlacesService service, Place place)
    {
        var persistence = service.Remove(place, out var removed);
        Assert.True(persistence.Saved, persistence.UserMessage);
        Assert.True(removed);
    }

    private static string[] Bubbles(PlacesService service)
        => service.Places.Where(p => p.IsFavourite).OrderBy(p => p.FavouriteOrder).Select(p => p.Alias).ToArray();

    /// <summary>
    /// Test 23: Remove stamps DeletedAt from the clock, hides the place from
    /// Places, lists it in RecentlyDeleted, and persists it there — in its
    /// list slot — so a reload finds it deleted, not gone (§4.8, D7).
    /// </summary>
    [Fact]
    public void Remove_StampsDeletedAt_MovesThePlaceToRecentlyDeleted_AndPersists()
    {
        var service = NewService();
        Add(service, "A");
        var b = Add(service, "B");
        Add(service, "C");
        _clock.Advance(TimeSpan.FromMinutes(5));

        Remove(service, b);

        Assert.Equal(Now.AddMinutes(5), b.DeletedAt);
        Assert.Equal(new[] { "A", "C" }, service.Places.Select(p => p.Alias));
        Assert.Same(b, Assert.Single(service.RecentlyDeleted));

        var written = JsonNode.Parse(File.ReadAllText(_temp.File("places.json")))!["places"]!.AsArray();
        Assert.Equal(new[] { "A", "B", "C" }, written.Select(p => p!["alias"]!.GetValue<string>()));
        Assert.Equal("2026-09-25T09:20:00+00:00", written[1]!["deletedAt"]!.GetValue<string>());

        var reloaded = NewService();
        Assert.Equal(new[] { "A", "C" }, reloaded.Places.Select(p => p.Alias));
        var deleted = Assert.Single(reloaded.RecentlyDeleted);
        Assert.Equal("B", deleted.Alias);
        Assert.Equal(Now.AddMinutes(5), deleted.DeletedAt);
    }

    /// <summary>
    /// Test 24: removing a favourite renumbers the active favourites dense
    /// and leaves the removed one's FavouriteOrder as its remembered slot
    /// (D9), on disk as well as in memory.
    /// </summary>
    [Fact]
    public void RemovingAFavourite_RenumbersTheActiveOnes_AndKeepsTheRemovedOnesSlot()
    {
        var service = NewService();
        var a = Add(service, "A");
        var b = Add(service, "B");
        var c = Add(service, "C");
        service.ToggleFavourite(a);
        service.ToggleFavourite(b);
        service.ToggleFavourite(c);

        Remove(service, b);

        Assert.Equal(0, a.FavouriteOrder);
        Assert.Equal(1, c.FavouriteOrder);
        Assert.True(b.IsFavourite);
        Assert.Equal(1, b.FavouriteOrder);

        var deleted = Assert.Single(NewService().RecentlyDeleted);
        Assert.True(deleted.IsFavourite);
        Assert.Equal(1, deleted.FavouriteOrder);
    }

    /// <summary>
    /// Test 26: a restore after a restart returns the place to the same list
    /// slot and the same bubble slot — the positions are in the record now
    /// (D7, D9), not in a session-only undo record.
    /// </summary>
    [Fact]
    public void RestoreAfterARestart_ReturnsToTheSameListSlotAndBubbleSlot()
    {
        var first = NewService();
        var a = Add(first, "A");
        var b = Add(first, "B");
        var c = Add(first, "C");
        Add(first, "D");
        first.ToggleFavourite(c);
        first.ToggleFavourite(b);
        first.ToggleFavourite(a);                                   // bubbles: C, B, A
        Remove(first, b);                                           // bubbles: C, A

        var second = NewService();
        var deleted = Assert.Single(second.RecentlyDeleted);
        var result = second.TryRestore(deleted, out var persistence);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(persistence.Saved, persistence.UserMessage);
        Assert.Null(deleted.DeletedAt);
        Assert.Equal(new[] { "A", "B", "C", "D" }, second.Places.Select(p => p.Alias));
        Assert.Equal(new[] { "C", "B", "A" }, Bubbles(second));
        Assert.Empty(second.RecentlyDeleted);

        var third = NewService();
        Assert.Equal(new[] { "A", "B", "C", "D" }, third.Places.Select(p => p.Alias));
        Assert.Equal(new[] { "C", "B", "A" }, Bubbles(third));
    }

    /// <summary>
    /// Not a numbered plan test (5.3 row 10): a restore shifts only active
    /// favourites to make room. Another deleted favourite's remembered slot
    /// is its own memory and must not move, or its later restore would land
    /// somewhere it never was.
    /// </summary>
    [Fact]
    public void Restore_ShiftsActiveFavouritesOnly_NeverAnotherDeletedRecordsSlot()
    {
        var service = NewService();
        var a = Add(service, "A");
        var b = Add(service, "B");
        var c = Add(service, "C");
        var d = Add(service, "D");
        foreach (var place in new[] { a, b, c, d })
            service.ToggleFavourite(place);                         // bubbles: A, B, C, D
        Remove(service, d);                                         // remembers slot 3
        Remove(service, b);                                         // remembers slot 1; bubbles: A, C

        Assert.True(service.TryRestore(b, out _).Success);

        Assert.Equal(new[] { "A", "B", "C" }, Bubbles(service));
        Assert.Equal(3, d.FavouriteOrder);

        Assert.True(service.TryRestore(d, out _).Success);

        Assert.Equal(new[] { "A", "B", "C", "D" }, Bubbles(service));
    }
}
