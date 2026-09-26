using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickerPlaces.Services.Activity;

/// <summary>
/// The accounting loop of Phase 9's folder activity tracking (plan 5.1):
/// turns successive probe passes into time credited to folders under the
/// tracked roots. The host (step 3) calls <see cref="Tick"/> on its own STA
/// thread every <see cref="PollInterval"/>, and <see cref="DiscardGap"/> on
/// lock and suspend; the store (step 2) sums what Tick returns.
///
/// No COM, no P/Invoke and no machine clock: the probe, presence, the
/// monotonic clock and the TimeProvider are all injected, so every rule
/// below is a unit test. UI-free and linked into the test project.
///
/// - D6: each tick credits the time since the previous one to the folder
///   that was in the foreground at the previous one, measured on the
///   monotonic clock and capped at twice the poll interval. A lock or a
///   suspend discards the gap outright.
/// - D7: only the foreground folder, and only while the user has been idle
///   for less than the root's timeout.
/// - D9: a visit counts once it has been seen in the foreground past its
///   dwell threshold, and time accrues from the threshold, not from arrival.
///   A visit ends when the root's foreground folder changes, including to
///   none. A visit still inside its dwell is dropped by idle, a lock or a
///   suspend, so its dwell starts again.
/// - D14: when the foreground tabs credit different folders, or one credits
///   none, nothing is credited until they agree.
/// - D19: time is split at local midnight in the injected clock's zone.
/// </summary>
public sealed class FolderActivityTracker
{
    /// <summary>How often to tick while an Explorer window is in the foreground (D2).</summary>
    public static readonly TimeSpan ForegroundPollInterval = TimeSpan.FromSeconds(1.5);

    /// <summary>How often to tick while none is (D2).</summary>
    public static readonly TimeSpan BackgroundPollInterval = TimeSpan.FromSeconds(15);

    private readonly IShellWindowProbe _probe;
    private readonly IUserPresence _presence;
    private readonly IMonotonicClock _clock;
    private readonly TimeProvider _timeProvider;

    private List<TrackedRootConfig> _roots = new();

    /// <summary>Each root's current visit, by RootId: the folder in the foreground at the previous tick.</summary>
    private readonly Dictionary<string, Visit> _visits = new(StringComparer.Ordinal);

    /// <summary>The monotonic time of the previous tick; null when there is none to measure from.</summary>
    private TimeSpan? _previousTick;

    public FolderActivityTracker(IShellWindowProbe probe, IUserPresence presence, IMonotonicClock clock, TimeProvider timeProvider)
    {
        _probe = probe;
        _presence = presence;
        _clock = clock;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// How long the host should wait before the next tick (D2), which is also
    /// what the next tick's cap is measured against (D6).
    /// </summary>
    public TimeSpan PollInterval { get; private set; } = ForegroundPollInterval;

    /// <summary>
    /// The enabled roots to track. A root that stays keeps its current visit;
    /// a root that goes stops being credited at once.
    /// </summary>
    public void SetRoots(IEnumerable<TrackedRootConfig> roots)
    {
        _roots = roots.ToList();
        var kept = _roots.Select(r => r.RootId).ToHashSet(StringComparer.Ordinal);
        foreach (var rootId in _visits.Keys.Where(id => !kept.Contains(id)).ToList())
            _visits.Remove(rootId);
    }

    /// <summary>
    /// Forgets the time since the previous tick, for a lock, a suspend or a
    /// resume (D6): the next tick measures from itself.
    /// </summary>
    public void DiscardGap()
    {
        _previousTick = null;
        foreach (var rootId in _visits.Where(v => !v.Value.Counted).Select(v => v.Key).ToList())
            _visits.Remove(rootId);
    }

    /// <summary>
    /// Samples the probe and returns the time credited since the previous
    /// tick, split by root, folder and local day. While the session is locked
    /// it neither samples nor credits anything.
    /// </summary>
    public IReadOnlyList<ActivityInterval> Tick()
    {
        if (_presence.SessionLocked)
        {
            DiscardGap();
            return Array.Empty<ActivityInterval>();
        }

        var now = _clock.Elapsed;
        var foreground = _probe.Sample().Where(w => w.IsForeground).ToList();
        TimeSpan? span = _previousTick is { } previous
            ? Min(now - previous, PollInterval + PollInterval)
            : null;

        var intervals = new List<ActivityInterval>();
        foreach (var root in _roots)
        {
            var folder = ForegroundFolder(foreground, root);
            var present = _presence.IdleFor < root.IdleTimeout;
            _visits.TryGetValue(root.RootId, out var visit);

            if (visit is not null && span is { } elapsed && present)
                Accrue(root, visit, SameFolder(visit.Folder, folder), now, elapsed, intervals);

            var next = NextVisit(visit, folder, present, now);
            if (next is null)
                _visits.Remove(root.RootId);
            else
                _visits[root.RootId] = next;
        }

        _previousTick = now;
        PollInterval = foreground.Count > 0 ? ForegroundPollInterval : BackgroundPollInterval;
        return intervals;
    }

    private sealed class Visit
    {
        public Visit(string folder, TimeSpan arrivedAt)
        {
            Folder = folder;
            ArrivedAt = arrivedAt;
        }

        public string Folder { get; }

        public TimeSpan ArrivedAt { get; }

        /// <summary>True once the visit has crossed its dwell threshold and been counted (D9).</summary>
        public bool Counted { get; set; }
    }

    /// <summary>
    /// The folder the foreground window credits under <paramref name="root"/>,
    /// or null when there is none, or when the foreground tabs disagree (D14).
    /// </summary>
    private static string? ForegroundFolder(IReadOnlyList<ShellWindowSnapshot> foreground, TrackedRootConfig root)
    {
        string? folder = null;
        foreach (var window in foreground)
        {
            var credited = RootPathMatcher.Credit(window.Path, root);
            if (credited is null || (folder is not null && !SameFolder(folder, credited)))
                return null;

            folder ??= credited;
        }

        return folder;
    }

    /// <summary>
    /// Credits the last <paramref name="elapsed"/> to the visit's folder. A
    /// visit not yet counted is credited only if it is still there and past
    /// its dwell threshold, and only from the threshold on (D9).
    /// </summary>
    private void Accrue(TrackedRootConfig root, Visit visit, bool stillThere, TimeSpan now, TimeSpan elapsed, List<ActivityInterval> into)
    {
        var from = now - elapsed;
        var startsVisit = false;
        if (!visit.Counted)
        {
            var crossing = visit.ArrivedAt + root.DwellThreshold;
            if (!stillThere || now < crossing)
                return;

            from = Max(from, crossing);
            visit.Counted = true;
            startsVisit = true;
        }

        if (now > from || startsVisit)
            Emit(root.RootId, visit.Folder, now - from, startsVisit, into);
    }

    /// <summary>
    /// The root's visit after this tick: the same one while its folder stays
    /// in the foreground, a new one when another folder arrives, and none
    /// when no folder is there. While the user is away no visit arrives, and
    /// one still inside its dwell is dropped (D7, D9).
    /// </summary>
    private static Visit? NextVisit(Visit? visit, string? folder, bool present, TimeSpan now)
    {
        if (folder is null)
            return null;

        if (visit is not null && SameFolder(visit.Folder, folder) && (visit.Counted || present))
            return visit;

        return present ? new Visit(folder, now) : null;
    }

    /// <summary>
    /// Adds <paramref name="duration"/>, ending now on the wall clock, split
    /// at each local midnight it spans (D19). A visit is counted on the first
    /// piece only.
    /// </summary>
    private void Emit(string rootId, string folder, TimeSpan duration, bool startsVisit, List<ActivityInterval> into)
    {
        var zone = _timeProvider.LocalTimeZone;
        var end = _timeProvider.GetUtcNow();
        var start = end - duration;
        var day = LocalDate(start, zone);
        var endDay = LocalDate(end, zone);

        while (day < endDay)
        {
            var midnight = LocalMidnightUtc(day.AddDays(1), zone);
            into.Add(new ActivityInterval(rootId, folder, day, midnight - start, startsVisit, midnight));
            startsVisit = false;
            start = midnight;
            day = day.AddDays(1);
        }

        if (end > start || startsVisit)
            into.Add(new ActivityInterval(rootId, folder, day, end - start, startsVisit, end));
    }

    private static DateOnly LocalDate(DateTimeOffset utc, TimeZoneInfo zone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, zone).DateTime);

    /// <summary>
    /// The UTC instant <paramref name="day"/> begins in <paramref name="zone"/>.
    /// In a zone whose clocks skip midnight, the day begins at the first
    /// minute that exists.
    /// </summary>
    private static DateTimeOffset LocalMidnightUtc(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(1);

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    private static bool SameFolder(string? a, string? b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
