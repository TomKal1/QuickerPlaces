using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Services.Documents;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Which PDFs count as open (sessions plan §4, D6–D9): file names in window
/// titles matched to paths, paths in titles and on command lines, files in
/// use, and Recent Items as suggestions.
/// </summary>
public sealed class OpenDocumentResolverTests
{
    private const string A101 = @"C:\Jobs\Tower B\A-101.pdf";
    private const string A102 = @"C:\Jobs\Tower B\A-102.pdf";
    private const string OldA101 = @"C:\Jobs\Tower A\A-101.pdf";
    private const string Spec = @"\\files\projects\Tower B\Spec.pdf";

    private static readonly DateTimeOffset Now = new(2026, 9, 28, 9, 0, 0, TimeSpan.Zero);

    private static RecentDocument Recent(string path, int minutesAgo) => new(path, Now.AddMinutes(-minutesAgo));

    private static ViewerWindow Window(string title, string app = "Adobe Acrobat", string? commandLine = null) => new(title, app, commandLine);

    private static OpenDocumentScan Resolve(ViewerWindow[] windows, RecentDocument[] recents, params string[] inUse)
        => OpenDocumentResolver.Resolve(new OpenDocumentEvidence(windows, recents), inUse);

    private static OpenDocumentScan ResolveHeld(ViewerWindow[] windows, RecentDocument[] recents, params HeldFile[] held)
        => OpenDocumentResolver.Resolve(new OpenDocumentEvidence(windows, recents) { HeldFiles = held }, Array.Empty<string>());

    private static string[] OpenPaths(OpenDocumentScan scan) => scan.Candidates.Where(c => c.IsLikelyOpen).Select(c => c.Path).ToArray();

    private static string[] SuggestedPaths(OpenDocumentScan scan) => scan.Candidates.Where(c => !c.IsLikelyOpen).Select(c => c.Path).ToArray();

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
        var recents = Enumerable.Range(0, OpenDocumentResolver.MaxRecentSuggestions + 5)
            .Select(i => Recent($@"C:\Jobs\R{i:00}.pdf", i))
            .Append(Recent(@"c:\jobs\r00.PDF", 500))
            .ToArray();

        var scan = Resolve(Array.Empty<ViewerWindow>(), recents);

        Assert.Equal(OpenDocumentResolver.MaxRecentSuggestions, scan.Candidates.Count);
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
        var evidence = new OpenDocumentEvidence(
            new[] { Window($"{Spec} - Revu", commandLine: $@"Revu.exe ""{A101}"" ""{Spec}""") },
            new[] { Recent(A101, 1), Recent(A102, 2) });

        Assert.Equal(new[] { Spec, A101, A102 }, OpenDocumentResolver.CandidatePaths(evidence));
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
        => Assert.Equal(expected, OpenDocumentResolver.PathsInCommandLine(commandLine));

    [Theory]
    [InlineData("A-101.pdf - Adobe Acrobat Pro", "A-101.pdf")]
    [InlineData("Drawing Set - A-101.pdf - Adobe Acrobat Pro", "A-101.pdf")]
    [InlineData("Revu - [Tower B A-101.pdf]", "Tower B A-101.pdf")]
    [InlineData("Adobe Acrobat Pro", null)]
    [InlineData("Price list.pdfx", null)]
    [InlineData(".pdf", null)]
    public void GuessNameInTitle_TakesTheNameAViewerShows(string title, string? expected)
        => Assert.Equal(expected, OpenDocumentResolver.GuessNameInTitle(title));

    private const string Report = @"C:\Jobs\Tower B\Report.docx";
    private const string OldReport = @"C:\Jobs\Tower A\Report.docx";
    private const string Budget = @"C:\Jobs\Tower B\Budget.xlsx";

    [Theory]
    [InlineData("Report.docx - Word")]
    [InlineData("Report - Word")]
    [InlineData("Report [Read-Only] - Word")]
    [InlineData("Report  -  Compatibility Mode - Word")]
    public void AWordTitle_WithOrWithoutItsExtension_IsMatched(string title)
    {
        var scan = Resolve(new[] { new ViewerWindow(title, "Microsoft Word", null, DocumentKind.Word) }, new[] { Recent(Report, 5), Recent(Budget, 6) });

        Assert.Equal(new[] { Report }, OpenPaths(scan));
        Assert.Equal("Open in Microsoft Word", scan.Candidates[0].Reason);
    }

    [Fact]
    public void AnExtensionlessName_OnlyCountsForThatProgramsKind()
    {
        const string reportPdf = @"C:\Jobs\Report.pdf";

        var scan = Resolve(new[] { new ViewerWindow("Report - Word", "Microsoft Word", null, DocumentKind.Word) }, new[] { Recent(reportPdf, 1) });

        Assert.Empty(OpenPaths(scan));
        Assert.Equal(new[] { "Report (Microsoft Word)" }, scan.UnmatchedTitles);
    }

    [Fact]
    public void AnExtensionlessName_InANonOfficeWindow_IsNotMatched()
    {
        var scan = Resolve(new[] { Window("Report - Notepad", "Notepad") }, new[] { Recent(Report, 1) });

        Assert.Empty(OpenPaths(scan));
        Assert.Empty(scan.UnmatchedTitles);
    }

    [Fact]
    public void TwoWordFilesWithTheSameName_TheOneInUseWins()
    {
        var scan = Resolve(new[] { new ViewerWindow("Report - Word", "Microsoft Word", null, DocumentKind.Word) },
            new[] { Recent(Report, 1), Recent(OldReport, 60) }, OldReport);

        Assert.Equal(new[] { OldReport }, OpenPaths(scan));
    }

    [Fact]
    public void ExcelWorkbooksInUse_AreOpen_AndAnUnsavedBookIsReported()
    {
        var scan = Resolve(new[] { new ViewerWindow("Book1 - Excel", "Microsoft Excel", null, DocumentKind.Excel) },
            new[] { Recent(Budget, 3) }, Budget);

        Assert.Equal(new[] { Budget }, OpenPaths(scan));
        Assert.Equal(new[] { "Book1 (Microsoft Excel)" }, scan.UnmatchedTitles);
    }

    [Fact]
    public void WordsStartScreen_IsNotReported()
    {
        var scan = Resolve(new[] { new ViewerWindow("Word", "Microsoft Word", null, DocumentKind.Word) }, Array.Empty<RecentDocument>());

        Assert.Empty(scan.UnmatchedTitles);
    }

    [Theory]
    [InlineData(@"WINWORD.EXE /n ""C:\Jobs\Tower B\Report.docx""", new[] { Report })]
    [InlineData(@"EXCEL.EXE ""C:\Jobs\Tower B\Budget.xlsx"" /e", new[] { Budget })]
    public void PathsInCommandLine_FindsOfficeArguments(string commandLine, string[] expected)
        => Assert.Equal(expected, OpenDocumentResolver.PathsInCommandLine(commandLine));

    [Theory]
    [InlineData("Budget.xlsx - Excel", "Budget.xlsx")]
    [InlineData("Minutes v2.docx - Word", "Minutes v2.docx")]
    public void GuessNameInTitle_TakesOfficeNamesWithExtensions(string title, string expected)
        => Assert.Equal(expected, OpenDocumentResolver.GuessNameInTitle(title));

    [Fact]
    public void AHeldFile_IsOpen_WithoutATitleOrARecentItem()
    {
        var scan = ResolveHeld(Array.Empty<ViewerWindow>(), Array.Empty<RecentDocument>(), new HeldFile(Spec, "Bluebeam Revu"));

        var candidate = Assert.Single(scan.Candidates);
        Assert.Equal(Spec, candidate.Path);
        Assert.True(candidate.IsLikelyOpen);
        Assert.Equal("Open in Bluebeam Revu", candidate.Reason);
        Assert.Null(candidate.LastOpenedAt);
    }

    [Fact]
    public void EveryHeldTab_IsOpen_AndTheTitlesNameMatchesAHeldPath_WithoutRecentItems()
    {
        var scan = ResolveHeld(new[] { Window("Bluebeam Revu x64 - [A-102.pdf]", "Bluebeam Revu") }, Array.Empty<RecentDocument>(),
            new HeldFile(A101, "Bluebeam Revu"), new HeldFile(A102, "Bluebeam Revu"));

        Assert.Equal(new[] { A102, A101 }, OpenPaths(scan));
        Assert.Empty(SuggestedPaths(scan));
        Assert.Empty(scan.UnmatchedTitles);
    }

    [Fact]
    public void AHeldFile_WinsOverAMoreRecentFileWithTheSameName()
    {
        var scan = ResolveHeld(new[] { Window("Bluebeam Revu x64 - [A-101.pdf]", "Bluebeam Revu") }, new[] { Recent(OldA101, 5) },
            new HeldFile(A101, "Bluebeam Revu"));

        Assert.Equal(new[] { A101 }, OpenPaths(scan));
        Assert.Equal(new[] { OldA101 }, SuggestedPaths(scan));
    }

    [Fact]
    public void AHeldFileAlsoInRecentItems_IsListedOnce_WithWhenItWasOpened()
    {
        var scan = ResolveHeld(Array.Empty<ViewerWindow>(), new[] { Recent(A101, 5) }, new HeldFile(A101, "Bluebeam Revu"));

        var candidate = Assert.Single(scan.Candidates);
        Assert.True(candidate.IsLikelyOpen);
        Assert.Equal(Now.AddMinutes(-5), candidate.LastOpenedAt);
    }

    [Fact]
    public void CandidatePaths_ListHeldFilesFirst()
    {
        var evidence = new OpenDocumentEvidence(new[] { Window(@"C:\Jobs\Tower B\A-102.pdf - Viewer", "Viewer") }, new[] { Recent(A101, 5) })
        {
            HeldFiles = new[] { new HeldFile(Spec, "Bluebeam Revu") },
        };

        Assert.Equal(new[] { Spec, A102, A101 }, OpenDocumentResolver.CandidatePaths(evidence));
    }

    private static readonly Dictionary<string, string> Drives = new() { ["P:"] = @"\\files\projects" };

    private const string ShareA101 = @"\\files\projects\A-101.pdf";
    private const string DriveA101 = @"P:\A-101.pdf";

    [Fact]
    public void AFileSpelledWithAShareInACommandLine_AndAMappedDriveWhenHeld_IsListedOnce()
    {
        var revu = new ViewerWindow("Bluebeam Revu x64 - [A-101.pdf]", "Bluebeam Revu",
            @"""C:\Program Files\Bluebeam Software\Bluebeam Revu\21\Revu\Revu.exe"" """ + ShareA101 + @"""");
        var evidence = new OpenDocumentEvidence(new[] { revu }, Array.Empty<RecentDocument>())
        {
            HeldFiles = new[] { new HeldFile(DriveA101, "Bluebeam Revu") },
            MappedDrives = Drives,
        };

        var candidate = Assert.Single(OpenDocumentResolver.Resolve(evidence, Array.Empty<string>()).Candidates);
        Assert.Equal(DriveA101, candidate.Path);
        Assert.True(candidate.IsLikelyOpen);
        Assert.Equal("Open in Bluebeam Revu", candidate.Reason);
    }

    [Fact]
    public void AHeldFileOnAMappedDrive_AndItsShareSpellingInRecentItems_IsListedOnce_WithWhenItWasOpened()
    {
        var evidence = new OpenDocumentEvidence(Array.Empty<ViewerWindow>(), new[] { Recent(ShareA101, 5) })
        {
            HeldFiles = new[] { new HeldFile(DriveA101, "Bluebeam Revu") },
            MappedDrives = Drives,
        };

        var candidate = Assert.Single(OpenDocumentResolver.Resolve(evidence, Array.Empty<string>()).Candidates);
        Assert.Equal(DriveA101, candidate.Path);
        Assert.True(candidate.IsLikelyOpen);
        Assert.Equal(Now.AddMinutes(-5), candidate.LastOpenedAt);
    }

    [Fact]
    public void AFileSpelledWithAShareInRecentItemsAndInUse_AndAMappedDriveWhenHeld_IsListedOnce()
    {
        var evidence = new OpenDocumentEvidence(Array.Empty<ViewerWindow>(), new[] { Recent(ShareA101, 5) })
        {
            HeldFiles = new[] { new HeldFile(DriveA101, "Bluebeam Revu") },
            MappedDrives = Drives,
        };

        var candidate = Assert.Single(OpenDocumentResolver.Resolve(evidence, new[] { ShareA101 }).Candidates);
        Assert.Equal(DriveA101, candidate.Path);
        Assert.True(candidate.IsLikelyOpen);
        Assert.Equal("Open in Bluebeam Revu", candidate.Reason);
        Assert.Equal(Now.AddMinutes(-5), candidate.LastOpenedAt);
    }

    [Fact]
    public void AFileHeldByTwoPrograms_IsListedOnce_UnderTheFirst()
    {
        var scan = ResolveHeld(Array.Empty<ViewerWindow>(), Array.Empty<RecentDocument>(),
            new HeldFile(A101, "Bluebeam Revu"), new HeldFile(A101, "Adobe Acrobat"));

        var candidate = Assert.Single(scan.Candidates);
        Assert.Equal("Open in Bluebeam Revu", candidate.Reason);
    }

    [Fact]
    public void AWordTitleWithoutExtension_PrefersTheHeldDocumentOverANewerRecentOne()
    {
        const string held = @"C:\Jobs\Report.docx";
        const string other = @"C:\Old\Report.docx";
        var scan = ResolveHeld(new[] { new ViewerWindow("Report - Word", "Microsoft Word", null, DocumentKind.Word) },
            new[] { Recent(other, 1) }, new HeldFile(held, "Microsoft Word"));

        Assert.Equal(new[] { held }, OpenPaths(scan));
        Assert.Equal(new[] { other }, SuggestedPaths(scan));
    }

    [Fact]
    public void AFileBothHeldAndInUse_GivesTheProgramAsTheReason()
    {
        var evidence = new OpenDocumentEvidence(Array.Empty<ViewerWindow>(), Array.Empty<RecentDocument>())
        {
            HeldFiles = new[] { new HeldFile(A101, "Bluebeam Revu") },
        };

        var candidate = Assert.Single(OpenDocumentResolver.Resolve(evidence, new[] { A101 }).Candidates);
        Assert.Equal("Open in Bluebeam Revu", candidate.Reason);
    }

    [Fact]
    public void EmptyEvidence_ResolvesWithoutThrowing()
    {
        // A file in use is only listed when some clue names it, so the empty evidence lists nothing; it must not throw.
        var scan = OpenDocumentResolver.Resolve(OpenDocumentEvidence.Empty, new[] { A101 });

        Assert.Empty(scan.Candidates);
        Assert.Empty(scan.UnmatchedTitles);
        Assert.Empty(OpenDocumentEvidence.Empty.MappedDrives);
    }
}
