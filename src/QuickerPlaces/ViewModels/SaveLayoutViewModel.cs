using System;
using System.Collections.Generic;
using System.Globalization;
using QuickerPlaces.Models.Workspace;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Library;

namespace QuickerPlaces.ViewModels;

/// <summary>
/// The Save layout dialog (configurable canvas plan D3, M5): a name, and for
/// Save as new, whether to include the current filters — off unless ticked —
/// said in words. When the chosen period is exactly this week or this month,
/// it asks whether the layout should keep "this week" (moving with the
/// calendar) or those exact days. Rename uses the same dialog, name only.
///
/// The name is checked by the caller, which knows the other layouts; a
/// refusal is shown in <see cref="ErrorMessage"/> and the dialog stays open.
/// UI-free and linked into the test project.
/// </summary>
public sealed class SaveLayoutViewModel : ObservableObject
{
    private readonly WorkspaceQuery _query;
    private readonly DateRuleKind? _relative;
    private string _name;
    private bool _includeFilters;
    private bool _keepRelative = true;
    private string? _errorMessage;

    private SaveLayoutViewModel(bool isRename, string name, WorkspaceQuery query, TimeProvider time, CultureInfo culture)
    {
        IsRename = isRename;
        _name = name;
        _query = query.Clone();
        FiltersSummary = Describe(_query, time, culture);

        // A week or month picked on the calendar is stored as its days; if it is
        // the current one, the user may have meant "this week" (D3).
        if (_query.Date.Kind == DateRuleKind.Range && _query.Date.Resolve(time, culture) is { } period)
        {
            if (DateRule.ThisWeek().Resolve(time, culture) == period)
                _relative = DateRuleKind.ThisWeek;
            else if (DateRule.ThisMonth().Resolve(time, culture) == period)
                _relative = DateRuleKind.ThisMonth;
            FixedDatesText = $"Always {LibraryViewModel.FormatDays(period.From, period.To, culture)}";
        }
    }

    /// <summary>Save as new: a suggested name, and the query shown now.</summary>
    public static SaveLayoutViewModel ForSaveAsNew(string suggestedName, WorkspaceQuery query, TimeProvider time, CultureInfo culture)
        => new(false, suggestedName, query, time, culture);

    /// <summary>Rename: the layout's name, and nothing about filters.</summary>
    public static SaveLayoutViewModel ForRename(string currentName)
        => new(true, currentName, WorkspaceQuery.Default, TimeProvider.System, CultureInfo.InvariantCulture);

    public bool IsRename { get; }

    public string Title => IsRename ? "Rename layout" : "Save as a new layout";

    public string SaveButtonText => IsRename ? "Rename" : "Save layout";

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? ""))
                ErrorMessage = null;
        }
    }

    /// <summary>True when there is a filter to include: Save as new only.</summary>
    public bool CanIncludeFilters => !IsRename && !_query.IsDefault;

    /// <summary>Include current filters (D3): off by default, so a layout arranges panels and nothing more unless asked.</summary>
    public bool IncludeFilters
    {
        get => _includeFilters;
        set
        {
            if (!SetProperty(ref _includeFilters, value && CanIncludeFilters))
                return;
            OnPropertyChanged(nameof(ShowsDateChoice));
        }
    }

    /// <summary>"Search “acme” · PDFs · Used 21–27 Sep 2026": the filters Include would save.</summary>
    public string FiltersSummary { get; }

    /// <summary>True when the filters include the current week or month, so the dialog asks how to keep it.</summary>
    public bool ShowsDateChoice => _includeFilters && _relative is not null;

    /// <summary>"This week, whichever week it is" or "This month, …".</summary>
    public string RelativeDatesText => _relative == DateRuleKind.ThisMonth
        ? "This month, whichever month it is"
        : "This week, whichever week it is";

    /// <summary>"Always 21–27 Sep 2026".</summary>
    public string FixedDatesText { get; } = "";

    /// <summary>True: the layout keeps "this week" and moves with the calendar. False: the exact days.</summary>
    public bool KeepRelative
    {
        get => _keepRelative;
        set
        {
            if (SetProperty(ref _keepRelative, value))
                OnPropertyChanged(nameof(KeepFixed));
        }
    }

    public bool KeepFixed
    {
        get => !_keepRelative;
        set => KeepRelative = !value;
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>The filters to save: null when not included; the week or month kept relative when chosen.</summary>
    public WorkspaceQuery? FiltersToSave()
    {
        if (!_includeFilters)
            return null;

        var filters = _query.Clone();
        if (_relative is { } relative && _keepRelative)
            filters.Date = relative == DateRuleKind.ThisWeek ? DateRule.ThisWeek() : DateRule.ThisMonth();
        return filters;
    }

    /// <summary>The query in words, for the dialog: each filter that isn't "all".</summary>
    public static string Describe(WorkspaceQuery query, TimeProvider time, CultureInfo culture)
    {
        var parts = new List<string>();
        if (query.Text.Trim().Length > 0)
            parts.Add($"Search “{query.Text.Trim()}”");
        if (Enum.TryParse<LibraryKind>(query.Kind, ignoreCase: true, out var kind))
            parts.Add(kind.PluralLabel());
        if (query.Source == "saved")
            parts.Add("Saved only");
        else if (query.Source == "recent")
            parts.Add("Recent only");
        if (!string.IsNullOrEmpty(query.Tag))
            parts.Add($"Sessions tagged “{query.Tag}”");
        if (!string.IsNullOrEmpty(query.Root))
            parts.Add("One tracked folder");

        switch (query.Date.Kind)
        {
            case DateRuleKind.ThisWeek:
                parts.Add("This week");
                break;
            case DateRuleKind.ThisMonth:
                parts.Add("This month");
                break;
            case DateRuleKind.Range when query.Date.Resolve(time, culture) is { } period:
                parts.Add($"Used {LibraryViewModel.FormatDays(period.From, period.To, culture)}");
                break;
        }

        return parts.Count == 0 ? "No filters: everything is shown." : string.Join(" · ", parts);
    }
}
