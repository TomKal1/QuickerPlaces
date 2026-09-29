using System.Collections.Generic;
using QuickerPlaces.Models.Workspace;

namespace QuickerPlaces.Services.Workspace;

/// <summary>
/// Where each shown panel goes on the twelve-column canvas (configurable
/// canvas plan D1): panels are packed in their stored order, left to right,
/// and a panel that no longer fits in what is left of a row starts the next
/// one. Nothing overlaps and nothing depends on screen coordinates.
///
/// M3 packs at the full twelve columns. M4 adds narrow-window reflow (stacking
/// without changing stored spans) here, where it can be tested. Pure logic;
/// UI-free and linked into the test project.
/// </summary>
public static class PanelLayoutEngine
{
    /// <summary>Places every panel that isn't hidden. A span this build doesn't allow is snapped to one it does.</summary>
    public static IReadOnlyList<PanelPlacement> Pack(IEnumerable<PanelInstance> panels)
    {
        var placements = new List<PanelPlacement>();
        var row = 0;
        var column = 0;

        foreach (var panel in panels)
        {
            if (panel.Hidden)
                continue;

            var span = PanelSpans.IsAllowed(panel.Span) ? panel.Span : PanelSpans.Snap(panel.Span);
            if (column > 0 && column + span > PanelSpans.Columns)
            {
                row++;
                column = 0;
            }

            placements.Add(new PanelPlacement(panel.Id, panel.Type, row, column, span));
            column += span;
            if (column >= PanelSpans.Columns)
            {
                row++;
                column = 0;
            }
        }

        return placements;
    }
}

/// <summary>One panel's place: its row, first column (0–11) and width in columns.</summary>
public sealed record PanelPlacement(string PanelId, string Type, int Row, int Column, int Span);
