using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    private PlacesService NewServiceAt(string fileName) => new(new FilePlacesStorage(_temp.Path, fileName));

    /// <summary>An absolute folder path that's fully qualified on whichever OS the tests run on.</summary>
    private string Folder(string name) => Path.Combine(_temp.Path, name);

    private static Place Add(PlacesService service, string alias, PlaceType type, string resource)
    {
        var result = service.TryAdd(alias, type, resource, out var created, out var persistence);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(persistence.Saved, persistence.UserMessage);
        return created!;
    }

    /// <summary>Removes <paramref name="place"/>, asserting it saved, and returns the undo record.</summary>
    private static RemovedPlace RemoveForUndo(PlacesService service, Place place)
    {
        var persistence = service.Remove(place, out var removed);
        Assert.True(persistence.Saved, persistence.UserMessage);
        Assert.NotNull(removed);
        return removed!;
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

    [Fact]
    public void Restore_puts_a_removed_place_back_where_it_was_and_persists()
    {
        var service = NewService();
        Add(service, "A", PlaceType.Folder, Folder("A"));
        var b = Add(service, "B", PlaceType.Folder, Folder("B"));
        Add(service, "C", PlaceType.Folder, Folder("C"));
        var dateAdded = b.DateAdded;

        var removed = RemoveForUndo(service, b);
        Assert.NotNull(removed);
        Assert.Equal(1, removed!.Index);

        Assert.True(service.TryRestore(removed, out _).Success);

        Assert.Equal(new[] { "A", "B", "C" }, service.Places.Select(p => p.Alias));
        Assert.Same(b, service.Places[1]);
        Assert.Equal(dateAdded, b.DateAdded);
        Assert.Equal(new[] { "A", "B", "C" }, NewService().Places.Select(p => p.Alias));
    }

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
        Assert.Equal(1, removed.FavouriteOrder);
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

    [Fact]
    public void Restore_refuses_when_the_alias_or_resource_has_been_reused()
    {
        var service = NewService();
        var docs = Add(service, "Docs", PlaceType.Folder, Folder("Docs"));
        var wiki = Add(service, "Wiki", PlaceType.Url, "https://wiki.example.com");

        var removedDocs = RemoveForUndo(service, docs);
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
    }

    [Fact]
    public void Restore_twice_is_refused_rather_than_duplicating()
    {
        var service = NewService();
        var docs = Add(service, "Docs", PlaceType.Folder, Folder("Docs"));
        var removed = RemoveForUndo(service, docs);

        Assert.True(service.TryRestore(removed, out _).Success);
        Assert.False(service.TryRestore(removed, out _).Success);
        Assert.Single(service.Places);
    }

    [Fact]
    public void Removing_a_place_not_in_the_store_returns_null()
    {
        var service = NewService();
        var docs = Add(service, "Docs", PlaceType.Folder, Folder("Docs"));
        RemoveForUndo(service, docs);

        service.Remove(docs, out var removedAgain);
        Assert.Null(removedAgain);
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
