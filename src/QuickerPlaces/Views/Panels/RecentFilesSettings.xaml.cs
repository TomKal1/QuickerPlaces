using System.Windows;
using System.Windows.Controls;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

/// <summary>
/// Recent Files' settings over the <see cref="LibraryViewModel"/> it gets as
/// its DataContext. The code-behind only asks before deleting the history.
/// </summary>
public partial class RecentFilesSettings : UserControl
{
    public RecentFilesSettings() => InitializeComponent();

    private void ClearRecentFiles_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LibraryViewModel viewModel)
            return;

        var owner = Window.GetWindow(this);
        if (MessageForm.ShowDestructiveConfirm(viewModel.ClearRecentFilesConfirmation, owner?.Title ?? AppInfo.Name, "Delete history", owner))
            viewModel.ClearRecentFiles();
    }
}
