using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickerPlaces.Mvvm;
using QuickerPlaces.Services.Sessions.Suggestions;

namespace QuickerPlaces.ViewModels;

/// <summary>One immediately saved settings model shared by both settings surfaces (G16).</summary>
public sealed class SuggestionSettingsViewModel : ObservableObject
{
    private readonly SessionSuggestionStore _store;
    private readonly Func<bool> _recentFilesEnabled;
    private string? _error;
    public SuggestionSettingsViewModel(SessionSuggestionStore store, Func<bool>? recentFilesEnabled = null)
    { _store = store; _recentFilesEnabled = recentFilesEnabled ?? (() => true); }
    public event Action? Changed;
    public bool IsAvailable => _store.IsAvailable && _recentFilesEnabled();
    public void RefreshAvailability() => OnPropertyChanged(nameof(IsAvailable));
    public string? Notice => _store.Notice;
    public string? ErrorMessage { get => _error; private set => SetProperty(ref _error, value); }
    public bool Enabled { get => _store.Settings.Enabled; set => Save(_store.Settings with { Enabled = value }); }
    public bool OpenTogether { get => _store.Settings.OpenTogether; set => Save(_store.Settings with { OpenTogether = value }); }
    public bool IsLow { get => _store.Settings.Overall == SuggestionSensitivity.Low; set { if (value) Save(_store.Settings with { Overall = SuggestionSensitivity.Low }); } }
    public bool IsBalanced { get => _store.Settings.Overall == SuggestionSensitivity.Balanced; set { if (value) Save(_store.Settings with { Overall = SuggestionSensitivity.Balanced }); } }
    public bool IsHigh { get => _store.Settings.Overall == SuggestionSensitivity.High; set { if (value) Save(_store.Settings with { Overall = SuggestionSensitivity.High }); } }
    public IReadOnlyList<SuggestionLevelChoice> RuleChoices { get; } = new SuggestionLevelChoice[]
    {
        new(null, "Same as overall"), new(SuggestionSensitivity.Low, "Low"),
        new(SuggestionSensitivity.Balanced, "Balanced"), new(SuggestionSensitivity.High, "High"),
    };
    public SuggestionLevelChoice UsedTogetherChoice
    {
        get => RuleChoices.First(c => c.Level == _store.Settings.UsedTogether);
        set { if (value is not null) Save(_store.Settings with { UsedTogether = value.Level }); }
    }
    public SuggestionLevelChoice OpenTogetherChoice
    {
        get => RuleChoices.First(c => c.Level == _store.Settings.OpenTogetherLevel);
        set { if (value is not null) Save(_store.Settings with { OpenTogetherLevel = value.Level }); }
    }
    public bool HasOverrides => _store.Settings.UsedTogether is not null || _store.Settings.OpenTogetherLevel is not null;
    public string OverallLabel => _store.Settings.Overall.ToString();
    /// <summary>Display the algorithm's actual thresholds, including each rule's effective override.</summary>
    public IReadOnlyList<SuggestionConditionRow> UsedTogetherConditions => ConditionRows(openTogether: false);
    public IReadOnlyList<SuggestionConditionRow> OpenTogetherConditions => ConditionRows(openTogether: true);

    private IReadOnlyList<SuggestionConditionRow> ConditionRows(bool openTogether)
    {
        var active = (openTogether ? _store.Settings.OpenTogetherLevel : _store.Settings.UsedTogether) ?? _store.Settings.Overall;
        return Enum.GetValues<SuggestionSensitivity>().Select(level =>
        {
            var options = SessionSuggestionOptions.For(level);
            var days = openTogether ? options.TogetherOpenDays : options.MinDaysTogether;
            return new SuggestionConditionRow(level.ToString(),
                $"{(openTogether ? options.TogetherOpen : options.BurstGap).TotalMinutes:0} min",
                days == 1 ? "1 day" : $"{days} days",
                openTogether ? $"> {options.AlwaysOpenAbove.TotalHours:0} h/day" : options.MinShare.ToString("P0", CultureInfo.CurrentCulture),
                level == active);
        }).ToArray();
    }
    public string Summary => HasOverrides
        ? $"Used together: {UsedTogetherChoice.Level ?? _store.Settings.Overall} · Open together: {OpenTogetherChoice.Level ?? _store.Settings.Overall} · Adjust"
        : $"Sensitivity: {OverallLabel} · Adjust";
    public string OpenTogetherHint => (_store.Settings.OpenTogetherLevel ?? _store.Settings.Overall) switch
    {
        SuggestionSensitivity.Low => "Files kept open together for an hour and a half on two days.",
        SuggestionSensitivity.High => "Files kept open together for half an hour.",
        _ => "Files kept open together for an hour.",
    };
    public void ResetToOverall() => Save(_store.Settings with { UsedTogether = null, OpenTogetherLevel = null });
    private void Save(SuggestionSettings settings)
    {
        if (!IsAvailable) return;
        if (settings == _store.Settings) return;
        var result = _store.SaveSettings(settings);
        ErrorMessage = result.UserMessage;
        foreach (var name in new[] { nameof(Enabled), nameof(OpenTogether), nameof(IsLow), nameof(IsBalanced), nameof(IsHigh),
            nameof(UsedTogetherChoice), nameof(OpenTogetherChoice), nameof(HasOverrides), nameof(OverallLabel), nameof(OpenTogetherHint), nameof(Summary),
            nameof(UsedTogetherConditions), nameof(OpenTogetherConditions) })
            OnPropertyChanged(name);
        Changed?.Invoke();
    }
}

public sealed record SuggestionLevelChoice(SuggestionSensitivity? Level, string Label);

public sealed record SuggestionConditionRow(string Level, string Timing, string DaysNeeded, string Additional, bool IsActive);
