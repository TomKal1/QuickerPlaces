using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

/// <summary>
/// The File shelf panel (configurable canvas plan M2): a thin view over the
/// shared <see cref="LibraryViewModel"/> it gets as its DataContext. Hosted
/// by the Library window and by the workspace (M3), where it is the Recents
/// panel (Desk layout design §4).
/// </summary>
public partial class FileShelfPanel : UserControl
{
    public FileShelfPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is LibraryViewModel old)
                old.PropertyChanged -= ViewModel_PropertyChanged;
            if (e.NewValue is LibraryViewModel now)
                now.PropertyChanged += ViewModel_PropertyChanged;
            UpdateColumns();
            UpdateSessionBar();
            UpdateTrackFolderButton();
        };
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.Tab))
            UpdateColumns();
        else if (e.PropertyName == nameof(LibraryViewModel.HasSessionScope))
            UpdateSessionBar();
        else if (e.PropertyName == nameof(LibraryViewModel.ShowsTrackedFolders))
            UpdateTrackFolderButton();
    }

    private bool _toolbarTaken;

    /// <summary>
    /// Lifts Track a folder and Save as session out of this panel, for a host to
    /// show on its own row (the File viewer's tab row). In the panel, Track a folder
    /// is part of the tracked folders strip; out of it, it shows while the strip
    /// would (<see cref="UpdateTrackFolderButton"/>).
    /// </summary>
    public FrameworkElement TakeToolbar()
    {
        _toolbarTaken = true;
        var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var button in new[] { TrackFolderButton, SaveAsSessionButton })
        {
            (button.Parent as Panel)?.Children.Remove(button);
            button.Margin = new Thickness(8, 0, 0, 0);
            bar.Children.Add(button);
        }

        bar.SetBinding(DataContextProperty, new System.Windows.Data.Binding(nameof(DataContext)) { Source = this });
        UpdateTrackFolderButton();
        return bar;
    }

    /// <summary>Taken out of the strip, Track a folder is shown exactly while the strip is: tracking attached, and a tab (or none) that shows the tracked folders.</summary>
    private void UpdateTrackFolderButton()
    {
        if (_toolbarTaken)
            TrackFolderButton.Visibility = ShowsTracking && ViewModel?.ShowsTrackedFolders != false ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Shows the columns the File viewer's tab asks for (File viewer design §4).
    /// With no tab, the columns are as they always were. DataGrid columns aren't
    /// in the visual tree, so they can't bind to the DataContext: this sets them.
    /// </summary>
    private void UpdateColumns()
    {
        var vm = ViewModel;
        static Visibility Show(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

        SourceColumn.Visibility = Show(vm?.ShowsSourceMarkers == true);
        WhereFromColumn.Visibility = Show(vm?.ShowsWhereFrom != false);
        VisitsColumn.Visibility = Show(vm?.ShowsVisitsAndTime != false);
        TimeColumn.Visibility = VisitsColumn.Visibility;
        SessionsColumn.Visibility = Show(vm?.ShowsSessions == true);
        TagsColumn.Visibility = Show(vm?.ShowsTags != false);
    }

    private void ClearSessionScope_Click(object sender, RoutedEventArgs e) => ViewModel?.ClearSessionScope();

    /// <summary>
    /// Shows Open all, Edit and Delete for the session this list is narrowed to
    /// (File viewer design §5). The File viewer turns it on for its Sessions tab.
    /// Each raises <see cref="SessionActionRequested"/>: the host has the store and the editor.
    /// </summary>
    public bool ShowsSessionActions
    {
        get => SessionActions.Visibility == Visibility.Visible;
        set
        {
            SessionActions.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            UpdateSessionBar();
        }
    }

    /// <summary>Raised by the session bar's buttons, with the id of the session the list is narrowed to.</summary>
    public event Action<SessionAction, string>? SessionActionRequested;

    /// <summary>The bar shows while it has a chip or buttons to hold, so an empty one takes no room.</summary>
    private void UpdateSessionBar()
        => SessionBar.Visibility = ShowsSessionActions || ViewModel?.HasSessionScope == true ? Visibility.Visible : Visibility.Collapsed;

    private void OpenSession_Click(object sender, RoutedEventArgs e) => RaiseSessionAction(SessionAction.OpenAll);

    private void EditSession_Click(object sender, RoutedEventArgs e) => RaiseSessionAction(SessionAction.Edit);

    private void DeleteSession_Click(object sender, RoutedEventArgs e) => RaiseSessionAction(SessionAction.Delete);

    private void RaiseSessionAction(SessionAction action)
    {
        if (ViewModel?.SessionScope is { } id)
            SessionActionRequested?.Invoke(action, id);
    }

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

    /// <summary>
    /// Shows the chosen period, with a button that clears it: the workspace
    /// turns this on while no Year activity panel is shown (D4).
    /// </summary>
    public bool ShowsPeriod
    {
        get => PeriodArea.Visibility == Visibility.Visible;
        set => PeriodArea.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearPeriod_Click(object sender, RoutedEventArgs e) => ViewModel?.ClearPeriod();

    /// <summary>Shows Save as session, which raises <see cref="SaveAsSessionRequested"/>: the workspace saves the listed files (M3).</summary>
    public bool ShowsSaveAsSession
    {
        get => SaveAsSessionButton.Visibility == Visibility.Visible;
        set => SaveAsSessionButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Raised by Save as session; the host has the sessions store and the review dialog.</summary>
    public event Action? SaveAsSessionRequested;

    private void SaveAsSession_Click(object sender, RoutedEventArgs e) => SaveAsSessionRequested?.Invoke();

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

    private ActivityViewModel? _activity;
    private INetworkDriveResolver? _networkDrives;
    private Func<string>? _trackingSummary;

    /// <summary>Raised by Add as place with the folder; the workspace has the places and the Add folder dialog.</summary>
    public event Action<string>? AddAsPlaceRequested;

    /// <summary>True in the workspace's Recents panel, once <see cref="AttachTracking"/> ran.</summary>
    public bool ShowsTracking => TrackingArea.Visibility == Visibility.Visible;

    /// <summary>
    /// Makes this the Recents panel (Desk layout design §4): the tracked
    /// folders strip, whose actions go to <paramref name="activity"/>, and the
    /// line <paramref name="trackingSummary"/> writes.
    /// </summary>
    public void AttachTracking(ActivityViewModel activity, INetworkDriveResolver networkDrives, Func<string> trackingSummary)
    {
        _activity = activity;
        _networkDrives = networkDrives;
        _trackingSummary = trackingSummary;
        TrackingProblem.DataContext = activity;
        TrackingProblem.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(ActivityViewModel.HasError))
        {
            Converter = new BooleanToVisibilityConverter(),
        });
        TrackingArea.Visibility = Visibility.Visible;
        UpdateTrackFolderButton();
        UpdateTracking();
    }

    /// <summary>Rewrites the tracking line: after a change to the tracked folders, or to tracking's pause.</summary>
    public void UpdateTracking()
    {
        if (_trackingSummary is not null)
            TrackingSummaryText.Text = _trackingSummary();
    }

    private void TrackFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_activity is null || _networkDrives is null || Window.GetWindow(this) is not { } owner)
            return;

        var picker = new OpenFolderDialog { Title = "Choose a folder to track" };
        if (picker.ShowDialog(owner) != true)
            return;
        var equivalents = AddRootDialog.Show(owner, picker.FolderName, _networkDrives);
        if (equivalents is not null)
            _activity.AddRoot(picker.FolderName, equivalents);
    }

    private void RootChip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TrackedRootChip chip)
            ViewModel?.ToggleRootScope(chip.RootId);
    }

    /// <summary>Points the Recents actions at the chip right-clicked, and names Stop or Resume tracking.</summary>
    private void RootChip_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_activity is null || sender is not Button { DataContext: TrackedRootChip chip, ContextMenu: { } menu })
        {
            e.Handled = true;
            return;
        }

        _activity.SelectedRoot = _activity.Roots.FirstOrDefault(r => r.RootId == chip.RootId);
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (Equals(item.Tag, "Toggle"))
                item.Header = chip.Enabled ? "Stop tracking" : "Resume tracking";
            item.IsEnabled = item.Tag is null || _activity.CanManage;
        }
    }

    private void EditRoot_Click(object sender, RoutedEventArgs e)
    {
        if (_activity?.SelectedRoot is not null && Window.GetWindow(this) is { } owner)
            new ActivityFolderSettingsDialog(owner, _activity).ShowDialog();
    }

    private void AboutRoot_Click(object sender, RoutedEventArgs e)
    {
        if (_activity?.SelectedRoot is not null)
            MessageForm.Show(_activity.AboutSelectedFolderText, "About tracked folder", owner: Window.GetWindow(this));
    }

    private void ToggleRoot_Click(object sender, RoutedEventArgs e) => _activity?.ToggleSelected();

    private void DeleteRoot_Click(object sender, RoutedEventArgs e)
    {
        if (_activity?.SelectedRoot is not { } selected)
            return;

        var message = $"Delete \"{selected.Path}\" and all of its recorded activity? This cannot be undone. To keep the data, use Stop tracking instead.";
        if (MessageForm.ShowDestructiveConfirm(message, "Delete tracked folder", "Delete folder and its data", Window.GetWindow(this)))
            _activity.DeleteSelected();
    }

    private void RetryTrackingSave_Click(object sender, RoutedEventArgs e) => _activity?.RetrySave();

    private void AddAsPlace_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is { CanAddAsPlace: true } row)
            AddAsPlaceRequested?.Invoke(row.Location);
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
        AddAsPlaceMenuItem.Visibility = ShowsTracking && SelectedRow.CanAddAsPlace ? Visibility.Visible : Visibility.Collapsed;
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
