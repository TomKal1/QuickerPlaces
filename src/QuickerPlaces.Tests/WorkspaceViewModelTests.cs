using System;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Models.Workspace;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Services.Workspace;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The workspace in the main window (configurable canvas plan M3): Activity
/// Atlas's panels and where they go, the one query shared with the Library
/// and remembered per layout, Add panel and Hide committed at once, and the
/// shelf's documents as a session.
/// </summary>
public sealed class WorkspaceViewModelTests
{
    private const string Pdf = @"C:\Jobs\Acme\A-101.pdf";
    private const string Word = @"C:\Jobs\Acme\Report.docx";

    private readonly ManualTimeProvider _time = new();
    private readonly FakeShell _shell = new();
    private readonly FakePlacesStorage _layoutStorage = new() { StoreFilePath = @"C:\fake\workspace-layouts.json" };
    private readonly PlacesService _places;
    private readonly SessionStore _sessions;

    public WorkspaceViewModelTests()
    {
        _places = new PlacesService(new FakePlacesStorage(), _time);
        _sessions = new SessionStore(new FakePlacesStorage(), _time);
    }

    private WorkspaceViewModel NewWorkspace()
    {
        var library = new LibraryViewModel(_places, _sessions, new ActivityStore(new FakePlacesStorage(), _time),
            new RecentFilesStore(new FakePlacesStorage(), _time), new PlaceLauncher(_places, _shell), _shell, _time, CultureInfo.InvariantCulture);
        return new WorkspaceViewModel(new WorkspaceLayoutService(new WorkspaceStore(_layoutStorage, null, _time)), library);
    }

    [Fact]
    public void FirstRun_ShowsActivityAtlas_WithItsPanelsPlaced()
    {
        var workspace = NewWorkspace();

        Assert.Equal("Activity Atlas", workspace.ActiveLayoutName);
        Assert.Equal(BuiltInLayouts.ActivityAtlasId, workspace.SelectedLayout?.Id);
        Assert.Equal(new[] { "activity", "shelf", "sessions" }, workspace.Panels.Select(p => p.Type));
        Assert.Equal(new[] { (0, 0, 12), (1, 0, 8), (1, 8, 4) }, workspace.Panels.Select(p => (p.Row, p.Column, p.Span)));
        Assert.All(workspace.Panels, p => Assert.True(p.IsAvailable));
        Assert.Null(workspace.LayoutMessage);
    }

    [Fact]
    public void OnlyTheBuiltInsWithWorkingPanels_AreOffered()
        => Assert.Equal(new[] { "Activity Atlas" }, NewWorkspace().Layouts.Select(l => l.Name));

    [Fact]
    public void TheSearchBox_IsTheLibrarySearch()
    {
        var workspace = NewWorkspace();

        workspace.SearchText = "acme";

        Assert.Equal("acme", workspace.Library.SearchText);
        Assert.True(workspace.IsSearching);
    }

    [Fact]
    public void AQueryChange_WaitsForAFlush_ThenIsRememberedForNextTime()
    {
        var workspace = NewWorkspace();
        var pending = 0;
        workspace.QueryPending += () => pending++;

        workspace.Library.SearchText = "report";
        workspace.Library.SetDateRule(DateRule.ThisWeek());

        Assert.Equal(2, pending);
        Assert.True(workspace.HasUnsavedChanges);
        Assert.True(workspace.FlushPending().Saved);
        Assert.False(workspace.HasUnsavedChanges);

        var restarted = NewWorkspace();
        Assert.Equal("report", restarted.Library.SearchText);
        Assert.Equal(DateRuleKind.ThisWeek, restarted.Library.Date.Kind);
    }

    [Fact]
    public void AddPanel_OffersSavedPlaces_AndKeepsItAtFullWidth_AtOnce()
    {
        var workspace = NewWorkspace();
        var changed = 0;
        workspace.PanelsChanged += () => changed++;

        Assert.Equal(new[] { PanelTypes.Places }, workspace.AddablePanels.Select(p => p.Type));
        Assert.True(workspace.AddPanel(PanelTypes.Places));

        var places = workspace.Panels.Last();
        Assert.Equal((PanelTypes.Places, 2, 0, 12), (places.Type, places.Row, places.Column, places.Span));
        Assert.Equal(1, changed);
        Assert.Empty(workspace.AddablePanels);
        Assert.False(workspace.CanAddPanel);
        Assert.Equal("Added Saved places.", workspace.Status);

        Assert.Equal(new[] { "activity", "shelf", "sessions", "places" }, NewWorkspace().Panels.Select(p => p.Type));
    }

    [Fact]
    public void Hide_TakesThePanelAway_AndAddPanelPutsItBackWhereItWas()
    {
        var workspace = NewWorkspace();

        Assert.True(workspace.HidePanel("shelf"));
        Assert.Equal(new[] { "activity", "sessions" }, workspace.Panels.Select(p => p.Type));
        Assert.Equal(new[] { PanelTypes.Shelf, PanelTypes.Places }, workspace.AddablePanels.Select(p => p.Type));
        Assert.Equal(new[] { "activity", "sessions" }, NewWorkspace().Panels.Select(p => p.Type));

        Assert.True(workspace.AddPanel(PanelTypes.Shelf));
        Assert.Equal(new[] { "activity", "shelf", "sessions" }, workspace.Panels.Select(p => p.Type));
    }

    [Fact]
    public void HidingEveryPanel_LeavesAnEmptyCanvasThatCanStillAddPanels()
    {
        var workspace = NewWorkspace();

        foreach (var id in workspace.Panels.Select(p => p.Id).ToList())
            Assert.True(workspace.HidePanel(id));

        Assert.False(workspace.HasPanels);
        Assert.True(workspace.CanAddPanel);
    }

    [Fact]
    public void HidingAnUnknownPanel_ChangesNothing()
    {
        var workspace = NewWorkspace();

        Assert.False(workspace.HidePanel("nope"));
        Assert.Equal(3, workspace.Panels.Count);
        Assert.False(workspace.HasUnsavedChanges);
    }

    [Fact]
    public void AFailedWrite_IsShown_AndRetryClearsIt()
    {
        var workspace = NewWorkspace();
        _layoutStorage.FailEveryWrite = true;

        workspace.AddPanel(PanelTypes.Places);

        Assert.NotNull(workspace.LayoutMessage);
        Assert.True(workspace.CanRetry);
        Assert.True(workspace.HasUnsavedChanges);

        _layoutStorage.FailEveryWrite = false;
        workspace.RetrySave();

        Assert.Null(workspace.LayoutMessage);
        Assert.False(workspace.CanRetry);
    }

    [Fact]
    public void TheListedFileSet_IsTheShelfsDocuments()
    {
        Assert.True(_places.TryAdd("Jobs", PlaceType.Folder, TestPaths.Folder("Jobs"), out _, out _).Success);
        Assert.True(_sessions.TryCreate("Acme", new[] { "Acme" }, new[] { Pdf, Word }, out _, out _).Success);
        var workspace = NewWorkspace();

        var set = workspace.ListedFileSet();

        Assert.Equal(new[] { Pdf, Word }, set.Files.OrderBy(f => f, StringComparer.Ordinal));
        Assert.Equal(1, set.FoldersLeftOut);

        workspace.SearchText = "report";
        Assert.Equal(new[] { Word }, workspace.ListedFileSet().Files);
    }
}
