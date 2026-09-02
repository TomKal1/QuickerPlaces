using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>Test 1 from the Phase 1 plan's section 6: a round trip through the same storage instance is intact.</summary>
public sealed class PlacesServiceRoundTripTests
{
    [Fact]
    public void AddedPlace_SurvivesReloadFromSameStorage()
    {
        var storage = new FakePlacesStorage();
        var service = new PlacesService(storage);

        var result = service.TryAdd("Docs", PlaceType.Folder, @"C:\Docs", out var created, out _);

        Assert.True(result.Success);
        Assert.NotNull(created);

        // A second PlacesService over the *same* FakePlacesStorage instance
        // simulates relaunching the app: it must see exactly what the
        // first instance wrote.
        var reloaded = new PlacesService(storage);

        Assert.False(reloaded.LoadFailed);
        var place = Assert.Single(reloaded.Places);
        Assert.Equal(created!.Alias, place.Alias);
        Assert.Equal(created.Type, place.Type);
        Assert.Equal(created.Resource, place.Resource);
        Assert.Equal(created.IsFavourite, place.IsFavourite);
        Assert.Equal(created.FavouriteOrder, place.FavouriteOrder);
        Assert.Equal(created.DateAdded, place.DateAdded);
    }
}
