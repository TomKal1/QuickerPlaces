using System;
using System.ComponentModel;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Tests 34 to 37 from the Phase 3 plan's section 7: PlaceSort (D29, D30) —
/// the comparer behind every grid sort, the header-click cycle, and the
/// lenient reading of what settings.json remembers. Pure: no service, no
/// view.
/// </summary>
public sealed class PlaceSortTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static Place P(string alias, PlaceType type = PlaceType.Url, string? resource = null, bool favourite = false,
        int opens = 0, DateTimeOffset? lastOpened = null, int addedDay = 1)
        => new()
        {
            Alias = alias,
            Type = type,
            Resource = resource ?? $"https://{alias.ToLowerInvariant()}.example.com",
            IsFavourite = favourite,
            OpenCount = opens,
            LastOpenedAt = lastOpened,
            DateAdded = T0.AddDays(addedDay)
        };

    private static string[] Sorted(PlaceSortKey key, ListSortDirection direction, params Place[] places)
        => places.OrderBy(p => p, new PlaceSort(key, direction).Comparer).Select(p => p.Alias).ToArray();

    // -----------------------------------------------------------------
    // Test 34: each key, both directions, with ties broken by alias
    // -----------------------------------------------------------------

    [Fact]
    public void Alias_SortsCaseInsensitively()
    {
        var places = new[] { P("charlie"), P("Alpha"), P("bravo") };

        Assert.Equal(new[] { "Alpha", "bravo", "charlie" }, Sorted(PlaceSortKey.Alias, ListSortDirection.Ascending, places));
        Assert.Equal(new[] { "charlie", "bravo", "Alpha" }, Sorted(PlaceSortKey.Alias, ListSortDirection.Descending, places));
    }

    [Fact]
    public void Type_SortsByLabel_ThenAlias()
    {
        var places = new[] { P("Zed", PlaceType.Url), P("Beta", PlaceType.Folder, @"C:\b"), P("Alpha", PlaceType.Url), P("Able", PlaceType.Folder, @"C:\a") };

        Assert.Equal(new[] { "Able", "Beta", "Alpha", "Zed" }, Sorted(PlaceSortKey.Type, ListSortDirection.Ascending, places));
        Assert.Equal(new[] { "Alpha", "Zed", "Able", "Beta" }, Sorted(PlaceSortKey.Type, ListSortDirection.Descending, places));
    }

    [Fact]
    public void Destination_SortsCaseInsensitively()
    {
        var places = new[] { P("One", resource: "https://c.example.com"), P("Two", resource: "https://A.example.com"), P("Three", resource: "https://b.example.com") };

        Assert.Equal(new[] { "Two", "Three", "One" }, Sorted(PlaceSortKey.Destination, ListSortDirection.Ascending, places));
        Assert.Equal(new[] { "One", "Three", "Two" }, Sorted(PlaceSortKey.Destination, ListSortDirection.Descending, places));
    }

    [Fact]
    public void Favourite_Descending_PutsFavouritesFirst_ThenAlias()
    {
        var places = new[] { P("Delta"), P("Charlie", favourite: true), P("Bravo"), P("Alpha", favourite: true) };

        Assert.Equal(new[] { "Alpha", "Charlie", "Bravo", "Delta" }, Sorted(PlaceSortKey.Favourite, ListSortDirection.Descending, places));
        Assert.Equal(new[] { "Bravo", "Delta", "Alpha", "Charlie" }, Sorted(PlaceSortKey.Favourite, ListSortDirection.Ascending, places));
    }

    [Fact]
    public void Opens_SortsByCount_ThenAlias()
    {
        var places = new[] { P("Charlie", opens: 2), P("Bravo", opens: 9), P("Alpha", opens: 2), P("Delta", opens: 0) };

        Assert.Equal(new[] { "Bravo", "Alpha", "Charlie", "Delta" }, Sorted(PlaceSortKey.Opens, ListSortDirection.Descending, places));
        Assert.Equal(new[] { "Delta", "Alpha", "Charlie", "Bravo" }, Sorted(PlaceSortKey.Opens, ListSortDirection.Ascending, places));
    }

    [Fact]
    public void LastOpened_SortsByInstant_ToTheSecond()
    {
        var places = new[] { P("Early", lastOpened: T0), P("Later", lastOpened: T0.AddSeconds(30)), P("Middle", lastOpened: T0.AddSeconds(1)) };

        Assert.Equal(new[] { "Later", "Middle", "Early" }, Sorted(PlaceSortKey.LastOpened, ListSortDirection.Descending, places));
    }

    [Fact]
    public void DateAdded_SortsByInstant()
    {
        var places = new[] { P("Second", addedDay: 2), P("Third", addedDay: 3), P("First", addedDay: 1) };

        Assert.Equal(new[] { "First", "Second", "Third" }, Sorted(PlaceSortKey.DateAdded, ListSortDirection.Ascending, places));
        Assert.Equal(new[] { "Third", "Second", "First" }, Sorted(PlaceSortKey.DateAdded, ListSortDirection.Descending, places));
    }

    [Fact]
    public void EqualAliases_FallBackToDestination_SoTheOrderIsTotal()
    {
        var places = new[] { P("Same", resource: "https://b.example.com"), P("same", resource: "https://a.example.com") };

        Assert.Equal(new[] { "same", "Same" }, Sorted(PlaceSortKey.Opens, ListSortDirection.Descending, places));
    }

    // -----------------------------------------------------------------
    // Test 35: never-opened places sit at the old end
    // -----------------------------------------------------------------

    [Fact]
    public void NeverOpened_IsLastWhenNewestFirst_AndFirstWhenOldestFirst_InAliasOrder()
    {
        var places = new[] { P("Zulu"), P("Opened", lastOpened: T0), P("Alpha"), P("Recent", lastOpened: T0.AddDays(1)) };

        Assert.Equal(new[] { "Recent", "Opened", "Alpha", "Zulu" }, Sorted(PlaceSortKey.LastOpened, ListSortDirection.Descending, places));
        Assert.Equal(new[] { "Alpha", "Zulu", "Opened", "Recent" }, Sorted(PlaceSortKey.LastOpened, ListSortDirection.Ascending, places));
    }

    // -----------------------------------------------------------------
    // Test 36: the header-click cycle
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(PlaceSortKey.Alias, ListSortDirection.Ascending)]
    [InlineData(PlaceSortKey.Type, ListSortDirection.Ascending)]
    [InlineData(PlaceSortKey.Destination, ListSortDirection.Ascending)]
    [InlineData(PlaceSortKey.Favourite, ListSortDirection.Descending)]
    [InlineData(PlaceSortKey.LastOpened, ListSortDirection.Descending)]
    [InlineData(PlaceSortKey.Opens, ListSortDirection.Descending)]
    [InlineData(PlaceSortKey.DateAdded, ListSortDirection.Descending)]
    public void Next_CyclesFirstDirection_ThenTheOther_ThenNoSort(PlaceSortKey key, ListSortDirection first)
    {
        var other = first == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;

        var one = PlaceSort.Next(null, key);
        var two = PlaceSort.Next(one, key);
        var three = PlaceSort.Next(two, key);

        Assert.Equal(new PlaceSort(key, first), one);
        Assert.Equal(new PlaceSort(key, other), two);
        Assert.Null(three);
    }

    [Fact]
    public void Next_OnAnotherColumn_StartsThatColumnsCycle()
    {
        var byOpensReversed = new PlaceSort(PlaceSortKey.Opens, ListSortDirection.Ascending);

        Assert.Equal(new PlaceSort(PlaceSortKey.Alias, ListSortDirection.Ascending), PlaceSort.Next(byOpensReversed, PlaceSortKey.Alias));
    }

    // -----------------------------------------------------------------
    // Test 37: what settings.json remembers, read leniently (D30)
    // -----------------------------------------------------------------

    [Fact]
    public void Format_ThenParse_RoundTripsEveryKeyAndDirection()
    {
        foreach (var key in Enum.GetValues<PlaceSortKey>())
            foreach (var direction in new[] { ListSortDirection.Ascending, ListSortDirection.Descending })
            {
                var sort = new PlaceSort(key, direction);
                var (keyText, directionText) = sort.Format();
                Assert.Equal(sort, PlaceSort.Parse(keyText, directionText));
            }
    }

    [Fact]
    public void Format_WritesTheKeyName_AndALowerCaseDirection()
        => Assert.Equal(("LastOpened", "descending"), new PlaceSort(PlaceSortKey.LastOpened, ListSortDirection.Descending).Format());

    [Fact]
    public void Parse_IsCaseInsensitive()
        => Assert.Equal(new PlaceSort(PlaceSortKey.Opens, ListSortDirection.Ascending), PlaceSort.Parse("opens", "ASCENDING"));

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "ascending")]
    [InlineData("Alias", null)]
    [InlineData("", "")]
    [InlineData("Nonsense", "ascending")]
    [InlineData("Alias", "sideways")]
    [InlineData("3", "ascending")]
    [InlineData("Alias", "0")]
    public void Parse_ReturnsNoSort_ForAnythingItDoesNotRecognise(string? key, string? direction)
        => Assert.Null(PlaceSort.Parse(key, direction));
}
