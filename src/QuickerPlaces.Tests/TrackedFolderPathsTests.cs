using QuickerPlaces.Services.Activity;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>Where a path sits among tracked folders (Desk layout design §4): its depth below a root, and which root holds it.</summary>
public sealed class TrackedFolderPathsTests
{
    [Theory]
    [InlineData(@"C:\Jobs", @"C:\Jobs", 0)]
    [InlineData(@"C:\Jobs\", @"C:\Jobs", 0)]
    [InlineData(@"C:\Jobs", @"C:\Jobs\Acme", 1)]
    [InlineData(@"C:\Jobs", @"c:\jobs\Acme\Plans\", 2)]
    [InlineData(@"\\server\share", @"\\server\share\Acme", 1)]
    public void LevelBelow_CountsFoldersBelowTheRoot(string root, string path, int level)
        => Assert.Equal(level, TrackedFolderPaths.LevelBelow(root, path));

    [Theory]
    [InlineData(@"C:\Jobs", @"C:\JobsOld\Acme")]
    [InlineData(@"C:\Jobs", @"D:\Jobs")]
    [InlineData(@"C:\Jobs", "")]
    public void LevelBelow_IsNull_OutsideTheRoot(string root, string path)
        => Assert.Null(TrackedFolderPaths.LevelBelow(root, path));

    [Fact]
    public void RootFor_IsTheInnermostRootHoldingThePath()
    {
        var roots = new[] { @"C:\Jobs", @"C:\Jobs\Acme", @"D:\Other" };

        Assert.Equal(@"C:\Jobs\Acme", TrackedFolderPaths.RootFor(roots, @"C:\Jobs\Acme\Plans"));
        Assert.Equal(@"C:\Jobs", TrackedFolderPaths.RootFor(roots, @"C:\Jobs\Beta"));
        Assert.Null(TrackedFolderPaths.RootFor(roots, @"E:\Elsewhere"));
    }

    [Theory]
    [InlineData(0, "Root folder")]
    [InlineData(1, "Level 1 · directly below root")]
    [InlineData(3, "Level 3 · below root")]
    public void LevelLabel_IsTheRecentsWindowsWording(int level, string label)
        => Assert.Equal(label, TrackedFolderPaths.LevelLabel(level));
}
