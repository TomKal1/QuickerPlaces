using QuickerPlaces.Services.Documents;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>Document kinds by extension, and how document paths are spelled, compared and split (sessions plan D4), on any OS.</summary>
public sealed class DocumentPathsTests
{
    [Theory]
    [InlineData(@"C:\Jobs\A-101.pdf", @"C:\Jobs\A-101.pdf")]
    [InlineData(@"  ""C:\Jobs\A-101.PDF""  ", @"C:\Jobs\A-101.PDF")]
    [InlineData(@"C:/Jobs//Tower B/./x/../A-101.pdf", @"C:\Jobs\Tower B\A-101.pdf")]
    [InlineData(@"\\server\share\Spec.pdf", @"\\server\share\Spec.pdf")]
    [InlineData(@"C:\A.pdf", @"C:\A.pdf")]
    public void Normalize_SpellsAFullPdfPathOneWay(string input, string expected)
        => Assert.Equal(expected, DocumentPaths.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"A-101.pdf")]
    [InlineData(@"Jobs\A-101.pdf")]
    [InlineData(@"\Jobs\A-101.pdf")]
    [InlineData(@"C:A-101.pdf")]
    [InlineData(@"C:\Jobs\A-101.txt")]
    [InlineData(@"C:\Jobs\A-101.pdf.lnk")]
    [InlineData(@"C:\Jobs\notes.csv")]
    [InlineData(@"C:\Jobs\.docx")]
    [InlineData(@"C:\Jobs\.pdf")]
    [InlineData(@"\\server\x.pdf")]
    [InlineData(@"https://example.com/A-101.pdf")]
    [InlineData(@"\\?\C:\A-101.pdf")]
    public void Normalize_RefusesAnythingElse(string? input)
        => Assert.Null(DocumentPaths.Normalize(input));

    [Theory]
    [InlineData(@"C:\A.pdf", DocumentKind.Pdf)]
    [InlineData(@"C:\A.PDF", DocumentKind.Pdf)]
    [InlineData(@"C:\A.docx", DocumentKind.Word)]
    [InlineData(@"C:\A.docm", DocumentKind.Word)]
    [InlineData(@"C:\A.doc", DocumentKind.Word)]
    [InlineData(@"C:\A.rtf", DocumentKind.Word)]
    [InlineData(@"C:\A.xlsx", DocumentKind.Excel)]
    [InlineData(@"C:\A.xlsm", DocumentKind.Excel)]
    [InlineData(@"C:\A.xls", DocumentKind.Excel)]
    [InlineData(@"C:\A.xlsb", DocumentKind.Excel)]
    public void Kinds_AreDecidedByExtension(string path, DocumentKind kind)
        => Assert.Equal(kind, DocumentKinds.FromPath(path));

    [Theory]
    [InlineData(@"C:\A.txt")]
    [InlineData(@"C:\A.pptx")]
    [InlineData(@"C:\docx")]
    [InlineData(@"C:\Jobs.pdf\readme")]
    [InlineData(null)]
    public void OtherFiles_HaveNoKind(string? path)
        => Assert.Null(DocumentKinds.FromPath(path));

    [Fact]
    public void Stem_DropsTheExtension()
    {
        Assert.Equal("Report v2", DocumentPaths.Stem(@"C:\Jobs\Report v2.docx"));
        Assert.Equal("Budget.final", DocumentPaths.Stem(@"C:\Budget.final.xlsx"));
    }

    [Fact]
    public void Same_IgnoresCaseAndSeparators()
    {
        Assert.True(DocumentPaths.Same(@"C:\Jobs\A-101.pdf", @"c:/jobs/a-101.PDF"));
        Assert.False(DocumentPaths.Same(@"C:\Jobs\A-101.pdf", @"C:\Jobs\A-102.pdf"));
    }

    [Theory]
    [InlineData(@"C:\Jobs\A-101.pdf", "A-101.pdf", @"C:\Jobs")]
    [InlineData(@"C:\A-101.pdf", "A-101.pdf", @"C:\")]
    [InlineData(@"\\server\share\A.pdf", "A.pdf", @"\\server\share")]
    [InlineData("A.pdf", "A.pdf", "")]
    public void FileNameAndFolder_SplitAtTheLastSeparator(string path, string name, string folder)
    {
        Assert.Equal(name, DocumentPaths.FileName(path));
        Assert.Equal(folder, DocumentPaths.Folder(path));
    }
}
