using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Models.Activity;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Models.History;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.History;

namespace QuickerPlaces.ViewModels;

/// <summary>
/// The Activity window's root configuration decisions. A successful change
/// wakes the host even when its immediate save fails, because ActivityStore
/// keeps that change in memory and reports the failure separately.
///
/// Days the store no longer holds come from the activity history (history
/// plan §5): the months the year strip and the chosen period need are
/// loaded when they are shown, and let go when they no longer are.
/// </summary>
public sealed class ActivityViewModel : ObservableObject
{
    private readonly ActivityStore _store;
    private readonly Action _rootsChanged;
    private readonly TimeProvider _time;
    private readonly CultureInfo _culture;
    private ActivityRootSnapshot? _selectedRoot;
    private string? _errorMessage;
    private string? _statusMessage;
    private RollupMode _rollup;
    private string _depthText = "1";
    private string _dwellSecondsText = "5";
    private string _idleMinutesText = "5";
    private string _equivalentPrefixesText = "";
    private ActivityPeriodMode _periodMode = ActivityPeriodMode.Week;
    private DateOnly _anchorDate;
    private int _calendarYear;
    private string _periodNotice = "";
    private string _periodSummary = "";
    private string _groupingNotice = "";
    private readonly HistoryMonthCache? _history;
    private IReadOnlyList<HistoryMonthDocument> _historyMonths = Array.Empty<HistoryMonthDocument>();

    public ActivityViewModel(ActivityStore store, Action rootsChanged, TimeProvider? time = null,
        CultureInfo? culture = null)
    {
        _store = store;
        _rootsChanged = rootsChanged;
        _time = time ?? TimeProvider.System;
        _culture = culture ?? CultureInfo.CurrentCulture;
        _history = store.HistoryReader is { } reader ? new HistoryMonthCache(reader) : null;
        _anchorDate = Today();
        _calendarYear = _anchorDate.Year;
        Reload();
    }

    public ObservableCollection<ActivityRootSnapshot> Roots { get; } = new();
    public ObservableCollection<ActivityFolderRow> PeriodRows { get; } = new();
    public ObservableCollection<ActivityCalendarMonth> CalendarMonths { get; } = new();
    public ObservableCollection<ActivityCalendarWeek> CalendarWeeks { get; } = new();
    public ObservableCollection<ActivityCalendarMonthMarker> CalendarMonthMarkers { get; } = new();
    private int _calendarStripWidth;
    public int CalendarStripWidth
    {
        get => _calendarStripWidth;
        private set => SetProperty(ref _calendarStripWidth, value);
    }
    private IReadOnlyList<string> _calendarWeekdayLabels = Array.Empty<string>();
    public IReadOnlyList<string> CalendarWeekdayLabels => _calendarWeekdayLabels;

    public IReadOnlyList<ActivityRollupOption> RollupOptions { get; } =
        new[]
        {
            new ActivityRollupOption(RollupMode.RootChild, "First folder below root"),
            new ActivityRollupOption(RollupMode.Exact, "All subfolders"),
            new ActivityRollupOption(RollupMode.Depth, "Chosen depth below root")
        };

    public bool CanManage => _store.IsAvailable;
    public string? Notice => _store.Notice;
    public bool HasRoots => Roots.Count > 0;
    public bool HasSelection => SelectedRoot is not null;
    public bool IsDepthRollup => Rollup == RollupMode.Depth;
    public bool IsTargetFolderSelection => SelectedRoot?.Config.IsAllFolders != true;
    public bool IsCoveredByAllFolders => _store.TrackAllFolders && IsTargetFolderSelection;
    public bool CanToggleSelected => CanManage && HasSelection && !IsCoveredByAllFolders;
    public bool HasError => ErrorMessage is not null;
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public void DismissStatus() => StatusMessage = null;
    public bool HasUnsavedChanges => _store.HasUnsavedChanges;
    public bool HasPeriodRows => PeriodRows.Count > 0;
    public bool CanMoveNext => PeriodTo < Today();
    public int CalendarYear => _calendarYear;
    public string CalendarYearLabel => $"{_calendarYear} activity";
    /// <summary>The first year the year list offers: a year back, or as far as the activity history reaches.</summary>
    public int EarliestCalendarYear
    {
        get
        {
            var kept = Today().AddDays(-364).Year;
            return _history?.EarliestDay() is { } first && first.Year < kept ? first.Year : kept;
        }
    }

    /// <summary>How many history months are loaded now, for tests: only what the strip and period need.</summary>
    public int LoadedHistoryMonths => _history?.LoadedCount ?? 0;
    public ActivityPeriodMode PeriodMode => _periodMode;
    public bool IsWeekMode => _periodMode == ActivityPeriodMode.Week;
    public bool IsMonthMode => _periodMode == ActivityPeriodMode.Month;
    public bool IsDayMode => _periodMode == ActivityPeriodMode.Day;
    public string PeriodNotice => string.IsNullOrEmpty(_groupingNotice)
        ? _periodNotice
        : string.IsNullOrEmpty(_periodNotice) ? _groupingNotice : $"{_periodNotice} {_groupingNotice}";
    public string AboutSelectedFolderText => SelectedRoot is not { } selected
        ? "No tracked folder selected."
        : $"{selected.Path}{Environment.NewLine}{SelectedStatusText}{Environment.NewLine}{TrackingStartedText}" +
          (string.IsNullOrWhiteSpace(PeriodNotice) ? "" :
              $"{Environment.NewLine}{Environment.NewLine}For {PeriodLabel}:{Environment.NewLine}{PeriodNotice}");
    public string PeriodSummary => _periodSummary;
    public string EmptyPeriodText => SelectedRoot is null
        ? "Add a root to see activity."
        : "No folder activity recorded for this period.";
    public DateOnly PeriodFrom => PeriodBounds().From;
    public DateOnly PeriodTo => PeriodBounds().To;
    public string PeriodLabel => _periodMode switch
    {
        ActivityPeriodMode.Month => _anchorDate.ToDateTime(TimeOnly.MinValue).ToString("Y", _culture),
        ActivityPeriodMode.Day => _anchorDate.ToDateTime(TimeOnly.MinValue).ToString("D", _culture),
        _ => $"{PeriodFrom.ToDateTime(TimeOnly.MinValue).ToString("d", _culture)} – {PeriodTo.ToDateTime(TimeOnly.MinValue).ToString("d", _culture)}"
    };
    public string ToggleLabel => SelectedRoot?.Config.IsAllFolders == true
        ? SelectedRoot.Enabled ? "Use target folders only" : "Track all folders"
        : SelectedRoot?.Enabled == true ? "Stop tracking" : "Resume tracking";
    public string SelectedStatusText => IsCoveredByAllFolders ? "Covered by All folders"
        : SelectedRoot?.Enabled == true ? "Tracking is on" : "Tracking is paused";
    public string TrackingStartedText => SelectedRoot is { } root
        ? $"Tracking started {root.TrackingStartedAt.ToLocalTime():d}"
        : "";

    public ActivityRootSnapshot? SelectedRoot
    {
        get => _selectedRoot;
        set
        {
            if (_selectedRoot?.RootId != value?.RootId)
                _groupingNotice = "";
            if (!SetProperty(ref _selectedRoot, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(IsTargetFolderSelection));
            OnPropertyChanged(nameof(IsCoveredByAllFolders));
            OnPropertyChanged(nameof(CanToggleSelected));
            OnPropertyChanged(nameof(ToggleLabel));
            OnPropertyChanged(nameof(SelectedStatusText));
            OnPropertyChanged(nameof(TrackingStartedText));
            if (value is null)
            {
                RefreshPeriod();
                return;
            }
            ResetSelectedSettingsDraft();
            RefreshPeriod();
        }
    }

    public void ResetSelectedSettingsDraft()
    {
        if (SelectedRoot is not { } selected) return;
        Rollup = selected.Config.Rollup;
        DepthText = selected.Config.Depth.ToString(CultureInfo.InvariantCulture);
        DwellSecondsText = selected.Config.DwellThreshold.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        IdleMinutesText = selected.Config.IdleTimeout.TotalMinutes.ToString(CultureInfo.InvariantCulture);
        EquivalentPrefixesText = string.Join(Environment.NewLine, selected.Config.EquivalentPrefixes);
        if (!_store.HasUnsavedChanges)
            ErrorMessage = null;
    }

    public RollupMode Rollup
    {
        get => _rollup;
        set
        {
            if (SetProperty(ref _rollup, value))
            {
                OnPropertyChanged(nameof(IsDepthRollup));
                OnPropertyChanged(nameof(SelectedRollupOption));
            }
        }
    }

    public ActivityRollupOption SelectedRollupOption
    {
        get => RollupOptions.First(option => option.Mode == Rollup);
        set { if (value is not null) Rollup = value.Mode; }
    }

    public string DepthText
    {
        get => _depthText;
        set => SetProperty(ref _depthText, value);
    }

    public string DwellSecondsText
    {
        get => _dwellSecondsText;
        set => SetProperty(ref _dwellSecondsText, value);
    }

    public string IdleMinutesText
    {
        get => _idleMinutesText;
        set => SetProperty(ref _idleMinutesText, value);
    }

    public string EquivalentPrefixesText
    {
        get => _equivalentPrefixesText;
        set => SetProperty(ref _equivalentPrefixesText, value);
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

    public bool AddRoot(string path, IReadOnlyList<string>? equivalentPrefixes)
    {
        var result = _store.TryAddRoot(path, equivalentPrefixes, out var added, out var persistence);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
            return false;
        }

        ApplyPersistence(persistence);
        _rootsChanged();
        Reload(added!.RootId);
        return true;
    }

    public bool SaveSelectedSettings()
    {
        DismissStatus();
        if (SelectedRoot is not { } selected) return false;
        if (!int.TryParse(DepthText, NumberStyles.None, CultureInfo.InvariantCulture, out var depth) || depth < 1)
            return Fail("Depth must be a whole number of at least 1.");
        if (!int.TryParse(DwellSecondsText, NumberStyles.None, CultureInfo.InvariantCulture, out var dwell) || dwell < 0)
            return Fail("Visit threshold must be a whole number of seconds, 0 or more.");
        if (!int.TryParse(IdleMinutesText, NumberStyles.None, CultureInfo.InvariantCulture, out var idle) || idle < 1)
            return Fail("Idle timeout must be a whole number of minutes, at least 1.");

        var prefixes = EquivalentPrefixesText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var settings = selected.Config with
        {
            Rollup = Rollup,
            Depth = depth,
            DwellThreshold = TimeSpan.FromSeconds(dwell),
            IdleTimeout = TimeSpan.FromMinutes(idle),
            EquivalentPrefixes = prefixes
        };
        var result = _store.TryUpdateRoot(settings, out var persistence);
        if (!result.Success)
            return Fail(result.ErrorMessage!);

        ApplyPersistence(persistence);
        _rootsChanged();
        Reload(selected.RootId);
        if (selected.Config.Rollup != settings.Rollup ||
            (settings.Rollup == RollupMode.Depth && selected.Config.Depth != settings.Depth))
        {
            _groupingNotice = "Grouping saved. Earlier rows keep the grouping used when they were recorded; new visits use this setting.";
            OnPropertyChanged(nameof(PeriodNotice));
        }
        if (persistence.Saved && !_store.HasUnsavedChanges)
            StatusMessage = "Folder settings updated.";
        return true;
    }

    public void ToggleSelected()
    {
        if (!CanToggleSelected) return;
        if (SelectedRoot is not { } selected) return;
        ApplyPersistence(_store.SetEnabled(selected.RootId, !selected.Enabled));
        _rootsChanged();
        Reload(selected.RootId);
    }

    public void RetrySave() => ApplyPersistence(_store.Flush());

    public void Reload(string? selectRootId = null)
    {
        selectRootId ??= SelectedRoot?.RootId;
        Roots.Clear();
        foreach (var root in _store.Roots)
            Roots.Add(root);
        SelectedRoot = Roots.FirstOrDefault(root => root.RootId == selectRootId) ?? Roots.FirstOrDefault();
        OnPropertyChanged(nameof(HasRoots));
        OnPropertyChanged(nameof(IsCoveredByAllFolders));
        OnPropertyChanged(nameof(CanToggleSelected));
        OnPropertyChanged(nameof(SelectedStatusText));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        RefreshPeriod();
    }

    public void SetPeriodMode(ActivityPeriodMode mode)
    {
        _periodMode = mode;
        _calendarYear = _anchorDate.Year;
        RefreshPeriod();
    }

    public void ShowDay(DateOnly day)
    {
        _periodMode = ActivityPeriodMode.Day;
        SelectCalendarDate(day);
    }

    public void SelectCalendarDate(DateOnly day)
    {
        _anchorDate = day;
        _calendarYear = _anchorDate.Year;
        RefreshPeriod();
    }

    public void ShowToday()
    {
        _calendarYear = Today().Year;
        ShowDay(Today());
    }

    public bool SelectCalendarYear(int year)
    {
        if (year < EarliestCalendarYear || year >= DateOnly.MaxValue.Year)
            return false;
        _calendarYear = year;
        LoadHistory();
        RefreshCalendar();
        OnPropertyChanged(nameof(LoadedHistoryMonths));
        return true;
    }

    public void MovePeriod(int direction) => MoveCalendarSelection(direction, horizontal: false);

    public bool MoveCalendarSelection(int direction, bool horizontal)
    {
        if (direction is not (-1 or 1)) return false;
        var next = _periodMode switch
        {
            ActivityPeriodMode.Month => new DateOnly(_anchorDate.Year, _anchorDate.Month, 1).AddMonths(direction),
            ActivityPeriodMode.Day => _anchorDate.AddDays(direction * (horizontal ? 7 : 1)),
            _ => _anchorDate.AddDays(direction * 7)
        };
        if (direction > 0 && next > Today()) return false;
        _anchorDate = next;
        _calendarYear = _anchorDate.Year;
        RefreshPeriod();
        return true;
    }

    public void RefreshPeriod()
    {
        LoadHistory();
        RefreshCalendar();
        PeriodRows.Clear();
        _periodNotice = "";
        _periodSummary = "";
        if (SelectedRoot is { } selected &&
            _store.QueryPeriod(selected.RootId, PeriodFrom, PeriodTo) is { } stored)
        {
            var period = WithHistory(stored, selected);
            foreach (var folder in period.Folders)
                PeriodRows.Add(new ActivityFolderRow(folder, _time.LocalTimeZone, _culture, selected.Path));

            if (period.StartsBeforeTracking)
                _periodNotice = $"Tracking started on {period.TrackingStartedOn.ToDateTime(TimeOnly.MinValue).ToString("d", _culture)}. Earlier days were not recorded.";
            if (period.DetailExpired)
                _periodNotice += (_periodNotice.Length > 0 ? " " : "") +
                    $"Folder details before {period.DetailKeptFrom.ToDateTime(TimeOnly.MinValue).ToString("d", _culture)} have expired.";

            var total = TimeSpan.FromTicks(period.Folders.Sum(folder => folder.Time.Ticks));
            _periodSummary = $"{ActivityFormat.Duration(total)} in {period.Folders.Count} {(period.Folders.Count == 1 ? "folder" : "folders")}";

            if (_periodMode == ActivityPeriodMode.Day && period.DetailExpired &&
                DayTotals(selected).TryGetValue(_anchorDate, out var dayTotal))
                _periodSummary = $"{ActivityFormat.Duration(dayTotal.Time)} total; folder details have expired";
        }

        OnPropertyChanged(nameof(PeriodMode));
        OnPropertyChanged(nameof(IsWeekMode));
        OnPropertyChanged(nameof(IsMonthMode));
        OnPropertyChanged(nameof(IsDayMode));
        OnPropertyChanged(nameof(PeriodFrom));
        OnPropertyChanged(nameof(PeriodTo));
        OnPropertyChanged(nameof(PeriodLabel));
        OnPropertyChanged(nameof(PeriodNotice));
        OnPropertyChanged(nameof(PeriodSummary));
        OnPropertyChanged(nameof(HasPeriodRows));
        OnPropertyChanged(nameof(EmptyPeriodText));
        OnPropertyChanged(nameof(CanMoveNext));
        OnPropertyChanged(nameof(LoadedHistoryMonths));
    }

    /// <summary>
    /// Loads the history months the year strip (for day totals) and the
    /// period (for folder detail) need, letting every other month go.
    /// </summary>
    private void LoadHistory()
    {
        if (_history is null)
            return;

        var cutoffs = HistoryCutoffs.For(Today());
        var index = _history.Index();
        var months = HistoryMonthCache.Needed(index, new DateOnly(_calendarYear, 1, 1), new DateOnly(_calendarYear, 12, 31), cutoffs.TotalsFrom)
            .Concat(HistoryMonthCache.Needed(index, PeriodFrom, PeriodTo, cutoffs.DetailFrom));
        _historyMonths = _history.Load(months, cutoffs);
    }

    /// <summary>The root's day totals: the store's year, and whatever history is loaded.</summary>
    private IReadOnlyDictionary<DateOnly, ActivityDayTotal> DayTotals(ActivityRootSnapshot root)
    {
        var stored = _store.QueryDayTotals(root.RootId) ?? new Dictionary<DateOnly, ActivityDayTotal>();
        return _historyMonths.Count == 0 ? stored : HistoryMerge.Add(stored, HistoryMerge.DayTotals(_historyMonths, root.Path));
    }

    /// <summary>When tracking began as far as anything recorded shows: on this PC, or earlier in the loaded history.</summary>
    private DateOnly TrackingStartedOn(ActivityRootSnapshot root)
    {
        var started = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(root.TrackingStartedAt, _time.LocalTimeZone).DateTime);
        return HistoryMerge.FirstDay(_historyMonths, root.Path) is { } first && first < started ? first : started;
    }

    /// <summary>
    /// The store's period with the loaded history's folders added. Without
    /// history it is the store's as it was. With it, detail counts as expired
    /// only before the last day in the period that has a total and no folders
    /// in either: days recorded before the history began.
    /// </summary>
    private ActivityPeriod WithHistory(ActivityPeriod stored, ActivityRootSnapshot root)
    {
        if (_history is null)
            return stored;

        var historyDays = HistoryMerge.FolderDays(_historyMonths, root.Path);
        var folders = HistoryMerge.SumFolders(stored.Folders, historyDays, stored.From, stored.To);
        var detailDates = historyDays.Select(d => d.Date).ToHashSet();
        var missing = DayTotals(root)
            .Where(d => d.Key >= stored.From && d.Key <= stored.To && d.Key < stored.DetailKeptFrom && d.Value.Visits > 0 && !detailDates.Contains(d.Key))
            .Select(d => d.Key)
            .DefaultIfEmpty(DateOnly.MinValue)
            .Max();
        var keptFrom = missing == DateOnly.MinValue ? stored.From : missing.AddDays(1);
        return stored with { Folders = folders, TrackingStartedOn = TrackingStartedOn(root), DetailKeptFrom = keptFrom };
    }

    private void RefreshCalendar()
    {
        CalendarMonths.Clear();
        CalendarWeeks.Clear();
        CalendarMonthMarkers.Clear();
        CalendarStripWidth = 0;
        OnPropertyChanged(nameof(CalendarYear));
        OnPropertyChanged(nameof(CalendarYearLabel));
        OnPropertyChanged(nameof(EarliestCalendarYear));
        if (SelectedRoot is not { } root || _store.QueryDayTotals(root.RootId) is null)
            return;

        var calendar = ActivityCalendar.BuildYear(DayTotals(root), TrackingStartedOn(root), Today(), _calendarYear, _culture,
            PeriodFrom, PeriodTo, _history?.EarliestDay());
        foreach (var month in calendar.Months)
            CalendarMonths.Add(month);
        foreach (var week in calendar.Weeks)
            CalendarWeeks.Add(week);
        foreach (var marker in calendar.MonthMarkers)
            CalendarMonthMarkers.Add(marker);
        CalendarStripWidth = calendar.StripWidth;
        _calendarWeekdayLabels = calendar.WeekdayLabels;
        OnPropertyChanged(nameof(CalendarWeekdayLabels));
    }

    private DateOnly Today() => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    private (DateOnly From, DateOnly To) PeriodBounds()
    {
        if (_periodMode == ActivityPeriodMode.Day) return (_anchorDate, _anchorDate);
        if (_periodMode == ActivityPeriodMode.Month)
        {
            var from = new DateOnly(_anchorDate.Year, _anchorDate.Month, 1);
            return (from, from.AddMonths(1).AddDays(-1));
        }
        var first = (int)_culture.DateTimeFormat.FirstDayOfWeek;
        var delta = ((int)_anchorDate.DayOfWeek - first + 7) % 7;
        var week = _anchorDate.AddDays(-delta);
        return (week, week.AddDays(6));
    }

    private bool Fail(string message)
    {
        ErrorMessage = message;
        return false;
    }

    private void ApplyPersistence(QuickerPlaces.Models.PersistenceResult result)
    {
        if (!result.Saved)
            ErrorMessage = result.UserMessage;
        else if (!_store.HasUnsavedChanges)
            ErrorMessage = null;
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }
}

public sealed record ActivityRollupOption(RollupMode Mode, string Label);

public enum ActivityPeriodMode { Week, Month, Day }

public sealed record ActivityFolderRow(FolderActivity Activity, TimeZoneInfo Zone, CultureInfo Culture, string RootPath)
{
    public string Folder => Activity.Folder;
    public TimeSpan Time => Activity.Time;
    public string TimeText => ActivityFormat.Duration(Time);
    public int Visits => Activity.Visits;
    public DateTimeOffset LastVisited => Activity.LastVisited;
    public string LastVisitedText => ActivityFormat.LastVisited(LastVisited, Zone, Culture);
    public int Level => TrackedFolderPaths.LevelBelow(RootPath, Folder) ?? 0;
    public string LevelLabel => TrackedFolderPaths.LevelLabel(Level);
}
