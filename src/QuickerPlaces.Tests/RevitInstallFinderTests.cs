using System.IO;
using System.Linq;
using QuickerPlaces.Services.Revit.Opening;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>Which Revit releases count as installed, and which release a running Revit.exe is.</summary>
public sealed class RevitInstallFinderTests
{
    private readonly FakeInstallSource _source = new();

    private static string Exe(string folder) => Path.Combine(folder, "Revit.exe");

    [Fact]
    public void DefaultFolders_AreFound_OldestFirst_AndOnlyWhenTheExeExists()
    {
        _source.AddDefault(2025);
        _source.AddDefault(2023);
        _source.AutodeskFolders.Add("Revit 2024"); // folder, but no exe

        var installs = RevitInstallFinder.Find(_source);

        Assert.Equal([2023, 2025], installs.Select(i => i.Release));
        Assert.Equal("2025", installs[1].ReleaseText);
    }

    [Fact]
    public void RegistryEntries_NamedRevitYear_WithAnInstallLocation_AreFound()
    {
        var d = Path.Combine("D:", "Apps", "Revit 2025");
        var e = Path.Combine("E:", "Revit 2026");
        _source.Entries.Add(new UninstallEntry("Autodesk Revit 2025", d));
        _source.Entries.Add(new UninstallEntry("Revit 2026", e));
        _source.Files.Add(Exe(d));
        _source.Files.Add(Exe(e));

        Assert.Equal([Exe(d), Exe(e)], RevitInstallFinder.Find(_source).Select(i => i.ExePath));
    }

    [Fact]
    public void RegistryLocation_BeatsTheDefault_AndFallsBackWhenItsExeIsGone()
    {
        var custom = Path.Combine("D:", "Revit 2025");
        var defaultExe = _source.AddDefault(2025);
        _source.Entries.Add(new UninstallEntry("Autodesk Revit 2025", custom));

        Assert.Equal(defaultExe, Assert.Single(RevitInstallFinder.Find(_source)).ExePath);

        _source.Files.Add(Exe(custom));
        Assert.Equal(Exe(custom), Assert.Single(RevitInstallFinder.Find(_source)).ExePath);
    }

    [Theory]
    [InlineData("Autodesk Revit LT 2025")]
    [InlineData("Autodesk Revit Interoperability for Revit 2025")]
    [InlineData("Revit 2025 Content")]
    [InlineData("Autodesk Revit 2021")]
    [InlineData("Autodesk Revit 2019")]
    [InlineData("Something else")]
    public void OtherEntries_AndReleasesBefore2022_AreIgnored(string displayName)
    {
        var folder = Path.Combine("D:", "x");
        _source.Entries.Add(new UninstallEntry(displayName, folder));
        _source.Files.Add(Exe(folder));

        Assert.Empty(RevitInstallFinder.Find(_source));
    }

    [Fact]
    public void ReleasesBefore2022_InTheDefaultFolder_AreIgnored()
    {
        _source.AddDefault(2021);

        Assert.Empty(RevitInstallFinder.Find(_source));
    }

    [Fact]
    public void BlankInstallLocation_OrNoProgramFiles_FindsNothing()
    {
        _source.Entries.Add(new UninstallEntry("Autodesk Revit 2025", "  "));
        _source.Entries.Add(new UninstallEntry(null, "x"));
        _source.ProgramFilesFolder = null;

        Assert.Empty(RevitInstallFinder.Find(_source));
    }

    [Fact]
    public void TheMachineSource_NeverThrows_AndOffWindowsFindsNothing()
    {
        var installs = RevitInstallFinder.Find(new SystemRevitInstallSource());

        if (!System.OperatingSystem.IsWindows())
            Assert.Empty(installs);
    }

    // --- Running processes -------------------------------------------------------

    private static readonly System.DateTime Started = new(2026, 10, 9, 8, 0, 0, System.DateTimeKind.Utc);

    [Fact]
    public void AProcess_IsMatchedToAnInstallByItsExePath_IgnoringCase()
    {
        var exe = _source.AddDefault(2025);
        var installs = RevitInstallFinder.Find(_source);

        var resolved = RevitProcesses.Resolve([new RevitProcessInfo(10, Started, exe.ToUpperInvariant(), FileMajorVersion: 24)], installs);

        Assert.Equal(2025, Assert.Single(resolved).Release); // the path wins over the version
    }

    [Fact]
    public void AProcess_WithNoMatchingInstall_FallsBackToTheFileVersionPlus2000()
    {
        var resolved = RevitProcesses.Resolve(
        [
            new RevitProcessInfo(1, Started, @"Z:\Elsewhere\Revit.exe", 26),
            new RevitProcessInfo(2, Started, null, 25),
            new RevitProcessInfo(3, Started, null, 21), // 2021: not supported
            new RevitProcessInfo(4, Started, null, null),
        ], []);

        Assert.Equal([2026, 2025, null, null], resolved.Select(r => r.Release));
        Assert.Equal("2026", resolved[0].ReleaseText);
    }
}
