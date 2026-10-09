using System.ComponentModel;
using System.Windows;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

/// <summary>Edits a Recent Files draft and applies it only when its own Save succeeds.</summary>
public partial class RecentFilesDialog : Window
{
    private readonly RecentFilesSettingsViewModel _editor;

    private RecentFilesDialog(Window owner, LibraryViewModel library)
    {
        InitializeComponent();
        Owner = owner;
        MaxHeight = SystemParameters.WorkArea.Height;
        _editor = library.CreateRecentFilesSettingsEditor();
        DataContext = _editor;
    }

    public static void Show(Window owner, LibraryViewModel library) => new RecentFilesDialog(owner, library).ShowDialog();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_editor.Save()) Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_editor.HasChanges)
            return;

        var choice = MessageBox.Show(this, "Save your Recent Files selections before closing?\n\nYes saves them. No discards your unsaved changes.",
            "Unsaved Recent Files changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        e.Cancel = choice == MessageBoxResult.Cancel || (choice == MessageBoxResult.Yes && !_editor.Save());
    }
}
