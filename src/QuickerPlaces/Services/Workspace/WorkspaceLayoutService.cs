using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Models.Workspace;

namespace QuickerPlaces.Services.Workspace;

/// <summary>
/// Everything the workspace does with layouts, without WPF (configurable
/// canvas plan D1–D3, M1): which layout is shown and how it is arranged,
/// Arrange mode's draft with Done and Revert, the edits Arrange allows, the
/// built-in and user layouts with every preset operation, the startup
/// choice, Undo, and the query each layout remembers.
///
/// Three kinds of state, kept apart (D2):
///
/// - A layout's <b>definition</b>: a built-in's factory panels, or a user
///   layout's saved panels. Only Save changes rewrites a user definition;
///   nothing rewrites a built-in.
/// - Its <b>working arrangement</b>: how it is actually shown when that
///   differs, kept per layout so switching away and back, or restarting,
///   returns to it. Done keeps changes here; Restore saved layout clears them.
/// - Arrange mode's <b>draft</b>: the panels while arranging. Edits change
///   only the draft and never write; Done moves it into the working
///   arrangement and writes once, Revert throws it away.
///
/// Every change that should last is written at once through
/// <see cref="WorkspaceStore"/>, and its result returned to be shown. Query
/// changes are the exception (plan §4): <see cref="SetQuery"/> only marks
/// them, and the caller debounces <see cref="FlushPending"/>.
///
/// Used from the UI thread only. UI-free and linked into the test project.
/// </summary>
public sealed class WorkspaceLayoutService
{
    private readonly WorkspaceStore _store;
    private readonly List<UndoEntry> _undo = new();
    private WorkspaceDocument _document;
    private List<PanelInstance> _panels;
    private List<PanelInstance>? _arrangeSnapshot;
    private bool _queryChanged;

    public WorkspaceLayoutService(WorkspaceStore store)
    {
        _store = store;
        _document = store.Document.Clone();

        // Startup (D2): the last layout, or the chosen startup layout; either
        // way its working arrangement and remembered query when it has them.
        var startupId = _document.Startup.ResumesLast ? _document.ActivePresetId : _document.Startup.PresetId;
        ActivePresetId = IsUsable(startupId) ? startupId! : BuiltInLayouts.DefaultId;
        _panels = ShownPanels(ActivePresetId);
        Query = FindWorking(ActivePresetId)?.Query?.Clone() ?? SavedFilters(ActivePresetId);
    }

    // ---------------------------------------------------------------
    // What is shown
    // ---------------------------------------------------------------

    public string ActivePresetId { get; private set; }

    public bool ActiveIsBuiltIn => BuiltInLayouts.IsBuiltInId(ActivePresetId);

    /// <summary>True when the active layout is a user layout saved with filters, which Save changes replaces with the current query (D3).</summary>
    public bool ActiveHasFilters => FindPreset(ActivePresetId)?.Filters is not null;

    public string ActiveName => NameOf(ActivePresetId);

    /// <summary>The active layout's arrangement: <see cref="LayoutArrangements.Columns"/>, or null for rows (Desk layout design §2).</summary>
    public string? ActiveArrangement => ArrangementOf(ActivePresetId);

    public bool ActiveIsColumns => LayoutArrangements.IsColumns(ActiveArrangement);

    /// <summary>The panels shown, in order, hidden ones included. Copies: change them through this service.</summary>
    public IReadOnlyList<PanelInstance> Panels => _panels.Select(p => p.Clone()).ToList();

    /// <summary>The panels shown, without hidden ones, in order.</summary>
    public IReadOnlyList<PanelInstance> VisiblePanels => _panels.Where(p => !p.Hidden).Select(p => p.Clone()).ToList();

    /// <summary>True when the shown arrangement differs from the active layout's definition: the Modified indicator (D2).</summary>
    public bool IsModified => !SamePanels(_panels, DefinitionPanels(ActivePresetId));

    /// <summary>The shared query (D4). Replace it with <see cref="SetQuery"/>.</summary>
    public WorkspaceQuery Query { get; private set; }

    public StartupChoice Startup => _document.Startup.Clone();

    /// <summary>The built-ins the picker lists, in order.</summary>
    public IReadOnlyList<LayoutEntry> BuiltInEntries
        => BuiltInLayouts.Offered.Select(b => Entry(b.Id, b.Name, isBuiltIn: true)).ToList();

    /// <summary>My layouts, in the order they were made.</summary>
    public IReadOnlyList<LayoutEntry> UserEntries
        => _document.Presets.Select(p => Entry(p.Id, p.Name, isBuiltIn: false)).ToList();

    /// <summary>The panel types Add panel offers: available in this build, and not already shown.</summary>
    public IReadOnlyList<string> AddablePanelTypes
        => PanelTypes.Available.Where(t => !_panels.Any(p => p.Type == t && !p.Hidden)).ToList();

    // ---------------------------------------------------------------
    // Persistence state
    // ---------------------------------------------------------------

    /// <summary>The store's one line about loading, or null.</summary>
    public string? Notice => _store.Notice;

    public bool CanWrite => _store.CanWrite;

    /// <summary>True when the damaged file's backup can be restored (<see cref="RestoreBackup"/>).</summary>
    public bool HasBackup => _store.HasBackup;

    /// <summary>True while a change has not reached disk: a failed save, or a query change not yet flushed.</summary>
    public bool HasUnsavedChanges => _store.HasUnsavedChanges || _queryChanged;

    public PersistenceResult RetrySave() => _queryChanged ? Persist(keepUndo: true) : _store.RetrySave();

    /// <summary>Writes a query change that <see cref="SetQuery"/> left pending; nothing when there is none. Called on a debounce and at close.</summary>
    public PersistenceResult FlushPending() => _queryChanged ? Persist(keepUndo: true) : _store.RetrySave();

    /// <summary>Replaces every layout with the damaged file's backup, and shows the layout it was last on.</summary>
    public PersistenceResult RestoreBackup()
    {
        var restored = _store.RestoreBackup(out var persistence);
        if (restored is null)
            return persistence;

        _document = restored;
        _undo.Clear();
        _arrangeSnapshot = null;
        ActivePresetId = IsUsable(_document.ActivePresetId) ? _document.ActivePresetId! : BuiltInLayouts.DefaultId;
        _panels = ShownPanels(ActivePresetId);
        Query = FindWorking(ActivePresetId)?.Query?.Clone() ?? SavedFilters(ActivePresetId);
        _queryChanged = false;
        return persistence;
    }

    // ---------------------------------------------------------------
    // Query
    // ---------------------------------------------------------------

    /// <summary>Replaces the shared query. Not written until <see cref="FlushPending"/> or the next layout change (plan §4).</summary>
    public void SetQuery(WorkspaceQuery query)
    {
        if (query.SameAs(Query))
            return;

        Query = query.Clone();
        _queryChanged = true;
    }

    // ---------------------------------------------------------------
    // Switching layouts
    // ---------------------------------------------------------------

    /// <summary>
    /// Shows another layout (D2): keeps the current layout's working
    /// arrangement first, then shows the destination's working arrangement,
    /// or its definition when it has none. The query becomes the
    /// destination's saved filters, or the defaults: filters never leak from
    /// one layout to another (D3). A draft in progress is kept, as Done would.
    /// </summary>
    public PersistenceResult Activate(string id)
    {
        if (!IsUsable(id))
            return PersistenceResult.Fail("That layout isn't available.");
        if (id == ActivePresetId)
            return PersistenceResult.Ok();

        EndArrange(keepDraft: true);
        KeepWorking();
        ActivePresetId = id;
        _panels = ShownPanels(id);
        Query = SavedFilters(id);
        return Persist();
    }

    // ---------------------------------------------------------------
    // Arrange mode
    // ---------------------------------------------------------------

    public bool IsArranging => _arrangeSnapshot is not null;

    /// <summary>
    /// Starts Arrange mode, remembering the arrangement to go back to on
    /// Revert. Undo in Arrange mode steps back through the draft only, so an
    /// offer from before it (a Hide made outside, a Restore) ends here.
    /// </summary>
    public void BeginArrange()
    {
        if (IsArranging)
            return;

        _arrangeSnapshot = Clone(_panels);
        _undo.Clear();
    }

    /// <summary>Done: keeps the draft as the working arrangement and writes it once.</summary>
    public PersistenceResult Done()
    {
        if (!IsArranging)
            return PersistenceResult.Ok();

        EndArrange(keepDraft: true);
        return Persist();
    }

    /// <summary>Revert: puts back the arrangement Arrange mode started with. Nothing is written.</summary>
    public void Revert() => EndArrange(keepDraft: false);

    /// <summary>
    /// Moves a panel so it sits just before <paramref name="beforePanelId"/>,
    /// or last when that is null: one committed reorder, as a drop is (D1).
    /// False, changing nothing, outside Arrange mode or for an unknown panel.
    /// </summary>
    public bool MoveBefore(string panelId, string? beforePanelId)
    {
        if (!IsArranging || panelId == beforePanelId)
            return false;

        var panel = _panels.Find(p => p.Id == panelId);
        if (panel is null || (beforePanelId is not null && !_panels.Exists(p => p.Id == beforePanelId)))
            return false;

        var moved = Clone(_panels);
        var item = moved.Find(p => p.Id == panelId)!;
        moved.Remove(item);
        var at = beforePanelId is null ? moved.Count : moved.FindIndex(p => p.Id == beforePanelId);
        moved.Insert(at, item);
        return ApplyDraft(moved, $"Move {PanelTypes.DisplayName(panel.Type)}");
    }

    /// <summary>Keyboard Move earlier: swaps with the visible panel before it in its column (D1; Desk layout design §3).</summary>
    public bool MoveEarlier(string panelId)
    {
        var visible = VisibleInColumnOf(panelId);
        var index = visible.FindIndex(p => p.Id == panelId);
        return index > 0 && MoveBefore(panelId, visible[index - 1].Id);
    }

    /// <summary>Keyboard Move later: swaps with the visible panel after it in its column (D1; Desk layout design §3).</summary>
    public bool MoveLater(string panelId)
    {
        var visible = VisibleInColumnOf(panelId);
        var index = visible.FindIndex(p => p.Id == panelId);
        if (index < 0 || index >= visible.Count - 1)
            return false;

        return MoveBefore(panelId, index + 2 < visible.Count ? visible[index + 2].Id : null);
    }

    /// <summary>Sets a panel's width to one of the allowed spans. False for any other width.</summary>
    public bool SetSpan(string panelId, int span)
    {
        if (!IsArranging || !PanelSpans.IsAllowed(span))
            return false;

        var draft = Clone(_panels);
        var panel = draft.Find(p => p.Id == panelId);
        if (panel is null || panel.Span == span)
            return false;

        panel.Span = span;
        return ApplyDraft(draft, $"Resize {PanelTypes.DisplayName(panel.Type)}");
    }

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

    /// <summary>Hides a panel. Presentation only: whatever it shows is untouched (D1, D6). Undo brings it back.</summary>
    public bool Hide(string panelId)
    {
        if (!IsArranging)
            return false;

        var draft = Clone(_panels);
        var panel = draft.Find(p => p.Id == panelId);
        if (panel is null || panel.Hidden)
            return false;

        panel.Hidden = true;
        return ApplyDraft(draft, $"Hide {PanelTypes.DisplayName(panel.Type)}");
    }

    /// <summary>
    /// Add panel: shows a hidden panel of this type where it was, or adds one
    /// at the end at its default width. Only types this build has (D6).
    /// </summary>
    public bool AddPanel(string type)
    {
        if (!IsArranging || !AddablePanelTypes.Contains(type))
            return false;

        var draft = Clone(_panels);
        var hidden = draft.Find(p => p.Type == type && p.Hidden);
        if (hidden is not null)
        {
            hidden.Hidden = false;
        }
        else
        {
            var ids = draft.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            draft.Add(new PanelInstance
            {
                Id = WorkspaceValidation.NewPanelId(type, ids),
                Type = type,
                Span = PanelTypes.DefaultSpan(type),
                Dock = ActiveIsColumns ? PanelDocks.Main : null,
            });
        }

        return ApplyDraft(draft, $"Add {PanelTypes.DisplayName(type)}");
    }

    /// <summary>
    /// Hide outside Arrange mode (M4): one step, kept and written at once,
    /// with Undo. In Arrange mode it is a draft edit, as <see cref="Hide"/>.
    /// </summary>
    public bool HideNow(string panelId, out PersistenceResult persistence) => EditNow(() => Hide(panelId), out persistence);

    /// <summary>Add panel outside Arrange mode (M4): one step, kept and written at once, with Undo. In Arrange mode, a draft edit.</summary>
    public bool AddPanelNow(string type, out PersistenceResult persistence) => EditNow(() => AddPanel(type), out persistence);

    /// <summary>
    /// Restore saved layout (D2): shows the active layout's definition again,
    /// clearing its working changes (for a built-in, Restore built-in layout).
    /// In Arrange mode it changes the draft; otherwise it is written at once.
    /// Either way Undo brings the changes back.
    /// </summary>
    public PersistenceResult RestoreSaved()
    {
        var definition = DefinitionPanels(ActivePresetId);
        if (SamePanels(_panels, definition))
            return PersistenceResult.Ok();

        var label = ActiveIsBuiltIn ? "Restore built-in layout" : "Restore saved layout";
        if (IsArranging)
        {
            ApplyDraft(definition, label);
            return PersistenceResult.Ok();
        }

        PushUndo(label);
        _panels = definition;
        return Persist(keepUndo: true);
    }

    // ---------------------------------------------------------------
    // Saving and managing layouts
    // ---------------------------------------------------------------

    /// <summary>
    /// Save changes: replaces the active user layout's definition with what is
    /// shown, and ends Arrange mode. A layout saved with filters has them
    /// replaced by the current query; one saved without keeps none. Built-in
    /// layouts are refused: save them as a new layout (D2).
    /// </summary>
    public ValidationResult SaveChanges(out PersistenceResult persistence)
    {
        persistence = PersistenceResult.Ok();
        var preset = FindPreset(ActivePresetId);
        if (preset is null)
            return ValidationResult.Fail("Built-in layouts can't be changed. Save it as a new layout instead.");

        EndArrange(keepDraft: true);
        preset.Panels = Clone(_panels);
        if (preset.Filters is not null)
            preset.Filters = Query.Clone();

        persistence = Persist();
        return ValidationResult.Ok();
    }

    /// <summary>
    /// Save as new: a new user layout with the panels shown and, only when
    /// <paramref name="includeFilters"/> is ticked, the current query (D3).
    /// It becomes the active layout. A draft in progress goes to the new
    /// layout; the layout it was made from keeps the arrangement it had when
    /// Arrange mode began.
    /// </summary>
    public ValidationResult SaveAsNew(string? name, bool includeFilters, out string? newId, out PersistenceResult persistence)
        => SaveAsNew(name, includeFilters ? Query : null, out newId, out persistence);

    /// <summary>
    /// Save as new with the filters to keep, or none (M5): the Save dialog
    /// may keep a chosen week as "this week". The new layout then shows
    /// exactly what it saved.
    /// </summary>
    public ValidationResult SaveAsNew(string? name, WorkspaceQuery? filters, out string? newId, out PersistenceResult persistence)
    {
        newId = null;
        persistence = PersistenceResult.Ok();
        var validation = WorkspaceValidation.ValidateName(name, _document.Presets, null, out var cleanName);
        if (!validation.Success)
            return validation;

        var arrangement = ActiveArrangement;
        var shown = Clone(_panels);
        EndArrange(keepDraft: false);
        KeepWorking();

        var preset = new LayoutPreset
        {
            Id = NewPresetId(),
            Name = cleanName,
            Arrangement = arrangement,
            Panels = shown,
            Filters = filters?.Clone(),
        };
        _document.Presets.Add(preset);
        ActivePresetId = preset.Id;
        _panels = Clone(shown);
        if (filters is not null)
            Query = filters.Clone();

        newId = preset.Id;
        persistence = Persist();
        return ValidationResult.Ok();
    }

    /// <summary>
    /// The name Save as new suggests: "My Activity Atlas" from a built-in,
    /// "Mine copy" from a user layout (M5). Always unused.
    /// </summary>
    public string SuggestedNewName()
    {
        var baseName = ActiveIsBuiltIn ? $"My {ActiveName}" : ActiveName;
        return _document.Presets.Any(p => string.Equals(p.Name, baseName, StringComparison.OrdinalIgnoreCase))
            ? WorkspaceValidation.UniqueName(baseName, _document.Presets)
            : baseName;
    }

    /// <summary>Renames a user layout. Ids stay, so nothing that refers to it changes (D2).</summary>
    public ValidationResult Rename(string id, string? name, out PersistenceResult persistence)
    {
        persistence = PersistenceResult.Ok();
        var preset = FindPreset(id);
        if (preset is null)
            return ValidationResult.Fail(BuiltInLayouts.IsBuiltInId(id) ? "Built-in layouts can't be renamed." : "That layout no longer exists.");

        var validation = WorkspaceValidation.ValidateName(name, _document.Presets, id, out var cleanName);
        if (!validation.Success)
            return validation;
        if (cleanName == preset.Name)
            return ValidationResult.Ok();

        preset.Name = cleanName;
        persistence = Persist();
        return ValidationResult.Ok();
    }

    /// <summary>Duplicate: a new user layout copied from any layout's saved definition and filters, named "… copy". Not shown.</summary>
    public ValidationResult Duplicate(string id, out string? newId, out PersistenceResult persistence)
    {
        newId = null;
        persistence = PersistenceResult.Ok();
        if (!IsUsable(id))
            return ValidationResult.Fail("That layout no longer exists.");

        var preset = new LayoutPreset
        {
            Id = NewPresetId(),
            Name = WorkspaceValidation.UniqueName(NameOf(id), _document.Presets),
            Arrangement = ArrangementOf(id),
            Panels = DefinitionPanels(id),
            Filters = FindPreset(id)?.Filters?.Clone(),
        };
        _document.Presets.Add(preset);

        newId = preset.Id;
        persistence = Persist();
        return ValidationResult.Ok();
    }

    /// <summary>
    /// Deletes a user layout, with Undo. When it was shown, Activity Atlas is
    /// shown instead; when it was the startup layout, startup resumes the
    /// last layout. Both in the same write (D2). Sessions, collections,
    /// searches and files are never touched: a layout only arranges them.
    /// </summary>
    public ValidationResult Delete(string id, out PersistenceResult persistence)
    {
        persistence = PersistenceResult.Ok();
        var preset = FindPreset(id);
        if (preset is null)
            return ValidationResult.Fail(BuiltInLayouts.IsBuiltInId(id) ? "Built-in layouts can't be deleted." : "That layout no longer exists.");

        if (id == ActivePresetId)
            EndArrange(keepDraft: false);

        PushUndo($"Delete \"{preset.Name}\"");
        _document.Presets.Remove(preset);
        _document.Working.RemoveAll(w => w.PresetId == id);
        if (_document.Startup.PresetId == id)
            _document.Startup = StartupChoice.Resume();

        if (id == ActivePresetId)
        {
            ActivePresetId = BuiltInLayouts.DefaultId;
            _panels = ShownPanels(ActivePresetId);
            Query = SavedFilters(ActivePresetId);
        }

        persistence = Persist(keepUndo: true);
        return ValidationResult.Ok();
    }

    /// <summary>Set as startup layout, or back to resuming the last one (D2).</summary>
    public ValidationResult SetStartup(StartupChoice choice, out PersistenceResult persistence)
    {
        persistence = PersistenceResult.Ok();
        if (!choice.ResumesLast && !IsUsable(choice.PresetId))
            return ValidationResult.Fail("That layout isn't available.");

        _document.Startup = choice.ResumesLast ? StartupChoice.Resume() : StartupChoice.ForPreset(choice.PresetId!);
        persistence = Persist();
        return ValidationResult.Ok();
    }

    // ---------------------------------------------------------------
    // Undo
    // ---------------------------------------------------------------

    public bool CanUndo => _undo.Count > 0;

    /// <summary>What Undo would undo ("Hide Sessions"), or null.</summary>
    public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;

    /// <summary>
    /// Undoes the last edit. A draft edit in Arrange mode changes only the
    /// draft, and nothing is written; a deletion or restore puts every layout
    /// back as it was and writes that.
    /// </summary>
    public PersistenceResult Undo()
    {
        if (_undo.Count == 0)
            return PersistenceResult.Ok();

        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        if (entry.Document is null)
        {
            _panels = Clone(entry.Panels);
            return PersistenceResult.Ok();
        }

        _document = entry.Document.Clone();
        ActivePresetId = entry.ActivePresetId;
        Query = entry.Query.Clone();
        if (!IsArranging)
            _panels = ShownPanels(ActivePresetId);
        return Persist(keepUndo: true);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    /// <summary>A layout that exists and can be shown: an offered built-in, or any user layout.</summary>
    private bool IsUsable(string? id)
        => id is not null && (BuiltInLayouts.Find(id) is { IsOffered: true } || FindPreset(id) is not null);

    private LayoutPreset? FindPreset(string? id) => _document.Presets.Find(p => p.Id == id);

    private WorkingArrangement? FindWorking(string id) => _document.Working.Find(w => w.PresetId == id);

    private string NameOf(string id) => BuiltInLayouts.Find(id)?.Name ?? FindPreset(id)?.Name ?? "";

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

    /// <summary>A fresh copy of a layout's definition: factory panels for a built-in, saved panels for a user layout.</summary>
    private List<PanelInstance> DefinitionPanels(string id)
        => BuiltInLayouts.Find(id)?.CreatePanels() ?? Clone(FindPreset(id)?.Panels ?? new List<PanelInstance>());

    /// <summary>The working arrangement when there is one, otherwise the definition.</summary>
    private List<PanelInstance> ShownPanels(string id)
        => FindWorking(id)?.Panels is { } working ? Clone(working) : DefinitionPanels(id);

    /// <summary>A copy of the layout's saved filters, or the defaults (D3).</summary>
    private WorkspaceQuery SavedFilters(string id) => FindPreset(id)?.Filters?.Clone() ?? WorkspaceQuery.Default;

    private LayoutEntry Entry(string id, string name, bool isBuiltIn)
    {
        var shown = id == ActivePresetId ? _panels : ShownPanels(id);
        return new LayoutEntry(id, name, isBuiltIn, id == ActivePresetId,
            !SamePanels(shown, DefinitionPanels(id)),
            !_document.Startup.ResumesLast && _document.Startup.PresetId == id,
            FindPreset(id)?.Filters is not null);
    }

    /// <summary>
    /// Records the active layout's arrangement and query in its working
    /// record (outside Arrange mode; in it, the arrangement Arrange began
    /// with, since a draft is never written). A record that says nothing is dropped.
    /// </summary>
    private void KeepWorking()
    {
        var shown = _arrangeSnapshot ?? _panels;
        var changed = !SamePanels(shown, DefinitionPanels(ActivePresetId));
        var query = Query.IsDefault ? null : Query.Clone();

        var working = FindWorking(ActivePresetId);
        if (!changed && query is null)
        {
            if (working is not null)
                _document.Working.Remove(working);
            return;
        }

        if (working is null)
        {
            working = new WorkingArrangement { PresetId = ActivePresetId };
            _document.Working.Add(working);
        }

        working.Panels = changed ? Clone(shown) : null;
        working.BuiltInVersion = changed ? BuiltInLayouts.Find(ActivePresetId)?.Version : null;
        working.Query = query;
    }

    /// <summary>
    /// Writes every layout, with the active one's working arrangement and
    /// query. Undo offers only the change just made: any other change that
    /// lasts ends it, unless <paramref name="keepUndo"/> (the undoable changes
    /// themselves, Undo, and a debounced query write).
    /// </summary>
    private PersistenceResult Persist(bool keepUndo = false)
    {
        if (!keepUndo)
            _undo.RemoveAll(u => u.Document is not null);
        KeepWorking();
        _document.ActivePresetId = ActivePresetId;
        _queryChanged = false;
        return _store.Save(_document);
    }

    private void EndArrange(bool keepDraft)
    {
        if (!IsArranging)
            return;

        if (!keepDraft)
            _panels = _arrangeSnapshot!;
        _arrangeSnapshot = null;
        _undo.RemoveAll(u => u.Document is null);
    }

    /// <summary>
    /// Runs one Arrange edit as a finished step: every layout is remembered
    /// for Undo, the edit is made to a draft and the draft is kept and
    /// written. Undo then offers it by the edit's own name ("Hide Sessions").
    /// </summary>
    private bool EditNow(Func<bool> edit, out PersistenceResult persistence)
    {
        persistence = PersistenceResult.Ok();
        if (IsArranging)
            return edit();

        PushUndo("");
        var undoAt = _undo.Count - 1;
        BeginArrangeKeepingUndo();
        if (!edit())
        {
            EndArrange(keepDraft: false);
            _undo.RemoveAt(undoAt);
            return false;
        }

        var label = _undo[^1].Label;
        EndArrange(keepDraft: true);
        _undo[undoAt] = _undo[undoAt] with { Label = label };
        persistence = Persist(keepUndo: true);
        return true;
    }

    private void BeginArrangeKeepingUndo() => _arrangeSnapshot = Clone(_panels);

    private bool ApplyDraft(List<PanelInstance> draft, string label)
    {
        if (SamePanels(draft, _panels))
            return false;

        _undo.Add(new UndoEntry(label, Clone(_panels), null, ActivePresetId, Query.Clone()));
        _panels = draft;
        return true;
    }

    /// <summary>Remembers every layout as it is now, the shown arrangement included, for Undo.</summary>
    private void PushUndo(string label)
    {
        KeepWorking();
        _document.ActivePresetId = ActivePresetId;
        _undo.Add(new UndoEntry(label, Clone(_panels), _document.Clone(), ActivePresetId, Query.Clone()));
    }

    private string NewPresetId()
    {
        var taken = _document.Presets.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        return WorkspaceValidation.NewId(taken);
    }

    private static List<PanelInstance> Clone(IEnumerable<PanelInstance> panels) => panels.Select(p => p.Clone()).ToList();

    private static bool SamePanels(IReadOnlyList<PanelInstance> a, IReadOnlyList<PanelInstance> b)
        => a.Count == b.Count && a.Zip(b).All(pair => pair.First.SameAs(pair.Second));

    /// <summary>A draft edit (no <paramref name="Document"/>: only the panels go back) or a change to the layouts themselves.</summary>
    private sealed record UndoEntry(string Label, List<PanelInstance> Panels, WorkspaceDocument? Document, string ActivePresetId, WorkspaceQuery Query);
}

/// <summary>One row of the layout picker (D2).</summary>
public sealed record LayoutEntry(
    string Id,
    string Name,
    bool IsBuiltIn,
    bool IsActive,
    bool IsModified,
    bool IsStartup,
    bool HasFilters)
{
    /// <summary>The picker's heading for this row: "Built-in" or "My layouts" (M5).</summary>
    public string Group => IsBuiltIn ? "Built-in" : "My layouts";

    /// <summary>"Modified · Starts here · Filters": what the picker says beside the name, or "".</summary>
    public string Notes => string.Join(" · ", new[]
    {
        IsModified ? "Modified" : null,
        IsStartup ? "Starts here" : null,
        HasFilters ? "Filters" : null,
    }.Where(n => n is not null));
}
