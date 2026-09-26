using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

/// <summary>Opt-in root management, period totals, and the year calendar.</summary>
public partial class ActivityWindow : Window
{
    private readonly ActivityViewModel _viewModel;
    private readonly INetworkDriveResolver _networkDrives;
    private readonly Action<string, Window> _addAsPlace;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _returnRefreshTimer;

    public ActivityWindow(Window owner, ActivityStore store, ActivityTrackingHost host,
        INetworkDriveResolver networkDrives, Action indicatorChanged, Action<string, Window> addAsPlace)
    {
        InitializeComponent();
        Owner = owner;
        _networkDrives = networkDrives;
        _addAsPlace = addAsPlace;
        _viewModel = new ActivityViewModel(store, () =>
        {
            host.RootsChanged();
            indicatorChanged();
        });
        DataContext = _viewModel;
        DayPicker.DisplayDateEnd = DateTime.Today;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += (_, _) => _viewModel.RefreshPeriod();
        _refreshTimer.Start();
        _returnRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _returnRefreshTimer.Tick += (_, _) =>
        {
            _returnRefreshTimer.Stop();
            _viewModel.RefreshPeriod();
        };
        Activated += (_, _) =>
        {
            _viewModel.RefreshPeriod();
            // The host can record Explorer's final foreground interval on its next 1.5 s tick.
            _returnRefreshTimer.Stop();
            _returnRefreshTimer.Start();
        };
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _returnRefreshTimer.Stop();
        };
    }

    private void AddRoot_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose a root folder to track" };
        if (picker.ShowDialog(this) != true) return;
        var equivalents = AddRootDialog.Show(this, picker.FolderName, _networkDrives);
        if (equivalents is not null)
            _viewModel.AddRoot(picker.FolderName, equivalents);
    }

    private void Toggle_Click(object sender, RoutedEventArgs e) => _viewModel.ToggleSelected();

    private void SaveSettings_Click(object sender, RoutedEventArgs e) => _viewModel.SaveSelectedSettings();

    private void RetrySave_Click(object sender, RoutedEventArgs e) => _viewModel.RetrySave();

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedRoot is not { } selected) return;
        var message = $"Delete \"{selected.Path}\" and all of its recorded activity? This cannot be undone. To keep the data, use Stop tracking instead.";
        if (MessageForm.ShowDestructiveConfirm(message, "Delete tracked root",
                "Delete root and its data", this))
            _viewModel.DeleteSelected();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Week_Click(object sender, RoutedEventArgs e)
        => _viewModel.SetPeriodMode(ActivityPeriodMode.Week);

    private void Month_Click(object sender, RoutedEventArgs e)
        => _viewModel.SetPeriodMode(ActivityPeriodMode.Month);

    private void PreviousPeriod_Click(object sender, RoutedEventArgs e)
        => _viewModel.MovePeriod(-1);

    private void NextPeriod_Click(object sender, RoutedEventArgs e)
        => _viewModel.MovePeriod(1);

    private void DayPicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DayPicker.SelectedDate is { } day && _viewModel is not null)
            _viewModel.ShowDay(DateOnly.FromDateTime(day));
    }

    private void AddAsPlace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ActivityFolderRow row })
            _addAsPlace(row.Folder, this);
    }

    private void CalendarDay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ActivityCalendarCell { Date: { } date, IsTracked: true } })
            return;
        _viewModel.ShowDay(date);
        DayPicker.SelectedDate = date.ToDateTime(TimeOnly.MinValue);
        ActivityTab.IsSelected = true;
    }

    private void ActivityGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var key = e.Column.SortMemberPath;
        if (string.IsNullOrEmpty(key)) return;
        var next = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : e.Column.SortDirection == ListSortDirection.Descending
                ? ListSortDirection.Ascending
                : key == nameof(ActivityFolderRow.Folder)
                    ? ListSortDirection.Ascending
                    : ListSortDirection.Descending;

        foreach (var column in ActivityGrid.Columns)
            column.SortDirection = null;
        var view = CollectionViewSource.GetDefaultView(ActivityGrid.ItemsSource);
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(key, next));
        e.Column.SortDirection = next;
    }
}
