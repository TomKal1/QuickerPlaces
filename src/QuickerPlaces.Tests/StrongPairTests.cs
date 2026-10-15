using System;
using QuickerPlaces.Services.Sessions.Suggestions;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>The unordered pair both rules produce (session suggestions plan §5).</summary>
public sealed class StrongPairTests
{
    // Windows literals: DocumentPaths never calls Path.*, so they normalise the same on Linux.
    private static string File(string name) => @"C:\Jobs\" + name;

    [Fact]
    public void Create_ignores_the_order_of_the_two_files()
    {
        var day = new DateOnly(2026, 10, 1);
        Assert.Equal(
            StrongPair.Create(File("a.pdf"), File("b.pdf"), SuggestionRule.UsedTogether, 3, day, TimeSpan.Zero),
            StrongPair.Create(File("b.pdf"), File("a.pdf"), SuggestionRule.UsedTogether, 3, day, TimeSpan.Zero));
    }

    [Fact]
    public void Create_puts_the_first_path_first_ignoring_case()
    {
        var pair = StrongPair.Create(File("b.pdf"), File("A.pdf"), SuggestionRule.OpenTogether, 1, new DateOnly(2026, 10, 1), TimeSpan.FromHours(1));
        Assert.Equal(File("A.pdf"), pair.First);
        Assert.Equal(File("b.pdf"), pair.Second);
    }

    [Fact]
    public void Create_rejects_the_same_file_twice()
        => Assert.Throws<ArgumentException>(() =>
            StrongPair.Create(File("a.pdf"), File("A.PDF"), SuggestionRule.UsedTogether, 1, new DateOnly(2026, 10, 1), TimeSpan.Zero));

    [Fact]
    public void Create_rejects_the_same_file_spelled_differently()
        => Assert.Throws<ArgumentException>(() =>
            StrongPair.Create(@"C:\Jobs\a.pdf", "C:/Jobs/x/../a.pdf", SuggestionRule.UsedTogether, 1, new DateOnly(2026, 10, 1), TimeSpan.Zero));

    [Fact]
    public void Create_orders_by_the_normalised_spelling_and_keeps_the_paths_as_given()
    {
        var day = new DateOnly(2026, 10, 1);
        var one = StrongPair.Create(@"C:\J\b.pdf", "C:/J/a.pdf", SuggestionRule.UsedTogether, 1, day, TimeSpan.Zero);
        var two = StrongPair.Create("C:/J/b.pdf", @"C:\J\a.pdf", SuggestionRule.UsedTogether, 1, day, TimeSpan.Zero);
        Assert.Equal("C:/J/a.pdf", one.First);
        Assert.Equal(@"C:\J\b.pdf", one.Second);
        Assert.Equal(@"C:\J\a.pdf", two.First);
        Assert.Equal("C:/J/b.pdf", two.Second);
    }

    [Fact]
    public void Create_keeps_the_day_of_the_longest_overlap()
    {
        var pair = StrongPair.Create(File("a.pdf"), File("b.pdf"), SuggestionRule.OpenTogether, 2,
            new DateOnly(2026, 10, 5), TimeSpan.FromMinutes(100), new DateOnly(2026, 10, 1));
        Assert.Equal(new DateOnly(2026, 10, 1), pair.LongestOverlapOn);
        Assert.Null(StrongPair.Create(File("a.pdf"), File("b.pdf"), SuggestionRule.UsedTogether, 2, new DateOnly(2026, 10, 5), TimeSpan.Zero).LongestOverlapOn);
    }

    [Fact]
    public void A_suggestion_adds_to_a_session_only_with_a_target()
    {
        var reason = new SuggestionReason(3, null, null, null);
        Assert.False(new SessionSuggestion("k", new[] { "a" }, new[] { "a" }, null, "Name", reason).IsAddToSession);
        Assert.True(new SessionSuggestion("k", new[] { "a" }, new[] { "a" }, "s1", null, reason).IsAddToSession);
    }
}
