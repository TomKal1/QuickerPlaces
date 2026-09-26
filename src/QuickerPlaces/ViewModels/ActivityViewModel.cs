using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Models.Activity;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.ViewModels;

/// <summary>
/// The Activity window's root configuration decisions. A successful change
/// wakes the host even when its immediate save fails, because ActivityStore
/// keeps that change in memory and reports the failure separately.
/// </summary>
public sealed class ActivityViewModel : ObservableObject
{
    private readonly ActivityStore _store;
    private readonly Action _rootsChanged;
    private readonly TimeProvider _time;
    private readonly CultureInfo _culture;
    private ActivityRootSnapshot? _selectedRoot;
    private string? _errorMessage;
    private RollupMode _rollup;
    private string _depthText = "1";
    private string _dwellSecondsText = "5";
    private string _idleMinutesText = "5";
    private string _equivalentPrefixesText = "";
    private ActivityPeriodMode _periodMode = ActivityPeriodMode.Week;
    private DateOnly _anchorDate;
    private string _periodNotice = "";
    private string _periodSummary = "";

    public ActivityViewModel(ActivityStore store, Action rootsChanged, TimeProvider? time = null,
        CultureInfo? culture = null)
    {
        _store = store;
        _rootsChanged = rootsChanged;
        _time = time ?? TimeProvider.System;
        _culture = culture ?? CultureInfo.CurrentCulture;
        _anchorDate = Today();
        Reload();
    }

    public ObservableCollection<ActivityRootSnapshot> Roots { get; } = new();
    public ObservableCollection<ActivityFolderRow> PeriodRows { get; } = new();
    public ObservableCollection<ActivityCalendarWeek> CalendarWeeks { get; } = new();
    private IReadOnlyList<string> _calendarWeekdayLabels = Array.Empty<string>();
    public IReadOnlyList<string> CalendarWeekdayLabels => _calendarWeekdayLabels;

    public IReadOnlyList<ActivityRollupOption> RollupOptions { get; } =
        new[]
        {
            new ActivityRollupOption(RollupMode.RootChild, "First folder below root"),
            new ActivityRollupOption(RollupMode.Exact, "Each exact folder"),
            new ActivityRollupOption(RollupMode.Depth, "Chosen depth below root")
        };

    public bool CanManage => _store.IsAvailable;
    public string? Notice => _store.Notice;
    public bool HasRoots => Roots.Count > 0;
    public bool HasSelection => SelectedRoot is not null;
    public bool IsDepthRollup => Rollup == RollupMode.Depth;
    public bool HasError => ErrorMessage is not null;
    public bool HasUnsavedChanges => _store.HasUnsavedChanges;
    public bool HasPeriodRows => PeriodRows.Count > 0;
    public bool CanMoveNext => PeriodTo < Today();
    public ActivityPeriodMode PeriodMode => _periodMode;
    public string PeriodNotice => _periodNotice;
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
    public string ToggleLabel => SelectedRoot?.Enabled == true ? "Stop tracking" : "Resume tracking";
    public string SelectedStatusText => SelectedRoot?.Enabled == true ? "Tracking is on" : "Tracking is paused";
    public string TrackingStartedText => SelectedRoot is { } root
        ? $"Tracking started {root.TrackingStartedAt.ToLocalTime():d}"
        : "";

    public ActivityRootSnapshot? SelectedRoot
    {
        get => _selectedRoot;
        set
        {
            if (!SetProperty(ref _selectedRoot, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(ToggleLabel));
            OnPropertyChanged(nameof(SelectedStatusText));
            OnPropertyChanged(nameof(TrackingStartedText));
            if (value is null)
            {
                RefreshPeriod();
                return;
            }
            Rollup = value.Config.Rollup;
            DepthText = value.Config.Depth.ToString(CultureInfo.InvariantCulture);
            DwellSecondsText = value.Config.DwellThreshold.TotalSeconds.ToString(CultureInfo.InvariantCulture);
            IdleMinutesText = value.Config.IdleTimeout.TotalMinutes.ToString(CultureInfo.InvariantCulture);
            EquivalentPrefixesText = string.Join(Environment.NewLine, value.Config.EquivalentPrefixes);
            RefreshPeriod();
        }
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
        return true;
    }

    public void ToggleSelected()
    {
        if (SelectedRoot is not { } selected) return;
        ApplyPersistence(_store.SetEnabled(selected.RootId, !selected.Enabled));
        _rootsChanged();
        Reload(selected.RootId);
    }

    public void DeleteSelected()
    {
        if (SelectedRoot is not { } selected) return;
        ApplyPersistence(_store.DeleteRoot(selected.RootId));
        _rootsChanged();
        Reload();
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
        OnPropertyChanged(nameof(HasUnsavedChanges));
        RefreshPeriod();
    }

    public void SetPeriodMode(ActivityPeriodMode mode)
    {
        _periodMode = mode;
        RefreshPeriod();
    }

    public void ShowDay(DateOnly day)
    {
        _anchorDate = day > Today() ? Today() : day;
        _periodMode = ActivityPeriodMode.Day;
        RefreshPeriod();
    }

    public void MovePeriod(int direction)
    {
        if (direction is not (-1 or 1)) return;
        var next = _periodMode switch
        {
            ActivityPeriodMode.Month => new DateOnly(_anchorDate.Year, _anchorDate.Month, 1).AddMonths(direction),
            ActivityPeriodMode.Day => _anchorDate.AddDays(direction),
            _ => _anchorDate.AddDays(direction * 7)
        };
        if (direction > 0 && next > Today()) return;
        _anchorDate = next;
        RefreshPeriod();
    }

    public void RefreshPeriod()
    {
        RefreshCalendar();
        PeriodRows.Clear();
        _periodNotice = "";
        _periodSummary = "";
        if (SelectedRoot is { } selected &&
            _store.QueryPeriod(selected.RootId, PeriodFrom, PeriodTo) is { } period)
        {
            foreach (var folder in period.Folders)
                PeriodRows.Add(new ActivityFolderRow(folder, _time.LocalTimeZone, _culture));

            if (period.StartsBeforeTracking)
                _periodNotice = $"Tracking started on {period.TrackingStartedOn.ToDateTime(TimeOnly.MinValue).ToString("d", _culture)}. Earlier days were not recorded.";
            if (period.DetailExpired)
                _periodNotice += (_periodNotice.Length > 0 ? " " : "") +
                    $"Folder details before {period.DetailKeptFrom.ToDateTime(TimeOnly.MinValue).ToString("d", _culture)} have expired.";

            var total = TimeSpan.FromTicks(period.Folders.Sum(folder => folder.Time.Ticks));
            _periodSummary = $"{ActivityFormat.Duration(total)} in {period.Folders.Count} {(period.Folders.Count == 1 ? "folder" : "folders")}";

            if (_periodMode == ActivityPeriodMode.Day && period.DetailExpired &&
                _store.QueryDayTotals(selected.RootId)?.TryGetValue(_anchorDate, out var dayTotal) == true &&
                dayTotal is not null)
                _periodSummary = $"{ActivityFormat.Duration(dayTotal.Time)} total; folder details have expired";
        }

        OnPropertyChanged(nameof(PeriodMode));
        OnPropertyChanged(nameof(PeriodFrom));
        OnPropertyChanged(nameof(PeriodTo));
        OnPropertyChanged(nameof(PeriodLabel));
        OnPropertyChanged(nameof(PeriodNotice));
        OnPropertyChanged(nameof(PeriodSummary));
        OnPropertyChanged(nameof(HasPeriodRows));
        OnPropertyChanged(nameof(EmptyPeriodText));
        OnPropertyChanged(nameof(CanMoveNext));
    }

    private void RefreshCalendar()
    {
        CalendarWeeks.Clear();
        if (SelectedRoot is not { } root || _store.QueryDayTotals(root.RootId) is not { } totals)
            return;

        var started = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(root.TrackingStartedAt,
            _time.LocalTimeZone).DateTime);
        var calendar = ActivityCalendar.Build(totals, started, Today(), _culture);
        foreach (var week in calendar.Weeks)
            CalendarWeeks.Add(week);
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

public sealed record ActivityFolderRow(FolderActivity Activity, TimeZoneInfo Zone, CultureInfo Culture)
{
    public string Folder => Activity.Folder;
    public TimeSpan Time => Activity.Time;
    public string TimeText => ActivityFormat.Duration(Time);
    public int Visits => Activity.Visits;
    public DateTimeOffset LastVisited => Activity.LastVisited;
    public string LastVisitedText => ActivityFormat.LastVisited(LastVisited, Zone, Culture);
}
