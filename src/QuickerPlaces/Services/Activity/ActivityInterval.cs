using System;

namespace QuickerPlaces.Services.Activity;

/// <summary>
/// Time the tracker credited to one folder on one local day (Phase 9 plan
/// 5.1, D19), which the activity store sums. <see cref="Folder"/> is spelled
/// with the root's own path (5.2). <see cref="StartsVisit"/> is true on the
/// interval in which a visit first crossed its dwell threshold (D9), so the
/// store counts a visit once however many intervals it spans; such an
/// interval can have a zero duration. <see cref="LastSeenAt"/> is the UTC
/// instant the interval ended.
/// </summary>
public sealed record ActivityInterval(
    string RootId,
    string Folder,
    DateOnly Date,
    TimeSpan Duration,
    bool StartsVisit,
    DateTimeOffset LastSeenAt);
