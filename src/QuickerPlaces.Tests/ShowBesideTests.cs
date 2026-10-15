using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Models.Workspace;
using QuickerPlaces.Services.Workspace;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The Suggestions panel type and <see cref="WorkspaceLayoutService.ShowBeside"/>
/// (session suggestions plan G13, §7.1): opened beside Sessions without Arrange
/// mode, as one undoable, saved step; not offered in Add panel until its view exists.
/// </summary>
public sealed class ShowBesideTests
{
    private const string ColumnsFile = """
        {
          "schemaVersion": 1,
          "presets": [
            { "id": "cols", "name": "Cols", "arrangement": "columns",
              "panels": [ { "id": "activity", "type": "activity", "span": 4, "dock": "left" },
                          { "id": "sessions", "type": "sessions", "span": 6, "dock": "left" },
                          { "id": "places", "type": "places", "span": 12, "dock": "main" },
                          { "id": "shelf", "type": "shelf", "span": 8, "dock": "main" } ] }
          ],
          "working": [],
          "activePresetId": "cols"
        }
        """;

    private const string HiddenFile = """
        {
          "schemaVersion": 1,
          "presets": [
            { "id": "cols", "name": "Cols", "arrangement": "columns",
              "panels": [ { "id": "sessions", "type": "sessions", "span": 4, "dock": "left" },
                          { "id": "shelf", "type": "shelf", "span": 8, "dock": "main" },
                          { "id": "suggestions", "type": "suggestions", "span": 12, "dock": "main", "hidden": true },
                          { "id": "places", "type": "places", "span": 12, "dock": "main" } ] }
          ],
          "working": [],
          "activePresetId": "cols"
        }
        """;

    private const string RowsFile = """
        {
          "schemaVersion": 1,
          "presets": [
            { "id": "rows", "name": "Rows",
              "panels": [ { "id": "sessions", "type": "sessions", "span": 4 },
                          { "id": "suggestions", "type": "suggestions", "span": 6, "hidden": true },
                          { "id": "shelf", "type": "shelf", "span": 8 } ] }
          ],
          "working": [],
          "activePresetId": "rows"
        }
        """;

    private static FakePlacesStorage NewStorage(string? contents = null)
    {
        var storage = new FakePlacesStorage { StoreFilePath = @"C:\fake\workspace-layouts.json" };
        if (contents is not null)
            storage.ContentsToReturn = contents;
        return storage;
    }

    private static WorkspaceLayoutService NewService(FakePlacesStorage storage)
        => new(new WorkspaceStore(storage, null, new ManualTimeProvider()));

    private static string[] All(WorkspaceLayoutService service) => service.Panels.Select(p => p.Id).ToArray();

    private static void Ok(PersistenceResult persistence) => Assert.True(persistence.Saved, persistence.UserMessage);

    [Fact]
    public void InAColumnsLayout_InsertsAfterTheAnchor_WithItsSpanAndDock()
    {
        var service = NewService(NewStorage(ColumnsFile));

        Assert.True(service.ShowBeside(PanelTypes.Suggestions, "sessions", out var persistence));
        Ok(persistence);

        Assert.Equal(new[] { "activity", "sessions", "suggestions", "places", "shelf" }, All(service));
        var added = service.Panels.Single(p => p.Type == PanelTypes.Suggestions);
        Assert.Equal(6, added.Span);
        Assert.Equal(PanelDocks.Left, added.Dock);
        Assert.False(added.Hidden);
        Assert.False(service.IsArranging);
        Assert.Equal("Show Suggestions", service.UndoLabel);
    }

    [Fact]
    public void InARowsLayout_InsertsAfterTheAnchor_WithItsSpan()
    {
        var service = NewService(NewStorage());

        Assert.True(service.ShowBeside(PanelTypes.Suggestions, PanelTypes.Shelf, out var persistence));
        Ok(persistence);

        Assert.Equal(new[] { PanelTypes.Activity, PanelTypes.Shelf, PanelTypes.Suggestions, PanelTypes.Sessions }, service.Panels.Select(p => p.Type));
        Assert.Equal(8, service.Panels.Single(p => p.Type == PanelTypes.Suggestions).Span);
        Assert.Equal(BuiltInLayouts.ActivityAtlasId, service.ActivePresetId);
        Assert.True(service.IsModified);

        Ok(service.Undo());

        Assert.False(service.IsModified);
        Assert.Equal(BuiltInLayouts.ActivityAtlasId, service.ActivePresetId);
        Assert.DoesNotContain(PanelTypes.Suggestions, service.Panels.Select(p => p.Type));
    }

    [Fact]
    public void InARowsLayout_AHiddenOne_IsShownInPlace_AndAVisibleOneIsLeftAlone()
    {
        var service = NewService(NewStorage(RowsFile));

        Assert.True(service.ShowBeside(PanelTypes.Suggestions, "sessions", out var persistence));
        Ok(persistence);
        Assert.Equal(new[] { "sessions", "suggestions", "shelf" }, All(service));
        Assert.False(service.Panels[1].Hidden);

        Assert.False(service.ShowBeside(PanelTypes.Suggestions, "sessions", out var again));
        Ok(again);
        Assert.Equal(new[] { "sessions", "suggestions", "shelf" }, All(service));

        Ok(service.Undo());
        Assert.True(service.Panels[1].Hidden);
    }

    [Fact]
    public void InARowsLayout_AnUnknownAnchor_InsertsLast_WithNoDock()
    {
        var service = NewService(NewStorage());

        Assert.True(service.ShowBeside(PanelTypes.Suggestions, "nope", out var persistence));
        Ok(persistence);

        Assert.Equal(PanelTypes.Suggestions, service.Panels[^1].Type);
        Assert.Null(service.Panels[^1].Dock);
    }

    [Fact]
    public void InARowsLayout_InArrangeMode_IsADraftEdit_RevertDropsIt()
    {
        var storage = NewStorage();
        var service = NewService(storage);
        service.BeginArrange();

        Assert.True(service.ShowBeside(PanelTypes.Suggestions, PanelTypes.Shelf, out var persistence));
        Ok(persistence);
        Assert.Equal(0, storage.WriteCount);
        Assert.Contains(PanelTypes.Suggestions, service.Panels.Select(p => p.Type));

        service.Revert();
        Assert.DoesNotContain(PanelTypes.Suggestions, service.Panels.Select(p => p.Type));
    }

    [Fact]
    public void AnUnknownType_IsRefused_AndChangesNothing()
    {
        var storage = NewStorage();
        var service = NewService(storage);

        Assert.False(service.ShowBeside("bogus", PanelTypes.Shelf, out var persistence));

        Ok(persistence);
        Assert.False(service.IsModified);
        Assert.False(service.CanUndo);
        Assert.Equal(0, storage.WriteCount);
    }

    [Fact]
    public void AHiddenOne_IsShownInItsRememberedPlace()
    {
        var service = NewService(NewStorage(HiddenFile));

        Assert.True(service.ShowBeside(PanelTypes.Suggestions, "sessions", out var persistence));
        Ok(persistence);

        Assert.Equal(new[] { "sessions", "shelf", "suggestions", "places" }, All(service));
        Assert.False(service.Panels.Single(p => p.Id == "suggestions").Hidden);
        Assert.Equal(12, service.Panels.Single(p => p.Id == "suggestions").Span);
        Assert.Equal("Show Suggestions", service.UndoLabel);
    }

    [Fact]
    public void WhenAlreadyVisible_ChangesNothing()
    {
        var storage = NewStorage(ColumnsFile);
        var service = NewService(storage);
        Assert.True(service.ShowBeside(PanelTypes.Suggestions, "sessions", out var persistence));
        Ok(persistence);
        var writes = storage.WriteCount;
        var before = All(service);

        Assert.False(service.ShowBeside(PanelTypes.Suggestions, "sessions", out var again));
        Ok(again);

        Assert.Equal(before, All(service));
        Assert.Equal(writes, storage.WriteCount);
    }

    [Fact]
    public void Undo_RemovesTheInsertedPanel()
    {
        var service = NewService(NewStorage(ColumnsFile));
        Assert.True(service.ShowBeside(PanelTypes.Suggestions, "sessions", out var persistence));
        Ok(persistence);

        Ok(service.Undo());

        Assert.Equal(new[] { "activity", "sessions", "places", "shelf" }, All(service));
    }

    [Fact]
    public void Undo_HidesAShownPanelAgain()
    {
        var service = NewService(NewStorage(HiddenFile));
        Assert.True(service.ShowBeside(PanelTypes.Suggestions, "sessions", out var persistence));
        Ok(persistence);

        Ok(service.Undo());

        Assert.True(service.Panels.Single(p => p.Id == "suggestions").Hidden);
    }

    [Fact]
    public void IsSaved_AndComesBackOnReload()
    {
        var storage = NewStorage(ColumnsFile);
        var service = NewService(storage);
        Assert.True(service.ShowBeside(PanelTypes.Suggestions, "sessions", out var persistence));
        Ok(persistence);

        var reloaded = NewService(storage);

        Assert.Equal(new[] { "activity", "sessions", "suggestions", "places", "shelf" }, All(reloaded));
    }

    [Fact]
    public void AnUnknownAnchor_InsertsLast_InTheMainColumn()
    {
        var service = NewService(NewStorage(ColumnsFile));

        Assert.True(service.ShowBeside(PanelTypes.Suggestions, "nope", out var persistence));
        Ok(persistence);

        Assert.Equal("suggestions", All(service)[^1]);
        Assert.Equal(PanelDocks.Main, service.Panels[^1].Dock);
        Assert.Equal(PanelTypes.DefaultSpan(PanelTypes.Suggestions), service.Panels[^1].Span);
    }

    [Fact]
    public void InArrangeMode_IsADraftEdit_NotWrittenUntilDone()
    {
        var storage = NewStorage(ColumnsFile);
        var service = NewService(storage);
        service.BeginArrange();

        Assert.True(service.ShowBeside(PanelTypes.Suggestions, "sessions", out var persistence));
        Ok(persistence);

        Assert.True(service.IsArranging);
        Assert.Equal(0, storage.WriteCount);
        Assert.Contains("suggestions", All(service));
        service.Revert();
        Assert.DoesNotContain("suggestions", All(service));
    }

    [Fact]
    public void Suggestions_IsKnownButNotAddable()
    {
        var service = NewService(NewStorage());

        Assert.True(PanelTypes.IsKnown(PanelTypes.Suggestions));
        Assert.False(PanelTypes.IsAvailable(PanelTypes.Suggestions));
        Assert.DoesNotContain(PanelTypes.Suggestions, service.AddablePanelTypes);
        Assert.Equal(PanelTypes.Sessions, PanelTypes.Known[PanelTypes.Known.ToList().IndexOf(PanelTypes.Suggestions) - 1]);
        Assert.False(service.AddPanelNow(PanelTypes.Suggestions, out _));
    }

    [Fact]
    public void Suggestions_NamesAndSizesItselfLikeSessions()
    {
        Assert.Equal("Suggestions", PanelTypes.DisplayName(PanelTypes.Suggestions));
        Assert.Equal(PanelTypes.DefaultSpan(PanelTypes.Sessions), PanelTypes.DefaultSpan(PanelTypes.Suggestions));
        Assert.Equal(PanelLayoutEngine.MinimumWidth(PanelTypes.Sessions), PanelLayoutEngine.MinimumWidth(PanelTypes.Suggestions));
    }
}
