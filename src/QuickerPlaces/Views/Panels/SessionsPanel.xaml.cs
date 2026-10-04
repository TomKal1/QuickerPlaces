using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly CardDropPreview<SessionRowViewModel> _dropPreview;

    public SessionsPanel()
    {
        InitializeComponent();
        _dropPreview = new CardDropPreview<SessionRowViewModel>(SessionsList);
        Unloaded += (_, _) => _dropPreview.Clear();
    }

    /// <summary>Raised after a session was saved, edited or deleted here, so views of the Library can read the sessions again.</summary>
    public event Action? SessionsChanged;

    /// <summary>Raised by a card's View session files with the session's id: the workspace shows its files in the File viewer (File viewer design §5).</summary>
    public event Action<string>? ViewFilesRequested;

    /// <summary>Offers View session files on a card's menu. The workspace turns it on while a File viewer is shown.</summary>
    public bool ShowsViewFiles { get; set; }

    /// <summary>
    /// Raised with the id of the card selected, or null when none is, each time that
    /// changes (not when the cards are rebuilt around the same selection). The
    /// workspace shows the selected session's files in the File viewer.
    /// </summary>
    public event Action<string?>? SelectedSessionChanged;

    private string? _notifiedId;

    /// <summary>
    /// True in the workspace: no card is selected until one is chosen, and Esc clears
    /// the choice. Set before <see cref="Attach"/>.
    /// </summary>
    public bool AllowsNoSelection { get; set; }

    /// <summary>Connects the panel to the sessions store; until then it shows nothing.</summary>
    public void Attach(SessionStore store, IShell shell, WindowsOpenDocumentProbe probe)
    {
        _store = store;
        _probe = probe;
        _viewModel = new SessionsViewModel(store, new SessionLauncher(store, shell), allowsNoSelection: AllowsNoSelection);
        _notifiedId = _viewModel.SelectedRow?.Id;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SessionsViewModel.SelectedRow))
                return;

            var id = _viewModel.SelectedRow?.Id;
            if (id == _notifiedId)
                return;

            _notifiedId = id;
            SelectedSessionChanged?.Invoke(id);
        };
        DataContext = _viewModel;
    }

    /// <summary>Selects the card for <paramref name="sessionId"/>, or none for null: the File viewer's chip cleared the session it was showing.</summary>
    public void SelectSession(string? sessionId)
    {
        if (_viewModel is null)
            return;

        if (sessionId is null)
        {
            if (AllowsNoSelection)
                _viewModel.SelectedRow = null;
        }
        else
        {
            _viewModel.Select(sessionId);
        }
    }

    /// <summary>Shows a session saved elsewhere (the File shelf's Save as session), selected, with how its write went.</summary>
    public void NoteSaved(string sessionId, string? persistenceMessage) => _viewModel?.NoteSaved(sessionId, persistenceMessage);

    /// <summary>Reads the sessions again, keeping the selected one: after one was opened or changed elsewhere.</summary>
    public void Reload() => _viewModel?.Reload(_viewModel.SelectedRow?.Id);

    /// <summary>
    /// False in the workspace, where the window's header box is the search: a
    /// second box here would only repeat it. The Sessions window keeps its own.
    /// </summary>
    public bool ShowsSearch
    {
        get => SearchArea.Visibility == Visibility.Visible;
        set => SearchArea.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    private Panel? _headerSlot;
    private bool _actionInHeader;

    /// <summary>Gives the panel the frame's title-line slot for Save files; it stays in the panel until <see cref="ActionInHeader"/> is set.</summary>
    public void AttachHeaderSlot(Panel slot)
    {
        _headerSlot = slot;
        PlaceAction();
    }

    /// <summary>
    /// True puts Save files on the frame's title line, level with the
    /// title (small enough not to make that line taller); false puts it back in
    /// the panel's first row, under the title. The workspace turns it off while
    /// the frame's own Hide and Arrange controls are shown, so they have the line.
    /// </summary>
    public bool ActionInHeader
    {
        get => _actionInHeader;
        set
        {
            if (_actionInHeader == value)
                return;

            _actionInHeader = value;
            PlaceAction();
        }
    }

    private void PlaceAction()
    {
        var inHeader = _actionInHeader && _headerSlot is not null;
        Panel target = inHeader ? _headerSlot! : ActionRow;
        if (ReferenceEquals(SaveOpenButton.Parent, target))
            return;

        (SaveOpenButton.Parent as Panel)?.Children.Remove(SaveOpenButton);
        if (inHeader)
        {
            SaveOpenButton.Height = 24;
            SaveOpenButton.Margin = new Thickness(0);
        }
        else
        {
            SaveOpenButton.ClearValue(HeightProperty);
            SaveOpenButton.Margin = new Thickness(12, 0, 0, 10);
        }

        target.Children.Add(SaveOpenButton);
    }

    /// <summary>Start where the work is: the list when there are sessions, Save files when there are none.</summary>
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
        if (_viewModel is null || (e.OriginalSource as FrameworkElement)?.DataContext is not SessionRowViewModel clicked)
        {
            e.Handled = true;
            return;
        }

        // The menu acts on the selected session, so the card it was opened on is selected first.
        if (_viewModel.SelectedRow?.Id != clicked.Id)
            _viewModel.SelectedRow = clicked;
        if (_viewModel.SelectedRow is null)
        {
            e.Handled = true;
            return;
        }

        ViewFilesMenuItem.Visibility = ShowsViewFiles ? Visibility.Visible : Visibility.Collapsed;
    }

    // -----------------------------------------------------------------
    // Drag a card to put the sessions in order, as favourites are dragged
    // (FavouritesPanel). A card's place in the order is its Ctrl+Shift
    // number, so this is how a shortcut is given. A plain click never
    // reaches DoDragDrop: only a press followed by a move past the system's
    // drag distance starts one.
    // -----------------------------------------------------------------

    private Point? _dragStart;

    private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _dragStart = e.GetPosition(null);

    private void Card_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is not { } start)
            return;

        if (sender is not ListBoxItem { DataContext: SessionRowViewModel row } item || _viewModel is not { CanChange: true })
            return;

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragStart = null;
        _dropPreview.Drag(item, row);
    }

    private void SessionsList_DragOver(object sender, DragEventArgs e)
    {
        if (_viewModel is { CanChange: true } vm &&
            e.Data.GetData(typeof(SessionRowViewModel)) is SessionRowViewModel dragged && vm.Rows.Contains(dragged))
        {
            _dropPreview.Update(e.GetPosition(SessionsList), dragged, vertical: true);
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            _dropPreview.Clear();
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void SessionsList_DragLeave(object sender, DragEventArgs e)
    {
        if (!_dropPreview.Contains(e.GetPosition(SessionsList)))
            _dropPreview.Clear();
    }

    private void SessionsList_Drop(object sender, DragEventArgs e)
    {
        _dropPreview.Clear();
        if (_viewModel is { CanChange: true } &&
            e.Data.GetData(typeof(SessionRowViewModel)) is SessionRowViewModel dragged &&
            _dropPreview.FindTarget(e.GetPosition(SessionsList), dragged, vertical: true) is { } target)
        {
            _viewModel.Move(dragged, target.Item, target.After);
        }
        e.Handled = true;
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
        else if (e.Key == Key.Escape && AllowsNoSelection && _viewModel.HasSelection)
        {
            // Esc puts the card down: nothing selected, so the File viewer shows every session again.
            _viewModel.SelectedRow = null;
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
