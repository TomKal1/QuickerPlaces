using QuickerPlaces.Services.Sessions;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Session sharing plan §4: turning a path in a synced OneDrive or
/// SharePoint folder into its web address and back, which is how a shared
/// session finds a file in someone else's synced copy of the same library.
/// </summary>
public sealed class CloudPathsTests
{
    private static readonly CloudSyncRoot AliceLibrary = new(
        @"C:\Users\alice\Contoso\Tower B - Documents",
        "https://contoso.sharepoint.com/sites/TowerB/Shared Documents/",
        CloudLibraryKind.Library);

    private static readonly CloudSyncRoot AliceOneDrive = new(
        @"C:\Users\alice\OneDrive - Contoso",
        "https://contoso-my.sharepoint.com/personal/alice_contoso_com/Documents",
        CloudLibraryKind.Personal);

    /// <summary>A shortcut to a team folder, added inside Alice's OneDrive folder.</summary>
    private static readonly CloudSyncRoot AliceShortcut = new(
        @"C:\Users\alice\OneDrive - Contoso\Tower B Drawings",
        "https://contoso.sharepoint.com/sites/TowerB/Shared%20Documents/Drawings",
        CloudLibraryKind.Library);

    private static readonly CloudSyncRoot BobLibrary = new(
        @"D:\Sync\Contoso\Tower B - Documents",
        "https://CONTOSO.sharepoint.com/sites/TowerB/Shared%20Documents",
        CloudLibraryKind.Library);

    [Theory]
    [InlineData("https://contoso.sharepoint.com/sites/A/Shared Documents/", "https://contoso.sharepoint.com/sites/A/Shared%20Documents")]
    [InlineData("https://Contoso.SharePoint.com/sites/A/Shared%20Documents/a%23b.pdf?web=1#x", "https://contoso.sharepoint.com/sites/A/Shared%20Documents/a%23b.pdf")]
    [InlineData("https://contoso.sharepoint.com", "https://contoso.sharepoint.com")]
    [InlineData("https://host:8443/a", "https://host:8443/a")]
    public void Canonical_SpellsAddressesOneWay(string url, string expected)
        => Assert.Equal(expected, CloudPaths.Canonical(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://contoso.sharepoint.com/a.pdf")]
    [InlineData("file:///C:/a.pdf")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pass@contoso.sharepoint.com/a.pdf")]
    [InlineData(@"C:\Jobs\a.pdf")]
    [InlineData("not a url")]
    public void Canonical_RefusesAnythingButPlainHttps(string? url)
    {
        Assert.Null(CloudPaths.Canonical(url));
        Assert.False(CloudPaths.IsWebUrl(url));
    }

    [Fact]
    public void ToUrl_EscapesEachSegmentUnderTheLibraryAddress()
    {
        var url = CloudPaths.ToUrl(@"C:\Users\alice\Contoso\Tower B - Documents\Drawings\A-101 #2.pdf", new[] { AliceLibrary }, out var root);

        Assert.Equal("https://contoso.sharepoint.com/sites/TowerB/Shared%20Documents/Drawings/A-101%20%232.pdf", url);
        Assert.Same(AliceLibrary, root);
    }

    [Fact]
    public void ToUrl_IsNullOutsideEverySyncedFolder()
    {
        Assert.Null(CloudPaths.ToUrl(@"C:\Users\alice\Desktop\A.pdf", new[] { AliceLibrary, AliceOneDrive }, out var root));
        Assert.Null(root);
    }

    [Fact]
    public void ToUrl_DoesNotMatchAFolderThatOnlySharesTheStartOfItsName()
        => Assert.Null(CloudPaths.ToUrl(@"C:\Users\alice\Contoso\Tower B - Documents (old)\A.pdf", new[] { AliceLibrary }, out _));

    [Fact]
    public void ToUrl_UsesTheDeepestSyncedFolder_SoAShortcutWinsOverTheOneDriveAroundIt()
    {
        var url = CloudPaths.ToUrl(@"C:\Users\alice\OneDrive - Contoso\Tower B Drawings\A-101.pdf", new[] { AliceOneDrive, AliceShortcut }, out var root);

        Assert.Equal("https://contoso.sharepoint.com/sites/TowerB/Shared%20Documents/Drawings/A-101.pdf", url);
        Assert.Same(AliceShortcut, root);
    }

    [Fact]
    public void ToLocal_FindsTheFileInTheRecipientsOwnSyncedFolder()
    {
        var url = CloudPaths.ToUrl(@"C:\Users\alice\Contoso\Tower B - Documents\Drawings\A-101 #2.pdf", new[] { AliceLibrary }, out _);

        Assert.Equal(@"D:\Sync\Contoso\Tower B - Documents\Drawings\A-101 #2.pdf", CloudPaths.ToLocal(url, new[] { BobLibrary }));
    }

    [Fact]
    public void ToLocal_UsesTheDeepestLibraryAddress()
    {
        var bobShortcut = AliceShortcut with { LocalPath = @"D:\OneDrive\Drawings" };

        Assert.Equal(@"D:\OneDrive\Drawings\A-101.pdf",
            CloudPaths.ToLocal("https://contoso.sharepoint.com/sites/TowerB/Shared%20Documents/Drawings/A-101.pdf", new[] { BobLibrary, bobShortcut }));
    }

    [Fact]
    public void ToLocal_IsNullWhenNoSyncedFolderHoldsTheLibrary()
    {
        Assert.Null(CloudPaths.ToLocal("https://contoso.sharepoint.com/sites/Other/Shared%20Documents/A.pdf", new[] { BobLibrary }));
        Assert.Null(CloudPaths.ToLocal("https://contoso.sharepoint.com/sites/TowerB/Shared%20DocumentsX/A.pdf", new[] { BobLibrary }));
    }

    [Theory]
    [InlineData("https://contoso.sharepoint.com/sites/TowerB/Shared%20Documents/%2E%2E/%2E%2E/Windows/evil.pdf")]
    [InlineData("https://contoso.sharepoint.com/sites/TowerB/Shared%20Documents/..%5C..%5Cevil.pdf")]
    [InlineData("https://contoso.sharepoint.com/sites/TowerB/Shared%20Documents/C%3A/evil.pdf")]
    [InlineData("https://contoso.sharepoint.com/sites/TowerB/Shared%20Documents/a%2Fb.pdf")]
    public void ToLocal_RefusesAnAddressThatCouldStepOutOfTheSyncedFolder(string url)
        => Assert.Null(CloudPaths.ToLocal(url, new[] { BobLibrary }));

    [Theory]
    [InlineData("https://contoso.sharepoint.com/sites/A/Shared%20Documents", CloudLibraryKind.Library)]
    [InlineData("https://contoso-my.sharepoint.com/personal/alice_contoso_com/Documents", CloudLibraryKind.Personal)]
    [InlineData("https://d.docs.live.net/1a2b3c", CloudLibraryKind.ConsumerPersonal)]
    public void KindFromUrl_TellsLibrariesFromPersonalOneDrives(string url, CloudLibraryKind expected)
        => Assert.Equal(expected, CloudPaths.KindFromUrl(url));

    [Fact]
    public void ParentFileNameAndHost_ReadTheAddress()
    {
        const string url = "https://contoso.sharepoint.com/sites/A/Shared%20Documents/A%20101.pdf";

        Assert.Equal("https://contoso.sharepoint.com/sites/A/Shared%20Documents", CloudPaths.Parent(url));
        Assert.Equal("A 101.pdf", CloudPaths.FileName(url));
        Assert.Equal("contoso.sharepoint.com", CloudPaths.Host(url));
        Assert.Null(CloudPaths.Parent("https://contoso.sharepoint.com"));
    }
}
