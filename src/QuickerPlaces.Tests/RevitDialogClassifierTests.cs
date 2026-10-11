using System.Linq;
using QuickerPlaces.Services.Revit.Dialogs;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>Which of a Revit process's windows count as "waiting on a dialog", the status line, and security prompt signatures.</summary>
public sealed class RevitDialogClassifierTests
{
    private static DialogClassification Classify(int? release, SecurityPromptSignatures? table, params DialogWindow[] windows)
        => RevitDialogClassifier.Classify(windows, release, table ?? SecurityPromptSignatures.Empty);

    [Fact]
    public void AnEnabledVisibleDialogWithButtons_IsWaiting()
    {
        var result = Classify(null, null, DialogFixtures.Dialog("Autodesk Revit", new[] { "OK", "Cancel" }, new[] { "Something happened." }));

        var waiting = Assert.Single(result.Waiting);
        Assert.Equal("Autodesk Revit", waiting.Title);
        Assert.Equal(new[] { "OK", "Cancel" }, waiting.ButtonTexts);
        Assert.Equal(new[] { "Something happened." }, waiting.StaticTexts);
        Assert.Equal("Revit is waiting on a dialog: Autodesk Revit", result.StatusText);
    }

    [Theory]
    [InlineData("Model Upgrade", DialogVerdict.NoButtons)]
    public void AProgressWindowWithoutButtons_IsNotWaiting(string title, DialogVerdict verdict)
    {
        var result = Classify(null, null, DialogFixtures.Dialog(title, new string[0]));

        Assert.Empty(result.Waiting);
        Assert.Equal(verdict, Assert.Single(result.Verdicts));
        Assert.Null(result.StatusText);
    }

    [Fact]
    public void AProgressWindowWhoseOnlyButtonIsCancel_IsNotWaiting()
    {
        var result = Classify(null, null,
            DialogFixtures.Dialog("Load Link", new[] { "Cancel Link" }),
            DialogFixtures.Dialog("Working", new[] { "&Cancel" }));

        Assert.Empty(result.Waiting);
        Assert.All(result.Verdicts, v => Assert.Equal(DialogVerdict.ProgressOnly, v));
    }

    [Fact]
    public void ACancelButtonBesideAnother_IsStillWaiting()
    {
        Assert.Single(Classify(null, null, DialogFixtures.Dialog("Question", new[] { "Yes", "Cancel" })).Waiting);
    }

    [Fact]
    public void ADisabledDialog_AHiddenDialog_AndANonDialogWindow_AreNotWaiting()
    {
        var result = Classify(null, null,
            DialogFixtures.Dialog("Behind", new[] { "OK" }, enabled: false),
            DialogFixtures.Dialog("Hidden", new[] { "OK" }, visible: false),
            DialogFixtures.Dialog("Autodesk Revit 2025", new[] { "OK" }, className: "Afx:00400000:b:00010003:00000006:00000000"));

        Assert.Empty(result.Waiting);
        Assert.Equal(new[] { DialogVerdict.Disabled, DialogVerdict.Hidden, DialogVerdict.NotADialog }, result.Verdicts);
    }

    [Fact]
    public void HiddenButtonsDoNotCount()
    {
        var window = DialogFixtures.Dialog("Question", new[] { "OK" }) with
        {
            Buttons = new[] { new DialogButton("OK", 1, Visible: false) }
        };

        Assert.Equal(DialogVerdict.NoButtons, RevitDialogClassifier.Judge(window));
    }

    [Fact]
    public void SeveralDialogs_NameTheFirstAndCountTheRest()
    {
        var result = Classify(null, null,
            DialogFixtures.Dialog("First", new[] { "OK" }),
            DialogFixtures.Dialog("Second", new[] { "OK" }),
            DialogFixtures.Dialog("Third", new[] { "OK" }));

        Assert.Equal("Revit is waiting on a dialog: First and 2 more", result.StatusText);
    }

    [Fact]
    public void ADialogWithNoTitle_StillGivesAStatus()
    {
        Assert.Equal("Revit is waiting on a dialog: (no title)", Classify(null, null, DialogFixtures.Dialog("", new[] { "OK" })).StatusText);
    }

    [Fact]
    public void WithNoSignatures_ASecurityPromptIsReportedLikeAnyDialog()
    {
        var result = Classify(2025, SecurityPromptSignatures.Empty, DialogFixtures.SecurityPrompt("MyAddin"));

        Assert.Null(Assert.Single(result.Waiting).SecurityPrompt);
        Assert.Equal("Revit is waiting on a dialog: Security - Unsigned Add-In", result.StatusText);
    }

    [Fact]
    public void ASignatureNamesTheAddInInTheStatus_AndTakesItsPath()
    {
        var result = Classify(2025, DialogFixtures.Table(DialogFixtures.Signature()), DialogFixtures.SecurityPrompt("MyAddin", "C:\\Addins\\My.dll"));

        var prompt = Assert.Single(result.Waiting).SecurityPrompt!;
        Assert.Equal("MyAddin", prompt.Name);
        Assert.Equal("C:\\Addins\\My.dll", prompt.DllPath);
        Assert.Equal("Revit is waiting on a dialog: Security - Unsigned Add-In (MyAddin)", result.StatusText);
    }

    [Fact]
    public void AnUnverifiedSignatureStillNamesTheAddIn_ButIsNotVerified()
    {
        var table = DialogFixtures.Table(DialogFixtures.Signature(verified: false));
        var prompt = Classify(2025, table, DialogFixtures.SecurityPrompt("MyAddin")).Waiting.Single().SecurityPrompt!;

        Assert.False(prompt.Signature.Verified);
        Assert.Null(table.Recognise(2025, DialogFixtures.SecurityPrompt("MyAddin"), verifiedOnly: true));
    }

    [Fact]
    public void ASignatureForNoPathReleaseMatchesNameOnly()
    {
        var table = DialogFixtures.Table(DialogFixtures.Signature(withPath: false));

        var prompt = table.Recognise(2025, DialogFixtures.SecurityPrompt("MyAddin", dll: null))!;

        Assert.Equal("MyAddin", prompt.Name);
        Assert.Null(prompt.DllPath);
    }

    [Fact]
    public void ASignatureThatNeedsAPath_RejectsAPromptWithout()
    {
        Assert.Null(DialogFixtures.Table(DialogFixtures.Signature()).Recognise(2025, DialogFixtures.SecurityPrompt("MyAddin", dll: null)));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("buttons")]
    [InlineData("extraButton")]
    [InlineData("text")]
    [InlineData("release")]
    public void AnyMismatch_IsReportOnly(string what)
    {
        var table = DialogFixtures.Table(DialogFixtures.Signature(2025));
        var window = what switch
        {
            "title" => DialogFixtures.Dialog("Security - Unsigned Add-In (changed)", new[] { "Always Load", "Load Once", "Do Not Load" }, new[] { "Add-in: A", "Location: C:\\a.dll" }),
            "buttons" => DialogFixtures.SecurityPrompt("A", "C:\\a.dll", "Always Load", "Do Not Load"),
            "extraButton" => DialogFixtures.SecurityPrompt("A", "C:\\a.dll", "Always Load", "Load Once", "Do Not Load", "Help"),
            "text" => DialogFixtures.Dialog(DialogFixtures.SecurityTitle, new[] { "Always Load", "Load Once", "Do Not Load" }, new[] { "Something else entirely" }),
            _ => DialogFixtures.SecurityPrompt("A", "C:\\a.dll"),
        };
        var release = what == "release" ? 2024 : 2025;

        var result = Classify(release, table, window);

        Assert.Single(result.Waiting);
        Assert.Null(result.Waiting[0].SecurityPrompt);
    }

    [Fact]
    public void ButtonAcceleratorsAndCaseDoNotMatter()
    {
        var window = DialogFixtures.SecurityPrompt("A", "C:\\a.dll", "&Always Load", "load once", "Do Not  Load");

        Assert.NotNull(DialogFixtures.Table(DialogFixtures.Signature()).Recognise(2025, window));
    }

    [Fact]
    public void ABrokenPattern_NeverMatches_AndNeverThrows()
    {
        var signature = DialogFixtures.Signature();
        signature.NamePattern = "(unclosed";

        Assert.Null(DialogFixtures.Table(signature).Recognise(2025, DialogFixtures.SecurityPrompt("A")));
    }

    [Fact]
    public void Signatures_ReadFromJson_AndADamagedFileGivesAnEmptyTableWithAProblem()
    {
        var json = """
            { "signatures": [ { "release": 2025, "verified": true, "title": "T", "buttonTexts": ["A","B"],
              "loadOnceButtonText": "B", "namePattern": "(?<name>x)" } ] }
            """;

        var table = SecurityPromptSignatures.FromJson(json);
        Assert.Null(table.Problem);
        Assert.True(table.HasVerified(2025));
        Assert.False(table.HasVerified(2024));

        var damaged = SecurityPromptSignatures.FromJson("{ not json");
        Assert.Empty(damaged.All);
        Assert.NotNull(damaged.Problem);
        Assert.Empty(SecurityPromptSignatures.Load("/no/such/file.json").All);
    }

    [Fact]
    public void TheShippedTableIsEmpty()
    {
        Assert.Empty(SecurityPromptSignatures.Empty.All);
        Assert.False(SecurityPromptSignatures.Empty.HasVerified(2025));
    }
}
