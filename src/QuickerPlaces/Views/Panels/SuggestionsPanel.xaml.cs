using System;
using System.Windows;
using System.Windows.Controls;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views.Panels;

public partial class SuggestionsPanel : UserControl
{
    private SessionStore? _sessions;
    private WindowsOpenDocumentProbe? _probe;
    private bool _initialized;
    private SuggestionsViewModel? Vm => DataContext as SuggestionsViewModel;
    public event Action? RecentFilesSettingsRequested;
    public SuggestionsPanel()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (Vm is not { } vm) return;
            if (!_initialized)
            {
                SettingsExpander.IsExpanded = !vm.Settings.Enabled;
                _initialized = true;
            }
            vm.Invalidate(); // Recheck missing files each time the panel is displayed (G12).
            await vm.RefreshAsync();
        };
    }
    public void Attach(SuggestionsViewModel vm, SessionStore sessions, WindowsOpenDocumentProbe probe)
    { DataContext = vm; _sessions = sessions; _probe = probe; }
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    { if (Vm is { } vm) { vm.Invalidate(); await vm.RefreshAsync(); } }
    private async void Undo_Click(object sender, RoutedEventArgs e)
    { if (Vm is { } vm) { vm.Undo(); await vm.RefreshAsync(); } }
    private async void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || (sender as FrameworkElement)?.DataContext is not SuggestionCardViewModel card) return;
        if (card.Suggestion.TargetSessionId is { } id) vm.Add(card, id); else vm.Create(card);
        await vm.RefreshAsync();
    }
    private async void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || (sender as FrameworkElement)?.DataContext is not SuggestionCardViewModel card) return;
        vm.Dismiss(card);
        await vm.RefreshAsync();
    }
    private void AddTo_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not Button button || button.DataContext is not SuggestionCardViewModel card) return;
        var menu = new ContextMenu { PlacementTarget = button };
        foreach (var session in card.AddTargets)
        {
            var item = new MenuItem { Header = session.Name };
            item.Click += async (_, _) => { vm.Add(card, session.Id); await vm.RefreshAsync(); };
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "No saved sessions", IsEnabled = false });
        menu.IsOpen = true;
    }
    private async void Review_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || _sessions is null || _probe is null || (sender as FrameworkElement)?.DataContext is not SuggestionCardViewModel card) return;
        try
        {
            var editor = SessionEditorViewModel.ForSuggestion(_sessions, card.Suggestion);
            if (SessionEditorDialog.Show(Window.GetWindow(this), editor, _probe))
            {
                vm.NoteReviewed(editor.SavedId!, editor.SavePersistenceMessage);
                await vm.RefreshAsync();
            }
        }
        catch (InvalidOperationException ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Session suggestions"); }
    }
    private void RecentSettings_Click(object sender, RoutedEventArgs e) => RecentFilesSettingsRequested?.Invoke();
}
