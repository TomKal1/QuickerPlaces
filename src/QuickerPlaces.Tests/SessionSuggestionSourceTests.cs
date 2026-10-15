using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickerPlaces.Models.Sessions;
using QuickerPlaces.Models.RecentFiles;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Services.Sessions.Suggestions;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

public sealed class SessionSuggestionSourceTests
{
    private const string A = @"C:\Jobs\240015\a.pdf";
    private const string B = @"C:\Jobs\240015\b.pdf";
    private const string C = @"C:\Jobs\240015\c.pdf";
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
    private readonly FakePlacesStorage _sessionDisk = new();
    private readonly FakePlacesStorage _suggestionDisk = new();
    private readonly FakeShell _shell = new();

    private (SessionSuggestionSource Source, SessionStore Sessions, SessionSuggestionStore Store, RecentFilesStore Recent) New(IShell? shell = null,
        Func<IReadOnlyList<TrackedRootConfig>>? roots = null)
    {
        var recent = new RecentFilesStore(new FakePlacesStorage(), _time);
        recent.SetEnabled(true);
        recent.SetScope(RecentFilesScope.Everywhere);
        var settings = new SessionSuggestionStore(_suggestionDisk, _time);
        settings.SaveSettings(new(true, true, SuggestionSensitivity.Balanced, null, null));
        settings.SetOpenSpans(new[] { A, B, C }.Select(f => new OpenSpan(f, _time.UtcNow.AddHours(-2), _time.UtcNow)));
        _shell.ExistingFiles.UnionWith(new[] { A, B, C });
        var sessions = new SessionStore(_sessionDisk, _time);
        return (new SessionSuggestionSource(recent, settings, sessions, shell ?? _shell, _time, roots), sessions, settings, recent);
    }

    [Fact]
    public async Task Refresh_CachesAndBadgeNeverCalculates()
    {
        var (source, _, _, _) = New();
        Assert.Equal(0, source.Count);
        var cards = await source.RefreshAsync();
        Assert.Single(cards);
        Assert.Equal(1, source.Count);
        _shell.ExistingFiles.Clear();
        Assert.Equal(1, source.Count);
        Assert.Same(cards, await source.RefreshAsync());
        source.Invalidate();
        Assert.Empty(await source.RefreshAsync());
    }

    [Fact]
    public async Task MissingFilesAreDropped_BeforeGrouping()
    {
        var (source, _, _, _) = New();
        _shell.ExistingFiles.Remove(C);
        Assert.Equal(new[] { A, B }, Assert.Single(await source.RefreshAsync()).Files);
        _shell.ExistingFiles.Remove(B);
        source.Invalidate();
        Assert.Empty(await source.RefreshAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EitherConsentOff_ClearsResults(bool suggestionsOff)
    {
        var (source, _, store, recent) = New();
        await source.RefreshAsync();
        if (suggestionsOff) store.SaveSettings(store.Settings with { Enabled = false });
        else recent.SetEnabled(false);
        Assert.Empty(await source.RefreshAsync());
        Assert.Equal(0, source.Count);
    }

    [Fact]
    public async Task Dismissal_PersistsSubsetsButAllowsGrowth_WithoutPaths()
    {
        var (source, _, store, _) = New();
        var card = Assert.Single(await source.RefreshAsync());
        Assert.True(source.Dismiss(card).Saved);
        Assert.Empty(await source.RefreshAsync());
        var reload = new SessionSuggestionStore(_suggestionDisk, _time);
        Assert.True(reload.IsDismissed(new[] { A, B }));
        Assert.False(reload.IsDismissed(new[] { A, B, C, @"C:\Jobs\240015\d.pdf" }));
        // Remove spans first: only hashes, never dismissed paths, belong in the dismissal record.
        store.SetOpenSpans([]);
        store.Flush();
        Assert.DoesNotContain("a.pdf", _suggestionDisk.LastWritten);
    }

    [Fact]
    public async Task FailedDismissalSave_StillHidesCardInMemory()
    {
        var (source, _, _, _) = New();
        var card = Assert.Single(await source.RefreshAsync());
        _suggestionDisk.FailNextWrite = true;
        Assert.False(source.Dismiss(card).Saved);
        Assert.Equal(0, source.Count);
        Assert.Empty(await source.RefreshAsync());
    }

    [Fact]
    public async Task NewAndUndo_RemoveOnlyCreatedSession()
    {
        var (source, sessions, _, _) = New();
        var result = source.Create(Assert.Single(await source.RefreshAsync()));
        Assert.True(result.Success);
        Assert.Equal(3, Assert.Single(sessions.Sessions).Files.Count);
        Assert.True(source.Undo().Success);
        Assert.Empty(sessions.Sessions);
        Assert.False(source.Undo().Success);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UndoNew_RefusesEditedOrDeletedSession(bool delete)
    {
        var (source, sessions, _, _) = New();
        var result = source.Create(Assert.Single(await source.RefreshAsync()));
        if (delete) sessions.Delete(result.SessionId!);
        else sessions.TryUpdate(result.SessionId!, "Edited", [], [A], out _);
        var undo = source.Undo();
        Assert.False(undo.Success);
        Assert.NotNull(undo.Message);
        if (!delete) Assert.Equal("Edited", Assert.Single(sessions.Sessions).Name);
    }

    [Fact]
    public async Task FailedCreateSave_CanStillBeUndone()
    {
        var (source, sessions, _, _) = New();
        var card = Assert.Single(await source.RefreshAsync());
        _sessionDisk.FailNextWrite = true;
        var result = source.Create(card);
        Assert.True(result.Success);
        Assert.False(result.Persistence.Saved);
        Assert.True(source.Undo().Success);
        Assert.Empty(sessions.Sessions);
    }

    [Fact]
    public async Task AddUsesCurrentNameTagsAndFiles_AndUndoRestoresThem()
    {
        var (source, sessions, _, _) = New();
        sessions.TryCreate("Original", ["old"], [A, B], out var target, out _);
        var card = Assert.Single(await source.RefreshAsync());
        sessions.TryUpdate(target!.Id, "Renamed", ["new"], [A], out _);
        Assert.True(source.Add(card, target.Id).Success);
        Assert.Equal(3, sessions.Find(target.Id)!.Files.Count);
        Assert.True(source.Undo().Success);
        var restored = sessions.Find(target.Id)!;
        Assert.Equal("Renamed", restored.Name);
        Assert.Equal(new[] { "new" }, restored.Tags);
        Assert.Equal(new[] { A }, restored.Files);
    }

    [Fact]
    public async Task AddRefusesDeletedTarget_WithoutCreatingSession()
    {
        var (source, sessions, _, _) = New();
        sessions.TryCreate("Target", [], [A, B], out var target, out _);
        var card = Assert.Single(await source.RefreshAsync());
        sessions.Delete(target!.Id);
        Assert.False(source.Add(card, target.Id).Success);
        Assert.Empty(sessions.Sessions);
    }

    [Fact]
    public async Task UndoAdd_RefusesSubsequentEdit()
    {
        var (source, sessions, _, _) = New();
        sessions.TryCreate("Target", [], [A, B], out var target, out _);
        source.Add(Assert.Single(await source.RefreshAsync()), target!.Id);
        sessions.TryUpdate(target.Id, "Edited", [], [A, B, C], out _);
        Assert.False(source.Undo().Success);
        Assert.Equal("Edited", sessions.Find(target.Id)!.Name);
    }

    [Fact]
    public async Task InvalidActionPreservesPreviousUndo()
    {
        var (source, sessions, _, _) = New();
        var card = Assert.Single(await source.RefreshAsync());
        source.Create(card);
        Assert.False(source.Add(card, "gone").Success);
        Assert.True(source.Undo().Success);
        Assert.Empty(sessions.Sessions);
    }

    [Fact]
    public async Task LegacyKeyOnlyDismissal_IsStillHonoured()
    {
        var (source, _, store, _) = New();
        var card = Assert.Single(await source.RefreshAsync());
        store.Dismiss(card.Key, card.Files.Count);
        Assert.Empty(await source.RefreshAsync());
    }

    [Theory]
    [InlineData("dismiss")]
    [InlineData("invalidate")]
    [InlineData("settings")]
    [InlineData("sessions")]
    [InlineData("kinds")]
    [InlineData("scope")]
    public async Task InFlightRefresh_DiscardsStaleReturnAndCache(string change)
    {
        using var shell = new BlockingShell();
        var (source, sessions, store, recent) = New(shell);
        var card = Assert.Single(await source.RefreshAsync());
        source.Invalidate();
        shell.Block = true;
        var refresh = source.RefreshAsync();
        try
        {
            Assert.True(await Task.Run(() => shell.Entered.Wait(TimeSpan.FromSeconds(5))));
            // These must complete while FileExists (and the expensive calculation) is blocked.
            Assert.Equal(0, source.Count);
            switch (change)
            {
                case "dismiss": source.Dismiss(card); break;
                case "settings": store.SaveSettings(store.Settings with { Enabled = false }); break;
                case "sessions": sessions.TryCreate("Now saved", [], [A, B, C], out _, out _); break;
                case "kinds": recent.TrySetKinds(new[] { DocumentKind.Word }, out _); break;
                case "scope": recent.SetScope(RecentFilesScope.TrackedFolders); break;
                default: source.Invalidate(); break;
            }
        }
        finally { shell.Release.Set(); }
        Assert.Empty(await refresh.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, source.Count);
    }

    [Fact]
    public async Task UsedTogetherRule_ProducesCardsWithoutOpenSpans()
    {
        var (source, _, store, recent) = New();
        store.SetOpenSpans([]);
        _time.Advance(TimeSpan.FromDays(4));
        for (var day = 3; day >= 1; day--)
            Assert.Equal(2, recent.Record(new[] { new RecentDocument(A, _time.UtcNow.AddDays(-day)),
                new RecentDocument(B, _time.UtcNow.AddDays(-day).AddMinutes(1)) }, _ => true));
        var card = Assert.Single(await source.RefreshAsync());
        Assert.Equal(3, card.Reason.DaysTogether);
        Assert.Null(card.Reason.LongestOpenTogether);
    }

    [Fact]
    public async Task SensitivityChange_RecalculatesCachedResult()
    {
        var (source, _, store, _) = New();
        await source.RefreshAsync();
        store.SaveSettings(store.Settings with { Overall = SuggestionSensitivity.Low });
        Assert.Empty(await source.RefreshAsync()); // Low needs two open-together days.
        store.SaveSettings(store.Settings with { Overall = SuggestionSensitivity.Balanced });
        Assert.Single(await source.RefreshAsync());
    }

    [Fact]
    public async Task SessionSavedElsewhere_InvalidatesCachedGrouping()
    {
        var (source, sessions, _, _) = New();
        await source.RefreshAsync();
        sessions.TryCreate("Saved elsewhere", [], [A, B, C], out _, out _);
        Assert.Empty(await source.RefreshAsync());
    }

    [Fact]
    public async Task CrossingHistoryWindow_ExpiresCachedCard()
    {
        var (source, _, _, _) = New();
        await source.RefreshAsync();
        _time.Advance(TimeSpan.FromDays(15));
        Assert.Empty(await source.RefreshAsync());
    }

    [Fact]
    public async Task DismissedGroup_ReturnsOnlyAfterGrowing()
    {
        var (source, _, store, _) = New();
        source.Dismiss(Assert.Single(await source.RefreshAsync()));
        const string extra = @"C:\Jobs\240015\d.pdf";
        _shell.ExistingFiles.Add(extra);
        store.SetOpenSpans(new[] { A, B, C, extra }.Select(f => new OpenSpan(f, _time.UtcNow.AddHours(-2), _time.UtcNow)));
        Assert.Equal(4, Assert.Single(await source.RefreshAsync()).Files.Count);
        Assert.True(store.IsDismissed(new[] { A.ToUpperInvariant(), B }));
    }

    [Fact]
    public async Task ExpiredDismissal_NoLongerHidesCard()
    {
        var (source, _, store, _) = New();
        source.Dismiss(Assert.Single(await source.RefreshAsync()));
        _time.Advance(TimeSpan.FromDays(366));
        store.SetOpenSpans(new[] { A, B, C }.Select(f => new OpenSpan(f, _time.UtcNow.AddHours(-2), _time.UtcNow)));
        Assert.Single(await source.RefreshAsync());
    }

    [Fact]
    public async Task FailedAddSave_IsAcceptedInMemoryAndUndoRestoresOldFiles()
    {
        var (source, sessions, _, _) = New();
        sessions.TryCreate("Target", [], [A, B], out var target, out _);
        var card = Assert.Single(await source.RefreshAsync());
        _sessionDisk.FailNextWrite = true;
        var result = source.Add(card, target!.Id);
        Assert.True(result.Success);
        Assert.False(result.Persistence.Saved);
        Assert.Equal(3, sessions.Find(target.Id)!.Files.Count);
        Assert.True(source.Undo().Success);
        Assert.Equal(new[] { A, B }, sessions.Find(target.Id)!.Files);
    }

    [Fact]
    public async Task FailedUndoSave_ReportsUnsavedMemoryChange()
    {
        var (source, sessions, _, _) = New();
        source.Create(Assert.Single(await source.RefreshAsync()));
        _sessionDisk.FailNextWrite = true;
        var result = source.Undo();
        Assert.True(result.Success);
        Assert.False(result.Persistence.Saved);
        Assert.Empty(sessions.Sessions);
        Assert.True(sessions.HasUnsavedChanges);
    }

    [Fact]
    public async Task OpeningAndReordering_AreNotEditsForUndoNew()
    {
        var (source, sessions, _, _) = New();
        sessions.TryCreate("Other", [], [A], out var other, out _);
        var created = source.Create(Assert.Single(await source.RefreshAsync()));
        sessions.MarkOpened(created.SessionId!);
        sessions.Move(created.SessionId!, other!.Id);
        Assert.True(source.Undo().Success);
        Assert.Equal(other.Id, Assert.Single(sessions.Sessions).Id);
    }

    [Fact]
    public async Task UndoAdd_PreservesLaterReopenHistory()
    {
        var (source, sessions, _, _) = New();
        sessions.TryCreate("Target", [], [A, B], out var target, out _);
        source.Add(Assert.Single(await source.RefreshAsync()), target!.Id);
        _time.Advance(TimeSpan.FromHours(1));
        sessions.MarkOpened(target.Id);
        Assert.True(source.Undo().Success);
        var restored = sessions.Find(target.Id)!;
        Assert.Equal(new[] { A, B }, restored.Files);
        Assert.Equal(_time.UtcNow, restored.LastOpenedAt);
        Assert.Equal(new[] { _time.UtcNow }, restored.OpenedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CurrentFileTypeSettings_FilterBothRulesAndInvalidateCache(bool openTogether)
    {
        var (source, _, store, recent) = New();
        if (!openTogether)
        {
            store.SetOpenSpans([]);
            _time.Advance(TimeSpan.FromDays(4));
            for (var day = 3; day >= 1; day--)
                recent.Record(new[] { new RecentDocument(A, _time.UtcNow.AddDays(-day)),
                    new RecentDocument(B, _time.UtcNow.AddDays(-day).AddMinutes(1)) }, _ => true);
        }
        Assert.Single(await source.RefreshAsync());
        recent.TrySetKinds(new[] { DocumentKind.Word }, out _);
        Assert.Empty(await source.RefreshAsync());
        recent.TrySetKinds(new[] { DocumentKind.Pdf }, out _);
        Assert.Single(await source.RefreshAsync());
    }

    [Fact]
    public async Task CurrentTrackedRoots_FilterRetainedFilesAndInvalidateCache()
    {
        var roots = new List<TrackedRootConfig>();
        var (source, _, _, recent) = New(roots: () => roots.ToArray());
        recent.SetScope(RecentFilesScope.TrackedFolders);
        Assert.Empty(await source.RefreshAsync());
        roots.Add(new("job", @"C:\Jobs\240015"));
        Assert.Single(await source.RefreshAsync());
        roots.Clear();
        Assert.Empty(await source.RefreshAsync());
        recent.SetScope(RecentFilesScope.Everywhere);
        Assert.Single(await source.RefreshAsync());
    }

    [Fact]
    public async Task EquivalentTrackedFolderPrefixes_AreRespected()
    {
        var roots = new[] { new TrackedRootConfig("mapped", @"Z:\Jobs") { EquivalentPrefixes = new[] { @"C:\Jobs" } } };
        var (source, _, _, recent) = New(roots: () => roots);
        recent.SetScope(RecentFilesScope.TrackedFolders);
        Assert.Single(await source.RefreshAsync());
    }

    [Fact]
    public async Task OutOfScopeFiles_AreRemovedBeforeGrouping()
    {
        var (source, _, store, recent) = New(roots: () => new[] { new TrackedRootConfig("job", @"C:\Jobs") });
        const string outside = @"C:\Other\private.pdf";
        _shell.ExistingFiles.Add(outside);
        store.SetOpenSpans(new[] { A, B, C, outside }.Select(f => new OpenSpan(f, _time.UtcNow.AddHours(-2), _time.UtcNow)));
        recent.SetScope(RecentFilesScope.TrackedFolders);
        Assert.Equal(new[] { A, B, C }, Assert.Single(await source.RefreshAsync()).Files);
    }

    private sealed class BlockingShell : IShell, IDisposable
    {
        public bool Block;
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public bool FileExists(string path)
        {
            if (Block)
            {
                Entered.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            }
            return true;
        }
        public bool DirectoryExists(string path) => false;
        public void Open(string target) { }
        public void Dispose() { Entered.Dispose(); Release.Dispose(); }
    }
}
