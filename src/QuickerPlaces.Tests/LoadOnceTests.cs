using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Services.Revit.AddIns;
using QuickerPlaces.Services.Revit.Dialogs;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>The Load Once list: what to offer after a prompt, when Load Once may be pressed, and the press itself.</summary>
public sealed class LoadOnceTests
{
    private const int Release = 2025;
    private const string Dll = "C:\\Addins\\Tool.dll";
    private const string Hash = "aaaa";
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 8, 0, 0, TimeSpan.FromHours(10));

    private static AddInManifest Manifest(string name = "Tool", string? dll = Dll, string? id = "ID-1", int release = Release)
        => new(release, AddInKind.Application, name, dll, id, "ACME", "Tool.App", "C:\\m\\" + name + ".addin", ManifestSource.UserAddins);

    private static LoadOnceEntry Entry(string name = "Tool", string dll = Dll, string hash = Hash, int release = Release)
        => new() { Release = release, AddInId = "ID-1", Name = name, DllPath = dll, DllSha256 = hash, AllowedUtc = Now };

    private static string? HashIs(string path) => path == Dll ? Hash : null;

    // ---- Offers ----

    [Fact]
    public void Offer_OneMatchingManifest_GivesAnEntryWithTheHash()
    {
        var offer = LoadOnceOffers.Offer(Release, "Tool", Dll, new[] { Manifest(), Manifest("Other", "C:\\o.dll") }, Now, HashIs);

        var entry = Assert.IsType<LoadOnceEntry>(offer.Entry);
        Assert.Equal((Release, "ID-1", "Tool", Dll, Hash), (entry.Release, entry.AddInId, entry.Name, entry.DllPath, entry.DllSha256));
        Assert.Equal(Now.ToUniversalTime(), entry.AllowedUtc);
    }

    [Fact]
    public void Offer_NoMatch_GivesNoEntryAndAReason()
    {
        var offer = LoadOnceOffers.Offer(Release, "Unknown", Dll, new[] { Manifest() }, Now, HashIs);

        Assert.Null(offer.Entry);
        Assert.Contains("Unknown", offer.Reason);
    }

    [Fact]
    public void Offer_AManifestOfAnotherRelease_DoesNotMatch()
    {
        Assert.Null(LoadOnceOffers.Offer(Release, "Tool", Dll, new[] { Manifest(release: 2024) }, Now, HashIs).Entry);
    }

    [Fact]
    public void Offer_SeveralDifferentMatches_GiveNoEntry()
    {
        var offer = LoadOnceOffers.Offer(Release, "Tool", null, new[] { Manifest(), Manifest(dll: "C:\\Other\\Tool.dll", id: "ID-2") }, Now, HashIs);

        Assert.Null(offer.Entry);
        Assert.Contains("2", offer.Reason);
    }

    [Fact]
    public void Offer_ThePathInThePrompt_PicksOneOfTwoSameNamedAddIns()
    {
        var other = Manifest(dll: "C:\\Other\\Tool.dll", id: "ID-2");

        var offer = LoadOnceOffers.Offer(Release, "Tool", "c:/addins/tool.dll", new[] { Manifest(), other }, Now, HashIs);

        Assert.Equal(Dll, offer.Entry!.DllPath);
    }

    [Fact]
    public void Offer_NameOnly_NeedsExactlyOneManifestWithThatName()
    {
        Assert.NotNull(LoadOnceOffers.Offer(Release, "tool", null, new[] { Manifest() }, Now, HashIs).Entry);
        Assert.Null(LoadOnceOffers.Offer(Release, "Tool", null, new[] { Manifest(), Manifest(dll: "C:\\X\\Tool.dll", id: "ID-2") }, Now, HashIs).Entry);
    }

    [Fact]
    public void Offer_TheSameAddInInTwoFolders_CountsOnce()
    {
        var offer = LoadOnceOffers.Offer(Release, "Tool", null, new[] { Manifest(), Manifest() with { Source = ManifestSource.MachineAddins } }, Now, HashIs);

        Assert.NotNull(offer.Entry);
    }

    [Fact]
    public void Offer_NoDllInTheManifest_OrAnUnreadableDll_GivesNoEntry()
    {
        Assert.Null(LoadOnceOffers.Offer(Release, "Tool", null, new[] { Manifest(dll: null) }, Now, HashIs).Entry);
        Assert.Null(LoadOnceOffers.Offer(Release, "Tool", null, new[] { Manifest() }, Now, _ => null).Entry);
    }

    [Fact]
    public void Entries_RoundTripThroughCamelCaseJson()
    {
        var json = LoadOnceJson.Serialise(new[] { Entry() });

        Assert.Contains("\"dllSha256\"", json);
        Assert.Contains("\"allowedUtc\"", json);
        var back = Assert.Single(LoadOnceJson.Deserialise(json));
        Assert.Equal(Entry().Name, back.Name);
        Assert.Equal(Now, back.AllowedUtc);
        Assert.Empty(LoadOnceJson.Deserialise("null"));
    }

    // ---- The click decision ----

    private static LoadOnceContext Context(
        bool switchOn = true, bool launched = true, bool appeared = false, bool verified = true, bool withPath = true,
        IReadOnlyList<LoadOnceEntry>? entries = null, IReadOnlyList<AddInManifest>? manifests = null)
        => new(switchOn, launched, appeared, Release,
            DialogFixtures.Table(DialogFixtures.Signature(Release, verified, withPath)),
            entries ?? new[] { Entry() },
            manifests ?? new[] { Manifest() });

    private static DialogWindow Prompt(string name = "Tool", string? dll = Dll, params string[] buttons)
        => DialogFixtures.SecurityPrompt(name, dll, buttons);

    [Fact]
    public void Decide_WhenEverythingHolds_PressesOnlyLoadOnce()
    {
        var window = Prompt();

        var decision = LoadOnceClickPolicy.Decide(Context(), window, HashIs);

        Assert.True(decision.MayClick, decision.Reason);
        Assert.Equal("Load Once", decision.Button!.Text);
        Assert.Equal(window.Buttons.Single(b => b.Text == "Load Once").Handle, decision.Button.Handle);
        Assert.Null(decision.EntryToRemove);
    }

    [Fact]
    public void Decide_NameOnlyRelease_ClicksWhenTheManifestsSingleOutTheAddIn()
    {
        var ctx = Context(withPath: false);

        Assert.True(LoadOnceClickPolicy.Decide(ctx, Prompt(dll: null), HashIs).MayClick);
        var ambiguous = Context(withPath: false, manifests: new[] { Manifest(), Manifest(dll: "C:\\X\\Tool.dll", id: "ID-2") });
        Assert.False(LoadOnceClickPolicy.Decide(ambiguous, Prompt(dll: null), HashIs).MayClick);
        var elsewhere = Context(withPath: false, manifests: new[] { Manifest(dll: "C:\\X\\Tool.dll") });
        Assert.False(LoadOnceClickPolicy.Decide(elsewhere, Prompt(dll: null), HashIs).MayClick);
    }

    public static IEnumerable<object[]> Refusals()
    {
        yield return new object[] { "switch off", "off" };
        yield return new object[] { "not launched", "launched" };
        yield return new object[] { "handler appeared", "handler" };
        yield return new object[] { "unverified signature", "verified" };
        yield return new object[] { "other release", "verified" };
        yield return new object[] { "dialog differs", "match" };
        yield return new object[] { "not on list", "isn't on" };
        yield return new object[] { "other path", "isn't on" };
        yield return new object[] { "other release entry", "isn't on" };
        yield return new object[] { "two entries", "More than one" };
        yield return new object[] { "dll unreadable", "can't be read" };
        yield return new object[] { "disabled window", "isn't an enabled" };
        yield return new object[] { "no load once button", "exactly one" };
        yield return new object[] { "two load once buttons", "found 2" };
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Decide_EachFailedCondition_RefusesWithItsReason(string what, string reasonContains)
    {
        var window = Prompt();
        var ctx = Context();
        Func<string, string?> hash = HashIs;
        switch (what)
        {
            case "switch off": ctx = Context(switchOn: false); break;
            case "not launched": ctx = Context(launched: false); break;
            case "handler appeared": ctx = Context(appeared: true); break;
            case "unverified signature": ctx = Context(verified: false); break;
            case "other release": ctx = ctx with { Release = 2024 }; break;
            case "dialog differs": window = DialogFixtures.Dialog("Another dialog", new[] { "OK" }); break;
            case "not on list": ctx = Context(entries: new[] { Entry("Different") }); break;
            case "other path": window = Prompt(dll: "C:\\Elsewhere\\Tool.dll"); break;
            case "other release entry": ctx = Context(entries: new[] { Entry(release: 2024) }); break;
            case "two entries": ctx = Context(entries: new[] { Entry(), Entry() }); break;
            case "dll unreadable": hash = _ => null; break;
            case "disabled window": window = window with { Enabled = false }; break;
            case "no load once button":
                ctx = Context() with { Signatures = DialogFixtures.Table(Sig(s => s.LoadOnceButtonText = "Load Twice")) };
                break;
            case "two load once buttons":
                var buttons = window.Buttons.Concat(new[] { new DialogButton("Load Once", 999) }).ToList();
                window = window with { Buttons = buttons };
                ctx = Context() with { Signatures = DialogFixtures.Table(Sig(s => s.ButtonTexts.Add("Load Once"))) };
                break;
        }

        var decision = LoadOnceClickPolicy.Decide(ctx, window, hash);

        Assert.False(decision.MayClick);
        Assert.Contains(reasonContains, decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(decision.Button);
        Assert.Null(decision.EntryToRemove);
    }

    private static SecurityPromptSignature Sig(Action<SecurityPromptSignature> change)
    {
        var signature = DialogFixtures.Signature();
        change(signature);
        return signature;
    }

    [Fact]
    public void Decide_AChangedDll_RefusesMarksTheEntryForRemovalAndAsksAgain()
    {
        var entry = Entry();

        var decision = LoadOnceClickPolicy.Decide(Context(entries: new[] { entry }), Prompt(), _ => "bbbb");

        Assert.False(decision.MayClick);
        Assert.Same(entry, decision.EntryToRemove);
        Assert.Equal("Tool changed since you allowed it \u2014 allow Load Once again?", decision.FollowUp);
        Assert.Null(decision.Button);
    }

    [Fact]
    public void Decide_NeverChoosesAlwaysLoadOrDoNotLoad_EvenIfTheRecordedTextNamesThem()
    {
        // Whatever text the signature records is the button pressed, so a signature must record Load Once.
        // The decision also never picks a button the signature doesn't name.
        var decision = LoadOnceClickPolicy.Decide(Context(), Prompt(), HashIs);

        Assert.NotEqual("Always Load", decision.Button!.Text);
        Assert.NotEqual("Do Not Load", decision.Button.Text);
    }

    // ---- The clicker ----

    private static (LoadOnceClicker Clicker, FakeDialogDetector Detector, List<string> Log) Clicker()
    {
        var detector = new FakeDialogDetector();
        var log = new List<string>();
        return (new LoadOnceClicker(detector, HashIs, log.Add), detector, log);
    }

    [Fact]
    public void Clicker_PressesTheLoadOnceButtonOnce_AndLogsIt()
    {
        var (clicker, detector, log) = Clicker();
        var window = Prompt();
        detector.Windows[42] = new List<DialogWindow> { DialogFixtures.Dialog("Model Upgrade", new string[0]), window };

        var result = clicker.Poll(42, Context());

        Assert.True(result.Clicked);
        var pressed = Assert.Single(detector.Pressed);
        Assert.Equal("Load Once", pressed.Text);
        var line = Assert.Single(log);
        Assert.Contains("Tool", line);
        Assert.Contains("2025", line);
        Assert.Contains("42", line);
        Assert.Contains(Dll, line);
        Assert.Contains(Hash, line);
    }

    [Fact]
    public void Clicker_WhenARefused_PressesNothingAndLogsNothing()
    {
        var (clicker, detector, log) = Clicker();
        detector.Windows[42] = new List<DialogWindow> { Prompt() };

        var result = clicker.Poll(42, Context(switchOn: false));

        Assert.False(result.Clicked);
        Assert.Empty(detector.Pressed);
        Assert.Empty(log);
        Assert.Single(result.Decisions);
    }

    [Fact]
    public void Clicker_OnlyLooksAtTheGivenProcess()
    {
        var (clicker, detector, _) = Clicker();
        detector.Windows[7] = new List<DialogWindow> { Prompt() };

        Assert.False(clicker.Poll(42, Context()).Clicked);
        Assert.Empty(detector.Pressed);
    }

    [Fact]
    public void Clicker_AChangedDll_ReportsTheEntryToRemoveAndTheFollowUp()
    {
        var detector = new FakeDialogDetector();
        detector.Windows[42] = new List<DialogWindow> { Prompt() };
        var clicker = new LoadOnceClicker(detector, _ => "changed", _ => { });

        var result = clicker.Poll(42, Context());

        Assert.False(result.Clicked);
        Assert.Single(result.EntriesToRemove);
        Assert.Contains("changed since you allowed it", Assert.Single(result.FollowUps));
        Assert.Empty(detector.Pressed);
    }

    [Fact]
    public void Clicker_TwoPromptsInOneLook_PressesOnlyOne()
    {
        var (clicker, detector, log) = Clicker();
        detector.Windows[42] = new List<DialogWindow> { Prompt(), Prompt() };

        clicker.Poll(42, Context());

        Assert.Single(detector.Pressed);
        Assert.Single(log);
    }

    [Fact]
    public void Clicker_LogsWhenThePressWasNotDelivered()
    {
        var (clicker, detector, log) = Clicker();
        detector.PressSucceeds = false;
        detector.Windows[42] = new List<DialogWindow> { Prompt() };

        clicker.Poll(42, Context());

        Assert.Contains("delivered: False", Assert.Single(log));
    }
}
