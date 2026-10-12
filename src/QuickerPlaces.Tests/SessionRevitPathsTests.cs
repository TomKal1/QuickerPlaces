using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Revit;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Saving open files as a session: an open Revit local is saved as its
/// central, so the session opens a new local next time.
/// </summary>
public sealed class SessionRevitPathsTests
{
    private const string Central = @"\\files\Jobs\Tower\Tower_Central.rvt";
    private const string Local = @"C:\Users\me\Documents\Tower_Central_me.rvt";
    private const string Plain = @"C:\Jobs\Shed.rvt";
    private const string Pdf = @"C:\Jobs\A-101.pdf";

    private readonly Dictionary<string, RevitFileInfo> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _existing = new(StringComparer.OrdinalIgnoreCase);
    private readonly RevitReleaseCache _cache;

    public SessionRevitPathsTests()
    {
        _cache = new RevitReleaseCache(
            path => _files.TryGetValue(path, out var info) ? info : RevitFileInfo.Failed(RevitFileProblem.Unreadable),
            _ => new FileStamp(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), 1000),
            new ManualTimeProvider());
        Add(Central, RevitWorksharing.Central, null);
        Add(Local, RevitWorksharing.Local, Central);
        Add(Plain, RevitWorksharing.NotWorkshared, null);
    }

    private void Add(string path, RevitWorksharing worksharing, string? central)
    {
        _files[path] = new RevitFileInfo(2025, "b", worksharing, central, null, 14, RevitFileProblem.None);
        _existing.Add(path);
    }

    private OpenDocumentScan Swap(params DocumentCandidate[] candidates) =>
        SessionRevitPaths.PreferCentral(new OpenDocumentScan(candidates, new[] { "Untitled - Notepad" }) { Warning = "slow" }, _cache, _existing.Contains);

    private static DocumentCandidate Open(string path, string reason = "Open in Revit") => new(path, true, reason, null);

    [Fact]
    public void AnOpenLocal_IsSavedAsItsCentral_SayingSo()
    {
        var scan = Swap(Open(Local));

        var candidate = Assert.Single(scan.Candidates);
        Assert.Equal(Central, candidate.Path);
        Assert.True(candidate.IsLikelyOpen);
        Assert.Equal("Central of open local Tower_Central_me.rvt", candidate.Reason);
        Assert.Equal(new[] { "Untitled - Notepad" }, scan.UnmatchedTitles);
        Assert.Equal("slow", scan.Warning);
    }

    [Fact]
    public void ANewLocalNotYetSaved_StillReadingAsItsCentral_IsSavedAsTheCentral()
    {
        // CreateNewLocal copies the central; the header says local only after Revit first saves the copy.
        const string fresh = @"C:\REVIT_LOCAL2025\Tower_Central_me_20261011-225147.rvt";
        Add(fresh, RevitWorksharing.Central, Central);

        var candidate = Swap(Open(fresh)).Candidates.Single();

        Assert.Equal(Central, candidate.Path);
        Assert.Equal("Central of open local Tower_Central_me_20261011-225147.rvt", candidate.Reason);
    }

    [Fact]
    public void ACentralAtItsOwnPath_StaysPut()
    {
        Add(Central, RevitWorksharing.Central, Central);

        Assert.Equal(Central, Swap(Open(Central)).Candidates.Single().Path);
    }

    [Fact]
    public void ALocalWhoseCentralIsMissing_StaysTheLocal()
    {
        _existing.Remove(Central);

        Assert.Equal(Local, Swap(Open(Local)).Candidates.Single().Path);
    }

    [Fact]
    public void ALocalWhoseCentralIsNoLongerACentral_StaysTheLocal()
    {
        Add(Central, RevitWorksharing.NotWorkshared, null);

        Assert.Equal(Local, Swap(Open(Local)).Candidates.Single().Path);
    }

    [Fact]
    public void ACentralAlreadyListed_IsNotListedTwice_AndIsTickedIfEitherWasOpen()
    {
        var scan = Swap(new DocumentCandidate(Central, false, "Recently opened", null), Open(Pdf, "Open in Acrobat"), Open(Local));

        Assert.Equal(new[] { Central, Pdf }, scan.Candidates.Select(c => c.Path));
        Assert.True(scan.Candidates[0].IsLikelyOpen);
    }

    [Fact]
    public void OtherFiles_AndSuggestions_AreLeftAlone()
    {
        var suggestion = new DocumentCandidate(Local, false, "Recently opened", null);

        var scan = Swap(Open(Pdf, "Open in Acrobat"), Open(Plain), suggestion);

        Assert.Equal(new[] { Pdf, Plain, Local }, scan.Candidates.Select(c => c.Path));
        Assert.Equal("Recently opened", scan.Candidates[2].Reason);
    }
}
