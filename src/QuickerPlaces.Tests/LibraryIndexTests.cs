using System;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Library;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>Merging the four sources into Library items (documents plan §6).</summary>
public sealed class LibraryIndexTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);

    private static Place Folder(string alias, string path, DateTimeOffset? lastOpened = null)
        => new() { Alias = alias, Type = PlaceType.Folder, Resource = path, LastOpenedAt = lastOpened };

    private static Place Link(string alias, string url) => new() { Alias = alias, Type = PlaceType.Url, Resource = url };

    private static SessionSnapshot Session(string name, string[] tags, params string[] files)
        => new(Guid.NewGuid().ToString("N"), name, tags, files, T0, T0, null, Array.Empty<DateTimeOffset>());

    [Fact]
    public void EachSourceGivesItsKind()
    {
        var items = LibraryIndex.Build(
            new[] { Folder("Jobs", @"C:\Jobs"), Link("Wiki", "https://wiki.example.com") },
            new[] { Session("Tower B", new[] { "Tower B" }, @"C:\Jobs\A.pdf", @"C:\Jobs\Report.docx", @"C:\Jobs\Budget.xlsx") },
            Array.Empty<FolderActivity>(),
            Array.Empty<RecentFileSummary>());

        Assert.Equal(
            new[] { (LibraryKind.Folder, "Jobs"), (LibraryKind.Link, "Wiki"), (LibraryKind.Pdf, "A.pdf"), (LibraryKind.Word, "Report.docx"), (LibraryKind.Excel, "Budget.xlsx") },
            items.OrderBy(i => i.Kind).Select(i => (i.Kind, i.Name)));
        Assert.All(items, i => Assert.True(i.IsSaved));
        Assert.All(items, i => Assert.False(i.IsRecent));
    }

    [Fact]
    public void ASavedFolderVisitedInRecents_IsOneRow_WithItsAlias_AndBothSources()
    {
        var items = LibraryIndex.Build(
            new[] { Folder("Acme job", @"C:\Jobs\Acme", T0) },
            Array.Empty<SessionSnapshot>(),
            new[] { new FolderActivity(@"c:/jobs/acme/", TimeSpan.FromMinutes(5), 3, T0.AddHours(2)) },
            Array.Empty<RecentFileSummary>());

        var item = Assert.Single(items);
        Assert.Equal("Acme job", item.Name);
        Assert.Equal(@"C:\Jobs\Acme", item.Location);
        Assert.Equal(3, item.RecentCount);
        Assert.Equal(T0.AddHours(2), item.LastUsedAt);
        Assert.Equal("Saved place · Visited 3 times", item.SourceText);
    }

    [Fact]
    public void AFileInTwoSessionsAndRecentFiles_IsOneRow_WithBothSessionsTags()
    {
        var items = LibraryIndex.Build(
            Array.Empty<Place>(),
            new[]
            {
                Session("Tower B", new[] { "Tower B", "markups" }, @"C:\Jobs\A.pdf"),
                Session("Review", new[] { "markups", "RFI" }, @"C:\JOBS\a.pdf"),
            },
            Array.Empty<FolderActivity>(),
            new[] { new RecentFileSummary(@"C:\Jobs\A.pdf", DocumentKind.Pdf, 1, T0.AddDays(1)) });

        var item = Assert.Single(items);
        Assert.Equal(new[] { "Tower B", "Review" }, item.Sessions);
        Assert.Equal(new[] { "Tower B", "markups", "RFI" }, item.Tags);
        Assert.True(item.IsSaved);
        Assert.True(item.IsRecent);
        Assert.Equal("In Tower B, Review · Opened once", item.SourceText);
        Assert.Equal(@"C:\Jobs", item.Folder);
    }

    [Fact]
    public void Items_AreMostRecentlyUsedFirst_NeverUsedLast()
    {
        var items = LibraryIndex.Build(
            new[] { Folder("Never", @"C:\Never"), Folder("Old", @"C:\Old", T0), Folder("New", @"C:\New", T0.AddDays(1)) },
            Array.Empty<SessionSnapshot>(), Array.Empty<FolderActivity>(), Array.Empty<RecentFileSummary>());

        Assert.Equal(new[] { "New", "Old", "Never" }, items.Select(i => i.Name));
    }

    [Theory]
    [InlineData("acme", true)]
    [InlineData("markups", true)]
    [InlineData("Tower", true)]
    [InlineData("jobs acme", true)]
    [InlineData("budget", false)]
    public void Matches_SearchesNameLocationTagsAndSessions(string search, bool expected)
    {
        var item = new LibraryItem(LibraryKind.Pdf, "Acme.pdf", @"C:\Jobs\Acme.pdf", null, new[] { "Tower B" }, new[] { "markups" }, 0, null);

        Assert.Equal(expected, LibraryIndex.Matches(item, search));
    }
}
