using System;
using System.Diagnostics;
using QuickerPlaces.Services.Revit.Handlers;
using Xunit;

namespace QuickerPlaces.Tests;

public sealed class SystemProcessProbeTests
{
    [Fact]
    public void TheCurrentProcess_HasItsOwnStartTime()
    {
        using var me = Process.GetCurrentProcess();

        var started = new SystemProcessProbe().GetStartTimeUtc(me.Id);

        Assert.NotNull(started);
        Assert.True((started.Value - me.StartTime.ToUniversalTime()).Duration() < TimeSpan.FromSeconds(1));
        Assert.Equal(DateTimeKind.Utc, started.Value.Kind);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(-5)]
    [InlineData(0)]
    public void NoSuchProcess_IsNullNotAnException(int id) => Assert.Null(new SystemProcessProbe().GetStartTimeUtc(id));
}
