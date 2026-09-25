using System;
using System.IO;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Test 16 from the Phase 1 plan's section 6: an existing v1 places.json,
/// with today's exact shape, loads with every field intact. This is the
/// regression net for an accidental change to JsonOptions or to Place's
/// property names.
///
/// Fixtures/places.v1.json is frozen once written — do not "fix" it to
/// match a future schema change; a new fixture is added for that instead.
/// </summary>
public sealed class PlacesStoreFixtureTests
{
    [Fact]
    public void FixtureFile_LoadsWithEveryFieldIntact()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "places.v1.json");
        var json = File.ReadAllText(path);

        var storage = new FakePlacesStorage { ContentsToReturn = json };
        var service = new PlacesService(storage);

        Assert.Equal(StoreLoadOutcome.Ok, service.LoadOutcome);
        Assert.Equal(3, service.Places.Count);

        var downloads = service.Places.Single(p => p.Alias == "Downloads");
        Assert.Equal(PlaceType.Folder, downloads.Type);
        Assert.Equal(@"C:\Users\Test\Downloads", downloads.Resource);
        Assert.True(downloads.IsFavourite);
        Assert.Equal(0, downloads.FavouriteOrder);
        Assert.Equal(new DateTime(2026, 1, 15, 9, 30, 0), downloads.DateAdded);

        var repo = service.Places.Single(p => p.Alias == "QuickerPlaces Repo");
        Assert.Equal(PlaceType.Url, repo.Type);
        Assert.Equal("https://github.com/example/quickerplaces", repo.Resource);
        Assert.True(repo.IsFavourite);
        Assert.Equal(1, repo.FavouriteOrder);
        Assert.Equal(new DateTime(2026, 2, 3, 14, 5, 22), repo.DateAdded);

        var oldReports = service.Places.Single(p => p.Alias == "Old Reports");
        Assert.Equal(PlaceType.Folder, oldReports.Type);
        Assert.Equal(@"D:\Archive\Reports\2024", oldReports.Resource);
        Assert.False(oldReports.IsFavourite);
        Assert.Null(oldReports.FavouriteOrder);
        Assert.Equal(new DateTime(2025, 11, 20, 8, 12, 47), oldReports.DateAdded);
    }
}
