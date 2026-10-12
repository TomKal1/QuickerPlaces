using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using QuickerPlaces.Models;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Revit.AddIns;
using QuickerPlaces.Services.Revit.Dialogs;
using QuickerPlaces.Services.Revit.Handlers;
using QuickerPlaces.Services.Revit.Opening;

namespace QuickerPlaces.ViewModels;

/// <summary>One handler the user can pick for a release, or "None" (<see cref="HandlerId"/> null).</summary>
public sealed record RevitHandlerChoice(string? HandlerId, string Label);

/// <summary>One release's draft: the handler choice and the local folder.</summary>
public sealed class RevitReleaseRowViewModel : ObservableObject
{
    private readonly Action _edited;
    private RevitHandlerChoice _selected;
    private string _localFolder;

    public RevitReleaseRowViewModel(string release, bool installed, IReadOnlyList<RevitHandlerChoice> choices,
        RevitHandlerChoice selected, string localFolder, bool anyHandlerRegistered, Action edited)
    {
        Release = release;
        IsInstalled = installed;
        Choices = choices;
        _selected = selected;
        _localFolder = localFolder;
        _edited = edited;
        HandlerHint = anyHandlerRegistered
            ? "Only handlers that can open a central as a new local are listed."
            : $"No handler has registered for Revit {release} yet. One appears here after Revit {release} has run once with it loaded.";
    }

    /// <summary>The release as its four-digit year ("2025"), the key in the settings.</summary>
    public string Release { get; }

    public string Title => "Revit " + Release;

    public bool IsInstalled { get; }

    /// <summary>Says why a release that isn't installed is listed; "" otherwise.</summary>
    public string InstalledNote => IsInstalled ? "" : "Not installed on this computer. It is listed because a handler registered for it.";

    public IReadOnlyList<RevitHandlerChoice> Choices { get; }

    public string HandlerHint { get; }
    public string PromptStatus { get; init; } = "Not recorded";

    public RevitHandlerChoice SelectedChoice
    {
        get => _selected;
        set
        {
            if (value is not null && !Equals(_selected, value))
            {
                _selected = value;
                OnPropertyChanged();
                _edited();
            }
        }
    }

    /// <summary>What the user typed; empty means the default.</summary>
    public string LocalFolder
    {
        get => _localFolder;
        set
        {
            value ??= "";
            if (_localFolder == value)
                return;
            _localFolder = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FolderError));
            OnPropertyChanged(nameof(CanReset));
            _edited();
        }
    }

    /// <summary>Shown greyed in the empty box and by Reset.</summary>
    public string DefaultFolder => RevitSettingsResolver.DefaultLocalFolder(Release);

    public bool CanReset => !string.IsNullOrWhiteSpace(_localFolder);

    /// <summary>Why the folder can't be used, or "" when it can (an empty box is the default).</summary>
    public string FolderError => RevitSettingsViewModel.FolderProblem(_localFolder, Release) ?? "";

    public void ResetFolder() => LocalFolder = "";

    /// <summary>The folder to store: null for the default.</summary>
    internal string? StoredFolder => string.IsNullOrWhiteSpace(_localFolder) ? null : _localFolder.Trim();
}

/// <summary>One allowed add-in, for the list with its Remove button.</summary>
public sealed class LoadOnceEntryRowViewModel
{
    public LoadOnceEntryRowViewModel(LoadOnceEntry entry, CultureInfo culture)
    {
        Entry = entry;
        Name = entry.Name;
        ReleaseText = "Revit " + entry.Release.ToString(CultureInfo.InvariantCulture);
        DllPath = entry.DllPath;
        AllowedText = "Allowed " + entry.AllowedUtc.ToLocalTime().ToString("d MMM yyyy", culture);
        RemoveName = $"Remove {entry.Name} for {ReleaseText}";
    }

    public LoadOnceEntry Entry { get; }
    public string Name { get; }
    public string ReleaseText { get; }
    public string DllPath { get; }
    public string AllowedText { get; }

    /// <summary>The remove button's accessible name.</summary>
    public string RemoveName { get; }
}

/// <summary>A compact display of saved/draft permissions across releases.</summary>
public sealed class LoadOnceGroupRowViewModel
{
    public LoadOnceGroupRowViewModel(IReadOnlyList<LoadOnceEntryRowViewModel> entries)
    {
        Entries = entries;
        Name = entries[0].Name;
        ReleaseText = RevitAddinRowViewModel.FormatYears(entries.Select(e => e.Entry.Release));
        DllDetails = string.Join(Environment.NewLine, entries.Select(e => $"{e.Entry.Release}: {e.DllPath}"));
        var paths = entries.Select(e => e.DllPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        DllPath = paths.Count == 1 ? paths[0] : $"{paths.Count} DLLs across releases";
    }
    public IReadOnlyList<LoadOnceEntryRowViewModel> Entries { get; }
    public string Name { get; }
    public string ReleaseText { get; }
    public string DllPath { get; }
    public string DllDetails { get; }
    public string RemoveName => $"Remove {Name} for all listed years";
}

/// <summary>One installed add-in across releases, with permission still stored per exact DLL and release.</summary>
public sealed class RevitAddinRowViewModel : ObservableObject
{
    private readonly RevitSettingsViewModel _owner;
    public RevitAddinRowViewModel(IReadOnlyList<AddInManifest> manifests, RevitSettingsViewModel owner)
    {
        Manifests = manifests;
        _owner = owner;
    }
    public IReadOnlyList<AddInManifest> Manifests { get; }
    public AddInManifest Manifest => Manifests[0];
    public string Name => Manifest.Name;
    public string Release => FormatYears(Manifests.Select(m => m.Release));
    public string DllPath => Manifests.Select(m => m.Assembly).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
        ? Manifest.Assembly ?? "DLL not declared" : $"{Manifests.Select(m => m.Assembly).Distinct(StringComparer.OrdinalIgnoreCase).Count()} DLLs across releases";
    public string DllDetails => string.Join(Environment.NewLine, Manifests.Select(m => $"{m.Release}: {m.Assembly ?? "DLL not declared"}"));
    public bool Matches(LoadOnceEntry entry) => Manifests.Any(m => entry.Release == m.Release
        && LoadOnceOffers.SameName(entry.Name, m.Name) && LoadOnceOffers.SamePath(entry.DllPath, m.Assembly));
    private int AllowedCount => Manifests.Count(m => _owner.Entries.Any(e => e.Entry.Release == m.Release
        && LoadOnceOffers.SameName(e.Name, m.Name) && LoadOnceOffers.SamePath(e.DllPath, m.Assembly)));
    public bool IsAllowed => AllowedCount == Manifests.Count;
    public string ActionText => IsAllowed ? "Remove" : "Allow all";
    public string PermissionText => AllowedCount == 0 ? "Not allowed" : IsAllowed ? "All allowed" : "Some allowed";
    public void Refresh() { OnPropertyChanged(nameof(IsAllowed)); OnPropertyChanged(nameof(ActionText)); OnPropertyChanged(nameof(PermissionText)); }

    internal static string FormatYears(IEnumerable<int> releases)
    {
        var years = releases.Distinct().OrderBy(y => y).ToArray();
        var ranges = new List<string>();
        for (var i = 0; i < years.Length; i++)
        {
            var start = years[i];
            while (i + 1 < years.Length && years[i + 1] == years[i] + 1) i++;
            ranges.Add(start == years[i] ? start.ToString(CultureInfo.InvariantCulture) : $"{start}–{years[i]}");
        }
        return string.Join(", ", ranges);
    }
}

/// <summary>
/// The Revit settings dialog's draft (roadmap §4.21; handoff step 5): per
/// release, which handler opens centrals and where the new locals go, and the
/// Allow Load Once switch with the add-ins already allowed. Nothing changes
/// until <see cref="Save"/> succeeds, which writes the settings through the
/// same <see cref="SettingsService"/> the Settings dialog uses.
///
/// The releases listed are the installed ones (Revit 2022 and later) plus any
/// with a registered handler, marked when not installed. A handler shows only
/// once Revit has run with it loaded; the dialog says so. The list of allowed
/// add-ins can be explicitly allowed here from installed manifests, with a
/// DLL fingerprint, or after a recognised prompt. UI-free and linked
/// into the test project.
/// </summary>
public sealed class RevitSettingsViewModel : ObservableObject
{
    private static readonly Regex DriveRooted = new(@"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant);
    private static readonly char[] BadCharacters = ['<', '>', '"', '|', '?', '*'];

    private readonly AppSettings _settings;
    private readonly SettingsService _service;
    private readonly Action _saved;
    private readonly CultureInfo _culture;
    private readonly List<LoadOnceEntry> _baselineEntries;
    private bool _baselineAllow;
    private bool _allowLoadOnce;
    private bool _savedOnce;
    private string? _saveError;
    private readonly Func<int, ManifestReadResult> _manifests;
    private readonly Func<string, string?> _hashOf;
    private readonly SecurityPromptSignatures _signatures;
    private string _addinRelease = "All releases";
    private int _skippedAddins;
    private int _autodeskAddins;
    private readonly List<AddInManifest> _installedManifests = [];
    private readonly List<AddInManifest> _autodeskManifests = [];
    private readonly Func<string, bool> _isAutodesk;

    public RevitSettingsViewModel(AppSettings settings, SettingsService service, RevitMachine machine, RevitHandlerRegistry registry,
        Action? saved = null, CultureInfo? culture = null, Func<int, ManifestReadResult>? manifests = null,
        SecurityPromptSignatures? signatures = null, Func<string, string?>? hashOf = null, Func<string, bool>? isAutodesk = null)
    {
        _settings = settings;
        _service = service;
        _saved = saved ?? (() => { });
        _culture = culture ?? CultureInfo.CurrentCulture;
        _manifests = manifests ?? (release => AddInManifestFinder.Find(release, AddInLocations.ForThisMachine()));
        _signatures = signatures ?? SecurityPromptSignatures.Load(Path.Combine(AppContext.BaseDirectory, SecurityPromptSignatures.FileName));
        _hashOf = hashOf ?? DllFingerprint.Sha256;
        _isAutodesk = isAutodesk ?? AddinPublisherCache.Shared.IsAutodeskSigned;
        _baselineAllow = _allowLoadOnce = settings.AllowLoadOnce;
        _baselineOverwrite = _overwriteExistingLocal = RevitSettingsResolver.ExistingLocal(settings) == HandlerProtocol.ExistingLocal.Overwrite;
        _baselineEntries = (settings.LoadOnceEntries ?? []).ToList();
        Entries = new ObservableCollection<LoadOnceEntryRowViewModel>(_baselineEntries.Select(e => new LoadOnceEntryRowViewModel(e, _culture)));

        var installed = machine.Installs().Select(i => i.ReleaseText).ToHashSet(StringComparer.Ordinal);
        var registered = registry.Read();
        var releases = installed.Union(registered.Select(r => r.Release), StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();
        Releases = releases.Select(release => BuildRow(release, installed.Contains(release),
            registered.FirstOrDefault(r => r.Release == release)?.Handlers ?? [])).ToList();
        _baseline = Releases.ToDictionary(r => r.Release, r => (r.SelectedChoice.HandlerId, r.StoredFolder));
        AddinReleaseChoices = new[] { "All releases" }.Concat(releases).ToList();
        RefreshAddins();
    }

    private readonly Dictionary<string, (string? HandlerId, string? Folder)> _baseline;

    public IReadOnlyList<RevitReleaseRowViewModel> Releases { get; }

    public bool HasReleases => Releases.Count > 0;

    /// <summary>Said when there is nothing to configure; "" otherwise.</summary>
    public string NoReleasesText => HasReleases ? "" : "No Revit 2022 or later was found on this computer, and no handler has registered yet.";

    public ObservableCollection<LoadOnceEntryRowViewModel> Entries { get; }

    public IEnumerable<LoadOnceGroupRowViewModel> AllowedAddins => Entries
        .Where(e => !_autodeskManifests.Any(m => m.Release == e.Entry.Release && LoadOnceOffers.SamePath(m.Assembly, e.DllPath)))
        .GroupBy(e => e.Name.Trim(), StringComparer.OrdinalIgnoreCase)
        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .Select(g => new LoadOnceGroupRowViewModel(g.OrderBy(e => e.Entry.Release).ToList()));
    public bool HasEntries => AllowedAddins.Any();
    public ObservableCollection<RevitAddinRowViewModel> InstalledAddins { get; } = [];
    public IReadOnlyList<string> AddinReleaseChoices { get; }
    public IEnumerable<RevitAddinRowViewModel> VisibleAddins => InstalledAddins.Where(r => _addinRelease == "All releases"
        || r.Manifests.Any(m => m.Release.ToString(CultureInfo.InvariantCulture) == _addinRelease));
    public string AddinRelease
    {
        get => _addinRelease;
        set { if (SetProperty(ref _addinRelease, value)) AddinFilterChanged(); }
    }
    public string AddinScanStatus => $"{VisibleAddins.Count()} add-ins shown. {_autodeskAddins} Autodesk-signed entries excluded."
        + (_skippedAddins == 0 ? "" : $" {_skippedAddins} manifest entries could not be read.");

    private void AddinFilterChanged()
    {
        OnPropertyChanged(nameof(VisibleAddins));
        OnPropertyChanged(nameof(AddinScanStatus));
    }

    public void RefreshAddins()
    {
        InstalledAddins.Clear();
        _installedManifests.Clear();
        _autodeskManifests.Clear();
        _skippedAddins = 0;
        _autodeskAddins = 0;
        var thirdParty = new List<AddInManifest>();
        var publishers = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var release in Releases)
        {
            var read = _manifests(int.Parse(release.Release, CultureInfo.InvariantCulture));
            _skippedAddins += read.Skipped.Count;
            foreach (var manifest in read.Manifests.GroupBy(m => (m.Name.ToUpperInvariant(), (m.Assembly ?? "").ToUpperInvariant(), m.AddInId)).Select(g => g.First()).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
            {
                _installedManifests.Add(manifest);
                var autodesk = false;
                if (manifest.Assembly is { } dll)
                {
                    if (!publishers.TryGetValue(dll, out autodesk))
                        publishers[dll] = autodesk = _isAutodesk(dll);
                }
                if (autodesk) { _autodeskAddins++; _autodeskManifests.Add(manifest); }
                else thirdParty.Add(manifest);
            }
        }
        foreach (var group in thirdParty.GroupBy(m => (m.Name.Trim().ToUpperInvariant(), (m.VendorId ?? "").Trim().ToUpperInvariant()))
            .OrderBy(g => g.First().Name, StringComparer.OrdinalIgnoreCase))
            InstalledAddins.Add(new RevitAddinRowViewModel(group.OrderBy(m => m.Release).ToList(), this));
        AddinFilterChanged();
        EntriesChanged();
    }

    /// <summary>Applies to all installed releases in the row, atomically; still a draft until Save.</summary>
    public void ToggleAllowed(RevitAddinRowViewModel? row)
    {
        if (row is null || !InstalledAddins.Contains(row)) return;
        if (row.IsAllowed)
        {
            foreach (var entry in Entries.Where(e => row.Matches(e.Entry)).ToList()) Entries.Remove(entry);
        }
        else
        {
            var additions = new List<LoadOnceEntry>();
            foreach (var manifest in row.Manifests)
            {
                var offer = LoadOnceOffers.Offer(manifest.Release, manifest.Name, manifest.Assembly,
                    _installedManifests, DateTimeOffset.UtcNow, _hashOf);
                if (offer.Entry is not { } entry) { _saveError = $"Revit {manifest.Release}: {offer.Reason}"; NotifyState(); return; }
                if (!additions.Any(e => e.Release == entry.Release && LoadOnceOffers.SameName(e.Name, entry.Name)
                    && LoadOnceOffers.SamePath(e.DllPath, entry.DllPath))) additions.Add(entry);
            }
            foreach (var entry in Entries.Where(e => row.Matches(e.Entry)).ToList()) Entries.Remove(entry);
            foreach (var entry in additions) Entries.Add(new LoadOnceEntryRowViewModel(entry, _culture));
        }
        EntriesChanged();
        Edited();
    }

    private void EntriesChanged()
    {
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(AllowedAddins));
        OnPropertyChanged(nameof(NoEntriesText));
        foreach (var row in InstalledAddins) row.Refresh();
    }

    /// <summary>Says how the list fills up while it is empty; "" otherwise.</summary>
    public string NoEntriesText => HasEntries ? "" : "No add-ins are allowed yet. Choose Allow beside an installed add-in, then Save.";

    /// <summary>The Allow Load Once switch (off by default).</summary>
    public bool AllowLoadOnce
    {
        get => _allowLoadOnce;
        set
        {
            if (SetProperty(ref _allowLoadOnce, value))
                Edited();
        }
    }

    private bool _overwriteExistingLocal;
    private bool _baselineOverwrite;

    /// <summary>
    /// A new local replaces an old one at its path (the default), as Revit's
    /// own "Overwrite existing file". The two radio buttons bind this and
    /// <see cref="KeepExistingLocal"/>; one setting for every release.
    /// </summary>
    public bool OverwriteExistingLocal
    {
        get => _overwriteExistingLocal;
        set
        {
            if (!value || !SetProperty(ref _overwriteExistingLocal, true))
                return;
            OnPropertyChanged(nameof(KeepExistingLocal));
            Edited();
        }
    }

    /// <summary>The old local is kept, renamed with its date, as Revit's own "Append timestamp".</summary>
    public bool KeepExistingLocal
    {
        get => !_overwriteExistingLocal;
        set
        {
            if (!value || !SetProperty(ref _overwriteExistingLocal, false, nameof(OverwriteExistingLocal)))
                return;
            OnPropertyChanged(nameof(KeepExistingLocal));
            Edited();
        }
    }

    public string ExistingLocalHint =>
        "When you open a central and a local of it is already in the local folder: replace it with a new copy, like Revit's own Overwrite "
        + "(changes in it that were never synchronised are lost), or keep it, renamed with its date (old locals then use disk space until you delete them). "
        + "The handler must support this; an older one keeps old locals.";

    public string AllowLoadOnceHint =>
        "Press Load Once, never Always Load, only in a Revit that QuickerPlaces started. "
        + "Choose allowed add-ins below; a recorded prompt and an unchanged DLL are required.";

    public bool HasChanges =>
        _allowLoadOnce != _baselineAllow
        || _overwriteExistingLocal != _baselineOverwrite
        || !Entries.Select(e => LoadOnceJson.Serialise([e.Entry])).OrderBy(e => e, StringComparer.Ordinal)
            .SequenceEqual(_baselineEntries.Select(e => LoadOnceJson.Serialise([e])).OrderBy(e => e, StringComparer.Ordinal))
        || Releases.Any(r => _baseline[r.Release] != (r.SelectedChoice.HandlerId, r.StoredFolder));

    public string? ErrorMessage => Releases.Any(r => r.FolderError.Length > 0)
        ? "A local folder must be a full path, for example " + RevitSettingsResolver.DefaultLocalFolder("2025") + "."
        : _saveError;

    public bool CanSave => HasChanges && Releases.All(r => r.FolderError.Length == 0);

    public string SaveStatus => HasChanges ? "Unsaved changes — select Save to apply."
        : _savedOnce ? "Saved. Your selections are up to date." : "No unsaved changes.";

    /// <summary>Takes the add-in off the list (applied by Save).</summary>
    public void Remove(LoadOnceEntryRowViewModel? row)
    {
        if (row is null || !Entries.Remove(row))
            return;
        EntriesChanged();
        Edited();
    }

    public void RemoveGroup(LoadOnceGroupRowViewModel? row)
    {
        if (row is null) return;
        var removed = false;
        foreach (var entry in row.Entries) removed |= Entries.Remove(entry);
        if (!removed) return;
        EntriesChanged();
        Edited();
    }

    /// <summary>Writes the draft to the settings and saves them. False (nothing written) when a folder is not a full path.</summary>
    public bool Save()
    {
        if (!CanSave)
            return false;

        var releases = _settings.RevitReleases ??= new Dictionary<string, RevitReleaseSettings>();
        foreach (var row in Releases)
        {
            var handler = row.SelectedChoice.HandlerId;
            var folder = row.StoredFolder;
            if (handler is null && folder is null)
            {
                releases.Remove(row.Release);
                continue;
            }

            // Keep the entry object, so anything else it ever holds survives.
            if (!releases.TryGetValue(row.Release, out var entry))
                releases[row.Release] = entry = new RevitReleaseSettings();
            entry.HandlerId = handler;
            entry.LocalFolder = folder;
        }

        _settings.AllowLoadOnce = _allowLoadOnce;
        _settings.RevitExistingLocal = _overwriteExistingLocal ? HandlerProtocol.ExistingLocal.Overwrite : HandlerProtocol.ExistingLocal.AppendTimestamp;
        _settings.LoadOnceEntries = Entries.Select(e => e.Entry).ToList();
        _service.Save(_settings);

        foreach (var row in Releases)
            _baseline[row.Release] = (row.SelectedChoice.HandlerId, row.StoredFolder);
        _baselineEntries.Clear();
        _baselineEntries.AddRange(Entries.Select(e => e.Entry));
        _baselineAllow = _allowLoadOnce;
        _baselineOverwrite = _overwriteExistingLocal;
        _savedOnce = true;
        _saveError = null;
        _saved();
        NotifyState();
        return true;
    }

    /// <summary>
    /// Why <paramref name="text"/> can't be a local folder, or null when it can: blank means the
    /// default; otherwise a drive-letter or UNC path (checked by shape, not by asking the disk).
    /// </summary>
    internal static string? FolderProblem(string? text, string release)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var path = text.Trim();
        var absolute = DriveRooted.IsMatch(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || Path.IsPathFullyQualified(path);
        if (!absolute)
            return $"Enter a full path such as {RevitSettingsResolver.DefaultLocalFolder(release)}.";
        if (path.IndexOfAny(BadCharacters) >= 0 || path.Skip(2).Any(c => c == ':'))
            return "The folder can't contain < > \" | ? * or a stray colon.";
        return null;
    }

    private RevitReleaseRowViewModel BuildRow(string release, bool installed, IReadOnlyList<RegisteredHandler> handlers)
    {
        var effective = RevitSettingsResolver.For(_settings, release);
        var choices = new List<RevitHandlerChoice> { new(null, "None") };
        foreach (var handler in handlers.Where(h => h.Supports(HandlerProtocol.ActionOpenNewLocal)))
            choices.Add(new RevitHandlerChoice(handler.HandlerId, Label(handler)));

        // A saved choice that isn't on offer stays visible, so it isn't dropped without the user seeing it.
        if (effective.HandlerId is { } chosen && choices.All(c => c.HandlerId != chosen))
        {
            var known = handlers.FirstOrDefault(h => h.HandlerId == chosen);
            choices.Add(new RevitHandlerChoice(chosen, known is null
                ? $"{chosen} (not registered)"
                : $"{known.DisplayName} (can't open centrals)"));
        }

        var selected = choices.First(c => c.HandlerId == effective.HandlerId);
        var stored = _settings.RevitReleases?.GetValueOrDefault(release)?.LocalFolder;
        return new RevitReleaseRowViewModel(release, installed, choices, selected, stored?.Trim() ?? "",
            handlers.Any(h => h.Supports(HandlerProtocol.ActionOpenNewLocal)), Edited)
        {
            PromptStatus = _signatures.HasVerified(int.Parse(release, CultureInfo.InvariantCulture)) ? "Ready" : "Not recorded",
        };
    }

    private string Label(RegisteredHandler handler)
    {
        var state = handler.IsLoaded
            ? "loaded"
            : handler.LastSeenUtc is { } seen
                ? "not seen since " + DateTime.SpecifyKind(seen, DateTimeKind.Utc).ToLocalTime().ToString("d MMM yyyy", _culture)
                : "not loaded";
        return $"{handler.DisplayName} ({state})";
    }

    private void Edited()
    {
        _saveError = null;
        NotifyState();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(SaveStatus));
    }
}
