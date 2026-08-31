using System;
using System.Windows.Input;

namespace QuickerPlaces.Mvvm;

/// <summary>
/// Minimal ICommand implementation, hand-rolled rather than taken from a
/// package. Hooks CanExecuteChanged to WPF's CommandManager.RequerySuggested
/// so most UI interactions (clicks, key presses, focus changes) trigger an
/// automatic CanExecute re-check; call RaiseCanExecuteChanged() when a
/// state change needs to be reflected immediately instead of waiting for
/// the next UI interaction (see MainViewModel.IsRunning for an example).
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>Forces an immediate CanExecute re-check across all commands.</summary>
    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
