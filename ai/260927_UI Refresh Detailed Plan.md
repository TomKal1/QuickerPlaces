# UI Refresh (Saab 900 look) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle QuickerPlaces to the Saab 900 design in the QuickerPlaces Design System (version 2), with Dark, Light and Match Windows themes and a user-chosen highlight colour.

**Architecture:** Colours move out of the single `Theme.xaml` into two palette dictionaries (`Palette.Dark.xaml`, `Palette.Light.xaml`) that define identical keys; every style and view refers to them with `DynamicResource`. A new app-only `ThemeManager` swaps the palette, sets WPF's `ThemeMode` (title bar and menus), and writes the five `Highlight.*` brushes computed by a UI-free, unit-tested `HighlightPalette`. Styles, icons (stroke geometry) and embedded fonts (TASA Orbiter, IBM Plex Mono) replace the violet theme and the Segoe icon font.

**Tech Stack:** WPF on .NET 10 (`net10.0-windows`), hand-rolled MVVM, xUnit tests on plain `net10.0` with linked UI-free sources, fontTools (Python) to make static font instances, PowerShell + System.Drawing for the app icon.

---

## 0. How to use this document

- Work on a branch: `claude/ui-refresh`, created from `main` in Task 1. Commit after every task. Merging is the user's call.
- Sources of truth for the look:
  - QuickerPlaces Design System, version 2: https://claude.ai/artifact/MCDp4Ax8HWUgVUw3S39N2W (tokens, components, usage rules). Read `project/README.md` and `project/tokens.json` there with the Artifact tool, never by guessing.
  - Design canvas with the mockups: https://claude.ai/artifact/46meR5TGTPBdtUDscAFGuS (Main window, Recents, Settings, Palette boards).
- Token names map mechanically: the Design System's `Bg-Base` is the XAML key `Bg.Base`, `Leather-Deep` is `Leather.Deep`, and so on.
- Build and test commands (run from `C:\QuickerPlaces\src`):
  - `dotnet build QuickerPlaces.sln -nologo -v q` — expected `0 Warning(s)`, `0 Error(s)`.
  - `dotnet test QuickerPlaces.Tests -nologo` — baseline before this plan: **547 passed**.
- Downloads (the fonts in Task 7) need the user's OK first: say what file, from where, and roughly how big.
- WPF can't be exercised by the test project (it links only UI-free files). Everything visual is checked by the manual walk in section 8.

## 1. Where this starts from

- `main` at `ba6ad88` (Phase 9 merged). Build clean, 547 tests pass.
- Look today: one dark violet theme in `src/QuickerPlaces/Resources/Theme.xaml`, referenced with `StaticResource` from every view; icons are Segoe Fluent Icons / MDL2 glyph strings (some produced by view models: `PlaceViewModel.TypeGlyph`, `FavouriteGlyph`, `RecentlyDeletedRowViewModel.TypeGlyph`); fonts are Segoe UI and Consolas; `App.xaml` sets `ThemeMode="Dark"` for a dark title bar.
- Settings live in `AppSettings` (schema 4) and `SettingsService` (JSON under `%LocalAppData%`).
- The Design System's first version (the violet baseline) is recorded in that artifact's history; version 2 is the target.

## 2. Scope

**In:**
1. Dark and Light palettes, Match Windows (follows the Windows app theme live).
2. Highlight presets: Hull green (default), Steel blue, Tail red, Cognac, Windows accent (follows the Windows accent colour live).
3. Settings dialog gains an Appearance section; choices preview live and revert on Cancel.
4. New styles for every control the app uses; stroke icons instead of the icon font; TASA Orbiter and IBM Plex Mono embedded.
5. Main window, Recents and every dialog restyled to the Design System.
6. Places list loses the Type column (the folder/globe icon shows it); names 15px; favourites become leather cards with their Ctrl+number.
7. Wording: sentence case; "Name" instead of "Alias", "Folder or link" instead of "Path / URL", "Add link" instead of "Add URL" — on screen, in validation messages and in `USERGUIDE.md`.
8. New app icon: Brand green tile, off-white "QP", silver edge.

**Out (noted in section 12):** visits bars in the Recents table; rounded corners on tables; letter-spacing on capitals labels; a free colour picker.

## 3. Target file layout

| File | Change | Responsibility |
|---|---|---|
| `src/QuickerPlaces/Models/ThemePreference.cs` | create | `AppTheme`, `HighlightPreset` enums; tolerant parse/format for settings.json |
| `src/QuickerPlaces/Models/AppSettings.cs` | modify | schema 5: `Theme`, `Highlight` strings |
| `src/QuickerPlaces/Services/Theming/ThemeColor.cs` | create | UI-free colour value: parse, luminance, contrast, mix |
| `src/QuickerPlaces/Services/Theming/HighlightPalette.cs` | create | preset table, Windows-accent derivation, ground colours |
| `src/QuickerPlaces/Services/ThemeManager.cs` | create | app-only: palette swap, `ThemeMode`, highlight brushes, system change events |
| `src/QuickerPlaces/Resources/Palette.Dark.xaml` | create | dark colour brushes |
| `src/QuickerPlaces/Resources/Palette.Light.xaml` | create | light colour brushes (same keys) |
| `src/QuickerPlaces/Resources/Icons.xaml` | create | stroke icon geometries |
| `src/QuickerPlaces/Resources/Styles.xaml` | create (replaces `Theme.xaml`) | fonts, focus ring, every control style |
| `src/QuickerPlaces/Resources/Theme.xaml` | delete | superseded |
| `src/QuickerPlaces/Resources/Fonts/*` | create | TASA Orbiter (5 weights), IBM Plex Mono (2 weights), licences |
| `src/QuickerPlaces/Resources/AppIcon.ico` | replace | new icon |
| `tools/make-app-icon.ps1` | create | regenerates AppIcon.ico |
| `src/QuickerPlaces/App.xaml`, `App.xaml.cs` | modify | merge palette/styles; create and apply ThemeManager |
| `src/QuickerPlaces/Views/*.xaml`, some `.xaml.cs` | modify | restyle and rewording |
| `src/QuickerPlaces/ViewModels/PlaceViewModel.cs` | modify | drop glyph strings; add `FavouriteShortcut` |
| `src/QuickerPlaces/ViewModels/RecentlyDeletedRowViewModel.cs`, `SelectablePlaceViewModel.cs` | modify | expose `Type`; drop glyph |
| `src/QuickerPlaces/ViewModels/MainViewModel.cs` | modify | `PlaceCountText`, wording, grid sort filter |
| `src/QuickerPlaces/Services/PlaceSort.cs` | modify | `ForPlacesGrid` |
| `src/QuickerPlaces/Services/PlacesService.cs` | modify | "name" wording in messages |
| `src/QuickerPlaces.Tests/*` | modify/create | new tests, updated assertions, palette files linked |
| `USERGUIDE.md`, `README.md`, `ai/BUILD_SUMMARY.md`, `ai/260927_UI Refresh Handoff.md` | modify/create | documentation |

## 4. Design decisions

- **U1 — Two palette files, one style file.** `Palette.Dark.xaml` and `Palette.Light.xaml` hold only `SolidColorBrush` resources with identical keys; `Styles.xaml` holds everything else. A test (Task 4) enforces identical keys and the contrast floors the Design System states.
- **U2 — `DynamicResource` for every brush.** Only brushes change at runtime; fonts, geometries and styles stay `StaticResource`.
- **U3 — Highlight brushes are written at the top level of `Application.Resources`.** Top-level entries win over merged dictionaries, so the palette files can carry Hull green defaults (useful in the designer) while `ThemeManager` overrides them.
- **U4 — `ThemeMode` stays, set in code.** It keeps Windows drawing a dark or light title bar and gives context menus a matching Fluent look. `ThemeManager` sets it to `Dark` or `Light` itself (never `System`) so it always matches the palette. The palette dictionary is found by its file name, not by index, because WPF inserts its own Fluent dictionary into `MergedDictionaries`.
- **U5 — Match Windows and Windows accent read the registry** (`HKCU\...\Themes\Personalize\AppsUseLightTheme`, `HKCU\Software\Microsoft\Windows\DWM\AccentColor`) and re-apply on `SystemEvents.UserPreferenceChanged`.
- **U6 — Windows accent is made safe, not trusted.** `HighlightPalette.FromAccent` picks black or white text by contrast and darkens or lightens the fill until that text reaches 4.5:1; the text colour is pushed until it reaches 4.5:1 on the ground.
- **U7 — Settings stay strings.** `Theme` ("dark", "light", "system") and `Highlight` ("green", "blue", "red", "cognac", "windows") are strings in settings.json, parsed tolerantly, for the same reason as the sort (D30): a bad value must not reset every setting. Default: dark + green, so nothing changes for anyone until they choose.
- **U8 — No implicit `TextBlock` style.** The old one forced `Text.Primary` onto every TextBlock, which would put dark text on a green selected row in the Light theme. Windows set `Foreground`; everything inherits it.
- **U9 — Static font instances.** WPF does not apply variable-font weight axes, so the variable TASA Orbiter is instanced into Regular, Medium, SemiBold, Bold and ExtraBold TTFs with fontTools, and embedded as resources. IBM Plex Mono ships static TTFs already.
- **U10 — Stroke icons as `Geometry` resources**, drawn by a `Path` style that takes its stroke from the inherited `TextElement.Foreground`, so an icon inside a button follows the button's text colour.
- **U11 — WPF gaps accepted:** labels in capitals are typed in capitals (WPF has no text-transform) and have no letter-spacing (TextBlock has none); tables keep square corners (WPF has no cheap rounded clip). The Design System gets a note saying so (Task 21).
- **U12 — Recents calendar keeps 12px cells at a 14px pitch** (the Design System says 11/13) because `ActivityCalendar` computes the strip width and month markers from that pitch; changing it is not worth touching tested layout code.
- **U13 — "Recently Deleted" stays capitalised** as the name of a place in the app, like "Recents".

## 5. Tasks

### Task 1: Branch and baseline

**Files:** none

- [ ] **Step 1: Create the branch**

```bash
git -C C:/QuickerPlaces switch -c claude/ui-refresh
```

- [ ] **Step 2: Confirm the baseline**

Run: `dotnet build QuickerPlaces.sln -nologo -v q` then `dotnet test QuickerPlaces.Tests -nologo` (from `C:\QuickerPlaces\src`).
Expected: `0 Warning(s)`, `0 Error(s)`; `Passed: 547`.

- [ ] **Step 3: Commit this plan**

```bash
git -C C:/QuickerPlaces add "ai/260927_UI Refresh Detailed Plan.md"
git -C C:/QuickerPlaces commit -m "Add the UI refresh plan"
```

### Task 2: Theme and highlight settings

**Files:**
- Create: `src/QuickerPlaces/Models/ThemePreference.cs`
- Modify: `src/QuickerPlaces/Models/AppSettings.cs`
- Modify: `src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj` (link the new file)
- Create: `src/QuickerPlaces.Tests/ThemePreferenceTests.cs`
- Modify: `src/QuickerPlaces.Tests/SettingsServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

`src/QuickerPlaces.Tests/ThemePreferenceTests.cs`:

```csharp
using QuickerPlaces.Models;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>UI refresh U7: theme and highlight are tolerant strings in settings.json.</summary>
public sealed class ThemePreferenceTests
{
    [Theory]
    [InlineData("dark", AppTheme.Dark)]
    [InlineData("Light", AppTheme.Light)]
    [InlineData(" system ", AppTheme.System)]
    [InlineData(null, AppTheme.Dark)]
    [InlineData("", AppTheme.Dark)]
    [InlineData("purple", AppTheme.Dark)]
    [InlineData("1", AppTheme.Dark)]
    public void ParseTheme_FallsBackToDark(string? value, AppTheme expected)
        => Assert.Equal(expected, ThemePreference.ParseTheme(value));

    [Theory]
    [InlineData("green", HighlightPreset.Green)]
    [InlineData("BLUE", HighlightPreset.Blue)]
    [InlineData("red", HighlightPreset.Red)]
    [InlineData("cognac", HighlightPreset.Cognac)]
    [InlineData("windows", HighlightPreset.Windows)]
    [InlineData(null, HighlightPreset.Green)]
    [InlineData("teal", HighlightPreset.Green)]
    public void ParseHighlight_FallsBackToGreen(string? value, HighlightPreset expected)
        => Assert.Equal(expected, ThemePreference.ParseHighlight(value));

    [Theory]
    [InlineData(AppTheme.Dark, "dark")]
    [InlineData(AppTheme.Light, "light")]
    [InlineData(AppTheme.System, "system")]
    public void FormatTheme_RoundTrips(AppTheme theme, string text)
    {
        Assert.Equal(text, ThemePreference.Format(theme));
        Assert.Equal(theme, ThemePreference.ParseTheme(ThemePreference.Format(theme)));
    }

    [Theory]
    [InlineData(HighlightPreset.Green, "green")]
    [InlineData(HighlightPreset.Blue, "blue")]
    [InlineData(HighlightPreset.Red, "red")]
    [InlineData(HighlightPreset.Cognac, "cognac")]
    [InlineData(HighlightPreset.Windows, "windows")]
    public void FormatHighlight_RoundTrips(HighlightPreset preset, string text)
    {
        Assert.Equal(text, ThemePreference.Format(preset));
        Assert.Equal(preset, ThemePreference.ParseHighlight(ThemePreference.Format(preset)));
    }
}
```

Append to `src/QuickerPlaces.Tests/SettingsServiceTests.cs`, inside the class:

```csharp
    [Fact]
    public void Version_4_settings_load_with_the_default_theme_and_highlight()
    {
        File.WriteAllText(_temp.File("settings.json"),
            """{ "schemaVersion": 4, "globalHotkey": "Ctrl+Shift+Q", "minimizeToTray": true }""");

        var loaded = NewService().Load();

        Assert.Equal("Ctrl+Shift+Q", loaded.GlobalHotkey);
        Assert.True(loaded.MinimizeToTray);
        Assert.Equal("dark", loaded.Theme);
        Assert.Equal("green", loaded.Highlight);
    }

    [Fact]
    public void Theme_and_highlight_round_trip()
    {
        NewService().Save(new AppSettings { Theme = "system", Highlight = "cognac" });
        var loaded = NewService().Load();

        Assert.Equal("system", loaded.Theme);
        Assert.Equal("cognac", loaded.Highlight);
    }

    [Fact]
    public void An_unrecognised_theme_costs_only_the_theme()
    {
        File.WriteAllText(_temp.File("settings.json"),
            """{ "schemaVersion": 5, "globalHotkey": "Ctrl+Shift+Q", "theme": "neon", "highlight": "teal" }""");

        var loaded = NewService().Load();

        Assert.Equal("Ctrl+Shift+Q", loaded.GlobalHotkey);
        Assert.Equal(AppTheme.Dark, ThemePreference.ParseTheme(loaded.Theme));
        Assert.Equal(HighlightPreset.Green, ThemePreference.ParseHighlight(loaded.Highlight));
    }
```

- [ ] **Step 2: Link the new file and run the tests to see them fail**

Add to the first `<ItemGroup>` of `src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj`, after the `AppSettings.cs` line:

```xml
    <Compile Include="..\QuickerPlaces\Models\ThemePreference.cs" Link="Linked\Models\ThemePreference.cs" />
```

Run: `dotnet test QuickerPlaces.Tests -nologo`
Expected: build fails — `ThemePreference`, `AppTheme`, `AppSettings.Theme` not found.

- [ ] **Step 3: Implement**

`src/QuickerPlaces/Models/ThemePreference.cs`:

```csharp
using System;

namespace QuickerPlaces.Models;

/// <summary>Which palette the app uses. System follows the Windows app theme.</summary>
public enum AppTheme
{
    Dark,
    Light,
    System
}

/// <summary>
/// The user's highlight colour (the Design System's presets). Windows
/// follows the Windows accent colour, made readable by HighlightPalette.
/// </summary>
public enum HighlightPreset
{
    Green,
    Blue,
    Red,
    Cognac,
    Windows
}

/// <summary>
/// Reads and writes the theme and highlight strings in settings.json (UI
/// refresh U7). Tolerant like PlaceSort.Parse: anything unrecognised is the
/// default, so a hand-edited value can never cost the other settings.
/// </summary>
public static class ThemePreference
{
    public static AppTheme ParseTheme(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "light" => AppTheme.Light,
        "system" => AppTheme.System,
        _ => AppTheme.Dark
    };

    public static HighlightPreset ParseHighlight(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "blue" => HighlightPreset.Blue,
        "red" => HighlightPreset.Red,
        "cognac" => HighlightPreset.Cognac,
        "windows" => HighlightPreset.Windows,
        _ => HighlightPreset.Green
    };

    public static string Format(AppTheme theme) => theme switch
    {
        AppTheme.Light => "light",
        AppTheme.System => "system",
        _ => "dark"
    };

    public static string Format(HighlightPreset preset) => preset switch
    {
        HighlightPreset.Blue => "blue",
        HighlightPreset.Red => "red",
        HighlightPreset.Cognac => "cognac",
        HighlightPreset.Windows => "windows",
        _ => "green"
    };
}
```

In `src/QuickerPlaces/Models/AppSettings.cs`: extend the `CurrentSchemaVersion` doc comment and bump the constant, then add the two properties at the end of the class.

```csharp
    /// 4: added MinimizeToTray and StartWithWindows (Phase 9 D25), both off
    /// when absent from an older file.
    ///
    /// 5: added Theme and Highlight (UI refresh U7). No migration needed: a
    /// version-4 file lacks them, which reads as dark with Hull green, the
    /// look closest to what that version showed.
    /// </summary>
    public const int CurrentSchemaVersion = 5;
```

```csharp
    /// <summary>
    /// "dark", "light" or "system" (see <see cref="ThemePreference"/>). A
    /// string, not an enum, for the same reason as <see cref="PlacesSortKey"/>.
    /// </summary>
    public string? Theme { get; set; } = "dark";

    /// <summary>"green", "blue", "red", "cognac" or "windows" (see <see cref="ThemePreference"/>).</summary>
    public string? Highlight { get; set; } = "green";
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test QuickerPlaces.Tests -nologo`
Expected: `Failed: 0`, and the total up from 547.

- [ ] **Step 5: Commit**

```bash
git -C C:/QuickerPlaces add src/QuickerPlaces/Models src/QuickerPlaces.Tests
git -C C:/QuickerPlaces commit -m "Add theme and highlight settings (schema 5)"
```

### Task 3: Colour maths and highlight presets

**Files:**
- Create: `src/QuickerPlaces/Services/Theming/ThemeColor.cs`
- Create: `src/QuickerPlaces/Services/Theming/HighlightPalette.cs`
- Modify: `src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj`
- Create: `src/QuickerPlaces.Tests/ThemeColorTests.cs`
- Create: `src/QuickerPlaces.Tests/HighlightPaletteTests.cs`

- [ ] **Step 1: Write the failing tests**

`src/QuickerPlaces.Tests/ThemeColorTests.cs`:

```csharp
using System;
using QuickerPlaces.Services.Theming;
using Xunit;

namespace QuickerPlaces.Tests;

public sealed class ThemeColorTests
{
    [Fact]
    public void Parse_ReadsRgbAndArgb()
    {
        Assert.Equal(new ThemeColor(255, 0x12, 0x15, 0x14), ThemeColor.Parse("#121514"));
        Assert.Equal(new ThemeColor(0x24, 0x7C, 0xC7, 0xAD), ThemeColor.Parse("#247cc7ad"));
    }

    [Theory]
    [InlineData("121514")]
    [InlineData("#12151")]
    [InlineData("#GG1514")]
    [InlineData("")]
    public void Parse_RejectsOtherShapes(string text)
        => Assert.Throws<FormatException>(() => ThemeColor.Parse(text));

    [Fact]
    public void ToHex_RoundTrips()
    {
        Assert.Equal("#1F5C4D", ThemeColor.Parse("#1f5c4d").ToHex());
        Assert.Equal("#1A1F5C4D", ThemeColor.Parse("#1A1F5C4D").ToHex());
    }

    [Fact]
    public void Contrast_MatchesWcagReferencePoints()
    {
        Assert.Equal(21.0, ThemeColor.Contrast(ThemeColor.White, ThemeColor.Black), 2);
        Assert.Equal(1.0, ThemeColor.Contrast(ThemeColor.White, ThemeColor.White), 2);
        // #767676 on white is the classic 4.54:1.
        Assert.Equal(4.54, ThemeColor.Contrast(ThemeColor.Parse("#767676"), ThemeColor.White), 2);
    }

    [Fact]
    public void Mix_MovesTowardTheOtherColour()
    {
        var half = ThemeColor.Black.Mix(ThemeColor.White, 0.5);
        Assert.Equal(new ThemeColor(255, 128, 128, 128), half);
        Assert.Equal(ThemeColor.Black, ThemeColor.Black.Mix(ThemeColor.White, 0));
        Assert.Equal(ThemeColor.White, ThemeColor.Black.Mix(ThemeColor.White, 1));
    }
}
```

`src/QuickerPlaces.Tests/HighlightPaletteTests.cs`:

```csharp
using System.Collections.Generic;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Theming;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>UI refresh U6: every highlight is readable in both themes.</summary>
public sealed class HighlightPaletteTests
{
    public static IEnumerable<object[]> PresetsAndThemes()
    {
        foreach (var preset in new[] { HighlightPreset.Green, HighlightPreset.Blue, HighlightPreset.Red, HighlightPreset.Cognac })
        {
            yield return new object[] { preset, true };
            yield return new object[] { preset, false };
        }
    }

    [Theory]
    [MemberData(nameof(PresetsAndThemes))]
    public void Presets_KeepTextReadable(HighlightPreset preset, bool dark)
    {
        var colors = HighlightPalette.For(preset, dark, windowsAccent: null);

        Assert.True(ThemeColor.Contrast(colors.OnFill, colors.Fill) >= 4.5, $"On-fill text on {preset} ({(dark ? "dark" : "light")})");
        Assert.True(ThemeColor.Contrast(colors.Text, HighlightPalette.GroundFor(dark)) >= 4.5, $"{preset} text on the ground");
    }

    [Fact]
    public void HullGreen_MatchesTheDesignSystem()
    {
        var dark = HighlightPalette.For(HighlightPreset.Green, dark: true, windowsAccent: null);
        var light = HighlightPalette.For(HighlightPreset.Green, dark: false, windowsAccent: null);

        Assert.Equal("#2A7563", dark.Fill.ToHex());
        Assert.Equal("#33866F", dark.Hover.ToHex());
        Assert.Equal("#7CC7AD", dark.Text.ToHex());
        Assert.Equal("#247CC7AD", dark.Soft.ToHex());
        Assert.Equal("#1F5C4D", light.Fill.ToHex());
        Assert.Equal("#174A3E", light.Hover.ToHex());
        Assert.Equal("#1F6B58", light.Text.ToHex());
        Assert.Equal("#1A1F5C4D", light.Soft.ToHex());
        Assert.Equal(ThemeColor.White, dark.OnFill);
    }

    [Fact]
    public void WindowsPreset_WithoutAnAccent_IsHullGreen()
        => Assert.Equal(HighlightPalette.For(HighlightPreset.Green, true, null),
                        HighlightPalette.For(HighlightPreset.Windows, true, null));

    [Theory]
    [InlineData("#FFD800", true)]   // bright yellow
    [InlineData("#FFD800", false)]
    [InlineData("#0078D4", true)]   // Windows default blue
    [InlineData("#0078D4", false)]
    [InlineData("#1A2B4C", true)]   // navy: too dark as text on the dark ground
    [InlineData("#F4C2C2", false)]  // pastel pink: too light as text on the light ground
    [InlineData("#808080", true)]   // mid grey: black text, and a text colour that needs lightening
    public void FromAccent_IsAlwaysReadable(string accent, bool dark)
    {
        var colors = HighlightPalette.For(HighlightPreset.Windows, dark, ThemeColor.Parse(accent));

        Assert.True(ThemeColor.Contrast(colors.OnFill, colors.Fill) >= 4.5, "text on the fill");
        Assert.True(ThemeColor.Contrast(colors.Text, HighlightPalette.GroundFor(dark)) >= 4.5, "text on the ground");
        Assert.Equal(255, colors.Fill.A);
    }

    [Fact]
    public void FromAccent_UsesBlackTextOnALightAccent()
        => Assert.Equal(ThemeColor.Black, HighlightPalette.For(HighlightPreset.Windows, true, ThemeColor.Parse("#FFD800")).OnFill);
}
```

- [ ] **Step 2: Link the files and run the tests to see them fail**

Add to the test csproj's first `<ItemGroup>`:

```xml
    <Compile Include="..\QuickerPlaces\Services\Theming\ThemeColor.cs" Link="Linked\Services\Theming\ThemeColor.cs" />
    <Compile Include="..\QuickerPlaces\Services\Theming\HighlightPalette.cs" Link="Linked\Services\Theming\HighlightPalette.cs" />
```

Run: `dotnet test QuickerPlaces.Tests -nologo`
Expected: build fails — `ThemeColor` and `HighlightPalette` not found.

- [ ] **Step 3: Implement**

`src/QuickerPlaces/Services/Theming/ThemeColor.cs`:

```csharp
using System;
using System.Globalization;

namespace QuickerPlaces.Services.Theming;

/// <summary>
/// A colour as the theme code reasons about it, free of System.Windows so the
/// contrast rules can be unit-tested (D5). Parses WPF's "#RRGGBB" and
/// "#AARRGGBB" forms; contrast follows WCAG 2 and ignores alpha.
/// </summary>
public readonly record struct ThemeColor(byte A, byte R, byte G, byte B)
{
    public static readonly ThemeColor White = new(255, 255, 255, 255);
    public static readonly ThemeColor Black = new(255, 0, 0, 0);

    public static ThemeColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    public static ThemeColor Parse(string text)
    {
        if (text is null || text.Length is not (7 or 9) || text[0] != '#')
            throw new FormatException($"Expected #RRGGBB or #AARRGGBB, got \"{text}\".");

        if (!uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"\"{text}\" is not a hex colour.");

        return text.Length == 7
            ? new ThemeColor(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : new ThemeColor((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    public string ToHex() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    public ThemeColor WithAlpha(byte alpha) => this with { A = alpha };

    /// <summary>WCAG relative luminance, 0 (black) to 1 (white).</summary>
    public double RelativeLuminance => 0.2126 * Channel(R) + 0.7152 * Channel(G) + 0.0722 * Channel(B);

    /// <summary>WCAG contrast ratio, 1 to 21.</summary>
    public static double Contrast(ThemeColor a, ThemeColor b)
    {
        var (light, dark) = a.RelativeLuminance >= b.RelativeLuminance
            ? (a.RelativeLuminance, b.RelativeLuminance)
            : (b.RelativeLuminance, a.RelativeLuminance);
        return (light + 0.05) / (dark + 0.05);
    }

    /// <summary>An opaque colour <paramref name="amount"/> (0 to 1) of the way to <paramref name="other"/>.</summary>
    public ThemeColor Mix(ThemeColor other, double amount) => new(
        255,
        Lerp(R, other.R, amount),
        Lerp(G, other.G, amount),
        Lerp(B, other.B, amount));

    private static byte Lerp(byte from, byte to, double amount)
        => (byte)Math.Round(from + (to - from) * Math.Clamp(amount, 0, 1), MidpointRounding.AwayFromZero);

    private static double Channel(byte value)
    {
        var c = value / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
```

`src/QuickerPlaces/Services/Theming/HighlightPalette.cs`:

```csharp
using QuickerPlaces.Models;

namespace QuickerPlaces.Services.Theming;

/// <summary>The five colours a highlight needs, as the Design System names them.</summary>
public sealed record HighlightColors(ThemeColor Fill, ThemeColor Hover, ThemeColor Text, ThemeColor Soft, ThemeColor OnFill);

/// <summary>
/// The highlight presets from the QuickerPlaces Design System (version 2),
/// and the rule that makes the Windows accent colour safe to use (UI
/// refresh U6). The ground colours must match Bg.Base in the palette files;
/// PaletteFileTests checks that.
/// </summary>
public static class HighlightPalette
{
    public static readonly ThemeColor DarkGround = ThemeColor.Parse("#121514");
    public static readonly ThemeColor LightGround = ThemeColor.Parse("#ECEEED");

    private const byte DarkSoftAlpha = 0x24;  // 14%
    private const byte LightSoftAlpha = 0x1A; // 10%
    private const double MinimumContrast = 4.5;

    public static ThemeColor GroundFor(bool dark) => dark ? DarkGround : LightGround;

    public static HighlightColors For(HighlightPreset preset, bool dark, ThemeColor? windowsAccent) => preset switch
    {
        HighlightPreset.Blue => dark
            ? Preset("#36679A", "#4077AE", "#9CC0E6", dark)
            : Preset("#2F5D8A", "#264D73", "#2C5A86", dark),
        HighlightPreset.Red => dark
            ? Preset("#B1372F", "#C4453B", "#F08F84", dark)
            : Preset("#A8322B", "#8F2923", "#A8322B", dark),
        HighlightPreset.Cognac => dark
            ? Preset("#8F5A33", "#A0683E", "#DCAA7D", dark)
            : Preset("#8A5530", "#734526", "#86522E", dark),
        HighlightPreset.Windows when windowsAccent is { } accent => FromAccent(accent, dark),
        _ => dark
            ? Preset("#2A7563", "#33866F", "#7CC7AD", dark)
            : Preset("#1F5C4D", "#174A3E", "#1F6B58", dark)
    };

    /// <summary>
    /// Turns any accent into a readable highlight: black or white text,
    /// whichever contrasts more, with the fill pushed away from that text
    /// until it reaches 4.5:1; and a text colour pushed toward the theme's
    /// own text until it reaches 4.5:1 on the ground.
    /// </summary>
    public static HighlightColors FromAccent(ThemeColor accent, bool dark)
    {
        var fill = accent.WithAlpha(255);
        var onFill = ThemeColor.Contrast(fill, ThemeColor.White) >= ThemeColor.Contrast(fill, ThemeColor.Black)
            ? ThemeColor.White
            : ThemeColor.Black;
        var awayFromText = onFill == ThemeColor.White ? ThemeColor.Black : ThemeColor.White;
        fill = PushUntil(fill, awayFromText, onFill);

        var ground = GroundFor(dark);
        var text = PushUntil(accent.WithAlpha(255), dark ? ThemeColor.White : ThemeColor.Black, ground);

        var hover = fill.Mix(awayFromText, 0.12);
        var soft = dark ? text.WithAlpha(DarkSoftAlpha) : fill.WithAlpha(LightSoftAlpha);
        return new HighlightColors(fill, hover, text, soft, onFill);
    }

    private static HighlightColors Preset(string fill, string hover, string text, bool dark)
    {
        var fillColor = ThemeColor.Parse(fill);
        var textColor = ThemeColor.Parse(text);
        var soft = dark ? textColor.WithAlpha(DarkSoftAlpha) : fillColor.WithAlpha(LightSoftAlpha);
        return new HighlightColors(fillColor, ThemeColor.Parse(hover), textColor, soft, ThemeColor.White);
    }

    /// <summary>Moves <paramref name="color"/> 5% at a time toward <paramref name="toward"/> until it contrasts 4.5:1 with <paramref name="against"/>.</summary>
    private static ThemeColor PushUntil(ThemeColor color, ThemeColor toward, ThemeColor against)
    {
        for (var step = 0; step < 60 && ThemeColor.Contrast(color, against) < MinimumContrast; step++)
            color = color.Mix(toward, 0.05);
        return color;
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test QuickerPlaces.Tests -nologo`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git -C C:/QuickerPlaces add src/QuickerPlaces/Services/Theming src/QuickerPlaces.Tests
git -C C:/QuickerPlaces commit -m "Add highlight presets and readable Windows accent derivation"
```

### Task 4: Palette files and their consistency test

**Files:**
- Create: `src/QuickerPlaces/Resources/Palette.Dark.xaml`
- Create: `src/QuickerPlaces/Resources/Palette.Light.xaml`
- Modify: `src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj`
- Create: `src/QuickerPlaces.Tests/PaletteFileTests.cs`

- [ ] **Step 1: Write the failing test**

`src/QuickerPlaces.Tests/PaletteFileTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Theming;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// UI refresh U1: the two palette files define the same keys, and their
/// colours keep the contrast the Design System promises. Reads the XAML as
/// XML (copied next to the test assembly), so no WPF is needed.
/// </summary>
public sealed class PaletteFileTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static Dictionary<string, ThemeColor> Load(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Palettes", name);
        return XDocument.Load(path).Root!
            .Elements()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .ToDictionary(e => (string)e.Attribute(Xaml + "Key")!, e => ThemeColor.Parse((string)e.Attribute("Color")!));
    }

    private static readonly Dictionary<string, ThemeColor> Dark = Load("Palette.Dark.xaml");
    private static readonly Dictionary<string, ThemeColor> Light = Load("Palette.Light.xaml");

    public static IEnumerable<object[]> Themes() => new[] { new object[] { true }, new object[] { false } };

    private static Dictionary<string, ThemeColor> For(bool dark) => dark ? Dark : Light;

    [Fact]
    public void BothPalettesDefineTheSameKeys()
        => Assert.Equal(Dark.Keys.OrderBy(k => k), Light.Keys.OrderBy(k => k));

    [Theory]
    [MemberData(nameof(Themes))]
    public void TheGroundMatchesHighlightPalette(bool dark)
        => Assert.Equal(HighlightPalette.GroundFor(dark), For(dark)["Bg.Base"]);

    [Theory]
    [MemberData(nameof(Themes))]
    public void TheHighlightDefaultsAreHullGreen(bool dark)
    {
        var green = HighlightPalette.For(HighlightPreset.Green, dark, null);
        var palette = For(dark);
        Assert.Equal(green.Fill, palette["Highlight"]);
        Assert.Equal(green.Hover, palette["Highlight.Hover"]);
        Assert.Equal(green.Text, palette["Highlight.Text"]);
        Assert.Equal(green.Soft, palette["Highlight.Soft"]);
        Assert.Equal(green.OnFill, palette["On.Highlight"]);
        Assert.Equal(green.Fill, palette["Preset.Green"]);
    }

    [Theory]
    [InlineData(true, "Text.Primary", "Bg.Base")]
    [InlineData(true, "Text.Primary", "Bg.Panel")]
    [InlineData(true, "Text.Primary", "Bg.Raised")]
    [InlineData(true, "Text.Primary", "Bg.Sunken")]
    [InlineData(true, "Text.Secondary", "Bg.Base")]
    [InlineData(true, "Text.Secondary", "Bg.Panel")]
    [InlineData(true, "Text.Secondary", "Bg.Raised")]
    [InlineData(true, "Text.Tertiary", "Bg.Base")]
    [InlineData(true, "Danger", "Bg.Base")]
    [InlineData(true, "Signal", "Bg.Base")]
    [InlineData(true, "On.Leather", "Leather")]
    [InlineData(true, "On.Leather.Badge", "Leather.Deep")]
    [InlineData(false, "Text.Primary", "Bg.Base")]
    [InlineData(false, "Text.Primary", "Bg.Panel")]
    [InlineData(false, "Text.Primary", "Bg.Raised")]
    [InlineData(false, "Text.Primary", "Bg.Sunken")]
    [InlineData(false, "Text.Secondary", "Bg.Base")]
    [InlineData(false, "Text.Secondary", "Bg.Panel")]
    [InlineData(false, "Text.Secondary", "Bg.Raised")]
    [InlineData(false, "Text.Tertiary", "Bg.Base")]
    [InlineData(false, "Text.Tertiary", "Bg.Raised")]
    [InlineData(false, "Danger", "Bg.Raised")]
    [InlineData(false, "On.Leather", "Leather")]
    [InlineData(false, "On.Leather.Badge", "Leather.Deep")]
    public void TextKeepsFourPointFiveToOne(bool dark, string text, string ground)
    {
        var palette = For(dark);
        var ratio = ThemeColor.Contrast(palette[text], palette[ground]);
        Assert.True(ratio >= 4.5, $"{text} on {ground} ({(dark ? "dark" : "light")}) is {ratio:0.00}:1");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SignalIsAtLeastThreeToOneOnRaised(bool dark)
    {
        var palette = For(dark);
        Assert.True(ThemeColor.Contrast(palette["Signal"], palette["Bg.Raised"]) >= 3.0);
    }
}
```

- [ ] **Step 2: Copy the palette files into the test output and run the test to see it fail**

Add a new `<ItemGroup>` to the test csproj:

```xml
  <ItemGroup>
    <!--
      UI refresh U1: PaletteFileTests reads the app's palette XAML as plain
      XML, so both files are copied next to the test assembly.
    -->
    <None Include="..\QuickerPlaces\Resources\Palette.Dark.xaml" Link="Palettes\Palette.Dark.xaml" CopyToOutputDirectory="PreserveNewest" />
    <None Include="..\QuickerPlaces\Resources\Palette.Light.xaml" Link="Palettes\Palette.Light.xaml" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

Run: `dotnet test QuickerPlaces.Tests -nologo`
Expected: FAIL — the palette files don't exist yet (build error for the missing `None` items, or `FileNotFoundException`).

- [ ] **Step 3: Create the palettes**

`src/QuickerPlaces/Resources/Palette.Dark.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!--
        Dark theme colours (the dash). Keys follow the QuickerPlaces Design
        System, version 2: its Bg-Base is Bg.Base here. Palette.Light.xaml
        defines exactly the same keys, and PaletteFileTests enforces that and
        the contrast floors. Refer to these with DynamicResource only, so a
        theme switch repaints open windows (UI refresh U2). The Highlight
        entries are the Hull green defaults; ThemeManager overrides them at
        the top of Application.Resources (U3).
    -->
    <SolidColorBrush x:Key="Bg.Base" Color="#FF121514" />
    <SolidColorBrush x:Key="Bg.Panel" Color="#FF171B1A" />
    <SolidColorBrush x:Key="Bg.Raised" Color="#FF1F2422" />
    <SolidColorBrush x:Key="Bg.Sunken" Color="#FF0E1110" />
    <SolidColorBrush x:Key="Bg.RowHover" Color="#FF1B201F" />
    <SolidColorBrush x:Key="Border.Default" Color="#FF2A302E" />
    <SolidColorBrush x:Key="Border.Strong" Color="#FF3D4542" />
    <SolidColorBrush x:Key="Text.Primary" Color="#FFEBE9E3" />
    <SolidColorBrush x:Key="Text.Secondary" Color="#FFA7ADAA" />
    <SolidColorBrush x:Key="Text.Tertiary" Color="#FF7F8683" />
    <SolidColorBrush x:Key="Trim.Band" Color="#FF080A09" />
    <SolidColorBrush x:Key="Trim.Line" Color="#FFAEB4B1" />
    <SolidColorBrush x:Key="Brand.Green" Color="#FF1F5C4D" />
    <SolidColorBrush x:Key="On.Brand" Color="#FFF2F1EC" />
    <SolidColorBrush x:Key="Signal" Color="#FFF0902F" />
    <SolidColorBrush x:Key="Danger" Color="#FFEC6A5E" />
    <SolidColorBrush x:Key="Info" Color="#FF9CC0E6" />
    <SolidColorBrush x:Key="Leather" Color="#FFC28A5C" />
    <SolidColorBrush x:Key="Leather.Deep" Color="#FF7A4C2B" />
    <SolidColorBrush x:Key="On.Leather" Color="#FF26170D" />
    <SolidColorBrush x:Key="On.Leather.Badge" Color="#FFF6E7D6" />
    <SolidColorBrush x:Key="Heat.0" Color="#FF1C211F" />
    <SolidColorBrush x:Key="Heat.1" Color="#FF3A2E24" />
    <SolidColorBrush x:Key="Heat.2" Color="#FF6A4930" />
    <SolidColorBrush x:Key="Heat.3" Color="#FFA06A40" />
    <SolidColorBrush x:Key="Heat.4" Color="#FFD9A068" />
    <SolidColorBrush x:Key="Heat.Untracked" Color="#FF191D1C" />
    <SolidColorBrush x:Key="Preset.Green" Color="#FF2A7563" />
    <SolidColorBrush x:Key="Preset.Blue" Color="#FF36679A" />
    <SolidColorBrush x:Key="Preset.Red" Color="#FFB1372F" />
    <SolidColorBrush x:Key="Preset.Cognac" Color="#FF8F5A33" />
    <SolidColorBrush x:Key="Highlight" Color="#FF2A7563" />
    <SolidColorBrush x:Key="Highlight.Hover" Color="#FF33866F" />
    <SolidColorBrush x:Key="Highlight.Text" Color="#FF7CC7AD" />
    <SolidColorBrush x:Key="Highlight.Soft" Color="#247CC7AD" />
    <SolidColorBrush x:Key="On.Highlight" Color="#FFFFFFFF" />
</ResourceDictionary>
```

`src/QuickerPlaces/Resources/Palette.Light.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!--
        Light theme colours (the studio). Same keys as Palette.Dark.xaml; see
        the note there.
    -->
    <SolidColorBrush x:Key="Bg.Base" Color="#FFECEEED" />
    <SolidColorBrush x:Key="Bg.Panel" Color="#FFF7F8F7" />
    <SolidColorBrush x:Key="Bg.Raised" Color="#FFFFFFFF" />
    <SolidColorBrush x:Key="Bg.Sunken" Color="#FFF4F5F4" />
    <SolidColorBrush x:Key="Bg.RowHover" Color="#FFEEF1F0" />
    <SolidColorBrush x:Key="Border.Default" Color="#FFD8DCDA" />
    <SolidColorBrush x:Key="Border.Strong" Color="#FFB3BAB7" />
    <SolidColorBrush x:Key="Text.Primary" Color="#FF161A19" />
    <SolidColorBrush x:Key="Text.Secondary" Color="#FF515956" />
    <SolidColorBrush x:Key="Text.Tertiary" Color="#FF646C69" />
    <SolidColorBrush x:Key="Trim.Band" Color="#FF262B29" />
    <SolidColorBrush x:Key="Trim.Line" Color="#FFC6CBC8" />
    <SolidColorBrush x:Key="Brand.Green" Color="#FF1F5C4D" />
    <SolidColorBrush x:Key="On.Brand" Color="#FFF2F1EC" />
    <SolidColorBrush x:Key="Signal" Color="#FFC25E0C" />
    <SolidColorBrush x:Key="Danger" Color="#FFB3261E" />
    <SolidColorBrush x:Key="Info" Color="#FF2C5A86" />
    <SolidColorBrush x:Key="Leather" Color="#FFC28A5C" />
    <SolidColorBrush x:Key="Leather.Deep" Color="#FF8E5C36" />
    <SolidColorBrush x:Key="On.Leather" Color="#FF26170D" />
    <SolidColorBrush x:Key="On.Leather.Badge" Color="#FFF6E7D6" />
    <SolidColorBrush x:Key="Heat.0" Color="#FFFFFFFF" />
    <SolidColorBrush x:Key="Heat.1" Color="#FFF0E0CF" />
    <SolidColorBrush x:Key="Heat.2" Color="#FFDDB48D" />
    <SolidColorBrush x:Key="Heat.3" Color="#FFB97F4D" />
    <SolidColorBrush x:Key="Heat.4" Color="#FF84512C" />
    <SolidColorBrush x:Key="Heat.Untracked" Color="#FFE2E5E4" />
    <SolidColorBrush x:Key="Preset.Green" Color="#FF1F5C4D" />
    <SolidColorBrush x:Key="Preset.Blue" Color="#FF2F5D8A" />
    <SolidColorBrush x:Key="Preset.Red" Color="#FFA8322B" />
    <SolidColorBrush x:Key="Preset.Cognac" Color="#FF8A5530" />
    <SolidColorBrush x:Key="Highlight" Color="#FF1F5C4D" />
    <SolidColorBrush x:Key="Highlight.Hover" Color="#FF174A3E" />
    <SolidColorBrush x:Key="Highlight.Text" Color="#FF1F6B58" />
    <SolidColorBrush x:Key="Highlight.Soft" Color="#1A1F5C4D" />
    <SolidColorBrush x:Key="On.Highlight" Color="#FFFFFFFF" />
</ResourceDictionary>
```

These files are compiled as `Page` items automatically (WPF SDK default), so no csproj change is needed in the app project.

- [ ] **Step 4: Run the tests**

Run: `dotnet test QuickerPlaces.Tests -nologo`
Expected: `Failed: 0`. If a contrast row fails, the palette value is wrong against the Design System: fix the value in both the XAML and the Design System (Task 21), never the threshold.

- [ ] **Step 5: Commit**

```bash
git -C C:/QuickerPlaces add src/QuickerPlaces/Resources/Palette.*.xaml src/QuickerPlaces.Tests
git -C C:/QuickerPlaces commit -m "Add dark and light palettes with a consistency test"
```

### Task 5: Places grid sort without the Type column

**Files:**
- Modify: `src/QuickerPlaces/Services/PlaceSort.cs`
- Modify: `src/QuickerPlaces/ViewModels/MainViewModel.cs:88`
- Modify: `src/QuickerPlaces.Tests/PlaceSortTests.cs`

- [ ] **Step 1: Write the failing test** (append inside `PlaceSortTests`)

```csharp
    /// <summary>
    /// UI refresh: the places grid has no Type column any more, so a Type
    /// sort remembered from an older version would show no header arrow.
    /// It becomes the stored order; every other sort survives.
    /// </summary>
    [Fact]
    public void ForPlacesGrid_DropsATypeSort()
    {
        Assert.Null(PlaceSort.ForPlacesGrid(new PlaceSort(PlaceSortKey.Type, ListSortDirection.Ascending)));
        Assert.Null(PlaceSort.ForPlacesGrid(null));
        var alias = new PlaceSort(PlaceSortKey.Alias, ListSortDirection.Descending);
        Assert.Equal(alias, PlaceSort.ForPlacesGrid(alias));
    }
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test QuickerPlaces.Tests -nologo --filter ForPlacesGrid_DropsATypeSort`
Expected: build error, `ForPlacesGrid` not defined.

- [ ] **Step 3: Implement** — add to `PlaceSort` in `src/QuickerPlaces/Services/PlaceSort.cs`, after `Parse`:

```csharp
    /// <summary>
    /// The sort the places grid can show: Type has no column there since the
    /// UI refresh (the type icon shows it), so a remembered Type sort is the
    /// stored order instead.
    /// </summary>
    public static PlaceSort? ForPlacesGrid(PlaceSort? sort) => sort?.Key == PlaceSortKey.Type ? null : sort;
```

In `src/QuickerPlaces/ViewModels/MainViewModel.cs`, replace line 88:

```csharp
        _currentSort = PlaceSort.ForPlacesGrid(PlaceSort.Parse(settings.PlacesSortKey, settings.PlacesSortDirection));
```

- [ ] **Step 4: Run the tests** — `dotnet test QuickerPlaces.Tests -nologo`. Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git -C C:/QuickerPlaces commit -am "Drop a remembered Type sort from the places grid"
```

### Task 6: View models lose glyph strings, gain the favourite shortcut

**Files:**
- Modify: `src/QuickerPlaces/ViewModels/PlaceViewModel.cs`
- Modify: `src/QuickerPlaces/ViewModels/RecentlyDeletedRowViewModel.cs`
- Modify: `src/QuickerPlaces/ViewModels/SelectablePlaceViewModel.cs`
- Modify: `src/QuickerPlaces.Tests/PlaceViewModelTests.cs`
- Modify: `src/QuickerPlaces.Tests/RecentlyDeletedRowViewModelTests.cs`

- [ ] **Step 1: Rewrite the tests**

In `PlaceViewModelTests.cs`, replace `FavouriteStar_ShowsTheStateAndWhatAClickDoes` with:

```csharp
    /// <summary>The grid's star tooltip says what a click will do (the star itself is drawn by the view).</summary>
    [Theory]
    [InlineData(true, "Remove from favourites (Ctrl+D)")]
    [InlineData(false, "Add to favourites (Ctrl+D)")]
    public void FavouriteStar_ToolTipSaysWhatAClickDoes(bool isFavourite, string toolTip)
    {
        var place = new PlaceViewModel(new QuickerPlaces.Models.Place { Alias = "Docs", Resource = "https://docs.example.com", IsFavourite = isFavourite });

        Assert.Equal(toolTip, place.FavouriteToolTip);
    }

    /// <summary>A favourite card shows its Ctrl+number; only the first nine have one.</summary>
    [Theory]
    [InlineData(true, 0, "1")]
    [InlineData(true, 8, "9")]
    [InlineData(true, 9, null)]
    [InlineData(false, null, null)]
    public void FavouriteShortcut_IsTheCtrlNumber(bool isFavourite, int? order, string? expected)
    {
        var place = new PlaceViewModel(new QuickerPlaces.Models.Place { Alias = "Docs", Resource = @"C:\Docs", IsFavourite = isFavourite, FavouriteOrder = order });

        Assert.Equal(expected, place.FavouriteShortcut);
    }

    [Fact]
    public void ToolTipText_IsTheDestination()
    {
        var place = new PlaceViewModel(new QuickerPlaces.Models.Place { Alias = "Docs", Resource = @"C:\Docs" });

        Assert.Equal(@"C:\Docs", place.ToolTipText);
    }
```

In `RecentlyDeletedRowViewModelTests.cs`, replace the two `TypeGlyph` assertions (lines 37 and 41) with:

```csharp
        Assert.Equal(PlaceType.Folder, folder.Type);
```

```csharp
        Assert.Equal(PlaceType.Url, url.Type);
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test QuickerPlaces.Tests -nologo`
Expected: build errors — `FavouriteShortcut` and `RecentlyDeletedRowViewModel.Type` not defined.

- [ ] **Step 3: Implement**

In `PlaceViewModel.cs`:
- Delete `TypeGlyph` (and its doc comment), `GlyphFor` (and its comment) and `FavouriteGlyph` (and its comment).
- Replace the `TypeLabel` comment with `/// <summary>"Folder" or "URL" — for the export and import lists and the Type sort.</summary>`.
- Replace `ToolTipText`:

```csharp
    /// <summary>Hover text for a favourite card: its folder or link, since the card only shows the name.</summary>
    public string ToolTipText => Model.Resource;
```

- Add after `FavouriteOrder`:

```csharp
    /// <summary>
    /// The number on a favourite card: its Ctrl+number shortcut ("1" to "9"),
    /// or null past the ninth or for a non-favourite. FavouriteOrder is
    /// contiguous from 0 (PlacesService.RenumberFavourites), and Refresh()
    /// raises this along with everything else.
    /// </summary>
    public string? FavouriteShortcut => Model.IsFavourite && Model.FavouriteOrder is >= 0 and < 9
        ? (Model.FavouriteOrder.Value + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
        : null;
```

In `RecentlyDeletedRowViewModel.cs`, replace line 51 (`TypeGlyph`) with:

```csharp
    /// <summary>Folder or Url: picks the row's icon.</summary>
    public PlaceType Type => Place.Type;
```

In `SelectablePlaceViewModel.cs`, add beside `TypeLabel` (line 26):

```csharp
    /// <summary>Folder or Url: picks the row's icon.</summary>
    public PlaceType Type => Place.Type;
```

Then confirm `MainViewModel.RebuildFavourites()` calls `Refresh()` on the favourites it lists (search the method for `Refresh`). If it does not, add as its last line:

```csharp
        foreach (var favourite in FavouritePlaces)
            favourite.Refresh();
```

- [ ] **Step 4: Run the tests and the build** — `dotnet test QuickerPlaces.Tests -nologo` → `Failed: 0`; `dotnet build QuickerPlaces.sln -nologo -v q` → clean. Views still bind the removed glyph properties; bindings are only checked at runtime, so this builds, and Tasks 12 and 15 replace those bindings.

- [ ] **Step 5: Commit**

```bash
git -C C:/QuickerPlaces commit -am "Replace glyph strings with the place type and favourite shortcut"
```

### Task 7: Fonts

**Files:**
- Create: `src/QuickerPlaces/Resources/Fonts/TASAOrbiter-{Regular,Medium,SemiBold,Bold,ExtraBold}.ttf`
- Create: `src/QuickerPlaces/Resources/Fonts/IBMPlexMono-{Regular,Medium}.ttf`
- Create: `src/QuickerPlaces/Resources/Fonts/OFL-TASAOrbiter.txt`, `OFL-IBMPlexMono.txt`
- Modify: `src/QuickerPlaces/QuickerPlaces.csproj`

- [ ] **Step 1: Ask the user before downloading.** Files: `TASAOrbiter[wght].ttf` and `OFL.txt` from `github.com/google/fonts` (`ofl/tasaorbiter/`, about 100 KB); `IBMPlexMono-Regular.ttf`, `IBMPlexMono-Medium.ttf` and `LICENSE.txt` from `github.com/IBM/plex` (`packages/plex-mono/`, about 300 KB); the `fonttools` Python package from PyPI.

- [ ] **Step 2: Download and instance** (PowerShell)

```powershell
$dest = "C:\QuickerPlaces\src\QuickerPlaces\Resources\Fonts"
New-Item -ItemType Directory -Force $dest | Out-Null
$tmp = Join-Path $env:TEMP "qp-fonts"; New-Item -ItemType Directory -Force $tmp | Out-Null
Invoke-WebRequest "https://raw.githubusercontent.com/google/fonts/main/ofl/tasaorbiter/TASAOrbiter%5Bwght%5D.ttf" -OutFile "$tmp\TASAOrbiter-VF.ttf"
Invoke-WebRequest "https://raw.githubusercontent.com/google/fonts/main/ofl/tasaorbiter/OFL.txt" -OutFile "$dest\OFL-TASAOrbiter.txt"
Invoke-WebRequest "https://raw.githubusercontent.com/IBM/plex/master/packages/plex-mono/fonts/complete/ttf/IBMPlexMono-Regular.ttf" -OutFile "$dest\IBMPlexMono-Regular.ttf"
Invoke-WebRequest "https://raw.githubusercontent.com/IBM/plex/master/packages/plex-mono/fonts/complete/ttf/IBMPlexMono-Medium.ttf" -OutFile "$dest\IBMPlexMono-Medium.ttf"
Invoke-WebRequest "https://raw.githubusercontent.com/IBM/plex/master/packages/plex-mono/LICENSE.txt" -OutFile "$dest\OFL-IBMPlexMono.txt"
python -m pip install --user --quiet fonttools
foreach ($w in @(@('Regular',400), @('Medium',500), @('SemiBold',600), @('Bold',700), @('ExtraBold',800))) {
    python -m fontTools.varLib.instancer "$tmp\TASAOrbiter-VF.ttf" "wght=$($w[1])" --update-name-table -o "$dest\TASAOrbiter-$($w[0]).ttf"
}
```

If a URL returns 404, the repository layout moved: find the file in that repository's file tree and use its raw URL. Never substitute a different font.

- [ ] **Step 3: Verify the names WPF will group by**

```powershell
python -c "from fontTools.ttLib import TTFont; import glob; [print(f.split('\\')[-1], '|', TTFont(f)['name'].getDebugName(16) or TTFont(f)['name'].getDebugName(1), '|', TTFont(f)['OS/2'].usWeightClass) for f in sorted(glob.glob(r'C:\QuickerPlaces\src\QuickerPlaces\Resources\Fonts\*.ttf'))]"
```

Expected: every TASA file reports family `TASA Orbiter` with weights 400, 500, 600, 700, 800; both Plex files report `IBM Plex Mono` with 400 and 500.

- [ ] **Step 4: Embed them** — add to `src/QuickerPlaces/QuickerPlaces.csproj`, in the `<ItemGroup>` that holds `AppIcon.ico`:

```xml
    <!--
      UI refresh U9: static instances of TASA Orbiter (WPF ignores variable
      weight axes) and IBM Plex Mono, both under the SIL Open Font Licence
      (OFL-*.txt beside them). Styles.xaml reaches them with
      pack://application:,,,/Resources/Fonts/#TASA Orbiter.
    -->
    <Resource Include="Resources\Fonts\*.ttf" />
    <None Include="Resources\Fonts\OFL-*.txt" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 5: Commit**

```bash
git -C C:/QuickerPlaces add src/QuickerPlaces/Resources/Fonts src/QuickerPlaces/QuickerPlaces.csproj
git -C C:/QuickerPlaces commit -m "Embed TASA Orbiter and IBM Plex Mono"
```

### Task 8: Icons

**Files:**
- Create: `src/QuickerPlaces/Resources/Icons.xaml`

- [ ] **Step 1: Create the geometries** (16 × 16 grid, drawn with a 1.6 stroke by the `Icon` style in Task 9; the shapes match the canvas mockups)

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!--
        Stroke icons on a 16 x 16 grid (UI refresh U10), replacing the Segoe
        icon font. Draw them with Style="{StaticResource Icon}" (Styles.xaml),
        which strokes them in the inherited text colour.
    -->
    <Geometry x:Key="Icon.Folder">M2.5,4.5 L6.5,4.5 L8,6 L13.5,6 L13.5,12 A1,1 0 0 1 12.5,13 L3.5,13 A1,1 0 0 1 2.5,12 Z</Geometry>
    <Geometry x:Key="Icon.Globe">M2.5,8 A5.5,5.5 0 1 1 13.5,8 A5.5,5.5 0 1 1 2.5,8 Z M2.5,8 L13.5,8 M8,2.5 C9.6,4.1 10.4,5.9 10.4,8 C10.4,10.1 9.6,11.9 8,13.5 M8,2.5 C6.4,4.1 5.6,5.9 5.6,8 C5.6,10.1 6.4,11.9 8,13.5</Geometry>
    <Geometry x:Key="Icon.Star">M8,2.2 L9.75,5.8 L13.7,6.35 L10.85,9.15 L11.52,13.1 L8,11.25 L4.48,13.1 L5.15,9.15 L2.3,6.35 L6.25,5.8 Z</Geometry>
    <Geometry x:Key="Icon.Search">M2.5,7 A4.5,4.5 0 1 1 11.5,7 A4.5,4.5 0 1 1 2.5,7 Z M10.5,10.5 L13.5,13.5</Geometry>
    <Geometry x:Key="Icon.Clock">M2.5,8 A5.5,5.5 0 1 1 13.5,8 A5.5,5.5 0 1 1 2.5,8 Z M8,5 L8,8.2 L10,9.5</Geometry>
    <Geometry x:Key="Icon.Menu">M3,4.5 L13,4.5 M3,8 L13,8 M3,11.5 L13,11.5</Geometry>
    <Geometry x:Key="Icon.Close">M4,4 L12,12 M12,4 L4,12</Geometry>
    <Geometry x:Key="Icon.Plus">M8,3.5 L8,12.5 M3.5,8 L12.5,8</Geometry>
    <Geometry x:Key="Icon.Check">M3.5,8.5 L6.5,11.5 L12.5,4.5</Geometry>
    <Geometry x:Key="Icon.ArrowDown">M8,3.5 L8,12.5 M4.5,9 L8,12.5 L11.5,9</Geometry>
    <Geometry x:Key="Icon.ArrowUp">M8,12.5 L8,3.5 M4.5,7 L8,3.5 L11.5,7</Geometry>
    <Geometry x:Key="Icon.ChevronLeft">M10,3.5 L5.5,8 L10,12.5</Geometry>
    <Geometry x:Key="Icon.ChevronRight">M6,3.5 L10.5,8 L6,12.5</Geometry>
    <Geometry x:Key="Icon.ChevronDown">M4,6 L8,10 L12,6</Geometry>
    <Geometry x:Key="Icon.Sun">M5,8 A3,3 0 1 1 11,8 A3,3 0 1 1 5,8 Z M8,1.5 L8,3 M8,13 L8,14.5 M1.5,8 L3,8 M13,8 L14.5,8 M3.4,3.4 L4.4,4.4 M11.6,11.6 L12.6,12.6 M3.4,12.6 L4.4,11.6 M11.6,4.4 L12.6,3.4</Geometry>
    <Geometry x:Key="Icon.Moon">M13,9.5 A5.5,5.5 0 0 1 6.5,3 A5.5,5.5 0 1 0 13,9.5 Z</Geometry>
    <Geometry x:Key="Icon.Monitor">M3,3 L13,3 A1,1 0 0 1 14,4 L14,10.5 A1,1 0 0 1 13,11.5 L3,11.5 A1,1 0 0 1 2,10.5 L2,4 A1,1 0 0 1 3,3 Z M6,14 L10,14 M8,11.5 L8,14</Geometry>
    <Geometry x:Key="Icon.Settings">M2.5,5 L13.5,5 M2.5,11 L13.5,11 M5.5,3 L5.5,7 M10.5,9 L10.5,13</Geometry>
    <Geometry x:Key="Icon.Trash">M3,4.5 L13,4.5 M6.5,4.5 L6.5,3 L9.5,3 L9.5,4.5 M4.5,4.5 L5,13 L11,13 L11.5,4.5</Geometry>
    <Geometry x:Key="Icon.Warning">M8,2.5 L14,13 L2,13 Z M8,6.5 L8,9.5 M8,11.2 L8,11.3</Geometry>
    <Geometry x:Key="Icon.Error">M2.5,8 A5.5,5.5 0 1 1 13.5,8 A5.5,5.5 0 1 1 2.5,8 Z M8,5 L8,8.5 M8,10.8 L8,10.9</Geometry>
    <Geometry x:Key="Icon.Windows">M2.5,2.5 L7.3,2.5 L7.3,7.3 L2.5,7.3 Z M8.7,2.5 L13.5,2.5 L13.5,7.3 L8.7,7.3 Z M2.5,8.7 L7.3,8.7 L7.3,13.5 L2.5,13.5 Z M8.7,8.7 L13.5,8.7 L13.5,13.5 L8.7,13.5 Z</Geometry>
</ResourceDictionary>
```

- [ ] **Step 2: Commit** (it builds once Styles.xaml merges it in Task 9)

```bash
git -C C:/QuickerPlaces add src/QuickerPlaces/Resources/Icons.xaml
git -C C:/QuickerPlaces commit -m "Add stroke icon geometries"
```

### Task 9: Styles.xaml replaces Theme.xaml

**Files:**
- Create: `src/QuickerPlaces/Resources/Styles.xaml`
- Delete: `src/QuickerPlaces/Resources/Theme.xaml`

- [ ] **Step 1: Write Styles.xaml**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!--
        QuickerPlaces styles: the Saab 900 look of the QuickerPlaces Design
        System, version 2. Colours come from Palette.Dark.xaml or
        Palette.Light.xaml plus the Highlight.* brushes ThemeManager writes,
        always through DynamicResource (UI refresh U2). Sizes, radii and type
        follow the Design System's tokens: buttons 34 px (compact 28), 6 px
        corners, TASA Orbiter at 13.5 px.

        There is deliberately no implicit TextBlock style (U8): windows set
        Foreground, and text inherits it, so a selected row's text can turn
        On.Highlight without every TextBlock being overridden.
    -->
    <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="Icons.xaml" />
    </ResourceDictionary.MergedDictionaries>

    <FontFamily x:Key="Font.Sans">pack://application:,,,/Resources/Fonts/#TASA Orbiter</FontFamily>
    <FontFamily x:Key="Font.Mono">pack://application:,,,/Resources/Fonts/#IBM Plex Mono</FontFamily>

    <!-- The keyboard focus ring for every control: 2 px Signal, 2 px outside. -->
    <Style x:Key="{x:Static SystemParameters.FocusVisualStyleKey}">
        <Setter Property="Control.Template">
            <Setter.Value>
                <ControlTemplate>
                    <Border Margin="-4" CornerRadius="9" BorderThickness="2"
                            BorderBrush="{DynamicResource Signal}" SnapsToDevicePixels="True" />
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="Window">
        <Setter Property="Background" Value="{DynamicResource Bg.Base}" />
        <Setter Property="Foreground" Value="{DynamicResource Text.Primary}" />
        <Setter Property="FontFamily" Value="{StaticResource Font.Sans}" />
        <Setter Property="FontSize" Value="13.5" />
        <Setter Property="Icon" Value="pack://application:,,,/Resources/AppIcon.ico" />
    </Style>

    <!-- Icons -->
    <Style x:Key="Icon" TargetType="Path">
        <Setter Property="Width" Value="16" />
        <Setter Property="Height" Value="16" />
        <Setter Property="Stretch" Value="None" />
        <Setter Property="StrokeThickness" Value="1.6" />
        <Setter Property="StrokeStartLineCap" Value="Round" />
        <Setter Property="StrokeEndLineCap" Value="Round" />
        <Setter Property="StrokeLineJoin" Value="Round" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Stroke" Value="{Binding (TextElement.Foreground), RelativeSource={RelativeSource Self}}" />
    </Style>
    <!-- A place's type: folder or globe, from the DataContext's Type. -->
    <Style x:Key="Icon.PlaceType" TargetType="Path" BasedOn="{StaticResource Icon}">
        <Setter Property="Data" Value="{StaticResource Icon.Globe}" />
        <Style.Triggers>
            <DataTrigger Binding="{Binding Type}" Value="Folder">
                <Setter Property="Data" Value="{StaticResource Icon.Folder}" />
            </DataTrigger>
        </Style.Triggers>
    </Style>
    <!-- The type icon in a table row: Highlight.Text, On.Highlight on a selected row. -->
    <Style x:Key="Icon.RowPlaceType" TargetType="Path" BasedOn="{StaticResource Icon.PlaceType}">
        <Setter Property="Stroke" Value="{DynamicResource Highlight.Text}" />
        <Setter Property="Margin" Value="0,0,9,0" />
        <Style.Triggers>
            <DataTrigger Binding="{Binding IsSelected, RelativeSource={RelativeSource AncestorType=DataGridRow}}" Value="True">
                <Setter Property="Stroke" Value="{DynamicResource On.Highlight}" />
            </DataTrigger>
        </Style.Triggers>
    </Style>

    <!-- Text -->
    <Style x:Key="TextBlock.WindowTitle" TargetType="TextBlock">
        <Setter Property="FontSize" Value="23" />
        <Setter Property="FontWeight" Value="ExtraBold" />
    </Style>
    <Style x:Key="TextBlock.SectionTitle" TargetType="TextBlock">
        <Setter Property="FontSize" Value="20" />
        <Setter Property="FontWeight" Value="SemiBold" />
    </Style>
    <!-- Section labels, typed in capitals (U11). -->
    <Style x:Key="TextBlock.Caps" TargetType="TextBlock">
        <Setter Property="FontSize" Value="12" />
        <Setter Property="FontWeight" Value="Bold" />
        <Setter Property="Foreground" Value="{DynamicResource Text.Secondary}" />
        <Setter Property="VerticalAlignment" Value="Center" />
    </Style>
    <Style x:Key="TextBlock.FieldLabel" TargetType="TextBlock">
        <Setter Property="FontSize" Value="14" />
        <Setter Property="FontWeight" Value="SemiBold" />
    </Style>
    <Style x:Key="TextBlock.Hint" TargetType="TextBlock">
        <Setter Property="FontSize" Value="12.5" />
        <Setter Property="Foreground" Value="{DynamicResource Text.Secondary}" />
        <Setter Property="TextWrapping" Value="Wrap" />
    </Style>

    <!-- Table cell text (ElementStyle on DataGridTextColumn). -->
    <Style x:Key="Cell.Text" TargetType="TextBlock">
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="TextTrimming" Value="CharacterEllipsis" />
    </Style>
    <Style x:Key="Cell.Centered" TargetType="TextBlock" BasedOn="{StaticResource Cell.Text}">
        <Setter Property="TextAlignment" Value="Center" />
    </Style>
    <Style x:Key="Cell.Secondary" TargetType="TextBlock" BasedOn="{StaticResource Cell.Text}">
        <Setter Property="Foreground" Value="{DynamicResource Text.Secondary}" />
        <Style.Triggers>
            <DataTrigger Binding="{Binding IsSelected, RelativeSource={RelativeSource AncestorType=DataGridRow}}" Value="True">
                <Setter Property="Foreground" Value="{DynamicResource On.Highlight}" />
                <Setter Property="Opacity" Value="0.85" />
            </DataTrigger>
        </Style.Triggers>
    </Style>
    <Style x:Key="Cell.Path" TargetType="TextBlock" BasedOn="{StaticResource Cell.Secondary}">
        <Setter Property="FontFamily" Value="{StaticResource Font.Mono}" />
        <Setter Property="FontSize" Value="12.5" />
    </Style>

    <!-- Buttons -->
    <ControlTemplate x:Key="Button.Template" TargetType="Button">
        <Border x:Name="Chrome" Background="{TemplateBinding Background}"
                BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                CornerRadius="6" SnapsToDevicePixels="True">
            <!-- RecognizesAccessKey: "_Restore selected" shows Alt+R instead of an underscore. -->
            <ContentPresenter Margin="{TemplateBinding Padding}"
                              HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                              VerticalAlignment="Center" RecognizesAccessKey="True" />
        </Border>
        <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="Chrome" Property="BorderBrush" Value="{DynamicResource Highlight.Text}" />
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource Bg.Panel}" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Foreground" Value="{DynamicResource Text.Tertiary}" />
                <Setter TargetName="Chrome" Property="BorderBrush" Value="{DynamicResource Border.Default}" />
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>

    <Style TargetType="Button">
        <Setter Property="Background" Value="{DynamicResource Bg.Raised}" />
        <Setter Property="Foreground" Value="{DynamicResource Text.Primary}" />
        <Setter Property="BorderBrush" Value="{DynamicResource Border.Strong}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Height" Value="34" />
        <Setter Property="Padding" Value="14,0" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template" Value="{StaticResource Button.Template}" />
    </Style>
    <Style x:Key="Button.Compact" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Height" Value="28" />
        <Setter Property="Padding" Value="10,0" />
        <Setter Property="FontSize" Value="13" />
    </Style>
    <Style x:Key="Button.IconOnly" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Width" Value="34" />
        <Setter Property="Padding" Value="0" />
    </Style>
    <Style x:Key="Button.IconOnlyCompact" TargetType="Button" BasedOn="{StaticResource Button.Compact}">
        <Setter Property="Width" Value="28" />
        <Setter Property="Padding" Value="0" />
    </Style>

    <!-- The one main action per screen, in the user's highlight colour, with a chrome top edge. -->
    <Style x:Key="Button.Primary" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Background" Value="{DynamicResource Highlight}" />
        <Setter Property="Foreground" Value="{DynamicResource On.Highlight}" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Grid SnapsToDevicePixels="True">
                        <Border x:Name="Chrome" Background="{TemplateBinding Background}" CornerRadius="6" />
                        <Border CornerRadius="6" BorderThickness="0,1,0,0" BorderBrush="#33FFFFFF" />
                        <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Center"
                                          VerticalAlignment="Center" RecognizesAccessKey="True" />
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource Highlight.Hover}" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource Bg.Panel}" />
                            <Setter Property="Foreground" Value="{DynamicResource Text.Tertiary}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- A favourite: a small tan-leather card (FavouriteCard in the Design System). -->
    <Style x:Key="Button.Favourite" TargetType="Button">
        <Setter Property="Background" Value="{DynamicResource Leather}" />
        <Setter Property="Foreground" Value="{DynamicResource On.Leather}" />
        <Setter Property="Height" Value="32" />
        <Setter Property="Padding" Value="5,0,12,0" />
        <Setter Property="Margin" Value="0,0,8,8" />
        <Setter Property="FontSize" Value="14.5" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Grid SnapsToDevicePixels="True">
                        <Border Background="{TemplateBinding Background}" CornerRadius="6">
                            <Border.Effect>
                                <DropShadowEffect Direction="270" ShadowDepth="1" BlurRadius="2" Opacity="0.22" Color="Black" />
                            </Border.Effect>
                        </Border>
                        <Border CornerRadius="6" BorderThickness="0,1,0,0" BorderBrush="#38FFFFFF" />
                        <Border x:Name="Sheen" CornerRadius="6" Background="#1AFFFFFF" Visibility="Collapsed" />
                        <ContentPresenter Margin="{TemplateBinding Padding}" VerticalAlignment="Center" />
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Sheen" Property="Visibility" Value="Visible" />
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="Sheen" Property="Background" Value="#1A000000" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <!-- The Ctrl+number badge on a favourite card. -->
    <Style x:Key="Border.FavouriteNumber" TargetType="Border">
        <Setter Property="Width" Value="22" />
        <Setter Property="Height" Value="22" />
        <Setter Property="CornerRadius" Value="4" />
        <Setter Property="Margin" Value="0,0,7,0" />
        <Setter Property="Background" Value="{DynamicResource Leather.Deep}" />
    </Style>

    <!--
        A bare glyph button inside a table cell (the favourite star): no
        chrome, a faint neutral wash under the pointer. Not focusable, so Tab
        and the arrow keys keep moving through rows; Ctrl+D is the keyboard
        route.
    -->
    <Style x:Key="Button.GridIcon" TargetType="Button">
        <Setter Property="Focusable" Value="False" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="Padding" Value="3,2" />
        <Setter Property="HorizontalAlignment" Value="Center" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="Chrome" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}" CornerRadius="4">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Chrome" Property="Background" Value="#1F808080" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Header pieces -->
    <Style x:Key="Border.Monogram" TargetType="Border">
        <Setter Property="Width" Value="40" />
        <Setter Property="Height" Value="40" />
        <Setter Property="CornerRadius" Value="8" />
        <Setter Property="Background" Value="{DynamicResource Brand.Green}" />
        <Setter Property="BorderBrush" Value="{DynamicResource Trim.Line}" />
        <Setter Property="BorderThickness" Value="1" />
    </Style>
    <!-- The silver rub strip under each window's header: 7 px band, 1 px pinstripe. -->
    <Style x:Key="TrimBand" TargetType="Control">
        <Setter Property="Height" Value="7" />
        <Setter Property="Focusable" Value="False" />
        <Setter Property="IsTabStop" Value="False" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Control">
                    <Border Background="{DynamicResource Trim.Band}">
                        <Rectangle Height="1" Fill="{DynamicResource Trim.Line}" VerticalAlignment="Center" />
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <!-- A keycap: <ContentControl Style="{StaticResource Kbd}" Content="Ctrl F" />. -->
    <Style x:Key="Kbd" TargetType="ContentControl">
        <Setter Property="Focusable" Value="False" />
        <Setter Property="IsTabStop" Value="False" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ContentControl">
                    <Border Padding="6,1" CornerRadius="4" BorderThickness="1"
                            BorderBrush="{DynamicResource Border.Strong}" Background="{DynamicResource Bg.Raised}">
                        <TextBlock Text="{TemplateBinding Content}" FontSize="11" FontWeight="SemiBold"
                                   Foreground="{DynamicResource Text.Secondary}" />
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <!-- The place count beside ALL PLACES. -->
    <Style x:Key="Badge.Count" TargetType="ContentControl">
        <Setter Property="Focusable" Value="False" />
        <Setter Property="IsTabStop" Value="False" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ContentControl">
                    <Border Padding="7,0" CornerRadius="9" BorderThickness="1"
                            BorderBrush="{DynamicResource Border.Default}" Background="{DynamicResource Bg.Raised}">
                        <TextBlock Text="{TemplateBinding Content}" FontSize="12" FontWeight="Bold"
                                   Foreground="{DynamicResource Text.Secondary}" />
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Text boxes -->
    <Style TargetType="TextBox">
        <Setter Property="Background" Value="{DynamicResource Bg.Sunken}" />
        <Setter Property="Foreground" Value="{DynamicResource Text.Primary}" />
        <Setter Property="BorderBrush" Value="{DynamicResource Border.Strong}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Padding" Value="8,6" />
        <Setter Property="MinHeight" Value="34" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="CaretBrush" Value="{DynamicResource Text.Primary}" />
        <Setter Property="SelectionBrush" Value="{DynamicResource Highlight}" />
        <Setter Property="SelectionOpacity" Value="0.4" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TextBox">
                    <Border x:Name="Chrome" Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="6" SnapsToDevicePixels="True">
                        <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}"
                                      VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                                      Focusable="False" HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="Chrome" Property="BorderBrush" Value="{DynamicResource Highlight.Text}" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.6" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <!-- A text box inside a composite field (the search box): no chrome of its own. -->
    <Style x:Key="TextBox.Bare" TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Padding" Value="0" />
        <Setter Property="MinHeight" Value="0" />
    </Style>

    <!-- Combo box: fully retemplated so the dropdown matches the theme. -->
    <Style TargetType="ComboBox">
        <Setter Property="Background" Value="{DynamicResource Bg.Raised}" />
        <Setter Property="Foreground" Value="{DynamicResource Text.Primary}" />
        <Setter Property="BorderBrush" Value="{DynamicResource Border.Strong}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Height" Value="34" />
        <Setter Property="Padding" Value="12,0,10,0" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ComboBox">
                    <Grid>
                        <Border x:Name="MainBorder" Background="{TemplateBinding Background}"
                                BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                                CornerRadius="6">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <ContentPresenter x:Name="ContentSite" IsHitTestVisible="False"
                                                  Content="{TemplateBinding SelectionBoxItem}"
                                                  ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                                                  ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"
                                                  Margin="{TemplateBinding Padding}" HorizontalAlignment="Left" VerticalAlignment="Center" />
                                <Path Grid.Column="1" Style="{StaticResource Icon}" Data="{StaticResource Icon.ChevronDown}"
                                      Stroke="{DynamicResource Text.Secondary}" Margin="0,0,10,0" />
                            </Grid>
                        </Border>
                        <ToggleButton x:Name="ToggleButton" Focusable="False" ClickMode="Press" Background="Transparent"
                                      IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
                            <ToggleButton.Template>
                                <ControlTemplate TargetType="ToggleButton">
                                    <Border Background="{TemplateBinding Background}" />
                                </ControlTemplate>
                            </ToggleButton.Template>
                        </ToggleButton>
                        <Popup x:Name="Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}"
                               AllowsTransparency="True" Focusable="False" PopupAnimation="Fade">
                            <Border MinWidth="{TemplateBinding ActualWidth}" MaxHeight="{TemplateBinding MaxDropDownHeight}"
                                    Margin="0,4,0,0" Padding="4" CornerRadius="8" BorderThickness="1"
                                    Background="{DynamicResource Bg.Raised}" BorderBrush="{DynamicResource Border.Strong}">
                                <ScrollViewer SnapsToDevicePixels="True">
                                    <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained" />
                                </ScrollViewer>
                            </Border>
                        </Popup>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="MainBorder" Property="BorderBrush" Value="{DynamicResource Highlight.Text}" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Foreground" Value="{DynamicResource Text.Tertiary}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType="ComboBoxItem">
        <Setter Property="Foreground" Value="{DynamicResource Text.Primary}" />
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="Height" Value="30" />
        <Setter Property="Padding" Value="10,0" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ComboBoxItem">
                    <Border x:Name="Chrome" Background="{TemplateBinding Background}" CornerRadius="6" Padding="{TemplateBinding Padding}">
                        <ContentPresenter VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsHighlighted" Value="True">
                            <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource Bg.RowHover}" />
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource Highlight}" />
                            <Setter Property="Foreground" Value="{DynamicResource On.Highlight}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Checkbox: a 16 px box that fills Highlight with a tick. -->
    <Style TargetType="CheckBox">
        <Setter Property="Foreground" Value="{DynamicResource Text.Primary}" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="CheckBox">
                    <Grid Background="Transparent">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <Grid Width="16" Height="16" VerticalAlignment="Center">
                            <Border x:Name="Box" CornerRadius="4" BorderThickness="1"
                                    BorderBrush="{DynamicResource Border.Strong}" Background="{DynamicResource Bg.Sunken}" />
                            <Path x:Name="Tick" Style="{StaticResource Icon}" Data="{StaticResource Icon.Check}"
                                  Stroke="{DynamicResource On.Highlight}" StrokeThickness="2" Visibility="Collapsed" />
                        </Grid>
                        <ContentPresenter Grid.Column="1" Margin="10,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True" />
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource Highlight.Text}" />
                        </Trigger>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Box" Property="Background" Value="{DynamicResource Highlight}" />
                            <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource Highlight}" />
                            <Setter TargetName="Tick" Property="Visibility" Value="Visible" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.5" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!--
        Segmented control: RadioButtons in a Border.SegmentGroup. Tag carries
        each segment's corner radius ("5,0,0,5" first, "0" middle, "0,5,5,0"
        last), and the last segment sets BorderThickness="0".
    -->
    <Style x:Key="Border.SegmentGroup" TargetType="Border">
        <Setter Property="BorderBrush" Value="{DynamicResource Border.Strong}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="6" />
        <Setter Property="Background" Value="{DynamicResource Bg.Raised}" />
        <Setter Property="SnapsToDevicePixels" Value="True" />
    </Style>
    <Style x:Key="RadioButton.Segment" TargetType="RadioButton">
        <Setter Property="Foreground" Value="{DynamicResource Text.Secondary}" />
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="BorderBrush" Value="{DynamicResource Border.Default}" />
        <Setter Property="BorderThickness" Value="0,0,1,0" />
        <Setter Property="Height" Value="32" />
        <Setter Property="Padding" Value="14,0" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Tag" Value="0" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="RadioButton">
                    <Border x:Name="Chrome" Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}">
                        <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Center"
                                          VerticalAlignment="Center" RecognizesAccessKey="True" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Foreground" Value="{DynamicResource Text.Primary}" />
                        </Trigger>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource Highlight}" />
                            <Setter Property="Foreground" Value="{DynamicResource On.Highlight}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!--
        A highlight swatch in Settings: a 40 px circle in its Background, the
        name underneath. Tag="windows" draws the four-pane icon instead of a
        colour.
    -->
    <Style x:Key="RadioButton.Swatch" TargetType="RadioButton">
        <Setter Property="Width" Value="64" />
        <Setter Property="Margin" Value="0,0,18,0" />
        <Setter Property="Foreground" Value="{DynamicResource Text.Secondary}" />
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="RadioButton">
                    <StackPanel Background="Transparent">
                        <Grid Width="48" Height="48" HorizontalAlignment="Center">
                            <Ellipse x:Name="Ring" Stroke="{DynamicResource Text.Primary}" StrokeThickness="2" Visibility="Hidden" />
                            <Ellipse x:Name="Dot" Width="40" Height="40" Fill="{TemplateBinding Background}" Stroke="#2EFFFFFF" StrokeThickness="1" />
                            <Path x:Name="WindowsIcon" Style="{StaticResource Icon}" Data="{StaticResource Icon.Windows}"
                                  Stroke="{DynamicResource Text.Primary}" HorizontalAlignment="Center" Visibility="Collapsed" />
                            <Path x:Name="Tick" Style="{StaticResource Icon}" Data="{StaticResource Icon.Check}"
                                  Stroke="#FFFFFFFF" StrokeThickness="2.2" HorizontalAlignment="Center" Visibility="Collapsed" />
                        </Grid>
                        <ContentPresenter Margin="0,4,0,0" HorizontalAlignment="Center" />
                    </StackPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Ring" Property="Visibility" Value="Visible" />
                            <Setter TargetName="Tick" Property="Visibility" Value="Visible" />
                        </Trigger>
                        <Trigger Property="Tag" Value="windows">
                            <Setter TargetName="WindowsIcon" Property="Visibility" Value="Visible" />
                            <Setter TargetName="Dot" Property="Stroke" Value="{DynamicResource Border.Strong}" />
                            <Setter TargetName="Tick" Property="Visibility" Value="Collapsed" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- A tracked folder in Recents: a selectable pill. -->
    <Style x:Key="ListBoxItem.Chip" TargetType="ListBoxItem">
        <Setter Property="Background" Value="{DynamicResource Bg.Raised}" />
        <Setter Property="Foreground" Value="{DynamicResource Text.Primary}" />
        <Setter Property="BorderBrush" Value="{DynamicResource Border.Strong}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Height" Value="30" />
        <Setter Property="Padding" Value="10,0,12,0" />
        <Setter Property="Margin" Value="0,0,8,6" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ListBoxItem">
                    <Border x:Name="Chrome" Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="15" Padding="{TemplateBinding Padding}">
                        <ContentPresenter VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Chrome" Property="BorderBrush" Value="{DynamicResource Highlight.Text}" />
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource Highlight.Soft}" />
                            <Setter TargetName="Chrome" Property="BorderBrush" Value="{DynamicResource Highlight.Text}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Tables: row dividers, no stripes, a hover wash, Highlight on selection. Square corners (U11). -->
    <Style TargetType="DataGrid">
        <Setter Property="Background" Value="{DynamicResource Bg.Panel}" />
        <Setter Property="Foreground" Value="{DynamicResource Text.Primary}" />
        <Setter Property="BorderBrush" Value="{DynamicResource Border.Default}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="RowBackground" Value="Transparent" />
        <Setter Property="AlternatingRowBackground" Value="Transparent" />
        <Setter Property="GridLinesVisibility" Value="Horizontal" />
        <Setter Property="HorizontalGridLinesBrush" Value="{DynamicResource Border.Default}" />
        <Setter Property="VerticalGridLinesBrush" Value="Transparent" />
        <Setter Property="HeadersVisibility" Value="Column" />
        <Setter Property="RowHeaderWidth" Value="0" />
        <Setter Property="RowHeight" Value="36" />
        <Setter Property="CanUserResizeRows" Value="False" />
    </Style>
    <Style TargetType="DataGridRow">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="Foreground" Value="{DynamicResource Text.Primary}" />
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="{DynamicResource Bg.RowHover}" />
            </Trigger>
            <Trigger Property="IsSelected" Value="True">
                <Setter Property="Background" Value="{DynamicResource Highlight}" />
                <Setter Property="Foreground" Value="{DynamicResource On.Highlight}" />
            </Trigger>
        </Style.Triggers>
    </Style>
    <Style TargetType="DataGridCell">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Padding" Value="12,0" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Foreground" Value="{Binding Foreground, RelativeSource={RelativeSource AncestorType=DataGridRow}}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="DataGridCell">
                    <Border Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}" SnapsToDevicePixels="True">
                        <ContentPresenter VerticalAlignment="Center" />
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <!-- The row paints the selection; keep the theme's blue cell fill off it. -->
            <Trigger Property="IsSelected" Value="True">
                <Setter Property="Background" Value="Transparent" />
                <Setter Property="Foreground" Value="{DynamicResource On.Highlight}" />
            </Trigger>
        </Style.Triggers>
    </Style>
    <!-- The column-resize handles at each edge of a header: invisible, 8 px wide. -->
    <Style x:Key="DataGridColumnHeader.Gripper" TargetType="Thumb">
        <Setter Property="Width" Value="8" />
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="Cursor" Value="SizeWE" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Thumb">
                    <Border Background="{TemplateBinding Background}" />
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <!--
        Headers in capitals (typed that way in each view, U11). A sorted
        column shows its label in Text.Primary, a Signal arrow and a 2 px
        Signal underline. PART_LeftHeaderGripper / PART_RightHeaderGripper
        are the names DataGridColumnHeader needs for column resizing.
    -->
    <Style TargetType="DataGridColumnHeader">
        <Setter Property="Background" Value="{DynamicResource Bg.Raised}" />
        <Setter Property="Foreground" Value="{DynamicResource Text.Secondary}" />
        <Setter Property="BorderBrush" Value="{DynamicResource Border.Default}" />
        <Setter Property="BorderThickness" Value="0,0,0,1" />
        <Setter Property="Height" Value="34" />
        <Setter Property="Padding" Value="12,0" />
        <Setter Property="FontSize" Value="11.5" />
        <Setter Property="FontWeight" Value="Bold" />
        <Setter Property="HorizontalContentAlignment" Value="Left" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="DataGridColumnHeader">
                    <Grid>
                        <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}">
                            <StackPanel Orientation="Horizontal"
                                        HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                        VerticalAlignment="{TemplateBinding VerticalContentAlignment}">
                                <ContentPresenter VerticalAlignment="Center" />
                                <Path x:Name="SortIcon" Style="{StaticResource Icon}" Stroke="{DynamicResource Signal}"
                                      Margin="6,0,0,0" Visibility="Collapsed" />
                            </StackPanel>
                        </Border>
                        <Border x:Name="SortBar" Height="2" VerticalAlignment="Bottom"
                                Background="{DynamicResource Signal}" Visibility="Collapsed" />
                        <Thumb x:Name="PART_LeftHeaderGripper" HorizontalAlignment="Left" Style="{StaticResource DataGridColumnHeader.Gripper}" />
                        <Thumb x:Name="PART_RightHeaderGripper" HorizontalAlignment="Right" Style="{StaticResource DataGridColumnHeader.Gripper}" />
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="SortDirection" Value="Ascending">
                            <Setter TargetName="SortIcon" Property="Data" Value="{StaticResource Icon.ArrowUp}" />
                            <Setter TargetName="SortIcon" Property="Visibility" Value="Visible" />
                            <Setter TargetName="SortBar" Property="Visibility" Value="Visible" />
                            <Setter Property="Foreground" Value="{DynamicResource Text.Primary}" />
                        </Trigger>
                        <Trigger Property="SortDirection" Value="Descending">
                            <Setter TargetName="SortIcon" Property="Data" Value="{StaticResource Icon.ArrowDown}" />
                            <Setter TargetName="SortIcon" Property="Visibility" Value="Visible" />
                            <Setter TargetName="SortBar" Property="Visibility" Value="Visible" />
                            <Setter Property="Foreground" Value="{DynamicResource Text.Primary}" />
                        </Trigger>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Foreground" Value="{DynamicResource Text.Primary}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <!-- A header centred over its column, for narrow centred columns (Opens). -->
    <Style x:Key="DataGridColumnHeader.Centered" TargetType="DataGridColumnHeader" BasedOn="{StaticResource {x:Type DataGridColumnHeader}}">
        <Setter Property="HorizontalContentAlignment" Value="Center" />
    </Style>

    <!-- Scroll bars: a thin thumb, no arrows. -->
    <Style TargetType="ScrollBar">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ScrollBar">
                    <Grid Background="{TemplateBinding Background}">
                        <Track x:Name="PART_Track" IsDirectionReversed="True" Orientation="{TemplateBinding Orientation}">
                            <Track.DecreaseRepeatButton>
                                <RepeatButton Background="Transparent" BorderThickness="0" Command="ScrollBar.PageUpCommand" Opacity="0" />
                            </Track.DecreaseRepeatButton>
                            <Track.Thumb>
                                <Thumb>
                                    <Thumb.Template>
                                        <ControlTemplate TargetType="Thumb">
                                            <Border Background="{DynamicResource Border.Strong}" CornerRadius="4" Margin="2" />
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                            <Track.IncreaseRepeatButton>
                                <RepeatButton Background="Transparent" BorderThickness="0" Command="ScrollBar.PageDownCommand" Opacity="0" />
                            </Track.IncreaseRepeatButton>
                        </Track>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property="Orientation" Value="Vertical">
                <Setter Property="Width" Value="10" />
            </Trigger>
            <Trigger Property="Orientation" Value="Horizontal">
                <Setter Property="Height" Value="10" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <Style TargetType="ToolTip">
        <Setter Property="Foreground" Value="{DynamicResource Text.Primary}" />
        <Setter Property="FontFamily" Value="{StaticResource Font.Sans}" />
        <Setter Property="FontSize" Value="12.5" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ToolTip">
                    <Border Background="{DynamicResource Bg.Raised}" BorderBrush="{DynamicResource Border.Strong}"
                            BorderThickness="1" CornerRadius="6" Padding="8,5">
                        <ContentPresenter />
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="Separator">
        <Setter Property="Background" Value="{DynamicResource Border.Default}" />
        <Setter Property="Height" Value="1" />
        <Setter Property="Margin" Value="0,20" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Separator">
                    <Border Background="{TemplateBinding Background}" />
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
</ResourceDictionary>
```

Context menus and menu items are left to WPF's Fluent styles, which follow `ThemeMode` (U4).

- [ ] **Step 2: Delete the old theme**

```bash
git -C C:/QuickerPlaces rm src/QuickerPlaces/Resources/Theme.xaml
```

- [ ] **Step 3: Commit** (the app builds again after Task 12)

```bash
git -C C:/QuickerPlaces add src/QuickerPlaces/Resources/Styles.xaml
git -C C:/QuickerPlaces commit -m "Replace the violet theme with the Saab 900 styles"
```

### Task 10: ThemeManager and startup

**Files:**
- Create: `src/QuickerPlaces/Services/ThemeManager.cs`
- Modify: `src/QuickerPlaces/App.xaml`
- Modify: `src/QuickerPlaces/App.xaml.cs`
- Modify: `src/QuickerPlaces/Views/MainWindow.xaml.cs` (constructor only here)

- [ ] **Step 1: Write ThemeManager**

```csharp
using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Theming;

namespace QuickerPlaces.Services;

/// <summary>
/// Applies the theme and highlight (UI refresh U1–U6): swaps the palette
/// dictionary App.xaml merges, sets ThemeMode so Windows draws a matching
/// title bar and menus, and writes the five Highlight brushes at the top of
/// Application.Resources, where they win over the palette's defaults. Every
/// view refers to colours with DynamicResource, so open windows repaint.
/// Match Windows and Windows accent follow system changes while running.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private const string DarkPalette = "Palette.Dark.xaml";
    private const string LightPalette = "Palette.Light.xaml";

    private readonly Application _app;

    public ThemeManager(Application app)
    {
        _app = app;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public AppTheme Theme { get; private set; } = AppTheme.Dark;

    public HighlightPreset Highlight { get; private set; } = HighlightPreset.Green;

    public void Apply(AppTheme theme, HighlightPreset highlight)
    {
        Theme = theme;
        Highlight = highlight;
        var dark = theme switch
        {
            AppTheme.Light => false,
            AppTheme.System => WindowsAppsUseDarkTheme(),
            _ => true
        };

        SwapPalette(dark ? DarkPalette : LightPalette);

#pragma warning disable WPF0001 // ThemeMode is marked experimental; here it only drives the title bar and the Fluent menus.
        _app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001

        var colors = HighlightPalette.For(highlight, dark,
            highlight == HighlightPreset.Windows ? ReadWindowsAccent() : null);
        SetBrush("Highlight", colors.Fill);
        SetBrush("Highlight.Hover", colors.Hover);
        SetBrush("Highlight.Text", colors.Text);
        SetBrush("Highlight.Soft", colors.Soft);
        SetBrush("On.Highlight", colors.OnFill);
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    /// <summary>
    /// Replaces the merged palette in place. Found by file name, not index:
    /// setting ThemeMode inserts WPF's own Fluent dictionary into the same
    /// list (U4).
    /// </summary>
    private void SwapPalette(string fileName)
    {
        var merged = _app.Resources.MergedDictionaries;
        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.OriginalString;
            if (source is null)
                continue;
            if (!source.EndsWith(DarkPalette, StringComparison.OrdinalIgnoreCase) &&
                !source.EndsWith(LightPalette, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!source.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                merged[i] = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Resources/{fileName}", UriKind.Absolute) };
            return;
        }

        throw new InvalidOperationException($"App.xaml must merge Resources/{DarkPalette} or Resources/{LightPalette}.");
    }

    private void SetBrush(string key, ThemeColor color)
    {
        var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();
        _app.Resources[key] = brush;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Raised on a system-events thread; the resources belong to the UI thread.
        if (Theme == AppTheme.System || Highlight == HighlightPreset.Windows)
            _app.Dispatcher.BeginInvoke(() => Apply(Theme, Highlight));
    }

    /// <summary>Windows' own "app mode" setting; dark when it can't be read.</summary>
    private static bool WindowsAppsUseDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
    }

    /// <summary>The Windows accent colour (stored as 0xAABBGGRR), or null when unavailable.</summary>
    private static ThemeColor? ReadWindowsAccent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
        if (key?.GetValue("AccentColor") is not int value)
            return null;
        var abgr = unchecked((uint)value);
        return ThemeColor.FromRgb((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));
    }
}
```

- [ ] **Step 2: Rewrite App.xaml** (the comment explains why ThemeMode moved to code)

```xml
<Application x:Class="QuickerPlaces.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!--
        No StartupUri: App.xaml.cs constructs MainWindow itself so it can
        inject its dependencies. ShutdownMode is WPF's default
        (OnLastWindowClose).

        ThemeMode is not set here any more: ThemeManager sets it to Dark or
        Light together with the palette (UI refresh U4). The palette merged
        first is only the starting point; ThemeManager swaps it before any
        window opens. Styles.xaml merges Icons.xaml itself so its
        StaticResource references resolve while it loads.
    -->
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Resources/Palette.Dark.xaml" />
                <ResourceDictionary Source="Resources/Styles.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 3: Apply the theme at startup** — in `App.xaml.cs`, `OnStartup`:

Replace:

```csharp
        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        var placesService = new PlacesService();
```

with:

```csharp
        var settingsService = new SettingsService();
        var settings = settingsService.Load();

        // Before the recovery prompt below, which is the first window that
        // can open: every window uses the chosen theme from the start.
        var themeManager = new ThemeManager(this);
        themeManager.Apply(ThemePreference.ParseTheme(settings.Theme), ThemePreference.ParseHighlight(settings.Highlight));

        var placesService = new PlacesService();
```

In the recovery early exit, add `themeManager.Dispose();` before `singleInstance.Dispose();`.

Replace the MainWindow construction:

```csharp
        var mainWindow = new MainWindow(mainViewModel, settings, settingsService, activityStore, activityHost, themeManager);
```

In the `Closing` handler, add `themeManager.Dispose();` after `activityHost.Dispose();`.

- [ ] **Step 4: Take the ThemeManager in MainWindow** — `Views/MainWindow.xaml.cs`:

Add the field after `_activityHost`:

```csharp
    private readonly ThemeManager _themeManager;
```

Change the constructor signature and body:

```csharp
    public MainWindow(MainViewModel viewModel, AppSettings settings, SettingsService settingsService,
        ActivityStore activityStore, ActivityTrackingHost activityHost, ThemeManager themeManager)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settings = settings;
        _settingsService = settingsService;
        _activityStore = activityStore;
        _activityHost = activityHost;
        _themeManager = themeManager;
        RestoreWindowState(settings);
        UpdateActivityIndicator();
    }
```

Add `using QuickerPlaces.Services;` at the top if it is not already there.

- [ ] **Step 5: Commit** (still not building until Task 12)

```bash
git -C C:/QuickerPlaces add -A src/QuickerPlaces
git -C C:/QuickerPlaces commit -m "Apply the theme and highlight at startup with ThemeManager"
```

### Task 11: Rename resource keys across the views

**Files:** every `src/QuickerPlaces/Views/*.xaml`; `Views/MessageForm.xaml.cs`

- [ ] **Step 1: Run the mechanical rename** (PowerShell; brushes become `DynamicResource` with their new names; `Font.Mono` stays `StaticResource`)

```powershell
$map = [ordered]@{
  'Bg.Elevated'='Bg.Raised'; 'Bg.Panel'='Bg.Panel'; 'Bg.Base'='Bg.Base';
  'Border.Default'='Border.Default'; 'Border.Strong'='Border.Strong';
  'Text.Primary'='Text.Primary'; 'Text.Secondary'='Text.Secondary'; 'Text.Faint'='Text.Tertiary';
  'Accent.Hover'='Highlight.Text'; 'Accent.Pressed'='Highlight.Hover'; 'Accent.Soft'='Highlight.Soft'; 'Accent'='Highlight';
  'Status.Success'='Highlight.Text'; 'Status.Warning'='Signal'; 'Status.Error'='Danger'; 'Status.Info'='Info';
  'Favourite.Star'='Signal'
}
Get-ChildItem C:\QuickerPlaces\src\QuickerPlaces\Views\*.xaml | ForEach-Object {
  $t = [IO.File]::ReadAllText($_.FullName)
  $t = [regex]::Replace($t, '\{StaticResource ((?:Bg|Border|Text|Accent|Status|Favourite)(?:\.[A-Za-z]+)?)\}', {
    param($m) $key = $m.Groups[1].Value
    if (-not $map.Contains($key)) { throw "Unmapped key $key in $($_.Name)" }
    '{DynamicResource ' + $map[$key] + '}' })
  [IO.File]::WriteAllText($_.FullName, $t)
}
```

- [ ] **Step 2: Update MessageForm's colours in code** — `Views/MessageForm.xaml.cs`:

Replace `IconBadge.Background = BrushFor(icon);` with:

```csharp
            var brush = BrushFor(icon);
            IconBadge.BorderBrush = brush;
            IconGlyph.Foreground = brush;
```

Replace the `GlyphFor` error arm `MessageFormIcon.Error => "X",` with `MessageFormIcon.Error => "\u00D7",` and `BrushFor` with:

```csharp
    private Brush BrushFor(MessageFormIcon icon) => icon switch
    {
        MessageFormIcon.Info => (Brush)FindResource("Info"),
        MessageFormIcon.Warning => (Brush)FindResource("Signal"),
        MessageFormIcon.Error => (Brush)FindResource("Danger"),
        MessageFormIcon.Question => (Brush)FindResource("Highlight.Text"),
        _ => Brushes.Transparent
    };
```

- [ ] **Step 3: Check nothing old remains**

```bash
grep -rnE "Bg\.Glow|Bg\.Elevated|Text\.Faint|Accent|Status\.|Favourite\.Star|Font\.Icons|TextBlock\.Glyph|Button\.Bubble|StaticResource (Bg|Text|Border|Highlight|Signal|Danger|Info)" C:/QuickerPlaces/src/QuickerPlaces --include=*.xaml --include=*.cs | grep -v /obj/
```

Expected: only `TextBlock.Glyph`, `Font.Icons` and `Button.Bubble` hits in `MainWindow.xaml` and `RecentlyDeletedDialog.xaml` (rewritten in Tasks 12 and 15). Any other hit: fix it by hand with the same mapping.

- [ ] **Step 4: Commit**

```bash
git -C C:/QuickerPlaces commit -am "Rename view colour keys to the new palette"
```

### Task 12: Main window

**Files:**
- Modify: `src/QuickerPlaces/Views/MainWindow.xaml` (replace the whole file)
- Modify: `src/QuickerPlaces/ViewModels/MainViewModel.cs` (`PlacesHeader`, `EmptyGridMessage`, copy message)

- [ ] **Step 1: Replace MainWindow.xaml**

```xml
<Window x:Class="QuickerPlaces.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:converters="clr-namespace:QuickerPlaces.Converters"
        mc:Ignorable="d"
        Title="{Binding AppName}"
        Height="650" Width="1000"
        MinHeight="200" MinWidth="700"
        WindowStartupLocation="CenterScreen"
        d:DesignHeight="650" d:DesignWidth="1000">

    <Window.Resources>
        <converters:BoolToGridLengthConverter x:Key="BoolToGridLengthConverter" />
        <converters:CollectionCountToVisibilityConverter x:Key="CollectionCountToVisibilityConverter" />
        <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />

        <!--
            Shared per-row / per-card context menu. A ContextMenu opens in its
            own Popup, outside the tree it's attached to, so every binding
            goes through PlacementTarget: Tag on the row or card is the
            window's MainViewModel (the Command half), and
            PlacementTarget.DataContext is the PlaceViewModel (the parameter).
        -->
        <ContextMenu x:Key="PlaceContextMenu">
            <MenuItem Header="Open" FontWeight="Bold"
                      Command="{Binding PlacementTarget.Tag.OpenCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
            <MenuItem Header="Copy folder or link" InputGestureText="Ctrl+C"
                      Command="{Binding PlacementTarget.Tag.CopyResourceCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
            <MenuItem Header="Rename" InputGestureText="F2"
                      Command="{Binding PlacementTarget.Tag.RenameAliasCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
            <MenuItem Header="Edit folder or link" InputGestureText="Ctrl+E"
                      Command="{Binding PlacementTarget.Tag.EditResourceCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
            <MenuItem Header="Toggle favourite" InputGestureText="Ctrl+D"
                      Command="{Binding PlacementTarget.Tag.ToggleFavouriteCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
            <Separator />
            <!-- No confirmation (roadmap §4.8): the tooltip says where it goes and how to get it back. -->
            <MenuItem Header="Remove" InputGestureText="Delete"
                      ToolTip="Moves this place to Recently Deleted. Ctrl+Z puts it back, or restore it from Recently Deleted within 7 days."
                      Command="{Binding PlacementTarget.Tag.RemoveCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
        </ContextMenu>

        <!-- SI §6.4: a favourite card's own menu offers removal directly. -->
        <ContextMenu x:Key="BubbleContextMenu">
            <MenuItem Header="Open" FontWeight="Bold"
                      Command="{Binding PlacementTarget.Tag.OpenCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
            <MenuItem Header="Copy folder or link"
                      Command="{Binding PlacementTarget.Tag.CopyResourceCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
            <MenuItem Header="Remove from favourites"
                      Command="{Binding PlacementTarget.Tag.ToggleFavouriteCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
        </ContextMenu>
    </Window.Resources>

    <!--
        Window-wide shortcuts. Row-level ones (Enter, F2, Ctrl+E, Ctrl+D,
        Delete) live in PlacesGrid_PreviewKeyDown so they act only on the
        selected row and never fire while typing in the search box.
    -->
    <Window.InputBindings>
        <KeyBinding Modifiers="Ctrl" Key="N" Command="{Binding AddFolderCommand}" />
        <KeyBinding Modifiers="Ctrl" Key="U" Command="{Binding AddUrlCommand}" />
        <KeyBinding Modifiers="Ctrl" Key="H" Command="{Binding ToggleGridCommand}" />
        <KeyBinding Modifiers="Ctrl" Key="Z" Command="{Binding UndoRemoveCommand}" />
        <KeyBinding Modifiers="Ctrl" Key="D1" Command="{Binding OpenFavouriteAtCommand}" CommandParameter="0" />
        <KeyBinding Modifiers="Ctrl" Key="D2" Command="{Binding OpenFavouriteAtCommand}" CommandParameter="1" />
        <KeyBinding Modifiers="Ctrl" Key="D3" Command="{Binding OpenFavouriteAtCommand}" CommandParameter="2" />
        <KeyBinding Modifiers="Ctrl" Key="D4" Command="{Binding OpenFavouriteAtCommand}" CommandParameter="3" />
        <KeyBinding Modifiers="Ctrl" Key="D5" Command="{Binding OpenFavouriteAtCommand}" CommandParameter="4" />
        <KeyBinding Modifiers="Ctrl" Key="D6" Command="{Binding OpenFavouriteAtCommand}" CommandParameter="5" />
        <KeyBinding Modifiers="Ctrl" Key="D7" Command="{Binding OpenFavouriteAtCommand}" CommandParameter="6" />
        <KeyBinding Modifiers="Ctrl" Key="D8" Command="{Binding OpenFavouriteAtCommand}" CommandParameter="7" />
        <KeyBinding Modifiers="Ctrl" Key="D9" Command="{Binding OpenFavouriteAtCommand}" CommandParameter="8" />
    </Window.InputBindings>

    <Window.CommandBindings>
        <CommandBinding Command="ApplicationCommands.Find" Executed="Find_Executed" />
    </Window.CommandBindings>

    <Grid x:Name="RootGrid" Margin="20,18,20,16">
        <Grid.RowDefinitions>
            <!-- 0: unsaved-changes banner; 1: header; 2: trim band; 3: favourites; 4: list header -->
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <!--
                5: the places table. A RowDefinition has no DataContext of its
                own, so the collapse binding goes through the root Grid by name.
            -->
            <RowDefinition Height="{Binding DataContext.IsGridExpanded, ElementName=RootGrid, Converter={StaticResource BoolToGridLengthConverter}}" />
            <!-- 6: status bar; collapses when there's no message. -->
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <!--
            Unsaved-changes banner (plan 5.2). Never takes focus: it can
            appear mid-interaction, and nothing inside it calls Focus().
        -->
        <Border Grid.Row="0" Margin="0,0,0,12" Padding="14,10,10,10" CornerRadius="8"
                Background="{DynamicResource Bg.Raised}" BorderBrush="{DynamicResource Signal}" BorderThickness="1"
                Focusable="False"
                Visibility="{Binding HasUnsavedChanges, Converter={StaticResource BooleanToVisibilityConverter}}">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Warning}" Stroke="{DynamicResource Signal}" Margin="0,0,12,0" />
                <TextBlock Grid.Column="1" Text="{Binding PersistenceMessage}" TextWrapping="Wrap" VerticalAlignment="Center" />
                <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center" Margin="12,0,0,0">
                    <Button Content="Retry" Command="{Binding RetrySaveCommand}" Style="{StaticResource Button.Primary}"
                            Height="28" Padding="10,0" FontSize="13" Margin="0,0,8,0" />
                    <Button Content="Show data folder" Command="{Binding OpenDataFolderCommand}" Style="{StaticResource Button.Compact}" Margin="0,0,8,0" />
                    <Button Content="Show log" Command="{Binding ShowLogCommand}" Style="{StaticResource Button.Compact}" />
                </StackPanel>
            </Grid>
        </Border>

        <!-- Header: monogram, name and subheader, main actions -->
        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <Border Style="{StaticResource Border.Monogram}">
                <TextBlock Text="{Binding Monogram}" FontSize="16" FontWeight="ExtraBold" Foreground="{DynamicResource On.Brand}"
                           HorizontalAlignment="Center" VerticalAlignment="Center" />
            </Border>
            <StackPanel Grid.Column="1" Margin="14,0,0,0" VerticalAlignment="Center" ClipToBounds="True">
                <TextBlock Text="{Binding AppName}" Style="{StaticResource TextBlock.WindowTitle}" />
                <TextBlock Text="{Binding SubHeaderText}" Foreground="{DynamicResource Text.Secondary}" Margin="0,2,0,0"
                           TextTrimming="CharacterEllipsis" />
            </StackPanel>
            <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
                <Button x:Name="ActivityButton" Click="ActivityButton_Click" Margin="0,0,8,0">
                    <StackPanel Orientation="Horizontal">
                        <Grid Width="16" Height="16">
                            <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Clock}" />
                            <Ellipse x:Name="ActivityDot" Width="7" Height="7" Margin="0,-2,-3,0"
                                     Fill="{DynamicResource Signal}" Stroke="{DynamicResource Bg.Raised}" StrokeThickness="2"
                                     HorizontalAlignment="Right" VerticalAlignment="Top" Visibility="Collapsed" />
                        </Grid>
                        <TextBlock Text="Recents" Margin="7,0,0,0" VerticalAlignment="Center" />
                    </StackPanel>
                </Button>
                <Button Command="{Binding AddFolderCommand}" Margin="0,0,8,0" ToolTip="Add folder (Ctrl+N)">
                    <StackPanel Orientation="Horizontal">
                        <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Plus}" />
                        <TextBlock Text="Add folder" Margin="7,0,0,0" VerticalAlignment="Center" />
                    </StackPanel>
                </Button>
                <Button Command="{Binding AddUrlCommand}" Style="{StaticResource Button.Primary}" ToolTip="Add link (Ctrl+U)">
                    <StackPanel Orientation="Horizontal">
                        <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Plus}" />
                        <TextBlock Text="Add link" Margin="7,0,0,0" VerticalAlignment="Center" />
                    </StackPanel>
                </Button>
            </StackPanel>
        </Grid>

        <!-- The silver rub strip, bleeding past the window padding. -->
        <Control Grid.Row="2" Style="{StaticResource TrimBand}" Margin="-20,16,-20,14" />

        <!--
            Favourites (SI §6.4): leather cards; click opens, drag reorders
            (Bubble_PreviewMouseMove starts the drag, FavouritesItemsControl_Drop
            calls MainViewModel.MoveFavourite).
        -->
        <StackPanel Grid.Row="3" Margin="0,0,0,10">
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="FAVOURITES" Style="{StaticResource TextBlock.Caps}" />
                <TextBlock Text="Ctrl+1 to Ctrl+9 open them from the keyboard" Style="{StaticResource TextBlock.Hint}"
                           Foreground="{DynamicResource Text.Tertiary}" Margin="10,0,0,0" VerticalAlignment="Center" />
            </StackPanel>
            <Grid Margin="0,8,0,0">
                <TextBlock Text="Right-click a place below and choose &quot;Toggle favourite&quot; to pin it here."
                           Foreground="{DynamicResource Text.Tertiary}" Margin="0,0,0,8"
                           Visibility="{Binding FavouritePlaces.Count, Converter={StaticResource CollectionCountToVisibilityConverter}}" />
                <ItemsControl x:Name="FavouritesItemsControl" ItemsSource="{Binding FavouritePlaces}"
                              AllowDrop="True" DragOver="FavouritesItemsControl_DragOver" Drop="FavouritesItemsControl_Drop">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <WrapPanel Orientation="Horizontal" />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Button Style="{StaticResource Button.Favourite}"
                                    ToolTip="{Binding ToolTipText}"
                                    Tag="{Binding RelativeSource={RelativeSource AncestorType=ItemsControl}, Path=DataContext}"
                                    ContextMenu="{StaticResource BubbleContextMenu}"
                                    Command="{Binding RelativeSource={RelativeSource AncestorType=ItemsControl}, Path=DataContext.OpenCommand}"
                                    CommandParameter="{Binding}"
                                    AutomationProperties.Name="{Binding Alias}"
                                    PreviewMouseLeftButtonDown="Bubble_PreviewMouseLeftButtonDown"
                                    PreviewMouseMove="Bubble_PreviewMouseMove">
                                <StackPanel Orientation="Horizontal">
                                    <Border>
                                        <Border.Style>
                                            <Style TargetType="Border" BasedOn="{StaticResource Border.FavouriteNumber}">
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding FavouriteShortcut}" Value="{x:Null}">
                                                        <Setter Property="Visibility" Value="Collapsed" />
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </Border.Style>
                                        <TextBlock Text="{Binding FavouriteShortcut}" FontSize="12" FontWeight="Bold"
                                                   Foreground="{DynamicResource On.Leather.Badge}"
                                                   HorizontalAlignment="Center" VerticalAlignment="Center" />
                                    </Border>
                                    <Path Style="{StaticResource Icon.PlaceType}" />
                                    <TextBlock Text="{Binding Alias}" Margin="7,0,0,0" VerticalAlignment="Center" />
                                </StackPanel>
                            </Button>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </Grid>
        </StackPanel>

        <!--
            List header: count, search, options, hide/show. The search box
            stays while the list is collapsed; typing re-expands it.
        -->
        <Grid Grid.Row="4" Margin="0,0,0,10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                <TextBlock Text="ALL PLACES" Style="{StaticResource TextBlock.Caps}" />
                <ContentControl Style="{StaticResource Badge.Count}" Content="{Binding PlaceCountText}" Margin="10,0,0,0" />
            </StackPanel>

            <Border Grid.Column="1" Margin="16,0,8,0" MaxWidth="360" MinWidth="220" HorizontalAlignment="Right"
                    Height="34" CornerRadius="6" BorderThickness="1" Padding="10,0,5,0"
                    Background="{DynamicResource Bg.Sunken}">
                <Border.Style>
                    <Style TargetType="Border">
                        <Setter Property="BorderBrush" Value="{DynamicResource Border.Strong}" />
                        <Style.Triggers>
                            <Trigger Property="IsKeyboardFocusWithin" Value="True">
                                <Setter Property="BorderBrush" Value="{DynamicResource Highlight.Text}" />
                            </Trigger>
                        </Style.Triggers>
                    </Style>
                </Border.Style>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Search}" Stroke="{DynamicResource Text.Tertiary}" />
                    <TextBox x:Name="SearchBox" Grid.Column="1" Margin="8,0,0,0" Style="{StaticResource TextBox.Bare}"
                             Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"
                             PreviewKeyDown="SearchBox_PreviewKeyDown"
                             AutomationProperties.Name="Search places"
                             ToolTip="Search by name, folder or link (Ctrl+F). Enter opens the top result; Down moves into the list; Esc clears." />
                    <!-- Placeholder: hit-test invisible so clicks land on the TextBox. -->
                    <TextBlock Grid.Column="1" Text="Search by name, folder or link" IsHitTestVisible="False"
                               Margin="8,0,0,0" VerticalAlignment="Center" Foreground="{DynamicResource Text.Tertiary}">
                        <TextBlock.Style>
                            <Style TargetType="TextBlock">
                                <Setter Property="Visibility" Value="Collapsed" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding Text.Length, ElementName=SearchBox}" Value="0">
                                        <Setter Property="Visibility" Value="Visible" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </TextBlock.Style>
                    </TextBlock>
                    <ContentControl Grid.Column="2" Content="Ctrl F" Margin="6,0,0,0">
                        <ContentControl.Style>
                            <Style TargetType="ContentControl" BasedOn="{StaticResource Kbd}">
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding IsSearching}" Value="True">
                                        <Setter Property="Visibility" Value="Collapsed" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </ContentControl.Style>
                    </ContentControl>
                    <Button Grid.Column="3" Style="{StaticResource Button.IconOnlyCompact}" Width="24" Height="24" Margin="4,0,0,0"
                            Command="{Binding ClearSearchCommand}" ToolTip="Clear search (Esc)" AutomationProperties.Name="Clear search"
                            Visibility="{Binding IsSearching, Converter={StaticResource BooleanToVisibilityConverter}}">
                        <Viewbox Width="11" Height="11">
                            <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Close}" />
                        </Viewbox>
                    </Button>
                </Grid>
            </Border>

            <StackPanel Grid.Column="2" Orientation="Horizontal">
                <Button x:Name="OptionsButton" Style="{StaticResource Button.IconOnly}" Click="OptionsButton_Click" Margin="0,0,8,0"
                        ToolTip="Options" AutomationProperties.Name="Options">
                    <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Menu}" />
                    <Button.ContextMenu>
                        <ContextMenu Placement="Bottom">
                            <MenuItem Header="Settings" Click="SettingsButton_Click">
                                <MenuItem.Icon>
                                    <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Settings}" />
                                </MenuItem.Icon>
                            </MenuItem>
                            <MenuItem Header="Recently Deleted" ToolTip="Places you removed in the last 7 days"
                                      Command="{Binding PlacementTarget.DataContext.ShowRecentlyDeletedCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}">
                                <MenuItem.Icon>
                                    <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Trash}" />
                                </MenuItem.Icon>
                            </MenuItem>
                            <Separator />
                            <MenuItem Header="Places file" ToolTip="Open the folder containing places.json and select the file"
                                      Command="{Binding PlacementTarget.DataContext.OpenDataFolderCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}">
                                <MenuItem.Icon>
                                    <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Folder}" />
                                </MenuItem.Icon>
                            </MenuItem>
                            <MenuItem Header="Import places"
                                      Command="{Binding PlacementTarget.DataContext.ImportCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
                            <MenuItem Header="Export places"
                                      Command="{Binding PlacementTarget.DataContext.ExportCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
                        </ContextMenu>
                    </Button.ContextMenu>
                </Button>
                <Button Command="{Binding ToggleGridCommand}" ToolTip="Hide or show the list (Ctrl+H)">
                    <Button.Style>
                        <Style TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
                            <Setter Property="Content" Value="Hide list" />
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding IsGridExpanded}" Value="False">
                                    <Setter Property="Content" Value="Show list" />
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </Button.Style>
                </Button>
            </StackPanel>
        </Grid>

        <!-- Places table (SI §6.3), collapsible through Row 5's bound height. -->
        <Grid Grid.Row="5">
            <DataGrid x:Name="PlacesGrid"
                      ItemsSource="{Binding PlacesView}"
                      AutoGenerateColumns="False"
                      IsReadOnly="True"
                      CanUserAddRows="False"
                      SelectionMode="Single"
                      PreviewKeyDown="PlacesGrid_PreviewKeyDown"
                      Sorting="PlacesGrid_Sorting">
                <DataGrid.RowStyle>
                    <Style TargetType="DataGridRow" BasedOn="{StaticResource {x:Type DataGridRow}}">
                        <Setter Property="Tag" Value="{Binding RelativeSource={RelativeSource AncestorType=DataGrid}, Path=DataContext}" />
                        <Setter Property="ContextMenu" Value="{StaticResource PlaceContextMenu}" />
                        <EventSetter Event="MouseDoubleClick" Handler="Row_MouseDoubleClick" />
                    </Style>
                </DataGrid.RowStyle>
                <DataGrid.Columns>
                    <!--
                        Every SortMemberPath is a PlaceSortKey name read by
                        PlacesGrid_Sorting (Phase 3 D29). No Type column: the
                        icon shows it, and a remembered Type sort is dropped
                        (PlaceSort.ForPlacesGrid). Widths are proportional with
                        minimums for the 700 px window.

                        Name: type icon, name (15 px), and the star straight
                        after it. The star is Signal on a favourite, always; on
                        any other place an outline appears only on the hovered
                        or selected row. Not focusable; Ctrl+D is the keyboard
                        route.
                    -->
                    <DataGridTemplateColumn Header="NAME" SortMemberPath="Alias" Width="2.4*" MinWidth="140">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <DockPanel VerticalAlignment="Center">
                                    <Path DockPanel.Dock="Left" Style="{StaticResource Icon.RowPlaceType}" />
                                    <DockPanel HorizontalAlignment="Left">
                                        <Button DockPanel.Dock="Right" Margin="4,0,0,0"
                                                Command="{Binding DataContext.ToggleFavouriteCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}"
                                                CommandParameter="{Binding}"
                                                ToolTip="{Binding FavouriteToolTip}"
                                                AutomationProperties.Name="{Binding FavouriteToolTip}">
                                            <Button.Style>
                                                <Style TargetType="Button" BasedOn="{StaticResource Button.GridIcon}">
                                                    <Setter Property="Visibility" Value="Hidden" />
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=DataGridRow}}" Value="True">
                                                            <Setter Property="Visibility" Value="Visible" />
                                                        </DataTrigger>
                                                        <DataTrigger Binding="{Binding IsSelected, RelativeSource={RelativeSource AncestorType=DataGridRow}}" Value="True">
                                                            <Setter Property="Visibility" Value="Visible" />
                                                        </DataTrigger>
                                                        <DataTrigger Binding="{Binding IsFavourite}" Value="True">
                                                            <Setter Property="Visibility" Value="Visible" />
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </Button.Style>
                                            <Path Data="{StaticResource Icon.Star}">
                                                <Path.Style>
                                                    <!-- Later triggers win: a favourite stays Signal whether hovered or selected. -->
                                                    <Style TargetType="Path" BasedOn="{StaticResource Icon}">
                                                        <Setter Property="Stroke" Value="{DynamicResource Text.Tertiary}" />
                                                        <Setter Property="Fill" Value="Transparent" />
                                                        <Style.Triggers>
                                                            <DataTrigger Binding="{Binding IsSelected, RelativeSource={RelativeSource AncestorType=DataGridRow}}" Value="True">
                                                                <Setter Property="Stroke" Value="{DynamicResource On.Highlight}" />
                                                            </DataTrigger>
                                                            <DataTrigger Binding="{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=Button}}" Value="True">
                                                                <Setter Property="Stroke" Value="{DynamicResource Text.Primary}" />
                                                            </DataTrigger>
                                                            <DataTrigger Binding="{Binding IsFavourite}" Value="True">
                                                                <Setter Property="Stroke" Value="{DynamicResource Signal}" />
                                                                <Setter Property="Fill" Value="{DynamicResource Signal}" />
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </Path.Style>
                                            </Path>
                                        </Button>
                                        <TextBlock Text="{Binding Alias}" FontSize="15" FontWeight="Medium"
                                                   VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                                    </DockPanel>
                                </DockPanel>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>
                    <DataGridTextColumn Header="FOLDER OR LINK" Binding="{Binding Resource}" SortMemberPath="Destination"
                                        Width="3*" MinWidth="160" ElementStyle="{StaticResource Cell.Path}" />
                    <!-- Formatted in the view model with the user's regional settings (D31). -->
                    <DataGridTextColumn Header="LAST OPENED" Binding="{Binding LastOpenedText}" SortMemberPath="LastOpened"
                                        Width="1.45*" MinWidth="128" MaxWidth="180" ElementStyle="{StaticResource Cell.Text}" />
                    <DataGridTextColumn Header="OPENS" Binding="{Binding OpenCount}" SortMemberPath="Opens"
                                        Width="0.6*" MinWidth="62" MaxWidth="90"
                                        HeaderStyle="{StaticResource DataGridColumnHeader.Centered}"
                                        ElementStyle="{StaticResource Cell.Centered}" />
                </DataGrid.Columns>
            </DataGrid>

            <!-- Explains an empty table: nothing saved yet, or nothing matches the search. -->
            <TextBlock Text="{Binding EmptyGridMessage}" IsHitTestVisible="False"
                       HorizontalAlignment="Center" VerticalAlignment="Center" Margin="16,48,16,0"
                       TextWrapping="Wrap" TextAlignment="Center" Foreground="{DynamicResource Text.Tertiary}">
                <TextBlock.Style>
                    <Style TargetType="TextBlock">
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding EmptyGridMessage}" Value="{x:Null}">
                                <Setter Property="Visibility" Value="Collapsed" />
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </TextBlock.Style>
            </TextBlock>
        </Grid>

        <!--
            Status bar: a short confirmation after Remove (with Undo), Undo,
            Copy or a restore. Hidden 10 s after a message offering Undo, 8 s
            after any other (D19); held open while the pointer or keyboard
            focus is inside it. Never takes focus.
        -->
        <Border x:Name="StatusBar" Grid.Row="6" Margin="0,10,0,0" Height="38" Padding="14,0,5,0" CornerRadius="8"
                Background="{DynamicResource Bg.Raised}" BorderBrush="{DynamicResource Border.Default}" BorderThickness="1"
                Focusable="False"
                MouseEnter="StatusBar_MouseEnter"
                MouseLeave="StatusBar_MouseLeave"
                IsKeyboardFocusWithinChanged="StatusBar_IsKeyboardFocusWithinChanged">
            <Border.Style>
                <Style TargetType="Border">
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding StatusMessage}" Value="{x:Null}">
                            <Setter Property="Visibility" Value="Collapsed" />
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </Border.Style>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Check}" Stroke="{DynamicResource Highlight.Text}" Margin="0,0,10,0" />
                <TextBlock Grid.Column="1" Text="{Binding StatusMessage}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                <Button Grid.Column="2" Style="{StaticResource Button.Compact}" Margin="8,0,0,0"
                        Command="{Binding UndoRemoveCommand}" ToolTip="Restore it (Ctrl+Z)"
                        Visibility="{Binding StatusOffersUndo, Converter={StaticResource BooleanToVisibilityConverter}}">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="Undo" VerticalAlignment="Center" />
                        <ContentControl Style="{StaticResource Kbd}" Content="Ctrl Z" Margin="7,0,0,0" />
                    </StackPanel>
                </Button>
                <Button Grid.Column="3" Style="{StaticResource Button.IconOnlyCompact}" Margin="6,0,0,0"
                        Command="{Binding DismissStatusCommand}" ToolTip="Dismiss" AutomationProperties.Name="Dismiss">
                    <Viewbox Width="12" Height="12">
                        <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Close}" />
                    </Viewbox>
                </Button>
            </Grid>
        </Border>
    </Grid>
</Window>
```

- [ ] **Step 2: Update MainViewModel's texts**

Replace `PlacesHeader` (and its comment, lines 222–225) with:

```csharp
    /// <summary>The count beside ALL PLACES: "12", or "3 of 12" while a search is narrowing the list.</summary>
    public string PlaceCountText => IsSearching
        ? $"{VisiblePlaceCount} of {Places.Count}"
        : Places.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
```

Replace every `nameof(PlacesHeader)` in the file with `nameof(PlaceCountText)` (one at line 723).

In `EmptyGridMessage`, replace the two "No places yet" strings with:

```csharp
                    ? "No places yet. Use Add folder (Ctrl+N) or Add link (Ctrl+U) to save your first one. Places you removed are in Recently Deleted."
                    : "No places yet. Use Add folder (Ctrl+N) or Add link (Ctrl+U) to save your first one.";
```

At line 589, replace the copy message with:

```csharp
            ShowStatus($"Copied {(place.Type == PlaceType.Folder ? "the folder path" : "the link")} of \"{place.Alias}\".");
```

In `Views/MainWindow.xaml.cs` line 255, replace `open Options next to Hide List and choose Settings` with `open Options next to Hide list and choose Settings`.

- [ ] **Step 3: Build**

Run: `dotnet build QuickerPlaces.sln -nologo -v q`
Expected: remaining errors only from `RecentlyDeletedDialog.xaml` (`TypeGlyph`, `TextBlock.Glyph`) and from `SettingsDialog`/`ActivityWindow` if Task 11 left anything. Fix those in Tasks 14–15; if `MainWindow` itself errors, fix it now.

- [ ] **Step 4: Commit**

```bash
git -C C:/QuickerPlaces commit -am "Restyle the main window"
```

### Task 13: Settings dialog with Appearance

**Files:**
- Modify: `src/QuickerPlaces/Views/SettingsDialog.xaml` (replace the whole file)
- Modify: `src/QuickerPlaces/Views/SettingsDialog.xaml.cs`
- Modify: `src/QuickerPlaces/Views/MainWindow.xaml.cs` (`SettingsButton_Click`, `ApplySettingsChoice`)

- [ ] **Step 1: Replace SettingsDialog.xaml**

```xml
<Window x:Class="QuickerPlaces.Views.SettingsDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Settings"
        ResizeMode="NoResize"
        ShowInTaskbar="False"
        SizeToContent="Height"
        Width="480"
        WindowStartupLocation="CenterOwner">
    <!--
        App settings: appearance (previewed live, reverted on Cancel), the
        global hotkey, and the tray and Windows startup opt-ins. Window size
        and the list's collapsed state are remembered automatically.
    -->
    <StackPanel Margin="24,22,24,20">
        <TextBlock Text="APPEARANCE" Style="{StaticResource TextBlock.Caps}" HorizontalAlignment="Left" />

        <TextBlock Text="Theme" Style="{StaticResource TextBlock.FieldLabel}" Margin="0,12,0,8" />
        <Border Style="{StaticResource Border.SegmentGroup}">
            <UniformGrid Rows="1">
                <RadioButton x:Name="ThemeLight" GroupName="Theme" Style="{StaticResource RadioButton.Segment}" Tag="5,0,0,5"
                             Checked="Appearance_Changed" AutomationProperties.Name="Light theme">
                    <StackPanel Orientation="Horizontal">
                        <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Sun}" />
                        <TextBlock Text="Light" Margin="7,0,0,0" VerticalAlignment="Center" />
                    </StackPanel>
                </RadioButton>
                <RadioButton x:Name="ThemeDark" GroupName="Theme" Style="{StaticResource RadioButton.Segment}" Tag="0"
                             Checked="Appearance_Changed" AutomationProperties.Name="Dark theme">
                    <StackPanel Orientation="Horizontal">
                        <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Moon}" />
                        <TextBlock Text="Dark" Margin="7,0,0,0" VerticalAlignment="Center" />
                    </StackPanel>
                </RadioButton>
                <RadioButton x:Name="ThemeSystem" GroupName="Theme" Style="{StaticResource RadioButton.Segment}" Tag="0,5,5,0"
                             BorderThickness="0" Checked="Appearance_Changed" AutomationProperties.Name="Match the Windows theme">
                    <StackPanel Orientation="Horizontal">
                        <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Monitor}" />
                        <TextBlock Text="Match Windows" Margin="7,0,0,0" VerticalAlignment="Center" />
                    </StackPanel>
                </RadioButton>
            </UniformGrid>
        </Border>

        <TextBlock Text="Highlight colour" Style="{StaticResource TextBlock.FieldLabel}" Margin="0,20,0,2" />
        <TextBlock Text="Colours the main button, selected rows and switches." Style="{StaticResource TextBlock.Hint}" />
        <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
            <RadioButton x:Name="HighlightGreen" GroupName="Highlight" Style="{StaticResource RadioButton.Swatch}"
                         Background="{DynamicResource Preset.Green}" Checked="Appearance_Changed" AutomationProperties.Name="Hull green">
                <TextBlock Text="Hull green" TextWrapping="Wrap" TextAlignment="Center" />
            </RadioButton>
            <RadioButton x:Name="HighlightBlue" GroupName="Highlight" Style="{StaticResource RadioButton.Swatch}"
                         Background="{DynamicResource Preset.Blue}" Checked="Appearance_Changed" AutomationProperties.Name="Steel blue">
                <TextBlock Text="Steel blue" TextWrapping="Wrap" TextAlignment="Center" />
            </RadioButton>
            <RadioButton x:Name="HighlightRed" GroupName="Highlight" Style="{StaticResource RadioButton.Swatch}"
                         Background="{DynamicResource Preset.Red}" Checked="Appearance_Changed" AutomationProperties.Name="Tail red">
                <TextBlock Text="Tail red" TextWrapping="Wrap" TextAlignment="Center" />
            </RadioButton>
            <RadioButton x:Name="HighlightCognac" GroupName="Highlight" Style="{StaticResource RadioButton.Swatch}"
                         Background="{DynamicResource Preset.Cognac}" Checked="Appearance_Changed" AutomationProperties.Name="Cognac">
                <TextBlock Text="Cognac" TextWrapping="Wrap" TextAlignment="Center" />
            </RadioButton>
            <RadioButton x:Name="HighlightWindows" GroupName="Highlight" Style="{StaticResource RadioButton.Swatch}" Tag="windows"
                         Background="{DynamicResource Bg.Raised}" Checked="Appearance_Changed" AutomationProperties.Name="Windows accent colour">
                <TextBlock Text="Windows accent" TextWrapping="Wrap" TextAlignment="Center" />
            </RadioButton>
        </StackPanel>

        <Separator />

        <TextBlock Text="SHORTCUT" Style="{StaticResource TextBlock.Caps}" HorizontalAlignment="Left" />
        <TextBlock Text="Bring QuickerPlaces to the front" Style="{StaticResource TextBlock.FieldLabel}" Margin="0,12,0,2" />
        <TextBlock Text="Works from any app while QuickerPlaces is running. Click the box, then press the keys you want, including Ctrl, Alt or Win."
                   Style="{StaticResource TextBlock.Hint}" />
        <!--
            Read-only so typing never inserts characters: the key handler
            turns each key press into a hotkey. Tab, and Enter/Esc with no
            modifiers, are let through so the dialog stays keyboard-usable.
        -->
        <TextBox x:Name="HotkeyBox" Margin="0,10,0,0"
                 IsReadOnly="True" IsReadOnlyCaretVisible="False"
                 PreviewKeyDown="HotkeyBox_PreviewKeyDown"
                 PreviewKeyUp="HotkeyBox_PreviewKeyUp"
                 AutomationProperties.Name="Global shortcut"
                 ToolTip="Press a key combination. Backspace turns the shortcut off." />
        <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
            <Button Content="Reset to default" Style="{StaticResource Button.Compact}" Margin="0,0,8,0" Click="ResetButton_Click" />
            <Button Content="Turn off" Style="{StaticResource Button.Compact}" Click="TurnOffButton_Click" />
        </StackPanel>

        <Separator />

        <TextBlock Text="TRAY" Style="{StaticResource TextBlock.Caps}" HorizontalAlignment="Left" />
        <CheckBox x:Name="MinimizeToTrayCheck" Content="Keep running in the tray when I close the window" Margin="0,12,0,10" />
        <CheckBox x:Name="StartWithWindowsCheck" Content="Start with Windows in the tray" />
        <TextBlock Text="Both are off by default. The tray menu has Open, Pause tracking and Exit."
                   Style="{StaticResource TextBlock.Hint}" Margin="0,8,0,0" />

        <TextBlock x:Name="ErrorText" Foreground="{DynamicResource Danger}" TextWrapping="Wrap"
                   Margin="0,14,0,0" Visibility="Collapsed" />

        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,22,0,0">
            <Button Content="Cancel" Margin="0,0,8,0" IsCancel="True" Click="CancelButton_Click" />
            <Button Content="Save" Style="{StaticResource Button.Primary}" IsDefault="True" Click="SaveButton_Click" />
        </StackPanel>
    </StackPanel>
</Window>
```

- [ ] **Step 2: Update SettingsDialog.xaml.cs**

Add `using QuickerPlaces.Models;` (already present) and replace the fields, constructor and `Show` with:

```csharp
    private readonly Func<SettingsChoice, string?> _tryApply;
    private readonly Action<AppTheme, HighlightPreset> _preview;
    private readonly bool _ready;
    private string _hotkeyText;

    private SettingsDialog(Window owner, string? currentHotkey, bool minimizeToTray, bool startWithWindows,
        AppTheme theme, HighlightPreset highlight, Action<AppTheme, HighlightPreset> preview,
        Func<SettingsChoice, string?> tryApply)
    {
        InitializeComponent();

        Owner = owner;
        _tryApply = tryApply;
        _preview = preview;
        _hotkeyText = HotkeyGesture.IsDisabled(currentHotkey) ? Disabled : currentHotkey!.Trim();
        ShowHotkey(_hotkeyText);
        MinimizeToTrayCheck.IsChecked = minimizeToTray;
        StartWithWindowsCheck.IsChecked = startWithWindows;

        ThemeButton(theme).IsChecked = true;
        HighlightButton(highlight).IsChecked = true;
        // Checked fires while the choices above are set; only the user's
        // changes should preview.
        _ready = true;

        Loaded += (_, _) => HotkeyBox.Focus();
    }

    /// <summary>The applied choices, set only when Save succeeded.</summary>
    public SettingsChoice? SavedSettings { get; private set; }

    /// <summary>
    /// Shows the dialog. <paramref name="preview"/> applies an appearance
    /// choice at once; the caller reverts it if this returns null.
    /// <paramref name="tryApply"/> is called with the choices on Save and
    /// returns an error message, or null once applied. Returns the saved
    /// choices, or null if cancelled.
    /// </summary>
    public static SettingsChoice? Show(Window owner, string? currentHotkey, bool minimizeToTray, bool startWithWindows,
        AppTheme theme, HighlightPreset highlight, Action<AppTheme, HighlightPreset> preview,
        Func<SettingsChoice, string?> tryApply)
    {
        var dialog = new SettingsDialog(owner, currentHotkey, minimizeToTray, startWithWindows, theme, highlight, preview, tryApply);
        dialog.ShowDialog();
        return dialog.SavedSettings;
    }

    private void Appearance_Changed(object sender, RoutedEventArgs e)
    {
        if (_ready)
            _preview(SelectedTheme(), SelectedHighlight());
    }

    private System.Windows.Controls.RadioButton ThemeButton(AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemeLight,
        AppTheme.System => ThemeSystem,
        _ => ThemeDark
    };

    private System.Windows.Controls.RadioButton HighlightButton(HighlightPreset preset) => preset switch
    {
        HighlightPreset.Blue => HighlightBlue,
        HighlightPreset.Red => HighlightRed,
        HighlightPreset.Cognac => HighlightCognac,
        HighlightPreset.Windows => HighlightWindows,
        _ => HighlightGreen
    };

    private AppTheme SelectedTheme()
        => ThemeLight.IsChecked == true ? AppTheme.Light
         : ThemeSystem.IsChecked == true ? AppTheme.System
         : AppTheme.Dark;

    private HighlightPreset SelectedHighlight()
        => HighlightBlue.IsChecked == true ? HighlightPreset.Blue
         : HighlightRed.IsChecked == true ? HighlightPreset.Red
         : HighlightCognac.IsChecked == true ? HighlightPreset.Cognac
         : HighlightWindows.IsChecked == true ? HighlightPreset.Windows
         : HighlightPreset.Green;
```

In `SaveButton_Click`, build the choice with the appearance:

```csharp
        var choice = new SettingsChoice(_hotkeyText,
            MinimizeToTrayCheck.IsChecked == true, StartWithWindowsCheck.IsChecked == true,
            SelectedTheme(), SelectedHighlight());
```

Replace the record at the end of the file:

```csharp
public sealed record SettingsChoice(string Hotkey, bool MinimizeToTray, bool StartWithWindows,
    AppTheme Theme, HighlightPreset Highlight);
```

- [ ] **Step 3: Wire it in MainWindow.xaml.cs**

In `SettingsButton_Click`, replace the `SettingsDialog.Show` call and the cancelled branch:

```csharp
        var saved = SettingsDialog.Show(this, _settings.GlobalHotkey,
            _settings.MinimizeToTray, _settings.StartWithWindows,
            ThemePreference.ParseTheme(_settings.Theme), ThemePreference.ParseHighlight(_settings.Highlight),
            _themeManager.Apply, ApplySettingsChoice);
        if (saved is null)
        {
            // Cancelled: put back what was there. If the hotkey fails again,
            // it was already failing before (and reported at startup).
            ApplyGlobalHotkey(_settings.GlobalHotkey);
            _themeManager.Apply(ThemePreference.ParseTheme(_settings.Theme), ThemePreference.ParseHighlight(_settings.Highlight));
            return;
        }
```

In `ApplySettingsChoice`, after `_settings.StartWithWindows = choice.StartWithWindows;` add:

```csharp
        _settings.Theme = ThemePreference.Format(choice.Theme);
        _settings.Highlight = ThemePreference.Format(choice.Highlight);
        _themeManager.Apply(choice.Theme, choice.Highlight);
```

- [ ] **Step 4: Build** — `dotnet build QuickerPlaces.sln -nologo -v q`. Expected: no errors from Settings or MainWindow.

- [ ] **Step 5: Commit**

```bash
git -C C:/QuickerPlaces commit -am "Add the Appearance section to Settings with live preview"
```

### Task 14: Recents window

**Files:**
- Modify: `src/QuickerPlaces/Views/ActivityWindow.xaml`

- [ ] **Step 1: Replace the `<Window.Resources>` block (lines 8–127)**

```xml
    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
        <Style x:Key="ActivityCellText.Centered" TargetType="TextBlock" BasedOn="{StaticResource Cell.Centered}">
            <Setter Property="HorizontalAlignment" Value="Stretch" />
        </Style>
        <Style x:Key="ActivityCellText.Folder" TargetType="TextBlock" BasedOn="{StaticResource Cell.Text}">
            <Setter Property="FontFamily" Value="{StaticResource Font.Mono}" />
            <Setter Property="FontSize" Value="12.5" />
        </Style>
        <Style x:Key="Button.CalendarYear" TargetType="Button">
            <Setter Property="Foreground" Value="{DynamicResource Text.Primary}" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="YearBackground" Background="Transparent" CornerRadius="4" Padding="{TemplateBinding Padding}">
                            <StackPanel Orientation="Horizontal">
                                <ContentPresenter VerticalAlignment="Center" />
                                <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.ChevronDown}" Margin="4,0,0,0" />
                            </StackPanel>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="YearBackground" Property="Background" Value="{DynamicResource Highlight.Soft}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <!--
            One day of the year strip. 12 px cells at a 14 px pitch, which
            ActivityCalendar's strip width and month markers assume (UI
            refresh U12). Leather heat: Heat.0 (none) to Heat.4 (most).
        -->
        <Style x:Key="Button.CalendarDay" TargetType="Button">
            <Setter Property="Width" Value="12" />
            <Setter Property="Height" Value="12" />
            <Setter Property="Margin" Value="1" />
            <Setter Property="Padding" Value="0" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="BorderBrush" Value="{DynamicResource Border.Default}" />
            <Setter Property="Background" Value="{DynamicResource Heat.0}" />
            <Setter Property="ToolTipService.ShowOnDisabled" Value="True" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Grid>
                            <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                                    BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="2" />
                            <Border x:Name="TodayOutline" Margin="-1" BorderBrush="{DynamicResource Signal}"
                                    BorderThickness="1.5" CornerRadius="3" Visibility="Collapsed" IsHitTestVisible="False" />
                        </Grid>
                        <ControlTemplate.Triggers>
                            <DataTrigger Binding="{Binding IsToday}" Value="True">
                                <Setter TargetName="TodayOutline" Property="Visibility" Value="Visible" />
                            </DataTrigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
            <Style.Triggers>
                <DataTrigger Binding="{Binding Intensity}" Value="-1">
                    <Setter Property="Background" Value="{DynamicResource Heat.Untracked}" />
                    <Setter Property="BorderThickness" Value="0" />
                </DataTrigger>
                <DataTrigger Binding="{Binding Intensity}" Value="1">
                    <Setter Property="Background" Value="{DynamicResource Heat.1}" />
                    <Setter Property="BorderThickness" Value="0" />
                </DataTrigger>
                <DataTrigger Binding="{Binding Intensity}" Value="2">
                    <Setter Property="Background" Value="{DynamicResource Heat.2}" />
                    <Setter Property="BorderThickness" Value="0" />
                </DataTrigger>
                <DataTrigger Binding="{Binding Intensity}" Value="3">
                    <Setter Property="Background" Value="{DynamicResource Heat.3}" />
                    <Setter Property="BorderThickness" Value="0" />
                </DataTrigger>
                <DataTrigger Binding="{Binding Intensity}" Value="4">
                    <Setter Property="Background" Value="{DynamicResource Heat.4}" />
                    <Setter Property="BorderThickness" Value="0" />
                </DataTrigger>
                <DataTrigger Binding="{Binding IsSelected}" Value="True">
                    <Setter Property="BorderBrush" Value="{DynamicResource Highlight.Text}" />
                    <Setter Property="BorderThickness" Value="2" />
                </DataTrigger>
                <DataTrigger Binding="{Binding IsInRange}" Value="False">
                    <Setter Property="Visibility" Value="Hidden" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
        <!-- Week / Month / Day: compact buttons whose on-state fills Highlight. -->
        <Style x:Key="Button.Period" TargetType="Button" BasedOn="{StaticResource Button.Compact}" />
        <Style x:Key="TrackedFolderCard" TargetType="ListBoxItem" BasedOn="{StaticResource ListBoxItem.Chip}">
            <Setter Property="ToolTip" Value="{Binding TrackingToolTip}" />
        </Style>
    </Window.Resources>
```

- [ ] **Step 2: Header, trim band and margins** — change `Title="Folder Activity"` to `Title="Recents"`; change `<Grid Margin="16">` to `<Grid Margin="20,18,20,16">`; replace the row-0 `StackPanel`'s first two TextBlocks with:

```xml
            <TextBlock Text="Recents" Style="{StaticResource TextBlock.WindowTitle}" />
            <TextBlock Text="Folders you open in File Explorer, day by day. Only the active Explorer window is tracked; return here to see new visits."
                       Foreground="{DynamicResource Text.Secondary}" TextWrapping="Wrap" Margin="0,3,0,0" />
```

and add as the last child of that `StackPanel` (after the Notice TextBlock):

```xml
            <Control Style="{StaticResource TrimBand}" Margin="-20,14,-20,0" />
```

Change that StackPanel's `Margin="0,0,0,8"` to `Margin="0,0,0,14"`.

- [ ] **Step 3: Tracked folders strip** — in the row-1 Border, set `Background="Transparent" BorderThickness="0" Padding="0"`; replace the "+ Folders" button with:

```xml
                <Button DockPanel.Dock="Right" Click="AddRoot_Click" Style="{StaticResource Button.Compact}"
                        IsEnabled="{Binding CanManage}" ToolTip="Add a folder to track" Margin="10,0,0,0">
                    <StackPanel Orientation="Horizontal">
                        <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Plus}" />
                        <TextBlock Text="Track a folder" Margin="7,0,0,0" VerticalAlignment="Center" />
                    </StackPanel>
                </Button>
```

replace the "Tracked folders" TextBlock with:

```xml
                <TextBlock DockPanel.Dock="Left" Text="TRACKED FOLDERS" Style="{StaticResource TextBlock.Caps}" Margin="0,0,12,0" />
```

and in the chip's DataTemplate replace the `&#xE8B7;` TextBlock with:

```xml
                                <Path Style="{StaticResource Icon}" Data="{StaticResource Icon.Folder}" Stroke="{DynamicResource Highlight.Text}" Margin="0,0,7,0" />
```

and give the path TextBlock `FontFamily="{StaticResource Font.Mono}" FontSize="12.5"`, and the "Paused" TextBlock `Foreground="{DynamicResource Text.Tertiary}" FontSize="12" FontWeight="SemiBold"`.

- [ ] **Step 4: Year panel** — change the "Year activity" TextBlock to `Text="YEAR ACTIVITY" Style="{StaticResource TextBlock.Caps}"`; give the Previous and Next buttons `Style="{StaticResource Button.Compact}"`; for each of Week, Month and Day change `BasedOn="{StaticResource {x:Type Button}}"` to `BasedOn="{StaticResource Button.Period}"` and replace its two DataTrigger setters with:

```xml
                                                        <Setter Property="Background" Value="{DynamicResource Highlight}" />
                                                        <Setter Property="BorderBrush" Value="{DynamicResource Highlight}" />
                                                        <Setter Property="Foreground" Value="{DynamicResource On.Highlight}" />
```

- [ ] **Step 5: Period and table** — change the PeriodLabel TextBlock to `Style="{StaticResource TextBlock.SectionTitle}"` (drop `FontSize` and `FontWeight`); set the column headers to `FOLDER`, `VISITS`, `TIME`, `LAST VISITED`, `IN PLACES`; give the "Add as Place" button `Style="{StaticResource Button.Compact}"` and `Content="Add as place"`; set `RowHeight="38"` on `ActivityGrid`.

- [ ] **Step 6: Status ribbon** — give its Dismiss button `Style="{StaticResource Button.Compact}"`.

- [ ] **Step 7: Build** — `dotnet build QuickerPlaces.sln -nologo -v q`. Expected: no errors from ActivityWindow.

- [ ] **Step 8: Commit**

```bash
git -C C:/QuickerPlaces commit -am "Restyle Recents with leather heat and the trim band"
```

### Task 15: Dialogs

**Files:**
- Modify: `Views/PlaceFormDialog.xaml`, `Views/PlaceFormDialog.xaml.cs`
- Modify: `Views/MessageForm.xaml`, `Views/RecoveryDialog.xaml`
- Modify: `Views/ExportDialog.xaml`, `Views/ImportDialog.xaml`
- Modify: `Views/RecentlyDeletedDialog.xaml`
- Modify: `Views/AddRootDialog.xaml`, `Views/ActivityFolderSettingsDialog.xaml`

- [ ] **Step 1: PlaceFormDialog** — in the XAML, change the `Alias` label to `Text="Name"` and give both field labels `Style="{StaticResource TextBlock.FieldLabel}"` (drop `FontWeight`); give `ResourceTextBox` `FontFamily="{StaticResource Font.Mono}"`; change `Content="Browse..."` to `Content="Browse…"`. In the code-behind, replace every `ResourceLabel.Text = "URL";` and the `"URL"` arms of the two ternaries with `"Link"`, and replace `TitleFor` with:

```csharp
    private static string TitleFor(PlaceFormMode mode, PlaceType type) => mode switch
    {
        PlaceFormMode.AddFolder => "Add folder",
        PlaceFormMode.AddUrl => "Add link",
        PlaceFormMode.RenameAlias => "Rename",
        PlaceFormMode.EditResource => type == PlaceType.Folder ? "Edit folder path" : "Edit link",
        PlaceFormMode.Restore => "Restore place",
        _ => "Place"
    };
```

- [ ] **Step 2: MessageForm and RecoveryDialog badges** — outlined circles with a coloured character.

In `MessageForm.xaml` replace the badge:

```xml
            <Border x:Name="IconBadge" Width="36" Height="36" CornerRadius="18" BorderThickness="2"
                    Background="{DynamicResource Bg.Raised}" VerticalAlignment="Top" Margin="0,0,14,0">
                <TextBlock x:Name="IconGlyph" FontWeight="ExtraBold" FontSize="16"
                           HorizontalAlignment="Center" VerticalAlignment="Center" />
            </Border>
```

In `RecoveryDialog.xaml` replace the badge:

```xml
            <Border x:Name="IconBadge" Width="36" Height="36" CornerRadius="18" BorderThickness="2"
                    Background="{DynamicResource Bg.Raised}" BorderBrush="{DynamicResource Signal}"
                    VerticalAlignment="Top" Margin="0,0,14,0">
                <TextBlock Text="!" FontWeight="ExtraBold" FontSize="16" Foreground="{DynamicResource Signal}"
                           HorizontalAlignment="Center" VerticalAlignment="Center" />
            </Border>
```

and give `PathText` `FontFamily="{StaticResource Font.Mono}"`.

- [ ] **Step 3: Export and Import** (identical edits in both files) — set `Title="Export places"` / `Title="Import places"`; buttons `Content="Select all"` and `Content="Select none"` with `Style="{StaticResource Button.Compact}"`; `Content="Export…"` in ExportDialog; replace the three text columns with:

```xml
                <DataGridTemplateColumn Header="NAME" Width="*">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                                <Path Style="{StaticResource Icon.RowPlaceType}" />
                                <TextBlock Text="{Binding Alias}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                            </StackPanel>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
                <DataGridTextColumn Header="FOLDER OR LINK" Binding="{Binding Resource}" Width="2*" IsReadOnly="True"
                                    ElementStyle="{StaticResource Cell.Path}" />
```

- [ ] **Step 4: RecentlyDeletedDialog** — replace the Alias template column's cell contents and drop the Type column:

```xml
                    <DataGridTemplateColumn Header="NAME" SortMemberPath="Alias" Width="*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                                    <Path Style="{StaticResource Icon.RowPlaceType}" />
                                    <TextBlock Text="{Binding Alias}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                                </StackPanel>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>
                    <DataGridTextColumn Header="FOLDER OR LINK" Binding="{Binding Resource}" Width="2*" ElementStyle="{StaticResource Cell.Path}" />
```

Set the other headers to `DELETED` and `DAYS REMAINING`, and remove `FontStyle="Italic"` from the empty message.

- [ ] **Step 5: AddRootDialog and ActivityFolderSettingsDialog** — AddRootDialog: title TextBlock `Style="{StaticResource TextBlock.SectionTitle}"` (drop `FontSize`/`FontWeight`); the root path TextBlock's `Foreground` becomes `{DynamicResource Highlight.Text}` (Task 11 already mapped it). ActivityFolderSettingsDialog: `Grid Margin="24"`; the title TextBlock `Style="{StaticResource TextBlock.SectionTitle}"`; section labels `Style="{StaticResource TextBlock.FieldLabel}"`; hint TextBlocks `Style="{StaticResource TextBlock.Hint}"`; the equivalent-paths TextBox `VerticalContentAlignment="Top"`.

- [ ] **Step 6: Build and check** — `dotnet build QuickerPlaces.sln -nologo -v q` → `0 Warning(s)`, `0 Error(s)`. Rerun the Task 11 grep: expected no hits.

- [ ] **Step 7: Commit**

```bash
git -C C:/QuickerPlaces commit -am "Restyle the dialogs"
```

### Task 16: Name wording in validation messages

**Files:**
- Modify: `src/QuickerPlaces/Services/PlacesService.cs`
- Modify: `src/QuickerPlaces.Tests/PlacesServiceRecentlyDeletedTests.cs:87,129`

- [ ] **Step 1: Update the two tests' expected text**

```csharp
        Assert.Equal("\"Docs\" can't be restored as it was: another place is now called \"docs\". Change the name below, then restore.", conflict.Explanation);
```

```csharp
        Assert.Equal("\"Docs\" can't be restored as it was: another place, \"docs\", now has its name and its folder path. Change them below, then restore.", conflict.Explanation);
```

- [ ] **Step 2: Run them to see them fail** — `dotnet test QuickerPlaces.Tests -nologo --filter PlacesServiceRecentlyDeletedTests`. Expected: 2 failures on the old "alias" wording.

- [ ] **Step 3: Change the messages** in `PlacesService.cs`:

- line 150: `"Alias can't be empty."` → `"Name can't be empty."`
- line 472: `that alias is now used by another place.` → `that name is now used by another place.`
- line 475: `its path/URL is now stored under another alias.` → `its folder or link is now stored under another name.`
- `RestoreConflict.Explanation`: `var destination = Place.Type == PlaceType.Folder ? "folder path" : "URL";` → `... : "link";`; `now has its alias and its` → `now has its name and its`; `Change the alias below` → `Change the name below`.

Search the file for any other user-facing `alias` or `URL` in a `ValidationResult.Fail(...)` string and apply the same words; leave log messages (`DiagnosticLog`) alone.

- [ ] **Step 4: Run all tests** — `dotnet test QuickerPlaces.Tests -nologo`. Expected: `Failed: 0`. Fix any other test that asserted the old words by updating its expected text only.

- [ ] **Step 5: Commit**

```bash
git -C C:/QuickerPlaces commit -am "Say name and link instead of alias and URL in messages"
```

### Task 17: New app icon

**Files:**
- Create: `tools/make-app-icon.ps1`
- Replace: `src/QuickerPlaces/Resources/AppIcon.ico`

- [ ] **Step 1: Write the script**

```powershell
# Regenerates src/QuickerPlaces/Resources/AppIcon.ico: a Brand green tile
# (#1F5C4D) with an off-white "QP" in TASA Orbiter ExtraBold and a thin
# silver edge (#C6CBC8), matching the monogram in the main window header.
# PNG frames at 16-256 px packed into one .ico. Run from any folder.
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$fontFile = Join-Path $root 'src\QuickerPlaces\Resources\Fonts\TASAOrbiter-ExtraBold.ttf'
$outFile = Join-Path $root 'src\QuickerPlaces\Resources\AppIcon.ico'

$fonts = New-Object System.Drawing.Text.PrivateFontCollection
$fonts.AddFontFile($fontFile)
$family = $fonts.Families[0]
$green = [System.Drawing.Color]::FromArgb(255, 0x1F, 0x5C, 0x4D)
$silver = [System.Drawing.Color]::FromArgb(255, 0xC6, 0xCB, 0xC8)
$ink = [System.Drawing.Color]::FromArgb(255, 0xF2, 0xF1, 0xEC)

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $inset = [Math]::Max(0.5, $s / 64)
    $w = $s - 2 * $inset
    $d = [Math]::Max(3, $s * 0.36)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($inset, $inset, $d, $d, 180, 90)
    $path.AddArc($inset + $w - $d, $inset, $d, $d, 270, 90)
    $path.AddArc($inset + $w - $d, $inset + $w - $d, $d, $d, 0, 90)
    $path.AddArc($inset, $inset + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $green), $path)
    $g.DrawPath((New-Object System.Drawing.Pen $silver, ([Math]::Max(1, $s / 48))), $path)
    $font = New-Object System.Drawing.Font $family, ([single]($s * 0.42)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString('QP', $font, (New-Object System.Drawing.SolidBrush $ink), (New-Object System.Drawing.RectangleF 0, ([single]($s * 0.02)), $s, $s), $format)
    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += , $stream.ToArray()
    $g.Dispose(); $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $out
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $side = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$side); $writer.Write([byte]$side); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frames[$i].Length); $writer.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
[System.IO.File]::WriteAllBytes($outFile, $out.ToArray())
"Wrote $outFile ($($out.Length) bytes, $($sizes.Count) frames)"
```

- [ ] **Step 2: Run it** — `powershell -ExecutionPolicy Bypass -File C:\QuickerPlaces\tools\make-app-icon.ps1`. Expected: `Wrote ...AppIcon.ico (... bytes, 9 frames)`. Open the 256 px frame (extract it or view the .ico in Explorer) and check the tile, silver edge and legible "QP"; at 16 px the letters may be soft, which is acceptable.

- [ ] **Step 3: Build** — `dotnet build QuickerPlaces.sln -nologo -v q` → clean.

- [ ] **Step 4: Commit**

```bash
git -C C:/QuickerPlaces add tools/make-app-icon.ps1 src/QuickerPlaces/Resources/AppIcon.ico
git -C C:/QuickerPlaces commit -m "Redraw the app icon in Brand green"
```

### Task 18: Full test and build pass

- [ ] **Step 1:** `dotnet build QuickerPlaces.sln -nologo -v q` → `0 Warning(s)`, `0 Error(s)`.
- [ ] **Step 2:** `dotnet test QuickerPlaces.Tests -nologo` → `Failed: 0`; record the new total for the handoff.
- [ ] **Step 3:** Launch the app (`dotnet run --project QuickerPlaces`) and confirm it opens without a XAML parse exception. A startup `XamlParseException` names the file and line: fix it, rebuild, relaunch.

### Task 19: User guide

**Files:** `USERGUIDE.md`

- [ ] **Step 1: Apply the wording** throughout: "alias" → "name" (and "Alias" → "Name"); "Path/URL", "Path / URL", "path/URL" → "folder or link"; **Add URL** → **Add link**; **Add Folder** → **Add folder**; **Hide List** → **Hide list**; **Rename Alias** → **Rename**; **Edit Path/URL** → **Edit folder or link**; **Copy Path/URL** → **Copy folder or link**; **Toggle Favourite** → **Toggle favourite**; **Import Places** / **Export Places** / **Places File** → **Import places** / **Export places** / **Places file**; **Restore Place** → **Restore place**; "bubble" → "card"; **All Places (3 of 12)** → the count "3 of 12" beside **ALL PLACES**; "gold star" → "orange star".
- [ ] **Step 2: Fix the facts that changed:** the grid's columns are Name, Folder or link, Last opened, Opens (no Type column; the icon shows folder or link); favourites are leather cards numbered 1–9 for Ctrl+1 to Ctrl+9; the Recently Deleted, Import and Export lists have no Type column either; the Recents window is titled Recents.
- [ ] **Step 3: Add a section** after "Bringing QuickerPlaces up from anywhere":

```markdown
## Appearance

Open **Options** › **Settings**. Under **Appearance**:

- **Theme:** **Light**, **Dark**, or **Match Windows**, which follows the light or dark setting in Windows and changes when you change it there.
- **Highlight colour:** the colour of the main button, selected rows and switches. Choose **Hull green** (the default), **Steel blue**, **Tail red**, **Cognac**, or **Windows accent**, which follows your Windows accent colour. QuickerPlaces adjusts an accent that would be hard to read.

Your choice shows straight away; **Cancel** puts back what you had, and **Save** keeps it.
```

- [ ] **Step 4: Commit** — `git -C C:/QuickerPlaces commit -am "Update the user guide for the new look"`

### Task 20: Build summary, README and handoff

**Files:** `ai/BUILD_SUMMARY.md`, `README.md`, create `ai/260927_UI Refresh Handoff.md`

- [ ] **Step 1: README** — find the "Theming" section (`grep -n "Theming" README.md`) and replace its body with: colours live in `Resources/Palette.Dark.xaml` and `Palette.Light.xaml` (same keys, tested by `PaletteFileTests`); styles in `Resources/Styles.xaml`; icons in `Resources/Icons.xaml`; `Services/ThemeManager.cs` switches theme and highlight; always use `DynamicResource` for colours; the source of truth is the QuickerPlaces Design System artifact (link it).
- [ ] **Step 2: BUILD_SUMMARY** — add a section `## UI refresh — Saab 900 look (2026-09-27)` with: why (the user's brief: simple, clean, easy to understand first time; Saab 900 materials), what was built (palettes, ThemeManager, highlight presets and derivation, Styles/Icons, fonts, Settings Appearance, restyled windows, wording, icon), decisions U1–U13 in one line each, the verification status (tests count, and that section 8's walk is still to do on Windows), and links to this plan, the canvas and the Design System.
- [ ] **Step 3: Handoff** — create `ai/260927_UI Refresh Handoff.md` with: what's done (tasks and commits), what's verified (build, tests, launch), what's not (section 8 checklist), known gaps (section 12), and the next roadmap step (Phase 4, general file support).
- [ ] **Step 4: Commit** — `git -C C:/QuickerPlaces add -A ai README.md && git -C C:/QuickerPlaces commit -m "Record the UI refresh"`

### Task 21: Bring the Design System in line

**Files:** the QuickerPlaces Design System artifact (not the repo)

- [ ] **Step 1:** Read its `project/README.md` and `project/tokens.json` with the Artifact tool (it may have been edited on the page).
- [ ] **Step 2:** Add to the README's "Not yet built" section (renaming it "Built in the app" with what is now true): the app implements version 2; WPF differences: capitals labels have no letter-spacing, tables have square corners, the Recents cells are 12 px at a 14 px pitch (U11, U12); `Preset-Red-Soft` light is 10% like the others. Update `Preset-Red-Soft` light to `rgba(168, 50, 43, 0.10)` in tokens.json.
- [ ] **Step 3:** Publish only the changed files, then the index with a new `lastChange` (`via` "Claude Code", note "App built to version 2; recorded WPF differences").

## 6. Constraints this plan checks itself against

- **D5 (tests never touch WPF):** `ThemePreference`, `ThemeColor` and `HighlightPalette` have no `System.Windows` reference; `PaletteFileTests` reads XAML as XML. `ThemeManager` is app-only.
- **D30's lesson (settings never reset on a bad value):** theme and highlight are strings parsed tolerantly (Task 2 tests).
- **Privacy rule for logs:** nothing new is logged.
- **No behaviour change beyond the look:** commands, shortcuts, sorting, drag-and-drop, tracking and persistence are untouched; the only data-visible change is settings.json schema 5.
- **Accessibility floors (Design System):** text 4.5:1 in both themes (Task 4 test), highlight text readable for every preset and any Windows accent (Task 3 tests), a visible keyboard focus ring everywhere (Styles.xaml), icon-only buttons carry `AutomationProperties.Name`.

## 7. Test plan

| # | Test | Task |
|---|---|---|
| 1 | Theme/highlight strings parse tolerantly and round-trip | 2 |
| 2 | Version-4 settings load with dark + green; theme/highlight round-trip; a bad theme never throws | 2 |
| 3 | Colour parse, hex, WCAG contrast reference points, mix | 3 |
| 4 | Every preset keeps on-fill and on-ground text at 4.5:1 in both themes | 3 |
| 5 | Hull green matches the Design System exactly | 3 |
| 6 | Windows accent derivation is readable for bright, dark, pastel and mid-grey accents | 3 |
| 7 | Both palettes define identical keys; ground and green defaults agree with HighlightPalette | 4 |
| 8 | Palette text/ground pairs keep 4.5:1; Signal 3:1 on raised | 4 |
| 9 | A remembered Type sort is dropped for the places grid | 5 |
| 10 | Favourite shortcut numbers; tooltip is the destination; row view models expose Type | 6 |
| 11 | Restore-conflict messages say "name" and "link" | 16 |

## 8. Manual verification (Windows)

Walk each in Dark, then Light, then Match Windows (switch the Windows app theme while the app is open):

1. Launch: no parse error; title bar matches the theme; TASA Orbiter visibly renders at five weights (window title ExtraBold, labels Bold, buttons SemiBold, names Medium, text Regular); paths in IBM Plex Mono.
2. Main window: monogram, title, subheader, Recents dot, Add folder / Add link; trim band full width; favourite cards leather with numbers 1–9; Ctrl+1 opens the first; drag reorders; right-click menu themed.
3. Places list: no Type column; folder/globe icons; names larger; hover wash; selected row in the highlight with readable text and icons; star states (favourite orange, outline on hover/selection, click toggles); sort arrow and orange underline on the sorted column; a Type sort saved by an older build shows as unsorted.
4. Search: icon, placeholder, Ctrl F keycap, focus border, clear button, Esc clears.
5. Status bar after Remove: tick, message, Undo with keycap, dismiss; timing unchanged.
6. Unsaved-changes banner (make places.json read-only, edit a place): orange outline, readable text, Retry/Show data folder/Show log.
7. Settings: segmented theme and swatches; each choice previews at once across open windows (main and Recents); Cancel and the title-bar close restore the previous look; Save persists across restart; Windows accent follows a change in Windows settings; a very light accent still has readable button text.
8. Recents: title, trim band, chips (selected, paused), Week/Month/Day on-state, leather heat in both themes, today outline, selected day, year picker, table headers and Add as place.
9. Dialogs: Add folder / Add link (Name, Link labels, Browse…), Rename, Edit, Restore place, message boxes (outlined coloured badges), recovery prompt, Export/Import (icons, no Type column), Recently Deleted, Track a folder, Folder tracking settings (combo box dropdown, multiline paths box).
10. Keyboard only: Tab through the main window and Settings; every focused control shows the orange ring; access keys still need Alt.
11. App icon: exe in Explorer, taskbar, Alt+Tab and every title bar show the green QP tile.

## 9. User guide changes

Task 19.

## 10. Order of work

Tasks 1–6 are test-first and keep both the tests and the build green. From Task 9 (the old theme is deleted) to Task 15, the app may fail to build or fail at startup on missing resource keys, because XAML resource keys are only resolved at runtime; do Tasks 9–15 in order without long pauses, and use Task 18 as the first full run. Tasks 16–21 follow in any order after Task 15.

## 11. Definition of done

- Build clean (0 warnings, 0 errors); all tests pass.
- Section 8 walked on Windows in all three theme modes, with any fixes committed.
- USERGUIDE, README, BUILD_SUMMARY and the handoff updated; the Design System records the WPF differences.
- The branch is ready for the user to review and merge.

## 12. Open questions and notes for later

- **Default theme:** dark + Hull green keeps today's users on a dark screen. Switch the default to Match Windows if preferred (change the two defaults in `AppSettings` and the Task 2 test).
- **Visits bars** in the Recents table (on the canvas) need a per-row share from `ActivityViewModel`; left for a follow-up with its own test.
- **Rounded table corners** would need a DataGrid template with an opacity-mask clip; skipped (U11).
- **Letter-spaced capitals** would need a custom text control; skipped (U11).
- **Free colour picker:** deliberately not offered; the presets plus Windows accent cover it.
