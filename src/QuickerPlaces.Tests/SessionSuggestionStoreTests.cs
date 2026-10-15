using System;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Models.Sessions;
using QuickerPlaces.Services.Sessions.Suggestions;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// session-suggestions.json (session suggestions plan §8): the switches, the
/// sensitivity levels, the dismissed suggestions and the buffered open spans,
/// and loading a file that is missing, damaged or from a newer version.
/// </summary>
public sealed class SessionSuggestionStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static (SessionSuggestionStore Store, FakePlacesStorage Storage, ManualTimeProvider Time) New(string? contents = null)
    {
        var storage = new FakePlacesStorage { StoreFilePath = @"C:\fake\session-suggestions.json", ContentsToReturn = contents };
        var time = new ManualTimeProvider(Now);
        return (new SessionSuggestionStore(storage, time), storage, time);
    }

    private static SuggestionSettings On(bool openTogether = true)
        => new(true, openTogether, SuggestionSensitivity.Balanced, null, null);

    private static int History => SessionSuggestionOptions.Balanced.HistoryDays;

    private static OpenSpan Span(string path, int hoursAgo, int lengthHours = 1)
        => new(path, Now.AddHours(-hoursAgo), Now.AddHours(-hoursAgo + lengthHours));

    [Fact]
    public void AMissingFile_ReadsAsOff()
    {
        var (store, storage, _) = New();

        Assert.Equal(StoreLoadOutcome.NotPresent, store.LoadOutcome);
        Assert.True(store.IsAvailable);
        Assert.Null(store.Notice);
        Assert.False(store.Settings.Enabled);
        Assert.True(store.Settings.OpenTogether);
        Assert.Equal(SuggestionSensitivity.Balanced, store.Settings.Overall);
        Assert.Null(store.Settings.UsedTogether);
        Assert.Null(store.Settings.OpenTogetherLevel);
        Assert.False(store.IsDismissed("k"));
        Assert.Empty(store.OpenSpans);
        Assert.Equal(0, storage.WriteCount);
    }

    [Fact]
    public void SettingsDismissalsAndSpans_RoundTrip()
    {
        var (store, storage, _) = New();
        var settings = new SuggestionSettings(true, true, SuggestionSensitivity.High, SuggestionSensitivity.Low, SuggestionSensitivity.Balanced);
        Assert.True(store.SaveSettings(settings).Saved);
        Assert.True(store.Dismiss("key-1", 3).Saved);
        store.SetOpenSpans(new[] { Span("a.pdf", 5) });
        Assert.True(store.HasUnsavedChanges);
        Assert.True(store.Flush().Saved);
        Assert.False(store.HasUnsavedChanges);

        var reloaded = new SessionSuggestionStore(storage, new ManualTimeProvider(Now));

        Assert.Equal(StoreLoadOutcome.Ok, reloaded.LoadOutcome);
        Assert.Equal(settings, reloaded.Settings);
        Assert.True(reloaded.IsDismissed("key-1"));
        Assert.Equal(new[] { Span("a.pdf", 5) }, reloaded.OpenSpans);
        Assert.Contains("\"schemaVersion\": 1", storage.LastWritten);
    }

    [Fact]
    public void UnknownSensitivityText_IsLenient()
    {
        var (store, _, _) = New("""{"schemaVersion":1,"enabled":true,"sensitivity":"medium","usedTogetherSensitivity":"x","openTogetherSensitivity":"HIGH","dismissed":[]}""");

        Assert.Equal(SuggestionSensitivity.Balanced, store.Settings.Overall);
        Assert.Null(store.Settings.UsedTogether);
        Assert.Equal(SuggestionSensitivity.High, store.Settings.OpenTogetherLevel);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{"schemaVersion":1,"enabled":true}""")]
    [InlineData("""{"enabled":true,"dismissed":[]}""")]
    public void ADamagedFile_IsQuarantined_AndStartsFresh(string contents)
    {
        var (store, storage, _) = New(contents);

        Assert.Equal(StoreLoadOutcome.Damaged, store.LoadOutcome);
        Assert.Equal(1, storage.QuarantineCount);
        Assert.True(store.IsAvailable);
        Assert.NotNull(store.Notice);
        Assert.False(store.Settings.Enabled);
        Assert.True(store.SaveSettings(On()).Saved);
    }

    [Fact]
    public void ADamagedFileThatCannotBeSetAside_IsReadOnly()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = "{ not json", QuarantineThrows = new System.IO.IOException("locked") };
        var store = new SessionSuggestionStore(storage, new ManualTimeProvider(Now));

        Assert.False(store.IsAvailable);
        Assert.NotNull(store.Notice);
        Assert.False(store.SaveSettings(On()).Saved);
        Assert.Equal(0, storage.WriteCount);
    }

    [Fact]
    public void AnUnreadableFile_IsNotAvailable()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = "{}", ReadThrows = new System.IO.IOException("locked") };
        var store = new SessionSuggestionStore(storage, new ManualTimeProvider(Now));

        Assert.Equal(StoreLoadOutcome.Unreadable, store.LoadOutcome);
        Assert.False(store.IsAvailable);
        Assert.NotNull(store.Notice);
        Assert.False(store.Dismiss("k", 2).Saved);
        Assert.Equal(0, storage.WriteCount);
        Assert.Equal(0, storage.QuarantineCount);
    }

    [Fact]
    public void ANewerFile_IsNotAvailable_AndNothingIsWritten()
    {
        var (store, storage, _) = New("""{"schemaVersion":2,"enabled":true,"dismissed":[]}""");

        Assert.Equal(StoreLoadOutcome.WrittenByNewerVersion, store.LoadOutcome);
        Assert.False(store.IsAvailable);
        Assert.NotNull(store.Notice);
        Assert.False(store.SaveSettings(On()).Saved);
        Assert.False(store.Dismiss("k", 2).Saved);
        store.SetOpenSpans(new[] { Span("a.pdf", 2) });
        Assert.Empty(store.OpenSpans);
        Assert.Equal(0, storage.WriteCount);
        Assert.Equal(0, storage.QuarantineCount);
    }

    [Fact]
    public void TurningOpenTogetherOff_ClearsSpans()
    {
        var (store, storage, _) = New();
        store.SaveSettings(On());
        store.SetOpenSpans(new[] { Span("a.pdf", 3) });
        store.Flush();

        Assert.True(store.SaveSettings(On(openTogether: false)).Saved);

        Assert.Empty(store.OpenSpans);
        Assert.DoesNotContain("a.pdf", storage.LastWritten);
    }

    [Fact]
    public void TurningEnabledOff_ClearsSpans_AndKeepsDismissals()
    {
        var (store, _, _) = New();
        store.SaveSettings(On());
        store.Dismiss("k", 2);
        store.SetOpenSpans(new[] { Span("a.pdf", 3) });

        Assert.True(store.SaveSettings(On() with { Enabled = false }).Saved);

        Assert.Empty(store.OpenSpans);
        Assert.True(store.IsDismissed("k"));
    }

    [Fact]
    public void SetOpenSpans_WhileRuleBIsOff_KeepsNothing()
    {
        var (store, storage, _) = New();

        store.SetOpenSpans(new[] { Span("a.pdf", 3) });
        Assert.Empty(store.OpenSpans);

        store.SaveSettings(On(openTogether: false));
        store.SetOpenSpans(new[] { Span("a.pdf", 3) });
        Assert.Empty(store.OpenSpans);
        Assert.False(store.HasUnsavedChanges);
        Assert.Equal(1, storage.WriteCount);
    }

    [Fact]
    public void Dismiss_IsIdempotent_AndKeepsTheLatestTime()
    {
        var (store, storage, time) = New();
        store.Dismiss("k", 2);
        time.Advance(TimeSpan.FromDays(1));

        Assert.True(store.Dismiss("k", 3).Saved);

        Assert.True(store.IsDismissed("k"));
        Assert.False(store.IsDismissed("other"));
        var reloaded = new SessionSuggestionStore(storage, time);
        Assert.True(reloaded.IsDismissed("k"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(storage.LastWritten!, "\"key\""));
        Assert.Contains("2026-10-16", storage.LastWritten);
        Assert.Contains("\"files\": 3", storage.LastWritten);
    }

    [Fact]
    public void OldDismissalsAndSpans_ArePrunedOnWrite()
    {
        var (store, storage, time) = New();
        store.SaveSettings(On());
        store.Dismiss("old", 2);
        time.Advance(TimeSpan.FromDays(366));
        store.SetOpenSpans(new[]
        {
            new OpenSpan("old.pdf", time.UtcNow.AddDays(-(History + 1)), time.UtcNow.AddDays(-(History + 1)).AddHours(1)),
            new OpenSpan("kept.pdf", time.UtcNow.AddDays(-(History - 1)), time.UtcNow.AddDays(-(History - 1)).AddHours(1)),
        });
        store.Dismiss("new", 2);

        Assert.False(store.IsDismissed("old"));
        Assert.True(store.IsDismissed("new"));
        Assert.Equal(new[] { "kept.pdf" }, store.OpenSpans.Select(s => s.Path));
        Assert.DoesNotContain("old.pdf", storage.LastWritten);
    }

    [Fact]
    public void Options_FollowOverallAndRuleLevels()
    {
        var (store, _, _) = New();
        store.SaveSettings(new SuggestionSettings(true, true, SuggestionSensitivity.High, null, SuggestionSensitivity.Low));

        var expected = SessionSuggestionOptions.For(SuggestionSensitivity.High, null, SuggestionSensitivity.Low);
        var options = store.Settings.Options;

        Assert.Equal(expected.TogetherOpen, options.TogetherOpen);
        Assert.Equal(expected.AlwaysOpenAbove, options.AlwaysOpenAbove);
        Assert.Equal(expected.MinShare, options.MinShare);
        Assert.NotEqual(SessionSuggestionOptions.For(SuggestionSensitivity.High).TogetherOpen, options.TogetherOpen);
    }

    [Fact]
    public void AFailedSettingsWrite_KeepsTheChange_AndFlushSavesIt()
    {
        var (store, storage, _) = New();
        store.SaveSettings(On());
        store.SetOpenSpans(new[] { Span("a.pdf", 3) });
        store.Flush();
        storage.FailNextWrite = true;

        var result = store.SaveSettings(On(openTogether: false));

        Assert.False(result.Saved);
        Assert.True(store.HasUnsavedChanges);
        Assert.Empty(store.OpenSpans);
        Assert.True(store.Flush().Saved);
        Assert.False(store.HasUnsavedChanges);
        Assert.Contains("\"openTogether\": false", storage.LastWritten);
        Assert.DoesNotContain("a.pdf", storage.LastWritten);
    }

    [Fact]
    public void AFailedDismiss_KeepsTheChange_AndFlushSavesIt()
    {
        var (store, storage, _) = New();
        storage.FailNextWrite = true;

        Assert.False(store.Dismiss("k", 2).Saved);

        Assert.True(store.HasUnsavedChanges);
        Assert.True(store.IsDismissed("k"));
        Assert.True(store.Flush().Saved);
        Assert.True(new SessionSuggestionStore(storage, new ManualTimeProvider(Now)).IsDismissed("k"));
    }

    [Fact]
    public void ANullOpenSpansList_LoadsOk()
    {
        var (store, _, _) = New("""{"schemaVersion":1,"enabled":true,"openTogether":true,"dismissed":[],"openSpans":null}""");

        Assert.Equal(StoreLoadOutcome.Ok, store.LoadOutcome);
        Assert.Empty(store.OpenSpans);
    }

    [Fact]
    public void NullAndBlankEntries_AreDroppedOnLoad()
    {
        var (store, _, _) = New("""
            {"schemaVersion":1,"enabled":true,"openTogether":true,
             "dismissed":[null,{"key":null},{"key":"k","files":2,"dismissedAt":"2026-10-01T00:00:00Z"}],
             "openSpans":[null,{"path":null,"from":"2026-10-14T09:00:00Z","to":"2026-10-14T10:00:00Z"},
                          {"path":"ok.pdf","from":"2026-10-14T09:00:00Z","to":"2026-10-14T10:00:00Z"}]}
            """);

        Assert.Equal(StoreLoadOutcome.Ok, store.LoadOutcome);
        Assert.True(store.IsDismissed("k"));
        Assert.Equal(new[] { "ok.pdf" }, store.OpenSpans.Select(s => s.Path));
    }

    // Comparisons are strict: an entry exactly at the limit is kept, one minute older is pruned.
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void DismissalsAreKeptForExactly365Days(int extraMinutes, bool kept)
    {
        var (store, _, time) = New();
        store.Dismiss("old", 2);
        time.Advance(TimeSpan.FromDays(365) + TimeSpan.FromMinutes(extraMinutes));

        store.Dismiss("new", 2);

        Assert.Equal(kept, store.IsDismissed("old"));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void SpansAreKeptForExactlyHistoryDays(int extraMinutes, bool kept)
    {
        var (store, _, time) = New();
        store.SaveSettings(On());
        var to = time.UtcNow.AddDays(-History).AddMinutes(-extraMinutes);
        store.SetOpenSpans(new[] { new OpenSpan("a.pdf", to.AddHours(-1), to) });

        store.Dismiss("k", 2);

        Assert.Equal(kept ? 1 : 0, store.OpenSpans.Count);
    }

    [Fact]
    public void TheFileUsesThePlannedPropertyNames()
    {
        var (store, storage, _) = New("""
            {"schemaVersion":1,"enabled":true,"openTogether":true,"sensitivity":"high",
             "usedTogetherSensitivity":"low","openTogetherSensitivity":"balanced",
             "dismissed":[{"key":"k","files":3,"dismissedAt":"2026-10-14T09:00:00+00:00"}],
             "openSpans":[{"path":"a.pdf","from":"2026-10-14T09:00:00+00:00","to":"2026-10-14T10:00:00+00:00"}]}
            """);

        Assert.Equal(new SuggestionSettings(true, true, SuggestionSensitivity.High, SuggestionSensitivity.Low, SuggestionSensitivity.Balanced), store.Settings);
        Assert.True(store.IsDismissed("k"));
        Assert.Single(store.OpenSpans);

        store.Dismiss("k2", 1);
        foreach (var name in new[] { "schemaVersion", "enabled", "openTogether", "sensitivity", "usedTogetherSensitivity",
                                     "openTogetherSensitivity", "dismissed", "key", "files", "dismissedAt", "openSpans", "path", "from", "to" })
            Assert.Contains($"\"{name}\":", storage.LastWritten);
    }

    [Fact]
    public void SpansLoadedWhileRuleBIsOff_AreDropped_AndFlushRemovesThemFromDisk()
    {
        var (store, storage, _) = New("""{"schemaVersion":1,"enabled":true,"openTogether":false,"dismissed":[],"openSpans":[{"path":"a.pdf","from":"2026-10-14T09:00:00Z","to":"2026-10-14T10:00:00Z"}]}""");

        Assert.Empty(store.OpenSpans);
        Assert.True(store.HasUnsavedChanges);
        Assert.True(store.Flush().Saved);
        Assert.False(store.HasUnsavedChanges);
        Assert.DoesNotContain("a.pdf", storage.LastWritten);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void DismissingABlankKey_WritesNothing(string? key)
    {
        var (store, storage, _) = New();

        Assert.True(store.Dismiss(key, 2).Saved);

        Assert.Equal(0, storage.WriteCount);
        Assert.False(store.HasUnsavedChanges);
    }
}
