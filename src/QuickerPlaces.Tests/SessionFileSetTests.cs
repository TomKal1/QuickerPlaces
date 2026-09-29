using System;
using System.Linq;
using QuickerPlaces.Services.Library;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>Saving the File shelf's documents as a session (configurable canvas plan M3).</summary>
public sealed class SessionFileSetTests
{
    private const string Pdf = @"C:\Jobs\Acme\A-101.pdf";
    private const string Word = @"C:\Jobs\Acme\Report.docx";

    private static LibraryItem Item(LibraryKind kind, string location)
        => new(kind, location, location, null, Array.Empty<string>(), Array.Empty<string>(), 0, null);

    [Fact]
    public void OnlyDocuments_AreKept_InShelfOrder_AndFoldersAndLinksAreCounted()
    {
        var set = SessionFileSet.From(new[]
        {
            Item(LibraryKind.Word, Word),
            Item(LibraryKind.Folder, @"C:\Jobs\Acme"),
            Item(LibraryKind.Pdf, Pdf),
            Item(LibraryKind.Link, "https://wiki.example.com"),
            Item(LibraryKind.Folder, @"C:\Jobs"),
        });

        Assert.Equal(new[] { Word, Pdf }, set.Files);
        Assert.Equal(2, set.FoldersLeftOut);
        Assert.Equal(1, set.LinksLeftOut);
        Assert.Contains("Left out: 2 folders and 1 link.", set.Summary);
    }

    [Fact]
    public void TheSameFile_SpelledTwoWays_IsListedOnce()
    {
        var set = SessionFileSet.From(new[]
        {
            Item(LibraryKind.Pdf, Pdf),
            Item(LibraryKind.Pdf, @"c:/jobs/acme/A-101.PDF"),
        });

        Assert.Single(set.Files);
    }

    [Fact]
    public void NoDocuments_IsAnEmptySet()
    {
        var set = SessionFileSet.From(new[] { Item(LibraryKind.Folder, @"C:\Jobs") });

        Assert.True(set.IsEmpty);
        Assert.Equal(1, set.FoldersLeftOut);
    }

    [Fact]
    public void TheEditor_StartsWithEveryFileTicked_AndDoesNotScan()
    {
        var store = new SessionStore(new FakePlacesStorage(), new ManualTimeProvider());
        var set = SessionFileSet.From(new[] { Item(LibraryKind.Pdf, Pdf), Item(LibraryKind.Word, Word) });

        var editor = SessionEditorViewModel.ForFileSet(store, set);

        Assert.True(editor.IsNew);
        Assert.False(editor.ScansOnOpen);
        Assert.Equal("Save Files as Session", editor.Title);
        Assert.Equal(new[] { Pdf, Word }, editor.Files.Select(f => f.Path));
        Assert.All(editor.Files, f => Assert.True(f.IsIncluded));
        Assert.Equal(set.Summary, editor.ScanSummary);
    }

    [Fact]
    public void TheEditor_SavesTheTickedFiles_AsANewSession()
    {
        var store = new SessionStore(new FakePlacesStorage(), new ManualTimeProvider());
        var editor = SessionEditorViewModel.ForFileSet(store,
            SessionFileSet.From(new[] { Item(LibraryKind.Pdf, Pdf), Item(LibraryKind.Word, Word) }));
        editor.Name = "Acme review";
        editor.Files[1].IsIncluded = false;

        Assert.True(editor.Save());

        var saved = Assert.Single(store.Sessions);
        Assert.Equal(new[] { Pdf }, saved.Files);
    }

    [Fact]
    public void ANewSessionOfOpenFiles_StillScansOnOpen()
    {
        var store = new SessionStore(new FakePlacesStorage(), new ManualTimeProvider());

        Assert.True(new SessionEditorViewModel(store, null).ScansOnOpen);
        Assert.Equal("Save Open Files", new SessionEditorViewModel(store, null).Title);
    }
}
