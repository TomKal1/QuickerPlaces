using System;
using System.IO;
using System.Security.Cryptography;

namespace QuickerPlaces.Services.Revit.AddIns;

/// <summary>
/// Identifies an add-in DLL by content. The SHA-256 is streamed (no whole-file
/// buffer) with a shared read, so a DLL that Revit has loaded can still be
/// hashed. Whether the DLL carries an Authenticode signature is deliberately
/// not checked: that needs WinTrust, and the Load Once rules rest on the hash.
/// </summary>
public static class DllFingerprint
{
    /// <summary>The file's SHA-256 as lower-case hex; null when it can't be read (missing, locked against reading, no access).</summary>
    public static string? Sha256(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public static bool SameHash(string? a, string? b)
        => a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
