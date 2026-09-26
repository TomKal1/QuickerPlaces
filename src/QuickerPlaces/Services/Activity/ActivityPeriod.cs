using System;
using System.Collections.Generic;

namespace QuickerPlaces.Services.Activity;

/// <summary>
/// A root's folders summed over the local dates <see cref="From"/> to
/// <see cref="To"/> inclusive (plan 5.4), most time first, and what the
/// window needs to say honestly how much of the period is really there
/// (§1, §7): days before tracking started were never recorded, and folder
/// detail older than the retention window has been deleted.
/// </summary>
public sealed record ActivityPeriod(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<FolderActivity> Folders,
    DateOnly TrackingStartedOn,
    DateOnly DetailKeptFrom)
{
    /// <summary>The period starts before tracking did: "no data yet — tracking started on …".</summary>
    public bool StartsBeforeTracking => From < TrackingStartedOn;

    /// <summary>Part of the period's folder detail has expired, so its totals are not the whole period.</summary>
    public bool DetailExpired => From < DetailKeptFrom;
}
