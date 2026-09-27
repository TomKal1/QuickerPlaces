using System;
using System.Runtime.InteropServices;

namespace QuickerPlaces.Services.Activity;

// COM connection points QueryInterface these public, visible interfaces on
// their sinks. Keep them separate from the probe's private STA state.

[ComVisible(true), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
[Guid("FE4106E0-399A-11D0-A48C-00A0C90A8F39")]
public interface IShellWindowEvents
{
    [DispId(200)] void WindowRegistered(int cookie);
    [DispId(201)] void WindowRevoked(int cookie);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class ShellWindowEventsSink : IShellWindowEvents
{
    private readonly Action _changed;

    public ShellWindowEventsSink(Action changed) => _changed = changed;

    public void WindowRegistered(int cookie) => _changed();
    public void WindowRevoked(int cookie) => _changed();
}

[ComVisible(true), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
[Guid("34A715A0-6587-11D0-924A-0020AFC7AC4D")]
public interface IBrowserNavigationEvents
{
    [DispId(252)] void NavigateComplete2(
        [MarshalAs(UnmanagedType.IDispatch)] object browser,
        [In, MarshalAs(UnmanagedType.Struct)] ref object url);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class BrowserNavigationEventsSink : IBrowserNavigationEvents
{
    private readonly Action _navigated;

    public BrowserNavigationEventsSink(Action navigated) => _navigated = navigated;

    public void NavigateComplete2(object browser, ref object url) => _navigated();
}
