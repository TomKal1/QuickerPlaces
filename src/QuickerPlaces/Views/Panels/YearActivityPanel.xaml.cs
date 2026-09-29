using System.Windows;
using System.Windows.Controls;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

/// <summary>
/// The Year activity panel (configurable canvas plan M2): a thin view over
/// the shared <see cref="LibraryViewModel"/> it gets as its DataContext.
/// Hosted by the Library window and by the workspace (M3).
/// </summary>
public partial class YearActivityPanel : UserControl
{
    public YearActivityPanel() => InitializeComponent();

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

    private void NextYear_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            vm.SelectCalendarYear(vm.CalendarYear + 1);
    }
}
