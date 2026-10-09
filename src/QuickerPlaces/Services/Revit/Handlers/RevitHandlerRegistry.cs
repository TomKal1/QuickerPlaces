using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuickerPlaces.Services.Revit.Handlers;

/// <summary>A live copy of a handler: a running Revit that has it loaded.</summary>
public sealed record LiveHandlerInstance(int ProcessId, DateTime LoadedUtc, DateTime? ReadyUtc)
{
    /// <summary>False while Revit is still starting and the handler cannot take requests yet.</summary>
    public bool IsReady => ReadyUtc is not null;
}

/// <summary>A registered handler in one release, with its live instances.</summary>
public sealed record RegisteredHandler(
    HandlerRegistration Registration, IReadOnlyList<LiveHandlerInstance> Instances, DateTime? LastSeenUtc)
{
    public string HandlerId => Registration.HandlerId;
    public string DisplayName => Registration.DisplayName;
    public string? HandlerVersion => Registration.HandlerVersion;
    public IReadOnlyList<string> Actions => Registration.Actions;
    public DateTime WrittenUtc => Registration.WrittenUtc;

    /// <summary>True when a Revit of this release has the handler loaded.</summary>
    public bool IsLoaded => Instances.Count > 0;

    /// <summary>True when a loaded copy can take a request now.</summary>
    public bool IsReady => Instances.Any(i => i.IsReady);

    public bool Supports(string action) => Actions.Contains(action, StringComparer.Ordinal);
}

/// <summary>The handlers registered for one release, by display name.</summary>
public sealed record ReleaseHandlers(string Release, IReadOnlyList<RegisteredHandler> Handlers);

/// <summary>
/// What handlers exist and which are running (docs/revit-handler-protocol.md,
/// "Registration" and "Instance"). Reads <c>handlers\*.json</c> and
/// <c>instances\*.json</c> afresh on each call, skips any file that cannot be
/// read or is not valid without throwing, and takes every identity (handler,
/// release, process) from the file's contents, never its name.
///
/// An instance is live only when a process with its id exists and started
/// within one second of its <c>processStartUtc</c>; the rest are stale
/// (Revit crashed or was ended). An instance file with no registration for
/// its handler and release is ignored: the handler writes the registration
/// first. If two registration files name the same handler and release, the
/// newer <c>writtenUtc</c> wins.
///
/// UI-free and linked into the test project and the qp CLI.
/// </summary>
public sealed class RevitHandlerRegistry
{
    /// <summary>How far a process's start time may differ from the instance file's and still be the same process.</summary>
    public static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(1);

    private readonly RevitProtocolFolder _folder;
    private readonly IProcessProbe _processes;
    private readonly TimeProvider _time;

    public RevitHandlerRegistry(RevitProtocolFolder folder, IProcessProbe? processes = null, TimeProvider? time = null)
    {
        _folder = folder;
        _processes = processes ?? new SystemProcessProbe();
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// The handlers of every release, releases in order. For a handler with
    /// no live instance, <see cref="RegisteredHandler.LastSeenUtc"/> is the
    /// latest time any of its instance files records (its ready time, else
    /// its loaded time), or, when no instance file is left, the registration's
    /// <c>writtenUtc</c>; it is null while the handler is loaded.
    /// </summary>
    public IReadOnlyList<ReleaseHandlers> Read()
    {
        var registrations = new Dictionary<(string Release, string HandlerId), HandlerRegistration>();
        foreach (var path in RevitProtocolFolder.JsonFiles(_folder.HandlersFolder))
        {
            if (HandlerJson.ReadFile<HandlerRegistration>(path).Value is not { } registration)
                continue;

            var key = (registration.RevitRelease, registration.HandlerId);
            if (!registrations.TryGetValue(key, out var known) || registration.WrittenUtc > known.WrittenUtc)
                registrations[key] = registration;
        }

        var instances = new Dictionary<(string Release, string HandlerId), List<HandlerInstance>>();
        foreach (var path in RevitProtocolFolder.JsonFiles(_folder.InstancesFolder))
        {
            if (HandlerJson.ReadFile<HandlerInstance>(path).Value is not { } instance)
                continue;

            var key = (instance.RevitRelease, instance.HandlerId);
            if (!registrations.ContainsKey(key))
                continue;

            if (!instances.TryGetValue(key, out var list))
                instances[key] = list = [];
            list.Add(instance);
        }

        return registrations
            .GroupBy(pair => pair.Key.Release, pair => (pair.Key, pair.Value))
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ReleaseHandlers(group.Key, group
                .Select(item => Describe(item.Value, instances.GetValueOrDefault(item.Key)))
                .OrderBy(handler => handler.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(handler => handler.HandlerId, StringComparer.Ordinal)
                .ToList()))
            .ToList();
    }

    /// <summary>The handlers of one release; empty when there are none.</summary>
    public IReadOnlyList<RegisteredHandler> Read(string release) =>
        Read().FirstOrDefault(r => r.Release == release)?.Handlers ?? [];

    /// <summary>True when the file's process exists and started within <see cref="StartTimeTolerance"/> of the recorded start.</summary>
    public bool IsLive(HandlerInstance instance)
    {
        if (_processes.GetStartTimeUtc(instance.ProcessId) is not { } started)
            return false;
        return (started - instance.ProcessStartUtc).Duration() <= StartTimeTolerance;
    }

    /// <summary>
    /// Deletes instance files that are not live and were last written more
    /// than an hour ago; returns how many. Files that cannot be read are left alone.
    /// </summary>
    public int DeleteStaleInstances()
    {
        var cutoff = _time.GetUtcNow().UtcDateTime - HandlerProtocol.StaleInstanceAge;
        var deleted = 0;
        foreach (var path in RevitProtocolFolder.JsonFiles(_folder.InstancesFolder))
        {
            if (HandlerJson.ReadFile<HandlerInstance>(path).Value is not { } instance || IsLive(instance))
                continue;

            try
            {
                if (File.GetLastWriteTimeUtc(path) >= cutoff)
                    continue;
                File.Delete(path);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use or not ours to delete: try again next time.
            }
        }
        return deleted;
    }

    private RegisteredHandler Describe(HandlerRegistration registration, List<HandlerInstance>? files)
    {
        files ??= [];
        var live = files
            .Where(IsLive)
            .Select(i => new LiveHandlerInstance(i.ProcessId, i.LoadedUtc, i.ReadyUtc))
            .OrderBy(i => i.ProcessId)
            .ToList();
        if (live.Count > 0)
            return new RegisteredHandler(registration, live, null);

        var lastSeen = files.Count > 0
            ? files.Max(i => i.ReadyUtc ?? i.LoadedUtc)
            : registration.WrittenUtc;
        return new RegisteredHandler(registration, [], lastSeen);
    }
}
