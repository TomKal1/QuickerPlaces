using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Services.Documents;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Paths of files a program holds open, as GetFinalPathNameByHandle spells
/// them, turned into session paths (held-files plan H2, H3).
/// </summary>
public sealed class HeldFilePathsTests
{
    private static readonly IReadOnlyDictionary<string, string> NoDrives = new Dictionary<string, string>();

    private static readonly Dictionary<string, string> Drives = new(StringComparer.OrdinalIgnoreCase)
    {
        ["P:"] = @"\\files\projects",
        ["T:"] = @"\\files\projects\Tower B\",
        ["Z:"] = @"\\other\share",
    };

    private static readonly string[] AppFolders =
    {
        @"C:\Users\Tom\AppData\Local",
        @"C:\Users\Tom\AppData\Roaming\",
        @"C:\Program Files",
    };

    private static string[] Paths(IEnumerable<string> finalPaths, IReadOnlyDictionary<string, string>? drives = null, string[]? folders = null)
        => HeldFilePaths.Resolve(finalPaths.Select(p => (p, "Bluebeam Revu")), drives ?? NoDrives, folders ?? Array.Empty<string>())
            .Select(f => f.Path)
            .ToArray();

    [Theory]
    [InlineData(@"\\?\C:\Jobs\A-101.pdf", @"C:\Jobs\A-101.pdf")]
    [InlineData(@"\\?\UNC\files\projects\Spec.pdf", @"\\files\projects\Spec.pdf")]
    [InlineData(@"\\?\unc\files\projects\Spec.pdf", @"\\files\projects\Spec.pdf")]
    [InlineData(@"C:\Jobs\A-101.pdf", @"C:\Jobs\A-101.pdf")]
    public void FromFinalPath_RemovesTheLongPathPrefix(string finalPath, string expected)
        => Assert.Equal(expected, HeldFilePaths.FromFinalPath(finalPath));

    [Theory]
    [InlineData(@"\\?\Volume{0b1c2d3e-0000-0000-0000-100000000000}\Jobs\A-101.pdf")]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolume3\A-101.pdf")]
    [InlineData("")]
    public void FromFinalPath_RefusesAVolumeOrDevicePath(string finalPath)
        => Assert.Null(HeldFilePaths.FromFinalPath(finalPath));

    [Theory]
    [InlineData(@"\\files\projects\Tower A\A-101.pdf", @"P:\Tower A\A-101.pdf")]
    [InlineData(@"\\FILES\Projects\Tower A\A-101.pdf", @"P:\Tower A\A-101.pdf")]
    [InlineData(@"\\files\projects\Tower B\Spec.pdf", @"T:\Spec.pdf")]
    [InlineData(@"\\files\projects2\A-101.pdf", @"\\files\projects2\A-101.pdf")]
    [InlineData(@"\\files\projects", @"\\files\projects")]
    [InlineData(@"C:\Jobs\A-101.pdf", @"C:\Jobs\A-101.pdf")]
    public void ToMappedDrive_UsesTheLongestMappedShare(string path, string expected)
        => Assert.Equal(expected, HeldFilePaths.ToMappedDrive(path, Drives));

    [Fact]
    public void ToMappedDrive_TwoLettersForOneShare_TheFirstLetterWins()
    {
        var drives = new Dictionary<string, string> { ["Q:"] = @"\\files\projects", ["P:"] = @"\\files\projects" };

        Assert.Equal(@"P:\A-101.pdf", HeldFilePaths.ToMappedDrive(@"\\files\projects\A-101.pdf", drives));
    }

    [Fact]
    public void Resolve_KeepsOnlyPdfWordAndExcelFiles()
        => Assert.Equal(new[] { @"C:\Jobs\A-101.pdf", @"C:\Jobs\Budget.xlsx" },
            Paths(new[] { @"\\?\C:\Jobs\A-101.pdf", @"\\?\C:\Jobs\markups.bfx", @"\\?\C:\Jobs\Budget.xlsx", @"\\?\C:\Jobs" }));

    [Fact]
    public void Resolve_DropsTemplatesAProgramHoldsForItsDocuments()
        => Assert.Equal(new[] { @"C:\Jobs\Letter.docx" }, Paths(new[]
        {
            @"\\?\S:\Templates\Letterhead.dotx", @"\\?\C:\Jobs\Letter.docx", @"\\?\S:\Templates\Macro.DOTM",
            @"\\?\S:\Templates\Budget.xltx", @"\\?\S:\Templates\Budget.xltm",
        }));

    [Fact]
    public void Resolve_DropsOfficeOwnerFiles()
        => Assert.Equal(new[] { @"C:\Jobs\Report.docx" }, Paths(new[] { @"\\?\C:\Jobs\~$Report.docx", @"\\?\C:\Jobs\Report.docx" }));

    [Fact]
    public void Resolve_DropsFilesInProgramAndApplicationFolders()
        => Assert.Equal(new[] { @"C:\Users\Tom\AppDataCopy\A-101.pdf" }, Paths(new[]
        {
            @"\\?\C:\Users\Tom\AppData\Local\Bluebeam\Revu\21\Studio\A-101.pdf",
            @"\\?\C:\Users\Tom\AppData\Roaming\Microsoft\Excel\XLSTART\PERSONAL.XLSB",
            @"\\?\C:\PROGRAM FILES\Bluebeam Software\Help.pdf",
            @"\\?\C:\Users\Tom\AppDataCopy\A-101.pdf",
        }, folders: AppFolders));

    [Fact]
    public void Resolve_SpellsSharesWithTheirMappedDrive()
        => Assert.Equal(new[] { @"P:\Tower A\A-101.pdf" }, Paths(new[] { @"\\?\UNC\files\projects\Tower A\A-101.pdf" }, Drives));

    [Fact]
    public void Resolve_DropsFilesInAFolderRedirectedToAMappedShare()
        => Assert.Equal(new[] { @"H:\Jobs\A-101.pdf" }, Paths(new[]
        {
            @"\\?\UNC\srv\profiles\Tom\AppData\Roaming\Microsoft\Excel\XLSTART\PERSONAL.XLSB",
            @"\\?\UNC\srv\profiles\Tom\Jobs\A-101.pdf",
        }, new Dictionary<string, string> { ["H:"] = @"\\srv\profiles\Tom" }, new[] { @"\\srv\profiles\Tom\AppData\Roaming" }));

    [Fact]
    public void Resolve_AFolderThatOnlySharesAnExcludedFoldersPrefix_IsKept()
        => Assert.Equal(new[] { @"C:\Program Files Archive\Tower A\A-101.pdf" },
            Paths(new[] { @"\\?\C:\Program Files Archive\Tower A\A-101.pdf" }, folders: AppFolders));

    [Fact]
    public void ToMappedDrive_IgnoresAMappingThatIsNotAShare()
    {
        var drives = new Dictionary<string, string> { ["P:"] = "", ["Q:"] = @"\\", ["R:"] = @"\\files", ["S:"] = @"\\files\" };

        Assert.Equal(@"\\files\projects\A-101.pdf", HeldFilePaths.ToMappedDrive(@"\\files\projects\A-101.pdf", drives));
    }

    [Fact]
    public void ToMappedDrive_WritesTheDriveLetterInUpperCase()
        => Assert.Equal(@"P:\A-101.pdf",
            HeldFilePaths.ToMappedDrive(@"\\files\projects\A-101.pdf", new Dictionary<string, string> { ["p:"] = @"\\files\projects" }));

    [Fact]
    public void Resolve_AFileHeldTwice_IsListedOnceWithTheFirstProgram()
    {
        var files = HeldFilePaths.Resolve(
            new[] { (@"\\?\C:\Jobs\A-101.pdf", "Bluebeam Revu"), (@"\\?\c:\jobs\a-101.PDF", "Adobe Acrobat") },
            NoDrives, Array.Empty<string>());

        Assert.Equal(new[] { new HeldFile(@"C:\Jobs\A-101.pdf", "Bluebeam Revu") }, files);
    }
}
