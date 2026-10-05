using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.Services.Sessions;

/// <summary>What kind of OneDrive library a synced folder is, which decides who else can reach its files.</summary>
public enum CloudLibraryKind
{
    /// <summary>A SharePoint or Teams document library: anyone given access can sync it.</summary>
    Library,

    /// <summary>A OneDrive for work or school ("-my.sharepoint.com/personal/…"): its owner's files.</summary>
    Personal,

    /// <summary>A personal Microsoft account's OneDrive ("d.docs.live.net").</summary>
    ConsumerPersonal,
}

/// <summary>
/// One folder OneDrive keeps in sync with a library: where it is on this PC
/// and the library's web address. A shortcut added with "Add shortcut to My
/// files" is a root of its own, inside the OneDrive folder.
/// </summary>
public sealed record CloudSyncRoot(string LocalPath, string Url, CloudLibraryKind Kind);

/// <summary>Reads the folders OneDrive syncs on this PC. WindowsCloudSyncRoots reads the registry; tests pass a list.</summary>
public interface ICloudSyncRoots
{
    /// <summary>Every synced folder found, or none. Never throws.</summary>
    IReadOnlyList<CloudSyncRoot> Read();
}

/// <summary>
/// Turns a path in a synced OneDrive or SharePoint folder into the file's
/// web address and back (session sharing plan §4). The same library is
/// synced to a different folder on every PC ("C:\Users\alice\Contoso\Proj -
/// Documents" on one, "D:\Contoso\Proj - Documents" on another), but its web
/// address is the same everywhere, so the address is what a shared session
/// carries between PCs.
///
/// Addresses are compared in one spelling (<see cref="Canonical"/>): https
/// only, the host in lower case, each path segment escaped the same way, and
/// no trailing slash, query or fragment. Pure string work, without Path.*,
/// so the Windows paths in the tests behave the same on Linux.
///
/// UI-free and linked into the test project.
/// </summary>
public static class CloudPaths
{
    /// <summary>
    /// <paramref name="url"/> in the one spelling addresses are compared in,
    /// or null when it is not an https address. An address with a user name
    /// or password in it is refused, since a shared file should never carry
    /// one and it could disguise where a click goes.
    /// </summary>
    public static string? Canonical(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            string.IsNullOrEmpty(uri.Host))
            return null;

        var host = uri.Host.ToLowerInvariant();
        if (!uri.IsDefaultPort)
            host += ":" + uri.Port;

        var segments = Segments(uri.AbsolutePath);
        return segments.Count == 0
            ? "https://" + host
            : "https://" + host + "/" + string.Join("/", segments.Select(Uri.EscapeDataString));
    }

    /// <summary>True when <paramref name="url"/> is an address a shared session may carry and a click may open.</summary>
    public static bool IsWebUrl(string? url) => Canonical(url) is not null;

    /// <summary>The library kind an address looks like, for a synced folder whose kind the registry doesn't say.</summary>
    public static CloudLibraryKind KindFromUrl(string url)
    {
        var canonical = Canonical(url) ?? "";
        if (canonical.Contains("-my.sharepoint.com/personal/", StringComparison.OrdinalIgnoreCase))
            return CloudLibraryKind.Personal;
        if (canonical.StartsWith("https://d.docs.live.net/", StringComparison.OrdinalIgnoreCase) ||
            canonical.StartsWith("https://onedrive.live.com/", StringComparison.OrdinalIgnoreCase))
            return CloudLibraryKind.ConsumerPersonal;
        return CloudLibraryKind.Library;
    }

    /// <summary>
    /// The synced folder <paramref name="localPath"/> is in, the deepest one
    /// when they nest (a shortcut inside the OneDrive folder), or null.
    /// </summary>
    public static CloudSyncRoot? RootFor(string localPath, IEnumerable<CloudSyncRoot> roots)
    {
        var path = RootPathMatcher.Normalize(localPath);
        if (path is null)
            return null;

        CloudSyncRoot? best = null;
        var bestLength = -1;
        foreach (var root in roots)
        {
            var rootPath = RootPathMatcher.Normalize(root.LocalPath);
            if (rootPath is null || Canonical(root.Url) is null)
                continue;

            if (IsUnder(path, rootPath) && rootPath.Length > bestLength)
            {
                best = root;
                bestLength = rootPath.Length;
            }
        }

        return best;
    }

    /// <summary>
    /// The web address of the file at <paramref name="localPath"/>, when it is
    /// in a synced folder, and that folder; otherwise null for both.
    /// </summary>
    public static string? ToUrl(string localPath, IEnumerable<CloudSyncRoot> roots, out CloudSyncRoot? root)
    {
        root = RootFor(localPath, roots);
        if (root is null)
            return null;

        var path = RootPathMatcher.Normalize(localPath)!;
        var rootPath = RootPathMatcher.Normalize(root.LocalPath)!;
        var below = path.Length > rootPath.Length ? path[rootPath.Length..].TrimStart('\\') : "";
        var rootUrl = Canonical(root.Url)!;
        var segments = below.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? rootUrl : rootUrl + "/" + string.Join("/", segments.Select(Uri.EscapeDataString));
    }

    /// <summary>
    /// Where the file at <paramref name="url"/> would be on this PC, when one
    /// of <paramref name="roots"/> syncs its library (the deepest when they
    /// nest), or null. Whether the file is there is not checked. A segment
    /// that could step outside the synced folder ("..", or one hiding a
    /// backslash) refuses the whole address.
    /// </summary>
    public static string? ToLocal(string? url, IEnumerable<CloudSyncRoot> roots)
    {
        var canonical = Canonical(url);
        if (canonical is null)
            return null;

        CloudSyncRoot? best = null;
        string? bestUrl = null;
        foreach (var root in roots)
        {
            var rootUrl = Canonical(root.Url);
            if (rootUrl is null || RootPathMatcher.Normalize(root.LocalPath) is null)
                continue;

            var under = string.Equals(canonical, rootUrl, StringComparison.OrdinalIgnoreCase) ||
                        canonical.StartsWith(rootUrl + "/", StringComparison.OrdinalIgnoreCase);
            if (under && (bestUrl is null || rootUrl.Length > bestUrl.Length))
            {
                best = root;
                bestUrl = rootUrl;
            }
        }

        if (best is null)
            return null;

        var below = canonical.Length > bestUrl!.Length ? canonical[(bestUrl.Length + 1)..] : "";
        var segments = below.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList();
        if (segments.Any(s => s is "." or ".." || s.IndexOfAny(new[] { '\\', '/', ':' }) >= 0 || s.Trim().Length == 0))
            return null;

        var rootPath = RootPathMatcher.Normalize(best.LocalPath)!.TrimEnd('\\');
        return RootPathMatcher.Normalize(segments.Count == 0 ? rootPath : rootPath + "\\" + string.Join("\\", segments));
    }

    /// <summary>The address of the folder <paramref name="url"/> is in, or null for a site's top.</summary>
    public static string? Parent(string? url)
    {
        var canonical = Canonical(url);
        if (canonical is null)
            return null;

        var hostEnd = canonical.IndexOf('/', "https://".Length);
        var cut = canonical.LastIndexOf('/');
        return hostEnd < 0 || cut <= hostEnd ? null : canonical[..cut];
    }

    /// <summary>The last segment of <paramref name="url"/>, unescaped: "A-101.pdf".</summary>
    public static string FileName(string? url)
    {
        var canonical = Canonical(url);
        if (canonical is null)
            return "";

        var cut = canonical.LastIndexOf('/');
        return cut < "https://".Length ? "" : Uri.UnescapeDataString(canonical[(cut + 1)..]);
    }

    /// <summary>The address's host, for showing where a click goes: "contoso.sharepoint.com".</summary>
    public static string Host(string? url)
    {
        var canonical = Canonical(url);
        if (canonical is null)
            return "";

        var rest = canonical["https://".Length..];
        var slash = rest.IndexOf('/');
        return slash < 0 ? rest : rest[..slash];
    }

    private static bool IsUnder(string path, string rootPath)
    {
        if (string.Equals(path, rootPath, StringComparison.OrdinalIgnoreCase))
            return true;

        // A drive root keeps its separator ("C:\"); any other root needs one added.
        var prefix = rootPath.EndsWith('\\') ? rootPath : rootPath + "\\";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> Segments(string escapedPath)
        => escapedPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList();
}
