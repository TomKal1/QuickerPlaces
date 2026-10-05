using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.Services.Sessions;

/// <summary>
/// Reads the folders the OneDrive app syncs for this Windows user, with each
/// library's web address (session sharing plan §4). Two places are read, both
/// under HKEY_CURRENT_USER and both written by OneDrive itself:
///
/// - Software\SyncEngines\Providers\OneDrive\*: one key per synced library or
///   shortcut, with MountPoint (the folder here), UrlNamespace (the library's
///   address) and LibraryType ("teamsite", "mysite" or "personal").
/// - Software\Microsoft\OneDrive\Accounts\*: each signed-in account's own
///   OneDrive folder, used only when the first place doesn't list it.
///
/// Neither is a documented API, so anything missing or unexpected is skipped
/// and the rest still used; reading never throws. Only counts are logged,
/// never a path or an address. Not verified against every OneDrive version:
/// see the plan's §9 checklist.
///
/// App-only (registry).
/// </summary>
public sealed class WindowsCloudSyncRoots : ICloudSyncRoots
{
    private const string ProvidersKey = @"Software\SyncEngines\Providers\OneDrive";
    private const string AccountsKey = @"Software\Microsoft\OneDrive\Accounts";

    public IReadOnlyList<CloudSyncRoot> Read()
    {
        var roots = new List<CloudSyncRoot>();
        try
        {
            ReadProviders(roots);
            ReadAccounts(roots);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Reading OneDrive's synced folders failed ({ex.GetType().Name}); {roots.Count} found before it.");
        }

        DiagnosticLog.Info($"Found {roots.Count} OneDrive synced folder(s) for session sharing.");
        return roots;
    }

    private static void ReadProviders(List<CloudSyncRoot> roots)
    {
        using var providers = Registry.CurrentUser.OpenSubKey(ProvidersKey);
        if (providers is null)
            return;

        foreach (var name in providers.GetSubKeyNames())
        {
            try
            {
                using var key = providers.OpenSubKey(name);
                var mountPoint = RootPathMatcher.Normalize(key?.GetValue("MountPoint") as string);
                var url = key?.GetValue("UrlNamespace") as string;
                if (mountPoint is null || CloudPaths.Canonical(url) is null)
                    continue;

                var kind = (key!.GetValue("LibraryType") as string)?.Trim().ToLowerInvariant() switch
                {
                    "teamsite" => CloudLibraryKind.Library,
                    "mysite" => CloudLibraryKind.Personal,
                    "personal" => CloudLibraryKind.ConsumerPersonal,
                    _ => CloudPaths.KindFromUrl(url!),
                };
                Add(roots, new CloudSyncRoot(mountPoint, url!, kind));
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn($"Skipped one OneDrive synced folder ({ex.GetType().Name}).");
            }
        }
    }

    private static void ReadAccounts(List<CloudSyncRoot> roots)
    {
        using var accounts = Registry.CurrentUser.OpenSubKey(AccountsKey);
        if (accounts is null)
            return;

        foreach (var name in accounts.GetSubKeyNames())
        {
            try
            {
                using var key = accounts.OpenSubKey(name);
                var folder = RootPathMatcher.Normalize(key?.GetValue("UserFolder") as string);
                if (folder is null)
                    continue;

                if (name.StartsWith("Business", StringComparison.OrdinalIgnoreCase))
                {
                    // ".../personal/alice_contoso_com/_api" names the account's site; its files are in "Documents".
                    var endpoint = key!.GetValue("ServiceEndpointUri") as string;
                    var site = CloudPaths.Canonical(endpoint);
                    if (site is not null && site.EndsWith("/_api", StringComparison.OrdinalIgnoreCase))
                        Add(roots, new CloudSyncRoot(folder, site[..^"/_api".Length] + "/Documents", CloudLibraryKind.Personal));
                }
                else if (string.Equals(name, "Personal", StringComparison.OrdinalIgnoreCase) && key!.GetValue("cid") is string cid && cid.Length > 0)
                {
                    Add(roots, new CloudSyncRoot(folder, "https://d.docs.live.net/" + Uri.EscapeDataString(cid.ToLowerInvariant()), CloudLibraryKind.ConsumerPersonal));
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn($"Skipped one OneDrive account ({ex.GetType().Name}).");
            }
        }
    }

    /// <summary>Adds <paramref name="root"/> unless a folder already listed is the same one.</summary>
    private static void Add(List<CloudSyncRoot> roots, CloudSyncRoot root)
    {
        if (roots.Any(r => string.Equals(r.LocalPath, root.LocalPath, StringComparison.OrdinalIgnoreCase)))
            return;

        roots.Add(root);
    }
}
