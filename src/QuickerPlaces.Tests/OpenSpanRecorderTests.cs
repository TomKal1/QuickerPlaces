using System;
using System.Linq;
using QuickerPlaces.Models.Sessions;
using QuickerPlaces.Services.Sessions.Suggestions;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// OpenSpanRecorder (session suggestions plan §5, G1, G6, G17): five-minute
/// samples of the open documents become open spans, and a missed sample, a
/// pause or a restart ends them.
/// </summary>
public sealed class OpenSpanRecorderTests
{
    private const string A = @"C:\Jobs\A.pdf";
    private const string B = @"C:\Jobs\B.pdf";
    private const string C = @"C:\Jobs\C.pdf";

    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly SessionSuggestionOptions Options = SessionSuggestionOptions.Balanced;

    private static OpenSpanRecorder New(params OpenSpan[] existing) => new(existing, Options);

    private static DateTimeOffset At(int minutes) => T0.AddMinutes(minutes);

    [Fact]
    public void ConsecutiveSamples_ExtendOneSpan()
    {
        var r = New();
        for (var i = 0; i <= 12; i++)
            r.Observe(At(i * 5), new[] { A });

        var span = Assert.Single(r.Spans(At(60)));
        Assert.Equal(new OpenSpan(A, At(0), At(60)), span);
        Assert.Equal(TimeSpan.FromMinutes(60), span.To - span.From);
    }

    [Fact]
    public void AFileMissingFromOneSample_ClosesItsSpan_AndASecondStartsWhenItReturns()
    {
        var r = New();
        r.Observe(At(0), new[] { A });
        r.Observe(At(5), new[] { A });
        r.Observe(At(10), new[] { B });
        r.Observe(At(15), new[] { A, B });

        Assert.Equal(
            new[]
            {
                new OpenSpan(A, At(0), At(5)),
                new OpenSpan(B, At(10), At(15)),
                new OpenSpan(A, At(15), At(15))
            },
            r.Spans(At(15)));
    }

    [Fact]
    public void AGapOfSevenAndAHalfMinutes_IsConsecutive()
    {
        var r = New();
        r.Observe(At(0), new[] { A });
        r.Observe(T0.AddMinutes(7.5), new[] { A });

        Assert.Equal(new OpenSpan(A, At(0), T0.AddMinutes(7.5)), Assert.Single(r.Spans(At(8))));
    }

    [Fact]
    public void AGapOverSevenAndAHalfMinutes_ClosesEverySpan()
    {
        var r = New();
        r.Observe(At(0), new[] { A, B });
        var later = T0.AddMinutes(7.5).AddSeconds(1);
        r.Observe(later, new[] { A });

        Assert.Equal(
            new[]
            {
                new OpenSpan(A, At(0), At(0)),
                new OpenSpan(B, At(0), At(0)),
                new OpenSpan(A, later, later)
            },
            r.Spans(later));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ANowNotAfterThePreviousSample_ClosesTheSpans_AndDoesNotThrow(int minutesAfter)
    {
        var r = New();
        r.Observe(At(10), new[] { A });
        var ex = Record.Exception(() => r.Observe(At(10 + minutesAfter), new[] { A }));

        Assert.Null(ex);
        Assert.Equal(
            new[] { new OpenSpan(A, At(10 + minutesAfter), At(10 + minutesAfter)), new OpenSpan(A, At(10), At(10)) }
                .OrderBy(s => s.From),
            r.Spans(At(20)));
    }

    [Fact]
    public void Interrupt_ClosesEverything_AndTheNextObserveStartsNewSpans()
    {
        var r = New();
        r.Observe(At(0), new[] { A });
        r.Observe(At(5), new[] { A });
        r.Interrupt();
        r.Observe(At(7), new[] { A });

        Assert.Equal(
            new[] { new OpenSpan(A, At(0), At(5)), new OpenSpan(A, At(7), At(7)) },
            r.Spans(At(7)));
    }

    [Fact]
    public void ExistingSpans_AreClosed_SoTheFirstSampleStartsANewOne()
    {
        var loaded = new OpenSpan(A, At(-60), At(-5));
        var r = New(loaded);
        r.Observe(At(0), new[] { A });

        Assert.Equal(new[] { loaded, new OpenSpan(A, At(0), At(0)) }, r.Spans(At(0)));
    }

    [Fact]
    public void PathsDifferingInCaseOrSlashes_AreOneFile_SpelledAsTheStartingSample()
    {
        var r = New();
        r.Observe(At(0), new[] { @"C:\Jobs\A.pdf" });
        r.Observe(At(5), new[] { "c:/jobs/a.PDF" });

        Assert.Equal(new OpenSpan(@"C:\Jobs\A.pdf", At(0), At(5)), Assert.Single(r.Spans(At(5))));
    }

    [Fact]
    public void DuplicatesInOneSample_CountOnce()
    {
        var r = New();
        r.Observe(At(0), new[] { A, A, @"c:\jobs\a.pdf" });
        r.Observe(At(5), new[] { A, A });

        Assert.Equal(new OpenSpan(A, At(0), At(5)), Assert.Single(r.Spans(At(5))));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Pruning_KeepsASpanEndingExactlyHistoryDaysAgo_AndDropsOneMinuteOlder(int extraMinutes, bool kept)
    {
        var old = new OpenSpan(A, T0.AddDays(-20), T0.AddDays(-Options.HistoryDays).AddMinutes(-extraMinutes));
        var r = New(old);

        Assert.Equal(kept, r.Spans(T0).Contains(old));

        r.Observe(T0, new[] { B });
        Assert.Equal(kept, r.Spans(T0).Contains(old));
    }

    [Fact]
    public void HasChanges_IsSetByEveryChange_AndClearedByMarkSaved()
    {
        var r = New();
        Assert.False(r.HasChanges);

        r.Observe(At(0), new[] { A });
        Assert.True(r.HasChanges);
        r.MarkSaved();
        Assert.False(r.HasChanges);

        r.Observe(At(5), new[] { A });
        Assert.True(r.HasChanges);
        r.MarkSaved();

        r.Observe(At(10), Array.Empty<string>());
        Assert.True(r.HasChanges);
        r.MarkSaved();

        r.Observe(At(15), new[] { A });
        r.MarkSaved();
        r.Interrupt();
        Assert.True(r.HasChanges);
        r.MarkSaved();

        r.Interrupt();
        Assert.False(r.HasChanges);

        var pruned = New(new OpenSpan(A, T0.AddDays(-30), T0.AddDays(-30)));
        pruned.Observe(T0, Array.Empty<string>());
        Assert.True(pruned.HasChanges);
    }

    [Fact]
    public void Output_IsOrderedByFromThenPath()
    {
        var r = New();
        r.Observe(At(0), new[] { B, A });
        r.Observe(At(5), new[] { C, B, A });
        r.Observe(At(10), new[] { C });

        Assert.Equal(
            new[]
            {
                new OpenSpan(A, At(0), At(5)),
                new OpenSpan(B, At(0), At(5)),
                new OpenSpan(C, At(5), At(10))
            },
            r.Spans(At(10)));
    }

    [Fact]
    public void TwoFilesOpenTogetherForAnHour_GiveTwoOverlappingSixtyMinuteSpans()
    {
        var r = New();
        for (var i = 0; i <= 12; i++)
            r.Observe(At(i * 5), new[] { A, B });

        Assert.Equal(
            new[] { new OpenSpan(A, At(0), At(60)), new OpenSpan(B, At(0), At(60)) },
            r.Spans(At(60)));
    }
}
