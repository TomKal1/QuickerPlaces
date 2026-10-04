using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using QuickerPlaces.Models;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.ViewModels;
using QuickerPlaces.Views.Panels;

namespace QuickerPlaces.Views;

/// <summary>
/// The Library (documents plan §6): the window around the Year activity and
/// File shelf panels (configurable canvas plan M2), which share one
/// <see cref="LibraryViewModel"/>, with Recent Files' settings
/// (<see cref="RecentFilesSettings"/>) below. This code-behind asks Recent
/// Files to read Recent Items at once when it opens so what was just opened
/// is listed, and keeps the list current while it is open through
/// <see cref="LibraryRefresh"/>.
/// </summary>
public partial class LibraryWindow : Window
{
    private readonly LibraryViewModel _viewModel;
    private readonly RecentFilesHost _recentFilesHost;
    private readonly LibraryRefresh _refresh;

    private LibraryWindow(Window owner, LibraryViewModel viewModel, RecentFilesHost recentFilesHost, ActivityTrackingHost activityHost)
    {
        InitializeComponent();
        Owner = owner;
        _viewModel = viewModel;
        _recentFilesHost = recentFilesHost;
        DataContext = viewModel;
        InputBindings.Add(new KeyBinding(new RelayCommand(viewModel.ClearPeriod, () => viewModel.HasPeriod), Key.Escape, ModifierKeys.None));
        _refresh = new LibraryRefresh(Dispatcher, viewModel, recentFilesHost, activityHost);
        Closed += (_, _) => _refresh.Dispose();

        Loaded += async (_, _) =>
        {
            Shelf.FocusSearch();
            try
            {
                await Task.Run(_recentFilesHost.RecordNow);
                _viewModel.Reload();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn($"Refreshing Recent Files for the Library failed ({ex.GetType().Name}).");
            }
        };
    }

    /// <summary>Shows the Library modally; <paramref name="placeOpened"/> lets the main grid show a place opened from here.</summary>
    public static void Show(Window owner, LibraryViewModel viewModel, RecentFilesHost recentFilesHost, ActivityTrackingHost activityHost,
        Action<Place, PersistenceResult> placeOpened)
    {
        viewModel.PlaceOpened += placeOpened;
        try
        {
            new LibraryWindow(owner, viewModel, recentFilesHost, activityHost).ShowDialog();
        }
        finally
        {
            viewModel.PlaceOpened -= placeOpened;
        }
    }
}
