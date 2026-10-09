using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace QuickerPlaces.Services.Revit;

/// <summary>What identifies one version of a file: when it was last written, and its length.</summary>
public readonly record struct FileStamp(DateTime LastWriteUtc, long Length)
{
    /// <summary>The file's stamp, or null when there is no such file.</summary>
    public static FileStamp? Of(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? new FileStamp(file.LastWriteTimeUtc, file.Length) : null;
    }
}

/// <summary>
/// The Revit release of each file the app shows, read once per version of
/// the file and kept for the life of the app. <see cref="Peek"/> answers from
/// memory alone and is safe on the UI thread; <see cref="Refresh"/> touches
/// the disk and belongs on a background thread.
///
/// A network share can stall, so each file's check runs under a timeout. One
/// that times out is reported as <see cref="RevitFileProblem.TimedOut"/> and
/// left running: if it finishes later its answer is kept, and no second read
/// of that file starts while it is still going. A file checked recently is
/// not checked again until <see cref="Freshness"/> has passed, so a list
/// that refreshes every minute or on each keystroke costs nothing.
///
/// UI-free and linked into the test project.
/// </summary>
public sealed class RevitReleaseCache
{
    private sealed record Entry(FileStamp Stamp, RevitFileInfo Info, DateTimeOffset CheckedAt);

    private readonly Func<string, RevitFileInfo> _read;
    private readonly Func<string, FileStamp?> _stamp;
    private readonly TimeProvider _time;
    private readonly TimeSpan _timeout;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task>> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public RevitReleaseCache(Func<string, RevitFileInfo>? read = null, Func<string, FileStamp?>? stamp = null,
        TimeProvider? time = null, TimeSpan? timeout = null, TimeSpan? freshness = null)
    {
        _read = read ?? RevitFileInfoReader.Read;
        _stamp = stamp ?? FileStamp.Of;
        _time = time ?? TimeProvider.System;
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
        Freshness = freshness ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>How long an answer is trusted before the file's stamp is checked again.</summary>
    public TimeSpan Freshness { get; }

    /// <summary>The last answer for <paramref name="path"/>, from memory; null when it hasn't been read yet.</summary>
    public RevitFileInfo? Peek(string path) => _entries.TryGetValue(path, out var entry) ? entry.Info : null;

    /// <summary>
    /// Brings each of <paramref name="paths"/> up to date, reading only files
    /// that are new to the cache or have changed, and returns an answer for
    /// every path. Blocks for up to the timeout per stalled file; call it off
    /// the UI thread.
    /// </summary>
    public IReadOnlyDictionary<string, RevitFileInfo> Refresh(IEnumerable<string> paths)
    {
        var answers = new Dictionary<string, RevitFileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (answers.ContainsKey(path))
                continue;

            if (_entries.TryGetValue(path, out var known) && _time.GetUtcNow() - known.CheckedAt < Freshness)
            {
                answers[path] = known.Info;
                continue;
            }

            // Lazy, so two refreshes racing on one path still start a single check.
            var check = _inFlight.GetOrAdd(path, key => new Lazy<Task>(() => Task.Run(() => Check(key))));
            var mine = new KeyValuePair<string, Lazy<Task>>(path, check);
            if (check.Value.Wait(_timeout))
            {
                _inFlight.TryRemove(mine);
                answers[path] = _entries.TryGetValue(path, out var entry) ? entry.Info : RevitFileInfo.Failed(RevitFileProblem.Unreadable);
            }
            else
            {
                check.Value.ContinueWith(_ => _inFlight.TryRemove(mine), TaskScheduler.Default);
                answers[path] = RevitFileInfo.Failed(RevitFileProblem.TimedOut);
            }
        }
        return answers;
    }

    /// <summary>Stats the file and reads it when it is new to the cache or its stamp changed; never throws.</summary>
    private void Check(string path)
    {
        var now = _time.GetUtcNow();
        FileStamp? stamp;
        try
        {
            stamp = _stamp(path);
        }
        catch (Exception)
        {
            // A bad path, refused access or a failed share: all mean the release can't be read now.
            _entries[path] = new Entry(default, RevitFileInfo.Failed(RevitFileProblem.Unreadable), now);
            return;
        }

        if (stamp is not { } current)
        {
            _entries[path] = new Entry(default, RevitFileInfo.Failed(RevitFileProblem.NotFound), now);
            return;
        }

        if (_entries.TryGetValue(path, out var known) && known.Stamp == current && known.Info.Problem is not (RevitFileProblem.InUse or RevitFileProblem.Unreadable))
        {
            _entries[path] = known with { CheckedAt = now };
            return;
        }

        RevitFileInfo info;
        try
        {
            info = _read(path);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error($"Reading the Revit release of {path} failed.", ex);
            info = RevitFileInfo.Failed(RevitFileProblem.Unreadable);
        }
        _entries[path] = new Entry(current, info, now);
    }
}
