using System;
using System.Collections.Generic;
using QuickerPlaces.Services.Documents;

namespace QuickerPlaces.Services.Sessions.Suggestions;

/// <summary>Which rule found a pair of files (session suggestions plan §5).</summary>
public enum SuggestionRule
{
    /// <summary>Rule A: the files were touched in the same burst of work on several days.</summary>
    UsedTogether,

    /// <summary>Rule B: the files were open side by side for long enough.</summary>
    OpenTogether
}

/// <summary>
/// Two files a rule found related. Unordered: <see cref="Create"/> puts the
/// paths in one fixed order, so (a, b) and (b, a) give the same First/Second.
/// Pair identity is (First, Second) compared with DocumentPaths' rules, not
/// record equality, because the other fields take part in equality.
/// For rule A <see cref="LongestOverlap"/> is zero and
/// <see cref="LongestOverlapOn"/> null; for rule B <see cref="DaysTogether"/>
/// counts the days with a long enough overlap and <see cref="LastDay"/> stays
/// the most recent such day, which ranking uses.
/// </summary>
public sealed record StrongPair(
    string First,
    string Second,
    SuggestionRule Rule,
    int DaysTogether,
    DateOnly LastDay,
    TimeSpan LongestOverlap,
    DateOnly? LongestOverlapOn = null)
{
    public static StrongPair Create(
        string a,
        string b,
        SuggestionRule rule,
        int daysTogether,
        DateOnly lastDay,
        TimeSpan longestOverlap,
        DateOnly? longestOverlapOn = null)
    {
        if (DocumentPaths.Same(a, b))
            throw new ArgumentException("A pair needs two different files.", nameof(b));

        // Order by the normalised spelling so separators and ".." can't flip the order; keep the paths as given.
        var ordered = StringComparer.OrdinalIgnoreCase.Compare(DocumentPaths.Normalize(a) ?? a, DocumentPaths.Normalize(b) ?? b) <= 0;
        return ordered
            ? new StrongPair(a, b, rule, daysTogether, lastDay, longestOverlap, longestOverlapOn)
            : new StrongPair(b, a, rule, daysTogether, lastDay, longestOverlap, longestOverlapOn);
    }
}

/// <summary>
/// The evidence shown with a suggestion. The open-together fields are null
/// for a used-together suggestion, and LastTogether is null when only
/// rule B found the group.
/// </summary>
public sealed record SuggestionReason(
    int DaysTogether,
    DateOnly? LastTogether,
    TimeSpan? LongestOpenTogether,
    DateOnly? LongestOpenOn);

/// <summary>
/// One suggested set of files. <paramref name="Files"/> is the whole set;
/// <paramref name="NewFiles"/> the ones not already in the target session.
/// With a <paramref name="TargetSessionId"/> it offers to add to that session,
/// otherwise to start a new one named <paramref name="SuggestedName"/>.
/// <paramref name="Key"/> identifies the set for dismissals (G9). The lists
/// make record equality reference-based, so compare Key or the sequences.
/// </summary>
public sealed record SessionSuggestion(
    string Key,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> NewFiles,
    string? TargetSessionId,
    string? SuggestedName,
    SuggestionReason Reason)
{
    public bool IsAddToSession => TargetSessionId is not null;
}
