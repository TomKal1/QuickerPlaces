using System.Linq;
using QuickerPlaces.Models.Workspace;
using QuickerPlaces.Services.Workspace;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>Packing panels onto the twelve-column canvas (configurable canvas plan D1, M3).</summary>
public sealed class PanelLayoutEngineTests
{
    private static PanelInstance Panel(string id, int span, bool hidden = false)
        => new() { Id = id, Type = id, Span = span, Hidden = hidden };

    private static string Describe(PanelPlacement p) => $"{p.PanelId}@{p.Row}:{p.Column}+{p.Span}";

    [Fact]
    public void ActivityAtlas_PutsTheYearAcrossTheTop_AndShelfBesideSessions()
    {
        var placed = PanelLayoutEngine.Pack(BuiltInLayouts.ActivityAtlas.CreatePanels());

        Assert.Equal(new[] { "activity@0:0+12", "shelf@1:0+8", "sessions@1:8+4" }, placed.Select(Describe));
    }

    [Fact]
    public void APanelThatDoesNotFit_StartsTheNextRow_InOrder()
    {
        var placed = PanelLayoutEngine.Pack(new[] { Panel("a", 8), Panel("b", 6), Panel("c", 4), Panel("d", 12) });

        Assert.Equal(new[] { "a@0:0+8", "b@1:0+6", "c@1:6+4", "d@2:0+12" }, placed.Select(Describe));
    }

    [Fact]
    public void HiddenPanels_TakeNoSpace()
    {
        var placed = PanelLayoutEngine.Pack(new[] { Panel("a", 8), Panel("b", 4, hidden: true), Panel("c", 4) });

        Assert.Equal(new[] { "a@0:0+8", "c@0:8+4" }, placed.Select(Describe));
    }

    [Fact]
    public void NoPanelsOverlap_AndNoneRunsPastTwelveColumns()
    {
        var spans = new[] { 4, 12, 6, 6, 8, 4, 4, 8, 12, 6, 4 };
        var placed = PanelLayoutEngine.Pack(spans.Select((s, i) => Panel($"p{i}", s)));

        Assert.All(placed, p => Assert.InRange(p.Column + p.Span, 1, PanelSpans.Columns));
        foreach (var row in placed.GroupBy(p => p.Row))
        {
            var cells = row.SelectMany(p => Enumerable.Range(p.Column, p.Span)).ToList();
            Assert.Equal(cells.Count, cells.Distinct().Count());
        }
    }

    [Fact]
    public void ASpanThisBuildDoesNotAllow_IsSnapped()
    {
        var placed = PanelLayoutEngine.Pack(new[] { Panel("a", 7), Panel("b", 0) });

        Assert.Equal(new[] { 8, 4 }, placed.Select(p => p.Span));
    }

    [Fact]
    public void AnEmptyLayout_PlacesNothing()
        => Assert.Empty(PanelLayoutEngine.Pack(new PanelInstance[0]));
}
