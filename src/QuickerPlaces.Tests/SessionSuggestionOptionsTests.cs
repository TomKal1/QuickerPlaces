using System;
using QuickerPlaces.Services.Sessions.Suggestions;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>The sensitivity levels and the numbers behind them (session suggestions plan §6.1).</summary>
public sealed class SessionSuggestionOptionsTests
{
    [Fact]
    public void Low_matches_the_design()
    {
        var o = SessionSuggestionOptions.For(SuggestionSensitivity.Low);
        Assert.Equal(TimeSpan.FromMinutes(10), o.BurstGap);
        Assert.Equal(4, o.MinDaysTogether);
        Assert.Equal(0.6, o.MinShare);
        Assert.Equal(TimeSpan.FromMinutes(90), o.TogetherOpen);
        Assert.Equal(2, o.TogetherOpenDays);
        Assert.Equal(TimeSpan.FromHours(5), o.AlwaysOpenAbove);
        Assert.Equal(1.0, o.GroupJoinFraction);
        Assert.Equal(5, o.MaxSuggestions);
    }

    [Fact]
    public void Balanced_matches_the_design()
    {
        var o = SessionSuggestionOptions.For(SuggestionSensitivity.Balanced);
        Assert.Equal(TimeSpan.FromMinutes(15), o.BurstGap);
        Assert.Equal(3, o.MinDaysTogether);
        Assert.Equal(0.5, o.MinShare);
        Assert.Equal(TimeSpan.FromMinutes(60), o.TogetherOpen);
        Assert.Equal(1, o.TogetherOpenDays);
        Assert.Equal(TimeSpan.FromHours(6), o.AlwaysOpenAbove);
        Assert.Equal(2.0 / 3, o.GroupJoinFraction);
        Assert.Equal(10, o.MaxSuggestions);
        Assert.Equal(o, SessionSuggestionOptions.Balanced);
    }

    [Fact]
    public void High_matches_the_design()
    {
        var o = SessionSuggestionOptions.For(SuggestionSensitivity.High);
        Assert.Equal(TimeSpan.FromMinutes(30), o.BurstGap);
        Assert.Equal(2, o.MinDaysTogether);
        Assert.Equal(0.35, o.MinShare);
        Assert.Equal(TimeSpan.FromMinutes(30), o.TogetherOpen);
        Assert.Equal(1, o.TogetherOpenDays);
        Assert.Equal(TimeSpan.FromHours(8), o.AlwaysOpenAbove);
        Assert.Equal(0.5, o.GroupJoinFraction);
        Assert.Equal(15, o.MaxSuggestions);
    }

    [Fact]
    public void Without_rule_levels_the_overall_level_drives_both_rules()
    {
        Assert.Equal(
            SessionSuggestionOptions.For(SuggestionSensitivity.High, SuggestionSensitivity.High, SuggestionSensitivity.High),
            SessionSuggestionOptions.For(SuggestionSensitivity.High));
    }

    [Fact]
    public void Used_together_level_changes_only_rule_A()
    {
        var o = SessionSuggestionOptions.For(SuggestionSensitivity.Balanced, usedTogether: SuggestionSensitivity.Low);
        var low = SessionSuggestionOptions.For(SuggestionSensitivity.Low);
        var balanced = SessionSuggestionOptions.Balanced;

        Assert.Equal(low.BurstGap, o.BurstGap);
        Assert.Equal(low.MinDaysTogether, o.MinDaysTogether);
        Assert.Equal(low.MinShare, o.MinShare);
        Assert.Equal(balanced.TogetherOpen, o.TogetherOpen);
        Assert.Equal(balanced.TogetherOpenDays, o.TogetherOpenDays);
        Assert.Equal(balanced.AlwaysOpenAbove, o.AlwaysOpenAbove);
        Assert.Equal(balanced.GroupJoinFraction, o.GroupJoinFraction);
        Assert.Equal(balanced.MaxSuggestions, o.MaxSuggestions);
    }

    [Fact]
    public void Open_together_level_changes_only_rule_B()
    {
        var o = SessionSuggestionOptions.For(SuggestionSensitivity.Balanced, openTogether: SuggestionSensitivity.High);
        var high = SessionSuggestionOptions.For(SuggestionSensitivity.High);
        var balanced = SessionSuggestionOptions.Balanced;

        Assert.Equal(high.TogetherOpen, o.TogetherOpen);
        Assert.Equal(high.TogetherOpenDays, o.TogetherOpenDays);
        Assert.Equal(high.AlwaysOpenAbove, o.AlwaysOpenAbove);
        Assert.Equal(balanced.BurstGap, o.BurstGap);
        Assert.Equal(balanced.MinDaysTogether, o.MinDaysTogether);
        Assert.Equal(balanced.MinShare, o.MinShare);
        Assert.Equal(balanced.GroupJoinFraction, o.GroupJoinFraction);
        Assert.Equal(balanced.MaxSuggestions, o.MaxSuggestions);
    }

    [Fact]
    public void Open_together_Low_over_Balanced_changes_only_rule_B()
    {
        var o = SessionSuggestionOptions.For(SuggestionSensitivity.Balanced, openTogether: SuggestionSensitivity.Low);
        var low = SessionSuggestionOptions.For(SuggestionSensitivity.Low);
        var balanced = SessionSuggestionOptions.Balanced;

        Assert.Equal(low.TogetherOpen, o.TogetherOpen);
        Assert.Equal(2, o.TogetherOpenDays);
        Assert.Equal(low.AlwaysOpenAbove, o.AlwaysOpenAbove);
        Assert.Equal(balanced.BurstGap, o.BurstGap);
        Assert.Equal(balanced.MinDaysTogether, o.MinDaysTogether);
        Assert.Equal(balanced.MinShare, o.MinShare);
        Assert.Equal(balanced.GroupJoinFraction, o.GroupJoinFraction);
        Assert.Equal(balanced.MaxSuggestions, o.MaxSuggestions);
    }

    [Theory]
    [InlineData(SuggestionSensitivity.Low)]
    [InlineData(SuggestionSensitivity.Balanced)]
    [InlineData(SuggestionSensitivity.High)]
    public void Fixed_values_are_the_same_at_every_level(SuggestionSensitivity level)
    {
        var o = SessionSuggestionOptions.For(level);
        Assert.Equal(TimeSpan.FromMinutes(5), o.SampleInterval);
        Assert.Equal(2, o.MinGroupFiles);
        Assert.Equal(15, o.MaxGroupFiles);
        Assert.Equal(40, o.MaxBurstFiles);
        Assert.Equal(14, o.HistoryDays);
    }

    [Theory]
    [InlineData("low", SuggestionSensitivity.Low)]
    [InlineData("LOW", SuggestionSensitivity.Low)]
    [InlineData(" high ", SuggestionSensitivity.High)]
    [InlineData("Balanced", SuggestionSensitivity.Balanced)]
    public void Parse_is_lenient_about_case_and_spaces(string text, SuggestionSensitivity expected)
        => Assert.Equal(expected, SuggestionSensitivities.Parse(text));

    [Theory]
    [InlineData("medium")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Parse_rejects_anything_else(string? text)
        => Assert.Null(SuggestionSensitivities.Parse(text));

    [Theory]
    [InlineData(SuggestionSensitivity.Low, "low")]
    [InlineData(SuggestionSensitivity.Balanced, "balanced")]
    [InlineData(SuggestionSensitivity.High, "high")]
    public void Format_is_lower_case_and_round_trips(SuggestionSensitivity level, string text)
    {
        Assert.Equal(text, SuggestionSensitivities.Format(level));
        Assert.Equal(level, SuggestionSensitivities.Parse(text));
    }
}
