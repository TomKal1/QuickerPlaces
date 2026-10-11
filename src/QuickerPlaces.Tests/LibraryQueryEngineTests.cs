using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Models.RecentFiles;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Library;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The workspace's one query over the Library's sources (configurable canvas
/// plan D4, D5, M2): items for an inclusive period, heat that honours the
/// non-date filters without being narrowed by the period, and saying what a
/// source can't show instead of showing unfiltered numbers as filtered ones.
/// </summary>
public sealed class LibraryQueryEngineTests
{
    private static readonly DateOnly Today = new(2026, 9, 25);
    private static readonly DateOnly DetailFrom = Today.AddDays(-(ActivityStore.DetailDays - 1));
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;

    private const string Acme = @"C:\Jobs\Acme";
    private const string Beta = @"C:\Jobs\Beta";
    private const string Plan = @"C:\Jobs\Acme\Plan.pdf";
    private const string Spec = @"C:\Jobs\Beta\Spec.pdf";
    private const string Budget = @"C:\Jobs\Acme\Budget.xlsx";

    private static DateTimeOffset At(DateOnly date, int hour = 9) => new(date.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);

    private static readonly RecentFilesSettingsSnapshot RecentFilesOn =
        new(true, DocumentKinds.All.ToArray(), RecentFilesScope.Everywhere, At(Today.AddDays(-300)), null);

    private static LibrarySnapshot Snapshot(
        IEnumerable<Place>? places = null,
        IEnumerable<SessionSnapshot>? sessions = null,
        IEnumerable<RecentsRootData>? roots = null,
        IEnumerable<RecentFileHistory>? files = null,
        RecentFilesSettingsSnapshot? settings = null)
        => new(
            (places ?? Array.Empty<Place>()).ToList(),
            (sessions ?? Array.Empty<SessionSnapshot>()).ToList(),
            true, null,
            (roots ?? Array.Empty<RecentsRootData>()).ToList(),
            DetailFrom,
            true, null,
            settings ?? RecentFilesOn,
            (files ?? Array.Empty<RecentFileHistory>()).ToList(),
            Today, Zone);

    private static SessionSnapshot Session(string name, string[] tags, string[] files, DateOnly created, params DateOnly[] reopened)
        => new(name + "-id", name, tags, files, At(created), At(created), reopened.Length > 0 ? At(reopened[^1]) : null,
            reopened.Select(d => At(d)).ToArray());

    private static FolderActivity Visit(string folder, int visits, DateOnly date) => new(folder, TimeSpan.FromMinutes(5), visits, At(date));

    /// <summary>A root with detail for the given days, and day totals for those days plus <paramref name="oldTotals"/>.</summary>
    private static RecentsRootData Root(IEnumerable<(DateOnly Date, FolderActivity[] Folders)> detail,
        IEnumerable<(DateOnly Date, int Visits)>? oldTotals = null, bool enabled = true, string id = "root", string path = @"C:\Jobs")
    {
        var days = detail.Select(d => new FolderDay(d.Date, d.Folders)).ToList();
        var totals = days.ToDictionary(d => d.Date, d => new ActivityDayTotal(TimeSpan.FromMinutes(5), d.Folders.Sum(f => f.Visits), d.Folders.Count));
        foreach (var (date, visits) in oldTotals ?? Array.Empty<(DateOnly, int)>())
            totals[date] = new ActivityDayTotal(TimeSpan.FromMinutes(5), visits, 1);
        return new RecentsRootData(id, enabled, Today.AddDays(-200), totals, days, path);
    }

    private static RecentFileHistory File(string path, params DateOnly[] opens)
        => new(path, DocumentKinds.FromPath(path)!.Value, opens.Select(d => At(d)).ToArray());

    private static LibraryQueryResult Run(LibrarySnapshot data, LibraryFilter? filter = null, (DateOnly, DateOnly)? period = null)
        => LibraryQueryEngine.Run(data, filter ?? LibraryFilter.None, period, CultureInfo.InvariantCulture);

    private static string[] Names(LibraryQueryResult result) => result.Items.Select(i => i.Name).OrderBy(n => n).ToArray();

    // ---------------------------------------------------------------
    // Items in a period
    // ---------------------------------------------------------------

    [Fact]
    public void APeriod_ListsWhatWasUsedInIt_InclusiveAtBothEnds()
    {
        var monday = new DateOnly(2026, 9, 21);
        var sunday = new DateOnly(2026, 9, 27);
        var data = Snapshot(
            roots: new[] { Root(new[] { (monday.AddDays(-1), new[] { Visit(Beta, 1, monday.AddDays(-1)) }), (monday, new[] { Visit(Acme, 2, monday) }) }) },
            files: new[] { File(Plan, sunday), File(Spec, sunday.AddDays(1)) });

        var week = Run(data, period: (monday, sunday));

        Assert.Equal(new[] { "Acme", "Plan.pdf" }, Names(week));
        Assert.Equal(2, week.ItemsInPeriod);
    }

    [Fact]
    public void APeriodGivenBackwards_IsPutInOrder()
    {
        var data = Snapshot(files: new[] { File(Plan, Today.AddDays(-2)) });

        Assert.Equal(new[] { "Plan.pdf" }, Names(Run(data, period: (Today, Today.AddDays(-3)))));
    }

    [Fact]
    public void AResourceInSeveralSources_IsOneItem_WithEverySourceNoted()
    {
        var data = Snapshot(
            places: new[] { new Place { Alias = "Acme job", Type = PlaceType.Folder, Resource = Acme + @"\", LastOpenedAt = At(Today) } },
            roots: new[] { Root(new[] { (Today, new[] { Visit(@"c:\jobs\ACME", 3, Today) }) }) },
            sessions: new[] { Session("Tower", new[] { "markups" }, new[] { Plan }, Today) },
            files: new[] { File(Plan, Today, Today) });

        var result = Run(data, period: (Today, Today));

        var folder = Assert.Single(result.Items, i => i.Kind == LibraryKind.Folder);
        Assert.Equal("Acme job", folder.Name);
        Assert.Equal("Saved place · Visited 3 times", folder.SourceText);
        var pdf = Assert.Single(result.Items, i => i.Kind == LibraryKind.Pdf);
        Assert.Equal("In Tower · Opened 2 times", pdf.SourceText);
    }

    [Fact]
    public void Filters_NarrowTheItems_ButNotByKind_SoTheChipsCanCount()
    {
        var data = Snapshot(
            sessions: new[] { Session("Tower", new[] { "markups" }, new[] { Plan }, Today) },
            files: new[] { File(Budget, Today), File(Spec, Today) });

        Assert.Equal(new[] { "Plan.pdf" }, Names(Run(data, new LibraryFilter(Tag: "MARKUPS"))));
        Assert.Equal(new[] { "Plan.pdf" }, Names(Run(data, new LibraryFilter(Source: LibrarySourceFilter.Saved))));
        Assert.Equal(new[] { "Budget.xlsx", "Spec.pdf" }, Names(Run(data, new LibraryFilter(Source: LibrarySourceFilter.Recent))));
        Assert.Equal(new[] { "Budget.xlsx", "Plan.pdf", "Spec.pdf" }, Names(Run(data, new LibraryFilter(Kind: LibraryKind.Pdf))));
        Assert.Equal(new[] { "Spec.pdf" }, Names(Run(data, new LibraryFilter(Text: "beta"))));
    }

    [Fact]
    public void APeriodReachingPastKeptDetail_SaysWhatCantBeListed()
    {
        var data = Snapshot(
            places: new[] { new Place { Alias = "Wiki", Type = PlaceType.Url, Resource = "https://wiki" } },
            roots: new[] { Root(Array.Empty<(DateOnly, FolderActivity[])>()) },
            settings: RecentFilesOn with { TrackingStartedAt = At(Today.AddDays(-10)) });

        var result = Run(data, period: (Today.AddDays(-100), Today));

        Assert.Contains(result.PeriodNotes, n => n.StartsWith("Saved places are listed for a period only if"));
        Assert.Contains(result.PeriodNotes, n => n.StartsWith($"Folders visited before {DetailFrom:d MMM yyyy}") && n.Contains("62 days"));
        Assert.Contains(result.PeriodNotes, n => n.StartsWith("Recent Files started recording on"));
        Assert.Empty(Run(data).PeriodNotes);
        Assert.DoesNotContain(Run(data, new LibraryFilter(Kind: LibraryKind.Pdf), (Today.AddDays(-100), Today)).PeriodNotes,
            n => n.StartsWith("Folders"));
    }

    // ---------------------------------------------------------------
    // Heat
    // ---------------------------------------------------------------

    [Fact]
    public void Heat_CoversEverythingKept_WhateverPeriodIsChosen()
    {
        var data = Snapshot(files: new[] { File(Plan, Today.AddDays(-40), Today) });

        var chosen = Run(data, period: (Today, Today));

        Assert.Equal(1, chosen.Heat[Today.AddDays(-40)].FileOpens);
        Assert.Equal(1, chosen.Heat[Today].FileOpens);
    }

    [Fact]
    public void Heat_WithNoFilter_AddsEverySourcesEvidence_WithABreakdown()
    {
        var data = Snapshot(
            roots: new[] { Root(new[] { (Today, new[] { Visit(Acme, 2, Today) }) }) },
            sessions: new[] { Session("Tower", Array.Empty<string>(), new[] { Plan }, Today, Today) },
            files: new[] { File(Budget, Today) });

        var day = Run(data).Heat[Today];

        Assert.Equal(new HeatDay(2, 1, 1, 1), day);
        Assert.Equal(5, day.Weight);
        Assert.Equal("2 folder visits · 1 file opened · 1 session saved · 1 session reopened", day.Summary);
        Assert.All(Run(data).Coverage, c => Assert.NotEqual(CoverageState.Unavailable, c.State));
    }

    [Fact]
    public void Heat_ForAKind_CountsOnlyThatKindsEvidence()
    {
        var data = Snapshot(
            roots: new[] { Root(new[] { (Today, new[] { Visit(Acme, 2, Today) }) }) },
            sessions: new[] { Session("Tower", Array.Empty<string>(), new[] { Plan }, Today) },
            files: new[] { File(Budget, Today) });

        Assert.Equal(new HeatDay(2, 0, 0, 0), Run(data, new LibraryFilter(Kind: LibraryKind.Folder)).Heat[Today]);
        Assert.Equal(new HeatDay(0, 1, 0, 0), Run(data, new LibraryFilter(Kind: LibraryKind.Excel)).Heat[Today]);
        Assert.Equal(new HeatDay(0, 0, 1, 0), Run(data, new LibraryFilter(Kind: LibraryKind.Pdf)).Heat[Today]);
        Assert.Empty(Run(data, new LibraryFilter(Kind: LibraryKind.Link)).Heat);

        var folders = Run(data, new LibraryFilter(Kind: LibraryKind.Folder)).Coverage;
        Assert.Equal(CoverageState.NotApplicable, folders.Single(c => c.Source == LibraryQueryEngine.FileSource).State);
    }

    [Fact]
    public void Heat_ForATag_CountsTaggedSessions_AndOpensOfTheirFiles_NotFolders()
    {
        var data = Snapshot(
            roots: new[] { Root(new[] { (Today, new[] { Visit(Acme, 2, Today) }) }) },
            sessions: new[]
            {
                Session("Tower", new[] { "markups" }, new[] { Plan }, Today),
                Session("Other", Array.Empty<string>(), new[] { Spec }, Today),
            },
            files: new[] { File(Plan, Today), File(Spec, Today) });

        var result = Run(data, new LibraryFilter(Tag: "markups"));

        Assert.Equal(new HeatDay(0, 1, 1, 0), result.Heat[Today]);
        Assert.Equal(CoverageState.NotApplicable, result.Coverage.Single(c => c.Source == LibraryQueryEngine.FolderSource).State);
    }

    [Fact]
    public void Heat_ForASearch_FiltersFolderVisitsFromKeptDetail_AndSaysEarlierDaysArentKnown()
    {
        var old = DetailFrom.AddDays(-30);
        var data = Snapshot(roots: new[]
        {
            Root(new[] { (Today, new[] { Visit(Acme, 2, Today), Visit(Beta, 5, Today) }) }, oldTotals: new[] { (old, 4) }),
        });

        var result = Run(data, new LibraryFilter(Text: "acme"));

        Assert.Equal(2, result.Heat[Today].FolderVisits);
        Assert.False(result.Heat[Today].FoldersUnknown);
        Assert.Equal(new HeatDay(0, 0, 0, 0, FoldersUnknown: true), result.Heat[old]);
        Assert.Equal("folder visits for this filter no longer known", result.Heat[old].Summary);
        var folders = result.Coverage.Single(c => c.Source == LibraryQueryEngine.FolderSource);
        Assert.Equal(CoverageState.Partial, folders.State);
        Assert.Contains("62 days", folders.Reason);

        // Without a search the year's day totals are used as they are.
        var unfiltered = Run(data);
        Assert.Equal(new HeatDay(4, 0, 0, 0), unfiltered.Heat[old]);
        Assert.Equal(CoverageState.Available, unfiltered.Coverage.Single(c => c.Source == LibraryQueryEngine.FolderSource).State);
    }

    [Fact]
    public void Heat_ForSaved_NarrowsFolderVisitsToSavedPlaces_AndLeavesRecentFilesOut()
    {
        var data = Snapshot(
            places: new[] { new Place { Alias = "Acme", Type = PlaceType.Folder, Resource = Acme } },
            roots: new[] { Root(new[] { (Today, new[] { Visit(Acme, 2, Today), Visit(Beta, 5, Today) }) }) },
            files: new[] { File(Budget, Today) });

        var saved = Run(data, new LibraryFilter(Source: LibrarySourceFilter.Saved));
        Assert.Equal(new HeatDay(2, 0, 0, 0), saved.Heat[Today]);

        var recent = Run(data, new LibraryFilter(Source: LibrarySourceFilter.Recent));
        Assert.Equal(new HeatDay(7, 1, 0, 0), recent.Heat[Today]);
        Assert.Equal(CoverageState.NotApplicable, recent.Coverage.Single(c => c.Source == LibraryQueryEngine.SessionSource).State);
    }

    [Fact]
    public void TrackingStart_IsTheEarliestCountedSource()
    {
        var data = Snapshot(
            roots: new[] { Root(Array.Empty<(DateOnly, FolderActivity[])>()) },
            settings: RecentFilesOn with { TrackingStartedAt = At(Today.AddDays(-10)) });

        Assert.Equal(Today.AddDays(-200), Run(data).TrackingStartedOn);
        Assert.Equal(Today.AddDays(-10), Run(data, new LibraryFilter(Kind: LibraryKind.Pdf)).TrackingStartedOn);
        Assert.Equal(Today, Run(Snapshot(settings: RecentFilesOn with { TrackingStartedAt = null, Enabled = false })).TrackingStartedOn);
    }

    // ---------------------------------------------------------------
    // Coverage
    // ---------------------------------------------------------------

    [Fact]
    public void Coverage_SaysWhenASourceHasNothingToGive()
    {
        var off = Snapshot(settings: RecentFilesOn with { Enabled = false, TrackingStartedAt = null });

        var coverage = Run(off).Coverage;

        Assert.Equal("No folders are tracked in Recents.", coverage.Single(c => c.Source == LibraryQueryEngine.FolderSource).Reason);
        var files = coverage.Single(c => c.Source == LibraryQueryEngine.FileSource);
        Assert.Equal(CoverageState.Unavailable, files.State);
        Assert.Equal("Recent Files is off.", files.Reason);
    }

    [Fact]
    public void Coverage_IsPartial_WhenRecentFilesWasOffForAWhile_OrIsOffNow()
    {
        var resumed = Snapshot(files: new[] { File(Plan, Today) },
            settings: RecentFilesOn with { ResumedAt = At(Today.AddDays(-5)) });
        Assert.Equal(CoverageState.Partial, Run(resumed).Coverage.Single(c => c.Source == LibraryQueryEngine.FileSource).State);

        var offWithHistory = Snapshot(files: new[] { File(Plan, Today) }, settings: RecentFilesOn with { Enabled = false });
        var result = Run(offWithHistory);
        Assert.Equal(CoverageState.Partial, result.Coverage.Single(c => c.Source == LibraryQueryEngine.FileSource).State);
        Assert.Equal(1, result.Heat[Today].FileOpens);
    }

    [Fact]
    public void Coverage_IsPartial_WhenEveryFolderHasTrackingOff()
    {
        var data = Snapshot(roots: new[] { Root(new[] { (Today, new[] { Visit(Acme, 1, Today) }) }, enabled: false) });

        var folders = Run(data).Coverage.Single(c => c.Source == LibraryQueryEngine.FolderSource);

        Assert.Equal(CoverageState.Partial, folders.State);
        Assert.Contains("Folder tracking is off", folders.Reason);
    }

    [Fact]
    public void ARootScope_ListsOnlyWhatIsInThatTrackedFolder_AndCountsOnlyItsVisits()
    {
        var other = @"D:\Other\Site";
        var data = Snapshot(
            roots: new[]
            {
                Root(new[] { (Today, new[] { Visit(Acme, 2, Today) }) }),
                Root(new[] { (Today, new[] { Visit(other, 5, Today) }) }, id: "other", path: @"D:\Other"),
            },
            files: new[] { File(Plan, Today), File(@"D:\Other\Site\Notes.docx", Today) });

        var scoped = Run(data, new LibraryFilter(Root: new RootScope("other", @"D:\Other")));

        Assert.Equal(new[] { "Notes.docx", "Site" }, Names(scoped));
        Assert.Equal(5, scoped.Heat[Today].FolderVisits);
        Assert.Equal(1, scoped.Heat[Today].FileOpens);
    }

    [Fact]
    public void TheSessionsSource_ListsOnlySessionFiles_AndCountsNoFolderVisits()
    {
        var data = Snapshot(
            sessions: new[]
            {
                Session("Acme", new[] { "tower" }, new[] { Plan, Budget }, Today),
                Session("Beta", Array.Empty<string>(), new[] { Spec }, Today, Today),
            },
            roots: new[] { Root(new[] { (Today, new[] { Visit(Acme, 2, Today) }) }) },
            files: new[] { File(Plan, Today) });

        var sessions = Run(data, new LibraryFilter(Source: LibrarySourceFilter.Sessions));

        Assert.Equal(new[] { "Budget.xlsx", "Plan.pdf", "Spec.pdf" }, Names(sessions));
        Assert.Equal(CoverageState.NotApplicable, sessions.Coverage.Single(c => c.Source == LibraryQueryEngine.FolderSource).State);
        Assert.Equal(0, sessions.Heat[Today].FolderVisits);
        Assert.Equal(1, sessions.Heat[Today].FileOpens);
        Assert.Equal((2, 1), (sessions.Heat[Today].SessionsSaved, sessions.Heat[Today].SessionsReopened));
    }

    [Fact]
    public void ASessionFilter_ListsThatSessionsFiles_AndCountsOnlyThatSession()
    {
        var data = Snapshot(
            sessions: new[]
            {
                Session("Acme", new[] { "tower" }, new[] { Plan, Budget }, Today),
                Session("Beta", Array.Empty<string>(), new[] { Spec }, Today, Today),
            },
            files: new[] { File(Plan, Today) });

        var beta = Run(data, new LibraryFilter(Source: LibrarySourceFilter.Sessions, Session: "beta"));

        Assert.Equal(new[] { "Spec.pdf" }, Names(beta));
        Assert.Equal(0, beta.Heat[Today].FileOpens);
        Assert.Equal((1, 1), (beta.Heat[Today].SessionsSaved, beta.Heat[Today].SessionsReopened));
        Assert.False(new LibraryFilter(Session: "Beta").OnlyKind);
    }
}
