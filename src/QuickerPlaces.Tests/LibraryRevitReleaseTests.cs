using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;
using QuickerPlaces.Services.Library;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Revit;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The Library's Type column names a Revit file's release ("Revit 2025"),
/// read in the background through <see cref="RevitReleaseCache"/> so the list
/// never waits on the disk.
/// </summary>
public sealed class LibraryRevitReleaseTests
{
    private const string Model = @"C:\Jobs\Tower\Tower_A.rvt";
    private const string OldModel = @"C:\Jobs\Tower\Old.rvt";
    private const string Pdf = @"C:\Jobs\Tower\A-101.pdf";

    private readonly ManualTimeProvider _time = new();
    private readonly FakeShell _shell = new();
    private readonly PlacesService _places;
    private readonly SessionStore _sessions;
    private readonly List<string> _reads = new();

    public LibraryRevitReleaseTests()
    {
        _places = new PlacesService(new FakePlacesStorage(), _time);
        _sessions = new SessionStore(new FakePlacesStorage(), _time);
        Assert.True(_sessions.TryCreate("Tower", Array.Empty<string>(), new[] { Model, OldModel, Pdf }, out _, out _).Success);
    }

    private RevitReleaseCache NewCache() => new(
        path =>
        {
            _reads.Add(path);
            return string.Equals(path, OldModel, StringComparison.OrdinalIgnoreCase)
                ? new RevitFileInfo(2021, "b", RevitWorksharing.NotWorkshared, null, null, 14, RevitFileProblem.TooOld)
                : new RevitFileInfo(2025, "b", RevitWorksharing.Local, @"\\server\Tower_A.rvt", Model, 14, RevitFileProblem.None);
        },
        _ => new FileStamp(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), 1000),
        _time);

    private LibraryViewModel NewViewModel(RevitReleaseCache? cache, IBackgroundWork? work = null) =>
        new(_places, _sessions, new ActivityStore(new FakePlacesStorage(), _time), new RecentFilesStore(new FakePlacesStorage(), _time),
            new PlaceLauncher(_places, _shell), _shell, _time, CultureInfo.InvariantCulture, work, cache);

    private static LibraryRowViewModel Row(LibraryViewModel vm, string path) =>
        vm.Rows.Single(r => string.Equals(r.Location, path, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void RevitRows_NameTheirRelease()
    {
        var vm = NewViewModel(NewCache());

        Assert.Equal("Revit 2025", Row(vm, Model).KindLabel);
        Assert.Equal(@"Saved in Revit 2025. Local copy of \\server\Tower_A.rvt", Row(vm, Model).KindToolTip);
        Assert.Equal("Revit 2021 (old)", Row(vm, OldModel).KindLabel);
    }

    [Fact]
    public void OtherRows_AreUntouched_AndNeverRead()
    {
        var vm = NewViewModel(NewCache());

        Assert.Equal("PDF", Row(vm, Pdf).KindLabel);
        Assert.Equal("", Row(vm, Pdf).KindToolTip);
        Assert.DoesNotContain(Pdf, _reads);
    }

    [Fact]
    public void WithoutACache_RevitRowsSayRevit()
    {
        var vm = NewViewModel(null);

        Assert.Equal("Revit", Row(vm, Model).KindLabel);
    }

    [Fact]
    public void TheListShowsAtOnce_AndTheReleaseArrivesLater()
    {
        var work = new QueuedWork();
        var vm = NewViewModel(NewCache(), work);
        work.RunAll(); // the Library's own query
        var row = Row(vm, Model);
        Assert.Equal("Revit", row.KindLabel);

        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        work.RunAll(); // the Revit lookup

        Assert.Equal("Revit 2025", row.KindLabel);
        Assert.Contains(nameof(LibraryRowViewModel.KindLabel), changed);
        Assert.Contains(nameof(LibraryRowViewModel.KindToolTip), changed);
    }

    [Fact]
    public void AnAnswerAlreadyKnown_IsShownWithoutWaiting()
    {
        var cache = NewCache();
        NewViewModel(cache);

        var work = new QueuedWork();
        var vm = NewViewModel(cache, work);
        work.RunAll(); // the query only; the lookup is still queued

        Assert.Equal("Revit 2025", Row(vm, Model).KindLabel);
    }

    [Fact]
    public void Refreshing_DoesNotReadAgain()
    {
        var vm = NewViewModel(NewCache());
        _reads.Clear();

        vm.SearchText = "Tower";
        vm.SearchText = "";

        Assert.Empty(_reads);
        Assert.Equal("Revit 2025", Row(vm, Model).KindLabel);
    }

    /// <summary>Holds background work until the test runs it.</summary>
    private sealed class QueuedWork : IBackgroundWork
    {
        private readonly List<Action> _queue = new();

        public void Run<T>(Func<T> work, Action<T> apply) => _queue.Add(() => apply(work()));

        public void RunAll()
        {
            var items = _queue.ToList();
            _queue.Clear();
            items.ForEach(a => a());
        }
    }
}
