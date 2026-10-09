using System;
using System.Collections.Generic;
using System.Threading;
using QuickerPlaces.Services.Revit;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The cache behind the Type column's "Revit 2025": reads a file once per
/// version, trusts an answer for a while, and never lets a stalled share hold
/// up the caller past its timeout.
/// </summary>
public sealed class RevitReleaseCacheTests
{
    private const string Model = @"\\server\projects\Tower.rvt";
    private static readonly FileStamp Monday = new(new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc), 8_000_000);
    private static readonly FileStamp Tuesday = new(new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc), 8_100_000);

    private readonly ManualTimeProvider _time = new();
    private readonly Dictionary<string, FileStamp?> _stamps = new(StringComparer.OrdinalIgnoreCase) { [Model] = Monday };
    private int _reads;
    private int _stats;
    private int _release = 2025;

    private RevitReleaseCache NewCache(Func<string, RevitFileInfo>? read = null, TimeSpan? timeout = null) => new(
        read ?? (_ => { Interlocked.Increment(ref _reads); return Info(_release); }),
        path => { Interlocked.Increment(ref _stats); return _stamps.TryGetValue(path, out var s) ? s : null; },
        _time, timeout ?? TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

    private static RevitFileInfo Info(int release) =>
        new(release, "build", RevitWorksharing.Central, Model, Model, 14, RevitFileProblem.None);

    [Fact]
    public void NothingIsKnown_UntilARefresh()
    {
        var cache = NewCache();

        Assert.Null(cache.Peek(Model));
        Assert.Equal(0, _stats);
    }

    [Fact]
    public void Refresh_ReadsTheFile_AndPeekRemembersIt()
    {
        var cache = NewCache();

        var answers = cache.Refresh(new[] { Model });

        Assert.Equal(2025, answers[Model].Release);
        Assert.Equal(2025, cache.Peek(Model)!.Release);
        Assert.Equal(2025, cache.Peek(Model.ToUpperInvariant())!.Release);
        Assert.Equal(1, _reads);
    }

    [Fact]
    public void ARecentAnswer_IsTrusted_WithoutTouchingTheDisk()
    {
        var cache = NewCache();
        cache.Refresh(new[] { Model });

        _time.Advance(TimeSpan.FromSeconds(29));
        cache.Refresh(new[] { Model, Model });

        Assert.Equal(1, _stats);
        Assert.Equal(1, _reads);
    }

    [Fact]
    public void AnUnchangedFile_IsStattedButNotReadAgain()
    {
        var cache = NewCache();
        cache.Refresh(new[] { Model });

        _time.Advance(TimeSpan.FromMinutes(1));
        _release = 2026;
        var answers = cache.Refresh(new[] { Model });

        Assert.Equal(2, _stats);
        Assert.Equal(1, _reads);
        Assert.Equal(2025, answers[Model].Release);
    }

    [Fact]
    public void AChangedFile_IsReadAgain()
    {
        var cache = NewCache();
        cache.Refresh(new[] { Model });

        _time.Advance(TimeSpan.FromMinutes(1));
        _stamps[Model] = Tuesday;
        _release = 2026;
        var answers = cache.Refresh(new[] { Model });

        Assert.Equal(2, _reads);
        Assert.Equal(2026, answers[Model].Release);
        Assert.Equal(2026, cache.Peek(Model)!.Release);
    }

    [Fact]
    public void AMissingFile_IsNotFound_WithoutAReadAttempt()
    {
        _stamps.Clear();
        var cache = NewCache();

        var answers = cache.Refresh(new[] { Model });

        Assert.Equal(RevitFileProblem.NotFound, answers[Model].Problem);
        Assert.Equal(0, _reads);
    }

    [Fact]
    public void AFileInUse_IsTriedAgain_EvenUnchanged()
    {
        var problem = RevitFileProblem.InUse;
        var cache = NewCache(read: _ =>
        {
            Interlocked.Increment(ref _reads);
            return problem == RevitFileProblem.None ? Info(2025) : RevitFileInfo.Failed(problem);
        });
        Assert.Equal(RevitFileProblem.InUse, cache.Refresh(new[] { Model })[Model].Problem);

        _time.Advance(TimeSpan.FromMinutes(1));
        problem = RevitFileProblem.None;

        Assert.Equal(2025, cache.Refresh(new[] { Model })[Model].Release);
        Assert.Equal(2, _reads);
    }

    [Fact]
    public void AReaderThatThrows_GivesUnreadable()
    {
        var cache = NewCache(read: _ => throw new InvalidOperationException("boom"));

        Assert.Equal(RevitFileProblem.Unreadable, cache.Refresh(new[] { Model })[Model].Problem);
    }

    [Fact]
    public void AStalledRead_TimesOut_KeepsGoing_AndIsNotStartedTwice()
    {
        using var release = new ManualResetEventSlim();
        var cache = NewCache(read: _ =>
        {
            Interlocked.Increment(ref _reads);
            release.Wait(TimeSpan.FromSeconds(10));
            return Info(2025);
        }, timeout: TimeSpan.FromMilliseconds(50));

        Assert.Equal(RevitFileProblem.TimedOut, cache.Refresh(new[] { Model })[Model].Problem);
        Assert.Null(cache.Peek(Model));

        // Still stuck: a second refresh waits on the same read rather than starting another.
        Assert.Equal(RevitFileProblem.TimedOut, cache.Refresh(new[] { Model })[Model].Problem);
        Assert.Equal(1, _reads);

        release.Set();
        SpinWait.SpinUntil(() => cache.Peek(Model) is not null, TimeSpan.FromSeconds(5));
        Assert.Equal(2025, cache.Peek(Model)!.Release);
    }

    [Fact]
    public void AfterAStalledReadFinishes_ANewOneCanStart()
    {
        using var release = new ManualResetEventSlim();
        var cache = NewCache(read: _ =>
        {
            Interlocked.Increment(ref _reads);
            release.Wait(TimeSpan.FromSeconds(10));
            return Info(2025);
        }, timeout: TimeSpan.FromMilliseconds(50));
        cache.Refresh(new[] { Model });
        release.Set();
        SpinWait.SpinUntil(() => cache.Peek(Model) is not null, TimeSpan.FromSeconds(5));

        _time.Advance(TimeSpan.FromMinutes(1));
        _stamps[Model] = Tuesday;
        SpinWait.SpinUntil(() => cache.Refresh(new[] { Model })[Model].Problem == RevitFileProblem.None, TimeSpan.FromSeconds(5));

        Assert.Equal(2, _reads);
    }

    [Fact]
    public void RealFiles_AreReadThroughTheReader()
    {
        using var temp = new TempDirectory();
        var path = RevitTestFiles.Write(temp.File("Tower.rvt"), RevitTestFiles.Modern("2024", true, 0, Model, Model));

        var answers = new RevitReleaseCache().Refresh(new[] { path });

        Assert.Equal(2024, answers[path].Release);
        Assert.Equal(RevitWorksharing.Central, answers[path].Worksharing);
    }
}
