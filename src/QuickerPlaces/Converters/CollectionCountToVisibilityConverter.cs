using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuickerPlaces.Converters;

/// <summary>
/// Converts an int (bind to a collection's Count) to Visibility: 0 →
/// Visible, anything else → Collapsed. Used to show the "no favourites
/// yet" placeholder only while FavouritePlaces is empty — ObservableCollection
/// raises PropertyChanged for its own Count property on every Add/Remove,
/// so this stays live without any extra wiring.
/// </summary>
public sealed class CollectionCountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(CollectionCountToVisibilityConverter)} does not support ConvertBack.");
}
