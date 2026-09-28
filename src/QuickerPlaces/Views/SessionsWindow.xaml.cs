using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

/// <summary>
/// Project Sessions (sessions plan §5): saved sessions, a search box and tag
/// chips, and the selected session's PDFs. A thin view over
/// <see cref="SessionsViewModel"/>, which decides what is shown and makes
/// every store call; this code-behind only passes clicks in, asks before
/// deleting (<see cref="MessageForm.ShowDestructiveConfirm"/>), and shows
/// <see cref="SessionEditorDialog"/>.
/// </summary>
public partial class SessionsWindow : Window
{
    private readonly SessionStore _store;
    private readonly WindowsOpenPdfProbe _probe;
    private readonly SessionsViewModel _viewModel;

    private SessionsWindow(Window owner, SessionStore store, IShell shell, WindowsOpenPdfProbe probe)
    {
        InitializeComponent();
        Owner = owner;
        _store = store;
        _probe = probe;
        _viewModel = new SessionsViewModel(store, new SessionLauncher(store, shell));
        DataContext = _viewModel;

        // Start where the work is: the list when there are sessions, Save open PDFs when there are none.
        Loaded += (_, _) =>
        {
            if (_viewModel.HasAnySessions)
                FocusSelectedSession();
            else
                SaveOpenButton.Focus();
        };
    }

    /// <summary>Shows the window modally over <paramref name="owner"/>.</summary>
    public static void Show(Window owner, SessionStore store, IShell shell, WindowsOpenPdfProbe probe)
        => new SessionsWindow(owner, store, shell, probe).ShowDialog();

    private void SaveOpen_Click(object sender, RoutedEventArgs e)
    {
        var editor = new SessionEditorViewModel(_store, null);
        if (SessionEditorDialog.Show(this, editor, _probe))
            _viewModel.NoteSaved(editor.SavedId!, editor.SavePersistenceMessage);
        FocusSelectedSession();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedRow is not { } row || !_viewModel.CanEditSelection)
            return;

        var editor = new SessionEditorViewModel(_store, row.Session);
        if (SessionEditorDialog.Show(this, editor, _probe))
            _viewModel.NoteSaved(editor.SavedId!, editor.SavePersistenceMessage);
        FocusSelectedSession();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanEditSelection)
            return;

        if (!MessageForm.ShowDestructiveConfirm(_viewModel.DeleteConfirmation, Title, "Delete session", this))
            return;

        _viewModel.DeleteSelected();
        FocusSelectedSession();
    }

    private void OpenAll_Click(object sender, RoutedEventArgs e) => _viewModel.OpenSelected();

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.SelectedItem is SessionFileViewModel file)
            _viewModel.OpenFile(file);
        else
            MessageForm.Show("Select a PDF in the list first, or use Open all.", Title, owner: this);
    }

    private void TagChip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TagFilterViewModel filter)
            _viewModel.SelectedTag = filter.Tag;
    }

    private void SessionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is SessionRowViewModel)
            _viewModel.OpenSelected();
    }

    private void SessionsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel.HasSelection)
        {
            _viewModel.OpenSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _viewModel.CanEditSelection)
        {
            Delete_Click(sender, e);
            e.Handled = true;
        }
    }

    private void FilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is SessionFileViewModel file)
            _viewModel.OpenFile(file);
    }

    private void FilesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter would otherwise move down a row, as a DataGrid does.
        if (e.Key == Key.Enter && FilesGrid.SelectedItem is SessionFileViewModel file)
        {
            _viewModel.OpenFile(file);
            e.Handled = true;
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && _viewModel.Rows.Count > 0)
        {
            FocusSelectedSession();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _viewModel.SearchText.Length > 0)
        {
            // Esc clears the search first; only an empty box lets it close the window.
            _viewModel.SearchText = "";
            e.Handled = true;
        }
    }

    /// <summary>Puts the keyboard focus on the selected session's card, so the arrow keys and Enter work at once.</summary>
    private void FocusSelectedSession()
    {
        if (_viewModel.SelectedRow is not { } row)
            return;

        SessionsList.ScrollIntoView(row);
        SessionsList.UpdateLayout();
        if (SessionsList.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem item)
            item.Focus();
        else
            SessionsList.Focus();
    }
}
