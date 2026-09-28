using System;
using System.ComponentModel;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>Reopening a session's PDFs (sessions plan §5): missing and refused files, and Last opened.</summary>
public sealed class SessionLauncherTests
{
    private const string A101 = @"C:\Jobs\A-101.pdf";
    private const string A102 = @"C:\Jobs\A-102.pdf";
    private const string Spec = @"\\files\projects\Spec.pdf";

    private readonly ManualTimeProvider _time = new();
    private readonly FakePlacesStorage _storage = new();
    private readonly FakeShell _shell = new();
    private readonly SessionStore _store;
    private readonly SessionLauncher _launcher;
    private readonly SessionSnapshot _session;

    public SessionLauncherTests()
    {
        _store = new SessionStore(_storage, _time);
        _launcher = new SessionLauncher(_store, _shell);
        Assert.True(_store.TryCreate("Tower B", null, new[] { A101, A102, Spec }, out var created, out _).Success);
        _session = created!;
        _time.Advance(TimeSpan.FromHours(1));
    }

    [Fact]
    public void EveryFile_IsOpenedInOrder_AndLastOpenedIsRecorded()
    {
        _shell.ExistingFiles.UnionWith(new[] { A101, A102, Spec });

        var outcome = _launcher.Open(_session);

        Assert.Equal(new[] { A101, A102, Spec }, _shell.Opened);
        Assert.Equal(new[] { A101, A102, Spec }, outcome.Launched);
        Assert.Null(outcome.Summary);
        Assert.Equal(_time.UtcNow, _store.Find(_session.Id)!.LastOpenedAt);
    }

    [Fact]
    public void AMissingFile_IsSkippedAndNamed_TheRestStillOpen()
    {
        _shell.ExistingFiles.UnionWith(new[] { A101, Spec });

        var outcome = _launcher.Open(_session);

        Assert.Equal(new[] { A101, Spec }, _shell.Opened);
        Assert.Equal(new[] { A102 }, outcome.Missing);
        Assert.Equal("1 file couldn't be found and wasn't opened: A-102.pdf.", outcome.Summary);
    }

    [Fact]
    public void AFileWindowsRefuses_IsReported_TheRestStillOpen()
    {
        _shell.ExistingFiles.UnionWith(new[] { A101, A102, Spec });
        _shell.RefusedTargets.Add(A102);

        var outcome = _launcher.Open(_session);

        Assert.Equal(new[] { A101, Spec }, _shell.Opened);
        var failure = Assert.Single(outcome.Failed);
        Assert.Equal(A102, failure.Path);
        Assert.StartsWith("Windows couldn't open A-102.pdf: ", outcome.Summary);
    }

    [Fact]
    public void NothingOpened_RecordsNothing()
    {
        var outcome = _launcher.Open(_session);

        Assert.Empty(outcome.Launched);
        Assert.Equal("3 files couldn't be found and weren't opened: A-101.pdf, A-102.pdf, Spec.pdf.", outcome.Summary);
        Assert.Null(_store.Find(_session.Id)!.LastOpenedAt);
        Assert.Equal(1, _storage.WriteCount);
    }

    [Fact]
    public void OneChosenFile_OpensOnlyThatFile()
    {
        _shell.ExistingFiles.UnionWith(new[] { A101, A102, Spec });

        var outcome = _launcher.Open(_session, new[] { A102.ToUpperInvariant() });

        Assert.Equal(new[] { A102 }, _shell.Opened);
        Assert.Equal(new[] { A102 }, outcome.Launched);
    }

    [Fact]
    public void AFailedLastOpenedSave_IsInTheSummary()
    {
        _shell.ExistingFiles.Add(A101);
        _storage.FailNextWrite = true;

        var outcome = _launcher.Open(_session, new[] { A101 });

        Assert.Equal(new[] { A101 }, outcome.Launched);
        Assert.False(outcome.Persistence.Saved);
        Assert.Contains("Couldn't save your sessions", outcome.Summary);
    }

    [Fact]
    public void MoreThanThreeMissing_AreCounted()
    {
        Assert.True(_store.TryCreate("Big", null, new[] { @"C:\1.pdf", @"C:\2.pdf", @"C:\3.pdf", @"C:\4.pdf", @"C:\5.pdf" }, out var big, out _).Success);

        var outcome = _launcher.Open(big!);

        Assert.Equal("5 files couldn't be found and weren't opened: 1.pdf, 2.pdf, 3.pdf and 2 more.", outcome.Summary);
    }

    [Fact]
    public void FakeShell_RefusesLikeWindows()
        => Assert.IsType<Win32Exception>(Record.Exception(() => { _shell.RefusedTargets.Add("x"); _shell.Open("x"); }));
}
