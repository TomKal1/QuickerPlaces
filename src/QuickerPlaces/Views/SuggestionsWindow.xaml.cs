using System;
using System.Linq;
using System.Windows;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

/// <summary>Settings opens this window without changing the workspace layout.</summary>
public partial class SuggestionsWindow : Window
{
    private SuggestionsWindow(Window owner, SuggestionsViewModel viewModel, SessionStore sessions,
        WindowsOpenDocumentProbe probe, Action<Window>? recentSettings)
    {
        InitializeComponent();
        Owner = owner;
        MaxHeight = SystemParameters.WorkArea.Height;
        Suggestions.Attach(viewModel, sessions, probe);
        Suggestions.RecentFilesSettingsRequested += () => recentSettings?.Invoke(this);
    }

    /// <summary>Reuses the owner's open window; repeated clicks never create duplicates.</summary>
    public static void Show(Window owner, SuggestionsViewModel viewModel, SessionStore sessions,
        WindowsOpenDocumentProbe probe, Action<Window>? recentSettings = null, bool modal = false)
    {
        var existing = owner.OwnedWindows.OfType<SuggestionsWindow>().FirstOrDefault();
        if (existing is not null)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        var window = new SuggestionsWindow(owner, viewModel, sessions, probe, recentSettings);
        if (modal) window.ShowDialog(); else window.Show();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
