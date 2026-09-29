using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace QuickerPlaces.Services;

/// <summary>
/// Where every store, settings.json and the log live. Normally the folders
/// this app has always used: %AppData%\QuickerPlaces\QuickerPlaces for what
/// roams (places, sessions) and %LocalAppData%\QuickerPlaces\QuickerPlaces
/// for what stays on this computer (settings, tracking, layouts, logs).
///
/// Started with <c>--data-root &lt;folder&gt;</c> (configurable canvas plan M0,
/// M3), both move under that folder instead, as "Roaming" and "Local", so the
/// workspace can be tried against test data without touching the real
/// stores. Set once at startup, before any store is built. UI-free and linked
/// into the test project.
/// </summary>
public static class AppDataFolders
{
    private static string? _root;

    /// <summary>The folder given by --data-root, made absolute, or null for the normal locations.</summary>
    public static string? Root => _root;

    /// <summary>Moves every store under <paramref name="root"/>; null or blank puts them back in the normal locations.</summary>
    public static void UseRoot(string? root) => _root = string.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(root);

    /// <summary>The machine-local folder: settings, activity, Recent Files, layouts and logs.</summary>
    public static string Local => LocalFor(_root);

    /// <summary>The roaming folder: places and sessions.</summary>
    public static string Roaming => RoamingFor(_root);

    public static string LocalFor(string? root) => root is null
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Publisher, AppInfo.Name)
        : Path.Combine(root, "Local");

    public static string RoamingFor(string? root) => root is null
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Publisher, AppInfo.Name)
        : Path.Combine(root, "Roaming");

    /// <summary>
    /// A short name for the single-instance gate, the same in every process
    /// for the same root (ignoring case and a trailing separator), or null for
    /// the normal locations. A copy running on test data therefore neither
    /// blocks nor is blocked by the everyday copy.
    /// </summary>
    public static string? InstanceScope(string? root)
    {
        if (root is null)
            return null;

        var normalized = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash, 0, 8);
    }
}
