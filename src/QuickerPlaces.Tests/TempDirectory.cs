using System;
using System.IO;

namespace QuickerPlaces.Tests;

/// <summary>A throwaway directory under the system temp folder, deleted on Dispose, so no test touches the user's real %AppData% files.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "QuickerPlaces.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Best effort — a leftover temp folder isn't worth failing a test over.
        }
    }
}
