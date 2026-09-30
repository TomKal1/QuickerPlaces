using QuickerPlaces.Services.Activity;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>The Recents panel's tracking line and the header's tooltip (Desk layout design §4, §6).</summary>
public sealed class ActivityFormatTests
{
    [Theory]
    [InlineData(2, 2, false, "Tracking 2 folders")]
    [InlineData(1, 3, false, "Tracking 1 folder")]
    [InlineData(2, 2, true, "Tracking paused")]
    [InlineData(0, 2, false, "Tracking is off for every folder")]
    [InlineData(0, 0, false, "No folders tracked")]
    public void TrackingSummary_SaysWhatIsTracked(int enabled, int all, bool paused, string expected)
        => Assert.Equal(expected, ActivityFormat.TrackingSummary(enabled, all, paused));
}
