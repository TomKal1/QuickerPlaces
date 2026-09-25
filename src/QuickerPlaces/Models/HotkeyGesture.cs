using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickerPlaces.Models;

/// <summary>Modifier keys for a global hotkey. Values match Win32's MOD_* flags for RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Windows = 0x8
}

/// <summary>
/// A global hotkey as written in settings.json, e.g. "Ctrl+Alt+Space".
/// Parsing lives here, free of WPF, so it can be unit-tested. Turning
/// <see cref="Key"/> into a real virtual-key code happens in
/// Services/GlobalHotkey.cs, which also reports a key name Windows
/// doesn't recognise.
/// </summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    /// <summary>The hotkey used when settings.json doesn't name one.</summary>
    public const string Default = "Ctrl+Alt+Space";

    private static readonly Dictionary<string, HotkeyModifiers> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = HotkeyModifiers.Control,
        ["Control"] = HotkeyModifiers.Control,
        ["Alt"] = HotkeyModifiers.Alt,
        ["Shift"] = HotkeyModifiers.Shift,
        ["Win"] = HotkeyModifiers.Windows,
        ["Windows"] = HotkeyModifiers.Windows
    };

    /// <summary>True when <paramref name="text"/> means "no global hotkey": empty, whitespace, or "None".</summary>
    public static bool IsDisabled(string? text)
        => string.IsNullOrWhiteSpace(text) || string.Equals(text.Trim(), "None", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses "Modifier+...+Key", case-insensitively. It needs exactly one
    /// non-modifier key and at least one of Ctrl, Alt or Win: a bare key,
    /// or Shift plus a key, would be captured system-wide and break
    /// ordinary typing in every other app.
    /// </summary>
    public static bool TryParse(string? text, out HotkeyGesture gesture, out string? error)
    {
        gesture = default;

        if (IsDisabled(text))
        {
            error = "No hotkey was given.";
            return false;
        }

        var tokens = text!.Split('+').Select(t => t.Trim()).ToList();
        if (tokens.Any(t => t.Length == 0))
        {
            error = $"\"{text}\" has an empty part. Write it like {Default}.";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        string? key = null;

        foreach (var token in tokens)
        {
            if (ModifierNames.TryGetValue(token, out var modifier))
            {
                if (modifiers.HasFlag(modifier))
                {
                    error = $"\"{text}\" names the same modifier twice.";
                    return false;
                }

                modifiers |= modifier;
            }
            else if (key is null)
            {
                key = token;
            }
            else
            {
                error = $"\"{text}\" names more than one key ({key} and {token}). Use one key plus modifiers.";
                return false;
            }
        }

        if (key is null)
        {
            error = $"\"{text}\" has only modifiers. Add a key, like {Default}.";
            return false;
        }

        if ((modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Windows)) == 0)
        {
            error = $"\"{text}\" needs Ctrl, Alt or Win, or it would stop that key working in other apps.";
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        error = null;
        return true;
    }

    /// <summary>Canonical display form, modifiers in Windows' usual order: "Ctrl+Alt+Space".</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(Key.Length == 1 ? Key.ToUpperInvariant() : char.ToUpperInvariant(Key[0]) + Key[1..]);
        return string.Join("+", parts);
    }
}
