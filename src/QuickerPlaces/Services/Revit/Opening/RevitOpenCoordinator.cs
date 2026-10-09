using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Revit.AddIns;
using QuickerPlaces.Services.Revit.Dialogs;
using QuickerPlaces.Services.Revit.Handlers;

namespace QuickerPlaces.Services.Revit.Opening;

/// <summary>Asks the user a yes/no question; the app shows it in its own dialog, tests answer it by hand.</summary>
public interface IRevitOpenPrompts
{
    /// <summary>True for yes. Called on the thread that called <see cref="IRevitOpenCoordinator.OpenAsync"/> (the UI thread in the app).</summary>
    Task<bool> AskAsync(string question);
}

/// <summary>How one Revit open ended, in words for the status line (<see cref="Message"/>), and whether it is a problem.</summary>
public sealed record RevitOpenReport(RevitOpenOutcome Outcome, string Message, bool IsError);

/// <summary>What the Library needs from the Revit open: one call, with status text on the way.</summary>
public interface IRevitOpenCoordinator
{
    /// <summary>
    /// Opens a Revit file the right way for what it is. Never throws: a problem
    /// is a report with <see cref="RevitOpenReport.IsError"/>, and cancelling
    /// is a report that isn't an error. <paramref name="status"/> gets the text to show while it runs.
    /// </summary>
    Task<RevitOpenReport> OpenAsync(string filePath, IProgress<string>? status = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// One Revit open from the Library's point of view (roadmap §4.21; the
/// handoff's "App UI" step). It reads the file's release through the shared
/// <see cref="RevitReleaseCache"/> (off the calling thread), gathers what is
/// installed, running and registered, asks <see cref="RevitOpenPlanner"/> what
/// to do and has <see cref="RevitOpener"/> do it.
///
/// While the opener waits on a Revit that QuickerPlaces launched, it looks at
/// that Revit's windows on each poll (until the handler's instance appears
/// there): a dialog Revit is waiting on becomes the status text ("Revit is
/// waiting on a dialog: ..."), and <see cref="LoadOnceClicker"/> may press
/// Load Once if the user switched that on and the add-in is on their list.
/// A security prompt that a signature recognised and that then disappeared
/// without the clicker pressing it was answered by the user: after the open,
/// each such add-in is offered for the list ("Allow Load Once for X next
/// time?"), and only a yes adds it. An allowed add-in whose DLL changed is
/// removed from the list and the user is asked again. Both changes are saved
/// at once. With no verified signature for the release nothing is recognised,
/// so nothing is offered or pressed.
///
/// The last step (settings and questions) runs on the context that called
/// <see cref="OpenAsync"/>, so the question can be a dialog. Every exception
/// is logged and turned into a report. UI-free and linked into the test project.
/// </summary>
public sealed class RevitOpenCoordinator : IRevitOpenCoordinator
{
    private readonly RevitMachine _machine;
    private readonly RevitReleaseCache _cache;
    private readonly RevitHandlerRegistry _registry;
    private readonly RevitOpener _opener;
    private readonly IDialogDetector _detector;
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly IRevitOpenPrompts _prompts;
    private readonly Func<SecurityPromptSignatures> _signatures;
    private readonly Func<int, IReadOnlyList<AddInManifest>> _manifests;
    private readonly Func<string, string?> _hashOf;
    private readonly LoadOnceClicker _clicker;
    private readonly TimeProvider _time;

    /// <param name="queue">Optional, for tests that control the poll rhythm; its folder and clock must be the ones given here.</param>
    /// <param name="signatures">Defaults to the table next to the app (<see cref="SecurityPromptSignatures.FileName"/>).</param>
    /// <param name="manifests">The add-in manifests of a release; defaults to the folders Revit reads on this machine.</param>
    /// <param name="hashOf">Hashes a DLL; defaults to <see cref="DllFingerprint.Sha256"/>.</param>
    public RevitOpenCoordinator(RevitMachine machine, RevitProtocolFolder folder, RevitReleaseCache cache, IDialogDetector detector,
        AppSettings settings, SettingsService settingsService, IRevitOpenPrompts prompts, TimeProvider? time = null,
        RevitRequestQueue? queue = null, Func<SecurityPromptSignatures>? signatures = null,
        Func<int, IReadOnlyList<AddInManifest>>? manifests = null, Func<string, string?>? hashOf = null)
    {
        _machine = machine;
        _cache = cache;
        _detector = detector;
        _settings = settings;
        _settingsService = settingsService;
        _prompts = prompts;
        _time = time ?? TimeProvider.System;
        _hashOf = hashOf ?? DllFingerprint.Sha256;
        _registry = new RevitHandlerRegistry(folder, machine.Probe, _time);
        _opener = new RevitOpener(folder, queue ?? new RevitRequestQueue(folder, _time), _registry, machine, _time);
        _clicker = new LoadOnceClicker(detector, _hashOf);
        _signatures = signatures ?? (() => SecurityPromptSignatures.Load(Path.Combine(AppContext.BaseDirectory, SecurityPromptSignatures.FileName)));
        _manifests = manifests ?? (release => AddInManifestFinder.Find(release, AddInLocations.ForThisMachine()).Manifests);
    }

    public async Task<RevitOpenReport> OpenAsync(string filePath, IProgress<string>? status = null, CancellationToken cancellationToken = default)
    {
        var name = NameOf(filePath);
        try
        {
            status?.Report($"Checking {name}…");
            RevitOpenPlan plan;
            try
            {
                plan = await Task.Run(() => Plan(filePath), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Report(new RevitOpenOutcome(RevitOpenResult.Cancelled, "Cancelled."), null);
            }

            if (plan.Kind == RevitOpenKind.Refuse)
            {
                var refused = new RevitOpenOutcome(RevitOpenResult.Refused, plan.RefusalReason ?? "The file isn't opened.");
                Log(name, plan, refused);
                return Report(refused, plan);
            }

            var watch = new Watch(this, plan, status);
            var outcome = await Task.Run(async () =>
            {
                var result = await _opener.RunAsync(plan, watch, wait: true, cancellationToken, watch.Poll);
                watch.Settle();
                return result;
            }, CancellationToken.None);
            Log(name, plan, outcome);

            // Back on the caller's context: the settings change and the questions.
            await Afterwards(plan, watch);
            return Report(outcome, plan);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error($"Opening the Revit file {name} failed.", ex);
            return new RevitOpenReport(new RevitOpenOutcome(RevitOpenResult.Failed, ex.Message), $"Couldn't open {name}: {ex.Message}", true);
        }
    }

    private RevitOpenPlan Plan(string filePath)
    {
        var info = _cache.Refresh([filePath]).TryGetValue(filePath, out var read) ? read : RevitFileInfo.Failed(RevitFileProblem.Unreadable);
        var installs = _machine.Installs();
        var running = _machine.RunningRevits(installs);
        return RevitOpenPlanner.Plan(filePath, info, installs, running, _registry.Read(), _settings,
            _time.GetUtcNow().UtcDateTime, _machine.Network);
    }

    /// <summary>The final words: "Opened X in Revit 2025." for a handler open, the outcome's own message otherwise.</summary>
    private static RevitOpenReport Report(RevitOpenOutcome outcome, RevitOpenPlan? plan)
    {
        if (outcome.Result == RevitOpenResult.Opened)
        {
            var local = outcome.LocalPath is { Length: > 0 } path ? NameOf(path) : NameOf(plan?.FilePath ?? "");
            return new RevitOpenReport(outcome, $"Opened {local} in Revit {plan?.Release}.", false);
        }

        return new RevitOpenReport(outcome, outcome.Message, !outcome.Ok && outcome.Result != RevitOpenResult.Cancelled);
    }

    /// <summary>One line per open, with the dialogs Revit raised during it: the evidence a handler's allowlist grows from.</summary>
    private static void Log(string name, RevitOpenPlan plan, RevitOpenOutcome outcome)
    {
        var dialogs = outcome.DialogList;
        var seen = dialogs.Count == 0 ? "" : ": " + string.Join(", ", dialogs.Select(d =>
            $"{d.DialogId ?? "?"} ({d.Kind ?? "?"}, {(d.Answered ? "answered" : "left")})"));
        DiagnosticLog.Info($"Revit open of {name} in Revit {plan.Release ?? "?"}: {outcome.Result}"
            + (outcome.ErrorCode is null ? "" : $" ({outcome.ErrorCode})") + $"; {dialogs.Count} dialog(s) raised{seen}.");
    }

    private static string NameOf(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    // ---------------------------------------------------------------
    // After the open: removals, follow-ups and offers
    // ---------------------------------------------------------------

    private async Task Afterwards(RevitOpenPlan plan, Watch watch)
    {
        try
        {
            var release = watch.Release;
            var entries = _settings.LoadOnceEntries ??= new List<LoadOnceEntry>();

            if (watch.EntriesToRemove.Count > 0)
            {
                foreach (var stale in watch.EntriesToRemove)
                    entries.Remove(stale);
                _settingsService.Save(_settings);
            }

            // The questions about add-ins that changed since they were allowed.
            var asked = new List<string>();
            foreach (var (stale, question) in watch.FollowUps)
            {
                asked.Add(stale.Name);
                if (await _prompts.AskAsync(question))
                    Allow(release, stale.Name, stale.DllPath);
            }

            // The prompts the user answered themselves.
            foreach (var prompt in watch.Answered)
            {
                if (asked.Any(a => LoadOnceOffers.SameName(a, prompt.Name)))
                    continue;

                var offer = LoadOnceOffers.Offer(release, prompt.Name, prompt.DllPath, watch.Manifests(), _time.GetUtcNow(), _hashOf);
                if (offer.Entry is not { } entry)
                {
                    DiagnosticLog.Info($"Load Once: not offered for \"{prompt.Name}\" in Revit {release}: {offer.Reason}");
                    continue;
                }
                if (entries.Any(e => Same(e, entry)))
                    continue;

                if (await _prompts.AskAsync($"Allow Load Once for {entry.Name} next time?"))
                    Add(entry);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("Updating the Load Once list after a Revit open failed.", ex);
        }
    }

    private void Allow(int release, string name, string dllPath)
    {
        var offer = LoadOnceOffers.Offer(release, name, dllPath, ManifestsOf(release), _time.GetUtcNow(), _hashOf);
        if (offer.Entry is { } entry)
            Add(entry);
        else
            DiagnosticLog.Info($"Load Once: \"{name}\" in Revit {release} not allowed again: {offer.Reason}");
    }

    private IReadOnlyList<AddInManifest> ManifestsOf(int release)
    {
        try
        {
            return _manifests(release);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Adds the entry, replacing one for the same add-in, and saves.</summary>
    private void Add(LoadOnceEntry entry)
    {
        var entries = _settings.LoadOnceEntries ??= new List<LoadOnceEntry>();
        entries.RemoveAll(e => e.Release == entry.Release && LoadOnceOffers.SameName(e.Name, entry.Name) && LoadOnceOffers.SamePath(e.DllPath, entry.DllPath));
        entries.Add(entry);
        _settingsService.Save(_settings);
        DiagnosticLog.Info($"Load Once: allowed \"{entry.Name}\" for Revit {entry.Release} (SHA-256 {entry.DllSha256}).");
    }

    private static bool Same(LoadOnceEntry a, LoadOnceEntry b)
        => a.Release == b.Release && LoadOnceOffers.SameName(a.Name, b.Name) && LoadOnceOffers.SamePath(a.DllPath, b.DllPath)
            && DllFingerprint.SameHash(a.DllSha256, b.DllSha256);

    // ---------------------------------------------------------------
    // Watching the launched Revit while the opener waits
    // ---------------------------------------------------------------

    /// <summary>
    /// The state of one open's wait. Everything here runs inside the opener's
    /// poll loop, one call at a time, so it needs no locks; the caller reads
    /// the results only after the opener has returned.
    /// </summary>
    private sealed class Watch : IProgress<RevitOpenStatus>
    {
        private readonly RevitOpenCoordinator _owner;
        private readonly RevitOpenPlan _plan;
        private readonly IProgress<string>? _status;
        private readonly Lazy<SecurityPromptSignatures> _signatureTable;
        private readonly bool _switchOn;
        private readonly IReadOnlyList<LoadOnceEntry> _entries;
        private readonly Dictionary<long, SecurityPromptRecognition> _pending = [];
        private readonly HashSet<string> _pressed = new(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<AddInManifest>? _manifests;
        private string? _openerText;
        private string? _dialogText;
        private string? _shown;
        private int? _pid;
        private bool _stopped;

        public Watch(RevitOpenCoordinator owner, RevitOpenPlan plan, IProgress<string>? status)
        {
            _owner = owner;
            _plan = plan;
            _status = status;
            Release = int.Parse(plan.Release!, System.Globalization.CultureInfo.InvariantCulture);
            _signatureTable = new Lazy<SecurityPromptSignatures>(owner._signatures);
            _switchOn = owner._settings.AllowLoadOnce;
            _entries = (owner._settings.LoadOnceEntries ?? []).ToList();
        }

        public int Release { get; }

        private SecurityPromptSignatures Signatures => _signatureTable.Value;

        /// <summary>Entries whose DLL changed since they were allowed.</summary>
        public List<LoadOnceEntry> EntriesToRemove { get; } = [];

        public List<(LoadOnceEntry Entry, string Question)> FollowUps { get; } = [];

        /// <summary>Prompts a signature recognised that the user answered (they went away and QuickerPlaces didn't press them).</summary>
        public List<SecurityPromptRecognition> Answered { get; } = [];

        public IReadOnlyList<AddInManifest> Manifests()
        {
            return _manifests ??= _owner.ManifestsOf(Release);
        }

        /// <summary>The opener's status: shown unless a dialog is waiting.</summary>
        public void Report(RevitOpenStatus value)
        {
            _openerText = value.Text;
            Show();
        }

        /// <summary>One look at the launched Revit's windows; called on each poll of the wait.</summary>
        public void Poll(int processId)
        {
            if (_stopped)
                return;

            try
            {
                _pid = processId;
                var windows = _owner._detector.ListWindows(processId);
                var handlerAppeared = HandlerAppeared(processId);
                _dialogText = handlerAppeared ? null : RevitDialogClassifier.Classify(windows, Release, Signatures).StatusText;
                Show();

                if (handlerAppeared)
                {
                    // The launch is over: no more pressing, and the last look at what the user answered.
                    Note(windows);
                    _stopped = true;
                    return;
                }

                var context = new LoadOnceContext(_switchOn, true, false, Release, Signatures, _entries, _switchOn ? Manifests() : []);
                var result = _owner._clicker.Poll(processId, context);
                foreach (var stale in result.EntriesToRemove)
                {
                    if (!EntriesToRemove.Contains(stale))
                        EntriesToRemove.Add(stale);
                }
                foreach (var decision in result.Decisions.Where(d => d.EntryToRemove is not null && d.FollowUp is not null))
                {
                    if (FollowUps.All(f => f.Entry != decision.EntryToRemove))
                        FollowUps.Add((decision.EntryToRemove!, decision.FollowUp!));
                }
                if (result.Clicked)
                {
                    // Pressed by QuickerPlaces, so not an answer to offer for.
                    foreach (var pressed in result.Decisions.Where(d => d.MayClick && d.Prompt is not null))
                    {
                        _pressed.Add(pressed.Prompt!.Name);
                        foreach (var key in _pending.Where(p => LoadOnceOffers.SameName(p.Value.Name, pressed.Prompt!.Name)).Select(p => p.Key).ToList())
                            _pending.Remove(key);
                    }
                }

                Note(windows);
            }
            catch (Exception ex)
            {
                // Looking at windows must never end the open.
                DiagnosticLog.Warn($"Watching Revit's dialogs failed ({ex.GetType().Name}).");
                _stopped = true;
            }
        }

        /// <summary>The last look, when the open ends before the handler appeared.</summary>
        public void Settle()
        {
            if (_stopped || _pid is not { } pid)
                return;

            try
            {
                Note(_owner._detector.ListWindows(pid));
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn($"Watching Revit's dialogs failed ({ex.GetType().Name}).");
            }
            _stopped = true;
        }

        /// <summary>
        /// Remembers the security prompts a verified signature recognises; one
        /// that was remembered and is gone while its Revit still runs was
        /// answered by the user.
        /// </summary>
        private void Note(IReadOnlyList<DialogWindow> windows)
        {
            if (_pid is not { } pid || _owner._machine.Probe.GetStartTimeUtc(pid) is null)
            {
                _pending.Clear();
                return;
            }

            var present = windows.Select(w => w.Handle).ToHashSet();
            foreach (var gone in _pending.Where(p => !present.Contains(p.Key)).ToList())
            {
                _pending.Remove(gone.Key);
                if (!Answered.Any(a => LoadOnceOffers.SameName(a.Name, gone.Value.Name) && LoadOnceOffers.SamePath(a.DllPath, gone.Value.DllPath)))
                    Answered.Add(gone.Value);
            }

            foreach (var window in windows.Where(w => RevitDialogClassifier.Judge(w) == DialogVerdict.Waiting))
            {
                if (Signatures.Recognise(Release, window, verifiedOnly: true) is { } prompt && !_pressed.Contains(prompt.Name))
                    _pending[window.Handle] = prompt;
            }
        }

        private bool HandlerAppeared(int processId)
            => _owner._registry.Read(_plan.Release!).FirstOrDefault(h => h.HandlerId == _plan.HandlerId)
                ?.Instances.Any(i => i.ProcessId == processId) == true;

        private void Show()
        {
            var text = _dialogText ?? _openerText;
            if (text is null || text == _shown)
                return;
            _shown = text;
            _status?.Report(text);
        }
    }
}
