using System;
using System.Collections.Generic;
using QuickerPlaces.Services.Revit.Handlers;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>A process table a test fills in by hand: process id to start time (UTC).</summary>
public sealed class FakeProcessProbe : IProcessProbe
{
    public Dictionary<int, DateTime> Running { get; } = [];

    public DateTime? GetStartTimeUtc(int processId) => Running.TryGetValue(processId, out var started) ? started : null;
}
