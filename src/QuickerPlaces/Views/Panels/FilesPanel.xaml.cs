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

    public FilesPanel(PlacesPanel places, FileShelfPanel shelf, LibraryViewModel library)
    {
        _library = library;
        InitializeComponent();
        SavedHost.Content = places;
        LibraryHost.Content = shelf;
        IsVisibleChanged += (_, _) => ApplyTab();
        ApplyTab();
    }

    /// <summary>The tab chosen: null for Saved places, otherwise the Library's tab. Kept while the app runs, not stored (File viewer design §3).</summary>
    public LibraryTab? Tab { get; private set; }

    /// <summary>A session card's View session files (File viewer design §5): the Sessions tab, narrowed to that session.</summary>
    public void ShowSession(string sessionId)
    {
        SessionsTab.IsChecked = true;
        _library.ScopeToSession(sessionId);
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
        if (SavedHost is null || LibraryHost is null)
            return;

        SavedHost.Visibility = Tab is null ? Visibility.Visible : Visibility.Collapsed;
        LibraryHost.Visibility = Tab is null ? Visibility.Collapsed : Visibility.Visible;
        _library.Tab = IsVisible ? Tab ?? LibraryTab.All : null;
    }
}
