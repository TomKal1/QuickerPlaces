using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickerPlaces.Models.Workspace;

/// <summary>
/// The workspace's one query scope (configurable canvas plan D3, D4): search
/// text, kind, source, an explicit collection or Session tag, and a date
/// rule. The File shelf and the year activity read it; a named layout saves
/// it only when the user ticks Include current filters.
///
/// Never holds a selected row, a scroll position, an error or anything read
/// from a file (D3). Kind and source are the Library's own filter names,
/// kept as strings so a value this build does not know reads as "all"
/// instead of failing the file. UI-free.
/// </summary>
public sealed class WorkspaceQuery
{
    public string Text { get; set; } = "";

    /// <summary>A Library kind filter name, or null for every kind.</summary>
    public string? Kind { get; set; }

    /// <summary>"saved", "recent", or null for both.</summary>
    public string? Source { get; set; }

    /// <summary>An explicitly chosen collection (M6), by id. Never an inferred project (D4).</summary>
    public string? CollectionId { get; set; }

    /// <summary>An existing Session tag, exactly as Sessions spell it.</summary>
    public string? Tag { get; set; }

    public DateRule Date { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>The defaults: no text, every kind and source, no scope, all recorded time.</summary>
    public static WorkspaceQuery Default => new();

    public bool IsDefault => Text.Length == 0 && Kind is null && Source is null && CollectionId is null && Tag is null && Date.Kind == DateRuleKind.All;

    public WorkspaceQuery Clone() => new()
    {
        Text = Text,
        Kind = Kind,
        Source = Source,
        CollectionId = CollectionId,
        Tag = Tag,
        Date = Date.Clone(),
        Extra = Extra is null ? null : new Dictionary<string, JsonElement>(Extra),
    };

    public bool SameAs(WorkspaceQuery other)
        => Text == other.Text && Kind == other.Kind && Source == other.Source && CollectionId == other.CollectionId &&
           string.Equals(Tag, other.Tag, StringComparison.OrdinalIgnoreCase) && Date.SameAs(other.Date);
}

/// <summary>What a <see cref="DateRule"/> means (plan D3).</summary>
public enum DateRuleKind
{
    All,
    ThisWeek,
    ThisMonth,
    Range,
}

/// <summary>
/// A typed date rule (plan D3): All recorded time, This week, This month, or
/// an explicit inclusive range. Relative rules are stored as rules and
/// resolved against the clock and culture each time they are used, so a saved
/// This week never turns into last week's dates.
///
/// Serialized with <see cref="Rule"/> as a string ("all", "thisWeek",
/// "thisMonth", "range") rather than an enum number, so an unknown rule from
/// a newer build reads as All instead of damaging the file.
/// </summary>
public sealed class DateRule
{
    public const string AllName = "all";
    public const string ThisWeekName = "thisWeek";
    public const string ThisMonthName = "thisMonth";
    public const string RangeName = "range";

    [JsonIgnore]
    public DateRuleKind Kind { get; set; } = DateRuleKind.All;

    /// <summary>The stored name of <see cref="Kind"/>. Anything unrecognised reads as All.</summary>
    [JsonPropertyName("rule")]
    public string Rule
    {
        get => Kind switch
        {
            DateRuleKind.ThisWeek => ThisWeekName,
            DateRuleKind.ThisMonth => ThisMonthName,
            DateRuleKind.Range => RangeName,
            _ => AllName,
        };
        set => Kind = value switch
        {
            ThisWeekName => DateRuleKind.ThisWeek,
            ThisMonthName => DateRuleKind.ThisMonth,
            RangeName => DateRuleKind.Range,
            _ => DateRuleKind.All,
        };
    }

    /// <summary>First day of a Range, inclusive. Ignored for other rules.</summary>
    public DateOnly? From { get; set; }

    /// <summary>Last day of a Range, inclusive. Ignored for other rules.</summary>
    public DateOnly? To { get; set; }

    public static DateRule All() => new();

    public static DateRule ThisWeek() => new() { Kind = DateRuleKind.ThisWeek };

    public static DateRule ThisMonth() => new() { Kind = DateRuleKind.ThisMonth };

    /// <summary>An explicit inclusive range; the two ends are put in order.</summary>
    public static DateRule Between(DateOnly from, DateOnly to)
        => from <= to
            ? new DateRule { Kind = DateRuleKind.Range, From = from, To = to }
            : new DateRule { Kind = DateRuleKind.Range, From = to, To = from };

    public DateRule Clone() => new() { Kind = Kind, From = From, To = To };

    public bool SameAs(DateRule other)
        => Kind == other.Kind && (Kind != DateRuleKind.Range || (From == other.From && To == other.To));

    /// <summary>
    /// The inclusive days this rule covers today, or null for All recorded
    /// time. "Today" is the local date of <paramref name="time"/>, and a week
    /// starts on <paramref name="culture"/>'s first day of the week, as the
    /// calendars' week columns do. A Range missing either end reads as All.
    /// </summary>
    public (DateOnly From, DateOnly To)? Resolve(TimeProvider time, CultureInfo culture)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        switch (Kind)
        {
            case DateRuleKind.ThisWeek:
                var offset = ((int)today.DayOfWeek - (int)culture.DateTimeFormat.FirstDayOfWeek + 7) % 7;
                var start = today.AddDays(-offset);
                return (start, start.AddDays(6));

            case DateRuleKind.ThisMonth:
                var first = new DateOnly(today.Year, today.Month, 1);
                return (first, first.AddMonths(1).AddDays(-1));

            case DateRuleKind.Range when From is { } from && To is { } to:
                return from <= to ? (from, to) : (to, from);

            default:
                return null;
        }
    }
}
