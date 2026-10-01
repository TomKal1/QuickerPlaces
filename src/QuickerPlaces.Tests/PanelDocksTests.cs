using QuickerPlaces.Models.Workspace;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>Columns layouts (Desk layout design §2): which column a panel is in, and what counts as a change.</summary>
public sealed class PanelDocksTests
{
    [Theory]
    [InlineData("left", true)]
    [InlineData("main", false)]
    [InlineData(null, false)]
    [InlineData("sideways", false)]
    public void OnlyLeft_IsTheLeftColumn(string? dock, bool left) => Assert.Equal(left, PanelDocks.IsLeft(dock));

    [Fact]
    public void AnAbsentOrUnknownDock_ReadsAsMain()
    {
        Assert.Equal(PanelDocks.Main, PanelDocks.Normalize(null));
        Assert.Equal(PanelDocks.Main, PanelDocks.Normalize("sideways"));
        Assert.Equal(PanelDocks.Left, PanelDocks.Normalize("left"));
    }

    [Fact]
    public void SameAs_ComparesColumns_NotSpellings()
    {
        var absent = new PanelInstance { Id = "shelf", Type = "shelf", Dock = null };
        var main = new PanelInstance { Id = "shelf", Type = "shelf", Dock = PanelDocks.Main };
        var left = new PanelInstance { Id = "shelf", Type = "shelf", Dock = PanelDocks.Left };

        Assert.True(absent.SameAs(main));
        Assert.False(main.SameAs(left));
        Assert.Equal(PanelDocks.Left, left.Clone().Dock);
    }

    [Fact]
    public void OnlyColumns_IsAColumnsArrangement()
    {
        Assert.True(LayoutArrangements.IsColumns("columns"));
        Assert.False(LayoutArrangements.IsColumns(null));
        Assert.False(LayoutArrangements.IsColumns("rows"));
        Assert.False(LayoutArrangements.IsColumns("spiral"));
        Assert.Equal("columns", new LayoutPreset { Arrangement = "columns" }.Clone().Arrangement);
    }
}
