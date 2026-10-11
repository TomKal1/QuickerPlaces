using System.Collections.Generic;

namespace QuickerPlaces.Services.Revit.AddIns;

/// <summary>What an <c>.addin</c> entry declares (its <c>Type</c> attribute).</summary>
public enum AddInKind
{
    Application,
    Command,
    DBApplication,

    /// <summary>A bundle component that names a DLL directly, so there is no manifest to say.</summary>
    Unspecified,
}

/// <summary>Where a manifest was found.</summary>
public enum ManifestSource
{
    /// <summary><c>%AppData%\Autodesk\Revit\Addins\&lt;release&gt;\*.addin</c>.</summary>
    UserAddins,

    /// <summary><c>%ProgramData%\Autodesk\Revit\Addins\&lt;release&gt;\*.addin</c>.</summary>
    MachineAddins,

    /// <summary><c>%AppData%\Autodesk\ApplicationPlugins\*.bundle</c>.</summary>
    UserBundle,

    /// <summary><c>%ProgramData%\Autodesk\ApplicationPlugins\*.bundle</c>.</summary>
    MachineBundle,
}

/// <summary>
/// One add-in as a manifest declares it. <see cref="Name"/> is the
/// <c>Name</c> element of an Application or DBApplication and the <c>Text</c>
/// element of a Command (the other is used when only it is present).
/// <see cref="Assembly"/> is the DLL's full path, with a relative path
/// resolved against the manifest's folder, or null when none is declared.
/// </summary>
public sealed record AddInManifest(
    int Release,
    AddInKind Kind,
    string Name,
    string? Assembly,
    string? AddInId,
    string? VendorId,
    string? FullClassName,
    string ManifestPath,
    ManifestSource Source);

/// <summary>A manifest or entry that was left out, and why. Reading never fails on one bad file.</summary>
public sealed record ManifestSkip(string Path, string Reason);

/// <summary>The manifests a read found, and what it had to leave out.</summary>
public sealed record ManifestReadResult(IReadOnlyList<AddInManifest> Manifests, IReadOnlyList<ManifestSkip> Skipped)
{
    public static ManifestReadResult Empty { get; } = new(System.Array.Empty<AddInManifest>(), System.Array.Empty<ManifestSkip>());
}
