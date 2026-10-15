using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models.Sessions;
using QuickerPlaces.Services.Documents;

namespace QuickerPlaces.Services.Sessions.Suggestions;

/// <summary>
/// Rule B, "open together" (session suggestions plan §5): two files seen open
/// at the same time, without a break, for at least
/// <see cref="SessionSuggestionOptions.TogetherOpen"/> on enough separate days.
/// A file left open nearly every day joins nothing (G4). Pure and UI-free,
/// linked into the test project; the output does not depend on input order.
/// </summary>
public static class OpenTogetherRule
{
    /// <summary>
    /// Finds the pairs of files kept open together (G1, rule B), from the spans
    /// <see cref="OpenSpanRecorder"/> keeps.
    /// </summary>
    /// <remarks>
    /// A pair's overlap is the intersection of one span of each file, from the later start to the earlier
    /// end, so two short overlaps on a day never add up: the longest single one counts. It belongs to the
    /// local day it starts on, and a day qualifies for the pair when its longest overlap is at least
    /// <see cref="SessionSuggestionOptions.TogetherOpen"/>. A file is "always open" (G4) when its open time
    /// is over <see cref="SessionSuggestionOptions.AlwaysOpenAbove"/> on more than half of the days it was
    /// seen at all, and it makes no pairs. Spans are only as exact as the sample interval (§5): at five
    /// minutes an hour is 13 consecutive samples with both files open, so 12 are 55 minutes and do not
    /// make a pair at Balanced.
    /// </remarks>
    /// <param name="spans">Every recorded span, in any order. Paths that differ only in case or separators
    /// are one file, so its spans never pair with each other; the spelling reported is that of its earliest
    /// span. A span that ends before it starts is ignored.</param>
    /// <param name="options">The thresholds (§6.1) and <see cref="SessionSuggestionOptions.HistoryDays"/> (G17).</param>
    /// <param name="zone">The zone that decides which day an overlap belongs to, so one starting at 23:30
    /// local counts on its own day, whatever the UTC date.</param>
    /// <param name="now">The window ends here, inclusive. It starts at the beginning of the local date
    /// <c>HistoryDays - 1</c> days before <paramref name="now"/>'s own, the same days
    /// <see cref="OpenedTogetherRule"/> reads. A span is clipped to the window, so time before it counts
    /// for neither a pair nor the always-open test.</param>
    /// <returns>Strong pairs, most qualifying days first, then longest overlap, then most recent day, then by
    /// path; the same for any input order. <see cref="StrongPair.LongestOverlapOn"/> is always set, on the
    /// earliest day when two days tie.</returns>
    public static IReadOnlyList<StrongPair> Find(
        IEnumerable<OpenSpan> spans,
        SessionSuggestionOptions options,
        TimeZoneInfo zone,
        DateTimeOffset now)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        var firstDay = LocalDate(now, zone).AddDays(-(options.HistoryDays - 1));
        var firstDayNumber = firstDay.DayNumber;

        // Local midnights, one past the last day so every day has an end.
        var dayStarts = new DateTimeOffset[options.HistoryDays + 1];
        for (var i = 0; i < dayStarts.Length; i++)
            dayStarts[i] = StartOfDay(firstDay.AddDays(i), zone);
        var windowStart = dayStarts[0];

        // Each file gets a small integer id once, so pairs below never hash or build strings.
        var ids = new Dictionary<string, int>(cmp);
        var keys = new List<string>();
        var spelling = new List<string>();
        var earliest = new List<DateTimeOffset>();
        var perFile = new List<List<Interval>>();
        foreach (var span in spans)
        {
            if (span.To < span.From || span.To <= windowStart || span.From > now)
                continue;

            var key = DocumentPaths.Normalize(span.Path) ?? span.Path;
            if (!ids.TryGetValue(key, out var id))
            {
                id = keys.Count;
                ids[key] = id;
                keys.Add(key);
                spelling.Add(span.Path);
                earliest.Add(span.From);
                perFile.Add(new List<Interval>());
            }
            else if (span.From < earliest[id] || (span.From == earliest[id] && string.CompareOrdinal(span.Path, spelling[id]) < 0))
            {
                // The earliest span names the file; the path breaks a tie so input order never decides.
                spelling[id] = span.Path;
                earliest[id] = span.From;
            }

            var from = span.From > windowStart ? span.From : windowStart;
            var to = span.To < now ? span.To : now;
            perFile[id].Add(new Interval(from, to, id));
        }

        // Merge each file's own overlapping spans (spelling variants, or a sample taken twice), then
        // judge always-open on what is left; the rest go to the sweep.
        var all = new List<Interval>();
        var seen = new bool[dayStarts.Length];
        var openTime = new TimeSpan[dayStarts.Length];
        for (var id = 0; id < perFile.Count; id++)
        {
            var merged = Merge(perFile[id]);
            Array.Clear(seen);
            Array.Clear(openTime);
            foreach (var interval in merged)
            {
                var first = LocalDate(interval.From, zone).DayNumber - firstDayNumber;
                var last = LocalDate(interval.To, zone).DayNumber - firstDayNumber;
                // A span ending exactly at midnight has not seen the new day.
                if (last > first && interval.To == dayStarts[last])
                    last--;

                for (var d = first; d <= last; d++)
                {
                    seen[d] = true;
                    var start = interval.From > dayStarts[d] ? interval.From : dayStarts[d];
                    var end = interval.To < dayStarts[d + 1] ? interval.To : dayStarts[d + 1];
                    openTime[d] += end - start;
                }
            }

            var seenDays = 0;
            var longDays = 0;
            for (var d = 0; d < seen.Length; d++)
            {
                if (!seen[d])
                    continue;
                seenDays++;
                if (openTime[d] > options.AlwaysOpenAbove)
                    longDays++;
            }

            if (longDays * 2 > seenDays)
                continue;

            all.AddRange(merged);
        }

        // Sweep by start. An active span ends after the one being added starts, so the overlap runs from the
        // new start to the earlier end; spans that ended can never overlap a later start either.
        all.Sort((x, y) =>
        {
            var c = x.From.CompareTo(y.From);
            if (c != 0) return c;
            c = x.To.CompareTo(y.To);
            return c != 0 ? c : x.Id.CompareTo(y.Id);
        });

        // Per pair, the longest overlap starting on each day of the window.
        var pairs = new Dictionary<long, TimeSpan[]>(PairKeyComparer.Instance);
        var active = new List<Interval>();
        foreach (var next in all)
        {
            var kept = 0;
            for (var i = 0; i < active.Count; i++)
            {
                if (active[i].To > next.From)
                    active[kept++] = active[i];
            }
            active.RemoveRange(kept, active.Count - kept);

            if (next.To > next.From)
            {
                var day = LocalDate(next.From, zone).DayNumber - firstDayNumber;
                foreach (var other in active)
                {
                    if (other.Id == next.Id)
                        continue;

                    var overlap = (other.To < next.To ? other.To : next.To) - next.From;
                    if (overlap <= TimeSpan.Zero)
                        continue;

                    var low = Math.Min(other.Id, next.Id);
                    var high = Math.Max(other.Id, next.Id);
                    var pairKey = ((long)low << 32) | (uint)high;
                    if (!pairs.TryGetValue(pairKey, out var longest))
                        pairs[pairKey] = longest = new TimeSpan[dayStarts.Length];
                    if (overlap > longest[day])
                        longest[day] = overlap;
                }
            }

            active.Add(next);
        }

        var found = new List<(StrongPair Pair, string FirstKey, string SecondKey)>();
        foreach (var (pairKey, longest) in pairs)
        {
            var qualifying = 0;
            var lastDay = -1;
            var bestDay = -1;
            for (var d = 0; d < longest.Length; d++)
            {
                if (longest[d] <= TimeSpan.Zero)
                    continue;
                if (bestDay < 0 || longest[d] > longest[bestDay])
                    bestDay = d;
                if (longest[d] >= options.TogetherOpen)
                {
                    qualifying++;
                    lastDay = d;
                }
            }

            if (qualifying == 0 || qualifying < options.TogetherOpenDays)
                continue;

            var a = (int)(pairKey >> 32);
            var b = (int)(uint)pairKey;
            var pair = StrongPair.Create(
                spelling[a], spelling[b], SuggestionRule.OpenTogether, qualifying,
                firstDay.AddDays(lastDay), longest[bestDay], firstDay.AddDays(bestDay));
            // Create put the two in its own order; the keys are the normalised spellings it ordered by.
            var aFirst = ReferenceEquals(pair.First, spelling[a]);
            found.Add((pair, aFirst ? keys[a] : keys[b], aFirst ? keys[b] : keys[a]));
        }

        return found
            .OrderByDescending(f => f.Pair.DaysTogether)
            .ThenByDescending(f => f.Pair.LongestOverlap)
            .ThenByDescending(f => f.Pair.LastDay)
            .ThenBy(f => f.FirstKey, cmp)
            .ThenBy(f => f.SecondKey, cmp)
            .Select(f => f.Pair)
            .ToList();
    }

    /// <summary>Spans that overlap (not merely touch) become one; the list comes back ordered by start.</summary>
    private static List<Interval> Merge(List<Interval> intervals)
    {
        intervals.Sort((x, y) =>
        {
            var c = x.From.CompareTo(y.From);
            return c != 0 ? c : x.To.CompareTo(y.To);
        });

        var merged = new List<Interval>(intervals.Count);
        foreach (var interval in intervals)
        {
            if (merged.Count > 0 && interval.From < merged[^1].To)
            {
                if (interval.To > merged[^1].To)
                    merged[^1] = merged[^1] with { To = interval.To };
            }
            else
            {
                merged.Add(interval);
            }
        }

        return merged;
    }

    private static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>
    /// Local midnight of <paramref name="date"/>. Where a zone skips midnight the day starts at the first
    /// real minute after it; where midnight happens twice it starts at the first.
    /// </summary>
    private static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(1);

        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    /// <summary>
    /// The default long hash is the two halves xor-ed, so (1, 2) and (3, 0) collide and small ids pile into
    /// a few buckets; multiplying spreads them.
    /// </summary>
    private sealed class PairKeyComparer : IEqualityComparer<long>
    {
        public static readonly PairKeyComparer Instance = new();

        public bool Equals(long x, long y) => x == y;

        public int GetHashCode(long key) => (int)(((ulong)key * 0x9E3779B97F4A7C15UL) >> 32);
    }

    private readonly record struct Interval(DateTimeOffset From, DateTimeOffset To, int Id);
}
