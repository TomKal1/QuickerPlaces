using System.Collections.Generic;

namespace QuickerPlaces.Services.Activity;

/// <summary>
/// Reads which folders the open Explorer windows are showing (Phase 9 plan
/// 5.1). The event-driven COM implementation caches ShellWindows paths (D3)
/// so the tracker can sample without cross-process COM calls.
/// </summary>
public interface IShellWindowProbe
{
    /// <summary>
    /// Every open Explorer window and tab, with the path each reported as is.
    /// The tracker decides what is a folder under a root (D13) and what is
    /// ambiguous (D14), so the probe passes every window through, including
    /// shell locations and paths outside every root: filtering them first
    /// would hide a foreground tab that disagrees with another.
    /// </summary>
    IReadOnlyList<ShellWindowSnapshot> Sample();
}
