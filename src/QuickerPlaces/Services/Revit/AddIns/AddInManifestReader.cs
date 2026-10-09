using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace QuickerPlaces.Services.Revit.AddIns;

/// <summary>
/// Reads Revit add-in manifests, read-only: it never writes, renames, moves or
/// deletes anything, and never throws on a bad file (the file is skipped with
/// a reason). XML is read with DTD processing prohibited and no resolver, so
/// a hostile manifest can't pull in files or expand entities.
///
/// What is supported:
/// - <c>.addin</c> files: <c>RevitAddIns/AddIn</c> elements with a
///   <c>Type</c> of Application, Command or DBApplication, and the child
///   elements <c>Name</c> (or <c>Text</c> for a Command), <c>Assembly</c>,
///   <c>AddInId</c>, <c>VendorId</c> and <c>FullClassName</c>. A relative
///   <c>Assembly</c> is resolved against the manifest's folder.
/// - Autodesk application bundles, <c>PackageContents.xml</c> under
///   <c>ApplicationPlugins\*.bundle</c>: each <c>Components</c> element's
///   <c>RuntimeRequirements</c> (<c>Platform</c> Revit or absent;
///   <c>SeriesMin</c> and <c>SeriesMax</c> such as "R2025", either optional)
///   decides whether its <c>ComponentEntry</c> children apply to a release.
///   A <c>ModuleName</c> ending in <c>.addin</c> is read as a manifest; any
///   other (a DLL) becomes an entry named by <c>AppName</c> with no id.
///   <c>ModuleName</c> is relative to the bundle folder. Not supported: other
///   platforms, <c>LoadOnCommandInvocation</c> details, and the per-OS
///   attributes, which are ignored.
/// </summary>
public static class AddInManifestReader
{
    private static readonly Regex SeriesYear = new(@"(\d{4})", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        CloseInput = true,
    };

    public static ManifestReadResult ReadAddinFile(string path, int release, ManifestSource source)
    {
        var skipped = new List<ManifestSkip>();
        var manifests = new List<AddInManifest>();
        if (!TryLoad(path, skipped, out var document))
            return new ManifestReadResult(manifests, skipped);

        var root = document.Root;
        if (root is null || root.Name.LocalName != "RevitAddIns")
        {
            skipped.Add(new ManifestSkip(path, "Not a Revit add-in manifest (no RevitAddIns element)."));
            return new ManifestReadResult(manifests, skipped);
        }

        var folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        foreach (var addIn in root.Elements().Where(e => e.Name.LocalName == "AddIn"))
        {
            var typeText = (string?)addIn.Attribute("Type");
            if (!Enum.TryParse<AddInKind>(typeText, ignoreCase: false, out var kind) || kind == AddInKind.Unspecified)
            {
                skipped.Add(new ManifestSkip(path, $"An AddIn entry has Type \"{typeText}\", which isn't Application, Command or DBApplication."));
                continue;
            }

            var name = kind == AddInKind.Command
                ? Text(addIn, "Text") ?? Text(addIn, "Name")
                : Text(addIn, "Name") ?? Text(addIn, "Text");
            if (name is null)
            {
                skipped.Add(new ManifestSkip(path, $"A {kind} entry has no name."));
                continue;
            }

            var assembly = Text(addIn, "Assembly");
            manifests.Add(new AddInManifest(
                release, kind, name,
                assembly is null ? null : Resolve(folder, assembly),
                Text(addIn, "AddInId"), Text(addIn, "VendorId"), Text(addIn, "FullClassName"),
                path, source));
        }

        return new ManifestReadResult(manifests, skipped);
    }

    /// <summary>The add-ins a bundle's <c>PackageContents.xml</c> declares for <paramref name="release"/>.</summary>
    public static ManifestReadResult ReadBundle(string packageContentsPath, int release, ManifestSource source)
    {
        var skipped = new List<ManifestSkip>();
        var manifests = new List<AddInManifest>();
        if (!TryLoad(packageContentsPath, skipped, out var document))
            return new ManifestReadResult(manifests, skipped);

        var root = document.Root;
        if (root is null || root.Name.LocalName != "ApplicationPackage")
        {
            skipped.Add(new ManifestSkip(packageContentsPath, "Not a PackageContents.xml (no ApplicationPackage element)."));
            return new ManifestReadResult(manifests, skipped);
        }

        var bundleFolder = Path.GetDirectoryName(Path.GetFullPath(packageContentsPath)) ?? string.Empty;
        foreach (var components in root.Elements().Where(e => e.Name.LocalName == "Components"))
        {
            if (!AppliesTo(components, release))
                continue;

            foreach (var entry in components.Elements().Where(e => e.Name.LocalName == "ComponentEntry"))
            {
                var module = (string?)entry.Attribute("ModuleName");
                if (string.IsNullOrWhiteSpace(module))
                {
                    skipped.Add(new ManifestSkip(packageContentsPath, "A ComponentEntry has no ModuleName."));
                    continue;
                }

                var modulePath = Resolve(bundleFolder, module);
                if (modulePath.EndsWith(".addin", StringComparison.OrdinalIgnoreCase))
                {
                    var inner = ReadAddinFile(modulePath, release, source);
                    manifests.AddRange(inner.Manifests);
                    skipped.AddRange(inner.Skipped);
                }
                else
                {
                    var name = (string?)entry.Attribute("AppName");
                    if (string.IsNullOrWhiteSpace(name))
                        name = Path.GetFileNameWithoutExtension(modulePath);
                    manifests.Add(new AddInManifest(release, AddInKind.Unspecified, name, modulePath, null, null, null, packageContentsPath, source));
                }
            }
        }

        return new ManifestReadResult(manifests, skipped);
    }

    /// <summary>True when the <c>Components</c> element's runtime requirements include <paramref name="release"/> on Revit.</summary>
    private static bool AppliesTo(XElement components, int release)
    {
        var requirements = components.Elements().FirstOrDefault(e => e.Name.LocalName == "RuntimeRequirements");
        if (requirements is null)
            return true;

        var platform = (string?)requirements.Attribute("Platform");
        if (!string.IsNullOrEmpty(platform) && !platform.Equals("Revit", StringComparison.OrdinalIgnoreCase))
            return false;

        var min = Year((string?)requirements.Attribute("SeriesMin"));
        var max = Year((string?)requirements.Attribute("SeriesMax"));
        return (min is null || release >= min) && (max is null || release <= max);
    }

    private static int? Year(string? series)
    {
        if (string.IsNullOrWhiteSpace(series))
            return null;
        try
        {
            var match = SeriesYear.Match(series);
            return match.Success ? int.Parse(match.Value, System.Globalization.CultureInfo.InvariantCulture) : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static string? Text(XElement parent, string name)
    {
        var value = parent.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// A rooted path (a drive, or a share) stays as written; a relative one is
    /// joined to <paramref name="folder"/> and tidied. Either slash is accepted.
    /// </summary>
    internal static string Resolve(string folder, string path)
    {
        if (IsRooted(path))
            return path;
        var relative = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(folder, relative));
    }

    /// <summary>Rooted by Windows rules even where this runs elsewhere, so a recorded "C:\Tools\a.dll" is never made relative.</summary>
    internal static bool IsRooted(string path)
        => Path.IsPathRooted(path) || Regex.IsMatch(path, @"^([A-Za-z]:[\\/]|\\\\)", RegexOptions.CultureInvariant);

    private static bool TryLoad(string path, List<ManifestSkip> skipped, out XDocument document)
    {
        document = new XDocument();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = XmlReader.Create(stream, Settings);
            document = XDocument.Load(reader);
            return true;
        }
        catch (XmlException ex)
        {
            skipped.Add(new ManifestSkip(path, "Not valid XML (or it has a DTD, which isn't read): " + ex.Message));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            skipped.Add(new ManifestSkip(path, "Can't be read: " + ex.Message));
        }

        return false;
    }
}
