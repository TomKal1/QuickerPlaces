using System;
using QuickerPlaces.Services;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>
/// In-memory IPlacesStorage used by almost every PlacesService test — no
/// real disk I/O, and every failure mode (a write that throws, a read that
/// throws, a missing file) is a property flip rather than an actual fault
/// on a real filesystem.
/// </summary>
public sealed class FakePlacesStorage : IPlacesStorage
{
    public string StoreFilePath { get; set; } = @"C:\fake\places.json";

    /// <summary>The text Read() returns, and what Exists is derived from. Set this to seed a "file already on disk" scenario before constructing a PlacesService.</summary>
    public string? ContentsToReturn { get; set; }

    public bool Exists => ContentsToReturn is not null;

    /// <summary>When set, Read() throws this instead of returning ContentsToReturn.</summary>
    public Exception? ReadThrows { get; set; }

    /// <summary>Makes exactly the next Write() call throw, then clears itself.</summary>
    public bool FailNextWrite { get; set; }

    /// <summary>Makes every Write() call throw, until turned off.</summary>
    public bool FailEveryWrite { get; set; }

    public int WriteCount { get; private set; }

    /// <summary>The contents passed to the most recent successful Write().</summary>
    public string? LastWritten { get; private set; }

    public int QuarantineCount { get; private set; }

    public string? QuarantinedPath { get; private set; }

    public string Read()
    {
        if (ReadThrows is not null)
            throw ReadThrows;

        return ContentsToReturn ?? throw new System.IO.FileNotFoundException("FakePlacesStorage has no contents to read.", StoreFilePath);
    }

    public void Write(string contents)
    {
        if (FailEveryWrite || FailNextWrite)
        {
            FailNextWrite = false;
            throw new System.IO.IOException("Simulated write failure.");
        }

        WriteCount++;
        LastWritten = contents;
        ContentsToReturn = contents;
    }

    public string Quarantine(DateTimeOffset timestamp)
    {
        QuarantineCount++;
        var stamp = timestamp.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        QuarantinedPath = $@"C:\fake\places.corrupt-{stamp}.json";
        ContentsToReturn = null;
        return QuarantinedPath;
    }
}
