using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Models.Workspace;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Services.Workspace;

namespace QuickerPlaces.ViewModels;

/// <summary>
/// The workspace in the main window (configurable canvas plan M3, M4, D1,
/// D4): which layout is shown and where its panels go at the width
/// available, the one shared query, and arranging: Arrange mode's moves,
/// widths, Hide and Add panel with Undo, Done and Revert; outside it, Hide
/// and Add panel as single steps that Undo takes back. Every change is
/// described in <see cref="Status"/>, which the view announces. And (M5,
/// D2, D3) the user's own layouts: Save changes, Save as new with or
/// without the filters, Rename, Duplicate, Delete with Undo, the startup
/// layout, and restoring the layouts from their backup after damage.
///
/// Owns the connection between <see cref="WorkspaceLayoutService"/> and the
/// shared <see cref="LibraryViewModel"/>: the layout's query is applied to
/// the Library at start and on every switch, and every change the user makes
/// to the Library's query is handed back to the layout service, whose write
/// the view debounces (<see cref="QueryPending"/>, <see cref="FlushPending"/>).
///
/// UI-free and linked into the test project; the view builds the panels.
/// </summary>
public sealed class WorkspaceViewModel : ObservableObject
{
    private readonly WorkspaceLayoutService _layout;
    private readonly TimeProvider _time;
    private readonly CultureInfo _culture;
    private IReadOnlyList<WorkspacePanelViewModel> _panels = Array.Empty<WorkspacePanelViewModel>();
    private double _width = double.PositiveInfinity;
    private string? _layoutMessage;
    private bool _canRetry;
    private string? _status;

    public WorkspaceViewModel(WorkspaceLayoutService layout, LibraryViewModel library, TimeProvider? time = null, CultureInfo? culture = null)
    {
        _layout = layout;
        _time = time ?? TimeProvider.System;
        _culture = culture ?? CultureInfo.CurrentCulture;
        Library = library;

        // Resume the query this layout was last used with (D3).
        Library.ApplyQuery(_layout.Query);
        Library.QueryChanged += OnLibraryQueryChanged;
        Library.PropertyChanged += OnLibraryPropertyChanged;

        _layoutMessage = _layout.Notice;
        RebuildPanels();
    }

    /// <summary>The shared Library query and results the Year activity and File shelf panels show.</summary>
    public LibraryViewModel Library { get; }

    /// <summary>Raised after the shown panels change: another layout, a panel added or hidden.</summary>
    public event Action? PanelsChanged;

    /// <summary>Raised when a query change is waiting to be written; the view calls <see cref="FlushPending"/> after a pause.</summary>
    public event Action? QueryPending;

    // ---------------------------------------------------------------
    // Search (D4): the shell's box is the Library's search
    // ---------------------------------------------------------------

    public string SearchText
    {
        get => Library.SearchText;
        set => Library.SearchText = value ?? "";
    }

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    // ---------------------------------------------------------------
    // Layouts
    // ---------------------------------------------------------------

    /// <summary>The layout picker's rows: the built-ins offered today, then My layouts.</summary>
    public IReadOnlyList<LayoutEntry> Layouts => _layout.BuiltInEntries.Concat(_layout.UserEntries).ToList();

    public LayoutEntry? SelectedLayout
    {
        get => Layouts.FirstOrDefault(l => l.Id == _layout.ActivePresetId);
        set
        {
            if (value is null || value.Id == _layout.ActivePresetId)
                return;

            var wasArranging = _layout.IsArranging;
            var persistence = _layout.Activate(value.Id);
            // Filters never leak between layouts (D3): the new layout's own, or the defaults.
            Library.ApplyQuery(_layout.Query);
            Report(persistence);
            // A draft in progress was kept, as Done would (D2).
            Status = wasArranging ? "Kept your arrangement and left Arrange mode." : null;
            RebuildPanels();
        }
    }

    public string ActiveLayoutName => _layout.ActiveName;

    // ---------------------------------------------------------------
    // Panels
    // ---------------------------------------------------------------

    /// <summary>The shown panels in order, each with its row, column and span at the width available.</summary>
    public IReadOnlyList<WorkspacePanelViewModel> Panels => _panels;

    public bool HasPanels => _panels.Count > 0;

    /// <summary>True when the active layout is in two columns (Desk layout design §2).</summary>
    public bool IsColumns => _layout.ActiveIsColumns;

    /// <summary>
    /// The canvas's width in device-independent pixels: panels too narrow
    /// there are shown wider, so they stack (D1). Presentation only; nothing
    /// is written and stored widths don't change.
    /// </summary>
    public void Reflow(double width)
    {
        if (double.IsNaN(width) || width <= 0 || Math.Abs(width - _width) < 0.5)
            return;

        _width = width;
        var placements = Pack();
        if (placements.SequenceEqual(_panels.Select(p => p.Placement)))
            return;

        RebuildPanels();
    }

    /// <summary>The panels Add panel offers: available in this build and not shown now.</summary>
    public IReadOnlyList<PanelChoice> AddablePanels
        => _layout.AddablePanelTypes.Select(t => new PanelChoice(t, PanelTypes.DisplayName(t))).ToList();

    public bool CanAddPanel => _layout.AddablePanelTypes.Count > 0;

    /// <summary>
    /// Adds a panel, or shows a hidden one where it was. In Arrange mode it
    /// is part of the draft; otherwise it is kept at once, and Undo takes it back.
    /// </summary>
    public bool AddPanel(string type)
    {
        var arranging = IsArranging;
        if (!_layout.AddPanelNow(type, out var persistence))
            return false;

        if (!arranging)
            Report(persistence);
        Status = $"Added {PanelTypes.DisplayName(type)}.";
        RebuildPanels();
        return true;
    }

    /// <summary>
    /// Hides a panel (presentation only, D1, D6): part of the draft in
    /// Arrange mode, otherwise kept at once. Undo or Add panel brings it back.
    /// </summary>
    public bool HidePanel(string panelId)
    {
        var type = _panels.FirstOrDefault(p => p.Id == panelId)?.Type;
        var arranging = IsArranging;
        if (type is null || !_layout.HideNow(panelId, out var persistence))
            return false;

        if (!arranging)
            Report(persistence);
        Status = $"Hid {PanelTypes.DisplayName(type)}. Undo or Add panel brings it back.";
        RebuildPanels();
        return true;
    }

    // ---------------------------------------------------------------
    // Arrange mode (M4, D1, D2)
    // ---------------------------------------------------------------

    public bool IsArranging => _layout.IsArranging;

    /// <summary>True when what is shown differs from the layout's definition: Restore can put it back (D2).</summary>
    public bool IsModified => _layout.IsModified;

    /// <summary>"Restore built-in layout" or "Restore saved layout", as the active layout is.</summary>
    public string RestoreLabel => _layout.ActiveIsBuiltIn ? "Restore built-in layout" : "Restore saved layout";

    public bool CanUndo => _layout.CanUndo;

    /// <summary>"Undo Hide Sessions", or plain "Undo" when there is nothing to undo.</summary>
    public string UndoText => _layout.UndoLabel is { } label ? $"Undo {label}" : "Undo";

    /// <summary>Starts Arrange mode: moves, widths, Hide and Add panel change a draft until Done or Revert.</summary>
    public void BeginArrange()
    {
        if (IsArranging)
            return;

        _layout.BeginArrange();
        Status = "Arranging. Drag a panel by its handle, or use its arrows and width. Done keeps the changes; Revert puts back how it was.";
        NotifyArrange();
    }

    /// <summary>Done: keeps the arrangement and writes it once.</summary>
    public void Done()
    {
        if (!IsArranging)
            return;

        Report(_layout.Done());
        Status = "Kept the new arrangement.";
        RebuildPanels();
    }

    /// <summary>Revert: puts back the arrangement Arrange mode started with. Nothing is written.</summary>
    public void Revert()
    {
        if (!IsArranging)
            return;

        _layout.Revert();
        Status = "Put back the arrangement you started with.";
        RebuildPanels();
    }

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

    /// <summary>
    /// A drop: <paramref name="draggedId"/> goes before or after
    /// <paramref name="targetId"/>, as one reorder. False, changing nothing,
    /// when that is where it already is.
    /// </summary>
    public bool Drop(string draggedId, string targetId, bool after)
    {
        var placements = _panels.Select(p => p.Placement).ToList();
        if (PanelLayoutEngine.Drop(placements, draggedId, targetId, after) is not { } drop ||
            !_layout.MoveBefore(draggedId, drop.BeforePanelId))
            return false;

        var target = _panels.First(p => p.Id == targetId).Title;
        Moved(draggedId, after ? $"after {target}" : $"before {target}");
        return true;
    }

    /// <summary>Sets a panel's own width to an allowed span, from its width choice or its dragged edge.</summary>
    public bool SetSpan(string panelId, int span)
    {
        var panel = _panels.FirstOrDefault(p => p.Id == panelId);
        if (panel is null || !_layout.SetSpan(panelId, span))
            return false;

        RebuildPanels();
        var shown = _panels.First(p => p.Id == panelId);
        Status = shown.IsWidened
            ? $"{panel.Title} is now {PanelSpans.DisplayName(span)} wide; shown {PanelSpans.DisplayName(shown.Span)} until the window is wider."
            : $"{panel.Title} is now {PanelSpans.DisplayName(span)} wide.";
        return true;
    }

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

    /// <summary>Restore built-in (or saved) layout: the layout's definition again, with Undo (D2).</summary>
    public void RestoreSaved()
    {
        if (!IsModified)
            return;

        var label = RestoreLabel;
        var arranging = IsArranging;
        var persistence = _layout.RestoreSaved();
        if (!arranging)
            Report(persistence);
        Status = $"{label}: done. Undo brings your arrangement back.";
        RebuildPanels();
    }

    /// <summary>A drag ended with Esc: nothing moved or changed width, and the line says so.</summary>
    public void DragCancelled() => Status = "Cancelled. Nothing moved.";

    /// <summary>Undoes the last arrangement step: a draft edit in Arrange mode, or a Hide, Add panel or Restore outside it.</summary>
    public void Undo()
    {
        if (!CanUndo)
            return;

        var label = _layout.UndoLabel;
        var active = _layout.ActivePresetId;
        var arranging = IsArranging;
        var persistence = _layout.Undo();
        if (!arranging)
        {
            // Undo puts back layouts, not searches: the Library keeps the query
            // shown unless Undo showed another layout, whose own query applies.
            if (_layout.ActivePresetId != active)
                Library.ApplyQuery(_layout.Query);
            else
                OnLibraryQueryChanged();
            Report(persistence);
        }

        Status = $"Undid {label}.";
        RebuildPanels();
    }

    private int IndexOf(string panelId)
    {
        for (var i = 0; i < _panels.Count; i++)
        {
            if (_panels[i].Id == panelId)
                return i;
        }

        return -1;
    }

    private void Moved(string panelId, string where)
    {
        var title = _panels.First(p => p.Id == panelId).Title;
        RebuildPanels();
        Status = $"Moved {title} {where}.";
    }

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

    private void NotifyArrange()
    {
        OnPropertyChanged(nameof(ActiveIsBuiltIn));
        OnPropertyChanged(nameof(IsUserLayout));
        OnPropertyChanged(nameof(StartupOptions));
        OnPropertyChanged(nameof(CanRestoreBackup));
        OnPropertyChanged(nameof(IsArranging));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(RestoreLabel));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoText));
    }

    // ---------------------------------------------------------------
    // My layouts (M5, D2, D3)
    // ---------------------------------------------------------------

    public bool ActiveIsBuiltIn => _layout.ActiveIsBuiltIn;

    /// <summary>Save changes, Rename and Delete are for the user's own layouts; a built-in is saved as a new one.</summary>
    public bool IsUserLayout => !_layout.ActiveIsBuiltIn;

    /// <summary>What the picker's Startup choice lists: resuming the last layout, then every layout.</summary>
    public IReadOnlyList<StartupOption> StartupOptions
    {
        get
        {
            var startup = _layout.Startup;
            var options = new List<StartupOption> { new(null, "The layout I used last", startup.ResumesLast) };
            options.AddRange(Layouts.Select(l => new StartupOption(l.Id, l.Name, l.IsStartup)));
            return options;
        }
    }

    /// <summary>The Save as new dialog, with a suggested name and the query shown now.</summary>
    public SaveLayoutViewModel NewSaveAs()
        => SaveLayoutViewModel.ForSaveAsNew(_layout.SuggestedNewName(), Library.CurrentQuery, _time, _culture);

    /// <summary>
    /// Save as new (D2, D3): a new user layout with the panels shown and, if
    /// ticked, the filters, which it then shows. False, with the reason in
    /// the dialog, when the name is refused.
    /// </summary>
    public bool SaveAsNew(SaveLayoutViewModel dialog)
    {
        var filters = dialog.FiltersToSave();
        var result = _layout.SaveAsNew(dialog.Name, filters, out _, out var persistence);
        if (!result.Success)
        {
            dialog.ErrorMessage = result.ErrorMessage;
            return false;
        }

        if (filters is not null)
            Library.ApplyQuery(_layout.Query);
        Report(persistence);
        Status = filters is null
            ? $"Saved “{ActiveLayoutName}” in My layouts."
            : $"Saved “{ActiveLayoutName}” in My layouts, with its filters.";
        RebuildPanels();
        return true;
    }

    /// <summary>
    /// Save changes: the shown arrangement becomes the active user layout's
    /// own. A layout saved with filters takes the current ones (D3).
    /// </summary>
    public void SaveChanges()
    {
        var name = ActiveLayoutName;
        var hadFilters = _layout.ActiveHasFilters;
        var result = _layout.SaveChanges(out var persistence);
        if (!result.Success)
        {
            Status = result.ErrorMessage;
            return;
        }

        Report(persistence);
        Status = hadFilters ? $"Saved changes to “{name}”, with the filters shown now." : $"Saved changes to “{name}”.";
        RebuildPanels();
    }

    /// <summary>The Rename dialog, for the active user layout.</summary>
    public SaveLayoutViewModel NewRename() => SaveLayoutViewModel.ForRename(ActiveLayoutName);

    public bool Rename(SaveLayoutViewModel dialog)
    {
        var result = _layout.Rename(_layout.ActivePresetId, dialog.Name, out var persistence);
        if (!result.Success)
        {
            dialog.ErrorMessage = result.ErrorMessage;
            return false;
        }

        Report(persistence);
        Status = $"Renamed to “{ActiveLayoutName}”.";
        RebuildPanels();
        return true;
    }

    /// <summary>Duplicate: a copy of the active layout as saved, in My layouts, named "… copy". The layout shown stays.</summary>
    public void Duplicate()
    {
        var result = _layout.Duplicate(_layout.ActivePresetId, out var newId, out var persistence);
        if (!result.Success)
        {
            Status = result.ErrorMessage;
            return;
        }

        Report(persistence);
        Status = $"Made “{Layouts.First(l => l.Id == newId).Name}” in My layouts.";
        RebuildPanels();
    }

    /// <summary>
    /// Deletes the active user layout, with Undo: Activity Atlas is shown
    /// instead, with its own filters. Sessions, places and files are untouched (D2).
    /// </summary>
    public void Delete()
    {
        var name = ActiveLayoutName;
        var result = _layout.Delete(_layout.ActivePresetId, out var persistence);
        if (!result.Success)
        {
            Status = result.ErrorMessage;
            return;
        }

        Library.ApplyQuery(_layout.Query);
        Report(persistence);
        Status = $"Deleted “{name}”. Undo brings it back.";
        RebuildPanels();
    }

    /// <summary>The layout QuickerPlaces starts with: a layout's id, or null for the one used last (D2).</summary>
    public void SetStartup(string? presetId)
    {
        var choice = presetId is null ? StartupChoice.Resume() : StartupChoice.ForPreset(presetId);
        var result = _layout.SetStartup(choice, out var persistence);
        if (!result.Success)
        {
            Status = result.ErrorMessage;
            return;
        }

        Report(persistence);
        Status = presetId is null
            ? "QuickerPlaces will start with the layout you used last."
            : $"QuickerPlaces will start with “{Layouts.First(l => l.Id == presetId).Name}”.";
        RebuildPanels();
    }

    /// <summary>True when the layouts file was damaged and its last good copy can be put back.</summary>
    public bool CanRestoreBackup => _layout.HasBackup;

    /// <summary>Puts every layout back from the last good copy, offered after damage and never done unasked.</summary>
    public void RestoreBackup()
    {
        var persistence = _layout.RestoreBackup();
        Library.ApplyQuery(_layout.Query);
        Report(persistence);
        Status = persistence.Saved ? "Your layouts are back as they were last saved." : null;
        RebuildPanels();
    }

    // ---------------------------------------------------------------
    // Feedback and persistence
    // ---------------------------------------------------------------

    /// <summary>What happened to the last layout action, briefly; null when there is nothing to say.</summary>
    public string? Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>A problem with the saved layouts: the store's notice, or a write that failed. Null when all is well.</summary>
    public string? LayoutMessage
    {
        get => _layoutMessage;
        private set => SetProperty(ref _layoutMessage, value);
    }

    /// <summary>True when <see cref="LayoutMessage"/> is a failed write that Retry can try again.</summary>
    public bool CanRetry
    {
        get => _canRetry;
        private set => SetProperty(ref _canRetry, value);
    }

    public bool HasUnsavedChanges => _layout.HasUnsavedChanges;

    public void RetrySave() => Report(_layout.RetrySave());

    /// <summary>Writes a waiting query change: after a pause in typing, and when the window closes.</summary>
    public PersistenceResult FlushPending()
    {
        var persistence = _layout.FlushPending();
        Report(persistence);
        return persistence;
    }

    /// <summary>The File shelf's documents, ready to review as a session.</summary>
    public SessionFileSet ListedFileSet() => SessionFileSet.From(Library.Rows.Select(r => r.Item));

    private void OnLibraryQueryChanged()
    {
        // The last layout action's line is old news once the user is searching,
        // except in Arrange mode, where it says how to leave.
        if (!IsArranging)
            Status = null;
        _layout.SetQuery(Library.CurrentQuery);
        if (_layout.HasUnsavedChanges)
            QueryPending?.Invoke();
    }

    private void OnLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.SearchText))
        {
            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(IsSearching));
        }
    }

    private void Report(PersistenceResult persistence)
    {
        if (persistence.Saved)
        {
            LayoutMessage = _layout.Notice;
            CanRetry = false;
        }
        else
        {
            LayoutMessage = persistence.UserMessage ?? "Your layout couldn't be saved.";
            CanRetry = _layout.CanWrite;
        }
    }

    private void RebuildPanels()
    {
        var placements = Pack();
        var visible = _layout.VisiblePanels;
        _panels = placements
            .Select((p, i) => IsColumns
                ? ColumnPanel(p, visible)
                : new WorkspacePanelViewModel(p, canMoveEarlier: i > 0, canMoveLater: i < placements.Count - 1))
            .ToList();

        OnPropertyChanged(nameof(Panels));
        OnPropertyChanged(nameof(HasPanels));
        OnPropertyChanged(nameof(IsColumns));
        OnPropertyChanged(nameof(AddablePanels));
        OnPropertyChanged(nameof(CanAddPanel));
        OnPropertyChanged(nameof(Layouts));
        OnPropertyChanged(nameof(SelectedLayout));
        OnPropertyChanged(nameof(ActiveLayoutName));
        NotifyArrange();
        PanelsChanged?.Invoke();
    }
}

/// <summary>One shown panel: which it is, and where it goes.</summary>
public sealed class WorkspacePanelViewModel
{
    public WorkspacePanelViewModel(PanelPlacement placement, bool canMoveEarlier = false, bool canMoveLater = false, bool inLeftColumn = false)
    {
        Placement = placement;
        CanMoveEarlier = canMoveEarlier;
        CanMoveLater = canMoveLater;
        InLeftColumn = inLeftColumn;
    }

    public PanelPlacement Placement { get; }

    public string Id => Placement.PanelId;

    public string Type => Placement.Type;

    public string Title => PanelTypes.DisplayName(Type);

    /// <summary>False for a panel type this build doesn't have: shown as a placeholder, never dropped (D6).</summary>
    public bool IsAvailable => PanelTypes.IsAvailable(Type);

    public int Row => Placement.Row;

    public int Column => Placement.Column;

    /// <summary>The columns it is shown across: <see cref="StoredSpan"/>, or more in a narrow window.</summary>
    public int Span => Placement.Span;

    /// <summary>Its own width, which Arrange mode's width choice sets.</summary>
    public int StoredSpan => Placement.StoredSpan;

    public bool IsWidened => Placement.IsWidened;

    public bool CanMoveEarlier { get; }

    public bool CanMoveLater { get; }

    /// <summary>The column it is shown in; none in a rows layout, or while a narrow window stacks the columns.</summary>
    public PanelDock Dock => Placement.Dock;

    /// <summary>In a columns layout, true when its own column is the left one, even while stacked: the Column choice shows this.</summary>
    public bool InLeftColumn { get; }
}

/// <summary>One choice for the layout QuickerPlaces starts with: a layout, or (null id) the one used last.</summary>
public sealed record StartupOption(string? PresetId, string Name, bool IsChosen);

/// <summary>A panel type Add panel offers.</summary>
public sealed record PanelChoice(string Type, string Title);
