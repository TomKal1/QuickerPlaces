using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models;

namespace QuickerPlaces.Services.Sessions;

/// <summary>
/// Reopens a saved session's PDFs (sessions plan §5): each file is checked,
/// then handed to Windows to open with its default application, as a place
/// is. A missing file is skipped and reported, and one Windows refuses does
/// not stop the rest. The session's Last opened time is recorded when at
/// least one file opened.
///
/// Deliberately not PlaceLauncher: a session's files are not places, so
/// reopening one never counts as opening a place (Phase 3 D23 keeps
/// PlaceLauncher as the only writer of place usage).
///
/// UI-free and linked into the test project.
/// </summary>
public sealed class SessionLauncher
{
    private readonly SessionStore _store;
    private readonly IShell _shell;

    public SessionLauncher(SessionStore store, IShell shell)
    {
        _store = store;
        _shell = shell;
    }

    /// <summary>
    /// Opens <paramref name="files"/> from <paramref name="session"/>, or all
    /// of its files when null, in the session's order.
    /// </summary>
    public SessionOpenOutcome Open(SessionSnapshot session, IEnumerable<string>? files = null)
    {
        var chosen = files is null
            ? session.Files
            : session.Files.Where(f => files.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();

        var launched = new List<string>();
        var missing = new List<string>();
        var failed = new List<SessionFileFailure>();

        foreach (var file in chosen)
        {
            if (!_shell.FileExists(file))
            {
                missing.Add(file);
                continue;
            }

            try
            {
                _shell.Open(file);
                launched.Add(file);
            }
            catch (Exception ex)
            {
                // The type of failure only, never the path (Phase 3 D26).
                DiagnosticLog.Warn($"Opening a PDF from a session failed ({ex.GetType().Name}).");
                failed.Add(new SessionFileFailure(file, ex.Message));
            }
        }

        var persistence = launched.Count > 0 ? _store.MarkOpened(session.Id) : PersistenceResult.Ok();
        return new SessionOpenOutcome(launched, missing, failed, persistence);
    }
}

/// <summary>What <see cref="SessionLauncher.Open"/> did.</summary>
/// <param name="Launched">Files Windows accepted.</param>
/// <param name="Missing">Files that no longer exist where the session says, so were not opened.</param>
/// <param name="Failed">Files Windows refused, with its message.</param>
/// <param name="Persistence">Whether the Last opened time reached disk.</param>
public sealed record SessionOpenOutcome(
    IReadOnlyList<string> Launched,
    IReadOnlyList<string> Missing,
    IReadOnlyList<SessionFileFailure> Failed,
    PersistenceResult Persistence)
{
    /// <summary>
    /// One line for the Sessions window, or null when every file opened and
    /// was recorded. Names at most three files of each kind.
    /// </summary>
    public string? Summary
    {
        get
        {
            var parts = new List<string>();
            if (Missing.Count > 0)
                parts.Add($"{Count(Missing.Count)} couldn't be found and {(Missing.Count == 1 ? "wasn't" : "weren't")} opened: {Names(Missing)}.");
            if (Failed.Count > 0)
                parts.Add($"Windows couldn't open {Names(Failed.Select(f => f.Path).ToList())}: {Failed[0].Message}");
            if (!Persistence.Saved)
                parts.Add(Persistence.UserMessage ?? "");

            return parts.Count == 0 ? null : string.Join(" ", parts);
        }
    }

    private static string Count(int n) => n == 1 ? "1 PDF" : $"{n} PDFs";

    private static string Names(IReadOnlyList<string> paths)
    {
        var names = paths.Take(3).Select(SessionPaths.FileName);
        var more = paths.Count > 3 ? $" and {paths.Count - 3} more" : "";
        return string.Join(", ", names) + more;
    }
}

public sealed record SessionFileFailure(string Path, string Message);
