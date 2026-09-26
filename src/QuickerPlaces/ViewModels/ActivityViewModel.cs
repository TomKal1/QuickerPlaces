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
    private ActivityRootSnapshot? _selectedRoot;
    private string? _errorMessage;
    private RollupMode _rollup;
    private string _depthText = "1";
    private string _dwellSecondsText = "5";
    private string _idleMinutesText = "5";
    private string _equivalentPrefixesText = "";

    public ActivityViewModel(ActivityStore store, Action rootsChanged)
    {
        _store = store;
        _rootsChanged = rootsChanged;
        Reload();
    }

    public ObservableCollection<ActivityRootSnapshot> Roots { get; } = new();

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
            if (value is null) return;
            Rollup = value.Config.Rollup;
            DepthText = value.Config.Depth.ToString(CultureInfo.InvariantCulture);
            DwellSecondsText = value.Config.DwellThreshold.TotalSeconds.ToString(CultureInfo.InvariantCulture);
            IdleMinutesText = value.Config.IdleTimeout.TotalMinutes.ToString(CultureInfo.InvariantCulture);
            EquivalentPrefixesText = string.Join(Environment.NewLine, value.Config.EquivalentPrefixes);
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
