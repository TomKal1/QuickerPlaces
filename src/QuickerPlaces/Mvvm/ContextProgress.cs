using System;
using System.Threading;

namespace QuickerPlaces.Mvvm;

/// <summary>
/// Hands a report to the context it was created on, or runs it at once when
/// called from that same context: status text from a background open reaches
/// a view model on the UI thread. UI-free and linked into the test project.
/// </summary>
internal sealed class ContextProgress(Action<string> apply) : IProgress<string>
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    public void Report(string value)
    {
        if (_context is null || ReferenceEquals(_context, SynchronizationContext.Current))
            apply(value);
        else
            _context.Post(_ => apply(value), null);
    }
}
