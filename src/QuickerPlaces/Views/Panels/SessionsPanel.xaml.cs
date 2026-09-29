using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

/// <summary>
/// The Sessions panel (sessions plan §5; configurable canvas plan M2): a thin
/// view over <see cref="SessionsViewModel"/>, which decides what is shown and
/// makes every store call. This code-behind only passes clicks in, asks
/// before deleting, and shows <see cref="SessionEditorDialog"/>. Hosted by the
/// Sessions window and by the workspace (M3).
/// </summary>
public partial class SessionsPanel : UserControl
{
    private SessionStore? _store;
    private WindowsOpenDocumentProbe? _probe;
    private SessionsViewModel? _viewModel;

    public SessionsPanel() => InitializeComponent();

    /// <summary>Raised after a session was saved, edited or deleted here, so views of the Library can read the sessions again.</summary>
    public event Action? SessionsChanged;

    /// <summary>Connects the panel to the sessions store; until then it shows nothing.</summary>
    public void Attach(SessionStore store, IShell shell, WindowsOpenDocumentProbe probe)
    {
        _store = store;
        _probe = probe;
        _viewModel = new SessionsViewModel(store, new SessionLauncher(store, shell));
        DataContext = _viewModel;
    }

    /// <summary>Start where the work is: the list when there are sessions, Save open files when there are none.</summary>
    public void FocusStart()
    {
        if (_viewModel is { HasAnySessions: true })
            FocusSelectedSession();
        else
            SaveOpenButton.Focus();
    }

    /// <summary>Below this width the selected session goes under the list instead of beside it.</summary>
    private const double StackBelow = 640;

    private bool _stacked;

    private void Body_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stack = e.NewSize.Width < StackBelow;
        if (stack == _stacked)
            return;
        _stacked = stack;

        if (stack)
        {
            ListColumn.Width = new GridLength(1, GridUnitType.Star);
            ListColumn.MinWidth = 0;
            GapColumn.Width = new GridLength(0);
            DetailsRow.Height = new GridLength(1, GridUnitType.Star);
            Grid.SetColumnSpan(ListArea, 3);
            Grid.SetRow(DetailsArea, 1);
            Grid.SetColumn(DetailsArea, 0);
            Grid.SetColumnSpan(DetailsArea, 3);
            DetailsArea.Margin = new Thickness(0, 12, 0, 0);
        }
        else
        {
            ListColumn.Width = new GridLength(340);
            ListColumn.MinWidth = 260;
            GapColumn.Width = new GridLength(16);
            DetailsRow.Height = new GridLength(0);
            Grid.SetColumnSpan(ListArea, 1);
            Grid.SetRow(DetailsArea, 0);
            Grid.SetColumn(DetailsArea, 2);
            Grid.SetColumnSpan(DetailsArea, 1);
            DetailsArea.Margin = new Thickness(0);
        }
    }

    private Window OwnerWindow => Window.GetWindow(this) ?? Application.Current.MainWindow;

    private string Title => OwnerWindow?.Title ?? AppInfo.Name;

    private void SaveOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_store is null || _probe is null || _viewModel is null)
            return;

        var editor = new SessionEditorViewModel(_store, null);
        if (SessionEditorDialog.Show(OwnerWindow, editor, _probe))
        {
            _viewModel.NoteSaved(editor.SavedId!, editor.SavePersistenceMessage);
            SessionsChanged?.Invoke();
        }
        FocusSelectedSession();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_store is null || _probe is null || _viewModel?.SelectedRow is not { } row || !_viewModel.CanEditSelection)
            return;

        var editor = new SessionEditorViewModel(_store, row.Session);
        if (SessionEditorDialog.Show(OwnerWindow, editor, _probe))
        {
            _viewModel.NoteSaved(editor.SavedId!, editor.SavePersistenceMessage);
            SessionsChanged?.Invoke();
        }
        FocusSelectedSession();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || !_viewModel.CanEditSelection)
            return;

        if (!MessageForm.ShowDestructiveConfirm(_viewModel.DeleteConfirmation, Title, "Delete session", OwnerWindow))
            return;

        _viewModel.DeleteSelected();
        SessionsChanged?.Invoke();
        FocusSelectedSession();
    }

    private void OpenAll_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.OpenSelected();
        SessionsChanged?.Invoke();
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.SelectedItem is SessionFileViewModel file)
            _viewModel?.OpenFile(file);
        else
            MessageForm.Show("Select a file in the list first, or use Open all.", Title, owner: OwnerWindow);
    }

    private void TagChip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TagFilterViewModel filter && _viewModel is not null)
            _viewModel.SelectedTag = filter.Tag;
    }

    private void SessionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is SessionRowViewModel)
            OpenAll_Click(sender, e);
    }

    private void SessionsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null)
            return;

        if (e.Key == Key.Enter && _viewModel.HasSelection)
        {
            OpenAll_Click(sender, e);
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
            _viewModel?.OpenFile(file);
    }

    private void FilesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter would otherwise move down a row, as a DataGrid does.
        if (e.Key == Key.Enter && FilesGrid.SelectedItem is SessionFileViewModel file)
        {
            _viewModel?.OpenFile(file);
            e.Handled = true;
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null)
            return;

        if (e.Key == Key.Down && _viewModel.Rows.Count > 0)
        {
            FocusSelectedSession();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _viewModel.SearchText.Length > 0)
        {
            // Esc clears the search first; only an empty box lets it reach the window.
            _viewModel.SearchText = "";
            e.Handled = true;
        }
    }

    /// <summary>Puts the keyboard focus on the selected session's card, so the arrow keys and Enter work at once.</summary>
    private void FocusSelectedSession()
    {
        if (_viewModel?.SelectedRow is not { } row)
            return;

        SessionsList.ScrollIntoView(row);
        SessionsList.UpdateLayout();
        if (SessionsList.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem item)
            item.Focus();
        else
            SessionsList.Focus();
    }
}
