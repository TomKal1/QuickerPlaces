using System;
using System.IO;
using System.Linq;
using System.Text;
using QuickerPlaces.Services.Revit.Handlers;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>The protocol folder: where files go, and writing them atomically.</summary>
public sealed class RevitProtocolFolderTests
{
    private const string RequestId = "4f1c2a9be0d34c7f8a61b0e5d27c9a13";

    [Fact]
    public void Paths_FollowTheLayout()
    {
        var folder = new RevitProtocolFolder(Path.Combine("root", "revit"));

        Assert.Equal(Path.Combine("root", "revit", "handlers"), folder.HandlersFolder);
        Assert.Equal(Path.Combine("root", "revit", "instances"), folder.InstancesFolder);
        Assert.Equal(Path.Combine("root", "revit", "requests", "2025", "contoso-x"), folder.RequestFolder("2025", "contoso-x"));
        Assert.Equal(Path.Combine("root", "revit", "requests", "2025", "contoso-x", RequestId + ".json"), folder.RequestFile("2025", "contoso-x", RequestId));
        Assert.Equal(Path.Combine("root", "revit", "requests", "2025", "contoso-x", RequestId + ".claimed-77"), folder.ClaimedFile("2025", "contoso-x", RequestId, 77));
        Assert.Equal(Path.Combine("root", "revit", "requests", "2025", "contoso-x", RequestId + ".result.json"), folder.ResultFile("2025", "contoso-x", RequestId));
        Assert.Equal(Path.Combine("root", "revit", "handlers", "contoso-x-2025.json"), folder.RegistrationFile("2025", "contoso-x"));
        Assert.Equal(Path.Combine("root", "revit", "instances", "contoso-x-2025-77.json"), folder.InstanceFile("2025", "contoso-x", 77));
    }

    [Fact]
    public void Default_IsUnderLocalAppData()
    {
        var root = RevitProtocolFolder.Default().Root;

        Assert.EndsWith(Path.Combine("QuickerPlaces", "revit"), root);
    }

    [Theory]
    [InlineData("2025", "..")]
    [InlineData("2025", "a/b")]
    [InlineData("..", "ok")]
    [InlineData("25", "ok")]
    public void PathsRefuseValuesThatBreakTheRules(string release, string handlerId)
    {
        var folder = new RevitProtocolFolder("root");

        Assert.Throws<ArgumentException>(() => folder.RequestFolder(release, handlerId));
    }

    [Fact]
    public void AtomicWrite_LeavesOnlyTheFinalFile()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "sub", "file.json");

        RevitProtocolFolder.WriteAtomic(path, Encoding.UTF8.GetBytes("{}"));

        Assert.Equal([path], Directory.GetFileSystemEntries(Path.GetDirectoryName(path)!));
        Assert.Equal("{}", File.ReadAllText(path));
    }

    [Fact]
    public void AtomicWrite_ReplacesAnExistingFile()
    {
        using var dir = new TempDirectory();
        var path = dir.File("file.json");

        RevitProtocolFolder.WriteAtomic(path, Encoding.UTF8.GetBytes("one"));
        RevitProtocolFolder.WriteAtomic(path, Encoding.UTF8.GetBytes("two"));

        Assert.Equal("two", File.ReadAllText(path));
        Assert.Single(Directory.GetFileSystemEntries(dir.Path));
    }

    [Fact]
    public void AtomicWrite_WhenTheRenameFails_RemovesItsTemporaryFile()
    {
        using var dir = new TempDirectory();
        var path = dir.File("target");
        Directory.CreateDirectory(path); // a folder in the way: the rename cannot replace it

        Assert.ThrowsAny<Exception>(() => RevitProtocolFolder.WriteAtomic(path, [1, 2, 3]));

        Assert.Equal([path], Directory.GetFileSystemEntries(dir.Path));
    }

    [Fact]
    public void ReadersSkip_DotFilesAndTmpFiles()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.File("a.json"), "{}");
        File.WriteAllText(dir.File(".b.json.0123456789abcdef0123456789abcdef.tmp"), "{}");
        File.WriteAllText(dir.File(".c.json"), "{}");
        File.WriteAllText(dir.File("d.json.tmp"), "{}");
        File.WriteAllText(dir.File("e.txt"), "{}");
        File.WriteAllText(dir.File("f.json"), "{}");

        var files = RevitProtocolFolder.JsonFiles(dir.Path).Select(Path.GetFileName);

        Assert.Equal(["a.json", "f.json"], files);
    }

    [Fact]
    public void JsonFiles_OfAMissingFolder_IsEmpty()
    {
        using var dir = new TempDirectory();

        Assert.Empty(RevitProtocolFolder.JsonFiles(dir.File("nope")));
    }

    [Theory]
    [InlineData(".x.json.abc.tmp", true)]
    [InlineData("x.tmp", true)]
    [InlineData(".hidden", true)]
    [InlineData("x.json", false)]
    [InlineData("x.claimed-12", false)]
    public void IgnorableNames(string name, bool ignorable) => Assert.Equal(ignorable, RevitProtocolFolder.IsIgnorable(name));
}
