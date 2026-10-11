using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Models.RecentFiles;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Library;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Revit;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

public sealed class LibraryRevitRollupTests
{
    private const string Central = @"C:\Jobs\Central1.rvt";
    private const string Local = @"C:\REVIT_LOCAL2025\Central1_user.rvt";
    private const string Backup = @"C:\REVIT_LOCAL2025\Central1_user_20261011-175602.rvt";
    private static readonly DateOnly Today = new(2026, 10, 11);
    private static DateTimeOffset At(int days = 0, int hour = 9) => new(Today.AddDays(days).ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);
    private static RevitFileInfo LocalInfo(string? central = Central) => new(2025, "b", RevitWorksharing.Local, central, null, 14, RevitFileProblem.None);

    private static LibrarySnapshot Snapshot() => new(
        Array.Empty<Place>(), Array.Empty<SessionSnapshot>(), true, null, Array.Empty<RecentsRootData>(), Today.AddDays(-61),
        true, null, new RecentFilesSettingsSnapshot(true, DocumentKinds.All, RecentFilesScope.Everywhere, At(-30), At(-30)),
        new[]
        {
            new RecentFileHistory(Central, DocumentKind.Revit, new[] { At(-1) }),
            new RecentFileHistory(Local, DocumentKind.Revit, new[] { At(-1, 10), At(0, 10) }),
            new RecentFileHistory(Backup, DocumentKind.Revit, new[] { At(0, 11) }),
        }, Today, TimeZoneInfo.Utc);

    private static LibrarySnapshot Rolled() => Snapshot().WithRevitInfo(new Dictionary<string, RevitFileInfo>
    {
        [Local] = LocalInfo(), [Backup] = LocalInfo(Central.ToUpperInvariant()),
    });

    [Fact]
    public void CopiesCombineCountsAndLatestOpen_WithoutChangingTheCentralNameOrFolder()
    {
        var row = Assert.Single(LibraryQueryEngine.Run(Rolled(), LibraryFilter.None, null).Items);
        Assert.Equal(Central, row.Location);
        Assert.Equal("Central1.rvt", row.Name);
        Assert.Equal(@"C:\Jobs", row.Folder);
        Assert.Equal(4, row.RecentCount);
        Assert.Equal(At(0, 11), row.LastUsedAt);
        Assert.Equal(2, row.LocalCopies.Count);
        var vm = new LibraryRowViewModel(row, "Revit", 0, TimeZoneInfo.Utc, CultureInfo.InvariantCulture);
        Assert.Equal("2 local copies", vm.LocalCopiesText);
        Assert.Contains(Backup, vm.NameToolTip);
        Assert.Contains("combined", vm.VisitsToolTip);
    }

    [Fact]
    public void CopiedCentralsRetainingCentralFlagRollUpAlongsideSavedLocals()
    {
        const string unsavedCopy = @"C:\REVIT_LOCAL2025\Central1_user_20261011-171810.rvt";
        const string savedLocal = @"C:\REVIT_LOCAL2025\Central1_user_20261011-171534.rvt";
        var data = Snapshot() with
        {
            Files = Snapshot().Files.Concat(new[]
            {
                new RecentFileHistory(unsavedCopy, DocumentKind.Revit, new[] { At(0, 12) }),
                new RecentFileHistory(savedLocal, DocumentKind.Revit, new[] { At(0, 9) }),
            }).ToArray(),
        };
        data = data.WithRevitInfo(new Dictionary<string, RevitFileInfo>
        {
            [Central] = LocalInfo() with { Worksharing = RevitWorksharing.Central, LastSavePath = Central },
            [Local] = LocalInfo(), [savedLocal] = LocalInfo(),
            [Backup] = LocalInfo() with { Worksharing = RevitWorksharing.Central, LastSavePath = Central },
            [unsavedCopy] = LocalInfo() with { Worksharing = RevitWorksharing.Central, LastSavePath = Central },
        });
        Assert.False(data.RevitCentrals.ContainsKey(Central));
        var result = LibraryQueryEngine.Run(data, LibraryFilter.None, null);
        var row = Assert.Single(result.Items);
        Assert.Equal(Central, row.Location);
        Assert.Equal(4, row.LocalCopies.Count);
        Assert.Equal(6, row.RecentCount);
        Assert.Equal(At(0, 12), row.LastUsedAt);
        Assert.Equal(4, result.Heat[Today].FileOpens);
    }

    [Theory]
    [InlineData(Central)]
    [InlineData("c:/jobs/./CENTRAL1.rvt")]
    public void OriginalCentralWithItsOwnRecordedPathRemainsItsOwnRow(string recordedPath)
    {
        var data = Snapshot().WithRevitInfo(new Dictionary<string, RevitFileInfo>
        {
            [Central] = LocalInfo(recordedPath) with { Worksharing = RevitWorksharing.Central },
        });
        Assert.Empty(data.RevitCentrals);
        Assert.Contains(LibraryQueryEngine.Run(data, LibraryFilter.None, null).Items, i => i.Location == Central);
    }

    [Fact]
    public void PeriodCountsOnlyOpensAndCopiesInThatPeriod()
    {
        var result = LibraryQueryEngine.Run(Rolled(), LibraryFilter.None, (Today.AddDays(-1), Today.AddDays(-1)));
        var row = Assert.Single(result.Items);
        Assert.Equal(2, row.RecentCount);
        Assert.Equal(new[] { Local }, row.LocalCopies);
        Assert.Equal(At(-1, 10), row.LastUsedAt);
        Assert.Equal(2, result.Heat[Today].FileOpens); // calendar is not narrowed by the chosen date
    }

    [Fact]
    public void OnlyLocalsRecorded_StillCreatesAnOpenableCentralRow()
    {
        var data = Rolled() with { Files = Snapshot().Files.Where(f => f.Path != Central).ToArray() };
        var row = Assert.Single(LibraryQueryEngine.Run(data, LibraryFilter.None, null).Items);
        Assert.Equal(Central, row.Location);
        Assert.Equal(3, row.RecentCount);
    }

    [Fact]
    public void SummaryCountsAreRolledUp_AndRawHistoryIsUntouched()
    {
        var original = Rolled();
        var data = original with { FileSummaries = original.Files.Select(f => new RecentFileSummary(f.Path, f.Kind, 20, f.Opens.Max())).ToArray() };
        var row = Assert.Single(LibraryQueryEngine.Run(data, LibraryFilter.None, null).Items);
        Assert.Equal(60, row.RecentCount);
        Assert.Equal(3, original.Files.Count);
        Assert.Equal(Local, original.Files[1].Path);
    }

    [Theory]
    [InlineData("Central1")]
    [InlineData("20261011-175602")]
    public void SearchAndCalendarFollowTheCentralIncludingLocalFileNames(string search)
    {
        var result = LibraryQueryEngine.Run(Rolled(), LibraryFilter.None with { Text = search, Root = new RootScope("jobs", @"C:\Jobs") }, null);
        Assert.Equal(Central, Assert.Single(result.Items).Location);
        Assert.Equal(2, result.Heat[Today].FileOpens);
        Assert.Equal(2, result.Heat[Today.AddDays(-1)].FileOpens);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Central1.rvt")]
    [InlineData("RSN://server/Central1.rvt")]
    [InlineData("C:\\Jobs\\Central1.pdf")]
    [InlineData(Local)]
    public void InvalidOrSelfCentralPathsRemainSeparate(string? central)
    {
        var data = Snapshot().WithRevitInfo(new Dictionary<string, RevitFileInfo> { [Local] = LocalInfo(central) });
        Assert.Empty(data.RevitCentrals);
        Assert.Equal(3, LibraryQueryEngine.Run(data, LibraryFilter.None, null).Items.Count);
    }

    [Fact]
    public void UnreadableAndStandaloneFilesRemainSeparate()
    {
        var data = Snapshot().WithRevitInfo(new Dictionary<string, RevitFileInfo>
        {
            [Local] = LocalInfo() with { Problem = RevitFileProblem.InUse },
            [Backup] = LocalInfo() with { Worksharing = RevitWorksharing.NotWorkshared },
        });
        Assert.Empty(data.RevitCentrals);
    }

    [Fact]
    public void CentralSessionTagsApplyToRolledActivity_ExplicitLocalSessionIsPreserved()
    {
        var data = Rolled() with
        {
            Sessions = new[]
            {
                new SessionSnapshot("c", "Central session", new[] { "Tower" }, new[] { Central }, At(-2), At(-2), null, Array.Empty<DateTimeOffset>()),
                new SessionSnapshot("l", "Local session", Array.Empty<string>(), new[] { Local }, At(-2), At(-2), null, Array.Empty<DateTimeOffset>()),
            },
        };
        var result = LibraryQueryEngine.Run(data, LibraryFilter.None with { Tag = "Tower" }, null);
        Assert.Equal(4, Assert.Single(result.Items).RecentCount);
        Assert.Equal(2, result.Heat[Today].FileOpens);
        var local = Assert.Single(LibraryQueryEngine.Run(data, LibraryFilter.None with { Source = LibrarySourceFilter.Sessions }, null).Items, i => i.Location == Local);
        Assert.Equal(0, local.RecentCount);
    }

    [Fact]
    public void SameNamedCentralsInDifferentFoldersAreNotMerged()
    {
        var data = Snapshot().WithRevitInfo(new Dictionary<string, RevitFileInfo>
        {
            [Local] = LocalInfo(), [Backup] = LocalInfo(@"D:\AnotherJob\Central1.rvt"),
        });
        Assert.Equal(2, LibraryQueryEngine.Run(data, LibraryFilter.None, null).Items.Count);
    }

    [Fact]
    public void ViewModelResolvesCopiesBeforeFiltering_AndKeepsRecordedPaths()
    {
        var time = new ManualTimeProvider(At(-1), TimeZoneInfo.Utc);
        var recent = new RecentFilesStore(new FakePlacesStorage(), time);
        recent.SetEnabled(true);
        recent.Record(new[] { new RecentDocument(Local, At(0, 10)), new RecentDocument(Backup, At(0, 11)) }, _ => true);
        time.UtcNow = At(0, 12);
        var places = new PlacesService(new FakePlacesStorage(), time);
        var shell = new FakeShell();
        shell.ExistingFiles.Add(Central);
        var cache = new RevitReleaseCache(path => path == Central ? LocalInfo() with { Worksharing = RevitWorksharing.Central } : LocalInfo(),
            _ => new FileStamp(At().UtcDateTime, 100), time);
        var vm = new LibraryViewModel(places, new SessionStore(new FakePlacesStorage(), time), new ActivityStore(new FakePlacesStorage(), time), recent,
            new PlaceLauncher(places, shell), shell, time, CultureInfo.InvariantCulture, revitReleases: cache);
        vm.Tab = LibraryTab.Recent;
        vm.SearchText = "Central1";
        var row = Assert.Single(vm.Rows);
        Assert.Equal(Central, row.Location);
        Assert.True(row.IsRevitCentral);
        Assert.Equal("2", row.VisitsText);
        vm.Open(row);
        Assert.Equal(new[] { Central }, shell.Opened);
        Assert.Equal(2, recent.QueryHistory().Count);
        Assert.Contains(recent.QueryHistory(), f => f.Path == Local);
    }

    [Fact]
    public void SelectedLocalMovesToCentralWhenItsMetadataBecomesReadable()
    {
        var time = new ManualTimeProvider(At(-1), TimeZoneInfo.Utc);
        var recent = new RecentFilesStore(new FakePlacesStorage(), time);
        recent.SetEnabled(true);
        recent.Record(new[] { new RecentDocument(Local, At(0, 10)) }, _ => true);
        time.UtcNow = At(0, 12);
        var readable = false;
        var cache = new RevitReleaseCache(path => path == Central ? LocalInfo() with { Worksharing = RevitWorksharing.Central } :
                readable ? LocalInfo() : RevitFileInfo.Failed(RevitFileProblem.InUse),
            _ => new FileStamp(At().UtcDateTime, 100), time);
        var places = new PlacesService(new FakePlacesStorage(), time);
        var shell = new FakeShell();
        var vm = new LibraryViewModel(places, new SessionStore(new FakePlacesStorage(), time), new ActivityStore(new FakePlacesStorage(), time), recent,
            new PlaceLauncher(places, shell), shell, time, CultureInfo.InvariantCulture, revitReleases: cache);
        vm.SelectedRow = Assert.Single(vm.Rows);
        Assert.Equal(Local, vm.SelectedRow.Location);
        readable = true;
        time.Advance(TimeSpan.FromMinutes(1));
        vm.Reload();
        Assert.Equal(Central, vm.SelectedRow?.Location);
    }
}
