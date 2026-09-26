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

    [Fact]
    public void TheZoneDecidesTheLocalDate()
        => Assert.Equal("24/09/2026 16:15", PlaceViewModel.FormatLastOpened(Opened, TestZones.MinusFive, CultureInfo.GetCultureInfo("en-GB")));
}
