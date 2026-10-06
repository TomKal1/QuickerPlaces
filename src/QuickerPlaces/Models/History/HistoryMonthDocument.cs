using System;
using System.Collections.Generic;
using QuickerPlaces.Models.Activity;

namespace QuickerPlaces.Models.History;

/// <summary>
/// One month of one PC's activity history (history plan §3): a file in
/// Documents\QuickerPlaces\History named "2026-09 (DESKTOP-ABC).json". It
/// holds what activity.json and recent-files.json hold for those days: each
/// tracked folder's time per day, each day's totals, and every recorded file
/// open. Unlike them, it is never pruned.
/// </summary>
public sealed class HistoryMonthDocument
{
    /// <summary>ActivityHistory writes its CurrentSchemaVersion and never writes over a newer one.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>The month, "2026-09". Dates in the file are local dates in it.</summary>
    public string Month { get; set; } = "";

    /// <summary>The PC that wrote the file. Each PC writes only its own files; reading merges them all.</summary>
    public string Machine { get; set; } = "";

    /// <summary>Folder activity, per tracked root as activity.json keeps it.</summary>
    public List<HistoryRoot> Roots { get; set; } = new();

    /// <summary>Recent Files' opens in the month.</summary>
    public List<HistoryFile> Files { get; set; } = new();
}

/// <summary>One tracked root's days in a month, keyed by local date, in activity.json's own shapes.</summary>
public sealed class HistoryRoot
{
    /// <summary>The root as activity.json spells it. Roots are matched by path, since ids are per PC.</summary>
    public string Path { get; set; } = "";

    /// <summary>Each day's time per folder.</summary>
    public Dictionary<DateOnly, DayActivity> Days { get; set; } = new();

    /// <summary>Each day's totals: kept even where a day's folders are not (detail recorded before this history began).</summary>
    public Dictionary<DateOnly, DayTotal> Totals { get; set; } = new();
}

/// <summary>One file and the times it was opened in the month, in UTC, oldest first.</summary>
public sealed class HistoryFile
{
    public string Path { get; set; } = "";

    public List<DateTimeOffset> Opens { get; set; } = new();
}
