using System;
using System.Linq;
using System.Threading.Tasks;
using QuickerPlaces.Models.Sessions;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Services.Sessions.Suggestions;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

public sealed class SuggestionsViewModelTests
{
    private const string A = @"C:\Jobs\240015\a.pdf";
    private const string B = @"C:\Jobs\240015\b.pdf";

    private static (SuggestionsViewModel Vm, SessionStore Sessions, SuggestionSettingsViewModel Settings, SessionSuggestionStore Store) New()
    {
        var time = new ManualTimeProvider();
        var recent = new RecentFilesStore(new FakePlacesStorage(), time);
        recent.SetEnabled(true);
        recent.SetScope(QuickerPlaces.Models.RecentFiles.RecentFilesScope.Everywhere);
        var store = new SessionSuggestionStore(new FakePlacesStorage(), time);
        var settings = new SuggestionSettingsViewModel(store);
        settings.Enabled = true;
        var sessions = new SessionStore(new FakePlacesStorage(), time);
        var shell = new FakeShell();
        shell.ExistingFiles.UnionWith(new[] { A, B });
        store.SetOpenSpans(new[] { A, B }.Select(f => new OpenSpan(f, time.UtcNow.AddHours(-2), time.UtcNow)));
        return (new SuggestionsViewModel(new SessionSuggestionSource(recent, store, sessions, shell, time), settings), sessions, settings, store);
    }

    [Fact]
    public async Task NewAndUndo_RefreshCardsAndNotifySessions()
    {
        var (vm, sessions, _, _) = New();
        var changes = 0;
        vm.SessionsChanged += () => changes++;
        await vm.RefreshAsync();
        Assert.Equal(1, vm.Count);
        vm.Create(Assert.Single(vm.Cards));
        await vm.RefreshAsync();
        Assert.Empty(vm.Cards);
        Assert.True(vm.CanUndo);
        Assert.Single(sessions.Sessions);
        vm.Undo();
        await vm.RefreshAsync();
        Assert.Single(vm.Cards);
        Assert.Equal(2, changes);
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public async Task Dismiss_RefreshesCardsAndBadge()
    {
        var (vm, _, _, _) = New();
        await vm.RefreshAsync();
        vm.Dismiss(Assert.Single(vm.Cards));
        await vm.RefreshAsync();
        Assert.Empty(vm.Cards);
        Assert.Equal(0, vm.Count);
    }

    [Fact]
    public void Settings_OverallAndOverridesPersistAndResetTogether()
    {
        var (_, _, settings, store) = New();
        settings.IsHigh = true;
        settings.UsedTogetherChoice = settings.RuleChoices.Single(c => c.Level == SuggestionSensitivity.Low);
        Assert.Equal(SuggestionSensitivity.High, store.Settings.Overall);
        Assert.Equal(SuggestionSensitivity.Low, store.Settings.UsedTogether);
        Assert.Equal("Low", settings.UsedTogetherConditions.Single(c => c.IsActive).Level);
        Assert.Equal("4 days", settings.UsedTogetherConditions.Single(c => c.IsActive).DaysNeeded);
        Assert.Equal("High", settings.OpenTogetherConditions.Single(c => c.IsActive).Level);
        settings.ResetToOverall();
        Assert.Null(store.Settings.UsedTogether);
        Assert.Null(store.Settings.OpenTogetherLevel);
        Assert.True(settings.IsHigh);
        Assert.Equal("High", settings.UsedTogetherConditions.Single(c => c.IsActive).Level);
        Assert.Equal("2 days", settings.UsedTogetherConditions.Single(c => c.IsActive).DaysNeeded);
    }

    [Fact]
    public void Review_NewDoesNotScan_AndAddIncludesExistingFiles()
    {
        var (_, sessions, _, _) = New();
        var card = new SessionSuggestion("k", new[] { A, B }, new[] { B }, null, "Suggested", new(3, null, null, null));
        var editor = SessionEditorViewModel.ForSuggestion(sessions, card);
        Assert.False(editor.ScansOnOpen);
        Assert.Equal("Suggested", editor.Name);
        Assert.Equal(2, editor.IncludedCount);
        sessions.TryCreate("Existing", new[] { "tag" }, new[] { A }, out var existing, out _);
        editor = SessionEditorViewModel.ForSuggestion(sessions, card with { TargetSessionId = existing!.Id });
        Assert.False(editor.IsNew);
        Assert.Equal("tag", editor.TagsText);
        Assert.Equal(2, editor.IncludedCount);
        editor.Files.Single(f => f.Path == B).IsIncluded = false;
        Assert.True(editor.Save());
        Assert.Equal(new[] { A }, sessions.Find(existing.Id)!.Files);
    }

    [Fact]
    public void RecentFilesConsent_IsRequiredBeforeChangingSuggestionSettings()
    {
        var store = new SessionSuggestionStore(new FakePlacesStorage(), new ManualTimeProvider());
        var enabled = false;
        var settings = new SuggestionSettingsViewModel(store, () => enabled);
        Assert.False(settings.IsAvailable);
        settings.Enabled = true;
        Assert.False(store.Settings.Enabled);
        enabled = true;
        settings.RefreshAvailability();
        Assert.True(settings.IsAvailable);
        settings.Enabled = true;
        Assert.True(store.Settings.Enabled);
    }

    [Fact]
    public void FailedSettingsSave_IsReportedWhileMemoryStateRemainsShared()
    {
        var disk = new FakePlacesStorage { FailEveryWrite = true };
        var store = new SessionSuggestionStore(disk, new ManualTimeProvider());
        var settings = new SuggestionSettingsViewModel(store);
        settings.Enabled = true;
        Assert.True(settings.Enabled);
        Assert.True(store.Settings.Enabled);
        Assert.NotNull(settings.ErrorMessage);
    }

    [Fact]
    public void ReviewSaveFailure_IsReportedAndSessionsAreNotified()
    {
        var (vm, _, _, _) = New();
        var notified = false;
        vm.SessionsChanged += () => notified = true;
        vm.NoteReviewed("saved-id", "Save failed; kept in memory.");
        Assert.True(notified);
        Assert.Equal("saved-id", vm.SavedSessionId);
        Assert.Equal("Save failed; kept in memory.", vm.Status);
    }
}
