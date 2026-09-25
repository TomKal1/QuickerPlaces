using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace QuickerPlaces.Services;

/// <summary>
/// A small, static, plain-text diagnostic log — no third-party logging
/// package. It lives under the user's <em>local</em> AppData folder, never
/// roaming: places.json roams by design (D4 in the Phase 1 plan), but a
/// machine's diagnostic log must not follow the user to another machine,
/// and it must not bloat a roaming profile with megabytes of history that
/// nobody syncs on purpose.
/// </summary>
/// <remarks>
/// <para>
/// Privacy rule: this log never records a place's alias or destination.
/// A save failure logs the record count and the store path, not the
/// records themselves — knowing "42 places failed to save to
/// C:\...\places.json" is enough to diagnose the problem without writing
/// anyone's data to disk a second time in a less-protected file. The one
/// deliberate exception is a quarantine path: that is a filename the user
/// needs in order to find the file recovery moved aside, so it is logged
/// in full.
/// </para>
/// <para>
/// Logging must never throw. Every public method swallows any failure to
/// write the log itself (a locked file, a missing drive, a permissions
/// error). This is the one remaining place in the app where a silent
/// catch is correct: the rest of Phase 1 exists specifically to replace
/// "swallow the exception and hope" with a diagnostic log, and a log that
/// can itself crash the app it is meant to be explaining defeats that
/// purpose.
/// </para>
/// </remarks>
public static class DiagnosticLog
{
    private const string LogFileName = "quickerplaces.log";
    private const string RolledLogFileName = "quickerplaces.1.log";

    /// <summary>
    /// Roll over once the live file would exceed this size. 256 KB holds
    /// many thousands of lines — far more than a user will ever read — while
    /// keeping the on-disk cost bounded even if something gets stuck in a
    /// failure loop and logs continuously while the app is left running.
    /// </summary>
    private const long MaxLogFileSizeBytes = 256 * 1024;

    private static readonly object SyncRoot = new();

    private static string? _directoryOverride;

    /// <summary>
    /// Full path to the live log file (the file most recent entries land
    /// in; see <see cref="RolledLogFilePath"/> for the previous generation
    /// once a rollover has happened).
    /// </summary>
    public static string LogFilePath => Path.Combine(GetDirectory(), LogFileName);

    /// <summary>
    /// Full path to the rolled-over log file, if one exists. Written once
    /// the live file would otherwise exceed <see cref="MaxLogFileSizeBytes"/>.
    /// </summary>
    public static string RolledLogFilePath => Path.Combine(GetDirectory(), RolledLogFileName);

    /// <summary>
    /// Redirects the log to <paramref name="directory"/> instead of the
    /// real local-AppData location. This exists for tests only — production
    /// code must never call it. Tests must point the log at a temp
    /// directory so they never read or write a real machine's AppData.
    /// </summary>
    public static void UseDirectoryForTests(string directory)
    {
        lock (SyncRoot)
        {
            _directoryOverride = directory;
        }
    }

    /// <summary>
    /// Undoes <see cref="UseDirectoryForTests"/>, returning subsequent
    /// writes to the real local-AppData location. Test-only, like its
    /// counterpart.
    /// </summary>
    public static void ResetDirectoryForTests()
    {
        lock (SyncRoot)
        {
            _directoryOverride = null;
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static string GetDirectory()
    {
        if (_directoryOverride is not null)
            return _directoryOverride;

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, AppInfo.Publisher, AppInfo.Name, "logs");
    }

    private static void Write(string level, string message, Exception? exception)
    {
        // Logging must never throw — see the class remarks. Any failure
        // here (disk full, folder unwritable, path too long, a concurrent
        // delete of the log folder) is swallowed rather than propagated.
        try
        {
            lock (SyncRoot)
            {
                var directory = GetDirectory();
                Directory.CreateDirectory(directory);

                var logFilePath = Path.Combine(directory, LogFileName);
                var entry = FormatEntry(level, message, exception);
                var entryBytes = Encoding.UTF8.GetByteCount(entry);

                RollIfNeeded(directory, logFilePath, entryBytes);

                File.AppendAllText(logFilePath, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // Intentional silent catch — the one place in this codebase
            // where that is correct. See the class remarks.
        }
    }

    private static void RollIfNeeded(string directory, string logFilePath, int incomingEntryBytes)
    {
        if (!File.Exists(logFilePath))
            return;

        var currentSize = new FileInfo(logFilePath).Length;
        if (currentSize + incomingEntryBytes <= MaxLogFileSizeBytes)
            return;

        // A single rollover: the live file becomes the ".1" file, replacing
        // whatever ".1" file was there before, and a fresh live file starts
        // empty. Two files, bounded total size, no dated files to clean up
        // by hand — an unattended failure loop cannot fill the disk.
        var rolledPath = Path.Combine(directory, RolledLogFileName);
        File.Delete(rolledPath);
        File.Move(logFilePath, rolledPath);
    }

    private static string FormatEntry(string level, string message, Exception? exception)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var builder = new StringBuilder();
        builder.Append(timestamp).Append("  ").Append(level).Append("  ").Append(message);

        if (exception is not null)
        {
            AppendIndented(builder, exception.GetType().FullName ?? exception.GetType().Name);
            AppendIndented(builder, exception.Message);

            if (!string.IsNullOrEmpty(exception.StackTrace))
            {
                foreach (var line in exception.StackTrace.Split('\n'))
                {
                    AppendIndented(builder, line.TrimEnd('\r'));
                }
            }
        }

        builder.Append(Environment.NewLine);
        return builder.ToString();
    }

    private static void AppendIndented(StringBuilder builder, string line)
    {
        builder.Append(Environment.NewLine).Append("    ").Append(line);
    }
}
