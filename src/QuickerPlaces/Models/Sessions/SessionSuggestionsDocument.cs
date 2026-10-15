using System;
using System.Collections.Generic;

namespace QuickerPlaces.Models.Sessions;

/// <summary>
/// The root of session-suggestions.json (session suggestions plan §8): the
/// suggestion switches and levels, the suggestions the user dismissed, and
/// the open spans the "open together" rule reads. Kept in local application
/// data, not roaming, because the spans describe what happened on this
/// machine (G14). A file of its own, so settings.json and sessions.json
/// keep their schemas.
/// </summary>
public sealed class SessionSuggestionsDocument
{
    /// <summary>SessionSuggestionStore sets this from its CurrentSchemaVersion on every write and checks it on every load.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Suggestions are off until the user turns them on (G2).</summary>
    public bool Enabled { get; set; }

    /// <summary>The "open together" rule's own switch. Only meaningful while <see cref="Enabled"/>.</summary>
    public bool OpenTogether { get; set; } = true;

    /// <summary>"low", "balanced" or "high"; anything else reads as balanced.</summary>
    public string? Sensitivity { get; set; } = "balanced";

    /// <summary>The "used together" rule's own level, or null to follow <see cref="Sensitivity"/>.</summary>
    public string? UsedTogetherSensitivity { get; set; }

    /// <summary>The "open together" rule's own level, or null to follow <see cref="Sensitivity"/>.</summary>
    public string? OpenTogetherSensitivity { get; set; }

    public List<DismissedSuggestion> Dismissed { get; set; } = new();

    public List<OpenSpan> OpenSpans { get; set; } = new();
}

/// <summary>A suggestion the user dismissed: its key, how many files it held, and when.</summary>
public sealed class DismissedSuggestion
{
    public string Key { get; set; } = "";

    public int Files { get; set; }

    public DateTimeOffset DismissedAt { get; set; }

    /// <summary>Individual file hashes for hiding subsets without keeping paths (G9).</summary>
    public List<string>? FileKeys { get; set; }
}

/// <summary>One stretch during which a file stayed open, for the "open together" rule.</summary>
public sealed record OpenSpan(string Path, DateTimeOffset From, DateTimeOffset To);
