using System.Windows;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

/// <summary>Recent Files' settings as a dialog, for the workspace (configurable canvas plan M3).</summary>
public partial class RecentFilesDialog : Window
{
    private RecentFilesDialog(Window owner, LibraryViewModel library)
    {
        InitializeComponent();
        Owner = owner;
        DataContext = library;
    }

    public static void Show(Window owner, LibraryViewModel library) => new RecentFilesDialog(owner, library).ShowDialog();
}
