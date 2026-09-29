using System;
using System.Collections.Generic;
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
/// The Library view model as the workspace's shared query (configurable
/// canvas plan D3, D4, D5, M2): day, week and month periods, relative rules,
/// the query in and out, stale background results dropped, and the selected
/// row kept across refreshes.
/// </summary>
public sealed class LibraryPeriodQueryTests
{
    private const string Pdf = @"C:\Jobs\Acme\A-101.pdf";
    private const string Excel = @"C:\Jobs\Acme\Budget.xlsx";

    // 2026-09-25 local (a Friday); weeks start on Monday, with the invariant culture's month names.
    private static readonly CultureInfo Uk = MondayFirst();

    private static CultureInfo MondayFirst()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.FirstDayOfWeek = DayOfWeek.Monday;
        return culture;
    }
    private readonly ManualTimeProvider _time = new();
    private readonly FakeShell _shell = new();
    private readonly PlacesService _places;
    private readonly SessionStore _sessions;
    private readonly ActivityStore _activity;
    private readonly RecentFilesStore _recentFiles;

    public LibraryPeriodQueryTests()
    {
        _places = new PlacesService(new FakePlacesStorage(), _time);
        _sessions = new SessionStore(new FakePlacesStorage(), _time);
        _activity = new ActivityStore(new FakePlacesStorage(), _time);
        _recentFiles = new RecentFilesStore(new FakePlacesStorage(), _time);
    }

    private LibraryViewModel NewViewModel(IBackgroundWork? work = null)
        => new(_places, _sessions, _activity, _recentFiles, new PlaceLauncher(_places, _shell), _shell, _time, Uk, work);

    /// <summary>Opens of the workbook on Monday 21 Sep and of the PDF today, and a session tagged "markups" saved today.</summary>
    private void Seed()
    {
        _time.UtcNow = _time.UtcNow.AddDays(-4);
        _recentFiles.SetEnabled(true);
        _recentFiles.SetScope(RecentFilesScope.Everywhere);
        _recentFiles.Record(new[] { new RecentDocument(Excel, _time.UtcNow.AddMinutes(1)) }, _ => true);
        _time.UtcNow = _time.UtcNow.AddDays(4);
        _recentFiles.Record(new[] { new RecentDocument(Pdf, _time.UtcNow.AddMinutes(1)) }, _ => true);
        Assert.True(_sessions.TryCreate("Acme", new[] { "markups" }, new[] { Pdf }, out _, out _).Success);
    }

    private static string[] Names(LibraryViewModel vm) => vm.Rows.Select(r => r.Name).OrderBy(n => n).ToArray();

    [Fact]
    public void AWeek_IsChosenFromAnyDayInIt_UsingTheCulturesWeek()
    {
        Seed();
        var vm = NewViewModel();
        vm.SelectionUnit = CalendarSelectionUnit.Week;

        vm.SelectCalendarDate(Today.AddDays(-2));

        Assert.Equal((new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27)), vm.Period);
        Assert.Equal("Used 21–27 Sep 2026", vm.PeriodText);
        Assert.Equal(new[] { "A-101.pdf", "Budget.xlsx" }, Names(vm));
        Assert.All(vm.CalendarWeeks.SelectMany(w => w.Days).Where(d => d.Date is { } date && date >= vm.Period!.Value.From && date <= vm.Period!.Value.To),
            d => Assert.True(d.IsSelected));

        // The same week again clears it.
        vm.SelectCalendarDate(Today);
        Assert.False(vm.HasPeriod);
    }

    [Fact]
    public void AMonth_IsChosenWhole()
    {
        Seed();
        var vm = NewViewModel();
        vm.SelectionUnit = CalendarSelectionUnit.Month;

        vm.SelectCalendarDate(new DateOnly(2026, 8, 12));
        Assert.Equal((new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), vm.Period);
        Assert.True(vm.IsEmpty);

        vm.SelectCalendarDate(Today);
        Assert.Equal("Used 1–30 Sep 2026", vm.PeriodText);
        Assert.Equal(new[] { "A-101.pdf", "Budget.xlsx" }, Names(vm));
    }

    [Fact]
    public void ChoosingAPeriod_ChangesTheRows_NotTheHeat()
    {
        Seed();
        var vm = NewViewModel();
        string LabelOf(DateOnly date) => vm.CalendarWeeks.SelectMany(w => w.Days).Single(d => d.Date == date).Label;
        var before = LabelOf(Today.AddDays(-4));

        vm.SelectCalendarDate(Today);

        Assert.Equal(before, LabelOf(Today.AddDays(-4)));
        Assert.EndsWith("1 file opened", before);
    }

    [Fact]
    public void ThisWeek_StaysRelative_AndSaysSo()
    {
        Seed();
        var vm = NewViewModel();

        vm.SetDateRule(DateRule.ThisWeek());

        Assert.Equal("Used this week (21–27 Sep 2026)", vm.PeriodText);
        Assert.Equal(DateRuleKind.ThisWeek, vm.CurrentQuery.Date.Kind);
        Assert.Null(vm.CurrentQuery.Date.From);
    }

    [Fact]
    public void EmptyPeriods_SayWhy()
    {
        Seed();
        var vm = NewViewModel();

        vm.SelectCalendarDate(Today.AddDays(3));
        Assert.StartsWith("That's still to come", vm.EmptyText);

        vm.SelectCalendarDate(new DateOnly(2026, 1, 5));
        Assert.StartsWith("Nothing was being recorded then", vm.EmptyText);

        vm.SelectCalendarDate(Today.AddDays(-1));
        Assert.StartsWith("Nothing was recorded as used in this period.", vm.EmptyText);

        vm.SelectCalendarDate(Today);
        vm.SearchText = "nothing like this";
        Assert.StartsWith("Nothing in this period matches.", vm.EmptyText);
    }

    [Fact]
    public void TheQuery_GoesOutAndComesBackWhole()
    {
        Seed();
        var vm = NewViewModel();
        vm.SearchText = "acme";
        vm.SelectedKind = LibraryKind.Pdf;
        vm.Source = LibrarySourceFilter.Saved;
        vm.Tag = "markups";
        vm.SelectCalendarDate(Today);

        var query = vm.CurrentQuery;
        Assert.Equal("acme", query.Text);
        Assert.Equal("pdf", query.Kind);
        Assert.Equal("saved", query.Source);
        Assert.Equal("markups", query.Tag);
        Assert.Equal(DateRule.Between(Today, Today).From, query.Date.From);

        var other = NewViewModel();
        var changes = 0;
        other.QueryChanged += () => changes++;
        other.ApplyQuery(query);

        Assert.Equal(0, changes);
        Assert.True(other.CurrentQuery.SameAs(query));
        Assert.Equal(LibraryKind.Pdf, other.SelectedKind);
        Assert.Equal(new[] { "A-101.pdf" }, Names(other));

        other.ApplyQuery(WorkspaceQuery.Default);
        Assert.True(other.CurrentQuery.IsDefault);
        Assert.Equal(2, other.Rows.Count);
    }

    [Fact]
    public void AnUnknownKindOrSource_ReadsAsAll()
    {
        var vm = NewViewModel();

        vm.ApplyQuery(new WorkspaceQuery { Kind = "hologram", Source = "borrowed" });

        Assert.Null(vm.SelectedKind);
        Assert.Equal(LibrarySourceFilter.All, vm.Source);
    }

    [Fact]
    public void EditingTheQuery_RaisesQueryChanged_ButGroupingDoesNot()
    {
        var vm = NewViewModel();
        var changes = 0;
        vm.QueryChanged += () => changes++;

        vm.SearchText = "x";
        vm.SelectedKind = LibraryKind.Folder;
        vm.SetDateRule(DateRule.ThisMonth());
        vm.Grouping = LibraryGrouping.Tag;
        vm.SelectionUnit = CalendarSelectionUnit.Week;

        Assert.Equal(3, changes);
    }

    [Fact]
    public void Tags_ComeFromSessions_AndAnyTagClearsTheFilter()
    {
        Seed();
        var vm = NewViewModel();

        Assert.Equal(new[] { LibraryViewModel.AnyTag, "markups" }, vm.TagChoices);
        vm.Tag = "markups";
        Assert.Equal(new[] { "A-101.pdf" }, Names(vm));
        vm.Tag = LibraryViewModel.AnyTag;
        Assert.Null(vm.Tag);
        Assert.Equal(2, vm.Rows.Count);
    }

    [Fact]
    public void TheSelectedRow_IsKeptByIdentity_AcrossARefresh()
    {
        Seed();
        var vm = NewViewModel();
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "Budget.xlsx");

        vm.Grouping = LibraryGrouping.Tag;
        Assert.Equal("Budget.xlsx", vm.SelectedRow?.Name);

        _recentFiles.Record(new[] { new RecentDocument(@"C:\Jobs\Acme\New.pdf", _time.UtcNow.AddMinutes(9)) }, _ => true);
        vm.Reload();
        Assert.Equal("Budget.xlsx", vm.SelectedRow?.Name);

        // Filtered out for now, then back: the selection comes back with it.
        vm.SelectedKind = LibraryKind.Pdf;
        Assert.Null(vm.SelectedRow);
        vm.SelectedKind = null;
        Assert.Equal("Budget.xlsx", vm.SelectedRow?.Name);
    }

    [Fact]
    public void ARefreshThatChangesNothingShown_KeepsTheSameRows()
    {
        Seed();
        var vm = NewViewModel();
        var before = vm.Rows.ToList();
        var resets = 0;
        vm.Rows.CollectionChanged += (_, _) => resets++;

        vm.Reload();
        vm.Grouping = LibraryGrouping.Type;

        Assert.Equal(0, resets);
        Assert.Equal(before, vm.Rows);

        _recentFiles.Record(new[] { new RecentDocument(Excel, _time.UtcNow.AddMinutes(30)) }, _ => true);
        vm.Reload();
        Assert.NotEqual(0, resets);
        Assert.Equal("Opened 2 times", vm.Rows.Single(r => r.Name == "Budget.xlsx").SourceText);
    }

    [Fact]
    public void AResultOvertakenByANewerQuery_IsDropped()
    {
        Seed();
        var work = new QueuedWork();
        var vm = NewViewModel(work);
        work.RunAll();
        Assert.Equal(2, vm.Rows.Count);

        vm.SearchText = "budget";
        vm.SearchText = "a-101";
        Assert.Equal(2, work.Pending);

        work.RunNewestFirst();

        Assert.Equal(new[] { "A-101.pdf" }, Names(vm));
    }

    [Fact]
    public void CoverageNotes_NameEverySourceThatIsntFullyCounted()
    {
        var vm = NewViewModel();

        Assert.Contains("Folder visits: not counted. No folders are tracked in Recents.", vm.CoverageNotes);
        Assert.Contains("Files opened: not counted. Recent Files is off.", vm.CoverageNotes);
        Assert.DoesNotContain(vm.CoverageNotes, n => n.StartsWith("Sessions"));
    }

    [Fact]
    public void ADayWhoseFilteredFolderVisitsArentKnown_IsShownAsUnknown()
    {
        var old = Today.AddDays(-(ActivityStore.DetailDays + 5));
        _time.UtcNow = new DateTimeOffset(old.ToDateTime(new TimeOnly(1, 0)), TimeSpan.Zero);
        var root = AddRoot(_activity);
        _activity.Record(new[] { Interval(root.RootId, Acme, old, 30, startsVisit: true) });
        _time.UtcNow = new DateTimeOffset(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var vm = NewViewModel();
        vm.Reload();

        vm.SearchText = "acme";

        var cell = vm.CalendarWeeks.SelectMany(w => w.Days).Single(d => d.Date == old);
        Assert.Equal(-1, cell.Intensity);
        Assert.EndsWith("folder visits for this filter no longer known", cell.Label);
    }

    [Theory]
    [InlineData("2026-09-25", "2026-09-25", "Fri 25 Sep 2026")]
    [InlineData("2026-09-21", "2026-09-27", "21–27 Sep 2026")]
    [InlineData("2026-09-28", "2026-10-04", "28 Sep – 4 Oct 2026")]
    [InlineData("2026-12-28", "2027-01-03", "28 Dec 2026 – 3 Jan 2027")]
    public void Periods_ReadNaturally(string from, string to, string expected)
        => Assert.Equal(expected, LibraryViewModel.FormatDays(DateOnly.Parse(from), DateOnly.Parse(to), Uk));

    // ---------------------------------------------------------------
    // Month view (M4)
    // ---------------------------------------------------------------

    [Fact]
    public void TheMonthView_ShowsThisMonth_AsTheYearStripLaysItOut()
    {
        var vm = NewViewModel();

        Assert.Equal(9, vm.CalendarMonth);
        Assert.Equal("September 2026", vm.CalendarMonthLabel);
        var days = vm.CalendarMonthWeeks.SelectMany(w => w.Days).Where(d => d.Date is not null).Select(d => d.Date!.Value).ToList();
        Assert.Equal(30, days.Count);
        Assert.All(days, d => Assert.Equal(9, d.Month));
        Assert.All(vm.CalendarMonthWeeks, w => Assert.Equal(DayOfWeek.Monday, w.StartsOn.DayOfWeek));
        Assert.True(vm.CalendarMonthWeeks.SelectMany(w => w.Days).Single(d => d.Date == Today).IsToday);
        Assert.False(vm.CanShowNextMonth);
    }

    [Fact]
    public void AChosenPeriod_StaysOutlinedInTheMonthView_AndTheMonthFollowsIt()
    {
        var vm = NewViewModel();
        vm.SelectionUnit = CalendarSelectionUnit.Week;

        vm.SelectCalendarDate(new DateOnly(2026, 3, 11));

        Assert.Equal(3, vm.CalendarMonth);
        var selected = vm.CalendarMonthWeeks.SelectMany(w => w.Days).Where(d => d.IsSelected).Select(d => d.Date!.Value).ToList();
        Assert.Equal(Enumerable.Range(9, 7).Select(d => new DateOnly(2026, 3, d)), selected);
    }

    [Fact]
    public void TheMonthArrows_CrossIntoTheYearBefore_ButNotPastThisMonth()
    {
        var vm = NewViewModel();

        Assert.False(vm.ShowCalendarMonth(1));
        for (var i = 0; i < 9; i++)
            Assert.True(vm.ShowCalendarMonth(-1));

        Assert.Equal(2025, vm.CalendarYear);
        Assert.Equal(12, vm.CalendarMonth);
        Assert.Equal("December 2025", vm.CalendarMonthLabel);
        Assert.All(vm.CalendarMonthWeeks.SelectMany(w => w.Days).Where(d => d.Date is not null), d => Assert.Equal(2025, d.Date!.Value.Year));
        Assert.True(vm.ShowCalendarMonth(1));
        Assert.Equal((2026, 1), (vm.CalendarYear, vm.CalendarMonth));
    }

    [Fact]
    public void ChoosingThisYearAgain_NeverShowsAMonthStillToCome()
    {
        var vm = NewViewModel();
        vm.SelectCalendarYear(2025);
        Assert.True(vm.ShowCalendarMonth(3));
        Assert.Equal((2025, 12), (vm.CalendarYear, vm.CalendarMonth));

        vm.SelectCalendarYear(2026);

        Assert.Equal(9, vm.CalendarMonth);
        Assert.False(vm.CanShowNextMonth);
    }


    /// <summary>Holds background work until the test runs it, in whatever order it chooses.</summary>
    private sealed class QueuedWork : IBackgroundWork
    {
        private readonly List<Action> _queue = new();

        public int Pending => _queue.Count;

        public void Run<T>(Func<T> work, Action<T> apply) => _queue.Add(() => apply(work()));

        public void RunAll()
        {
            var items = _queue.ToList();
            _queue.Clear();
            items.ForEach(a => a());
        }

        public void RunNewestFirst()
        {
            var items = Enumerable.Reverse(_queue).ToList();
            _queue.Clear();
            items.ForEach(a => a());
        }
    }
}
