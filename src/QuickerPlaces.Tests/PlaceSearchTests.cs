using QuickerPlaces.Models;
using QuickerPlaces.Services;
using Xunit;

namespace QuickerPlaces.Tests;

public sealed class PlaceSearchTests
{
    private static readonly Place Wiki = new() { Alias = "Company Wiki", Type = PlaceType.Url, Resource = "https://wiki.example.com/prod" };
    private static readonly Place Downloads = new() { Alias = "Downloads", Type = PlaceType.Folder, Resource = @"C:\Users\Gavin\Downloads" };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_query_matches_everything(string? query)
    {
        Assert.True(PlaceSearch.Matches(Wiki, query));
        Assert.True(PlaceSearch.Matches(Downloads, query));
    }

    [Theory]
    [InlineData("wiki")]      // alias
    [InlineData("WIKI")]      // case-insensitive
    [InlineData("pany wi")]   // substrings, each term separately
    [InlineData("example")]   // resource only
    [InlineData("wiki prod")] // one term from each field
    [InlineData("  wiki  ")]  // surrounding whitespace
    public void Matches_alias_or_resource_terms(string query)
        => Assert.True(PlaceSearch.Matches(Wiki, query));

    [Theory]
    [InlineData("downloads")]
    [InlineData("gavin")]
    [InlineData(@"users\gavin")]
    public void Matches_folder_paths(string query)
        => Assert.True(PlaceSearch.Matches(Downloads, query));

    [Theory]
    [InlineData("intranet")]
    [InlineData("wiki intranet")] // every term must match
    public void Rejects_when_any_term_is_missing(string query)
        => Assert.False(PlaceSearch.Matches(Wiki, query));
}
