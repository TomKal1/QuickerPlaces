using System;
using System.Linq;
using QuickerPlaces.Services.Sessions;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Which PDFs count as open (sessions plan §4, D6–D9): file names in window
/// titles matched to paths, paths in titles and on command lines, files in
/// use, and Recent Items as suggestions.
/// </summary>
public sealed class OpenPdfResolverTests
{
    private const string A101 = @"C:\Jobs\Tower B\A-101.pdf";
    private const string A102 = @"C:\Jobs\Tower B\A-102.pdf";
    private const string OldA101 = @"C:\Jobs\Tower A\A-101.pdf";
    private const string Spec = @"\\files\projects\Tower B\Spec.pdf";

    private static readonly DateTimeOffset Now = new(2026, 9, 28, 9, 0, 0, TimeSpan.Zero);

    private static RecentDocument Recent(string path, int minutesAgo) => new(path, Now.AddMinutes(-minutesAgo));

    private static ViewerWindow Window(string title, string app = "Adobe Acrobat", string? commandLine = null) => new(title, app, commandLine);

    private static OpenPdfScan Resolve(ViewerWindow[] windows, RecentDocument[] recents, params string[] inUse)
        => OpenPdfResolver.Resolve(new OpenPdfEvidence(windows, recents), inUse);

    private static string[] OpenPaths(OpenPdfScan scan) => scan.Candidates.Where(c => c.IsLikelyOpen).Select(c => c.Path).ToArray();

    private static string[] SuggestedPaths(OpenPdfScan scan) => scan.Candidates.Where(c => !c.IsLikelyOpen).Select(c => c.Path).ToArray();

    [Theory]
    [InlineData("A-101.pdf - Adobe Acrobat Pro (64-bit)")]
    [InlineData("A-101.pdf and 3 more pages - Personal - Microsoft Edge")]
    [InlineData("Bluebeam Revu x64 - [A-101.pdf]")]
    [InlineData("A-101.PDF - SumatraPDF")]
    [InlineData("\"A-101.pdf\" - Foxit PDF Reader")]
    public void AFileNameInATitle_IsMatchedToItsRecentPath_AsOpen(string title)
    {
        var scan = Resolve(new[] { Window(title, "Viewer") }, new[] { Recent(A101, 5), Recent(A102, 10) });

        Assert.Equal(new[] { A101 }, OpenPaths(scan));
        Assert.Equal("Open in Viewer", scan.Candidates[0].Reason);
        Assert.Equal(new[] { A102 }, SuggestedPaths(scan));
        Assert.Empty(scan.UnmatchedTitles);
    }

    [Fact]
    public void ANameInsideALongerWord_DoesNotMatch()
    {
        var scan = Resolve(new[] { Window("RevA-101.pdf - Adobe Acrobat"), Window("XA-101.pdf - Adobe Acrobat") }, new[] { Recent(A101, 5) });

        Assert.Empty(OpenPaths(scan));
        Assert.Equal(new[] { "RevA-101.pdf (Adobe Acrobat)", "XA-101.pdf (Adobe Acrobat)" }, scan.UnmatchedTitles);
    }

    [Fact]
    public void TheLongestKnownName_WinsInATitle()
    {
        const string longer = @"C:\Jobs\Set - A-101.pdf";

        var scan = Resolve(new[] { Window("Set - A-101.pdf - Adobe Acrobat") }, new[] { Recent(A101, 5), Recent(longer, 50) });

        Assert.Equal(new[] { longer }, OpenPaths(scan));
    }

    [Fact]
    public void TwoFilesWithTheSameName_TheMostRecentlyOpenedIsChosen()
    {
        var scan = Resolve(new[] { Window("A-101.pdf - Adobe Acrobat") }, new[] { Recent(OldA101, 60), Recent(A101, 5) });

        Assert.Equal(new[] { A101 }, OpenPaths(scan));
        Assert.Equal(new[] { OldA101 }, SuggestedPaths(scan));
    }

    [Fact]
    public void TwoFilesWithTheSameName_OneInUseIsChosenOverTheMoreRecent()
    {
        var scan = Resolve(new[] { Window("A-101.pdf - Adobe Acrobat") }, new[] { Recent(OldA101, 60), Recent(A101, 5) }, OldA101);

        Assert.Equal(new[] { OldA101 }, OpenPaths(scan));
    }

    [Fact]
    public void TwoFilesWithTheSameName_OneTheViewerWasStartedWithIsChosenFirst()
    {
        var window = Window("A-101.pdf - Adobe Acrobat", commandLine: $@"""C:\Program Files\Acrobat.exe"" ""{OldA101}""");

        var scan = Resolve(new[] { window }, new[] { Recent(A101, 5) }, A101);

        Assert.Equal(new[] { OldA101, A101 }, OpenPaths(scan));
        Assert.Equal("In use by an open program", scan.Candidates[1].Reason);
    }

    [Fact]
    public void AFullPathInATitle_IsOpen_EvenWithNoOtherClue_AndSettlesItsName()
    {
        var scan = Resolve(new[] { Window($@"{OldA101} - SumatraPDF", "SumatraPDF") }, new[] { Recent(A101, 1) });

        Assert.Equal(new[] { OldA101 }, OpenPaths(scan));
        Assert.Equal(new[] { A101 }, SuggestedPaths(scan));
    }

    [Fact]
    public void AUncPathInATitle_IsOpen()
    {
        var scan = Resolve(new[] { Window($"Revu - [{Spec}]", "Bluebeam Revu") }, Array.Empty<RecentDocument>());

        Assert.Equal(new[] { Spec }, OpenPaths(scan));
    }

    [Fact]
    public void FilesInUse_AreOpen_EvenWhenNoTitleShowsThem_AsBackgroundTabs()
    {
        var scan = Resolve(new[] { Window("A-101.pdf - Adobe Acrobat") }, new[] { Recent(A101, 5), Recent(A102, 6), Recent(Spec, 7) }, A102, Spec);

        Assert.Equal(new[] { A101, A102, Spec }, OpenPaths(scan));
        Assert.Equal("In use by an open program", scan.Candidates.Single(c => c.Path == A102).Reason);
    }

    [Fact]
    public void ACommandLinePathAlone_IsOnlyASuggestion()
    {
        var window = Window("Adobe Acrobat Pro", commandLine: $@"Acrobat.exe /n ""{A101}""");

        var scan = Resolve(new[] { window }, Array.Empty<RecentDocument>());

        Assert.Empty(OpenPaths(scan));
        var suggestion = Assert.Single(scan.Candidates);
        Assert.Equal(A101, suggestion.Path);
        Assert.Equal("Adobe Acrobat was started with it", suggestion.Reason);
    }

    [Fact]
    public void ATitleWithNoKnownPath_IsReportedAsUnmatched_WithItsProgram()
    {
        var scan = Resolve(new[] { Window("Spec sheet.pdf - Personal - Microsoft Edge", "Microsoft Edge") }, Array.Empty<RecentDocument>());

        Assert.Empty(scan.Candidates);
        Assert.Equal(new[] { "Spec sheet.pdf (Microsoft Edge)" }, scan.UnmatchedTitles);
    }

    [Fact]
    public void RecentSuggestions_AreNewestFirst_Deduplicated_AndCapped()
    {
        var recents = Enumerable.Range(0, OpenPdfResolver.MaxRecentSuggestions + 5)
            .Select(i => Recent($@"C:\Jobs\R{i:00}.pdf", i))
            .Append(Recent(@"c:\jobs\r00.PDF", 500))
            .ToArray();

        var scan = Resolve(Array.Empty<ViewerWindow>(), recents);

        Assert.Equal(OpenPdfResolver.MaxRecentSuggestions, scan.Candidates.Count);
        Assert.Equal(@"C:\Jobs\R00.pdf", scan.Candidates[0].Path);
        Assert.Equal(Now, scan.Candidates[0].LastOpenedAt);
        Assert.Equal(@"C:\Jobs\R01.pdf", scan.Candidates[1].Path);
        Assert.All(scan.Candidates, c => Assert.Equal("Recently opened", c.Reason));
    }

    [Fact]
    public void RecentItemsThatAreNotPdfPaths_AreIgnored()
    {
        var scan = Resolve(Array.Empty<ViewerWindow>(), new[] { Recent("relative.pdf", 1), Recent(@"C:\notes.txt", 1) });

        Assert.Empty(scan.Candidates);
    }

    [Fact]
    public void CandidatePaths_ListsEveryPathNamed_Once()
    {
        var evidence = new OpenPdfEvidence(
            new[] { Window($"{Spec} - Revu", commandLine: $@"Revu.exe ""{A101}"" ""{Spec}""") },
            new[] { Recent(A101, 1), Recent(A102, 2) });

        Assert.Equal(new[] { Spec, A101, A102 }, OpenPdfResolver.CandidatePaths(evidence));
    }

    [Theory]
    [InlineData(@"""C:\Program Files\Adobe\Acrobat.exe"" ""C:\Jobs\Tower B\A-101.pdf""", new[] { A101 })]
    [InlineData(@"SumatraPDF.exe -reuse-instance C:\Jobs\A.pdf ""C:\Jobs\Tower B\A-102.pdf""", new[] { @"C:\Jobs\A.pdf", A102 })]
    [InlineData(@"msedge.exe --single-argument file:///C:/Jobs/Tower%20B/A-101.pdf", new[] { A101 })]
    [InlineData(@"msedge.exe file://files/projects/Tower%20B/Spec.pdf", new[] { Spec })]
    [InlineData(@"Acrobat.exe /A ""page=2"" --x=C:\a.pdf", new string[0])]
    [InlineData(@"Acrobat.exe", new string[0])]
    [InlineData(null, new string[0])]
    public void PathsInCommandLine_FindsPdfArguments(string? commandLine, string[] expected)
        => Assert.Equal(expected, OpenPdfResolver.PathsInCommandLine(commandLine));

    [Theory]
    [InlineData("A-101.pdf - Adobe Acrobat Pro", "A-101.pdf")]
    [InlineData("Drawing Set - A-101.pdf - Adobe Acrobat Pro", "A-101.pdf")]
    [InlineData("Revu - [Tower B A-101.pdf]", "Tower B A-101.pdf")]
    [InlineData("Adobe Acrobat Pro", null)]
    [InlineData("Price list.pdfx", null)]
    [InlineData(".pdf", null)]
    public void GuessNameInTitle_TakesTheNameAViewerShows(string title, string? expected)
        => Assert.Equal(expected, OpenPdfResolver.GuessNameInTitle(title));
}
