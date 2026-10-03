using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Models.RecentFiles;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Library;
using QuickerPlaces.Services.RecentFiles;
using QuickerPlaces.Services.Sessions;
using Xunit;

namespace QuickerPlaces.Tests;

public sealed class LibraryQueryAllocationTests
{
    [Theory]
    [InlineData(false, 2, 3)]
    [InlineData(true, 3, 2)]
    public void HistoryIsIndexedOnceForAllTime_ButSeparatelyForAChosenPeriod(bool restricted, int traversals, int opens)
    {
        var today = new DateOnly(2026, 9, 25);
        var at = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        // Unsorted history: the most recent open must not be inferred from array position.
        var history = new CountedList<RecentFileHistory>(new[]
        {
            new RecentFileHistory(@"C:\Jobs\Report.pdf", DocumentKind.Pdf,
                new[] { at, at.AddDays(-1), at.AddHours(-2) }),
            new RecentFileHistory(@"C:\Jobs\Empty.pdf", DocumentKind.Pdf, Array.Empty<DateTimeOffset>()),
        });
        var data = new LibrarySnapshot(Array.Empty<Place>(), Array.Empty<SessionSnapshot>(), true, null,
            Array.Empty<RecentsRootData>(), today.AddDays(-61), true, null,
            new RecentFilesSettingsSnapshot(true, DocumentKinds.All.ToArray(), RecentFilesScope.Everywhere, at.AddDays(-10), null),
            history, today, TimeZoneInfo.Utc);

        var result = LibraryQueryEngine.Run(data, LibraryFilter.None, restricted ? (today, today) : null,
            CultureInfo.InvariantCulture);

        var item = Assert.Single(result.Items);
        Assert.Equal("Report.pdf", item.Name);
        Assert.Equal(opens, item.RecentCount);
        Assert.Equal(at, item.LastUsedAt);
        Assert.Equal(2, result.Heat[today].FileOpens);
        Assert.Equal(1, result.Heat[today.AddDays(-1)].FileOpens);
        Assert.Equal(traversals, history.Enumerations);
    }

    private sealed class CountedList<T>(IReadOnlyList<T> items) : IReadOnlyList<T>
    {
        public int Enumerations { get; private set; }
        public int Count => items.Count;
        public T this[int index] => items[index];
        public IEnumerator<T> GetEnumerator()
        {
            Enumerations++;
            return items.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
