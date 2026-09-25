using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

public sealed class PlacesServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string PlacesFile => Path.Combine(_temp.Path, "places.json");

    private PlacesService NewService() => NewServiceAt("places.json");

    /// <summary>One pinned clock for every service in a test (D12), so DeletedAt and DateAdded never read the machine's.</summary>
    private readonly ManualTimeProvider _clock = new();

    private PlacesService NewServiceAt(string fileName) => new(new FilePlacesStorage(_temp.Path, fileName), _clock);

    /// <summary>An absolute folder path that's fully qualified on whichever OS the tests run on.</summary>
    private string Folder(string name) => Path.Combine(_temp.Path, name);

    private static Place Add(PlacesService service, string alias, PlaceType type, string resource)
    {
        var result = service.TryAdd(alias, type, resource, out var created, out var persistence);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(persistence.Saved, persistence.UserMessage);
        return created!;
    }

    /// <summary>Removes <paramref name="place"/>, asserting it saved, and returns what the Undo stack holds: the record itself (D10).</summary>
    private static Place RemoveForUndo(PlacesService service, Place place)
    {
        var persistence = service.Remove(place, out var removed);
        Assert.True(persistence.Saved, persistence.UserMessage);
        Assert.True(removed);
        return place;
    }

    // -----------------------------------------------------------------
    // Loading
    // -----------------------------------------------------------------

    [Fact]
    public void Null_entries_in_file_are_dropped_on_load()
    {
        var resource = Folder("Docs").Replace("\\", "\\\\");
        File.WriteAllText(PlacesFile, $$"""
            { "schemaVersion": 1, "places": [ null, { "alias": "Docs", "type": "folder", "resource": "{{resource}}" } ] }
            """);

        var service = NewService();

        Assert.Equal(StoreLoadOutcome.Ok, service.LoadOutcome);
        Assert.Single(service.Places);
        Assert.Equal("Docs", service.Places[0].Alias);
    }

    // -----------------------------------------------------------------
    // Validation
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_alias_is_rejected(string? alias)
        => Assert.False(NewService().ValidateAlias(alias).Success);

    [Fact]
    public void Alias_uniqueness_is_case_insensitive_and_trimmed()
    {
        var service = NewService();
        Add(service, "Docs", PlaceType.Folder, Folder("Docs"));

        Assert.False(service.ValidateAlias("docs").Success);
        Assert.False(service.ValidateAlias("  DOCS  ").Success);
        Assert.True(service.ValidateAlias("Docs2").Success);
    }

    [Fact]
    public void Alias_check_excludes_the_place_being_edited()
    {
        var service = NewService();
        var docs = Add(service, "Docs", PlaceType.Folder, Folder("Docs"));

        Assert.True(service.ValidateAlias("DOCS", excluding: docs).Success);
    }

    [Fact]
    public void Resource_duplicate_check_is_case_insensitive_but_not_normalized()
    {
        var service = NewService();
        var path = Folder("Projects");
        Add(service, "Projects", PlaceType.Folder, path);

        Assert.False(service.ValidateResource(path.ToUpperInvariant(), PlaceType.Folder).Success);
        // Trailing separator is deliberately a different value (SI §6.2).
        Assert.True(service.ValidateResource(path + Path.DirectorySeparatorChar, PlaceType.Folder).Success);
    }

    [Fact]
    public void Resource_duplicate_check_is_per_type()
    {
        var service = NewService();
        Add(service, "Wiki", PlaceType.Url, "https://wiki.example.com");

        Assert.False(service.ValidateResource("https://WIKI.example.com", PlaceType.Url).Success);
        Assert.True(service.ValidateResource("http://wiki.example.com", PlaceType.Url).Success);
    }

    [Theory]
    [InlineData("Projects")]
    [InlineData("..\\Docs")]
    [InlineData("../Docs")]
    public void Relative_folder_paths_are_rejected(string path)
        => Assert.False(NewService().ValidateResource(path, PlaceType.Folder).Success);

    [Fact]
    public void Folder_need_not_exist_on_disk()
        => Assert.True(NewService().ValidateResource(Folder("not-created-yet"), PlaceType.Folder).Success);

    [Fact]
    public void Windows_drive_and_unc_paths_are_accepted()
    {
        if (!OperatingSystem.IsWindows())
            return; // Drive letters and UNC roots only mean "fully qualified" on Windows.

        var service = NewService();
        Assert.True(service.ValidateResource(@"C:\Projects", PlaceType.Folder).Success);
        Assert.True(service.ValidateResource(@"\\server\share\team", PlaceType.Folder).Success);
        Assert.False(service.ValidateResource(@"C:Projects", PlaceType.Folder).Success);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://localhost:8080/path?q=1", true)]
    [InlineData("example.com", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    public void Url_format_validation(string url, bool expected)
        => Assert.Equal(expected, NewService().ValidateResource(url, PlaceType.Url).Success);

    // -----------------------------------------------------------------
    // Mutations + write-through persistence
    // -----------------------------------------------------------------

    [Fact]
    public void Add_trims_and_persists_immediately()
    {
        var service = NewService();
        Add(service, "  Wiki  ", PlaceType.Url, "  https://wiki.example.com  ");

        var reloaded = NewService();
        var place = Assert.Single(reloaded.Places);
        Assert.Equal("Wiki", place.Alias);
        Assert.Equal("https://wiki.example.com", place.Resource);
        Assert.Equal(PlaceType.Url, place.Type);
        Assert.False(place.IsFavourite);
        Assert.Null(place.FavouriteOrder);
    }

    [Fact]
    public void Failed_add_does_not_change_store()
    {
        var service = NewService();
        Add(service, "Wiki", PlaceType.Url, "https://wiki.example.com");

        var result = service.TryAdd("wiki", PlaceType.Url, "https://other.example.com", out var created, out _);

        Assert.False(result.Success);
        Assert.Null(created);
        Assert.Single(NewService().Places);
    }

    [Fact]
    public void Rename_and_edit_persist()
    {
        var service = NewService();
        var place = Add(service, "Docs", PlaceType.Folder, Folder("Docs"));

        Assert.True(service.TryRenameAlias(place, "Documents", out _).Success);
        Assert.True(service.TryEditResource(place, Folder("Documents"), out _).Success);

        var reloaded = Assert.Single(NewService().Places);
        Assert.Equal("Documents", reloaded.Alias);
        Assert.Equal(Folder("Documents"), reloaded.Resource);
    }

    [Fact]
    public void Rename_to_another_places_alias_is_rejected()
    {
        var service = NewService();
        Add(service, "Docs", PlaceType.Folder, Folder("Docs"));
        var wiki = Add(service, "Wiki", PlaceType.Url, "https://wiki.example.com");

        Assert.False(service.TryRenameAlias(wiki, "DOCS", out _).Success);
        Assert.Equal("Wiki", wiki.Alias);
    }

    [Fact]
    public void Remove_persists()
    {
        var service = NewService();
        var place = Add(service, "Docs", PlaceType.Folder, Folder("Docs"));

        RemoveForUndo(service, place);

        Assert.Empty(NewService().Places);
    }

    // -----------------------------------------------------------------
    // Undo remove
    // -----------------------------------------------------------------

    /// <summary>Phase 2 test 25: Undo returns the same instance to the same list slot, and persists (§4.8).</summary>
    [Fact]
    public void Restore_puts_a_removed_place_back_where_it_was_and_persists()
    {
        var service = NewService();
        Add(service, "A", PlaceType.Folder, Folder("A"));
        var b = Add(service, "B", PlaceType.Folder, Folder("B"));
        Add(service, "C", PlaceType.Folder, Folder("C"));
        var dateAdded = b.DateAdded;

        var removed = RemoveForUndo(service, b);
        Assert.Equal(new[] { "A", "C" }, service.Places.Select(p => p.Alias));

        Assert.True(service.TryRestore(removed, out _).Success);

        Assert.Equal(new[] { "A", "B", "C" }, service.Places.Select(p => p.Alias));
        Assert.Same(b, service.Places[1]);
        Assert.Equal(dateAdded, b.DateAdded);
        Assert.Equal(new[] { "A", "B", "C" }, NewService().Places.Select(p => p.Alias));
    }

    // Phase 2 test 27: this test and the three after it are the pre-Phase-2
    // favourite-restore tests, unchanged apart from RemovedPlace -> Place.
    // D9 persists exactly today's rule, so they must still hold.

    [Fact]
    public void Restore_puts_a_favourite_back_at_its_old_bubble_position()
    {
        var service = NewService();
        var a = Add(service, "A", PlaceType.Folder, Folder("A"));
        var b = Add(service, "B", PlaceType.Folder, Folder("B"));
        var c = Add(service, "C", PlaceType.Folder, Folder("C"));
        service.ToggleFavourite(a);
        service.ToggleFavourite(b);
        service.ToggleFavourite(c);

        var removed = RemoveForUndo(service, b);
        Assert.Equal(1, removed.FavouriteOrder);   // its remembered slot (D9)
        Assert.Equal(1, c.FavouriteOrder);

        Assert.True(service.TryRestore(removed, out _).Success);

        Assert.True(b.IsFavourite);
        Assert.Equal(new int?[] { 0, 1, 2 }, new[] { a, b, c }.Select(p => p.FavouriteOrder));
    }

    [Fact]
    public void Restore_uses_bubble_order_not_list_order_after_a_drag_reorder()
    {
        var service = NewService();
        var a = Add(service, "A", PlaceType.Folder, Folder("A"));
        var b = Add(service, "B", PlaceType.Folder, Folder("B"));
        var c = Add(service, "C", PlaceType.Folder, Folder("C"));
        service.ToggleFavourite(a);
        service.ToggleFavourite(b);
        service.ToggleFavourite(c);
        service.SetFavouriteOrder(new[] { b, c, a });   // bubbles: B, C, A

        var removed = RemoveForUndo(service, c);              // bubbles: B, A
        Assert.True(service.TryRestore(removed, out _).Success);

        // C sits later than A in the list, but goes back between B and A.
        Assert.Equal(new[] { "B", "C", "A" },
            service.Places.Where(p => p.IsFavourite).OrderBy(p => p.FavouriteOrder).Select(p => p.Alias));
    }

    [Fact]
    public void Restore_closes_gaps_when_other_favourites_went_in_the_meantime()
    {
        var service = NewService();
        var a = Add(service, "A", PlaceType.Folder, Folder("A"));
        var b = Add(service, "B", PlaceType.Folder, Folder("B"));
        var c = Add(service, "C", PlaceType.Folder, Folder("C"));
        service.ToggleFavourite(a);
        service.ToggleFavourite(b);
        service.ToggleFavourite(c);

        var removedC = RemoveForUndo(service, c);   // was bubble 2
        service.ToggleFavourite(a);          // unfavourite A: B is now bubble 0

        Assert.True(service.TryRestore(removedC, out _).Success);

        Assert.Equal(0, b.FavouriteOrder);
        Assert.Equal(1, c.FavouriteOrder);
        Assert.Null(a.FavouriteOrder);
    }

    [Fact]
    public void Removals_restore_in_reverse_order_to_their_original_positions()
    {
        var service = NewService();
        var places = new[] { "A", "B", "C", "D" }.Select(n => Add(service, n, PlaceType.Folder, Folder(n))).ToList();

        var removedB = RemoveForUndo(service, places[1]);
        var removedD = RemoveForUndo(service, places[3]);
        var removedA = RemoveForUndo(service, places[0]);

        Assert.True(service.TryRestore(removedA, out _).Success);
        Assert.True(service.TryRestore(removedD, out _).Success);
        Assert.True(service.TryRestore(removedB, out _).Success);

        Assert.Equal(new[] { "A", "B", "C", "D" }, service.Places.Select(p => p.Alias));
    }

    /// <summary>Phase 2 test 28: a conflicting restore is refused with the alias or path/URL message, and the place stays in Recently Deleted (D15).</summary>
    [Fact]
    public void Restore_refuses_when_the_alias_or_resource_has_been_reused()
    {
        var service = NewService();
        var docs = Add(service, "Docs", PlaceType.Folder, Folder("Docs"));
        var wiki = Add(service, "Wiki", PlaceType.Url, "https://wiki.example.com");

        var removedDocs = RemoveForUndo(service, docs);
        _clock.Advance(TimeSpan.FromMinutes(1));
        var removedWiki = RemoveForUndo(service, wiki);
        Add(service, "docs", PlaceType.Folder, Folder("Other"));
        Add(service, "New Wiki", PlaceType.Url, "https://wiki.example.com");

        var aliasResult = service.TryRestore(removedDocs, out _);
        var resourceResult = service.TryRestore(removedWiki, out _);

        Assert.False(aliasResult.Success);
        Assert.Contains("alias", aliasResult.ErrorMessage);
        Assert.False(resourceResult.Success);
        Assert.Contains("path/URL", resourceResult.ErrorMessage);
        Assert.Equal(new[] { "docs", "New Wiki" }, service.Places.Select(p => p.Alias));
        Assert.Equal(new[] { wiki, docs }, service.RecentlyDeleted);
        Assert.Equal(new[] { "Wiki", "Docs" }, NewService().RecentlyDeleted.Select(p => p.Alias));
    }

    /// <summary>Phase 2 test 29, first half: a second restore is refused rather than duplicating (5.4 step 3).</summary>
    [Fact]
    public void Restore_twice_is_refused_rather_than_duplicating()
    {
        var service = NewService();
        var docs = Add(service, "Docs", PlaceType.Folder, Folder("Docs"));
        var removed = RemoveForUndo(service, docs);

        Assert.True(service.TryRestore(removed, out _).Success);
        var again = service.TryRestore(removed, out _);

        Assert.False(again.Success);
        Assert.Contains("already back in the list", again.ErrorMessage);
        Assert.Single(service.Places);
    }

    /// <summary>
    /// Phase 2 test 29, second half: restoring a record that is no longer in
    /// the store says it is no longer in Recently Deleted (5.4 step 2).
    /// Here the instance is the one an earlier session's Undo stack would
    /// hold after the store was reloaded: a different object from the
    /// record now in memory, so it is not in the store.
    /// </summary>
    [Fact]
    public void Restoring_a_place_no_longer_in_the_store_says_so()
    {
        var service = NewService();
        var docs = Add(service, "Docs", PlaceType.Folder, Folder("Docs"));
        RemoveForUndo(service, docs);
        var reloaded = NewService();

        var result = reloaded.TryRestore(docs, out var persistence);

        Assert.False(result.Success);
        Assert.Contains("no longer in Recently Deleted", result.ErrorMessage);
        Assert.True(persistence.Saved);
        Assert.Empty(reloaded.Places);
        Assert.Single(reloaded.RecentlyDeleted);
    }

    /// <summary>Phase 2 test 30: removing a place that is not in the store, or is already deleted, reports removed == false and writes nothing.</summary>
    [Fact]
    public void Removing_a_place_not_in_the_store_or_already_removed_changes_nothing()
    {
        var storage = new FakePlacesStorage();
        var service = new PlacesService(storage, _clock);
        var docs = Add(service, "Docs", PlaceType.Folder, Folder("Docs"));
        RemoveForUndo(service, docs);
        var deletedAt = docs.DeletedAt;
        var writes = storage.WriteCount;
        _clock.Advance(TimeSpan.FromHours(1));

        var againPersistence = service.Remove(docs, out var removedAgain);
        var strangerPersistence = service.Remove(new Place { Alias = "Stranger", Type = PlaceType.Folder, Resource = Folder("Stranger") }, out var removedStranger);

        Assert.False(removedAgain);
        Assert.True(againPersistence.Saved);
        Assert.False(removedStranger);
        Assert.True(strangerPersistence.Saved);
        Assert.Equal(deletedAt, docs.DeletedAt);
        Assert.Equal(writes, storage.WriteCount);
    }

    [Fact]
    public void Save_leaves_no_temp_file_behind()
    {
        var service = NewService();
        Add(service, "Docs", PlaceType.Folder, Folder("Docs"));

        Assert.False(File.Exists(PlacesFile + ".tmp"));
    }

    // -----------------------------------------------------------------
    // Favourites
    // -----------------------------------------------------------------

    [Fact]
    public void Favourite_order_stays_dense_through_toggle_reorder_and_remove()
    {
        var service = NewService();
        var a = Add(service, "A", PlaceType.Url, "https://a.example.com");
        var b = Add(service, "B", PlaceType.Url, "https://b.example.com");
        var c = Add(service, "C", PlaceType.Url, "https://c.example.com");

        service.ToggleFavourite(a);
        service.ToggleFavourite(b);
        service.ToggleFavourite(c);
        Assert.Equal(new int?[] { 0, 1, 2 }, new[] { a, b, c }.Select(p => p.FavouriteOrder));

        service.SetFavouriteOrder(new[] { c, a, b });
        Assert.Equal(new int?[] { 1, 2, 0 }, new[] { a, b, c }.Select(p => p.FavouriteOrder));

        service.ToggleFavourite(a); // un-favourite the middle one
        Assert.False(a.IsFavourite);
        Assert.Null(a.FavouriteOrder);
        Assert.Equal(new int?[] { 1, 0 }, new[] { b, c }.Select(p => p.FavouriteOrder));

        RemoveForUndo(service, c);
        Assert.Equal(0, b.FavouriteOrder);

        var reloaded = Assert.Single(NewService().Places, p => p.IsFavourite);
        Assert.Equal("B", reloaded.Alias);
        Assert.Equal(0, reloaded.FavouriteOrder);
    }

    // -----------------------------------------------------------------
    // Export / import
    // -----------------------------------------------------------------

    [Fact]
    public void Export_then_import_into_empty_store_round_trips()
    {
        var source = NewService();
        Add(source, "Docs", PlaceType.Folder, Folder("Docs"));
        var wiki = Add(source, "Wiki", PlaceType.Url, "https://wiki.example.com");
        source.ToggleFavourite(wiki);

        var exportFile = Path.Combine(_temp.Path, "export.json");
        Assert.Null(source.Export(source.Places, exportFile));

        var target = NewServiceAt("other.json");
        var (candidates, error) = target.GetImportCandidates(exportFile);
        Assert.Null(error);
        Assert.Equal(2, candidates.Count);

        var (imported, _) = target.CommitImport(candidates);
        Assert.Equal(2, imported.Count);
        // Imported items arrive as fresh, non-favourite records.
        Assert.All(target.Places, p => Assert.False(p.IsFavourite));
        Assert.Equal(2, NewServiceAt("other.json").Places.Count);

        // Phase 2 test 39: and the export reads as version 2.
        Assert.Equal(2, JsonNode.Parse(File.ReadAllText(exportFile))!["schemaVersion"]!.GetValue<int>());
    }

    /// <summary>
    /// Phase 2 test 33: Export leaves out places in Recently Deleted even
    /// when the caller passes them, writes schemaVersion 2, and no record
    /// has a deletedAt key (D16).
    /// </summary>
    [Fact]
    public void Export_leaves_out_deleted_places_even_when_given_them()
    {
        var service = NewService();
        Add(service, "Docs", PlaceType.Folder, Folder("Docs"));
        var wiki = Add(service, "Wiki", PlaceType.Url, "https://wiki.example.com");
        Add(service, "Mail", PlaceType.Url, "https://mail.example.com");
        RemoveForUndo(service, wiki);
        var exportFile = Path.Combine(_temp.Path, "export.json");

        Assert.Null(service.Export(service.Places.Concat(service.RecentlyDeleted), exportFile));

        var written = JsonNode.Parse(File.ReadAllText(exportFile))!;
        Assert.Equal(2, written["schemaVersion"]!.GetValue<int>());
        var places = written["places"]!.AsArray();
        Assert.Equal(new[] { "Docs", "Mail" }, places.Select(p => p!["alias"]!.GetValue<string>()));
        Assert.All(places, p => Assert.False(p!.AsObject().ContainsKey("deletedAt")));
    }

    [Fact]
    public void Export_replaces_an_existing_file_and_leaves_no_temp_file()
    {
        var service = NewService();
        var docs = Add(service, "Docs", PlaceType.Folder, Folder("Docs"));
        var exportFile = Path.Combine(_temp.Path, "export.json");
        File.WriteAllText(exportFile, "an older, longer export that must not leave a tail behind");

        Assert.Null(service.Export(new[] { docs }, exportFile));

        Assert.False(File.Exists(exportFile + ".tmp"));
        var (candidates, error) = NewServiceAt("other.json").GetImportCandidates(exportFile);
        Assert.Null(error);
        Assert.Equal("Docs", Assert.Single(candidates).Alias);
    }

    [Fact]
    public void Failed_export_returns_an_error_and_cleans_up_its_temp_file()
    {
        var service = NewService();
        var docs = Add(service, "Docs", PlaceType.Folder, Folder("Docs"));
        var exportFile = Path.Combine(_temp.Path, "export.json");
        Directory.CreateDirectory(exportFile);

        var error = service.Export(new[] { docs }, exportFile);

        Assert.NotNull(error);
        Assert.False(File.Exists(exportFile + ".tmp"));
    }

    [Fact]
    public void Import_candidates_exclude_collisions_with_existing_store()
    {
        var exportFile = Path.Combine(_temp.Path, "export.json");
        var source = NewServiceAt("source.json");
        Add(source, "Docs", PlaceType.Folder, Folder("Docs"));          // alias collides
        Add(source, "Other Wiki", PlaceType.Url, "https://WIKI.example.com"); // resource collides
        Add(source, "New", PlaceType.Url, "https://new.example.com");   // clean
        source.Export(source.Places, exportFile);

        var target = NewService();
        Add(target, "docs", PlaceType.Folder, Folder("Elsewhere"));
        Add(target, "Wiki", PlaceType.Url, "https://wiki.example.com");

        var (candidates, error) = target.GetImportCandidates(exportFile);

        Assert.Null(error);
        Assert.Equal("New", Assert.Single(candidates).Alias);
    }

    [Fact]
    public void Import_candidates_exclude_duplicates_within_the_file()
    {
        var exportFile = Path.Combine(_temp.Path, "export.json");
        File.WriteAllText(exportFile, """
            { "schemaVersion": 1, "places": [
                { "alias": "Wiki",  "type": "url", "resource": "https://wiki.example.com" },
                { "alias": "WIKI",  "type": "url", "resource": "https://other.example.com" },
                { "alias": "Wiki2", "type": "url", "resource": "https://Wiki.example.com" },
                { "alias": "Fine",  "type": "url", "resource": "https://fine.example.com" }
            ] }
            """);

        var (candidates, error) = NewService().GetImportCandidates(exportFile);

        Assert.Null(error);
        Assert.Equal(new[] { "Wiki", "Fine" }, candidates.Select(p => p.Alias));
    }

    /// <summary>JSON string content for a path, with backslashes escaped.</summary>
    private static string Json(string path) => path.Replace("\\", "\\\\");

    /// <summary>
    /// Phase 2 test 34: an export written before Phase 2 — places.v1.json's
    /// exact shape, offset-less dates — still offers every place, with its
    /// dates migrated to UTC by the same rule as the store (D17; the clock's
    /// zone is PlusTen), and the candidates commit.
    /// </summary>
    [Fact]
    public void A_v1_export_offers_every_place_with_utc_dates_and_they_commit()
    {
        var exportFile = Path.Combine(_temp.Path, "export.json");
        File.WriteAllText(exportFile, $$"""
            {
              "schemaVersion": 1,
              "places": [
                { "alias": "Downloads", "type": "folder", "resource": "{{Json(Folder("Downloads"))}}", "isFavourite": true, "favouriteOrder": 0, "dateAdded": "2026-01-15T09:30:00" },
                { "alias": "QuickerPlaces Repo", "type": "url", "resource": "https://github.com/example/quickerplaces", "isFavourite": true, "favouriteOrder": 1, "dateAdded": "2026-02-03T14:05:22" },
                { "alias": "Old Reports", "type": "folder", "resource": "{{Json(Folder("Old Reports"))}}", "isFavourite": false, "favouriteOrder": null, "dateAdded": "2025-11-20T08:12:47" }
              ]
            }
            """);
        var service = NewService();

        var (candidates, error) = service.GetImportCandidates(exportFile);

        Assert.Null(error);
        Assert.Equal(new[] { "Downloads", "QuickerPlaces Repo", "Old Reports" }, candidates.Select(p => p.Alias));
        Assert.Equal(
            new[]
            {
                new DateTimeOffset(2026, 1, 14, 23, 30, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 2, 3, 4, 5, 22, TimeSpan.Zero),
                new DateTimeOffset(2025, 11, 19, 22, 12, 47, TimeSpan.Zero)
            },
            candidates.Select(p => p.DateAdded));
        Assert.All(candidates, p => Assert.Equal(TimeSpan.Zero, p.DateAdded.Offset));

        var (imported, persistence) = service.CommitImport(candidates);
        Assert.Equal(3, imported.Count);
        Assert.True(persistence.Saved);
        Assert.Equal(3, NewService().Places.Count);
    }

    /// <summary>
    /// Phase 2 test 35: records carrying deletedAt in an import file are not
    /// offered (D17). A v2 document shaped like places.v2.json: only its
    /// three active records are candidates — including "reports", whose
    /// deleted namesake "Reports" is simply skipped.
    /// </summary>
    [Fact]
    public void Records_with_deletedAt_in_an_import_file_are_not_offered()
    {
        var exportFile = Path.Combine(_temp.Path, "export.json");
        File.WriteAllText(exportFile, $$"""
            {
              "schemaVersion": 2,
              "places": [
                { "alias": "Downloads", "type": "folder", "resource": "{{Json(Folder("Downloads"))}}", "isFavourite": true, "favouriteOrder": 0, "dateAdded": "2026-01-14T23:30:00+00:00" },
                { "alias": "Old Wiki", "type": "url", "resource": "https://wiki.example.com/old", "isFavourite": true, "favouriteOrder": 1, "dateAdded": "2025-06-02T10:15:00+00:00", "deletedAt": "2026-09-20T08:00:00+00:00" },
                { "alias": "QuickerPlaces Repo", "type": "url", "resource": "https://github.com/example/quickerplaces", "isFavourite": true, "favouriteOrder": 1, "dateAdded": "2026-02-03T04:05:22+00:00" },
                { "alias": "Reports", "type": "folder", "resource": "{{Json(Folder("Archive Reports"))}}", "isFavourite": false, "favouriteOrder": null, "dateAdded": "2025-11-19T22:12:47+00:00", "deletedAt": "2026-09-24T17:45:00+00:00" },
                { "alias": "reports", "type": "folder", "resource": "{{Json(Folder("Reports"))}}", "isFavourite": false, "favouriteOrder": null, "dateAdded": "2026-09-24T18:02:10+00:00" }
              ]
            }
            """);

        var (candidates, error) = NewService().GetImportCandidates(exportFile);

        Assert.Null(error);
        Assert.Equal(new[] { "Downloads", "QuickerPlaces Repo", "reports" }, candidates.Select(p => p.Alias));
        Assert.All(candidates, p => Assert.Null(p.DeletedAt));
    }

    /// <summary>
    /// Phase 2 test 36: a candidate that collides only with a place in
    /// Recently Deleted is offered and commits (§4.10, D17) — the deleted
    /// one's later restore is what becomes a conflict.
    /// </summary>
    [Fact]
    public void A_candidate_colliding_only_with_a_deleted_place_is_offered_and_commits()
    {
        var service = NewService();
        var oldDocs = Add(service, "Docs", PlaceType.Folder, Folder("Docs"));
        RemoveForUndo(service, oldDocs);
        var exportFile = Path.Combine(_temp.Path, "export.json");
        File.WriteAllText(exportFile, $$"""
            { "schemaVersion": 2, "places": [ { "alias": "Docs", "type": "folder", "resource": "{{Json(Folder("Docs"))}}" } ] }
            """);

        var (candidates, error) = service.GetImportCandidates(exportFile);
        Assert.Null(error);
        var (imported, _) = service.CommitImport(candidates);

        var docs = Assert.Single(imported);
        Assert.Equal("Docs", docs.Alias);
        Assert.NotSame(oldDocs, docs);
        Assert.Contains(docs, service.Places);
        Assert.Same(oldDocs, Assert.Single(service.RecentlyDeleted));
        Assert.False(service.TryRestore(oldDocs, out _).Success);
    }

    /// <summary>Phase 2 test 37: an import file from a newer version is refused with the newer-version message, and offers nothing (D17).</summary>
    [Fact]
    public void An_import_file_from_a_newer_version_is_refused()
    {
        var exportFile = Path.Combine(_temp.Path, "export.json");
        File.WriteAllText(exportFile, """
            { "schemaVersion": 3, "places": [ { "alias": "Wiki", "type": "url", "resource": "https://wiki.example.com" } ] }
            """);

        var (candidates, error) = NewService().GetImportCandidates(exportFile);

        Assert.Empty(candidates);
        Assert.Equal("That file was exported by a newer version of QuickerPlaces. Update QuickerPlaces to import it.", error);
    }

    /// <summary>Phase 2 test 38: an import file without schemaVersion is still read, as version 1 (D17's unchanged leniency).</summary>
    [Fact]
    public void An_import_file_without_a_version_still_imports()
    {
        var exportFile = Path.Combine(_temp.Path, "export.json");
        File.WriteAllText(exportFile, """
            { "places": [ { "alias": "Wiki", "type": "url", "resource": "https://wiki.example.com", "dateAdded": "2026-01-15T09:30:00" } ] }
            """);
        var service = NewService();

        var (candidates, error) = service.GetImportCandidates(exportFile);

        Assert.Null(error);
        var wiki = Assert.Single(candidates);
        Assert.Equal(new DateTimeOffset(2026, 1, 14, 23, 30, 0, TimeSpan.Zero), wiki.DateAdded);
        Assert.Single(service.CommitImport(candidates).imported);
    }

    /// <summary>
    /// Not a numbered plan test (D17's table): a schemaVersion that is not a
    /// number is refused as not a QuickerPlaces export. So is one below 1,
    /// which is numeric but was never written by any build — the store's
    /// gate treats it the same way (plan 5.1).
    /// </summary>
    [Theory]
    [InlineData("\"2\"")]
    [InlineData("true")]
    [InlineData("1.5")]
    [InlineData("0")]
    [InlineData("-1")]
    public void An_import_file_with_an_unusable_version_is_refused(string version)
    {
        var exportFile = Path.Combine(_temp.Path, "export.json");
        File.WriteAllText(exportFile, $$"""
            { "schemaVersion": {{version}}, "places": [ { "alias": "Wiki", "type": "url", "resource": "https://wiki.example.com" } ] }
            """);

        var (candidates, error) = NewService().GetImportCandidates(exportFile);

        Assert.Empty(candidates);
        Assert.Equal("That file isn't a QuickerPlaces export.", error);
    }

    [Fact]
    public void Import_of_unreadable_file_returns_error_not_exception()
    {
        var bad = Path.Combine(_temp.Path, "bad.json");
        File.WriteAllText(bad, "not json");

        var (candidates, error) = NewService().GetImportCandidates(bad);
        Assert.Empty(candidates);
        Assert.NotNull(error);

        var (missingCandidates, missingError) = NewService().GetImportCandidates(Path.Combine(_temp.Path, "missing.json"));
        Assert.Empty(missingCandidates);
        Assert.NotNull(missingError);
    }

    [Fact]
    public void Commit_import_revalidates_against_current_store()
    {
        var service = NewService();
        var candidate = new Place { Alias = "Wiki", Type = PlaceType.Url, Resource = "https://wiki.example.com" };

        // Something with the same alias was added after the preview was shown.
        Add(service, "wiki", PlaceType.Url, "https://elsewhere.example.com");

        Assert.Empty(service.CommitImport(new[] { candidate }).imported);
        Assert.Single(service.Places);
    }
}
