using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using QuickerPlaces.Services.Revit.Opening;

namespace QuickerPlaces.Tests.Fakes;

/// <summary>Records each Revit open and answers with <see cref="Next"/>, or holds it until the test completes it.</summary>
public sealed class FakeRevitOpenCoordinator : IRevitOpenCoordinator
{
    private TaskCompletionSource<RevitOpenReport>? _hold;

    public List<string> Opened { get; } = [];
    public RevitOpenReport Next { get; set; } = new(new RevitOpenOutcome(RevitOpenResult.Opened, "ok"), "ok", false);
    public IProgress<string>? Status { get; private set; }
    public CancellationToken Token { get; private set; }
    public bool Throws { get; set; }

    /// <summary>Answers for the next opens, in order; <see cref="Next"/> once they run out.</summary>
    public Queue<RevitOpenReport> Replies { get; } = new();

    /// <summary>Makes the next open wait until the returned source is completed.</summary>
    public TaskCompletionSource<RevitOpenReport> Hold() => _hold = new TaskCompletionSource<RevitOpenReport>();

    public Task<RevitOpenReport> OpenAsync(string filePath, IProgress<string>? status = null, CancellationToken cancellationToken = default)
    {
        Opened.Add(filePath);
        Status = status;
        Token = cancellationToken;
        if (Throws)
            throw new InvalidOperationException("boom");
        if (_hold is { } hold)
        {
            _hold = null;
            return hold.Task;
        }
        return Task.FromResult(Replies.Count > 0 ? Replies.Dequeue() : Next);
    }
}
