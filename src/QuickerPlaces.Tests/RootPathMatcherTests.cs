using QuickerPlaces.Models.Activity;
using QuickerPlaces.Services.Activity;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Which folder an observed Explorer path credits under a tracked root
/// (Phase 9 plan D8, D13, D15, D22). Windows paths are literal strings and
/// never go near Path.*, which only knows '/' on Linux, where these tests
/// also run.
/// </summary>
public sealed class RootPathMatcherTests
{
    private static TrackedRootConfig Root(string path, RollupMode rollup = RollupMode.RootChild, int depth = 1, params string[] equivalents)
        => new("root-1", path) { Rollup = rollup, Depth = depth, EquivalentPrefixes = equivalents };

    // ---- D8: the three rollups ----

    [Fact]
    public void RootChild_CreditsTheImmediateChildOfTheRoot()
    {
        Assert.Equal(@"C:\Jobs\Acme", RootPathMatcher.Credit(@"C:\Jobs\Acme\Drawings\Rev3", Root(@"C:\Jobs")));
    }

    [Fact]
    public void Exact_CreditsTheFolderItself()
    {
        Assert.Equal(@"C:\Jobs\Acme\Drawings\Rev3", RootPathMatcher.Credit(@"C:\Jobs\Acme\Drawings\Rev3", Root(@"C:\Jobs", RollupMode.Exact)));
    }

    [Fact]
    public void Depth_CreditsTheAncestorThatManyLevelsBelowTheRoot()
    {
        Assert.Equal(@"C:\Jobs\Acme\Drawings", RootPathMatcher.Credit(@"C:\Jobs\Acme\Drawings\Rev3", Root(@"C:\Jobs", RollupMode.Depth, 2)));
    }

    [Fact]
    public void DepthOne_IsRootChild()
    {
        Assert.Equal(@"C:\Jobs\Acme", RootPathMatcher.Credit(@"C:\Jobs\Acme\Drawings\Rev3", Root(@"C:\Jobs", RollupMode.Depth, 1)));
    }

    [Fact]
    public void Depth_DeeperThanThePath_CreditsTheFolderItself()
    {
        Assert.Equal(@"C:\Jobs\Acme", RootPathMatcher.Credit(@"C:\Jobs\Acme", Root(@"C:\Jobs", RollupMode.Depth, 3)));
    }

    [Theory]
    [InlineData(RollupMode.RootChild)]
    [InlineData(RollupMode.Exact)]
    [InlineData(RollupMode.Depth)]
    public void ThePathThatIsTheRoot_CreditsTheRootItself(RollupMode rollup)
    {
        Assert.Equal(@"C:\Jobs", RootPathMatcher.Credit(@"C:\Jobs", Root(@"C:\Jobs", rollup, 2)));
    }

    [Fact]
    public void ADriveRootAsTheRoot_CreditsItsChildrenWithOneSeparator()
    {
        Assert.Equal(@"J:\Acme", RootPathMatcher.Credit(@"J:\Acme\Drawings", Root(@"J:\")));
    }

    [Fact]
    public void ADriveRootAsTheRoot_CreditsItselfAsTheDriveRoot()
    {
        // "J:" alone means "the current directory on J:", so the root keeps its separator.
        Assert.Equal(@"J:\", RootPathMatcher.Credit(@"J:\", Root(@"J:\")));
    }

    [Fact]
    public void AUncShareAsTheRoot_CreditsItsChildren()
    {
        Assert.Equal(@"\\fileserver\projects\Acme", RootPathMatcher.Credit(@"\\fileserver\projects\Acme\Drawings", Root(@"\\fileserver\projects")));
    }

    // ---- D13: what is dropped ----

    [Theory]
    [InlineData("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]              // This PC
    [InlineData(@"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}\C:\Jobs\Acme")]
    [InlineData("ftp://example.com/Jobs/Acme")]
    [InlineData("https://example.com/Jobs")]
    [InlineData(@"file:///C:/Jobs/Acme")]
    [InlineData(@"Jobs\Acme")]                                             // relative
    [InlineData(@"\Jobs\Acme")]                                            // rooted, but no drive
    [InlineData(@"C:Jobs\Acme")]                                           // drive-relative
    [InlineData(@"\\fileserver")]                                          // a server, not a share
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NonFilesystemAndRelativePaths_AreDropped(string? path)
    {
        Assert.Null(RootPathMatcher.Credit(path, Root(@"C:\Jobs")));
    }

    [Theory]
    [InlineData(@"C:\Other\Acme")]
    [InlineData(@"D:\Jobs\Acme")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\JobsArchive\Acme")]                                   // shares a prefix, not a folder
    public void APathOutsideTheRoot_IsDropped(string path)
    {
        Assert.Null(RootPathMatcher.Credit(path, Root(@"C:\Jobs")));
    }

    // ---- D15: normalization ----

    [Theory]
    [InlineData(@"c:\jobs\ACME\Drawings")]
    [InlineData(@"C:/Jobs/Acme/Drawings")]
    [InlineData(@"C:\Jobs\\Acme\Drawings\")]
    [InlineData(@"C:\Jobs\.\Acme\Drawings")]
    [InlineData(@"C:\Jobs\Other\..\Acme\Drawings")]
    public void Matching_IsCaseAndSeparatorInsensitive(string path)
    {
        var credited = RootPathMatcher.Credit(path, Root(@"C:\Jobs"));

        Assert.Equal(@"C:\Jobs\Acme", credited, ignoreCase: true);
    }

    [Theory]
    [InlineData(@"c:\JOBS")]
    [InlineData(@"C:/Jobs/")]
    [InlineData(@"C:\Jobs\\")]
    public void TheRootsOwnSpelling_MatchesWhateverWayItIsWritten(string rootPath)
    {
        Assert.StartsWith(@"C:\Jobs\Acme", RootPathMatcher.Credit(@"C:\Jobs\Acme", Root(rootPath)) ?? "", System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheCreditedFolder_IsSpelledWithTheRootsOwnPath()
    {
        // The root as the user chose it, the rest as Explorer showed it.
        Assert.Equal(@"C:\Jobs\ACME", RootPathMatcher.Credit(@"c:\jobs\ACME\Drawings", Root(@"C:\Jobs")));
    }

    [Fact]
    public void TheCreditedFolder_UsesBackslashesEvenWhenTheRootWasWrittenWithSlashes()
    {
        Assert.Equal(@"C:\Jobs\Acme", RootPathMatcher.Credit(@"C:\Jobs\Acme", Root(@"C:/Jobs/")));
    }

    [Fact]
    public void AnEquivalentPrefix_Matches_AndIsCreditedUnderTheRootsOwnPath()
    {
        var root = Root(@"J:\Jobs", equivalents: @"\\fileserver\projects\Jobs");

        Assert.Equal(@"J:\Jobs\Acme", RootPathMatcher.Credit(@"\\fileserver\projects\Jobs\Acme\Drawings", root));
        Assert.Equal(@"J:\Jobs\Acme", RootPathMatcher.Credit(@"J:\Jobs\Acme\Drawings", root));
    }

    [Fact]
    public void AnEquivalentPrefix_IsMatchedCaseAndSeparatorInsensitively()
    {
        var root = Root(@"J:\Jobs", equivalents: @"\\FileServer\Projects\Jobs\");

        Assert.Equal(@"J:\Jobs\Acme", RootPathMatcher.Credit(@"\\fileserver\projects\jobs\Acme", root), ignoreCase: true);
    }

    [Fact]
    public void AnEquivalentPrefix_ThatIsTheRoot_CreditsTheRootsOwnPath()
    {
        var root = Root(@"J:\Jobs", equivalents: @"\\fileserver\projects\Jobs");

        Assert.Equal(@"J:\Jobs", RootPathMatcher.Credit(@"\\fileserver\projects\Jobs", root));
    }

    [Fact]
    public void AMappedDriveThatWasNotConfigured_DoesNotMatch()
    {
        // D15: nothing is unified behind the user's back.
        Assert.Null(RootPathMatcher.Credit(@"\\fileserver\projects\Jobs\Acme", Root(@"J:\Jobs")));
    }

    [Fact]
    public void AnUnusableEquivalentPrefix_IsIgnored()
    {
        var root = Root(@"J:\Jobs", equivalents: new[] { "", "relative", "::{GUID}" });

        Assert.Null(RootPathMatcher.Credit(@"relative\Acme", root));
        Assert.Equal(@"J:\Jobs\Acme", RootPathMatcher.Credit(@"J:\Jobs\Acme", root));
    }
}
