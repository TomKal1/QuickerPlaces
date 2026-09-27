using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

/// <summary>Opt-in root management, period totals, and the year calendar.</summary>
public partial class ActivityWindow : Window
{
    private const int FirstYearOption = 2026;
    private const int LastYearOption = 2100;
    private readonly ActivityViewModel _viewModel;
    private readonly INetworkDriveResolver _networkDrives;
    private readonly Action<string, Window> _addAsPlace;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _returnRefreshTimer;
    private readonly DispatcherTimer _statusTimer;
    private string _sortKey = nameof(ActivityFolderRow.LastVisited);
    private ListSortDirection _sortDirection = ListSortDirection.Descending;

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
        Loaded += (_, _) =>
        {
            ApplyActivityView(nameof(ActivityFolderRow.LastVisited), ListSortDirection.Descending);
            RevealSelectedPeriod();
        };
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            _viewModel.DismissStatus();
        };
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
            _statusTimer.Stop();
        };
    }

    private void AddRoot_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose a folder to track" };
        if (picker.ShowDialog(this) != true) return;
        var equivalents = AddRootDialog.Show(this, picker.FolderName, _networkDrives);
        if (equivalents is not null)
            _viewModel.AddRoot(picker.FolderName, equivalents);
    }

    private void TrackedFoldersList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(TrackedFoldersList, source) is not ListBoxItem { DataContext: ActivityRootSnapshot root })
        {
            e.Handled = true;
            return;
        }

        _viewModel.SelectedRoot = root;
        var menu = TrackedFoldersList.ContextMenu;
        if (menu is null) return;
        foreach (var item in menu.Items)
            if (item is MenuItem action)
            {
                if (Equals(action.Tag, "ToggleTracking"))
                    action.Header = root.Enabled ? "Stop tracking" : "Resume tracking";
                action.IsEnabled = _viewModel.CanManage || Equals(action.Tag, "ReadOnly");
            }
    }

    private void EditTrackedFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedRoot is null) return;
        var dialog = new ActivityFolderSettingsDialog(this, _viewModel);
        if (dialog.ShowDialog() == true && _viewModel.StatusMessage is not null)
        {
            _statusTimer.Stop();
            _statusTimer.Start();
        }
    }

    private void AboutTrackedFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedRoot is null) return;
        MessageForm.Show(_viewModel.AboutSelectedFolderText, "About tracked folder", owner: this);
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedRoot is not null)
            _viewModel.ToggleSelected();
    }

    private void DismissStatus_Click(object sender, RoutedEventArgs e)
    {
        _statusTimer.Stop();
        _viewModel.DismissStatus();
    }

    private void StatusRibbon_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        => _statusTimer.Stop();

    private void StatusRibbon_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_viewModel.StatusMessage is not null && !StatusRibbon.IsKeyboardFocusWithin)
            _statusTimer.Start();
    }

    private void StatusRibbon_IsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (StatusRibbon.IsKeyboardFocusWithin)
            _statusTimer.Stop();
        else if (_viewModel.StatusMessage is not null && !StatusRibbon.IsMouseOver)
            _statusTimer.Start();
    }

    private void RetrySave_Click(object sender, RoutedEventArgs e) => _viewModel.RetrySave();

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedRoot is not { } selected) return;
        var message = $"Delete \"{selected.Path}\" and all of its recorded activity? This cannot be undone. To keep the data, use Stop tracking instead.";
        if (MessageForm.ShowDestructiveConfirm(message, "Delete tracked folder",
                "Delete folder and its data", this))
            _viewModel.DeleteSelected();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Week_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetPeriodMode(ActivityPeriodMode.Week);
        RevealSelectedPeriod();
    }

    private void Month_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetPeriodMode(ActivityPeriodMode.Month);
        RevealSelectedPeriod();
    }

    private void Day_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetPeriodMode(ActivityPeriodMode.Day);
        RevealSelectedPeriod();
    }

    private void ActivityWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || !_viewModel.HasSelection || CalendarYearPopup.IsOpen ||
            TrackedFoldersList.IsKeyboardFocusWithin || ActivityGrid.IsKeyboardFocusWithin)
            return;

        var direction = e.Key switch
        {
            Key.Left or Key.Up => -1,
            Key.Right or Key.Down => 1,
            _ => 0
        };
        if (direction == 0) return;

        var horizontal = e.Key is Key.Left or Key.Right;
        if (!_viewModel.MoveCalendarSelection(direction, horizontal)) return;

        e.Handled = true;
        CalendarScroll.Focus();
        RevealSelectedPeriod();
    }

    private void CalendarYearButton_Click(object sender, RoutedEventArgs e)
    {
        CalendarYearList.ItemsSource = Enumerable.Range(FirstYearOption, LastYearOption - FirstYearOption + 1);
        CalendarYearList.SelectedItem = Math.Clamp(_viewModel.CalendarYear, FirstYearOption, LastYearOption);
        CalendarYearPopup.IsOpen = true;
        Dispatcher.InvokeAsync(() =>
        {
            CalendarYearList.ScrollIntoView(CalendarYearList.SelectedItem);
            CalendarYearList.Focus();
        }, DispatcherPriority.Input);
    }

    private void CalendarYearList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(CalendarYearList, source) is ListBoxItem { Content: int year })
        {
            ApplyCalendarYear(year);
            e.Handled = true;
        }
    }

    private void CalendarYearList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && CalendarYearList.SelectedItem is int year)
        {
            ApplyCalendarYear(year);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CalendarYearPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void ApplyCalendarYear(int year)
    {
        if (!_viewModel.SelectCalendarYear(year)) return;
        CalendarYearPopup.IsOpen = false;
        RevealSelectedPeriod();
    }

    private void PreviousPeriod_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.MovePeriod(-1);
        RevealSelectedPeriod();
    }

    private void NextPeriod_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.MovePeriod(1);
        RevealSelectedPeriod();
    }

    private void AddAsPlace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ActivityFolderRow row })
            _addAsPlace(row.Folder, this);
    }

    private void CalendarDay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ActivityCalendarCell { Date: { } date, IsInRange: true } })
            return;
        _viewModel.SelectCalendarDate(date);
        CalendarScroll.Focus();
        RevealSelectedPeriod();
    }

    private void RevealSelectedPeriod()
        => Dispatcher.InvokeAsync(RevealSelectedPeriodCore, DispatcherPriority.Loaded);

    private void RevealSelectedPeriodCore()
    {
        if (_viewModel.CalendarYear != _viewModel.PeriodFrom.Year &&
            _viewModel.CalendarYear != _viewModel.PeriodTo.Year)
        {
            CalendarScroll.ScrollToHorizontalOffset(0);
            return;
        }

        var target = _viewModel.PeriodFrom.AddDays(
            (_viewModel.PeriodTo.DayNumber - _viewModel.PeriodFrom.DayNumber) / 2);
        if (target.Year != _viewModel.CalendarYear)
            target = _viewModel.CalendarYear == _viewModel.PeriodFrom.Year
                ? _viewModel.PeriodFrom : _viewModel.PeriodTo;
        var weeks = _viewModel.CalendarWeeks;
        for (var index = 0; index < weeks.Count; index++)
        {
            if (target < weeks[index].StartsOn || target >= weeks[index].StartsOn.AddDays(7))
                continue;
            CalendarScroll.UpdateLayout();
            var center = (index + 0.5) * 14;
            var viewport = CalendarScroll.ViewportWidth;
            if (viewport > 0 && (center < CalendarScroll.HorizontalOffset + 24 ||
                center > CalendarScroll.HorizontalOffset + viewport - 24))
            {
                CalendarScroll.ScrollToHorizontalOffset(Math.Max(0, center - viewport / 2));
                CalendarScroll.UpdateLayout();
            }
            break;
        }
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

        _sortKey = key;
        _sortDirection = next;
        ApplyActivityView(key, next);
        e.Column.SortDirection = next;
    }

    private void GroupByLevel_Changed(object sender, RoutedEventArgs e)
    {
        if (ActivityGrid is null || GroupByLevel is null)
            return;
        ApplyActivityView(_sortKey, _sortDirection);
    }

    private void ApplyActivityView(string key, ListSortDirection direction)
    {
        var view = CollectionViewSource.GetDefaultView(ActivityGrid.ItemsSource);
        if (view is null) return;
        using (view.DeferRefresh())
        {
            view.GroupDescriptions.Clear();
            view.SortDescriptions.Clear();
            if (GroupByLevel.IsChecked == true)
            {
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ActivityFolderRow.LevelLabel)));
                view.SortDescriptions.Add(new SortDescription(nameof(ActivityFolderRow.Level), ListSortDirection.Ascending));
            }
            view.SortDescriptions.Add(new SortDescription(key, direction));
            if (key != nameof(ActivityFolderRow.Folder))
                view.SortDescriptions.Add(new SortDescription(nameof(ActivityFolderRow.Folder), ListSortDirection.Ascending));
        }
        foreach (var column in ActivityGrid.Columns)
            column.SortDirection = column.SortMemberPath == key ? direction : null;
    }
}
