using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models.Workspace;

namespace QuickerPlaces.Services.Workspace;

/// <summary>
/// The rules a layout document must meet (configurable canvas plan §4, D2),
/// and the repairs that make a hand-edited or partly damaged one usable
/// without losing what is still good in it. Each repair is described in one
/// short line so the workspace can say what it changed, never silently.
/// UI-free.
/// </summary>
public static class WorkspaceValidation
{
    public const int MaxNameLength = 80;

    /// <summary>What a nameless layout found in the file is called, before the user renames it.</summary>
    public const string UntitledName = "Untitled layout";

    /// <summary>
    /// Checks a name for My layouts: trimmed, not blank, at most
    /// <see cref="MaxNameLength"/> characters, and not already used by
    /// another user layout ignoring case. Built-in names may be reused: the
    /// picker lists the two groups apart.
    /// </summary>
    public static ValidationResult ValidateName(string? name, IEnumerable<LayoutPreset> presets, string? exceptId, out string cleanName)
    {
        cleanName = (name ?? "").Trim();
        if (cleanName.Length == 0)
            return ValidationResult.Fail("Give the layout a name.");
        if (cleanName.Length > MaxNameLength)
            return ValidationResult.Fail($"Keep the name to {MaxNameLength} characters or fewer.");

        var taken = cleanName;
        if (presets.Any(p => p.Id != exceptId && string.Equals(p.Name, taken, StringComparison.OrdinalIgnoreCase)))
            return ValidationResult.Fail($"You already have a layout called \"{cleanName}\".");

        return ValidationResult.Ok();
    }

    /// <summary>
    /// A name for a copy of <paramref name="baseName"/> that no user layout
    /// uses yet: "X copy", then "X copy 2", and so on, shortened to fit.
    /// </summary>
    public static string UniqueName(string baseName, IEnumerable<LayoutPreset> presets)
        => FirstFree(baseName, presets.Select(p => p.Name), n => n == 1 ? " copy" : $" copy {n}");

    /// <summary><paramref name="baseName"/> with the first tail that makes it unused, shortened to fit <see cref="MaxNameLength"/>.</summary>
    private static string FirstFree(string baseName, IEnumerable<string> used, Func<int, string> tailFor)
    {
        var names = used.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var n = 1; ; n++)
        {
            var tail = tailFor(n);
            var head = baseName.Length + tail.Length > MaxNameLength ? baseName[..(MaxNameLength - tail.Length)].TrimEnd() : baseName;
            var candidate = head + tail;
            if (!names.Contains(candidate))
                return candidate;
        }
    }

    /// <summary>
    /// Repairs <paramref name="document"/> in place and says what changed:
    ///
    /// - A layout with no id, a repeated id or a built-in's id gets a new id.
    ///   A blank, overlong or repeated name is replaced or shortened.
    /// - A panel with no type is dropped; one of a type this build does not
    ///   know is kept, as a placeholder (plan D6). A missing or repeated panel
    ///   id is replaced; a width that is not 4, 6, 8 or 12 snaps to the nearest.
    /// - A working arrangement for a layout that no longer exists, or a
    ///   second one for the same layout, is dropped.
    /// - An active or startup layout that no longer exists falls back to the
    ///   default (plan D2).
    ///
    /// Unknown properties are left as they are.
    /// </summary>
    public static IReadOnlyList<string> Repair(WorkspaceDocument document)
    {
        var notes = new List<string>();

        document.Presets = (document.Presets ?? new List<LayoutPreset>()).Where(p => p is not null).ToList();
        document.Working = (document.Working ?? new List<WorkingArrangement>()).Where(w => w is not null).ToList();
        document.Startup ??= new StartupChoice();

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in document.Presets)
        {
            if (string.IsNullOrWhiteSpace(preset.Id) || BuiltInLayouts.IsBuiltInId(preset.Id) || !ids.Add(preset.Id))
            {
                preset.Id = NewId(ids);
                notes.Add("A layout with a missing or repeated id was given a new one.");
            }

            var name = (preset.Name ?? "").Trim();
            if (name.Length == 0)
                name = UntitledName;
            if (name.Length > MaxNameLength)
                name = name[..MaxNameLength].TrimEnd();
            if (names.Contains(name))
                name = FirstFree(name, names, n => $" ({n + 1})");
            if (name != preset.Name)
                notes.Add($"A layout was renamed \"{name}\" because its name was blank, too long or already used.");
            preset.Name = name;
            names.Add(name);

            preset.Panels = RepairPanels(preset.Panels, notes);
            preset.Filters = RepairQuery(preset.Filters);
        }

        var working = new List<WorkingArrangement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var arrangement in document.Working)
        {
            if (!Exists(document, arrangement.PresetId) || !seen.Add(arrangement.PresetId))
            {
                notes.Add("Changes kept for a layout that no longer exists were removed.");
                continue;
            }

            if (arrangement.Panels is not null)
                arrangement.Panels = RepairPanels(arrangement.Panels, notes);
            arrangement.Query = RepairQuery(arrangement.Query);
            working.Add(arrangement);
        }

        document.Working = working;

        if (document.ActivePresetId is not null && !Exists(document, document.ActivePresetId))
        {
            document.ActivePresetId = null;
            notes.Add("The layout last shown no longer exists, so the workspace opens with Activity Atlas.");
        }

        if (!document.Startup.ResumesLast && !Exists(document, document.Startup.PresetId))
        {
            document.Startup = StartupChoice.Resume();
            notes.Add("The startup layout no longer exists, so the workspace resumes the last layout instead.");
        }

        return notes.Distinct().ToList();
    }

    /// <summary>True when <paramref name="id"/> names a built-in or one of the document's layouts.</summary>
    public static bool Exists(WorkspaceDocument document, string? id)
        => id is not null && (BuiltInLayouts.Find(id) is not null || document.Presets.Any(p => p.Id == id));

    private static List<PanelInstance> RepairPanels(List<PanelInstance>? panels, List<string> notes)
    {
        var repaired = new List<PanelInstance>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var panel in panels ?? new List<PanelInstance>())
        {
            if (panel is null || string.IsNullOrWhiteSpace(panel.Type))
            {
                notes.Add("A panel with no type was removed.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(panel.Id) || !ids.Add(panel.Id))
            {
                panel.Id = NewPanelId(panel.Type, ids);
                notes.Add("A panel with a missing or repeated id was given a new one.");
            }

            if (!PanelSpans.IsAllowed(panel.Span))
            {
                panel.Span = PanelSpans.Snap(panel.Span);
                notes.Add("A panel width that isn't a third, half, two thirds or full width was corrected.");
            }

            repaired.Add(panel);
        }

        return repaired;
    }

    private static WorkspaceQuery? RepairQuery(WorkspaceQuery? query)
    {
        if (query is null)
            return null;

        query.Text ??= "";
        query.Date ??= new DateRule();
        return query;
    }

    /// <summary>A fresh layout id not in <paramref name="taken"/>, which it is then added to.</summary>
    public static string NewId(HashSet<string> taken)
    {
        string id;
        do
            id = Guid.NewGuid().ToString("N");
        while (!taken.Add(id));
        return id;
    }

    /// <summary>The panel's type as its id when free (one panel per type), otherwise the type with a number.</summary>
    public static string NewPanelId(string type, HashSet<string> taken)
    {
        if (taken.Add(type))
            return type;

        for (var n = 2; ; n++)
        {
            var id = $"{type}-{n}";
            if (taken.Add(id))
                return id;
        }
    }
}
