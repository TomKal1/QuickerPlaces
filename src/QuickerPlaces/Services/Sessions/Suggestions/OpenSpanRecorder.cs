using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models.Sessions;
using QuickerPlaces.Services.Documents;

namespace QuickerPlaces.Services.Sessions.Suggestions;

/// <summary>
/// Turns the sampled list of open documents into open spans for the "open
/// together" rule (session suggestions plan §5). The sampler calls
/// <see cref="Observe"/> every <see cref="SessionSuggestionOptions.SampleInterval"/>;
/// a file seen in consecutive samples is one span, from the first sample to
/// the last.
///
/// UI-free and linked into the test project; no timer, no clock and no store
/// here, so the sampler decides when to read and save.
///
/// - G1: a span runs from the first sample a file was seen in to the last of
///   an unbroken run, so it is only as exact as the sample interval.
/// - G6: samples are consecutive when they are at most one and a half
///   intervals apart, which allows for timer jitter. A longer gap, or a clock
///   that went backwards, is a missed sample: every open span closes.
///   Spans loaded from the store are closed too, since a restart is a missed
///   sample.
/// - G17: spans older than <see cref="SessionSuggestionOptions.HistoryDays"/>
///   are dropped on every sample, which bounds the memory. A span of one
///   sample is kept; the always-open filter needs the days a file was seen.
///
/// Paths are compared as <see cref="DocumentPaths.Normalize"/> spells them,
/// ignoring case. They are never logged.
/// </summary>
public sealed class OpenSpanRecorder
{
    private readonly object _gate = new();
    private readonly SessionSuggestionOptions _options;
    private readonly List<OpenSpan> _closed;
    private readonly Dictionary<string, OpenSpan> _open = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _previous;
    private bool _hasChanges;

    public OpenSpanRecorder(IEnumerable<OpenSpan> existing, SessionSuggestionOptions options)
    {
        ArgumentNullException.ThrowIfNull(existing);
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _closed = existing.ToList();
    }

    /// <summary>True when a span started, extended, closed or was pruned since the last <see cref="MarkSaved"/>.</summary>
    public bool HasChanges
    {
        get { lock (_gate) return _hasChanges; }
    }

    /// <summary>The spans are now stored; <see cref="HasChanges"/> starts again from false.</summary>
    public void MarkSaved()
    {
        lock (_gate)
            _hasChanges = false;
    }

    /// <summary>One sample: the documents open at <paramref name="now"/>.</summary>
    public void Observe(DateTimeOffset now, IEnumerable<string> openPaths)
    {
        ArgumentNullException.ThrowIfNull(openPaths);

        // Each file once, spelled as the sample gives it; the key is the normalized form.
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in openPaths)
        {
            if (string.IsNullOrEmpty(path))
                continue;
            seen.TryAdd(DocumentPaths.Normalize(path) ?? path, path);
        }

        lock (_gate)
        {
            var gap = _previous is { } previous ? now - previous : TimeSpan.Zero;
            var consecutive = gap > TimeSpan.Zero && gap <= _options.SampleInterval + _options.SampleInterval / 2;

            if (!consecutive)
                CloseAll();

            foreach (var key in _open.Keys.ToList())
            {
                if (seen.ContainsKey(key))
                {
                    _open[key] = _open[key] with { To = now };
                }
                else
                {
                    _closed.Add(_open[key]);
                    _open.Remove(key);
                }
                _hasChanges = true;
            }

            foreach (var (key, path) in seen)
            {
                if (_open.ContainsKey(key))
                    continue;
                _open[key] = new OpenSpan(path, now, now);
                _hasChanges = true;
            }

            _previous = now;
            if (_closed.RemoveAll(s => IsOld(s, now)) > 0)
                _hasChanges = true;
        }
    }

    /// <summary>
    /// Closes every open span: tracking was paused, the user is away or the PC
    /// slept. The next <see cref="Observe"/> starts new spans.
    /// </summary>
    public void Interrupt()
    {
        lock (_gate)
        {
            CloseAll();
            _previous = null;
        }
    }

    /// <summary>
    /// Every span, closed and open, as a snapshot: oldest start first, then by
    /// path, so the output is the same for the same samples.
    /// </summary>
    public IReadOnlyList<OpenSpan> Spans(DateTimeOffset now)
    {
        lock (_gate)
        {
            return _closed.Concat(_open.Values)
                .Where(s => !IsOld(s, now))
                .OrderBy(s => s.From)
                .ThenBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Path, StringComparer.Ordinal)
                .ThenBy(s => s.To)
                .ToList();
        }
    }

    private void CloseAll()
    {
        if (_open.Count == 0)
            return;

        _closed.AddRange(_open.Values);
        _open.Clear();
        _hasChanges = true;
    }

    /// <summary>Strictly past the limit, as the store prunes: a span ending exactly HistoryDays ago stays.</summary>
    private bool IsOld(OpenSpan span, DateTimeOffset now) => span.To < now.AddDays(-_options.HistoryDays);
}
