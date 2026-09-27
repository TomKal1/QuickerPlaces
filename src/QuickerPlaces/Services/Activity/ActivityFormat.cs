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
}
