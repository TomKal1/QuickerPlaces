using System;
using System.Globalization;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The texts one row of the Recently Deleted dialog shows (plan 5.5):
/// alias, type, destination, the deletion time in local time, and the
/// countdown from RecentlyDeletedPolicy with its expiry tooltip. Every row
/// here gets a fixed zone, so no assertion depends on the machine's own.
/// </summary>
public sealed class RecentlyDeletedRowViewModelTests
{
    private static readonly DateTimeOffset DeletedAt = new(2026, 9, 24, 17, 45, 0, TimeSpan.Zero);

    private static Place Deleted(string alias = "Reports", PlaceType type = PlaceType.Folder, string? resource = null) => new()
    {
        Alias = alias,
        Type = type,
        Resource = resource ?? TestPaths.Folder(alias),
        DeletedAt = DeletedAt
    };

    [Fact]
    public void Row_ShowsThePlace_AsTheMainGridDoes()
    {
        var folder = new RecentlyDeletedRowViewModel(Deleted("Reports"), DeletedAt, TestZones.PlusTen);
        var url = new RecentlyDeletedRowViewModel(Deleted("Wiki", PlaceType.Url, "https://wiki.example.com"), DeletedAt, TestZones.PlusTen);

        Assert.Equal("Reports", folder.Alias);
        Assert.Equal("Folder", folder.TypeLabel);
        Assert.Equal(PlaceViewModel.GlyphFor(PlaceType.Folder), folder.TypeGlyph);
        Assert.Equal(TestPaths.Folder("Reports"), folder.Resource);

        Assert.Equal("URL", url.TypeLabel);
        Assert.Equal(PlaceViewModel.GlyphFor(PlaceType.Url), url.TypeGlyph);
        Assert.Equal("https://wiki.example.com", url.Resource);
    }

    /// <summary>The Deleted column is local wall-clock time in the given zone, not the stored UTC value.</summary>
    [Fact]
    public void DeletedLocal_IsTheUtcInstantInTheLocalZone()
    {
        var plusTen = new RecentlyDeletedRowViewModel(Deleted(), DeletedAt, TestZones.PlusTen);
        var minusFive = new RecentlyDeletedRowViewModel(Deleted(), DeletedAt, TestZones.MinusFive);

        Assert.Equal(new DateTime(2026, 9, 25, 3, 45, 0), plusTen.DeletedLocal);
        Assert.Equal(new DateTime(2026, 9, 24, 12, 45, 0), minusFive.DeletedLocal);
        Assert.Equal(DeletedAt, plusTen.DeletedAt);
    }

    /// <summary>The Deleted column's text is the "g" format (short date and time) in the user's culture.</summary>
    [Fact]
    public void DeletedText_IsTheGeneralShortFormat_InTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var row = new RecentlyDeletedRowViewModel(Deleted(), DeletedAt, TestZones.PlusTen);
            Assert.Equal("09/25/2026 03:45", row.DeletedText);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>The countdown is RecentlyDeletedPolicy's, measured from the given instant (D13).</summary>
    [Theory]
    [InlineData(0L, "7 days", 7)]
    [InlineData(TimeSpan.TicksPerDay * 3, "4 days", 4)]
    [InlineData(TimeSpan.TicksPerDay * 6 + 1, "1 day", 1)]
    [InlineData(TimeSpan.TicksPerDay * 7, "Expiring", 0)]
    public void DaysRemaining_ComesFromThePolicy(long ticksAfterDeletion, string text, int days)
    {
        var now = DeletedAt + TimeSpan.FromTicks(ticksAfterDeletion);
        var row = new RecentlyDeletedRowViewModel(Deleted(), now, TestZones.PlusTen);

        Assert.Equal(text, row.DaysRemainingText);
        Assert.Equal(days, row.DaysRemaining);
        Assert.Equal(RecentlyDeletedPolicy.DaysRemainingText(DeletedAt, now), row.DaysRemainingText);
        Assert.Equal(now, row.Now);
    }

    /// <summary>The tooltip gives the exact expiry in local time, "at least" because the purge waits for the next start or save (D14); past it, that the place can still be restored.</summary>
    [Fact]
    public void ExpiryToolTip_GivesTheLocalExpiry_AndSaysWhenItHasPassed()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var waiting = new RecentlyDeletedRowViewModel(Deleted(), DeletedAt + TimeSpan.FromDays(1), TestZones.PlusTen);
            Assert.False(waiting.IsExpired);
            Assert.Equal(DeletedAt + TimeSpan.FromDays(7), waiting.ExpiresAt);
            Assert.Equal(
                "Kept until at least 10/02/2026 03:45, then deleted for good the next time QuickerPlaces starts or saves.",
                waiting.ExpiryToolTip);

            var expired = new RecentlyDeletedRowViewModel(Deleted(), DeletedAt + TimeSpan.FromDays(7), TestZones.PlusTen);
            Assert.True(expired.IsExpired);
            Assert.Equal(
                "Its 7 days ended 10/02/2026 03:45. It will be deleted for good the next time QuickerPlaces starts or saves; restore it now to keep it.",
                expired.ExpiryToolTip);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>A row is only ever built for a place in Recently Deleted.</summary>
    [Fact]
    public void Row_RefusesAnActivePlace()
    {
        var active = new Place { Alias = "Docs", Type = PlaceType.Folder, Resource = TestPaths.Folder("Docs") };

        Assert.Throws<ArgumentException>(() => new RecentlyDeletedRowViewModel(active, DeletedAt, TestZones.PlusTen));
    }
}
