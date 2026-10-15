using System.Windows;
using System.Windows.Controls;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

public partial class SuggestionSettings : UserControl
{
    public SuggestionSettings() => InitializeComponent();
    private void Reset_Click(object sender, RoutedEventArgs e) => (DataContext as SuggestionSettingsViewModel)?.ResetToOverall();
}
