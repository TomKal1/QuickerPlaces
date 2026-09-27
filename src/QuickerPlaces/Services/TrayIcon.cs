using System;
using System.Drawing;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Views;
using Forms = System.Windows.Forms;

namespace QuickerPlaces.Services;

/// <summary>The user-visible tray controls for optional background coverage.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly MainWindow _window;
    private readonly ActivityStore _store;
    private readonly ActivityTrackingHost _host;
    private readonly Action _indicatorChanged;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Icon? _ownedIcon;

    public TrayIcon(MainWindow window, ActivityStore store, ActivityTrackingHost host, Action indicatorChanged)
    {
        _window = window;
        _store = store;
        _host = host;
        _indicatorChanged = indicatorChanged;
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            try { _ownedIcon = Icon.ExtractAssociatedIcon(path); }
            catch (ArgumentException) { /* The system icon below is enough. */ }
        }
        _pauseItem = new Forms.ToolStripMenuItem("Pause tracking");
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open QuickerPlaces", null, (_, _) => _window.BringToFront());
        menu.Items.Add(_pauseItem);
        menu.Items.Add("Exit", null, (_, _) => _window.ExitFromTray());
        _pauseItem.Click += (_, _) =>
        {
            _host.SetTrackingPaused(!_host.IsPaused);
            Refresh(_window.Settings);
            _indicatorChanged();
        };
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _ownedIcon ?? SystemIcons.Application,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => _window.BringToFront();
    }

    public void Refresh(AppSettings settings)
    {
        _notifyIcon.Visible = settings.MinimizeToTray || settings.StartWithWindows;
        var count = _store.EnabledRoots().Count;
        _pauseItem.Enabled = count > 0;
        _pauseItem.Text = _host.IsPaused ? "Resume tracking" : "Pause tracking";
        _notifyIcon.Text = _host.IsPaused ? "QuickerPlaces — tracking paused"
            : count > 0 ? $"QuickerPlaces — tracking {count} {(count == 1 ? "root" : "roots")}"
            : "QuickerPlaces — no roots tracked";
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _ownedIcon?.Dispose();
    }
}
