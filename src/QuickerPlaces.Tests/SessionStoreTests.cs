using System;
using System.IO;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// sessions.json (sessions plan §3): saving, editing and deleting sessions,
/// the rules a session must meet, tags, and loading a file that is missing,
/// damaged, unreadable or from a newer version.
/// </summary>
public sealed class SessionStoreTests
{
    private const string A101 = @"C:\Jobs\Tower B\A-101.pdf";
    private const string A102 = @"C:\Jobs\Tower B\A-102.pdf";
    private const string Spec = @"\\files\projects\Tower B\Spec.pdf";

    private static SessionStore NewStore(FakePlacesStorage? storage = null, ManualTimeProvider? time = null)
        => new(storage ?? new FakePlacesStorage { StoreFilePath = @"C:\fake\sessions.json" }, time ?? new ManualTimeProvider());

    private static SessionSnapshot Create(SessionStore store, string name, string[]? tags = null, params string[] files)
    {
        var result = store.TryCreate(name, tags, files.Length == 0 ? new[] { A101 } : files, out var created, out var persistence);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(persistence.Saved);
        return created!;
    }

    [Fact]
    public void ANewSession_IsSavedAtOnce_AndLoadsBack()
    {
        var storage = new FakePlacesStorage();
        var time = new ManualTimeProvider();
        var store = NewStore(storage, time);

        var created = Create(store, "  Tower B markups  ", new[] { "Tower B", "markups" }, A101, Spec);

        Assert.Equal(1, storage.WriteCount);
        Assert.Equal("Tower B markups", created.Name);
        Assert.Equal(new[] { "Tower B", "markups" }, created.Tags);
        Assert.Equal(new[] { A101, Spec }, created.Files);
        Assert.Equal(time.UtcNow, created.CreatedAt);
        Assert.Equal(time.UtcNow, created.UpdatedAt);
        Assert.Null(created.LastOpenedAt);

        var reloaded = NewStore(storage, time);
        Assert.Equal(StoreLoadOutcome.Ok, reloaded.LoadOutcome);
        var session = Assert.Single(reloaded.Sessions);
        Assert.Equal(created.Id, session.Id);
        Assert.Equal(created.Tags, session.Tags);
        Assert.Equal(created.Files, session.Files);
    }

    [Fact]
    public void NoFile_IsFirstRun_AndNothingIsWrittenUntilASave()
    {
        var storage = new FakePlacesStorage();
        var store = NewStore(storage);

        Assert.Equal(StoreLoadOutcome.NotPresent, store.LoadOutcome);
        Assert.True(store.IsAvailable);
        Assert.Null(store.Notice);
        Assert.Empty(store.Sessions);
        Assert.Equal(0, storage.WriteCount);
    }

    [Theory]
    [InlineData(null, "Give the session a name.")]
    [InlineData("   ", "Give the session a name.")]
    public void ABlankName_IsRefused(string? name, string message)
    {
        var store = NewStore();

        var result = store.TryCreate(name, null, new[] { A101 }, out var created, out _);

        Assert.False(result.Success);
        Assert.Equal(message, result.ErrorMessage);
        Assert.Null(created);
        Assert.Empty(store.Sessions);
    }

    [Fact]
    public void ANameInUse_IgnoringCase_IsRefused_ButEditingASessionMayKeepItsOwnName()
    {
        var store = NewStore();
        var first = Create(store, "Tower B");

        Assert.False(store.TryCreate("tower b", null, new[] { A102 }, out _, out _).Success);
        Assert.True(store.TryUpdate(first.Id, "TOWER B", null, new[] { A101 }, out _).Success);
        Assert.Equal("TOWER B", store.Find(first.Id)!.Name);
    }

    [Fact]
    public void AnOverlongName_IsRefused()
    {
        var store = NewStore();

        Assert.False(store.TryCreate(new string('x', SessionStore.MaxNameLength + 1), null, new[] { A101 }, out _, out _).Success);
        Assert.True(store.TryCreate(new string('x', SessionStore.MaxNameLength), null, new[] { A101 }, out _, out _).Success);
    }

    [Fact]
    public void NoFiles_IsRefused()
    {
        var store = NewStore();

        var result = store.TryCreate("Empty", null, Array.Empty<string>(), out _, out _);

        Assert.False(result.Success);
        Assert.Equal("Choose at least one file for the session.", result.ErrorMessage);
    }

    [Theory]
    [InlineData(@"Jobs\A-101.pdf")]
    [InlineData(@"C:\Jobs\notes.txt")]
    [InlineData(@"https://example.com/a.pdf")]
    [InlineData(@"C:\")]
    public void APathThatIsNotAFullPathToAPdf_IsRefused_AndNamed(string path)
    {
        var store = NewStore();

        var result = store.TryCreate("Bad", null, new[] { A101, path }, out _, out _);

        Assert.False(result.Success);
        Assert.Contains(path, result.ErrorMessage);
        Assert.Empty(store.Sessions);
    }

    [Fact]
    public void RepeatedFilesAndTags_AreKeptOnce_FirstSpellingWins()
    {
        var store = NewStore();

        var created = Create(store, "Dupes", new[] { "Tower B", "tower b", " #Tower  B ", "", "Markups" },
            A101, A101.ToUpperInvariant(), @"C:\Jobs\Tower B\.\A-101.pdf", A102);

        Assert.Equal(new[] { "Tower B", "Markups" }, created.Tags);
        Assert.Equal(new[] { A101, A102 }, created.Files);
    }

    [Fact]
    public void TooManyOrOverlongTags_AreRefused()
    {
        var store = NewStore();

        var many = Enumerable.Range(1, SessionStore.MaxTags + 1).Select(i => $"t{i}").ToArray();
        Assert.False(store.TryCreate("Many", many, new[] { A101 }, out _, out _).Success);
        Assert.False(store.TryCreate("Long", new[] { new string('t', SessionStore.MaxTagLength + 1) }, new[] { A101 }, out _, out _).Success);
        Assert.False(store.TryCreate("Comma", new[] { "a,b" }, new[] { A101 }, out _, out _).Success);
    }

    [Fact]
    public void ParseTags_SplitsOnCommasAndSemicolons_AndCleansEachTag()
    {
        Assert.Equal(new[] { "Tower B", "markups", "RFI 12" }, SessionStore.ParseTags(" Tower B , markups;#RFI   12;; tower b,"));
        Assert.Empty(SessionStore.ParseTags(null));
        Assert.Empty(SessionStore.ParseTags(" , ; "));
        Assert.Equal("a, b", SessionStore.FormatTags(new[] { "a", "b" }));
    }

    [Fact]
    public void Tags_ListsEachTagOnce_WithItsSessionCount_Alphabetically()
    {
        var store = NewStore();
        Create(store, "One", new[] { "Tower B", "markups" });
        Create(store, "Two", new[] { "tower b" }, A102);
        Create(store, "Three", new[] { "Admin" }, Spec);

        Assert.Equal(
            new[] { new TagCount("Admin", 1), new TagCount("markups", 1), new TagCount("Tower B", 2) },
            store.Tags);
    }

    [Fact]
    public void Update_ReplacesNameTagsAndFiles_AndStampsUpdatedAt()
    {
        var time = new ManualTimeProvider();
        var store = NewStore(time: time);
        var created = Create(store, "Tower B", new[] { "a" });
        time.Advance(TimeSpan.FromHours(1));

        var result = store.TryUpdate(created.Id, "Tower B rev 2", new[] { "b" }, new[] { A102, Spec }, out var persistence);

        Assert.True(result.Success);
        Assert.True(persistence.Saved);
        var updated = store.Find(created.Id)!;
        Assert.Equal("Tower B rev 2", updated.Name);
        Assert.Equal(new[] { "b" }, updated.Tags);
        Assert.Equal(new[] { A102, Spec }, updated.Files);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.Equal(time.UtcNow, updated.UpdatedAt);
    }

    [Fact]
    public void Update_WithNothingChanged_WritesNothing()
    {
        var storage = new FakePlacesStorage();
        var store = NewStore(storage);
        var created = Create(store, "Tower B", new[] { "a" });

        Assert.True(store.TryUpdate(created.Id, "Tower B", new[] { "a" }, created.Files, out _).Success);

        Assert.Equal(1, storage.WriteCount);
    }

    [Fact]
    public void Update_OfADeletedSession_IsRefused()
    {
        var store = NewStore();
        var created = Create(store, "Tower B");
        store.Delete(created.Id);

        var result = store.TryUpdate(created.Id, "Tower B", null, new[] { A101 }, out _);

        Assert.False(result.Success);
        Assert.Equal("That session no longer exists.", result.ErrorMessage);
    }

    [Fact]
    public void Delete_RemovesOnlyThatSession_AndIsSaved()
    {
        var storage = new FakePlacesStorage();
        var store = NewStore(storage);
        var keep = Create(store, "Keep");
        var drop = Create(store, "Drop", null, A102);

        Assert.True(store.Delete(drop.Id).Saved);
        Assert.True(store.Delete("unknown").Saved);

        Assert.Equal(new[] { keep.Id }, NewStore(storage).Sessions.Select(s => s.Id));
    }

    [Fact]
    public void Sessions_AreMostRecentlyUsedFirst_OpeningCounts()
    {
        var time = new ManualTimeProvider();
        var store = NewStore(time: time);
        var older = Create(store, "Older");
        time.Advance(TimeSpan.FromMinutes(1));
        var newer = Create(store, "Newer", null, A102);
        Assert.Equal(new[] { "Newer", "Older" }, store.Sessions.Select(s => s.Name));

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(store.MarkOpened(older.Id).Saved);

        Assert.Equal(new[] { "Older", "Newer" }, store.Sessions.Select(s => s.Name));
        Assert.Equal(time.UtcNow, store.Find(older.Id)!.LastOpenedAt);
        Assert.Equal(time.UtcNow, store.Find(older.Id)!.LastUsedAt);
        Assert.Null(store.Find(newer.Id)!.LastOpenedAt);
    }

    [Fact]
    public void AFailedWrite_IsReported_KeptInMemory_AndRetried()
    {
        var storage = new FakePlacesStorage { FailNextWrite = true };
        var store = NewStore(storage);

        var result = store.TryCreate("Tower B", null, new[] { A101 }, out var created, out var persistence);

        Assert.True(result.Success);
        Assert.False(persistence.Saved);
        Assert.Contains("Couldn't save your sessions", persistence.UserMessage);
        Assert.True(store.HasUnsavedChanges);
        Assert.NotNull(store.Find(created!.Id));

        Assert.True(store.RetrySave().Saved);
        Assert.False(store.HasUnsavedChanges);
        Assert.Single(NewStore(storage).Sessions);
    }

    [Fact]
    public void ADamagedFile_IsQuarantined_AndTheListStartsEmpty_WithANotice()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = "{ not json" };

        var store = NewStore(storage);

        Assert.Equal(StoreLoadOutcome.Damaged, store.LoadOutcome);
        Assert.True(store.IsAvailable);
        Assert.Equal(1, storage.QuarantineCount);
        Assert.Contains(storage.QuarantinedPath!, store.Notice);
        Assert.Empty(store.Sessions);
        Assert.True(store.TryCreate("New", null, new[] { A101 }, out _, out _).Success);
    }

    [Theory]
    [InlineData(@"{""sessions"":[]}")]
    [InlineData(@"{""schemaVersion"":""1"",""sessions"":[]}")]
    [InlineData(@"{""schemaVersion"":0,""sessions"":[]}")]
    [InlineData(@"{""schemaVersion"":1}")]
    [InlineData(@"{""schemaVersion"":1,""schemaVersion"":1,""sessions"":[]}")]
    [InlineData(@"[]")]
    public void AFileWithoutAUsableVersionOrList_IsDamaged(string json)
    {
        var storage = new FakePlacesStorage { ContentsToReturn = json };

        Assert.Equal(StoreLoadOutcome.Damaged, NewStore(storage).LoadOutcome);
    }

    [Fact]
    public void ADamagedFileThatCantBeSetAside_LeavesTheStoreReadOnly()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = "{ not json", QuarantineThrows = new IOException("locked") };

        var store = NewStore(storage);

        Assert.False(store.IsAvailable);
        Assert.False(store.TryCreate("New", null, new[] { A101 }, out _, out _).Success);
        Assert.Equal(0, storage.WriteCount);
    }

    [Fact]
    public void AnUnreadableFile_IsLeftAlone_AndEveryChangeIsRefused()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = "{}", ReadThrows = new IOException("locked") };

        var store = NewStore(storage);

        Assert.Equal(StoreLoadOutcome.Unreadable, store.LoadOutcome);
        Assert.False(store.IsAvailable);
        Assert.Contains("couldn't be opened", store.Notice);
        var result = store.TryCreate("New", null, new[] { A101 }, out _, out _);
        Assert.False(result.Success);
        Assert.Equal(store.Notice, result.ErrorMessage);
        Assert.False(store.Delete("x").Saved);
        Assert.Equal(0, storage.WriteCount);
        Assert.Equal(0, storage.QuarantineCount);
    }

    [Fact]
    public void AFileFromANewerVersion_IsLeftAlone_AndEveryChangeIsRefused()
    {
        var storage = new FakePlacesStorage { ContentsToReturn = @"{""schemaVersion"":2,""sessions"":[]}" };

        var store = NewStore(storage);

        Assert.Equal(StoreLoadOutcome.WrittenByNewerVersion, store.LoadOutcome);
        Assert.False(store.IsAvailable);
        Assert.False(store.TryCreate("New", null, new[] { A101 }, out _, out _).Success);
        Assert.True(store.MarkOpened("x").Saved);
        Assert.Equal(0, storage.WriteCount);
        Assert.Equal(0, storage.QuarantineCount);
    }

    [Fact]
    public void AHandEditedFile_IsMadeSafe_NullsDropped_RepeatedIdsReplaced()
    {
        var storage = new FakePlacesStorage
        {
            ContentsToReturn = @"{""schemaVersion"":1,""sessions"":[
                null,
                {""id"":""a"",""name"":""One"",""tags"":[""x"",null,""X"",""  ""],""files"":[""C:\\A.pdf"",null,""c:\\a.pdf""]},
                {""id"":""a"",""name"":""Two""}
            ]}",
        };

        var store = NewStore(storage);

        Assert.Equal(StoreLoadOutcome.Ok, store.LoadOutcome);
        Assert.Equal(2, store.Sessions.Count);
        Assert.Equal(2, store.Sessions.Select(s => s.Id).Distinct().Count());
        var one = store.Sessions.Single(s => s.Name == "One");
        Assert.Equal(new[] { "x" }, one.Tags);
        Assert.Equal(new[] { @"C:\A.pdf" }, one.Files);
        Assert.Empty(store.Sessions.Single(s => s.Name == "Two").Files);
    }

    [Fact]
    public void ARealFile_RoundTripsUnicode_AndADamagedOneIsSetAsideBesideItself()
    {
        using var dir = new TempDirectory();
        var time = new ManualTimeProvider();
        const string file = @"C:\Jobs – Ärchiv\Café 日本.pdf";
        var first = new SessionStore(new FilePlacesStorage(dir.Path, "sessions.json"), time);
        Assert.True(first.TryCreate("Ärchiv 🗂", new[] { "日本" }, new[] { file }, out _, out var saved).Success);
        Assert.True(saved.Saved);

        var second = new SessionStore(new FilePlacesStorage(dir.Path, "sessions.json"), time);
        var session = Assert.Single(second.Sessions);
        Assert.Equal("Ärchiv 🗂", session.Name);
        Assert.Equal(new[] { "日本" }, session.Tags);
        Assert.Equal(new[] { file }, session.Files);

        File.WriteAllText(dir.File("places.json"), "the user's places");
        File.WriteAllText(dir.File("sessions.json"), "{ damaged");
        var third = new SessionStore(new FilePlacesStorage(dir.Path, "sessions.json"), time);
        Assert.Equal(StoreLoadOutcome.Damaged, third.LoadOutcome);
        Assert.Single(Directory.GetFiles(dir.Path, "sessions.corrupt-*.json"));
        Assert.Equal("the user's places", File.ReadAllText(dir.File("places.json")));
    }

    [Fact]
    public void EveryReopen_IsKept_ForTheYearView_AndDaysCountSavesAndReopensByTag()
    {
        // ManualTimeProvider's zone is UTC+10.
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero));
        var storage = new FakePlacesStorage();
        var store = NewStore(storage, time);
        var tower = Create(store, "Tower B", new[] { "Tower B" });
        Create(store, "Admin", new[] { "admin" }, A102);
        time.Advance(TimeSpan.FromDays(1));
        store.MarkOpened(tower.Id);
        store.MarkOpened(tower.Id);

        var reloaded = NewStore(storage, time);
        Assert.Equal(2, reloaded.Find(tower.Id)!.OpenedAt.Count);

        var days = reloaded.QueryDays();
        Assert.Equal(2, days[new DateOnly(2026, 9, 28)].Saved);
        Assert.Equal(0, days[new DateOnly(2026, 9, 28)].Reopens);
        Assert.Equal(2, days[new DateOnly(2026, 9, 29)].Reopens);
        Assert.Equal(new[] { "Tower B" }, days[new DateOnly(2026, 9, 29)].Names);

        var tagged = reloaded.QueryDays("TOWER B");
        Assert.Equal(1, tagged[new DateOnly(2026, 9, 28)].Saved);
        Assert.Equal(new[] { "Tower B" }, tagged[new DateOnly(2026, 9, 28)].Names);
    }

    [Fact]
    public void ReopenHistory_IsKeptAYear()
    {
        var time = new ManualTimeProvider();
        var store = NewStore(time: time);
        var session = Create(store, "Tower B");
        store.MarkOpened(session.Id);
        time.Advance(TimeSpan.FromDays(SessionStore.HistoryDays + 1));

        store.MarkOpened(session.Id);

        Assert.Equal(new[] { time.UtcNow }, store.Find(session.Id)!.OpenedAt);
    }
}
