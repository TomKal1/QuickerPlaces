using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Models.RecentFiles;
using QuickerPlaces.Models.Workspace;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Library;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.ViewModels;

/// <summary>How the Library's rows are grouped.</summary>
public enum LibraryGrouping
{
    /// <summary>Folders, Links, PDFs, Word, Excel.</summary>
    Type,

    /// <summary>One group per session tag, then "No tag". An item with two tags is in both.</summary>
    Tag,

    /// <summary>By depth below the tracked folder that holds each item, then "Not in a tracked folder" (Desk layout design §4).</summary>
    Level,
}

/// <summary>The File viewer's tabs that show the Library (File viewer design §4). Its Saved places tab is the places table.</summary>
public enum LibraryTab
{
    Recent,
    Sessions,
    All,
}

/// <summary>How much a click on the year strip chooses (configurable canvas plan D1: Day/Week/Month selection).</summary>
public enum CalendarSelectionUnit
{
    Day,
    Week,
    Month,
}

/// <summary>
/// Backs the Library window and the workspace's Year activity and File shelf
/// panels (documents plan §6; configurable canvas plan D4, D5, M2):
/// everything QuickerPlaces knows about, together — saved places, files in
/// saved sessions, folders from Recents and files from Recent Files — split
/// by kind, filtered to saved or recent, to a Session tag and by search,
/// grouped by type, tag or folder level, for a chosen period, with a year strip of the
/// activity each source recorded.
///
/// The query is one explicit thing (D4): <see cref="CurrentQuery"/> reads it
/// and <see cref="ApplyQuery"/> replaces it, and <see cref="QueryChanged"/>
/// says when the user changed it. Choosing a period changes the rows and the
/// selection outline, never the year's heat, which covers everything kept for
/// the other filters. What a source can't show is said in
/// <see cref="CoverageNotes"/> and <see cref="PeriodNotes"/>, never shown as
/// zero (D5).
///
/// Sources are read into a <see cref="LibrarySnapshot"/> on the UI thread
/// and queried through <see cref="IBackgroundWork"/>; a result that arrives
/// after a newer query started is dropped. The selected row is kept across
/// refreshes by the item's identity, not its position.
///
/// The four sources stay separate, each with its own switch; this only reads
/// them, except for Recent Files' own settings, which live here because this
/// is where Recent Files is seen. UI-free and linked into the test project;
/// the views only bind and pass clicks in.
/// </summary>
public sealed class LibraryViewModel : ObservableObject
{
    private readonly PlacesService _places;
    private readonly SessionStore _sessions;
    private readonly ActivityStore _activity;
    private readonly RecentFilesStore _recentFiles;
    private readonly PlaceLauncher _placeLauncher;
    private readonly IShell _shell;
    private readonly TimeProvider _time;
    private readonly CultureInfo _culture;
    private readonly IBackgroundWork _work;
    private LibrarySnapshot _snapshot;
    private LibraryQueryResult? _result;
    private int _generation;
    private LibraryKind? _selectedKind;
    private LibrarySourceFilter _source;
    private LibraryGrouping _grouping;
    private string _searchText = "";
    private string? _tag;
    private string? _rootId;
    private LibraryTab? _tab;
    private string? _sessionId;
    private string? _scopeStatus;
    private DateRule _date = DateRule.All();
    private CalendarSelectionUnit _selectionUnit;
    private DateOnly? _clickedDate;
    private int _calendarYear;
    private int _calendarMonth;
    private ActivityCalendarYearResult? _lastYear;
    private CalendarSource? _builtFrom;
    private string? _selectedKey;
    private LibraryRowViewModel? _selectedRow;
    private string? _statusMessage;
    private string? _errorMessage;

    public LibraryViewModel(PlacesService places, SessionStore sessions, ActivityStore activity, RecentFilesStore recentFiles,
        PlaceLauncher placeLauncher, IShell shell, TimeProvider? time = null, CultureInfo? culture = null, IBackgroundWork? work = null)
    {
        _places = places;
        _sessions = sessions;
        _activity = activity;
        _recentFiles = recentFiles;
        _placeLauncher = placeLauncher;
        _shell = shell;
        _time = time ?? TimeProvider.System;
        _culture = culture ?? CultureInfo.CurrentCulture;
        _work = work ?? InlineBackgroundWork.Instance;
        _calendarYear = Today().Year;
        _calendarMonth = Today().Month;
        _snapshot = Capture();
        BuildRootChips();
        Refresh();
        OnRecentFilesSettingsChanged();
    }

    /// <summary>Raised after a saved place was opened from here, so the main grid can show its new Last Opened.</summary>
    public event Action<Place, PersistenceResult>? PlaceOpened;

    /// <summary>Raised when the user changes the query (not by <see cref="ApplyQuery"/>), so the workspace can remember it.</summary>
    public event Action? QueryChanged;

    // ---------------------------------------------------------------
    // The query (D4)
    // ---------------------------------------------------------------

    /// <summary>The query as the workspace stores it.</summary>
    public WorkspaceQuery CurrentQuery => new()
    {
        Text = _searchText,
        Kind = _selectedKind?.ToString().ToLowerInvariant(),
        Source = _source switch
        {
            LibrarySourceFilter.Saved => "saved",
            LibrarySourceFilter.Recent => "recent",
            LibrarySourceFilter.Sessions => "sessions",
            _ => null,
        },
        Tag = _tag,
        Root = _rootId,
        Date = _date.Clone(),
    };

    /// <summary>
    /// Replaces the whole query at once — a layout's saved filters, or the
    /// defaults — with one refresh. A kind or source this build doesn't know
    /// reads as all. Does not raise <see cref="QueryChanged"/>.
    /// </summary>
    public void ApplyQuery(WorkspaceQuery query)
    {
        _searchText = query.Text ?? "";
        _selectedKind = Enum.TryParse<LibraryKind>(query.Kind, ignoreCase: true, out var kind) ? kind : null;
        _source = query.Source switch
        {
            "saved" => LibrarySourceFilter.Saved,
            "recent" => LibrarySourceFilter.Recent,
            "sessions" => LibrarySourceFilter.Sessions,
            _ => LibrarySourceFilter.All,
        };

        // While the File viewer shows a tab, the tab decides the source (File viewer design §4).
        if (_tab is { } shown)
            _source = SourceOf(shown);

        _tag =string.IsNullOrWhiteSpace(query.Tag) ? null : query.Tag;
        _rootId = string.IsNullOrWhiteSpace(query.Root) ? null : query.Root;
        _date = query.Date?.Clone() ?? DateRule.All();

        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(SelectedKind));
        NotifySource();
        OnPropertyChanged(nameof(Tag));
        OnPropertyChanged(nameof(TagChoice));
        NotifyPeriod();
        BuildRootChips();
        Refresh();
    }

    // ---------------------------------------------------------------
    // Filters
    // ---------------------------------------------------------------

    /// <summary>"All" and then each kind, with how many items of it pass the other filters.</summary>
    public ObservableCollection<LibraryKindFilter> KindFilters { get; } = new();

    /// <summary>The kind shown, or null for all.</summary>
    public LibraryKind? SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (SetProperty(ref _selectedKind, value))
                QueryEdited();
        }
    }

    public LibrarySourceFilter Source
    {
        get => _source;
        set
        {
            if (!SetProperty(ref _source, value))
                return;
            NotifySource();
            QueryEdited();
        }
    }

    public bool IsSourceAll { get => _source == LibrarySourceFilter.All; set { if (value) Source = LibrarySourceFilter.All; } }
    public bool IsSourceSaved { get => _source == LibrarySourceFilter.Saved; set { if (value) Source = LibrarySourceFilter.Saved; } }
    public bool IsSourceRecent { get => _source == LibrarySourceFilter.Recent; set { if (value) Source = LibrarySourceFilter.Recent; } }

    public LibraryGrouping Grouping
    {
        get => _grouping;
        set
        {
            if (!SetProperty(ref _grouping, value))
                return;
            OnPropertyChanged(nameof(IsGroupedByType));
            OnPropertyChanged(nameof(IsGroupedByTag));
            OnPropertyChanged(nameof(IsGroupedByLevel));
            BuildRows();
        }
    }

    public bool IsGroupedByType { get => _grouping == LibraryGrouping.Type; set { if (value) Grouping = LibraryGrouping.Type; } }
    public bool IsGroupedByTag { get => _grouping == LibraryGrouping.Tag; set { if (value) Grouping = LibraryGrouping.Tag; } }
    public bool IsGroupedByLevel { get => _grouping == LibraryGrouping.Level; set { if (value) Grouping = LibraryGrouping.Level; } }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? ""))
                QueryEdited();
        }
    }

    /// <summary>An existing Session tag to narrow to (D4: tags, never inferred projects), or null for any.</summary>
    public string? Tag
    {
        get => _tag;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == AnyTag)
                value = null;
            if (!SetProperty(ref _tag, value))
                return;
            OnPropertyChanged(nameof(TagChoice));
            QueryEdited();
        }
    }

    /// <summary>The tag picker's item: the tag, or <see cref="AnyTag"/>.</summary>
    public string TagChoice
    {
        get => _tag ?? AnyTag;
        set => Tag = value;
    }

    /// <summary>What the tag picker shows for no tag.</summary>
    public const string AnyTag = "Any tag";

    /// <summary>"Any tag", then every Session tag in use, for the tag picker.</summary>
    public IReadOnlyList<string> TagChoices
        => new[] { AnyTag }.Concat(_snapshot.Sessions.SelectMany(s => s.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)).ToList();

    /// <summary>Recents' tracked folders, as chips that scope the list (Desk layout design §4).</summary>
    public ObservableCollection<TrackedRootChip> TrackedRootChips { get; } = new();

    public bool HasTrackedRoots => TrackedRootChips.Count > 0;

    /// <summary>The tracked folder the list is narrowed to, by root id, or null. A root that no longer exists scopes nothing.</summary>
    public string? RootScope
    {
        get => _rootId;
        set
        {
            if (!SetProperty(ref _rootId, value))
                return;
            BuildRootChips();
            QueryEdited();
        }
    }

    /// <summary>A chip's click: narrows to that tracked folder, or back to everything when it already is.</summary>
    public void ToggleRootScope(string rootId) => RootScope = _rootId == rootId ? null : rootId;

    // ---------------------------------------------------------------
    // The File viewer's tabs (File viewer design §4, §5)
    // ---------------------------------------------------------------

    /// <summary>
    /// The File viewer tab shown, or null outside it: a Recents panel and the
    /// Library window, where every column shows as before. A tab decides the
    /// source. Leaving Sessions clears a session scope. Sessions has no
    /// Folder level, so that grouping falls back to Type there.
    /// </summary>
    public LibraryTab? Tab
    {
        get => _tab;
        set
        {
            if (_tab == value)
                return;

            _tab = value;
            if (value != LibraryTab.Sessions)
            {
                _sessionId = null;
                ClearScopeStatus();
            }
            if (value == LibraryTab.Sessions && _grouping == LibraryGrouping.Level)
                Grouping = LibraryGrouping.Type;

            // No tab: a Recents panel's Show segment has no Sessions choice, so that source reads as All there.
            var source = value is { } tab ? SourceOf(tab) : _source == LibrarySourceFilter.Sessions ? LibrarySourceFilter.All : _source;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowsSourceChoice));
            OnPropertyChanged(nameof(ShowsTrackedFolders));
            OnPropertyChanged(nameof(ShowsWhereFrom));
            OnPropertyChanged(nameof(ShowsOpens));
            OnPropertyChanged(nameof(ShowsVisitsAndTime));
            OnPropertyChanged(nameof(ShowsTags));
            OnPropertyChanged(nameof(ShowsSessions));
            OnPropertyChanged(nameof(ShowsSourceMarkers));
            OnPropertyChanged(nameof(ShowsFolderLevel));
            NotifySessionScope();

            if (source != _source)
            {
                _source = source;
                NotifySource();
                QueryEdited();
            }
            else
            {
                Refresh();
            }
        }
    }

    /// <summary>The Show segment (All / Saved / Recent): the tabs replace it.</summary>
    public bool ShowsSourceChoice => _tab is null;

    /// <summary>The tracked folders strip: Recent's.</summary>
    public bool ShowsTrackedFolders => _tab is null or LibraryTab.Recent;

    public bool ShowsWhereFrom => _tab is null or LibraryTab.All;

    /// <summary>The Opens column: the All tab's count of opens (files) or visits (folders), as Saved places has Opens. Recent has Visits and Time.</summary>
    public bool ShowsOpens => _tab == LibraryTab.All;

    public bool ShowsVisitsAndTime => _tab is null or LibraryTab.Recent;

    public bool ShowsTags => _tab != LibraryTab.Recent;

    /// <summary>Which sessions hold each file: the Sessions tab's column.</summary>
    public bool ShowsSessions => _tab == LibraryTab.Sessions;

    /// <summary>The Source markers (saved place, recent, in a session): the All tab's column.</summary>
    public bool ShowsSourceMarkers => _tab == LibraryTab.All;

    /// <summary>Folder level grouping: sessions hold files from anywhere, so not on Sessions.</summary>
    public bool ShowsFolderLevel => _tab != LibraryTab.Sessions;

    /// <summary>The session the Sessions tab is narrowed to, by id, or null for every session's files (File viewer design §5).</summary>
    public string? SessionScope => _sessionId;

    /// <summary>The scoped session's name now: a rename keeps the scope, and a deleted session scopes nothing.</summary>
    private string? SessionScopeName
        => _sessionId is null ? null : _snapshot.Sessions.FirstOrDefault(s => s.Id == _sessionId)?.Name;

    public bool HasSessionScope => SessionScopeName is not null;

    /// <summary>"Session: Tower B": the chip that clears the scope.</summary>
    public string SessionScopeText => SessionScopeName is { } name ? $"Session: {name}" : "";

    /// <summary>A session card's View session files: the Sessions tab, narrowed to that session.</summary>
    public void ScopeToSession(string sessionId)
    {
        Tab = LibraryTab.Sessions;
        _sessionId = sessionId;
        NotifySessionScope();
        Refresh();
        _scopeStatus = SessionScopeName is { } name ? $"Showing the files in {name}." : null;
        StatusMessage = _scopeStatus;
    }

    /// <summary>Takes the scope's status line down with the scope, unless another message has replaced it since.</summary>
    private void ClearScopeStatus()
    {
        if (_scopeStatus is not null && StatusMessage == _scopeStatus)
            StatusMessage = null;

        _scopeStatus = null;
    }

    /// <summary>The chip's ×: every session's files again.</summary>
    public void ClearSessionScope()
    {
        if (_sessionId is null)
            return;

        _sessionId = null;
        ClearScopeStatus();
        NotifySessionScope();
        Refresh();
    }

    private static LibrarySourceFilter SourceOf(LibraryTab tab) => tab switch
    {
        LibraryTab.Recent => LibrarySourceFilter.Recent,
        LibraryTab.Sessions => LibrarySourceFilter.Sessions,
        _ => LibrarySourceFilter.All,
    };

    private void NotifySessionScope()
    {
        OnPropertyChanged(nameof(SessionScope));
        OnPropertyChanged(nameof(HasSessionScope));
        OnPropertyChanged(nameof(SessionScopeText));
    }

    // ---------------------------------------------------------------
    // Period (D3, D4)
    // ---------------------------------------------------------------

    /// <summary>The date rule: all recorded time, this week or month, or chosen days.</summary>
    public DateRule Date => _date.Clone();

    /// <summary>The inclusive days listed, resolved from <see cref="Date"/> today, or null for everything kept.</summary>
    public (DateOnly From, DateOnly To)? Period => _date.Resolve(_time, _culture);

    public bool HasPeriod => Period is not null;

    /// <summary>"Mon 28 Sep 2026", "21–27 Sep 2026", "This week (…)", "This month (…)": the chip that clears the period.</summary>
    public string PeriodText
    {
        get
        {
            if (Period is not { } p)
                return "";
            var days = FormatDays(p.From, p.To, _culture);
            return _date.Kind switch
            {
                DateRuleKind.ThisWeek => $"This week ({days})",
                DateRuleKind.ThisMonth => $"This month ({days})",
                _ => days,
            };
        }
    }

    /// <summary>
    /// Whether a click on the year strip chooses a day, its week or its
    /// month. Changing it while a period is chosen chooses the day, week or
    /// month around the day last clicked (or the period's first day, for one
    /// that came from a saved query), so Day, then Week, then Day again comes
    /// back to the same day. All time, This week and This month stay as they are.
    /// </summary>
    public CalendarSelectionUnit SelectionUnit
    {
        get => _selectionUnit;
        set
        {
            if (!SetProperty(ref _selectionUnit, value))
                return;
            OnPropertyChanged(nameof(IsUnitDay));
            OnPropertyChanged(nameof(IsUnitWeek));
            OnPropertyChanged(nameof(IsUnitMonth));

            if (_date.Kind != DateRuleKind.Range || Period is not { } period)
                return;
            var anchor = _clickedDate is { } clicked && clicked >= period.From && clicked <= period.To ? clicked : period.From;
            var (from, to) = PeriodAround(anchor);
            SetDateRule(DateRule.Between(from, to));
        }
    }

    public bool IsUnitDay { get => _selectionUnit == CalendarSelectionUnit.Day; set { if (value) SelectionUnit = CalendarSelectionUnit.Day; } }
    public bool IsUnitWeek { get => _selectionUnit == CalendarSelectionUnit.Week; set { if (value) SelectionUnit = CalendarSelectionUnit.Week; } }
    public bool IsUnitMonth { get => _selectionUnit == CalendarSelectionUnit.Month; set { if (value) SelectionUnit = CalendarSelectionUnit.Month; } }

    /// <summary>
    /// Chooses the day, week or month (<see cref="SelectionUnit"/>) holding
    /// <paramref name="date"/>; choosing exactly the current period again
    /// clears it. Future and untracked days can be chosen: the list then says
    /// why it is empty (D5).
    /// </summary>
    public void SelectCalendarDate(DateOnly date)
    {
        var (from, to) = PeriodAround(date);
        _clickedDate = date;
        SetDateRule(Period == (from, to) && _date.Kind == DateRuleKind.Range ? DateRule.All() : DateRule.Between(from, to));
    }

    /// <summary>The day, week or month (<see cref="SelectionUnit"/>) holding <paramref name="date"/>.</summary>
    private (DateOnly From, DateOnly To) PeriodAround(DateOnly date) => _selectionUnit switch
    {
        CalendarSelectionUnit.Week => WeekOf(date),
        CalendarSelectionUnit.Month => (new DateOnly(date.Year, date.Month, 1), new DateOnly(date.Year, date.Month, 1).AddMonths(1).AddDays(-1)),
        _ => (date, date),
    };

    /// <summary>Sets the date rule: This week and This month stay relative, and move with the clock (D3).</summary>
    public void SetDateRule(DateRule rule)
    {
        if (rule.SameAs(_date))
            return;
        _date = rule.Clone();
        NotifyPeriod();
        QueryEdited();
    }

    /// <summary>Back to all recorded time.</summary>
    public void ClearPeriod() => SetDateRule(DateRule.All());

    // ---------------------------------------------------------------
    // Rows
    // ---------------------------------------------------------------

    /// <summary>The rows shown, in group order; each carries its group's name for the view to group on.</summary>
    public ObservableCollection<LibraryRowViewModel> Rows { get; } = new();

    /// <summary>The selected row. Kept across refreshes by the item's identity, so a reload doesn't lose it.</summary>
    public LibraryRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!SetProperty(ref _selectedRow, value))
                return;
            if (value is not null)
                _selectedKey = value.Item.Key;
        }
    }

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>What to say in place of an empty list: why nothing is there, not just that nothing is (D5).</summary>
    public string EmptyText
    {
        get
        {
            if (Period is null && _tab == LibraryTab.Sessions && _snapshot.Sessions.All(s => s.Files.Count == 0))
                return "No session files yet. Save files as a session from the Sessions panel.";
            if (Period is null && _tab == LibraryTab.Recent && _snapshot.Roots.Count == 0 && _snapshot.Files.Count == 0)
                return "Nothing recent yet. Track a folder above, or turn on Recent Files.";
            var inPeriod = _result?.ItemsInPeriod ?? 0;
            if (Period is not { } p)
                return inPeriod == 0
                    ? "Nothing here yet. Save places, save open files as a session, or turn on Recents or Recent Files."
                    : "Nothing matches. Clear the search, choose All, or pick another day.";

            if (inPeriod > 0)
                return "Nothing in this period matches. Clear the search or choose All.";
            var today = Today();
            if (p.From > today)
                return "That's still to come, so nothing has been used then yet.";
            if (_result is { } result && p.To < result.TrackingStartedOn)
                return $"Nothing was being recorded then: the earliest record starts on {FormatDays(result.TrackingStartedOn, result.TrackingStartedOn, _culture)}.";
            return "Nothing was recorded as used in this period." + (PeriodNotes.Count > 0 ? " " + string.Join(" ", PeriodNotes) : "");
        }
    }

    /// <summary>What the list can't include for this period, as sentences. Empty when nothing is missing.</summary>
    public IReadOnlyList<string> PeriodNotes => _result?.PeriodNotes ?? Array.Empty<string>();

    public string PeriodNotesText => string.Join(" ", PeriodNotes);

    // ---------------------------------------------------------------
    // Year strip
    // ---------------------------------------------------------------

    public ObservableCollection<ActivityCalendarWeek> CalendarWeeks { get; } = new();
    public ObservableCollection<ActivityCalendarMonthMarker> CalendarMonthMarkers { get; } = new();
    public IReadOnlyList<string> CalendarWeekdayLabels { get; private set; } = Array.Empty<string>();
    public int CalendarStripWidth { get; private set; }
    /// <summary>The earliest year the strip can show.</summary>
    public const int FirstCalendarYear = 2000;

    /// <summary>The latest year the strip can show.</summary>
    public const int LastCalendarYear = 2100;

    public int CalendarYear => _calendarYear;
    public bool CanShowPreviousYear => _calendarYear > FirstCalendarYear;
    public bool CanShowNextYear => _calendarYear < LastCalendarYear;
    public string CalendarYearLabel => $"{_calendarYear}";

    /// <summary>What the strip counts for the current kind filter, as its caption: recorded activity, never "files used" (D5).</summary>
    public string CalendarCaption => _selectedKind switch
    {
        LibraryKind.Folder => "Folder visits from Recents",
        LibraryKind.Link => "Links keep no history, only when each was last opened",
        LibraryKind.Pdf or LibraryKind.Word or LibraryKind.Excel => $"{_selectedKind.Value.PluralLabel()} opened (Recent Files), and sessions saved or reopened",
        _ => "Recorded activity: folder visits, files opened, and sessions saved or reopened",
    };

    /// <summary>Each source the strip counts, and how much of it: "Folder visits: partly counted — …".</summary>
    public IReadOnlyList<SourceCoverage> Coverage => _result?.Coverage ?? Array.Empty<SourceCoverage>();

    /// <summary>One line for each source the strip can't fully count, for under the strip. Empty when all are counted.</summary>
    public IReadOnlyList<string> CoverageNotes
        => Coverage.Where(c => c.State is CoverageState.Partial or CoverageState.Unavailable)
            .Select(c => c.State == CoverageState.Partial ? $"{c.Source}: partly counted. {c.Reason}" : $"{c.Source}: not counted. {c.Reason}")
            .ToList();

    public string CoverageNotesText => string.Join("\n", CoverageNotes);

    public bool SelectCalendarYear(int year)
    {
        if (year < FirstCalendarYear || year > LastCalendarYear || year == _calendarYear)
            return false;
        _calendarYear = year;
        OnPropertyChanged(nameof(CalendarYear));
        OnPropertyChanged(nameof(CanShowPreviousYear));
        OnPropertyChanged(nameof(CanShowNextYear));
        OnPropertyChanged(nameof(CalendarYearLabel));
        BuildCalendar();
        return true;
    }

    // ---------------------------------------------------------------
    // Month view (M4): the strip's fallback where a year doesn't fit
    // ---------------------------------------------------------------

    /// <summary>
    /// The weeks of one month of <see cref="CalendarYear"/>, laid out as the
    /// year strip lays them out, for a panel too narrow for the whole year
    /// (configurable canvas plan D1). Same cells, so the chosen period's
    /// outline and today's ring carry over.
    /// </summary>
    public ObservableCollection<ActivityCalendarWeek> CalendarMonthWeeks { get; } = new();

    /// <summary>1–12: the month the month view shows. Follows a chosen period that starts in the year shown.</summary>
    public int CalendarMonth => _calendarMonth;

    public string CalendarMonthLabel => new DateTime(_calendarYear, _calendarMonth, 1).ToString("MMMM yyyy", _culture);

    /// <summary>The month's own name, "September", for a header that shows the year separately.</summary>
    public string CalendarMonthName => new DateTime(_calendarYear, _calendarMonth, 1).ToString("MMMM", _culture);

    public bool CanShowNextMonth => new DateOnly(_calendarYear, _calendarMonth, 1) < new DateOnly(LastCalendarYear, 12, 1);

    public bool CanShowPreviousMonth => _calendarYear > FirstCalendarYear || _calendarMonth > 1;

    /// <summary>The month view's arrows: one month back or on, into the next or previous year when needed. Months still to come are allowed, from January 2000 to December 2100.</summary>
    public bool ShowCalendarMonth(int delta)
    {
        var first = new DateOnly(_calendarYear, _calendarMonth, 1).AddMonths(delta);
        if (first.Year < FirstCalendarYear || first.Year > LastCalendarYear)
            return false;

        var yearChanged = first.Year != _calendarYear;
        _calendarYear = first.Year;
        _calendarMonth = first.Month;
        if (yearChanged)
        {
            OnPropertyChanged(nameof(CalendarYear));
            OnPropertyChanged(nameof(CalendarYearLabel));
            OnPropertyChanged(nameof(CanShowPreviousYear));
            OnPropertyChanged(nameof(CanShowNextYear));
            BuildCalendar();
        }
        else
        {
            BuildMonth(_lastYear);
        }

        return true;
    }

    // ---------------------------------------------------------------
    // Recent Files settings
    // ---------------------------------------------------------------

    public bool RecentFilesAvailable => _recentFiles.IsAvailable;

    public string? RecentFilesNotice => _recentFiles.Notice;

    /// <summary>Recent Files on or off. Turning it on starts from now; turning it off keeps what was recorded.</summary>
    public bool RecentFilesEnabled
    {
        get => _recentFiles.Settings.Enabled;
        set
        {
            if (value == RecentFilesEnabled)
                return;
            Report(_recentFiles.SetEnabled(value), value ? "Recent Files is on. Files you open from now on will appear here within a minute." : "Recent Files is off. What it recorded is kept.");
            OnRecentFilesSettingsChanged();
            Reload();
        }
    }

    public bool TrackPdf { get => Tracks(DocumentKind.Pdf); set => SetTracked(DocumentKind.Pdf, value); }
    public bool TrackWord { get => Tracks(DocumentKind.Word); set => SetTracked(DocumentKind.Word, value); }
    public bool TrackExcel { get => Tracks(DocumentKind.Excel); set => SetTracked(DocumentKind.Excel, value); }

    /// <summary>True to record files anywhere; false (the default) for only under folders tracked in Recents.</summary>
    public bool TrackEverywhere
    {
        get => _recentFiles.Settings.Scope == RecentFilesScope.Everywhere;
        set
        {
            if (value == TrackEverywhere)
                return;
            Report(_recentFiles.SetScope(value ? RecentFilesScope.Everywhere : RecentFilesScope.TrackedFolders), null);
            OnRecentFilesSettingsChanged();
        }
    }

    /// <summary>The other side of <see cref="TrackEverywhere"/>, for the second radio button.</summary>
    public bool TrackUnderTrackedFolders
    {
        get => !TrackEverywhere;
        set
        {
            if (value)
                TrackEverywhere = false;
        }
    }

    /// <summary>One line about what Recent Files is doing.</summary>
    public string RecentFilesStatus
    {
        get
        {
            if (!_recentFiles.IsAvailable)
                return _recentFiles.Notice ?? "Recent Files is unavailable.";

            var settings = _recentFiles.Settings;
            if (!settings.Enabled)
                return "Recent Files is off. When on, it lists the PDF, Word and Excel files you open, from Windows' Recent Items, on this computer only.";

            var kinds = string.Join(", ", settings.Kinds.Select(k => k.PluralLabel()));
            var where = settings.Scope == RecentFilesScope.Everywhere
                ? "anywhere"
                : _activity.EnabledRoots().Count == 0
                    ? "under folders tracked in Recents — none are tracked yet, so nothing is recorded"
                    : "under folders tracked in Recents";
            return $"Recording {kinds} you open {where}.";
        }
    }

    public string ClearRecentFilesConfirmation => "Delete everything Recent Files has recorded?\n\nYour saved places, sessions and the files themselves are not touched.";

    public void ClearRecentFiles()
    {
        Report(_recentFiles.ClearHistory(), "Recent Files history deleted.");
        Reload();
    }

    /// <summary>Removes one file from Recent Files' history (not from sessions, and not from disk).</summary>
    public void Forget(LibraryRowViewModel? row)
    {
        if (row is null || row.Item.Kind.ToDocumentKind() is null || !row.Item.IsRecent)
            return;
        Report(_recentFiles.Forget(row.Item.Location), $"Removed \"{row.Name}\" from Recent Files.");
        Reload();
    }

    // ---------------------------------------------------------------
    // Actions and messages
    // ---------------------------------------------------------------

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    /// <summary>
    /// Opens a row: a saved place through PlaceLauncher, so it counts as a
    /// place open exactly as from the main grid, and the list is read again
    /// to show it; anything else is checked and handed to Windows, and
    /// counts as nothing.
    /// </summary>
    public void Open(LibraryRowViewModel? row)
    {
        if (row is null)
            return;

        StatusMessage = null;
        ErrorMessage = null;
        var item = row.Item;

        if (item.Place is { } place)
        {
            var outcome = _placeLauncher.Open(place);
            switch (outcome.Status)
            {
                case OpenStatus.Missing:
                    ErrorMessage = $"This folder no longer exists: {place.Resource}";
                    return;
                case OpenStatus.Failed:
                    ErrorMessage = $"Couldn't open \"{place.Alias}\": {outcome.ErrorMessage}";
                    return;
            }

            StatusMessage = $"Opened \"{place.Alias}\".";
            if (!outcome.Persistence.Saved)
                ErrorMessage = outcome.Persistence.UserMessage;
            PlaceOpened?.Invoke(place, outcome.Persistence);
            Reload();
            return;
        }

        var exists = item.Kind == LibraryKind.Folder ? _shell.DirectoryExists(item.Location) : _shell.FileExists(item.Location);
        if (!exists)
        {
            ErrorMessage = $"\"{item.Name}\" couldn't be found at {item.Location}.";
            return;
        }

        try
        {
            _shell.Open(item.Location);
            StatusMessage = $"Opened \"{item.Name}\".";
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Opening a {item.Kind} from the Library failed ({ex.GetType().Name}).");
            ErrorMessage = $"Windows couldn't open \"{item.Name}\": {ex.Message}";
        }
    }

    /// <summary>Reads all four sources again and refreshes the rows and the year strip: after a launch, a change to a source, or new tracking.</summary>
    public void Reload()
    {
        _snapshot = Capture();
        BuildRootChips();
        if (_sessionId is not null && SessionScopeName is null)
        {
            _sessionId = null;
            ClearScopeStatus();
        }
        NotifySessionScope();
        OnPropertyChanged(nameof(TagChoices));
        Refresh();
    }

    // ---------------------------------------------------------------
    // Building
    // ---------------------------------------------------------------

    private LibrarySnapshot Capture() => LibrarySnapshot.Capture(_places, _sessions, _activity, _recentFiles, _time);

    private RootScope? ScopeFor(string? rootId)
        => rootId is not null && _snapshot.Roots.FirstOrDefault(r => r.RootId == rootId) is { } root
            ? new RootScope(root.RootId, root.Path)
            : null;

    private void BuildRootChips()
    {
        TrackedRootChips.Clear();
        foreach (var root in _snapshot.Roots)
            TrackedRootChips.Add(new TrackedRootChip(root.RootId, root.Path, root.Enabled, root.RootId == _rootId));
        OnPropertyChanged(nameof(HasTrackedRoots));
    }

    private void QueryEdited()
    {
        Refresh();
        QueryChanged?.Invoke();
    }

    /// <summary>Runs the query on the current snapshot; a result overtaken by a newer query is dropped.</summary>
    private void Refresh()
    {
        var generation = ++_generation;
        var snapshot = _snapshot;
        var filter = new LibraryFilter(_selectedKind, _source, _searchText, _tag, ScopeFor(_rootId), SessionScopeName);
        var period = Period;
        var culture = _culture;
        _work.Run(() => LibraryQueryEngine.Run(snapshot, filter, period, culture), result =>
        {
            if (generation != _generation)
                return;
            _result = result;
            BuildRows();

            // The strip is hundreds of cells: tearing it down on every keystroke made typing stall, and most searches leave the year's activity as it was.
            if (!CalendarIsCurrent(result))
                BuildCalendar();
            OnPropertyChanged(nameof(CalendarCaption));
            OnPropertyChanged(nameof(Coverage));
            OnPropertyChanged(nameof(CoverageNotes));
            OnPropertyChanged(nameof(CoverageNotesText));
            OnPropertyChanged(nameof(PeriodNotes));
            OnPropertyChanged(nameof(PeriodNotesText));
        });
    }

    private void BuildRows()
    {
        var passing = _result?.Items ?? Array.Empty<LibraryItem>();
        var selected = _selectedKind;
        KindFilters.Clear();
        KindFilters.Add(new LibraryKindFilter(null, $"All ({passing.Count})", selected is null));
        foreach (var kind in LibraryKinds.All)
            KindFilters.Add(new LibraryKindFilter(kind, $"{kind.PluralLabel()} ({passing.Count(i => i.Kind == kind)})", selected == kind));

        var shown = passing.Where(i => selected is null || i.Kind == selected).ToList();
        var rows = new List<LibraryRowViewModel>();
        var zone = _time.LocalTimeZone;
        if (_grouping == LibraryGrouping.Type)
        {
            foreach (var item in shown.OrderBy(i => i.Kind))
                rows.Add(new LibraryRowViewModel(item, item.Kind.PluralLabel(), (int)item.Kind, zone, _culture));
        }
        else if (_grouping == LibraryGrouping.Level)
        {
            var roots = _snapshot.Roots.Select(r => r.Path).Where(p => p.Length > 0).ToList();
            var placed = shown.Select(item =>
            {
                var root = item.TreePath.Length == 0 ? null : TrackedFolderPaths.RootFor(roots, item.TreePath);
                return (Item: item, Level: root is null ? null : TrackedFolderPaths.LevelBelow(root, item.TreePath));
            }).ToList();

            // OrderBy is stable: within a level, most recently used first, as elsewhere.
            foreach (var (item, level) in placed.Where(p => p.Level is not null).OrderBy(p => p.Level))
                rows.Add(new LibraryRowViewModel(item, TrackedFolderPaths.LevelLabel(level!.Value), level.Value, zone, _culture));
            foreach (var (item, _) in placed.Where(p => p.Level is null))
                rows.Add(new LibraryRowViewModel(item, TrackedFolderPaths.NotTracked, int.MaxValue, zone, _culture));
        }
        else
        {
            var tags = shown.SelectMany(i => i.Tags).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToList();
            for (var index = 0; index < tags.Count; index++)
            {
                foreach (var item in shown.Where(i => i.Tags.Contains(tags[index], StringComparer.OrdinalIgnoreCase)))
                    rows.Add(new LibraryRowViewModel(item, tags[index], index, zone, _culture));
            }

            foreach (var item in shown.Where(i => i.Tags.Count == 0))
                rows.Add(new LibraryRowViewModel(item, "No tag", tags.Count, zone, _culture));
        }

        // A refresh that changes nothing shown leaves the rows alone, so the list keeps its scroll position and selection.
        if (rows.Count != Rows.Count || !rows.Zip(Rows).All(pair => pair.First.Looks(pair.Second)))
        {
            var keep = _selectedKey;
            Rows.Clear();
            foreach (var row in rows)
                Rows.Add(row);

            // The same item keeps the selection, wherever the refresh put it; the key survives a row that is gone for now.
            SelectedRow = keep is null ? null : Rows.FirstOrDefault(r => ResourceIdentity.Comparer.Equals(r.Item.Key, keep));
            _selectedKey = keep;
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    /// <summary>True when the strip on screen was built from what <paramref name="result"/> and the current period and year would build: nothing in it would change.</summary>
    private bool CalendarIsCurrent(LibraryQueryResult result)
    {
        if (_builtFrom is not { } built)
            return false;

        return built.Year == _calendarYear && built.Today == Today() && built.Period == Period &&
               built.TrackingStartedOn == result.TrackingStartedOn && SameHeat(built.Heat, result.Heat);
    }

    private static bool SameHeat(IReadOnlyDictionary<DateOnly, HeatDay> a, IReadOnlyDictionary<DateOnly, HeatDay> b)
        => ReferenceEquals(a, b) || (a.Count == b.Count && a.All(day => b.TryGetValue(day.Key, out var other) && day.Value == other));

    private void BuildCalendar()
    {
        var result = _result;
        var heat = result?.Heat ?? new Dictionary<DateOnly, HeatDay>();
        var days = heat.ToDictionary(d => d.Key, d => new CalendarDay(d.Value.Weight, d.Value.Summary, Unknown: d.Value.FoldersUnknown && d.Value.Weight == 0));
        var period = Period;
        var year = ActivityCalendar.BuildYear(days, result?.TrackingStartedOn ?? Today(), Today(), _calendarYear, _culture,
            period?.From, period?.To);
        _builtFrom = result is null ? null : new CalendarSource(heat, result.TrackingStartedOn, Today(), period, _calendarYear);

        CalendarWeeks.Clear();
        foreach (var week in year.Weeks)
            CalendarWeeks.Add(week);
        CalendarMonthMarkers.Clear();
        foreach (var marker in year.MonthMarkers)
            CalendarMonthMarkers.Add(marker);
        CalendarWeekdayLabels = year.WeekdayLabels;
        CalendarStripWidth = year.StripWidth;
        OnPropertyChanged(nameof(CalendarWeekdayLabels));
        OnPropertyChanged(nameof(CalendarStripWidth));
        _lastYear = year;
        BuildMonth(year);
    }

    private void BuildMonth(ActivityCalendarYearResult? year)
    {
        CalendarMonthWeeks.Clear();
        if (year?.Months.FirstOrDefault(m => m.Month == _calendarMonth) is { } month)
        {
            foreach (var week in month.Weeks)
                CalendarMonthWeeks.Add(week);
        }

        OnPropertyChanged(nameof(CalendarMonth));
        OnPropertyChanged(nameof(CalendarMonthLabel));
        OnPropertyChanged(nameof(CalendarMonthName));
        OnPropertyChanged(nameof(CanShowNextMonth));
        OnPropertyChanged(nameof(CanShowPreviousMonth));
    }

    private (DateOnly From, DateOnly To) WeekOf(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek - (int)_culture.DateTimeFormat.FirstDayOfWeek + 7) % 7;
        var start = date.AddDays(-offset);
        return (start, start.AddDays(6));
    }

    /// <summary>"Thu 24 Sep 2026" for one day, "21–27 Sep 2026" within a month, "28 Sep – 4 Oct 2026" across months.</summary>
    public static string FormatDays(DateOnly from, DateOnly to, CultureInfo culture)
    {
        string F(DateOnly d, string format) => d.ToDateTime(TimeOnly.MinValue).ToString(format, culture);

        if (from == to)
            return F(from, "ddd d MMM yyyy");
        if (from.Year != to.Year)
            return $"{F(from, "d MMM yyyy")} – {F(to, "d MMM yyyy")}";
        if (from.Month != to.Month)
            return $"{F(from, "d MMM")} – {F(to, "d MMM yyyy")}";
        return $"{F(from, "%d")}–{F(to, "d MMM yyyy")}";
    }

    private bool Tracks(DocumentKind kind) => _recentFiles.Settings.Kinds.Contains(kind);

    private void SetTracked(DocumentKind kind, bool tracked)
    {
        if (tracked == Tracks(kind))
            return;

        var kinds = _recentFiles.Settings.Kinds.Where(k => k != kind).ToList();
        if (tracked)
            kinds.Add(kind);

        var result = _recentFiles.TrySetKinds(kinds, out var persistence);
        if (!result.Success)
            ErrorMessage = result.ErrorMessage;
        else
            Report(persistence, null);
        OnRecentFilesSettingsChanged();
    }

    private void Report(PersistenceResult persistence, string? success)
    {
        StatusMessage = persistence.Saved ? success : null;
        ErrorMessage = persistence.Saved ? null : persistence.UserMessage;
    }

    private void OnRecentFilesSettingsChanged()
    {
        OnPropertyChanged(nameof(RecentFilesEnabled));
        OnPropertyChanged(nameof(TrackPdf));
        OnPropertyChanged(nameof(TrackWord));
        OnPropertyChanged(nameof(TrackExcel));
        OnPropertyChanged(nameof(TrackEverywhere));
        OnPropertyChanged(nameof(TrackUnderTrackedFolders));
        OnPropertyChanged(nameof(RecentFilesStatus));
    }

    private void NotifySource()
    {
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(IsSourceAll));
        OnPropertyChanged(nameof(IsSourceSaved));
        OnPropertyChanged(nameof(IsSourceRecent));
    }

    private void NotifyPeriod()
    {
        // The month view shows where a period chosen in the year shown starts, so a
        // week picked on the year strip is still outlined when the panel narrows.
        if (Period is { } period && period.From.Year == _calendarYear)
            _calendarMonth = period.From.Month;

        OnPropertyChanged(nameof(Date));
        OnPropertyChanged(nameof(Period));
        OnPropertyChanged(nameof(HasPeriod));
        OnPropertyChanged(nameof(PeriodText));
    }

    private DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).DateTime);
}

/// <summary>What the year strip was last built from, so a refresh that changes none of it leaves the strip alone.</summary>
internal sealed record CalendarSource(IReadOnlyDictionary<DateOnly, HeatDay> Heat, DateOnly TrackingStartedOn, DateOnly Today,
    (DateOnly From, DateOnly To)? Period, int Year);

/// <summary>One Library row: an item, and the group it is shown in.</summary>
public sealed class LibraryRowViewModel
{
    private readonly TimeZoneInfo _zone;
    private readonly CultureInfo _culture;

    public LibraryRowViewModel(LibraryItem item, string groupName, int groupOrder, TimeZoneInfo zone, CultureInfo culture)
    {
        Item = item;
        GroupName = groupName;
        GroupOrder = groupOrder;
        _zone = zone;
        _culture = culture;
    }

    public LibraryItem Item { get; }

    /// <summary>The heading this row is grouped under: a kind, a tag, or "No tag".</summary>
    public string GroupName { get; }

    public int GroupOrder { get; }

    public string Name => Item.Name;
    /// <summary>Picks the row's icon: folder, globe, or a document for the three file kinds.</summary>
    public LibraryKind Kind => Item.Kind;
    public string KindLabel => Item.Kind.Label();
    public string Location => Item.Location;
    public string Folder => Item.Folder;
    public string TagsText => string.Join(", ", Item.Tags);
    public string SourceText => Item.SourceText;

    /// <summary>Which sources know the item, without the count: the Where from column.</summary>
    public string WhereFromText => Item.WhereFromText;

    /// <summary>Opens (a file) or visits (a folder) in the period, as Saved places' Opens; "" when none were recorded.</summary>
    public string OpensText => Item.RecentCount > 0 ? Item.RecentCount.ToString(_culture) : "";

    /// <summary>"3 opens", "1 visit": says what the Opens number counts for this row; "" with none.</summary>
    public string OpensToolTip
    {
        get
        {
            if (Item.RecentCount <= 0)
                return "";

            var noun = Item.Kind == LibraryKind.Folder ? "visit" : "open";
            return Item.RecentCount == 1 ? $"1 {noun}" : $"{Item.RecentCount.ToString(_culture)} {noun}s";
        }
    }

    /// <summary>Sorts the Last used column; never-used rows last.</summary>
    public DateTimeOffset LastUsedSort => Item.LastUsedAt ?? DateTimeOffset.MinValue;

    public string LastUsedText => Item.LastUsedAt is { } at
        ? TimeZoneInfo.ConvertTime(at, _zone).DateTime.ToString("g", _culture)
        : "";

    /// <summary>Visits in the period, for a folder Recents recorded; "" otherwise (Desk layout design §4).</summary>
    public string VisitsText => Item.Kind == LibraryKind.Folder && Item.RecentCount > 0 ? Item.RecentCount.ToString(_culture) : "";

    /// <summary>Time spent in the folder in the period (Recents); "" otherwise.</summary>
    public string TimeText => Item.Kind == LibraryKind.Folder && Item.RecentTime > TimeSpan.Zero ? ActivityFormat.Duration(Item.RecentTime) : "";

    /// <summary>A folder that isn't a saved place yet: the Recents panel offers Add as place.</summary>
    public bool CanAddAsPlace => Item.Kind == LibraryKind.Folder && !Item.IsSavedPlace;

    /// <summary>The All tab's Source markers (File viewer design §4).</summary>
    public bool IsSavedPlace => Item.IsSavedPlace;
    public bool IsRecent => Item.IsRecent;
    public bool IsInSession => Item.IsInSession;

    /// <summary>The markers in words, for screen readers: "Saved place, Recent, In a session".</summary>
    public string MarkersText => string.Join(", ", new[]
    {
        IsSavedPlace ? "Saved place" : null,
        IsRecent ? "Recent" : null,
        IsInSession ? "In a session" : null,
    }.OfType<string>());

    /// <summary>The sessions that hold it, for the Sessions tab.</summary>
    public string SessionsText => string.Join(", ", Item.Sessions);

    /// <summary>True when <paramref name="other"/> is the same item, shown the same way in the same group.</summary>
    public bool Looks(LibraryRowViewModel other)
        => ResourceIdentity.Comparer.Equals(Item.Key, other.Item.Key) && GroupName == other.GroupName && Name == other.Name &&
           SourceText == other.SourceText && OpensText == other.OpensText && TagsText == other.TagsText && LastUsedText == other.LastUsedText &&
           VisitsText == other.VisitsText && TimeText == other.TimeText && SessionsText == other.SessionsText &&
           ReferenceEquals(Item.Place, other.Item.Place);

    /// <summary>True for a file Recent Files recorded, which Remove from Recent Files can forget.</summary>
    public bool CanForget => Item.IsRecent && Item.Kind.ToDocumentKind() is not null;
}

/// <summary>One kind chip: a kind, or null for All, with its count.</summary>
public sealed class LibraryKindFilter
{
    public LibraryKindFilter(LibraryKind? kind, string label, bool isSelected)
    {
        Kind = kind;
        Label = label;
        IsSelected = isSelected;
    }

    public LibraryKind? Kind { get; }
    public string Label { get; }
    public bool IsSelected { get; }
}

/// <summary>One tracked folder as the Recents panel shows it: selected when the list is narrowed to it.</summary>
public sealed record TrackedRootChip(string RootId, string Path, bool Enabled, bool IsSelected);
