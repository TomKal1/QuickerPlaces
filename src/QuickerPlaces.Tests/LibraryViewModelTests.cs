using System;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Models.RecentFiles;
using QuickerPlaces.Models.Workspace;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Library;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;
using static QuickerPlaces.Tests.Fakes.ActivityFixtures;

namespace QuickerPlaces.Tests;

/// <summary>
/// The Library's decisions (documents plan §6): kind chips, saved and
/// recent, grouping by type or tag, a day from the year strip, opening,
/// and Recent Files' settings.
/// </summary>
public sealed class LibraryViewModelTests
{
    private const string Pdf = @"C:\Jobs\Acme\A-101.pdf";
    private const string Word = @"C:\Jobs\Acme\Report.docx";
    private const string Excel = @"C:\Jobs\Acme\Budget.xlsx";

    // 2026-09-25 00:00 UTC, in UTC+10: "today" is 2026-09-25 local (ActivityFixtures.Today).
    private readonly ManualTimeProvider _time = new();
    private readonly FakeShell _shell = new();
    private readonly PlacesService _places;
    private readonly SessionStore _sessions;
    private readonly ActivityStore _activity;
    private readonly RecentFilesStore _recentFiles;

    public LibraryViewModelTests()
    {
        _places = new PlacesService(new FakePlacesStorage(), _time);
        _sessions = new SessionStore(new FakePlacesStorage(), _time);
        _activity = new ActivityStore(new FakePlacesStorage(), _time);
        _recentFiles = new RecentFilesStore(new FakePlacesStorage(), _time);
    }

    private LibraryViewModel NewViewModel()
        => new(_places, _sessions, _activity, _recentFiles, new PlaceLauncher(_places, _shell), _shell, _time, CultureInfo.InvariantCulture);

    /// <summary>A saved folder and link, a tagged session of a PDF and Word file, a Recents folder, and a Recent Files workbook.</summary>
    private void Seed()
    {
        Assert.True(_places.TryAdd("Jobs", PlaceType.Folder, TestPaths.Folder("Jobs"), out _, out _).Success);
        Assert.True(_places.TryAdd("Wiki", PlaceType.Url, "https://wiki.example.com", out _, out _).Success);
        Assert.True(_sessions.TryCreate("Acme", new[] { "Acme", "markups" }, new[] { Pdf, Word }, out _, out _).Success);
        var root = AddRoot(_activity);
        _activity.Record(new[] { Interval(root.RootId, Acme, Today, 30, startsVisit: true) });
        _recentFiles.SetEnabled(true);
        _recentFiles.SetScope(RecentFilesScope.Everywhere);
        _recentFiles.Record(new[] { new RecentDocument(Excel, _time.UtcNow.AddMinutes(5)) }, _ => true);
    }

    [Fact]
    public void Everything_IsListedTogether_GroupedByType_WithCountsOnTheKindChips()
    {
        Seed();

        var vm = NewViewModel();

        Assert.Equal(new[] { "All (6)", "Folders (2)", "Links (1)", "PDFs (1)", "Word (1)", "Excel (1)" }, vm.KindFilters.Select(k => k.Label));
        Assert.Equal(new[] { "Folders", "Folders", "Links", "PDFs", "Word", "Excel" }, vm.Rows.Select(r => r.GroupName));
        Assert.False(vm.IsEmpty);
    }

    [Theory]
    [InlineData(LibraryKind.Folder, new[] { "Acme", "Jobs" })]
    [InlineData(LibraryKind.Link, new[] { "Wiki" })]
    [InlineData(LibraryKind.Pdf, new[] { "A-101.pdf" })]
    [InlineData(LibraryKind.Word, new[] { "Report.docx" })]
    [InlineData(LibraryKind.Excel, new[] { "Budget.xlsx" })]
    public void AKindChip_ShowsOnlyThatKind(LibraryKind kind, string[] names)
    {
        Seed();
        var vm = NewViewModel();

        vm.SelectedKind = kind;

        Assert.Equal(names.OrderBy(n => n), vm.Rows.Select(r => r.Name).OrderBy(n => n));
        Assert.True(vm.KindFilters.Single(k => k.Kind == kind).IsSelected);
    }

    [Fact]
    public void SavedAndRecent_SplitBySource()
    {
        Seed();
        var vm = NewViewModel();

        vm.Source = LibrarySourceFilter.Saved;
        Assert.Equal(new[] { "A-101.pdf", "Jobs", "Report.docx", "Wiki" }, vm.Rows.Select(r => r.Name).OrderBy(n => n));
        Assert.True(vm.IsSourceSaved);

        vm.IsSourceRecent = true;
        Assert.Equal(new[] { "Acme", "Budget.xlsx" }, vm.Rows.Select(r => r.Name).OrderBy(n => n));
        Assert.Equal(new[] { "All (2)", "Folders (1)", "Links (0)", "PDFs (0)", "Word (0)", "Excel (1)" }, vm.KindFilters.Select(k => k.Label));
    }

    [Fact]
    public void ByTag_EachTagIsAGroup_AnItemWithTwoTagsIsInBoth_AndUntaggedComeLast()
    {
        Seed();
        var vm = NewViewModel();

        vm.Grouping = LibraryGrouping.Tag;

        Assert.True(vm.IsGroupedByTag);
        var groups = vm.Rows.GroupBy(r => r.GroupName).ToDictionary(g => g.Key, g => g.Select(r => r.Name).OrderBy(n => n).ToArray());
        Assert.Equal(new[] { "Acme", "markups", "No tag" }, vm.Rows.Select(r => r.GroupName).Distinct());
        Assert.Equal(new[] { "A-101.pdf", "Report.docx" }, groups["Acme"]);
        Assert.Equal(new[] { "A-101.pdf", "Report.docx" }, groups["markups"]);
        Assert.Equal(new[] { "Acme", "Budget.xlsx", "Jobs", "Wiki" }, groups["No tag"]);
        Assert.Equal("Acme, markups", vm.Rows.First(r => r.Name == "A-101.pdf").TagsText);
    }

    [Fact]
    public void GroupByFolderLevel_PutsItemsUnderTheirDepth_AndTheRestLast()
    {
        Seed();
        var vm = NewViewModel();

        vm.Grouping = LibraryGrouping.Level;

        Assert.True(vm.IsGroupedByLevel);
        Assert.Equal(new[] { TrackedFolderPaths.LevelLabel(1), TrackedFolderPaths.NotTracked }, vm.Rows.Select(r => r.GroupName).Distinct());
        Assert.Equal(new[] { "A-101.pdf", "Acme", "Budget.xlsx", "Report.docx" },
            vm.Rows.Where(r => r.GroupName == TrackedFolderPaths.LevelLabel(1)).Select(r => r.Name).OrderBy(n => n));
        Assert.Equal(new[] { "Jobs", "Wiki" }, vm.Rows.Where(r => r.GroupName == TrackedFolderPaths.NotTracked).Select(r => r.Name).OrderBy(n => n));
    }

    [Fact]
    public void Search_NarrowsEverything_IncludingTheChipCounts()
    {
        Seed();
        var vm = NewViewModel();

        vm.SearchText = "markups";

        Assert.Equal(new[] { "A-101.pdf", "Report.docx" }, vm.Rows.Select(r => r.Name).OrderBy(n => n));
        Assert.Equal("All (2)", vm.KindFilters[0].Label);
    }

    [Fact]
    public void TheYearStrip_CountsFolderVisitsFileOpensAndSessions_AndFollowsTheKindChip()
    {
        Seed();
        var vm = NewViewModel();

        var today = vm.CalendarWeeks.SelectMany(w => w.Days).Single(d => d.Date == Today);
        Assert.EndsWith("1 folder visit · 1 file opened · 1 session saved", today.Label);

        vm.SelectedKind = LibraryKind.Folder;
        today = vm.CalendarWeeks.SelectMany(w => w.Days).Single(d => d.Date == Today);
        Assert.EndsWith("1 folder visit", today.Label);
        Assert.Equal("Folder visits from Recents", vm.CalendarCaption);

        // The session holds a PDF and a Word file, so its save counts for those kinds, not for Excel.
        vm.SelectedKind = LibraryKind.Excel;
        today = vm.CalendarWeeks.SelectMany(w => w.Days).Single(d => d.Date == Today);
        Assert.EndsWith("1 file opened", today.Label);

        vm.SelectedKind = LibraryKind.Pdf;
        today = vm.CalendarWeeks.SelectMany(w => w.Days).Single(d => d.Date == Today);
        Assert.EndsWith("1 session saved", today.Label);
    }

    [Fact]
    public void ChoosingADay_ListsOnlyWhatWasUsedThatDay_AndChoosingItAgainClearsIt()
    {
        Seed();
        var vm = NewViewModel();
        var yesterday = Today.AddDays(-1);

        vm.SelectCalendarDate(yesterday);
        Assert.True(vm.IsEmpty);
        Assert.Equal("Used on Thu 24 Sep 2026", vm.PeriodText);

        vm.SelectCalendarDate(Today);
        Assert.Equal(new[] { "A-101.pdf", "Acme", "Budget.xlsx", "Report.docx" }, vm.Rows.Select(r => r.Name).OrderBy(n => n));

        vm.SelectCalendarDate(Today);
        Assert.False(vm.HasPeriod);
        Assert.Equal(6, vm.Rows.Count);
    }

    [Fact]
    public void OpeningASavedPlace_CountsAsAPlaceOpen_AndIsReported()
    {
        Seed();
        _shell.ExistingDirectories.Add(TestPaths.Folder("Jobs"));
        var vm = NewViewModel();
        Place? opened = null;
        vm.PlaceOpened += (place, _) => opened = place;

        vm.Open(vm.Rows.Single(r => r.Name == "Jobs"));

        Assert.Equal(new[] { TestPaths.Folder("Jobs") }, _shell.Opened);
        Assert.Equal("Jobs", opened!.Alias);
        Assert.Equal(1, opened.OpenCount);
        Assert.Equal("Opened \"Jobs\".", vm.StatusMessage);
    }

    [Fact]
    public void OpeningAFile_IsCheckedFirst_AndCountsAsNoPlaceOpen()
    {
        Seed();
        var vm = NewViewModel();
        var row = vm.Rows.Single(r => r.Name == "A-101.pdf");

        vm.Open(row);
        Assert.Empty(_shell.Opened);
        Assert.Contains("couldn't be found", vm.ErrorMessage);

        _shell.ExistingFiles.Add(Pdf);
        vm.Open(row);
        Assert.Equal(new[] { Pdf }, _shell.Opened);
        Assert.False(vm.HasError);
        Assert.All(_places.Places, p => Assert.Equal(0, p.OpenCount));
    }

    [Fact]
    public void RecentFilesSettings_AreChangedHere_AndSaved()
    {
        var vm = NewViewModel();
        Assert.False(vm.RecentFilesEnabled);
        Assert.StartsWith("Recent Files is off.", vm.RecentFilesStatus);

        vm.RecentFilesEnabled = true;
        Assert.True(_recentFiles.IsTracking);
        Assert.Contains("none are tracked yet", vm.RecentFilesStatus);

        vm.TrackWord = false;
        vm.TrackEverywhere = true;
        Assert.Equal(new[] { DocumentKind.Pdf, DocumentKind.Excel }, _recentFiles.Settings.Kinds);
        Assert.Equal("Recording PDFs, Excel workbooks you open anywhere.", vm.RecentFilesStatus);

        vm.TrackPdf = false;
        vm.TrackExcel = false;
        Assert.Equal("Choose at least one kind of file to track, or turn Recent Files off.", vm.ErrorMessage);
        Assert.True(vm.TrackExcel);
    }

    [Fact]
    public void ForgetAndClear_RemoveOnlyRecentFilesHistory()
    {
        Seed();
        _recentFiles.Record(new[] { new RecentDocument(Pdf, _time.UtcNow.AddMinutes(6)) }, _ => true);
        var vm = NewViewModel();

        var pdf = vm.Rows.Single(r => r.Name == "A-101.pdf");
        Assert.True(pdf.CanForget);
        Assert.False(vm.Rows.Single(r => r.Name == "Report.docx").CanForget);
        vm.Forget(pdf);
        Assert.Contains("A-101.pdf", vm.Rows.Select(r => r.Name));
        Assert.False(vm.Rows.Single(r => r.Name == "A-101.pdf").Item.IsRecent);

        vm.ClearRecentFiles();
        Assert.DoesNotContain("Budget.xlsx", vm.Rows.Select(r => r.Name));
        Assert.Single(_sessions.Sessions);
    }

    [Fact]
    public void Empty_SaysWhereThingsComeFrom()
    {
        var vm = NewViewModel();

        Assert.True(vm.IsEmpty);
        Assert.StartsWith("Nothing here yet.", vm.EmptyText);
    }

    [Fact]
    public void FolderRows_ShowVisitsAndTime_AndOfferAddAsPlaceUnlessSaved()
    {
        Seed();

        var vm = NewViewModel();
        var acme = vm.Rows.Single(r => r.Name == "Acme");
        var jobs = vm.Rows.Single(r => r.Name == "Jobs");
        var pdf = vm.Rows.Single(r => r.Name == "A-101.pdf");

        Assert.Equal(("1", ActivityFormat.Duration(TimeSpan.FromSeconds(30))), (acme.VisitsText, acme.TimeText));
        Assert.Equal(("", ""), (pdf.VisitsText, pdf.TimeText));
        Assert.True(acme.CanAddAsPlace);
        Assert.False(jobs.CanAddAsPlace);
        Assert.False(pdf.CanAddAsPlace);
    }

    [Fact]
    public void ATrackedFolderChip_ScopesTheList_AndAgainClearsIt()
    {
        Seed();
        var vm = NewViewModel();
        var root = _activity.Roots.Single();
        var changed = 0;
        vm.QueryChanged += () => changed++;

        var chip = Assert.Single(vm.TrackedRootChips);
        Assert.Equal((root.RootId, root.Path, false), (chip.RootId, chip.Path, chip.IsSelected));

        vm.ToggleRootScope(root.RootId);
        Assert.Equal(new[] { "A-101.pdf", "Acme", "Budget.xlsx", "Report.docx" }, vm.Rows.Select(r => r.Name).OrderBy(n => n));
        Assert.True(vm.TrackedRootChips.Single().IsSelected);
        Assert.Equal(root.RootId, vm.CurrentQuery.Root);
        Assert.Equal(1, changed);

        vm.ToggleRootScope(root.RootId);
        Assert.Equal(6, vm.Rows.Count);
        Assert.Null(vm.CurrentQuery.Root);
    }

    [Fact]
    public void AScopeForATrackedFolderThatIsGone_ScopesNothing()
    {
        Seed();
        var vm = NewViewModel();

        vm.ApplyQuery(new WorkspaceQuery { Root = "deleted-root" });

        Assert.Equal(6, vm.Rows.Count);
        Assert.False(vm.TrackedRootChips.Single().IsSelected);
    }
}
