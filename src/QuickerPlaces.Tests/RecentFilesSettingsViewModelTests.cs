using System;
using QuickerPlaces.Models.RecentFiles;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

public sealed class RecentFilesSettingsViewModelTests
{
    private readonly FakePlacesStorage _storage = new();
    private readonly ManualTimeProvider _time = new();
    private RecentFilesStore NewStore() => new(_storage, _time);

    [Fact]
    public void EditingAndAbandoningADraft_DoesNotChangeTrackingOrWrite()
    {
        var store = NewStore();
        var editor = new RecentFilesSettingsViewModel(store);
        Assert.False(editor.HasChanges);
        Assert.False(editor.CanSave);

        editor.RecentFilesEnabled = true;
        editor.TrackWord = false;
        editor.TrackEverywhere = true;

        Assert.True(editor.HasChanges);
        Assert.Contains("Unsaved changes", editor.SaveStatus);
        Assert.Contains("Anywhere", editor.SelectionSummary);
        Assert.Equal(0, _storage.WriteCount);
        Assert.False(store.IsTracking);
        Assert.Equal(RecentFilesScope.TrackedFolders, store.Settings.Scope);
        var reopened = new RecentFilesSettingsViewModel(store);
        Assert.True(reopened.TrackUnderTrackedFolders);
        Assert.True(reopened.TrackWord);
    }

    [Fact]
    public void Save_WritesAllSelectionsOnce_AndAnywhereSurvivesReload()
    {
        var notified = 0;
        var editor = new RecentFilesSettingsViewModel(NewStore(), () => notified++);
        editor.RecentFilesEnabled = true;
        editor.TrackEverywhere = true;
        editor.TrackWord = false;

        Assert.True(editor.CanSave);
        Assert.True(editor.Save());
        Assert.Equal(1, _storage.WriteCount);
        Assert.Equal(1, notified);
        Assert.False(editor.HasChanges);
        Assert.False(editor.CanSave);
        Assert.StartsWith("Saved.", editor.SaveStatus);

        var reloaded = NewStore();
        var reopened = new RecentFilesSettingsViewModel(reloaded);
        Assert.True(reloaded.IsTracking);
        Assert.True(reopened.TrackEverywhere);
        Assert.False(reopened.TrackUnderTrackedFolders);
        Assert.False(reopened.TrackWord);
        Assert.Equal(new[] { DocumentKind.Pdf, DocumentKind.Excel }, reloaded.Settings.Kinds);
        Assert.Equal(_time.UtcNow, reloaded.Settings.ResumedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RadioUncheckNotifications_DoNotSelectTheOppositeScope(bool uncheckFirst)
    {
        var editor = new RecentFilesSettingsViewModel(NewStore());
        if (uncheckFirst) editor.TrackUnderTrackedFolders = false;
        editor.TrackEverywhere = true;
        if (!uncheckFirst) editor.TrackUnderTrackedFolders = false;
        Assert.True(editor.TrackEverywhere);

        // Opening/rebinding an already selected radio must not undo its choice.
        editor.TrackEverywhere = false;
        Assert.True(editor.TrackEverywhere);

        if (uncheckFirst) editor.TrackEverywhere = false;
        editor.TrackUnderTrackedFolders = true;
        if (!uncheckFirst) editor.TrackEverywhere = false;
        Assert.True(editor.TrackUnderTrackedFolders);
    }

    [Fact]
    public void RevertingSelections_RemovesTheUnsavedIndicator()
    {
        var editor = new RecentFilesSettingsViewModel(NewStore());
        editor.RecentFilesEnabled = true;
        editor.TrackPdf = false;
        editor.TrackEverywhere = true;
        editor.RecentFilesEnabled = false;
        editor.TrackPdf = true;
        editor.TrackUnderTrackedFolders = true;

        Assert.False(editor.HasChanges);
        Assert.False(editor.CanSave);
        Assert.Equal("No unsaved changes.", editor.SaveStatus);
        Assert.Equal(0, _storage.WriteCount);
    }

    [Fact]
    public void FailedSave_DoesNotApplySettings_AndCanBeRetried()
    {
        var store = NewStore();
        var notified = 0;
        var editor = new RecentFilesSettingsViewModel(store, () => notified++);
        editor.RecentFilesEnabled = true;
        editor.TrackEverywhere = true;
        editor.TrackExcel = false;
        _storage.FailNextWrite = true;

        Assert.False(editor.Save());
        Assert.False(store.IsTracking);
        Assert.Equal(RecentFilesScope.TrackedFolders, store.Settings.Scope);
        Assert.Equal(DocumentKinds.All, store.Settings.Kinds);
        Assert.Null(store.Settings.TrackingStartedAt);
        Assert.False(store.HasUnsavedChanges);
        Assert.True(store.Flush().Saved);
        Assert.Equal(0, _storage.WriteCount);
        Assert.Equal(0, notified);
        Assert.True(editor.HasChanges);
        Assert.True(editor.CanSave);
        Assert.Contains("not been applied", editor.ErrorMessage);

        Assert.True(editor.Save());
        Assert.Null(editor.ErrorMessage);
        Assert.Equal(1, notified);
        Assert.True(NewStore().Settings.Enabled);
        Assert.True(new RecentFilesSettingsViewModel(NewStore()).TrackEverywhere);
    }

    [Fact]
    public void FailedSettingsSave_KeepsBufferedHistoryForFlush_WithoutApplyingTheDraft()
    {
        var store = NewStore();
        store.SetEnabled(true);
        store.Record(new[] { new RecentDocument(@"C:\Jobs\test.pdf", _time.UtcNow.AddMinutes(1)) }, _ => true);
        var editor = new RecentFilesSettingsViewModel(store) { TrackEverywhere = true, RecentFilesEnabled = false };
        _storage.FailNextWrite = true;

        Assert.False(editor.Save());
        Assert.True(store.IsTracking);
        Assert.True(store.HasUnsavedChanges);
        Assert.True(store.Flush().Saved);
        var reloaded = NewStore();
        Assert.True(reloaded.IsTracking);
        Assert.Equal(RecentFilesScope.TrackedFolders, reloaded.Settings.Scope);
        Assert.Single(reloaded.QueryFiles());
    }

    [Fact]
    public void EmptyFileTypes_ShowValidationAndCannotBeSaved()
    {
        var editor = new RecentFilesSettingsViewModel(NewStore());
        editor.TrackPdf = editor.TrackWord = editor.TrackExcel = false;
        Assert.False(editor.CanSave);
        Assert.Equal("Choose at least one file type.", editor.ErrorMessage);
        Assert.False(editor.Save());
        Assert.Equal(0, _storage.WriteCount);

        editor.TrackPdf = true;
        Assert.True(editor.CanSave);
        Assert.Null(editor.ErrorMessage);
    }

    [Fact]
    public void SavingScopeOrKinds_DoesNotRestartTracking_AndTurningOffKeepsHistory()
    {
        var store = NewStore();
        store.SetEnabled(true);
        var started = store.Settings.ResumedAt;
        store.Record(new[] { new RecentDocument(@"C:\Jobs\test.pdf", _time.UtcNow.AddMinutes(1)) }, _ => true);
        _time.Advance(TimeSpan.FromHours(1));
        var editor = new RecentFilesSettingsViewModel(store) { TrackEverywhere = true };

        Assert.True(editor.Save());
        Assert.Equal(started, store.Settings.ResumedAt);
        editor.RecentFilesEnabled = false;
        Assert.True(editor.Save());
        Assert.Single(NewStore().QueryFiles());
        editor.RecentFilesEnabled = true;
        Assert.True(editor.Save());
        Assert.Equal(_time.UtcNow, store.Settings.ResumedAt);
        Assert.Equal(started, store.Settings.TrackingStartedAt);
    }

    [Fact]
    public void UnavailableStore_CannotBeSaved()
    {
        _storage.ReadThrows = new System.IO.IOException("Unavailable");
        _storage.ContentsToReturn = "{}";
        var editor = new RecentFilesSettingsViewModel(NewStore()) { TrackEverywhere = true };
        Assert.False(editor.RecentFilesAvailable);
        Assert.False(editor.CanSave);
        Assert.NotNull(editor.RecentFilesNotice);
        Assert.False(editor.Save());
        Assert.Equal(0, _storage.WriteCount);
    }
}
