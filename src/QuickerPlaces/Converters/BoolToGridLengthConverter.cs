using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuickerPlaces.Converters;

/// <summary>
/// Converts IsGridExpanded to a RowDefinition.Height: True → a 1* row,
/// False → 0, or Auto with ConverterParameter=Auto, for a row whose content
/// collapses only part of itself. Used for the Places DataGrid's collapsible
/// row (SI §6.3 "The grid must be collapsible/expandable"). A GridLength
/// can't be bound directly with a plain bool, hence this converter rather
/// than a bare BooleanToVisibilityConverter — a Star row with a Collapsed
/// child still reserves its proportional share of space, which is not
/// what "collapsed" should look like here.
/// </summary>
public sealed class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isExpanded = value is true;
        if (isExpanded)
            return new GridLength(1, GridUnitType.Star);

        // "Auto": the row keeps whatever its content still shows while
        // collapsed (the places list's header, the workspace's toolbar).
        return parameter is "Auto" ? GridLength.Auto : new GridLength(0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(BoolToGridLengthConverter)} does not support ConvertBack.");
}
