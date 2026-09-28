using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Models.RecentFiles;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Library;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.ViewModels;

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

/// <summary>How the Library's rows are grouped.</summary>
public enum LibraryGrouping
{
    /// <summary>Folders, Links, PDFs, Word, Excel.</summary>
    Type,

    /// <summary>One group per session tag, then "No tag". An item with two tags is in both.</summary>
    Tag,
}

/// <summary>
/// Backs the Library window (documents plan §6): everything QuickerPlaces
/// knows about, together — saved places, files in saved sessions, folders
/// from Recents and files from Recent Files — split by kind (folders, links,
/// PDFs, Word, Excel), filtered to saved or recent, searched, and grouped by
/// type or by tag, with a year strip of when they were used. The four
/// sources stay separate, each with its own switch; this only reads them,
/// except for Recent Files' own settings, which live here because this is
/// where Recent Files is seen.
///
/// UI-free and linked into the test project; LibraryWindow only binds and
/// passes clicks in.
/// </summary>
public sealed class LibraryViewModel : ObservableObject
{
    /// <summary>Days of Recents folder detail the Library reads when no day is chosen: all that is kept.</summary>
    private const int FolderDays = ActivityStore.DetailDays;

    private readonly PlacesService _places;
    private readonly SessionStore _sessions;
    private readonly ActivityStore _activity;
    private readonly RecentFilesStore _recentFiles;
    private readonly PlaceLauncher _placeLauncher;
    private readonly IShell _shell;
    private readonly TimeProvider _time;
    private readonly CultureInfo _culture;
    private IReadOnlyList<LibraryItem> _items = Array.Empty<LibraryItem>();
    private LibraryKind? _selectedKind;
    private LibrarySourceFilter _source;
    private LibraryGrouping _grouping;
    private string _searchText = "";
    private DateOnly? _selectedDay;
    private int _calendarYear;
    private string? _statusMessage;
    private string? _errorMessage;

    public LibraryViewModel(PlacesService places, SessionStore sessions, ActivityStore activity, RecentFilesStore recentFiles,
        PlaceLauncher placeLauncher, IShell shell, TimeProvider? time = null, CultureInfo? culture = null)
    {
        _places = places;
        _sessions = sessions;
        _activity = activity;
        _recentFiles = recentFiles;
        _placeLauncher = placeLauncher;
        _shell = shell;
        _time = time ?? TimeProvider.System;
        _culture = culture ?? CultureInfo.CurrentCulture;
        _calendarYear = Today().Year;
        Reload();
    }

    /// <summary>Raised after a saved place was opened from here, so the main grid can show its new Last Opened.</summary>
    public event Action<Place, PersistenceResult>? PlaceOpened;

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
                Refresh();
        }
    }

    public LibrarySourceFilter Source
    {
        get => _source;
        set
        {
            if (!SetProperty(ref _source, value))
                return;
            OnPropertyChanged(nameof(IsSourceAll));
            OnPropertyChanged(nameof(IsSourceSaved));
            OnPropertyChanged(nameof(IsSourceRecent));
            Refresh();
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
            Refresh();
        }
    }

    public bool IsGroupedByType { get => _grouping == LibraryGrouping.Type; set { if (value) Grouping = LibraryGrouping.Type; } }
    public bool IsGroupedByTag { get => _grouping == LibraryGrouping.Tag; set { if (value) Grouping = LibraryGrouping.Tag; } }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? ""))
                Refresh();
        }
    }

    /// <summary>The day chosen on the year strip: only what was used that day is listed. Null for everything kept.</summary>
    public DateOnly? SelectedDay => _selectedDay;

    public bool HasSelectedDay => _selectedDay is not null;

    /// <summary>"Used on Mon 28 Sep 2026", for the chip that clears the day.</summary>
    public string SelectedDayText => _selectedDay is { } day
        ? $"Used on {day.ToDateTime(TimeOnly.MinValue).ToString("ddd d MMM yyyy", _culture)}"
        : "";

    // ---------------------------------------------------------------
    // Rows
    // ---------------------------------------------------------------

    /// <summary>The rows shown, in group order; each carries its group's name for the view to group on.</summary>
    public ObservableCollection<LibraryRowViewModel> Rows { get; } = new();

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>What to say in place of an empty list.</summary>
    public string EmptyText => _items.Count == 0 && _selectedDay is null
        ? "Nothing here yet. Save places, save open files as a session, or turn on Recents or Recent Files."
        : "Nothing matches. Clear the search, choose All, or pick another day.";

    // ---------------------------------------------------------------
    // Year strip
    // ---------------------------------------------------------------

    public ObservableCollection<ActivityCalendarWeek> CalendarWeeks { get; } = new();
    public ObservableCollection<ActivityCalendarMonthMarker> CalendarMonthMarkers { get; } = new();
    public IReadOnlyList<string> CalendarWeekdayLabels { get; private set; } = Array.Empty<string>();
    public int CalendarStripWidth { get; private set; }
    public int CalendarYear => _calendarYear;
    public string CalendarYearLabel => $"{_calendarYear}";

    /// <summary>What the strip counts for the current kind filter, as its caption.</summary>
    public string CalendarCaption => _selectedKind switch
    {
        LibraryKind.Folder => "Folder visits from Recents",
        LibraryKind.Link => "Links keep no history, only when each was last opened",
        LibraryKind.Pdf or LibraryKind.Word or LibraryKind.Excel => $"{_selectedKind.Value.PluralLabel()} opened (Recent Files), and sessions saved or reopened",
        _ => "Folder visits, files opened, and sessions saved or reopened",
    };

    /// <summary>Chooses a day to list only what was used on it; choosing it again clears it.</summary>
    public void SelectCalendarDate(DateOnly day)
    {
        _selectedDay = _selectedDay == day ? null : day;
        NotifyDayChanged();
        Reload();
    }

    public void ClearSelectedDay()
    {
        if (_selectedDay is null)
            return;
        _selectedDay = null;
        NotifyDayChanged();
        Reload();
    }

    public bool SelectCalendarYear(int year)
    {
        if (year < 2000 || year > Today().Year || year == _calendarYear)
            return false;
        _calendarYear = year;
        OnPropertyChanged(nameof(CalendarYear));
        OnPropertyChanged(nameof(CalendarYearLabel));
        RebuildCalendar();
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
    /// place open exactly as from the main grid; anything else is checked
    /// and handed to Windows, and counts as nothing.
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

    /// <summary>Reads all four sources again and rebuilds the rows and the year strip.</summary>
    public void Reload()
    {
        _items = LibraryIndex.Build(PlacesInPeriod(), SessionsInPeriod(), FoldersInPeriod(), FilesInPeriod());
        Refresh();
        OnRecentFilesSettingsChanged();
    }

    // ---------------------------------------------------------------
    // Building
    // ---------------------------------------------------------------

    private void Refresh()
    {
        var passing = _items.Where(i => PassesSource(i) && LibraryIndex.Matches(i, _searchText)).ToList();

        var selected = _selectedKind;
        KindFilters.Clear();
        KindFilters.Add(new LibraryKindFilter(null, $"All ({passing.Count})", selected is null));
        foreach (var kind in LibraryKinds.All)
            KindFilters.Add(new LibraryKindFilter(kind, $"{kind.PluralLabel()} ({passing.Count(i => i.Kind == kind)})", selected == kind));

        var shown = passing.Where(i => selected is null || i.Kind == selected).ToList();
        Rows.Clear();
        if (_grouping == LibraryGrouping.Type)
        {
            foreach (var item in shown.OrderBy(i => i.Kind))
                Rows.Add(new LibraryRowViewModel(item, item.Kind.PluralLabel(), (int)item.Kind, _time.LocalTimeZone, _culture));
        }
        else
        {
            var tags = shown.SelectMany(i => i.Tags).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToList();
            for (var index = 0; index < tags.Count; index++)
            {
                foreach (var item in shown.Where(i => i.Tags.Contains(tags[index], StringComparer.OrdinalIgnoreCase)))
                    Rows.Add(new LibraryRowViewModel(item, tags[index], index, _time.LocalTimeZone, _culture));
            }

            foreach (var item in shown.Where(i => i.Tags.Count == 0))
                Rows.Add(new LibraryRowViewModel(item, "No tag", tags.Count, _time.LocalTimeZone, _culture));
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(CalendarCaption));
        RebuildCalendar();
    }

    private bool PassesSource(LibraryItem item) => _source switch
    {
        LibrarySourceFilter.Saved => item.IsSaved,
        LibrarySourceFilter.Recent => item.IsRecent,
        _ => true,
    };

    private IEnumerable<Place> PlacesInPeriod()
        => _selectedDay is { } day
            ? _places.Places.Where(p => p.LastOpenedAt is { } at && LocalDate(at) == day)
            : _places.Places;

    private IEnumerable<SessionSnapshot> SessionsInPeriod()
        => _selectedDay is { } day
            ? _sessions.Sessions.Where(s => LocalDate(s.CreatedAt) == day || s.OpenedAt.Any(o => LocalDate(o) == day))
            : _sessions.Sessions;

    private IEnumerable<FolderActivity> FoldersInPeriod()
    {
        var to = _selectedDay ?? Today();
        var from = _selectedDay ?? Today().AddDays(-(FolderDays - 1));
        return _activity.Roots.SelectMany(root => _activity.QueryPeriod(root.RootId, from, to)?.Folders ?? Array.Empty<FolderActivity>());
    }

    private IEnumerable<RecentFileSummary> FilesInPeriod()
        => _recentFiles.QueryFiles(_selectedDay, _selectedDay);

    private void RebuildCalendar()
    {
        var days = new Dictionary<DateOnly, (int Visits, int Opens, int Saved, int Reopens)>();
        void Add(DateOnly date, int visits = 0, int opens = 0, int saved = 0, int reopens = 0)
        {
            days.TryGetValue(date, out var day);
            days[date] = (day.Visits + visits, day.Opens + opens, day.Saved + saved, day.Reopens + reopens);
        }

        var kind = _selectedKind;
        var starts = new List<DateOnly>();

        if (kind is null or LibraryKind.Folder)
        {
            foreach (var root in _activity.Roots)
            {
                starts.Add(LocalDate(root.TrackingStartedAt));
                foreach (var (date, total) in _activity.QueryDayTotals(root.RootId) ?? new Dictionary<DateOnly, ActivityDayTotal>())
                    Add(date, visits: total.Visits);
            }
        }

        if (kind is null or LibraryKind.Pdf or LibraryKind.Word or LibraryKind.Excel)
        {
            if (_recentFiles.Settings.TrackingStartedAt is { } started)
                starts.Add(LocalDate(started));
            var kinds = kind?.ToDocumentKind() is { } documentKind ? new[] { documentKind } : null;
            foreach (var (date, total) in _recentFiles.QueryDayTotals(kinds))
                Add(date, opens: total.Opens);

            foreach (var session in _sessions.Sessions)
                starts.Add(LocalDate(session.CreatedAt));
            foreach (var (date, total) in _sessions.QueryDays())
                Add(date, saved: total.Saved, reopens: total.Reopens);
        }

        var calendarDays = days.ToDictionary(d => d.Key, d => new CalendarDay(
            d.Value.Visits + d.Value.Opens + d.Value.Saved + d.Value.Reopens, DaySummary(d.Value)));
        var trackingStartedOn = starts.Count > 0 ? starts.Min() : Today();
        var year = ActivityCalendar.BuildYear(calendarDays, trackingStartedOn, Today(), _calendarYear, _culture, _selectedDay);

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
    }

    private static string DaySummary((int Visits, int Opens, int Saved, int Reopens) day)
    {
        static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

        var parts = new List<string>();
        if (day.Visits > 0) parts.Add(Count(day.Visits, "folder visit", "folder visits"));
        if (day.Opens > 0) parts.Add(Count(day.Opens, "file opened", "files opened"));
        if (day.Saved > 0) parts.Add(Count(day.Saved, "session saved", "sessions saved"));
        if (day.Reopens > 0) parts.Add(Count(day.Reopens, "session reopened", "sessions reopened"));
        return string.Join(" · ", parts);
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
        OnPropertyChanged(nameof(RecentFilesStatus));
    }

    private void NotifyDayChanged()
    {
        OnPropertyChanged(nameof(SelectedDay));
        OnPropertyChanged(nameof(HasSelectedDay));
        OnPropertyChanged(nameof(SelectedDayText));
    }

    private DateOnly Today() => LocalDate(_time.GetUtcNow());

    private DateOnly LocalDate(DateTimeOffset instant)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _time.LocalTimeZone).DateTime);
}

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
    public string Glyph => Item.Kind.Glyph();
    public string KindLabel => Item.Kind.Label();
    public string Location => Item.Location;
    public string Folder => Item.Folder;
    public string TagsText => string.Join(", ", Item.Tags);
    public string SourceText => Item.SourceText;

    /// <summary>Sorts the Last used column; never-used rows last.</summary>
    public DateTimeOffset LastUsedSort => Item.LastUsedAt ?? DateTimeOffset.MinValue;

    public string LastUsedText => Item.LastUsedAt is { } at
        ? TimeZoneInfo.ConvertTime(at, _zone).DateTime.ToString("g", _culture)
        : "";

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
