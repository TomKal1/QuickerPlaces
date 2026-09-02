using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>Test 18 from the Phase 1 plan's section 6: favourite ordering stays a dense 0..n-1 sequence across toggle-on, toggle-off, and Remove of a favourite.</summary>
public sealed class PlacesServiceFavouriteTests
{
    private static PlacesService NewServiceWithThreePlaces(out Place a, out Place b, out Place c)
    {
        var service = new PlacesService(new FakePlacesStorage());
        service.TryAdd("A", PlaceType.Folder, @"C:\A", out var placeA);
        service.TryAdd("B", PlaceType.Folder, @"C:\B", out var placeB);
        service.TryAdd("C", PlaceType.Folder, @"C:\C", out var placeC);
        a = placeA!;
        b = placeB!;
        c = placeC!;
        return service;
    }

    private static void AssertDenseFavouriteOrder(PlacesService service)
    {
        var orders = service.Places
            .Where(p => p.IsFavourite)
            .OrderBy(p => p.FavouriteOrder)
            .Select(p => p.FavouriteOrder)
            .ToList();

        for (var i = 0; i < orders.Count; i++)
            Assert.Equal(i, orders[i]);
    }

    [Fact]
    public void TogglingOn_AppendsToEndOfFavouriteOrder()
    {
        var service = NewServiceWithThreePlaces(out var a, out var b, out var c);

        service.ToggleFavourite(a);
        service.ToggleFavourite(b);

        Assert.Equal(0, a.FavouriteOrder);
        Assert.Equal(1, b.FavouriteOrder);
        Assert.False(c.IsFavourite);
        AssertDenseFavouriteOrder(service);
    }

    [Fact]
    public void TogglingOff_RenumbersRemainingFavourites()
    {
        var service = NewServiceWithThreePlaces(out var a, out var b, out var c);
        service.ToggleFavourite(a);
        service.ToggleFavourite(b);
        service.ToggleFavourite(c);

        // Turn off the middle one; b and c should close the gap.
        service.ToggleFavourite(b);

        Assert.False(b.IsFavourite);
        Assert.Null(b.FavouriteOrder);
        Assert.True(a.IsFavourite);
        Assert.True(c.IsFavourite);
        AssertDenseFavouriteOrder(service);
        Assert.Equal(0, a.FavouriteOrder);
        Assert.Equal(1, c.FavouriteOrder);
    }

    [Fact]
    public void RemovingAFavourite_RenumbersRemainingFavourites()
    {
        var service = NewServiceWithThreePlaces(out var a, out var b, out var c);
        service.ToggleFavourite(a);
        service.ToggleFavourite(b);
        service.ToggleFavourite(c);

        service.Remove(a);

        AssertDenseFavouriteOrder(service);
        Assert.Equal(0, b.FavouriteOrder);
        Assert.Equal(1, c.FavouriteOrder);
    }

    [Fact]
    public void RemovingANonFavourite_LeavesFavouriteOrderUnchanged()
    {
        var service = NewServiceWithThreePlaces(out var a, out var b, out var c);
        service.ToggleFavourite(a);
        service.ToggleFavourite(b);
        // c stays a non-favourite.

        service.Remove(c);

        AssertDenseFavouriteOrder(service);
        Assert.Equal(0, a.FavouriteOrder);
        Assert.Equal(1, b.FavouriteOrder);
    }
}
