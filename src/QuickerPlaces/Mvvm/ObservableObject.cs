using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuickerPlaces.Mvvm;

/// <summary>
/// Minimal INotifyPropertyChanged base class, hand-rolled rather than taken
/// from a package (e.g. CommunityToolkit.Mvvm) — see README.md "Extending
/// the Template" for the tradeoff and how to swap in a package later if a
/// specific derived app wants source-generated properties/commands.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
