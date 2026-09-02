using System;

namespace QuickerPlaces.Services;

/// <summary>
/// Everything PlacesService needs from the filesystem, pulled out from
/// behind direct File.* calls so the failure paths this phase is building
/// towards — a write that fails partway, a file another process has
/// locked, a store written by a newer version — can be exercised by a test
/// without touching a real disk. At this step the production
/// implementation (FilePlacesStorage) behaves identically to the File.*
/// calls it replaces; only the seam is new.
/// </summary>
public interface IPlacesStorage
{
    /// <summary>Full path to the store file, for "Show Data Folder" and log messages.</summary>
    string StoreFilePath { get; }

    /// <summary>True if a store file is present. False means first run, not failure.</summary>
    bool Exists { get; }

    /// <summary>
    /// Reads the store file's full text. Throws if the file cannot be
    /// opened or read — that failure is unreadability, not damage, and
    /// PlacesService is responsible for classifying it accordingly (D6:
    /// an IOException/UnauthorizedAccessException here says nothing about
    /// whether the file's contents are intact). Parsing happens above this
    /// interface, so a damaged document never surfaces as a storage
    /// exception in the first place.
    /// </summary>
    string Read();

    /// <summary>
    /// Replaces the store file's contents durably: write to a uniquely
    /// named temporary file in the same directory, flush it to disk, then
    /// replace the live file in one filesystem operation, keeping a backup
    /// copy of whatever was there before. Throws if any step fails; the
    /// caller decides what the user sees.
    /// </summary>
    void Write(string contents);

    /// <summary>
    /// Renames the current store file out of the way as
    /// places.corrupt-yyyyMMdd-HHmmss.json (derived from
    /// <paramref name="timestamp"/>) and returns the new full path. Never
    /// deletes the file, and never overwrites an existing quarantine file
    /// with the same name — disambiguates instead.
    /// </summary>
    string Quarantine(DateTimeOffset timestamp);
}
