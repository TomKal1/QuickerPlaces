using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using QuickerPlaces.Services.Revit.Handlers;

namespace QuickerPlaces.Services.Revit.Opening;

/// <summary>
/// The line shown under the progress while a central opens as a new local,
/// when the local folder already has a local of that central: it will be
/// replaced (or kept with its date), and where that choice is changed
/// (Thomas, 2026-10-12). QuickerPlaces doesn't know Revit's user name, so a
/// local is any <c>&lt;central name&gt;_*.rvt</c> in the folder that isn't
/// an old local already set aside with a date. UI-free and linked into the test project.
/// </summary>
public static partial class ExistingLocalNote
{
    private const string Where = "change \"When a local already exists\" in Settings › Revit.";

    /// <param name="filesIn">Lists a folder's .rvt files; may throw, which means "say nothing".</param>
    public static string? For(RevitOpenPlan plan, Func<string, IEnumerable<string>> filesIn)
    {
        if (plan.Kind != RevitOpenKind.HandlerRequest || plan.ExistingLocal is null || string.IsNullOrWhiteSpace(plan.LocalFolder))
            return null;

        var stem = Path.GetFileNameWithoutExtension(plan.FilePath) + "_";
        string? local;
        try
        {
            local = filesIn(plan.LocalFolder)
                .Select(Path.GetFileName)
                .Where(n => n is not null && n.StartsWith(stem, StringComparison.OrdinalIgnoreCase) &&
                    n.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase) && !Dated().IsMatch(Path.GetFileNameWithoutExtension(n)))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return local is null ? null : plan.ExistingLocal switch
        {
            HandlerProtocol.ExistingLocal.Overwrite => $"Your existing local {local} will be replaced with a new copy. To keep old locals instead, {Where}",
            HandlerProtocol.ExistingLocal.AppendTimestamp => $"Your existing local {local} will be kept, renamed with its date. To replace it instead, {Where}",
            _ => null,
        };
    }

    /// <summary>A name ending in Revit's "_11Oct2026_233201" or the handler's "_20261011-171534", with an optional "-2".</summary>
    [GeneratedRegex(@"_(\d{2}[A-Za-z]{3}\d{4}_\d{6}|\d{8}-\d{6})(-\d+)?$")]
    private static partial Regex Dated();
}
