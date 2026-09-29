using System;
using System.Globalization;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Test 39 from the Phase 3 plan's section 7: the Last Opened column's text
/// (D31). The zone and culture are parameters, so neither comes from the
/// machine running the test.
/// </summary>
public sealed class PlaceViewModelTests
{
    private static readonly DateTimeOffset Opened = new(2026, 9, 24, 21, 15, 0, TimeSpan.Zero);

    [Fact]
    public void NeverOpened_IsAnEmDash()
        => Assert.Equal("—", PlaceViewModel.FormatLastOpened(null, TestZones.PlusTen, CultureInfo.GetCultureInfo("en-GB")));

    /// <summary>21:15 UTC is 07:15 the next morning at +10:00: local date and time, in the culture's short pattern.</summary>
    [Fact]
    public void Opened_IsLocalDateAndTime_InTheCulturesShortPattern()
    {
        Assert.Equal("25/09/2026 07:15", PlaceViewModel.FormatLastOpened(Opened, TestZones.PlusTen, CultureInfo.GetCultureInfo("en-GB")));

        var us = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal(new DateTime(2026, 9, 25, 7, 15, 0).ToString("g", us), PlaceViewModel.FormatLastOpened(Opened, TestZones.PlusTen, us));
    }

    /// <summary>The grid's star tooltip says what a click will do (the star itself is drawn by the view).</summary>
    [Theory]
    [InlineData(true, "Remove from favourites (Ctrl+D)")]
    [InlineData(false, "Add to favourites (Ctrl+D)")]
    public void FavouriteStar_ToolTipSaysWhatAClickDoes(bool isFavourite, string toolTip)
    {
        var place = new PlaceViewModel(new QuickerPlaces.Models.Place { Alias = "Docs", Resource = "https://docs.example.com", IsFavourite = isFavourite });

        Assert.Equal(toolTip, place.FavouriteToolTip);
    }

    /// <summary>A favourite card shows its Ctrl+number; only the first nine have one.</summary>
    [Theory]
    [InlineData(true, 0, "1")]
    [InlineData(true, 8, "9")]
    [InlineData(true, 9, null)]
    [InlineData(false, null, null)]
    public void FavouriteShortcut_IsTheCtrlNumber(bool isFavourite, int? order, string? expected)
    {
        var place = new PlaceViewModel(new QuickerPlaces.Models.Place { Alias = "Docs", Resource = @"C:\Docs", IsFavourite = isFavourite, FavouriteOrder = order });

        Assert.Equal(expected, place.FavouriteShortcut);
    }

    [Fact]
    public void ToolTipText_IsTheDestination()
    {
        var place = new PlaceViewModel(new QuickerPlaces.Models.Place { Alias = "Docs", Resource = @"C:\Docs" });

        Assert.Equal(@"C:\Docs", place.ToolTipText);
    }

    [Fact]
    public void TheZoneDecidesTheLocalDate()
        => Assert.Equal("24/09/2026 16:15", PlaceViewModel.FormatLastOpened(Opened, TestZones.MinusFive, CultureInfo.GetCultureInfo("en-GB")));
}
