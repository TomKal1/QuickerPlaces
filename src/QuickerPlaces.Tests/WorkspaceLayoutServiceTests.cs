using System;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Models.Workspace;
using QuickerPlaces.Services.Workspace;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The layout service (configurable canvas plan D1–D3, M1): built-ins that
/// can't be changed, working arrangements kept per layout, Arrange mode's
/// draft with Done and Revert, every preset operation, startup, Undo, and
/// filters that never leak between layouts — all without WPF.
/// </summary>
public sealed class WorkspaceLayoutServiceTests
{
    private static FakePlacesStorage NewStorage() => new() { StoreFilePath = @"C:\fake\workspace-layouts.json" };

    private static WorkspaceLayoutService NewService(FakePlacesStorage storage)
        => new(new WorkspaceStore(storage, null, new ManualTimeProvider()));

    private static string[] Order(WorkspaceLayoutService service) => service.VisiblePanels.Select(p => p.Id).ToArray();

    private static string SaveAs(WorkspaceLayoutService service, string name, bool includeFilters = false)
    {
        var result = service.SaveAsNew(name, includeFilters, out var id, out var persistence);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(persistence.Saved);
        return id!;
    }

    private static void Ok(PersistenceResult persistence) => Assert.True(persistence.Saved, persistence.UserMessage);

    // ---------------------------------------------------------------
    // First run and built-ins
    // ---------------------------------------------------------------

    [Fact]
    public void FirstRun_ShowsActivityAtlas_AndWritesNothing()
    {
        var storage = NewStorage();
        var service = NewService(storage);

        Assert.Equal(BuiltInLayouts.ActivityAtlasId, service.ActivePresetId);
        Assert.True(service.ActiveIsBuiltIn);
        Assert.Equal("Activity Atlas", service.ActiveName);
        Assert.Equal(new[] { PanelTypes.Activity, PanelTypes.Shelf, PanelTypes.Sessions }, Order(service));
        Assert.Equal(new[] { 12, 8, 4 }, service.VisiblePanels.Select(p => p.Span));
        Assert.False(service.IsModified);
        Assert.True(service.Query.IsDefault);
        Assert.Equal(0, storage.WriteCount);
    }

    [Fact]
    public void OnlyBuiltInsWhosePanelsExist_AreOffered()
    {
        var service = NewService(NewStorage());

        // Collections and Saved searches come in M6; until then Activity Atlas and Files First are the ones with every panel they need.
        Assert.Equal(new[] { BuiltInLayouts.ActivityAtlasId, BuiltInLayouts.FilesFirstId }, service.BuiltInEntries.Select(e => e.Id));
        Assert.False(BuiltInLayouts.ProjectCanvas.IsOffered);
        Assert.False(BuiltInLayouts.PersonalDesk.IsOffered);
        Assert.False(service.Activate(BuiltInLayouts.PersonalDeskId).Saved);
        Assert.Equal(BuiltInLayouts.ActivityAtlasId, service.ActivePresetId);
    }

    [Fact]
    public void BuiltInFactoryDefinitions_MatchThePlan()
    {
        static (string, int)[] Panels(BuiltInLayout layout) => layout.CreatePanels().Select(p => (p.Type, p.Span)).ToArray();

        Assert.Equal(new[] { ("activity", 12), ("shelf", 8), ("sessions", 4) }, Panels(BuiltInLayouts.ActivityAtlas));
        Assert.Equal(new[] { ("shelf", 8), ("activity", 4), ("sessions", 12) }, Panels(BuiltInLayouts.FilesFirst));
        Assert.Equal(new[] { ("collections", 12), ("shelf", 8), ("activity", 4) }, Panels(BuiltInLayouts.ProjectCanvas));
        Assert.Equal(new[] { ("shelf", 8), ("searches", 4), ("activity", 8), ("sessions", 4) }, Panels(BuiltInLayouts.PersonalDesk));
    }

    [Fact]
    public void BuiltIns_CannotBeSavedOver_RenamedOrDeleted()
    {
        var service = NewService(NewStorage());

        Assert.False(service.SaveChanges(out _).Success);
        Assert.False(service.Rename(BuiltInLayouts.ActivityAtlasId, "Mine", out _).Success);
        Assert.False(service.Delete(BuiltInLayouts.ActivityAtlasId, out _).Success);
        Assert.Equal("Activity Atlas", service.BuiltInEntries[0].Name);
    }

    // ---------------------------------------------------------------
    // Arrange mode
    // ---------------------------------------------------------------

    [Fact]
    public void EditsOutsideArrangeMode_AreRefused()
    {
        var service = NewService(NewStorage());

        Assert.False(service.MoveEarlier(PanelTypes.Sessions));
        Assert.False(service.SetSpan(PanelTypes.Shelf, 6));
        Assert.False(service.Hide(PanelTypes.Shelf));
        Assert.False(service.IsModified);
    }

    [Fact]
    public void ArrangeEdits_ChangeOnlyTheDraft_AndDoneWritesOnce()
    {
        var storage = NewStorage();
        var service = NewService(storage);

        service.BeginArrange();
        Assert.True(service.MoveBefore(PanelTypes.Sessions, PanelTypes.Activity));
        Assert.True(service.SetSpan(PanelTypes.Shelf, 6));
        Assert.True(service.Hide(PanelTypes.Activity));
        Assert.Equal(0, storage.WriteCount);
        Assert.True(service.IsModified);

        Ok(service.Done());

        Assert.Equal(1, storage.WriteCount);
        Assert.False(service.IsArranging);
        Assert.Equal(new[] { PanelTypes.Sessions, PanelTypes.Shelf }, Order(service));
        Assert.Equal(6, service.Panels.Single(p => p.Id == PanelTypes.Shelf).Span);
        Assert.True(service.IsModified);

        // The factory definition is untouched: a fresh Atlas elsewhere still has it.
        Assert.Equal(8, BuiltInLayouts.ActivityAtlas.CreatePanels().Single(p => p.Type == PanelTypes.Shelf).Span);
    }

    [Fact]
    public void Revert_RestoresTheArrangementArrangeBeganWith_WithoutWriting()
    {
        var storage = NewStorage();
        var service = NewService(storage);
        service.BeginArrange();
        service.SetSpan(PanelTypes.Shelf, 12);
        Ok(service.Done());
        var writes = storage.WriteCount;

        service.BeginArrange();
        service.Hide(PanelTypes.Shelf);
        service.MoveEarlier(PanelTypes.Sessions);
        service.Revert();

        Assert.Equal(writes, storage.WriteCount);
        Assert.Equal(new[] { PanelTypes.Activity, PanelTypes.Shelf, PanelTypes.Sessions }, Order(service));
        Assert.Equal(12, service.Panels.Single(p => p.Id == PanelTypes.Shelf).Span);
    }

    [Fact]
    public void KeyboardMoves_SkipHiddenPanels_AndStopAtTheEnds()
    {
        var service = NewService(NewStorage());
        service.BeginArrange();
        service.Hide(PanelTypes.Shelf);

        Assert.False(service.MoveEarlier(PanelTypes.Activity));
        Assert.False(service.MoveLater(PanelTypes.Sessions));

        Assert.True(service.MoveEarlier(PanelTypes.Sessions));
        Assert.Equal(new[] { PanelTypes.Sessions, PanelTypes.Activity }, Order(service));

        Assert.True(service.MoveLater(PanelTypes.Sessions));
        Assert.Equal(new[] { PanelTypes.Activity, PanelTypes.Sessions }, Order(service));
    }

    [Fact]
    public void SpansOtherThanTheAllowedOnes_AreRefused()
    {
        var service = NewService(NewStorage());
        service.BeginArrange();

        Assert.False(service.SetSpan(PanelTypes.Shelf, 5));
        Assert.False(service.SetSpan(PanelTypes.Shelf, 0));
        Assert.False(service.SetSpan(PanelTypes.Shelf, 8)); // unchanged
        Assert.False(service.SetSpan("nope", 6));
        Assert.True(service.SetSpan(PanelTypes.Shelf, 4));
    }

    [Fact]
    public void AddPanel_ShowsAHiddenPanelWhereItWas_OrAddsANewOneAtTheEnd()
    {
        var service = NewService(NewStorage());
        service.BeginArrange();

        Assert.Equal(new[] { PanelTypes.Places }, service.AddablePanelTypes);
        Assert.False(service.AddPanel(PanelTypes.Shelf)); // already shown
        Assert.False(service.AddPanel(PanelTypes.Collections)); // not built yet

        service.Hide(PanelTypes.Shelf);
        Assert.Contains(PanelTypes.Shelf, service.AddablePanelTypes);
        Assert.True(service.AddPanel(PanelTypes.Shelf));
        Assert.Equal(new[] { PanelTypes.Activity, PanelTypes.Shelf, PanelTypes.Sessions }, Order(service));
        Assert.Equal(8, service.Panels.Single(p => p.Id == PanelTypes.Shelf).Span);

        Assert.True(service.AddPanel(PanelTypes.Places));
        var added = service.VisiblePanels.Last();
        Assert.Equal(PanelTypes.Places, added.Type);
        Assert.Equal(PanelTypes.DefaultSpan(PanelTypes.Places), added.Span);
    }

    [Fact]
    public void UndoInArrangeMode_StepsBackThroughTheDraft()
    {
        var storage = NewStorage();
        var service = NewService(storage);
        service.BeginArrange();
        service.SetSpan(PanelTypes.Shelf, 6);
        service.Hide(PanelTypes.Sessions);

        Assert.Equal("Hide Sessions", service.UndoLabel);
        Ok(service.Undo());
        Assert.Contains(PanelTypes.Sessions, Order(service));
        Assert.Equal("Resize File shelf", service.UndoLabel);
        Ok(service.Undo());
        Assert.False(service.IsModified);
        Assert.False(service.CanUndo);
        Assert.Equal(0, storage.WriteCount);
    }

    [Fact]
    public void HideOutsideArrangeMode_IsWrittenAtOnce_AndUndoBringsItBack()
    {
        var storage = NewStorage();
        var service = NewService(storage);

        Assert.True(service.HideNow(PanelTypes.Sessions, out var persistence));
        Ok(persistence);
        Assert.False(service.IsArranging);
        Assert.Equal(1, storage.WriteCount);
        Assert.DoesNotContain(PanelTypes.Sessions, Order(NewService(storage)));
        Assert.Equal("Hide Sessions", service.UndoLabel);

        Ok(service.Undo());
        Assert.Contains(PanelTypes.Sessions, Order(service));
        Assert.False(service.IsModified);
        Assert.False(service.CanUndo);
        Assert.Contains(PanelTypes.Sessions, Order(NewService(storage)));
    }

    [Fact]
    public void AddPanelOutsideArrangeMode_IsWrittenAtOnce_WithUndo()
    {
        var storage = NewStorage();
        var service = NewService(storage);

        Assert.True(service.AddPanelNow(PanelTypes.Places, out var persistence));
        Ok(persistence);
        Assert.Contains(PanelTypes.Places, Order(NewService(storage)));
        Assert.Equal("Add Saved places", service.UndoLabel);

        Ok(service.Undo());
        Assert.DoesNotContain(PanelTypes.Places, Order(service));
    }

    [Fact]
    public void AStepThatChangesNothing_WritesNothing_AndOffersNoUndo()
    {
        var storage = NewStorage();
        var service = NewService(storage);

        Assert.False(service.HideNow("nope", out _));
        Assert.False(service.AddPanelNow(PanelTypes.Shelf, out _));

        Assert.False(service.CanUndo);
        Assert.False(service.IsArranging);
        Assert.Equal(0, storage.WriteCount);
    }

    [Fact]
    public void HideInArrangeMode_IsADraftEdit()
    {
        var storage = NewStorage();
        var service = NewService(storage);
        service.BeginArrange();

        Assert.True(service.HideNow(PanelTypes.Sessions, out _));

        Assert.True(service.IsArranging);
        Assert.Equal(0, storage.WriteCount);
        service.Revert();
        Assert.Contains(PanelTypes.Sessions, Order(service));
    }

    [Fact]
    public void ArrangeMode_EndsAnUndoOfferFromBeforeIt()
    {
        var service = NewService(NewStorage());
        Assert.True(service.HideNow(PanelTypes.Sessions, out _));

        service.BeginArrange();

        // Undo in Arrange mode steps back through the draft only.
        Assert.False(service.CanUndo);
    }

    [Fact]
    public void AnEmptyCanvas_StillOffersAddPanel()
    {
        var service = NewService(NewStorage());
        service.BeginArrange();
        foreach (var panel in service.VisiblePanels)
            service.Hide(panel.Id);

        Assert.Empty(service.VisiblePanels);
        Assert.Equal(new[] { PanelTypes.Activity, PanelTypes.Shelf, PanelTypes.Sessions, PanelTypes.Places }, service.AddablePanelTypes);
    }

    // ---------------------------------------------------------------
    // Restoring definitions
    // ---------------------------------------------------------------

    [Fact]
    public void RestoreBuiltInLayout_ClearsOnlyItsArrangementChanges_WithUndo()
    {
        var storage = NewStorage();
        var service = NewService(storage);
        service.BeginArrange();
        service.Hide(PanelTypes.Sessions);
        Ok(service.Done());
        service.SetQuery(new WorkspaceQuery { Text = "floor plan" });

        Ok(service.RestoreSaved());

        Assert.False(service.IsModified);
        Assert.Equal("floor plan", service.Query.Text);
        Assert.Equal("Restore built-in layout", service.UndoLabel);

        Ok(service.Undo());
        Assert.True(service.IsModified);
        Assert.DoesNotContain(PanelTypes.Sessions, Order(service));
        Assert.DoesNotContain(PanelTypes.Sessions, Order(NewService(storage)));
    }

    // ---------------------------------------------------------------
    // User layouts
    // ---------------------------------------------------------------

    [Fact]
    public void SaveAsNew_FromAnArrangeDraft_GivesTheDraftToTheNewLayout_AndLeavesTheSourceAsItWas()
    {
        var storage = NewStorage();
        var service = NewService(storage);
        service.BeginArrange();
        service.Hide(PanelTypes.Sessions);

        var id = SaveAs(service, "  Reading desk  ");

        Assert.False(service.IsArranging);
        Assert.Equal(id, service.ActivePresetId);
        Assert.Equal("Reading desk", service.ActiveName);
        Assert.False(service.IsModified);
        Assert.Equal(new[] { PanelTypes.Activity, PanelTypes.Shelf }, Order(service));

        Ok(service.Activate(BuiltInLayouts.ActivityAtlasId));
        Assert.False(service.IsModified);
        Assert.Contains(PanelTypes.Sessions, Order(service));
    }

    [Fact]
    public void Names_AreTrimmed_Required_Limited_AndUniqueIgnoringCase()
    {
        var service = NewService(NewStorage());
        SaveAs(service, "Reading desk");

        Assert.False(service.SaveAsNew("   ", false, out _, out _).Success);
        Assert.False(service.SaveAsNew(new string('x', 81), false, out _, out _).Success);
        Assert.True(service.SaveAsNew(new string('x', 80), false, out _, out _).Success);
        var duplicate = service.SaveAsNew(" READING DESK ", false, out _, out _);
        Assert.False(duplicate.Success);
        Assert.Contains("already", duplicate.ErrorMessage);

        // A built-in's name is fine: the picker lists the groups apart.
        Assert.True(service.SaveAsNew("Activity Atlas", false, out _, out _).Success);
    }

    [Fact]
    public void TwoPersonalLayouts_SwitchAndRestart_KeepingTheirOwnArrangements()
    {
        var storage = NewStorage();
        var service = NewService(storage);
        service.BeginArrange();
        service.Hide(PanelTypes.Sessions);
        var reading = SaveAs(service, "Reading");

        Ok(service.Activate(BuiltInLayouts.ActivityAtlasId));
        service.BeginArrange();
        service.MoveBefore(PanelTypes.Sessions, null);
        service.SetSpan(PanelTypes.Shelf, 12);
        var wide = SaveAs(service, "Wide shelf");

        Ok(service.Activate(reading));
        Assert.Equal(new[] { PanelTypes.Activity, PanelTypes.Shelf }, Order(service));

        var restarted = NewService(storage);
        Assert.Equal(reading, restarted.ActivePresetId);
        Assert.Equal(new[] { "Reading", "Wide shelf" }, restarted.UserEntries.Select(e => e.Name));
        Ok(restarted.Activate(wide));
        Assert.Equal(12, restarted.Panels.Single(p => p.Id == PanelTypes.Shelf).Span);
        Ok(restarted.Activate(BuiltInLayouts.ActivityAtlasId));
        Assert.False(restarted.IsModified);
    }

    [Fact]
    public void WorkingChanges_ToAUserLayout_ShowModified_UntilSaveChanges()
    {
        var storage = NewStorage();
        var service = NewService(storage);
        var id = SaveAs(service, "Mine");

        service.BeginArrange();
        service.SetSpan(PanelTypes.Shelf, 6);
        Ok(service.Done());
        Assert.True(service.IsModified);
        Assert.True(service.UserEntries.Single().IsModified);

        // Switching away and back returns to the working arrangement, still modified.
        Ok(service.Activate(BuiltInLayouts.ActivityAtlasId));
        Ok(service.Activate(id));
        Assert.True(service.IsModified);

        Assert.True(service.SaveChanges(out var persistence).Success);
        Ok(persistence);
        Assert.False(service.IsModified);
        Assert.Equal(6, NewService(storage).Panels.Single(p => p.Id == PanelTypes.Shelf).Span);
    }

    [Fact]
    public void RestoreSavedLayout_ReturnsAUserLayoutToItsDefinition()
    {
        var service = NewService(NewStorage());
        SaveAs(service, "Mine");
        service.BeginArrange();
        service.Hide(PanelTypes.Shelf);
        Ok(service.Done());

        Ok(service.RestoreSaved());

        Assert.False(service.IsModified);
        Assert.Equal("Restore saved layout", service.UndoLabel);
        Assert.Contains(PanelTypes.Shelf, Order(service));
    }

    [Fact]
    public void Rename_KeepsTheId_AndEveryReference()
    {
        var storage = NewStorage();
        var service = NewService(storage);
        var id = SaveAs(service, "Mine");
        Assert.True(service.SetStartup(StartupChoice.ForPreset(id), out _).Success);

        Assert.True(service.Rename(id, "Drawings", out var persistence).Success);
        Ok(persistence);
        Assert.False(service.Rename(id, "", out _).Success);

        var restarted = NewService(storage);
        Assert.Equal(id, restarted.ActivePresetId);
        Assert.Equal("Drawings", restarted.ActiveName);
        Assert.True(restarted.UserEntries.Single().IsStartup);
    }

    [Fact]
    public void Duplicate_CopiesTheSavedDefinition_UnderAFreeName()
    {
        var service = NewService(NewStorage());
        Assert.True(service.Duplicate(BuiltInLayouts.ActivityAtlasId, out var first, out _).Success);
        Assert.True(service.Duplicate(BuiltInLayouts.ActivityAtlasId, out var second, out _).Success);

        Assert.Equal(new[] { "Activity Atlas copy", "Activity Atlas copy 2" }, service.UserEntries.Select(e => e.Name));
        Assert.Equal(BuiltInLayouts.ActivityAtlasId, service.ActivePresetId);

        Ok(service.Activate(second!));
        Assert.False(service.IsModified);
        Assert.Equal(new[] { PanelTypes.Activity, PanelTypes.Shelf, PanelTypes.Sessions }, Order(service));

        // Copies are independent of each other.
        service.BeginArrange();
        service.Hide(PanelTypes.Activity);
        Assert.True(service.SaveChanges(out _).Success);
        Ok(service.Activate(first!));
        Assert.Contains(PanelTypes.Activity, Order(service));
    }

    [Fact]
    public void DeletingTheShownAndStartupLayout_FallsBackInOneWrite_AndUndoBringsItBack()
    {
        var storage = NewStorage();
        var service = NewService(storage);
        service.BeginArrange();
        service.Hide(PanelTypes.Sessions);
        var id = SaveAs(service, "Mine", includeFilters: false);
        Assert.True(service.SetStartup(StartupChoice.ForPreset(id), out _).Success);
        var writes = storage.WriteCount;

        Assert.True(service.Delete(id, out var persistence).Success);
        Ok(persistence);

        Assert.Equal(writes + 1, storage.WriteCount);
        Assert.Equal(BuiltInLayouts.ActivityAtlasId, service.ActivePresetId);
        Assert.True(service.Startup.ResumesLast);
        Assert.Empty(service.UserEntries);
        Assert.Equal("Delete \"Mine\"", service.UndoLabel);

        Ok(service.Undo());
        Assert.Equal(id, service.ActivePresetId);
        Assert.Equal(id, service.Startup.PresetId);
        Assert.DoesNotContain(PanelTypes.Sessions, Order(service));
        Assert.Equal(id, NewService(storage).ActivePresetId);
    }

    [Fact]
    public void AnotherLastingChange_EndsTheUndoOffer()
    {
        var service = NewService(NewStorage());
        var id = SaveAs(service, "Mine");
        Assert.True(service.Delete(id, out _).Success);
        Assert.True(service.CanUndo);

        Assert.True(service.Duplicate(BuiltInLayouts.ActivityAtlasId, out _, out _).Success);

        Assert.False(service.CanUndo);
    }

    // ---------------------------------------------------------------
    // Startup
    // ---------------------------------------------------------------

    [Fact]
    public void Startup_ResumesTheLastLayout_ByDefault()
    {
        var storage = NewStorage();
        var service = NewService(storage);
        var id = SaveAs(service, "Mine");
        Ok(service.Activate(BuiltInLayouts.ActivityAtlasId));
        Ok(service.Activate(id));

        Assert.Equal(id, NewService(storage).ActivePresetId);
    }

    [Fact]
    public void Startup_WithAChosenLayout_StartsThere_WithItsWorkingArrangement()
    {
        var storage = NewStorage();
        var service = NewService(storage);
        var id = SaveAs(service, "Mine");
        service.BeginArrange();
        service.SetSpan(PanelTypes.Shelf, 12);
        Ok(service.Done());
        Assert.True(service.SetStartup(StartupChoice.ForPreset(id), out _).Success);
        Ok(service.Activate(BuiltInLayouts.ActivityAtlasId));

        var restarted = NewService(storage);

        Assert.Equal(id, restarted.ActivePresetId);
        Assert.True(restarted.IsModified);
        Assert.Equal(12, restarted.Panels.Single(p => p.Id == PanelTypes.Shelf).Span);
    }

    [Fact]
    public void Startup_RefusesALayoutThatDoesNotExist()
    {
        var service = NewService(NewStorage());

        Assert.False(service.SetStartup(StartupChoice.ForPreset("missing"), out _).Success);
        Assert.False(service.SetStartup(StartupChoice.ForPreset(BuiltInLayouts.ProjectCanvasId), out _).Success);
        Assert.True(service.Startup.ResumesLast);
    }

    // ---------------------------------------------------------------
    // Filters and the query (D3, D4)
    // ---------------------------------------------------------------

    [Fact]
    public void LayoutsSavedWithoutFilters_ClearTheQuery_WhenShown()
    {
        var service = NewService(NewStorage());
        var id = SaveAs(service, "Plain");
        Ok(service.Activate(BuiltInLayouts.ActivityAtlasId));
        service.SetQuery(new WorkspaceQuery { Text = "invoice", Kind = "pdf" });

        Ok(service.Activate(id));

        Assert.True(service.Query.IsDefault);
    }

    [Fact]
    public void LayoutsSavedWithFilters_ApplyThem_WhenShown()
    {
        var service = NewService(NewStorage());
        service.SetQuery(new WorkspaceQuery { Text = "Tower B", Tag = "markups", Date = DateRule.ThisWeek() });
        var id = SaveAs(service, "Tower B this week", includeFilters: true);
        Ok(service.Activate(BuiltInLayouts.ActivityAtlasId));
        Assert.True(service.Query.IsDefault);

        Ok(service.Activate(id));

        Assert.Equal("Tower B", service.Query.Text);
        Assert.Equal("markups", service.Query.Tag);
        Assert.Equal(DateRuleKind.ThisWeek, service.Query.Date.Kind);
        Assert.True(service.UserEntries.Single().HasFilters);
    }

    [Fact]
    public void QueryChanges_WaitForAFlush_AndAreRememberedForStartupOnly()
    {
        var storage = NewStorage();
        var service = NewService(storage);
        service.SetQuery(new WorkspaceQuery { Text = "spec" });

        Assert.Equal(0, storage.WriteCount);
        Assert.True(service.HasUnsavedChanges);
        Ok(service.FlushPending());
        Assert.Equal(1, storage.WriteCount);
        Assert.False(service.HasUnsavedChanges);
        Ok(service.FlushPending());
        Assert.Equal(1, storage.WriteCount);

        // Resuming at startup brings the query back with its layout.
        Assert.Equal("spec", NewService(storage).Query.Text);
    }

    [Fact]
    public void ARememberedQuery_IsNotCarriedWhenSwitchingBack()
    {
        var service = NewService(NewStorage());
        var id = SaveAs(service, "Mine");
        Ok(service.Activate(BuiltInLayouts.ActivityAtlasId));
        service.SetQuery(new WorkspaceQuery { Text = "spec" });

        Ok(service.Activate(id));
        Ok(service.Activate(BuiltInLayouts.ActivityAtlasId));

        Assert.True(service.Query.IsDefault);
    }

    [Fact]
    public void SaveChanges_RefreshesSavedFilters_OnlyForALayoutThatHasThem()
    {
        var service = NewService(NewStorage());
        var plain = SaveAs(service, "Plain");
        service.SetQuery(new WorkspaceQuery { Text = "old" });
        var filtered = SaveAs(service, "Filtered", includeFilters: true);

        service.SetQuery(new WorkspaceQuery { Text = "new" });
        Assert.True(service.SaveChanges(out _).Success);
        Ok(service.Activate(plain));
        service.SetQuery(new WorkspaceQuery { Text = "ignored" });
        Assert.True(service.SaveChanges(out _).Success);

        Ok(service.Activate(filtered));
        Assert.Equal("new", service.Query.Text);
        Ok(service.Activate(plain));
        Assert.True(service.Query.IsDefault);
    }

    // ---------------------------------------------------------------
    // Persistence failures
    // ---------------------------------------------------------------

    [Fact]
    public void AFailedSave_IsReported_KeptInMemory_AndRetried()
    {
        var storage = NewStorage();
        var service = NewService(storage);
        storage.FailNextWrite = true;

        var result = service.SaveAsNew("Mine", false, out var id, out var persistence);

        Assert.True(result.Success);
        Assert.False(persistence.Saved);
        Assert.NotNull(persistence.UserMessage);
        Assert.True(service.HasUnsavedChanges);
        Assert.Equal(id, service.ActivePresetId);

        Ok(service.RetrySave());
        Assert.False(service.HasUnsavedChanges);
        Assert.Equal("Mine", NewService(storage).ActiveName);
    }

    [Fact]
    public void AReadOnlyStore_StillShowsBuiltIns_ButSavesNothing()
    {
        var storage = NewStorage();
        storage.ContentsToReturn = "{ \"schemaVersion\": 99, \"presets\": [] }";
        var service = NewService(storage);

        Assert.False(service.CanWrite);
        Assert.NotNull(service.Notice);
        Assert.Equal(BuiltInLayouts.ActivityAtlasId, service.ActivePresetId);

        service.BeginArrange();
        service.Hide(PanelTypes.Sessions);
        var persistence = service.Done();

        Assert.False(persistence.Saved);
        Assert.Equal(0, storage.WriteCount);
        Assert.Contains("99", storage.ContentsToReturn);
        Assert.DoesNotContain(PanelTypes.Sessions, Order(service));
    }

    // ---------------------------------------------------------------
    // Columns layouts (Desk layout design §2, §3)
    // ---------------------------------------------------------------

    private const string ColumnsFile = """
        {
          "schemaVersion": 1,
          "presets": [
            { "id": "cols", "name": "Cols", "arrangement": "columns",
              "panels": [ { "id": "sessions", "type": "sessions", "span": 4, "dock": "left" },
                          { "id": "places", "type": "places", "span": 12, "dock": "main" },
                          { "id": "shelf", "type": "shelf", "span": 8, "dock": "main" } ] }
          ],
          "working": [],
          "activePresetId": "cols"
        }
        """;

    private static WorkspaceLayoutService ColumnsService(FakePlacesStorage storage)
    {
        storage.ContentsToReturn = ColumnsFile;
        return NewService(storage);
    }

    private static string[] Column(WorkspaceLayoutService service, string dock)
        => service.VisiblePanels.Where(p => PanelDocks.Normalize(p.Dock) == dock).Select(p => p.Id).ToArray();

    [Fact]
    public void AColumnsLayout_SaysSo_AndARowsLayoutDoesnt()
    {
        var service = ColumnsService(NewStorage());

        Assert.True(service.ActiveIsColumns);
        Ok(service.Activate(BuiltInLayouts.ActivityAtlasId));
        Assert.False(service.ActiveIsColumns);
    }

    [Fact]
    public void SetDock_MovesAPanelToTheBottomOfTheOtherColumn_AsOneUndoableStep()
    {
        var service = ColumnsService(NewStorage());
        service.BeginArrange();

        Assert.True(service.SetDock("shelf", PanelDocks.Left));
        Assert.Equal(new[] { "sessions", "shelf" }, Column(service, PanelDocks.Left));
        Assert.Equal(new[] { "places" }, Column(service, PanelDocks.Main));
        Assert.Equal($"Move {PanelTypes.DisplayName(PanelTypes.Shelf)}", service.UndoLabel);
        Assert.False(service.SetDock("shelf", PanelDocks.Left));

        service.Undo();
        Assert.Equal(new[] { "places", "shelf" }, Column(service, PanelDocks.Main));
    }

    [Fact]
    public void SetDock_OnARowsLayout_OrOutsideArrange_ChangesNothing()
    {
        var columns = ColumnsService(NewStorage());
        Assert.False(columns.SetDock("shelf", PanelDocks.Left));

        var rows = NewService(NewStorage());
        rows.BeginArrange();
        Assert.False(rows.SetDock("shelf", PanelDocks.Left));
    }

    [Fact]
    public void MoveEarlierAndLater_StayInThePanelsColumn()
    {
        var service = ColumnsService(NewStorage());
        service.BeginArrange();

        // Places is first in the main column, though Sessions comes before it in the stored order.
        Assert.False(service.MoveEarlier("places"));
        Assert.False(service.MoveLater("shelf"));
        Assert.False(service.MoveLater("sessions"));

        Assert.True(service.MoveEarlier("shelf"));
        Assert.Equal(new[] { "shelf", "places" }, Column(service, PanelDocks.Main));
    }

    [Fact]
    public void MoveTo_DropsIntoTheOtherColumn_BeforeAPanel()
    {
        var service = ColumnsService(NewStorage());
        service.BeginArrange();

        Assert.True(service.MoveTo("shelf", PanelDocks.Left, "sessions"));
        Assert.Equal(new[] { "shelf", "sessions" }, Column(service, PanelDocks.Left));
        Ok(service.Done());
        Assert.True(service.IsModified);
    }

    [Fact]
    public void AddPanel_OnAColumnsLayout_GoesToTheBottomOfTheMainColumn()
    {
        var service = ColumnsService(NewStorage());

        Assert.True(service.AddPanelNow(PanelTypes.Activity, out var persistence));
        Ok(persistence);
        Assert.Equal(new[] { "places", "shelf", "activity" }, Column(service, PanelDocks.Main));
    }

    [Fact]
    public void SaveAsNew_AndDuplicate_KeepAColumnsArrangement()
    {
        var storage = NewStorage();
        var service = ColumnsService(storage);

        var mine = SaveAs(service, "Mine");
        Assert.True(NewService(storage).ActiveIsColumns);

        Assert.True(service.Duplicate("cols", out var copy, out var persistence).Success);
        Ok(persistence);
        Ok(service.Activate(copy!));
        Assert.True(service.ActiveIsColumns);
        Assert.NotEqual(mine, copy);
    }

    [Fact]
    public void ARowsLayout_WritesNoArrangementOrDock()
    {
        var storage = NewStorage();
        var service = NewService(storage);

        SaveAs(service, "Mine");

        Assert.DoesNotContain("arrangement", storage.LastWritten);
        Assert.DoesNotContain("\"dock\"", storage.LastWritten);
    }
}
