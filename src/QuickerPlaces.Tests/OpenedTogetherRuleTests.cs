using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions.Suggestions;
using QuickerPlaces.Tests.Fakes;
using Xunit;
using Xunit.Abstractions;

namespace QuickerPlaces.Tests;

public sealed class OpenedTogetherRuleTests
{
    private readonly ITestOutputHelper _output;

    public OpenedTogetherRuleTests(ITestOutputHelper output) => _output = output;

    private const string A = @"C:\Jobs\240015\A-101.pdf";
    private const string B = @"C:\Jobs\240015\B-202.pdf";

    private static readonly TimeZoneInfo Zone = TestZones.PlusTen;
    private static readonly SessionSuggestionOptions Balanced = SessionSuggestionOptions.Balanced;

    // 2026-09-25 12:00 in PlusTen.
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.FromHours(10));

    private static DateTimeOffset At(int day, int hour, int minute, int second = 0) =>
        new(2026, 9, day, hour, minute, second, TimeSpan.FromHours(10));

    private static RecentFileHistory File(string path, params DateTimeOffset[] opens) =>
        new(path, DocumentKind.Pdf, opens);

    private static IReadOnlyList<StrongPair> Find(IEnumerable<RecentFileHistory> history, SessionSuggestionOptions? options = null) =>
        OpenedTogetherRule.Find(history, options ?? Balanced, Zone, Now);

    /// <summary>A and B opened five minutes apart at the given hour on each given day.</summary>
    private static RecentFileHistory[] Together(string a, string b, int hour, params int[] days) =>
    [
        File(a, days.Select(d => At(d, hour, 0)).ToArray()),
        File(b, days.Select(d => At(d, hour, 5)).ToArray())
    ];

    [Fact]
    public void TwoFilesFiveMinutesApartOnThreeDays_AreOnePairWithTheLastDay()
    {
        var pairs = Find(Together(A, B, 9, 14, 18, 22));

        var pair = Assert.Single(pairs);
        Assert.Equal(A, pair.First);
        Assert.Equal(B, pair.Second);
        Assert.Equal(SuggestionRule.UsedTogether, pair.Rule);
        Assert.Equal(3, pair.DaysTogether);
        Assert.Equal(new DateOnly(2026, 9, 22), pair.LastDay);
        Assert.Equal(TimeSpan.Zero, pair.LongestOverlap);
        Assert.Null(pair.LongestOverlapOn);
    }

    [Fact]
    public void TwoDays_AreNoPairAtBalanced_ButOneAtHigh()
    {
        var history = Together(A, B, 9, 14, 18);

        Assert.Empty(Find(history));
        var pair = Assert.Single(Find(history, SessionSuggestionOptions.For(SuggestionSensitivity.High)));
        Assert.Equal(2, pair.DaysTogether);
    }

    [Fact]
    public void AGapOfExactlyTheBurstGap_StaysInTheBurst_AndOneSecondMoreSplitsIt()
    {
        var days = new[] { 14, 18, 22 };
        var joined = new[]
        {
            File(A, days.Select(d => At(d, 9, 0)).ToArray()),
            File(B, days.Select(d => At(d, 9, 15)).ToArray())
        };
        var split = new[]
        {
            File(A, days.Select(d => At(d, 9, 0)).ToArray()),
            File(B, days.Select(d => At(d, 9, 15, 1)).ToArray())
        };

        Assert.Single(Find(joined));
        Assert.Empty(Find(split));
    }

    [Fact]
    public void ABurstChainsThroughFilesEachWithinTheGapOfThePreviousOpen()
    {
        // A 9:00, B 9:14, C 9:28: A and C are 28 minutes apart but in one burst.
        const string c = @"C:\Jobs\240015\C-303.pdf";
        var days = new[] { 14, 18, 22 };
        var history = new[]
        {
            File(A, days.Select(d => At(d, 9, 0)).ToArray()),
            File(B, days.Select(d => At(d, 9, 14)).ToArray()),
            File(c, days.Select(d => At(d, 9, 28)).ToArray())
        };

        Assert.Equal(3, Find(history).Count);
    }

    [Fact]
    public void ADailyFile_ThatSharesFewOfItsDaysWithTheOther_MakesNoPair()
    {
        // Timesheet opened every day 1-10. A is active on 8 days (1-3 with the timesheet, 4-8 on its own).
        // Together on 3 days, min(10, 8) = 8, share 3/8 = 0.375 < 0.5.
        const string timesheet = @"C:\Jobs\Timesheet.xlsx";
        var history = new[]
        {
            File(timesheet, Enumerable.Range(14, 10).Select(d => At(d, 9, 0)).ToArray()),
            File(A, Enumerable.Range(14, 3).Select(d => At(d, 9, 5)).Concat(Enumerable.Range(17, 5).Select(d => At(d, 14, 0))).ToArray())
        };

        Assert.Empty(Find(history));
    }

    [Fact]
    public void AShareOfExactlyHalf_IsKept()
    {
        // As above, but A is active on 6 days: 3 together, min(10, 6) = 6, share exactly 0.5.
        const string timesheet = @"C:\Jobs\Timesheet.xlsx";
        var history = new[]
        {
            File(timesheet, Enumerable.Range(14, 10).Select(d => At(d, 9, 0)).ToArray()),
            File(A, Enumerable.Range(14, 3).Select(d => At(d, 9, 5)).Concat(Enumerable.Range(17, 3).Select(d => At(d, 14, 0))).ToArray())
        };

        var pair = Assert.Single(Find(history));
        Assert.Equal(3, pair.DaysTogether);
    }

    [Fact]
    public void ABurstOf41Files_IsIgnored_AndOneOf40Counts()
    {
        var days = new[] { 14, 18, 22 };

        RecentFileHistory[] Sweep(int count) => Enumerable.Range(0, count)
            .Select(i => File($@"C:\Jobs\Sweep\F{i:D2}.pdf", days.Select(d => At(d, 9, 0).AddSeconds(i)).ToArray()))
            .ToArray();

        Assert.Empty(Find(Sweep(41)));
        Assert.Equal(40 * 39 / 2, Find(Sweep(40)).Count);
    }

    [Fact]
    public void ADroppedBurstStillCountsTowardsActiveDays()
    {
        // A and B together on 3 days. A is also in a 41-file sweep on 3 more days, and B has 3 solo days:
        // both are active on 6 days, 3/6 = 0.5, kept. One more solo day each makes it 3/7 and drops it.
        var sweepDays = new[] { 15, 16, 17 };
        var sweep = Enumerable.Range(0, 40)
            .Select(i => File($@"C:\Jobs\Sweep\F{i:D2}.pdf", sweepDays.Select(d => At(d, 9, 0).AddSeconds(i)).ToArray()))
            .Append(File(A, sweepDays.Select(d => At(d, 9, 0).AddSeconds(50)).ToArray()));

        var together = Together(A, B, 9, 12, 13, 14);
        var bSolo = File(B, new[] { 18, 19, 20 }.Select(d => At(d, 14, 0)).ToArray());

        var kept = Find(together.Concat(sweep).Append(bSolo));
        Assert.Contains(kept, p => p.First == A && p.Second == B);

        var extra = new[]
        {
            File(A, At(21, 14, 0)),
            File(B, At(21, 16, 0))
        };
        var dropped = Find(together.Concat(sweep).Append(bSolo).Concat(extra));
        Assert.DoesNotContain(dropped, p => p.First == A && p.Second == B);
    }

    [Fact]
    public void ABurstThatCrossesMidnightLocally_CountsOnItsFirstLocalDay()
    {
        // MinusFive: 23:55 local on the 14th is 04:55 UTC on the 15th, so UTC would give the wrong day.
        var zone = TestZones.MinusFive;
        DateTimeOffset Local(int day, int hour, int minute) => new(2026, 9, day, hour, minute, 0, TimeSpan.FromHours(-5));

        var history = new[]
        {
            File(A, Local(10, 10, 0), Local(12, 10, 0), Local(14, 23, 55)),
            File(B, Local(10, 10, 5), Local(12, 10, 5), Local(14, 23, 58), Local(15, 0, 5))
        };

        var pair = Assert.Single(OpenedTogetherRule.Find(history, Balanced, zone, Local(20, 12, 0)));
        Assert.Equal(3, pair.DaysTogether);
        Assert.Equal(new DateOnly(2026, 9, 14), pair.LastDay);
    }


    [Fact]
    public void PathsDifferingOnlyInCaseOrSlashes_AreOneFile()
    {
        const string respelled = "c:/jobs/240015/a-101.PDF";
        var history = new[]
        {
            File(A, At(14, 9, 0), At(18, 9, 0)),
            File(respelled, At(22, 9, 0), At(14, 9, 2)),
            File(B, At(14, 9, 5), At(18, 9, 5), At(22, 9, 5))
        };

        var pair = Assert.Single(Find(history));
        Assert.Equal(3, pair.DaysTogether);
        Assert.Equal(A, pair.First);
        Assert.Equal(B, pair.Second);
    }

    [Fact]
    public void TheFirstRecordsSpelling_IsReported()
    {
        const string respelled = "c:/jobs/240015/a-101.PDF";
        var history = new[]
        {
            File(respelled, At(14, 9, 0), At(18, 9, 0), At(22, 9, 0)),
            File(A, At(20, 9, 0)),
            File(B, At(14, 9, 5), At(18, 9, 5), At(22, 9, 5))
        };

        var pair = Assert.Single(Find(history));
        Assert.True(DocumentPaths.Same(pair.First, A));
        Assert.Equal(respelled, pair.First);
    }

    [Fact]
    public void TheSpellingOfTheEarliestOpen_IsReported_WhateverTheInputOrder()
    {
        const string respelled = "c:/jobs/240015/a-101.PDF";
        const string upper = @"C:\JOBS\240015\A-101.PDF";
        // The first record in input order holds the latest opens; the respelled one has the earliest. A fourth
        // spelling's only open is before the window, so it must not win.
        var history = new[]
        {
            File(upper, At(22, 9, 0)),
            File(A, At(18, 9, 0)),
            File(respelled, At(14, 9, 0)),
            File(@"C:\jobs\240015\a-101.pdf", At(1, 9, 0)),
            File(B, At(14, 9, 5), At(18, 9, 5), At(22, 9, 5))
        };

        var pair = Assert.Single(Find(history));
        Assert.Equal(respelled, pair.First);

        var random = new Random(11);
        for (var i = 0; i < 10; i++)
            Assert.Equal(pair, Assert.Single(Find(history.OrderBy(_ => random.Next()).ToList())));
    }

    [Fact]
    public void OpensBeforeTheFirstLocalDay_OrAfterNow_AreIgnored()
    {
        // With 14 days and today the 25th local, the window starts at 00:00 local on the 12th.
        var firstDay = new DateOnly(2026, 9, 25).AddDays(-(Balanced.HistoryDays - 1));
        var start = new DateTimeOffset(firstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(10));

        // Just before it: the 11th local, so out, though 14 x 24 h before now would still be in.
        var before = new[]
        {
            File(A, start.AddMinutes(-2), At(18, 9, 0), At(20, 9, 0)),
            File(B, start.AddMinutes(-1), At(18, 9, 5), At(20, 9, 5))
        };
        Assert.Empty(Find(before));

        // Right at it, 00:00 local on the first day: in.
        var first = new[]
        {
            File(A, start, At(18, 9, 0), At(20, 9, 0)),
            File(B, start.AddMinutes(5), At(18, 9, 5), At(20, 9, 5))
        };
        var pair = Assert.Single(Find(first));
        Assert.Equal(3, pair.DaysTogether);

        // After now: out, even later on today's local date.
        var after = new[]
        {
            File(A, At(18, 9, 0), At(20, 9, 0), Now.AddMinutes(1)),
            File(B, At(18, 9, 5), At(20, 9, 5), Now.AddMinutes(6))
        };
        Assert.Empty(Find(after));

        // Exactly at now: in.
        var atNow = new[]
        {
            File(A, At(18, 9, 0), At(20, 9, 0), Now),
            File(B, At(18, 9, 5), At(20, 9, 5), Now.AddMinutes(-5))
        };
        Assert.Equal(new DateOnly(2026, 9, 25), Assert.Single(Find(atNow)).LastDay);
    }

    [Fact]
    public void DaysAreLocalAcrossTheAutumnChange_NotUtcDays()
    {
        // Central Europe, clocks back on 2026-10-25. 22:30Z on the 24th is 00:30 CEST on the 25th, and
        // 23:30Z on the 25th is 00:30 CET on the 26th: local days 25 and 26, UTC days 24 and 25.
        var zone = TestZones.CentralEuropean;
        var now = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        var options = Balanced with { MinDaysTogether = 2 };
        var history = new[]
        {
            File(A, Utc(2026, 10, 24, 22, 30), Utc(2026, 10, 25, 23, 30)),
            File(B, Utc(2026, 10, 24, 22, 35), Utc(2026, 10, 25, 23, 35))
        };

        var pair = Assert.Single(OpenedTogetherRule.Find(history, options, zone, now));
        Assert.Equal(2, pair.DaysTogether);
        Assert.Equal(new DateOnly(2026, 10, 26), pair.LastDay);

        // The ambiguous hour on the change day: 02:10 twice (00:10Z and 01:10Z) is two bursts on one date.
        var twice = new[]
        {
            File(A, Utc(2026, 10, 25, 0, 10), Utc(2026, 10, 25, 1, 10)),
            File(B, Utc(2026, 10, 25, 0, 15), Utc(2026, 10, 25, 1, 15))
        };
        var one = Assert.Single(OpenedTogetherRule.Find(twice, Balanced with { MinDaysTogether = 1 }, zone, now));
        Assert.Equal(1, one.DaysTogether);
        Assert.Equal(new DateOnly(2026, 10, 25), one.LastDay);
    }

    [Fact]
    public void DaysAreLocalAcrossTheSpringChange_NotUtcDays()
    {
        // Clocks forward on 2026-03-29. 23:30Z on the 28th is 00:30 CET on the 29th, and 22:30Z on the 29th
        // is 00:30 CEST on the 30th: local days 29 and 30, UTC days 28 and 29.
        var zone = TestZones.CentralEuropean;
        var now = new DateTimeOffset(2026, 3, 31, 12, 0, 0, TimeSpan.Zero);
        var options = Balanced with { MinDaysTogether = 2 };
        var history = new[]
        {
            File(A, Utc(2026, 3, 28, 23, 30), Utc(2026, 3, 29, 22, 30)),
            File(B, Utc(2026, 3, 28, 23, 35), Utc(2026, 3, 29, 22, 35))
        };

        var pair = Assert.Single(OpenedTogetherRule.Find(history, options, zone, now));
        Assert.Equal(2, pair.DaysTogether);
        Assert.Equal(new DateOnly(2026, 3, 30), pair.LastDay);
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);


    [Fact]
    public void ShuffledInput_GivesTheSameOutput()
    {
        const string c = @"C:\Jobs\240016\C-1.pdf";
        const string d = @"C:\Jobs\240016\D-1.pdf";
        var history = Together(A, B, 9, 14, 18, 22, 23)
            .Concat(Together(c, d, 14, 12, 15, 19))
            .Concat(Together(A, c, 16, 13, 16, 20))
            .ToList();
        var expected = Find(history);
        Assert.NotEmpty(expected);

        var random = new Random(7);
        for (var i = 0; i < 10; i++)
        {
            var shuffled = history
                .Select(h => h with { Opens = h.Opens.OrderBy(_ => random.Next()).ToArray() })
                .OrderBy(_ => random.Next())
                .ToList();
            Assert.Equal(expected, Find(shuffled));
        }
    }

    [Fact]
    public void Pairs_AreOrderedByDaysThenLastDayThenPaths()
    {
        var history = Together(@"C:\P\a1.pdf", @"C:\P\a2.pdf", 8, 12, 13, 14, 15)       // 4 days
            .Concat(Together(@"C:\P\b1.pdf", @"C:\P\b2.pdf", 10, 16, 17, 24))          // 3 days, last 24
            .Concat(Together(@"C:\P\d1.pdf", @"C:\P\d2.pdf", 12, 16, 17, 22))          // 3 days, last 22
            .Concat(Together(@"C:\P\c1.pdf", @"C:\P\c2.pdf", 14, 16, 17, 22))          // 3 days, last 22, sorts before d
            .ToArray();

        var pairs = Find(history);

        Assert.Equal(
            [@"C:\P\a1.pdf", @"C:\P\b1.pdf", @"C:\P\c1.pdf", @"C:\P\d1.pdf"],
            pairs.Select(p => p.First).ToArray());
        Assert.Equal([4, 3, 3, 3], pairs.Select(p => p.DaysTogether).ToArray());
    }

    [Fact]
    public void AHeavyHistory_GivesTheSamePairsAsAPlainReference()
    {
        // 25 projects of 40 files: each project's burst of 40 files (the largest that counts) happens on most
        // days of the window, at its own time of day, and some files are shared between projects.
        var firstDay = new DateOnly(2026, 9, 25).AddDays(-(Balanced.HistoryDays - 1));
        var opens = new Dictionary<string, List<DateTimeOffset>>();
        void Add(string path, DateTimeOffset when)
        {
            if (!opens.TryGetValue(path, out var list))
                opens[path] = list = new List<DateTimeOffset>();
            list.Add(when);
        }

        for (var project = 0; project < 25; project++)
        {
            for (var dayIndex = 0; dayIndex < Balanced.HistoryDays - 1; dayIndex++)
            {
                if ((project + dayIndex) % 4 == 0)
                    continue;

                var start = new DateTimeOffset(firstDay.AddDays(dayIndex).ToDateTime(new TimeOnly(0, 0)), TimeSpan.FromHours(10))
                    .AddMinutes(project * 30 + dayIndex % 3);
                for (var i = 0; i < 40; i++)
                {
                    var path = i == 39 && project % 2 == 0
                        ? $@"C:\Heavy\Shared\S{project / 5}.pdf"
                        : $@"C:\Heavy\P{project:D2}\F{i:D2}.pdf";
                    Add(path, start.AddSeconds(i));
                }
            }
        }

        var history = opens
            .Select(kv => File(kv.Key, kv.Value.ToArray()))
            .OrderBy(h => h.Path, StringComparer.Ordinal)
            .ToList();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var actual = Find(history);
        watch.Stop();
        _output.WriteLine($"Find on {history.Count} files: {watch.ElapsedMilliseconds} ms (information only)");

        var expected = Reference(history, Balanced, Zone, Now);
        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// The rule written the plain way (string-keyed sets, a scan of every pair), to compare the real one
    /// against on a heavy case.
    /// </summary>
    private static List<StrongPair> Reference(IEnumerable<RecentFileHistory> history, SessionSuggestionOptions options, TimeZoneInfo zone, DateTimeOffset now)
    {
        DateOnly Local(DateTimeOffset t) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t, zone).DateTime);
        var from = Local(now).AddDays(-(options.HistoryDays - 1));

        var all = history
            .SelectMany(h => h.Opens.Select(o => (When: o, Key: (DocumentPaths.Normalize(h.Path) ?? h.Path).ToUpperInvariant(), h.Path)))
            .Where(x => x.When <= now && Local(x.When) >= from)
            .OrderBy(x => x.When).ThenBy(x => x.Key, StringComparer.Ordinal).ThenBy(x => x.Path, StringComparer.Ordinal)
            .ToList();

        var spelling = new Dictionary<string, string>();
        var active = new Dictionary<string, HashSet<DateOnly>>();
        var together = new Dictionary<(string, string), HashSet<DateOnly>>();
        var bursts = new List<(HashSet<string> Files, DateOnly Day)>();
        DateTimeOffset? previous = null;
        foreach (var (when, key, path) in all)
        {
            spelling.TryAdd(key, path);
            if (!active.TryGetValue(key, out var set))
                active[key] = set = new HashSet<DateOnly>();
            set.Add(Local(when));

            if (previous is null || when - previous.Value > options.BurstGap)
                bursts.Add((new HashSet<string>(), Local(when)));
            bursts[^1].Files.Add(key);
            previous = when;
        }

        foreach (var (files, day) in bursts)
        {
            if (files.Count < options.MinGroupFiles || files.Count > options.MaxBurstFiles)
                continue;
            var sorted = files.OrderBy(f => f, StringComparer.Ordinal).ToList();
            for (var i = 0; i < sorted.Count; i++)
            {
                for (var j = i + 1; j < sorted.Count; j++)
                {
                    if (!together.TryGetValue((sorted[i], sorted[j]), out var days))
                        together[(sorted[i], sorted[j])] = days = new HashSet<DateOnly>();
                    days.Add(day);
                }
            }
        }

        return together
            .Where(kv => kv.Value.Count >= options.MinDaysTogether
                && kv.Value.Count / (double)Math.Min(active[kv.Key.Item1].Count, active[kv.Key.Item2].Count) >= options.MinShare)
            .Select(kv => StrongPair.Create(spelling[kv.Key.Item1], spelling[kv.Key.Item2], SuggestionRule.UsedTogether,
                kv.Value.Count, kv.Value.Max(), TimeSpan.Zero, null))
            .OrderByDescending(p => p.DaysTogether).ThenByDescending(p => p.LastDay)
            .ThenBy(p => DocumentPaths.Normalize(p.First) ?? p.First, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => DocumentPaths.Normalize(p.Second) ?? p.Second, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [Fact]
    public void NoHistory_GivesNoPairs()
    {
        Assert.Empty(Find([]));
        Assert.Empty(Find([File(A, At(14, 9, 0), At(18, 9, 0), At(22, 9, 0))]));
    }
}
