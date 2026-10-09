using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

/// <summary>
/// Share Session (session sharing plan §5): a thin view over
/// <see cref="ShareSessionViewModel"/>. This code-behind shows the Save As
/// picker, writes the .qpsession file, and closes once it is written.
/// </summary>
public partial class ShareSessionDialog : Window
{
    private readonly ShareSessionViewModel _viewModel;

    private ShareSessionDialog(Window owner, ShareSessionViewModel viewModel)
    {
        InitializeComponent();
        Owner = owner;
        _viewModel = viewModel;
        DataContext = viewModel;
        PrivacyText.Text = ShareSessionViewModel.PrivacyNote;
    }

    /// <summary>Shows the dialog modally. The path written, or null when cancelled.</summary>
    public static string? Show(Window owner, ShareSessionViewModel viewModel)
    {
        var dialog = new ShareSessionDialog(owner, viewModel);
        return dialog.ShowDialog() == true ? dialog._writtenPath : null;
    }

    private string? _writtenPath;

    private void TickAll_Click(object sender, RoutedEventArgs e) => _viewModel.SetAllIncluded(true);

    private void UntickAll_Click(object sender, RoutedEventArgs e) => _viewModel.SetAllIncluded(false);

    private void FilesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var selected = FilesGrid.SelectedItems.OfType<SharedFileChoiceViewModel>().ToList();
        if (e.Key == Key.Space && selected.Count > 0)
        {
            // Tick them all unless all are ticked already, as the Save Session dialog does.
            var tick = !selected.All(c => c.IsIncluded);
            foreach (var choice in selected)
                choice.IsIncluded = tick;
            e.Handled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var document = _viewModel.BuildDocument();
        if (document is null)
            return;

        var picker = new SaveFileDialog
        {
            Title = "Save shared session",
            Filter = SharedSessionFormat.FileDialogFilter,
            DefaultExt = SharedSessionFormat.Extension,
            FileName = _viewModel.SuggestedFileName,
            OverwritePrompt = true,
        };
        if (picker.ShowDialog(this) != true)
            return;

        if (SharedSessionFormat.Write(picker.FileName, document) is { } error)
        {
            _viewModel.NoteWriteFailed(error);
            return;
        }

        _writtenPath = picker.FileName;
        DialogResult = true;
    }
}
