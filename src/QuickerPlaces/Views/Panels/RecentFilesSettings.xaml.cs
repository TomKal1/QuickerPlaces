using System.Windows.Controls;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

/// <summary>
/// Recent Files' settings over the <see cref="LibraryViewModel"/> it gets as
/// its DataContext, or a dialog-local <see cref="RecentFilesSettingsViewModel"/> draft. Recorded opens can't be deleted from here: they are kept
/// as activity history (history plan H5).
/// </summary>
public partial class RecentFilesSettings : UserControl
{
    public RecentFilesSettings() => InitializeComponent();
}
