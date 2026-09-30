using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.Services.Library;

/// <summary>
/// Everything the Library reads from its four sources, copied at one moment
/// (configurable canvas plan M2): saved places, saved sessions, Recents'
/// day totals and kept folder detail, and Recent Files' kept opens, with
/// today's date and the local zone.
///
/// Captured on the UI thread, because PlacesService and SessionStore are
/// UI-thread only; <see cref="LibraryQueryEngine"/> then works on it off
/// the UI thread. Session snapshots and the store records are immutable;
/// <see cref="Place"/> objects are shared, not copied, so opening a row still
/// opens the saved place itself, and the engine only reads their fields.
/// UI-free and linked into the test project.
/// </summary>
public sealed record LibrarySnapshot(
    IReadOnlyList<Place> Places,
    IReadOnlyList<SessionSnapshot> Sessions,
    bool RecentsAvailable,
    string? RecentsNotice,
    IReadOnlyList<RecentsRootData> Roots,
    DateOnly FolderDetailKeptFrom,
    bool RecentFilesAvailable,
    string? RecentFilesNotice,
    RecentFilesSettingsSnapshot RecentFilesSettings,
    IReadOnlyList<RecentFileHistory> Files,
    DateOnly Today,
    TimeZoneInfo Zone)
{
    public static LibrarySnapshot Capture(PlacesService places, SessionStore sessions, ActivityStore activity,
        RecentFilesStore recentFiles, TimeProvider time)
    {
        var zone = time.LocalTimeZone;
        DateOnly Local(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

        var roots = activity.Roots
            .Select(root => new RecentsRootData(
                root.RootId,
                root.Enabled,
                Local(root.TrackingStartedAt),
                activity.QueryDayTotals(root.RootId) ?? new Dictionary<DateOnly, ActivityDayTotal>(),
                activity.QueryFolderDays(root.RootId) ?? Array.Empty<FolderDay>(),
                root.Path))
            .ToList();

        return new LibrarySnapshot(
            places.Places,
            sessions.Sessions,
            activity.IsAvailable,
            activity.Notice,
            roots,
            activity.DetailKeptFrom,
            recentFiles.IsAvailable,
            recentFiles.Notice,
            recentFiles.Settings,
            recentFiles.QueryHistory(),
            Local(time.GetUtcNow()),
            zone);
    }

    /// <summary>The local date of <paramref name="instant"/>.</summary>
    public DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);
}

/// <summary>One Recents root: whether it is tracking, since when, its day totals (a year) and its folder detail (<see cref="ActivityStore.DetailDays"/> days).</summary>
/// <param name="Path">The tracked folder's path, for scoping and folder levels.</param>
public sealed record RecentsRootData(
    string RootId,
    bool Enabled,
    DateOnly TrackingStartedOn,
    IReadOnlyDictionary<DateOnly, ActivityDayTotal> DayTotals,
    IReadOnlyList<FolderDay> FolderDays,
    string Path = "");
