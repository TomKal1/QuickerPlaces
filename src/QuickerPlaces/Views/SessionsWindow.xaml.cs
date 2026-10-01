using System.Windows;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.Views;

/// <summary>
/// Project Sessions (sessions plan §5): the window around
/// <see cref="Panels.SessionsPanel"/>, kept while the workspace is built
/// (configurable canvas plan M2).
/// </summary>
public partial class SessionsWindow : Window
{
    private SessionsWindow(Window owner, SessionStore store, IShell shell, WindowsOpenDocumentProbe probe)
    {
        InitializeComponent();
        Owner = owner;
        Panel.Attach(store, shell, probe);
        Loaded += (_, _) => Panel.FocusStart();
    }

    /// <summary>Shows the window modally over <paramref name="owner"/>.</summary>
    public static void Show(Window owner, SessionStore store, IShell shell, WindowsOpenDocumentProbe probe)
        => new SessionsWindow(owner, store, shell, probe).ShowDialog();
}
