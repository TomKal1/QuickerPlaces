using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Runtime.Versioning;

namespace QuickerPlaces.Services.Documents;

/// <summary>
/// Reads the document entries in Windows' Recent Items: the
/// documented FOLDERID_Recent folder of shortcuts that Windows and programs
/// add to when a document is opened (SHAddToRecentDocs). Each shortcut's
/// target is its document, and its last-write time is when that document
/// was last opened. Explorer's undocumented AutomaticDestinations jump-list
/// storage is not read (roadmap §2).
///
/// Shared by the session scan (WindowsOpenDocumentProbe) and Recent Files
/// tracking (RecentFilesHost). Shortcut targets are resolved through
/// IShellLink without searching for moved targets, so no network access,
/// and are cached by shortcut name and write time, so a periodic read only
/// resolves the shortcuts that changed.
///
/// Best effort, as the File Activity note says: a program can skip Recent
/// Items, a policy can turn it off, and only each document's latest open is
/// kept there. App-only: it calls Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRecentItems
{
    private readonly object _sync = new();
    private readonly Dictionary<string, (DateTime WrittenUtc, string? Target)> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The documents opened since <paramref name="sinceUtc"/>, newest first,
    /// at most <paramref name="maxShortcuts"/> shortcuts of any kind read.
    /// Throws if the folder can't be listed.
    /// </summary>
    public IReadOnlyList<RecentDocument> Read(DateTimeOffset sinceUtc, int maxShortcuts)
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return Array.Empty<RecentDocument>();

        // Every shortcut, not just "*.pdf.lnk": the target decides, whatever the shortcut happens to be called.
        var since = sinceUtc.UtcDateTime;
        var shortcuts = new DirectoryInfo(folder)
            .EnumerateFiles("*.lnk")
            .Where(f => f.LastWriteTimeUtc >= since)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(maxShortcuts)
            .ToList();

        var documents = new List<RecentDocument>();
        lock (_sync)
        {
            foreach (var shortcut in shortcuts)
            {
                var written = shortcut.LastWriteTimeUtc;
                if (!_cache.TryGetValue(shortcut.Name, out var cached) || cached.WrittenUtc != written)
                    _cache[shortcut.Name] = cached = (written, ShortcutTarget(shortcut.FullName));

                if (cached.Target is { } target && DocumentKinds.FromPath(target) is not null)
                    documents.Add(new RecentDocument(target, new DateTimeOffset(written, TimeSpan.Zero)));
            }

            // Keep the cache to what is still there, so it can't grow without limit.
            if (_cache.Count > maxShortcuts * 2)
            {
                var keep = shortcuts.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var name in _cache.Keys.Where(k => !keep.Contains(k)).ToList())
                    _cache.Remove(name);
            }
        }

        return documents;
    }

    /// <summary>The path a shortcut points at, as stored: no search for a moved target, so no network access.</summary>
    private static string? ShortcutTarget(string shortcutPath)
    {
        object? link = null;
        try
        {
            link = new ShellLink();
            ((IPersistFile)link).Load(shortcutPath, 0);
            var target = new StringBuilder(1024);
            ((IShellLinkW)link).GetPath(target, target.Capacity, IntPtr.Zero, SlgpRawPath);
            var path = Environment.ExpandEnvironmentVariables(target.ToString());
            return path.Length == 0 ? null : path;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (link is not null)
                Marshal.FinalReleaseComObject(link);
        }
    }

    private const uint SlgpRawPath = 0x4;

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
