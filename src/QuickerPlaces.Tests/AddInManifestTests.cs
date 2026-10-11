using System;
using System.IO;
using System.Linq;
using QuickerPlaces.Services.Revit.AddIns;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>Reading and finding Revit add-in manifests (read-only), and fingerprinting a DLL.</summary>
public sealed class AddInManifestTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string AddIn(string type, string body) => $"<?xml version=\"1.0\" encoding=\"utf-8\"?><RevitAddIns><AddIn Type=\"{type}\">{body}</AddIn></RevitAddIns>";

    [Fact]
    public void AnApplication_ReadsNameAssemblyAndIds_AndResolvesARelativeAssembly()
    {
        var path = Write(Path.Combine("a", "Tool.addin"),
            AddIn("Application", "<Name>Duct Exporter</Name><Assembly>bin\\Duct.dll</Assembly><AddInId>11111111-2222-3333-4444-555555555555</AddInId><FullClassName>Duct.App</FullClassName><VendorId>ACME</VendorId>"));

        var result = AddInManifestReader.ReadAddinFile(path, 2025, ManifestSource.UserAddins);

        var m = Assert.Single(result.Manifests);
        Assert.Equal(AddInKind.Application, m.Kind);
        Assert.Equal("Duct Exporter", m.Name);
        Assert.Equal(Path.Combine(_temp.Path, "a", "bin", "Duct.dll"), m.Assembly);
        Assert.Equal("11111111-2222-3333-4444-555555555555", m.AddInId);
        Assert.Equal("ACME", m.VendorId);
        Assert.Equal("Duct.App", m.FullClassName);
        Assert.Equal(2025, m.Release);
        Assert.Equal(path, m.ManifestPath);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public void ACommand_UsesText_AndADbApplicationUsesName_AndAnAbsoluteAssemblyIsKept()
    {
        var path = Write("m.addin",
            "<RevitAddIns>"
            + "<AddIn Type=\"Command\"><Text>Run It</Text><Assembly>C:\\Tools\\Cmd.dll</Assembly><AddInId>A</AddInId></AddIn>"
            + "<AddIn Type=\"DBApplication\"><Name>Updater</Name><Assembly>" + Path.Combine(_temp.Path, "Upd.dll") + "</Assembly></AddIn>"
            + "</RevitAddIns>");

        var manifests = AddInManifestReader.ReadAddinFile(path, 2024, ManifestSource.MachineAddins).Manifests;

        Assert.Equal(new[] { "Run It", "Updater" }, manifests.Select(m => m.Name));
        Assert.Equal("C:\\Tools\\Cmd.dll", manifests[0].Assembly);
        Assert.Equal(AddInKind.DBApplication, manifests[1].Kind);
        Assert.Equal(Path.Combine(_temp.Path, "Upd.dll"), manifests[1].Assembly);
    }

    [Fact]
    public void AnEntryWithAnUnknownTypeOrNoName_IsSkippedWithAReason_AndTheRestAreRead()
    {
        var path = Write("m.addin",
            "<RevitAddIns><AddIn Type=\"Widget\"><Name>X</Name></AddIn><AddIn Type=\"Application\"/><AddIn Type=\"Application\"><Name>Good</Name></AddIn></RevitAddIns>");

        var result = AddInManifestReader.ReadAddinFile(path, 2025, ManifestSource.UserAddins);

        Assert.Equal("Good", Assert.Single(result.Manifests).Name);
        Assert.Equal(2, result.Skipped.Count);
        Assert.All(result.Skipped, s => Assert.False(string.IsNullOrWhiteSpace(s.Reason)));
    }

    [Theory]
    [InlineData("<RevitAddIns><AddIn")]
    [InlineData("")]
    [InlineData("<Other/>")]
    public void MalformedOrForeignXml_IsSkippedNotThrown(string content)
    {
        var result = AddInManifestReader.ReadAddinFile(Write("bad.addin", content), 2025, ManifestSource.UserAddins);

        Assert.Empty(result.Manifests);
        Assert.Single(result.Skipped);
    }

    [Fact]
    public void ADtd_IsRefused_AndNothingExternalIsRead()
    {
        var secret = Write("secret.txt", "TOP-SECRET");
        var path = Write("evil.addin",
            $"<!DOCTYPE RevitAddIns [<!ENTITY x SYSTEM \"{new Uri(secret).AbsoluteUri}\">]><RevitAddIns><AddIn Type=\"Application\"><Name>&x;</Name></AddIn></RevitAddIns>");

        var result = AddInManifestReader.ReadAddinFile(path, 2025, ManifestSource.UserAddins);

        Assert.Empty(result.Manifests);
        Assert.Contains("DTD", Assert.Single(result.Skipped).Reason);
    }

    [Fact]
    public void AMissingFile_IsSkippedWithAReason()
    {
        var result = AddInManifestReader.ReadAddinFile(Path.Combine(_temp.Path, "gone.addin"), 2025, ManifestSource.UserAddins);

        Assert.Empty(result.Manifests);
        Assert.Single(result.Skipped);
    }

    private const string Bundle = """
        <?xml version="1.0" encoding="utf-8"?>
        <ApplicationPackage SchemaVersion="1.0" Name="Thing">
          <Components>
            <RuntimeRequirements OS="Win64" Platform="Revit" SeriesMin="R2022" SeriesMax="R2024" />
            <ComponentEntry AppName="Thing" ModuleName="./Contents/2022/Thing.addin" />
          </Components>
          <Components>
            <RuntimeRequirements OS="Win64" Platform="Revit" SeriesMin="R2025" />
            <ComponentEntry AppName="Thing25" ModuleName="./Contents/2025/Thing.addin" />
            <ComponentEntry AppName="Direct" ModuleName="./Contents/2025/Direct.dll" />
          </Components>
          <Components>
            <RuntimeRequirements OS="Win64" Platform="AutoCAD" SeriesMin="R24.0" />
            <ComponentEntry AppName="Cad" ModuleName="./Cad.dll" />
          </Components>
        </ApplicationPackage>
        """;

    [Theory]
    [InlineData(2021, 0)]
    [InlineData(2023, 1)]
    [InlineData(2024, 1)]
    [InlineData(2025, 2)]
    [InlineData(2026, 2)]
    public void ABundle_AppliesOnlyToTheSeriesItNames(int release, int expected)
    {
        var contents = Write(Path.Combine("Thing.bundle", "PackageContents.xml"), Bundle);
        Write(Path.Combine("Thing.bundle", "Contents", "2022", "Thing.addin"), AddIn("Application", "<Name>Old</Name><Assembly>Old.dll</Assembly>"));
        Write(Path.Combine("Thing.bundle", "Contents", "2025", "Thing.addin"), AddIn("Application", "<Name>New</Name><Assembly>New.dll</Assembly>"));

        var result = AddInManifestReader.ReadBundle(contents, release, ManifestSource.UserBundle);

        Assert.Equal(expected, result.Manifests.Count);
        Assert.DoesNotContain(result.Manifests, m => m.Name == "Cad");
    }

    [Fact]
    public void ABundle_ReadsTheAddinItPointsAt_AndNamesADirectDll()
    {
        var contents = Write(Path.Combine("Thing.bundle", "PackageContents.xml"), Bundle);
        Write(Path.Combine("Thing.bundle", "Contents", "2025", "Thing.addin"), AddIn("Application", "<Name>New</Name><Assembly>New.dll</Assembly>"));

        var manifests = AddInManifestReader.ReadBundle(contents, 2025, ManifestSource.UserBundle).Manifests;

        var viaAddin = manifests.Single(m => m.Name == "New");
        Assert.Equal(Path.Combine(_temp.Path, "Thing.bundle", "Contents", "2025", "New.dll"), viaAddin.Assembly);
        var direct = manifests.Single(m => m.Name == "Direct");
        Assert.Equal(AddInKind.Unspecified, direct.Kind);
        Assert.Equal(Path.Combine(_temp.Path, "Thing.bundle", "Contents", "2025", "Direct.dll"), direct.Assembly);
        Assert.Null(direct.AddInId);
    }

    [Fact]
    public void ABundleModuleThatIsMissing_IsSkippedWithAReason()
    {
        var contents = Write(Path.Combine("Thing.bundle", "PackageContents.xml"), Bundle);

        var result = AddInManifestReader.ReadBundle(contents, 2025, ManifestSource.UserBundle);

        Assert.Single(result.Manifests); // the direct DLL entry needs no manifest
        Assert.Single(result.Skipped);
    }

    [Fact]
    public void Discovery_FindsAllFourLocations_ForTheRequestedReleaseOnly()
    {
        var locations = new AddInLocations(
            Path.Combine(_temp.Path, "user", "Revit", "Addins"),
            Path.Combine(_temp.Path, "machine", "Revit", "Addins"),
            Path.Combine(_temp.Path, "user"),
            Path.Combine(_temp.Path, "machine"));
        Write(Path.Combine("user", "Revit", "Addins", "2025", "U.addin"), AddIn("Application", "<Name>UserAddin</Name><Assembly>u.dll</Assembly>"));
        Write(Path.Combine("user", "Revit", "Addins", "2024", "Other.addin"), AddIn("Application", "<Name>Other release</Name>"));
        Write(Path.Combine("machine", "Revit", "Addins", "2025", "M.addin"), AddIn("Command", "<Text>MachineAddin</Text><Assembly>m.dll</Assembly>"));
        Write(Path.Combine("machine", "Revit", "Addins", "2025", "notes.txt"), "not a manifest");
        foreach (var (root, name) in new[] { ("user", "UserBundle"), ("machine", "MachineBundle") })
        {
            Write(Path.Combine(root, "ApplicationPlugins", name + ".bundle", "PackageContents.xml"),
                $"<ApplicationPackage><Components><RuntimeRequirements Platform=\"Revit\" SeriesMin=\"R2024\" SeriesMax=\"R2026\"/><ComponentEntry AppName=\"{name}\" ModuleName=\"./{name}.dll\"/></Components></ApplicationPackage>");
        }
        Write(Path.Combine("user", "ApplicationPlugins", "Junk.bundle", "PackageContents.xml"), "<broken");

        var result = AddInManifestFinder.Find(2025, locations);

        Assert.Equal(
            new[] { "UserAddin", "MachineAddin", "UserBundle", "MachineBundle" },
            result.Manifests.Select(m => m.Name));
        Assert.Equal(
            new[] { ManifestSource.UserAddins, ManifestSource.MachineAddins, ManifestSource.UserBundle, ManifestSource.MachineBundle },
            result.Manifests.Select(m => m.Source));
        Assert.All(result.Manifests, m => Assert.Equal(2025, m.Release));
        Assert.Single(result.Skipped);
    }

    [Fact]
    public void Discovery_WithMissingOrNullFolders_FindsNothing()
    {
        var result = AddInManifestFinder.Find(2025, new AddInLocations(null, Path.Combine(_temp.Path, "nowhere"), null, Path.Combine(_temp.Path, "nowhere2")));

        Assert.Empty(result.Manifests);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public void Discovery_NeverChangesAnything()
    {
        Write(Path.Combine("user", "Revit", "Addins", "2025", "U.addin"), AddIn("Application", "<Name>X</Name><Assembly>x.dll</Assembly>"));
        string Snapshot() => string.Join("|", Directory.GetFiles(_temp.Path, "*", SearchOption.AllDirectories).OrderBy(f => f).Select(f => f + File.GetLastWriteTimeUtc(f).Ticks + new FileInfo(f).Length));
        var before = Snapshot();

        AddInManifestFinder.Find(2025, new AddInLocations(Path.Combine(_temp.Path, "user", "Revit", "Addins"), null, Path.Combine(_temp.Path, "user"), null));

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Sha256_OfAStandInDll_IsTheKnownHash_AndWorksWhileTheFileIsOpen()
    {
        var path = Write("Stand.dll", "hello");
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        // sha256("hello")
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", DllFingerprint.Sha256(path));
    }

    [Fact]
    public void Sha256_OfAMissingFile_IsNull()
    {
        Assert.Null(DllFingerprint.Sha256(Path.Combine(_temp.Path, "gone.dll")));
        Assert.False(DllFingerprint.SameHash(null, null));
        Assert.True(DllFingerprint.SameHash("AB", "ab"));
    }
}
