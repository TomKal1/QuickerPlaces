using System;
using System.Collections.Generic;

namespace QuickerPlaces.Cli;

/// <summary>
/// A span of local dates ending today, from --period or --days. Null bounds
/// mean "all kept history". Dates are the machine's own (TimeProvider's
/// local zone), the way the app's Week/Month views count them.
/// </summary>
public sealed record Period(string Name, DateOnly? From, DateOnly To)
{
    public static readonly OptionSpec PeriodOption = new("period", "day, week, month, year or all. Each ends today; week is the last 7 days.", ValueName: "name");

    public static readonly OptionSpec DaysOption = new("days", "The last N days, ending today (1-3650). Overrides --period.", ValueName: "n");

    public static IReadOnlyList<OptionSpec> Options { get; } = new[] { PeriodOption, DaysOption };

    public bool Contains(DateOnly date) => (From is null || date >= From) && date <= To;

    public bool Contains(DateTimeOffset instant, TimeProvider time) => Contains(LocalDate(instant, time));

    public static DateOnly LocalDate(DateTimeOffset instant, TimeProvider time)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, time.LocalTimeZone).DateTime);

    public static Period Parse(CliArgs args, TimeProvider time, string defaultName = "week")
    {
        var today = LocalDate(time.GetUtcNow(), time);
        if (args.Int("days", 1, 3650) is { } days)
            return new Period($"{days}d", today.AddDays(-(days - 1)), today);

        var name = (args.Value("period") ?? defaultName).ToLowerInvariant();
        return name switch
        {
            "day" or "today" => new Period("day", today, today),
            "week" => new Period("week", today.AddDays(-6), today),
            "month" => new Period("month", today.AddDays(-29), today),
            "year" => new Period("year", today.AddDays(-364), today),
            "all" => new Period("all", null, today),
            _ => throw CliError.Usage("--period must be day, week, month, year or all.")
        };
    }

    public Dictionary<string, object?> ToJson() => new()
    {
        ["name"] = Name,
        ["from"] = From is { } from ? Json.Date(from) : null,
        ["to"] = Json.Date(To)
    };
}
