using System;
using System.Globalization;

namespace QuickerPlaces.Services.Activity;

/// <summary>Plain, culture-aware labels for the Activity period view.</summary>
public static class ActivityFormat
{
    public static string Duration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return "0m";
        var minutes = (long)duration.TotalMinutes;
        if (minutes == 0) return "<1m";
        var hours = minutes / 60;
        var rest = minutes % 60;
        return hours == 0 ? $"{rest}m" : rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
    }

    public static string LastVisited(DateTimeOffset instant, TimeZoneInfo zone, CultureInfo culture)
        => TimeZoneInfo.ConvertTime(instant, zone).ToString("g", culture);

    /// <summary>
    /// What Recents is tracking, in a line: the Recents panel shows it as is,
    /// and the header's tooltip after "Recents — ". Paused is the tracking
    /// host's own pause, while folders are tracked.
    /// </summary>
    public static string TrackingSummary(int enabledRoots, int allRoots, bool paused, bool allFolders = false)
        => enabledRoots > 0 && paused ? "Tracking paused"
            : enabledRoots > 0 && allFolders ? "Tracking all folders"
            : enabledRoots > 0 ? $"Tracking {enabledRoots} {(enabledRoots == 1 ? "folder" : "folders")}"
            : allRoots > 0 ? "Tracking is off for every folder"
            : "No folders tracked";
}
