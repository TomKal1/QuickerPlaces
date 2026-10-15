using System.Linq;
using QuickerPlaces.Services.Sessions.Suggestions;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>The stable identity of a suggested file set (session suggestions plan G9).</summary>
public sealed class SessionSuggestionKeyTests
{
    // Windows literals: DocumentPaths never calls Path.*, so they normalise the same on Linux.
    private static string File(string name) => @"C:\Jobs\" + name;

    [Fact]
    public void Order_does_not_matter()
        => Assert.Equal(
            SessionSuggestionKey.For(new[] { File("a.pdf"), File("b.pdf"), File("c.docx") }),
            SessionSuggestionKey.For(new[] { File("c.docx"), File("a.pdf"), File("b.pdf") }));

    [Fact]
    public void Case_does_not_matter()
        => Assert.Equal(
            SessionSuggestionKey.For(new[] { File("a.pdf"), File("b.pdf") }),
            SessionSuggestionKey.For(new[] { File("a.pdf"), File("b.pdf") }.Select(p => p.ToUpperInvariant())));

    [Fact]
    public void Duplicate_paths_are_ignored()
        => Assert.Equal(
            SessionSuggestionKey.For(new[] { File("a.pdf"), File("b.pdf") }),
            SessionSuggestionKey.For(new[] { File("a.pdf"), File("b.pdf"), File("A.pdf") }));

    [Fact]
    public void Different_sets_have_different_keys()
        => Assert.NotEqual(
            SessionSuggestionKey.For(new[] { File("a.pdf"), File("b.pdf") }),
            SessionSuggestionKey.For(new[] { File("a.pdf"), File("c.pdf") }));

    [Fact]
    public void Separators_and_dot_dot_do_not_matter()
        => Assert.Equal(
            SessionSuggestionKey.For(new[] { @"C:\Jobs\a.pdf" }),
            SessionSuggestionKey.For(new[] { "C:/Jobs/x/../a.pdf" }));

    [Fact]
    public void Key_format_is_pinned()
        => Assert.Equal(
            "f143cc5b405b3e2cdcf4c73326a3cd42",
            SessionSuggestionKey.For(new[] { @"C:\Jobs\b-201.pdf", @"c:\jobs\A-101.pdf" }));

    [Fact]
    public void Key_is_32_lower_case_hex_characters()
    {
        var key = SessionSuggestionKey.For(new[] { File("a.pdf"), File("b.pdf") });
        Assert.Equal(32, key.Length);
        Assert.All(key, c => Assert.True(c is >= '0' and <= '9' or >= 'a' and <= 'f'));
    }
}
