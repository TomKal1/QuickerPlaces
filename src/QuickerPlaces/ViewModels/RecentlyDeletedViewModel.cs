using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services;

namespace QuickerPlaces.ViewModels;

/// <summary>
/// Backs the Recently Deleted dialog (plan 5.5): its rows, which buttons are
/// enabled, the confirmation wording, the dialog's own error line, and every
/// call into PlacesService. RecentlyDeletedDialog is a thin view over this
/// (D21): it binds, passes its selection in, asks the two irreversible
/// questions (MessageForm.ShowDestructiveConfirm) and runs the conflict
/// flow (PlaceFormDialog.ShowRestore), because those are windows.
///
/// UI-free and linked into the test project, so every decision the dialog
/// makes is tested without a Window (D5). Each action goes through
/// PlacesService, which saves once per action (D22) and keeps the change in
/// memory if the save fails (D1). A failure is shown in <see
/// cref="ErrorMessage"/> so the user isn't left guessing behind a modal;
/// the main window's banner stays driven by PlacesService.HasUnsavedChanges
/// alone, refreshed by MainViewModel after each action (<see cref="Changed"/>).
/// </summary>
public sealed class RecentlyDeletedViewModel : ObservableObject
{
    /// <summary>Shown over the empty grid.</summary>
    public const string EmptyMessage = "Nothing here. Places you remove stay here for 7 days.";

    private readonly PlacesService _placesService;
    private readonly TimeZoneInfo? _localZone;
    private readonly List<Place> _restored = new();
    private IReadOnlyList<RecentlyDeletedRowViewModel> _selection = Array.Empty<RecentlyDeletedRowViewModel>();
    private string? _errorMessage;

    /// <param name="localZone">The zone rows show dates in; null for the machine's own. Tests pass a fixed one.</param>
    public RecentlyDeletedViewModel(PlacesService placesService, TimeZoneInfo? localZone = null)
    {
        _placesService = placesService;
        _localZone = localZone;
        Reload();
    }

    /// <summary>The places in Recently Deleted, most recently removed first.</summary>
    public ObservableCollection<RecentlyDeletedRowViewModel> Rows { get; } = new();

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Restore selected and Delete selected permanently: only with something selected.</summary>
    public bool CanActOnSelection => _selection.Count > 0;

    /// <summary>Empty Recently Deleted: only when there is something to empty.</summary>
    public bool CanEmpty => Rows.Count > 0;

    public int SelectedCount => _selection.Count;

    /// <summary>
    /// The dialog's own error line: why the last action's save failed, or
    /// why it was refused. Null when there is nothing to report.
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => ErrorMessage is not null;

    /// <summary>Every place restored while the dialog was open, in restore order. Each is also passed to <see cref="Changed"/> as it is restored.</summary>
    public IReadOnlyList<Place> RestoredPlaces => _restored;

    /// <summary>
    /// Raised after every action, with the places that action restored
    /// (often none), so the main window can catch up while the dialog is
    /// still open instead of when it closes.
    /// </summary>
    public event Action<IReadOnlyList<Place>>? Changed;

    /// <summary>Called by the view whenever the grid's selection changes.</summary>
    public void SetSelection(IEnumerable<RecentlyDeletedRowViewModel> selected)
    {
        _selection = selected.ToList();
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanActOnSelection));
    }

    /// <summary>The question asked before Delete selected permanently.</summary>
    public string DeleteSelectedConfirmation => _selection.Count == 1
        ? $"Permanently delete \"{_selection[0].Alias}\"? This can't be undone."
        : $"Permanently delete {_selection.Count} places? This can't be undone.";

    /// <summary>The question asked before Empty Recently Deleted.</summary>
    public string EmptyConfirmation => Rows.Count == 1
        ? $"Permanently delete \"{Rows[0].Alias}\", the only place in Recently Deleted? This can't be undone."
        : $"Permanently delete all {Rows.Count} places in Recently Deleted? This can't be undone.";

    /// <summary>
    /// Restore selected: restores every selected place that is free to come
    /// back, in one save, and returns the rest as conflicts for the edit
    /// flow (D15), newest deletion first (D22). None is skipped: the view
    /// takes each returned conflict to <see cref="CurrentConflict"/> and
    /// then PlaceFormDialog.ShowRestore.
    /// </summary>
    public IReadOnlyList<RestoreConflict> RestoreSelected()
    {
        var (restored, conflicts, persistence) = _placesService.RestoreSelected(_selection.Select(r => r.Place));
        _restored.AddRange(restored);
        ApplyPersistence(persistence);
        Reload();
        Changed?.Invoke(restored);
        return conflicts;
    }

    /// <summary>
    /// What stands in the way of <paramref name="place"/> now, for the next
    /// step of the conflict flow. Asked afresh rather than reusing the
    /// conflict RestoreSelected returned, because an earlier conflict's
    /// edit may have taken a new alias or destination in the meantime. If
    /// nothing stands in the way any more it is restored here, as
    /// Restore selected would have; null then, or if it has left Recently
    /// Deleted, since there is nothing to ask.
    /// </summary>
    public RestoreConflict? CurrentConflict(Place place)
    {
        if (_placesService.GetRestoreConflict(place) is { } conflict)
            return conflict;

        if (place.DeletedAt is null || !_placesService.RecentlyDeleted.Contains(place))
            return null;

        // Only D3 can refuse this now (it would say so through the
        // persistence result); nothing else stands in the way.
        var restored = _placesService.TryRestore(place, out var persistence).Success;
        if (restored)
            _restored.Add(place);

        ApplyPersistence(persistence);
        Reload();
        Changed?.Invoke(restored ? new[] { place } : Array.Empty<Place>());
        return null;
    }

    /// <summary>
    /// Records a place the conflict flow has just restored (through
    /// PlacesService.TryRestore with the edited alias and destination).
    /// PlaceFormDialog discards that call's save result, as it does for
    /// every edit, so the error line is refreshed from the service.
    /// </summary>
    public void NoteRestored(Place place)
    {
        _restored.Add(place);
        ApplyPersistence(null);
        Reload();
        Changed?.Invoke(new[] { place });
    }

    /// <summary>Delete selected permanently, after the view has asked <see cref="DeleteSelectedConfirmation"/>.</summary>
    public void DeleteSelectedPermanently()
    {
        var persistence = _placesService.DeletePermanently(_selection.Select(r => r.Place));
        ApplyPersistence(persistence);
        Reload();
        Changed?.Invoke(Array.Empty<Place>());
    }

    /// <summary>Empty Recently Deleted, after the view has asked <see cref="EmptyConfirmation"/>.</summary>
    public void EmptyRecentlyDeleted()
    {
        var persistence = _placesService.EmptyRecentlyDeleted();
        ApplyPersistence(persistence);
        Reload();
        Changed?.Invoke(Array.Empty<Place>());
    }

    /// <summary>Rebuilds every row from the service, with the service's clock, so the countdown is current.</summary>
    public void Reload()
    {
        var now = _placesService.UtcNow;

        Rows.Clear();
        foreach (var place in _placesService.RecentlyDeleted)
            Rows.Add(new RecentlyDeletedRowViewModel(place, now, _localZone));

        // The grid clears its selection when its rows are replaced and
        // reports that through SetSelection; this makes sure no action can
        // run on a stale row before it does.
        SetSelection(Array.Empty<RecentlyDeletedRowViewModel>());

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanEmpty));
    }

    /// <summary>
    /// Sets the error line after an action, the way MainViewModel's
    /// RefreshPersistenceState sets the banner: a failure reported just now
    /// shows its own message; the line clears only once the store is
    /// actually saved (HasUnsavedChanges, never a returned Ok, which is
    /// also what an action that wrote nothing reports); otherwise it stays
    /// as it was. <paramref name="result"/> is null when the save result
    /// was discarded (the conflict flow's dialog): then an unsaved store
    /// gets a standing sentence if the line has nothing to say yet.
    /// </summary>
    private void ApplyPersistence(PersistenceResult? result)
    {
        if (result is { Saved: false, UserMessage: { } message })
            ErrorMessage = message;
        else if (!_placesService.HasUnsavedChanges)
            ErrorMessage = null;
        else if (result is null && ErrorMessage is null)
            ErrorMessage = $"Some changes to \"{_placesService.PlacesFilePath}\" haven't been saved yet. They are kept for now: close this and use Retry in the main window.";
    }
}
