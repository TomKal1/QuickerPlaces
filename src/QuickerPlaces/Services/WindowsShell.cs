using System.Diagnostics;
using System.IO;

namespace QuickerPlaces.Services;

/// <summary>The production <see cref="IShell"/>: the real file system and Windows' own shell execute.</summary>
public sealed class WindowsShell : IShell
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    // UseShellExecute lets Windows pick the right handler either way:
    // Explorer for a folder path, the default browser for a URL — no need
    // to branch on the place's type here. The Process it returns is null
    // for both, and is not needed: Windows accepting the request is all a
    // launcher can know (D24).
    public void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
