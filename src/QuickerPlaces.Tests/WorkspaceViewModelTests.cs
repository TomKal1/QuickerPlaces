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
/// shelf's documents as a session; Arrange mode, Undo and reflow (M4).
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
        return new WorkspaceViewModel(new WorkspaceLayoutService(new WorkspaceStore(_layoutStorage, null, _time)), library, _time,
            CultureInfo.InvariantCulture);
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
        => Assert.Equal(new[] { "Activity Atlas", "Files First" }, NewWorkspace().Layouts.Select(l => l.Name));

    [Fact]
    public void FilesFirst_PutsTheShelfFirst_WithTheCalendarBesideIt_AndStartsWithoutFilters()
    {
        var workspace = NewWorkspace();
        workspace.SearchText = "acme";

        workspace.SelectedLayout = workspace.Layouts.Single(l => l.Id == BuiltInLayouts.FilesFirstId);

        Assert.Equal("Files First", workspace.ActiveLayoutName);
        Assert.Equal(new[] { (PanelTypes.Shelf, 0, 0, 8), (PanelTypes.Activity, 0, 8, 4), (PanelTypes.Sessions, 1, 0, 12) },
            workspace.Panels.Select(p => (p.Type, p.Row, p.Column, p.Span)));
        // Filters never follow you from one layout to another (D3).
        Assert.Equal("", workspace.SearchText);
    }

    [Fact]
    public void FilesFirst_KeepsTheCalendarBesideTheShelf_AtTheSmallestWindow()
    {
        var workspace = NewWorkspace();
        workspace.SelectedLayout = workspace.Layouts.Single(l => l.Id == BuiltInLayouts.FilesFirstId);

        // About what the canvas gets in the main window at its least width, 960.
        workspace.Reflow(930);

        Assert.Equal(new[] { (0, 8), (0, 4), (1, 12) }, workspace.Panels.Select(p => (p.Row, p.Span)));
    }

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
        workspace.SearchText = "jobs";
        Assert.Null(workspace.Status);

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

    // ---------------------------------------------------------------
    // Arrange mode (M4)
    // ---------------------------------------------------------------

    private static string[] Types(WorkspaceViewModel workspace) => workspace.Panels.Select(p => p.Type).ToArray();

    [Fact]
    public void Hide_OffersUndo_WhichPutsThePanelBack()
    {
        var workspace = NewWorkspace();

        Assert.True(workspace.HidePanel("sessions"));
        Assert.True(workspace.CanUndo);
        Assert.Equal("Undo Hide Sessions", workspace.UndoText);

        workspace.Undo();

        Assert.Equal(new[] { "activity", "shelf", "sessions" }, Types(workspace));
        Assert.Equal("Undid Hide Sessions.", workspace.Status);
        Assert.False(workspace.CanUndo);
        Assert.Equal(new[] { "activity", "shelf", "sessions" }, Types(NewWorkspace()));
    }

    [Fact]
    public void UndoingAHide_KeepsTheSearchShown()
    {
        var workspace = NewWorkspace();
        workspace.HidePanel("sessions");
        workspace.SearchText = "acme";

        workspace.Undo();

        Assert.Equal("acme", workspace.SearchText);
        workspace.FlushPending();
        Assert.Equal("acme", NewWorkspace().SearchText);
    }

    [Fact]
    public void ArrangeMode_MovesAndResizes_InADraft_ThatDoneWritesOnce()
    {
        var workspace = NewWorkspace();
        var before = _layoutStorage.WriteCount;

        workspace.BeginArrange();
        Assert.True(workspace.IsArranging);
        Assert.True(workspace.MoveEarlier("sessions"));
        Assert.Equal("Moved Sessions before File shelf.", workspace.Status);
        Assert.True(workspace.SetSpan("sessions", PanelSpans.TwoThirds));
        Assert.Equal("Sessions is now two thirds wide.", workspace.Status);
        Assert.Equal(new[] { "activity", "sessions", "shelf" }, Types(workspace));
        Assert.True(workspace.IsModified);
        Assert.Equal(before, _layoutStorage.WriteCount);

        workspace.Done();

        Assert.False(workspace.IsArranging);
        Assert.Equal(before + 1, _layoutStorage.WriteCount);
        var reopened = NewWorkspace();
        Assert.Equal(new[] { "activity", "sessions", "shelf" }, Types(reopened));
        Assert.Equal(PanelSpans.TwoThirds, reopened.Panels[1].StoredSpan);
    }

    [Fact]
    public void Revert_PutsBackWhatArrangeModeStartedWith_AndWritesNothing()
    {
        var workspace = NewWorkspace();
        var before = _layoutStorage.WriteCount;
        workspace.BeginArrange();
        workspace.Drop("activity", "sessions", after: true);
        workspace.HidePanel("shelf");
        workspace.AddPanel(PanelTypes.Places);

        workspace.Revert();

        Assert.False(workspace.IsArranging);
        Assert.Equal(new[] { "activity", "shelf", "sessions" }, Types(workspace));
        Assert.Equal(before, _layoutStorage.WriteCount);
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void UndoInArrangeMode_StepsBackOneEditAtATime()
    {
        var workspace = NewWorkspace();
        workspace.BeginArrange();
        workspace.MoveLater("activity");
        workspace.SetSpan("shelf", PanelSpans.Half);

        Assert.Equal("Undo Resize File shelf", workspace.UndoText);
        workspace.Undo();
        Assert.Equal(PanelSpans.TwoThirds, workspace.Panels.First(p => p.Type == "shelf").StoredSpan);
        Assert.Equal("Undo Move Year activity", workspace.UndoText);
        workspace.Undo();
        Assert.Equal(new[] { "activity", "shelf", "sessions" }, Types(workspace));
        Assert.True(workspace.IsArranging);
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void ADropWhereThePanelAlreadyIs_ChangesNothing()
    {
        var workspace = NewWorkspace();
        workspace.BeginArrange();

        Assert.False(workspace.Drop("shelf", "activity", after: true));
        Assert.False(workspace.Drop("shelf", "shelf", after: false));

        Assert.False(workspace.CanUndo);
        Assert.False(workspace.IsModified);
    }

    [Fact]
    public void TheFirstAndLastPanels_CannotMoveFurther()
    {
        var workspace = NewWorkspace();
        workspace.BeginArrange();

        Assert.False(workspace.Panels[0].CanMoveEarlier);
        Assert.True(workspace.Panels[0].CanMoveLater);
        Assert.False(workspace.Panels[^1].CanMoveLater);
        Assert.False(workspace.MoveEarlier("activity"));
        Assert.False(workspace.MoveLater("sessions"));
    }

    [Fact]
    public void RestoreBuiltInLayout_PutsTheFactoryArrangementBack_WithUndo()
    {
        var workspace = NewWorkspace();
        workspace.HidePanel("sessions");
        Assert.True(workspace.IsModified);
        Assert.Equal("Restore built-in layout", workspace.RestoreLabel);

        workspace.RestoreSaved();

        Assert.False(workspace.IsModified);
        Assert.Equal(new[] { "activity", "shelf", "sessions" }, Types(workspace));
        Assert.Equal("Undo Restore built-in layout", workspace.UndoText);
        workspace.Undo();
        Assert.Equal(new[] { "activity", "shelf" }, Types(workspace));
    }

    [Fact]
    public void WidthsChangeOnlyInArrangeMode_AndPickingTheShownLayoutKeepsArranging()
    {
        var workspace = NewWorkspace();
        workspace.SetSpan("shelf", PanelSpans.Half);       // outside Arrange: refused
        Assert.False(workspace.IsModified);

        workspace.BeginArrange();
        workspace.HidePanel("sessions");
        workspace.SelectedLayout = workspace.Layouts[0];     // already shown: nothing happens
        Assert.True(workspace.IsArranging);

        workspace.Done();
        Assert.Equal(new[] { "activity", "shelf" }, Types(NewWorkspace()));
    }

    // ---------------------------------------------------------------
    // Reflow (M4)
    // ---------------------------------------------------------------

    [Fact]
    public void ANarrowCanvas_StacksThePanels_WithoutChangingOrWritingTheirWidths()
    {
        var workspace = NewWorkspace();
        var changed = 0;
        workspace.PanelsChanged += () => changed++;
        var before = _layoutStorage.WriteCount;

        workspace.Reflow(780);

        Assert.Equal(1, changed);
        Assert.Equal(new[] { (0, 12), (1, 12), (2, 12) }, workspace.Panels.Select(p => (p.Row, p.Span)));
        Assert.Equal(new[] { 12, 8, 4 }, workspace.Panels.Select(p => p.StoredSpan));
        Assert.Equal(before, _layoutStorage.WriteCount);

        workspace.Reflow(1400);
        Assert.Equal(new[] { (0, 12), (1, 8), (1, 4) }, workspace.Panels.Select(p => (p.Row, p.Span)));
        workspace.Reflow(1500);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void AWidthSetInANarrowWindow_SaysItIsShownWiderForNow()
    {
        var workspace = NewWorkspace();
        workspace.Reflow(780);
        workspace.BeginArrange();

        Assert.True(workspace.SetSpan("shelf", PanelSpans.Half));

        Assert.Equal("File shelf is now half wide; shown full width until the window is wider.", workspace.Status);
    }

    // ---------------------------------------------------------------
    // My layouts (M5)
    // ---------------------------------------------------------------

    private static LayoutEntry Entry(WorkspaceViewModel workspace, string name) => workspace.Layouts.Single(l => l.Name == name);

    private static void SaveAs(WorkspaceViewModel workspace, string name, bool includeFilters = false)
    {
        var dialog = workspace.NewSaveAs();
        dialog.Name = name;
        dialog.IncludeFilters = includeFilters;
        Assert.True(workspace.SaveAsNew(dialog), dialog.ErrorMessage);
    }

    [Fact]
    public void TwoPersonalLayouts_SwitchAndSurviveARestart_WithoutTouchingTheBuiltIns()
    {
        // The M5 exit check: two layouts of one's own, switched between, working
        // changes recovered, and a restart into the chosen one.
        var workspace = NewWorkspace();
        workspace.HidePanel("sessions");
        SaveAs(workspace, "Just files");
        workspace.SelectedLayout = Entry(workspace, "Activity Atlas");
        Assert.True(workspace.IsModified);   // the hide made before saving is still Atlas's working change
        workspace.RestoreSaved();
        workspace.BeginArrange();
        workspace.MoveEarlier("sessions");
        SaveAs(workspace, "Sessions first");

        Assert.Equal(new[] { "Activity Atlas", "Files First", "Just files", "Sessions first" }, workspace.Layouts.Select(l => l.Name));
        Assert.Equal(new[] { "Built-in", "Built-in", "My layouts", "My layouts" }, workspace.Layouts.Select(l => l.Group));
        Assert.Equal(new[] { "activity", "sessions", "shelf" }, Types(workspace));

        // A working change to a personal layout is recovered after switching away and back.
        workspace.SelectedLayout = Entry(workspace, "Just files");
        Assert.Equal(new[] { "activity", "shelf" }, Types(workspace));
        workspace.SetSpan("shelf", PanelSpans.Full);  // refused: not arranging
        workspace.BeginArrange();
        workspace.SetSpan("shelf", PanelSpans.Full);
        workspace.Done();
        workspace.SelectedLayout = Entry(workspace, "Sessions first");
        workspace.SelectedLayout = Entry(workspace, "Just files");
        Assert.Equal(PanelSpans.Full, workspace.Panels.Single(p => p.Type == "shelf").StoredSpan);
        Assert.Equal("Modified", Entry(workspace, "Just files").Notes);

        workspace.SetStartup(Entry(workspace, "Sessions first").Id);
        workspace.SelectedLayout = Entry(workspace, "Files First");

        var restarted = NewWorkspace();
        Assert.Equal("Sessions first", restarted.ActiveLayoutName);
        Assert.Equal(new[] { "activity", "sessions", "shelf" }, Types(restarted));
        Assert.Equal("Starts here", Entry(restarted, "Sessions first").Notes);
        Assert.Equal(new[] { "activity", "shelf", "sessions" },
            BuiltInLayouts.ActivityAtlas.CreatePanels().Select(p => p.Type));
    }

    [Fact]
    public void SaveAsNew_SuggestsAName_AndRefusesOneInUse_InTheDialog()
    {
        var workspace = NewWorkspace();
        var dialog = workspace.NewSaveAs();
        Assert.Equal("My Activity Atlas", dialog.Name);
        Assert.True(workspace.SaveAsNew(dialog));

        var again = workspace.NewSaveAs();
        Assert.Equal("My Activity Atlas copy", again.Name);
        again.Name = "my activity atlas";
        Assert.False(workspace.SaveAsNew(again));
        Assert.NotNull(again.ErrorMessage);
        again.Name = "Another";
        Assert.Null(again.ErrorMessage);
    }

    [Fact]
    public void SaveChangesAndRename_AreForOnesOwnLayouts()
    {
        var workspace = NewWorkspace();
        Assert.False(workspace.IsUserLayout);
        workspace.SaveChanges();
        Assert.Equal("Built-in layouts can't be changed. Save it as a new layout instead.", workspace.Status);

        SaveAs(workspace, "Mine");
        Assert.True(workspace.IsUserLayout);
        workspace.HidePanel("sessions");
        Assert.True(workspace.IsModified);
        workspace.SaveChanges();
        Assert.False(workspace.IsModified);
        Assert.Equal("Saved changes to “Mine”.", workspace.Status);

        var rename = workspace.NewRename();
        Assert.Equal("Mine", rename.Name);
        Assert.False(rename.CanIncludeFilters);
        rename.Name = "  Drawings  ";
        Assert.True(workspace.Rename(rename));
        Assert.Equal("Drawings", workspace.ActiveLayoutName);
        Assert.Equal("Drawings", NewWorkspace().ActiveLayoutName);
    }

    [Fact]
    public void FiltersAreSavedOnlyWhenTicked_AndThenComeBackWithTheLayout()
    {
        var workspace = NewWorkspace();
        workspace.SearchText = "acme";
        SaveAs(workspace, "Plain");
        SaveAs(workspace, "Acme", includeFilters: true);

        workspace.SelectedLayout = Entry(workspace, "Plain");
        Assert.Equal("", workspace.SearchText);
        workspace.SelectedLayout = Entry(workspace, "Acme");
        Assert.Equal("acme", workspace.SearchText);
        Assert.Equal("Filters", Entry(workspace, "Acme").Notes);

        workspace.SearchText = "tower";
        workspace.SaveChanges();
        Assert.Equal("Saved changes to “Acme”, with the filters shown now.", workspace.Status);
        workspace.SelectedLayout = Entry(workspace, "Plain");
        workspace.SelectedLayout = Entry(workspace, "Acme");
        Assert.Equal("tower", workspace.SearchText);
    }

    [Fact]
    public void ThisWeekPickedOnTheCalendar_CanBeSavedToStayThisWeek()
    {
        var workspace = NewWorkspace();
        workspace.Library.SelectionUnit = CalendarSelectionUnit.Week;
        workspace.Library.SelectCalendarDate(new DateOnly(2026, 9, 23));

        var dialog = workspace.NewSaveAs();
        dialog.Name = "This week";
        dialog.IncludeFilters = true;
        Assert.True(dialog.ShowsDateChoice);
        Assert.Equal("This week, whichever week it is", dialog.RelativeDatesText);
        Assert.Equal("Always 20–26 Sep 2026", dialog.FixedDatesText);
        Assert.True(dialog.KeepRelative);
        Assert.True(workspace.SaveAsNew(dialog));

        // A week later the layout shows that week, not the one it was saved in.
        _time.UtcNow = _time.UtcNow.AddDays(7);
        var later = NewWorkspace();
        Assert.Equal((new DateOnly(2026, 9, 27), new DateOnly(2026, 10, 3)), later.Library.Period);
    }

    [Fact]
    public void Duplicate_MakesACopyInMyLayouts_AndKeepsTheLayoutShown()
    {
        var workspace = NewWorkspace();

        workspace.Duplicate();
        workspace.Duplicate();

        Assert.Equal("Activity Atlas", workspace.ActiveLayoutName);
        Assert.Equal(new[] { "Activity Atlas copy", "Activity Atlas copy 2" },
            workspace.Layouts.Where(l => !l.IsBuiltIn).Select(l => l.Name));
        Assert.Equal("Made “Activity Atlas copy 2” in My layouts.", workspace.Status);
    }

    [Fact]
    public void DeletingTheShownLayout_ShowsActivityAtlas_AndUndoBringsItBackWithItsFilters()
    {
        var workspace = NewWorkspace();
        workspace.SearchText = "acme";
        SaveAs(workspace, "Acme", includeFilters: true);
        workspace.SetStartup(Entry(workspace, "Acme").Id);

        workspace.Delete();

        Assert.Equal("Activity Atlas", workspace.ActiveLayoutName);
        Assert.Equal("", workspace.SearchText);
        Assert.Equal("Deleted “Acme”. Undo brings it back.", workspace.Status);
        Assert.True(workspace.StartupOptions[0].IsChosen);
        Assert.Equal("Activity Atlas", NewWorkspace().ActiveLayoutName);

        workspace.Undo();

        Assert.Equal("Acme", workspace.ActiveLayoutName);
        Assert.Equal("acme", workspace.SearchText);
        Assert.Equal("Acme", NewWorkspace().ActiveLayoutName);
    }

    [Fact]
    public void BuiltInsCannotBeRenamedOrDeleted()
    {
        var workspace = NewWorkspace();

        workspace.Delete();

        Assert.Equal("Built-in layouts can't be deleted.", workspace.Status);
        Assert.Equal(2, workspace.Layouts.Count);
    }

    [Fact]
    public void TheStartupChoice_ListsTheLastLayoutThenEveryLayout()
    {
        var workspace = NewWorkspace();

        Assert.Equal(new[] { "The layout I used last", "Activity Atlas", "Files First" }, workspace.StartupOptions.Select(o => o.Name));
        Assert.True(workspace.StartupOptions[0].IsChosen);

        workspace.SetStartup(BuiltInLayouts.FilesFirstId);
        Assert.Equal("QuickerPlaces will start with “Files First”.", workspace.Status);
        Assert.True(workspace.StartupOptions.Single(o => o.PresetId == BuiltInLayouts.FilesFirstId).IsChosen);
        Assert.Equal("Files First", NewWorkspace().ActiveLayoutName);

        workspace.SetStartup(null);
        Assert.Equal("Activity Atlas", NewWorkspace().ActiveLayoutName);
    }

    [Fact]
    public void AFailedSave_KeepsTheLayoutInMemory_WithRetry()
    {
        var workspace = NewWorkspace();
        _layoutStorage.FailEveryWrite = true;

        SaveAs(workspace, "Mine");

        Assert.Equal("Mine", workspace.ActiveLayoutName);
        Assert.True(workspace.CanRetry);
        _layoutStorage.FailEveryWrite = false;
        workspace.RetrySave();
        Assert.Null(workspace.LayoutMessage);
        Assert.Equal("Mine", NewWorkspace().ActiveLayoutName);
    }
}
