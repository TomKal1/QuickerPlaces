using System;
using System.Globalization;
using QuickerPlaces.Models.Workspace;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Typed date rules and panel spans (configurable canvas plan D1, D3):
/// relative rules resolve from the clock and the culture's week each time,
/// ranges are inclusive, and widths snap to the allowed spans.
/// </summary>
public sealed class WorkspaceQueryTests
{
    /// <summary>A clock whose local date (UTC+10) is <paramref name="local"/>, at midday.</summary>
    private static ManualTimeProvider At(DateOnly local)
        => new(new DateTimeOffset(local.Year, local.Month, local.Day, 2, 0, 0, TimeSpan.Zero));

    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("en-GB");

    [Fact]
    public void All_CoversEverything()
        => Assert.Null(DateRule.All().Resolve(At(new DateOnly(2026, 9, 29)), Us));

    [Fact]
    public void ThisWeek_StartsOnTheCulturesFirstDay()
    {
        var tuesday = At(new DateOnly(2026, 9, 29));

        Assert.Equal((new DateOnly(2026, 9, 27), new DateOnly(2026, 10, 3)), DateRule.ThisWeek().Resolve(tuesday, Us));
        Assert.Equal((new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 4)), DateRule.ThisWeek().Resolve(tuesday, Uk));
    }

    [Fact]
    public void ThisWeek_OnTheFirstDay_StartsThatDay()
    {
        var monday = At(new DateOnly(2026, 9, 28));

        Assert.Equal(new DateOnly(2026, 9, 28), DateRule.ThisWeek().Resolve(monday, Uk)!.Value.From);
    }

    [Fact]
    public void ThisWeek_CrossesAYearEnd()
        => Assert.Equal((new DateOnly(2026, 12, 28), new DateOnly(2027, 1, 3)),
            DateRule.ThisWeek().Resolve(At(new DateOnly(2027, 1, 1)), Uk));

    [Fact]
    public void ThisMonth_CoversTheWholeMonth_IncludingALeapDay()
    {
        Assert.Equal((new DateOnly(2028, 2, 1), new DateOnly(2028, 2, 29)), DateRule.ThisMonth().Resolve(At(new DateOnly(2028, 2, 10)), Us));
        Assert.Equal((new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 28)), DateRule.ThisMonth().Resolve(At(new DateOnly(2027, 2, 10)), Us));
    }

    [Fact]
    public void RelativeRules_UseTheLocalDate_AndMoveAtLocalMidnight()
    {
        // 13:59 UTC on 30 Sep is 23:59 on 30 Sep in UTC+10; a minute later it is October there.
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 30, 13, 59, 0, TimeSpan.Zero));
        var rule = DateRule.ThisMonth();
        Assert.Equal(9, rule.Resolve(clock, Us)!.Value.From.Month);

        clock.UtcNow = clock.UtcNow.AddMinutes(1);

        Assert.Equal(10, rule.Resolve(clock, Us)!.Value.From.Month);
    }

    [Fact]
    public void Ranges_AreInclusive_AndPutInOrder()
    {
        var rule = DateRule.Between(new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 1));

        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10)), rule.Resolve(At(new DateOnly(2027, 1, 1)), Us));
    }

    [Fact]
    public void ARangeMissingAnEnd_ReadsAsAll()
        => Assert.Null(new DateRule { Kind = DateRuleKind.Range, From = new DateOnly(2026, 1, 1) }.Resolve(At(new DateOnly(2026, 9, 1)), Us));

    [Fact]
    public void DefaultQuery_IsDefault_UntilAnythingIsSet()
    {
        Assert.True(WorkspaceQuery.Default.IsDefault);
        Assert.False(new WorkspaceQuery { Tag = "x" }.IsDefault);
        Assert.False(new WorkspaceQuery { Date = DateRule.ThisWeek() }.IsDefault);
    }

    [Fact]
    public void QueriesCompare_TagIgnoringCase_AndRangeDatesOnlyForRanges()
    {
        Assert.True(new WorkspaceQuery { Tag = "Tower B" }.SameAs(new WorkspaceQuery { Tag = "tower b" }));
        Assert.True(new WorkspaceQuery { Date = new DateRule { Kind = DateRuleKind.ThisWeek, From = new DateOnly(2026, 1, 1) } }
            .SameAs(new WorkspaceQuery { Date = DateRule.ThisWeek() }));
        Assert.False(new WorkspaceQuery { Date = DateRule.Between(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2)) }
            .SameAs(new WorkspaceQuery { Date = DateRule.Between(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3)) }));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 4)]
    [InlineData(5, 6)]
    [InlineData(7, 8)]
    [InlineData(10, 12)]
    [InlineData(99, 12)]
    public void Spans_SnapToTheNearestAllowed_WiderOnATie(int span, int expected)
        => Assert.Equal(expected, PanelSpans.Snap(span));
}
