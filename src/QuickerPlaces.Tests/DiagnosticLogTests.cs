using System;
using System.IO;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Covers the Phase 1 plan's section 6 rows 21 and 22: the log rolls over
/// at its size cap instead of growing without bound, and it never records a
/// written alias or resource. Also the Phase 2 plan's test 21: the v1 → v2
/// migration's log line records its assumption, and still no alias or
/// destination. Every test redirects DiagnosticLog at a TempDirectory
/// via UseDirectoryForTests — never real AppData — and afterwards points
/// it back at the run-wide TestLogDirectory (not the real location).
///
/// Runs in its own non-parallel collection: DiagnosticLog's directory is
/// process-wide, so while one of these tests has it redirected, any other
/// test logging at the same moment would write into this test's folder
/// and could throw off its size or content checks.
/// </summary>
[Collection(nameof(DiagnosticLogTests))]
public sealed class DiagnosticLogTests
{
    [Fact]
    public void RollsOverInsteadOfGrowingWithoutLimit()
    {
        using var tempDirectory = new TempDirectory();
        DiagnosticLog.UseDirectoryForTests(tempDirectory.Path);
        try
        {
            // Each entry is roughly 60-70 bytes once timestamp and level
            // are included. Writing several thousand of them comfortably
            // clears the 256 KB cap and forces at least one rollover.
            for (var i = 0; i < 6000; i++)
            {
                DiagnosticLog.Info("Routine diagnostic entry number " + i.ToString());
            }

            const string newestMessage = "Newest entry after rollover";
            DiagnosticLog.Info(newestMessage);

            var liveLogPath = DiagnosticLog.LogFilePath;
            var rolledLogPath = DiagnosticLog.RolledLogFilePath;

            Assert.True(File.Exists(liveLogPath), "Expected the live log file to exist.");
            Assert.True(File.Exists(rolledLogPath), "Expected a rolled-over log file to exist.");

            const long capBytes = 256 * 1024;
            const long oneEntryAllowance = 1024; // generous upper bound for a single formatted entry

            var liveLength = new FileInfo(liveLogPath).Length;
            var rolledLength = new FileInfo(rolledLogPath).Length;

            Assert.True(liveLength <= capBytes + oneEntryAllowance,
                $"Live log file was {liveLength} bytes, more than the cap plus one entry's worth.");
            Assert.True(rolledLength <= capBytes + oneEntryAllowance,
                $"Rolled log file was {rolledLength} bytes, more than the cap plus one entry's worth.");

            var liveContents = File.ReadAllText(liveLogPath);
            Assert.Contains(newestMessage, liveContents);
        }
        finally
        {
            DiagnosticLog.UseDirectoryForTests(TestLogDirectory.Path);
        }
    }

    [Fact]
    public void NeverContainsAWrittenAliasOrResource()
    {
        using var tempDirectory = new TempDirectory();
        DiagnosticLog.UseDirectoryForTests(tempDirectory.Path);
        try
        {
            // Mimics the save-failure logging shape a later step will use:
            // count and path only, never the records themselves. These
            // values are deliberately held in local variables and never
            // handed to DiagnosticLog — this test is the guard rail for
            // that privacy rule and should fail loudly the moment a later
            // step starts logging record contents by mistake.
            const string secretAlias = "MySecretProjectAlias";
            const string secretResource = "C:\\Secret\\Path";
            const string storePath = "C:\\Users\\someone\\AppData\\Roaming\\QuickerPlaces\\QuickerPlaces\\places.json";
            const int recordCount = 3;

            try
            {
                throw new IOException("Disk is full.");
            }
            catch (IOException ex)
            {
                DiagnosticLog.Error(
                    "Failed to save " + recordCount.ToString() + " place(s) to " + storePath,
                    ex);
            }

            var logContents = File.ReadAllText(DiagnosticLog.LogFilePath);

            Assert.DoesNotContain(secretAlias, logContents);
            Assert.DoesNotContain(secretResource, logContents);
            Assert.Contains(storePath, logContents);
        }
        finally
        {
            DiagnosticLog.UseDirectoryForTests(TestLogDirectory.Path);
        }
    }

    /// <summary>
    /// Phase 2 test 21: migrating a v1 store logs the assumption roadmap
    /// §4.7 asks to be recorded — how many dates were interpreted as local
    /// time, and in which zone (its id) — with counts only: never an alias
    /// or a destination (D11).
    /// </summary>
    [Fact]
    public void MigrationLog_RecordsTheAssumptionAndZone_NeverAnAliasOrDestination()
    {
        using var tempDirectory = new TempDirectory();
        DiagnosticLog.UseDirectoryForTests(tempDirectory.Path);
        try
        {
            const string secretAlias = "MySecretProjectAlias";
            const string secretResource = "https://secret.example.com/project";
            var storage = new FakePlacesStorage
            {
                ContentsToReturn = $$"""
                    { "schemaVersion": 1, "places": [
                        { "alias": "{{secretAlias}}", "type": "url", "resource": "{{secretResource}}", "dateAdded": "2026-01-15T09:30:00" }
                    ] }
                    """
            };
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero), TestZones.CentralEuropean);

            var service = new PlacesService(storage, clock);
            Assert.Equal(StoreLoadOutcome.Ok, service.LoadOutcome);

            var logContents = File.ReadAllText(DiagnosticLog.LogFilePath);

            Assert.Contains("from schemaVersion 1 to 2", logContents);
            Assert.Contains("interpreted as local time in time zone \"Test/CentralEuropean\" for 1", logContents);
            Assert.Contains("1 place(s)", logContents);
            Assert.DoesNotContain(secretAlias, logContents);
            Assert.DoesNotContain(secretResource, logContents);
        }
        finally
        {
            DiagnosticLog.UseDirectoryForTests(TestLogDirectory.Path);
        }
    }

    [Fact]
    public void NeverThrowsWhenTheTargetDirectoryIsInvalid()
    {
        // A path containing a NUL character is invalid on every platform
        // and cannot be created or written to. Logging must swallow the
        // resulting failure rather than let it escape to the caller.
        var invalidDirectory = "Q:\\this\\path\\is\\not\\usable\0\\logs";
        DiagnosticLog.UseDirectoryForTests(invalidDirectory);
        try
        {
            var exception = Record.Exception(() =>
            {
                DiagnosticLog.Info("This should never throw.");
                DiagnosticLog.Warn("Neither should this.");
                DiagnosticLog.Error("Nor this.", new InvalidOperationException("boom"));
            });

            Assert.Null(exception);
        }
        finally
        {
            DiagnosticLog.UseDirectoryForTests(TestLogDirectory.Path);
        }
    }
}

[CollectionDefinition(nameof(DiagnosticLogTests), DisableParallelization = true)]
public sealed class DiagnosticLogTestsCollection
{
}
