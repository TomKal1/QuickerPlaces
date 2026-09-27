using System;

namespace QuickerPlaces.Services.Activity;

/// <summary>
/// Whether the person is at the machine (Phase 9 plan D7). The app's
/// implementation reads <c>GetLastInputInfo</c> and <c>SessionSwitch</c>;
/// the tracker only ever sees this seam.
/// </summary>
public interface IUserPresence
{
    /// <summary>How long since the last keyboard or mouse input.</summary>
    TimeSpan IdleFor { get; }

    /// <summary>True while the Windows session is locked.</summary>
    bool SessionLocked { get; }
}
