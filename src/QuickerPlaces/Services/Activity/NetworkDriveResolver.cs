using System;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickerPlaces.Services.Activity;

/// <summary>Offers an explicit mapped-drive equivalent at Add Root time (D22).</summary>
public sealed class NetworkDriveResolver : INetworkDriveResolver
{
    private const int NoError = 0;
    private const int MoreData = 234;

    public string? GetNetworkPath(string driveLetterPath)
    {
        var normalized = RootPathMatcher.Normalize(driveLetterPath);
        if (normalized is null || normalized.Length < 3 || normalized[1] != ':' ||
            !char.IsAsciiLetter(normalized[0]))
            return null;

        var length = 260;
        var remote = new StringBuilder(length);
        var result = WNetGetConnection(normalized[..2], remote, ref length);
        if (result == MoreData)
        {
            remote = new StringBuilder(length);
            result = WNetGetConnection(normalized[..2], remote, ref length);
        }
        if (result != NoError)
            return null;

        return RootPathMatcher.Normalize(remote.ToString() + normalized[2..]);
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);
}
