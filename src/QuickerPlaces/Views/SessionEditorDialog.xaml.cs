using System;
using System.Linq;
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
/// Save Open PDFs and Edit Session (sessions plan §5, D9): the review step
/// between finding PDFs and saving them. A thin view over
/// <see cref="SessionEditorViewModel"/>; this code-behind runs the scan off
/// the UI thread, shows the file picker, and closes on a successful save.
/// </summary>
public partial class SessionEditorDialog : Window
{
    private readonly SessionEditorViewModel _viewModel;
    private readonly WindowsOpenDocumentProbe _probe;

    private SessionEditorDialog(Window owner, SessionEditorViewModel viewModel, WindowsOpenDocumentProbe probe)
    {
        InitializeComponent();
        Owner = owner;
        _viewModel = viewModel;
        _probe = probe;
        DataContext = viewModel;

        Loaded += async (_, _) =>
        {
            NameBox.Focus();

            // A new session starts from what is open now; an edit starts from
            // what was saved, and one from the File shelf from what it listed.
            if (_viewModel.ScansOnOpen)
                await ScanAsync();
        };
    }

    /// <summary>Shows the dialog modally. True when the session was saved.</summary>
    public static bool Show(Window owner, SessionEditorViewModel viewModel, WindowsOpenDocumentProbe probe)
        => new SessionEditorDialog(owner, viewModel, probe).ShowDialog() == true;

    private async System.Threading.Tasks.Task ScanAsync()
    {
        if (_viewModel.IsScanning)
            return;

        _viewModel.IsScanning = true;
        ScanButton.IsEnabled = false;
        try
        {
            var scan = await _probe.ScanAsync();
            _viewModel.ApplyScan(scan);
        }
        catch (Exception ex)
        {
            // ScanAsync catches what it expects; anything else must still leave the dialog usable.
            DiagnosticLog.Error("Document scan failed unexpectedly.", ex);
            _viewModel.ApplyScan(OpenDocumentScan.Empty with { Warning = "Open files couldn't be looked for. Use Add files to choose them." });
        }
        finally
        {
            _viewModel.IsScanning = false;
            ScanButton.IsEnabled = true;
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Add files to the session",
            Filter = DocumentKinds.FileDialogFilter,
            Multiselect = true,
            CheckFileExists = true,
        };

        if (picker.ShowDialog(this) == true)
            _viewModel.AddFiles(picker.FileNames);
    }

    private void TagSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string tag)
            _viewModel.AddTag(tag);
    }

    private void TickAll_Click(object sender, RoutedEventArgs e) => _viewModel.SetAllIncluded(true);

    private void UntickAll_Click(object sender, RoutedEventArgs e) => _viewModel.SetAllIncluded(false);

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        foreach (var choice in FilesGrid.SelectedItems.OfType<SessionFileChoiceViewModel>().ToList())
            _viewModel.Remove(choice);
    }

    private void FilesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var selected = FilesGrid.SelectedItems.OfType<SessionFileChoiceViewModel>().ToList();
        if (e.Key == Key.Space && selected.Count > 0)
        {
            // Tick them all unless all are ticked already, as Explorer's check boxes do.
            var tick = !selected.All(c => c.IsIncluded);
            foreach (var choice in selected)
                choice.IsIncluded = tick;
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && selected.Count > 0)
        {
            Remove_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsScanning)
            return;

        if (_viewModel.Save())
        {
            DialogResult = true;
            return;
        }

        // Point at what needs fixing.
        if (string.IsNullOrWhiteSpace(_viewModel.Name))
            NameBox.Focus();
        else if (_viewModel.IncludedCount == 0)
            FilesGrid.Focus();
    }
}
