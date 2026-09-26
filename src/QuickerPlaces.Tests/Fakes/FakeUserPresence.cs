using System;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>An IUserPresence a test sets by hand. Starts present: no idle time, unlocked.</summary>
public sealed class FakeUserPresence : IUserPresence
{
    public TimeSpan IdleFor { get; set; }

    public bool SessionLocked { get; set; }
}
