using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Revit.Handlers;

namespace QuickerPlaces.Services.Revit.Opening;

/// <summary>What the caller may override for one open (qp's options); null keeps the settings' value.</summary>
public sealed record RevitOpenOverrides(string? HandlerId = null, string? LocalFolder = null, string? Worksets = null);

/// <summary>
/// Decides what to do for a Revit file (roadmap §4.21), as a pure function of
/// what is known: the file's own info, the installed releases, the running
/// Revits, the handler registry and the settings. It reads and writes
/// nothing itself (the mapped-drive resolver is the one outside call).
///
/// Rules: the release comes from the file, never "the newest Revit"; a
/// release that isn't installed is refused, never opened in another; a
/// central is opened only through a chosen, registered handler (the command
/// line fallback for centrals stays off until verified per release); a
/// central that records another path as its own central is a copy and is
/// refused. UI-free and linked into the test project and the qp CLI.
/// </summary>
public static class RevitOpenPlanner
{
    /// <summary>A Revit that started more than this ago without the handler has not got it loaded.</summary>
    public static readonly TimeSpan StartupGrace = TimeSpan.FromMinutes(2);

    public static RevitOpenPlan Plan(string filePath, RevitFileInfo info, IReadOnlyList<RevitInstall> installs,
        IReadOnlyList<RunningRevit> running, IReadOnlyList<ReleaseHandlers> handlers, AppSettings settings, DateTime nowUtc,
        INetworkDriveResolver? network = null, RevitOpenOverrides? overrides = null)
    {
        var name = filePath[(filePath.LastIndexOfAny(['\\', '/']) + 1)..];

        switch (info.Problem)
        {
            case RevitFileProblem.None:
                break;
            case RevitFileProblem.TooOld:
                return RevitOpenPlan.Refused(filePath, null, info.Release is { } old
                    ? $"{name} was saved in Revit {old}. QuickerPlaces supports Revit {RevitFileInfoReader.OldestSupportedRelease} and later."
                    : $"{name} was saved before Revit 2019. QuickerPlaces supports Revit {RevitFileInfoReader.OldestSupportedRelease} and later.");
            case RevitFileProblem.NotRevitFile:
                return RevitOpenPlan.Refused(filePath, null, $"{name} doesn't look like a Revit file.");
            case RevitFileProblem.NotFound:
                return RevitOpenPlan.Refused(filePath, null, $"{name} isn't there.");
            case RevitFileProblem.ReleaseNotRecorded:
                return RevitOpenPlan.Refused(filePath, null, $"{name} doesn't say which Revit release saved it, so it isn't opened. Open it in the Revit that saved it.");
            default:
                return RevitOpenPlan.Refused(filePath, null, $"{name} couldn't be read, so its Revit release isn't known ({info.Problem}). Try again.");
        }

        if (info.Release is not { } releaseNumber)
            return RevitOpenPlan.Refused(filePath, null, $"{name} doesn't say which Revit release saved it, so it isn't opened. Open it in the Revit that saved it.");

        var release = releaseNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (releaseNumber < RevitFileInfoReader.OldestSupportedRelease)
            return RevitOpenPlan.Refused(filePath, release, $"{name} was saved in Revit {release}. QuickerPlaces supports Revit {RevitFileInfoReader.OldestSupportedRelease} and later.");

        if (info.Worksharing == RevitWorksharing.Unknown)
            return RevitOpenPlan.Refused(filePath, release, $"{name} is workshared, but QuickerPlaces can't tell whether it is a central or a local (Revit may be mid-save). Open it in Revit {release}.");

        var install = installs.FirstOrDefault(i => i.Release == releaseNumber);
        if (install is null)
            return RevitOpenPlan.Refused(filePath, release, $"Revit {release} is not installed. {name} was saved in Revit {release}, and QuickerPlaces doesn't open it in another release.");

        if (info.Worksharing != RevitWorksharing.Central)
            return new RevitOpenPlan { Kind = RevitOpenKind.DirectOpen, FilePath = filePath, Release = release, ExePath = install.ExePath };

        if (CopyOfCentral(filePath, info.CentralModelPath, network) is { } recorded)
            return RevitOpenPlan.Refused(filePath, release,
                $"This file records its central as {recorded}; it looks like a copy of a central. Opening a new local from the copy would not be a local of the real central. Open the central at {recorded}, or open this copy in Revit {release} yourself.");

        var effective = RevitSettingsResolver.For(settings, release);
        var handlerId = string.IsNullOrWhiteSpace(overrides?.HandlerId) ? effective.HandlerId : overrides.HandlerId.Trim();
        var localFolder = string.IsNullOrWhiteSpace(overrides?.LocalFolder) ? effective.LocalFolder : overrides.LocalFolder.Trim();
        var worksets = string.IsNullOrWhiteSpace(overrides?.Worksets) ? HandlerProtocol.Worksets.LastViewed : overrides.Worksets.Trim();

        if (!HandlerProtocol.Worksets.IsKnown(worksets))
            return RevitOpenPlan.Refused(filePath, release, $"Worksets must be {HandlerProtocol.Worksets.LastViewed}, {HandlerProtocol.Worksets.All} or {HandlerProtocol.Worksets.None}, not \"{worksets}\".");

        if (handlerId is null)
            return RevitOpenPlan.Refused(filePath, release,
                $"{name} is a central model. QuickerPlaces opens centrals as new locals through a Revit handler add-in, and none is chosen for Revit {release}. Choose one in Settings (a handler appears there after Revit {release} has run once with it loaded), or open the file in Revit yourself.");

        var registered = handlers.FirstOrDefault(r => r.Release == release)?.Handlers.FirstOrDefault(h => h.HandlerId == handlerId);
        if (registered is null)
            return RevitOpenPlan.Refused(filePath, release,
                $"The handler \"{handlerId}\" has not registered for Revit {release}. Start Revit {release} once with it installed, or choose another handler in Settings.");
        if (!registered.Supports(HandlerProtocol.ActionOpenNewLocal))
            return RevitOpenPlan.Refused(filePath, release,
                $"The handler \"{registered.DisplayName}\" doesn't support opening a central as a new local. Choose another handler for Revit {release} in Settings.");

        var mine = running.Where(r => r.Release == releaseNumber).ToList();
        return new RevitOpenPlan
        {
            Kind = RevitOpenKind.HandlerRequest,
            FilePath = filePath,
            Release = release,
            ExePath = install.ExePath,
            HandlerId = handlerId,
            HandlerName = registered.DisplayName,
            LocalFolder = localFolder,
            Worksets = worksets,
            Availability = Assess(registered, mine, nowUtc),
            RunningProcessIds = mine.Select(r => r.ProcessId).ToList(),
        };
    }

    /// <summary>
    /// Whether the handler can be expected to take a request. A live
    /// instance means Loaded. Otherwise: no Revit of the release running is
    /// NotRunning; any that started within <see cref="StartupGrace"/> is
    /// Starting (it may still load the handler); else NotLoaded.
    /// <paramref name="runningOfRelease"/> must already be the release's Revits.
    /// </summary>
    public static HandlerAvailability Assess(RegisteredHandler handler, IReadOnlyList<RunningRevit> runningOfRelease, DateTime nowUtc)
    {
        if (handler.IsLoaded)
            return HandlerAvailability.Loaded;
        if (runningOfRelease.Count == 0)
            return HandlerAvailability.NotRunning;
        return runningOfRelease.Any(r => nowUtc - r.StartUtc < StartupGrace)
            ? HandlerAvailability.Starting
            : HandlerAvailability.NotLoaded;
    }

    /// <summary>
    /// The central path the file records when it differs from where the file
    /// is, after mapped drives are resolved to UNC; null when they agree or
    /// when either can't be resolved (then nothing is said).
    /// </summary>
    private static string? CopyOfCentral(string filePath, string? recorded, INetworkDriveResolver? network)
    {
        if (string.IsNullOrWhiteSpace(recorded))
            return null;

        var actual = Canonical(filePath, network, out var actualResolved);
        var other = Canonical(recorded, network, out var otherResolved);
        if (string.Equals(actual, other, StringComparison.OrdinalIgnoreCase))
            return null;
        return actualResolved && otherResolved ? recorded : null;
    }

    /// <summary>
    /// Trimmed, forward slashes made backslashes. A UNC path is resolved as it
    /// is. For a drive-letter path the resolver is asked for its UNC spelling:
    /// an answer replaces the path, and null means "not a mapped drive", so
    /// the path itself is the canonical form (a local disk compares by its
    /// own letter). <paramref name="resolved"/> is false only when there is no
    /// resolver, or it throws, or the path is neither drive nor UNC (a Revit
    /// Server path, a relative path). Blind spot: a SUBST drive, or a mapped
    /// drive whose lookup fails, is compared by its letter; that can only
    /// cause a refusal, whose message already tells the user how to open the
    /// file themselves.
    /// </summary>
    private static string Canonical(string path, INetworkDriveResolver? network, out bool resolved)
    {
        var text = path.Trim().Replace('/', '\\').TrimEnd('\\');
        if (text.StartsWith(@"\\", StringComparison.Ordinal))
        {
            resolved = true;
            return text;
        }

        if (network is not null && text.Length >= 3 && text[1] == ':' && char.IsAsciiLetter(text[0]) && text[2] == '\\')
        {
            try
            {
                var unc = network.GetNetworkPath(text);
                resolved = true;
                return unc is null ? text : unc.Replace('/', '\\').TrimEnd('\\');
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or DllNotFoundException)
            {
                // Can't resolve: unresolved below.
            }
        }

        resolved = false;
        return text;
    }
}
