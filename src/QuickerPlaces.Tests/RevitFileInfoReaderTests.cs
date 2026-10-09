using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using QuickerPlaces.Services.Revit;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Reading a Revit file's saved release and worksharing state from its
/// BasicFileInfo stream (roadmap §4.21, "Reading the saved release"), against
/// stand-in compound files built by <see cref="RevitTestFiles"/>.
/// </summary>
public sealed class RevitFileInfoReaderTests : IDisposable
{
    private const string Central = @"\\server\projects\222111\222111_M_R25.rvt";
    private const string Local = @"C:\REVIT_LOCAL2025\222111_M_R25_someone.rvt";

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private RevitFileInfo ReadFile(byte[] basicFileInfo, string name = "model.rvt") =>
        RevitFileInfoReader.Read(RevitTestFiles.Write(_temp.File(name), basicFileInfo));

    [Fact]
    public void ModernLocal_GivesReleaseBuildAndLocal()
    {
        var info = ReadFile(RevitTestFiles.Modern("2025", workshared: true, worksharingType: 1, Central, Local));

        Assert.Equal(RevitFileProblem.None, info.Problem);
        Assert.Equal(2025, info.Release);
        Assert.Equal("20260410_1515(x64)", info.Build);
        Assert.Equal(RevitWorksharing.Local, info.Worksharing);
        Assert.Equal(Central, info.CentralModelPath);
        Assert.Equal(Local, info.LastSavePath);
        Assert.Equal(14, info.LayoutVersion);
    }

    [Fact]
    public void ModernCentral_IsCentral()
    {
        var info = ReadFile(RevitTestFiles.Modern("2024", workshared: true, worksharingType: 0, Central, Central));

        Assert.Equal(2024, info.Release);
        Assert.Equal(RevitWorksharing.Central, info.Worksharing);
    }

    [Fact]
    public void JustCreatedLocal_IsLocal()
    {
        var info = ReadFile(RevitTestFiles.Modern("2026", workshared: true, worksharingType: 3, Central, Local));

        Assert.Equal(RevitWorksharing.Local, info.Worksharing);
    }

    [Fact]
    public void InProgressWorksharing_IsUnknown()
    {
        var info = ReadFile(RevitTestFiles.Modern("2026", workshared: true, worksharingType: 2, Central, Local));

        Assert.Equal(2026, info.Release);
        Assert.Equal(RevitWorksharing.Unknown, info.Worksharing);
    }

    [Fact]
    public void NotWorkshared_IgnoresTheTypeByte()
    {
        var info = ReadFile(RevitTestFiles.Modern("2025", workshared: false, worksharingType: 1, null, @"C:\Work\Plain.rvt"));

        Assert.Equal(RevitWorksharing.NotWorkshared, info.Worksharing);
        Assert.Null(info.CentralModelPath);
    }

    [Fact]
    public void FormatIsTheOnlySourceOfTheRelease_NotTheBuildDate()
    {
        // Revit 2025's builds are dated 2026; the build must never be read as the release.
        var info = ReadFile(RevitTestFiles.Modern("2025", true, 1, Central, Local, build: "20260410_1515(x64)"));

        Assert.Equal(2025, info.Release);
    }

    [Theory]
    [InlineData("")]
    [InlineData("25")]
    [InlineData("Revit 2025")]
    [InlineData("1999")]
    public void UnreadableFormat_GivesNoRelease(string format)
    {
        var info = ReadFile(RevitTestFiles.BasicFileInfo(14, true, 1, "someone", Central, format, "20260410_1515(x64)", Local));

        Assert.Null(info.Release);
        Assert.Equal(RevitFileProblem.ReleaseNotRecorded, info.Problem);
    }

    [Theory]
    [InlineData("2019")]
    [InlineData("2021")]
    public void ReleaseBefore2022_IsTooOld_ButStillNamed(string format)
    {
        var info = ReadFile(RevitTestFiles.Modern(format, true, 1, Central, Local));

        Assert.Equal(int.Parse(format), info.Release);
        Assert.Equal(RevitFileProblem.TooOld, info.Problem);
    }

    [Fact]
    public void Release2022_IsSupported()
    {
        var info = ReadFile(RevitTestFiles.Modern("2022", true, 0, Central, Central));

        Assert.Equal(2022, info.Release);
        Assert.Equal(RevitFileProblem.None, info.Problem);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    public void LayoutBeforeTheFormatYear_IsTooOld(int layout)
    {
        // As Revit's own 2008 sample files are: no format year, only a build date.
        var info = ReadFile(RevitTestFiles.BasicFileInfo(layout, false, 0, null, null, "20080126_1900", @"C:\Documents and Settings\x\BatchPrintInitial.rvt"));

        Assert.Null(info.Release);
        Assert.Equal(RevitFileProblem.TooOld, info.Problem);
        Assert.Equal(layout, info.LayoutVersion);
    }

    [Fact]
    public void MissingLastSavePath_StillGivesTheRelease()
    {
        var bytes = RevitTestFiles.BasicFileInfo(14, true, 1, "someone", Central, "2025", "20260410_1515(x64)");
        var cut = bytes[..(bytes.Length - 10)]; // drop the filler: the stream ends right after the build

        var info = ReadFile(cut);

        Assert.Equal(2025, info.Release);
        Assert.Null(info.LastSavePath);
        Assert.Equal(RevitFileProblem.None, info.Problem);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000)]
    public void ImplausibleLayoutVersion_IsUnrecognised(int layout)
    {
        var info = ReadFile(RevitTestFiles.BasicFileInfo(layout, true, 1, "someone", Central, "2025", "b", Local));

        Assert.Null(info.Release);
        Assert.Equal(RevitFileProblem.UnrecognisedLayout, info.Problem);
    }

    [Fact]
    public void StringLongerThanTheStream_IsUnrecognised()
    {
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.Unicode, leaveOpen: true))
        {
            writer.Write(14);
            writer.Write(true);
            writer.Write((byte)1);
            writer.Write(5_000_000); // username length far past the end
            writer.Write(Encoding.Unicode.GetBytes("abc"));
        }

        var info = ReadFile(bytes.ToArray());

        Assert.Equal(RevitFileProblem.UnrecognisedLayout, info.Problem);
        Assert.Null(info.Release);
    }

    [Fact]
    public void StreamEndingMidHeader_IsUnrecognised()
    {
        var info = ReadFile(new byte[] { 14, 0, 0 });

        Assert.Equal(RevitFileProblem.UnrecognisedLayout, info.Problem);
    }

    [Fact]
    public void OversizedStream_IsNotRead()
    {
        var huge = new byte[RevitFileInfoReader.MaxStreamBytes + 1];
        BitConverter.GetBytes(14).CopyTo(huge, 0);

        var info = ReadFile(huge);

        Assert.Equal(RevitFileProblem.UnrecognisedLayout, info.Problem);
    }

    [Fact]
    public void CompoundFileWithoutBasicFileInfo_IsNotARevitFile()
    {
        var path = RevitTestFiles.Write(_temp.File("other.rvt"), new Dictionary<string, byte[]> { ["WordDocument"] = new byte[100] });

        var info = RevitFileInfoReader.Read(path);

        Assert.Equal(RevitFileProblem.NotRevitFile, info.Problem);
    }

    [Fact]
    public void FileThatIsNotACompoundFile_IsNotARevitFile()
    {
        var path = _temp.File("text.rvt");
        File.WriteAllText(path, "This is not a Revit model, just text pretending to be one.");

        var info = RevitFileInfoReader.Read(path);

        Assert.Equal(RevitFileProblem.NotRevitFile, info.Problem);
    }

    [Fact]
    public void EmptyFile_IsNotARevitFile()
    {
        var path = _temp.File("empty.rvt");
        File.WriteAllBytes(path, Array.Empty<byte>());

        Assert.Equal(RevitFileProblem.NotRevitFile, RevitFileInfoReader.Read(path).Problem);
    }

    [Fact]
    public void MissingFile_IsNotFound()
    {
        Assert.Equal(RevitFileProblem.NotFound, RevitFileInfoReader.Read(_temp.File("gone.rvt")).Problem);
        Assert.Equal(RevitFileProblem.NotFound, RevitFileInfoReader.Read(_temp.File(Path.Combine("no-folder", "gone.rvt"))).Problem);
    }

    [Fact]
    public void FileOpenForWritingElsewhere_IsStillRead()
    {
        // Revit keeps an open model's file open; reading must share with it.
        var path = RevitTestFiles.Write(_temp.File("open.rvt"), RevitTestFiles.Modern("2025", true, 1, Central, Local));
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var info = RevitFileInfoReader.Read(path);

        Assert.Equal(2025, info.Release);
    }

    [Fact]
    public void FileLockedAgainstReading_IsInUse()
    {
        var path = RevitTestFiles.Write(_temp.File("locked.rvt"), RevitTestFiles.Modern("2025", true, 1, Central, Local));
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var info = RevitFileInfoReader.Read(path);

        if (OperatingSystem.IsWindows())
            Assert.Equal(RevitFileProblem.InUse, info.Problem);
    }

    [Fact]
    public void ReadingNeverChangesTheFile()
    {
        var path = RevitTestFiles.Write(_temp.File("model.rvt"), RevitTestFiles.Modern("2025", true, 1, Central, Local));
        var before = File.ReadAllBytes(path);
        var written = File.GetLastWriteTimeUtc(path);

        RevitFileInfoReader.Read(path);

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    /// <summary>
    /// Real models, when a folder of them is named in QP_REVIT_SAMPLES
    /// (never committed: they carry usernames and network paths). Each file
    /// is named for its release and state, e.g. "R2025 central.rvt" or
    /// "R2026 local.rvt"; the test checks the reader agrees. Passes with
    /// nothing to check when the variable is unset.
    /// </summary>
    [Fact]
    public void RealSamples_WhenProvided()
    {
        var folder = Environment.GetEnvironmentVariable("QP_REVIT_SAMPLES");
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        foreach (var path in Directory.EnumerateFiles(folder, "R20*.rvt"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var info = RevitFileInfoReader.Read(path);
            Assert.True(info.Problem == RevitFileProblem.None, $"{name}: {info.Problem}");
            Assert.Equal(int.Parse(name.Substring(1, 4)), info.Release);

            var expected = name.Contains("central", StringComparison.OrdinalIgnoreCase) ? RevitWorksharing.Central
                : name.Contains("local", StringComparison.OrdinalIgnoreCase) ? RevitWorksharing.Local
                : RevitWorksharing.NotWorkshared;
            Assert.Equal(expected, info.Worksharing);
        }
    }
}
