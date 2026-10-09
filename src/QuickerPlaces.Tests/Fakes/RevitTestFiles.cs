using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenMcdf;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>
/// Builds stand-in Revit files: an OLE compound file holding a
/// <c>BasicFileInfo</c> stream laid out as Revit writes it (layout version,
/// workshared flag, worksharing type, then length-prefixed UTF-16 strings).
/// No real model is ever committed; real ones carry usernames and network
/// paths.
/// </summary>
public static class RevitTestFiles
{
    /// <summary>
    /// The start of a BasicFileInfo stream. <paramref name="strings"/> follow
    /// the two flag bytes in order (username, central path, then format and
    /// build for layout 12 and later, or the build alone before that, then the
    /// last save path); a null entry is written as length 0.
    /// </summary>
    public static byte[] BasicFileInfo(int layoutVersion, bool workshared, byte worksharingType, params string?[] strings)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, Encoding.Unicode);
        writer.Write(layoutVersion);
        writer.Write(workshared);
        writer.Write(worksharingType);
        foreach (var value in strings)
            WriteString(writer, value);

        // Revit follows these with more fields; some filler keeps the tests honest about stopping early.
        writer.Write(new byte[] { 0x03, 0x00, 0x00, 0x00, 0xFF, 0xFE, 0x0D, 0x00, 0x0A, 0x00 });
        writer.Flush();
        return bytes.ToArray();
    }

    /// <summary>A Revit 2025-style stream: layout 14, format year and build present.</summary>
    public static byte[] Modern(string format, bool workshared, byte worksharingType, string? centralPath, string? lastSavePath, string build = "20260410_1515(x64)") =>
        BasicFileInfo(14, workshared, worksharingType, "someone", centralPath, format, build, lastSavePath);

    public static void WriteString(BinaryWriter writer, string? value)
    {
        if (value is null)
        {
            writer.Write(0);
            return;
        }
        writer.Write(value.Length);
        writer.Write(Encoding.Unicode.GetBytes(value));
    }

    /// <summary>Writes a compound file at <paramref name="path"/> with the given streams at its root.</summary>
    public static string Write(string path, IReadOnlyDictionary<string, byte[]> streams)
    {
        using (var root = RootStorage.Create(path))
        {
            foreach (var (name, data) in streams)
            {
                using var stream = root.CreateStream(name);
                stream.Write(data, 0, data.Length);
            }
        }
        return path;
    }

    /// <summary>Writes a compound file whose BasicFileInfo stream is <paramref name="basicFileInfo"/>, beside a dummy Contents stream as a real model has.</summary>
    public static string Write(string path, byte[] basicFileInfo) =>
        Write(path, new Dictionary<string, byte[]>
        {
            ["BasicFileInfo"] = basicFileInfo,
            ["Contents"] = new byte[600]
        });
}
