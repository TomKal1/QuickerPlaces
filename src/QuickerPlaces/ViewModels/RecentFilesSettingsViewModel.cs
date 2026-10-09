using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models.RecentFiles;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.RecentFiles;

namespace QuickerPlaces.ViewModels;

/// <summary>A dialog-local draft. Selecting options never changes tracking until Save succeeds.</summary>
public sealed class RecentFilesSettingsViewModel : ObservableObject
{
    private readonly RecentFilesStore _store;
    private readonly Action _saved;
    private RecentFilesSettingsSnapshot _baseline;
    private bool _enabled;
    private readonly HashSet<DocumentKind> _kinds;
    private RecentFilesScope _scope;
    private string? _saveError;
    private bool _savedOnce;

    public RecentFilesSettingsViewModel(RecentFilesStore store, Action? saved = null)
    {
        _store = store;
        _saved = saved ?? (() => { });
        _baseline = store.Settings;
        _enabled = _baseline.Enabled;
        _kinds = new HashSet<DocumentKind>(_baseline.Kinds);
        _scope = _baseline.Scope;
    }

    public bool RecentFilesAvailable => _store.IsAvailable;
    public string? RecentFilesNotice => _store.Notice;
    public bool RecentFilesEnabled
    {
        get => _enabled;
        set { if (SetProperty(ref _enabled, value)) Edited(); }
    }

    public bool TrackPdf { get => _kinds.Contains(DocumentKind.Pdf); set => SetKind(DocumentKind.Pdf, value); }
    public bool TrackWord { get => _kinds.Contains(DocumentKind.Word); set => SetKind(DocumentKind.Word, value); }
    public bool TrackExcel { get => _kinds.Contains(DocumentKind.Excel); set => SetKind(DocumentKind.Excel, value); }

    public bool TrackEverywhere
    {
        get => _scope == RecentFilesScope.Everywhere;
        set { if (value) SelectScope(RecentFilesScope.Everywhere); }
    }

    public bool TrackUnderTrackedFolders
    {
        get => _scope == RecentFilesScope.TrackedFolders;
        set { if (value) SelectScope(RecentFilesScope.TrackedFolders); }
    }

    public bool HasChanges => _enabled != _baseline.Enabled || _scope != _baseline.Scope || !_kinds.SetEquals(_baseline.Kinds);
    public string? ErrorMessage => _kinds.Count == 0 ? "Choose at least one file type." : _saveError;
    public bool CanSave => RecentFilesAvailable && HasChanges && _kinds.Count > 0;
    public string SaveStatus => HasChanges ? "Unsaved changes — select Save to apply."
        : _savedOnce ? "Saved. Your selections are up to date." : "No unsaved changes.";

    public string SelectionSummary
    {
        get
        {
            var kinds = string.Join(", ", DocumentKinds.All.Where(_kinds.Contains).Select(k => k.Label()));
            var where = TrackEverywhere ? "Anywhere" : "Tracked folders only";
            return $"Selected: {(_enabled ? "Recording on" : "Recording off")} · {(kinds.Length == 0 ? "No file types" : kinds)} · {where}";
        }
    }

    public bool Save()
    {
        if (!RecentFilesAvailable || _kinds.Count == 0)
            return false;

        var result = _store.TrySaveSettings(_enabled, _kinds, _scope);
        _saveError = result.Saved ? null : result.UserMessage;
        if (result.Saved)
        {
            _baseline = _store.Settings;
            _savedOnce = true;
            _saved();
        }
        NotifyState();
        return result.Saved;
    }

    private void SetKind(DocumentKind kind, bool selected)
    {
        if (selected ? !_kinds.Add(kind) : !_kinds.Remove(kind))
            return;
        OnPropertyChanged(kind switch
        {
            DocumentKind.Pdf => nameof(TrackPdf),
            DocumentKind.Word => nameof(TrackWord),
            _ => nameof(TrackExcel),
        });
        Edited();
    }

    private void SelectScope(RecentFilesScope scope)
    {
        if (_scope == scope)
            return;
        _scope = scope;
        OnPropertyChanged(nameof(TrackEverywhere));
        OnPropertyChanged(nameof(TrackUnderTrackedFolders));
        Edited();
    }

    private void Edited()
    {
        _saveError = null;
        NotifyState();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(SaveStatus));
        OnPropertyChanged(nameof(SelectionSummary));
    }
}
