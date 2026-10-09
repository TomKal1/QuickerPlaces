using System;
using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

public partial class FolderTrackingDialog : Window
{
    private readonly FolderTrackingSettingsViewModel _editor;

    public FolderTrackingDialog(Window owner, ActivityStore store, Action saved)
    {
        InitializeComponent();
        Owner = owner;
        MaxHeight = SystemParameters.WorkArea.Height;
        _editor = new FolderTrackingSettingsViewModel(store, saved);
        DataContext = _editor;
    }

    public static void Show(Window owner, ActivityStore store, Action saved)
        => new FolderTrackingDialog(owner, store, saved).ShowDialog();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_editor.Save()) Close();
    }

    private void PickFolders_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose folders to track", Multiselect = true };
        if (picker.ShowDialog(this) != true) return;
        foreach (var path in picker.FolderNames) _editor.AddTarget(path);
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FolderTrackingTargetViewModel target })
            _editor.RemoveTarget(target);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_editor.HasChanges) return;
        var choice = MessageBox.Show(this, "Save your folder activity changes before closing?\n\nYes saves them. No discards your unsaved changes.",
            "Unsaved folder activity changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        e.Cancel = choice == MessageBoxResult.Cancel || (choice == MessageBoxResult.Yes && !_editor.Save());
    }
}
