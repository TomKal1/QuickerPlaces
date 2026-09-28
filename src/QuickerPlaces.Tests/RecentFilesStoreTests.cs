using System;
using System.IO;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Models.RecentFiles;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// recent-files.json (documents plan §5): opt-in, recording each open once,
/// kinds and scope, retention, queries, and loading.
/// </summary>
public sealed class RecentFilesStoreTests
{
    private const string A101 = @"C:\Jobs\Tower B\A-101.pdf";
    private const string Report = @"C:\Jobs\Tower B\Report.docx";
    private const string Budget = @"C:\Jobs\Tower B\Budget.xlsx";
    private const string Elsewhere = @"D:\Personal\Taxes.xlsx";

    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero));
    private readonly FakePlacesStorage _storage = new() { StoreFilePath = @"C:\fake\recent-files.json" };

    private RecentFilesStore NewStore() => new(_storage, _time);

    private RecentDocument At(string path, double minutesFromNow) => new(path, _time.UtcNow.AddMinutes(minutesFromNow));

    private static bool Anywhere(string _) => true;

    private RecentFilesStore Enabled()
    {
        var store = NewStore();
        Assert.True(store.SetEnabled(true).Saved);
        return store;
    }

    [Fact]
    public void OffByDefault_AndRecordsNothingUntilTurnedOn()
    {
        var store = NewStore();

        Assert.False(store.Settings.Enabled);
        Assert.False(store.IsTracking);
        Assert.Equal(0, store.Record(new[] { At(A101, 1) }, Anywhere));
        Assert.Empty(store.QueryFiles());
        Assert.Equal(0, _storage.WriteCount);
    }

    [Fact]
    public void TurningOn_IsSavedAtOnce_AndStampsWhenTrackingStarted()
    {
        var store = Enabled();

        Assert.Equal(1, _storage.WriteCount);
        Assert.True(store.IsTracking);
        Assert.Equal(_time.UtcNow, store.Settings.TrackingStartedAt);
        Assert.Equal(_time.UtcNow, store.Settings.ResumedAt);
        Assert.Equal(DocumentKinds.All, store.Settings.Kinds);
        Assert.Equal(RecentFilesScope.TrackedFolders, store.Settings.Scope);
    }

    [Fact]
    public void OpensBeforeTrackingWasTurnedOn_AreNeverRecorded()
    {
        var store = Enabled();

        Assert.Equal(1, store.Record(new[] { At(A101, -5), At(Report, 2) }, Anywhere));

        Assert.Equal(new[] { Report }, store.QueryFiles().Select(f => f.Path));
    }

    [Fact]
    public void AnOpenIsRecordedOnce_HoweverOftenItIsSeen_AndEachLaterOpenCounts()
    {
        var store = Enabled();
        var first = At(A101, 1);

        store.Record(new[] { first }, Anywhere);
        store.Record(new[] { first }, Anywhere);
        store.Record(new[] { new RecentDocument(A101.ToUpperInvariant(), first.LastOpenedAt) }, Anywhere);
        store.Record(new[] { At(A101, 30) }, Anywhere);

        var row = Assert.Single(store.QueryFiles());
        Assert.Equal(2, row.Opens);
        Assert.Equal(_time.UtcNow.AddMinutes(30), row.LastOpenedAt);
        Assert.Equal(DocumentKind.Pdf, row.Kind);
    }

    [Fact]
    public void OnlyChosenKinds_AndOnlyInScope_AreRecorded()
    {
        var store = Enabled();
        Assert.True(store.TrySetKinds(new[] { DocumentKind.Word, DocumentKind.Excel }, out _).Success);

        store.Record(new[] { At(A101, 1), At(Report, 1), At(Budget, 1), At(Elsewhere, 1), At(@"C:\Jobs\notes.txt", 1) },
            path => path.StartsWith(@"C:\Jobs", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(new[] { Budget, Report }, store.QueryFiles().Select(f => f.Path).OrderBy(p => p));
    }

    [Fact]
    public void NoKinds_IsRefused()
    {
        var store = Enabled();

        var result = store.TrySetKinds(Array.Empty<DocumentKind>(), out _);

        Assert.False(result.Success);
        Assert.Equal(DocumentKinds.All, store.Settings.Kinds);
    }

    [Fact]
    public void TurningOff_KeepsHistory_StopsRecording_AndTurningOnAgainSkipsTheGap()
    {
        var store = Enabled();
        store.Record(new[] { At(A101, 1) }, Anywhere);
        store.SetEnabled(false);
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, store.Record(new[] { At(Report, -10) }, Anywhere));

        store.SetEnabled(true);
        _time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(0, store.Record(new[] { At(Report, -90) }, Anywhere));
        Assert.Equal(1, store.Record(new[] { At(Report, -10) }, Anywhere));
        Assert.Equal(2, store.QueryFiles().Count);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero), store.Settings.TrackingStartedAt);
    }

    [Fact]
    public void Opens_AreBuffered_UntilFlush()
    {
        var store = Enabled();
        store.Record(new[] { At(A101, 1) }, Anywhere);

        Assert.True(store.HasUnsavedChanges);
        Assert.Equal(1, _storage.WriteCount);
        Assert.True(store.Flush().Saved);
        Assert.Equal(2, _storage.WriteCount);
        Assert.True(store.Flush().Saved);
        Assert.Equal(2, _storage.WriteCount);

        var reloaded = NewStore();
        Assert.True(reloaded.IsTracking);
        Assert.Single(reloaded.QueryFiles());
    }

    [Fact]
    public void AFailedFlush_IsReported_AndKeptForTheNext()
    {
        var store = Enabled();
        store.Record(new[] { At(A101, 1) }, Anywhere);
        _storage.FailNextWrite = true;

        Assert.False(store.Flush().Saved);
        Assert.True(store.HasUnsavedChanges);
        Assert.True(store.Flush().Saved);
        Assert.Single(NewStore().QueryFiles());
    }

    [Fact]
    public void QueryFiles_CountsOpensInThePeriod_AndFiltersByKind()
    {
        var store = Enabled();
        store.Record(new[] { At(A101, 60) }, Anywhere);
        _time.Advance(TimeSpan.FromDays(2));
        store.Record(new[] { At(A101, 60), At(Report, 30) }, Anywhere);

        var today = store.Today();
        Assert.Equal(2, store.QueryFiles().Single(f => f.Path == A101).Opens);
        Assert.Equal(1, store.QueryFiles(today, today).Single(f => f.Path == A101).Opens);
        Assert.Equal(new[] { A101 }, store.QueryFiles(today.AddDays(-2), today.AddDays(-2)).Select(f => f.Path));
        Assert.Equal(new[] { Report }, store.QueryFiles(kinds: new[] { DocumentKind.Word }).Select(f => f.Path));
    }

    [Fact]
    public void DayTotals_CountOpensAndDistinctFiles_InTheLocalZone()
    {
        // ManualTimeProvider's zone is UTC+10, so 20:00 UTC is the next local day.
        var store = Enabled();
        store.Record(new[] { At(A101, 60), At(Report, 90) }, Anywhere);
        store.Record(new[] { At(A101, 20 * 60) }, Anywhere);

        var totals = store.QueryDayTotals();

        Assert.Equal(new RecentFileDayTotal(2, 2), totals[new DateOnly(2026, 9, 28)]);
        Assert.Equal(new RecentFileDayTotal(1, 1), totals[new DateOnly(2026, 9, 29)]);
        Assert.Equal(new RecentFileDayTotal(1, 1), store.QueryDayTotals(new[] { DocumentKind.Word })[new DateOnly(2026, 9, 28)]);
    }

    [Fact]
    public void OpensOlderThanAYear_ArePruned_AtTheFirstFlushOfANewDay()
    {
        var store = Enabled();
        store.Record(new[] { At(A101, 1) }, Anywhere);
        store.Flush();

        _time.Advance(TimeSpan.FromDays(RecentFilesStore.RetentionDays + 1));
        Assert.True(store.Flush().Saved);

        Assert.Empty(store.QueryFiles());
    }

    [Fact]
    public void Forget_AndClearHistory_AreSavedAtOnce_AndKeepTheSettings()
    {
        var store = Enabled();
        store.Record(new[] { At(A101, 1), At(Report, 1) }, Anywhere);

        Assert.True(store.Forget(A101.ToLowerInvariant()).Saved);
        Assert.Equal(new[] { Report }, NewStore().QueryFiles().Select(f => f.Path));

        Assert.True(store.ClearHistory().Saved);
        var reloaded = NewStore();
        Assert.Empty(reloaded.QueryFiles());
        Assert.True(reloaded.IsTracking);
    }

    [Fact]
    public void IsInScope_TrackedFolders_MeansUnderATrackedRootOrItsEquivalent()
    {
        var roots = new[]
        {
            new TrackedRootConfig("a", @"C:\Jobs") { EquivalentPrefixes = new[] { @"\\files\jobs" } },
        };

        Assert.True(RecentFilesStore.IsInScope(A101, RecentFilesScope.TrackedFolders, roots));
        Assert.True(RecentFilesStore.IsInScope(@"\\files\jobs\Tower B\A-101.pdf", RecentFilesScope.TrackedFolders, roots));
        Assert.False(RecentFilesStore.IsInScope(Elsewhere, RecentFilesScope.TrackedFolders, roots));
        Assert.False(RecentFilesStore.IsInScope(A101, RecentFilesScope.TrackedFolders, Array.Empty<TrackedRootConfig>()));
        Assert.True(RecentFilesStore.IsInScope(Elsewhere, RecentFilesScope.Everywhere, Array.Empty<TrackedRootConfig>()));
    }

    [Fact]
    public void ADamagedFile_IsSetAside_AndTrackingRestartsOff()
    {
        _storage.ContentsToReturn = "{ damaged";

        var store = NewStore();

        Assert.Equal(StoreLoadOutcome.Damaged, store.LoadOutcome);
        Assert.True(store.IsAvailable);
        Assert.False(store.IsTracking);
        Assert.Equal(1, _storage.QuarantineCount);
        Assert.Contains("off until you turn it on again", store.Notice);
    }

    [Fact]
    public void AnUnreadableOrNewerFile_IsLeftAlone_AndNothingIsWritten()
    {
        _storage.ContentsToReturn = @"{""schemaVersion"":2,""files"":[]}";
        var newer = NewStore();
        Assert.Equal(StoreLoadOutcome.WrittenByNewerVersion, newer.LoadOutcome);
        Assert.False(newer.SetEnabled(true).Saved);

        _storage.ReadThrows = new IOException("locked");
        var unreadable = NewStore();
        Assert.Equal(StoreLoadOutcome.Unreadable, unreadable.LoadOutcome);
        Assert.False(unreadable.IsAvailable);

        Assert.Equal(0, _storage.WriteCount);
        Assert.Equal(0, _storage.QuarantineCount);
    }

    [Fact]
    public void AHandEditedFile_IsMadeSafe()
    {
        _storage.ContentsToReturn = @"{""schemaVersion"":1,
            ""settings"":{""enabled"":true,""kinds"":[],""scope"":""everywhere""},
            ""files"":[null,{""path"":""relative.pdf"",""opens"":[""2026-09-27T00:00:00Z""]},
                {""path"":""C:\\A.pdf"",""opens"":[""2026-09-27T02:00:00Z"",""2026-09-27T01:00:00Z""]},
                {""path"":""c:\\a.PDF"",""opens"":[""2026-09-27T01:00:00Z"",""2026-09-27T03:00:00Z""]}]}";

        var store = NewStore();

        Assert.Equal(StoreLoadOutcome.Ok, store.LoadOutcome);
        // Enabled without a ResumedAt can't say where tracking restarted, so it is off.
        Assert.False(store.Settings.Enabled);
        Assert.Equal(DocumentKinds.All, store.Settings.Kinds);
        Assert.Equal(RecentFilesScope.Everywhere, store.Settings.Scope);
        var row = Assert.Single(store.QueryFiles());
        Assert.Equal(3, row.Opens);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 3, 0, 0, TimeSpan.Zero), row.LastOpenedAt);
    }

    [Fact]
    public void ARealFile_RoundTrips()
    {
        using var dir = new TempDirectory();
        var first = new RecentFilesStore(new FilePlacesStorage(dir.Path, "recent-files.json"), _time);
        first.SetEnabled(true);
        first.SetScope(RecentFilesScope.Everywhere);
        first.Record(new[] { At(@"C:\Jobs – Ärchiv\Café 日本.xlsx", 1) }, Anywhere);
        Assert.True(first.Flush().Saved);

        var second = new RecentFilesStore(new FilePlacesStorage(dir.Path, "recent-files.json"), _time);

        Assert.Equal(RecentFilesScope.Everywhere, second.Settings.Scope);
        Assert.Equal(@"C:\Jobs – Ärchiv\Café 日本.xlsx", Assert.Single(second.QueryFiles()).Path);
        Assert.Contains("\"everywhere\"", File.ReadAllText(dir.File("recent-files.json")));
    }
}
