using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using QuickerPlaces.Models.Sessions;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.ViewModels;

/// <summary>
/// Backs the Share Session dialog (session sharing plan §5): the session's
/// files, each with where it lives and whether it goes in the shared file.
/// Files only on this PC start unticked, since no one else can reach them;
/// everything else starts ticked. The dialog says what the file reveals (the
/// paths) before anything is written, and nothing is written until Save.
///
/// UI-free and linked into the test project.
/// </summary>
public sealed class ShareSessionViewModel : ObservableObject
{
    /// <summary>What the shared file holds, said before it is saved.</summary>
    public const string PrivacyNote =
        "The shared file lists each ticked file's full path on this PC (which can include your user name) and its web or network address. It doesn't contain the files themselves, or when you opened them.";

    private readonly SessionSnapshot _session;
    private readonly TimeProvider _time;
    private string? _errorMessage;

    public ShareSessionViewModel(SessionSnapshot session, IReadOnlyList<CloudSyncRoot> roots, INetworkDriveResolver? network, TimeProvider time)
    {
        _session = session;
        _time = time;

        foreach (var path in session.Files)
        {
            var choice = new SharedFileChoiceViewModel(SessionSharing.Describe(path, roots, network));
            choice.PropertyChanged += Choice_PropertyChanged;
            Files.Add(choice);
        }
    }

    public string Title => $"Share \"{_session.Name}\"";

    public string SessionName => _session.Name;

    public ObservableCollection<SharedFileChoiceViewModel> Files { get; } = new();

    public int IncludedCount => Files.Count(f => f.IsIncluded);

    /// <summary>"3 of 4 files will be shared".</summary>
    public string IncludedText => $"{IncludedCount} of {SessionsViewModel.FileCount(Files.Count)} will be shared";

    /// <summary>What the recipient will and won't be able to reach, one line per kind that needs saying; null when every file is reachable.</summary>
    public string? Advice
    {
        get
        {
            var lines = new List<string>();
            var thisPc = Files.Count(f => f.Location == SharedFileLocation.ThisPc);
            var personal = Files.Count(f => f.Location == SharedFileLocation.PersonalCloud);
            if (thisPc > 0)
                lines.Add($"{Of(thisPc)} {(thisPc == 1 ? "is" : "are")} only on this PC, so no one else can open {(thisPc == 1 ? "it" : "them")}. {(thisPc == 1 ? "It's" : "They're")} left out unless you tick {(thisPc == 1 ? "it" : "them")}. Moving {(thisPc == 1 ? "it" : "them")} to a shared library first works best.");
            if (personal > 0)
                lines.Add($"{Of(personal)} {(personal == 1 ? "is" : "are")} in a personal OneDrive. Others can open {(personal == 1 ? "it" : "them")} only if {(personal == 1 ? "it was" : "they were")} shared with them in OneDrive.");
            return lines.Count == 0 ? null : string.Join("\n", lines);

            static string Of(int n) => n == 1 ? "1 file" : $"{n} files";
        }
    }

    public string SuggestedFileName => SharedSessionFormat.SuggestedFileName(_session.Name);

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public void SetAllIncluded(bool included)
    {
        foreach (var file in Files)
            file.IsIncluded = included;
    }

    /// <summary>
    /// The shared session as it will be written: the name, tags and ticked
    /// files, in the session's order. Null, with <see cref="ErrorMessage"/>
    /// set, when no file is ticked.
    /// </summary>
    public SharedSessionDocument? BuildDocument()
    {
        var included = Files.Where(f => f.IsIncluded).Select(f => f.Shared).ToList();
        if (included.Count == 0)
        {
            ErrorMessage = "Tick at least one file to share.";
            return null;
        }

        ErrorMessage = null;
        return new SharedSessionDocument
        {
            SharedAt = _time.GetUtcNow(),
            Name = _session.Name,
            Tags = _session.Tags.ToList(),
            Files = included,
        };
    }

    /// <summary>Called by the view when writing the file failed.</summary>
    public void NoteWriteFailed(string message) => ErrorMessage = message;

    private void Choice_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SharedFileChoiceViewModel.IsIncluded))
            return;

        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(IncludedText));
        if (IncludedCount > 0)
            ErrorMessage = null;
    }
}

/// <summary>One file in the Share Session dialog.</summary>
public sealed class SharedFileChoiceViewModel : ObservableObject
{
    private bool _isIncluded;

    public SharedFileChoiceViewModel(SharedSessionFile shared)
    {
        Shared = shared;
        _isIncluded = shared.Location != SharedFileLocation.ThisPc;
    }

    public SharedSessionFile Shared { get; }

    public SharedFileLocation Location => Shared.Location;

    public string Path => Shared.Path;

    public string FileName => DocumentPaths.FileName(Shared.Path);

    public string Folder => DocumentPaths.Folder(Shared.Path);

    /// <summary>Where it lives, in the words the dialog's column uses.</summary>
    public string LocationText => Shared.Location switch
    {
        SharedFileLocation.CloudLibrary => "SharePoint or Teams",
        SharedFileLocation.PersonalCloud => "Personal OneDrive",
        SharedFileLocation.Network => "Network share",
        _ => "This PC only",
    };

    /// <summary>How the recipient's PC will look for it: the web or network address, for the row's tooltip.</summary>
    public string Detail => Shared.Url ?? Shared.NetworkPath ?? "Only this PC's path is known.";

    public bool IsIncluded
    {
        get => _isIncluded;
        set => SetProperty(ref _isIncluded, value);
    }
}
