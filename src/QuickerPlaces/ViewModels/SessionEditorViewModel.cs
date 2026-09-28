using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.ViewModels;

/// <summary>
/// Backs the Save Session dialog, for a new session and for editing one
/// (sessions plan §5): the name, the tags, and the review list of PDFs, each
/// with a tick. A scan's files judged open start ticked, its suggestions
/// start unticked, and files added by hand start ticked. Nothing is saved
/// until Save, and only ticked files are saved (D9).
///
/// The scan itself runs in the view, off the UI thread, and arrives here
/// through <see cref="ApplyScan"/>. UI-free and linked into the test project.
/// </summary>
public sealed class SessionEditorViewModel : ObservableObject
{
    private readonly SessionStore _store;
    private readonly SessionSnapshot? _editing;
    private string _name;
    private string _tagsText;
    private string? _scanSummary;
    private string? _errorMessage;
    private bool _isScanning;

    /// <param name="editing">The session to edit, or null for a new one.</param>
    public SessionEditorViewModel(SessionStore store, SessionSnapshot? editing)
    {
        _store = store;
        _editing = editing;
        _name = editing?.Name ?? "";
        _tagsText = editing is null ? "" : SessionStore.FormatTags(editing.Tags);

        foreach (var file in editing?.Files ?? Array.Empty<string>())
            AddChoice(new SessionFileChoiceViewModel(file, "In this session", isIncluded: true));

        RefreshTagSuggestions();
    }

    public bool IsNew => _editing is null;

    public string Title => IsNew ? "Save Open PDFs" : "Edit Session";

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? "");
    }

    /// <summary>The tags as typed, comma-separated.</summary>
    public string TagsText
    {
        get => _tagsText;
        set
        {
            if (SetProperty(ref _tagsText, value ?? ""))
                RefreshTagSuggestions();
        }
    }

    /// <summary>Tags other sessions use that this one doesn't have yet, to add with one click.</summary>
    public ObservableCollection<string> TagSuggestions { get; } = new();

    public bool HasTagSuggestions => TagSuggestions.Count > 0;

    /// <summary>The review list: every file found or added, ticked or not.</summary>
    public ObservableCollection<SessionFileChoiceViewModel> Files { get; } = new();

    public int IncludedCount => Files.Count(f => f.IsIncluded);

    /// <summary>"3 of 7 PDFs ticked", under the list.</summary>
    public string IncludedText => $"{IncludedCount} of {SessionsViewModel.PdfCount(Files.Count)} ticked";

    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }

    /// <summary>What the last scan found, in a line or two, or null before any scan.</summary>
    public string? ScanSummary
    {
        get => _scanSummary;
        private set => SetProperty(ref _scanSummary, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>The saved session's id, after a successful <see cref="Save"/>.</summary>
    public string? SavedId { get; private set; }

    /// <summary>After a successful <see cref="Save"/>: the reason the write didn't reach disk, or null if it did.</summary>
    public string? SavePersistenceMessage { get; private set; }

    /// <summary>
    /// Adds a scan's files to the list. A file already listed keeps its tick,
    /// except that one judged open is ticked. The summary says how many were
    /// judged open and names the windows that couldn't be matched to a file.
    /// </summary>
    public void ApplyScan(OpenPdfScan scan)
    {
        var added = 0;
        foreach (var candidate in scan.Candidates)
        {
            var existing = Find(candidate.Path);
            if (existing is not null)
            {
                if (candidate.IsLikelyOpen)
                    existing.IsIncluded = true;
                continue;
            }

            AddChoice(new SessionFileChoiceViewModel(candidate.Path, candidate.Reason, candidate.IsLikelyOpen));
            added++;
        }

        var open = scan.Candidates.Count(c => c.IsLikelyOpen);
        var lines = new List<string>
        {
            open switch
            {
                0 => "No open PDFs were found. Tick any recently opened ones below, or use Add PDFs.",
                1 => "Found 1 open PDF. Check the list, then Save.",
                _ => $"Found {open} open PDFs. Check the list, then Save.",
            },
        };

        if (scan.UnmatchedTitles.Count > 0)
            lines.Add($"Also open, but not matched to a file: {string.Join(", ", scan.UnmatchedTitles)}. Use Add PDFs to include {(scan.UnmatchedTitles.Count == 1 ? "it" : "them")}.");
        if (!string.IsNullOrEmpty(scan.Warning))
            lines.Add(scan.Warning);

        ScanSummary = string.Join("\n", lines);
        OnPropertyChanged(nameof(IncludedText));
    }

    /// <summary>Adds files the user chose by hand, ticked. A path that isn't a PDF is refused and named.</summary>
    public void AddFiles(IEnumerable<string> paths)
    {
        ErrorMessage = null;
        var refused = new List<string>();
        foreach (var path in paths)
        {
            var normalized = SessionPaths.NormalizePdf(path);
            if (normalized is null)
            {
                refused.Add(path);
                continue;
            }

            if (Find(normalized) is { } existing)
                existing.IsIncluded = true;
            else
                AddChoice(new SessionFileChoiceViewModel(normalized, "Added by you", isIncluded: true));
        }

        if (refused.Count > 0)
            ErrorMessage = $"Only PDFs can be added: {string.Join(", ", refused.Select(SessionPaths.FileName))}.";
        OnPropertyChanged(nameof(IncludedText));
    }

    /// <summary>Takes a file out of the list altogether.</summary>
    public void Remove(SessionFileChoiceViewModel? choice)
    {
        if (choice is null || !Files.Remove(choice))
            return;

        choice.PropertyChanged -= Choice_PropertyChanged;
        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(IncludedText));
    }

    /// <summary>Ticks or unticks every file.</summary>
    public void SetAllIncluded(bool included)
    {
        foreach (var file in Files)
            file.IsIncluded = included;
    }

    /// <summary>Appends <paramref name="tag"/> to the tags box.</summary>
    public void AddTag(string tag)
    {
        var tags = SessionStore.ParseTags(TagsText).ToList();
        if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            tags.Add(tag);
        TagsText = SessionStore.FormatTags(tags);
    }

    /// <summary>
    /// Saves the ticked files under the name and tags. False, with
    /// <see cref="ErrorMessage"/> set, when the store refuses them; true
    /// otherwise, even if the write then failed (the session is kept in
    /// memory, and <see cref="SavePersistenceMessage"/> says why).
    /// </summary>
    public bool Save()
    {
        var tags = SessionStore.ParseTags(TagsText);
        var files = Files.Where(f => f.IsIncluded).Select(f => f.Path).ToList();

        Services.ValidationResult validation;
        Models.PersistenceResult persistence;
        if (_editing is null)
        {
            validation = _store.TryCreate(Name, tags, files, out var created, out persistence);
            SavedId = created?.Id;
        }
        else
        {
            validation = _store.TryUpdate(_editing.Id, Name, tags, files, out persistence);
            SavedId = validation.Success ? _editing.Id : null;
        }

        if (!validation.Success)
        {
            ErrorMessage = validation.ErrorMessage;
            return false;
        }

        ErrorMessage = null;
        SavePersistenceMessage = persistence.Saved ? null : persistence.UserMessage;
        return true;
    }

    private SessionFileChoiceViewModel? Find(string path)
        => Files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));

    private void AddChoice(SessionFileChoiceViewModel choice)
    {
        choice.PropertyChanged += Choice_PropertyChanged;
        Files.Add(choice);
        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(IncludedText));
    }

    private void Choice_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SessionFileChoiceViewModel.IsIncluded))
            return;

        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(IncludedText));
    }

    private void RefreshTagSuggestions()
    {
        var current = SessionStore.ParseTags(TagsText);
        TagSuggestions.Clear();
        foreach (var tag in _store.Tags.Select(t => t.Tag).Where(t => !current.Contains(t, StringComparer.OrdinalIgnoreCase)))
            TagSuggestions.Add(tag);
        OnPropertyChanged(nameof(HasTagSuggestions));
    }
}

/// <summary>One file in the Save Session review list.</summary>
public sealed class SessionFileChoiceViewModel : ObservableObject
{
    private bool _isIncluded;

    public SessionFileChoiceViewModel(string path, string reason, bool isIncluded)
    {
        Path = path;
        Reason = reason;
        _isIncluded = isIncluded;
    }

    public string Path { get; }

    public string FileName => SessionPaths.FileName(Path);

    public string Folder => SessionPaths.Folder(Path);

    /// <summary>Why it is listed: "Open in Adobe Acrobat", "Recently opened", "Added by you", "In this session".</summary>
    public string Reason { get; }

    public bool IsIncluded
    {
        get => _isIncluded;
        set => SetProperty(ref _isIncluded, value);
    }
}
