using System;
using System.Collections.Generic;
using QuickerPlaces.Services.Revit.AddIns;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The Revit settings dialog's signature check runs once per DLL per app run,
/// and again only when the DLL changes or cannot be stamped.
/// </summary>
public sealed class AddinPublisherCacheTests
{
    private const string Dll = @"C:\ProgramData\Autodesk\Revit\Addins\2025\Tool.dll";

    private readonly List<string> _checked = [];
    private readonly Dictionary<string, (DateTime, long)?> _stamps = new(StringComparer.OrdinalIgnoreCase)
    {
        [Dll] = (new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), 1000),
    };

    private AddinPublisherCache NewCache() => new(path => { _checked.Add(path); return true; },
        path => _stamps.TryGetValue(path, out var stamp) ? stamp : null);

    [Fact]
    public void AnUnchangedDll_IsCheckedOnce()
    {
        var cache = NewCache();

        Assert.True(cache.IsAutodeskSigned(Dll));
        Assert.True(cache.IsAutodeskSigned(Dll.ToUpperInvariant()));

        Assert.Single(_checked);
    }

    [Fact]
    public void AChangedDll_IsCheckedAgain()
    {
        var cache = NewCache();
        cache.IsAutodeskSigned(Dll);

        _stamps[Dll] = (new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), 1000);
        cache.IsAutodeskSigned(Dll);
        _stamps[Dll] = (new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), 2000);
        cache.IsAutodeskSigned(Dll);

        Assert.Equal(3, _checked.Count);
    }

    [Fact]
    public void ADllThatCannotBeStamped_IsCheckedEveryTime()
    {
        var cache = NewCache();
        const string missing = @"C:\Missing\Gone.dll";

        cache.IsAutodeskSigned(missing);
        cache.IsAutodeskSigned(missing);

        Assert.Equal(2, _checked.Count);
    }
}
