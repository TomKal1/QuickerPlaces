using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Services.Revit.Dialogs;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>A detector over a made-up list of windows that records the buttons pressed.</summary>
public sealed class FakeDialogDetector : IDialogDetector
{
    public Dictionary<int, List<DialogWindow>> Windows { get; } = new();

    public List<DialogButton> Pressed { get; } = new();

    public bool PressSucceeds { get; set; } = true;

    public IReadOnlyList<DialogWindow> ListWindows(int processId)
        => Windows.TryGetValue(processId, out var list) ? list : new List<DialogWindow>();

    public bool PressButton(DialogButton button)
    {
        Pressed.Add(button);
        return PressSucceeds;
    }
}

/// <summary>Builders for the windows the dialog tests describe.</summary>
public static class DialogFixtures
{
    public const string SecurityTitle = "Security - Unsigned Add-In";
    public const string AlwaysLoad = "Always Load";
    public const string LoadOnce = "Load Once";
    public const string DoNotLoad = "Do Not Load";

    private static long _next = 100;

    public static DialogWindow Dialog(string title, string[] buttons, string[]? statics = null, bool enabled = true, bool visible = true, string className = DialogWindow.DialogClassName)
        => new(++_next, className, enabled, visible, title, statics ?? new string[0],
            buttons.Select(b => new DialogButton(b, ++_next)).ToList());

    /// <summary>A security prompt as the made-up Revit shows it: the add-in's name, and its DLL on the next line when given.</summary>
    public static DialogWindow SecurityPrompt(string name, string? dll = "C:\\Addins\\Tool.dll", params string[] buttons)
    {
        var texts = new List<string> { $"Add-in: {name}" };
        if (dll is not null)
            texts.Add($"Location: {dll}");
        return Dialog(SecurityTitle, buttons.Length == 0 ? new[] { AlwaysLoad, LoadOnce, DoNotLoad } : buttons, texts.ToArray());
    }

    /// <summary>A verified signature for <see cref="SecurityPrompt"/>.</summary>
    public static SecurityPromptSignature Signature(int release = 2025, bool verified = true, bool withPath = true) => new()
    {
        Release = release,
        Verified = verified,
        Title = SecurityTitle,
        ButtonTexts = { AlwaysLoad, LoadOnce, DoNotLoad },
        LoadOnceButtonText = LoadOnce,
        NamePattern = @"^Add-in: (?<name>.+)$",
        PathPattern = withPath ? @"^Location: (?<path>.+)$" : null,
    };

    public static SecurityPromptSignatures Table(params SecurityPromptSignature[] signatures) => new(signatures);
}
