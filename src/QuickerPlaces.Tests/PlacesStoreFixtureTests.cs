using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Test 16 from the Phase 1 plan's section 6, as re-asserted by test 11 of
/// the Phase 2 plan's section 7: an existing v1 places.json, with the shape
/// every pre-Phase-2 build wrote, loads with every field intact — now
/// migrated to v2, DateAdded in UTC. And Phase 2's test 12: a v2 file, with
/// places in Recently Deleted, loads with every field intact too. This is
/// the regression net for an accidental change to JsonOptions, to Place's
/// property names, or to the migration.
///
/// Fixtures/places.v1.json and places.v2.json are frozen once written — do
/// not "fix" either to match a future schema change; a new fixture is
/// added for that instead. Each test pins its fixture's content hash, so an
/// edit fails here rather than quietly changing what is being proven.
/// </summary>
public sealed class PlacesStoreFixtureTests
{
    private static string ReadFixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    /// <summary>
    /// SHA-256 of the fixture's text with line endings normalised to LF, so
    /// the pin holds on a checkout that converts them (git's autocrlf on
    /// Windows) and fails only when the content itself changes.
    /// </summary>
    private static string ContentHash(string text)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"))));

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute, int second)
        => new(year, month, day, hour, minute, second, TimeSpan.Zero);

    /// <summary>Asserts the instant and that the value is held as UTC — DateTimeOffset equality alone ignores the offset.</summary>
    private static void AssertUtc(DateTimeOffset expected, DateTimeOffset? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected, actual!.Value);
        Assert.Equal(TimeSpan.Zero, actual.Value.Offset);
    }

    /// <summary>
    /// Test 11: places.v1.json loads migrated, every field intact, DateAdded
    /// in UTC. Its offset-less dates are read as local time in the injected
    /// PlusTen zone (D11), never the machine's, so 09:30 on 15 January is
    /// 23:30 UTC the day before wherever this runs.
    /// </summary>
    [Fact]
    public void FixtureFile_LoadsWithEveryFieldIntact()
    {
        var json = ReadFixture("places.v1.json");
        Assert.Equal("0417e71fc1083d5fe46defb2b30dcc913d90ba6beb4d2346fb0cf10c6c072135", ContentHash(json));

        var storage = new FakePlacesStorage { ContentsToReturn = json };
        var service = new PlacesService(storage, new ManualTimeProvider(Utc(2026, 9, 25, 0, 0, 0), TestZones.PlusTen));

        Assert.Equal(StoreLoadOutcome.Ok, service.LoadOutcome);
        Assert.Equal(3, service.Places.Count);
        Assert.Empty(service.RecentlyDeleted);

        var downloads = service.Places.Single(p => p.Alias == "Downloads");
        Assert.Equal(PlaceType.Folder, downloads.Type);
        Assert.Equal(@"C:\Users\Test\Downloads", downloads.Resource);
        Assert.True(downloads.IsFavourite);
        Assert.Equal(0, downloads.FavouriteOrder);
        AssertUtc(Utc(2026, 1, 14, 23, 30, 0), downloads.DateAdded);
        Assert.Null(downloads.DeletedAt);

        var repo = service.Places.Single(p => p.Alias == "QuickerPlaces Repo");
        Assert.Equal(PlaceType.Url, repo.Type);
        Assert.Equal("https://github.com/example/quickerplaces", repo.Resource);
        Assert.True(repo.IsFavourite);
        Assert.Equal(1, repo.FavouriteOrder);
        AssertUtc(Utc(2026, 2, 3, 4, 5, 22), repo.DateAdded);
        Assert.Null(repo.DeletedAt);

        var oldReports = service.Places.Single(p => p.Alias == "Old Reports");
        Assert.Equal(PlaceType.Folder, oldReports.Type);
        Assert.Equal(@"D:\Archive\Reports\2024", oldReports.Resource);
        Assert.False(oldReports.IsFavourite);
        Assert.Null(oldReports.FavouriteOrder);
        AssertUtc(Utc(2025, 11, 19, 22, 12, 47), oldReports.DateAdded);
        Assert.Null(oldReports.DeletedAt);

        // Loading never writes (roadmap §4.3): the migrated store is only
        // in memory, and the stored text is the fixture's, untouched.
        Assert.Equal(0, storage.WriteCount);
        Assert.Equal(json, storage.ContentsToReturn);
    }

    /// <summary>
    /// Test 12: places.v2.json loads with every field intact — three
    /// active places and two in Recently Deleted, with DeletedAt and the
    /// remembered favourite slot kept (D9). Places excludes the deleted
    /// ones; RecentlyDeleted lists them newest deletion first. The clock is
    /// 2026-09-25, before either deletion's seven days are up.
    /// </summary>
    [Fact]
    public void V2FixtureFile_LoadsWithEveryFieldIntact()
    {
        var json = ReadFixture("places.v2.json");
        Assert.Equal("e65b4a51b68895da9948b0c20c946be62cc22f732481243073bc3c115f573c60", ContentHash(json));

        var storage = new FakePlacesStorage { ContentsToReturn = json };
        var service = new PlacesService(storage, new ManualTimeProvider(Utc(2026, 9, 25, 0, 0, 0), TestZones.MinusFive));

        Assert.Equal(StoreLoadOutcome.Ok, service.LoadOutcome);
        Assert.Equal(new[] { "Downloads", "QuickerPlaces Repo", "reports" }, service.Places.Select(p => p.Alias));
        Assert.Equal(new[] { "Reports", "Old Wiki" }, service.RecentlyDeleted.Select(p => p.Alias));

        var downloads = service.Places[0];
        Assert.Equal(PlaceType.Folder, downloads.Type);
        Assert.Equal(@"C:\Users\Test\Downloads", downloads.Resource);
        Assert.True(downloads.IsFavourite);
        Assert.Equal(0, downloads.FavouriteOrder);
        AssertUtc(Utc(2026, 1, 14, 23, 30, 0), downloads.DateAdded);
        Assert.Null(downloads.DeletedAt);

        var repo = service.Places[1];
        Assert.Equal(PlaceType.Url, repo.Type);
        Assert.Equal("https://github.com/example/quickerplaces", repo.Resource);
        Assert.True(repo.IsFavourite);
        Assert.Equal(1, repo.FavouriteOrder);
        AssertUtc(Utc(2026, 2, 3, 4, 5, 22), repo.DateAdded);
        Assert.Null(repo.DeletedAt);

        var activeReports = service.Places[2];
        Assert.Equal(PlaceType.Folder, activeReports.Type);
        Assert.Equal(@"D:\Reports", activeReports.Resource);
        Assert.False(activeReports.IsFavourite);
        Assert.Null(activeReports.FavouriteOrder);
        AssertUtc(Utc(2026, 9, 24, 18, 2, 10), activeReports.DateAdded);
        Assert.Null(activeReports.DeletedAt);

        var deletedReports = service.RecentlyDeleted[0];
        Assert.Equal(PlaceType.Folder, deletedReports.Type);
        Assert.Equal(@"D:\Archive\Reports", deletedReports.Resource);
        Assert.False(deletedReports.IsFavourite);
        Assert.Null(deletedReports.FavouriteOrder);
        AssertUtc(Utc(2025, 11, 19, 22, 12, 47), deletedReports.DateAdded);
        AssertUtc(Utc(2026, 9, 24, 17, 45, 0), deletedReports.DeletedAt);

        // Its remembered bubble slot (1) is kept even though the active
        // "QuickerPlaces Repo" now holds bubble 1: loading never renumbers.
        var oldWiki = service.RecentlyDeleted[1];
        Assert.Equal(PlaceType.Url, oldWiki.Type);
        Assert.Equal("https://wiki.example.com/old", oldWiki.Resource);
        Assert.True(oldWiki.IsFavourite);
        Assert.Equal(1, oldWiki.FavouriteOrder);
        AssertUtc(Utc(2025, 6, 2, 10, 15, 0), oldWiki.DateAdded);
        AssertUtc(Utc(2026, 9, 20, 8, 0, 0), oldWiki.DeletedAt);

        Assert.Equal(0, storage.WriteCount);
    }

    /// <summary>
    /// Not a numbered plan test: saving the loaded v2 fixture writes it
    /// back exactly — every record, deleted ones included and in the same
    /// list slots (D7), with this build's own formatting. Proves the fixture
    /// really is the shape this build writes, not merely one it can read.
    /// </summary>
    [Fact]
    public void V2FixtureFile_IsWrittenBackExactly()
    {
        var json = ReadFixture("places.v2.json");
        var storage = new FakePlacesStorage { ContentsToReturn = json };
        var service = new PlacesService(storage, new ManualTimeProvider(Utc(2026, 9, 25, 0, 0, 0)));

        Assert.True(service.RetrySave().Saved);

        Assert.Equal(json.Replace("\r\n", "\n").TrimEnd(), storage.LastWritten!.Replace("\r\n", "\n").TrimEnd());
    }
}
