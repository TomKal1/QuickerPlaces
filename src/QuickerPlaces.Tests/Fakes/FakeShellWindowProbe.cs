using System.Collections.Generic;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>
/// Scriptable IShellWindowProbe for the tracker's tests: a test sets which
/// Explorer windows are "open" before each tick. No test touches COM or a
/// real Explorer window.
/// </summary>
public sealed class FakeShellWindowProbe : IShellWindowProbe
{
    /// <summary>What the next Sample returns.</summary>
    public List<ShellWindowSnapshot> Windows { get; } = new();

    /// <summary>How many times Sample was called.</summary>
    public int Samples { get; private set; }

    /// <summary>Replaces the open windows with one foreground window showing <paramref name="path"/>.</summary>
    public void ShowForeground(string path, nint hwnd = 1)
    {
        Windows.Clear();
        Windows.Add(new ShellWindowSnapshot(path, hwnd, true));
    }

    /// <summary>Closes every window.</summary>
    public void CloseAll() => Windows.Clear();

    public IReadOnlyList<ShellWindowSnapshot> Sample()
    {
        Samples++;
        return Windows.ToArray();
    }
}
