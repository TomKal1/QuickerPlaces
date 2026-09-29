using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Models.Workspace;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Services.Workspace;

namespace QuickerPlaces.ViewModels;

/// <summary>
/// The workspace in the main window (configurable canvas plan M3, D4): which
/// layout is shown and where its panels go, the one shared query, and the
/// few layout actions M3 offers before Arrange mode (M4) — Add panel and
/// Hide, each committed at once.
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
    private IReadOnlyList<WorkspacePanelViewModel> _panels = Array.Empty<WorkspacePanelViewModel>();
    private string? _layoutMessage;
    private bool _canRetry;
    private string? _status;

    public WorkspaceViewModel(WorkspaceLayoutService layout, LibraryViewModel library)
    {
        _layout = layout;
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

            var persistence = _layout.Activate(value.Id);
            // Filters never leak between layouts (D3): the new layout's own, or the defaults.
            Library.ApplyQuery(_layout.Query);
            Report(persistence);
            RebuildPanels();
        }
    }

    public string ActiveLayoutName => _layout.ActiveName;

    // ---------------------------------------------------------------
    // Panels
    // ---------------------------------------------------------------

    /// <summary>The shown panels in order, each with its row, column and span.</summary>
    public IReadOnlyList<WorkspacePanelViewModel> Panels => _panels;

    public bool HasPanels => _panels.Count > 0;

    /// <summary>The panels Add panel offers: available in this build and not shown now.</summary>
    public IReadOnlyList<PanelChoice> AddablePanels
        => _layout.AddablePanelTypes.Select(t => new PanelChoice(t, PanelTypes.DisplayName(t))).ToList();

    public bool CanAddPanel => _layout.AddablePanelTypes.Count > 0;

    /// <summary>
    /// Adds a panel, or shows a hidden one where it was, and keeps the
    /// arrangement at once: M3 has no Arrange mode to finish (M4).
    /// </summary>
    public bool AddPanel(string type)
    {
        _layout.BeginArrange();
        if (!_layout.AddPanel(type))
        {
            _layout.Revert();
            return false;
        }

        Report(_layout.Done());
        Status = $"Added {PanelTypes.DisplayName(type)}.";
        RebuildPanels();
        return true;
    }

    /// <summary>Hides a panel (presentation only, D1, D6) and keeps the arrangement at once. Add panel brings it back.</summary>
    public bool HidePanel(string panelId)
    {
        var type = _panels.FirstOrDefault(p => p.Id == panelId)?.Type;
        _layout.BeginArrange();
        if (type is null || !_layout.Hide(panelId))
        {
            _layout.Revert();
            return false;
        }

        Report(_layout.Done());
        Status = $"Hid {PanelTypes.DisplayName(type)}. Add panel brings it back.";
        RebuildPanels();
        return true;
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
        // The last layout action's line is old news once the user is searching.
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
        _panels = PanelLayoutEngine.Pack(_layout.Panels)
            .Select(p => new WorkspacePanelViewModel(p))
            .ToList();

        OnPropertyChanged(nameof(Panels));
        OnPropertyChanged(nameof(HasPanels));
        OnPropertyChanged(nameof(AddablePanels));
        OnPropertyChanged(nameof(CanAddPanel));
        OnPropertyChanged(nameof(Layouts));
        OnPropertyChanged(nameof(SelectedLayout));
        OnPropertyChanged(nameof(ActiveLayoutName));
        PanelsChanged?.Invoke();
    }
}

/// <summary>One shown panel: which it is, and where it goes.</summary>
public sealed class WorkspacePanelViewModel
{
    public WorkspacePanelViewModel(PanelPlacement placement) => Placement = placement;

    public PanelPlacement Placement { get; }

    public string Id => Placement.PanelId;

    public string Type => Placement.Type;

    public string Title => PanelTypes.DisplayName(Type);

    /// <summary>False for a panel type this build doesn't have: shown as a placeholder, never dropped (D6).</summary>
    public bool IsAvailable => PanelTypes.IsAvailable(Type);

    public int Row => Placement.Row;

    public int Column => Placement.Column;

    public int Span => Placement.Span;
}

/// <summary>A panel type Add panel offers.</summary>
public sealed record PanelChoice(string Type, string Title);
