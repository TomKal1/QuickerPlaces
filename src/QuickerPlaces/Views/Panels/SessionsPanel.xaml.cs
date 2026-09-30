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

    /// <summary>Raised by a card's View session files with the session's id: the workspace shows its files in the File viewer (File viewer design §5).</summary>
    public event Action<string>? ViewFilesRequested;

    /// <summary>Offers View session files on a card's menu. The workspace turns it on while a File viewer is shown.</summary>
    public bool ShowsViewFiles { get; set; }

    /// <summary>Connects the panel to the sessions store; until then it shows nothing.</summary>
    public void Attach(SessionStore store, IShell shell, WindowsOpenDocumentProbe probe)
    {
        _store = store;
        _probe = probe;
        _viewModel = new SessionsViewModel(store, new SessionLauncher(store, shell));
        DataContext = _viewModel;
    }

    /// <summary>Shows a session saved elsewhere (the File shelf's Save as session), selected, with how its write went.</summary>
    public void NoteSaved(string sessionId, string? persistenceMessage) => _viewModel?.NoteSaved(sessionId, persistenceMessage);

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
    private bool _cardsOnly;

    /// <summary>
    /// Shows the session cards alone, without the selected session's files: the
    /// workspace turns this on while a File viewer is shown, whose Sessions tab
    /// lists every session's files and has Open all, Edit and Delete (File
    /// viewer design §5). The card menu still has them.
    /// </summary>
    public bool CardsOnly
    {
        get => _cardsOnly;
        set
        {
            if (_cardsOnly == value)
                return;
            _cardsOnly = value;
            ApplyBodyLayout();
        }
    }

    private void Body_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stack = e.NewSize.Width < StackBelow;
        if (stack == _stacked)
            return;
        _stacked = stack;
        ApplyBodyLayout();
    }

    private void ApplyBodyLayout()
    {
        if (_cardsOnly)
        {
            ListColumn.Width = new GridLength(1, GridUnitType.Star);
            ListColumn.MinWidth = 0;
            GapColumn.Width = new GridLength(0);
            DetailsRow.Height = new GridLength(0);
            Grid.SetColumnSpan(ListArea, 3);
            DetailsArea.Visibility = Visibility.Collapsed;
            return;
        }

        DetailsArea.Visibility = Visibility.Visible;
        if (_stacked)
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

    /// <summary>
    /// Does <paramref name="action"/> to the session with <paramref name="sessionId"/>,
    /// as if its card were chosen and the button pressed: the File viewer's
    /// Sessions tab asks for it. The card is selected first, past any filter.
    /// </summary>
    public void RunSessionAction(SessionAction action, string sessionId)
    {
        if (_viewModel is null || !_viewModel.Select(sessionId))
            return;

        switch (action)
        {
            case SessionAction.OpenAll:
                OpenSelected();
                break;
            case SessionAction.Edit:
                EditSelected(focusCards: false);
                break;
            case SessionAction.Delete:
                DeleteSelected(focusCards: false);
                break;
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

    private void Edit_Click(object sender, RoutedEventArgs e) => EditSelected(focusCards: true);

    private void EditSelected(bool focusCards)
    {
        if (_store is null || _probe is null || _viewModel?.SelectedRow is not { } row || !_viewModel.CanEditSelection)
            return;

        var editor = new SessionEditorViewModel(_store, row.Session);
        if (SessionEditorDialog.Show(OwnerWindow, editor, _probe))
        {
            _viewModel.NoteSaved(editor.SavedId!, editor.SavePersistenceMessage);
            SessionsChanged?.Invoke();
        }
        if (focusCards)
            FocusSelectedSession();
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected(focusCards: true);

    private void DeleteSelected(bool focusCards)
    {
        if (_viewModel is null || !_viewModel.CanEditSelection)
            return;

        if (!MessageForm.ShowDestructiveConfirm(_viewModel.DeleteConfirmation, Title, "Delete session", OwnerWindow))
            return;

        _viewModel.DeleteSelected();
        SessionsChanged?.Invoke();
        if (focusCards)
            FocusSelectedSession();
    }

    private void OpenAll_Click(object sender, RoutedEventArgs e) => OpenSelected();

    private void OpenSelected()
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

    /// <summary>Opens a card's menu on that card only: empty space has no session to act on.</summary>
    private void SessionsList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_viewModel?.SelectedRow is null || (e.OriginalSource as FrameworkElement)?.DataContext is not SessionRowViewModel)
        {
            e.Handled = true;
            return;
        }

        ViewFilesMenuItem.Visibility = ShowsViewFiles ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ViewFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedRow is { } row)
            ViewFilesRequested?.Invoke(row.Id);
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

/// <summary>What the File viewer's Sessions tab can ask of a session (File viewer design §5).</summary>
public enum SessionAction
{
    OpenAll,
    Edit,
    Delete,
}
