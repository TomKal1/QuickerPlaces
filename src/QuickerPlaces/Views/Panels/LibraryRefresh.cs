using System;
using System.Windows.Threading;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

/// <summary>
/// Keeps a <see cref="LibraryViewModel"/> current while a view of it is open
/// (configurable canvas plan M2): new Recent Files opens and Recents wakes
/// ask for a reload, from whatever thread they happen on, and at most one
/// reload runs per <see cref="Throttle"/> on the UI thread. The view model
/// leaves its rows alone when a reload changes nothing shown.
///
/// Dispose it when the view goes away: it unsubscribes from both hosts, so a
/// closed window or removed panel is not kept alive or refreshed.
/// </summary>
public sealed class LibraryRefresh : IDisposable
{
    /// <summary>The shortest gap between two reloads: tracking wakes far more often than what it records changes.</summary>
    public static readonly TimeSpan Throttle = TimeSpan.FromSeconds(30);

    private readonly Dispatcher _dispatcher;
    private readonly LibraryViewModel _viewModel;
    private readonly RecentFilesHost? _recentFiles;
    private readonly ActivityTrackingHost? _activity;
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    public LibraryRefresh(Dispatcher dispatcher, LibraryViewModel viewModel, RecentFilesHost? recentFiles, ActivityTrackingHost? activity)
    {
        _dispatcher = dispatcher;
        _viewModel = viewModel;
        _recentFiles = recentFiles;
        _activity = activity;
        _timer = new DispatcherTimer(Throttle, DispatcherPriority.Background, OnTick, dispatcher) { IsEnabled = false };

        if (_recentFiles is not null)
            _recentFiles.Recorded += OnRecorded;
        if (_activity is not null)
            _activity.WakeCompleted += OnWake;
    }

    /// <summary>Asks for a reload within <see cref="Throttle"/>; safe from any thread.</summary>
    public void Request() => _dispatcher.BeginInvoke(() =>
    {
        if (!_disposed && !_timer.IsEnabled)
            _timer.Start();
    });

    private void OnRecorded(int added) => Request();

    private void OnWake(ActivityTrackingLoop loop) => Request();

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        if (!_disposed)
            _viewModel.Reload();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _timer.Stop();
        if (_recentFiles is not null)
            _recentFiles.Recorded -= OnRecorded;
        if (_activity is not null)
            _activity.WakeCompleted -= OnWake;
    }
}
