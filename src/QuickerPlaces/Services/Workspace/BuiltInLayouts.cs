using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models.Workspace;

namespace QuickerPlaces.Services.Workspace;

/// <summary>
/// The built-in layouts (configurable canvas plan M1, D2). Code, not
/// data: they cannot be overwritten or deleted, and each carries a
/// <see cref="BuiltInLayout.Version"/> so a later change to a factory
/// definition does not overwrite an arrangement the user already
/// personalised from an older one.
///
/// A built-in is offered only when every panel it needs has a working panel
/// (<see cref="PanelTypes.Available"/>): until Collections and Saved searches
/// land in M6, that is Activity Atlas, Files First and the two Desks. UI-free.
/// </summary>
public static class BuiltInLayouts
{
    public const string ActivityAtlasId = "builtin.activity-atlas";
    public const string FilesFirstId = "builtin.files-first";
    public const string ProjectCanvasId = "builtin.project-canvas";
    public const string PersonalDeskId = "builtin.personal-desk";
    public const string DeskId = "builtin.desk";
    public const string DeskSeparateId = "builtin.desk-separate";

    /// <summary>The prefix every built-in id has, and no user layout's id may.</summary>
    public const string IdPrefix = "builtin.";

    /// <summary>What the workspace falls back to when nothing else is usable (plan D2).</summary>
    public const string DefaultId = ActivityAtlasId;

    public static readonly BuiltInLayout ActivityAtlas = new(ActivityAtlasId, "Activity Atlas", 1, new[]
    {
        (PanelTypes.Activity, PanelSpans.Full),
        (PanelTypes.Shelf, PanelSpans.TwoThirds),
        (PanelTypes.Sessions, PanelSpans.Third),
    });

    /// <summary>
    /// For people who look for files rather than days: the shelf leads, with
    /// the activity calendar beside it in a third, where it shows a month at
    /// a time (still with its activity shading), and Sessions below.
    /// </summary>
    public static readonly BuiltInLayout FilesFirst = new(FilesFirstId, "Files First", 1, new[]
    {
        (PanelTypes.Shelf, PanelSpans.TwoThirds),
        (PanelTypes.Activity, PanelSpans.Third),
        (PanelTypes.Sessions, PanelSpans.Full),
    });

    /// <summary>
    /// The user's sketch (Desk layout design) with the File viewer (File
    /// viewer design §2): favourites and sessions in a left column; Files
    /// (saved places, recents and session files as tabs) and the year calendar
    /// in the main column. Version 2: version 1 had separate panels.
    /// </summary>
    public static readonly BuiltInLayout Desk = new(DeskId, "Desk", 2, LayoutArrangements.Columns, new (string, int, string?)[]
    {
        (PanelTypes.Favourites, PanelSpans.Third, PanelDocks.Left),
        (PanelTypes.Sessions, PanelSpans.Third, PanelDocks.Left),
        (PanelTypes.Files, PanelSpans.Full, PanelDocks.Main),
        (PanelTypes.Activity, PanelSpans.Full, PanelDocks.Main),
    });

    /// <summary>
    /// Desk as first built (Desk layout design): saved places, Recents and the
    /// year calendar as separate panels. The way back from the File viewer.
    /// </summary>
    public static readonly BuiltInLayout DeskSeparate = new(DeskSeparateId, "Desk · separate panels", 1, LayoutArrangements.Columns, new (string, int, string?)[]
    {
        (PanelTypes.Favourites, PanelSpans.Third, PanelDocks.Left),
        (PanelTypes.Sessions, PanelSpans.Third, PanelDocks.Left),
        (PanelTypes.Places, PanelSpans.Full, PanelDocks.Main),
        (PanelTypes.Shelf, PanelSpans.TwoThirds, PanelDocks.Main),
        (PanelTypes.Activity, PanelSpans.Full, PanelDocks.Main),
    });

    public static readonly BuiltInLayout ProjectCanvas = new(ProjectCanvasId, "Project Canvas", 1, new[]
    {
        (PanelTypes.Collections, PanelSpans.Full),
        (PanelTypes.Shelf, PanelSpans.TwoThirds),
        (PanelTypes.Activity, PanelSpans.Third),
    });

    public static readonly BuiltInLayout PersonalDesk = new(PersonalDeskId, "Personal Desk", 1, new[]
    {
        (PanelTypes.Shelf, PanelSpans.TwoThirds),
        (PanelTypes.Searches, PanelSpans.Third),
        (PanelTypes.Activity, PanelSpans.TwoThirds),
        (PanelTypes.Sessions, PanelSpans.Third),
    });

    /// <summary>Every built-in, in picker order, offered or not.</summary>
    public static readonly IReadOnlyList<BuiltInLayout> All = new[] { ActivityAtlas, FilesFirst, Desk, DeskSeparate, ProjectCanvas, PersonalDesk };

    /// <summary>The built-ins the picker lists today.</summary>
    public static IReadOnlyList<BuiltInLayout> Offered => All.Where(b => b.IsOffered).ToList();

    public static bool IsBuiltInId(string? id) => id is not null && id.StartsWith(IdPrefix, StringComparison.Ordinal);

    public static BuiltInLayout? Find(string? id) => All.FirstOrDefault(b => b.Id == id);
}

/// <summary>One built-in layout's factory definition.</summary>
public sealed class BuiltInLayout
{
    private readonly (string Type, int Span, string? Dock)[] _panels;

    public BuiltInLayout(string id, string name, int version, (string Type, int Span)[] panels)
        : this(id, name, version, null, panels.Select(p => (p.Type, p.Span, (string?)null)).ToArray())
    {
    }

    /// <summary>A built-in with an arrangement (<see cref="LayoutArrangements"/>) and, in columns, each panel's column.</summary>
    public BuiltInLayout(string id, string name, int version, string? arrangement, (string Type, int Span, string? Dock)[] panels)
    {
        Id = id;
        Name = name;
        Version = version;
        Arrangement = arrangement;
        _panels = panels;
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>Raised whenever the factory definition changes.</summary>
    public int Version { get; }

    /// <summary>Null for rows, or <see cref="LayoutArrangements.Columns"/>.</summary>
    public string? Arrangement { get; }

    /// <summary>True when every panel this layout needs is available in this build.</summary>
    public bool IsOffered => _panels.All(p => PanelTypes.IsAvailable(p.Type));

    /// <summary>A fresh copy of the factory panels. Panel ids are the panel types: one of each.</summary>
    public List<PanelInstance> CreatePanels()
        => _panels.Select(p => new PanelInstance { Id = p.Type, Type = p.Type, Span = p.Span, Dock = p.Dock }).ToList();
}
