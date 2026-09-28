using System;
using System.Linq;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>The Save Session dialog's decisions (sessions plan §5, D9): what starts ticked, adding by hand, tags, and saving.</summary>
public sealed class SessionEditorViewModelTests
{
    private const string A101 = @"C:\Jobs\Tower B\A-101.pdf";
    private const string A102 = @"C:\Jobs\Tower B\A-102.pdf";
    private const string Spec = @"C:\Jobs\Tower A\Spec.pdf";

    private readonly FakePlacesStorage _storage = new();
    private readonly SessionStore _store;

    public SessionEditorViewModelTests()
    {
        _store = new SessionStore(_storage, new ManualTimeProvider());
    }

    private static OpenPdfScan Scan(params PdfCandidate[] candidates) => new(candidates, Array.Empty<string>());

    private static PdfCandidate Open(string path) => new(path, true, "Open in Adobe Acrobat", null);

    private static PdfCandidate Suggested(string path) => new(path, false, "Recently opened", null);

    [Fact]
    public void AScan_TicksOpenFiles_AndListsSuggestionsUnticked()
    {
        var vm = new SessionEditorViewModel(_store, null);

        vm.ApplyScan(Scan(Open(A101), Open(A102), Suggested(Spec)));

        Assert.Equal(new[] { true, true, false }, vm.Files.Select(f => f.IsIncluded));
        Assert.Equal("Open in Adobe Acrobat", vm.Files[0].Reason);
        Assert.Equal("2 of 3 PDFs ticked", vm.IncludedText);
        Assert.Equal("Found 2 open PDFs. Check the list, then Save.", vm.ScanSummary);
    }

    [Fact]
    public void AScanWithNothingOpen_SaysSo_AndNamesUnmatchedWindows()
    {
        var vm = new SessionEditorViewModel(_store, null);

        vm.ApplyScan(new OpenPdfScan(new[] { Suggested(Spec) }, new[] { "Spec sheet.pdf (Microsoft Edge)" }) { Warning = "Part of the scan took too long." });

        Assert.Equal(
            "No open PDFs were found. Tick any recently opened ones below, or use Add PDFs.\n" +
            "Also open, but not matched to a file: Spec sheet.pdf (Microsoft Edge). Use Add PDFs to include it.\n" +
            "Part of the scan took too long.",
            vm.ScanSummary);
    }

    [Fact]
    public void ASecondScan_AddsOnlyNewFiles_AndTicksOnesNowOpen_WithoutUntickingAny()
    {
        var vm = new SessionEditorViewModel(_store, null);
        vm.ApplyScan(Scan(Open(A101), Suggested(Spec), Suggested(A102)));
        vm.Files[0].IsIncluded = false;
        vm.Files[1].IsIncluded = true;

        vm.ApplyScan(Scan(Open(A102), Suggested(Spec)));

        Assert.Equal(new[] { A101, Spec, A102 }, vm.Files.Select(f => f.Path));
        Assert.Equal(new[] { false, true, true }, vm.Files.Select(f => f.IsIncluded));
    }

    [Fact]
    public void AddFiles_AddsTicked_TicksOnesAlreadyListed_AndRefusesNonPdfs()
    {
        var vm = new SessionEditorViewModel(_store, null);
        vm.ApplyScan(Scan(Suggested(A101)));

        vm.AddFiles(new[] { A101.ToLowerInvariant(), A102, @"C:\Jobs\notes.txt" });

        Assert.Equal(new[] { A101, A102 }, vm.Files.Select(f => f.Path));
        Assert.All(vm.Files, f => Assert.True(f.IsIncluded));
        Assert.Equal("Added by you", vm.Files[1].Reason);
        Assert.Equal("Only PDFs can be added: notes.txt.", vm.ErrorMessage);
    }

    [Fact]
    public void Save_KeepsOnlyTickedFiles_WithTheParsedTags()
    {
        var vm = new SessionEditorViewModel(_store, null);
        vm.ApplyScan(Scan(Open(A101), Suggested(Spec), Open(A102)));
        vm.Name = "Tower B";
        vm.TagsText = "Tower B, markups";

        Assert.True(vm.Save());

        var saved = _store.Find(vm.SavedId!)!;
        Assert.Equal(new[] { A101, A102 }, saved.Files);
        Assert.Equal(new[] { "Tower B", "markups" }, saved.Tags);
        Assert.Null(vm.SavePersistenceMessage);
    }

    [Fact]
    public void Save_WithNothingTicked_IsRefused_AndSaysWhy()
    {
        var vm = new SessionEditorViewModel(_store, null);
        vm.ApplyScan(Scan(Suggested(A101)));
        vm.Name = "Tower B";

        Assert.False(vm.Save());
        Assert.Equal("Choose at least one PDF for the session.", vm.ErrorMessage);
        Assert.Null(vm.SavedId);
        Assert.Empty(_store.Sessions);
    }

    [Fact]
    public void Save_WhoseWriteFails_StillSaves_AndPassesTheReasonOn()
    {
        _storage.FailNextWrite = true;
        var vm = new SessionEditorViewModel(_store, null) { Name = "Tower B" };
        vm.AddFiles(new[] { A101 });

        Assert.True(vm.Save());
        Assert.Contains("Couldn't save your sessions", vm.SavePersistenceMessage);
    }

    [Fact]
    public void Editing_StartsFromTheSession_AndSavesChangesToIt()
    {
        Assert.True(_store.TryCreate("Tower B", new[] { "a", "b" }, new[] { A101, A102 }, out var created, out _).Success);

        var vm = new SessionEditorViewModel(_store, created);

        Assert.False(vm.IsNew);
        Assert.Equal("Edit Session", vm.Title);
        Assert.Equal("Tower B", vm.Name);
        Assert.Equal("a, b", vm.TagsText);
        Assert.Equal(new[] { A101, A102 }, vm.Files.Select(f => f.Path));
        Assert.All(vm.Files, f => Assert.True(f.IsIncluded));

        vm.Remove(vm.Files[0]);
        vm.AddFiles(new[] { Spec });
        vm.Name = "Tower B rev 2";
        Assert.True(vm.Save());

        Assert.Equal(created!.Id, vm.SavedId);
        var saved = Assert.Single(_store.Sessions);
        Assert.Equal("Tower B rev 2", saved.Name);
        Assert.Equal(new[] { A102, Spec }, saved.Files);
    }

    [Fact]
    public void TagSuggestions_AreOtherSessionsTags_NotAlreadyTyped()
    {
        Assert.True(_store.TryCreate("Tower A", new[] { "Admin", "Markups" }, new[] { Spec }, out _, out _).Success);
        var vm = new SessionEditorViewModel(_store, null);
        Assert.Equal(new[] { "Admin", "Markups" }, vm.TagSuggestions);

        vm.AddTag("Markups");

        Assert.Equal("Markups", vm.TagsText);
        Assert.Equal(new[] { "Admin" }, vm.TagSuggestions);

        vm.TagsText = "markups, admin";
        Assert.False(vm.HasTagSuggestions);
    }

    [Fact]
    public void SetAllIncluded_TicksOrUnticksEverything()
    {
        var vm = new SessionEditorViewModel(_store, null);
        vm.ApplyScan(Scan(Open(A101), Suggested(Spec)));

        vm.SetAllIncluded(true);
        Assert.Equal(2, vm.IncludedCount);

        vm.SetAllIncluded(false);
        Assert.Equal(0, vm.IncludedCount);
        Assert.Equal("0 of 2 PDFs ticked", vm.IncludedText);
    }
}
