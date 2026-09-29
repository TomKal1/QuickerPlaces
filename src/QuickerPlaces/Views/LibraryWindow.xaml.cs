using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

/// <summary>
/// The Library (documents plan §6): saved places, session files, Recents
/// folders and Recent Files together, split by kind or grouped by tag, with
/// a year strip. A thin view over <see cref="LibraryViewModel"/>; this
/// code-behind passes clicks in, asks before deleting Recent Files history,
/// copies to the clipboard, and on opening asks Recent Files to read Recent
/// Items at once so what was just opened is listed.
/// </summary>
public partial class LibraryWindow : Window
{
    private readonly LibraryViewModel _viewModel;
    private readonly RecentFilesHost _recentFilesHost;

    private LibraryWindow(Window owner, LibraryViewModel viewModel, RecentFilesHost recentFilesHost)
    {
        InitializeComponent();
        Owner = owner;
        _viewModel = viewModel;
        _recentFilesHost = recentFilesHost;
        DataContext = viewModel;

        Loaded += async (_, _) =>
        {
            SearchBox.Focus();
            try
            {
                await Task.Run(_recentFilesHost.RecordNow);
                _viewModel.Reload();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn($"Refreshing Recent Files for the Library failed ({ex.GetType().Name}).");
            }
        };
    }

    /// <summary>Shows the Library modally; <paramref name="placeOpened"/> lets the main grid show a place opened from here.</summary>
    public static void Show(Window owner, LibraryViewModel viewModel, RecentFilesHost recentFilesHost, Action<Place, PersistenceResult> placeOpened)
    {
        viewModel.PlaceOpened += placeOpened;
        try
        {
            new LibraryWindow(owner, viewModel, recentFilesHost).ShowDialog();
        }
        finally
        {
            viewModel.PlaceOpened -= placeOpened;
        }
    }

    private LibraryRowViewModel? SelectedRow => RowsGrid.SelectedItem as LibraryRowViewModel;

    private void KindChip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LibraryKindFilter filter)
            _viewModel.SelectedKind = filter.Kind;
    }

    private void CalendarDay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ActivityCalendarCell { Date: { } date, IsInRange: true })
            _viewModel.SelectCalendarDate(date);
    }

    private void ClearDay_Click(object sender, RoutedEventArgs e) => _viewModel.ClearPeriod();

    private void PreviousYear_Click(object sender, RoutedEventArgs e) => _viewModel.SelectCalendarYear(_viewModel.CalendarYear - 1);

    private void NextYear_Click(object sender, RoutedEventArgs e) => _viewModel.SelectCalendarYear(_viewModel.CalendarYear + 1);

    private void RowsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is LibraryRowViewModel row)
            _viewModel.Open(row);
    }

    private void RowsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter would otherwise move down a row, as a DataGrid does.
        if (e.Key == Key.Enter && SelectedRow is { } row)
        {
            _viewModel.Open(row);
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

    private void OpenMenu_Click(object sender, RoutedEventArgs e) => _viewModel.Open(SelectedRow);

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
            MessageForm.Show("The clipboard is busy. Try again in a moment.", Title, owner: this);
        }
    }

    private void ForgetMenu_Click(object sender, RoutedEventArgs e) => _viewModel.Forget(SelectedRow);

    private void ClearRecentFiles_Click(object sender, RoutedEventArgs e)
    {
        if (MessageForm.ShowDestructiveConfirm(_viewModel.ClearRecentFilesConfirmation, Title, "Delete history", this))
            _viewModel.ClearRecentFiles();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _viewModel.SearchText.Length > 0)
        {
            // Esc clears the search first; only an empty box lets it close the window.
            _viewModel.SearchText = "";
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
