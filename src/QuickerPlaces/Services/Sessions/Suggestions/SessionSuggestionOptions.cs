using System;

namespace QuickerPlaces.Services.Sessions.Suggestions;

/// <summary>
/// Every number the two suggestion rules use (session suggestions plan §6.1).
/// The sensitivity levels set the first three groups; the last group is the
/// same at every level (G15) but stays settable so tests can shrink it.
/// UI-free and linked into the test project.
/// </summary>
public sealed record SessionSuggestionOptions
{
    // Rule A, used together: files touched within one burst of work.

    /// <summary>The longest quiet gap that still belongs to the same burst.</summary>
    public TimeSpan BurstGap { get; init; }

    /// <summary>How many different days a pair must share a burst on.</summary>
    public int MinDaysTogether { get; init; }

    /// <summary>The share of either file's burst days the pair must be together on.</summary>
    public double MinShare { get; init; }

    // Rule B, open together: files kept open side by side.

    /// <summary>How long two files must be open at the same time to count for a day.</summary>
    public TimeSpan TogetherOpen { get; init; }

    /// <summary>How many days such an overlap must happen on.</summary>
    public int TogetherOpenDays { get; init; }

    /// <summary>A file open longer than this in one day is a background window, not a pairing signal.</summary>
    public TimeSpan AlwaysOpenAbove { get; init; }

    // Shared by both rules.

    /// <summary>The fraction of a group's members a file must be a strong pair with to join the group.</summary>
    public double GroupJoinFraction { get; init; }

    /// <summary>The most suggestions shown at once.</summary>
    public int MaxSuggestions { get; init; }

    // Fixed at every level (G15).

    /// <summary>How often the open documents were sampled.</summary>
    public TimeSpan SampleInterval { get; init; } = TimeSpan.FromMinutes(5);

    public int MinGroupFiles { get; init; } = 2;

    public int MaxGroupFiles { get; init; } = 15;

    /// <summary>A burst touching more files than this is a sweep, not a working set.</summary>
    public int MaxBurstFiles { get; init; } = 40;

    /// <summary>
    /// How many days of history the rules look at, and how long open spans are
    /// kept: two weeks (Thomas, 2026-10-15). Recent Files keeps more, but a
    /// working set older than that is no longer the one worth suggesting.
    /// </summary>
    public int HistoryDays { get; init; } = 14;

    public static SessionSuggestionOptions Balanced { get; } = For(SuggestionSensitivity.Balanced);

    /// <summary>
    /// The options for <paramref name="overall"/>; <paramref name="usedTogether"/>
    /// and <paramref name="openTogether"/> override just that rule's values.
    /// The shared rows always follow <paramref name="overall"/>.
    /// </summary>
    public static SessionSuggestionOptions For(
        SuggestionSensitivity overall,
        SuggestionSensitivity? usedTogether = null,
        SuggestionSensitivity? openTogether = null)
    {
        var shared = Row(overall);
        var a = Row(usedTogether ?? overall);
        var b = Row(openTogether ?? overall);

        return new SessionSuggestionOptions
        {
            BurstGap = a.BurstGap,
            MinDaysTogether = a.MinDaysTogether,
            MinShare = a.MinShare,
            TogetherOpen = b.TogetherOpen,
            TogetherOpenDays = b.TogetherOpenDays,
            AlwaysOpenAbove = b.AlwaysOpenAbove,
            GroupJoinFraction = shared.GroupJoinFraction,
            MaxSuggestions = shared.MaxSuggestions
        };
    }

    private static SessionSuggestionOptions Row(SuggestionSensitivity level) => level switch
    {
        SuggestionSensitivity.Low => new()
        {
            BurstGap = TimeSpan.FromMinutes(10),
            MinDaysTogether = 4,
            MinShare = 0.6,
            TogetherOpen = TimeSpan.FromMinutes(90),
            TogetherOpenDays = 2,
            AlwaysOpenAbove = TimeSpan.FromHours(5),
            GroupJoinFraction = 1.0,
            MaxSuggestions = 5
        },
        SuggestionSensitivity.High => new()
        {
            BurstGap = TimeSpan.FromMinutes(30),
            MinDaysTogether = 2,
            MinShare = 0.35,
            TogetherOpen = TimeSpan.FromMinutes(30),
            TogetherOpenDays = 1,
            AlwaysOpenAbove = TimeSpan.FromHours(8),
            GroupJoinFraction = 0.5,
            MaxSuggestions = 15
        },
        _ => new()
        {
            BurstGap = TimeSpan.FromMinutes(15),
            MinDaysTogether = 3,
            MinShare = 0.5,
            TogetherOpen = TimeSpan.FromMinutes(60),
            TogetherOpenDays = 1,
            AlwaysOpenAbove = TimeSpan.FromHours(6),
            GroupJoinFraction = 2.0 / 3,
            MaxSuggestions = 10
        }
    };
}
