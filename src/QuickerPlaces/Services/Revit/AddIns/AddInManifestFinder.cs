using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuickerPlaces.Services.Revit.AddIns;

/// <summary>
/// The folders Revit reads manifests from, as roots that tests can point at
/// temporary folders. <c>Addins</c> roots hold a folder per release
/// (<c>Revit\Addins\2025</c>); <c>Autodesk</c> roots hold
/// <c>ApplicationPlugins\*.bundle</c>. Null skips a root.
/// </summary>
public sealed record AddInLocations(string? UserAddinsRoot, string? MachineAddinsRoot, string? UserAutodeskRoot, string? MachineAutodeskRoot)
{
    /// <summary>%AppData%\Autodesk and %ProgramData%\Autodesk on this machine.</summary>
    public static AddInLocations ForThisMachine()
    {
        var user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk");
        var machine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk");
        return new AddInLocations(Path.Combine(user, "Revit", "Addins"), Path.Combine(machine, "Revit", "Addins"), user, machine);
    }
}

/// <summary>
/// Finds the manifests Revit would read for a release, in the four places
/// (roadmap §4.21, Load Once list): the per-user and all-users
/// <c>Addins\&lt;release&gt;</c> folders, and the per-user and all-users
/// bundles. Read-only. A folder that is missing or unreadable is simply empty.
/// </summary>
public static class AddInManifestFinder
{
    public static ManifestReadResult Find(int release, AddInLocations locations)
    {
        var manifests = new List<AddInManifest>();
        var skipped = new List<ManifestSkip>();

        void Add(ManifestReadResult result)
        {
            manifests.AddRange(result.Manifests);
            skipped.AddRange(result.Skipped);
        }

        void AddinFolder(string? root, ManifestSource source)
        {
            if (string.IsNullOrEmpty(root))
                return;
            foreach (var file in Files(Path.Combine(root, release.ToString(System.Globalization.CultureInfo.InvariantCulture)), "*.addin", skipped))
                Add(AddInManifestReader.ReadAddinFile(file, release, source));
        }

        void Bundles(string? autodeskRoot, ManifestSource source)
        {
            if (string.IsNullOrEmpty(autodeskRoot))
                return;
            var plugins = Path.Combine(autodeskRoot, "ApplicationPlugins");
            foreach (var bundle in Directories(plugins, "*.bundle", skipped))
            {
                var contents = Path.Combine(bundle, "PackageContents.xml");
                if (File.Exists(contents))
                    Add(AddInManifestReader.ReadBundle(contents, release, source));
            }
        }

        AddinFolder(locations.UserAddinsRoot, ManifestSource.UserAddins);
        AddinFolder(locations.MachineAddinsRoot, ManifestSource.MachineAddins);
        Bundles(locations.UserAutodeskRoot, ManifestSource.UserBundle);
        Bundles(locations.MachineAutodeskRoot, ManifestSource.MachineBundle);
        return new ManifestReadResult(manifests, skipped);
    }

    private static IEnumerable<string> Files(string folder, string pattern, List<ManifestSkip> skipped)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetFiles(folder, pattern).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList() : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            skipped.Add(new ManifestSkip(folder, "Can't be listed: " + ex.Message));
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> Directories(string folder, string pattern, List<ManifestSkip> skipped)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetDirectories(folder, pattern).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList() : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            skipped.Add(new ManifestSkip(folder, "Can't be listed: " + ex.Message));
            return Array.Empty<string>();
        }
    }
}
