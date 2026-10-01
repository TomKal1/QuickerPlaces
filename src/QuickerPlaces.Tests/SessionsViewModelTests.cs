using System;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>The Sessions window's decisions (sessions plan §5): search, tag filter, selection, open and delete.</summary>
public sealed class SessionsViewModelTests
{
    private const string A101 = @"C:\Jobs\Tower B\A-101.pdf";
    private const string A102 = @"C:\Jobs\Tower B\A-102.pdf";
    private const string Spec = @"C:\Jobs\Tower A\Spec.pdf";

    private readonly ManualTimeProvider _time = new();
    private readonly FakePlacesStorage _storage = new();
    private readonly FakeShell _shell = new();
    private readonly SessionStore _store;

    public SessionsViewModelTests()
    {
        _store = new SessionStore(_storage, _time);
    }

    private SessionSnapshot Add(string name, string[] tags, params string[] files)
    {
        Assert.True(_store.TryCreate(name, tags, files, out var created, out _).Success);
        _time.Advance(TimeSpan.FromMinutes(1));
        return created!;
    }

    private SessionsViewModel NewViewModel() => new(_store, new SessionLauncher(_store, _shell), TestZones.PlusTen);

    [Fact]
    public void Empty_ShowsTheEmptyMessage_AndOnlyAllTags()
    {
        var vm = NewViewModel();

        Assert.True(vm.IsListEmpty);
        Assert.Equal(SessionsViewModel.EmptyMessage, vm.ListPlaceholder);
        Assert.Equal(new[] { "All tags" }, vm.TagFilters.Select(t => t.Label));
        Assert.False(vm.HasSelection);
    }

    [Fact]
    public void Rows_AreInTheSavedOrder_TheFirstIsSelected_AndItsFilesListed()
    {
        Add("Tower B", new[] { "Tower B", "Markups" }, A101, A102);
        Add("Tower A", new[] { "Tower A" }, Spec);

        var vm = NewViewModel();

        Assert.Equal(new[] { "Tower B", "Tower A" }, vm.Rows.Select(r => r.Name));
        Assert.Equal("Tower B", vm.SelectedRow!.Name);
        Assert.Equal(new[] { "A-101.pdf", "A-102.pdf" }, vm.SelectedFiles.Select(f => f.FileName));
        Assert.Equal(@"C:\Jobs\Tower B", vm.SelectedFiles[0].Folder);
        Assert.StartsWith("2 files · saved ", vm.SelectedRow.DetailText);
        Assert.Equal(new[] { "All tags", "Markups (1)", "Tower A (1)", "Tower B (1)" }, vm.TagFilters.Select(t => t.Label));
    }

    [Fact]
    public void ACardsDetailLine_GivesDatesOnly_NoTimeOfDay()
    {
        _shell.ExistingFiles.Add(Spec);
        var tower = Add("Tower A", Array.Empty<string>(), Spec);
        var vm = NewViewModel();
        vm.OpenSelected();

        // The zone is ten hours ahead, so the date shown is the zone's, and no time follows it.
        var zoned = TimeZoneInfo.ConvertTime(tower.UpdatedAt, TestZones.PlusTen).DateTime;
        var saved = zoned.ToString("d", CultureInfo.CurrentCulture);
        var detail = vm.Rows.Single().DetailText;

        Assert.StartsWith($"1 file · saved {saved} · opened ", detail);
        Assert.DoesNotContain(zoned.ToString("t", CultureInfo.CurrentCulture), detail);
    }

    [Fact]
    public void ACardsNumber_IsItsPlaceInTheOrder_OneToNine_AndNoneAfter()
    {
        for (var i = 1; i <= 11; i++)
            Add($"Session {i:00}", Array.Empty<string>(), A101);

        var vm = NewViewModel();

        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", null, null }, vm.Rows.Select(r => r.ShortcutText));
        Assert.Equal(new[] { true, true, true, true, true, true, true, true, true, false, false }, vm.Rows.Select(r => r.HasShortcut));
        Assert.Equal("Ctrl+Shift+3 opens this session", vm.Rows[2].ShortcutToolTip);
        Assert.Equal("Ctrl+Shift+9 opens this session", vm.Rows[8].ShortcutToolTip);
        Assert.Null(vm.Rows[9].ShortcutToolTip);
    }

    private SessionsViewModel NoSelectionViewModel() => new(_store, new SessionLauncher(_store, _shell), TestZones.PlusTen, allowsNoSelection: true);

    [Fact]
    public void WhereNoSelectionIsAllowed_NoCardIsSelectedUntilOneIsChosen_AndAChoiceCanBePutDown()
    {
        Add("Tower A", Array.Empty<string>(), Spec);
        Add("Tower B", Array.Empty<string>(), A101);

        var vm = NoSelectionViewModel();

        Assert.Equal(2, vm.Rows.Count);
        Assert.Null(vm.SelectedRow);
        Assert.False(vm.HasSelection);

        vm.SelectedRow = vm.Rows[1];
        Assert.Equal("Tower B", vm.SelectedRow!.Name);

        vm.SelectedRow = null;
        Assert.False(vm.HasSelection);
        Assert.Empty(vm.SelectedFiles);
    }

    [Fact]
    public void ACardsSelection_SurvivesTheCardsBeingRebuilt_AndNoSelectionStaysNone()
    {
        Add("Tower A", Array.Empty<string>(), Spec);
        Add("Tower B", Array.Empty<string>(), A101);
        var vm = NoSelectionViewModel();

        vm.SearchText = "tower";
        Assert.Null(vm.SelectedRow);

        vm.SelectedRow = vm.Rows.Single(r => r.Name == "Tower A");
        vm.SearchText = "tower ";
        Assert.Equal("Tower A", vm.SelectedRow!.Name);

        // The list clears its own selection while it rebuilds; that is not the user putting the card down.
        vm.Reload(vm.SelectedRow.Id);
        Assert.Equal("Tower A", vm.SelectedRow!.Name);
    }

    [Fact]
    public void Delete_WhereNoSelectionIsAllowed_LeavesNothingSelected()
    {
        Add("Tower A", Array.Empty<string>(), Spec);
        Add("Tower B", Array.Empty<string>(), A101);
        var vm = NoSelectionViewModel();
        vm.SelectedRow = vm.Rows[0];

        vm.DeleteSelected();

        Assert.Single(vm.Rows);
        Assert.Null(vm.SelectedRow);
    }

    private static string[] Names(SessionsViewModel vm) => vm.Rows.Select(r => r.Name).ToArray();

    [Fact]
    public void ADraggedCard_TakesTheDroppedOnCardsPlace_AndItsNumberWithIt()
    {
        Add("Tower A", Array.Empty<string>(), Spec);
        Add("Tower B", Array.Empty<string>(), A101);
        Add("Tower C", Array.Empty<string>(), A102);
        var vm = NewViewModel();

        // Down onto C: lands after it, as a dragged favourite does.
        vm.Move(vm.Rows[0], vm.Rows[2]);
        Assert.Equal(new[] { "Tower B", "Tower C", "Tower A" }, Names(vm));
        Assert.Equal(new[] { "1", "2", "3" }, vm.Rows.Select(r => r.ShortcutText));
        Assert.Equal("\"Tower A\" opens with Ctrl+Shift+3.", vm.StatusMessage);

        // Up onto B: lands before it.
        vm.Move(vm.Rows[2], vm.Rows[0]);
        Assert.Equal(new[] { "Tower A", "Tower B", "Tower C" }, Names(vm));

        // Onto empty space: last.
        vm.Move(vm.Rows[0], null);
        Assert.Equal(new[] { "Tower B", "Tower C", "Tower A" }, Names(vm));
    }

    [Fact]
    public void TheOrderOfTheCards_IsKept_AndDroppingACardOnItselfChangesNothing()
    {
        Add("Tower A", Array.Empty<string>(), Spec);
        Add("Tower B", Array.Empty<string>(), A101);
        var vm = NewViewModel();

        var writes = _storage.WriteCount;
        vm.Move(vm.Rows[0], vm.Rows[0]);
        Assert.Equal(writes, _storage.WriteCount);

        vm.Move(vm.Rows[0], vm.Rows[1]);

        Assert.Equal(new[] { "Tower B", "Tower A" }, Names(NewViewModel()));
    }

    [Fact]
    public void ADraggedCard_KeepsTheSelection()
    {
        Add("Tower A", Array.Empty<string>(), Spec);
        var b = Add("Tower B", Array.Empty<string>(), A101);
        var vm = NoSelectionViewModel();
        vm.SelectedRow = vm.Rows.Single(r => r.Id == b.Id);

        vm.Move(vm.Rows[0], vm.Rows[1]);

        Assert.Equal(b.Id, vm.SelectedRow!.Id);
    }

    [Fact]
    public void Select_SelectsThatSession_AndClearsAFilterThatHidesIt()
    {
        Add("Tower A", new[] { "Tower A" }, Spec);
        var towerB = Add("Tower B", new[] { "Tower B" }, A101);
        var vm = NewViewModel();
        vm.SelectedTag = "tower a";
        Assert.DoesNotContain(vm.Rows, r => r.Id == towerB.Id);

        Assert.True(vm.Select(towerB.Id));

        Assert.Equal("Tower B", vm.SelectedRow!.Name);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Null(vm.SelectedTag);
    }

    [Fact]
    public void Select_ReturnsFalse_ForASessionThatDoesNotExist()
    {
        Add("Tower A", new[] { "Tower A" }, Spec);
        var vm = NewViewModel();

        Assert.False(vm.Select("missing"));
        Assert.Equal("Tower A", vm.SelectedRow!.Name);
    }

    [Fact]
    public void ChoosingATag_FiltersTheList_AndMarksTheChoice()
    {
        Add("Tower A", new[] { "Tower A", "Markups" }, Spec);
        Add("Tower B", new[] { "Tower B" }, A101);
        var vm = NewViewModel();

        vm.SelectedTag = "markups";

        Assert.Equal(new[] { "Tower A" }, vm.Rows.Select(r => r.Name));
        Assert.Equal("Tower A", vm.SelectedRow!.Name);
        Assert.Equal(new[] { false, true, false, false }, vm.TagFilters.Select(t => t.IsSelected));

        vm.SelectedTag = null;
        Assert.Equal(2, vm.Rows.Count);
        Assert.True(vm.TagFilters[0].IsSelected);
    }

    [Theory]
    [InlineData("tower", 2)]
    [InlineData("markups", 1)]
    [InlineData("a-102", 1)]
    [InlineData("tower spec", 1)]
    [InlineData("nothing", 0)]
    [InlineData("  ", 2)]
    public void Search_MatchesEveryWord_InNameTagsOrFileNames(string search, int expected)
    {
        Add("Tower A", new[] { "Admin" }, Spec);
        Add("Tower B", new[] { "Markups" }, A101, A102);
        var vm = NewViewModel();

        vm.SearchText = search;

        Assert.Equal(expected, vm.Rows.Count);
        if (expected == 0)
            Assert.Equal(SessionsViewModel.NoMatchesMessage, vm.ListPlaceholder);
    }

    [Fact]
    public void Search_DoesNotMatchFolderNames()
    {
        Add("One", Array.Empty<string>(), A101);

        Assert.False(SessionsViewModel.Matches(_store.Sessions[0], "Jobs", null));
    }

    [Fact]
    public void OpenSelected_OpensItsPdfs_ReportsIt_AndKeepsItSelected()
    {
        Add("Tower A", Array.Empty<string>(), Spec);
        Add("Tower B", Array.Empty<string>(), A101, A102);
        _shell.ExistingFiles.UnionWith(new[] { A101, Spec });
        var vm = NewViewModel();
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "Tower A");

        vm.OpenSelected();

        Assert.Equal(new[] { Spec }, _shell.Opened);
        Assert.Equal("Opened 1 file from \"Tower A\".", vm.StatusMessage);
        Assert.False(vm.HasError);
        Assert.Equal("Tower A", vm.SelectedRow!.Name);
        Assert.Equal("Tower A", vm.Rows[0].Name);
        Assert.Contains(" · opened ", vm.SelectedRow.DetailText);
    }

    [Fact]
    public void OpenSelected_WithAMissingFile_ShowsWhichOne()
    {
        Add("Tower B", Array.Empty<string>(), A101, A102);
        _shell.ExistingFiles.Add(A101);
        var vm = NewViewModel();

        vm.OpenSelected();

        Assert.Equal(new[] { A101 }, _shell.Opened);
        Assert.Equal("1 file couldn't be found and wasn't opened: A-102.pdf.", vm.ErrorMessage);
    }

    [Fact]
    public void OpenFile_OpensJustThatPdf()
    {
        Add("Tower B", Array.Empty<string>(), A101, A102);
        _shell.ExistingFiles.UnionWith(new[] { A101, A102 });
        var vm = NewViewModel();

        vm.OpenFile(vm.SelectedFiles[1]);

        Assert.Equal(new[] { A102 }, _shell.Opened);
    }

    [Fact]
    public void DeleteSelected_RemovesIt_SelectsTheNext_AndDropsItsUnusedTags()
    {
        Add("Tower B", new[] { "Markups" }, A101);
        Add("Tower A", new[] { "Admin" }, Spec);
        var vm = NewViewModel();
        Assert.Contains("\"Tower B\"", vm.DeleteConfirmation);
        Assert.Contains("Its files stay where they are.", vm.DeleteConfirmation);

        vm.DeleteSelected();

        Assert.Equal(new[] { "Tower A" }, vm.Rows.Select(r => r.Name));
        Assert.Equal("Tower A", vm.SelectedRow!.Name);
        Assert.Equal("Deleted \"Tower B\".", vm.StatusMessage);
        Assert.Equal(new[] { "All tags", "Admin (1)" }, vm.TagFilters.Select(t => t.Label));
    }

    [Fact]
    public void DeletingTheLastSessionOfTheChosenTag_GoesBackToAllTags()
    {
        Add("Tower A", new[] { "Admin" }, Spec);
        Add("Tower B", new[] { "Markups" }, A101);
        var vm = NewViewModel();
        vm.SelectedTag = "Markups";

        vm.DeleteSelected();

        Assert.Null(vm.SelectedTag);
        Assert.Equal(new[] { "Tower A" }, vm.Rows.Select(r => r.Name));
    }

    [Fact]
    public void NoteSaved_ClearsTheFilters_AndSelectsTheSavedSession()
    {
        Add("Tower A", new[] { "Admin" }, Spec);
        var vm = NewViewModel();
        vm.SearchText = "Tower A";
        var added = Add("Tower B", new[] { "Markups" }, A101);

        vm.NoteSaved(added.Id, null);

        Assert.Equal("", vm.SearchText);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal("Tower B", vm.SelectedRow!.Name);
        Assert.Equal("Saved \"Tower B\" with 1 file.", vm.StatusMessage);
    }

    [Fact]
    public void AStoreThatRefusesChanges_TurnsEditingOff_AndShowsItsNotice()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = @"{""schemaVersion"":9,""sessions"":[]}" };
        var store = new SessionStore(storage, _time);

        var vm = new SessionsViewModel(store, new SessionLauncher(store, _shell), TestZones.PlusTen);

        Assert.False(vm.CanChange);
        Assert.True(vm.HasNotice);
        Assert.False(vm.CanEditSelection);
    }
}
