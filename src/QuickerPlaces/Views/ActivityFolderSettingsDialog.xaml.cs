using System.Windows;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

/// <summary>Edits the selected tracked folder without occupying an Activity tab.</summary>
public partial class ActivityFolderSettingsDialog : Window
{
    private readonly ActivityViewModel _viewModel;

    public ActivityFolderSettingsDialog(Window owner, ActivityViewModel viewModel)
    {
        InitializeComponent();
        Owner = owner;
        _viewModel = viewModel;
        _viewModel.ResetSelectedSettingsDraft();
        DataContext = viewModel;
        Closed += (_, _) =>
        {
            if (DialogResult != true)
                _viewModel.ResetSelectedSettingsDraft();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SaveSelectedSettings() && !_viewModel.HasUnsavedChanges)
            DialogResult = true;
    }
}
