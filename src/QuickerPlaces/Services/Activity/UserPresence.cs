using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace QuickerPlaces.Services.Activity;

/// <summary>Input idle duration and lock/power transitions for the host.</summary>
public sealed class UserPresence : IUserPresence, IDisposable
{
    private volatile bool _locked;
    private bool _disposed;

    public UserPresence()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public event Action<TrackingSignal>? Signal;

    public bool SessionLocked => _locked;

    public TimeSpan IdleFor
    {
        get
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref info))
                return TimeSpan.Zero;
            // Both values are 32-bit Windows uptime ticks, so subtraction
            // naturally handles wraparound after roughly 49 days.
            return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs args)
    {
        if (_disposed) return;
        if (args.Reason == SessionSwitchReason.SessionLock)
        {
            _locked = true;
            Signal?.Invoke(TrackingSignal.Locked);
        }
        else if (args.Reason == SessionSwitchReason.SessionUnlock)
        {
            _locked = false;
            Signal?.Invoke(TrackingSignal.Unlocked);
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (_disposed) return;
        if (args.Mode == PowerModes.Suspend)
            Signal?.Invoke(TrackingSignal.Suspending);
        else if (args.Mode == PowerModes.Resume)
            Signal?.Invoke(TrackingSignal.Resumed);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
