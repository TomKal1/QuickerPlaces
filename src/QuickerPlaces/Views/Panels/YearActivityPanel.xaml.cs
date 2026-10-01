using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

/// <summary>
/// The Activity panel (configurable canvas plan M2): a thin view over the
/// shared <see cref="LibraryViewModel"/> it gets as its DataContext. Hosted
/// by the Library window and by the workspace (M3). The Day / Week / Month
/// buttons sit in a column down the left; the title shows only while
/// <see cref="ShowsTitle"/> is set (the workspace's panel frame names the
/// panel itself); a year button opens a list of years. Picks the year strip
/// or, when the panel is too narrow for the year, the month view, and puts
/// the header's controls under its title when they don't fit beside it
/// (M4, D1): a narrow panel shows legible days instead of shrinking them.
/// </summary>
public partial class YearActivityPanel : UserControl
{
    /// <summary>The least room the title keeps beside the header's controls.</summary>
    private const double TitleRoom = 110;

    /// <summary>Whether the panel shows its own "ACTIVITY" title; a host whose frame already names it turns this off.</summary>
    public static readonly DependencyProperty ShowsTitleProperty = DependencyProperty.Register(
        nameof(ShowsTitle), typeof(bool), typeof(YearActivityPanel),
        new PropertyMetadata(true, (d, _) => ((YearActivityPanel)d).ShowsTitleChanged()));

    public bool ShowsTitle
    {
        get => (bool)GetValue(ShowsTitleProperty);
        set => SetValue(ShowsTitleProperty, value);
    }

    private LibraryViewModel? _watched;

    public YearActivityPanel()
    {
        InitializeComponent();
        SizeChanged += (_, _) => FitWidth();
        Loaded += (_, _) =>
        {
            Watch(ViewModel);
            FitWidth();
        };
        Unloaded += (_, _) => Watch(null);
        DataContextChanged += (_, _) =>
        {
            if (IsLoaded)
                Watch(ViewModel);
            FitWidth();
        };

        // The chip for a chosen period comes and goes, and the navigation changes with the view.
        HeaderControls.SizeChanged += (_, _) => FitHeader();
        UnitGroup.SizeChanged += (_, _) => FitHeader();
    }

    private void ShowsTitleChanged()
    {
        TitleText.Visibility = ShowsTitle ? Visibility.Visible : Visibility.Collapsed;
        FitHeader();
    }

    /// <summary>
    /// Follows the view model's strip width: it is 0 until the activity data
    /// has loaded, so a panel that starts narrow must choose again when it arrives.
    /// </summary>
    private void Watch(LibraryViewModel? viewModel)
    {
        if (ReferenceEquals(_watched, viewModel))
            return;

        if (_watched is not null)
            _watched.PropertyChanged -= ViewModelPropertyChanged;
        _watched = viewModel;
        if (_watched is not null)
            _watched.PropertyChanged += ViewModelPropertyChanged;
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.CalendarStripWidth))
            FitWidth();
    }

    private void FitWidth()
    {
        if (ActualWidth <= 0 || ViewModel is not { } vm)
            return;

        var monthView = ActivityCalendar.ShowsMonthView(ActualWidth, vm.CalendarStripWidth);
        YearView.Visibility = monthView ? Visibility.Collapsed : Visibility.Visible;
        MonthView.Visibility = monthView ? Visibility.Visible : Visibility.Collapsed;
        YearNav.Visibility = monthView ? Visibility.Collapsed : Visibility.Visible;
        MonthNav.Visibility = monthView ? Visibility.Visible : Visibility.Collapsed;
        FitHeader();
    }

    private void FitHeader()
    {
        if (ActualWidth <= 0)
            return;

        // The controls' width on one line, whichever line they wrap to now:
        // it doesn't depend on where they are docked, so this settles at once.
        var controls = HeaderControls.Children.OfType<FrameworkElement>().Sum(c => c.DesiredSize.Width);
        // The unit buttons' column takes its width from the left, and a hidden title needs no room.
        var available = ActualWidth - UnitGroup.ActualWidth;
        var below = available < controls + (ShowsTitle ? TitleRoom : 0);
        var margin = below && ShowsTitle ? new Thickness(0, 6, 0, 0) : new Thickness(0);
        if (below == (DockPanel.GetDock(HeaderControls) == Dock.Bottom) && HeaderControls.Margin == margin)
            return;

        DockPanel.SetDock(HeaderControls, below ? Dock.Bottom : Dock.Right);
        HeaderControls.Margin = margin;
    }

    private LibraryViewModel? ViewModel => DataContext as LibraryViewModel;

    private void CalendarDay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ActivityCalendarCell { Date: { } date, IsInRange: true })
            ViewModel?.SelectCalendarDate(date);
    }

    private void ClearPeriod_Click(object sender, RoutedEventArgs e) => ViewModel?.ClearPeriod();

    private void PreviousYear_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            vm.SelectCalendarYear(vm.CalendarYear - 1);
    }

    private void PreviousMonth_Click(object sender, RoutedEventArgs e) => ViewModel?.ShowCalendarMonth(-1);

    private void NextMonth_Click(object sender, RoutedEventArgs e) => ViewModel?.ShowCalendarMonth(1);

    private void NextYear_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            vm.SelectCalendarYear(vm.CalendarYear + 1);
    }

    /// <summary>Opens the list of years under whichever year button was clicked (the strip's or the month view's).</summary>
    private void YearButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        YearPopup.PlacementTarget = (UIElement)sender;
        YearList.ItemsSource = Enumerable.Range(LibraryViewModel.FirstCalendarYear,
            LibraryViewModel.LastCalendarYear - LibraryViewModel.FirstCalendarYear + 1);
        YearList.SelectedItem = Math.Clamp(vm.CalendarYear, LibraryViewModel.FirstCalendarYear, LibraryViewModel.LastCalendarYear);
        YearPopup.IsOpen = true;
        Dispatcher.InvokeAsync(() =>
        {
            YearList.ScrollIntoView(YearList.SelectedItem);
            YearList.Focus();
        }, DispatcherPriority.Input);
    }

    private void YearList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(YearList, source) is ListBoxItem { Content: int year })
        {
            ApplyYear(year);
            e.Handled = true;
        }
    }

    private void YearList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && YearList.SelectedItem is int year)
        {
            ApplyYear(year);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            YearPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void ApplyYear(int year)
    {
        ViewModel?.SelectCalendarYear(year);
        YearPopup.IsOpen = false;
    }
}
