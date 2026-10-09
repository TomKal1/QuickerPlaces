using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

/// <summary>
/// Open Shared Session (session sharing plan §6): a thin view over
/// <see cref="ImportSharedSessionViewModel"/>. This code-behind runs the
/// network check off the UI thread (a server that doesn't answer can take
/// many seconds), reads OneDrive's folders again for Check again, shows the
/// Locate picker, and closes once the session is saved.
/// </summary>
public partial class ImportSharedSessionDialog : Window
{
    private readonly ImportSharedSessionViewModel _viewModel;
    private readonly IShell _shell;
    private readonly ICloudSyncRoots _cloudRoots;

    private ImportSharedSessionDialog(Window owner, ImportSharedSessionViewModel viewModel, IShell shell, ICloudSyncRoots cloudRoots)
    {
        InitializeComponent();
        Owner = owner;
        _viewModel = viewModel;
        _shell = shell;
        _cloudRoots = cloudRoots;
        DataContext = viewModel;
        Loaded += (_, _) => NameBox.Focus();
    }

    /// <summary>Shows the dialog modally. True when the session was saved.</summary>
    public static bool Show(Window owner, ImportSharedSessionViewModel viewModel, IShell shell, ICloudSyncRoots cloudRoots)
        => new ImportSharedSessionDialog(owner, viewModel, shell, cloudRoots).ShowDialog() == true;

    private SharedFileMatchViewModel? SelectedRow => FilesGrid.SelectedItem as SharedFileMatchViewModel;

    private async void CheckNetwork_Click(object sender, RoutedEventArgs e)
    {
        var paths = _viewModel.NetworkPathsToCheck;
        if (paths.Count == 0)
            return;

        CheckNetworkButton.IsEnabled = false;
        var previousCursor = Cursor;
        Cursor = Cursors.Wait;
        try
        {
            var answers = await Task.Run(() => paths.ToDictionary(p => p, Exists, StringComparer.OrdinalIgnoreCase));
            _viewModel.ApplyNetworkCheck(answers);
        }
        finally
        {
            Cursor = previousCursor;
            CheckNetworkButton.IsEnabled = _viewModel.HasUncheckedNetwork;
        }
    }

    private bool Exists(string path)
    {
        try
        {
            return _shell.FileExists(path);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Checking a shared network file failed ({ex.GetType().Name}).");
            return false;
        }
    }

    private void CheckAgain_Click(object sender, RoutedEventArgs e) => _viewModel.Recheck(_cloudRoots.Read());

    private void Locate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not { } row)
        {
            MessageForm.Show("Select a file in the list first.", Title, owner: this);
            return;
        }

        var picker = new OpenFileDialog
        {
            Title = $"Where is {row.FileName}?",
            Filter = DocumentKinds.FileDialogFilter,
            FileName = row.FileName,
            CheckFileExists = true,
        };
        if (picker.ShowDialog(this) == true)
            _viewModel.Locate(row, picker.FileName);
    }

    private void OpenOnline_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is { } row)
            _viewModel.OpenOnline(row);
    }

    private void OpenFolderOnline_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is { } row)
            _viewModel.OpenFolderOnline(row);
    }

    /// <summary>Offers Open online only for a file with a web address; opens on a row only.</summary>
    private void FilesGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not SharedFileMatchViewModel row)
        {
            e.Handled = true;
            return;
        }

        FilesGrid.SelectedItem = row;
        OpenOnlineMenuItem.IsEnabled = row.CanOpenOnline;
        OpenFolderOnlineMenuItem.IsEnabled = row.CanOpenOnline;
    }

    private void FilesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && SelectedRow is { CanInclude: true } row)
        {
            row.IsIncluded = !row.IsIncluded;
            e.Handled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Save())
        {
            DialogResult = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(_viewModel.Name))
            NameBox.Focus();
    }
}
