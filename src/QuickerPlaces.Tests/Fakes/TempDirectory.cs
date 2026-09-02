using System;
using System.IO;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>
/// A uniquely named directory under the system temp path, recursively
/// deleted on Dispose. Used by the handful of tests that must exercise the
/// real FilePlacesStorage (backup file creation, quarantine naming,
/// temp-file cleanup) — no test in this project ever touches a real
/// AppData path.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "QuickerPlacesTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only — a locked handle left over from a
            // test that intentionally held a file open (e.g. the
            // FileShare.None tests) shouldn't fail the test run over a
            // leftover temp directory.
        }
    }
}
