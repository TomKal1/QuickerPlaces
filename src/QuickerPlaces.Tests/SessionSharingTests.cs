using System;
using System.Collections.Generic;
using QuickerPlaces.Models.Sessions;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Session sharing plan §4, §5: how the sender's PC describes each file, and
/// how the recipient's finds it, never contacting a server named by the
/// file's sender until asked.
/// </summary>
public sealed class SessionSharingTests
{
    private static readonly CloudSyncRoot AliceLibrary = new(
        @"C:\Users\alice\Contoso\Tower B - Documents", "https://contoso.sharepoint.com/sites/TowerB/Shared%20Documents", CloudLibraryKind.Library);

    private static readonly CloudSyncRoot AliceOneDrive = new(
        @"C:\Users\alice\OneDrive - Contoso", "https://contoso-my.sharepoint.com/personal/alice_contoso_com/Documents", CloudLibraryKind.Personal);

    private static readonly CloudSyncRoot BobLibrary = AliceLibrary with { LocalPath = @"D:\Contoso\Tower B - Documents" };

    private static readonly IReadOnlyList<CloudSyncRoot> AliceRoots = new[] { AliceLibrary, AliceOneDrive };

    private sealed class FakeNetworkDrives : INetworkDriveResolver
    {
        public Dictionary<char, string> Drives { get; } = new();

        public bool Throws { get; init; }

        public string? GetNetworkPath(string driveLetterPath)
        {
            if (Throws)
                throw new InvalidOperationException("mpr.dll unavailable");
            return Drives.TryGetValue(char.ToUpperInvariant(driveLetterPath[0]), out var unc) ? unc + driveLetterPath[2..] : null;
        }
    }

    [Fact]
    public void Describe_AFileInASyncedLibrary_CarriesItsAddress()
    {
        var file = SessionSharing.Describe(@"C:\Users\alice\Contoso\Tower B - Documents\A-101.pdf", AliceRoots, null);

        Assert.Equal(SharedFileLocation.CloudLibrary, file.Location);
        Assert.Equal("https://contoso.sharepoint.com/sites/TowerB/Shared%20Documents/A-101.pdf", file.Url);
        Assert.Null(file.NetworkPath);
    }

    [Fact]
    public void Describe_AFileInAPersonalOneDrive_IsMarkedPersonal()
    {
        var file = SessionSharing.Describe(@"C:\Users\alice\OneDrive - Contoso\Notes.docx", AliceRoots, null);

        Assert.Equal(SharedFileLocation.PersonalCloud, file.Location);
        Assert.Equal("https://contoso-my.sharepoint.com/personal/alice_contoso_com/Documents/Notes.docx", file.Url);
    }

    [Fact]
    public void Describe_AShareOrMappedDrive_CarriesTheNetworkPath()
    {
        var drives = new FakeNetworkDrives { Drives = { ['Z'] = @"\\files\projects" } };

        var share = SessionSharing.Describe(@"\\files\projects\Spec.pdf", AliceRoots, drives);
        var mapped = SessionSharing.Describe(@"Z:\Tower B\Spec.pdf", AliceRoots, drives);

        Assert.Equal(SharedFileLocation.Network, share.Location);
        Assert.Equal(@"\\files\projects\Spec.pdf", share.NetworkPath);
        Assert.Equal(SharedFileLocation.Network, mapped.Location);
        Assert.Equal(@"\\files\projects\Tower B\Spec.pdf", mapped.NetworkPath);
    }

    [Fact]
    public void Describe_ALocalFile_IsThisPcOnly_EvenWhenTheDriveLookupFails()
    {
        var file = SessionSharing.Describe(@"C:\Users\alice\Desktop\A.pdf", AliceRoots, new FakeNetworkDrives { Throws = true });

        Assert.Equal(SharedFileLocation.ThisPc, file.Location);
        Assert.Null(file.Url);
        Assert.Null(file.NetworkPath);
    }

    [Fact]
    public void Resolve_PrefersTheSamePath()
    {
        var shell = new FakeShell { ExistingFiles = { @"C:\Jobs\A.pdf" } };

        var match = SessionSharing.Resolve(new SharedSessionFile { Path = @"C:\Jobs\A.pdf" }, shell, Array.Empty<CloudSyncRoot>(), checkNetwork: false);

        Assert.Equal(new SharedFileMatch(SharedFileStatus.SamePath, @"C:\Jobs\A.pdf"), match);
    }

    [Fact]
    public void Resolve_FindsTheFileInTheRecipientsSyncedLibrary()
    {
        var shell = new FakeShell { ExistingFiles = { @"D:\Contoso\Tower B - Documents\Drawings\A-101.pdf" } };
        var shared = SessionSharing.Describe(@"C:\Users\alice\Contoso\Tower B - Documents\Drawings\A-101.pdf", AliceRoots, null);

        var match = SessionSharing.Resolve(shared, shell, new[] { BobLibrary }, checkNetwork: false);

        Assert.Equal(SharedFileStatus.SyncedLibrary, match.Status);
        Assert.Equal(@"D:\Contoso\Tower B - Documents\Drawings\A-101.pdf", match.LocalPath);
    }

    [Fact]
    public void Resolve_ALibraryNotSyncedHere_IsOnlineOnly()
    {
        var shared = SessionSharing.Describe(@"C:\Users\alice\Contoso\Tower B - Documents\A-101.pdf", AliceRoots, null);

        var match = SessionSharing.Resolve(shared, new FakeShell(), Array.Empty<CloudSyncRoot>(), checkNetwork: false);

        Assert.Equal(new SharedFileMatch(SharedFileStatus.OnlineOnly, null), match);
    }

    [Fact]
    public void Resolve_ASyncedLibraryWithoutTheFile_SaysSo()
    {
        var shared = SessionSharing.Describe(@"C:\Users\alice\Contoso\Tower B - Documents\A-101.pdf", AliceRoots, null);

        var match = SessionSharing.Resolve(shared, new FakeShell(), new[] { BobLibrary }, checkNetwork: false);

        Assert.Equal(SharedFileStatus.NotInSyncedLibrary, match.Status);
    }

    /// <summary>A server named in someone else's file is never looked up unasked: a lookup can send it this PC's Windows sign-in.</summary>
    [Fact]
    public void Resolve_NeverTouchesANetworkPathUntilAsked()
    {
        var shell = new RecordingShell();
        var shared = new SharedSessionFile { Path = @"\\attacker\share\A.pdf", NetworkPath = @"\\attacker\share\A.pdf", Location = SharedFileLocation.Network };

        var match = SessionSharing.Resolve(shared, shell, Array.Empty<CloudSyncRoot>(), checkNetwork: false);

        Assert.Equal(SharedFileStatus.NetworkNotChecked, match.Status);
        Assert.Empty(shell.Checked);
    }

    [Fact]
    public void Resolve_LooksAtTheNetworkPathWhenAsked()
    {
        var shell = new FakeShell { ExistingFiles = { @"\\files\projects\Spec.pdf" } };
        var shared = new SharedSessionFile { Path = @"Z:\Spec.pdf", NetworkPath = @"\\files\projects\Spec.pdf", Location = SharedFileLocation.Network };

        Assert.Equal(new SharedFileMatch(SharedFileStatus.Network, @"\\files\projects\Spec.pdf"),
            SessionSharing.Resolve(shared, shell, Array.Empty<CloudSyncRoot>(), checkNetwork: true));
        Assert.Equal(new SharedFileMatch(SharedFileStatus.Missing, null),
            SessionSharing.Resolve(shared, new FakeShell(), Array.Empty<CloudSyncRoot>(), checkNetwork: true));
    }

    [Fact]
    public void Resolve_AFileOnlyOnTheSendersPc_IsMissing()
        => Assert.Equal(SharedFileStatus.Missing,
            SessionSharing.Resolve(new SharedSessionFile { Path = @"C:\Users\alice\Desktop\A.pdf" }, new FakeShell(), Array.Empty<CloudSyncRoot>(), false).Status);

    [Theory]
    [InlineData(@"C:\Users\alice\Jobs\Tower B\A.pdf", @"D:\Work\Jobs\Tower B\A.pdf", @"C:\Users\alice", @"D:\Work")]
    [InlineData(@"C:\Jobs\Tower B\A.pdf", @"D:\Jobs\Tower B\A.pdf", @"C:\", @"D:\")]
    [InlineData(@"C:\Jobs\Tower B\A.pdf", @"D:\Elsewhere\Renamed.pdf", @"C:\Jobs\Tower B", @"D:\Elsewhere")]
    [InlineData(@"\\old\share\Tower B\A.pdf", @"\\new\share\Tower B\A.pdf", @"\\old\share", @"\\new\share")]
    [InlineData(@"\\files\share\Tower B\A.pdf", @"S:\Tower B\A.pdf", @"\\files\share", @"S:\")]
    public void SwapBetween_KeepsTheFoldersBothPathsEndWith(string original, string chosen, string from, string to)
        => Assert.Equal(new FolderSwap(from, to), SessionSharing.SwapBetween(original, chosen));

    [Fact]
    public void SwapBetween_IsNullForTheSamePlace()
        => Assert.Null(SessionSharing.SwapBetween(@"C:\Jobs\A.pdf", @"c:\jobs\A.pdf"));

    [Theory]
    [InlineData(@"C:\Users\alice\Jobs\B.pdf", @"D:\Work\Jobs\B.pdf")]
    [InlineData(@"c:\users\ALICE\x.pdf", @"D:\Work\x.pdf")]
    [InlineData(@"C:\Users\alicex\B.pdf", null)]
    [InlineData(@"E:\Users\alice\B.pdf", null)]
    [InlineData("", null)]
    public void FolderSwap_MovesOnlyPathsUnderItsFolder(string path, string? expected)
        => Assert.Equal(expected, new FolderSwap(@"C:\Users\alice", @"D:\Work").Apply(path));

    [Fact]
    public void FolderSwap_FromADriveRoot()
        => Assert.Equal(@"D:\Jobs\B.pdf", new FolderSwap(@"C:\", @"D:\").Apply(@"C:\Jobs\B.pdf"));

    [Fact]
    public void ServerOf_ReadsTheServer()
    {
        Assert.Equal("files", SessionSharing.ServerOf(@"\\files\projects\A.pdf"));
        Assert.Equal("files", SessionSharing.ServerOf(@"\\files"));
    }

    private sealed class RecordingShell : QuickerPlaces.Services.IShell
    {
        public List<string> Checked { get; } = new();

        public bool DirectoryExists(string path)
        {
            Checked.Add(path);
            return false;
        }

        public bool FileExists(string path)
        {
            Checked.Add(path);
            return false;
        }

        public void Open(string target) => throw new InvalidOperationException("Nothing should be opened.");
    }
}
