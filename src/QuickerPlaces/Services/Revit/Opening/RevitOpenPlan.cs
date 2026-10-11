using System.Collections.Generic;

namespace QuickerPlaces.Services.Revit.Opening;

public enum RevitOpenKind
{
    /// <summary>Don't open: <see cref="RevitOpenPlan.RefusalReason"/> says why, in words for the user.</summary>
    Refuse,

    /// <summary>Launch the release's <c>Revit.exe</c> with the file (a local, or a file that isn't workshared).</summary>
    DirectOpen,

    /// <summary>Ask the chosen handler to open the central as a new local.</summary>
    HandlerRequest,
}

/// <summary>Whether the chosen handler can be expected to take a request, from what is running now.</summary>
public enum HandlerAvailability
{
    /// <summary>Not a handler plan.</summary>
    NotApplicable,

    /// <summary>A live instance exists: a Revit of the release has the handler loaded.</summary>
    Loaded,

    /// <summary>No instance yet, but a Revit of the release started less than two minutes ago and may still load it.</summary>
    Starting,

    /// <summary>No Revit of the release is running: launch one.</summary>
    NotRunning,

    /// <summary>A Revit of the release has been running for two minutes or more without the handler: it isn't loaded there.</summary>
    NotLoaded,
}

/// <summary>What to do for one file, and why not when refused. Produced by <see cref="RevitOpenPlanner"/>.</summary>
public sealed record RevitOpenPlan
{
    public RevitOpenKind Kind { get; init; }

    public string FilePath { get; init; } = "";

    /// <summary>The file's release as text ("2025"); null when it couldn't be read.</summary>
    public string? Release { get; init; }

    public string? RefusalReason { get; init; }

    /// <summary>The <c>Revit.exe</c> to launch (DirectOpen) or to start if needed (HandlerRequest).</summary>
    public string? ExePath { get; init; }

    public string? HandlerId { get; init; }
    public string? HandlerName { get; init; }

    /// <summary>Where the new local goes (HandlerRequest).</summary>
    public string? LocalFolder { get; init; }

    public string? Worksets { get; init; }

    public HandlerAvailability Availability { get; init; }

    /// <summary>Revit processes of the release running now (ids), for the report.</summary>
    public IReadOnlyList<int> RunningProcessIds { get; init; } = [];

    /// <summary>True when a HandlerRequest needs QuickerPlaces to launch Revit (none of the release is running).</summary>
    public bool NeedsLaunch => Kind == RevitOpenKind.HandlerRequest && Availability == HandlerAvailability.NotRunning;

    /// <summary>True when a Revit of the release is running without the handler (past the start-up grace period).</summary>
    public bool HandlerNotLoaded => Kind == RevitOpenKind.HandlerRequest && Availability == HandlerAvailability.NotLoaded;

    public static RevitOpenPlan Refused(string filePath, string? release, string reason) =>
        new() { Kind = RevitOpenKind.Refuse, FilePath = filePath, Release = release, RefusalReason = reason };
}
