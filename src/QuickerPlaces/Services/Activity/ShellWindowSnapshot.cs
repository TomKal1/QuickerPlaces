namespace QuickerPlaces.Services.Activity;

/// <summary>
/// One Explorer window (or tab) as a probe pass saw it (Phase 9 plan 5.1):
/// the path it reported, exactly as reported, its window handle, and
/// whether that handle was the foreground window. Tabs in one window share
/// a handle, so several snapshots can be foreground at once (D14).
/// </summary>
public sealed record ShellWindowSnapshot(string Path, nint Hwnd, bool IsForeground);
