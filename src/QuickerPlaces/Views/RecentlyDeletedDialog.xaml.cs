using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

/// <summary>
/// Recently Deleted (roadmap §4.9, plan 5.5): the places removed in the last
/// seven days, with Restore selected, Delete selected permanently and Empty
/// Recently Deleted. A thin view (D21) over <see cref="RecentlyDeletedViewModel"/>,
/// which decides what is shown and makes every PlacesService call; this
/// code-behind only does what needs a window: passing the grid's selection
/// in, asking before the two irreversible actions
/// (<see cref="MessageForm.ShowDestructiveConfirm"/>, D18), running the
/// restore-conflict dialog (<see cref="PlaceFormDialog.ShowRestore"/>, D15),
/// and putting the keyboard focus somewhere useful after each action.
/// </summary>
public partial class RecentlyDeletedDialog : Window
{
    private readonly PlacesService _placesService;
    private readonly RecentlyDeletedViewModel _viewModel;

    private RecentlyDeletedDialog(PlacesService placesService)
    {
        InitializeComponent();

        _placesService = placesService;
        _viewModel = new RecentlyDeletedViewModel(placesService);
        DataContext = _viewModel;

        var owner = Application.Current?.MainWindow;
        if (owner is not null && owner.IsLoaded && !ReferenceEquals(owner, this))
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        // Start on the first (most recently removed) row, so the dialog
        // works from the keyboard at once; with nothing here, on Close.
        Loaded += (_, _) => FocusAfterAction(0);
    }

    /// <summary>
    /// Shows the dialog modally. <paramref name="changed"/> runs after every
    /// action, while the dialog is still open, with the places that action
    /// restored, so MainViewModel can insert them into the grid and refresh
    /// the banner from PlacesService straight away.
    /// </summary>
    public static void Show(PlacesService placesService, Action<IReadOnlyList<Place>> changed)
    {
        var dialog = new RecentlyDeletedDialog(placesService);
        dialog._viewModel.Changed += changed;
        dialog.ShowDialog();
    }

    private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _viewModel.SetSelection(ItemsGrid.SelectedItems.OfType<RecentlyDeletedRowViewModel>());

    /// <summary>
    /// Restores what can come back as it is, in one save, then takes each
    /// conflict in turn to the Restore Place dialog. Cancelling one leaves
    /// it here and moves on to the next.
    /// </summary>
    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        var index = ItemsGrid.SelectedIndex;

        foreach (var returned in _viewModel.RestoreSelected())
        {
            if (_viewModel.CurrentConflict(returned.Place) is not { } conflict)
                continue;

            if (PlaceFormDialog.ShowRestore(conflict, _placesService, this))
                _viewModel.NoteRestored(conflict.Place);
        }

        FocusAfterAction(index);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanActOnSelection)
            return;

        if (!MessageForm.ShowDestructiveConfirm(_viewModel.DeleteSelectedConfirmation, Title, "Delete permanently", this))
            return;

        var index = ItemsGrid.SelectedIndex;
        _viewModel.DeleteSelectedPermanently();
        FocusAfterAction(index);
    }

    private void EmptyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanEmpty)
            return;

        if (!MessageForm.ShowDestructiveConfirm(_viewModel.EmptyConfirmation, Title, "Empty Recently Deleted", this))
            return;

        _viewModel.EmptyRecentlyDeleted();
        FocusAfterAction(0);
    }

    /// <summary>
    /// After an action the rows are rebuilt and the button just pressed may
    /// be disabled, which would leave the keyboard focus nowhere. Selects
    /// and focuses the row now at <paramref name="index"/> (clamped), or
    /// Close when the list is empty.
    /// </summary>
    private void FocusAfterAction(int index)
    {
        if (ItemsGrid.Items.Count == 0)
        {
            CloseButton.Focus();
            return;
        }

        index = Math.Clamp(index, 0, ItemsGrid.Items.Count - 1);
        var item = ItemsGrid.Items[index];
        ItemsGrid.SelectedItem = item;
        ItemsGrid.ScrollIntoView(item);

        // Focusing the DataGrid itself doesn't focus a row, so arrow keys
        // wouldn't move from it: focus the row's first cell. Not
        // MainWindow.FocusGridRow's MoveFocus(Next) from the row, because
        // this grid's TabNavigation="Once" sends that out of the grid, onto
        // Restore selected, where Enter would restore instead of closing.
        ItemsGrid.CurrentCell = new DataGridCellInfo(item, ItemsGrid.Columns[0]);
        ItemsGrid.UpdateLayout();
        if (ItemsGrid.ItemContainerGenerator.ContainerFromIndex(index) is DataGridRow row
            && ItemsGrid.Columns[0].GetCellContent(row)?.Parent is DataGridCell cell)
            cell.Focus();
        else
            ItemsGrid.Focus();
    }
}
