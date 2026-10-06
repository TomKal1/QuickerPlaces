using System;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// History plan §6: recent-files.json version 2 keeps each open for 62 days,
/// a count per kind per day for a year, and per file its last open and open
/// count, as activity.json keeps folders. A version 1 file is converted at load.
/// </summary>
public sealed class RecentFilesSummaryTests
{
    private const string A101 = @"C:\Jobs\Tower B\A-101.pdf";
    private const string A102 = @"C:\Jobs\Tower B\A-102.pdf";
    private const string Budget = @"C:\Jobs\Tower B\Budget.xlsx";

    private readonly ManualTimeProvider _time = new();
    private readonly FakePlacesStorage _storage = new() { StoreFilePath = @"C:\fake\recent-files.json" };

    private DateOnly Today => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    private RecentFilesStore Enabled()
    {
        var store = new RecentFilesStore(_storage, _time);
        Assert.True(store.SetEnabled(true).Saved);
        return store;
    }

    private RecentDocument At(string path, double minutesFromNow) => new(path, _time.UtcNow.AddMinutes(minutesFromNow));

    [Fact]
    public void After62Days_TheOpensGo_ButTheFileStaysListedAndItsDayCounted()
    {
        var store = Enabled();
        store.Record(new[] { At(A101, 1) }, _ => true);
        store.Record(new[] { At(A101, 2) }, _ => true);
        var openedOn = Today;
        var lastOpen = _time.UtcNow.AddMinutes(2);

        _time.Advance(TimeSpan.FromDays(RecentFilesStore.DetailDays + 1));
        Assert.True(store.Flush().Saved);

        Assert.Empty(store.QueryHistory());
        Assert.Empty(store.QueryFiles(openedOn, openedOn));
        var listed = Assert.Single(store.QueryFiles());
        Assert.Equal(2, listed.Opens);
        Assert.Equal(lastOpen, listed.LastOpenedAt);
        Assert.Equal(new RecentFileDayTotal(2, 1), store.QueryDayTotals()[openedOn]);
        Assert.Equal(2, store.QueryDayCounts()[openedOn][DocumentKind.Pdf]);
    }

    [Fact]
    public void AYearAfterItsLastOpen_TheFileAndItsDaysGo()
    {
        var store = Enabled();
        store.Record(new[] { At(A101, 1) }, _ => true);

        _time.Advance(TimeSpan.FromDays(RecentFilesStore.RetentionDays + 1));
        Assert.True(store.Flush().Saved);

        Assert.Empty(store.QueryFiles());
        Assert.Empty(store.QueryDayTotals());
    }

    [Fact]
    public void DayCounts_CountOpensAndDistinctFiles_PerKind()
    {
        var store = Enabled();
        store.Record(new[] { At(A101, 1) }, _ => true);
        store.Record(new[] { At(A101, 2), At(A102, 3), At(Budget, 4) }, _ => true);

        Assert.Equal(new RecentFileDayTotal(4, 3), store.QueryDayTotals()[Today]);
        Assert.Equal(new RecentFileDayTotal(3, 2), store.QueryDayTotals(new[] { DocumentKind.Pdf })[Today]);
        Assert.Equal(1, store.QueryDayCounts()[Today][DocumentKind.Excel]);
    }

    [Fact]
    public void AnOpenNoLaterThanTheLastOne_IsNotCountedAgain_EvenAfterItsOpensAgedOut()
    {
        var store = Enabled();
        var first = At(A101, 1);
        store.Record(new[] { first }, _ => true);
        _time.Advance(TimeSpan.FromDays(RecentFilesStore.DetailDays + 1));
        Assert.True(store.Flush().Saved);

        Assert.Equal(0, store.Record(new[] { first }, _ => true));
        Assert.Equal(1, store.QueryFiles().Single().Opens);
    }

    [Fact]
    public void AVersion1File_IsConverted_WithCountsFromItsYearOfOpens()
    {
        var old = _time.UtcNow.AddDays(-100);
        var recent = _time.UtcNow.AddDays(-3);
        var started = _time.UtcNow.AddDays(-200).ToString("O");
        _storage.ContentsToReturn = $$"""
            {"schemaVersion":1,"settings":{"enabled":true,"trackingStartedAt":"{{started}}","resumedAt":"{{started}}"},
             "files":[{"path":"C:\\Jobs\\Tower B\\A-101.pdf","opens":["{{old:O}}","{{recent:O}}"]}]}
            """;

        var store = new RecentFilesStore(_storage, _time);

        Assert.Single(store.QueryHistory().Single().Opens);
        Assert.Equal(2, store.QueryFiles().Single().Opens);
        var oldDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(old, _time.LocalTimeZone).DateTime);
        Assert.Equal(new RecentFileDayTotal(1, 1), store.QueryDayTotals()[oldDay]);

        Assert.True(store.SetEnabled(false).Saved);
        Assert.Contains("\"schemaVersion\": 2", _storage.LastWritten);
        Assert.Equal(2, new RecentFilesStore(_storage, _time).QueryFiles().Single().Opens);
    }

    [Fact]
    public void TheLibrary_ListsFilesOpenedMonthsAgo_AndShadesTheirDays()
    {
        var places = new PlacesService(new FakePlacesStorage(), _time);
        var files = Enabled();
        files.Record(new[] { At(Budget, 1) }, _ => true);
        var openedOn = Today;
        _time.Advance(TimeSpan.FromDays(100));
        Assert.True(files.Flush().Saved);
        Assert.Empty(files.QueryHistory());
        var shell = new FakeShell();

        var vm = new LibraryViewModel(places, new SessionStore(new FakePlacesStorage(), _time), new ActivityStore(new FakePlacesStorage(), _time),
            files, new PlaceLauncher(places, shell), shell, _time, CultureInfo.InvariantCulture);

        Assert.Contains(vm.Rows, r => r.Name == "Budget.xlsx");
        vm.SelectCalendarYear(openedOn.Year);
        var cell = vm.CalendarWeeks.SelectMany(w => w.Days).Single(c => c.Date == openedOn);
        Assert.True(cell.Intensity > 0);
    }
}
