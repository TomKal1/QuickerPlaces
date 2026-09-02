using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>Test 17 from the Phase 1 plan's section 6: the refactor to route saves through IPlacesStorage does not change any validation rule.</summary>
public sealed class PlacesServiceValidationTests
{
    private static PlacesService NewService() => new(new FakePlacesStorage());

    [Fact]
    public void DuplicateAlias_IsRejectedCaseInsensitively()
    {
        var service = NewService();
        Assert.True(service.TryAdd("Docs", PlaceType.Folder, @"C:\Docs", out _).Success);

        var result = service.TryAdd("docs", PlaceType.Folder, @"C:\OtherDocs", out var created);

        Assert.False(result.Success);
        Assert.Null(created);
    }

    [Fact]
    public void DuplicateResource_SameType_IsRejectedCaseInsensitively()
    {
        var service = NewService();
        Assert.True(service.TryAdd("Docs", PlaceType.Url, "https://example.com/docs", out _).Success);

        var result = service.TryAdd("Docs2", PlaceType.Url, "HTTPS://EXAMPLE.COM/DOCS", out var created);

        Assert.False(result.Success);
        Assert.Null(created);
    }

    [Fact]
    public void SameResourceString_UnderDifferentType_IsAllowed()
    {
        var service = NewService();

        // A Windows drive-letter path is, perhaps surprisingly, also a
        // valid absolute Uri (System.Uri treats "C:\..." as an implicit
        // file:// URI) — so this one literal legitimately passes both
        // ValidateFolderFormat and ValidateUrlFormat, letting the same
        // resource string be used for both PlaceTypes in this test.
        const string resource = @"C:\Shared\Data";

        Assert.True(service.TryAdd("AsUrl", PlaceType.Url, resource, out _).Success);

        var result = service.TryAdd("AsFolder", PlaceType.Folder, resource, out var created);

        Assert.True(result.Success);
        Assert.NotNull(created);
    }

    [Fact]
    public void EditingAPlace_DoesNotCollideWithItself()
    {
        var service = NewService();
        service.TryAdd("Docs", PlaceType.Folder, @"C:\Docs", out var created);
        var place = created!;

        var renameResult = service.TryRenameAlias(place, "Docs");
        Assert.True(renameResult.Success);

        var resourceResult = service.TryEditResource(place, @"C:\Docs");
        Assert.True(resourceResult.Success);
    }
}
