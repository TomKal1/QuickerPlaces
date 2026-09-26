using System;

namespace QuickerPlaces.Services.Activity;

/// <summary>One day's total under a root, for the calendar (D20): "3h 12m in 9 folders".</summary>
public sealed record ActivityDayTotal(TimeSpan Time, int Visits, int Folders);
