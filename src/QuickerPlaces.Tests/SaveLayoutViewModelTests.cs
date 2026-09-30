using System;
using System.Globalization;
using QuickerPlaces.Models.Workspace;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The Save layout dialog (configurable canvas plan D3, M5): filters left
/// out unless ticked, said in words, and the current week or month kept
/// relative or fixed as the user chooses.
/// </summary>
public sealed class SaveLayoutViewModelTests
{
    // Friday 25 Sep 2026 local; the invariant culture's weeks start on Sunday.
    private readonly ManualTimeProvider _time = new();
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private SaveLayoutViewModel Dialog(WorkspaceQuery query) => SaveLayoutViewModel.ForSaveAsNew("Mine", query, _time, Culture);

    [Fact]
    public void Filters_AreLeftOut_UnlessTicked()
    {
        var dialog = Dialog(new WorkspaceQuery { Text = "acme" });

        Assert.True(dialog.CanIncludeFilters);
        Assert.False(dialog.IncludeFilters);
        Assert.Null(dialog.FiltersToSave());

        dialog.IncludeFilters = true;
        Assert.Equal("acme", dialog.FiltersToSave()!.Text);
    }

    [Fact]
    public void WithNoFilters_ThereIsNothingToInclude()
    {
        var dialog = Dialog(WorkspaceQuery.Default);

        dialog.IncludeFilters = true;

        Assert.False(dialog.CanIncludeFilters);
        Assert.False(dialog.IncludeFilters);
        Assert.Equal("No filters: everything is shown.", dialog.FiltersSummary);
    }

    [Fact]
    public void TheSummary_NamesEachFilter()
    {
        var query = new WorkspaceQuery
        {
            Text = " tower ",
            Kind = "pdf",
            Source = "recent",
            Tag = "markups",
            Date = DateRule.Between(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)),
        };

        Assert.Equal("Search “tower” · PDFs · Recent only · Sessions tagged “markups” · Used 2–8 Mar 2026",
            Dialog(query).FiltersSummary);
        Assert.Equal("This month", SaveLayoutViewModel.Describe(new WorkspaceQuery { Date = DateRule.ThisMonth() }, _time, Culture));
    }

    [Fact]
    public void ThisWeeksDays_CanBeKeptAsThisWeek_OrAsThoseDays()
    {
        var dialog = Dialog(new WorkspaceQuery { Date = DateRule.Between(new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 26)) });
        Assert.False(dialog.ShowsDateChoice);

        dialog.IncludeFilters = true;

        Assert.True(dialog.ShowsDateChoice);
        Assert.Equal(DateRuleKind.ThisWeek, dialog.FiltersToSave()!.Date.Kind);
        dialog.KeepFixed = true;
        var fixedDates = dialog.FiltersToSave()!.Date;
        Assert.Equal((DateRuleKind.Range, new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 26)), (fixedDates.Kind, fixedDates.From, fixedDates.To));
    }

    [Fact]
    public void ThisMonthsDays_CanBeKeptAsThisMonth()
    {
        var dialog = Dialog(new WorkspaceQuery { Date = DateRule.Between(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)) });
        dialog.IncludeFilters = true;

        Assert.Equal("This month, whichever month it is", dialog.RelativeDatesText);
        Assert.Equal(DateRuleKind.ThisMonth, dialog.FiltersToSave()!.Date.Kind);
    }

    [Fact]
    public void AnOlderWeek_IsNotOfferedAsThisWeek()
    {
        var dialog = Dialog(new WorkspaceQuery { Date = DateRule.Between(new DateOnly(2026, 9, 13), new DateOnly(2026, 9, 19)) });
        dialog.IncludeFilters = true;

        Assert.False(dialog.ShowsDateChoice);
        Assert.Equal(DateRuleKind.Range, dialog.FiltersToSave()!.Date.Kind);
    }

    [Fact]
    public void Rename_AsksOnlyForAName()
    {
        var dialog = SaveLayoutViewModel.ForRename("Mine");

        Assert.True(dialog.IsRename);
        Assert.Equal("Rename layout", dialog.Title);
        Assert.False(dialog.CanIncludeFilters);
        Assert.Null(dialog.FiltersToSave());
    }
}
