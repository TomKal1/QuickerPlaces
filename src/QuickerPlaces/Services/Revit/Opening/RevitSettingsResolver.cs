using QuickerPlaces.Models;
using QuickerPlaces.Services.Revit.Handlers;

namespace QuickerPlaces.Services.Revit.Opening;

/// <summary>A release's Revit settings with the defaults filled in.</summary>
public sealed record EffectiveRevitSettings(string Release, string? HandlerId, string LocalFolder);

/// <summary>Reads <see cref="AppSettings.RevitReleases"/> with defaults. UI-free.</summary>
public static class RevitSettingsResolver
{
    /// <summary>C:\REVIT_LOCAL2025: the default local folder (Thomas, 2026-10-08).</summary>
    public static string DefaultLocalFolder(string release) => @"C:\REVIT_LOCAL" + release;

    /// <summary>What a new local does to an old one at its path: the setting when known, else overwrite (Revit's usual choice).</summary>
    public static string ExistingLocal(AppSettings settings) =>
        HandlerProtocol.ExistingLocal.IsKnown(settings.RevitExistingLocal?.Trim()) ? settings.RevitExistingLocal!.Trim() : HandlerProtocol.ExistingLocal.Overwrite;

    public static EffectiveRevitSettings For(AppSettings settings, string release)
    {
        RevitReleaseSettings? entry = null;
        settings.RevitReleases?.TryGetValue(release, out entry);

        var handler = string.IsNullOrWhiteSpace(entry?.HandlerId) ? null : entry.HandlerId.Trim();
        var folder = string.IsNullOrWhiteSpace(entry?.LocalFolder) ? DefaultLocalFolder(release) : entry.LocalFolder.Trim();
        return new EffectiveRevitSettings(release, handler, folder);
    }
}
