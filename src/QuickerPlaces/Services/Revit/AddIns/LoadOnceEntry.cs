using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace QuickerPlaces.Services.Revit.AddIns;

/// <summary>
/// One add-in the user allowed Load Once for: the release, the add-in's
/// identity as its manifest gave it, the DLL it was and that DLL's SHA-256
/// when the user said yes. The next piece of work stores the list in
/// QuickerPlaces' settings; <see cref="Json"/> is how it serialises.
/// </summary>
public sealed class LoadOnceEntry
{
    public int Release { get; set; }

    /// <summary>The manifest's AddInId; null for a bundle entry that names a DLL directly.</summary>
    public string? AddInId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string DllPath { get; set; } = string.Empty;

    /// <summary>Lower-case hex SHA-256 of the DLL when it was allowed.</summary>
    public string DllSha256 { get; set; } = string.Empty;

    public DateTimeOffset AllowedUtc { get; set; }
}

/// <summary>The list's JSON: System.Text.Json, camelCase, like the app's stores.</summary>
public static class LoadOnceJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string Serialise(IEnumerable<LoadOnceEntry> entries) => JsonSerializer.Serialize(entries.ToList(), Options);

    public static List<LoadOnceEntry> Deserialise(string json) => JsonSerializer.Deserialize<List<LoadOnceEntry>>(json, Options) ?? new List<LoadOnceEntry>();
}

/// <summary>An entry to ask the user about, or why there is none.</summary>
public sealed record LoadOnceOffer(LoadOnceEntry? Entry, string Reason);

/// <summary>
/// After a launch in which the user answered a security prompt: the entry to
/// offer ("Allow Load Once for DuctExporter next time?"). Exactly one
/// manifest of that release must match the prompt, or nothing is offered.
/// </summary>
public static class LoadOnceOffers
{
    /// <param name="release">The Revit release that showed the prompt.</param>
    /// <param name="name">The add-in name the prompt showed.</param>
    /// <param name="dllPath">The DLL path the prompt showed, or null when that release shows only the name (then the name alone must be unique).</param>
    /// <param name="manifests">The release's manifests (<see cref="AddInManifestFinder"/>).</param>
    /// <param name="hashOf">Hashes a DLL; <see cref="DllFingerprint.Sha256"/> when null.</param>
    public static LoadOnceOffer Offer(int release, string name, string? dllPath, IReadOnlyList<AddInManifest> manifests, DateTimeOffset now, Func<string, string?>? hashOf = null)
    {
        var matches = Matching(release, name, dllPath, manifests);
        if (matches.Count == 0)
            return new LoadOnceOffer(null, $"No manifest for Revit {release} names an add-in \"{name}\"" + (dllPath is null ? "." : $" with the DLL {dllPath}."));
        if (matches.Count > 1)
            return new LoadOnceOffer(null, $"{matches.Count} different add-ins for Revit {release} match \"{name}\", so none is offered.");

        var match = matches[0];
        if (string.IsNullOrEmpty(match.Assembly))
            return new LoadOnceOffer(null, $"The manifest for \"{name}\" doesn't name a DLL.");

        var hash = (hashOf ?? DllFingerprint.Sha256)(match.Assembly);
        if (hash is null)
            return new LoadOnceOffer(null, $"The DLL {match.Assembly} can't be read, so it can't be identified.");

        return new LoadOnceOffer(new LoadOnceEntry
        {
            Release = release,
            AddInId = match.AddInId,
            Name = match.Name,
            DllPath = match.Assembly,
            DllSha256 = hash,
            AllowedUtc = now.ToUniversalTime(),
        }, "One manifest matches.");
    }

    /// <summary>
    /// The distinct add-ins of <paramref name="release"/> called <paramref name="name"/> (and, when given, built from
    /// <paramref name="dllPath"/>). The same add-in listed twice (the same DLL and id, say in a user and an all-users
    /// folder) counts once.
    /// </summary>
    internal static List<AddInManifest> Matching(int release, string name, string? dllPath, IReadOnlyList<AddInManifest> manifests)
        => manifests
            .Where(m => m.Release == release && SameName(m.Name, name) && (dllPath is null || SamePath(m.Assembly, dllPath)))
            .GroupBy(m => (NormalPath(m.Assembly), (m.AddInId ?? string.Empty).ToUpperInvariant()))
            .Select(g => g.First())
            .ToList();

    internal static bool SameName(string? a, string? b)
        => a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    internal static bool SamePath(string? a, string? b)
        => a is not null && b is not null && NormalPath(a) == NormalPath(b);

    private static string NormalPath(string? path)
        => (path ?? string.Empty).Replace('/', '\\').Trim().ToUpperInvariant();
}
