using System;
using System.IO;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Tests 9, 13, 14, 15, and 15a to 15f from the Phase 1 plan's section 6:
/// the schema-version gate (5.3) and the D6 load-failure classification
/// that feeds recovery (5.4).
///
/// Per the plan's section 7 step 5, 15a-15f are written before any
/// recovery UI exists — they are what stops the Unreadable path from
/// quietly acquiring an empty-store button later. Anyone changing
/// LoadFromDisk's classification should be breaking one of these tests on
/// purpose, not by accident.
/// </summary>
public sealed class PlacesServiceLoadOutcomeTests
{
    /// <summary>A fresh storage with no file yet is NotPresent, not a failure.</summary>
    [Fact]
    public void NoStoreFile_YieldsNotPresent()
    {
        var storage = new FakePlacesStorage();
        var service = new PlacesService(storage);

        Assert.Equal(StoreLoadOutcome.NotPresent, service.LoadOutcome);
        Assert.Empty(service.Places);
        Assert.False(service.IsRecoveryUnresolved);
    }

    /// <summary>A valid v1 store loads normally and never sets recovery state.</summary>
    [Fact]
    public void ValidV1Store_YieldsOk()
    {
        var storage = new FakePlacesStorage
        {
            ContentsToReturn = "{ \"schemaVersion\": 1, \"places\": [] }"
        };
        var service = new PlacesService(storage);

        Assert.Equal(StoreLoadOutcome.Ok, service.LoadOutcome);
        Assert.False(service.IsRecoveryUnresolved);
    }

    /// <summary>Test 9: unparseable JSON yields Damaged, and the file on disk is left byte-identical — proven against a real FilePlacesStorage, not the fake.</summary>
    [Fact]
    public void UnparseableJson_YieldsDamaged_AndLeavesTheFileByteIdentical()
    {
        using var dir = new TempDirectory();
        var storage = new FilePlacesStorage(dir.Path, "places.json");
        var targetPath = Path.Combine(dir.Path, "places.json");
        File.WriteAllText(targetPath, "{ this is not valid json at all");
        var originalBytes = File.ReadAllBytes(targetPath);

        var service = new PlacesService(storage);

        Assert.Equal(StoreLoadOutcome.Damaged, service.LoadOutcome);
        Assert.True(service.IsRecoveryUnresolved);
        Assert.Empty(service.Places);
        Assert.Equal(originalBytes, File.ReadAllBytes(targetPath));
    }

    /// <summary>Test 13: a schemaVersion above current yields WrittenByNewerVersion, and the file is untouched.</summary>
    [Fact]
    public void SchemaVersionAboveCurrent_YieldsWrittenByNewerVersion_AndLeavesTheFileUntouched()
    {
        using var dir = new TempDirectory();
        var storage = new FilePlacesStorage(dir.Path, "places.json");
        var targetPath = Path.Combine(dir.Path, "places.json");
        File.WriteAllText(targetPath, "{ \"schemaVersion\": 99, \"places\": [] }");
        var originalBytes = File.ReadAllBytes(targetPath);

        var service = new PlacesService(storage);

        Assert.Equal(StoreLoadOutcome.WrittenByNewerVersion, service.LoadOutcome);
        Assert.True(service.IsRecoveryUnresolved);
        Assert.Empty(service.Places);
        Assert.Equal(originalBytes, File.ReadAllBytes(targetPath));
    }

    /// <summary>Test 14: a missing schemaVersion is Damaged, not assumed to be v1.</summary>
    [Fact]
    public void MissingSchemaVersion_YieldsDamaged()
    {
        var storage = new FakePlacesStorage
        {
            ContentsToReturn = "{ \"places\": [] }"
        };
        var service = new PlacesService(storage);

        Assert.Equal(StoreLoadOutcome.Damaged, service.LoadOutcome);
    }

    /// <summary>Test 14: a non-numeric schemaVersion is also Damaged, not assumed to be v1.</summary>
    [Fact]
    public void NonNumericSchemaVersion_YieldsDamaged()
    {
        var storage = new FakePlacesStorage
        {
            ContentsToReturn = "{ \"schemaVersion\": \"one\", \"places\": [] }"
        };
        var service = new PlacesService(storage);

        Assert.Equal(StoreLoadOutcome.Damaged, service.LoadOutcome);
    }

    /// <summary>
    /// Test 15: even though there is currently nothing to migrate (no
    /// prior schema version exists), the load path must never write to
    /// disk on its own — only a save through the normal path may. A
    /// version equal to current already exercises "loaded, never written
    /// unless a save happens", which is the guarantee this test protects.
    /// </summary>
    [Fact]
    public void LoadingAStore_NeverWritesToDiskUntilASaveSucceeds()
    {
        var storage = new FakePlacesStorage
        {
            ContentsToReturn = "{ \"schemaVersion\": 1, \"places\": [] }",
            FailEveryWrite = true
        };

        var service = new PlacesService(storage);

        Assert.Equal(StoreLoadOutcome.Ok, service.LoadOutcome);
        Assert.Equal(0, storage.WriteCount);
    }

    /// <summary>Test 15a (D6): an IOException on read yields Unreadable, not Damaged.</summary>
    [Fact]
    public void IOExceptionOnRead_YieldsUnreadable()
    {
        var storage = new FakePlacesStorage
        {
            ContentsToReturn = "irrelevant — Read() throws before this is used",
            ReadThrows = new IOException("Simulated read failure.")
        };

        var service = new PlacesService(storage);

        Assert.Equal(StoreLoadOutcome.Unreadable, service.LoadOutcome);
        Assert.True(service.IsRecoveryUnresolved);
    }

    /// <summary>Test 15b (D6): an UnauthorizedAccessException on read yields Unreadable.</summary>
    [Fact]
    public void UnauthorizedAccessExceptionOnRead_YieldsUnreadable()
    {
        var storage = new FakePlacesStorage
        {
            ContentsToReturn = "irrelevant — Read() throws before this is used",
            ReadThrows = new UnauthorizedAccessException("Simulated permission failure.")
        };

        var service = new PlacesService(storage);

        Assert.Equal(StoreLoadOutcome.Unreadable, service.LoadOutcome);
        Assert.True(service.IsRecoveryUnresolved);
    }

    /// <summary>Test 15c (D6's safe default): an unexpected exception type on read is still Unreadable, not Damaged.</summary>
    [Fact]
    public void UnexpectedExceptionTypeOnRead_YieldsUnreadable()
    {
        var storage = new FakePlacesStorage
        {
            ContentsToReturn = "irrelevant — Read() throws before this is used",
            ReadThrows = new InvalidOperationException("Simulated unexpected failure.")
        };

        var service = new PlacesService(storage);

        Assert.Equal(StoreLoadOutcome.Unreadable, service.LoadOutcome);
    }

    /// <summary>
    /// Test 15d: an Unreadable outcome never quarantines and never writes.
    /// Real FilePlacesStorage over a file held open with FileShare.None,
    /// so Read() genuinely throws — the file's bytes and name are
    /// unchanged afterwards and no quarantine file exists.
    /// </summary>
    [Fact]
    public void UnreadableOutcome_NeverQuarantinesAndNeverWrites()
    {
        using var dir = new TempDirectory();
        var storage = new FilePlacesStorage(dir.Path, "places.json");
        var targetPath = Path.Combine(dir.Path, "places.json");
        File.WriteAllText(targetPath, "{ \"schemaVersion\": 1, \"places\": [] }");
        var originalBytes = File.ReadAllBytes(targetPath);

        using (var handle = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var service = new PlacesService(storage);

            Assert.Equal(StoreLoadOutcome.Unreadable, service.LoadOutcome);
        }

        Assert.True(File.Exists(targetPath), "The original file must still exist under its original name.");
        Assert.Equal(originalBytes, File.ReadAllBytes(targetPath));
        Assert.Empty(Directory.GetFiles(dir.Path, "places.corrupt-*.json"));
    }

    /// <summary>
    /// Test 15e: a retry after the lock is released loads the original
    /// places intact via Reload(), and clears the recovery-unresolved
    /// state.
    /// </summary>
    [Fact]
    public void ReloadAfterLockIsReleased_LoadsTheOriginalPlacesIntact()
    {
        using var dir = new TempDirectory();
        var storage = new FilePlacesStorage(dir.Path, "places.json");
        var targetPath = Path.Combine(dir.Path, "places.json");
        File.WriteAllText(
            targetPath,
            "{ \"schemaVersion\": 1, \"places\": [ { \"alias\": \"Docs\", \"type\": \"folder\", \"resource\": \"C:\\\\Docs\" } ] }");

        PlacesService service;
        using (var handle = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            service = new PlacesService(storage);
            Assert.Equal(StoreLoadOutcome.Unreadable, service.LoadOutcome);
            Assert.True(service.IsRecoveryUnresolved);
        }
        // The handle is released here — the file is now readable again.

        var outcome = service.Reload();

        Assert.Equal(StoreLoadOutcome.Ok, outcome);
        Assert.Equal(StoreLoadOutcome.Ok, service.LoadOutcome);
        Assert.False(service.IsRecoveryUnresolved);
        var place = Assert.Single(service.Places);
        Assert.Equal("Docs", place.Alias);
    }

    /// <summary>
    /// Test 15f: a WrittenByNewerVersion outcome never quarantines and
    /// never writes — the directory contains exactly the one original
    /// file afterwards.
    /// </summary>
    [Fact]
    public void WrittenByNewerVersionOutcome_NeverQuarantinesAndNeverWrites()
    {
        using var dir = new TempDirectory();
        var storage = new FilePlacesStorage(dir.Path, "places.json");
        File.WriteAllText(Path.Combine(dir.Path, "places.json"), "{ \"schemaVersion\": 99, \"places\": [] }");

        var service = new PlacesService(storage);

        Assert.Equal(StoreLoadOutcome.WrittenByNewerVersion, service.LoadOutcome);
        Assert.Single(Directory.GetFiles(dir.Path));
    }
}
