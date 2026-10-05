using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Models.Sessions;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.ViewModels;

/// <summary>
/// Backs the Open Shared Session dialog (session sharing plan §6): each
/// shared file, where it was found on this PC (if anywhere), and a tick for
/// whether it is saved in the new session. A session only holds paths on
/// this PC, so a file that wasn't found can't be ticked until it is: by
/// syncing its library and choosing Check again, by Locate, or, for a file
/// on a network share, by Check network files, which the user is asked to
/// press because looking a server up contacts it.
///
/// Every check for a file goes through <see cref="IShell"/>; network
/// checks are handed to the view (<see cref="NetworkPathsToCheck"/>), which
/// runs them off the UI thread and returns the answers. Nothing is saved
/// until Save. UI-free and linked into the test project.
/// </summary>
public sealed class ImportSharedSessionViewModel : ObservableObject
{
    private readonly SessionStore _store;
    private readonly IShell _shell;
    private readonly SharedSessionDocument _document;
    private readonly TimeZoneInfo _localZone;
    private IReadOnlyList<CloudSyncRoot> _roots;
    private readonly List<FolderSwap> _swaps = new();
    private string _name;
    private string _tagsText;
    private string? _errorMessage;
    private string? _statusMessage;

    /// <param name="localZone">The zone "Shared on" is shown in; null for the machine's own. Tests pass a fixed one.</param>
    public ImportSharedSessionViewModel(SessionStore store, IShell shell, IReadOnlyList<CloudSyncRoot> roots,
        SharedSessionDocument document, TimeZoneInfo? localZone = null)
    {
        _store = store;
        _shell = shell;
        _roots = roots;
        _document = document;
        _localZone = localZone ?? TimeZoneInfo.Local;
        _name = UniqueName(document.Name);
        _tagsText = SessionStore.FormatTags(document.Tags);

        foreach (var file in document.Files)
        {
            var row = new SharedFileMatchViewModel(file);
            row.Apply(SessionSharing.Resolve(file, shell, roots, checkNetwork: false));
            row.PropertyChanged += Row_PropertyChanged;
            Rows.Add(row);
        }
    }

    public string Title => "Open Shared Session";

    /// <summary>"Tower B, shared 28/09/2026 with 6 files" in the user's own date format.</summary>
    public string Heading
    {
        get
        {
            var files = SessionsViewModel.FileCount(Rows.Count);
            if (_document.SharedAt == default)
                return $"\"{_document.Name}\" with {files}";

            var date = TimeZoneInfo.ConvertTime(_document.SharedAt, _localZone).DateTime.ToString("d", CultureInfo.CurrentCulture);
            return $"\"{_document.Name}\", shared {date} with {files}";
        }
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? "");
    }

    public string TagsText
    {
        get => _tagsText;
        set => SetProperty(ref _tagsText, value ?? "");
    }

    public ObservableCollection<SharedFileMatchViewModel> Rows { get; } = new();

    public int IncludedCount => Rows.Count(r => r.IsIncluded);

    public int FoundCount => Rows.Count(r => r.IsFound);

    /// <summary>"4 of 6 files found on this PC; 4 will be saved".</summary>
    public string IncludedText => $"{FoundCount} of {SessionsViewModel.FileCount(Rows.Count)} found on this PC; {IncludedCount} will be saved";

    /// <summary>What to do about the files not found, one line per kind; null when every file was found.</summary>
    public string? Advice
    {
        get
        {
            var lines = new List<string>();
            var online = Rows.Count(r => r.Status == SharedFileStatus.OnlineOnly);
            var notSynced = Rows.Count(r => r.Status == SharedFileStatus.NotInSyncedLibrary);
            var missing = Rows.Count(r => r.Status == SharedFileStatus.Missing);
            if (online > 0)
                lines.Add($"{Of(online)} {(online == 1 ? "is" : "are")} in a OneDrive or SharePoint library this PC doesn't sync. Right-click to open {(online == 1 ? "it" : "them")} online, or open the folder online and choose Sync; then press Check again.");
            if (notSynced > 0)
                lines.Add($"{Of(notSynced)} {(notSynced == 1 ? "isn't" : "aren't")} in your synced copy of {(notSynced == 1 ? "its" : "their")} library yet. Wait for OneDrive to finish syncing and press Check again, or use Locate.");
            if (HasUncheckedNetwork)
                lines.Add($"Some files are on network shares ({NetworkServersText}). Press Check network files to look for them there.");
            if (missing > 0)
                lines.Add($"{Of(missing)} couldn't be found. Use Locate to point at {(missing == 1 ? "it" : "one")}; other files in the same folders are then found too.");
            return lines.Count == 0 ? null : string.Join("\n", lines);

            static string Of(int n) => n == 1 ? "1 file" : $"{n} files";
        }
    }

    /// <summary>True while some files are on network shares that haven't been looked at.</summary>
    public bool HasUncheckedNetwork => Rows.Any(r => r.Status == SharedFileStatus.NetworkNotChecked);

    /// <summary>The servers Check network files would contact: "\\files, \\archive".</summary>
    public string NetworkServersText => string.Join(", ", NetworkPathsToCheck
        .Select(SessionSharing.ServerOf)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(s => @"\\" + s));

    /// <summary>The network paths Check network files looks for, each once.</summary>
    public IReadOnlyList<string> NetworkPathsToCheck => Rows
        .Where(r => r.Status == SharedFileStatus.NetworkNotChecked)
        .Select(r => SessionSharing.NetworkPathOf(r.Shared))
        .OfType<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>What the last Locate or check found.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>The new session's id once saved.</summary>
    public string? SavedId { get; private set; }

    /// <summary>Why the new session didn't reach disk, or null.</summary>
    public string? SavePersistenceMessage { get; private set; }

    /// <summary>
    /// Takes the answers to <see cref="NetworkPathsToCheck"/> (true for a
    /// path that exists) and looks at each unchecked file again with them.
    /// </summary>
    public void ApplyNetworkCheck(IReadOnlyDictionary<string, bool> exists)
    {
        var answers = new Dictionary<string, bool>(exists, StringComparer.OrdinalIgnoreCase);
        var shell = new AnsweredShell(_shell, answers);
        var found = 0;
        foreach (var row in Rows.Where(r => r.Status == SharedFileStatus.NetworkNotChecked).ToList())
        {
            row.Apply(SessionSharing.Resolve(row.Shared, shell, _roots, checkNetwork: true));
            if (row.IsFound)
                found++;
        }

        StatusMessage = found == 1 ? "Found 1 file on the network." : $"Found {found} files on the network.";
        RaiseCounts();
    }

    /// <summary>
    /// Looks again for every file not yet found, with <paramref name="roots"/>
    /// read again: after the user synced a library or OneDrive caught up.
    /// Network shares stay unchecked until the user asks.
    /// </summary>
    public void Recheck(IReadOnlyList<CloudSyncRoot> roots)
    {
        _roots = roots;
        var found = 0;
        foreach (var row in Rows.Where(r => !r.IsFound && r.Status != SharedFileStatus.NetworkNotChecked).ToList())
        {
            row.Apply(SessionSharing.Resolve(row.Shared, _shell, roots, checkNetwork: false));
            if (!row.IsFound)
                TrySwaps(row);
            if (row.IsFound)
                found++;
        }

        StatusMessage = found switch
        {
            0 => "No more files were found.",
            1 => "Found 1 more file.",
            _ => $"Found {found} more files.",
        };
        RaiseCounts();
    }

    /// <summary>
    /// The user pointed at <paramref name="chosenPath"/> for
    /// <paramref name="row"/>. It is ticked, and the folder swap between its
    /// shared path and the chosen one is tried on every file not yet found.
    /// Returns how many other files that found.
    /// </summary>
    public int Locate(SharedFileMatchViewModel row, string chosenPath)
    {
        var path = DocumentPaths.Normalize(chosenPath);
        if (path is null)
        {
            ErrorMessage = $"\"{chosenPath}\" isn't a PDF, Word or Excel file, so it can't be saved in a session.";
            return 0;
        }

        ErrorMessage = null;
        row.Apply(new SharedFileMatch(SharedFileStatus.Located, path));

        var others = 0;
        var original = row.Shared.Path.Length > 0 ? row.Shared.Path : null;
        if (original is not null && SessionSharing.SwapBetween(original, path) is { } swap)
        {
            _swaps.Add(swap);
            foreach (var other in Rows.Where(r => !r.IsFound).ToList())
            {
                if (TrySwap(other, swap))
                    others++;
            }
        }

        StatusMessage = others switch
        {
            0 => $"Using {DocumentPaths.FileName(path)} from {DocumentPaths.Folder(path)}.",
            1 => "Found that file, and 1 more in the same folders.",
            _ => $"Found that file, and {others} more in the same folders.",
        };
        RaiseCounts();
        return others;
    }

    /// <summary>Opens the row's file online, when it has a web address. False when it has none.</summary>
    public bool OpenOnline(SharedFileMatchViewModel row) => OpenUrl(row.Shared.Url);

    /// <summary>Opens the folder holding the row's file online, where SharePoint's Sync button is. False when it has no address.</summary>
    public bool OpenFolderOnline(SharedFileMatchViewModel row) => OpenUrl(CloudPaths.Parent(row.Shared.Url));

    public void SetAllIncluded(bool included)
    {
        foreach (var row in Rows)
            row.IsIncluded = included;
    }

    /// <summary>
    /// Saves the ticked files as a new session, by the store's own rules.
    /// False, with <see cref="ErrorMessage"/> set, when nothing is ticked or
    /// the store refuses it (a name already used, say).
    /// </summary>
    public bool Save()
    {
        var files = Rows.Where(r => r.IsIncluded && r.LocalPath is not null).Select(r => r.LocalPath!).ToList();
        if (files.Count == 0)
        {
            ErrorMessage = FoundCount == 0
                ? "None of the shared files were found on this PC yet, so there's nothing to save. Sync their library, use Locate, or check network files first."
                : "Tick at least one file to save.";
            return false;
        }

        var result = _store.TryCreate(Name, SessionStore.ParseTags(TagsText), files, out var created, out var persistence);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
            return false;
        }

        ErrorMessage = null;
        SavedId = created!.Id;
        SavePersistenceMessage = persistence.Saved ? null : persistence.UserMessage;
        return true;
    }

    /// <summary>
    /// <paramref name="name"/>, or when a session already has it,
    /// "<paramref name="name"/> (shared)", then "(shared 2)" and on, kept
    /// within a session name's length.
    /// </summary>
    private string UniqueName(string name)
    {
        var taken = _store.Sessions.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name))
            return name;

        for (var n = 1; ; n++)
        {
            var suffix = n == 1 ? " (shared)" : $" (shared {n})";
            var stem = name.Length + suffix.Length > SessionStore.MaxNameLength
                ? name[..(SessionStore.MaxNameLength - suffix.Length)].TrimEnd()
                : name;
            var candidate = stem + suffix;
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    private void TrySwaps(SharedFileMatchViewModel row)
    {
        foreach (var swap in _swaps)
        {
            if (TrySwap(row, swap))
                return;
        }
    }

    private bool TrySwap(SharedFileMatchViewModel row, FolderSwap swap)
    {
        if (swap.Apply(row.Shared.Path) is not { } candidate || !_shell.FileExists(candidate))
            return false;

        row.Apply(new SharedFileMatch(SharedFileStatus.Located, candidate));
        return true;
    }

    private bool OpenUrl(string? url)
    {
        var canonical = CloudPaths.Canonical(url);
        if (canonical is null)
            return false;

        try
        {
            _shell.Open(canonical);
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Opening a shared file online failed ({ex.GetType().Name}).");
            ErrorMessage = $"Windows couldn't open the web address: {ex.Message}";
            return false;
        }
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharedFileMatchViewModel.IsIncluded))
            RaiseCounts();
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(FoundCount));
        OnPropertyChanged(nameof(IncludedText));
        OnPropertyChanged(nameof(Advice));
        OnPropertyChanged(nameof(HasUncheckedNetwork));
        OnPropertyChanged(nameof(NetworkServersText));
    }

    /// <summary>The real shell, except that network paths already looked up off the UI thread are answered from those answers.</summary>
    private sealed class AnsweredShell : IShell
    {
        private readonly IShell _inner;
        private readonly IReadOnlyDictionary<string, bool> _answers;

        public AnsweredShell(IShell inner, IReadOnlyDictionary<string, bool> answers)
        {
            _inner = inner;
            _answers = answers;
        }

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public bool FileExists(string path) => _answers.TryGetValue(path, out var exists) ? exists : _inner.FileExists(path);

        public void Open(string target) => _inner.Open(target);
    }
}

/// <summary>One shared file in the Open Shared Session dialog.</summary>
public sealed class SharedFileMatchViewModel : ObservableObject
{
    private SharedFileMatch _match = new(SharedFileStatus.Missing, null);
    private bool _isIncluded;

    public SharedFileMatchViewModel(SharedSessionFile shared) => Shared = shared;

    public SharedSessionFile Shared { get; }

    public SharedFileStatus Status => _match.Status;

    /// <summary>Where the file is on this PC, or null when it wasn't found.</summary>
    public string? LocalPath => _match.LocalPath;

    public bool IsFound => _match.IsFound;

    public string FileName => Shared.Path.Length > 0 ? DocumentPaths.FileName(Shared.Path) : CloudPaths.FileName(Shared.Url);

    /// <summary>Where it is on this PC, or where it was on the sender's.</summary>
    public string Where => LocalPath is { } local
        ? DocumentPaths.Folder(local)
        : SessionSharing.NetworkPathOf(Shared) is { } unc ? DocumentPaths.Folder(unc)
        : Shared.Url is { } url ? CloudPaths.Parent(url) ?? url
        : DocumentPaths.Folder(Shared.Path);

    public string StatusText => Status switch
    {
        SharedFileStatus.SamePath => "Found at the same path",
        SharedFileStatus.SyncedLibrary => "Found in your synced library",
        SharedFileStatus.Network => "Found on the network",
        SharedFileStatus.Located => "Found where you pointed",
        SharedFileStatus.NetworkNotChecked => $"On \\\\{SessionSharing.ServerOf(SessionSharing.NetworkPathOf(Shared) ?? "")}, not checked yet",
        SharedFileStatus.OnlineOnly => "Library not synced here: open online",
        SharedFileStatus.NotInSyncedLibrary => "Not in your synced library yet",
        _ => "Not found: use Locate",
    };

    /// <summary>The row's tooltip: the paths and address the shared file gave.</summary>
    public string Detail
    {
        get
        {
            var lines = new List<string>();
            if (LocalPath is { } local)
                lines.Add($"On this PC: {local}");
            if (Shared.Path.Length > 0)
                lines.Add($"On the sender's PC: {Shared.Path}");
            if (Shared.Url is { } url)
                lines.Add($"Online: {url}");
            if (Shared.NetworkPath is { } unc && !string.Equals(unc, Shared.Path, StringComparison.OrdinalIgnoreCase))
                lines.Add($"Network: {unc}");
            return string.Join("\n", lines);
        }
    }

    public bool CanOpenOnline => CloudPaths.IsWebUrl(Shared.Url);

    /// <summary>Only a file found on this PC can be saved in a session.</summary>
    public bool CanInclude => IsFound;

    /// <summary>Ticked when found; a file not found can't be ticked.</summary>
    public bool IsIncluded
    {
        get => _isIncluded;
        set => SetProperty(ref _isIncluded, value && IsFound);
    }

    /// <summary>Takes a new answer to where the file is. A newly found file is ticked; a lost one is unticked.</summary>
    internal void Apply(SharedFileMatch match)
    {
        var wasFound = IsFound;
        _match = match;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(LocalPath));
        OnPropertyChanged(nameof(IsFound));
        OnPropertyChanged(nameof(CanInclude));
        OnPropertyChanged(nameof(Where));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Detail));
        if (IsFound != wasFound || !IsFound)
            IsIncluded = IsFound;
    }
}
