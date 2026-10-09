using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.ViewModels;

/// <summary>A saved choice of which visited folders the activity tracker records.</summary>
public sealed class FolderTrackingSettingsViewModel : ObservableObject
{
    private readonly ActivityStore _store;
    private readonly Action _saved;
    private bool _baseline;
    private bool _allFolders;
    private string? _error;
    private bool _savedOnce;
    private TrackedFolderChoice[] _baselineTargets = Array.Empty<TrackedFolderChoice>();

    public FolderTrackingSettingsViewModel(ActivityStore store, Action? saved = null)
    {
        _store = store;
        _saved = saved ?? (() => { });
        _allFolders = _baseline = store.TrackAllFolders;
        LoadTargets();
    }

    public bool IsAvailable => _store.IsAvailable;
    public string? Notice => _store.Notice;
    public ObservableCollection<FolderTrackingTargetViewModel> Targets { get; } = new();
    public bool HasTargets => Targets.Count > 0;
    public bool CanEditTargets => IsAvailable && !AllFolders;
    public bool AllFolders
    {
        get => _allFolders;
        set => Select(value);
    }
    public bool TargetFoldersOnly
    {
        get => !_allFolders;
        set { if (value) Select(false); }
    }
    public bool HasChanges => _allFolders != _baseline || Targets.Count != _baselineTargets.Length ||
        Targets.Where((target, index) => !Same(target.Choice, _baselineTargets[index])).Any();
    public bool CanSave => IsAvailable && HasChanges;
    public string? ErrorMessage => _error;
    public string SaveStatus => HasChanges ? "Unsaved changes — select Save to apply."
        : _savedOnce ? "Saved. Your folder tracking choice is up to date." : "No unsaved changes.";
    public string SelectionSummary => AllFolders
        ? "Selected: All folders you visit in File Explorer."
        : Targets.Any(t => t.Enabled)
        ? $"Selected: {Targets.Count(t => t.Enabled)} target {(Targets.Count(t => t.Enabled) == 1 ? "folder" : "folders")} and their subfolders."
        : "No target folders selected. Folder recording will be off.";

    public bool AddTarget(string path, IReadOnlyList<string>? equivalentPrefixes = null)
    {
        if (!CanEditTargets) return false;
        var normalized = RootPathMatcher.Normalize(path);
        if (normalized is null) return Fail("Choose a folder on a drive or a network share.");
        if (Targets.Any(t => string.Equals(t.Path, normalized, StringComparison.OrdinalIgnoreCase)))
            return Fail("That folder is already in your tracking list.");
        var previous = _store.AllRoots.FirstOrDefault(r => !r.Config.IsAllFolders &&
            string.Equals(RootPathMatcher.Normalize(r.Path), normalized, StringComparison.OrdinalIgnoreCase));
        var target = new FolderTrackingTargetViewModel(new TrackedFolderChoice(previous?.RootId, normalized, true,
            equivalentPrefixes ?? previous?.Config.EquivalentPrefixes ?? Array.Empty<string>()));
        target.PropertyChanged += TargetEdited;
        Targets.Add(target);
        Edited();
        return true;
    }

    public void RemoveTarget(FolderTrackingTargetViewModel target)
    {
        if (!CanEditTargets || !Targets.Remove(target)) return;
        target.PropertyChanged -= TargetEdited;
        Edited();
    }

    public bool Save()
    {
        if (!HasChanges) return IsAvailable;
        var result = _store.TrySaveTrackingSettings(_allFolders, Targets.Select(t => t.Choice));
        _error = result.Saved ? null : result.UserMessage;
        if (result.Saved)
        {
            _baseline = _store.TrackAllFolders;
            _savedOnce = true;
            LoadTargets();
            _saved();
        }
        NotifyState();
        return result.Saved;
    }

    private void Select(bool allFolders)
    {
        if (_allFolders == allFolders) return;
        _allFolders = allFolders;
        _error = null;
        OnPropertyChanged(nameof(AllFolders));
        OnPropertyChanged(nameof(TargetFoldersOnly));
        NotifyState();
    }

    private void LoadTargets()
    {
        foreach (var target in Targets) target.PropertyChanged -= TargetEdited;
        Targets.Clear();
        foreach (var root in _store.Roots.Where(r => !r.Config.IsAllFolders))
        {
            var target = new FolderTrackingTargetViewModel(new TrackedFolderChoice(root.RootId, root.Path, root.Enabled, root.Config.EquivalentPrefixes));
            target.PropertyChanged += TargetEdited;
            Targets.Add(target);
        }
        _baselineTargets = Targets.Select(t => t.Choice).ToArray();
    }

    private static bool Same(TrackedFolderChoice a, TrackedFolderChoice b)
        => a.RootId == b.RootId && a.Path == b.Path && a.Enabled == b.Enabled &&
           a.EquivalentPrefixes.SequenceEqual(b.EquivalentPrefixes, StringComparer.OrdinalIgnoreCase);

    private void TargetEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderTrackingTargetViewModel.Enabled)) Edited();
    }

    private void Edited()
    {
        _error = null;
        NotifyState();
    }

    private bool Fail(string message)
    {
        _error = message;
        NotifyState();
        return false;
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(SaveStatus));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(HasTargets));
        OnPropertyChanged(nameof(CanEditTargets));
    }
}

/// <summary>One target card; edits are applied only when the parent dialog saves.</summary>
public sealed class FolderTrackingTargetViewModel : ObservableObject
{
    private readonly TrackedFolderChoice _original;
    private bool _enabled;
    public FolderTrackingTargetViewModel(TrackedFolderChoice choice) { _original = choice; _enabled = choice.Enabled; }
    public string Path => _original.Path;
    public string Name => Path.TrimEnd('\\').Split('\\')[^1];
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!SetProperty(ref _enabled, value)) return;
            OnPropertyChanged(nameof(Status));
        }
    }
    public string Status => Enabled ? "Selected for tracking" : "Paused";
    public TrackedFolderChoice Choice => _original with { Enabled = _enabled };
}
