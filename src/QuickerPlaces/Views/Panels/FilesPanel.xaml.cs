using System;
using System.Windows;
using System.Windows.Controls;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

/// <summary>
/// The File viewer (File viewer design §3, §4): a tab strip over the places
/// table and the Library grid. It gives the shared <see cref="LibraryViewModel"/>
/// its tab only while it is on screen. Hidden, or while another layout shows,
/// the Library has no tab, so a Recents panel shows as it always did.
/// </summary>
public partial class FilesPanel : UserControl
{
    private readonly LibraryViewModel _library;
    private readonly FileShelfPanel _shelf;
    private readonly PlacesPanel _places;

    public FilesPanel(PlacesPanel places, FileShelfPanel shelf, LibraryViewModel library)
    {
        _library = library;
        _shelf = shelf;
        _places = places;
        InitializeComponent();
        SavedHost.Content = places;
        LibraryHost.Content = shelf;
        SavedToolbarHost.Content = places.TakeToolbar();
        LibraryToolbarHost.Content = shelf.TakeToolbar();
        shelf.SessionActionRequested += (action, id) => SessionActionRequested?.Invoke(action, id);
        IsVisibleChanged += (_, _) => ApplyTab();
        ApplyTab();
    }

    /// <summary>The tab chosen: null for Saved places, otherwise the Library's tab. Kept while the app runs, not stored (File viewer design §3).</summary>
    public LibraryTab? Tab { get; private set; }

    /// <summary>True while the Saved places table is the tab on screen, so the header's search keys act on it, not on the hidden Library grid.</summary>
    public bool ShowsSavedPlaces => IsVisible && Tab is null;

    /// <summary>Opens the first saved place the search leaves. False when there is none.</summary>
    public bool OpenTopSavedPlace() => _places.OpenTopResult();

    /// <summary>Moves keyboard focus into the Saved places table.</summary>
    public void FocusSavedPlaces() => _places.FocusList();

    /// <summary>
    /// Whether the Sessions tab offers Open all, Edit and Delete: the workspace turns
    /// it on while a Sessions panel is shown, since that panel holds the store and the editor.
    /// </summary>
    public bool OffersSessionActions
    {
        get => _offersSessionActions;
        set
        {
            _offersSessionActions = value;
            ApplyTab();
        }
    }

    private bool _offersSessionActions;

    /// <summary>Raised by the Sessions tab's Open all, Edit and Delete, with the session they are for (File viewer design §5).</summary>
    public event Action<SessionAction, string>? SessionActionRequested;

    /// <summary>A session card's View session files (File viewer design §5): the Sessions tab, narrowed to that session.</summary>
    public void ShowSession(string sessionId)
    {
        SessionsTab.IsChecked = true;
        _library.ScopeToSession(sessionId);
    }

    /// <summary>
    /// A session card was chosen, or put down (null): the Sessions tab shows that
    /// session's files, or every session's. Does nothing while the viewer isn't on screen.
    /// </summary>
    public void ScopeSession(string? sessionId)
    {
        if (!IsVisible || sessionId == _library.SessionScope)
            return;

        if (sessionId is null)
            _library.ClearSessionScope();
        else
            ShowSession(sessionId);
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        Tab = sender == RecentTab ? LibraryTab.Recent
            : sender == SessionsTab ? LibraryTab.Sessions
            : sender == AllTab ? LibraryTab.All
            : null;
        ApplyTab();
    }

    /// <summary>
    /// Shows the chosen tab's view. While on screen, it also gives the Library
    /// that tab; on Saved places the Library gets All, so the year calendar
    /// counts everything.
    /// </summary>
    private void ApplyTab()
    {
        // The first tab is checked while the XAML loads, before the hosts exist.
        if (SavedHost is null || LibraryHost is null || SavedToolbarHost is null || LibraryToolbarHost is null)
            return;

        SavedHost.Visibility = Tab is null ? Visibility.Visible : Visibility.Collapsed;
        LibraryHost.Visibility = Tab is null ? Visibility.Collapsed : Visibility.Visible;
        SavedToolbarHost.Visibility = SavedHost.Visibility;
        LibraryToolbarHost.Visibility = LibraryHost.Visibility;
        _library.Tab = IsVisible ? Tab ?? LibraryTab.All : null;
        _shelf.ShowsSessionActions = _offersSessionActions && Tab == LibraryTab.Sessions;
    }
}
