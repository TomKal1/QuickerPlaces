using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Library;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;
using static QuickerPlaces.Tests.Fakes.ActivityFixtures;

namespace QuickerPlaces.Tests;

public sealed class CalendarPresentationTests
{
    private readonly ManualTimeProvider _time = new();
    private readonly FakeShell _shell = new();
    private readonly PlacesService _places;
    private readonly SessionStore _sessions;
    private readonly ActivityStore _activity;
    private readonly RecentFilesStore _files;

    public CalendarPresentationTests()
    {
        _places = new(new FakePlacesStorage(), _time);
        _sessions = new(new FakePlacesStorage(), _time);
        _activity = new(new FakePlacesStorage(), _time);
        _files = new(new FakePlacesStorage(), _time);
        Assert.True(_sessions.TryCreate("Acme", Array.Empty<string>(), new[] { @"C:\Jobs\Acme\Report.pdf" }, out _, out _).Success);
    }

    private LibraryViewModel NewViewModel(CultureInfo? culture = null)
        => new(_places, _sessions, _activity, _files, new PlaceLauncher(_places, _shell), _shell,
            _time, culture ?? CultureInfo.InvariantCulture);

    [Fact]
    public void SelectingAndClearingADay_KeepWeekContainersAndUnchangedCells()
    {
        var vm = NewViewModel();
        var weeks = vm.CalendarWeeks.ToArray();
        var monthWeeks = vm.CalendarMonthWeeks.ToArray();
        var cells = weeks.SelectMany(w => w.Days).ToArray();
        var events = new List<NotifyCollectionChangedEventArgs>();
        var changedWeek = weeks.Single(w => w.Days.Any(d => d.Date == Today));
        var days = Assert.IsType<ObservableCollection<ActivityCalendarCell>>(changedWeek.Days);
        days.CollectionChanged += (_, e) => events.Add(e);
        var outerChanges = 0;
        vm.CalendarWeeks.CollectionChanged += (_, _) => outerChanges++;
        vm.CalendarMonthWeeks.CollectionChanged += (_, _) => outerChanges++;

        vm.SelectCalendarDate(Today);

        Assert.Equal(0, outerChanges);
        Assert.Equal(NotifyCollectionChangedAction.Replace, Assert.Single(events).Action);
        for (var i = 0; i < weeks.Length; i++) Assert.Same(weeks[i], vm.CalendarWeeks[i]);
        for (var i = 0; i < monthWeeks.Length; i++) Assert.Same(monthWeeks[i], vm.CalendarMonthWeeks[i]);
        var selected = vm.CalendarWeeks.SelectMany(w => w.Days).ToArray();
        for (var i = 0; i < cells.Length; i++)
        {
            Assert.Equal(cells[i].Label, selected[i].Label);
            Assert.Equal(cells[i].Intensity, selected[i].Intensity);
            Assert.Equal(cells[i].IsToday, selected[i].IsToday);
            if (cells[i].Date == Today) Assert.True(selected[i].IsSelected);
            else Assert.Same(cells[i], selected[i]);
        }
        AssertCalendar(vm, CultureInfo.InvariantCulture);

        events.Clear();
        vm.ClearPeriod();
        Assert.Equal(0, outerChanges);
        Assert.Equal(NotifyCollectionChangedAction.Replace, Assert.Single(events).Action);
        Assert.DoesNotContain(vm.CalendarWeeks.SelectMany(w => w.Days), d => d.IsSelected);
        AssertCalendar(vm, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void ChangedHeat_ReplacesOnlyChangedCells_AndKeepsMarkersLabelsAndWidth()
    {
        var vm = NewViewModel();
        var weeks = vm.CalendarWeeks.ToArray();
        var cells = weeks.SelectMany(w => w.Days).ToArray();
        var markers = vm.CalendarMonthMarkers.ToArray();
        var labels = vm.CalendarWeekdayLabels;
        var presentationChanges = new List<string?>();
        vm.PropertyChanged += (_, e) => presentationChanges.Add(e.PropertyName);
        var markerChanges = 0;
        vm.CalendarMonthMarkers.CollectionChanged += (_, _) => markerChanges++;

        vm.SearchText = "missing";

        for (var i = 0; i < weeks.Length; i++) Assert.Same(weeks[i], vm.CalendarWeeks[i]);
        var filtered = vm.CalendarWeeks.SelectMany(w => w.Days).ToArray();
        for (var i = 0; i < cells.Length; i++)
            if (cells[i].Date != Today) Assert.Same(cells[i], filtered[i]);
        Assert.EndsWith("no activity", filtered.Single(d => d.Date == Today).Label);
        Assert.Equal(0, markerChanges);
        for (var i = 0; i < markers.Length; i++) Assert.Same(markers[i], vm.CalendarMonthMarkers[i]);
        Assert.Same(labels, vm.CalendarWeekdayLabels);
        Assert.DoesNotContain(nameof(LibraryViewModel.CalendarStripWidth), presentationChanges);
        Assert.DoesNotContain(nameof(LibraryViewModel.CalendarWeekdayLabels), presentationChanges);
        AssertCalendar(vm, CultureInfo.InvariantCulture, new LibraryFilter(Text: "missing"));

        vm.SearchText = "";
        AssertCalendar(vm, CultureInfo.InvariantCulture);
    }

    [Theory]
    [InlineData("en-US", 2024)]
    [InlineData("en-GB", 2025)]
    [InlineData("fr-FR", 2026)]
    public void YearMonthAndSelectionChanges_MatchFreshCalendarIncludingBlankAndFutureDays(string cultureName, int year)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var vm = NewViewModel(culture);
        vm.SelectCalendarYear(year);
        AssertCalendar(vm, culture);
        vm.ShowCalendarMonth(1);
        AssertCalendar(vm, culture);
        vm.SelectionUnit = CalendarSelectionUnit.Week;
        vm.SelectCalendarDate(new DateOnly(year, 2, 28));
        AssertCalendar(vm, culture);
        vm.SelectionUnit = CalendarSelectionUnit.Month;
        AssertCalendar(vm, culture);
        vm.ClearPeriod();
        vm.SelectCalendarYear(2027);
        AssertCalendar(vm, culture);
        vm.SelectCalendarYear(2026);
        AssertCalendar(vm, culture);
    }

    [Fact]
    public void EquivalentKindChips_AreKeptAcrossSearch_AndSelectionChangesDoNotResetTheCollection()
    {
        var vm = NewViewModel();
        var filters = vm.KindFilters.ToArray();
        var changes = new List<NotifyCollectionChangedEventArgs>();
        vm.KindFilters.CollectionChanged += (_, e) => changes.Add(e);

        vm.SearchText = "Acme";

        Assert.Empty(changes);
        for (var i = 0; i < filters.Length; i++) Assert.Same(filters[i], vm.KindFilters[i]);
        vm.SelectedKind = LibraryKind.Pdf;
        Assert.Equal(2, changes.Count);
        Assert.All(changes, e => Assert.Equal(NotifyCollectionChangedAction.Replace, e.Action));
        Assert.True(vm.KindFilters.Single(f => f.Kind == LibraryKind.Pdf).IsSelected);
        Assert.False(vm.KindFilters[0].IsSelected);
    }

    [Fact]
    public void ReloadAcrossMidnight_UpdatesTodaysRingAndHeatWithoutResettingWeeks()
    {
        var vm = NewViewModel();
        var reset = false;
        vm.CalendarWeeks.CollectionChanged += (_, e) => reset |= e.Action == NotifyCollectionChangedAction.Reset;
        _time.Advance(TimeSpan.FromDays(1));

        vm.Reload();

        Assert.False(reset);
        AssertCalendar(vm, CultureInfo.InvariantCulture);
        Assert.Equal(Today.AddDays(1), vm.CalendarWeeks.SelectMany(w => w.Days).Single(d => d.IsToday).Date);
    }

    private void AssertCalendar(LibraryViewModel vm, CultureInfo culture, LibraryFilter? filter = null)
    {
        var snapshot = LibrarySnapshot.Capture(_places, _sessions, _activity, _files, _time);
        var query = LibraryQueryEngine.Run(snapshot, filter ?? LibraryFilter.None, vm.Period, culture);
        var days = query.Heat.ToDictionary(d => d.Key, d => new CalendarDay(d.Value.Weight, d.Value.Summary,
            d.Value.FoldersUnknown && d.Value.Weight == 0));
        var expected = ActivityCalendar.BuildYear(days, query.TrackingStartedOn, snapshot.Today, vm.CalendarYear,
            culture, vm.Period?.From, vm.Period?.To);
        Assert.Equal(expected.Weeks.Select(w => w.StartsOn), vm.CalendarWeeks.Select(w => w.StartsOn));
        Assert.Equal(expected.Weeks.SelectMany(w => w.Days), vm.CalendarWeeks.SelectMany(w => w.Days));
        Assert.Equal(expected.Months.Single(m => m.Month == vm.CalendarMonth).Weeks.SelectMany(w => w.Days),
            vm.CalendarMonthWeeks.SelectMany(w => w.Days));
        Assert.Equal(expected.MonthMarkers, vm.CalendarMonthMarkers);
        Assert.Equal(expected.WeekdayLabels, vm.CalendarWeekdayLabels);
        Assert.Equal(expected.StripWidth, vm.CalendarStripWidth);
    }
}
