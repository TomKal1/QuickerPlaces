using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using QuickerPlaces.Services;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

/// <summary>
/// Saved places (SI §6.3; configurable canvas plan M3): the places list with
/// its search box, Options menu, sorting and row shortcuts, as the main
/// window had it, over the <see cref="MainViewModel"/> it gets as its
/// DataContext. The main window hosts it in list mode, and the workspace as
/// its Saved places panel.
/// </summary>
public partial class PlacesPanel : UserControl
{
    private bool _collapsesWithWindow;
    private bool _loadedOnce;

    public PlacesPanel()
    {
        InitializeComponent();
        ApplyCollapse();
    }

    /// <summary>
    /// True in the main window's list mode: Hide list (Ctrl+H) collapses the
    /// table, leaving the header and its search box. False in the workspace,
    /// where the table is always shown and the window's own Hide collapses
    /// the whole workspace instead.
    /// </summary>
    public bool CollapsesWithWindow
    {
        get => _collapsesWithWindow;
        set
        {
            _collapsesWithWindow = value;
            ApplyCollapse();
        }
    }

    /// <summary>
    /// False in the workspace, where the window's header box is the search and
    /// filters this table too: a second box here would only repeat it.
    /// </summary>
    public bool ShowsSearch
    {
        get => SearchArea.Visibility == Visibility.Visible;
        set => SearchArea.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Lifts the place count and the Options menu out of this panel's header, for
    /// a host to show on its own row (the File viewer's tab row), and collapses
    /// the header so the table starts higher. Meant for a panel whose search box
    /// is hidden: with it shown, the header stays as it was and this returns null.
    /// The returned bar takes this panel's DataContext, so its bindings and menu still work.
    /// </summary>
    public FrameworkElement? TakeToolbar()
    {
        if (ShowsSearch || HeaderArea.Visibility != Visibility.Visible)
            return null;

        HeaderArea.Children.Remove(CountArea);
        HeaderArea.Children.Remove(ButtonsArea);
        CountArea.Margin = new Thickness(0, 0, 12, 0);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(CountArea);
        bar.Children.Add(ButtonsArea);
        bar.SetBinding(DataContextProperty, new Binding(nameof(DataContext)) { Source = this });
        HeaderArea.Visibility = Visibility.Collapsed;
        return bar;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>Focuses the search box and selects what is in it, so typing replaces it.</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void ApplyCollapse()
    {
        if (_collapsesWithWindow)
        {
            ListArea.SetBinding(VisibilityProperty, new Binding(nameof(MainViewModel.IsGridExpanded)) { Converter = new BooleanToVisibilityConverter() });
            ListToggleButton.Visibility = Visibility.Visible;
        }
        else
        {
            BindingOperations.ClearBinding(ListArea, VisibilityProperty);
            ListArea.Visibility = Visibility.Visible;
            ListToggleButton.Visibility = Visibility.Collapsed;
        }
    }

    private void PlacesPanel_Loaded(object sender, RoutedEventArgs e)
    {
        // Loaded again whenever the panel is shown again; the check is for startup.
        if (_loadedOnce)
            return;
        _loadedOnce = true;

        // The remembered sort is already applied to the view (MainViewModel's
        // constructor); this shows its arrow. Done once the grid is loaded, so
        // nothing in its own start-up can clear the arrow afterwards. A
        // remembered sort with no column to show it (Favourite, whose column
        // became the star beside the alias) would be a sort nobody can see
        // or undo, so it goes back to stored order instead.
        if (ViewModel is { CurrentSort: { } remembered } viewModel && !HasColumnFor(remembered.Key))
            viewModel.ClearSort();

        UpdateSortArrows();
    }

    private void OptionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (OptionsButton.ContextMenu is not { } menu) return;
        menu.PlacementTarget = OptionsButton;
        menu.IsOpen = true;
    }

    // -----------------------------------------------------------------
    // Search box + row shortcuts. Window-wide shortcuts are KeyBindings in
    // MainWindow.xaml; these handlers cover the ones that depend on focus
    // (the search box, the grid's selected row).
    // -----------------------------------------------------------------

    /// <summary>
    /// Makes the search box a launcher: Enter opens the top result, Down
    /// moves into the grid to pick a different one, and Esc clears the
    /// search (or, if it's already empty, hands focus to the grid).
    /// </summary>
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        switch (e.Key)
        {
            case Key.Escape:
                if (viewModel.IsSearching)
                    viewModel.SearchText = string.Empty;
                else
                    FocusGridRow(PlacesGrid.SelectedIndex);
                e.Handled = true;
                break;

            case Key.Enter:
                if (PlacesGrid.Items.Count > 0 && PlacesGrid.Items[0] is PlaceViewModel top)
                    viewModel.OpenCommand.Execute(top);
                e.Handled = true;
                break;

            case Key.Down:
                FocusGridRow(0);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Row shortcuts, acting on the selected row: Enter opens, F2 renames, Ctrl+E edits the path/URL, Ctrl+D toggles favourite, Ctrl+C copies the path/URL, Delete removes.</summary>
    private void PlacesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (PlacesGrid.SelectedItem is not PlaceViewModel place || ViewModel is not { } viewModel)
            return;

        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        var none = Keyboard.Modifiers == ModifierKeys.None;

        var command = e.Key switch
        {
            Key.Enter when none => viewModel.OpenCommand,
            Key.F2 when none => viewModel.RenameAliasCommand,
            Key.E when ctrl => viewModel.EditResourceCommand,
            Key.D when ctrl => viewModel.ToggleFavouriteCommand,
            // Replaces DataGrid's own Ctrl+C, which copies every cell of the row.
            Key.C when ctrl => viewModel.CopyResourceCommand,
            Key.Delete when none => viewModel.RemoveCommand,
            _ => null
        };

        if (command is null)
            return;

        // Handled first: DataGrid's own Enter handling would otherwise
        // also move the selection down a row.
        e.Handled = true;
        var index = PlacesGrid.SelectedIndex;
        command.Execute(place);

        // Remove no longer opens a confirmation, so nothing hands the focus
        // back: the focused row has just gone, and the next Delete or arrow
        // key would go nowhere. Select and focus the row that took its place
        // (the one before, if it was the last).
        if (ReferenceEquals(command, viewModel.RemoveCommand) && !PlacesGrid.Items.Contains(place))
        {
            if (PlacesGrid.Items.Count > 0)
                FocusGridRow(index);
            else
                PlacesGrid.Focus();
        }
    }

    /// <summary>Opens the first place listed, as Enter does in this panel's own search box. False when the search leaves none.</summary>
    public bool OpenTopResult()
    {
        if (ViewModel is not { } viewModel || PlacesGrid.Items.Count == 0 || PlacesGrid.Items[0] is not PlaceViewModel top)
            return false;

        viewModel.OpenCommand.Execute(top);
        return true;
    }

    /// <summary>Moves keyboard focus into the table, to the selected row, or the first.</summary>
    public void FocusList() => FocusGridRow(Math.Max(0, PlacesGrid.SelectedIndex));

    /// <summary>Selects and keyboard-focuses the grid row at <paramref name="index"/> (clamped; first row if nothing was selected).</summary>
    private void FocusGridRow(int index)
    {
        if (PlacesGrid.Items.Count == 0)
            return;

        index = Math.Clamp(index, 0, PlacesGrid.Items.Count - 1);
        var item = PlacesGrid.Items[index];
        PlacesGrid.SelectedItem = item;
        PlacesGrid.ScrollIntoView(item);

        // Focusing the DataGrid itself only focuses the grid, not a row, so
        // arrow keys wouldn't move from the selection. Focus the row's
        // container once it has been generated.
        PlacesGrid.UpdateLayout();
        if (PlacesGrid.ItemContainerGenerator.ContainerFromIndex(index) is DataGridRow row)
            row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        else
            PlacesGrid.Focus();
    }

    // -----------------------------------------------------------------
    // Sorting (Phase 3 D29). The DataGrid's own sorting is replaced, not
    // extended: it would set SortDescriptions, which can't express "never
    // opened is oldest" or the alias tie-break, and each column's first
    // direction is PlaceSort's to choose. Each column's SortMemberPath is its PlaceSortKey
    // name, and nothing else reads it.
    // -----------------------------------------------------------------

    private void PlacesGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;

        if (ViewModel is not { } viewModel ||
            !Enum.TryParse<PlaceSortKey>(e.Column.SortMemberPath, out var key))
            return;

        viewModel.SortBy(key);
        UpdateSortArrows();
    }

    private bool HasColumnFor(PlaceSortKey key)
    {
        foreach (var column in PlacesGrid.Columns)
        {
            if (Enum.TryParse<PlaceSortKey>(column.SortMemberPath, out var columnKey) && columnKey == key)
                return true;
        }

        return false;
    }

    /// <summary>Shows the current sort's arrow on its column, and none on the others.</summary>
    private void UpdateSortArrows()
    {
        var sort = ViewModel?.CurrentSort;

        foreach (var column in PlacesGrid.Columns)
        {
            column.SortDirection = sort is { } active && Enum.TryParse<PlaceSortKey>(column.SortMemberPath, out var key) && key == active.Key
                ? active.Direction
                : null;
        }
    }

    /// <summary>Double-click on a grid row = Open (SI §6.3), the same action as the row's top context-menu item.</summary>
    private void Row_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // A quick double-click on the row's favourite star is two toggles,
        // not an open: the row raises MouseDoubleClick even though the star
        // button handled both clicks.
        if (IsInsideButton(e.OriginalSource as DependencyObject))
            return;

        if (sender is DataGridRow { Item: PlaceViewModel place } && ViewModel is { } viewModel)
            viewModel.OpenCommand.Execute(place);
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        for (var current = element; current is not null and not DataGridRow;
             current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase)
                return true;
        }

        return false;
    }
}
