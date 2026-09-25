using System;
using System.IO;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using Xunit;

namespace QuickerPlaces.Tests;

public sealed class PlacesServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string PlacesFile => _temp.File("places.json");

    private PlacesService NewService() => new(PlacesFile);

    /// <summary>An absolute folder path that's fully qualified on whichever OS the tests run on.</summary>
    private string Folder(string name) => Path.Combine(_temp.Path, name);

    private static Place Add(PlacesService service, string alias, PlaceType type, string resource)
    {
        var result = service.TryAdd(alias, type, resource, out var created);
        Assert.True(result.Success, result.ErrorMessage);
        return created!;
    }

    // -----------------------------------------------------------------
    // Loading
    // -----------------------------------------------------------------

    [Fact]
    public void Missing_file_starts_empty_without_failure()
    {
        var service = NewService();

        Assert.Empty(service.Places);
        Assert.False(service.LoadFailed);
        Assert.False(File.Exists(PlacesFile));
    }

    [Fact]
    public void Corrupt_file_starts_empty_flags_failure_and_is_left_untouched()
    {
        File.WriteAllText(PlacesFile, "{ this is not json");

        var service = NewService();

        Assert.Empty(service.Places);
        Assert.True(service.LoadFailed);
        Assert.Equal("{ this is not json", File.ReadAllText(PlacesFile));
    }

    [Fact]
    public void Null_entries_in_file_are_dropped_on_load()
    {
        var resource = Folder("Docs").Replace("\\", "\\\\");
        File.WriteAllText(PlacesFile, $$"""
            { "schemaVersion": 1, "places": [ null, { "alias": "Docs", "type": "folder", "resource": "{{resource}}" } ] }
            """);

        var service = NewService();

        Assert.False(service.LoadFailed);
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

        var result = service.TryAdd("wiki", PlaceType.Url, "https://other.example.com", out var created);

        Assert.False(result.Success);
        Assert.Null(created);
        Assert.Single(NewService().Places);
    }

    [Fact]
    public void Rename_and_edit_persist()
    {
        var service = NewService();
        var place = Add(service, "Docs", PlaceType.Folder, Folder("Docs"));

        Assert.True(service.TryRenameAlias(place, "Documents").Success);
        Assert.True(service.TryEditResource(place, Folder("Documents")).Success);

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

        Assert.False(service.TryRenameAlias(wiki, "DOCS").Success);
        Assert.Equal("Wiki", wiki.Alias);
    }

    [Fact]
    public void Remove_persists()
    {
        var service = NewService();
        var place = Add(service, "Docs", PlaceType.Folder, Folder("Docs"));

        service.Remove(place);

        Assert.Empty(NewService().Places);
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

        service.Remove(c);
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

        var exportFile = _temp.File("export.json");
        Assert.Null(source.Export(source.Places, exportFile));

        var target = new PlacesService(_temp.File("other.json"));
        var (candidates, error) = target.GetImportCandidates(exportFile);
        Assert.Null(error);
        Assert.Equal(2, candidates.Count);

        var imported = target.CommitImport(candidates);
        Assert.Equal(2, imported.Count);
        // Imported items arrive as fresh, non-favourite records.
        Assert.All(target.Places, p => Assert.False(p.IsFavourite));
        Assert.Equal(2, new PlacesService(_temp.File("other.json")).Places.Count);
    }

    [Fact]
    public void Import_candidates_exclude_collisions_with_existing_store()
    {
        var exportFile = _temp.File("export.json");
        var source = new PlacesService(_temp.File("source.json"));
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
        var exportFile = _temp.File("export.json");
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
        var bad = _temp.File("bad.json");
        File.WriteAllText(bad, "not json");

        var (candidates, error) = NewService().GetImportCandidates(bad);
        Assert.Empty(candidates);
        Assert.NotNull(error);

        var (missingCandidates, missingError) = NewService().GetImportCandidates(_temp.File("missing.json"));
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

        Assert.Empty(service.CommitImport(new[] { candidate }));
        Assert.Single(service.Places);
    }
}
