using System;
using System.IO;
using System.Linq;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Tests 6, 7, 8, 11 and 12 from the Phase 1 plan's section 6: the real
/// FilePlacesStorage, exercised against a real (temporary) directory —
/// backup file creation, temp-file cleanup on failure, unique temp
/// naming, and quarantine behaviour.
/// </summary>
public sealed class FilePlacesStorageTests
{
    [Fact]
    public void Write_Twice_LeavesPreviousContentsInBackupFile()
    {
        using var dir = new TempDirectory();
        var storage = new FilePlacesStorage(dir.Path, "places.json");

        storage.Write("first contents");
        storage.Write("second contents");

        Assert.Equal("second contents", File.ReadAllText(Path.Combine(dir.Path, "places.json")));
        Assert.Equal("first contents", File.ReadAllText(Path.Combine(dir.Path, "places.bak.json")));
    }

    [Fact]
    public void FailedWrite_LeavesNoTempFileBehind()
    {
        using var dir = new TempDirectory();
        var storage = new FilePlacesStorage(dir.Path, "places.json");
        var targetPath = Path.Combine(dir.Path, "places.json");

        // A prior successful write, so the target exists and Write() takes
        // the File.Replace branch below.
        storage.Write("original contents");

        // Hold the target open exclusively so File.Replace fails partway
        // through the second write.
        using (var handle = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<Exception>(() => storage.Write("new contents"));
        }

        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
        // The original contents survive untouched.
        Assert.Equal("original contents", File.ReadAllText(targetPath));
    }

    private sealed class RecordingFilePlacesStorage : FilePlacesStorage
    {
        public System.Collections.Generic.List<string> GeneratedTempPaths { get; } = new();

        public RecordingFilePlacesStorage(string folder, string fileName) : base(folder, fileName)
        {
        }

        protected override string CreateTemporaryFilePath()
        {
            var path = base.CreateTemporaryFilePath();
            GeneratedTempPaths.Add(path);
            return path;
        }
    }

    [Fact]
    public void TwoWrites_UseDifferentTempFileNames()
    {
        using var dir = new TempDirectory();
        var storage = new RecordingFilePlacesStorage(dir.Path, "places.json");

        storage.Write("first");
        storage.Write("second");

        Assert.Equal(2, storage.GeneratedTempPaths.Count);
        Assert.NotEqual(storage.GeneratedTempPaths[0], storage.GeneratedTempPaths[1]);
    }

    [Fact]
    public void Quarantine_RenamesFile_AndBytesMatchOriginal()
    {
        using var dir = new TempDirectory();
        var storage = new FilePlacesStorage(dir.Path, "places.json");
        storage.Write("damaged-looking but doesn't matter to Quarantine");

        var originalBytes = File.ReadAllBytes(Path.Combine(dir.Path, "places.json"));

        var quarantinedPath = storage.Quarantine(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));

        Assert.False(File.Exists(Path.Combine(dir.Path, "places.json")));
        Assert.True(File.Exists(quarantinedPath));
        Assert.Equal("places.corrupt-20260304-050607.json", Path.GetFileName(quarantinedPath));
        Assert.Equal(originalBytes, File.ReadAllBytes(quarantinedPath));
    }

    [Fact]
    public void Quarantine_NeverOverwritesAnExistingQuarantineFile()
    {
        using var dir = new TempDirectory();
        var storage = new FilePlacesStorage(dir.Path, "places.json");
        storage.Write("second store contents");

        var timestamp = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var existingQuarantinePath = Path.Combine(dir.Path, "places.corrupt-20260304-050607.json");
        File.WriteAllText(existingQuarantinePath, "first store contents");

        var newQuarantinePath = storage.Quarantine(timestamp);

        Assert.NotEqual(existingQuarantinePath, newQuarantinePath);
        Assert.True(File.Exists(existingQuarantinePath));
        Assert.True(File.Exists(newQuarantinePath));
        Assert.Equal("first store contents", File.ReadAllText(existingQuarantinePath));
        Assert.Equal("second store contents", File.ReadAllText(newQuarantinePath));
    }
}
