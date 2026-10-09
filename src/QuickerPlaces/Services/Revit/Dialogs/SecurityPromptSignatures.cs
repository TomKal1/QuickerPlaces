using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuickerPlaces.Services.Revit.Dialogs;

/// <summary>
/// What one release's add-in security prompt ("Always Load / Load Once / Do
/// Not Load") looks like, recorded from evidence (<c>qp revit dialogs</c>, or
/// UI Automation). Nothing here is guessed: the table ships empty, and a
/// signature counts for clicking only when <see cref="Verified"/> is true,
/// which Thomas sets after recording the prompt in that release.
///
/// A prompt matches when its title is exactly <see cref="Title"/>, its visible
/// buttons are exactly <see cref="ButtonTexts"/> (any order, "&amp;" marks and
/// case ignored), and <see cref="NamePattern"/> matches the static text.
/// </summary>
public sealed class SecurityPromptSignature
{
    /// <summary>The Revit release, for example 2025.</summary>
    public int Release { get; set; }

    /// <summary>True once someone has recorded this prompt in this release. Unverified signatures only help the status line.</summary>
    public bool Verified { get; set; }

    /// <summary>The dialog's exact title, as recorded.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Every button the prompt shows, as recorded.</summary>
    public List<string> ButtonTexts { get; set; } = new();

    /// <summary>The text of the one button that means Load Once, as recorded (it must be one of <see cref="ButtonTexts"/>).</summary>
    public string LoadOnceButtonText { get; set; } = string.Empty;

    /// <summary>A regular expression over the dialog's static text (lines joined with a newline); its <c>name</c> group is the add-in's name.</summary>
    public string NamePattern { get; set; } = string.Empty;

    /// <summary>Like <see cref="NamePattern"/>, with a <c>path</c> group, when the release shows the DLL path. Null when it doesn't.</summary>
    public string? PathPattern { get; set; }

    /// <summary>Where and when this was recorded; free text.</summary>
    public string? Notes { get; set; }
}

/// <summary>The add-in a recognised security prompt is about.</summary>
public sealed record SecurityPromptRecognition(SecurityPromptSignature Signature, string Name, string? DllPath);

/// <summary>
/// The recorded signatures, one or more per release. <see cref="Empty"/> is
/// what ships. A file next to the app (<see cref="FileName"/>) can supply
/// them: <c>{ "signatures": [ { "release": 2025, "verified": true, ... } ] }</c>
/// with the property names of <see cref="SecurityPromptSignature"/> in camel
/// case. A damaged file gives an empty table and a problem, never an exception.
/// </summary>
public sealed class SecurityPromptSignatures
{
    public const string FileName = "revit-security-prompts.json";

    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(200);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly List<SecurityPromptSignature> _signatures;

    public SecurityPromptSignatures(IEnumerable<SecurityPromptSignature> signatures, string? problem = null)
    {
        _signatures = signatures.ToList();
        Problem = problem;
    }

    public static SecurityPromptSignatures Empty { get; } = new(Array.Empty<SecurityPromptSignature>());

    public IReadOnlyList<SecurityPromptSignature> All => _signatures;

    /// <summary>Why the table is empty when a file couldn't be read; null otherwise.</summary>
    public string? Problem { get; }

    public static SecurityPromptSignatures FromJson(string json)
    {
        try
        {
            var file = JsonSerializer.Deserialize<SignatureFile>(json, JsonOptions);
            return new SecurityPromptSignatures(file?.Signatures ?? new List<SecurityPromptSignature>());
        }
        catch (JsonException ex)
        {
            return new SecurityPromptSignatures(Array.Empty<SecurityPromptSignature>(), "The signatures file isn't valid JSON: " + ex.Message);
        }
    }

    /// <summary>Reads <paramref name="path"/>; a missing file is the empty table with no problem.</summary>
    public static SecurityPromptSignatures Load(string path)
    {
        try
        {
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SecurityPromptSignatures(Array.Empty<SecurityPromptSignature>(), "The signatures file can't be read: " + ex.Message);
        }
    }

    public bool HasVerified(int release) => _signatures.Any(s => s.Release == release && s.Verified);

    /// <summary>
    /// The add-in a security prompt is about, when <paramref name="window"/>
    /// matches a signature of <paramref name="release"/> (verified or not;
    /// check <see cref="SecurityPromptSignature.Verified"/> before clicking).
    /// Null when no signature fits, which is the normal case. With
    /// <paramref name="verifiedOnly"/>, only verified signatures are tried.
    /// </summary>
    public SecurityPromptRecognition? Recognise(int release, DialogWindow window, bool verifiedOnly = false)
    {
        foreach (var signature in _signatures.Where(s => s.Release == release && (s.Verified || !verifiedOnly)))
        {
            if (Matches(signature, window) is { } recognition)
                return recognition;
        }

        return null;
    }

    private static SecurityPromptRecognition? Matches(SecurityPromptSignature signature, DialogWindow window)
    {
        if (!string.Equals(window.Title, signature.Title, StringComparison.Ordinal))
            return null;

        var shown = window.Buttons.Where(b => b.Visible).Select(b => DialogText.Normalise(b.Text)).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        var recorded = signature.ButtonTexts.Select(DialogText.Normalise).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        if (recorded.Count == 0 || !shown.SequenceEqual(recorded, StringComparer.OrdinalIgnoreCase))
            return null;

        var text = string.Join("\n", window.StaticTexts);
        var name = Capture(signature.NamePattern, "name", text);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        string? path = null;
        if (!string.IsNullOrEmpty(signature.PathPattern))
        {
            path = Capture(signature.PathPattern, "path", text);
            if (string.IsNullOrWhiteSpace(path))
                return null;
        }

        return new SecurityPromptRecognition(signature, name.Trim(), path?.Trim());
    }

    private static string? Capture(string pattern, string group, string text)
    {
        if (string.IsNullOrEmpty(pattern))
            return null;
        try
        {
            var match = Regex.Match(text, pattern, RegexOptions.CultureInvariant | RegexOptions.Multiline, PatternTimeout);
            return match.Success && match.Groups[group].Success ? match.Groups[group].Value : null;
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private sealed class SignatureFile
    {
        public List<SecurityPromptSignature>? Signatures { get; set; }
    }
}

/// <summary>How dialog text is compared: accelerator marks and surrounding or doubled spaces don't count.</summary>
public static class DialogText
{
    public static string Normalise(string text)
        => Regex.Replace(text.Replace("&", string.Empty, StringComparison.Ordinal), @"\s+", " ").Trim();
}
