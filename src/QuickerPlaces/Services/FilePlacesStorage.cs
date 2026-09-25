using System;
using System.Globalization;
using System.IO;

namespace QuickerPlaces.Services;

/// <summary>
/// Production IPlacesStorage over a plain file on disk, at
/// %AppData%\QuickerPlaces\QuickerPlaces\places.json by default (see
/// <see cref="ForDefaultLocation"/>).
///
/// Not sealed, and <see cref="CreateTemporaryFilePath"/> is protected
/// virtual purely so a test subclass can override it to record the
/// generated temp file names (proving two consecutive writes use two
/// distinct names) while still returning the real, unique value — nothing
/// about production behaviour depends on this being overridable.
/// </summary>
public class FilePlacesStorage : IPlacesStorage
{
    private readonly string _folder;
    private readonly string _fileName;

    /// <summary>
    /// The store file's name without its extension ("places" for
    /// places.json). The backup and quarantine files are named from this
    /// rather than from a hard-coded "places" literal, so a storage
    /// pointed at a differently-named file doesn't scatter files called
    /// places.* next to it.
    /// </summary>
    private readonly string _baseName;

    public FilePlacesStorage(string folder, string fileName)
    {
        _folder = folder;
        _fileName = fileName;
        _baseName = Path.GetFileNameWithoutExtension(fileName);
        Directory.CreateDirectory(_folder);
        StoreFilePath = Path.Combine(_folder, _fileName);
    }

    /// <summary>Builds a FilePlacesStorage over the real, roaming AppData location this app has always used for places.json.</summary>
    public static FilePlacesStorage ForDefaultLocation()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(root, AppInfo.Publisher, AppInfo.Name);
        return new FilePlacesStorage(folder, "places.json");
    }

    public string StoreFilePath { get; }

    public bool Exists => File.Exists(StoreFilePath);

    public string Read() => File.ReadAllText(StoreFilePath);

    /// <summary>
    /// Generates the temp file path used by <see cref="Write"/>: a GUID
    /// suffix (rather than a fixed ".tmp" name) so a stale temp file left
    /// behind by a previous crash, or a second process writing at the same
    /// time, can never collide with this write's own temp file.
    /// </summary>
    protected virtual string CreateTemporaryFilePath()
        => Path.Combine(_folder, $"{_fileName}.{Guid.NewGuid():N}.tmp");

    public void Write(string contents)
    {
        var tempPath = CreateTemporaryFilePath();

        try
        {
            // Write through a FileStream (rather than File.WriteAllText) so
            // we can call Flush(flushToDisk: true) before closing. The
            // atomic rename/replace below guarantees no process ever
            // observes a half-written file; it does NOT, on its own,
            // guarantee the new bytes reached the platter before a power
            // loss — that's what this explicit flush buys.
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(StoreFilePath))
            {
                // File.Replace is what leaves a recoverable copy of the
                // outgoing file as backupPath — a plain overwriting move
                // does not. backupPath is a sibling file, not a subfolder:
                // the settled answer to plan open question 1, because a
                // sibling is easier to talk a user through recovering over
                // the phone than a subfolder they have to be told to open.
                var backupPath = Path.Combine(_folder, $"{_baseName}.bak.json");
                File.Replace(tempPath, StoreFilePath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, StoreFilePath);
            }
        }
        catch
        {
            // Best-effort cleanup of the temp file, then let the original
            // failure propagate — PlacesService decides what the user
            // sees, this layer only reports that the write did not
            // succeed. Never `throw ex;` here: that would reset the stack
            // trace to this catch block instead of the real failure site.
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // The temp file is orphaned but harmless (a random GUID
                // name, never mistaken for the real store); nothing useful
                // can be done if even deleting it fails.
            }

            throw;
        }
    }

    public string Quarantine(DateTimeOffset timestamp)
    {
        var stamp = timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var candidate = Path.Combine(_folder, $"{_baseName}.corrupt-{stamp}.json");

        // Never overwrite an existing quarantine file (two recoveries in
        // the same second, or a retry after a partial failure) — disambiguate
        // with a numeric suffix instead of clobbering whatever's already
        // there.
        var suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(_folder, $"{_baseName}.corrupt-{stamp}-{suffix}.json");
            suffix++;
        }

        File.Move(StoreFilePath, candidate);
        return candidate;
    }
}
