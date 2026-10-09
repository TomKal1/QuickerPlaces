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
/// add-ins is only ever added to by a "yes" after a launch, never here, so
/// this view model can remove entries but not add them. UI-free and linked
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
    private readonly bool _baselineAllow;
    private bool _allowLoadOnce;
    private bool _savedOnce;
    private string? _saveError;

    public RevitSettingsViewModel(AppSettings settings, SettingsService service, RevitMachine machine, RevitHandlerRegistry registry,
        Action? saved = null, CultureInfo? culture = null)
    {
        _settings = settings;
        _service = service;
        _saved = saved ?? (() => { });
        _culture = culture ?? CultureInfo.CurrentCulture;
        _baselineAllow = _allowLoadOnce = settings.AllowLoadOnce;
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
    }

    private readonly Dictionary<string, (string? HandlerId, string? Folder)> _baseline;

    public IReadOnlyList<RevitReleaseRowViewModel> Releases { get; }

    public bool HasReleases => Releases.Count > 0;

    /// <summary>Said when there is nothing to configure; "" otherwise.</summary>
    public string NoReleasesText => HasReleases ? "" : "No Revit 2022 or later was found on this computer, and no handler has registered yet.";

    public ObservableCollection<LoadOnceEntryRowViewModel> Entries { get; }

    public bool HasEntries => Entries.Count > 0;

    /// <summary>Says how the list fills up while it is empty; "" otherwise.</summary>
    public string NoEntriesText => HasEntries ? "" : "No add-ins are allowed yet. After a Revit that QuickerPlaces started shows an add-in security prompt that you answer yourself, you are asked whether to allow that add-in next time.";

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

    public string AllowLoadOnceHint =>
        "When on, QuickerPlaces presses Load Once, never Always Load, on a Revit add-in security prompt, but only for an add-in listed here, "
        + "only in a Revit that QuickerPlaces started, and only until the handler has loaded. It checks the add-in's DLL hasn't changed first, "
        + "and logs every press.";

    public bool HasChanges =>
        _allowLoadOnce != _baselineAllow
        || Entries.Count != _baselineEntries.Count
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
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(NoEntriesText));
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
        _settings.LoadOnceEntries = Entries.Select(e => e.Entry).ToList();
        _service.Save(_settings);

        foreach (var row in Releases)
            _baseline[row.Release] = (row.SelectedChoice.HandlerId, row.StoredFolder);
        _baselineEntries.Clear();
        _baselineEntries.AddRange(Entries.Select(e => e.Entry));
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
            handlers.Any(h => h.Supports(HandlerProtocol.ActionOpenNewLocal)), Edited);
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
