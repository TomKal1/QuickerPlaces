namespace QuickerPlaces.Services.Activity;

/// <summary>Something that happened outside the tracking loop and changes what it should do (Phase 9 plan D6, D26).</summary>
public enum TrackingSignal
{
    /// <summary>The Windows session was locked (SessionSwitch).</summary>
    Locked,

    /// <summary>The Windows session was unlocked.</summary>
    Unlocked,

    /// <summary>The machine is going to sleep (PowerModeChanged).</summary>
    Suspending,

    /// <summary>The machine woke up.</summary>
    Resumed,

    /// <summary>A root was added, edited, stopped, resumed or deleted.</summary>
    RootsChanged
}
