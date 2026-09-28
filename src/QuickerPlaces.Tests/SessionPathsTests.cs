using QuickerPlaces.Services.Sessions;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>How sessions spell, compare and split PDF paths (sessions plan D4), on any OS.</summary>
public sealed class SessionPathsTests
{
    [Theory]
    [InlineData(@"C:\Jobs\A-101.pdf", @"C:\Jobs\A-101.pdf")]
    [InlineData(@"  ""C:\Jobs\A-101.PDF""  ", @"C:\Jobs\A-101.PDF")]
    [InlineData(@"C:/Jobs//Tower B/./x/../A-101.pdf", @"C:\Jobs\Tower B\A-101.pdf")]
    [InlineData(@"\\server\share\Spec.pdf", @"\\server\share\Spec.pdf")]
    [InlineData(@"C:\A.pdf", @"C:\A.pdf")]
    public void NormalizePdf_SpellsAFullPdfPathOneWay(string input, string expected)
        => Assert.Equal(expected, SessionPaths.NormalizePdf(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"A-101.pdf")]
    [InlineData(@"Jobs\A-101.pdf")]
    [InlineData(@"\Jobs\A-101.pdf")]
    [InlineData(@"C:A-101.pdf")]
    [InlineData(@"C:\Jobs\A-101.txt")]
    [InlineData(@"C:\Jobs\A-101.pdf.lnk")]
    [InlineData(@"C:\Jobs\.pdf")]
    [InlineData(@"\\server\x.pdf")]
    [InlineData(@"https://example.com/A-101.pdf")]
    [InlineData(@"\\?\C:\A-101.pdf")]
    public void NormalizePdf_RefusesAnythingElse(string? input)
        => Assert.Null(SessionPaths.NormalizePdf(input));

    [Fact]
    public void Same_IgnoresCaseAndSeparators()
    {
        Assert.True(SessionPaths.Same(@"C:\Jobs\A-101.pdf", @"c:/jobs/a-101.PDF"));
        Assert.False(SessionPaths.Same(@"C:\Jobs\A-101.pdf", @"C:\Jobs\A-102.pdf"));
    }

    [Theory]
    [InlineData(@"C:\Jobs\A-101.pdf", "A-101.pdf", @"C:\Jobs")]
    [InlineData(@"C:\A-101.pdf", "A-101.pdf", @"C:\")]
    [InlineData(@"\\server\share\A.pdf", "A.pdf", @"\\server\share")]
    [InlineData("A.pdf", "A.pdf", "")]
    public void FileNameAndFolder_SplitAtTheLastSeparator(string path, string name, string folder)
    {
        Assert.Equal(name, SessionPaths.FileName(path));
        Assert.Equal(folder, SessionPaths.Folder(path));
    }
}
