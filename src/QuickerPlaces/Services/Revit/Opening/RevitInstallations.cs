using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace QuickerPlaces.Services.Revit.Opening;

/// <summary>An installed Revit release and the <c>Revit.exe</c> that runs it.</summary>
public sealed record RevitInstall(int Release, string ExePath)
{
    public string ReleaseText => Release.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>One entry of the Windows uninstall list: only what finding Revit needs.</summary>
public sealed record UninstallEntry(string? DisplayName, string? InstallLocation);

/// <summary>What finding installs reads from the machine, so the selection logic is tested with fakes.</summary>
public interface IRevitInstallSource
{
    /// <summary>HKLM Uninstall entries in the 64-bit registry view; none off Windows.</summary>
    IReadOnlyList<UninstallEntry> ReadUninstallEntries();

    /// <summary>%ProgramFiles%, or null when there is none.</summary>
    string? ProgramFilesFolder { get; }

    /// <summary>Names (not paths) of the sub-folders of <paramref name="folder"/>; none when it can't be listed.</summary>
    IReadOnlyList<string> SubfolderNames(string folder);

    bool FileExists(string path);
}

/// <summary>
/// Finds installed Revit 2022+ releases, by the approach Model Delta's
/// BatchRvtInstallation and RBP's RevitVersion take (not their code):
/// candidates come from the uninstall list (an entry named "Revit 2025" or
/// "Autodesk Revit 2025" with an InstallLocation) and from the default
/// <c>%ProgramFiles%\Autodesk\Revit &lt;year&gt;</c> folders; only those whose
/// <c>Revit.exe</c> exists are kept. The registry's location wins over the
/// default one for the same release. Revit LT and older releases are ignored.
/// UI-free and linked into the test project and the qp CLI.
/// </summary>
public static class RevitInstallFinder
{
    public const string ExeName = "Revit.exe";

    private static readonly Regex DisplayNamePattern = new(@"^\s*(?:Autodesk\s+)?Revit\s+(\d{4})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex FolderPattern = new(@"^Revit\s+(\d{4})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Installed releases, oldest first.</summary>
    public static IReadOnlyList<RevitInstall> Find(IRevitInstallSource source)
    {
        var candidates = new List<(int Release, string Exe)>();

        foreach (var entry in source.ReadUninstallEntries())
        {
            if (entry.DisplayName is null || string.IsNullOrWhiteSpace(entry.InstallLocation))
                continue;
            if (DisplayNamePattern.Match(entry.DisplayName) is { Success: true } match)
                candidates.Add((int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                    Path.Combine(entry.InstallLocation.Trim().Trim('"'), ExeName)));
        }

        if (!string.IsNullOrEmpty(source.ProgramFilesFolder))
        {
            var autodesk = Path.Combine(source.ProgramFilesFolder, "Autodesk");
            foreach (var name in source.SubfolderNames(autodesk))
            {
                if (FolderPattern.Match(name) is { Success: true } match)
                    candidates.Add((int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                        Path.Combine(autodesk, name, ExeName)));
            }
        }

        // Candidates are in preference order, so the first existing exe of a release wins.
        return candidates
            .Where(c => c.Release >= RevitFileInfoReader.OldestSupportedRelease && source.FileExists(c.Exe))
            .GroupBy(c => c.Release)
            .Select(g => new RevitInstall(g.Key, g.First().Exe))
            .OrderBy(i => i.Release)
            .ToList();
    }
}

/// <summary>The machine's own source: registry and file system on Windows, nothing elsewhere.</summary>
public sealed class SystemRevitInstallSource : IRevitInstallSource
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public string? ProgramFilesFolder
    {
        get
        {
            if (!OperatingSystem.IsWindows())
                return null;
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return string.IsNullOrEmpty(folder) ? null : folder;
        }
    }

    public IReadOnlyList<UninstallEntry> ReadUninstallEntries() =>
        OperatingSystem.IsWindows() ? ReadRegistry() : [];

    public IReadOnlyList<string> SubfolderNames(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder).Select(p => Path.GetFileName(p)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public bool FileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<UninstallEntry> ReadRegistry()
    {
        var entries = new List<UninstallEntry>();
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var uninstall = hklm.OpenSubKey(UninstallKey);
            if (uninstall is null)
                return entries;

            foreach (var name in uninstall.GetSubKeyNames())
            {
                try
                {
                    using var key = uninstall.OpenSubKey(name);
                    if (key?.GetValue("DisplayName") is string displayName)
                        entries.Add(new UninstallEntry(displayName, key.GetValue("InstallLocation") as string));
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    // One unreadable key doesn't hide the others.
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // No registry access: only the default folders are used.
        }
        return entries;
    }
}
