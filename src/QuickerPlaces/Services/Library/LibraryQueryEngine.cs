using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.Services.Library;

/// <summary>Which items the Library shows by where they come from.</summary>
public enum LibrarySourceFilter
{
    /// <summary>Everything.</summary>
    All,

    /// <summary>Saved places and files in saved sessions.</summary>
    Saved,

    /// <summary>Folders from Recents and files from Recent Files.</summary>
    Recent,
}

/// <summary>
/// The query's filters other than the period (configurable canvas plan D4):
/// a kind, saved or recent, search words, and an existing Session tag. The
/// File shelf lists items passing all of them; the year strip counts
/// evidence for items passing all of them.
/// </summary>
public sealed record LibraryFilter(LibraryKind? Kind = null, LibrarySourceFilter Source = LibrarySourceFilter.All,
    string Text = "", string? Tag = null)
{
    public static LibraryFilter None { get; } = new();

    /// <summary>True when nothing but the kind narrows the items.</summary>
    public bool OnlyKind => Source == LibrarySourceFilter.All && string.IsNullOrWhiteSpace(Text) && Tag is null;
}

/// <summary>How much of a source's history a view can show (plan D5).</summary>
public enum CoverageState
{
    /// <summary>Everything the source recorded is counted.</summary>
    Available,

    /// <summary>Some of it is counted; <see cref="SourceCoverage.Reason"/> says what is missing.</summary>
    Partial,

    /// <summary>Nothing from this source can be counted; the reason says why.</summary>
    Unavailable,

    /// <summary>The filters leave this source out (a Folders filter has no file opens).</summary>
    NotApplicable,
}

/// <summary>What one source contributes to the year strip, and why not all of it when it can't.</summary>
public sealed record SourceCoverage(string Source, CoverageState State, string? Reason = null);

/// <summary>
/// One day of recorded activity (plan D5): not a count of files used, but
/// the evidence each source kept — folder visits, file opens, sessions saved
/// and reopened — for the items that pass the filters. When
/// <see cref="FoldersUnknown"/>, Recents counted visits that day but no longer
/// keeps which folders, so filtered folder visits can't be known.
/// </summary>
public sealed record HeatDay(int FolderVisits, int FileOpens, int SessionsSaved, int SessionsReopened, bool FoldersUnknown = false)
{
    public int Weight => FolderVisits + FileOpens + SessionsSaved + SessionsReopened;

    /// <summary>"2 folder visits · 1 file opened · 1 session saved", with what isn't known.</summary>
    public string Summary
    {
        get
        {
            static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

            var parts = new List<string>();
            if (FolderVisits > 0) parts.Add(Count(FolderVisits, "folder visit", "folder visits"));
            if (FileOpens > 0) parts.Add(Count(FileOpens, "file opened", "files opened"));
            if (SessionsSaved > 0) parts.Add(Count(SessionsSaved, "session saved", "sessions saved"));
            if (SessionsReopened > 0) parts.Add(Count(SessionsReopened, "session reopened", "sessions reopened"));
            if (FoldersUnknown) parts.Add("folder visits for this filter no longer known");
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>What <see cref="LibraryQueryEngine.Run"/> found.</summary>
/// <param name="Items">Items used in the period that pass every filter but the kind, most recently used first (the kind chips count these).</param>
/// <param name="ItemsInPeriod">How many items were used in the period before any filter: 0 means nothing was used, not nothing matched.</param>
/// <param name="Heat">Recorded activity per local day, for the filters, over everything kept.</param>
/// <param name="TrackingStartedOn">The earliest day any counted source was recording.</param>
/// <param name="Coverage">One line per source: counted, partly counted, not counted, or left out by the filters.</param>
/// <param name="PeriodNotes">What the item list can't include for this period, in plain sentences.</param>
public sealed record LibraryQueryResult(
    IReadOnlyList<LibraryItem> Items,
    int ItemsInPeriod,
    IReadOnlyDictionary<DateOnly, HeatDay> Heat,
    DateOnly TrackingStartedOn,
    IReadOnlyList<SourceCoverage> Coverage,
    IReadOnlyList<string> PeriodNotes);

/// <summary>
/// Answers the workspace's one query against a <see cref="LibrarySnapshot"/>
/// (configurable canvas plan D4, D5, M2): the items used in an inclusive
/// period of local days, and the year strip's recorded activity for the
/// same non-date filters, with what each source can and can't show.
///
/// - Items come from <see cref="LibraryIndex"/>, so a resource in several
///   sources is one row with every source noted.
/// - Choosing a period changes the items, never the heat: heat covers
///   everything kept (D4).
/// - Filters apply to heat only where the evidence can be tied to an item:
///   file opens and sessions always; folder visits only while Recents keeps
///   which folders were visited. Before that the day says so, rather than
///   showing unfiltered totals as filtered ones (D5).
/// - A session's saves and reopens are counted as session events, never as
///   opens of its files (D5).
///
/// Pure: no store, clock or thread affinity, so it runs off the UI thread.
/// UI-free and linked into the test project.
/// </summary>
public static class LibraryQueryEngine
{
    public const string FolderSource = "Folder visits";
    public const string FileSource = "Files opened";
    public const string SessionSource = "Sessions saved and reopened";

    public static LibraryQueryResult Run(LibrarySnapshot data, LibraryFilter filter, (DateOnly From, DateOnly To)? period,
        CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (period is { } p && p.From > p.To)
            period = (p.To, p.From);

        var inPeriod = LibraryIndex.Build(PlacesIn(data, period), SessionsIn(data, period), FoldersIn(data, period), FilesIn(data, period));
        var items = inPeriod.Where(i => Passes(i, filter, ignoreKind: true)).ToList();

        // Every item known across all kept history, to tie heat evidence to items.
        var everything = LibraryIndex.Build(data.Places, data.Sessions, FoldersIn(data, null), FilesIn(data, null))
            .ToDictionary(i => i.Key, ResourceIdentity.Comparer);

        var heat = new Dictionary<DateOnly, HeatDay>();
        var starts = new List<DateOnly>();
        var coverage = new List<SourceCoverage>
        {
            AddFolderHeat(data, filter, everything, heat, starts),
            AddFileHeat(data, filter, everything, heat, starts),
            AddSessionHeat(data, filter, everything, heat, starts),
        };

        return new LibraryQueryResult(items, inPeriod.Count, heat, starts.Count > 0 ? starts.Min() : data.Today,
            coverage, PeriodNotes(data, filter, period, culture));
    }

    /// <summary>True when <paramref name="item"/> passes the filters (the kind too, unless <paramref name="ignoreKind"/>).</summary>
    public static bool Passes(LibraryItem item, LibraryFilter filter, bool ignoreKind = false)
        => (ignoreKind || filter.Kind is null || item.Kind == filter.Kind) &&
           filter.Source switch
           {
               LibrarySourceFilter.Saved => item.IsSaved,
               LibrarySourceFilter.Recent => item.IsRecent,
               _ => true,
           } &&
           (filter.Tag is null || item.Tags.Contains(filter.Tag, StringComparer.OrdinalIgnoreCase)) &&
           LibraryIndex.Matches(item, filter.Text);

    // ---------------------------------------------------------------
    // Items in the period
    // ---------------------------------------------------------------

    private static bool In(DateOnly date, (DateOnly From, DateOnly To)? period)
        => period is not { } p || (date >= p.From && date <= p.To);

    /// <summary>Saved places whose last open is in the period: places keep only their last open, not a history.</summary>
    private static IEnumerable<Place> PlacesIn(LibrarySnapshot data, (DateOnly From, DateOnly To)? period)
        => period is null ? data.Places : data.Places.Where(p => p.LastOpenedAt is { } at && In(data.LocalDate(at), period));

    /// <summary>Sessions saved or reopened in the period.</summary>
    private static IEnumerable<SessionSnapshot> SessionsIn(LibrarySnapshot data, (DateOnly From, DateOnly To)? period)
        => period is null
            ? data.Sessions
            : data.Sessions.Where(s => In(data.LocalDate(s.CreatedAt), period) || s.OpenedAt.Any(o => In(data.LocalDate(o), period)));

    /// <summary>
    /// Folders visited in the period, from the kept detail, summed across
    /// days. Detail older than the kept window is ignored even before Recents
    /// prunes it, so what is listed doesn't depend on when pruning last ran.
    /// </summary>
    private static IEnumerable<FolderActivity> FoldersIn(LibrarySnapshot data, (DateOnly From, DateOnly To)? period)
        => data.Roots
            .SelectMany(r => r.FolderDays)
            .Where(d => d.Date >= data.FolderDetailKeptFrom && In(d.Date, period))
            .SelectMany(d => d.Folders)
            .GroupBy(f => f.Folder, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FolderActivity(g.First().Folder, TimeSpan.FromTicks(g.Sum(f => f.Time.Ticks)), g.Sum(f => f.Visits), g.Max(f => f.LastVisited)));

    /// <summary>Files opened in the period, with how often and when last.</summary>
    private static IEnumerable<RecentFileSummary> FilesIn(LibrarySnapshot data, (DateOnly From, DateOnly To)? period)
    {
        foreach (var file in data.Files)
        {
            var opens = file.Opens.Where(o => In(data.LocalDate(o), period)).ToList();
            if (opens.Count > 0)
                yield return new RecentFileSummary(file.Path, file.Kind, opens.Count, opens.Max());
        }
    }

    private static IReadOnlyList<string> PeriodNotes(LibrarySnapshot data, LibraryFilter filter, (DateOnly From, DateOnly To)? period, CultureInfo culture)
    {
        var notes = new List<string>();
        if (period is not { } p)
            return notes;

        string Day(DateOnly date) => date.ToDateTime(TimeOnly.MinValue).ToString("d MMM yyyy", culture);

        if ((filter.Kind is null or LibraryKind.Folder or LibraryKind.Link) && data.Places.Count > 0)
            notes.Add("Saved places are listed for a period only if it holds their last open.");
        if ((filter.Kind is null or LibraryKind.Folder) && data.Roots.Count > 0 && p.From < data.FolderDetailKeptFrom)
            notes.Add($"Folders visited before {Day(data.FolderDetailKeptFrom)} aren't listed: Recents keeps which folders were visited for {ActivityStore.DetailDays} days.");
        if (filter.Kind is not (LibraryKind.Folder or LibraryKind.Link) &&
            data.RecentFilesSettings.TrackingStartedAt is { } started && p.From < data.LocalDate(started))
            notes.Add($"Recent Files started recording on {Day(data.LocalDate(started))}.");

        return notes;
    }

    // ---------------------------------------------------------------
    // Heat
    // ---------------------------------------------------------------

    private static void Add(Dictionary<DateOnly, HeatDay> heat, DateOnly date, int visits = 0, int opens = 0, int saved = 0,
        int reopens = 0, bool foldersUnknown = false)
    {
        var day = heat.GetValueOrDefault(date) ?? new HeatDay(0, 0, 0, 0);
        heat[date] = new HeatDay(day.FolderVisits + visits, day.FileOpens + opens, day.SessionsSaved + saved,
            day.SessionsReopened + reopens, day.FoldersUnknown || foldersUnknown);
    }

    private static SourceCoverage AddFolderHeat(LibrarySnapshot data, LibraryFilter filter,
        IReadOnlyDictionary<string, LibraryItem> everything, Dictionary<DateOnly, HeatDay> heat, List<DateOnly> starts)
    {
        // Folders carry no Session tags, and a document or link filter leaves them out.
        if (filter.Kind is not (null or LibraryKind.Folder) || filter.Tag is not null)
            return new SourceCoverage(FolderSource, CoverageState.NotApplicable);
        if (!data.RecentsAvailable)
            return new SourceCoverage(FolderSource, CoverageState.Unavailable, data.RecentsNotice ?? "Recents couldn't be read.");
        if (data.Roots.Count == 0)
            return new SourceCoverage(FolderSource, CoverageState.Unavailable, "No folders are tracked in Recents.");

        starts.AddRange(data.Roots.Select(r => r.TrackingStartedOn));

        // A Saved filter narrows folder visits too, to saved places' folders; a Recent filter doesn't: every visit is recent.
        var unfiltered = string.IsNullOrWhiteSpace(filter.Text) && filter.Source != LibrarySourceFilter.Saved;
        if (unfiltered)
        {
            foreach (var root in data.Roots)
            foreach (var (date, total) in root.DayTotals)
                Add(heat, date, visits: total.Visits);
        }
        else
        {
            foreach (var root in data.Roots)
            foreach (var day in root.FolderDays.Where(d => d.Date >= data.FolderDetailKeptFrom))
            foreach (var folder in day.Folders)
            {
                if (everything.TryGetValue(ResourceIdentity.Key(LibraryKind.Folder, folder.Folder), out var item) && Passes(item, filter))
                    Add(heat, day.Date, visits: Math.Max(1, folder.Visits));
            }

            // Days whose totals outlive their detail: visits happened, but to which folders is no longer known.
            foreach (var root in data.Roots)
            foreach (var (date, total) in root.DayTotals)
            {
                if (date < data.FolderDetailKeptFrom && total.Visits > 0)
                    Add(heat, date, foldersUnknown: true);
            }
        }

        var state = CoverageState.Available;
        string? reason = null;
        if (!unfiltered)
        {
            state = CoverageState.Partial;
            reason = $"Recents keeps which folders were visited for {ActivityStore.DetailDays} days, so earlier visits can't be searched or narrowed to saved places.";
        }
        else if (data.Roots.All(r => !r.Enabled))
        {
            state = CoverageState.Partial;
            reason = "Tracking is off for every folder in Recents, so nothing new is counted.";
        }

        return new SourceCoverage(FolderSource, state, reason);
    }

    private static SourceCoverage AddFileHeat(LibrarySnapshot data, LibraryFilter filter,
        IReadOnlyDictionary<string, LibraryItem> everything, Dictionary<DateOnly, HeatDay> heat, List<DateOnly> starts)
    {
        if (filter.Kind is LibraryKind.Folder or LibraryKind.Link)
            return new SourceCoverage(FileSource, CoverageState.NotApplicable);
        if (!data.RecentFilesAvailable)
            return new SourceCoverage(FileSource, CoverageState.Unavailable, data.RecentFilesNotice ?? "Recent Files couldn't be read.");

        var settings = data.RecentFilesSettings;
        if (settings.TrackingStartedAt is { } started)
            starts.Add(data.LocalDate(started));
        if (!settings.Enabled && data.Files.Count == 0)
            return new SourceCoverage(FileSource, CoverageState.Unavailable, "Recent Files is off.");

        foreach (var file in data.Files)
        {
            var kind = LibraryKinds.From(file.Kind);
            if (filter.Kind is { } only && only != kind)
                continue;
            if (!filter.OnlyKind && !(everything.TryGetValue(ResourceIdentity.Key(kind, file.Path), out var item) && Passes(item, filter)))
                continue;

            foreach (var open in file.Opens)
                Add(heat, data.LocalDate(open), opens: 1);
        }

        if (!settings.Enabled)
            return new SourceCoverage(FileSource, CoverageState.Partial, "Recent Files is off, so files opened since it was turned off aren't counted.");
        if (settings.ResumedAt is { } resumed && settings.TrackingStartedAt is { } first && resumed > first)
            return new SourceCoverage(FileSource, CoverageState.Partial,
                $"Recent Files was off for a while before {data.LocalDate(resumed):d MMM yyyy}; files opened then aren't counted.");
        return new SourceCoverage(FileSource, CoverageState.Available);
    }

    private static SourceCoverage AddSessionHeat(LibrarySnapshot data, LibraryFilter filter,
        IReadOnlyDictionary<string, LibraryItem> everything, Dictionary<DateOnly, HeatDay> heat, List<DateOnly> starts)
    {
        // Sessions hold documents, and are saved items, never recent ones.
        if (filter.Kind is LibraryKind.Folder or LibraryKind.Link || filter.Source == LibrarySourceFilter.Recent)
            return new SourceCoverage(SessionSource, CoverageState.NotApplicable);

        foreach (var session in data.Sessions)
        {
            if (filter.Tag is not null && !session.Tags.Contains(filter.Tag, StringComparer.OrdinalIgnoreCase))
                continue;

            var fileFilter = filter with { Tag = null };
            var counts = session.Files.Any(path =>
                DocumentKinds.FromPath(path) is { } kind &&
                everything.TryGetValue(ResourceIdentity.Key(LibraryKinds.From(kind), path), out var item) &&
                Passes(item, fileFilter));
            if (!counts)
                continue;

            starts.Add(data.LocalDate(session.CreatedAt));
            Add(heat, data.LocalDate(session.CreatedAt), saved: 1);
            foreach (var opened in session.OpenedAt)
                Add(heat, data.LocalDate(opened), reopens: 1);
        }

        return new SourceCoverage(SessionSource, CoverageState.Available,
            "A session's saves and reopens are counted, not opens of each of its files.");
    }
}
