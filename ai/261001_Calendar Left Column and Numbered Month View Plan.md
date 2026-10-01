# Calendar left column and numbered month view — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A calendar saved in the left column shows the compact month view from startup, and the month view shows the day number in each day's cell.

**Architecture:** The year-or-month choice becomes a pure function in `ActivityCalendar` (tested in the UI-free test project), and `YearActivityPanel` re-runs it whenever the view model's `CalendarStripWidth` changes. Numbered days are a separate task and commit (a new `Button.CalendarDayNumbered` style, a `DayNumber` on the cell, and per-theme `Heat.Text.*` colours checked by the existing palette contrast test), so the user can revert them alone.

**Tech Stack:** C# / WPF on net10.0-windows, xUnit (the test project links UI-free sources, so `dotnet test` runs without WPF).

Spec: [261001_Calendar Left Column and Numbered Month View Design.md](261001_Calendar%20Left%20Column%20and%20Numbered%20Month%20View%20Design.md). Run commands from `C:\QuickerPlaces\src`.

---

## File structure

| File | Change |
|---|---|
| `QuickerPlaces/ViewModels/ActivityCalendar.cs` | Add `YearChrome`, `ShowsMonthView`; add `DayNumber` to `ActivityCalendarCell` (Task 3) |
| `QuickerPlaces/Views/Panels/YearActivityPanel.xaml.cs` | Use `ShowsMonthView`; re-run on `CalendarStripWidth` changes |
| `QuickerPlaces/Views/Panels/YearActivityPanel.xaml` | Month view uses `Button.CalendarDayNumbered` (Task 3) |
| `QuickerPlaces/Resources/Styles.xaml` | `Button.CalendarDayNumbered` (Task 3) |
| `QuickerPlaces/Resources/Palette.Dark.xaml`, `Palette.Light.xaml` | `Heat.Text.1`–`Heat.Text.4` (Task 3) |
| `QuickerPlaces.Tests/ActivityCalendarTests.cs` | `ShowsMonthView` and `DayNumber` tests |
| `QuickerPlaces.Tests/PaletteFileTests.cs` | Contrast rows for the day numbers |
| `ai/BUILD_SUMMARY.md` | A short note (Task 4) |

---

### Task 1: The year-or-month choice as a tested function

**Files:**
- Modify: `QuickerPlaces/ViewModels/ActivityCalendar.cs` (inside `public static class ActivityCalendar`, line 10)
- Test: `QuickerPlaces.Tests/ActivityCalendarTests.cs`

- [ ] **Step 1: Write the failing tests**

Add inside `ActivityCalendarTests`, after the last test:

```csharp
    [Theory]
    [InlineData(300.0, 0, true)]     // no strip yet, narrow: month
    [InlineData(2000.0, 0, true)]    // no strip yet, wide: still month (nothing to show as a year)
    [InlineData(300.0, 600, true)]   // strip loaded, panel narrower than strip plus chrome
    [InlineData(663.0, 600, true)]   // just short of strip + 64
    [InlineData(664.0, 600, false)]  // exactly strip + 64: year
    [InlineData(1900.0, 600, false)] // wide: year
    public void ShowsMonthViewWhenThereIsNoStripOrThePanelIsTooNarrowForIt(double panelWidth, int stripWidth, bool expected)
        => Assert.Equal(expected, ActivityCalendar.ShowsMonthView(panelWidth, stripWidth));
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test QuickerPlaces.Tests/QuickerPlaces.Tests.csproj --filter "FullyQualifiedName~ShowsMonthView"`
Expected: build FAIL, `'ActivityCalendar' does not contain a definition for 'ShowsMonthView'`.

- [ ] **Step 3: Implement**

In `ActivityCalendar.cs`, directly after `public static class ActivityCalendar` and its opening `{`:

```csharp
    /// <summary>Beyond the strip itself: the weekday labels, the panel's border and padding, and a little room.</summary>
    public const double YearChrome = 64;

    /// <summary>
    /// True when the Year activity panel should show one month rather than the
    /// whole year: when the strip hasn't been built yet (<paramref name="stripWidth"/>
    /// is 0, so there is no year to show), or the panel is narrower than the
    /// strip and its <see cref="YearChrome"/>.
    /// </summary>
    public static bool ShowsMonthView(double panelWidth, int stripWidth)
        => stripWidth <= 0 || panelWidth < stripWidth + YearChrome;
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test QuickerPlaces.Tests/QuickerPlaces.Tests.csproj --filter "FullyQualifiedName~ShowsMonthView"`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add QuickerPlaces/ViewModels/ActivityCalendar.cs QuickerPlaces.Tests/ActivityCalendarTests.cs
git commit -m "Add ActivityCalendar.ShowsMonthView, the year-or-month choice as a tested function"
```

(Add the trailer `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` to every commit message in this plan.)

---

### Task 2: The panel re-checks when the strip width arrives

**Files:**
- Modify: `QuickerPlaces/Views/Panels/YearActivityPanel.xaml.cs`

`LibraryViewModel.CalendarStripWidth` is 0 until the activity data loads, and `FitWidth` runs only on load and resize, so a calendar that starts in a narrow column picks the year strip and keeps it. The panel now listens for the width changing, only while it is loaded (so a discarded panel is not kept alive by the long-lived view model).

- [ ] **Step 1: Replace the constants, constructor and `FitWidth`**

In `YearActivityPanel.xaml.cs`, add `using System.ComponentModel;` at the top. Delete the `YearChrome` constant (and its summary line). Replace the constructor and `FitWidth` with:

```csharp
    private LibraryViewModel? _watched;

    public YearActivityPanel()
    {
        InitializeComponent();
        SizeChanged += (_, _) => FitWidth();
        Loaded += (_, _) =>
        {
            Watch(ViewModel);
            FitWidth();
        };
        Unloaded += (_, _) => Watch(null);
        DataContextChanged += (_, _) =>
        {
            if (IsLoaded)
                Watch(ViewModel);
            FitWidth();
        };

        // The chip for a chosen period comes and goes, and the navigation changes with the view.
        HeaderControls.SizeChanged += (_, _) => FitHeader();
    }

    /// <summary>
    /// Follows the view model's strip width: it is 0 until the activity data
    /// has loaded, so a panel that starts narrow must choose again when it arrives.
    /// </summary>
    private void Watch(LibraryViewModel? viewModel)
    {
        if (ReferenceEquals(_watched, viewModel))
            return;

        if (_watched is not null)
            _watched.PropertyChanged -= ViewModelPropertyChanged;
        _watched = viewModel;
        if (_watched is not null)
            _watched.PropertyChanged += ViewModelPropertyChanged;
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.CalendarStripWidth))
            FitWidth();
    }

    private void FitWidth()
    {
        if (ActualWidth <= 0 || ViewModel is not { } vm)
            return;

        var monthView = ActivityCalendar.ShowsMonthView(ActualWidth, vm.CalendarStripWidth);
        YearView.Visibility = monthView ? Visibility.Collapsed : Visibility.Visible;
        MonthView.Visibility = monthView ? Visibility.Visible : Visibility.Collapsed;
        YearNav.Visibility = monthView ? Visibility.Collapsed : Visibility.Visible;
        MonthNav.Visibility = monthView ? Visibility.Visible : Visibility.Collapsed;
        FitHeader();
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build QuickerPlaces/QuickerPlaces.csproj`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Run the whole test suite**

Run: `dotnet test QuickerPlaces.Tests/QuickerPlaces.Tests.csproj`
Expected: all tests pass (643+ at last count, plus the 6 new).

- [ ] **Step 4: Commit**

```bash
git add QuickerPlaces/Views/Panels/YearActivityPanel.xaml.cs
git commit -m "Choose the calendar's year or month view again when the strip width arrives"
```

---

### Task 3: Numbered days in the month view (its own commit, so it reverts alone)

**Files:**
- Modify: `QuickerPlaces/ViewModels/ActivityCalendar.cs:212` (`ActivityCalendarCell`)
- Modify: `QuickerPlaces/Resources/Palette.Dark.xaml`, `QuickerPlaces/Resources/Palette.Light.xaml`
- Modify: `QuickerPlaces/Resources/Styles.xaml` (after `Button.CalendarDay`, ends line 961)
- Modify: `QuickerPlaces/Views/Panels/YearActivityPanel.xaml:155` (the month view's day button)
- Test: `QuickerPlaces.Tests/ActivityCalendarTests.cs`, `QuickerPlaces.Tests/PaletteFileTests.cs`

The heat colours run light to dark in the Light theme and dark to light in Dark, so each theme gets its own text colours (`Heat.Text.1`–`4`), picked from existing palette colours except pure black on Dark's heat 3 (the nearest existing colour gave 3.8:1). Empty and untracked days use `Text.Secondary`.

- [ ] **Step 1: Write the failing tests**

In `ActivityCalendarTests`:

```csharp
    [Fact]
    public void ACellKnowsItsDayOfTheMonth()
    {
        var withDate = new ActivityCalendarCell(new DateOnly(2026, 9, 26), true, true, 2, "label");
        var blank = new ActivityCalendarCell(null, false, false, 0, "");

        Assert.Equal(26, withDate.DayNumber);
        Assert.Null(blank.DayNumber);
    }
```

In `PaletteFileTests`, add to the `TextKeepsFourPointFiveToOne` `[InlineData]` list:

```csharp
    [InlineData(true, "Heat.Text.1", "Heat.1")]
    [InlineData(true, "Heat.Text.2", "Heat.2")]
    [InlineData(true, "Heat.Text.3", "Heat.3")]
    [InlineData(true, "Heat.Text.4", "Heat.4")]
    [InlineData(true, "Text.Secondary", "Heat.0")]
    [InlineData(true, "Text.Secondary", "Heat.Untracked")]
    [InlineData(false, "Heat.Text.1", "Heat.1")]
    [InlineData(false, "Heat.Text.2", "Heat.2")]
    [InlineData(false, "Heat.Text.3", "Heat.3")]
    [InlineData(false, "Heat.Text.4", "Heat.4")]
    [InlineData(false, "Text.Secondary", "Heat.0")]
    [InlineData(false, "Text.Secondary", "Heat.Untracked")]
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test QuickerPlaces.Tests/QuickerPlaces.Tests.csproj --filter "FullyQualifiedName~ACellKnowsItsDay|FullyQualifiedName~TextKeepsFourPointFiveToOne"`
Expected: build FAIL on `DayNumber`; after adding it alone, the palette rows FAIL with `KeyNotFoundException` for `Heat.Text.*`.

- [ ] **Step 3: Add `DayNumber` and the palette colours**

`ActivityCalendar.cs` — replace the record with:

```csharp
public sealed record ActivityCalendarCell(DateOnly? Date, bool IsInRange, bool IsTracked,
    int Intensity, string Label, bool IsSelected = false, bool IsToday = false)
{
    /// <summary>The day of the month, for the month view's numbered cells; null for a blank cell.</summary>
    public int? DayNumber => Date?.Day;
}
```

`Palette.Dark.xaml`, after the `Heat.Untracked` line:

```xml
    <!-- The day number on each heat level of the month view (checked at 4.5:1 by PaletteFileTests); empty days use Text.Secondary. -->
    <SolidColorBrush x:Key="Heat.Text.1" Color="#FFEBE9E3" />
    <SolidColorBrush x:Key="Heat.Text.2" Color="#FFEBE9E3" />
    <SolidColorBrush x:Key="Heat.Text.3" Color="#FF000000" />
    <SolidColorBrush x:Key="Heat.Text.4" Color="#FF26170D" />
```

`Palette.Light.xaml`, after its `Heat.Untracked` line:

```xml
    <!-- The day number on each heat level of the month view (checked at 4.5:1 by PaletteFileTests); empty days use Text.Secondary. -->
    <SolidColorBrush x:Key="Heat.Text.1" Color="#FF161A19" />
    <SolidColorBrush x:Key="Heat.Text.2" Color="#FF161A19" />
    <SolidColorBrush x:Key="Heat.Text.3" Color="#FF161A19" />
    <SolidColorBrush x:Key="Heat.Text.4" Color="#FFFFFFFF" />
```

- [ ] **Step 4: Run to verify the tests pass**

Run: `dotnet test QuickerPlaces.Tests/QuickerPlaces.Tests.csproj --filter "FullyQualifiedName~ACellKnowsItsDay|FullyQualifiedName~PaletteFileTests"`
Expected: PASS (including `BothPalettesDefineTheSameKeys`). If a `Text.Secondary on Heat.*` row fails, change that row's text to `Text.Primary` in both the test and `Heat.Text.0` use in step 5 instead.

- [ ] **Step 5: Add the numbered style and use it in the month view**

`Styles.xaml`, directly after the `Button.CalendarDay` style's closing `</Style>`:

```xml
    <!--
        The month view's day: the same cell as Button.CalendarDay (size, heat,
        today outline, selection) with the day of the month in it. The number
        is Text.Secondary on empty days and Heat.Text.n on a heat level, since
        the heats run light to dark in one theme and dark to light in the other.
    -->
    <Style x:Key="Button.CalendarDayNumbered" TargetType="Button" BasedOn="{StaticResource Button.CalendarDay}">
        <Setter Property="Foreground" Value="{DynamicResource Text.Secondary}" />
        <Setter Property="FontSize" Value="8" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Grid>
                        <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="2" />
                        <TextBlock Text="{Binding DayNumber}" Foreground="{TemplateBinding Foreground}" FontSize="{TemplateBinding FontSize}"
                                   HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False" />
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
            <DataTrigger Binding="{Binding Intensity}" Value="1">
                <Setter Property="Foreground" Value="{DynamicResource Heat.Text.1}" />
            </DataTrigger>
            <DataTrigger Binding="{Binding Intensity}" Value="2">
                <Setter Property="Foreground" Value="{DynamicResource Heat.Text.2}" />
            </DataTrigger>
            <DataTrigger Binding="{Binding Intensity}" Value="3">
                <Setter Property="Foreground" Value="{DynamicResource Heat.Text.3}" />
            </DataTrigger>
            <DataTrigger Binding="{Binding Intensity}" Value="4">
                <Setter Property="Foreground" Value="{DynamicResource Heat.Text.4}" />
            </DataTrigger>
        </Style.Triggers>
    </Style>
```

`YearActivityPanel.xaml`, in the **month view only** (the second `Button.CalendarDay` use, inside `x:Name="MonthView"`, near line 155), change `Style="{StaticResource Button.CalendarDay}"` to `Style="{StaticResource Button.CalendarDayNumbered}"`. Leave the year strip's button (line 106) as it is. The button's `ToolTip` and `AutomationProperties.Name` already carry the date label.

- [ ] **Step 6: Build and run all tests**

Run: `dotnet build QuickerPlaces/QuickerPlaces.csproj` then `dotnet test QuickerPlaces.Tests/QuickerPlaces.Tests.csproj`
Expected: `Build succeeded.`; all tests pass.

- [ ] **Step 7: Commit (one commit, so `git revert` removes the numbers alone)**

```bash
git add QuickerPlaces/ViewModels/ActivityCalendar.cs QuickerPlaces/Resources/Palette.Dark.xaml QuickerPlaces/Resources/Palette.Light.xaml QuickerPlaces/Resources/Styles.xaml QuickerPlaces/Views/Panels/YearActivityPanel.xaml QuickerPlaces.Tests/ActivityCalendarTests.cs QuickerPlaces.Tests/PaletteFileTests.cs
git commit -m "Number the days in the calendar's month view"
```

---

### Task 4: Look at it in the real app, and note it

Follows the real-click testing note: build to the scratchpad, launch with `--workspace --data-root <scratch folder>` (the user's own data and running instance are untouched), drive with UI Automation, and never call `app.Run()` from a probe.

- [ ] **Step 1: Build to the scratchpad**

Run: `dotnet build QuickerPlaces/QuickerPlaces.csproj -o <scratchpad>\qpbin`
Expected: `Build succeeded.`

- [ ] **Step 2: Walk the spec's checks**

Launch `<scratchpad>\qpbin\QuickerPlaces.exe --workspace --data-root <scratchpad>\data`, add a folder or two, then:
1. **Reset to Desk** (Settings → Customise layout…): the year strip runs along the bottom of the main column, with no numbers on it.
2. **Arrange**: set Year activity's column to *Left column*, **Save as new…**, name it, then close the app and launch it again: the calendar is in the left column and shows the **month view with numbered days from the first frame**.
3. In Dark and in Light (Settings): numbers are readable on empty days, every heat level, today's outlined day and the selected day. Capture a screenshot of each.
4. A 28-day, a 30-day and a 31-day month (the arrows), including a month that starts on the first and last weekday: two-digit numbers fit their cells.
5. Move the calendar back to the main column and drag the window narrower and wider: the view follows the width. Hover a day: the tooltip still reads the date and activity.

Fix anything found, with a test where it is UI-free.

- [ ] **Step 3: Note it**

Add a short paragraph to `ai/BUILD_SUMMARY.md` in its latest-changes section: the startup fix (what the cause was) and the numbered month view, naming the commit to revert for the numbers. Commit:

```bash
git add ai/BUILD_SUMMARY.md
git commit -m "Note the calendar's startup month view and numbered days in BUILD_SUMMARY"
```

- [ ] **Step 4: Tell the user how to try it and how to undo the numbers**

Say which commit holds the numbered days (`git log --oneline -3`), and that `git revert <that commit>` removes them without touching the startup fix.
