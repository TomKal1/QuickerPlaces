using System;
using System.IO;
using System.Linq;
using QuickerPlaces.Services.Revit.Handlers;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>Which handlers are registered, and which of them are really running.</summary>
public sealed class RevitHandlerRegistryTests : IDisposable
{
    private static readonly DateTime Started = new(2026, 10, 8, 21, 13, 41, DateTimeKind.Utc);
    private static readonly DateTime Written = new(2026, 10, 8, 21, 14, 3, DateTimeKind.Utc);

    private readonly TempDirectory _dir = new();
    private readonly RevitProtocolFolder _folder;
    private readonly FakeProcessProbe _processes = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
    private readonly RevitHandlerRegistry _registry;

    public RevitHandlerRegistryTests()
    {
        _folder = new RevitProtocolFolder(_dir.Path);
        _registry = new RevitHandlerRegistry(_folder, _processes, _time);
    }

    public void Dispose() => _dir.Dispose();

    private void Register(string handlerId, string release = "2025", string? name = null, DateTime? written = null)
    {
        var registration = new HandlerRegistration
        {
            Protocol = 1, HandlerId = handlerId, DisplayName = name ?? handlerId, HandlerVersion = "1.0", RevitRelease = release,
            Actions = [HandlerProtocol.ActionOpenNewLocal], WrittenUtc = written ?? Written,
        };
        RevitProtocolFolder.WriteAtomic(_folder.RegistrationFile(release, handlerId), HandlerJson.ToUtf8(registration));
    }

    private string Instance(string handlerId, int pid, string release = "2025", DateTime? processStart = null, DateTime? ready = null, DateTime? loaded = null)
    {
        var instance = new HandlerInstance
        {
            Protocol = 1, HandlerId = handlerId, RevitRelease = release, ProcessId = pid,
            ProcessStartUtc = processStart ?? Started, LoadedUtc = loaded ?? Written, ReadyUtc = ready,
        };
        var path = _folder.InstanceFile(release, handlerId, pid);
        RevitProtocolFolder.WriteAtomic(path, HandlerJson.ToUtf8(instance));
        return path;
    }

    [Fact]
    public void NothingOnDisk_IsEmpty()
    {
        Assert.Empty(_registry.Read());
    }

    [Fact]
    public void ARegistrationWithNoInstance_IsListedAsNotSeen_SinceItWasWritten()
    {
        Register("sample", name: "Sample");

        var release = Assert.Single(_registry.Read());
        var handler = Assert.Single(release.Handlers);

        Assert.Equal("2025", release.Release);
        Assert.Equal("sample", handler.HandlerId);
        Assert.Equal("Sample", handler.DisplayName);
        Assert.Equal("1.0", handler.HandlerVersion);
        Assert.Equal([HandlerProtocol.ActionOpenNewLocal], handler.Actions);
        Assert.True(handler.Supports(HandlerProtocol.ActionOpenNewLocal));
        Assert.False(handler.IsLoaded);
        Assert.Equal(Written, handler.LastSeenUtc);
    }

    [Fact]
    public void AnInstanceWhoseProcessMatches_IsLive_AndReadyWhenItSaysSo()
    {
        Register("sample");
        _processes.Running[100] = Started.AddMilliseconds(400);
        Instance("sample", 100, ready: Written.AddSeconds(30));

        var handler = Assert.Single(Assert.Single(_registry.Read()).Handlers);

        Assert.True(handler.IsLoaded);
        Assert.True(handler.IsReady);
        Assert.Null(handler.LastSeenUtc);
        var live = Assert.Single(handler.Instances);
        Assert.Equal(100, live.ProcessId);
        Assert.Equal(Written, live.LoadedUtc);
        Assert.Equal(Written.AddSeconds(30), live.ReadyUtc);
    }

    [Fact]
    public void AnInstanceWithoutReadyUtc_IsLiveButStillStarting()
    {
        Register("sample");
        _processes.Running[100] = Started;
        Instance("sample", 100);

        var handler = Assert.Single(Assert.Single(_registry.Read()).Handlers);

        Assert.True(handler.IsLoaded);
        Assert.False(handler.IsReady);
        Assert.False(Assert.Single(handler.Instances).IsReady);
    }

    [Theory]
    [InlineData(-1.5)]
    [InlineData(1.5)]
    [InlineData(3600)]
    public void AProcessThatStartedMoreThanASecondApart_IsAReusedId_NotLive(double offsetSeconds)
    {
        Register("sample");
        _processes.Running[100] = Started.AddSeconds(offsetSeconds);
        Instance("sample", 100, ready: Written);

        var handler = Assert.Single(Assert.Single(_registry.Read()).Handlers);

        Assert.False(handler.IsLoaded);
        Assert.Equal(Written, handler.LastSeenUtc);
    }

    [Fact]
    public void AStartWithinOneSecond_IsTheSameProcess()
    {
        var instance = new HandlerInstance { Protocol = 1, HandlerId = "s", RevitRelease = "2025", ProcessId = 5, ProcessStartUtc = Started, LoadedUtc = Written };
        _processes.Running[5] = Started.AddSeconds(1);
        Assert.True(_registry.IsLive(instance));

        _processes.Running[5] = Started.AddSeconds(-1);
        Assert.True(_registry.IsLive(instance));

        _processes.Running[5] = Started.AddSeconds(1).AddTicks(1);
        Assert.False(_registry.IsLive(instance));
    }

    [Fact]
    public void AMissingProcess_IsNotLive_AndLastSeenIsTheNewestInstanceTime()
    {
        Register("sample");
        Instance("sample", 100, loaded: Written.AddMinutes(1));
        Instance("sample", 101, ready: Written.AddMinutes(9), loaded: Written.AddMinutes(5));

        var handler = Assert.Single(Assert.Single(_registry.Read()).Handlers);

        Assert.False(handler.IsLoaded);
        Assert.Equal(Written.AddMinutes(9), handler.LastSeenUtc);
    }

    [Fact]
    public void TwoCopiesOfOneRelease_AreBothListed()
    {
        Register("sample");
        _processes.Running[100] = Started;
        _processes.Running[200] = Started.AddHours(1);
        Instance("sample", 100);
        Instance("sample", 200, processStart: Started.AddHours(1), ready: Written);

        var handler = Assert.Single(Assert.Single(_registry.Read()).Handlers);

        Assert.Equal([100, 200], handler.Instances.Select(i => i.ProcessId));
        Assert.True(handler.IsReady);
    }

    [Fact]
    public void TwoHandlersInOneRelease_AreReportedSeparately_EachWithItsOwnInstances()
    {
        Register("alpha", name: "Zeta tools");
        Register("beta", name: "Alpha tools");
        _processes.Running[100] = Started;
        Instance("beta", 100);

        var handlers = Assert.Single(_registry.Read()).Handlers;

        Assert.Equal(["beta", "alpha"], handlers.Select(h => h.HandlerId));
        Assert.True(handlers[0].IsLoaded);
        Assert.False(handlers[1].IsLoaded);
    }

    [Fact]
    public void ReleasesAreSeparate_AndInOrder()
    {
        Register("sample", "2026");
        Register("sample", "2024");
        _processes.Running[100] = Started;
        Instance("sample", 100, "2026");

        var releases = _registry.Read();

        Assert.Equal(["2024", "2026"], releases.Select(r => r.Release));
        Assert.False(releases[0].Handlers[0].IsLoaded);
        Assert.True(releases[1].Handlers[0].IsLoaded);
        Assert.Single(_registry.Read("2024"));
        Assert.Empty(_registry.Read("2025"));
    }

    [Fact]
    public void AHandlerIdWithDashes_IsTakenFromTheFileContents_NotItsName()
    {
        Register("contoso-revit-tools", "2025");
        Register("contoso", "2025");
        _processes.Running[300] = Started;
        Instance("contoso-revit-tools", 300);

        var handlers = Assert.Single(_registry.Read()).Handlers;

        Assert.Equal(["contoso", "contoso-revit-tools"], handlers.Select(h => h.HandlerId));
        Assert.False(handlers.Single(h => h.HandlerId == "contoso").IsLoaded);
        Assert.True(handlers.Single(h => h.HandlerId == "contoso-revit-tools").IsLoaded);
    }

    [Fact]
    public void IdentityComesFromContents_EvenWhenTheFileNameLies()
    {
        Register("real");
        File.Move(_folder.RegistrationFile("2025", "real"), Path.Combine(_folder.HandlersFolder, "something-else-1999.json"));

        var handler = Assert.Single(Assert.Single(_registry.Read()).Handlers);

        Assert.Equal("real", handler.HandlerId);
        Assert.Equal("2025", _registry.Read()[0].Release);
    }

    [Fact]
    public void UnreadableAndInvalidFiles_AreSkipped()
    {
        Register("good");
        File.WriteAllText(Path.Combine(_folder.HandlersFolder, "junk.json"), "not json");
        File.WriteAllText(Path.Combine(_folder.HandlersFolder, "empty.json"), "");
        File.WriteAllText(Path.Combine(_folder.HandlersFolder, "bad-id.json"), """{"protocol":1,"handlerId":"BAD ID","displayName":"x","revitRelease":"2025","actions":[],"writtenUtc":"2026-10-08T21:14:03Z"}""");
        File.WriteAllText(Path.Combine(_folder.HandlersFolder, ".hidden.json"), File.ReadAllText(_folder.RegistrationFile("2025", "good")).Replace("good", "hidden"));
        File.WriteAllText(Path.Combine(_folder.HandlersFolder, "half.json.tmp"), File.ReadAllText(_folder.RegistrationFile("2025", "good")).Replace("good", "half"));
        File.WriteAllText(Path.Combine(_folder.HandlersFolder, "huge.json"), new string(' ', 70_000));
        Directory.CreateDirectory(_folder.InstancesFolder);
        File.WriteAllText(Path.Combine(_folder.InstancesFolder, "junk.json"), "{");
        _processes.Running[100] = Started;
        Instance("good", 100);

        var handler = Assert.Single(Assert.Single(_registry.Read()).Handlers);

        Assert.Equal("good", handler.HandlerId);
        Assert.Single(handler.Instances);
    }

    [Fact]
    public void AnInstanceWithNoRegistration_IsIgnored()
    {
        _processes.Running[100] = Started;
        Instance("orphan", 100);

        Assert.Empty(_registry.Read());
    }

    [Fact]
    public void TwoRegistrationsForOneHandler_KeepTheNewer()
    {
        Register("sample", name: "Old");
        File.Move(_folder.RegistrationFile("2025", "sample"), Path.Combine(_folder.HandlersFolder, "copy.json"));
        Register("sample", name: "New", written: Written.AddDays(1));

        var handler = Assert.Single(Assert.Single(_registry.Read()).Handlers);

        Assert.Equal("New", handler.DisplayName);
    }

    [Fact]
    public void DeleteStaleInstances_RemovesOnlyDeadFilesOlderThanAnHour()
    {
        Register("sample");
        _processes.Running[100] = Started;
        var live = Instance("sample", 100);
        var deadOld = Instance("sample", 101);
        var deadNew = Instance("sample", 102);
        var liveOld = live;
        File.SetLastWriteTimeUtc(deadOld, _time.GetUtcNow().UtcDateTime.AddMinutes(-61));
        File.SetLastWriteTimeUtc(deadNew, _time.GetUtcNow().UtcDateTime.AddMinutes(-59));
        File.SetLastWriteTimeUtc(liveOld, _time.GetUtcNow().UtcDateTime.AddDays(-3));

        var deleted = _registry.DeleteStaleInstances();

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(deadOld));
        Assert.True(File.Exists(deadNew));
        Assert.True(File.Exists(liveOld));
    }

    [Fact]
    public void DeleteStaleInstances_WithNoFolder_DeletesNothing()
    {
        Assert.Equal(0, _registry.DeleteStaleInstances());
    }
}
