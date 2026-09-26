namespace QuickerPlaces.Models.Activity;

/// <summary>
/// Which folder a visit deep under a tracked root is credited to (Phase 9
/// plan D8). Observing C:\Jobs\Acme\Drawings\Rev3 under root C:\Jobs:
/// </summary>
public enum RollupMode
{
    /// <summary>C:\Jobs\Acme, the immediate child of the root. The default: a short list that stays readable across a month.</summary>
    RootChild,

    /// <summary>C:\Jobs\Acme\Drawings\Rev3, the folder itself.</summary>
    Exact,

    /// <summary>The ancestor <c>Depth</c> levels below the root; a depth of 1 is <see cref="RootChild"/>.</summary>
    Depth
}
