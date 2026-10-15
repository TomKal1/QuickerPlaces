using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.RecentFiles;

namespace QuickerPlaces.Services.Sessions.Suggestions;

/// <summary>
/// Rule A, "used together" (session suggestions plan §4): files opened in the
/// same burst of work on enough separate days. Pairs count days, not bursts,
/// so one busy morning is not a habit (G3), and a pair must be together on at
/// least <see cref="SessionSuggestionOptions.MinShare"/> of the rarer file's
/// active days, so a daily timesheet joins nothing (G4). Pure and UI-free,
/// linked into the test project; the output does not depend on input order.
/// </summary>
public static class OpenedTogetherRule
{
    /// <summary>
    /// Finds the pairs of files used together.
    /// </summary>
    /// <param name="history">Every recorded file with its opens, in any order. Records whose paths differ
    /// only in case or separators are one file; the spelling reported is that of its earliest in-window open.</param>
    /// <param name="options">The thresholds (§6.1) and <see cref="SessionSuggestionOptions.HistoryDays"/>.</param>
    /// <param name="zone">The zone that decides which day an open belongs to, so a burst at 23:55 local counts
    /// on its own day, whatever the UTC date.</param>
    /// <param name="now">The window ends here, inclusive. It starts on the local date
    /// <c>HistoryDays - 1</c> days before <paramref name="now"/>'s own, the same days
    /// <c>RecentFilesStore.DetailKeptFrom</c> keeps, so an open is in or out by its local date.</param>
    /// <returns>Strong pairs, most days together first, then most recent day, then by path; the same for any
    /// input order.</returns>
    public static IReadOnlyList<StrongPair> Find(
        IEnumerable<RecentFileHistory> history,
        SessionSuggestionOptions options,
        TimeZoneInfo zone,
        DateTimeOffset now)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        var firstDay = LocalDate(now, zone).AddDays(-(options.HistoryDays - 1));
        var firstDayNumber = firstDay.DayNumber;

        // Each file gets a small integer id once, so pairs and day sets below never hash or build strings.
        var ids = new Dictionary<string, int>(cmp);
        var keys = new List<string>();
        var opens = new List<Open>();
        foreach (var file in history)
        {
            var key = DocumentPaths.Normalize(file.Path) ?? file.Path;
            var id = -1;
            foreach (var when in file.Opens)
            {
                if (when > now)
                    continue;

                var day = LocalDate(when, zone).DayNumber - firstDayNumber;
                if (day < 0)
                    continue;

                if (id < 0 && !ids.TryGetValue(key, out id))
                {
                    id = keys.Count;
                    ids[key] = id;
                    keys.Add(key);
                }

                opens.Add(new Open(when, id, day, file.Path));
            }
        }

        // Time, then key and spelling, so ties break the same way for any input order (ids depend on it).
        opens.Sort((x, y) =>
        {
            var c = x.When.CompareTo(y.When);
            if (c != 0) return c;
            if (x.Id != y.Id)
                return cmp.Compare(keys[x.Id], keys[y.Id]);
            return string.CompareOrdinal(x.Path, y.Path);
        });

        // A day set is a bitset over the window's days, `words` ulongs wide (one for any window up to 64 days).
        var words = Math.Max(1, (options.HistoryDays + 63) / 64);
        var count = keys.Count;
        var spelling = new string?[count];
        var activeDays = new ulong[count * words];

        // Pairs are keyed by their two ids in one long; each pair's day bits live in one growing array.
        var pairSlots = new Dictionary<long, int>(PairKeyComparer.Instance);
        var pairBits = new ulong[Math.Max(16, words) * words];
        var pairCount = 0;

        var inBurst = new int[count];
        Array.Fill(inBurst, -1);
        var burst = new List<int>();
        var burstNumber = 0;
        var burstDay = 0;
        var previous = default(DateTimeOffset);

        void EndBurst()
        {
            if (burst.Count >= options.MinGroupFiles && burst.Count <= options.MaxBurstFiles)
            {
                burst.Sort();
                for (var i = 0; i < burst.Count; i++)
                {
                    for (var j = i + 1; j < burst.Count; j++)
                    {
                        var pairKey = ((long)burst[i] << 32) | (uint)burst[j];
                        if (!pairSlots.TryGetValue(pairKey, out var slot))
                        {
                            slot = pairCount++;
                            pairSlots[pairKey] = slot;
                            if ((slot + 1) * words > pairBits.Length)
                                Array.Resize(ref pairBits, pairBits.Length * 2);
                        }

                        pairBits[slot * words + (burstDay >> 6)] |= 1UL << (burstDay & 63);
                    }
                }
            }

            burst.Clear();
            burstNumber++;
        }

        foreach (var open in opens)
        {
            spelling[open.Id] ??= open.Path;
            activeDays[open.Id * words + (open.Day >> 6)] |= 1UL << (open.Day & 63);

            // Exactly the gap stays in the burst; only a longer one starts a new one.
            if (burst.Count > 0 && open.When - previous > options.BurstGap)
                EndBurst();
            if (burst.Count == 0)
                burstDay = open.Day;

            if (inBurst[open.Id] != burstNumber)
            {
                inBurst[open.Id] = burstNumber;
                burst.Add(open.Id);
            }

            previous = open.When;
        }

        EndBurst();

        var found = new List<(StrongPair Pair, string FirstKey, string SecondKey)>();
        foreach (var (pairKey, slot) in pairSlots)
        {
            var a = (int)(pairKey >> 32);
            var b = (int)(uint)pairKey;
            var together = PopCount(pairBits, slot * words, words);
            if (together < options.MinDaysTogether)
                continue;

            // Division, not multiplication, so a share of exactly MinShare compares exactly.
            var rarer = Math.Min(PopCount(activeDays, a * words, words), PopCount(activeDays, b * words, words));
            if (together / (double)rarer < options.MinShare)
                continue;

            var last = firstDay.AddDays(LastBit(pairBits, slot * words, words));
            var pair = StrongPair.Create(spelling[a]!, spelling[b]!, SuggestionRule.UsedTogether, together, last, TimeSpan.Zero, null);
            // Create put the two in its own order; the keys are the normalised spellings it ordered by.
            var aFirst = ReferenceEquals(pair.First, spelling[a]);
            found.Add((pair, aFirst ? keys[a] : keys[b], aFirst ? keys[b] : keys[a]));
        }

        // The path order is the one StrongPair.Create uses: the normalised spelling, ignoring case.
        return found
            .OrderByDescending(f => f.Pair.DaysTogether)
            .ThenByDescending(f => f.Pair.LastDay)
            .ThenBy(f => f.FirstKey, cmp)
            .ThenBy(f => f.SecondKey, cmp)
            .Select(f => f.Pair)
            .ToList();
    }

    private static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    private static int PopCount(ulong[] bits, int start, int words)
    {
        var total = 0;
        for (var i = 0; i < words; i++)
            total += BitOperations.PopCount(bits[start + i]);
        return total;
    }

    /// <summary>The index of the highest set bit; the set is never empty.</summary>
    private static int LastBit(ulong[] bits, int start, int words)
    {
        for (var i = words - 1; i >= 0; i--)
        {
            if (bits[start + i] != 0)
                return i * 64 + 63 - BitOperations.LeadingZeroCount(bits[start + i]);
        }

        return 0;
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

    private readonly record struct Open(DateTimeOffset When, int Id, int Day, string Path);
}
