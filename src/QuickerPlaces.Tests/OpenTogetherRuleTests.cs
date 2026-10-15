using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using QuickerPlaces.Models.Sessions;
using QuickerPlaces.Services.Sessions.Suggestions;
using QuickerPlaces.Tests.Fakes;
using Xunit;
using Xunit.Abstractions;

namespace QuickerPlaces.Tests;

public sealed class OpenTogetherRuleTests
{
    private readonly ITestOutputHelper _output;

    public OpenTogetherRuleTests(ITestOutputHelper output) => _output = output;

    private const string A = @"C:\Jobs\240015\A-101.pdf";
    private const string B = @"C:\Jobs\240015\B-202.pdf";
    private const string C = @"C:\Jobs\240015\C-303.pdf";

    private static readonly TimeZoneInfo Zone = TestZones.PlusTen;
    private static readonly SessionSuggestionOptions Balanced = SessionSuggestionOptions.Balanced;
    private static readonly SessionSuggestionOptions Low = SessionSuggestionOptions.For(SuggestionSensitivity.Low);
    private static readonly SessionSuggestionOptions High = SessionSuggestionOptions.For(SuggestionSensitivity.High);

    // 2026-09-25 12:00 in PlusTen; the window starts on 2026-09-12 local.
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.FromHours(10));

    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new(2026, 9, day, hour, minute, 0, TimeSpan.FromHours(10));

    private static OpenSpan Span(string path, DateTimeOffset from, DateTimeOffset to) => new(path, from, to);

    private static OpenSpan Span(string path, int day, int fromHour, int fromMinute, int toHour, int toMinute) =>
        new(path, At(day, fromHour, fromMinute), At(day, toHour, toMinute));

    private static IReadOnlyList<StrongPair> Find(IEnumerable<OpenSpan> spans, SessionSuggestionOptions? options = null) =>
        OpenTogetherRule.Find(spans, options ?? Balanced, Zone, Now);

    /// <summary>A and B both open from the first time to the second on each given day.</summary>
    private static OpenSpan[] Both(string a, string b, int fromHour, int fromMinute, int toHour, int toMinute, params int[] days) =>
        days.SelectMany(d => new[]
        {
            Span(a, d, fromHour, fromMinute, toHour, toMinute),
            Span(b, d, fromHour, fromMinute, toHour, toMinute)
        }).ToArray();

    [Fact]
    public void AnHourTogether_IsOnePair_WithTheOverlapAndItsDay()
    {
        var pair = Assert.Single(Find(Both(A, B, 9, 0, 10, 0, 18)));

        Assert.Equal(A, pair.First);
        Assert.Equal(B, pair.Second);
        Assert.Equal(SuggestionRule.OpenTogether, pair.Rule);
        Assert.Equal(1, pair.DaysTogether);
        Assert.Equal(new DateOnly(2026, 9, 18), pair.LastDay);
        Assert.Equal(TimeSpan.FromMinutes(60), pair.LongestOverlap);
        Assert.Equal(new DateOnly(2026, 9, 18), pair.LongestOverlapOn);
    }

    [Fact]
    public void FiftyNineMinutesTogether_IsNoPairAtBalanced()
    {
        Assert.Empty(Find(Both(A, B, 9, 0, 9, 59, 18)));
    }

    [Fact]
    public void HalfAnHour_IsAPairAtHigh_AndNoneAtBalanced()
    {
        var spans = Both(A, B, 9, 0, 9, 30, 18);

        Assert.Empty(Find(spans));
        var pair = Assert.Single(Find(spans, High));
        Assert.Equal(TimeSpan.FromMinutes(30), pair.LongestOverlap);
    }

    [Fact]
    public void NinetyMinutesOnTwoDays_IsAPairAtLow_AndOneDayIsNone()
    {
        var pair = Assert.Single(Find(Both(A, B, 9, 0, 10, 30, 15, 18), Low));
        Assert.Equal(2, pair.DaysTogether);
        Assert.Equal(new DateOnly(2026, 9, 18), pair.LastDay);

        Assert.Empty(Find(Both(A, B, 9, 0, 10, 30, 18), Low));
    }

    [Fact]
    public void APartialOverlap_IsTheIntersection_NotFirstToLast()
    {
        // A is open 4 h and B 50 min inside it: together 50 min, whatever the outer spread.
        var spans = new[]
        {
            Span(A, 18, 8, 0, 12, 0),
            Span(B, 18, 9, 30, 10, 20)
        };

        Assert.Empty(Find(spans));
    }

    [Fact]
    public void TwoShortOverlapsOnOneDay_DoNotAddUp()
    {
        var spans = new[]
        {
            Span(A, 18, 8, 0, 12, 0),
            Span(B, 18, 8, 0, 8, 40),
            Span(B, 18, 9, 0, 9, 40)
        };

        Assert.Empty(Find(spans));
    }

    [Fact]
    public void AFileOpenOverSixHoursOnMostDays_IsAlwaysOpen_AndMakesNoPairs()
    {
        // Seven hours on 3 of 4 days seen; the fourth day is short, and B shares two hours with it.
        var spans = new[]
        {
            Span(A, 15, 8, 0, 15, 0),
            Span(A, 16, 8, 0, 15, 0),
            Span(A, 17, 8, 0, 15, 0),
            Span(A, 18, 8, 0, 9, 0),
            Span(B, 16, 9, 0, 11, 0)
        };

        Assert.Empty(Find(spans));
    }

    [Fact]
    public void AFileOverSixHoursOnExactlyHalfItsDays_IsNotAlwaysOpen()
    {
        var spans = new[]
        {
            Span(A, 15, 8, 0, 15, 0),
            Span(A, 16, 8, 0, 15, 0),
            Span(A, 17, 8, 0, 9, 0),
            Span(A, 18, 8, 0, 9, 0),
            Span(B, 16, 9, 0, 11, 0)
        };

        var pair = Assert.Single(Find(spans));
        Assert.Equal(TimeSpan.FromHours(2), pair.LongestOverlap);
    }

    [Fact]
    public void ExactlySixHours_IsNotOverTheLimit()
    {
        var spans = new[]
        {
            Span(A, 16, 8, 0, 14, 0),
            Span(B, 16, 9, 0, 11, 0)
        };

        Assert.Single(Find(spans));
    }

    [Fact]
    public void AnOpenTimeIsTheUnion_SoOverlappingSpansOfOneFileCountOnce()
    {
        // 4 h + 4 h overlapping by 2 h is 6 h of open time, not 8.
        var spans = new[]
        {
            Span(A, 16, 8, 0, 12, 0),
            Span(A, 16, 10, 0, 14, 0),
            Span(B, 16, 9, 0, 11, 0)
        };

        Assert.Single(Find(spans));
    }

    [Fact]
    public void AnOverlapAcrossMidnight_CountsOnItsStartDay()
    {
        var spans = new[]
        {
            Span(A, At(20, 22), At(21, 2)),
            Span(B, At(20, 23, 30), At(21, 0, 40))
        };

        var pair = Assert.Single(Find(spans));
        Assert.Equal(TimeSpan.FromMinutes(70), pair.LongestOverlap);
        Assert.Equal(new DateOnly(2026, 9, 20), pair.LongestOverlapOn);
        Assert.Equal(new DateOnly(2026, 9, 20), pair.LastDay);
        Assert.Equal(1, pair.DaysTogether);
    }

    [Fact]
    public void ASpanBeforeTheWindow_IsIgnored()
    {
        Assert.Empty(Find(Both(A, B, 9, 0, 11, 0, 11)));
    }

    [Fact]
    public void ASpanCrossingIntoTheFirstDay_IsClipped_SoTheEarlierOverlapDoesNotCount()
    {
        // Together 22:00 on the 11th to 00:30 on the 12th: only the last half hour is in the window.
        var spans = new[]
        {
            Span(A, At(11, 22), At(12, 0, 30)),
            Span(B, At(11, 22), At(12, 0, 30))
        };

        Assert.Empty(Find(spans));
        var pair = Assert.Single(Find(spans, High));
        Assert.Equal(TimeSpan.FromMinutes(30), pair.LongestOverlap);
        Assert.Equal(new DateOnly(2026, 9, 12), pair.LongestOverlapOn);
    }

    [Fact]
    public void ASpanAfterNow_IsIgnored_AndOneCrossingNowIsClipped()
    {
        Assert.Empty(Find(Both(A, B, 13, 0, 15, 0, 25)));

        var pair = Assert.Single(Find(Both(A, B, 11, 0, 14, 0, 25)));
        Assert.Equal(TimeSpan.FromMinutes(60), pair.LongestOverlap);
    }

    [Fact]
    public void SpellingVariantsOfOneFile_AreOneFile_WithNoSelfPair()
    {
        var variant = @"c:/jobs/240015/a-101.PDF";

        Assert.Empty(Find([Span(A, 18, 9, 0, 11, 0), Span(variant, 18, 9, 30, 11, 30)]));

        // The variants' spans merge to 09:00-11:30, spelled as the earliest span has it.
        var pair = Assert.Single(Find(
        [
            Span(variant, 18, 9, 30, 11, 30),
            Span(A, 18, 9, 0, 10, 0),
            Span(B, 18, 9, 0, 11, 30)
        ]));
        Assert.Equal(A, pair.First);
        Assert.Equal(B, pair.Second);
        Assert.Equal(TimeSpan.FromMinutes(150), pair.LongestOverlap);
    }

    [Fact]
    public void TouchingSpansOfOneFile_StayApart_SoNoOverlapIsLongerThanASpan()
    {
        // Two hours back to back against one two-hour span: 60 + 60, not 120, so no pair at Low's 90 min
        // (on one day here, so the test turns on the length alone).
        var spans = new[]
        {
            Span(A, 18, 9, 0, 10, 0),
            Span(A, 18, 10, 0, 11, 0),
            Span(B, 18, 9, 0, 11, 0)
        };

        Assert.Empty(Find(spans, Low with { TogetherOpenDays = 1 }));
    }

    [Fact]
    public void ASpanEndingAtMidnight_DoesNotSeeTheNextDay()
    {
        // A is long (7 h) on the 15th and 16th and short on the 18th: 2 of 3 seen days, so always open.
        // Were the midnight end to count the 17th as seen, it would be 2 of 4 and A would pair with B.
        var spans = new[]
        {
            Span(A, At(15, 17), At(16, 0)),
            Span(A, At(16, 17), At(17, 0)),
            Span(A, 18, 9, 0, 10, 0),
            Span(B, 15, 18, 0, 20, 0)
        };

        Assert.Empty(Find(spans));
    }

    [Fact]
    public void ASpanEndingAtTheWindowStart_IsIgnored_SoItDoesNotMarkTheFirstDaySeen()
    {
        // A ends exactly at the start of the 12th. If it marked that day seen, A would be long (7 h) on 1 of 2
        // seen days (not over half) and pair with B; ignored, its one seen day is the long one. B's 2 h stay
        // under Balanced's 6 h, so only A's count decides.
        var spans = new[]
        {
            Span(A, At(11, 17), At(12, 0)),
            Span(A, 13, 8, 0, 15, 0),
            Span(B, 13, 10, 0, 12, 0)
        };
        Assert.Empty(Find(spans));
    }

    [Fact]
    public void ATieForTheLongestOverlap_GoesToTheEarliestDay()
    {
        var pair = Assert.Single(Find(Both(A, B, 9, 0, 10, 30, 20, 16, 18)));

        Assert.Equal(TimeSpan.FromMinutes(90), pair.LongestOverlap);
        Assert.Equal(new DateOnly(2026, 9, 16), pair.LongestOverlapOn);
        Assert.Equal(new DateOnly(2026, 9, 20), pair.LastDay);
        Assert.Equal(3, pair.DaysTogether);
    }

    // Central European time: clocks go back at 03:00 local on 2026-10-25 (a 25 h day) and forward at 02:00
    // on 2026-03-29 (a 23 h day). TestZones has no zone whose change falls at midnight, so the branch that
    // starts a day at the first real minute after a skipped midnight is unproven here.
    private static DateTimeOffset Cet(int month, int day, int hour, int minute = 0, int offsetHours = 1) =>
        new(2026, month, day, hour, minute, 0, TimeSpan.FromHours(offsetHours));

    private static IReadOnlyList<StrongPair> FindCet(IEnumerable<OpenSpan> spans, DateTimeOffset now, SessionSuggestionOptions? options = null) =>
        OpenTogetherRule.Find(spans, options ?? Balanced, TestZones.CentralEuropean, now);

    [Fact]
    public void AnOverlapAcrossTheAutumnChange_IsRealTime_AndDatedByItsStartDay()
    {
        var now = Cet(10, 27, 12);
        // 23:30 CEST on the 24th to 01:00 CEST on the 25th, before the clocks go back: 90 real minutes.
        var spans = new[]
        {
            Span(A, Cet(10, 24, 23, 30, 2), Cet(10, 25, 1, 0, 2)),
            Span(B, Cet(10, 24, 23, 30, 2), Cet(10, 25, 1, 0, 2))
        };

        var pair = Assert.Single(FindCet(spans, now));
        Assert.Equal(TimeSpan.FromMinutes(90), pair.LongestOverlap);
        Assert.Equal(new DateOnly(2026, 10, 24), pair.LongestOverlapOn);
    }

    [Fact]
    public void ThePlusOneHourDay_IsOneDayForAlwaysOpen_AndAMidnightEndOnItDoesNotSeeTheNext()
    {
        var now = Cet(10, 27, 12);
        var spans = new[]
        {
            // The whole 25 h day of the 25th (midnight CEST to midnight CET), long.
            Span(A, Cet(10, 25, 0, 0, 2), Cet(10, 26, 0)),
            // A second long day, and a short one: 2 of 3 seen days. A 24 h reading of the long day would
            // run it into the 26th, make 4 seen days and let A pair with B.
            Span(A, Cet(10, 20, 8, 0, 2), Cet(10, 20, 15, 0, 2)),
            Span(A, Cet(10, 27, 9, 0), Cet(10, 27, 10, 0)),
            Span(B, Cet(10, 20, 10, 0, 2), Cet(10, 20, 12, 0, 2))
        };

        Assert.Empty(FindCet(spans, now));
    }

    [Fact]
    public void AnOverlapAcrossTheSpringChange_IsRealTime()
    {
        var now = Cet(3, 30, 12, 0, 2);
        // 01:30 CET to 03:30 CEST on the 29th: 02:00-03:00 does not exist, so 1 h of real time.
        var spans = new[]
        {
            Span(A, Cet(3, 29, 1, 30), Cet(3, 29, 3, 30, 2)),
            Span(B, Cet(3, 29, 1, 30), Cet(3, 29, 3, 30, 2))
        };

        var pair = Assert.Single(FindCet(spans, now));
        Assert.Equal(TimeSpan.FromHours(1), pair.LongestOverlap);
        Assert.Equal(new DateOnly(2026, 3, 29), pair.LongestOverlapOn);
    }

    [Fact]
    public void ASpanWithToBeforeFrom_IsIgnored()
    {
        Assert.Empty(Find([Span(A, At(18, 10), At(18, 9)), Span(B, 18, 9, 0, 10, 0)]));
    }

    [Fact]
    public void ShuffledInput_GivesTheSameOutput()
    {
        var spans = Both(A, B, 9, 0, 11, 0, 15, 18)
            .Concat(Both(C, A, 13, 0, 14, 30, 18))
            .Concat(Both(@"C:\Jobs\240015\D.pdf", B, 15, 0, 16, 0, 20))
            .ToList();
        var expected = Find(spans);
        Assert.True(expected.Count >= 3);

        var random = new Random(7);
        for (var i = 0; i < 20; i++)
        {
            var shuffled = spans.OrderBy(_ => random.Next()).ToList();
            Assert.Equal(expected, Find(shuffled));
        }
    }

    [Fact]
    public void Pairs_AreOrderedByDays_ThenOverlap_ThenLastDay_ThenPaths()
    {
        var spans = new List<OpenSpan>();
        spans.AddRange(Both(@"C:\x\F1.pdf", @"C:\x\F2.pdf", 9, 0, 9, 40, 13, 14));   // 2 days
        spans.AddRange(Both(@"C:\x\F3.pdf", @"C:\x\F4.pdf", 9, 0, 11, 0, 20));       // 120 min, day 20
        spans.AddRange(Both(@"C:\x\F5.pdf", @"C:\x\F6.pdf", 9, 0, 11, 0, 22));       // 120 min, day 22
        spans.AddRange(Both(@"C:\x\F7.pdf", @"C:\x\F8.pdf", 9, 0, 9, 50, 21));       // 50 min
        spans.AddRange(Both(@"C:\x\H2.pdf", @"C:\x\H1.pdf", 17, 0, 19, 0, 22));      // as F5/F6, sorts after by path
        spans.AddRange(Both(@"C:\x\E9.pdf", @"C:\x\E8.pdf", 14, 0, 16, 0, 22));      // as F5/F6, sorts before by path

        var forward = Find(spans, High);

        Assert.Equal(
            [@"C:\x\F1.pdf", @"C:\x\E8.pdf", @"C:\x\F5.pdf", @"C:\x\H1.pdf", @"C:\x\F3.pdf", @"C:\x\F7.pdf"],
            forward.Select(p => p.First));
        Assert.Equal(forward, Find(Enumerable.Reverse(spans), High));
    }

    [Fact]
    public void ARecorderRunOfThirteenSamples_IsAnHour_AndTwelveIsNot()
    {
        Assert.Single(FindFromSamples(13));
        Assert.Empty(FindFromSamples(12));
    }

    private static IReadOnlyList<StrongPair> FindFromSamples(int samples)
    {
        var recorder = new OpenSpanRecorder([], Balanced);
        for (var i = 0; i < samples; i++)
            recorder.Observe(At(24, 9).AddMinutes(5 * i), [A, B]);
        return Find(recorder.Spans(Now));
    }

    [Fact]
    public void ManySpans_FinishAndMatchAPlainReference()
    {
        var spans = BuildHeavy();
        Assert.Equal(2000, spans.Count);

        var watch = Stopwatch.StartNew();
        var found = Find(spans, Low);
        watch.Stop();
        _output.WriteLine($"{spans.Count} spans, {found.Count} pairs in {watch.ElapsedMilliseconds} ms");

        Assert.Equal(Reference(spans, Low), found);
        var high = Find(spans, High);
        Assert.NotEmpty(high);
        Assert.Equal(Reference(spans, High), high);
        Assert.Equal(high, Find(spans.OrderByDescending(s => s.Path).ToList(), High));
    }

    private static List<OpenSpan> BuildHeavy()
    {
        // 500 files, 4 spans each, one per 3-day slot so a file never overlaps itself and none is open long.
        var random = new Random(11);
        var spans = new List<OpenSpan>();
        for (var file = 0; file < 500; file++)
        {
            var path = $@"C:\Jobs\{file:000}\Drawing-{file:000}.pdf";
            for (var k = 0; k < 4; k++)
            {
                var day = 12 + k * 3 + random.Next(3);
                var start = At(day, 8 + random.Next(11), 5 * random.Next(12));
                spans.Add(new OpenSpan(path, start, start.AddMinutes(5 * (6 + random.Next(31)))));
            }
        }
        return spans;
    }

    /// <summary>Every span against every other, with none of the rule's sweep.</summary>
    private static IReadOnlyList<StrongPair> Reference(List<OpenSpan> spans, SessionSuggestionOptions options)
    {
        var longest = new Dictionary<(string, string), Dictionary<DateOnly, TimeSpan>>();
        for (var i = 0; i < spans.Count; i++)
        {
            for (var j = i + 1; j < spans.Count; j++)
            {
                var x = spans[i];
                var y = spans[j];
                if (string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase))
                    continue;
                var from = x.From > y.From ? x.From : y.From;
                var to = x.To < y.To ? x.To : y.To;
                if (to <= from)
                    continue;
                var key = string.Compare(x.Path, y.Path, StringComparison.OrdinalIgnoreCase) < 0 ? (x.Path, y.Path) : (y.Path, x.Path);
                if (!longest.TryGetValue(key, out var days))
                    longest[key] = days = new();
                var day = DateOnly.FromDateTime(from.DateTime);
                days[day] = days.TryGetValue(day, out var old) && old > to - from ? old : to - from;
            }
        }

        var result = new List<StrongPair>();
        foreach (var ((a, b), days) in longest)
        {
            var qualifying = days.Where(d => d.Value >= options.TogetherOpen).ToList();
            if (qualifying.Count < options.TogetherOpenDays)
                continue;
            var best = days.OrderByDescending(d => d.Value).ThenBy(d => d.Key).First();
            result.Add(StrongPair.Create(a, b, SuggestionRule.OpenTogether, qualifying.Count, qualifying.Max(d => d.Key), best.Value, best.Key));
        }

        return result
            .OrderByDescending(p => p.DaysTogether)
            .ThenByDescending(p => p.LongestOverlap)
            .ThenByDescending(p => p.LastDay)
            .ThenBy(p => p.First, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Second, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
