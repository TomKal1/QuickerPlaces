using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models.History;

namespace QuickerPlaces.Services.History;

/// <summary>
/// The month files one view has open (history plan §5): Recents or the
/// Library asks for the months its year strip and chosen period need, and
/// gets them, read once. Every other month is let go, so choosing a period
/// in 2019 and then clearing it leaves nothing of 2019 in memory.
///
/// Only months a working store doesn't fully hold are read: a month this PC
/// recorded is needed only before the cutoff the caller gives, and a month
/// another PC recorded is always needed. Thread-safe: the Library loads off
/// the UI thread. UI-free and linked into the test project.
/// </summary>
public sealed class HistoryMonthCache
{
    private readonly object _sync = new();
    private readonly IHistoryReader _reader;
    private readonly Dictionary<(int Year, int Month), HistoryMonthDocument?> _loaded = new();
    private HistoryCutoffs? _cutoffs;

    public HistoryMonthCache(IHistoryReader reader) => _reader = reader;

    /// <summary>How many months are held now: what was asked for last, and nothing else.</summary>
    public int LoadedCount
    {
        get { lock (_sync) return _loaded.Count; }
    }

    /// <summary>The months in the history folder, read from the file names each call: cheap, and new months appear daily.</summary>
    public IReadOnlyList<HistoryMonthInfo> Index() => _reader.MonthIndex();

    /// <summary>The first day any PC has history for, or null when there is none.</summary>
    public DateOnly? EarliestDay() => Index().FirstOrDefault()?.First;

    /// <summary>
    /// The months touching <paramref name="from"/> to <paramref name="to"/>
    /// that need reading: another PC's, or this PC's starting before
    /// <paramref name="ownNeededBefore"/>.
    /// </summary>
    public IReadOnlyList<(int Year, int Month)> Needed(DateOnly from, DateOnly to, DateOnly ownNeededBefore)
        => Needed(Index(), from, to, ownNeededBefore);

    public static IReadOnlyList<(int Year, int Month)> Needed(IReadOnlyList<HistoryMonthInfo> index, DateOnly from, DateOnly to, DateOnly ownNeededBefore)
        => index.Where(m => m.Last >= from && m.First <= to && (m.HasOthers || (m.HasOwn && m.First < ownNeededBefore)))
            .Select(m => (m.Year, m.Month))
            .ToList();

    /// <summary>
    /// The months asked for, read where they aren't held already, with this
    /// PC's days on and after <paramref name="cutoffs"/> left out. Months not
    /// asked for are let go. New cutoffs (a new day) read everything again.
    /// </summary>
    public IReadOnlyList<HistoryMonthDocument> Load(IEnumerable<(int Year, int Month)> months, HistoryCutoffs cutoffs)
    {
        var wanted = months.Distinct().ToList();
        lock (_sync)
        {
            if (_cutoffs != cutoffs)
            {
                _loaded.Clear();
                _cutoffs = cutoffs;
            }

            foreach (var month in _loaded.Keys.Except(wanted).ToList())
                _loaded.Remove(month);

            foreach (var month in wanted.Where(m => !_loaded.ContainsKey(m)))
                _loaded[month] = _reader.ReadMonth(month.Year, month.Month, cutoffs);

            return wanted.Select(m => _loaded[m]).OfType<HistoryMonthDocument>().ToList();
        }
    }

    /// <summary>Lets every month go, so the next Load reads them again: after a reload, when another PC may have written.</summary>
    public void Clear()
    {
        lock (_sync)
            _loaded.Clear();
    }

    /// <summary>Every month touching <paramref name="from"/> to <paramref name="to"/>, in order.</summary>
    public static IEnumerable<(int Year, int Month)> MonthsBetween(DateOnly from, DateOnly to)
    {
        for (var month = new DateOnly(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
            yield return (month.Year, month.Month);
    }
}
