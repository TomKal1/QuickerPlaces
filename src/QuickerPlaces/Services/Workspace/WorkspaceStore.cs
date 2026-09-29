using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using QuickerPlaces.Models;
using QuickerPlaces.Models.Workspace;

namespace QuickerPlaces.Services.Workspace;

/// <summary>
/// Owns workspace-layouts.json (configurable canvas plan §4): the user's
/// named layouts, working arrangements, the active layout and the startup
/// choice, in %LocalAppData% beside settings.json. Built over the same
/// IPlacesStorage seam as the other stores, so FilePlacesStorage gives it the
/// temp-file-and-replace write, a workspace-layouts.bak.json backup and a
/// workspace-layouts.corrupt-*.json quarantine.
///
/// Unlike settings.json, a layout file is never silently reset (plan §4):
///
/// - An unreadable file, or one a newer version wrote, is left untouched
///   and <see cref="CanWrite"/> is false for the session. The built-in
///   layouts still work, in memory.
/// - A damaged file is set aside (never deleted), and when the backup from
///   the last good write can be read, <see cref="HasBackup"/> offers it; the
///   user chooses whether to <see cref="RestoreBackup"/>. If the damaged file
///   cannot be set aside, nothing is written.
/// - A readable file with bad values is repaired in memory, with each kind
///   of repair listed in <see cref="Repairs"/>; the file is only rewritten by
///   the next real save.
/// - A failed write is returned to be shown, never swallowed, and the
///   document stays in memory for <see cref="RetrySave"/>.
///
/// Used from the UI thread only. UI-free and linked into the test project.
/// </summary>
public sealed class WorkspaceStore
{
    /// <summary>The workspace-layouts.json schema version this build writes and reads.</summary>
    public const int CurrentSchemaVersion = 1;

    public const string FileName = "workspace-layouts.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IPlacesStorage _storage;
    private readonly IPlacesStorage? _backup;
    private readonly TimeProvider _time;
    private WorkspaceDocument? _pending;

    /// <param name="storage">workspace-layouts.json.</param>
    /// <param name="backup">The backup the storage keeps of the last good file, read only to offer it after damage. Null when there is none.</param>
    public WorkspaceStore(IPlacesStorage storage, IPlacesStorage? backup, TimeProvider timeProvider)
    {
        _storage = storage;
        _backup = backup;
        _time = timeProvider;

        var (document, outcome) = JsonStoreLoader.Load<WorkspaceDocument>(_storage, CurrentSchemaVersion, "presets", "Workspace layouts", JsonOptions);
        LoadOutcome = outcome;
        Document = document ?? new WorkspaceDocument();
        Repairs = WorkspaceValidation.Repair(Document);
        if (Repairs.Count > 0)
            DiagnosticLog.Warn($"Repaired {Repairs.Count} kind(s) of problem in {_storage.StoreFilePath}.");

        switch (outcome)
        {
            case StoreLoadOutcome.Ok:
            case StoreLoadOutcome.NotPresent:
                CanWrite = true;
                if (Repairs.Count > 0)
                    Notice = "Some of your saved layouts had problems and were repaired: " + string.Join(" ", Repairs);
                break;

            case StoreLoadOutcome.Damaged:
                try
                {
                    var kept = _storage.Quarantine(_time.GetLocalNow());
                    DiagnosticLog.Warn($"Quarantined damaged workspace layouts to {kept}.");
                    CanWrite = true;
                    HasBackup = ReadBackup() is not null;
                    Notice = HasBackup
                        ? $"Your saved layouts couldn't be read. The damaged file was kept as \"{kept}\", and the copy from the last successful save can be restored."
                        : $"Your saved layouts couldn't be read, and no earlier copy could be. The damaged file was kept as \"{kept}\"; the built-in layouts are shown.";
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Error($"Failed to quarantine damaged workspace layouts at {_storage.StoreFilePath}; layouts are read-only this session.", ex);
                    Notice = $"Your saved layouts couldn't be read, and the damaged file couldn't be set aside, so layout changes can't be saved. The file is \"{_storage.StoreFilePath}\".";
                }

                break;

            case StoreLoadOutcome.Unreadable:
                Notice = $"Your saved layouts couldn't be opened, so layout changes can't be saved until QuickerPlaces restarts. The built-in layouts are shown. The file is \"{_storage.StoreFilePath}\".";
                break;

            case StoreLoadOutcome.WrittenByNewerVersion:
                Notice = "Your saved layouts were written by a newer version of QuickerPlaces, so they can't be changed here. The built-in layouts are shown; update QuickerPlaces to use yours.";
                break;
        }
    }

    /// <summary>Builds the store over workspace-layouts.json in %LocalAppData%, beside settings.json.</summary>
    public static WorkspaceStore CreateDefault() => CreateIn(AppDataFolders.Local, TimeProvider.System);

    /// <summary>Builds the store over workspace-layouts.json in <paramref name="folder"/>, with its backup file.</summary>
    public static WorkspaceStore CreateIn(string folder, TimeProvider timeProvider)
        => new(new FilePlacesStorage(folder, FileName),
            new FilePlacesStorage(folder, Path.GetFileNameWithoutExtension(FileName) + ".bak.json"),
            timeProvider);

    /// <summary>What happened when the file was loaded.</summary>
    public StoreLoadOutcome LoadOutcome { get; }

    /// <summary>
    /// The document as loaded and repaired (empty when there was none or it
    /// could not be used). The layout service works on a copy.
    /// </summary>
    public WorkspaceDocument Document { get; private set; }

    /// <summary>One line per kind of repair made while loading. Empty when none.</summary>
    public IReadOnlyList<string> Repairs { get; private set; }

    /// <summary>False when the file could not be opened, a newer version wrote it, or it was damaged and could not be set aside.</summary>
    public bool CanWrite { get; }

    /// <summary>True when the file was damaged and the backup of the last good save can be read.</summary>
    public bool HasBackup { get; private set; }

    /// <summary>The one line to show about loading, or null when there is nothing to say.</summary>
    public string? Notice { get; private set; }

    public string StoreFilePath => _storage.StoreFilePath;

    /// <summary>True while a document handed to <see cref="Save"/> has not reached disk.</summary>
    public bool HasUnsavedChanges => _pending is not null;

    /// <summary>
    /// Writes <paramref name="document"/> (a copy is kept, so later changes
    /// to it are not saved by accident). Refused when <see cref="CanWrite"/>
    /// is false; the document is still kept, so nothing the user did is lost
    /// from memory.
    /// </summary>
    public PersistenceResult Save(WorkspaceDocument document)
    {
        _pending = document.Clone();
        _pending.SchemaVersion = CurrentSchemaVersion;
        return WritePending();
    }

    /// <summary>Rewrites the file if a save is still waiting to reach disk.</summary>
    public PersistenceResult RetrySave() => _pending is null ? PersistenceResult.Ok() : WritePending();

    /// <summary>
    /// Replaces the current layouts with the backup of the last good save,
    /// repaired as a load would be, and writes it back as the store file.
    /// Only offered after damage (<see cref="HasBackup"/>). Returns the
    /// restored document, or null with a reason when it can't.
    /// </summary>
    public WorkspaceDocument? RestoreBackup(out PersistenceResult persistence)
    {
        persistence = PersistenceResult.Ok();
        var backup = HasBackup ? ReadBackup() : null;
        if (backup is null)
        {
            persistence = PersistenceResult.Fail("The earlier copy of your layouts couldn't be read.");
            return null;
        }

        Repairs = WorkspaceValidation.Repair(backup);
        Document = backup;
        HasBackup = false;
        Notice = null;
        persistence = Save(backup);
        DiagnosticLog.Info($"Restored workspace layouts from the backup ({backup.Presets.Count} layout(s)).");
        return backup.Clone();
    }

    private WorkspaceDocument? ReadBackup()
    {
        if (_backup is null)
            return null;

        var (document, outcome) = JsonStoreLoader.Load<WorkspaceDocument>(_backup, CurrentSchemaVersion, "presets", "Workspace layouts backup", JsonOptions);
        return outcome == StoreLoadOutcome.Ok ? document : null;
    }

    private PersistenceResult WritePending()
    {
        if (!CanWrite)
            return PersistenceResult.Fail(Notice ?? "Layout changes can't be saved this session.");

        try
        {
            _storage.Write(JsonSerializer.Serialize(_pending, JsonOptions));
            _pending = null;
            return PersistenceResult.Ok();
        }
        catch (Exception ex)
        {
            // Counts and the file path only, never a layout name.
            DiagnosticLog.Error($"Failed to save {_pending!.Presets.Count} layout(s) to {_storage.StoreFilePath}; kept in memory for a retry.", ex);
            return PersistenceResult.Fail($"Couldn't save your layouts to \"{_storage.StoreFilePath}\". {ex.Message}");
        }
    }
}
