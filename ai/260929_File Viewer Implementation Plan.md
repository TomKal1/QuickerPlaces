# The File viewer — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a **Files** panel whose four tabs replace Desk's separate panels:
- **Saved places** (the existing places table);
- **Recent**, **Sessions** and **All** (the Library grid, with columns chosen per tab).

Session cards gain **View session files**, which scopes the Sessions tab. Desk switches to the Files panel. Today's Desk stays as **Desk · separate panels**.

**Spec:** [260929_File Viewer Design.md](260929_File%20Viewer%20Design.md). Read it first.

**Architecture:**
- **Query layer:** the Library query engine gains a Sessions source and a session filter.
- **`LibraryViewModel`:** gains a nullable `Tab` (Recent / Sessions / All) that picks the source and the column flags, plus a session scope held by id. A null tab (a Recents panel, the Library window) behaves exactly as today.
- **`FilesPanel`:** a WPF control hosting one `PlacesPanel` and one `FileShelfPanel`. It gives the Library its tab only while it is on screen.
- **`WorkspaceView`:** keeps a list of shelves instead of one field. Sessions cards raise `ViewFilesRequested(id)`.

**Tech stack:** .NET 10, WPF, xUnit.
- Tests link app sources one by one in `src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj`. This plan adds no UI-free source file, so nothing new is linked.
- Run all commands from `C:\QuickerPlaces`.
- Baseline: 1130 tests pass.

**Conventions:**
- Comments and doc comments follow the surrounding code: plain sentences, with the spec section in brackets ("File viewer design §4").
- Commit messages are imperative sentence case, with no prefix, and end with a `Co-Authored-By` trailer.
- Tests are named `Something_HappensWhen…`.
- Keep CRLF line endings and each file's encoding. Use the Edit tool for C# containing backslashes, because shell heredocs collapse `\\`.

---

## File map

| File | Change |
|---|---|
| `src/QuickerPlaces/Services/Library/LibraryQueryEngine.cs` | `LibrarySourceFilter.Sessions`, `LibraryFilter.Session`, `Passes`, folder and session heat |
| `src/QuickerPlaces/ViewModels/LibraryViewModel.cs` | `LibraryTab`, `Tab`, column flags, session scope, empty texts, row markers and `SessionsText` |
| `src/QuickerPlaces/Models/Workspace/PanelInstance.cs` | `PanelTypes.Files`, `ExcludedBy` |
| `src/QuickerPlaces/Services/Workspace/PanelLayoutEngine.cs` | Files' minimum width |
| `src/QuickerPlaces/Services/Workspace/BuiltInLayouts.cs` | Desk v2 with Files; `DeskSeparate` |
| `src/QuickerPlaces/Services/Workspace/WorkspaceLayoutService.cs` | `AddablePanelTypes` uses `ExcludedBy` |
| `src/QuickerPlaces/Resources/Icons.xaml` | `Icon.Bookmark` |
| `src/QuickerPlaces/Views/Panels/FileShelfPanel.xaml(.cs)` | columns follow the tab, Source markers, Sessions column, session chip |
| `src/QuickerPlaces/Views/Panels/FilesPanel.xaml(.cs)` | **new**: the tab strip and the two hosted views |
| `src/QuickerPlaces/Views/WorkspaceView.xaml.cs` | `CreateShelf`, `_shelves`, `files` case, View session files wiring |
| `src/QuickerPlaces/Views/Panels/SessionsPanel.xaml(.cs)` | card menu, `ViewFilesRequested`, `ShowsViewFiles` |
| Tests | `LibraryQueryEngineTests`, `LibraryViewModelTests`, `WorkspaceLayoutServiceTests`, `WorkspaceViewModelTests` |
| `ai/BUILD_SUMMARY.md`, the spec | build notes and Windows checks |

---

### Task 1: A Sessions source and a session filter in the query engine

**Files:**
- Modify: `src/QuickerPlaces/Services/Library/LibraryQueryEngine.cs`
- Test: `src/QuickerPlaces.Tests/LibraryQueryEngineTests.cs`

- [ ] **Step 1: Write the failing tests**

Append inside `LibraryQueryEngineTests`, after `ARootScope_ListsOnlyWhatIsInThatTrackedFolder_AndCountsOnlyItsVisits`:

```csharp
    [Fact]
    public void TheSessionsSource_ListsOnlySessionFiles_AndCountsNoFolderVisits()
    {
        var data = Snapshot(
            sessions: new[]
            {
                Session("Acme", new[] { "tower" }, new[] { Plan, Budget }, Today),
                Session("Beta", Array.Empty<string>(), new[] { Spec }, Today, Today),
            },
            roots: new[] { Root(new[] { (Today, new[] { Visit(Acme, 2, Today) }) }) },
            files: new[] { File(Plan, Today) });

        var sessions = Run(data, new LibraryFilter(Source: LibrarySourceFilter.Sessions));

        Assert.Equal(new[] { "Budget.xlsx", "Plan.pdf", "Spec.pdf" }, Names(sessions));
        Assert.Equal(CoverageState.NotApplicable, sessions.Coverage.Single(c => c.Source == LibraryQueryEngine.FolderSource).State);
        Assert.Equal(0, sessions.Heat[Today].FolderVisits);
        Assert.Equal(1, sessions.Heat[Today].FileOpens);
        Assert.Equal((2, 1), (sessions.Heat[Today].SessionsSaved, sessions.Heat[Today].SessionsReopened));
    }

    [Fact]
    public void ASessionFilter_ListsThatSessionsFiles_AndCountsOnlyThatSession()
    {
        var data = Snapshot(
            sessions: new[]
            {
                Session("Acme", new[] { "tower" }, new[] { Plan, Budget }, Today),
                Session("Beta", Array.Empty<string>(), new[] { Spec }, Today, Today),
            },
            files: new[] { File(Plan, Today) });

        var beta = Run(data, new LibraryFilter(Source: LibrarySourceFilter.Sessions, Session: "beta"));

        Assert.Equal(new[] { "Spec.pdf" }, Names(beta));
        Assert.Equal(0, beta.Heat[Today].FileOpens);
        Assert.Equal((1, 1), (beta.Heat[Today].SessionsSaved, beta.Heat[Today].SessionsReopened));
        Assert.False(new LibraryFilter(Session: "Beta").OnlyKind);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/QuickerPlaces.Tests --filter "FullyQualifiedName~LibraryQueryEngineTests"`

Expected: build errors, because `LibrarySourceFilter.Sessions` and the `Session` parameter don't exist.

- [ ] **Step 3: Implement**

In `LibraryQueryEngine.cs`:

Add a value to `LibrarySourceFilter`, after `Recent`:

```csharp

    /// <summary>Files in saved sessions: the File viewer's Sessions tab (File viewer design §4).</summary>
    Sessions,
```

Replace the `LibraryFilter` record header and `OnlyKind` with:

```csharp
public sealed record LibraryFilter(LibraryKind? Kind = null, LibrarySourceFilter Source = LibrarySourceFilter.All,
    string Text = "", string? Tag = null, RootScope? Root = null, string? Session = null)
{
    public static LibraryFilter None { get; } = new();

    /// <summary>True when nothing but the kind narrows the items.</summary>
    public bool OnlyKind => Source == LibrarySourceFilter.All && string.IsNullOrWhiteSpace(Text) && Tag is null && Root is null && Session is null;
}
```

Update the record's doc comment to end "…search words, an existing Session tag, a tracked folder, and one session by name (File viewer design §5)."

Replace `Passes` with:

```csharp
    public static bool Passes(LibraryItem item, LibraryFilter filter, bool ignoreKind = false)
        => (ignoreKind || filter.Kind is null || item.Kind == filter.Kind) &&
           filter.Source switch
           {
               LibrarySourceFilter.Saved => item.IsSaved,
               LibrarySourceFilter.Recent => item.IsRecent,
               LibrarySourceFilter.Sessions => item.IsInSession,
               _ => true,
           } &&
           (filter.Tag is null || item.Tags.Contains(filter.Tag, StringComparer.OrdinalIgnoreCase)) &&
           (filter.Session is null || item.Sessions.Contains(filter.Session, StringComparer.OrdinalIgnoreCase)) &&
           (filter.Root is null || (item.TreePath.Length > 0 && TrackedFolderPaths.LevelBelow(filter.Root.Path, item.TreePath) is not null)) &&
           LibraryIndex.Matches(item, filter.Text);
```

In `AddFolderHeat`, replace the first check and its comment with:

```csharp
        // Folders carry no Session tags and are in no session, and a document or link filter leaves them out.
        if (filter.Kind is not (null or LibraryKind.Folder) || filter.Tag is not null ||
            filter.Source == LibrarySourceFilter.Sessions || filter.Session is not null)
            return new SourceCoverage(FolderSource, CoverageState.NotApplicable);
```

In `AddSessionHeat`, add this as the first statement inside `foreach (var session in data.Sessions)`:

```csharp
            if (filter.Session is not null && !string.Equals(session.Name, filter.Session, StringComparison.OrdinalIgnoreCase))
                continue;
```

Then change `var fileFilter = filter with { Tag = null };` to `var fileFilter = filter with { Tag = null, Session = null };`.

- [ ] **Step 4: Run all the tests**

Run: `dotnet test src/QuickerPlaces.Tests`

Expected: PASS, 1132.

- [ ] **Step 5: Commit**

```bash
git add src/QuickerPlaces/Services/Library/LibraryQueryEngine.cs src/QuickerPlaces.Tests/LibraryQueryEngineTests.cs
git commit -m "Let the Library list session files alone, or one session's files

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Tabs, columns and a session scope in the Library view model

**Files:**
- Modify: `src/QuickerPlaces/ViewModels/LibraryViewModel.cs`
- Test: `src/QuickerPlaces.Tests/LibraryViewModelTests.cs`

`Seed()` in `LibraryViewModelTests` gives six rows:
- saved folder **Jobs** and saved link **Wiki**;
- session **Acme** (tags Acme, markups) with **A-101.pdf** and **Report.docx**;
- Recents folder **Acme**;
- Recent Files workbook **Budget.xlsx**.

- [ ] **Step 1: Write the failing tests**

Append inside `LibraryViewModelTests`:

```csharp
    // ---------------------------------------------------------------
    // The File viewer's tabs (File viewer design §4, §5)
    // ---------------------------------------------------------------

    private static string[] Names(LibraryViewModel vm) => vm.Rows.Select(r => r.Name).OrderBy(n => n).ToArray();

    private static (bool Choice, bool WhereFrom, bool VisitsAndTime, bool Tags, bool Sessions, bool Tracked, bool Markers, bool Level) Columns(LibraryViewModel vm)
        => (vm.ShowsSourceChoice, vm.ShowsWhereFrom, vm.ShowsVisitsAndTime, vm.ShowsTags, vm.ShowsSessions, vm.ShowsTrackedFolders, vm.ShowsSourceMarkers, vm.ShowsFolderLevel);

    [Fact]
    public void EachFileViewerTab_ChoosesItsSourceAndColumns_AndNoTabIsAsBefore()
    {
        Seed();
        var vm = NewViewModel();
        Assert.Null(vm.Tab);
        Assert.Equal((true, true, true, true, false, true, false, true), Columns(vm));

        vm.Tab = LibraryTab.Recent;
        Assert.Equal(new[] { "Acme", "Budget.xlsx" }, Names(vm));
        Assert.Equal((false, false, true, false, false, true, false, true), Columns(vm));

        vm.Tab = LibraryTab.Sessions;
        Assert.Equal(new[] { "A-101.pdf", "Report.docx" }, Names(vm));
        Assert.Equal((false, false, false, true, true, false, false, false), Columns(vm));
        Assert.Equal("sessions", vm.CurrentQuery.Source);

        vm.Tab = LibraryTab.All;
        Assert.Equal(6, vm.Rows.Count);
        Assert.Equal((false, true, false, true, false, false, true, true), Columns(vm));

        vm.Tab = LibraryTab.Sessions;
        vm.Tab = null;
        Assert.Equal(LibrarySourceFilter.All, vm.Source);
        Assert.Equal(6, vm.Rows.Count);
    }

    [Fact]
    public void WhileATabIsShown_ALayoutsQuery_KeepsTheTabsSource()
    {
        Seed();
        var vm = NewViewModel();
        vm.Tab = LibraryTab.Sessions;

        vm.ApplyQuery(new WorkspaceQuery { Source = "recent" });

        Assert.Equal(LibrarySourceFilter.Sessions, vm.Source);
        Assert.Equal(new[] { "A-101.pdf", "Report.docx" }, Names(vm));
    }

    [Fact]
    public void TheSessionsTab_HasNoFolderLevel_SoThatGroupingFallsBackToType()
    {
        Seed();
        var vm = NewViewModel();
        vm.Grouping = LibraryGrouping.Level;

        vm.Tab = LibraryTab.Sessions;

        Assert.Equal(LibraryGrouping.Type, vm.Grouping);
    }

    [Fact]
    public void ViewSessionFiles_ScopesTheSessionsTab_FollowsARename_AndClears()
    {
        Seed();
        Assert.True(_sessions.TryCreate("Beta", Array.Empty<string>(), new[] { Excel }, out _, out _).Success);
        var vm = NewViewModel();
        var acme = _sessions.Sessions.Single(s => s.Name == "Acme").Id;

        vm.ScopeToSession(acme);

        Assert.Equal(LibraryTab.Sessions, vm.Tab);
        Assert.Equal(new[] { "A-101.pdf", "Report.docx" }, Names(vm));
        Assert.Equal("Session: Acme", vm.SessionScopeText);
        Assert.True(vm.HasSessionScope);
        Assert.Equal("Showing the files in Acme.", vm.StatusMessage);

        Assert.True(_sessions.TryUpdate(acme, "Acme tower", new[] { "Acme", "markups" }, new[] { Pdf, Word }, out _).Success);
        vm.Reload();
        Assert.Equal("Session: Acme tower", vm.SessionScopeText);
        Assert.Equal(new[] { "A-101.pdf", "Report.docx" }, Names(vm));

        vm.ClearSessionScope();
        Assert.False(vm.HasSessionScope);
        Assert.Equal(new[] { "A-101.pdf", "Budget.xlsx", "Report.docx" }, Names(vm));
    }

    [Fact]
    public void ASessionScope_ClearsWhenItsSessionIsDeleted_OrTheTabChanges()
    {
        Seed();
        Assert.True(_sessions.TryCreate("Beta", Array.Empty<string>(), new[] { Excel }, out var beta, out _).Success);
        var vm = NewViewModel();

        vm.ScopeToSession(beta!.Id);
        Assert.Equal(new[] { "Budget.xlsx" }, Names(vm));
        _sessions.Delete(beta.Id);
        vm.Reload();
        Assert.False(vm.HasSessionScope);
        Assert.Null(vm.SessionScope);
        Assert.Equal(new[] { "A-101.pdf", "Report.docx" }, Names(vm));

        var acme = _sessions.Sessions.Single(s => s.Name == "Acme").Id;
        vm.ScopeToSession(acme);
        vm.Tab = LibraryTab.All;
        Assert.False(vm.HasSessionScope);
        Assert.Equal(6, vm.Rows.Count);
    }

    [Fact]
    public void AllTabRows_CarryTheirSourceMarkers_AndSessionsRowsTheirSessions()
    {
        Seed();
        var vm = NewViewModel();
        vm.Tab = LibraryTab.All;

        var jobs = vm.Rows.Single(r => r.Name == "Jobs");
        var pdf = vm.Rows.Single(r => r.Name == "A-101.pdf");
        var budget = vm.Rows.Single(r => r.Name == "Budget.xlsx");

        Assert.Equal((true, false, false, "Saved place"), (jobs.IsSavedPlace, jobs.IsRecent, jobs.IsInSession, jobs.MarkersText));
        Assert.Equal((false, false, true, "In a session", "Acme"), (pdf.IsSavedPlace, pdf.IsRecent, pdf.IsInSession, pdf.MarkersText, pdf.SessionsText));
        Assert.Equal("Recent", budget.MarkersText);
    }

    [Fact]
    public void EmptyTabs_SayWhatWouldFillThem()
    {
        var vm = NewViewModel();

        vm.Tab = LibraryTab.Sessions;
        Assert.Equal("No session files yet. Save open files as a session from the Sessions panel.", vm.EmptyText);
        vm.Tab = LibraryTab.Recent;
        Assert.Equal("Nothing recent yet. Track a folder above, or turn on Recent Files.", vm.EmptyText);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/QuickerPlaces.Tests --filter "FullyQualifiedName~LibraryViewModelTests"`

Expected: build errors, because `LibraryTab`, `Tab`, the `Shows…` flags, `ScopeToSession` and the row markers don't exist.

- [ ] **Step 3: Implement the tab enum and fields**

In `LibraryViewModel.cs`, after the `LibraryGrouping` enum, add:

```csharp
/// <summary>The File viewer's tabs that show the Library (File viewer design §4). Its Saved places tab is the places table.</summary>
public enum LibraryTab
{
    Recent,
    Sessions,
    All,
}
```

In `LibraryViewModel`, add these fields after `private string? _rootId;`:

```csharp
    private LibraryTab? _tab;
    private string? _sessionId;
```

- [ ] **Step 4: The query maps Sessions, and a tab keeps its source**

In `CurrentQuery`, add `LibrarySourceFilter.Sessions => "sessions",` to the `Source` switch, before `_ => null`.

In `ApplyQuery`, add `"sessions" => LibrarySourceFilter.Sessions,` to the `_source` switch, before `_ => …`. Then, directly after that switch statement, add:

```csharp
        // While the File viewer shows a tab, the tab decides the source (File viewer design §4).
        if (_tab is { } shown)
            _source = SourceOf(shown);
```

- [ ] **Step 5: The tab, its column flags and the session scope**

After `ToggleRootScope`, add:

```csharp
    // ---------------------------------------------------------------
    // The File viewer's tabs (File viewer design §4, §5)
    // ---------------------------------------------------------------

    /// <summary>
    /// The File viewer tab shown, or null outside it: a Recents panel and the
    /// Library window, where every column shows as before. A tab decides the
    /// source. Leaving Sessions clears a session scope. Sessions has no
    /// Folder level, so that grouping falls back to Type there.
    /// </summary>
    public LibraryTab? Tab
    {
        get => _tab;
        set
        {
            if (_tab == value)
                return;

            _tab = value;
            if (value != LibraryTab.Sessions)
                _sessionId = null;
            if (value == LibraryTab.Sessions && _grouping == LibraryGrouping.Level)
                Grouping = LibraryGrouping.Type;

            // No tab: a Recents panel's Show segment has no Sessions choice, so that source reads as All there.
            var source = value is { } tab ? SourceOf(tab) : _source == LibrarySourceFilter.Sessions ? LibrarySourceFilter.All : _source;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowsSourceChoice));
            OnPropertyChanged(nameof(ShowsTrackedFolders));
            OnPropertyChanged(nameof(ShowsWhereFrom));
            OnPropertyChanged(nameof(ShowsVisitsAndTime));
            OnPropertyChanged(nameof(ShowsTags));
            OnPropertyChanged(nameof(ShowsSessions));
            OnPropertyChanged(nameof(ShowsSourceMarkers));
            OnPropertyChanged(nameof(ShowsFolderLevel));
            NotifySessionScope();

            if (source != _source)
            {
                _source = source;
                NotifySource();
                QueryEdited();
            }
            else
            {
                Refresh();
            }
        }
    }

    /// <summary>The Show segment (All / Saved / Recent): the tabs replace it.</summary>
    public bool ShowsSourceChoice => _tab is null;

    /// <summary>The tracked folders strip: Recent's.</summary>
    public bool ShowsTrackedFolders => _tab is null or LibraryTab.Recent;

    public bool ShowsWhereFrom => _tab is null or LibraryTab.All;

    public bool ShowsVisitsAndTime => _tab is null or LibraryTab.Recent;

    public bool ShowsTags => _tab != LibraryTab.Recent;

    /// <summary>Which sessions hold each file: the Sessions tab's column.</summary>
    public bool ShowsSessions => _tab == LibraryTab.Sessions;

    /// <summary>The Source markers (saved place, recent, in a session): the All tab's column.</summary>
    public bool ShowsSourceMarkers => _tab == LibraryTab.All;

    /// <summary>Folder level grouping: sessions hold files from anywhere, so not on Sessions.</summary>
    public bool ShowsFolderLevel => _tab != LibraryTab.Sessions;

    /// <summary>The session the Sessions tab is narrowed to, by id, or null for every session's files (File viewer design §5).</summary>
    public string? SessionScope => _sessionId;

    /// <summary>The scoped session's name now: a rename keeps the scope, and a deleted session scopes nothing.</summary>
    private string? SessionScopeName
        => _sessionId is null ? null : _snapshot.Sessions.FirstOrDefault(s => s.Id == _sessionId)?.Name;

    public bool HasSessionScope => SessionScopeName is not null;

    /// <summary>"Session: Tower B": the chip that clears the scope.</summary>
    public string SessionScopeText => SessionScopeName is { } name ? $"Session: {name}" : "";

    /// <summary>A session card's View session files: the Sessions tab, narrowed to that session.</summary>
    public void ScopeToSession(string sessionId)
    {
        Tab = LibraryTab.Sessions;
        _sessionId = sessionId;
        NotifySessionScope();
        Refresh();
        StatusMessage = SessionScopeName is { } name ? $"Showing the files in {name}." : null;
    }

    /// <summary>The chip's ×: every session's files again.</summary>
    public void ClearSessionScope()
    {
        if (_sessionId is null)
            return;

        _sessionId = null;
        NotifySessionScope();
        Refresh();
    }

    private static LibrarySourceFilter SourceOf(LibraryTab tab) => tab switch
    {
        LibraryTab.Recent => LibrarySourceFilter.Recent,
        LibraryTab.Sessions => LibrarySourceFilter.Sessions,
        _ => LibrarySourceFilter.All,
    };

    private void NotifySessionScope()
    {
        OnPropertyChanged(nameof(SessionScope));
        OnPropertyChanged(nameof(HasSessionScope));
        OnPropertyChanged(nameof(SessionScopeText));
    }
```

- [ ] **Step 6: Refresh, Reload and the empty text**

In `Refresh`, change the filter line to:

```csharp
        var filter = new LibraryFilter(_selectedKind, _source, _searchText, _tag, ScopeFor(_rootId), SessionScopeName);
```

In `Reload`, after `BuildRootChips();`, add:

```csharp
        if (_sessionId is not null && SessionScopeName is null)
            _sessionId = null;
        NotifySessionScope();
```

In `EmptyText`'s getter, add these as its first statements, before `var inPeriod = …`:

```csharp
            if (Period is null && _tab == LibraryTab.Sessions && _snapshot.Sessions.All(s => s.Files.Count == 0))
                return "No session files yet. Save open files as a session from the Sessions panel.";
            if (Period is null && _tab == LibraryTab.Recent && _snapshot.Roots.Count == 0 && _snapshot.Files.Count == 0)
                return "Nothing recent yet. Track a folder above, or turn on Recent Files.";
```

- [ ] **Step 7: Row markers and sessions**

In `LibraryRowViewModel`, after `CanAddAsPlace`, add:

```csharp
    /// <summary>The All tab's Source markers (File viewer design §4).</summary>
    public bool IsSavedPlace => Item.IsSavedPlace;
    public bool IsRecent => Item.IsRecent;
    public bool IsInSession => Item.IsInSession;

    /// <summary>The markers in words, for screen readers: "Saved place, Recent, In a session".</summary>
    public string MarkersText => string.Join(", ", new[]
    {
        IsSavedPlace ? "Saved place" : null,
        IsRecent ? "Recent" : null,
        IsInSession ? "In a session" : null,
    }.OfType<string>());

    /// <summary>The sessions that hold it, for the Sessions tab.</summary>
    public string SessionsText => string.Join(", ", Item.Sessions);
```

In `Looks`, add `SessionsText == other.SessionsText &&` after `VisitsText == other.VisitsText && TimeText == other.TimeText &&`.

- [ ] **Step 8: Run all the tests**

Run: `dotnet test src/QuickerPlaces.Tests`

Expected: PASS, 1139.

- [ ] **Step 9: Commit**

```bash
git add src/QuickerPlaces/ViewModels/LibraryViewModel.cs src/QuickerPlaces.Tests/LibraryViewModelTests.cs
git commit -m "Give the Library the File viewer's tabs, their columns, and a session scope

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The Files panel type, and the two Desks

**Files:**
- Modify: `src/QuickerPlaces/Models/Workspace/PanelInstance.cs`
- Modify: `src/QuickerPlaces/Services/Workspace/PanelLayoutEngine.cs`
- Modify: `src/QuickerPlaces/Services/Workspace/BuiltInLayouts.cs`
- Modify: `src/QuickerPlaces/Services/Workspace/WorkspaceLayoutService.cs`
- Test: `src/QuickerPlaces.Tests/WorkspaceLayoutServiceTests.cs`, `src/QuickerPlaces.Tests/WorkspaceViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Append inside `WorkspaceLayoutServiceTests`:

```csharp
    // ---------------------------------------------------------------
    // The File viewer (File viewer design §2, §3)
    // ---------------------------------------------------------------

    [Fact]
    public void Desk_HasTheFileViewer_AndDeskSeparatePanels_TheSavedPlacesAndRecentsPanels()
    {
        var service = NewService(NewStorage());

        Ok(service.Activate(BuiltInLayouts.DeskId));
        Assert.Equal(new[] { PanelTypes.Favourites, PanelTypes.Sessions }, Column(service, PanelDocks.Left));
        Assert.Equal(new[] { PanelTypes.Files, PanelTypes.Activity }, Column(service, PanelDocks.Main));

        Ok(service.Activate(BuiltInLayouts.DeskSeparateId));
        Assert.True(service.ActiveIsColumns);
        Assert.Equal(new[] { PanelTypes.Favourites, PanelTypes.Sessions }, Column(service, PanelDocks.Left));
        Assert.Equal(new[] { PanelTypes.Places, PanelTypes.Shelf, PanelTypes.Activity }, Column(service, PanelDocks.Main));
    }

    [Fact]
    public void FilesAndTheSavedPlacesOrRecentsPanels_ExcludeEachOther_InAddPanel()
    {
        var service = NewService(NewStorage());
        Ok(service.Activate(BuiltInLayouts.DeskId));
        service.BeginArrange();

        Assert.DoesNotContain(PanelTypes.Places, service.AddablePanelTypes);
        Assert.DoesNotContain(PanelTypes.Shelf, service.AddablePanelTypes);
        Assert.False(service.AddPanel(PanelTypes.Places));

        Assert.True(service.Hide(PanelTypes.Files));
        Assert.Contains(PanelTypes.Places, service.AddablePanelTypes);
        Assert.True(service.AddPanel(PanelTypes.Shelf));
        Assert.DoesNotContain(PanelTypes.Files, service.AddablePanelTypes);
        Assert.False(service.AddPanel(PanelTypes.Files));
    }
```

Append inside `WorkspaceViewModelTests`:

```csharp
    [Fact]
    public void BothDesks_AreOffered_TheFileViewerOneFirst()
    {
        var workspace = NewWorkspace();
        workspace.SelectedLayout = workspace.Layouts.Single(l => l.Id == BuiltInLayouts.DeskId);

        Assert.Equal(new[] { PanelTypes.Files, PanelTypes.Activity },
            workspace.Panels.Where(p => p.Dock == PanelDock.Main).Select(p => p.Type));
        Assert.Equal("Files", workspace.Panels.Single(p => p.Type == PanelTypes.Files).Title);
        Assert.Contains(workspace.Layouts, l => l.Id == BuiltInLayouts.DeskSeparateId && l.Name == "Desk · separate panels");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/QuickerPlaces.Tests --filter "FullyQualifiedName~Workspace"`

Expected: build errors, because `PanelTypes.Files` and `BuiltInLayouts.DeskSeparateId` don't exist.

- [ ] **Step 3: The panel type**

In `PanelInstance.cs`, in `PanelTypes`:

1. Add `public const string Files = "files";` after `Favourites`.
2. Change `Known` to:
   `new[] { Activity, Shelf, Sessions, Places, Favourites, Files, Collections, Searches }`
3. Change `Available` to:
   `new[] { Activity, Shelf, Sessions, Places, Favourites, Files }`
4. In `DisplayName`, add `Files => "Files",` after `Favourites => "Favourites",`.
5. In `DefaultSpan`, change `Activity or Places => PanelSpans.Full,` to `Activity or Places or Files => PanelSpans.Full,`.
6. After `DefaultSpan`, add:

```csharp
    /// <summary>
    /// True when a <paramref name="type"/> panel can't be added beside the
    /// <paramref name="shown"/> types. Files holds Saved places and Recents as
    /// tabs (File viewer design §3), so a layout shows Files or them, never both.
    /// </summary>
    public static bool ExcludedBy(string type, IReadOnlyCollection<string> shown) => type == Files
        ? shown.Contains(Places) || shown.Contains(Shelf)
        : (type is Places or Shelf) && shown.Contains(Files);
```

In `PanelLayoutEngine.MinimumWidth`, change `PanelTypes.Places => 560,` to `PanelTypes.Places or PanelTypes.Files => 560,`.

In `WorkspaceLayoutService.cs`, replace `AddablePanelTypes` and its doc comment with:

```csharp
    /// <summary>The panel types Add panel offers: available in this build, not already shown, and not excluded by one shown (File viewer design §3).</summary>
    public IReadOnlyList<string> AddablePanelTypes
    {
        get
        {
            var shown = _panels.Where(p => !p.Hidden).Select(p => p.Type).ToList();
            return PanelTypes.Available.Where(t => !shown.Contains(t) && !PanelTypes.ExcludedBy(t, shown)).ToList();
        }
    }
```

- [ ] **Step 4: The two Desks**

In `BuiltInLayouts.cs`:
- Add `public const string DeskSeparateId = "builtin.desk-separate";` after `DeskId`.
- Replace the `Desk` field and its doc comment with the two fields below.
- Change `All` to `new[] { ActivityAtlas, FilesFirst, Desk, DeskSeparate, ProjectCanvas, PersonalDesk }`.
- In the class doc comment, change "that is Activity Atlas, Files First and Desk." to "that is Activity Atlas, Files First and the two Desks."

```csharp
    /// <summary>
    /// The user's sketch (Desk layout design) with the File viewer (File
    /// viewer design §2): favourites and sessions in a left column; Files
    /// (saved places, recents and session files as tabs) and the year calendar
    /// in the main column. Version 2: version 1 had separate panels.
    /// </summary>
    public static readonly BuiltInLayout Desk = new(DeskId, "Desk", 2, LayoutArrangements.Columns, new (string, int, string?)[]
    {
        (PanelTypes.Favourites, PanelSpans.Third, PanelDocks.Left),
        (PanelTypes.Sessions, PanelSpans.Third, PanelDocks.Left),
        (PanelTypes.Files, PanelSpans.Full, PanelDocks.Main),
        (PanelTypes.Activity, PanelSpans.Full, PanelDocks.Main),
    });

    /// <summary>
    /// Desk as first built (Desk layout design): saved places, Recents and the
    /// year calendar as separate panels. The way back from the File viewer.
    /// </summary>
    public static readonly BuiltInLayout DeskSeparate = new(DeskSeparateId, "Desk · separate panels", 1, LayoutArrangements.Columns, new (string, int, string?)[]
    {
        (PanelTypes.Favourites, PanelSpans.Third, PanelDocks.Left),
        (PanelTypes.Sessions, PanelSpans.Third, PanelDocks.Left),
        (PanelTypes.Places, PanelSpans.Full, PanelDocks.Main),
        (PanelTypes.Shelf, PanelSpans.TwoThirds, PanelDocks.Main),
        (PanelTypes.Activity, PanelSpans.Full, PanelDocks.Main),
    });
```

- [ ] **Step 5: Update the tests that list built-ins, Add panel choices or Desk's panels**

Run `dotnet test src/QuickerPlaces.Tests`. These existing tests change mechanically. Update only their expected values, and report each one you touch:

- **`WorkspaceLayoutServiceTests`:**
  - `OnlyBuiltInsWhosePanelsExist_AreOffered`: add `BuiltInLayouts.DeskSeparateId` after `DeskId`, and say "the two Desks" in its comment.
  - `AnEmptyCanvas_StillOffersAddPanel`: add `PanelTypes.Files` at the end.
  - `Desk_PutsFavouritesAndSessionsLeft_AndTheRestInTheMainColumn`: activate `DeskSeparateId` instead of `DeskId`, and rename the test to `DeskSeparate_PutsFavouritesAndSessionsLeft_AndTheRestInTheMainColumn`.
- **`WorkspaceViewModelTests`:**
  - `OnlyTheBuiltInsWithWorkingPanels_AreOffered`: add `"Desk · separate panels"` after `"Desk"`.
  - `Desk_ShowsFavouritesAsAPanel_InTheLeftColumn`: the main column is now `PanelTypes.Files, PanelTypes.Activity`.
  - `BuiltInsCannotBeRenamedOrDeleted`, `TheStartupChoice_ListsTheLastLayoutThenEveryLayout` and `TwoPersonalLayouts_SwitchAndSurviveARestart_WithoutTouchingTheBuiltIns`: one more built-in, named "Desk · separate panels".

If any other test fails, stop and report it; don't change its expectation.

- [ ] **Step 6: Run all the tests**

Run: `dotnet test src/QuickerPlaces.Tests`

Expected: PASS, 1142.

Run: `dotnet build src/QuickerPlaces/QuickerPlaces.csproj`

Expected: 0 warnings. The workspace shows Files as "This panel isn't available…" until Task 5.

- [ ] **Step 7: Commit**

```bash
git add src/QuickerPlaces/Models/Workspace/PanelInstance.cs src/QuickerPlaces/Services/Workspace/PanelLayoutEngine.cs src/QuickerPlaces/Services/Workspace/BuiltInLayouts.cs src/QuickerPlaces/Services/Workspace/WorkspaceLayoutService.cs src/QuickerPlaces.Tests/WorkspaceLayoutServiceTests.cs src/QuickerPlaces.Tests/WorkspaceViewModelTests.cs
git commit -m "Add the Files panel type, Desk with it, and Desk with separate panels

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: The shelf's columns follow the tab

This is WPF, so it has no unit tests. Build now; Task 7 verifies it on Windows.

**Files:**
- Modify: `src/QuickerPlaces/Resources/Icons.xaml`
- Modify: `src/QuickerPlaces/Views/Panels/FileShelfPanel.xaml`
- Modify: `src/QuickerPlaces/Views/Panels/FileShelfPanel.xaml.cs`

- [ ] **Step 1: The bookmark icon**

In `Icons.xaml`, after `Icon.Star`, add:

```xml
    <Geometry x:Key="Icon.Bookmark">M4.5,2.5 L11.5,2.5 L11.5,13.5 L8,10.75 L4.5,13.5 Z</Geometry>
```

- [ ] **Step 2: Tracked folders and the Show segment follow the tab**

In `FileShelfPanel.xaml`:

1. Wrap the whole `<StackPanel x:Name="TrackingArea" …>…</StackPanel>` element in a Border. Change nothing inside it:

```xml
                <!-- Only on the File viewer's Recent tab, or with no tab (File viewer design §4). -->
                <Border Visibility="{Binding ShowsTrackedFolders, Converter={StaticResource BooleanToVisibilityConverter}}">
                    …the existing TrackingArea StackPanel…
                </Border>
```

2. In the `WrapPanel` that holds SHOW, GROUP BY and TAG, replace the SHOW `TextBlock` and the Show `GroupBox` with one StackPanel that holds them unchanged:

```xml
                    <StackPanel Orientation="Horizontal" Margin="0,0,24,0"
                                Visibility="{Binding ShowsSourceChoice, Converter={StaticResource BooleanToVisibilityConverter}}">
                        <TextBlock Text="SHOW" Style="{StaticResource TextBlock.Caps}" Margin="0,0,10,0" />
                        …the existing Show GroupBox…
                    </StackPanel>
```

3. On the GROUP BY `TextBlock`, change `Margin="24,0,10,0"` to `Margin="0,0,10,0"`. The new StackPanel's right margin now keeps the gap.

4. On the "Folder level" `RadioButton`, add `Visibility="{Binding ShowsFolderLevel, Converter={StaticResource BooleanToVisibilityConverter}}"`.

5. Remove the `GroupName="Source"` and `GroupName="Grouping"` attributes from the six `RadioButton`s.
   - Why: a `GroupName` groups across the whole window. With a Recents panel and the File viewer's shelf both in the window (one of them collapsed), checking a radio in one shelf would uncheck its twin in the other.
   - Without `GroupName`, radios group by their parent panel, and each set already has its own `StackPanel`.
   - Add this comment above the Show `GroupBox`: `<!-- No GroupName: radios group by their parent, so two shelves in one window don't uncheck each other (File viewer design §7). -->`

- [ ] **Step 3: The session scope chip**

In `FileShelfPanel.xaml`, directly after the `PeriodArea` Border, add:

```xml
                <!-- A session card's View session files (File viewer design §5): the session shown, and × for every session's files. -->
                <Border HorizontalAlignment="Left" Background="{DynamicResource Highlight.Soft}" CornerRadius="14" Height="28"
                        Padding="12,0,3,0" Margin="0,0,0,8"
                        Visibility="{Binding HasSessionScope, Converter={StaticResource BooleanToVisibilityConverter}}">
                    <StackPanel Orientation="Horizontal">
                        <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Sessions}" Margin="0,0,7,0" />
                        <TextBlock Text="{Binding SessionScopeText}" FontWeight="SemiBold" VerticalAlignment="Center" />
                        <Button Style="{StaticResource Button.IconOnlyCompact}" Width="22" Height="22" Margin="6,0,0,0"
                                ToolTip="Show every session's files" AutomationProperties.Name="Clear the session"
                                Click="ClearSessionScope_Click">
                            <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Close}" />
                        </Button>
                    </StackPanel>
                </Border>
```

- [ ] **Step 4: The columns**

In `FileShelfPanel.xaml`, replace the `<DataGrid.Columns>` content with the following. It adds SOURCE first and SESSIONS after TIME, and names the columns that come and go:

```xml
                    <DataGridTemplateColumn x:Name="SourceColumn" Header="SOURCE" Width="76" Visibility="Collapsed">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Background="Transparent"
                                            ToolTip="{Binding SourceText}" AutomationProperties.Name="{Binding MarkersText}">
                                    <Path Style="{StaticResource Icon.Row}" Data="{StaticResource Icon.Bookmark}" Margin="0,0,4,0"
                                          Visibility="{Binding IsSavedPlace, Converter={StaticResource BooleanToVisibilityConverter}}" />
                                    <Path Style="{StaticResource Icon.Row}" Data="{StaticResource Icon.Clock}" Margin="0,0,4,0"
                                          Visibility="{Binding IsRecent, Converter={StaticResource BooleanToVisibilityConverter}}" />
                                    <Path Style="{StaticResource Icon.Row}" Data="{StaticResource Icon.Sessions}" Margin="0"
                                          Visibility="{Binding IsInSession, Converter={StaticResource BooleanToVisibilityConverter}}" />
                                </StackPanel>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>
                    <DataGridTemplateColumn Header="NAME" Width="2*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                                    <Path Style="{StaticResource Icon.LibraryKind}" />
                                    <TextBlock Text="{Binding Name}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"
                                               ToolTip="{Binding Location}" />
                                </StackPanel>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>
                    <DataGridTextColumn Header="TYPE" Binding="{Binding KindLabel}" Width="70" ElementStyle="{StaticResource Cell.Secondary}" />
                    <DataGridTextColumn x:Name="WhereFromColumn" Header="WHERE FROM" Binding="{Binding SourceText}" Width="2*" ElementStyle="{StaticResource Cell.Secondary}" />
                    <DataGridTextColumn x:Name="VisitsColumn" Header="VISITS" Binding="{Binding VisitsText}" Width="60" ElementStyle="{StaticResource Cell.Secondary}" />
                    <DataGridTextColumn x:Name="TimeColumn" Header="TIME" Binding="{Binding TimeText}" Width="70" ElementStyle="{StaticResource Cell.Secondary}" />
                    <DataGridTextColumn x:Name="SessionsColumn" Header="SESSIONS" Binding="{Binding SessionsText}" Width="*" ElementStyle="{StaticResource Cell.Secondary}" Visibility="Collapsed" />
                    <DataGridTextColumn x:Name="TagsColumn" Header="TAGS" Binding="{Binding TagsText}" Width="*" ElementStyle="{StaticResource Cell.Text}" />
                    <DataGridTextColumn Header="FOLDER" Binding="{Binding Folder}" Width="2*" ElementStyle="{StaticResource Cell.Path}" />
                    <DataGridTextColumn Header="LAST USED" Binding="{Binding LastUsedText}" Width="140" ElementStyle="{StaticResource Cell.Secondary}" />
```

Update the header comment's last sentence to add: "In the File viewer, the Library's tab picks the columns and hides the Show segment (File viewer design §4)."

- [ ] **Step 5: Code-behind**

In `FileShelfPanel.xaml.cs`, add `using System.ComponentModel;`. Replace the constructor `public FileShelfPanel() => InitializeComponent();` with:

```csharp
    public FileShelfPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is LibraryViewModel old)
                old.PropertyChanged -= ViewModel_PropertyChanged;
            if (e.NewValue is LibraryViewModel now)
                now.PropertyChanged += ViewModel_PropertyChanged;
            UpdateColumns();
        };
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.Tab))
            UpdateColumns();
    }

    /// <summary>
    /// Shows the columns the File viewer's tab asks for (File viewer design §4).
    /// With no tab, the columns are as they always were. DataGrid columns aren't
    /// in the visual tree, so they can't bind to the DataContext: this sets them.
    /// </summary>
    private void UpdateColumns()
    {
        var vm = ViewModel;
        static Visibility Show(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

        SourceColumn.Visibility = Show(vm?.ShowsSourceMarkers == true);
        WhereFromColumn.Visibility = Show(vm?.ShowsWhereFrom != false);
        VisitsColumn.Visibility = Show(vm?.ShowsVisitsAndTime != false);
        TimeColumn.Visibility = VisitsColumn.Visibility;
        SessionsColumn.Visibility = Show(vm?.ShowsSessions == true);
        TagsColumn.Visibility = Show(vm?.ShowsTags != false);
    }

    private void ClearSessionScope_Click(object sender, RoutedEventArgs e) => ViewModel?.ClearSessionScope();
```

- [ ] **Step 6: Build and test**

Run: `dotnet build src/QuickerPlaces/QuickerPlaces.csproj`

Expected: 0 warnings, 0 errors.

Run: `dotnet test src/QuickerPlaces.Tests`

Expected: PASS, 1142.

- [ ] **Step 7: Commit**

```bash
git add src/QuickerPlaces/Resources/Icons.xaml src/QuickerPlaces/Views/Panels/FileShelfPanel.xaml src/QuickerPlaces/Views/Panels/FileShelfPanel.xaml.cs
git commit -m "Let the shelf show the columns, markers and session chip a File viewer tab asks for

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: The Files panel in the workspace

This is WPF, so it has no unit tests. Build now; Task 7 verifies it on Windows.

**Files:**
- Create: `src/QuickerPlaces/Views/Panels/FilesPanel.xaml`
- Create: `src/QuickerPlaces/Views/Panels/FilesPanel.xaml.cs`
- Modify: `src/QuickerPlaces/Views/WorkspaceView.xaml.cs`

Match the encoding and BOM of `FavouritesPanel.xaml(.cs)` for the new files.

- [ ] **Step 1: `FilesPanel.xaml`**

```xml
<UserControl x:Class="QuickerPlaces.Views.Panels.FilesPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!--
        The File viewer (File viewer design §3, §4): saved places, Recents and
        session files as tabs over one place. The Saved places tab is the
        places table; Recent, Sessions and All are the Library grid, whose tab
        (LibraryViewModel.Tab) picks its source and columns. Both views are
        made once and kept, so each keeps its selection and scroll.
    -->
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <GroupBox Style="{StaticResource GroupBox.Unstyled}" AutomationProperties.Name="File viewer tabs"
                  HorizontalAlignment="Left" Margin="0,0,0,12">
            <Border Style="{StaticResource Border.SegmentGroup}">
                <StackPanel Orientation="Horizontal">
                    <RadioButton x:Name="SavedTab" Content="Saved places" IsChecked="True"
                                 Style="{StaticResource RadioButton.Segment}" Tag="5,0,0,5" Checked="Tab_Checked"
                                 ToolTip="Your saved folders and links" />
                    <RadioButton x:Name="RecentTab" Content="Recent"
                                 Style="{StaticResource RadioButton.Segment}" Tag="0" Checked="Tab_Checked"
                                 ToolTip="Folders from Recents and files from Recent Files, with the tracked folders" />
                    <RadioButton x:Name="SessionsTab" Content="Sessions"
                                 Style="{StaticResource RadioButton.Segment}" Tag="0" Checked="Tab_Checked"
                                 ToolTip="Files in your sessions" />
                    <RadioButton x:Name="AllTab" Content="All"
                                 Style="{StaticResource RadioButton.Segment}" Tag="0,5,5,0" BorderThickness="0" Checked="Tab_Checked"
                                 ToolTip="Everything, with where each item comes from" />
                </StackPanel>
            </Border>
        </GroupBox>

        <ContentControl x:Name="SavedHost" Grid.Row="1" Focusable="False" />
        <ContentControl x:Name="LibraryHost" Grid.Row="1" Focusable="False" Visibility="Collapsed" />
    </Grid>
</UserControl>
```

- [ ] **Step 2: `FilesPanel.xaml.cs`**

```csharp
using System.Windows;
using System.Windows.Controls;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

/// <summary>
/// The File viewer (File viewer design §3, §4): a tab strip over the places
/// table and the Library grid. It gives the shared <see cref="LibraryViewModel"/>
/// its tab only while it is on screen. Hidden, or while another layout shows,
/// the Library has no tab, so a Recents panel shows as it always did.
/// </summary>
public partial class FilesPanel : UserControl
{
    private readonly LibraryViewModel _library;

    public FilesPanel(PlacesPanel places, FileShelfPanel shelf, LibraryViewModel library)
    {
        _library = library;
        InitializeComponent();
        SavedHost.Content = places;
        LibraryHost.Content = shelf;
        IsVisibleChanged += (_, _) => ApplyTab();
        ApplyTab();
    }

    /// <summary>The tab chosen: null for Saved places, otherwise the Library's tab. Kept while the app runs, not stored (File viewer design §3).</summary>
    public LibraryTab? Tab { get; private set; }

    /// <summary>A session card's View session files (File viewer design §5): the Sessions tab, narrowed to that session.</summary>
    public void ShowSession(string sessionId)
    {
        SessionsTab.IsChecked = true;
        _library.ScopeToSession(sessionId);
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        Tab = sender == RecentTab ? LibraryTab.Recent
            : sender == SessionsTab ? LibraryTab.Sessions
            : sender == AllTab ? LibraryTab.All
            : null;
        ApplyTab();
    }

    /// <summary>
    /// Shows the chosen tab's view. While on screen, it also gives the Library
    /// that tab; on Saved places the Library gets All, so the year calendar
    /// counts everything.
    /// </summary>
    private void ApplyTab()
    {
        // The first tab is checked while the XAML loads, before the hosts exist.
        if (SavedHost is null || LibraryHost is null)
            return;

        SavedHost.Visibility = Tab is null ? Visibility.Visible : Visibility.Collapsed;
        LibraryHost.Visibility = Tab is null ? Visibility.Collapsed : Visibility.Visible;
        _library.Tab = IsVisible ? Tab ?? LibraryTab.All : null;
    }
}
```

- [ ] **Step 3: `WorkspaceView`: a list of shelves**

In `WorkspaceView.xaml.cs`:

1. Replace the field `private FileShelfPanel? _shelf;` with:

```csharp
    /// <summary>The Library grids made so far: a Recents panel's and the File viewer's (File viewer design §7).</summary>
    private readonly List<FileShelfPanel> _shelves = new();
    private FilesPanel? _filesPanel;

    /// <summary>The shelf on screen, if any: the one the search box's Down key moves into.</summary>
    private FileShelfPanel? ShownShelf => _shelves.FirstOrDefault(s => s.IsVisible);
```

2. In `BuildCanvas`, replace:

```csharp
        if (_shelf is not null)
            _shelf.ShowsPeriod = _workspace.Panels.All(p => p.Type != PanelTypes.Activity);
```

with:

```csharp
        var noCalendar = _workspace.Panels.All(p => p.Type != PanelTypes.Activity);
        foreach (var shelf in _shelves)
            shelf.ShowsPeriod = noCalendar;
```

3. In `CreateContent`, replace the whole `case PanelTypes.Shelf:` block with:

```csharp
            case PanelTypes.Shelf:
                return CreateShelf();

            case PanelTypes.Files:
                // The File viewer (File viewer design §3): Saved places and Recents as tabs, with Sessions and All.
                _filesPanel = new FilesPanel(new PlacesPanel { DataContext = _places, CollapsesWithWindow = false }, CreateShelf(), _workspace!.Library);
                return _filesPanel;
```

4. After `CreateContent`, add:

```csharp
    /// <summary>
    /// A Library grid with Recents' tracked folders and actions (Desk layout
    /// design §4): the Recents panel, or the File viewer's Recent, Sessions and
    /// All tabs.
    /// </summary>
    private FileShelfPanel CreateShelf()
    {
        var shelf = new FileShelfPanel { DataContext = _workspace!.Library, ShowsSearch = false, ShowsSaveAsSession = true };
        shelf.SaveAsSessionRequested += SaveShelfAsSession;
        shelf.AddAsPlaceRequested += AddAsPlace;
        var activity = new ActivityViewModel(_activityStore!, () =>
        {
            _activityHost!.RootsChanged();
            _trackingChanged?.Invoke();
            foreach (var each in _shelves)
                each.UpdateTracking();
            RequestReload();
        });
        shelf.AttachTracking(activity, _networkDrives!, () => ActivityFormat.TrackingSummary(
            _activityStore!.EnabledRoots().Count, _activityStore.Roots.Count, _activityHost!.IsPaused));
        _shelves.Add(shelf);
        return shelf;
    }
```

5. In `SearchBox_PreviewKeyDown`:
   - Change `_shelf?.FocusList();` to `ShownShelf?.FocusList();`.
   - Replace the `Key.Down` case body with:

```csharp
            case Key.Down:
                if (ShownShelf is { } shelf)
                {
                    shelf.FocusList();
                    e.Handled = true;
                }
                break;
```

6. Grep the file for any remaining `_shelf`. There should be none.

- [ ] **Step 4: Build and test**

Run: `dotnet build src/QuickerPlaces/QuickerPlaces.csproj`

Expected: 0 warnings, 0 errors.

Run: `dotnet test src/QuickerPlaces.Tests`

Expected: PASS, 1142.

- [ ] **Step 5: A quick look**

1. Build to a scratch folder.
2. Run `QuickerPlaces.exe --workspace --data-root <scratch>\data` and pick Desk.
3. With real clicks, check:
   - Files shows the four tabs, and Saved places shows the places table.
   - Recent shows the tracked-folder strip, Visits and Time, and no Show segment.
   - Sessions shows the SESSIONS column.
   - All shows SOURCE markers.
4. Pick Desk · separate panels. Its Recents panel shows the Show segment and every column as before.
5. Close the app by PID, after checking its command line holds your scratch path.

- [ ] **Step 6: Commit**

```bash
git add src/QuickerPlaces/Views/Panels/FilesPanel.xaml src/QuickerPlaces/Views/Panels/FilesPanel.xaml.cs src/QuickerPlaces/Views/WorkspaceView.xaml.cs
git commit -m "Show the Files panel in the workspace: the places table and the Library as tabs

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: View session files from a session card

This is WPF, so it has no unit tests. Build now; Task 7 verifies it on Windows.

**Files:**
- Modify: `src/QuickerPlaces/Views/Panels/SessionsPanel.xaml`
- Modify: `src/QuickerPlaces/Views/Panels/SessionsPanel.xaml.cs`
- Modify: `src/QuickerPlaces/Views/WorkspaceView.xaml.cs`

- [ ] **Step 1: The card menu**

In `SessionsPanel.xaml`, on the `<ListBox x:Name="SessionsList" …>` element, add the attribute `ContextMenuOpening="SessionsList_ContextMenuOpening"`. Then add this as its first child, before `<ListBox.ItemTemplate>`:

```xml
                        <!-- A card's menu (File viewer design §5). Right-click selects the card first, so it acts on the selected session. -->
                        <ListBox.ContextMenu>
                            <ContextMenu>
                                <MenuItem x:Name="ViewFilesMenuItem" Header="View session files" FontWeight="Bold"
                                          Click="ViewFiles_Click" Visibility="Collapsed" />
                                <MenuItem Header="Open all" Click="OpenAll_Click" />
                                <Separator />
                                <MenuItem Header="Edit…" Click="Edit_Click" IsEnabled="{Binding CanEditSelection}" />
                                <MenuItem Header="Delete…" Click="Delete_Click" IsEnabled="{Binding CanEditSelection}" />
                            </ContextMenu>
                        </ListBox.ContextMenu>
```

- [ ] **Step 2: Code-behind**

In `SessionsPanel.xaml.cs`, after `SessionsChanged`, add:

```csharp
    /// <summary>Raised by a card's View session files with the session's id: the workspace shows its files in the File viewer (File viewer design §5).</summary>
    public event Action<string>? ViewFilesRequested;

    /// <summary>Offers View session files on a card's menu. The workspace turns it on while a File viewer is shown.</summary>
    public bool ShowsViewFiles { get; set; }
```

After `OpenFile_Click`, add:

```csharp
    /// <summary>Opens a card's menu on that card only: empty space has no session to act on.</summary>
    private void SessionsList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_viewModel?.SelectedRow is null || (e.OriginalSource as FrameworkElement)?.DataContext is not SessionRowViewModel)
        {
            e.Handled = true;
            return;
        }

        ViewFilesMenuItem.Visibility = ShowsViewFiles ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ViewFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedRow is { } row)
            ViewFilesRequested?.Invoke(row.Id);
    }
```

- [ ] **Step 3: Wire it in the workspace**

In `WorkspaceView.xaml.cs`:
- In the `case PanelTypes.Sessions:` block, after `_sessionsPanel.SessionsChanged += RequestReload;`, add:

```csharp
                _sessionsPanel.ViewFilesRequested += id => _filesPanel?.ShowSession(id);
```

- In `BuildCanvas`, after the `foreach (var shelf in _shelves)` loop that Task 5 added, add:

```csharp
        if (_sessionsPanel is not null)
            _sessionsPanel.ShowsViewFiles = _workspace.Panels.Any(p => p.Type == PanelTypes.Files);
```

- [ ] **Step 4: Build and test**

Run: `dotnet build src/QuickerPlaces/QuickerPlaces.csproj`

Expected: 0 warnings, 0 errors.

Run: `dotnet test src/QuickerPlaces.Tests`

Expected: PASS, 1142.

- [ ] **Step 5: Commit**

```bash
git add src/QuickerPlaces/Views/Panels/SessionsPanel.xaml src/QuickerPlaces/Views/Panels/SessionsPanel.xaml.cs src/QuickerPlaces/Views/WorkspaceView.xaml.cs
git commit -m "Show a session's files in the File viewer from its card's menu

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Verify on Windows, and record it

Not code. Record every result in `ai/BUILD_SUMMARY.md`, then commit.

- **Setup:**
  - Build to a scratch folder and run against a scratch data root: `dotnet build src/QuickerPlaces/QuickerPlaces.csproj -o <scratch>\qpbin`, then `<scratch>\qpbin\QuickerPlaces.exe --workspace --data-root <scratch>\data-verify`.
  - Never use the real data, and never call `app.Run()` from a probe.
  - Seed the scratch data with:
    - two saved places, one of them a favourite;
    - two sessions with PDF, Word or Excel files that exist under `<scratch>`;
    - a tracked folder under `<scratch>` with a visited subfolder.
- **How to drive it:**
  - Find elements with UI Automation; click with real mouse input (`SetCursorPos` plus `mouse_event`); check with screenshots.
  - Bring the QuickerPlaces window to the front before each click sequence.
  - Close every instance you start by PID, after checking its command line holds your scratch path.

Checks:

- [ ] **Desk:**
  - Favourites and Sessions on the left; Files and Year activity on the right.
  - Files opens on Saved places.
  - No favourites strip, and no Recents button in the header.
- [ ] **Desk · separate panels:** as the Desk layout was before, with Saved places, Recents and Year activity. Switch back and forth twice. Each time, the Recents panel shows its Show segment and all its columns, and the Files tab you left is still chosen.
- [ ] **Saved places tab:**
  - Rename a place.
  - Toggle favourite: its card appears in Favourites.
  - The table's own search box filters it.
- [ ] **Recent tab:**
  - The tracked-folder chips scope the list and clear it.
  - Visits and Time are filled.
  - Group by Folder level works.
  - Add as place… opens the Add folder dialog prefilled.
- [ ] **Sessions tab:**
  - Every session's files, with the SESSIONS column.
  - Group by shows no Folder level.
- [ ] **All tab:**
  - SOURCE markers match each row's Where from, with its tooltip.
  - Tags show.
- [ ] **View session files:**
  - Right-click a session card and choose View session files: Files switches to Sessions, scoped, with the chip and the status line.
  - × clears the scope.
  - View session files on the other card re-scopes.
  - Choosing All clears the scope.
  - In Desk · separate panels, the card menu has no View session files.
- [ ] **Calendar:** a day picked in Year activity narrows Recent, Sessions and All. Saved places is unchanged.
- [ ] **Arrange:**
  - Add panel doesn't offer Saved places or Recents while Files is shown.
  - Hide Files: now they are offered.
  - Undo brings Files back.
- [ ] **Carried over from the Desk layout's Task 15:**
  - Edit tracking settings, Delete tracked folder (confirmation shown) and Retry save, if a save failure can be caused.
  - Drag a Favourites card to reorder; Ctrl+1.
  - The Library window in list mode (no `--workspace`, another scratch data root):
    - no tracked-folders strip or Add as place;
    - Folder level grouping works;
    - the Show segment and every column are there.
- [ ] **Themes:** in dark and in light, the tabs, chips, markers, Column choice and Favourites cards are readable.

In `ai/BUILD_SUMMARY.md`, add a section `## File viewer (2026-09-29)` above `## Desk layout, Favourites and Recents panels (2026-09-29)`. Use the same shape as that section:
- *What was built:* one bullet per task, each with its commit.
- *Decisions made while building:* a table.
- *Verification status:* the test count, then every check above as done (with its result) or not done (with the reason).

Record these decisions:
- The tab lives only while the app runs.
- Files excludes the Saved places and Recents panels in Add panel.
- The session scope is held by id.
- The Saved places tab keeps its own search.
- The File viewer gives the Library its tab only while on screen.

In the spec, change `Status:` to "Built (see ai/BUILD_SUMMARY.md)".

```bash
git add ai/BUILD_SUMMARY.md "ai/260929_File Viewer Design.md"
git commit -m "Record the File viewer's build and Windows checks

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
