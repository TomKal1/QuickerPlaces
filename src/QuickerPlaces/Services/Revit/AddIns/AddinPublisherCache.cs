using System;
using System.Collections.Concurrent;
using System.IO;

namespace QuickerPlaces.Services.Revit.AddIns;

/// <summary>
/// Remembers which add-in DLLs are Autodesk-signed for the life of the app, so
/// the Revit settings dialog does not re-verify every signature each time it
/// opens (about 200 ms for 87 DLLs). A DLL is checked again when its size or
/// last write time changes, or when it cannot be stamped.
/// </summary>
public sealed class AddinPublisherCache
{
    private readonly Func<string, bool> _check;
    private readonly Func<string, (DateTime WrittenUtc, long Length)?> _stamp;
    private readonly ConcurrentDictionary<string, (DateTime WrittenUtc, long Length, bool IsAutodesk)> _known =
        new(StringComparer.OrdinalIgnoreCase);

    public AddinPublisherCache(Func<string, bool> check, Func<string, (DateTime WrittenUtc, long Length)?> stamp)
    {
        _check = check;
        _stamp = stamp;
    }

    /// <summary>The app-wide cache over <see cref="AutodeskAddinClassifier.IsAutodeskSigned"/>.</summary>
    public static AddinPublisherCache Shared { get; } = new(AutodeskAddinClassifier.IsAutodeskSigned, FileStampOf);

    public bool IsAutodeskSigned(string path)
    {
        if (_stamp(path) is not { } stamp)
            return _check(path);
        if (_known.TryGetValue(path, out var known) && known.WrittenUtc == stamp.WrittenUtc && known.Length == stamp.Length)
            return known.IsAutodesk;
        var result = _check(path);
        _known[path] = (stamp.WrittenUtc, stamp.Length, result);
        return result;
    }

    private static (DateTime, long)? FileStampOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.LastWriteTimeUtc, info.Length) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
