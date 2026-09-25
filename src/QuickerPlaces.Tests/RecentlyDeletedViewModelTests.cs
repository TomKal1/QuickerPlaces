using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Everything the Recently Deleted dialog decides (plan 5.5, D21), tested
/// without a Window (D5): its rows and empty state, which buttons are
/// enabled, the two confirmation questions, what each action does through
/// PlacesService, the conflict flow's bookkeeping, and the dialog's own
/// error line after a failed save (D1) or a refused one (D3).
/// </summary>
public sealed class RecentlyDeletedViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);

    private static PlacesService NewService(out FakePlacesStorage storage, out ManualTimeProvider clock)
    {
        storage = new FakePlacesStorage();
        clock = new ManualTimeProvider(Now);
        return new PlacesService(storage, clock);
    }

    private static Place AddFolder(PlacesService service, string alias, string? folder = null)
    {
        var result = service.TryAdd(alias, PlaceType.Folder, TestPaths.Folder(folder ?? alias), out var created, out var persistence);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(persistence.Saved, persistence.UserMessage);
        return created!;
    }

    private static void Remove(PlacesService service, Place place)
    {
        var persistence = service.Remove(place, out var removed);
        Assert.True(removed);
        Assert.True(persistence.Saved, persistence.UserMessage);
    }

    private static void Select(RecentlyDeletedViewModel viewModel, params Place[] places)
        => viewModel.SetSelection(viewModel.Rows.Where(r => places.Contains(r.Place)));

    // -----------------------------------------------------------------
    // Rows, empty state, enabling
    // -----------------------------------------------------------------

    [Fact]
    public void Rows_AreTheDeletedPlaces_NewestFirst_OnTheServiceClock()
    {
        var service = NewService(out _, out var clock);
        var a = AddFolder(service, "A");
        AddFolder(service, "B");
        var c = AddFolder(service, "C");
        Remove(service, a);
        clock.Advance(TimeSpan.FromDays(2));
        Remove(service, c);

        var viewModel = new RecentlyDeletedViewModel(service, TestZones.PlusTen);

        Assert.Equal(new[] { c, a }, viewModel.Rows.Select(r => r.Place));
        Assert.All(viewModel.Rows, r => Assert.Equal(service.UtcNow, r.Now));
        Assert.Equal(new[] { "7 days", "5 days" }, viewModel.Rows.Select(r => r.DaysRemainingText));
        Assert.False(viewModel.IsEmpty);
        Assert.True(viewModel.CanEmpty);
        Assert.False(viewModel.CanActOnSelection);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public void EmptyRecentlyDeleted_ShowsTheEmptyState_WithEveryActionDisabled()
    {
        var service = NewService(out _, out _);
        AddFolder(service, "Active");

        var viewModel = new RecentlyDeletedViewModel(service, TestZones.PlusTen);

        Assert.Empty(viewModel.Rows);
        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.CanEmpty);
        Assert.False(viewModel.CanActOnSelection);
        Assert.Equal("Nothing here. Places you remove stay here for 7 days.", RecentlyDeletedViewModel.EmptyMessage);
    }

    [Fact]
    public void Selection_EnablesRestoreAndDelete_AndSaysSo()
    {
        var service = NewService(out _, out _);
        var docs = AddFolder(service, "Docs");
        Remove(service, docs);
        var viewModel = new RecentlyDeletedViewModel(service, TestZones.PlusTen);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Select(viewModel, docs);
        Assert.True(viewModel.CanActOnSelection);
        Assert.Equal(1, viewModel.SelectedCount);
        Assert.Contains(nameof(RecentlyDeletedViewModel.CanActOnSelection), changed);

        viewModel.SetSelection(Array.Empty<RecentlyDeletedRowViewModel>());
        Assert.False(viewModel.CanActOnSelection);
    }

    // -----------------------------------------------------------------
    // Confirmations (D18)
    // -----------------------------------------------------------------

    [Fact]
    public void Confirmations_NameOnePlace_AndCountSeveral()
    {
        var service = NewService(out _, out _);
        var docs = AddFolder(service, "Docs");
        Remove(service, docs);
        var viewModel = new RecentlyDeletedViewModel(service, TestZones.PlusTen);

        Select(viewModel, docs);
        Assert.Equal("Permanently delete \"Docs\"? This can't be undone.", viewModel.DeleteSelectedConfirmation);
        Assert.Equal("Permanently delete \"Docs\", the only place in Recently Deleted? This can't be undone.", viewModel.EmptyConfirmation);

        var wiki = AddFolder(service, "Wiki");
        var notes = AddFolder(service, "Notes");
        Remove(service, wiki);
        Remove(service, notes);
        viewModel.Reload();
        Select(viewModel, docs, wiki);

        Assert.Equal("Permanently delete 2 places? This can't be undone.", viewModel.DeleteSelectedConfirmation);
        Assert.Equal("Permanently delete all 3 places in Recently Deleted? This can't be undone.", viewModel.EmptyConfirmation);
    }

    // -----------------------------------------------------------------
    // Restore selected and the conflict flow (D15, D22)
    // -----------------------------------------------------------------

    [Fact]
    public void RestoreSelected_RestoresWhatIsFree_InOneSave_AndReturnsTheConflicts()
    {
        var service = NewService(out var storage, out var clock);
        var docs = AddFolder(service, "Docs");
        var wiki = AddFolder(service, "Wiki");
        var notes = AddFolder(service, "Notes");
        Remove(service, docs);
        clock.Advance(TimeSpan.FromMinutes(1));
        Remove(service, wiki);
        clock.Advance(TimeSpan.FromMinutes(1));
        Remove(service, notes);
        var newDocs = AddFolder(service, "docs", "Elsewhere");
        var viewModel = new RecentlyDeletedViewModel(service, TestZones.PlusTen);
        Select(viewModel, docs, wiki);
        var writes = storage.WriteCount;

        var conflicts = viewModel.RestoreSelected();

        Assert.Equal(writes + 1, storage.WriteCount);
        var conflict = Assert.Single(conflicts);
        Assert.Same(docs, conflict.Place);
        Assert.Same(newDocs, conflict.AliasHeldBy);
        Assert.Equal(new[] { wiki }, viewModel.RestoredPlaces);
        Assert.Null(wiki.DeletedAt);
        Assert.Equal(new[] { notes, docs }, viewModel.Rows.Select(r => r.Place));
        Assert.False(viewModel.CanActOnSelection);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public void ConflictFlow_AskedAfresh_ThenRestoredUnderTheEditedValues_IsRecorded()
    {
        var service = NewService(out _, out _);
        var docs = AddFolder(service, "Docs");
        Remove(service, docs);
        AddFolder(service, "Docs", "Elsewhere");
        var viewModel = new RecentlyDeletedViewModel(service, TestZones.PlusTen);
        Select(viewModel, docs);

        var returned = Assert.Single(viewModel.RestoreSelected());
        var current = viewModel.CurrentConflict(returned.Place);
        Assert.NotNull(current);
        Assert.Equal(returned.Explanation, current!.Explanation);

        // What PlaceFormDialog.ShowRestore commits, then reports back.
        Assert.True(service.TryRestore(docs, "Docs (old)", docs.Resource, out _).Success);
        viewModel.NoteRestored(docs);

        Assert.Equal(new[] { docs }, viewModel.RestoredPlaces);
        Assert.Equal("Docs (old)", docs.Alias);
        Assert.True(viewModel.IsEmpty);
        Assert.Null(viewModel.ErrorMessage);
    }

    /// <summary>A conflict that has gone away by the time it is asked about is restored as Restore selected would have; one that has left Recently Deleted is not asked about.</summary>
    [Fact]
    public void CurrentConflict_RestoresAPlaceNoLongerInTheWay_AndSkipsOneThatIsGone()
    {
        var service = NewService(out _, out _);
        var docs = AddFolder(service, "Docs");
        var wiki = AddFolder(service, "Wiki");
        Remove(service, docs);
        Remove(service, wiki);
        var newDocs = AddFolder(service, "Docs", "Elsewhere");
        AddFolder(service, "Wiki", "Elsewhere2");
        var viewModel = new RecentlyDeletedViewModel(service, TestZones.PlusTen);
        Select(viewModel, docs, wiki);
        var conflicts = viewModel.RestoreSelected();
        Assert.Equal(2, conflicts.Count);

        Remove(service, newDocs);
        Assert.True(service.DeletePermanently(new[] { wiki }).Saved);

        Assert.Null(viewModel.CurrentConflict(docs));
        Assert.Null(viewModel.CurrentConflict(wiki));

        Assert.Null(docs.DeletedAt);
        Assert.Equal(new[] { docs }, viewModel.RestoredPlaces);
        Assert.Equal(new[] { newDocs }, viewModel.Rows.Select(r => r.Place));
    }

    // -----------------------------------------------------------------
    // Permanent deletion
    // -----------------------------------------------------------------

    [Fact]
    public void DeleteSelectedPermanently_RemovesOnlyTheSelection_InOneSave()
    {
        var service = NewService(out var storage, out _);
        var docs = AddFolder(service, "Docs");
        var wiki = AddFolder(service, "Wiki");
        var active = AddFolder(service, "Active");
        Remove(service, docs);
        Remove(service, wiki);
        var viewModel = new RecentlyDeletedViewModel(service, TestZones.PlusTen);
        Select(viewModel, docs);
        var writes = storage.WriteCount;

        viewModel.DeleteSelectedPermanently();

        Assert.Equal(writes + 1, storage.WriteCount);
        Assert.Equal(new[] { wiki }, viewModel.Rows.Select(r => r.Place));
        Assert.Equal(new[] { wiki }, service.RecentlyDeleted);
        Assert.Equal(new[] { active }, service.Places);
        Assert.Empty(viewModel.RestoredPlaces);
    }

    [Fact]
    public void EmptyRecentlyDeleted_RemovesEveryDeletedPlace_AndKeepsTheActiveOnes()
    {
        var service = NewService(out var storage, out _);
        var docs = AddFolder(service, "Docs");
        var wiki = AddFolder(service, "Wiki");
        var active = AddFolder(service, "Active");
        Remove(service, docs);
        Remove(service, wiki);
        var viewModel = new RecentlyDeletedViewModel(service, TestZones.PlusTen);
        var writes = storage.WriteCount;

        viewModel.EmptyRecentlyDeleted();

        Assert.Equal(writes + 1, storage.WriteCount);
        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.CanEmpty);
        Assert.Empty(service.RecentlyDeleted);
        Assert.Equal(new[] { active }, service.Places);
    }

    // -----------------------------------------------------------------
    // The error line (D1, D3)
    // -----------------------------------------------------------------

    /// <summary>D1: a failed save shows its message in the dialog, the change stays made in memory, and the line clears once a later save succeeds.</summary>
    [Fact]
    public void FailedSave_ShowsItsMessage_KeepsTheChange_AndClearsOnceSaved()
    {
        var service = NewService(out var storage, out _);
        var docs = AddFolder(service, "Docs");
        var wiki = AddFolder(service, "Wiki");
        Remove(service, docs);
        Remove(service, wiki);
        var viewModel = new RecentlyDeletedViewModel(service, TestZones.PlusTen);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        storage.FailNextWrite = true;
        Select(viewModel, docs);
        viewModel.DeleteSelectedPermanently();

        Assert.NotNull(viewModel.ErrorMessage);
        Assert.True(viewModel.HasError);
        Assert.Contains(nameof(RecentlyDeletedViewModel.HasError), changed);
        Assert.True(service.HasUnsavedChanges);
        Assert.Equal(new[] { wiki }, viewModel.Rows.Select(r => r.Place));
        Assert.DoesNotContain(docs, service.RecentlyDeleted);

        Select(viewModel, wiki);
        Assert.Empty(viewModel.RestoreSelected());

        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.HasError);
        Assert.False(service.HasUnsavedChanges);
    }

    /// <summary>The conflict flow's dialog discards its save result, so a failed save there is reported from HasUnsavedChanges.</summary>
    [Fact]
    public void FailedSaveInTheConflictFlow_IsReportedFromTheService()
    {
        var service = NewService(out var storage, out _);
        var docs = AddFolder(service, "Docs");
        Remove(service, docs);
        AddFolder(service, "Docs", "Elsewhere");
        var viewModel = new RecentlyDeletedViewModel(service, TestZones.PlusTen);

        storage.FailNextWrite = true;
        Assert.True(service.TryRestore(docs, "Docs (old)", docs.Resource, out var persistence).Success);
        Assert.False(persistence.Saved);
        viewModel.NoteRestored(docs);

        Assert.NotNull(viewModel.ErrorMessage);
        Assert.Contains("haven't been saved yet", viewModel.ErrorMessage);
        Assert.Equal(new[] { docs }, viewModel.RestoredPlaces);
        Assert.Null(docs.DeletedAt);
    }

    /// <summary>D3: while recovery is unresolved every action is refused, and the dialog says why.</summary>
    [Fact]
    public void Actions_WhileRecoveryIsUnresolved_ShowTheBlockedMessage()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = "{ not valid json" };
        var service = new PlacesService(storage, new ManualTimeProvider(Now));
        Assert.True(service.IsRecoveryUnresolved);
        var stranger = new Place { Alias = "X", Type = PlaceType.Folder, Resource = TestPaths.Folder("X"), DeletedAt = Now };
        var viewModel = new RecentlyDeletedViewModel(service, TestZones.PlusTen);
        Assert.True(viewModel.IsEmpty);

        viewModel.SetSelection(new[] { new RecentlyDeletedRowViewModel(stranger, Now, TestZones.PlusTen) });
        viewModel.DeleteSelectedPermanently();

        Assert.Equal(service.RecoveryBlockedMessage, viewModel.ErrorMessage);
        Assert.Equal(0, storage.WriteCount);
    }
}
