using System;
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
    public void Rows_AreMostRecentFirst_TheFirstIsSelected_AndItsFilesListed()
    {
        Add("Tower A", new[] { "Tower A" }, Spec);
        Add("Tower B", new[] { "Tower B", "Markups" }, A101, A102);

        var vm = NewViewModel();

        Assert.Equal(new[] { "Tower B", "Tower A" }, vm.Rows.Select(r => r.Name));
        Assert.Equal("Tower B", vm.SelectedRow!.Name);
        Assert.Equal(new[] { "A-101.pdf", "A-102.pdf" }, vm.SelectedFiles.Select(f => f.FileName));
        Assert.Equal(@"C:\Jobs\Tower B", vm.SelectedFiles[0].Folder);
        Assert.StartsWith("2 files · saved ", vm.SelectedRow.DetailText);
        Assert.Equal(new[] { "All tags", "Markups (1)", "Tower A (1)", "Tower B (1)" }, vm.TagFilters.Select(t => t.Label));
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
        Add("Tower A", new[] { "Admin" }, Spec);
        Add("Tower B", new[] { "Markups" }, A101);
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
