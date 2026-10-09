using System.ComponentModel;
using System.Windows;
using QuickerPlaces.Models;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

/// <summary>Edits a Revit settings draft and applies it only when its own Save succeeds.</summary>
public partial class RevitSettingsDialog : Window
{
    private readonly RevitSettingsViewModel _editor;

    private RevitSettingsDialog(Window owner, RevitSettingsViewModel editor)
    {
        InitializeComponent();
        Owner = owner;
        MaxHeight = SystemParameters.WorkArea.Height;
        _editor = editor;
        DataContext = _editor;
    }

    public static void Show(Window owner, RevitSettingsViewModel editor) => new RevitSettingsDialog(owner, editor).ShowDialog();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_editor.Save()) Close();
    }

    private void ResetFolder_Click(object sender, RoutedEventArgs e)
        => (((FrameworkElement)sender).DataContext as RevitReleaseRowViewModel)?.ResetFolder();

    private void RemoveEntry_Click(object sender, RoutedEventArgs e)
        => _editor.Remove(((FrameworkElement)sender).DataContext as LoadOnceEntryRowViewModel);

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_editor.HasChanges)
            return;

        var choice = MessageForm.Show("Save your Revit selections before closing?\n\nYes saves them. No discards your unsaved changes.",
            "Unsaved Revit changes", MessageFormButtons.YesNoCancel, MessageFormIcon.Question, this);
        e.Cancel = choice == MessageFormResult.Cancel || (choice == MessageFormResult.Yes && !_editor.Save());
    }
}
