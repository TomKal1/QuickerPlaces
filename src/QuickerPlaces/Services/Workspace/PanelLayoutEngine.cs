using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models.Workspace;

namespace QuickerPlaces.Services.Workspace;

/// <summary>
/// Where each shown panel goes on the twelve-column canvas (configurable
/// canvas plan D1): panels are packed in their stored order, left to right,
/// and a panel that no longer fits in what is left of a row starts the next
/// one. Nothing overlaps and nothing depends on screen coordinates.
///
/// Given the width available (M4), a panel whose stored span would be
/// narrower than it can be read at is shown wider, up to the full width, so
/// a narrow window stacks panels instead of squeezing them. That is
/// presentation only: stored spans and order never change here. Also the
/// arithmetic behind a drop (which panel a dragged one goes before) and a
/// resize (which allowed span a dragged edge snaps to), so both are tested
/// without WPF. Pure logic; UI-free and linked into the test project.
/// </summary>
public static class PanelLayoutEngine
{
    /// <summary>The space between two panels, in device-independent pixels: each panel's frame has a margin of half this.</summary>
    public const double Gap = 12;

    /// <summary>Places every panel that isn't hidden at the stored spans, as on a canvas wide enough for all of them.</summary>
    public static IReadOnlyList<PanelPlacement> Pack(IEnumerable<PanelInstance> panels) => Pack(panels, double.PositiveInfinity);

    /// <summary>
    /// Places every panel that isn't hidden on a canvas <paramref name="width"/>
    /// pixels wide. A span this build doesn't allow is snapped to one it does;
    /// a panel narrower than <see cref="MinimumWidth"/> at its span is shown
    /// at the narrowest allowed span that is wide enough, or full width; and
    /// a row that such a wider panel leaves or joins is filled by stretching
    /// its last panel, so stacking leaves no holes.
    /// </summary>
    public static IReadOnlyList<PanelPlacement> Pack(IEnumerable<PanelInstance> panels, double width)
    {
        var rows = new List<List<PanelPlacement>>();
        var row = new List<PanelPlacement>();
        var column = 0;
        var stretchRow = false;

        void EndRow()
        {
            if (row.Count == 0)
                return;
            if (stretchRow && column < PanelSpans.Columns)
            {
                var last = row[^1];
                row[^1] = last with { Span = PanelSpans.Columns - last.Column };
            }

            rows.Add(row);
            row = new List<PanelPlacement>();
            column = 0;
            stretchRow = false;
        }

        foreach (var panel in panels)
        {
            if (panel.Hidden)
                continue;

            var stored = PanelSpans.IsAllowed(panel.Span) ? panel.Span : PanelSpans.Snap(panel.Span);
            var span = ShownSpan(panel.Type, stored, width);
            var widened = span != stored;
            if (column > 0 && column + span > PanelSpans.Columns)
            {
                // The row this panel no longer fits beside is filled when reflow is why.
                stretchRow |= widened;
                EndRow();
            }

            row.Add(new PanelPlacement(panel.Id, panel.Type, rows.Count, column, span, stored));
            stretchRow |= widened;
            column += span;
            if (column >= PanelSpans.Columns)
                EndRow();
        }

        EndRow();
        return rows.SelectMany(r => r).ToList();
    }

    /// <summary>The width of one card in the left column of a columns layout: a session card, or a favourite, one to a row.</summary>
    public const double CardWidth = 340;

    /// <summary>
    /// How wide a columns layout's left column is, gap included: one card wide,
    /// or as wide as the widest panel docked there needs, so it is never cut off.
    /// </summary>
    public static double LeftColumnWidth(IEnumerable<string> leftPanelTypes)
        => Math.Max(CardWidth, leftPanelTypes.Select(MinimumWidth).DefaultIfEmpty(0).Max()) + Gap;

    /// <summary>
    /// Places a columns layout's panels (Desk layout design §2): a left column
    /// one card wide (<see cref="LeftColumnWidth"/>) and a main column with the
    /// rest, each a stack of its own panels in their stored order. An empty
    /// column gives its width to the other. The placements' column and span
    /// only say which column a panel is in, and whether it shares the canvas
    /// (4 and 8) or has it alone (12): the view sizes the left column in
    /// pixels. When a panel can't be read at its column's width
    /// (<see cref="MinimumWidth"/>), the columns stack instead, main first,
    /// every panel full width and <see cref="PanelDock.None"/>: presentation
    /// only, as reflow is. Stored docks never change here.
    /// </summary>
    public static IReadOnlyList<PanelPlacement> PackColumns(IEnumerable<PanelInstance> panels, double width)
    {
        var shown = panels.Where(p => !p.Hidden).ToList();
        var left = shown.Where(p => PanelDocks.IsLeft(p.Dock)).ToList();
        var main = shown.Where(p => !PanelDocks.IsLeft(p.Dock)).ToList();
        var leftSpan = main.Count == 0 ? PanelSpans.Full : PanelSpans.Third;
        var mainSpan = left.Count == 0 ? PanelSpans.Full : PanelSpans.TwoThirds;

        var leftWidth = left.Count == 0 || main.Count == 0 ? PanelWidth(PanelSpans.Full, width) : LeftColumnWidth(left.Select(p => p.Type)) - Gap;
        var mainWidth = left.Count == 0 ? PanelWidth(PanelSpans.Full, width) : main.Count == 0 ? 0 : width - LeftColumnWidth(left.Select(p => p.Type)) - Gap;
        var fits = left.All(p => leftWidth >= MinimumWidth(p.Type)) &&
                   main.All(p => mainWidth >= MinimumWidth(p.Type));
        if (!fits)
        {
            return main.Concat(left)
                .Select((p, row) => new PanelPlacement(p.Id, p.Type, row, 0, PanelSpans.Full, PanelSpans.Full))
                .ToList();
        }

        var mainColumn = left.Count == 0 ? 0 : PanelSpans.Third;
        return left.Select((p, row) => new PanelPlacement(p.Id, p.Type, row, 0, leftSpan, leftSpan, PanelDock.Left))
            .Concat(main.Select((p, row) => new PanelPlacement(p.Id, p.Type, row, mainColumn, mainSpan, mainSpan, PanelDock.Main)))
            .ToList();
    }

    /// <summary>
    /// The narrowest a panel of this type can be read at, in device-independent
    /// pixels. The Year activity panel switches to a month view below the
    /// year's width, so its minimum is the month view's; Saved places is a
    /// four-column table. Favourites' cards wrap, so it reads narrow.
    /// </summary>
    public static double MinimumWidth(string type) => type switch
    {
        PanelTypes.Activity => 280,
        PanelTypes.Shelf => 460,
        PanelTypes.Sessions => 300,
        PanelTypes.Places or PanelTypes.Files => 560,
        PanelTypes.Favourites => 200,
        _ => 280,
    };

    /// <summary>The span a panel is shown at on a canvas this wide: its own when it is wide enough there, otherwise the next wider allowed span that is, or full width.</summary>
    public static int ShownSpan(string type, int span, double width)
    {
        var minimum = MinimumWidth(type);
        foreach (var allowed in PanelSpans.Allowed)
        {
            if (allowed >= span && PanelWidth(allowed, width) >= minimum)
                return allowed;
        }

        return PanelSpans.Full;
    }

    /// <summary>A panel's own width at this span on a canvas this wide, without the gap around it.</summary>
    public static double PanelWidth(int span, double canvasWidth) => span * canvasWidth / PanelSpans.Columns - Gap;

    /// <summary>
    /// Resizing by a panel's edge: the allowed span nearest to a panel
    /// <paramref name="width"/> pixels wide (gap included) on a canvas this
    /// wide. The wider span wins a tie, as <see cref="PanelSpans.Snap"/>.
    /// </summary>
    public static int SpanForWidth(double width, double canvasWidth)
    {
        if (canvasWidth <= 0 || double.IsNaN(width))
            return PanelSpans.Full;

        var columns = width / (canvasWidth / PanelSpans.Columns);
        var best = PanelSpans.Full;
        foreach (var allowed in PanelSpans.Allowed)
        {
            if (Math.Abs(allowed - columns) <= Math.Abs(best - columns))
                best = allowed;
        }

        return best;
    }

    /// <summary>
    /// A drop while dragging <paramref name="draggedId"/> over
    /// <paramref name="targetId"/>, before it or, when
    /// <paramref name="after"/>, after it. The result names the panel the
    /// dragged one would go before (null: last), or is null when the drop
    /// would leave the order as it is — on itself, or beside where it already is.
    /// </summary>
    public static DropTarget? Drop(IReadOnlyList<PanelPlacement> placements, string draggedId, string targetId, bool after)
    {
        var order = placements.Select(p => p.PanelId).ToList();
        var dragged = order.IndexOf(draggedId);
        var target = order.IndexOf(targetId);
        if (dragged < 0 || target < 0 || dragged == target)
            return null;

        var beforeIndex = after ? target + 1 : target;
        if (beforeIndex == dragged || beforeIndex == dragged + 1)
            return null;

        return new DropTarget(beforeIndex < order.Count ? order[beforeIndex] : null);
    }

    /// <summary>
    /// A drop in a columns layout while dragging <paramref name="draggedId"/>
    /// over <paramref name="targetId"/>, above it or, when
    /// <paramref name="after"/>, below it: the target's column, and the panel
    /// the dragged one would go before in the stored order (null: last). Null
    /// when the drop would leave it where it is. <paramref name="panels"/> are
    /// the shown panels in stored order.
    /// </summary>
    public static ColumnDropTarget? ColumnDrop(IReadOnlyList<PanelInstance> panels, string draggedId, string targetId, bool after)
    {
        var dragged = panels.FirstOrDefault(p => p.Id == draggedId);
        var target = panels.FirstOrDefault(p => p.Id == targetId);
        if (dragged is null || target is null || draggedId == targetId)
            return null;

        var dock = PanelDocks.Normalize(target.Dock);
        var column = panels.Where(p => PanelDocks.Normalize(p.Dock) == dock).Select(p => p.Id).ToList();
        var others = column.Where(id => id != draggedId).ToList();
        var at = others.IndexOf(targetId) + (after ? 1 : 0);
        var before = at < others.Count ? others[at] : null;

        if (PanelDocks.Normalize(dragged.Dock) == dock)
        {
            var index = column.IndexOf(draggedId);
            var currentBefore = index + 1 < column.Count ? column[index + 1] : null;
            if (currentBefore == before)
                return null;
        }

        return new ColumnDropTarget(dock, before);
    }
}

/// <summary>
/// One panel's place: its row, first column (0–11) and the columns it is
/// shown across. In a columns layout, <see cref="Row"/> is its place in its own column (<see cref="Dock"/>).
/// <see cref="StoredSpan"/> is its own width, which
/// <see cref="Span"/> exceeds when a narrow canvas makes it wider.
/// </summary>
public sealed record PanelPlacement(string PanelId, string Type, int Row, int Column, int Span, int StoredSpan, PanelDock Dock = PanelDock.None)
{
    public PanelPlacement(string panelId, string type, int row, int column, int span)
        : this(panelId, type, row, column, span, span)
    {
    }

    /// <summary>True when the panel is shown wider than its own span, because the canvas is too narrow for it.</summary>
    public bool IsWidened => Span > StoredSpan;
}

/// <summary>Where a dropped panel goes: just before <see cref="BeforePanelId"/>, or last when that is null.</summary>
public sealed record DropTarget(string? BeforePanelId);

/// <summary>Which column of a columns layout a placement is in: none in a rows layout, or when a narrow window stacks the columns.</summary>
public enum PanelDock
{
    None,
    Left,
    Main,
}

/// <summary>Where a panel dropped in a columns layout goes: into <see cref="Dock"/>, just before <see cref="BeforePanelId"/> in the stored order, or last.</summary>
public sealed record ColumnDropTarget(string Dock, string? BeforePanelId);
