using System;

namespace QuickerPlaces.Services;

/// <summary>
/// Runs a query away from the UI thread and hands its result back to it
/// (configurable canvas plan M2). The caller decides whether a result is
/// still wanted when it arrives; this only moves the work.
/// UI-free and linked into the test project.
/// </summary>
public interface IBackgroundWork
{
    /// <summary>Runs <paramref name="work"/>, then <paramref name="apply"/> with its result on the thread that called this.</summary>
    void Run<T>(Func<T> work, Action<T> apply);
}

/// <summary>Runs the work and applies it at once, on the calling thread: for tests, and wherever a view has no dispatcher.</summary>
public sealed class InlineBackgroundWork : IBackgroundWork
{
    public static InlineBackgroundWork Instance { get; } = new();

    public void Run<T>(Func<T> work, Action<T> apply) => apply(work());
}
