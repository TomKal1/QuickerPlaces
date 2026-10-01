using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

/// <summary>
/// The Year activity panel (configurable canvas plan M2): a thin view over
/// the shared <see cref="LibraryViewModel"/> it gets as its DataContext.
/// Hosted by the Library window and by the workspace (M3). Picks the year
/// strip or, when the panel is too narrow for the year, the month view, and
/// puts the header's controls under its title when they don't fit beside it
/// (M4, D1): a narrow panel shows legible days instead of shrinking them.
/// </summary>
public partial class YearActivityPanel : UserControl
{
    /// <summary>The least room the title and caption keep beside the header's controls.</summary>
    private const double CaptionRoom = 220;

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
        var below = ActualWidth < controls + CaptionRoom;
        if (below == (DockPanel.GetDock(HeaderControls) == Dock.Bottom))
            return;

        DockPanel.SetDock(HeaderControls, below ? Dock.Bottom : Dock.Right);
        HeaderControls.Margin = below ? new Thickness(0, 6, 0, 0) : new Thickness(0);
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
}
