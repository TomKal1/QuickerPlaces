using System.Linq;
using QuickerPlaces.Models.Workspace;
using QuickerPlaces.Services.Workspace;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Packing panels onto the twelve-column canvas (configurable canvas plan
/// D1, M3), reflow in a narrow window, and the arithmetic behind a drop and
/// a resize (M4).
/// </summary>
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

    // ---------------------------------------------------------------
    // Reflow (M4)
    // ---------------------------------------------------------------

    private static PanelInstance Typed(string type, int span, bool hidden = false)
        => new() { Id = type, Type = type, Span = span, Hidden = hidden };

    [Fact]
    public void AWideCanvas_KeepsTheStoredSpans()
    {
        var placed = PanelLayoutEngine.Pack(BuiltInLayouts.ActivityAtlas.CreatePanels(), 1400);

        Assert.Equal(new[] { "activity@0:0+12", "shelf@1:0+8", "sessions@1:8+4" }, placed.Select(Describe));
        Assert.All(placed, p => Assert.False(p.IsWidened));
    }

    [Fact]
    public void ANarrowCanvas_StacksPanels_WithoutHoles_AndKeepsTheirOwnSpans()
    {
        // Sessions at a third of 780 is 253 px: too narrow, and half (378) is what it needs.
        // Shown half, it no longer fits beside the shelf, so both rows are filled.
        var panels = BuiltInLayouts.ActivityAtlas.CreatePanels();
        var placed = PanelLayoutEngine.Pack(panels, 780);

        Assert.Equal(new[] { "activity@0:0+12", "shelf@1:0+12", "sessions@2:0+12" }, placed.Select(Describe));
        Assert.Equal(new[] { 12, 8, 4 }, placed.Select(p => p.StoredSpan));
        Assert.Equal(new[] { 12, 8, 4 }, panels.Select(p => p.Span));
    }

    [Fact]
    public void APanelTooNarrowForItsSpan_IsShownAtTheNextSpanThatFits()
    {
        // Saved places needs 560 px: at 1200 wide a third is 388, half 588.
        var placed = PanelLayoutEngine.Pack(new[] { Typed(PanelTypes.Places, 4), Typed(PanelTypes.Sessions, 6) }, 1200);

        Assert.Equal(new[] { "places@0:0+6", "sessions@0:6+6" }, placed.Select(Describe));
        Assert.True(placed[0].IsWidened);
        Assert.False(placed[1].IsWidened);
    }

    [Fact]
    public void ARowTheUserLeftPart_Empty_StaysSo_WhenNothingWasWidened()
    {
        var placed = PanelLayoutEngine.Pack(new[] { Typed(PanelTypes.Shelf, 8), Typed(PanelTypes.Activity, 12) }, 1400);

        Assert.Equal(new[] { "shelf@0:0+8", "activity@1:0+12" }, placed.Select(Describe));
    }

    [Fact]
    public void ACanvasTooNarrowForAnything_ShowsEachPanelFullWidth_InOrder()
    {
        var placed = PanelLayoutEngine.Pack(new[] { Typed(PanelTypes.Sessions, 4), Typed(PanelTypes.Shelf, 4), Typed(PanelTypes.Places, 4) }, 200);

        Assert.Equal(new[] { "sessions@0:0+12", "shelf@1:0+12", "places@2:0+12" }, placed.Select(Describe));
    }

    [Theory]
    [InlineData(300)]
    [InlineData(700)]
    [InlineData(960)]
    [InlineData(1300)]
    [InlineData(2600)]
    public void AtAnyWidth_NoPanelsOverlap_AndNoneRunsPastTwelveColumns(double width)
    {
        var types = new[] { PanelTypes.Activity, PanelTypes.Shelf, PanelTypes.Sessions, PanelTypes.Places, "future-panel" };
        var spans = new[] { 4, 6, 4, 8, 4 };
        var placed = PanelLayoutEngine.Pack(types.Select((t, i) => Typed(t, spans[i])), width);

        Assert.Equal(types, placed.Select(p => p.PanelId));
        Assert.All(placed, p => Assert.InRange(p.Column + p.Span, 1, PanelSpans.Columns));
        Assert.All(placed, p => Assert.True(p.Span >= p.StoredSpan));
        foreach (var row in placed.GroupBy(p => p.Row))
        {
            var cells = row.SelectMany(p => Enumerable.Range(p.Column, p.Span)).ToList();
            Assert.Equal(cells.Count, cells.Distinct().Count());
        }
    }

    // ---------------------------------------------------------------
    // Resize and drop (M4)
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(250, 4)]   // 3 columns: a third is the narrowest
    [InlineData(400, 4)]   // 4.8 columns
    [InlineData(583.5, 8)] // 7.002: just past the tie at 7, which the wider span wins
    [InlineData(500, 6)]   // exactly 6
    [InlineData(575, 6)]   // 6.9
    [InlineData(600, 8)]   // 7.2
    [InlineData(900, 12)]
    [InlineData(5000, 12)]
    public void ADraggedEdge_SnapsToTheNearestAllowedSpan(double width, int span)
        => Assert.Equal(span, PanelLayoutEngine.SpanForWidth(width, 1000));

    private static readonly PanelPlacement[] Atlas = PanelLayoutEngine.Pack(new[]
    {
        Typed(PanelTypes.Activity, 12), Typed(PanelTypes.Shelf, 8), Typed(PanelTypes.Sessions, 4),
    }).ToArray();

    [Fact]
    public void DroppingBeforeOrAfterAnotherPanel_NamesThePanelItGoesBefore()
    {
        Assert.Equal("activity", PanelLayoutEngine.Drop(Atlas, "sessions", "activity", after: false)?.BeforePanelId);
        Assert.Equal("shelf", PanelLayoutEngine.Drop(Atlas, "sessions", "activity", after: true)?.BeforePanelId);
        Assert.Equal("sessions", PanelLayoutEngine.Drop(Atlas, "activity", "shelf", after: true)?.BeforePanelId);
    }

    [Fact]
    public void DroppingAfterTheLastPanel_PutsItLast()
    {
        var drop = PanelLayoutEngine.Drop(Atlas, "activity", "sessions", after: true);

        Assert.NotNull(drop);
        Assert.Null(drop!.BeforePanelId);
    }

    [Theory]
    [InlineData("shelf", "shelf", false)]
    [InlineData("shelf", "shelf", true)]
    [InlineData("shelf", "activity", true)]    // just after activity is where the shelf is
    [InlineData("shelf", "sessions", false)]   // just before sessions, likewise
    [InlineData("sessions", "sessions", true)]
    [InlineData("shelf", "nope", false)]
    public void ADropThatChangesNothing_IsNoDrop(string dragged, string target, bool after)
        => Assert.Null(PanelLayoutEngine.Drop(Atlas, dragged, target, after));

    // ---------------------------------------------------------------
    // Columns layouts (Desk layout design §2)
    // ---------------------------------------------------------------

    private static PanelInstance Docked(string type, string? dock, bool hidden = false)
        => new() { Id = type, Type = type, Span = PanelSpans.Third, Dock = dock, Hidden = hidden };

    private static string DescribeDocked(PanelPlacement p) => $"{p.PanelId}@{p.Row}:{p.Column}+{p.Span}/{p.Dock}";

    private static readonly PanelInstance[] Desk =
    {
        Docked(PanelTypes.Sessions, PanelDocks.Left),
        Docked(PanelTypes.Places, PanelDocks.Main),
        Docked(PanelTypes.Shelf, PanelDocks.Main),
        Docked(PanelTypes.Activity, PanelDocks.Main),
    };

    [Fact]
    public void Columns_StackEachColumn_LeftOneCardWide_MainTheRest()
    {
        var placed = PanelLayoutEngine.PackColumns(Desk, 1800);

        Assert.Equal(new[] { "sessions@0:0+4/Left", "places@0:4+8/Main", "shelf@1:4+8/Main", "activity@2:4+8/Main" },
            placed.Select(DescribeDocked));
    }

    [Fact]
    public void AnEmptyColumn_GivesItsWidthToTheOther()
    {
        var mainOnly = PanelLayoutEngine.PackColumns(new[] { Docked(PanelTypes.Shelf, PanelDocks.Main), Docked(PanelTypes.Sessions, PanelDocks.Left, hidden: true) }, 1800);
        var leftOnly = PanelLayoutEngine.PackColumns(new[] { Docked(PanelTypes.Sessions, PanelDocks.Left) }, 1800);

        Assert.Equal(new[] { "shelf@0:0+12/Main" }, mainOnly.Select(DescribeDocked));
        Assert.Equal(new[] { "sessions@0:0+12/Left" }, leftOnly.Select(DescribeDocked));
    }

    [Fact]
    public void AnUnknownDock_ReadsAsMain()
    {
        var placed = PanelLayoutEngine.PackColumns(new[] { Docked(PanelTypes.Sessions, PanelDocks.Left), Docked(PanelTypes.Shelf, "sideways") }, 1800);

        Assert.Equal(PanelDock.Main, placed.Single(p => p.PanelId == PanelTypes.Shelf).Dock);
    }

    [Fact]
    public void ANarrowWindow_StacksTheColumns_MainFirst_FullWidth()
    {
        // Even with the left column folded to a rail, 590 leaves Saved places (560) too little.
        var placed = PanelLayoutEngine.PackColumns(Desk, 590);

        Assert.Equal(new[] { "places@0:0+12/None", "shelf@1:0+12/None", "activity@2:0+12/None", "sessions@3:0+12/None" },
            placed.Select(DescribeDocked));
    }

    [Fact]
    public void TheLeftColumn_IsOneCardWide_WhateverTheCanvasWidth()
    {
        var types = new[] { PanelTypes.Favourites, PanelTypes.Sessions };

        Assert.Equal(PanelLayoutEngine.CardWidth + PanelLayoutEngine.Gap, PanelLayoutEngine.LeftColumnWidth(types));
    }

    [Fact]
    public void TheLeftColumn_GrowsToTheWidestPanelDockedInIt()
    {
        var width = PanelLayoutEngine.LeftColumnWidth(new[] { PanelTypes.Sessions, PanelTypes.Files });

        Assert.Equal(PanelLayoutEngine.MinimumWidth(PanelTypes.Files) + PanelLayoutEngine.Gap, width);
    }

    [Fact]
    public void Columns_StackOnlyWhenTheMainColumnGetsTooLittle_NotTheLeft()
    {
        // The left column takes 352, so Saved places (560) needs the canvas to be 352 + 12 + 560 = 924.
        Assert.Equal(PanelDock.None, PanelLayoutEngine.PackColumns(Desk, 923, allowRail: false).First().Dock);
        Assert.Equal(PanelDock.Left, PanelLayoutEngine.PackColumns(Desk, 924).First().Dock);
    }

    [Fact]
    public void ANarrowWindow_FoldsTheLeftColumnToARail_WhileTheMainColumnFits()
    {
        // The rail takes 32 + 12, so Saved places (560) fits down to 604.
        Assert.Equal(PanelDock.Rail, PanelLayoutEngine.PackColumns(Desk, 923).First(p => p.PanelId == "sessions").Dock);
        Assert.Equal(PanelDock.Main, PanelLayoutEngine.PackColumns(Desk, 604).First(p => p.PanelId == "places").Dock);
        Assert.Equal(PanelDock.None, PanelLayoutEngine.PackColumns(Desk, 603).First().Dock);
    }

    [Fact]
    public void AFoldedLeftColumn_IsARail_AtAnyWidth()
    {
        Assert.Equal(PanelDock.Rail, PanelLayoutEngine.PackColumns(Desk, 1800, foldLeft: true).First(p => p.PanelId == "sessions").Dock);
        Assert.Equal(PanelDock.Left, PanelLayoutEngine.PackColumns(Desk, 1800, allowRail: false, foldLeft: true).First(p => p.PanelId == "sessions").Dock);
    }

    [Fact]
    public void ColumnDrop_ReordersWithinAColumn()
    {
        var drop = PanelLayoutEngine.ColumnDrop(Desk, PanelTypes.Activity, PanelTypes.Places, after: false);

        Assert.Equal(new ColumnDropTarget(PanelDocks.Main, PanelTypes.Places), drop);
    }

    [Fact]
    public void ColumnDrop_AcrossColumns_TakesTheTargetsColumn()
    {
        var below = PanelLayoutEngine.ColumnDrop(Desk, PanelTypes.Shelf, PanelTypes.Sessions, after: true);
        var above = PanelLayoutEngine.ColumnDrop(Desk, PanelTypes.Shelf, PanelTypes.Sessions, after: false);

        Assert.Equal(new ColumnDropTarget(PanelDocks.Left, null), below);
        Assert.Equal(new ColumnDropTarget(PanelDocks.Left, PanelTypes.Sessions), above);
    }

    [Theory]
    [InlineData("shelf", "shelf", false)]
    [InlineData("shelf", "places", true)]
    [InlineData("shelf", "activity", false)]
    [InlineData("activity", "shelf", true)]
    public void ColumnDrop_WhereItAlreadyIs_IsNoDrop(string dragged, string target, bool after)
        => Assert.Null(PanelLayoutEngine.ColumnDrop(Desk, dragged, target, after));
}
