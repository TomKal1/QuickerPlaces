using System;
using System.Collections.Generic;
using QuickerPlaces.Services;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>
/// Scriptable IShell for PlaceLauncher's tests (Phase 3 D23): which folders
/// "exist", whether Windows "refuses" a launch, and what was launched. No
/// test ever starts a real process.
/// </summary>
public sealed class FakeShell : IShell
{
    /// <summary>Paths DirectoryExists answers true for (case-insensitive, like Windows).</summary>
    public HashSet<string> ExistingDirectories { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Paths FileExists answers true for (case-insensitive, like Windows).</summary>
    public HashSet<string> ExistingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Targets Open throws for, as Windows refuses one file with no handler; ThrowOnOpen applies to every target.</summary>
    public HashSet<string> RefusedTargets { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When set, Open throws this, as Process.Start does when Windows refuses a launch.</summary>
    public Exception? ThrowOnOpen { get; set; }

    /// <summary>Every target Open was asked for and did not throw on, in order.</summary>
    public List<string> Opened { get; } = new();

    /// <summary>How many times DirectoryExists was asked.</summary>
    public int ExistenceChecks { get; private set; }

    public bool DirectoryExists(string path)
    {
        ExistenceChecks++;
        return ExistingDirectories.Contains(path);
    }

    public bool FileExists(string path) => ExistingFiles.Contains(path);

    public void Open(string target)
    {
        if (ThrowOnOpen is not null)
            throw ThrowOnOpen;
        if (RefusedTargets.Contains(target))
            throw new System.ComponentModel.Win32Exception("No application is associated with the specified file.");

        Opened.Add(target);
    }
}
