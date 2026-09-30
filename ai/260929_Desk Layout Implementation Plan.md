# Desk layout, Favourites panel and Recents panel — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Desk built-in layout from the user's sketch:
- a left column of Favourites and Sessions;
- a main column of Saved places, Recents and Year activity.

Along the way the canvas gains column layouts, favourites become a panel, and the File shelf becomes Recents, taking over the Recents window's features.

**Spec:** [260929_Desk Layout and Recents Panel Design.md](260929_Desk%20Layout%20and%20Recents%20Panel%20Design.md). Read it first.

**Architecture:**
- **Layouts:** a layout gains an optional `arrangement` (`"columns"`) and each panel an optional `dock` (`"left"` / `"main"`). A new `PanelLayoutEngine.PackColumns` places column layouts. The row packer is untouched.
- **View:** `WorkspaceView` puts column layouts in two stack grids.
- **Favourites:** the strip moves from `MainWindow` into a reusable `FavouritesPanel`.
- **Recents:** the shelf's view model gains a tracked-folder scope, per-folder time and folder-level grouping, all UI-free and tested. `FileShelfPanel` gains a tracked-folders strip driven by the existing `ActivityViewModel`.

**Tech stack:** .NET 10, WPF, xUnit.
- Tests link app sources one by one in `src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj`. A new UI-free source file must be linked there.
- Run all commands from `C:\QuickerPlaces`.

**Conventions:**
- Comments and doc comments follow the surrounding code: plain sentences, and the plan section in brackets where the code already does that.
- Commit messages are imperative sentence case, with no `feat:` prefix, and end with the `Co-Authored-By` trailer.
- Tests are named `Something_HappensWhen…`, as in the existing files.

---

## File map

| File | Change |
|---|---|
| `src/QuickerPlaces/Models/Workspace/PanelInstance.cs` | `Dock`; `PanelDocks`; `PanelTypes.Favourites`; Recents name |
| `src/QuickerPlaces/Models/Workspace/LayoutPreset.cs` | `Arrangement`; `LayoutArrangements` |
| `src/QuickerPlaces/Models/Workspace/WorkspaceQuery.cs` | `Root` |
| `src/QuickerPlaces/Services/Workspace/PanelLayoutEngine.cs` | `PanelDock`, `PackColumns`, `ColumnDrop`, `ColumnDropTarget`, Favourites' minimum width |
| `src/QuickerPlaces/Services/Workspace/BuiltInLayouts.cs` | arrangement and docks on `BuiltInLayout`; `Desk` |
| `src/QuickerPlaces/Services/Workspace/WorkspaceLayoutService.cs` | `ActiveArrangement`, `ActiveIsColumns`, `SetDock`, `MoveTo`, column-aware moves, Add panel, Save as new, Duplicate |
| `src/QuickerPlaces/ViewModels/WorkspaceViewModel.cs` | `IsColumns`, `SetDock`, `ColumnDrop`, `DropInColumn`, `ShowsFavouritesPanel`, column arrows |
| `src/QuickerPlaces/Views/Panels/PanelFrame.xaml(.cs)` | Column choice, up/down arrows, no resize edge in columns |
| `src/QuickerPlaces/Views/WorkspaceView.xaml.cs` | two stacks, heights, column drag, Favourites and Recents wiring |
| `src/QuickerPlaces/Views/Panels/FavouritesPanel.xaml(.cs)` | **new**: the favourites strip as a control |
| `src/QuickerPlaces/Views/MainWindow.xaml(.cs)` | uses `FavouritesPanel`; strip rule; hides Recents button in the workspace |
| `src/QuickerPlaces/Services/Activity/TrackedFolderPaths.cs` | **new**: level below a root, innermost root, level labels |
| `src/QuickerPlaces/Services/Activity/ActivityFormat.cs` | `TrackingSummary` |
| `src/QuickerPlaces/ViewModels/ActivityViewModel.cs` | `ActivityFolderRow` uses `TrackedFolderPaths` |
| `src/QuickerPlaces/Services/Library/LibraryIndex.cs` | `LibraryItem.RecentTime`, `TreePath` |
| `src/QuickerPlaces/Services/Library/LibrarySnapshot.cs` | `RecentsRootData.Path` |
| `src/QuickerPlaces/Services/Library/LibraryQueryEngine.cs` | `RootScope` in `LibraryFilter`, `Passes`, folder heat |
| `src/QuickerPlaces/ViewModels/LibraryViewModel.cs` | root scope, chips, `Level` grouping, row Visits/Time/CanAddAsPlace |
| `src/QuickerPlaces/ViewModels/SaveLayoutViewModel.cs` | describes a root scope |
| `src/QuickerPlaces/Views/Panels/FileShelfPanel.xaml(.cs)` | tracked-folders strip, Visits/Time columns, Folder level, Add as place |
| `src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj` | link `TrackedFolderPaths.cs` |
| Tests | `PanelDocksTests.cs` (new), `TrackedFolderPathsTests.cs` (new), plus additions to `PanelLayoutEngineTests`, `WorkspaceStoreTests`, `WorkspaceLayoutServiceTests`, `WorkspaceViewModelTests`, `LibraryIndexTests`, `LibraryQueryEngineTests`, `LibraryViewModelTests`, `ActivityFormatTests.cs` (new) |

---

### Task 1: Arrangement and dock in the layout model

**Files:**
- Modify: `src/QuickerPlaces/Models/Workspace/PanelInstance.cs`
- Modify: `src/QuickerPlaces/Models/Workspace/LayoutPreset.cs`
- Test: `src/QuickerPlaces.Tests/PanelDocksTests.cs` (new), `src/QuickerPlaces.Tests/WorkspaceStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `src/QuickerPlaces.Tests/PanelDocksTests.cs`:

```csharp
using QuickerPlaces.Models.Workspace;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>Columns layouts (Desk layout design §2): which column a panel is in, and what counts as a change.</summary>
public sealed class PanelDocksTests
{
    [Theory]
    [InlineData("left", true)]
    [InlineData("main", false)]
    [InlineData(null, false)]
    [InlineData("sideways", false)]
    public void OnlyLeft_IsTheLeftColumn(string? dock, bool left) => Assert.Equal(left, PanelDocks.IsLeft(dock));

    [Fact]
    public void AnAbsentOrUnknownDock_ReadsAsMain()
    {
        Assert.Equal(PanelDocks.Main, PanelDocks.Normalize(null));
        Assert.Equal(PanelDocks.Main, PanelDocks.Normalize("sideways"));
        Assert.Equal(PanelDocks.Left, PanelDocks.Normalize("left"));
    }

    [Fact]
    public void SameAs_ComparesColumns_NotSpellings()
    {
        var absent = new PanelInstance { Id = "shelf", Type = "shelf", Dock = null };
        var main = new PanelInstance { Id = "shelf", Type = "shelf", Dock = PanelDocks.Main };
        var left = new PanelInstance { Id = "shelf", Type = "shelf", Dock = PanelDocks.Left };

        Assert.True(absent.SameAs(main));
        Assert.False(main.SameAs(left));
        Assert.Equal(PanelDocks.Left, left.Clone().Dock);
    }

    [Fact]
    public void OnlyColumns_IsAColumnsArrangement()
    {
        Assert.True(LayoutArrangements.IsColumns("columns"));
        Assert.False(LayoutArrangements.IsColumns(null));
        Assert.False(LayoutArrangements.IsColumns("rows"));
        Assert.False(LayoutArrangements.IsColumns("spiral"));
        Assert.Equal("columns", new LayoutPreset { Arrangement = "columns" }.Clone().Arrangement);
    }
}
```

Add to `src/QuickerPlaces.Tests/WorkspaceStoreTests.cs`, after `UnknownPanelsAndFields_SurviveARoundTrip`:

```csharp
    [Fact]
    public void ArrangementAndDock_SurviveARoundTrip_UnknownValuesIncluded()
    {
        var storage = NewStorage("""
            {
              "schemaVersion": 1,
              "presets": [
                { "id": "p1", "name": "Cols", "arrangement": "columns",
                  "panels": [ { "id": "sessions", "type": "sessions", "span": 4, "dock": "left" },
                              { "id": "shelf", "type": "shelf", "span": 8, "dock": "sideways" } ] },
                { "id": "p2", "name": "Later", "arrangement": "spiral",
                  "panels": [ { "id": "shelf", "type": "shelf", "span": 12 } ] }
              ],
              "working": []
            }
            """);
        var service = new WorkspaceLayoutService(NewStore(storage));
        Assert.True(service.Activate("p1").Saved);

        var written = storage.LastWritten!;
        Assert.Contains("\"arrangement\": \"columns\"", written);
        Assert.Contains("\"arrangement\": \"spiral\"", written);
        Assert.Contains("\"dock\": \"left\"", written);
        Assert.Contains("\"dock\": \"sideways\"", written);
        Assert.Equal(PanelDocks.Left, service.Panels[0].Dock);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/QuickerPlaces.Tests --filter "FullyQualifiedName~PanelDocksTests|FullyQualifiedName~WorkspaceStoreTests"`
Expected: build errors: `PanelDocks`, `LayoutArrangements`, `Dock` and `Arrangement` don't exist.

- [ ] **Step 3: Add `Dock` and `PanelDocks`**

In `src/QuickerPlaces/Models/Workspace/PanelInstance.cs`, add this property after `Hidden`:

```csharp
    /// <summary>
    /// In a columns layout, the column: <see cref="PanelDocks.Left"/> or
    /// <see cref="PanelDocks.Main"/>. Absent, or a value this build doesn't
    /// know, reads as main and is kept as written. Rows layouts ignore it.
    /// </summary>
    public string? Dock { get; set; }
```

In `Clone()`, add `Dock = Dock,` after `Hidden = Hidden,`. Replace `SameAs` with:

```csharp
    /// <summary>Same panel, place-independent: everything but <see cref="Extra"/>, which this build never changes. Docks compare by column.</summary>
    public bool SameAs(PanelInstance other)
        => Id == other.Id && Type == other.Type && Span == other.Span && Hidden == other.Hidden &&
           PanelDocks.IsLeft(Dock) == PanelDocks.IsLeft(other.Dock) && SameView(View, other.View);
```

At the end of the file, after `PanelSpans`, add:

```csharp
/// <summary>The two columns of a columns layout (Desk layout design §2): a left third and a main two-thirds.</summary>
public static class PanelDocks
{
    public const string Left = "left";
    public const string Main = "main";

    public static bool IsLeft(string? dock) => dock == Left;

    /// <summary>Left, or main for anything else: absent and unknown docks read as main.</summary>
    public static string Normalize(string? dock) => IsLeft(dock) ? Left : Main;

    /// <summary>"left column" or "main column", as the status line and Undo name it.</summary>
    public static string DisplayName(string? dock) => IsLeft(dock) ? "left column" : "main column";
}
```

- [ ] **Step 4: Add `Arrangement` and `LayoutArrangements`**

In `src/QuickerPlaces/Models/Workspace/LayoutPreset.cs`, add after `Name`:

```csharp
    /// <summary>
    /// <see cref="LayoutArrangements.Columns"/> for a layout in two columns;
    /// null (rows) otherwise. A value this build doesn't know reads as rows
    /// and is kept as written.
    /// </summary>
    public string? Arrangement { get; set; }
```

In `LayoutPreset.Clone()`, add `Arrangement = Arrangement,` after `Name = Name,`. At the end of the file add:

```csharp
/// <summary>How a layout places its panels (Desk layout design §2): rows of the twelve-column canvas, or two columns.</summary>
public static class LayoutArrangements
{
    public const string Rows = "rows";
    public const string Columns = "columns";

    public static bool IsColumns(string? arrangement) => arrangement == Columns;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test src/QuickerPlaces.Tests`
Expected: PASS, everything (1071 + 9).

- [ ] **Step 6: Commit**

```bash
git add src/QuickerPlaces/Models/Workspace/PanelInstance.cs src/QuickerPlaces/Models/Workspace/LayoutPreset.cs src/QuickerPlaces.Tests/PanelDocksTests.cs src/QuickerPlaces.Tests/WorkspaceStoreTests.cs
git commit -m "Add a layout's arrangement and a panel's column to the workspace models

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `PackColumns` and `ColumnDrop` in the engine

**Files:**
- Modify: `src/QuickerPlaces/Services/Workspace/PanelLayoutEngine.cs`
- Test: `src/QuickerPlaces.Tests/PanelLayoutEngineTests.cs`

- [ ] **Step 1: Write the failing tests**

Append inside `PanelLayoutEngineTests`:

```csharp
    // ---------------------------------------------------------------
    // Columns layouts (Desk layout design §2)
    // ---------------------------------------------------------------

    private static PanelInstance Docked(string type, string? dock, bool hidden = false)
        => new() { Id = type, Type = type, Span = PanelSpans.Third, Dock = dock, Hidden = hidden };

    private static string DescribeDocked(PanelPlacement p) => $"{p.PanelId}@{p.Row}:{p.Column}+{p.Span}/{p.Dock}";

    private static readonly PanelInstance[] Desk =
    {
        Docked(PanelTypes.Sessions, PanelDocks.Left),
        Docked(PanelTypes.Places, PanelDocks.Main),
        Docked(PanelTypes.Shelf, PanelDocks.Main),
        Docked(PanelTypes.Activity, PanelDocks.Main),
    };

    [Fact]
    public void Columns_StackEachColumn_LeftAThird_MainTwoThirds()
    {
        var placed = PanelLayoutEngine.PackColumns(Desk, 1800);

        Assert.Equal(new[] { "sessions@0:0+4/Left", "places@0:4+8/Main", "shelf@1:4+8/Main", "activity@2:4+8/Main" },
            placed.Select(DescribeDocked));
    }

    [Fact]
    public void AnEmptyColumn_GivesItsWidthToTheOther()
    {
        var mainOnly = PanelLayoutEngine.PackColumns(new[] { Docked(PanelTypes.Shelf, PanelDocks.Main), Docked(PanelTypes.Sessions, PanelDocks.Left, hidden: true) }, 1800);
        var leftOnly = PanelLayoutEngine.PackColumns(new[] { Docked(PanelTypes.Sessions, PanelDocks.Left) }, 1800);

        Assert.Equal(new[] { "shelf@0:0+12/Main" }, mainOnly.Select(DescribeDocked));
        Assert.Equal(new[] { "sessions@0:0+12/Left" }, leftOnly.Select(DescribeDocked));
    }

    [Fact]
    public void AnUnknownDock_ReadsAsMain()
    {
        var placed = PanelLayoutEngine.PackColumns(new[] { Docked(PanelTypes.Sessions, PanelDocks.Left), Docked(PanelTypes.Shelf, "sideways") }, 1800);

        Assert.Equal(PanelDock.Main, placed.Single(p => p.PanelId == PanelTypes.Shelf).Dock);
    }

    [Fact]
    public void ANarrowWindow_StacksTheColumns_MainFirst_FullWidth()
    {
        // A third of 700 is too narrow for Sessions (300).
        var placed = PanelLayoutEngine.PackColumns(Desk, 700);

        Assert.Equal(new[] { "places@0:0+12/None", "shelf@1:0+12/None", "activity@2:0+12/None", "sessions@3:0+12/None" },
            placed.Select(DescribeDocked));
    }

    [Fact]
    public void ColumnDrop_ReordersWithinAColumn()
    {
        var drop = PanelLayoutEngine.ColumnDrop(Desk, PanelTypes.Activity, PanelTypes.Places, after: false);

        Assert.Equal(new ColumnDropTarget(PanelDocks.Main, PanelTypes.Places), drop);
    }

    [Fact]
    public void ColumnDrop_AcrossColumns_TakesTheTargetsColumn()
    {
        var below = PanelLayoutEngine.ColumnDrop(Desk, PanelTypes.Shelf, PanelTypes.Sessions, after: true);
        var above = PanelLayoutEngine.ColumnDrop(Desk, PanelTypes.Shelf, PanelTypes.Sessions, after: false);

        Assert.Equal(new ColumnDropTarget(PanelDocks.Left, null), below);
        Assert.Equal(new ColumnDropTarget(PanelDocks.Left, PanelTypes.Sessions), above);
    }

    [Theory]
    [InlineData("shelf", "shelf", false)]
    [InlineData("shelf", "places", true)]
    [InlineData("shelf", "activity", false)]
    [InlineData("activity", "shelf", true)]
    public void ColumnDrop_WhereItAlreadyIs_IsNoDrop(string dragged, string target, bool after)
        => Assert.Null(PanelLayoutEngine.ColumnDrop(Desk, dragged, target, after));
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/QuickerPlaces.Tests --filter "FullyQualifiedName~PanelLayoutEngineTests"`
Expected: build errors: `PackColumns`, `ColumnDrop`, `ColumnDropTarget`, `PanelDock` and `PanelPlacement.Dock` don't exist.

- [ ] **Step 3: Implement**

In `src/QuickerPlaces/Services/Workspace/PanelLayoutEngine.cs`, replace the `PanelPlacement` record header with:

```csharp
public sealed record PanelPlacement(string PanelId, string Type, int Row, int Column, int Span, int StoredSpan, PanelDock Dock = PanelDock.None)
```

The five-argument constructor stays as it is. Update the record's doc comment's first sentence to add: "In a columns layout, <see cref=\"Row\"/> is its place in its own column (<see cref=\"Dock\"/>)."

After the `DropTarget` record, add:

```csharp
/// <summary>Which column of a columns layout a placement is in: none in a rows layout, or when a narrow window stacks the columns.</summary>
public enum PanelDock
{
    None,
    Left,
    Main,
}

/// <summary>Where a panel dropped in a columns layout goes: into <see cref="Dock"/>, just before <see cref="BeforePanelId"/> in the stored order, or last.</summary>
public sealed record ColumnDropTarget(string Dock, string? BeforePanelId);
```

Inside `PanelLayoutEngine`, after `Pack(IEnumerable<PanelInstance>, double)`, add:

```csharp
    /// <summary>
    /// Places a columns layout's panels (Desk layout design §2): a left column
    /// a third wide and a main column two-thirds wide, each a stack of its own
    /// panels in their stored order. An empty column gives its width to the
    /// other. When a panel can't be read at its column's width
    /// (<see cref="MinimumWidth"/>), the columns stack instead, main first,
    /// every panel full width and <see cref="PanelDock.None"/>: presentation
    /// only, as reflow is. Stored docks never change here.
    /// </summary>
    public static IReadOnlyList<PanelPlacement> PackColumns(IEnumerable<PanelInstance> panels, double width)
    {
        var shown = panels.Where(p => !p.Hidden).ToList();
        var left = shown.Where(p => PanelDocks.IsLeft(p.Dock)).ToList();
        var main = shown.Where(p => !PanelDocks.IsLeft(p.Dock)).ToList();
        var leftSpan = main.Count == 0 ? PanelSpans.Full : PanelSpans.Third;
        var mainSpan = left.Count == 0 ? PanelSpans.Full : PanelSpans.TwoThirds;

        var fits = left.All(p => PanelWidth(leftSpan, width) >= MinimumWidth(p.Type)) &&
                   main.All(p => PanelWidth(mainSpan, width) >= MinimumWidth(p.Type));
        if (!fits)
        {
            return main.Concat(left)
                .Select((p, row) => new PanelPlacement(p.Id, p.Type, row, 0, PanelSpans.Full, PanelSpans.Full))
                .ToList();
        }

        var mainColumn = left.Count == 0 ? 0 : PanelSpans.Third;
        return left.Select((p, row) => new PanelPlacement(p.Id, p.Type, row, 0, leftSpan, leftSpan, PanelDock.Left))
            .Concat(main.Select((p, row) => new PanelPlacement(p.Id, p.Type, row, mainColumn, mainSpan, mainSpan, PanelDock.Main)))
            .ToList();
    }
```

After `Drop(...)`, add:

```csharp
    /// <summary>
    /// A drop in a columns layout while dragging <paramref name="draggedId"/>
    /// over <paramref name="targetId"/>, above it or, when
    /// <paramref name="after"/>, below it: the target's column, and the panel
    /// the dragged one would go before in the stored order (null: last). Null
    /// when the drop would leave it where it is. <paramref name="panels"/> are
    /// the shown panels in stored order.
    /// </summary>
    public static ColumnDropTarget? ColumnDrop(IReadOnlyList<PanelInstance> panels, string draggedId, string targetId, bool after)
    {
        var dragged = panels.FirstOrDefault(p => p.Id == draggedId);
        var target = panels.FirstOrDefault(p => p.Id == targetId);
        if (dragged is null || target is null || draggedId == targetId)
            return null;

        var dock = PanelDocks.Normalize(target.Dock);
        var column = panels.Where(p => PanelDocks.Normalize(p.Dock) == dock).Select(p => p.Id).ToList();
        var others = column.Where(id => id != draggedId).ToList();
        var at = others.IndexOf(targetId) + (after ? 1 : 0);
        var before = at < others.Count ? others[at] : null;

        if (PanelDocks.Normalize(dragged.Dock) == dock)
        {
            var index = column.IndexOf(draggedId);
            var currentBefore = index + 1 < column.Count ? column[index + 1] : null;
            if (currentBefore == before)
                return null;
        }

        return new ColumnDropTarget(dock, before);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/QuickerPlaces.Tests --filter "FullyQualifiedName~PanelLayoutEngineTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/QuickerPlaces/Services/Workspace/PanelLayoutEngine.cs src/QuickerPlaces.Tests/PanelLayoutEngineTests.cs
git commit -m "Place columns layouts, and work out drops within and across columns

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Column edits in the layout service

**Files:**
- Modify: `src/QuickerPlaces/Services/Workspace/BuiltInLayouts.cs`
- Modify: `src/QuickerPlaces/Services/Workspace/WorkspaceLayoutService.cs`
- Test: `src/QuickerPlaces.Tests/WorkspaceLayoutServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Append inside `WorkspaceLayoutServiceTests`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/QuickerPlaces.Tests --filter "FullyQualifiedName~WorkspaceLayoutServiceTests"`
Expected: build errors: `ActiveIsColumns`, `SetDock` and `MoveTo` don't exist.

- [ ] **Step 3: Give built-ins an arrangement and docks**

In `src/QuickerPlaces/Services/Workspace/BuiltInLayouts.cs`, replace the whole `BuiltInLayout` class with:

```csharp
/// <summary>One built-in layout's factory definition.</summary>
public sealed class BuiltInLayout
{
    private readonly (string Type, int Span, string? Dock)[] _panels;

    public BuiltInLayout(string id, string name, int version, (string Type, int Span)[] panels)
        : this(id, name, version, null, panels.Select(p => (p.Type, p.Span, (string?)null)).ToArray())
    {
    }

    /// <summary>A built-in with an arrangement (<see cref="LayoutArrangements"/>) and, in columns, each panel's column.</summary>
    public BuiltInLayout(string id, string name, int version, string? arrangement, (string Type, int Span, string? Dock)[] panels)
    {
        Id = id;
        Name = name;
        Version = version;
        Arrangement = arrangement;
        _panels = panels;
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>Raised whenever the factory definition changes.</summary>
    public int Version { get; }

    /// <summary>Null for rows, or <see cref="LayoutArrangements.Columns"/>.</summary>
    public string? Arrangement { get; }

    /// <summary>True when every panel this layout needs is available in this build.</summary>
    public bool IsOffered => _panels.All(p => PanelTypes.IsAvailable(p.Type));

    /// <summary>A fresh copy of the factory panels. Panel ids are the panel types: one of each.</summary>
    public List<PanelInstance> CreatePanels()
        => _panels.Select(p => new PanelInstance { Id = p.Type, Type = p.Type, Span = p.Span, Dock = p.Dock }).ToList();
}
```

- [ ] **Step 4: Add the column edits to the service**

In `src/QuickerPlaces/Services/Workspace/WorkspaceLayoutService.cs`:

After `ActiveName`, add:

```csharp
    /// <summary>The active layout's arrangement: <see cref="LayoutArrangements.Columns"/>, or null for rows (Desk layout design §2).</summary>
    public string? ActiveArrangement => ArrangementOf(ActivePresetId);

    public bool ActiveIsColumns => LayoutArrangements.IsColumns(ActiveArrangement);
```

In `MoveEarlier` and `MoveLater`, replace `var visible = _panels.Where(p => !p.Hidden).ToList();` with `var visible = VisibleInColumnOf(panelId);`, and update both doc comments to say "…the visible panel before (after) it in its column (D1; Desk layout design §3)".

After `SetSpan`, add:

```csharp
    /// <summary>
    /// Columns layouts' Column choice: moves a panel to the bottom of the
    /// other column. False outside Arrange mode, in a rows layout, or when it
    /// is in that column already.
    /// </summary>
    public bool SetDock(string panelId, string dock)
    {
        if (!IsArranging || !ActiveIsColumns)
            return false;

        var draft = Clone(_panels);
        var panel = draft.Find(p => p.Id == panelId);
        var column = PanelDocks.Normalize(dock);
        if (panel is null || PanelDocks.Normalize(panel.Dock) == column)
            return false;

        panel.Dock = column;
        draft.Remove(panel);
        draft.Add(panel);
        return ApplyDraft(draft, $"Move {PanelTypes.DisplayName(panel.Type)}");
    }

    /// <summary>
    /// A drop in a columns layout: the panel goes into <paramref name="dock"/>,
    /// just before <paramref name="beforePanelId"/> in the stored order, or
    /// last when that is null, as one step (D1).
    /// </summary>
    public bool MoveTo(string panelId, string dock, string? beforePanelId)
    {
        if (!IsArranging || !ActiveIsColumns || panelId == beforePanelId)
            return false;

        var draft = Clone(_panels);
        var panel = draft.Find(p => p.Id == panelId);
        if (panel is null || (beforePanelId is not null && !draft.Exists(p => p.Id == beforePanelId)))
            return false;

        panel.Dock = PanelDocks.Normalize(dock);
        draft.Remove(panel);
        var at = beforePanelId is null ? draft.Count : draft.FindIndex(p => p.Id == beforePanelId);
        draft.Insert(at, panel);
        return ApplyDraft(draft, $"Move {PanelTypes.DisplayName(panel.Type)}");
    }
```

In `AddPanel`, replace the `draft.Add(new PanelInstance { … })` line with:

```csharp
            draft.Add(new PanelInstance
            {
                Id = WorkspaceValidation.NewPanelId(type, ids),
                Type = type,
                Span = PanelTypes.DefaultSpan(type),
                Dock = ActiveIsColumns ? PanelDocks.Main : null,
            });
```

In `SaveAsNew(string? name, WorkspaceQuery? filters, …)`, add `var arrangement = ActiveArrangement;` as the first line after the name validation succeeds, before `var shown = Clone(_panels);`. In the `new LayoutPreset { … }` initializer add `Arrangement = arrangement,`.

In `Duplicate`, add `Arrangement = ArrangementOf(id),` to the `new LayoutPreset { … }` initializer.

In the Helpers region, after `NameOf`, add:

```csharp
    private string? ArrangementOf(string id) => BuiltInLayouts.Find(id)?.Arrangement ?? FindPreset(id)?.Arrangement;

    /// <summary>The visible panels a keyboard move swaps among: all of them in a rows layout, the panel's own column in a columns layout.</summary>
    private List<PanelInstance> VisibleInColumnOf(string panelId)
    {
        var visible = _panels.Where(p => !p.Hidden).ToList();
        if (!ActiveIsColumns)
            return visible;

        var column = PanelDocks.Normalize(visible.Find(p => p.Id == panelId)?.Dock);
        return visible.Where(p => PanelDocks.Normalize(p.Dock) == column).ToList();
    }
```

- [ ] **Step 5: Run all the tests**

Run: `dotnet test src/QuickerPlaces.Tests`
Expected: PASS, everything.

- [ ] **Step 6: Commit**

```bash
git add src/QuickerPlaces/Services/Workspace/BuiltInLayouts.cs src/QuickerPlaces/Services/Workspace/WorkspaceLayoutService.cs src/QuickerPlaces.Tests/WorkspaceLayoutServiceTests.cs
git commit -m "Move panels between and within columns in the layout service

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Columns in the workspace view model

**Files:**
- Modify: `src/QuickerPlaces/ViewModels/WorkspaceViewModel.cs`
- Test: `src/QuickerPlaces.Tests/WorkspaceViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Append inside `WorkspaceViewModelTests`:

```csharp
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

    private WorkspaceViewModel NewColumnsWorkspace()
    {
        _layoutStorage.ContentsToReturn = ColumnsFile;
        return NewWorkspace();
    }

    [Fact]
    public void AColumnsLayout_PlacesItsPanelsInTwoStacks()
    {
        var workspace = NewColumnsWorkspace();

        Assert.True(workspace.IsColumns);
        Assert.Equal(new[] { ("sessions", PanelDock.Left, 0, 0, 4), ("places", PanelDock.Main, 0, 4, 8), ("shelf", PanelDock.Main, 1, 4, 8) },
            workspace.Panels.Select(p => (p.Id, p.Dock, p.Row, p.Column, p.Span)));
        Assert.True(workspace.Panels.Single(p => p.Id == "sessions").InLeftColumn);
    }

    [Fact]
    public void InAColumnsLayout_TheArrowsMoveWithinAColumn()
    {
        var workspace = NewColumnsWorkspace();
        workspace.BeginArrange();

        var places = workspace.Panels.Single(p => p.Id == "places");
        var shelf = workspace.Panels.Single(p => p.Id == "shelf");
        var sessions = workspace.Panels.Single(p => p.Id == "sessions");
        Assert.Equal((false, true), (places.CanMoveEarlier, places.CanMoveLater));
        Assert.Equal((true, false), (shelf.CanMoveEarlier, shelf.CanMoveLater));
        Assert.Equal((false, false), (sessions.CanMoveEarlier, sessions.CanMoveLater));

        Assert.True(workspace.MoveEarlier("shelf"));
        Assert.Equal($"Moved {PanelTypes.DisplayName(PanelTypes.Shelf)} up.", workspace.Status);
    }

    [Fact]
    public void TheColumnChoice_AndADrop_MoveAPanelToTheOtherColumn()
    {
        var workspace = NewColumnsWorkspace();
        workspace.BeginArrange();

        Assert.True(workspace.SetDock("shelf", PanelDocks.Left));
        Assert.Equal($"Moved {PanelTypes.DisplayName(PanelTypes.Shelf)} to the left column.", workspace.Status);
        Assert.Equal(new[] { "sessions", "shelf" }, workspace.Panels.Where(p => p.Dock == PanelDock.Left).Select(p => p.Id));

        Assert.NotNull(workspace.ColumnDrop("sessions", "places", after: true));
        Assert.True(workspace.DropInColumn("sessions", "places", after: true));
        Assert.Equal(new[] { "places", "sessions" }, workspace.Panels.Where(p => p.Dock == PanelDock.Main).Select(p => p.Id));
        Assert.Equal("Moved Sessions below Saved places.", workspace.Status);
    }

    [Fact]
    public void ANarrowWindow_StacksAColumnsLayout()
    {
        var workspace = NewColumnsWorkspace();

        workspace.Reflow(700);

        Assert.All(workspace.Panels, p => Assert.Equal((PanelDock.None, 12), (p.Dock, p.Span)));
        Assert.Equal(new[] { "places", "shelf", "sessions" }, workspace.Panels.Select(p => p.Id));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/QuickerPlaces.Tests --filter "FullyQualifiedName~WorkspaceViewModelTests"`
Expected: build errors: `IsColumns`, `Dock`, `InLeftColumn`, `SetDock`, `ColumnDrop` and `DropInColumn` don't exist.

- [ ] **Step 3: Implement**

In `src/QuickerPlaces/ViewModels/WorkspaceViewModel.cs`:

Add after `HasPanels`:

```csharp
    /// <summary>True when the active layout is in two columns (Desk layout design §2).</summary>
    public bool IsColumns => _layout.ActiveIsColumns;
```

In `Reflow`, replace `var placements = PanelLayoutEngine.Pack(_layout.Panels, _width);` with `var placements = Pack();`.

Replace `MoveEarlier` and `MoveLater` with:

```csharp
    /// <summary>Keyboard Move earlier: swaps with the shown panel before it, or above it in its column.</summary>
    public bool MoveEarlier(string panelId)
    {
        if (IsColumns)
            return _layout.MoveEarlier(panelId) && MovedInColumn(panelId, "up");

        var index = IndexOf(panelId);
        if (index <= 0 || !_layout.MoveEarlier(panelId))
            return false;

        Moved(panelId, $"before {_panels[index - 1].Title}");
        return true;
    }

    /// <summary>Keyboard Move later: swaps with the shown panel after it, or below it in its column.</summary>
    public bool MoveLater(string panelId)
    {
        if (IsColumns)
            return _layout.MoveLater(panelId) && MovedInColumn(panelId, "down");

        var index = IndexOf(panelId);
        if (index < 0 || index >= _panels.Count - 1 || !_layout.MoveLater(panelId))
            return false;

        Moved(panelId, $"after {_panels[index + 1].Title}");
        return true;
    }
```

After `SetSpan`, add:

```csharp
    /// <summary>Columns layouts' Column choice: to the bottom of the other column.</summary>
    public bool SetDock(string panelId, string dock)
    {
        var panel = _panels.FirstOrDefault(p => p.Id == panelId);
        if (panel is null || !_layout.SetDock(panelId, dock))
            return false;

        RebuildPanels();
        Status = $"Moved {panel.Title} to the {PanelDocks.DisplayName(dock)}.";
        return true;
    }

    /// <summary>Where dragging <paramref name="draggedId"/> above or below <paramref name="targetId"/> would put it in a columns layout, or null for nowhere new.</summary>
    public ColumnDropTarget? ColumnDrop(string draggedId, string targetId, bool after)
        => PanelLayoutEngine.ColumnDrop(_layout.VisiblePanels, draggedId, targetId, after);

    /// <summary>A drop in a columns layout, as one move.</summary>
    public bool DropInColumn(string draggedId, string targetId, bool after)
    {
        if (ColumnDrop(draggedId, targetId, after) is not { } drop || !_layout.MoveTo(draggedId, drop.Dock, drop.BeforePanelId))
            return false;

        var target = _panels.First(p => p.Id == targetId).Title;
        Moved(draggedId, after ? $"below {target}" : $"above {target}");
        return true;
    }
```

After `Moved(...)`, add:

```csharp
    private bool MovedInColumn(string panelId, string direction)
    {
        var title = _panels.First(p => p.Id == panelId).Title;
        RebuildPanels();
        Status = $"Moved {title} {direction}.";
        return true;
    }

    private IReadOnlyList<PanelPlacement> Pack()
        => IsColumns ? PanelLayoutEngine.PackColumns(_layout.Panels, _width) : PanelLayoutEngine.Pack(_layout.Panels, _width);

    /// <summary>A panel of a columns layout, whose arrows move it within its own column.</summary>
    private static WorkspacePanelViewModel ColumnPanel(PanelPlacement placement, IReadOnlyList<PanelInstance> visible)
    {
        var dock = PanelDocks.Normalize(visible.First(v => v.Id == placement.PanelId).Dock);
        var column = visible.Where(v => PanelDocks.Normalize(v.Dock) == dock).Select(v => v.Id).ToList();
        var index = column.IndexOf(placement.PanelId);
        return new WorkspacePanelViewModel(placement, index > 0, index < column.Count - 1, PanelDocks.IsLeft(dock));
    }
```

Replace the first two statements of `RebuildPanels` with:

```csharp
        var placements = Pack();
        var visible = _layout.VisiblePanels;
        _panels = placements
            .Select((p, i) => IsColumns
                ? ColumnPanel(p, visible)
                : new WorkspacePanelViewModel(p, canMoveEarlier: i > 0, canMoveLater: i < placements.Count - 1))
            .ToList();
```

and add `OnPropertyChanged(nameof(IsColumns));` after `OnPropertyChanged(nameof(HasPanels));`.

In `WorkspacePanelViewModel`, replace the constructor and add two properties:

```csharp
    public WorkspacePanelViewModel(PanelPlacement placement, bool canMoveEarlier = false, bool canMoveLater = false, bool inLeftColumn = false)
    {
        Placement = placement;
        CanMoveEarlier = canMoveEarlier;
        CanMoveLater = canMoveLater;
        InLeftColumn = inLeftColumn;
    }
```

```csharp
    /// <summary>The column it is shown in; none in a rows layout, or while a narrow window stacks the columns.</summary>
    public PanelDock Dock => Placement.Dock;

    /// <summary>In a columns layout, true when its own column is the left one, even while stacked: the Column choice shows this.</summary>
    public bool InLeftColumn { get; }
```

- [ ] **Step 4: Run all the tests**

Run: `dotnet test src/QuickerPlaces.Tests`
Expected: PASS, everything.

- [ ] **Step 5: Commit**

```bash
git add src/QuickerPlaces/ViewModels/WorkspaceViewModel.cs src/QuickerPlaces.Tests/WorkspaceViewModelTests.cs
git commit -m "Show columns layouts in the workspace view model, with column moves and drops

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Two stacks on the canvas, and Arrange for columns

No unit tests: this is WPF. Verify it by building now, and on Windows in Task 7 once Desk exists.

**Files:**
- Modify: `src/QuickerPlaces/Views/Panels/PanelFrame.xaml`
- Modify: `src/QuickerPlaces/Views/Panels/PanelFrame.xaml.cs`
- Modify: `src/QuickerPlaces/Views/WorkspaceView.xaml.cs`

- [ ] **Step 1: PanelFrame XAML**

In `PanelFrame.xaml`, give the two arrow icons names:
- `<Path Style="{StaticResource Icon}" Data="{StaticResource Icon.ChevronLeft}" />` becomes `<Path x:Name="EarlierIcon" Style="{StaticResource Icon}" Data="{StaticResource Icon.ChevronLeft}" />`.
- `<Path Style="{StaticResource Icon}" Data="{StaticResource Icon.ChevronRight}" />` becomes `<Path x:Name="LaterIcon" Style="{StaticResource Icon}" Data="{StaticResource Icon.ChevronRight}" />`.

After the `WidthChoice` ComboBox, inside the same StackPanel, add:

```xml
                    <!-- Columns layouts: which column, instead of a width (Desk layout design §3). -->
                    <ComboBox x:Name="ColumnChoice" Height="26" MinWidth="120" Padding="8,0,6,0" FontSize="12" Visibility="Collapsed"
                              SelectionChanged="ColumnChoice_SelectionChanged">
                        <ComboBoxItem Content="Left column" Tag="left" />
                        <ComboBoxItem Content="Main column" Tag="main" />
                    </ComboBox>
```

In the header comment, add after "a width choice,": "or in a columns layout a column choice and up/down arrows,".

- [ ] **Step 2: PanelFrame code-behind**

In `PanelFrame.xaml.cs`, add `using System.Windows.Media;`. Change the constructor's call to `Update(panel, arranging: false, columns: false);`. Add the event after `SpanRequested`:

```csharp
    /// <summary>A columns layout's Column choice: "left" or "main".</summary>
    public event Action<PanelFrame, string>? DockRequested;
```

Replace `Update` with:

```csharp
    /// <summary>Shows the panel's current place, whether Arrange mode's controls are shown, and whether the layout is in columns.</summary>
    public void Update(WorkspacePanelViewModel panel, bool arranging, bool columns)
    {
        Panel = panel;
        var title = panel.Title;
        TitleText.Text = title.ToUpperInvariant();
        AutomationProperties.SetName(this, title);

        var arrange = arranging ? Visibility.Visible : Visibility.Collapsed;
        ArrangeControls.Visibility = arrange;
        MoveHandle.Visibility = arrange;
        ArrangeOutline.Visibility = arrange;

        // A column's width comes from the column: no edge to drag, and a column choice instead of a width.
        ResizeHandle.Visibility = arranging && !columns ? Visibility.Visible : Visibility.Collapsed;
        WidthChoice.Visibility = columns ? Visibility.Collapsed : Visibility.Visible;
        ColumnChoice.Visibility = columns ? Visibility.Visible : Visibility.Collapsed;

        HideButton.ToolTip = arranging ? $"Hide {title}" : $"Hide {title}. Undo or Add panel brings it back.";
        AutomationProperties.SetName(HideButton, $"Hide {title}");

        // An arrow that a move has just disabled would drop the keyboard focus: hand it to the other one.
        if (EarlierButton.IsKeyboardFocused && !panel.CanMoveEarlier && panel.CanMoveLater)
            LaterButton.Focus();
        else if (LaterButton.IsKeyboardFocused && !panel.CanMoveLater && panel.CanMoveEarlier)
            EarlierButton.Focus();

        EarlierIcon.Data = (Geometry)FindResource(columns ? "Icon.ArrowUp" : "Icon.ChevronLeft");
        LaterIcon.Data = (Geometry)FindResource(columns ? "Icon.ArrowDown" : "Icon.ChevronRight");
        var earlier = columns ? $"Move {title} up" : $"Move {title} earlier";
        var later = columns ? $"Move {title} down" : $"Move {title} later";
        EarlierButton.IsEnabled = panel.CanMoveEarlier;
        EarlierButton.ToolTip = earlier;
        AutomationProperties.SetName(EarlierButton, earlier);
        LaterButton.IsEnabled = panel.CanMoveLater;
        LaterButton.ToolTip = later;
        AutomationProperties.SetName(LaterButton, later);

        MoveHandle.ToolTip = $"Drag to move {title}. Esc cancels.";
        ResizeHandle.ToolTip = $"Drag to change the width of {title}. Esc cancels.";

        // Its own width: in a narrow window it may be shown wider, which the tooltip says.
        _updating = true;
        WidthChoice.SelectedItem = WidthChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, panel.StoredSpan.ToString()));
        ColumnChoice.SelectedIndex = panel.InLeftColumn ? 0 : 1;
        _updating = false;
        WidthChoice.ToolTip = panel.IsWidened
            ? $"Width of {title}. Shown {PanelSpans.DisplayName(panel.Span)} while the window is too narrow for {PanelSpans.DisplayName(panel.StoredSpan)}."
            : $"Width of {title}";
        AutomationProperties.SetName(WidthChoice, $"Width of {title}");
        ColumnChoice.ToolTip = $"Column of {title}";
        AutomationProperties.SetName(ColumnChoice, $"Column of {title}");
    }
```

After `WidthChoice_SelectionChanged`, add:

```csharp
    private void ColumnChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && ColumnChoice.SelectedItem is ComboBoxItem { Tag: string dock })
            DockRequested?.Invoke(this, dock);
    }
```

- [ ] **Step 3: WorkspaceView: the stacks**

In `WorkspaceView.xaml.cs`, add these fields after `_frames`:

```csharp
    /// <summary>A columns layout's two stacks, placed on the canvas across their columns (Desk layout design §2).</summary>
    private readonly Grid _leftStack = new();
    private readonly Grid _mainStack = new();
```

In `UpdateFrames`, change the call to `frame.Update(panel, _workspace.IsArranging, _workspace.IsColumns);`.

In `CreateFrame`, add after the `SpanRequested` line:

```csharp
        frame.DockRequested += (f, dock) => _workspace?.SetDock(f.PanelId, dock);
```

Replace `BuildCanvas` with:

```csharp
    /// <summary>
    /// Places every shown panel. A rows layout: twelve equal columns, one
    /// grid row per canvas row. A columns layout: a stack per column, each a
    /// grid across its column's span, one row per panel. A row holding only
    /// the year strip takes its own height; any other shares what is left,
    /// never below <see cref="MinRowHeight"/>. Panels are made once; a hidden
    /// one's frame is collapsed, not removed, and a frame changes grid only
    /// when it changes column.
    /// </summary>
    private void BuildCanvas()
    {
        if (_workspace is null)
            return;

        var panels = _workspace.Panels;
        var columns = panels.Any(p => p.Dock != PanelDock.None);
        PanelCanvas.RowDefinitions.Clear();
        _leftStack.RowDefinitions.Clear();
        _mainStack.RowDefinitions.Clear();

        if (columns)
        {
            PlaceStack(_leftStack, panels.Where(p => p.Dock == PanelDock.Left).ToList());
            PlaceStack(_mainStack, panels.Where(p => p.Dock == PanelDock.Main).ToList());
        }
        else
        {
            _leftStack.Visibility = Visibility.Collapsed;
            _mainStack.Visibility = Visibility.Collapsed;
            foreach (var row in panels.GroupBy(p => p.Row).OrderBy(g => g.Key))
                PanelCanvas.RowDefinitions.Add(RowFor(fitsContent: row.All(p => p.Type == PanelTypes.Activity)));
        }

        var shown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var panel in panels)
        {
            if (!_frames.TryGetValue(panel.Id, out var frame))
            {
                frame = CreateFrame(panel);
                _frames[panel.Id] = frame;
            }

            Reparent(frame, panel.Dock switch
            {
                PanelDock.Left => _leftStack,
                PanelDock.Main => _mainStack,
                _ => PanelCanvas,
            });
            Grid.SetRow(frame, panel.Row);
            Grid.SetColumn(frame, columns ? 0 : panel.Column);
            Grid.SetColumnSpan(frame, columns ? 1 : panel.Span);
            frame.Visibility = Visibility.Visible;
            shown.Add(panel.Id);
        }

        foreach (var (id, frame) in _frames)
        {
            if (shown.Contains(id))
                continue;

            // Keeps whatever the panel holds for when it comes back.
            frame.Visibility = Visibility.Collapsed;
            Grid.SetRow(frame, 0);
            Grid.SetColumn(frame, 0);
            Grid.SetColumnSpan(frame, 1);
        }

        UpdateFrames();
        if (_shelf is not null)
            _shelf.ShowsPeriod = _workspace.Panels.All(p => p.Type != PanelTypes.Activity);
        EmptyCanvasText.Visibility = _workspace.HasPanels ? Visibility.Collapsed : Visibility.Visible;
        FitCanvasHeight();
    }

    private static RowDefinition RowFor(bool fitsContent) => fitsContent
        ? new RowDefinition { Height = GridLength.Auto }
        : new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = MinRowHeight };

    /// <summary>One column of a columns layout: a grid across the column's span, one row per panel; collapsed when the column is empty.</summary>
    private void PlaceStack(Grid stack, IReadOnlyList<WorkspacePanelViewModel> panels)
    {
        if (panels.Count == 0)
        {
            stack.Visibility = Visibility.Collapsed;
            return;
        }

        if (stack.Parent is null)
            PanelCanvas.Children.Add(stack);
        Grid.SetRow(stack, 0);
        Grid.SetColumn(stack, panels[0].Column);
        Grid.SetColumnSpan(stack, panels[0].Span);
        stack.Visibility = Visibility.Visible;
        foreach (var panel in panels)
            stack.RowDefinitions.Add(RowFor(fitsContent: panel.Type == PanelTypes.Activity));
    }

    /// <summary>Moves a frame to another grid only when it must: a move within one keeps the panel loaded, with its selection and scroll (M4).</summary>
    private static void Reparent(PanelFrame frame, Grid parent)
    {
        if (ReferenceEquals(frame.Parent, parent))
            return;

        (frame.Parent as Panel)?.Children.Remove(frame);
        parent.Children.Add(frame);
    }
```

Remove the old `PanelCanvas.Children.Add(frame);` line: `Reparent` now adds the frame.

Replace `FitCanvasHeight` with:

```csharp
    /// <summary>
    /// The canvas's height: the window's, or more when the rows' least
    /// heights add up to more (in a columns layout, the taller column's), and
    /// the canvas then scrolls. Shared rows get a real height to share, so the
    /// lists in them stay virtualized.
    /// </summary>
    private void FitCanvasHeight()
    {
        var needed = Math.Max(Needed(PanelCanvas), Math.Max(Needed(_leftStack), Needed(_mainStack)));
        var height = Math.Max(CanvasScroller.ActualHeight, needed);
        if (double.IsNaN(PanelCanvas.Height) || Math.Abs(PanelCanvas.Height - height) >= 0.5)
            PanelCanvas.Height = height;
    }

    /// <summary>What a grid's rows need at least: a shared row its minimum, a row sized to the year strip its tallest frame.</summary>
    private static double Needed(Grid grid)
    {
        if (grid.Visibility != Visibility.Visible)
            return 0;

        var needed = 0.0;
        for (var row = 0; row < grid.RowDefinitions.Count; row++)
        {
            var definition = grid.RowDefinitions[row];
            if (!definition.Height.IsAuto)
            {
                needed += definition.MinHeight;
                continue;
            }

            needed += grid.Children.OfType<PanelFrame>()
                .Where(f => f.Visibility == Visibility.Visible && Grid.GetRow(f) == row)
                .Select(f => f.DesiredSize.Height)
                .DefaultIfEmpty(0)
                .Max();
        }

        return needed;
    }
```

- [ ] **Step 4: WorkspaceView: dragging in columns**

In `MoveDragMoved`, replace everything from `var bounds = nearest.Bounds;` through the `_dropAfter = after;` line with:

```csharp
        var bounds = nearest.Bounds;
        var columns = _workspace.IsColumns;

        // In a column, or beside a full-width panel, a drop goes above or below; otherwise left or right.
        var vertical = columns || nearest.Frame.Panel.Span == PanelSpans.Columns;
        var after = vertical ? pointer.Y > bounds.Top + bounds.Height / 2 : pointer.X > bounds.Left + bounds.Width / 2;
        var possible = columns
            ? _workspace.ColumnDrop(frame.PanelId, nearest.Frame.PanelId, after) is not null
            : PanelLayoutEngine.Drop(_workspace.Panels.Select(p => p.Placement).ToList(), frame.PanelId, nearest.Frame.PanelId, after) is not null;
        if (!possible)
        {
            HideDropMarker();
            return;
        }

        _dropTargetId = nearest.Frame.PanelId;
        _dropAfter = after;
```

Then change `if (fullWidth)` to `if (vertical)` in the marker code below it, and delete the old `fullWidth` variable. Update the method's doc comment to: "…above or below a full-width panel or any panel of a columns layout, left or right of any other."

In `MoveDragEnded`, replace `else if (target is not null) _workspace?.Drop(frame.PanelId, target, _dropAfter);` with:

```csharp
        else if (target is not null && _workspace is { } workspace)
        {
            if (workspace.IsColumns)
                workspace.DropInColumn(frame.PanelId, target, _dropAfter);
            else
                workspace.Drop(frame.PanelId, target, _dropAfter);
        }
```

- [ ] **Step 5: Build**

Run: `dotnet build src/QuickerPlaces/QuickerPlaces.csproj`
Expected: Build succeeded, 0 warnings, 0 errors.

Run: `dotnet test src/QuickerPlaces.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/QuickerPlaces/Views/Panels/PanelFrame.xaml src/QuickerPlaces/Views/Panels/PanelFrame.xaml.cs src/QuickerPlaces/Views/WorkspaceView.xaml.cs
git commit -m "Show columns layouts as two stacks, with a column choice and up/down moves in Arrange

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: The Favourites panel

**Files:**
- Create: `src/QuickerPlaces/Views/Panels/FavouritesPanel.xaml`
- Create: `src/QuickerPlaces/Views/Panels/FavouritesPanel.xaml.cs`
- Modify: `src/QuickerPlaces/Models/Workspace/PanelInstance.cs` (`PanelTypes`)
- Modify: `src/QuickerPlaces/Services/Workspace/PanelLayoutEngine.cs` (`MinimumWidth`)
- Modify: `src/QuickerPlaces/ViewModels/WorkspaceViewModel.cs`
- Modify: `src/QuickerPlaces/Views/MainWindow.xaml`, `src/QuickerPlaces/Views/MainWindow.xaml.cs`
- Modify: `src/QuickerPlaces/Views/WorkspaceView.xaml.cs`
- Test: `src/QuickerPlaces.Tests/WorkspaceViewModelTests.cs`

- [ ] **Step 1: Write the failing test, and update the Add panel tests**

Append inside `WorkspaceViewModelTests`:

```csharp
    [Fact]
    public void TheFavouritesStrip_IsNeededOnlyWhileNoFavouritesPanelIsShown()
    {
        var workspace = NewWorkspace();
        Assert.False(workspace.ShowsFavouritesPanel);

        Assert.True(workspace.AddPanel(PanelTypes.Favourites));
        Assert.True(workspace.ShowsFavouritesPanel);
        Assert.Equal("Added Favourites.", workspace.Status);

        Assert.True(workspace.HidePanel(PanelTypes.Favourites));
        Assert.False(workspace.ShowsFavouritesPanel);

        workspace.Undo();
        Assert.True(workspace.ShowsFavouritesPanel);
    }
```

Update the tests that list what Add panel offers, now that Favourites is available:
- In `AddPanel_OffersSavedPlaces_AndKeepsItAtFullWidth_AtOnce`:
  - `Assert.Equal(new[] { PanelTypes.Places }, …)` becomes `Assert.Equal(new[] { PanelTypes.Places, PanelTypes.Favourites }, …)`.
  - After adding Places, `Assert.Empty(workspace.AddablePanels);` becomes `Assert.Equal(new[] { PanelTypes.Favourites }, workspace.AddablePanels.Select(p => p.Type));`.
  - `Assert.False(workspace.CanAddPanel);` becomes `Assert.True(workspace.CanAddPanel);`.
- In `Hide_TakesThePanelAway_AndAddPanelPutsItBackWhereItWas`: `new[] { PanelTypes.Shelf, PanelTypes.Places }` becomes `new[] { PanelTypes.Shelf, PanelTypes.Places, PanelTypes.Favourites }`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/QuickerPlaces.Tests --filter "FullyQualifiedName~WorkspaceViewModelTests"`
Expected: build errors: `PanelTypes.Favourites` and `ShowsFavouritesPanel` don't exist.

- [ ] **Step 3: The panel type**

In `PanelTypes` (`PanelInstance.cs`):
- Add `public const string Favourites = "favourites";` after `Places`.
- `Known` becomes `new[] { Activity, Shelf, Sessions, Places, Favourites, Collections, Searches }`.
- `Available` becomes `new[] { Activity, Shelf, Sessions, Places, Favourites }`.
- Add `Favourites => "Favourites",` to `DisplayName` after `Places => "Saved places",`.

`DefaultSpan` needs no change: its default arm is a third.

In `PanelLayoutEngine.MinimumWidth`, add `PanelTypes.Favourites => 200,` after the Places arm. Update its doc comment to add: "Favourites' cards wrap, so it reads narrow."

- [ ] **Step 4: `ShowsFavouritesPanel`**

In `WorkspaceViewModel`, after `IsColumns`, add:

```csharp
    /// <summary>
    /// True while the layout shows the Favourites panel (Desk layout design
    /// §5). The main window shows its favourites strip only while this is
    /// false, so favourites are never out of reach.
    /// </summary>
    public bool ShowsFavouritesPanel => _panels.Any(p => p.Type == PanelTypes.Favourites);
```

In `RebuildPanels`, add `OnPropertyChanged(nameof(ShowsFavouritesPanel));` after `OnPropertyChanged(nameof(IsColumns));`.

- [ ] **Step 5: Run the tests**

Run: `dotnet test src/QuickerPlaces.Tests`
Expected: PASS, everything.

- [ ] **Step 6: Create `FavouritesPanel`**

`src/QuickerPlaces/Views/Panels/FavouritesPanel.xaml`, with markup moved from `MainWindow.xaml`'s row 3 and its `BubbleContextMenu`:

```xml
<UserControl x:Class="QuickerPlaces.Views.Panels.FavouritesPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:converters="clr-namespace:QuickerPlaces.Converters">
    <!--
        Favourites (SI §6.4; Desk layout design §5): leather cards; click
        opens, drag reorders (Bubble_PreviewMouseMove starts the drag,
        FavouritesItemsControl_Drop calls MainViewModel.MoveFavourite).
        DataContext is the MainViewModel. The main window shows it as the
        strip under the header; the workspace also makes it a panel, whose
        frame has its own title (ShowsTitle = false). Ctrl+1 to Ctrl+9 are the
        window's key bindings, so they work whether or not this is shown.
    -->
    <UserControl.Resources>
        <converters:CollectionCountToVisibilityConverter x:Key="CollectionCountToVisibilityConverter" />

        <!-- SI §6.4: a favourite card's own menu offers removal directly. -->
        <ContextMenu x:Key="BubbleContextMenu">
            <MenuItem Header="Open" FontWeight="Bold"
                      Command="{Binding PlacementTarget.Tag.OpenCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
            <MenuItem Header="Copy folder or link"
                      Command="{Binding PlacementTarget.Tag.CopyResourceCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
            <MenuItem Header="Remove from favourites"
                      Command="{Binding PlacementTarget.Tag.ToggleFavouriteCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
        </ContextMenu>
    </UserControl.Resources>

    <StackPanel>
        <WrapPanel Orientation="Horizontal">
            <TextBlock x:Name="TitleText" Text="FAVOURITES" Style="{StaticResource TextBlock.Caps}" Margin="0,0,10,0" />
            <TextBlock Text="Ctrl+1 to Ctrl+9 open them from the keyboard" Style="{StaticResource TextBlock.Hint}"
                       Foreground="{DynamicResource Text.Tertiary}" VerticalAlignment="Center" />
        </WrapPanel>
        <Grid Margin="0,8,0,0">
            <TextBlock Text="Right-click a saved place and choose &quot;Toggle favourite&quot; to pin it here."
                       Foreground="{DynamicResource Text.Tertiary}" Margin="0,0,0,8" TextWrapping="Wrap"
                       Visibility="{Binding FavouritePlaces.Count, Converter={StaticResource CollectionCountToVisibilityConverter}}" />
            <ItemsControl x:Name="FavouritesItemsControl" ItemsSource="{Binding FavouritePlaces}" AutomationProperties.Name="Favourites"
                          AllowDrop="True" DragOver="FavouritesItemsControl_DragOver" Drop="FavouritesItemsControl_Drop">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <WrapPanel Orientation="Horizontal" />
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Button Style="{StaticResource Button.Favourite}"
                                ToolTip="{Binding ToolTipText}"
                                Tag="{Binding RelativeSource={RelativeSource AncestorType=ItemsControl}, Path=DataContext}"
                                ContextMenu="{StaticResource BubbleContextMenu}"
                                Command="{Binding RelativeSource={RelativeSource AncestorType=ItemsControl}, Path=DataContext.OpenCommand}"
                                CommandParameter="{Binding}"
                                AutomationProperties.Name="{Binding Alias}"
                                PreviewMouseLeftButtonDown="Bubble_PreviewMouseLeftButtonDown"
                                PreviewMouseMove="Bubble_PreviewMouseMove">
                            <StackPanel Orientation="Horizontal">
                                <Border>
                                    <Border.Style>
                                        <Style TargetType="Border" BasedOn="{StaticResource Border.FavouriteNumber}">
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding FavouriteShortcut}" Value="{x:Null}">
                                                    <Setter Property="Visibility" Value="Collapsed" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Border.Style>
                                    <TextBlock Text="{Binding FavouriteShortcut}" FontSize="12" FontWeight="Bold"
                                               Foreground="{DynamicResource On.Leather.Badge}"
                                               HorizontalAlignment="Center" VerticalAlignment="Center" />
                                </Border>
                                <Path Style="{StaticResource Icon.PlaceType}" />
                                <TextBlock Text="{Binding Alias}" Margin="7,0,0,0" VerticalAlignment="Center" />
                            </StackPanel>
                        </Button>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </Grid>
    </StackPanel>
</UserControl>
```

The `ItemsPanel` and `ItemTemplate` are the ones in `MainWindow.xaml` today, moved unchanged.

`src/QuickerPlaces/Views/Panels/FavouritesPanel.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

/// <summary>
/// The favourites (SI §6.4; Desk layout design §5): the main window's strip,
/// and in the workspace a panel. DataContext is the <see cref="MainViewModel"/>.
/// </summary>
public partial class FavouritesPanel : UserControl
{
    private Point _bubbleDragStartPoint;

    public FavouritesPanel() => InitializeComponent();

    /// <summary>False in a workspace panel, whose frame already shows the title.</summary>
    public bool ShowsTitle
    {
        get => TitleText.Visibility == Visibility.Visible;
        set => TitleText.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    // -----------------------------------------------------------------
    // Favourite bubble drag-to-reorder (SI §6.4). A Button already
    // consumes the mouse for its own Click, so reordering is driven from
    // Preview* events: PreviewMouseLeftButtonDown records where the drag
    // could start, PreviewMouseMove checks whether the pointer has moved
    // past the OS drag threshold and — only then — starts a WPF drag/drop
    // operation. A plain click (no meaningful movement) never reaches
    // DoDragDrop, so it still fires the Button's own Click/Open normally.
    // -----------------------------------------------------------------

    private void Bubble_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _bubbleDragStartPoint = e.GetPosition(null);

    private void Bubble_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        if (sender is not Button { DataContext: PlaceViewModel place } button)
            return;

        var current = e.GetPosition(null);
        var movedX = System.Math.Abs(current.X - _bubbleDragStartPoint.X);
        var movedY = System.Math.Abs(current.Y - _bubbleDragStartPoint.Y);

        if (movedX < SystemParameters.MinimumHorizontalDragDistance &&
            movedY < SystemParameters.MinimumVerticalDragDistance)
            return;

        DragDrop.DoDragDrop(button, new DataObject(typeof(PlaceViewModel), place), DragDropEffects.Move);
    }

    private void FavouritesItemsControl_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(PlaceViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void FavouritesItemsControl_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PlaceViewModel)) is not PlaceViewModel dragged || DataContext is not MainViewModel viewModel)
            return;

        var targetPlace = FindPlaceUnderPoint(e.GetPosition(FavouritesItemsControl));

        // Dropped back onto itself (a short wobble rather than a real
        // move): leave it where it was. Only a drop on empty space — past
        // the last bubble, or in a gap — means "move to the end".
        if (ReferenceEquals(targetPlace, dragged))
            return;

        var items = viewModel.FavouritePlaces;
        viewModel.MoveFavourite(dragged, targetPlace is not null ? items.IndexOf(targetPlace) : items.Count - 1);
    }

    /// <summary>Which bubble, if any, is at <paramref name="point"/> inside the favourites.</summary>
    private PlaceViewModel? FindPlaceUnderPoint(Point point)
    {
        var hit = VisualTreeHelper.HitTest(FavouritesItemsControl, point)?.VisualHit;
        while (hit is not null)
        {
            if (hit is FrameworkElement { DataContext: PlaceViewModel place })
                return place;
            hit = VisualTreeHelper.GetParent(hit);
        }

        return null;
    }
}
```

- [ ] **Step 7: Use it in the main window**

In `MainWindow.xaml`:
- Add `xmlns:panels="clr-namespace:QuickerPlaces.Views.Panels"` to the `Window` element.
- Delete the `BubbleContextMenu` resource.
- Replace the whole favourites `<StackPanel Grid.Row="3" Margin="0,0,0,10">…</StackPanel>` and the comment above it with:

```xml
        <!--
            Favourites (SI §6.4): the strip under the header. In the workspace
            it shows only while the layout has no Favourites panel (Desk
            layout design §5).
        -->
        <panels:FavouritesPanel x:Name="FavouritesStrip" Grid.Row="3" Margin="0,0,0,10" />
```

In `MainWindow.xaml.cs`:
- Delete the `_bubbleDragStartPoint` field, the drag-to-reorder comment block, and the four handlers `Bubble_PreviewMouseLeftButtonDown`, `Bubble_PreviewMouseMove`, `FavouritesItemsControl_DragOver`, `FavouritesItemsControl_Drop` and `FindPlaceUnderPoint`.
- In the constructor's workspace branch, replace the two lines that make and attach the workspace view model with:

```csharp
        var workspace = new WorkspaceViewModel(workspaceLayout, library);
        _workspaceView.Attach(workspace, viewModel, sessionStore,
            new WindowsOpenDocumentProbe(recentItems), recentFilesHost, activityHost);

        // Desk shows favourites as a panel; the strip is for layouts that don't (Desk layout design §5).
        void ShowFavouritesStrip() => FavouritesStrip.Visibility = workspace.ShowsFavouritesPanel ? Visibility.Collapsed : Visibility.Visible;
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.ShowsFavouritesPanel))
                ShowFavouritesStrip();
        };
        ShowFavouritesStrip();
```

- [ ] **Step 8: Make it in the workspace**

In `WorkspaceView.CreateContent`, add before `default:`:

```csharp
            case PanelTypes.Favourites:
                return new FavouritesPanel { DataContext = _places, ShowsTitle = false };
```

- [ ] **Step 9: Build and test**

Run: `dotnet build src/QuickerPlaces/QuickerPlaces.csproj`
Expected: Build succeeded, 0 warnings.

Run: `dotnet test src/QuickerPlaces.Tests`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add src/QuickerPlaces/Views/Panels/FavouritesPanel.xaml src/QuickerPlaces/Views/Panels/FavouritesPanel.xaml.cs src/QuickerPlaces/Models/Workspace/PanelInstance.cs src/QuickerPlaces/Services/Workspace/PanelLayoutEngine.cs src/QuickerPlaces/ViewModels/WorkspaceViewModel.cs src/QuickerPlaces/Views/MainWindow.xaml src/QuickerPlaces/Views/MainWindow.xaml.cs src/QuickerPlaces/Views/WorkspaceView.xaml.cs src/QuickerPlaces.Tests/WorkspaceViewModelTests.cs
git commit -m "Make favourites a panel, and show the strip only while no Favourites panel is

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: The Desk built-in

**Files:**
- Modify: `src/QuickerPlaces/Services/Workspace/BuiltInLayouts.cs`
- Test: `src/QuickerPlaces.Tests/WorkspaceLayoutServiceTests.cs`, `src/QuickerPlaces.Tests/WorkspaceViewModelTests.cs`

- [ ] **Step 1: Write the failing tests, and update the offered lists**

In `WorkspaceLayoutServiceTests.OnlyBuiltInsWhosePanelsExist_AreOffered`, change the expected ids to `new[] { BuiltInLayouts.ActivityAtlasId, BuiltInLayouts.FilesFirstId, BuiltInLayouts.DeskId }` and its comment to: "Collections and Saved searches come in M6; until then Activity Atlas, Files First and Desk are the ones with every panel they need."

In `WorkspaceViewModelTests.OnlyTheBuiltInsWithWorkingPanels_AreOffered`, expect `new[] { "Activity Atlas", "Files First", "Desk" }`.

Append to `WorkspaceLayoutServiceTests`:

```csharp
    [Fact]
    public void Desk_PutsFavouritesAndSessionsLeft_AndTheRestInTheMainColumn()
    {
        var service = NewService(NewStorage());
        Ok(service.Activate(BuiltInLayouts.DeskId));

        Assert.True(service.ActiveIsColumns);
        Assert.Equal(new[] { PanelTypes.Favourites, PanelTypes.Sessions }, Column(service, PanelDocks.Left));
        Assert.Equal(new[] { PanelTypes.Places, PanelTypes.Shelf, PanelTypes.Activity }, Column(service, PanelDocks.Main));
        Assert.False(service.IsModified);

        service.BeginArrange();
        Assert.True(service.SetDock(PanelTypes.Sessions, PanelDocks.Main));
        Ok(service.Done());
        Assert.True(service.IsModified);
        Ok(service.RestoreSaved());
        Assert.False(service.IsModified);
    }
```

Append to `WorkspaceViewModelTests`:

```csharp
    [Fact]
    public void Desk_ShowsFavouritesAsAPanel_InTheLeftColumn()
    {
        var workspace = NewWorkspace();
        workspace.SelectedLayout = workspace.Layouts.Single(l => l.Id == BuiltInLayouts.DeskId);

        Assert.True(workspace.IsColumns);
        Assert.True(workspace.ShowsFavouritesPanel);
        Assert.Equal(new[] { (PanelTypes.Favourites, PanelDock.Left, 0), (PanelTypes.Sessions, PanelDock.Left, 1) },
            workspace.Panels.Where(p => p.Dock == PanelDock.Left).Select(p => (p.Type, p.Dock, p.Row)));
        Assert.Equal(new[] { PanelTypes.Places, PanelTypes.Shelf, PanelTypes.Activity },
            workspace.Panels.Where(p => p.Dock == PanelDock.Main).Select(p => p.Type));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/QuickerPlaces.Tests --filter "FullyQualifiedName~Workspace"`
Expected: build error: `BuiltInLayouts.DeskId` doesn't exist.

- [ ] **Step 3: Add Desk**

In `BuiltInLayouts.cs`, add `public const string DeskId = "builtin.desk";` after `PersonalDeskId`. After `FilesFirst`, add:

```csharp
    /// <summary>
    /// The user's sketch (Desk layout design, 2026-09-29): favourites and
    /// sessions in a left column; saved places, Recents and the year calendar
    /// stacked in the main column.
    /// </summary>
    public static readonly BuiltInLayout Desk = new(DeskId, "Desk", 1, LayoutArrangements.Columns, new (string, int, string?)[]
    {
        (PanelTypes.Favourites, PanelSpans.Third, PanelDocks.Left),
        (PanelTypes.Sessions, PanelSpans.Third, PanelDocks.Left),
        (PanelTypes.Places, PanelSpans.Full, PanelDocks.Main),
        (PanelTypes.Shelf, PanelSpans.TwoThirds, PanelDocks.Main),
        (PanelTypes.Activity, PanelSpans.Full, PanelDocks.Main),
    });
```

Change `All` to `new[] { ActivityAtlas, FilesFirst, Desk, ProjectCanvas, PersonalDesk }`. In the class doc comment, change "that is Activity Atlas and Files First" to "that is Activity Atlas, Files First and Desk".

- [ ] **Step 4: Run all the tests**

Run: `dotnet test src/QuickerPlaces.Tests`
Expected: PASS.

- [ ] **Step 5: Try it on Windows**

Build to a scratch folder and run against test data, never against your real data:

```bash
dotnet build src/QuickerPlaces/QuickerPlaces.csproj -o <scratch>\qpbin
<scratch>\qpbin\QuickerPlaces.exe --workspace --data-root <scratch>\data-desk
```

Check with real mouse clicks. UI Automation's Expand/Invoke skips hit-testing, so it misses what a click would hit (see the memory note on real-click testing).
- Pick **Desk**. Favourites is above Sessions on the left; Saved places, File shelf and Year activity are stacked on the right; and the favourites strip under the header is gone.
- Arrange: set Sessions' Column to Main column and back; drag the shelf above Saved places; Undo; Done.
- Hide Favourites: the strip comes back. Undo: it goes.
- Narrow the window until one stack shows, main first.
- After a column change, the shelf keeps its selected row and scroll position. This is the spec's risk to verify; record what you find.

- [ ] **Step 6: Commit**

```bash
git add src/QuickerPlaces/Services/Workspace/BuiltInLayouts.cs src/QuickerPlaces.Tests/WorkspaceLayoutServiceTests.cs src/QuickerPlaces.Tests/WorkspaceViewModelTests.cs
git commit -m "Add Desk: favourites and sessions left, places, shelf and calendar in the main column

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: `TrackedFolderPaths`

**Files:**
- Create: `src/QuickerPlaces/Services/Activity/TrackedFolderPaths.cs`
- Modify: `src/QuickerPlaces/ViewModels/ActivityViewModel.cs` (`ActivityFolderRow`)
- Modify: `src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj`
- Test: `src/QuickerPlaces.Tests/TrackedFolderPathsTests.cs` (new)

- [ ] **Step 1: Write the failing test**

```csharp
using QuickerPlaces.Services.Activity;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>Where a path sits among tracked folders (Desk layout design §4): its depth below a root, and which root holds it.</summary>
public sealed class TrackedFolderPathsTests
{
    [Theory]
    [InlineData(@"C:\Jobs", @"C:\Jobs", 0)]
    [InlineData(@"C:\Jobs\", @"C:\Jobs", 0)]
    [InlineData(@"C:\Jobs", @"C:\Jobs\Acme", 1)]
    [InlineData(@"C:\Jobs", @"c:\jobs\Acme\Plans\", 2)]
    [InlineData(@"\\server\share", @"\\server\share\Acme", 1)]
    public void LevelBelow_CountsFoldersBelowTheRoot(string root, string path, int level)
        => Assert.Equal(level, TrackedFolderPaths.LevelBelow(root, path));

    [Theory]
    [InlineData(@"C:\Jobs", @"C:\JobsOld\Acme")]
    [InlineData(@"C:\Jobs", @"D:\Jobs")]
    [InlineData(@"C:\Jobs", "")]
    public void LevelBelow_IsNull_OutsideTheRoot(string root, string path)
        => Assert.Null(TrackedFolderPaths.LevelBelow(root, path));

    [Fact]
    public void RootFor_IsTheInnermostRootHoldingThePath()
    {
        var roots = new[] { @"C:\Jobs", @"C:\Jobs\Acme", @"D:\Other" };

        Assert.Equal(@"C:\Jobs\Acme", TrackedFolderPaths.RootFor(roots, @"C:\Jobs\Acme\Plans"));
        Assert.Equal(@"C:\Jobs", TrackedFolderPaths.RootFor(roots, @"C:\Jobs\Beta"));
        Assert.Null(TrackedFolderPaths.RootFor(roots, @"E:\Elsewhere"));
    }

    [Theory]
    [InlineData(0, "Root folder")]
    [InlineData(1, "Level 1 · directly below root")]
    [InlineData(3, "Level 3 · below root")]
    public void LevelLabel_IsTheRecentsWindowsWording(int level, string label)
        => Assert.Equal(label, TrackedFolderPaths.LevelLabel(level));
}
```

- [ ] **Step 2: Link the source (the test project compiles app files it names)**

In `QuickerPlaces.Tests.csproj`, after the `TrackingSignal.cs` link, add:

```xml
    <Compile Include="..\QuickerPlaces\Services\Activity\TrackedFolderPaths.cs" Link="Linked\Services\Activity\TrackedFolderPaths.cs" />
```

Run: `dotnet test src/QuickerPlaces.Tests --filter "FullyQualifiedName~TrackedFolderPathsTests"`
Expected: FAIL. The build can't find `TrackedFolderPaths.cs`.

- [ ] **Step 3: Implement**

`src/QuickerPlaces/Services/Activity/TrackedFolderPaths.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickerPlaces.Services.Activity;

/// <summary>
/// Where a path sits among Recents' tracked folders (Desk layout design §4):
/// how deep below a root it is, which root holds it, and the Recents
/// window's labels for the depth. Paths compare without case, as Windows
/// does. Pure logic; UI-free and linked into the test project.
/// </summary>
public static class TrackedFolderPaths
{
    /// <summary>The group for items no tracked folder holds.</summary>
    public const string NotTracked = "Not in a tracked folder";

    /// <summary>How many folders below <paramref name="rootPath"/> <paramref name="path"/> is: 0 for the root itself; null when it isn't inside it.</summary>
    public static int? LevelBelow(string rootPath, string path)
    {
        var root = rootPath.TrimEnd('\\', '/');
        if (root.Length == 0 || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return null;
        if (path.Length > root.Length && path[root.Length] is not ('\\' or '/'))
            return null;

        var below = path[root.Length..].Trim('\\', '/');
        return below.Length == 0 ? 0 : below.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>The innermost of <paramref name="rootPaths"/> that holds <paramref name="path"/>, or null.</summary>
    public static string? RootFor(IEnumerable<string> rootPaths, string path)
        => rootPaths
            .Where(root => LevelBelow(root, path) is not null)
            .OrderByDescending(root => root.TrimEnd('\\', '/').Length)
            .FirstOrDefault();

    /// <summary>"Root folder", "Level 1 · directly below root", "Level 2 · below root"…</summary>
    public static string LevelLabel(int level) => level switch
    {
        0 => "Root folder",
        1 => "Level 1 · directly below root",
        _ => $"Level {level} · below root",
    };
}
```

In `ActivityViewModel.cs`, replace the body of `ActivityFolderRow`'s `Level` and `LevelLabel` with:

```csharp
    public int Level => TrackedFolderPaths.LevelBelow(RootPath, Folder) ?? 0;
    public string LevelLabel => TrackedFolderPaths.LevelLabel(Level);
```

- [ ] **Step 4: Run all the tests**

Run: `dotnet test src/QuickerPlaces.Tests`
Expected: PASS. `ActivityViewModelTests` still pass, because the level logic is unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/QuickerPlaces/Services/Activity/TrackedFolderPaths.cs src/QuickerPlaces/ViewModels/ActivityViewModel.cs src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj src/QuickerPlaces.Tests/TrackedFolderPathsTests.cs
git commit -m "Share where a path sits among tracked folders between Recents and the Library

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Visits and time on Library rows, and Add as place

**Files:**
- Modify: `src/QuickerPlaces/Services/Library/LibraryIndex.cs`
- Modify: `src/QuickerPlaces/ViewModels/LibraryViewModel.cs` (`LibraryRowViewModel`)
- Test: `src/QuickerPlaces.Tests/LibraryIndexTests.cs`, `src/QuickerPlaces.Tests/LibraryViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `LibraryIndexTests`; its usings already cover these types:

```csharp
    [Fact]
    public void AFoldersTimeInRecents_IsKept_AndAFilesIsZero()
    {
        var items = LibraryIndex.Build(
            Array.Empty<Place>(),
            Array.Empty<SessionSnapshot>(),
            new[] { new FolderActivity(@"C:\Jobs\Acme", TimeSpan.FromMinutes(25), 3, DateTimeOffset.UnixEpoch) },
            new[] { new RecentFileSummary(@"C:\Jobs\Acme\Plan.pdf", DocumentKind.Pdf, 1, DateTimeOffset.UnixEpoch) });

        Assert.Equal(TimeSpan.FromMinutes(25), items.Single(i => i.Kind == LibraryKind.Folder).RecentTime);
        Assert.Equal(TimeSpan.Zero, items.Single(i => i.Kind == LibraryKind.Pdf).RecentTime);
        Assert.Equal(@"C:\Jobs\Acme", items.Single(i => i.Kind == LibraryKind.Folder).TreePath);
        Assert.Equal(@"C:\Jobs\Acme", items.Single(i => i.Kind == LibraryKind.Pdf).TreePath);
    }
```

Append to `LibraryViewModelTests`:

```csharp
    [Fact]
    public void FolderRows_ShowVisitsAndTime_AndOfferAddAsPlaceUnlessSaved()
    {
        Seed();

        var vm = NewViewModel();
        var acme = vm.Rows.Single(r => r.Name == "Acme");
        var jobs = vm.Rows.Single(r => r.Name == "Jobs");
        var pdf = vm.Rows.Single(r => r.Name == "A-101.pdf");

        Assert.Equal(("1", ActivityFormat.Duration(TimeSpan.FromSeconds(30))), (acme.VisitsText, acme.TimeText));
        Assert.Equal(("", ""), (pdf.VisitsText, pdf.TimeText));
        Assert.True(acme.CanAddAsPlace);
        Assert.False(jobs.CanAddAsPlace);
        Assert.False(pdf.CanAddAsPlace);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/QuickerPlaces.Tests --filter "FullyQualifiedName~LibraryIndexTests|FullyQualifiedName~LibraryViewModelTests"`
Expected: build errors: `RecentTime`, `TreePath`, `VisitsText`, `TimeText` and `CanAddAsPlace` don't exist.

- [ ] **Step 3: Implement the index**

In `LibraryIndex.cs`, add inside the `LibraryItem` record body, after `Folder`:

```csharp
    /// <summary>Time spent in a folder in the period (Recents); zero for anything else.</summary>
    public TimeSpan RecentTime { get; init; }

    /// <summary>What places it among tracked folders: a folder itself, or the folder a file is in; "" for a link.</summary>
    public string TreePath => Kind == LibraryKind.Folder ? Location : Folder;
```

In `Builder`, add `public TimeSpan RecentTime { get; set; }` after `RecentCount`. In `Build(...)`'s `recentFolders` loop, add `item.RecentTime += folder.Time;` after the `RecentCount` line. In `Builder.Build()`, change the return to:

```csharp
            return new LibraryItem(Kind, name, location, Place, _sessions.ToArray(), _tags.ToArray(), RecentCount, LastUsedAt)
            {
                RecentTime = RecentTime,
            };
```

- [ ] **Step 4: Implement the row**

In `LibraryRowViewModel` (`LibraryViewModel.cs`), after `LastUsedText`:

```csharp
    /// <summary>Visits in the period, for a folder Recents recorded; "" otherwise (Desk layout design §4).</summary>
    public string VisitsText => Item.Kind == LibraryKind.Folder && Item.RecentCount > 0 ? Item.RecentCount.ToString(_culture) : "";

    /// <summary>Time spent in the folder in the period (Recents); "" otherwise.</summary>
    public string TimeText => Item.Kind == LibraryKind.Folder && Item.RecentTime > TimeSpan.Zero ? ActivityFormat.Duration(Item.RecentTime) : "";

    /// <summary>A folder that isn't a saved place yet: the Recents panel offers Add as place.</summary>
    public bool CanAddAsPlace => Item.Kind == LibraryKind.Folder && !Item.IsSavedPlace;
```

In `Looks(...)`, add `&& VisitsText == other.VisitsText && TimeText == other.TimeText` before `&& ReferenceEquals(...)`.

- [ ] **Step 5: Run all the tests**

Run: `dotnet test src/QuickerPlaces.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/QuickerPlaces/Services/Library/LibraryIndex.cs src/QuickerPlaces/ViewModels/LibraryViewModel.cs src/QuickerPlaces.Tests/LibraryIndexTests.cs src/QuickerPlaces.Tests/LibraryViewModelTests.cs
git commit -m "Give Library rows a folder's visits and time, and say which can be added as places

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Scope the Library to a tracked folder

**Files:**
- Modify: `src/QuickerPlaces/Models/Workspace/WorkspaceQuery.cs`
- Modify: `src/QuickerPlaces/Services/Library/LibrarySnapshot.cs`
- Modify: `src/QuickerPlaces/Services/Library/LibraryQueryEngine.cs`
- Modify: `src/QuickerPlaces/ViewModels/LibraryViewModel.cs`
- Modify: `src/QuickerPlaces/ViewModels/SaveLayoutViewModel.cs`
- Test: `src/QuickerPlaces.Tests/LibraryQueryEngineTests.cs`, `src/QuickerPlaces.Tests/LibraryViewModelTests.cs`, `src/QuickerPlaces.Tests/WorkspaceQueryTests.cs`

- [ ] **Step 1: Write the failing tests**

In `LibraryQueryEngineTests`, change the `Root` helper's signature and return:

```csharp
    private static RecentsRootData Root(IEnumerable<(DateOnly Date, FolderActivity[] Folders)> detail,
        IEnumerable<(DateOnly Date, int Visits)>? oldTotals = null, bool enabled = true, string id = "root", string path = @"C:\Jobs")
```

```csharp
        return new RecentsRootData(id, enabled, Today.AddDays(-200), totals, days, path);
```

Then append:

```csharp
    [Fact]
    public void ARootScope_ListsOnlyWhatIsInThatTrackedFolder_AndCountsOnlyItsVisits()
    {
        var other = @"D:\Other\Site";
        var data = Snapshot(
            roots: new[]
            {
                Root(new[] { (Today, new[] { Visit(Acme, 2, Today) }) }),
                Root(new[] { (Today, new[] { Visit(other, 5, Today) }) }, id: "other", path: @"D:\Other"),
            },
            files: new[] { File(Plan, Today), File(@"D:\Other\Site\Notes.docx", Today) });

        var scoped = Run(data, new LibraryFilter(Root: new RootScope("other", @"D:\Other")));

        Assert.Equal(new[] { "Notes.docx", "Site" }, Names(scoped));
        Assert.Equal(5, scoped.Heat[Today].FolderVisits);
        Assert.Equal(1, scoped.Heat[Today].FileOpens);
    }
```

Append to `LibraryViewModelTests`:

```csharp
    [Fact]
    public void ATrackedFolderChip_ScopesTheList_AndAgainClearsIt()
    {
        Seed();
        var vm = NewViewModel();
        var root = _activity.Roots.Single();
        var changed = 0;
        vm.QueryChanged += () => changed++;

        var chip = Assert.Single(vm.TrackedRootChips);
        Assert.Equal((root.RootId, root.Path, false), (chip.RootId, chip.Path, chip.IsSelected));

        vm.ToggleRootScope(root.RootId);
        Assert.Equal(new[] { "A-101.pdf", "Acme", "Budget.xlsx", "Report.docx" }, vm.Rows.Select(r => r.Name).OrderBy(n => n));
        Assert.True(vm.TrackedRootChips.Single().IsSelected);
        Assert.Equal(root.RootId, vm.CurrentQuery.Root);
        Assert.Equal(1, changed);

        vm.ToggleRootScope(root.RootId);
        Assert.Equal(6, vm.Rows.Count);
        Assert.Null(vm.CurrentQuery.Root);
    }

    [Fact]
    public void AScopeForATrackedFolderThatIsGone_ScopesNothing()
    {
        Seed();
        var vm = NewViewModel();

        vm.ApplyQuery(new WorkspaceQuery { Root = "deleted-root" });

        Assert.Equal(6, vm.Rows.Count);
        Assert.False(vm.TrackedRootChips.Single().IsSelected);
    }
```

Add `using QuickerPlaces.Models.Workspace;` to `LibraryViewModelTests.cs`.

Append to `WorkspaceQueryTests`:

```csharp
    [Fact]
    public void ARootScope_IsPartOfTheQuery()
    {
        var scoped = new WorkspaceQuery { Root = "r1" };

        Assert.False(scoped.IsDefault);
        Assert.Equal("r1", scoped.Clone().Root);
        Assert.False(scoped.SameAs(WorkspaceQuery.Default));
        Assert.True(scoped.SameAs(new WorkspaceQuery { Root = "r1" }));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/QuickerPlaces.Tests --filter "FullyQualifiedName~Library|FullyQualifiedName~WorkspaceQueryTests"`
Expected: build errors: `Root`, `RootScope`, `TrackedRootChips`, `ToggleRootScope` and the `RecentsRootData` path argument don't exist.

- [ ] **Step 3: The query and the snapshot**

`WorkspaceQuery`:
- Add after `Tag`:

```csharp
    /// <summary>A tracked folder's root id the list is narrowed to (the Recents panel's chips, Desk layout design §4), or null.</summary>
    public string? Root { get; set; }
```

- `IsDefault`: add `&& Root is null`.
- `Clone()`: add `Root = Root,`.
- `SameAs`: add `&& Root == other.Root`.

`LibrarySnapshot.cs`:
- `RecentsRootData` gets a last parameter `string Path = ""`, documented as "The tracked folder's path, for scoping and folder levels".
- In `Capture`, pass `root.Path` as the new last argument of `new RecentsRootData(...)`.

`SaveLayoutViewModel.Describe`: after the `Tag` line, add:

```csharp
        if (!string.IsNullOrEmpty(query.Root))
            parts.Add("One tracked folder");
```

- [ ] **Step 4: The engine**

In `LibraryQueryEngine.cs`, change `LibraryFilter` to:

```csharp
public sealed record LibraryFilter(LibraryKind? Kind = null, LibrarySourceFilter Source = LibrarySourceFilter.All,
    string Text = "", string? Tag = null, RootScope? Root = null)
{
    public static LibraryFilter None { get; } = new();

    public bool OnlyKind => Source == LibrarySourceFilter.All && string.IsNullOrWhiteSpace(Text) && Tag is null && Root is null;
}

/// <summary>A tracked folder the Library is narrowed to (Desk layout design §4): its root id, and its path.</summary>
public sealed record RootScope(string RootId, string Path);
```

Keep `LibraryFilter`'s existing doc comment. In `Passes`, add before `LibraryIndex.Matches(...)`:

```csharp
           (filter.Root is null || (item.TreePath.Length > 0 && TrackedFolderPaths.LevelBelow(filter.Root.Path, item.TreePath) is not null)) &&
```

In `AddFolderHeat`, after the `if (data.Roots.Count == 0) …` check, add:

```csharp
        // A tracked folder's scope counts that root's visits alone, from its day totals when nothing else narrows them.
        var roots = filter.Root is { } scope ? data.Roots.Where(r => r.RootId == scope.RootId).ToList() : data.Roots;
```

Then, in the rest of `AddFolderHeat`, replace every remaining `data.Roots` with `roots`. That covers `starts.AddRange`, the three `foreach (var root in …)` loops and `.All(r => !r.Enabled)`.

- [ ] **Step 5: The view model**

In `LibraryViewModel`:
- Add the field `private string? _rootId;` after `_tag`.
- `CurrentQuery`: add `Root = _rootId,`.
- `ApplyQuery`: add `_rootId = string.IsNullOrWhiteSpace(query.Root) ? null : query.Root;` after the `_tag` line, and `BuildRootChips();` before `Refresh();`.
- The constructor: add `BuildRootChips();` after `_snapshot = Capture();`.
- `Reload`: add `BuildRootChips();` after `_snapshot = Capture();`.
- `Refresh`: change the filter line to:

```csharp
        var filter = new LibraryFilter(_selectedKind, _source, _searchText, _tag, ScopeFor(_rootId));
```

Add after `TagChoices`:

```csharp
    /// <summary>Recents' tracked folders, as chips that scope the list (Desk layout design §4).</summary>
    public ObservableCollection<TrackedRootChip> TrackedRootChips { get; } = new();

    public bool HasTrackedRoots => TrackedRootChips.Count > 0;

    /// <summary>The tracked folder the list is narrowed to, by root id, or null. A root that no longer exists scopes nothing.</summary>
    public string? RootScope
    {
        get => _rootId;
        set
        {
            if (!SetProperty(ref _rootId, value))
                return;
            BuildRootChips();
            QueryEdited();
        }
    }

    /// <summary>A chip's click: narrows to that tracked folder, or back to everything when it already is.</summary>
    public void ToggleRootScope(string rootId) => RootScope = _rootId == rootId ? null : rootId;
```

In the Building region, add:

```csharp
    private RootScope? ScopeFor(string? rootId)
        => rootId is not null && _snapshot.Roots.FirstOrDefault(r => r.RootId == rootId) is { } root
            ? new RootScope(root.RootId, root.Path)
            : null;

    private void BuildRootChips()
    {
        TrackedRootChips.Clear();
        foreach (var root in _snapshot.Roots)
            TrackedRootChips.Add(new TrackedRootChip(root.RootId, root.Path, root.Enabled, root.RootId == _rootId));
        OnPropertyChanged(nameof(HasTrackedRoots));
    }
```

At the end of the file add:

```csharp
/// <summary>One tracked folder as the Recents panel shows it: selected when the list is narrowed to it.</summary>
public sealed record TrackedRootChip(string RootId, string Path, bool Enabled, bool IsSelected);
```

- [ ] **Step 6: Run all the tests**

Run: `dotnet test src/QuickerPlaces.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/QuickerPlaces/Models/Workspace/WorkspaceQuery.cs src/QuickerPlaces/Services/Library/LibrarySnapshot.cs src/QuickerPlaces/Services/Library/LibraryQueryEngine.cs src/QuickerPlaces/ViewModels/LibraryViewModel.cs src/QuickerPlaces/ViewModels/SaveLayoutViewModel.cs src/QuickerPlaces.Tests/LibraryQueryEngineTests.cs src/QuickerPlaces.Tests/LibraryViewModelTests.cs src/QuickerPlaces.Tests/WorkspaceQueryTests.cs
git commit -m "Narrow the Library to one tracked folder, list and calendar alike

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Group by folder level

**Files:**
- Modify: `src/QuickerPlaces/ViewModels/LibraryViewModel.cs`
- Test: `src/QuickerPlaces.Tests/LibraryViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void GroupByFolderLevel_PutsItemsUnderTheirDepth_AndTheRestLast()
    {
        Seed();
        var vm = NewViewModel();

        vm.Grouping = LibraryGrouping.Level;

        Assert.True(vm.IsGroupedByLevel);
        Assert.Equal(new[] { TrackedFolderPaths.LevelLabel(1), TrackedFolderPaths.NotTracked }, vm.Rows.Select(r => r.GroupName).Distinct());
        Assert.Equal(new[] { "A-101.pdf", "Acme", "Budget.xlsx", "Report.docx" },
            vm.Rows.Where(r => r.GroupName == TrackedFolderPaths.LevelLabel(1)).Select(r => r.Name).OrderBy(n => n));
        Assert.Equal(new[] { "Jobs", "Wiki" }, vm.Rows.Where(r => r.GroupName == TrackedFolderPaths.NotTracked).Select(r => r.Name).OrderBy(n => n));
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/QuickerPlaces.Tests --filter "FullyQualifiedName~GroupByFolderLevel"`
Expected: build errors: `LibraryGrouping.Level` and `IsGroupedByLevel` don't exist.

- [ ] **Step 3: Implement**

In the `LibraryGrouping` enum, add:

```csharp
    /// <summary>By depth below the tracked folder that holds each item, then "Not in a tracked folder" (Desk layout design §4).</summary>
    Level,
```

In the `Grouping` setter, add `OnPropertyChanged(nameof(IsGroupedByLevel));`. After `IsGroupedByTag`, add:

```csharp
    public bool IsGroupedByLevel { get => _grouping == LibraryGrouping.Level; set { if (value) Grouping = LibraryGrouping.Level; } }
```

In `BuildRows`, replace the `if (_grouping == LibraryGrouping.Type) { … } else { … }` block with:

```csharp
        var zone = _time.LocalTimeZone;
        if (_grouping == LibraryGrouping.Type)
        {
            foreach (var item in shown.OrderBy(i => i.Kind))
                rows.Add(new LibraryRowViewModel(item, item.Kind.PluralLabel(), (int)item.Kind, zone, _culture));
        }
        else if (_grouping == LibraryGrouping.Level)
        {
            var roots = _snapshot.Roots.Select(r => r.Path).Where(p => p.Length > 0).ToList();
            var placed = shown.Select(item =>
            {
                var root = item.TreePath.Length == 0 ? null : TrackedFolderPaths.RootFor(roots, item.TreePath);
                return (Item: item, Level: root is null ? null : TrackedFolderPaths.LevelBelow(root, item.TreePath));
            }).ToList();

            // OrderBy is stable: within a level, most recently used first, as elsewhere.
            foreach (var (item, level) in placed.Where(p => p.Level is not null).OrderBy(p => p.Level))
                rows.Add(new LibraryRowViewModel(item, TrackedFolderPaths.LevelLabel(level!.Value), level.Value, zone, _culture));
            foreach (var (item, _) in placed.Where(p => p.Level is null))
                rows.Add(new LibraryRowViewModel(item, TrackedFolderPaths.NotTracked, int.MaxValue, zone, _culture));
        }
        else
        {
            var tags = shown.SelectMany(i => i.Tags).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToList();
            for (var index = 0; index < tags.Count; index++)
            {
                foreach (var item in shown.Where(i => i.Tags.Contains(tags[index], StringComparer.OrdinalIgnoreCase)))
                    rows.Add(new LibraryRowViewModel(item, tags[index], index, zone, _culture));
            }

            foreach (var item in shown.Where(i => i.Tags.Count == 0))
                rows.Add(new LibraryRowViewModel(item, "No tag", tags.Count, zone, _culture));
        }
```

Also update the class doc comment: "grouped by type or by tag" becomes "grouped by type, tag or folder level".

- [ ] **Step 4: Run all the tests**

Run: `dotnet test src/QuickerPlaces.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/QuickerPlaces/ViewModels/LibraryViewModel.cs src/QuickerPlaces.Tests/LibraryViewModelTests.cs
git commit -m "Group the Library by folder level below the tracked folders

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: The Recents panel's tracked folders, columns and actions

**Files:**
- Modify: `src/QuickerPlaces/Services/Activity/ActivityFormat.cs`
- Modify: `src/QuickerPlaces/Views/Panels/FileShelfPanel.xaml`, `src/QuickerPlaces/Views/Panels/FileShelfPanel.xaml.cs`
- Modify: `src/QuickerPlaces/Views/WorkspaceView.xaml.cs`
- Modify: `src/QuickerPlaces/Views/MainWindow.xaml.cs`
- Test: `src/QuickerPlaces.Tests/ActivityFormatTests.cs` (new)

- [ ] **Step 1: Write the failing test for the tracking line**

```csharp
using QuickerPlaces.Services.Activity;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>The Recents panel's tracking line and the header's tooltip (Desk layout design §4, §6).</summary>
public sealed class ActivityFormatTests
{
    [Theory]
    [InlineData(2, 2, false, "Tracking 2 folders")]
    [InlineData(1, 3, false, "Tracking 1 folder")]
    [InlineData(2, 2, true, "Tracking paused")]
    [InlineData(0, 2, false, "Tracking is off for every folder")]
    [InlineData(0, 0, false, "No folders tracked")]
    public void TrackingSummary_SaysWhatIsTracked(int enabled, int all, bool paused, string expected)
        => Assert.Equal(expected, ActivityFormat.TrackingSummary(enabled, all, paused));
}
```

Run: `dotnet test src/QuickerPlaces.Tests --filter "FullyQualifiedName~ActivityFormatTests"`
Expected: build error: `TrackingSummary` doesn't exist.

- [ ] **Step 2: Implement `TrackingSummary` and use it in the header**

Add to `ActivityFormat`:

```csharp
    /// <summary>
    /// What Recents is tracking, in a line: the Recents panel shows it as is,
    /// and the header's tooltip after "Recents — ". Paused is the tracking
    /// host's own pause, while folders are tracked.
    /// </summary>
    public static string TrackingSummary(int enabledRoots, int allRoots, bool paused)
        => enabledRoots > 0 && paused ? "Tracking paused"
            : enabledRoots > 0 ? $"Tracking {enabledRoots} {(enabledRoots == 1 ? "folder" : "folders")}"
            : allRoots > 0 ? "Tracking is off for every folder"
            : "No folders tracked";
```

In `MainWindow.UpdateActivityIndicator`, replace the `var text = …;` statement with:

```csharp
        var summary = ActivityFormat.TrackingSummary(count, _activityStore.Roots.Count, _activityHost.IsPaused);
        var text = $"Recents — {char.ToLowerInvariant(summary[0])}{summary[1..]}";
```

`MainWindow.xaml.cs` already has `using QuickerPlaces.Services.Activity;`.

Run: `dotnet test src/QuickerPlaces.Tests`
Expected: PASS.

- [ ] **Step 3: FileShelfPanel XAML**

In `FileShelfPanel.xaml`, make the first child of the row-0 `StackPanel` (before `PeriodArea`) the tracked-folders strip:

```xml
                <!--
                    The Recents panel's tracked folders (ShowsTracking; Desk layout
                    design §4): a chip scopes the list to its folder, its menu
                    manages tracking, and Track a folder adds one. The chips are
                    the Library's (TrackedRootChips); the actions go to the
                    ActivityViewModel the workspace attaches.
                -->
                <StackPanel x:Name="TrackingArea" Visibility="Collapsed" Margin="0,0,0,10">
                    <DockPanel LastChildFill="True">
                        <Button DockPanel.Dock="Right" Click="TrackFolder_Click" Style="{StaticResource Button.Compact}"
                                ToolTip="Add a folder to track" AutomationProperties.Name="Track a folder" Margin="10,0,0,0">
                            <StackPanel Orientation="Horizontal">
                                <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Plus}" />
                                <TextBlock Text="Track a folder" Margin="7,0,0,0" VerticalAlignment="Center" />
                            </StackPanel>
                        </Button>
                        <TextBlock Text="TRACKED FOLDERS" Style="{StaticResource TextBlock.Caps}" Margin="0,0,12,0" VerticalAlignment="Center" />
                        <TextBlock x:Name="TrackingSummaryText" Foreground="{DynamicResource Text.Secondary}" VerticalAlignment="Center"
                                   TextTrimming="CharacterEllipsis" AutomationProperties.LiveSetting="Polite" />
                    </DockPanel>
                    <ItemsControl ItemsSource="{Binding TrackedRootChips}" Margin="0,8,0,0" AutomationProperties.Name="Tracked folders">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate><WrapPanel Orientation="Horizontal" /></ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Button Style="{StaticResource Button.KindChip}" Click="RootChip_Click" ContextMenuOpening="RootChip_ContextMenuOpening"
                                        ToolTip="{Binding Path}" AutomationProperties.Name="{Binding Path}"
                                        AutomationProperties.HelpText="Shows only what is in this folder; again shows everything. Right-click to manage tracking.">
                                    <Button.ContextMenu>
                                        <ContextMenu>
                                            <MenuItem Header="Edit tracking settings…" Tag="Manage" Click="EditRoot_Click" />
                                            <MenuItem Header="About folder…" Click="AboutRoot_Click" />
                                            <Separator />
                                            <MenuItem Header="Stop tracking" Tag="Toggle" Click="ToggleRoot_Click" />
                                            <Separator />
                                            <MenuItem Header="Delete tracked folder…" Tag="Manage" Click="DeleteRoot_Click" />
                                        </ContextMenu>
                                    </Button.ContextMenu>
                                    <StackPanel Orientation="Horizontal">
                                        <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Folder}" Margin="0,0,7,0" />
                                        <TextBlock Text="{Binding Path}" FontFamily="{StaticResource Font.Mono}" FontSize="12.5"
                                                   TextTrimming="CharacterEllipsis" MaxWidth="260" VerticalAlignment="Center" />
                                        <TextBlock Text="Paused" FontSize="12" Margin="8,0,0,0" VerticalAlignment="Center"
                                                   Foreground="{DynamicResource Text.Secondary}">
                                            <TextBlock.Style>
                                                <Style TargetType="TextBlock">
                                                    <Setter Property="Visibility" Value="Collapsed" />
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding Enabled}" Value="False">
                                                            <Setter Property="Visibility" Value="Visible" />
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </TextBlock.Style>
                                        </TextBlock>
                                    </StackPanel>
                                </Button>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <!-- Recents' own save problem, with Retry, as its window shows it. DataContext: the ActivityViewModel. -->
                    <DockPanel x:Name="TrackingProblem" Margin="0,6,0,0" Visibility="Collapsed">
                        <Button DockPanel.Dock="Right" Content="Retry save" Click="RetryTrackingSave_Click" Style="{StaticResource Button.Compact}" Margin="12,0,0,0" />
                        <TextBlock Text="{Binding ErrorMessage}" Foreground="{DynamicResource Danger}" TextWrapping="Wrap" VerticalAlignment="Center" />
                    </DockPanel>
                </StackPanel>
```

In the Group by segment:
- The Tag radio becomes the middle one. Replace `Tag="0,5,5,0" BorderThickness="0"` on it with `Tag="0"`.
- Add after it:

```xml
                                <RadioButton Content="Folder level" GroupName="Grouping" IsChecked="{Binding IsGroupedByLevel}"
                                             Style="{StaticResource RadioButton.Segment}" Tag="0,5,5,0" BorderThickness="0"
                                             ToolTip="By depth below the tracked folder each item is in; the rest last" />
```

In the row context menu, after `Copy path or link`, add:

```xml
                        <MenuItem x:Name="AddAsPlaceMenuItem" Header="Add as place…" Click="AddAsPlace_Click" Visibility="Collapsed" />
```

In `DataGrid.Columns`, after `WHERE FROM`, add:

```xml
                    <DataGridTextColumn Header="VISITS" Binding="{Binding VisitsText}" Width="60" ElementStyle="{StaticResource Cell.Secondary}" />
                    <DataGridTextColumn Header="TIME" Binding="{Binding TimeText}" Width="70" ElementStyle="{StaticResource Cell.Secondary}" />
```

In the header comment, add: "In the workspace it is the Recents panel (Desk layout design §4): the tracked folders strip, Add as place, and the Recents window's actions."

- [ ] **Step 4: FileShelfPanel code-behind**

Add usings: `using System.Linq;`, `using Microsoft.Win32;`, `using QuickerPlaces.Services.Activity;`. `MessageForm`, `AddRootDialog` and `ActivityFolderSettingsDialog` are in the parent namespace `QuickerPlaces.Views`, so they resolve without one. Add inside the class:

```csharp
    private ActivityViewModel? _activity;
    private INetworkDriveResolver? _networkDrives;
    private Func<string>? _trackingSummary;

    /// <summary>Raised by Add as place with the folder; the workspace has the places and the Add folder dialog.</summary>
    public event Action<string>? AddAsPlaceRequested;

    /// <summary>True in the workspace's Recents panel, once <see cref="AttachTracking"/> ran.</summary>
    public bool ShowsTracking => TrackingArea.Visibility == Visibility.Visible;

    /// <summary>
    /// Makes this the Recents panel (Desk layout design §4): the tracked
    /// folders strip, whose actions go to <paramref name="activity"/>, and the
    /// line <paramref name="trackingSummary"/> writes.
    /// </summary>
    public void AttachTracking(ActivityViewModel activity, INetworkDriveResolver networkDrives, Func<string> trackingSummary)
    {
        _activity = activity;
        _networkDrives = networkDrives;
        _trackingSummary = trackingSummary;
        TrackingProblem.DataContext = activity;
        TrackingProblem.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(ActivityViewModel.HasError))
        {
            Converter = new BooleanToVisibilityConverter(),
        });
        TrackingArea.Visibility = Visibility.Visible;
        UpdateTracking();
    }

    /// <summary>Rewrites the tracking line: after a change to the tracked folders, or to tracking's pause.</summary>
    public void UpdateTracking()
    {
        if (_trackingSummary is not null)
            TrackingSummaryText.Text = _trackingSummary();
    }

    private void TrackFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_activity is null || _networkDrives is null || Window.GetWindow(this) is not { } owner)
            return;

        var picker = new OpenFolderDialog { Title = "Choose a folder to track" };
        if (picker.ShowDialog(owner) != true)
            return;
        var equivalents = AddRootDialog.Show(owner, picker.FolderName, _networkDrives);
        if (equivalents is not null)
            _activity.AddRoot(picker.FolderName, equivalents);
    }

    private void RootChip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TrackedRootChip chip)
            ViewModel?.ToggleRootScope(chip.RootId);
    }

    /// <summary>Points the Recents actions at the chip right-clicked, and names Stop or Resume tracking.</summary>
    private void RootChip_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_activity is null || sender is not Button { DataContext: TrackedRootChip chip, ContextMenu: { } menu })
        {
            e.Handled = true;
            return;
        }

        _activity.SelectedRoot = _activity.Roots.FirstOrDefault(r => r.RootId == chip.RootId);
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (Equals(item.Tag, "Toggle"))
                item.Header = chip.Enabled ? "Stop tracking" : "Resume tracking";
            item.IsEnabled = item.Tag is null || _activity.CanManage;
        }
    }

    private void EditRoot_Click(object sender, RoutedEventArgs e)
    {
        if (_activity?.SelectedRoot is not null && Window.GetWindow(this) is { } owner)
            new ActivityFolderSettingsDialog(owner, _activity).ShowDialog();
    }

    private void AboutRoot_Click(object sender, RoutedEventArgs e)
    {
        if (_activity?.SelectedRoot is not null)
            MessageForm.Show(_activity.AboutSelectedFolderText, "About tracked folder", owner: Window.GetWindow(this));
    }

    private void ToggleRoot_Click(object sender, RoutedEventArgs e) => _activity?.ToggleSelected();

    private void DeleteRoot_Click(object sender, RoutedEventArgs e)
    {
        if (_activity?.SelectedRoot is not { } selected)
            return;

        var message = $"Delete \"{selected.Path}\" and all of its recorded activity? This cannot be undone. To keep the data, use Stop tracking instead.";
        if (MessageForm.ShowDestructiveConfirm(message, "Delete tracked folder", "Delete folder and its data", Window.GetWindow(this)))
            _activity.DeleteSelected();
    }

    private void RetryTrackingSave_Click(object sender, RoutedEventArgs e) => _activity?.RetrySave();

    private void AddAsPlace_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is { CanAddAsPlace: true } row)
            AddAsPlaceRequested?.Invoke(row.Location);
    }
```

In `RowsGrid_ContextMenuOpening`, after the `ForgetMenuItem` line, add:

```csharp
        AddAsPlaceMenuItem.Visibility = ShowsTracking && SelectedRow.CanAddAsPlace ? Visibility.Visible : Visibility.Collapsed;
```

Update the class doc comment to: "…Hosted by the Library window and by the workspace (M3), where it is the Recents panel (Desk layout design §4)."

- [ ] **Step 5: Wire it in the workspace**

In `WorkspaceView.xaml.cs`:
- Add usings `using QuickerPlaces.Services.Activity;` (already present) and `QuickerPlaces.ViewModels` (present).
- Add fields after `_sessionsPanel`:

```csharp
    private ActivityStore? _activityStore;
    private ActivityTrackingHost? _activityHost;
    private INetworkDriveResolver? _networkDrives;
    private Action? _trackingChanged;
```

Change `Attach`'s signature and its first lines:

```csharp
    /// <summary>Connects the view to the workspace, the places and the stores the panels need. Call once, before it is shown.</summary>
    public void Attach(WorkspaceViewModel workspace, MainViewModel places, SessionStore sessions, WindowsOpenDocumentProbe probe,
        RecentFilesHost recentFilesHost, ActivityTrackingHost activityHost, ActivityStore activityStore,
        INetworkDriveResolver networkDrives, Action trackingChanged)
    {
        _workspace = workspace;
        _places = places;
        _sessions = sessions;
        _probe = probe;
        _recentFilesHost = recentFilesHost;
        _activityHost = activityHost;
        _activityStore = activityStore;
        _networkDrives = networkDrives;
        _trackingChanged = trackingChanged;
```

The rest of the method is unchanged. Replace the `PanelTypes.Shelf` case in `CreateContent` with:

```csharp
            case PanelTypes.Shelf:
                // The Recents panel (Desk layout design §4): the shelf, with Recents' tracked folders and actions.
                _shelf = new FileShelfPanel { DataContext = _workspace!.Library, ShowsSearch = false, ShowsSaveAsSession = true };
                _shelf.SaveAsSessionRequested += SaveShelfAsSession;
                _shelf.AddAsPlaceRequested += AddAsPlace;
                var activity = new ActivityViewModel(_activityStore!, () =>
                {
                    _activityHost!.RootsChanged();
                    _trackingChanged?.Invoke();
                    _shelf?.UpdateTracking();
                    RequestReload();
                });
                _shelf.AttachTracking(activity, _networkDrives!, () => ActivityFormat.TrackingSummary(
                    _activityStore!.EnabledRoots().Count, _activityStore.Roots.Count, _activityHost!.IsPaused));
                return _shelf;
```

After `SaveShelfAsSession`, add:

```csharp
    /// <summary>The Recents panel's Add as place: the usual Add folder dialog for that folder, then the Library read again.</summary>
    private void AddAsPlace(string folder)
    {
        if (_places is null || Window.GetWindow(this) is not { } owner)
            return;

        _places.AddFolderFromActivity(folder, owner);
        RequestReload();
    }
```

In `MainWindow.xaml.cs`, pass the new arguments to `Attach`:

```csharp
        _workspaceView.Attach(workspace, viewModel, sessionStore,
            new WindowsOpenDocumentProbe(recentItems), recentFilesHost, activityHost,
            activityStore, new NetworkDriveResolver(), UpdateActivityIndicator);
```

- [ ] **Step 6: Build and test**

Run: `dotnet build src/QuickerPlaces/QuickerPlaces.csproj`
Expected: Build succeeded, 0 warnings.

Run: `dotnet test src/QuickerPlaces.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/QuickerPlaces/Services/Activity/ActivityFormat.cs src/QuickerPlaces/Views/Panels/FileShelfPanel.xaml src/QuickerPlaces/Views/Panels/FileShelfPanel.xaml.cs src/QuickerPlaces/Views/WorkspaceView.xaml.cs src/QuickerPlaces/Views/MainWindow.xaml.cs src/QuickerPlaces.Tests/ActivityFormatTests.cs
git commit -m "Give the workspace's shelf Recents' tracked folders, visits, folder levels and Add as place

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 13: Call the panel Recents

**Files:**
- Modify: `src/QuickerPlaces/Models/Workspace/PanelInstance.cs`
- Modify: `src/QuickerPlaces/Views/WorkspaceView.xaml`
- Test: `src/QuickerPlaces.Tests/WorkspaceLayoutServiceTests.cs`, `src/QuickerPlaces.Tests/WorkspaceViewModelTests.cs`

- [ ] **Step 1: Update the tests to the new name (they fail first)**

- `WorkspaceLayoutServiceTests`: `"Resize File shelf"` becomes `"Resize Recents"`.
- `WorkspaceViewModelTests`:
  - `"Moved Sessions before File shelf."` becomes `"Moved Sessions before Recents."`.
  - `"Undo Resize File shelf"` becomes `"Undo Resize Recents"`.
  - `"File shelf is now half wide; shown full width until the window is wider."` becomes `"Recents is now half wide; shown full width until the window is wider."`.

Find any others with `grep -rn "File shelf" src/QuickerPlaces.Tests --include=*.cs` and update assertions on user-visible text only, not comments.

Run: `dotnet test src/QuickerPlaces.Tests --filter "FullyQualifiedName~Workspace"`
Expected: FAIL on those assertions.

- [ ] **Step 2: Rename**

In `PanelTypes.DisplayName`, `Shelf => "File shelf",` becomes `Shelf => "Recents",`. Add to its doc comment: "The shelf is Recents in the workspace (Desk layout design §4); its stored type stays shelf."

In `WorkspaceView.xaml`, the search box's tooltip `Down moves into the File shelf` becomes `Down moves into Recents`. Run `grep -rn "File shelf" src/QuickerPlaces/Views/WorkspaceView.xaml*` and update any other workspace wording the user sees.

- [ ] **Step 3: Run all the tests**

Run: `dotnet test src/QuickerPlaces.Tests`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/QuickerPlaces/Models/Workspace/PanelInstance.cs src/QuickerPlaces/Views/WorkspaceView.xaml src/QuickerPlaces.Tests
git commit -m "Call the workspace's File shelf Recents

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 14: Hide the header's Recents button in the workspace, and document

**Files:**
- Modify: `src/QuickerPlaces/Views/MainWindow.xaml.cs`
- Modify: `ai/BUILD_SUMMARY.md`, `ai/260929_Desk Layout and Recents Panel Design.md`

- [ ] **Step 1: Hide the button**

In the `MainWindow` constructor's workspace branch, after `SessionsButton.Visibility = Visibility.Collapsed;`, add:

```csharp
        // The Recents panel does what the Recents window did (Desk layout design §6); the tray still shows tracking.
        ActivityButton.Visibility = Visibility.Collapsed;
```

Run: `dotnet build src/QuickerPlaces/QuickerPlaces.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 2: Document**

In `ai/BUILD_SUMMARY.md`, add a section `## Desk layout, Favourites and Recents panels (2026-09-29)` before `## Status snapshot — 2026-09-28`. Use the M5 section's shape: *What was built* (one bullet per task group: columns layouts, Desk, Favourites panel, Recents panel, header), *Decisions made while building* (a table), and *Verification status* (test count, then the Windows checks from Task 15 as done or not done, with results).

Record these decisions:
- Arrangement lives on the layout, not on working arrangements.
- An empty column gives its width away, so the Column choice is how a panel reaches it.
- The strip shows while no Favourites panel does.
- The stored type stays `shelf`.
- The header's Recents tooltip now says "tracking is off for every folder" where it said "paused".

In the design spec, change `Status:` to "Built (see ai/BUILD_SUMMARY.md)".

- [ ] **Step 3: Commit**

```bash
git add src/QuickerPlaces/Views/MainWindow.xaml.cs ai/BUILD_SUMMARY.md "ai/260929_Desk Layout and Recents Panel Design.md"
git commit -m "Hide the Recents button in the workspace, and document the Desk layout

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 15: Verify on Windows

**Status (2026-09-29):** closed. The checks run while building are recorded in `BUILD_SUMMARY.md`. The rest move to the File viewer's Windows checks.

Not code. Record every result in `BUILD_SUMMARY.md`'s *Verification status* and commit that.

Build to a scratch folder and run against a scratch data root. Never run against the real data, and never call `app.Run()` from a probe.

```bash
dotnet build src/QuickerPlaces/QuickerPlaces.csproj -c Debug -o <scratch>\qpbin
<scratch>\qpbin\QuickerPlaces.exe --workspace --data-root <scratch>\data-verify
```

Use real mouse clicks (UI Automation to find elements, `SetCursorPos` plus `mouse_event` to click) and screenshots.

- [ ] Pick **Desk**:
  - Favourites then Sessions on the left; Saved places, Recents and Year activity on the right.
  - No favourites strip at the top.
  - No Recents button in the header.
- [ ] **Favourites panel:**
  - Add a place and favourite it: its card appears in the panel.
  - Click a card to open it.
  - Drag one card onto another to reorder them.
  - Ctrl+1 opens the first favourite.
  - Hide the panel and the strip returns; Undo and it goes.
- [ ] **Arrange:**
  - The Column choice opens on click and moves Sessions to the main column and back.
  - Up/down arrows move within a column.
  - Drag Recents above Saved places.
  - Undo, Revert, Done.
  - A column move keeps Recents' selected row and scroll position (the spec's risk).
- [ ] **Save as new** from Desk, then restart: it resumes in columns.
- [ ] **Narrow window:** one stack, main first. Widen it and the columns come back.
- [ ] **Recents panel:**
  - Track a scratch folder (a folder under `<scratch>`).
  - Click its chip to scope the list; click again to clear it.
  - Right-click the chip: Stop tracking (Paused shows), Resume, Edit tracking settings, About folder, Delete (confirmation shown).
  - The tracking line follows each change.
  - Group by Folder level.
  - Visit a subfolder in Explorer, then return: its row shows Visits and Time.
  - Right-click it, Add as place…, and the Add folder dialog opens prefilled.
- [ ] **Library window** (list mode, no `--workspace`, another scratch data root): the shelf shows no tracked-folders strip or Add as place, and its Folder level grouping works.
- [ ] Dark and light themes: the chips, Column choice and Favourites cards are readable.

Commit the updated verification notes:

```bash
git add ai/BUILD_SUMMARY.md
git commit -m "Record the Desk layout's Windows checks

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
