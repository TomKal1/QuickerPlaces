using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuickerPlaces.Converters;

/// <summary>
/// Converts IsGridExpanded to a RowDefinition.Height: True → a Star row
/// (as sized by <see cref="Parameter"/>-less default of 1*, via the
/// converter parameter), False → Auto (0-height, since the row's only
/// child collapses with it). Used for the Places DataGrid's collapsible
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
        return isExpanded ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(BoolToGridLengthConverter)} does not support ConvertBack.");
}
