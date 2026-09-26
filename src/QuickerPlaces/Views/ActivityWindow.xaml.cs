using System.Windows;
using Microsoft.Win32;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

/// <summary>Root management and opt-in; period and calendar views arrive in later steps.</summary>
public partial class ActivityWindow : Window
{
    private readonly ActivityViewModel _viewModel;
    private readonly INetworkDriveResolver _networkDrives;

    public ActivityWindow(Window owner, ActivityStore store, ActivityTrackingHost host,
        INetworkDriveResolver networkDrives, System.Action indicatorChanged)
    {
        InitializeComponent();
        Owner = owner;
        _networkDrives = networkDrives;
        _viewModel = new ActivityViewModel(store, () =>
        {
            host.RootsChanged();
            indicatorChanged();
        });
        DataContext = _viewModel;
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
}
