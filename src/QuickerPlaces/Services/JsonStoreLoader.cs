using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickerPlaces.Models;

namespace QuickerPlaces.Services;

/// <summary>
/// Loads and classifies one of the smaller JSON stores — activity.json,
/// sessions.json and recent-files.json — the way PlacesService classifies
/// places.json (Phase 1 D6), so each store needn't repeat it:
///
/// - no file is <see cref="StoreLoadOutcome.NotPresent"/>;
/// - a file that can't be read is <see cref="StoreLoadOutcome.Unreadable"/>,
///   never Damaged: a lock or a permission says nothing about its contents;
/// - a schemaVersion above the store's is <see cref="StoreLoadOutcome.WrittenByNewerVersion"/>;
/// - anything read but not understood — not JSON, a duplicate key, no
///   usable schemaVersion, no list where the store keeps its list, or
///   content the serializer rejects — is <see cref="StoreLoadOutcome.Damaged"/>.
///
/// What each store then does about a failure (quarantine, go read-only,
/// what it tells the user) stays with the store. Logs the file path and the
/// outcome only, never contents. UI-free and linked into the test project.
/// </summary>
public static class JsonStoreLoader
{
    /// <summary>Duplicate keys are refused at parse time, as places.json's are, so they classify as Damaged.</summary>
    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowDuplicateProperties = false };

    /// <param name="storage">The store's file.</param>
    /// <param name="currentVersion">The highest schemaVersion this build reads.</param>
    /// <param name="listProperty">The camel-case name of the array the document must hold ("roots", "sessions").</param>
    /// <param name="storeName">How the log names the store ("Activity store").</param>
    /// <param name="options">The store's serializer options.</param>
    public static (T? Document, StoreLoadOutcome Outcome) Load<T>(IPlacesStorage storage, int currentVersion,
        string listProperty, string storeName, JsonSerializerOptions options)
        where T : class
    {
        if (!storage.Exists)
            return (null, StoreLoadOutcome.NotPresent);

        string json;
        try
        {
            json = storage.Read();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error($"{storeName} at {storage.StoreFilePath} could not be opened; it is left untouched this session.", ex);
            return (null, StoreLoadOutcome.Unreadable);
        }

        try
        {
            var root = JsonNode.Parse(json, documentOptions: DocumentOptions) as JsonObject;
            if (root?["schemaVersion"] is not JsonValue versionValue ||
                versionValue.GetValueKind() != JsonValueKind.Number ||
                !versionValue.TryGetValue<int>(out var version) ||
                version < 1)
            {
                DiagnosticLog.Warn($"{storeName} at {storage.StoreFilePath} has no usable schemaVersion; treating as damaged.");
                return (null, StoreLoadOutcome.Damaged);
            }

            if (version > currentVersion)
            {
                DiagnosticLog.Warn($"{storeName} at {storage.StoreFilePath} has schemaVersion {version}, newer than this build's {currentVersion}.");
                return (null, StoreLoadOutcome.WrittenByNewerVersion);
            }

            // The list must be there: a document class's initializer would otherwise read a missing one as empty.
            var document = root[listProperty] is JsonArray ? root.Deserialize<T>(options) : null;
            if (document is null)
            {
                DiagnosticLog.Warn($"{storeName} at {storage.StoreFilePath} holds no usable {listProperty} list; treating as damaged.");
                return (null, StoreLoadOutcome.Damaged);
            }

            return (document, StoreLoadOutcome.Ok);
        }
        catch (Exception ex)
        {
            // Read, but not understood: whatever the exception (a malformed
            // date key or number can surface as more than JsonException),
            // the bytes are intact on disk and quarantine only renames them.
            DiagnosticLog.Error($"{storeName} at {storage.StoreFilePath} is not a valid document.", ex);
            return (null, StoreLoadOutcome.Damaged);
        }
    }
}
