using System.Windows;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Revit;
using QuickerPlaces.Services.Revit.Opening;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.Views;

/// <summary>
/// Project Sessions (sessions plan §5): the window around
/// <see cref="Panels.SessionsPanel"/>, kept while the workspace is built
/// (configurable canvas plan M2).
/// </summary>
public partial class SessionsWindow : Window
{
    private SessionsWindow(Window owner, SessionStore store, IShell shell, WindowsOpenDocumentProbe probe,
        IRevitOpenCoordinator? revitOpener, RevitReleaseCache? revitReleases)
    {
        InitializeComponent();
        Owner = owner;
        Panel.Attach(store, shell, probe, revitOpener, revitReleases);
        Loaded += (_, _) => Panel.FocusStart();
    }

    /// <summary>Shows the window modally over <paramref name="owner"/>.</summary>
    public static void Show(Window owner, SessionStore store, IShell shell, WindowsOpenDocumentProbe probe,
        IRevitOpenCoordinator? revitOpener = null, RevitReleaseCache? revitReleases = null)
        => new SessionsWindow(owner, store, shell, probe, revitOpener, revitReleases).ShowDialog();
}
