using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickerPlaces.Services;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

/// <summary>
/// The File shelf panel (configurable canvas plan M2): a thin view over the
/// shared <see cref="LibraryViewModel"/> it gets as its DataContext. Hosted
/// by the Library window and by the workspace (M3).
/// </summary>
public partial class FileShelfPanel : UserControl
{
    public FileShelfPanel() => InitializeComponent();

    private LibraryViewModel? ViewModel => DataContext as LibraryViewModel;

    private LibraryRowViewModel? SelectedRow => RowsGrid.SelectedItem as LibraryRowViewModel;

    /// <summary>Puts the keyboard focus in the search box.</summary>
    public void FocusSearch() => SearchBox.Focus();

    /// <summary>
    /// False in the workspace, whose search box in the toolbar searches the
    /// same Library (configurable canvas plan D4): two boxes for one search
    /// would only disagree about which has the focus.
    /// </summary>
    public bool ShowsSearch
    {
        get => SearchArea.Visibility == Visibility.Visible;
        set => SearchArea.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Selects the first row, or keeps the selected one, and focuses the list: Down from a search box.</summary>
    public void FocusList()
    {
        if (RowsGrid.Items.Count == 0)
            return;

        if (RowsGrid.SelectedIndex < 0)
            RowsGrid.SelectedIndex = 0;
        RowsGrid.ScrollIntoView(RowsGrid.SelectedItem);
        RowsGrid.UpdateLayout();
        if (RowsGrid.ItemContainerGenerator.ContainerFromItem(RowsGrid.SelectedItem) is DataGridRow row)
            row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        else
            RowsGrid.Focus();
    }

    private void KindChip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LibraryKindFilter filter && ViewModel is { } vm)
            vm.SelectedKind = filter.Kind;
    }

    private void RowsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is LibraryRowViewModel row)
            ViewModel?.Open(row);
    }

    private void RowsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter would otherwise move down a row, as a DataGrid does.
        if (e.Key == Key.Enter && SelectedRow is { } row)
        {
            ViewModel?.Open(row);
            e.Handled = true;
        }
    }

    private void RowsGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (SelectedRow is null)
        {
            e.Handled = true;
            return;
        }

        ForgetMenuItem.IsEnabled = SelectedRow.CanForget;
    }

    private void OpenMenu_Click(object sender, RoutedEventArgs e) => ViewModel?.Open(SelectedRow);

    private void CopyMenu_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not { } row)
            return;

        try
        {
            Clipboard.SetText(row.Location);
        }
        catch (Exception ex)
        {
            // Another program can hold the clipboard; nothing to do but say so.
            DiagnosticLog.Warn($"Copying from the Library failed ({ex.GetType().Name}).");
            var owner = Window.GetWindow(this);
            MessageForm.Show("The clipboard is busy. Try again in a moment.", owner?.Title ?? AppInfo.Name, owner: owner);
        }
    }

    private void ForgetMenu_Click(object sender, RoutedEventArgs e) => ViewModel?.Forget(SelectedRow);

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        if (e.Key == Key.Escape && vm.SearchText.Length > 0)
        {
            // Esc clears the search first; only an empty box lets it reach the window.
            vm.SearchText = "";
            e.Handled = true;
        }
        else if (e.Key == Key.Down && RowsGrid.Items.Count > 0)
        {
            RowsGrid.SelectedIndex = 0;
            RowsGrid.Focus();
            e.Handled = true;
        }
    }
}
