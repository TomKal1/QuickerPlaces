using System.Diagnostics;

namespace QuickerPlaces.Services;

/// <summary>
/// Reveals a file in Explorer with it pre-selected. Pulled out as its own
/// static helper because two callers need exactly this: the startup
/// recovery flow's "Show me the file" option (App.xaml.cs) and
/// MainViewModel's "Show Data Folder" banner command (plan 5.2). A small
/// static method on a Services type, rather than a public static on App,
/// keeps App.xaml.cs's surface limited to startup/shutdown orchestration
/// and lets MainViewModel depend on Services (which it already does)
/// instead of reaching into the Application subclass.
/// </summary>
public static class ExplorerReveal
{
    /// <summary>
    /// Windows gotcha, and the reason this uses the legacy Arguments
    /// string rather than the tidier ArgumentList: Explorer's switch is
    /// literally "/select,&lt;path&gt;" — the path must be attached to the
    /// comma with no space between them. ArgumentList joins its entries
    /// with spaces, so passing "/select," and the path as two entries
    /// produces "/select, C:\...\places.json", which Explorer does not
    /// recognize as a selection request; it silently opens the default
    /// Documents view instead, leaving the user staring at the wrong
    /// folder. One pre-quoted argument string is the only shape that
    /// works here.
    /// </summary>
    public static void Reveal(string path)
    {
        try
        {
            // /select on a file that doesn't exist yet (before the first
            // save) opens an unrelated default folder, so open the
            // containing folder itself instead when the file is missing.
            var folder = System.IO.Path.GetDirectoryName(path);
            var startInfo = !System.IO.File.Exists(path) && System.IO.Directory.Exists(folder)
                ? new ProcessStartInfo(folder) { UseShellExecute = true }
                : new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"");

            Process.Start(startInfo);
        }
        catch
        {
            // Failing to open Explorer must never crash or block whatever
            // flow asked for it. The recovery dialog and the banner show
            // the path in text, so the user can still navigate there by
            // hand; the header's folder button is a convenience whose
            // failure costs nothing but the click.
        }
    }
}
