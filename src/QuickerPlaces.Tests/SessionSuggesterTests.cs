using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Services.Sessions.Suggestions;
using Xunit;

namespace QuickerPlaces.Tests;

public sealed class SessionSuggesterTests
{
    private static readonly SessionSuggestionOptions Balanced = SessionSuggestionOptions.Balanced;
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static string F(int n) => $@"C:\Jobs\240015\f{n}.pdf";

    private static DateOnly Day(int d) => new(2026, 10, d);

    /// <summary>A rule A pair.</summary>
    private static StrongPair A(string a, string b, int days = 5, int lastDay = 10) =>
        StrongPair.Create(a, b, SuggestionRule.UsedTogether, days, Day(lastDay), TimeSpan.Zero);

    /// <summary>A rule B pair.</summary>
    private static StrongPair B(string a, string b, int days = 1, int minutes = 90, int onDay = 11) =>
        StrongPair.Create(a, b, SuggestionRule.OpenTogether, days, Day(onDay), TimeSpan.FromMinutes(minutes), Day(onDay));

    /// <summary>Every pair among the files, as rule A pairs.</summary>
    private static List<StrongPair> Clique(int days, int lastDay, params string[] files)
    {
        var pairs = new List<StrongPair>();
        for (var i = 0; i < files.Length; i++)
            for (var j = i + 1; j < files.Length; j++)
                pairs.Add(A(files[i], files[j], days, lastDay));
        return pairs;
    }

    private static SessionSnapshot S(string id, params string[] files) =>
        new(id, "Session " + id, [], files, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, [], 0);

    private static SessionSnapshot Named(string id, string name, params string[] files) => S(id, files) with { Name = name };

    private static IReadOnlyList<SessionSuggestion> Suggest(
        IEnumerable<StrongPair> pairs,
        IReadOnlyList<SessionSnapshot>? sessions = null,
        Func<string, bool>? dismissed = null,
        SessionSuggestionOptions? options = null) =>
        SessionSuggester.Suggest(pairs, sessions ?? [], dismissed ?? (_ => false), options ?? Balanced, Today);

    private static List<(string, string)> Shape(IEnumerable<SessionSuggestion> suggestions) =>
        suggestions.Select(s => (s.Key, string.Join("|", s.Files))).ToList();

    // ---------------------------------------------------------------
    // Grouping
    // ---------------------------------------------------------------

    [Fact]
    public void NoPairs_GiveNothing()
    {
        Assert.Empty(Suggest([]));
        Assert.Empty(Suggest([], [S("s1")]));
    }

    [Fact]
    public void ThreeLinkedFiles_AreOneGroup_AndAFourthLinkedToOneStaysOut()
    {
        var pairs = Clique(5, 10, F(1), F(2), F(3));
        pairs.Add(A(F(1), F(4), days: 3));

        var result = Suggest(pairs);

        // F4 is 1/3 linked, under Balanced's 2/3, so it stays out of the group of three. Its leftover
        // link F1-F4 makes a pair of its own, but a new-session card never shares a file (G18).
        var only = Assert.Single(result);
        Assert.Equal([F(1), F(2), F(3)], only.Files);
    }

    [Fact]
    public void ACandidateWithTwoLinksToThreeMembers_JoinsAtBalanced_AndWithOneDoesNot()
    {
        // Triangle 1-2-3 (strongest), candidate 4 linked to 1 and 2.
        var pairs = Clique(9, 10, F(1), F(2), F(3));
        pairs.Add(A(F(1), F(4), days: 4));
        pairs.Add(A(F(2), F(4), days: 4));

        Assert.Equal(4, Suggest(pairs)[0].Files.Count);

        var oneLink = Clique(9, 10, F(1), F(2), F(3));
        oneLink.Add(A(F(1), F(4), days: 4));
        Assert.Equal(3, Suggest(oneLink)[0].Files.Count);
    }

    [Fact]
    public void AtLow_ACandidateNeedsLinksToAllMembers()
    {
        var low = SessionSuggestionOptions.For(SuggestionSensitivity.Low);

        var partial = Clique(9, 10, F(1), F(2), F(3));
        partial.Add(A(F(1), F(4), days: 4));
        partial.Add(A(F(2), F(4), days: 4));
        Assert.DoesNotContain(Suggest(partial, options: low), s => s.Files.Count == 4);

        var full = Clique(9, 10, F(1), F(2), F(3), F(4));
        Assert.Contains(Suggest(full, options: low), s => s.Files.Count == 4);
    }

    [Fact]
    public void AtHigh_OneLinkOfTwoMembersIsEnough()
    {
        var high = SessionSuggestionOptions.For(SuggestionSensitivity.High);
        var pairs = new List<StrongPair> { A(F(1), F(2), days: 9), A(F(1), F(3), days: 4) };

        var result = Suggest(pairs, options: high);

        Assert.Single(result);
        Assert.Equal([F(1), F(2), F(3)], result[0].Files);
    }

    [Fact]
    public void TwoUnrelatedProjects_GiveTwoGroups()
    {
        var other = new[] { @"D:\Other\1.pdf", @"D:\Other\2.pdf", @"D:\Other\3.pdf" };
        var pairs = Clique(5, 10, F(1), F(2), F(3));
        pairs.AddRange(Clique(5, 10, other));

        var result = Suggest(pairs);

        Assert.Equal(2, result.Count);
        Assert.All(result, s => Assert.Equal(3, s.Files.Count));
    }

    [Fact]
    public void ARuleAPairAndARuleBPair_ShareAFileAndTheThirdLink_GiveOneGroup()
    {
        var pairs = new List<StrongPair> { A(F(1), F(2)), B(F(2), F(3)), A(F(1), F(3), days: 4) };

        var result = Suggest(pairs);

        Assert.Single(result);
        Assert.Equal([F(1), F(2), F(3)], result[0].Files);
    }

    [Fact]
    public void ThePairFromBothRules_IsOneRelation()
    {
        var pairs = new List<StrongPair> { A(F(1), F(2), days: 4), B(F(1), F(2), days: 2) };

        var result = Suggest(pairs);

        Assert.Single(result);
        Assert.Equal(4, result[0].Reason.DaysTogether);
        Assert.Equal(TimeSpan.FromMinutes(90), result[0].Reason.LongestOpenTogether);
    }

    [Fact]
    public void MaxGroupFiles_CapsAGroup()
    {
        var options = Balanced with { MaxGroupFiles = 3 };
        var pairs = Clique(5, 10, F(1), F(2), F(3), F(4), F(5));

        var result = Suggest(pairs, options: options);

        Assert.NotEmpty(result);
        Assert.Equal(3, result[0].Files.Count);
        Assert.All(result, s => Assert.True(s.Files.Count <= 3));
    }

    [Fact]
    public void TheOutput_IsTheSameForShuffledPairOrder()
    {
        var pairs = Clique(5, 10, F(1), F(2), F(3), F(4));
        pairs.AddRange(Clique(3, 9, @"D:\P\a.pdf", @"D:\P\b.pdf", @"D:\P\c.pdf"));
        pairs.Add(B(F(4), @"D:\P\a.pdf"));
        pairs.Add(A(F(1), @"D:\P\c.pdf", days: 3));

        var expected = Shape(Suggest(pairs));
        var random = new Random(7);
        for (var round = 0; round < 10; round++)
        {
            var shuffled = pairs.OrderBy(_ => random.Next()).ToList();
            Assert.Equal(expected, Shape(Suggest(shuffled)));
        }

        Assert.Equal(expected, Shape(Suggest(Enumerable.Reverse(pairs))));
    }

    [Fact]
    public void AGroupInsideALargerOne_IsNotRepeated()
    {
        // 1-2-3-4 are all linked; no smaller subset group may appear beside the group of four.
        var result = Suggest(Clique(5, 10, F(1), F(2), F(3), F(4)));

        Assert.Single(result);
    }

    // ---------------------------------------------------------------
    // Sessions
    // ---------------------------------------------------------------

    private static List<StrongPair> Triangle() => Clique(5, 10, F(1), F(2), F(3));

    [Fact]
    public void AGroupEqualToASession_IsDropped()
    {
        Assert.Empty(Suggest(Triangle(), [S("s1", F(1), F(2), F(3))]));
    }

    [Fact]
    public void AGroupInsideASession_IsDropped()
    {
        Assert.Empty(Suggest(Triangle(), [S("s1", F(1), F(2), F(3), F(9))]));
    }

    [Fact]
    public void TwoOfThreeInASession_IsAnAddSuggestion_WithOnlyTheMissingFileNew()
    {
        var result = Suggest(Triangle(), [S("s1", F(1), F(2), F(9))]);

        var only = Assert.Single(result);
        Assert.Equal("s1", only.TargetSessionId);
        Assert.True(only.IsAddToSession);
        Assert.Equal([F(3)], only.NewFiles);
        Assert.Equal([F(1), F(2), F(3)], only.Files);
        Assert.Null(only.SuggestedName);
    }

    [Fact]
    public void OneOfThreeInASession_IsANewSessionSuggestion()
    {
        var result = Suggest(Triangle(), [S("s1", F(1), F(8), F(9))]);

        var only = Assert.Single(result);
        Assert.Null(only.TargetSessionId);
        Assert.Equal(only.Files, only.NewFiles);
        Assert.NotNull(only.SuggestedName);
    }

    [Fact]
    public void SessionsWithNoFiles_AreIgnored()
    {
        var only = Assert.Single(Suggest(Triangle(), [S("empty")]));
        Assert.Null(only.TargetSessionId);
    }

    [Fact]
    public void TheSessionWithTheMostSharedFilesWins()
    {
        var sessions = new[] { S("s1", F(1), F(8)), S("s2", F(1), F(2), F(9)) };

        Assert.Equal("s2", Suggest(Triangle(), sessions)[0].TargetSessionId);
    }

    [Fact]
    public void WhenSessionsTie_TheEarlierOneWins()
    {
        var sessions = new[] { S("s1", F(1), F(2), F(8)), S("s2", F(2), F(3), F(9)) };

        Assert.Equal("s1", Suggest(Triangle(), sessions)[0].TargetSessionId);
    }

    [Fact]
    public void PathsDifferingInCaseOrSlashes_CountAsTheSameFile()
    {
        var session = S("s1", @"c:/jobs/240015/F1.PDF", @"C:\JOBS\240015\f2.pdf", F(9));

        var only = Assert.Single(Suggest(Triangle(), [session]));
        Assert.Equal("s1", only.TargetSessionId);
        Assert.Equal([F(3)], only.NewFiles);
    }

    // ---------------------------------------------------------------
    // Dismissals
    // ---------------------------------------------------------------

    [Fact]
    public void ADismissedKey_IsDropped()
    {
        var key = SessionSuggestionKey.For([F(1), F(2), F(3)]);

        Assert.Empty(Suggest(Triangle(), dismissed: k => k == key));
    }

    [Fact]
    public void TheSameGroupPlusOneMoreFile_HasANewKey_AndIsShown()
    {
        var dismissed = SessionSuggestionKey.For([F(1), F(2), F(3)]);

        var result = Suggest(Clique(5, 10, F(1), F(2), F(3), F(4)), dismissed: k => k == dismissed);

        var only = Assert.Single(result);
        Assert.Equal(4, only.Files.Count);
        Assert.NotEqual(dismissed, only.Key);
        Assert.Equal(SessionSuggestionKey.For(only.Files), only.Key);
    }

    // ---------------------------------------------------------------
    // Ranking
    // ---------------------------------------------------------------

    private static string[] P(int n) => [$@"D:\P{n}\a.pdf", $@"D:\P{n}\b.pdf"];

    [Fact]
    public void AddSuggestionsComeFirst_ThenStrength_ThenRecency()
    {
        var pairs = new List<StrongPair>
        {
            A(P(1)[0], P(1)[1], days: 9, lastDay: 10),   // strongest new
            A(P(2)[0], P(2)[1], days: 4, lastDay: 12),   // weaker, more recent
            A(P(3)[0], P(3)[1], days: 4, lastDay: 11),   // same days, older
            A(P(4)[0], P(4)[1], days: 2, lastDay: 5)     // weakest but an add
        };
        // Sharing one of two files meets the add rule (1 * 2 >= 2).
        var sessions = new[] { S("s1", P(4)[0], @"D:\other.pdf", @"D:\other2.pdf") };

        var result = Suggest(pairs, sessions);

        Assert.Equal(4, result.Count);
        Assert.Equal("s1", result[0].TargetSessionId);
        Assert.Equal(P(1), result[1].Files);
        Assert.Equal(P(2), result[2].Files);
        Assert.Equal(P(3), result[3].Files);
    }

    [Fact]
    public void LongerOverlap_BreaksADaysTie()
    {
        var pairs = new List<StrongPair>
        {
            B(P(1)[0], P(1)[1], days: 1, minutes: 70),
            B(P(2)[0], P(2)[1], days: 1, minutes: 120)
        };

        var result = Suggest(pairs);

        Assert.Equal(P(2), result[0].Files);
    }

    [Fact]
    public void MaxSuggestions_LimitsTheCount()
    {
        var pairs = new List<StrongPair>();
        for (var n = 1; n <= 4; n++)
            pairs.Add(A(P(n)[0], P(n)[1], days: 10 - n));

        var result = Suggest(pairs, options: Balanced with { MaxSuggestions = 2 });

        Assert.Equal(2, result.Count);
        Assert.Equal(P(1), result[0].Files);
        Assert.Equal(P(2), result[1].Files);
    }

    // ---------------------------------------------------------------
    // Reason
    // ---------------------------------------------------------------

    [Fact]
    public void RuleAOnly_HasNoOpenTogetherFields()
    {
        var reason = Suggest([A(F(1), F(2), days: 6, lastDay: 12)])[0].Reason;

        Assert.Equal(new SuggestionReason(6, Day(12), null, null), reason);
    }

    [Fact]
    public void RuleBOnly_HasZeroDays_AndNoLastTogether()
    {
        var reason = Suggest([B(F(1), F(2), days: 2, minutes: 75, onDay: 9)])[0].Reason;

        Assert.Equal(new SuggestionReason(0, null, TimeSpan.FromMinutes(75), Day(9)), reason);
    }

    [Fact]
    public void BothRules_FillEveryField()
    {
        var pairs = new List<StrongPair>
        {
            A(F(1), F(2), days: 4, lastDay: 10),
            B(F(1), F(3), days: 1, minutes: 100, onDay: 8),
            B(F(2), F(3), days: 1, minutes: 80, onDay: 12)
        };

        var reason = Suggest(pairs)[0].Reason;

        Assert.Equal(new SuggestionReason(4, Day(10), TimeSpan.FromMinutes(100), Day(8)), reason);
    }

    // ---------------------------------------------------------------
    // Names
    // ---------------------------------------------------------------

    private static string? NameFor(params string[] files) =>
        Suggest(Clique(5, 10, files))[0].SuggestedName;

    [Fact]
    public void ANumberedFolderInTheCommonPrefix_IsTheName()
    {
        Assert.Equal("240015", NameFor(@"C:\X\2024\240015\a.pdf", @"C:\X\2024\240015\b.pdf"));
    }

    [Fact]
    public void TheDeepestNumberedFolder_Wins_EvenWhenTheCommonFolderIsDeeper()
    {
        Assert.Equal("240015", NameFor(@"C:\X\2024\240015\Drawings\a.pdf", @"C:\X\2024\240015\Drawings\b.pdf"));
        Assert.Equal("240015", NameFor(@"C:\X\2024\240015\Drawings\a.pdf", @"C:\X\2024\240015\Drawings\Sub\b.pdf"));
        Assert.Equal("24016", NameFor(@"C:\240015\24016\Models\a.pdf", @"C:\240015\24016\Models\b.pdf"));
    }

    [Fact]
    public void ACommonFolderWithNoNumber_IsTheName()
    {
        Assert.Equal("Widgets", NameFor(@"C:\Work\Widgets\a.pdf", @"C:\Work\Widgets\b.pdf"));
    }

    [Fact]
    public void ForwardSlashesAreSplitToo()
    {
        Assert.Equal("Widgets", NameFor("C:/Work/Widgets/a.pdf", "C:/Work/Widgets/b.pdf"));
    }

    [Fact]
    public void NoCommonFolder_ButANumberInTheMostFolders_IsTheName()
    {
        Assert.Equal("240015", NameFor(@"C:\A\240015\x.pdf", @"D:\B\240015\y.pdf"));
        Assert.Equal("240015", NameFor(@"C:\A\240015\x.pdf", @"D:\B\240015\y.pdf", @"E:\C\240016\z.pdf"));
    }

    [Fact]
    public void NothingToGoOn_GivesTheDatedName()
    {
        Assert.Equal("Suggested session 15 Oct", NameFor(@"C:\A\x.pdf", @"D:\B\y.pdf"));
    }

    [Fact]
    public void ADriveRoot_IsNeverAName()
    {
        Assert.Equal("Suggested session 15 Oct", NameFor(@"C:\x.pdf", @"C:\y.pdf"));
    }

    [Fact]
    public void AShareRoot_IsNeverAName()
    {
        Assert.Equal("Suggested session 15 Oct", NameFor(@"\\srv\share\x.pdf", @"\\srv\share\y.pdf"));
        Assert.Equal("Sub", NameFor(@"\\srv\share\Sub\x.pdf", @"\\srv\share\Sub\y.pdf"));
    }

    [Fact]
    public void ANameClashingWithASession_GetsASuffix()
    {
        var sessions = new[] { Named("s1", " widgets ", @"E:\1.pdf", @"E:\2.pdf") };

        var result = Suggest(Clique(5, 10, @"C:\Work\Widgets\a.pdf", @"C:\Work\Widgets\b.pdf"), sessions);

        Assert.Equal("Widgets (2)", result[0].SuggestedName);
    }

    [Fact]
    public void TwoSuggestionsWithTheSameName_GetASuffixOnTheSecond()
    {
        var pairs = new List<StrongPair>
        {
            A(@"C:\Work\Widgets\a.pdf", @"C:\Work\Widgets\b.pdf", days: 9),
            A(@"C:\Work\Widgets\c.pdf", @"C:\Work\Widgets\d.pdf", days: 5),
            A(@"C:\Work\Widgets\e.pdf", @"C:\Work\Widgets\f.pdf", days: 3)
        };

        var result = Suggest(pairs);

        Assert.Equal(["Widgets", "Widgets (2)", "Widgets (3)"], result.Select(s => s.SuggestedName));
    }

    [Fact]
    public void ALongName_IsTrimmedToTheLimit_AndASuffixStillFits()
    {
        var folder = new string('W', 150);
        var pairs = new List<StrongPair>
        {
            A($@"C:\{folder}\a.pdf", $@"C:\{folder}\b.pdf", days: 9),
            A($@"C:\{folder}\c.pdf", $@"C:\{folder}\d.pdf", days: 5)
        };

        var result = Suggest(pairs);

        Assert.Equal(SessionStore.MaxNameLength, result[0].SuggestedName!.Length);
        Assert.Equal(SessionStore.MaxNameLength, result[1].SuggestedName!.Length);
        Assert.EndsWith(" (2)", result[1].SuggestedName);
        Assert.NotEqual(result[0].SuggestedName, result[1].SuggestedName);
    }

    [Fact]
    public void AnExistingSuffixedSession_PushesTheSuffixOn()
    {
        var pairs = Clique(5, 10, @"C:\Work\Widgets\a.pdf", @"C:\Work\Widgets\b.pdf");

        var onlyTwo = new[] { Named("s1", "Widgets (2)", @"E:\1.pdf", @"E:\2.pdf") };
        Assert.Equal("Widgets", Suggest(pairs, onlyTwo)[0].SuggestedName);

        var both = new[] { Named("s1", "Widgets", @"E:\1.pdf", @"E:\2.pdf"), Named("s2", "Widgets (2)", @"E:\3.pdf", @"E:\4.pdf") };
        Assert.Equal("Widgets (3)", Suggest(pairs, both)[0].SuggestedName);
    }

    [Fact]
    public void WhenNoFolderIsShared_AndNumbersTie_TheFirstInSortedOrderIsTheName()
    {
        Assert.Equal("111111", NameFor(@"C:\A\222222\x.pdf", @"D:\B\111111\y.pdf"));
    }

    [Fact]
    public void MixedSeparatorsInOnePath_AreSplitToo()
    {
        Assert.Equal("Widgets", NameFor(@"C:\Work/Widgets\a.pdf", @"C:\Work\Widgets/b.pdf"));
    }

    [Fact]
    public void TheDriveOrShare_NeverNamesASession_OrCountsAsAProjectNumber()
    {
        Assert.Equal("Suggested session 15 Oct", NameFor(@"\\12345\share\a.pdf", @"\\12345\share\b.pdf"));
        Assert.Equal("Proj", NameFor(@"\\12345\share\Proj\a.pdf", @"\\12345\share\Proj\b.pdf"));
        Assert.Equal("Suggested session 15 Oct", NameFor(@"\\srv\12345\a.pdf", @"\\srv\12345\b.pdf"));
        // The same folders on different drives share nothing.
        Assert.Equal("Suggested session 15 Oct", NameFor(@"C:\X\Jobs\a.pdf", @"D:\X\Jobs\b.pdf"));
    }

    // ---------------------------------------------------------------
    // One card per file (G18)
    // ---------------------------------------------------------------

    private static List<StrongPair> Path() => [A(F(1), F(2), days: 9), A(F(2), F(3), days: 8)];

    /// <summary>F1 is linked to F2..F5, which are not linked to each other.</summary>
    private static List<StrongPair> Hub() =>
    [
        A(F(1), F(2), days: 9),
        A(F(1), F(3), days: 8, lastDay: 12),
        A(F(1), F(4), days: 7, lastDay: 14),
        A(F(1), F(5), days: 6)
    ];

    [Fact]
    public void APath_GivesOneCardAtBalanced_AndTheWholePathAtHigh()
    {
        var balanced = Assert.Single(Suggest(Path()));
        Assert.Equal([F(1), F(2)], balanced.Files);

        var high = Assert.Single(Suggest(Path(), options: SessionSuggestionOptions.For(SuggestionSensitivity.High)));
        Assert.Equal([F(1), F(2), F(3)], high.Files);

        Assert.Single(Suggest(Path(), options: SessionSuggestionOptions.For(SuggestionSensitivity.Low)));
    }

    [Fact]
    public void AHub_GivesOneNewSessionCard()
    {
        var only = Assert.Single(Suggest(Hub()));

        Assert.Null(only.TargetSessionId);
        Assert.Equal([F(1), F(2)], only.Files);
    }

    [Fact]
    public void OnceTheHubGroupIsASession_TheOtherFilesGiveOneMergedAddCard()
    {
        var result = Suggest(Hub(), [S("s1", F(1), F(2))]);

        var only = Assert.Single(result);
        Assert.Equal("s1", only.TargetSessionId);
        Assert.Equal([F(1), F(3), F(4), F(5)], only.Files);
        Assert.Equal([F(3), F(4), F(5)], only.NewFiles);
        Assert.Equal(SessionSuggestionKey.For(only.Files), only.Key);
        // The reason is the max of each field: days 8 (F1-F3), last day 14 (F1-F4).
        Assert.Equal(new SuggestionReason(8, Day(14), null, null), only.Reason);
    }

    [Fact]
    public void AMergedAddCard_IsCheckedForDismissalAgain()
    {
        var merged = SessionSuggestionKey.For([F(1), F(3), F(4), F(5)]);

        Assert.Empty(Suggest(Hub(), [S("s1", F(1), F(2))], dismissed: k => k == merged));
    }

    [Fact]
    public void AMergedAddCard_IsRankedAsItsBestMember()
    {
        var pairs = Hub();
        pairs.Add(A(P(5)[0], P(5)[1], days: 7));
        var sessions = new[] { S("s1", F(1), F(2)), S("s2", P(5)[0], @"D:\other1.pdf", @"D:\other2.pdf") };

        var result = Suggest(pairs, sessions);

        // The merged card's best member has 8 days; its weakest has 6, under s2's 7.
        Assert.Equal(["s1", "s2"], result.Select(s => s.TargetSessionId));
    }

    [Fact]
    public void NewSessionCards_NeverShareAFile()
    {
        var pairs = Clique(9, 10, F(1), F(2), F(3));
        pairs.Add(A(F(3), @"D:\Z\z.pdf", days: 2));

        var result = Suggest(pairs);

        Assert.Single(result);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("hub")]
    public void OnThePathAndHubFixtures_EveryLevelGivesOneCard(string fixture)
    {
        foreach (var level in new[] { SuggestionSensitivity.Low, SuggestionSensitivity.Balanced, SuggestionSensitivity.High })
            Assert.Single(Suggest(fixture == "path" ? Path() : Hub(), options: SessionSuggestionOptions.For(level)));
    }

    [Fact]
    public void ALongerPath_HasNoCardCountPromiseAcrossLevels_ButHighStopsAtOneOfThree()
    {
        // G18 promises fewer near-duplicates, not a count: here Low and Balanced give two cards, High one.
        var pairs = new List<StrongPair> { A(F(1), F(2), days: 9), A(F(2), F(3), days: 8), A(F(3), F(4), days: 7) };

        foreach (var level in new[] { SuggestionSensitivity.Low, SuggestionSensitivity.Balanced })
        {
            var cards = Suggest(pairs, options: SessionSuggestionOptions.For(level));
            Assert.Equal([[F(1), F(2)], [F(3), F(4)]], cards.Select(c => c.Files.ToArray()));
        }

        // F4 has 1 link of the 3 members {1,2,3}, under High's half, so it stays out.
        var high = Assert.Single(Suggest(pairs, options: SessionSuggestionOptions.For(SuggestionSensitivity.High)));
        Assert.Equal([F(1), F(2), F(3)], high.Files);
    }

    private static List<StrongPair> Edges(string edges) =>
        edges.Split(' ').Select(e =>
        {
            var colon = e.Split(':');
            var ends = colon[0].Split('-');
            return A(G(int.Parse(ends[0])), G(int.Parse(ends[1])), days: int.Parse(colon[1]));
        }).ToList();

    [Fact]
    public void TheCandidateWithMostLinks_JoinsBeforeTheOneWithTheStrongestLink()
    {
        // Found by search. Seed 0-4; 3 has the strongest link (1-3:39) but 2 has two links to {0,4}.
        var pairs = Edges("0-4:58 1-3:39 0-3:33 2-4:32 0-2:18");

        var only = Assert.Single(Suggest(pairs, options: SessionSuggestionOptions.For(SuggestionSensitivity.High)));

        Assert.Equal([G(0), G(2), G(4)], only.Files);
    }

    [Fact]
    public void ACandidateWithThreeLinksOfSixMembers_DoesNotJoinAtBalanced()
    {
        // Found by search, like the subset fixture: with 3 of 6 allowed to join, 4 would be in the card.
        var pairs = Edges("0-1:42 0-2:60 0-3:29 0-5:32 0-6:7 1-2:44 1-3:6 1-5:4 2-3:11 2-6:9 3-4:45 3-6:48 4-5:12 4-6:16 5-6:34");

        var only = Assert.Single(Suggest(pairs));

        Assert.Equal([G(0), G(1), G(2), G(3), G(5), G(6)], only.Files);
    }

    // ---------------------------------------------------------------
    // Subsets and join edges
    // ---------------------------------------------------------------

    private static string G(int n) => $@"C:\p\f{n}.pdf";

    /// <summary>
    /// Balanced: the first group built is {1,2,3,4,5} (seed 1-5), then {0,1,3,4,5}, and later {2,3,4,5}
    /// grows from the leftover 2-4 and sits inside {1,2,3,4,5}.
    /// </summary>
    private static List<StrongPair> SubsetFixture() =>
        "0-3:15 0-4:9 0-5:22 1-2:16 1-3:5 1-4:12 1-5:29 2-4:26 2-5:28 3-5:24 4-5:24".Split(' ')
            .Select(e =>
            {
                var colon = e.Split(':');
                var ends = colon[0].Split('-');
                return A(G(int.Parse(ends[0])), G(int.Parse(ends[1])), days: int.Parse(colon[1]));
            })
            .ToList();

    [Fact]
    public void AnEarlierSmallerGroupInsideALaterLargerOne_IsDropped()
    {
        var result = Suggest(SubsetFixture());

        var only = Assert.Single(result);
        Assert.Equal([G(0), G(1), G(3), G(4), G(5)], only.Files);
    }

    [Fact]
    public void ADismissedSuperset_HidesItsSubset()
    {
        var superset = SessionSuggestionKey.For([G(1), G(2), G(3), G(4), G(5)]);
        var other = SessionSuggestionKey.For([G(0), G(1), G(3), G(4), G(5)]);

        var result = Suggest(SubsetFixture(), dismissed: k => k == superset || k == other);

        // {2,3,4,5} must not come back as a card of its own.
        Assert.Empty(result);
    }

    [Fact]
    public void ACandidateWithOneLinkOfTwoMembers_DoesNotJoinAtBalanced()
    {
        var result = Suggest([A(F(1), F(2), days: 9), A(F(1), F(3), days: 4)]);

        var only = Assert.Single(result);
        Assert.Equal([F(1), F(2)], only.Files);
    }

    [Fact]
    public void TheJoinFractionIsRecomputedAfterEveryJoin()
    {
        // Triangle 1-2-3; 4 and 5 are each linked to 1 and 2. Once 4 joins, 5 has 2 of 4 links.
        var pairs = Clique(9, 10, F(1), F(2), F(3));
        pairs.Add(A(F(1), F(4), days: 5));
        pairs.Add(A(F(2), F(4), days: 5));
        pairs.Add(A(F(1), F(5), days: 4));
        pairs.Add(A(F(2), F(5), days: 4));

        var balanced = Assert.Single(Suggest(pairs));
        Assert.Equal([F(1), F(2), F(3), F(4)], balanced.Files);

        var high = Assert.Single(Suggest(pairs, options: SessionSuggestionOptions.For(SuggestionSensitivity.High)));
        Assert.Equal([F(1), F(2), F(3), F(4), F(5)], high.Files);
    }

    // ---------------------------------------------------------------
    // Evidence, spelling, arguments
    // ---------------------------------------------------------------

    [Fact]
    public void RuleBWithNoOverlapDay_FallsBackToItsLastDay()
    {
        var pair = StrongPair.Create(F(1), F(2), SuggestionRule.OpenTogether, 1, Day(9), TimeSpan.FromMinutes(90), null);

        Assert.Equal(new SuggestionReason(0, null, TimeSpan.FromMinutes(90), Day(9)), Suggest([pair])[0].Reason);
    }

    [Fact]
    public void SameRuleDuplicates_MergeByMax()
    {
        var pairs = new List<StrongPair>
        {
            A(F(1), F(2), days: 3, lastDay: 12),
            A(F(2), F(1), days: 6, lastDay: 8),
            B(F(1), F(2), days: 1, minutes: 60, onDay: 5),
            B(F(2), F(1), days: 2, minutes: 100, onDay: 4)
        };

        var only = Assert.Single(Suggest(pairs));

        Assert.Equal(new SuggestionReason(6, Day(12), TimeSpan.FromMinutes(100), Day(4)), only.Reason);
    }

    [Fact]
    public void LastTogether_IsTheLatestRuleADay_AndRuleBDaysDoNotCount()
    {
        var pairs = new List<StrongPair>
        {
            A(F(1), F(2), lastDay: 8),
            A(F(2), F(3), lastDay: 12),
            A(F(1), F(3), lastDay: 5),
            B(F(1), F(3), onDay: 14)
        };

        Assert.Equal(Day(12), Suggest(pairs)[0].Reason.LastTogether);
    }

    [Fact]
    public void Files_AreTheTidyWindowsForm_SpelledByTheirStrongestRelation()
    {
        var pairs = new List<StrongPair>
        {
            A(@"C:/JOBS/240015/f1.pdf", @"C:\Jobs\240015\f2.pdf", days: 9),
            A(@"C:\Jobs\240015\f1.pdf", @"C:\Jobs\240015\f3.pdf", days: 8),
            A(@"C:\Jobs\240015\f2.pdf", @"C:\Jobs\240015\f3.pdf", days: 7)
        };

        var only = Assert.Single(Suggest(pairs));

        Assert.Equal([@"C:\JOBS\240015\f1.pdf", @"C:\Jobs\240015\f2.pdf", @"C:\Jobs\240015\f3.pdf"], only.Files);
    }

    [Fact]
    public void ANullArgument_Throws_AndNullElementsAreSkipped()
    {
        var pairs = Triangle();
        Func<string, bool> none = _ => false;

        Assert.Throws<ArgumentNullException>(() => SessionSuggester.Suggest(null!, [], none, Balanced, Today));
        Assert.Throws<ArgumentNullException>(() => SessionSuggester.Suggest(pairs, null!, none, Balanced, Today));
        Assert.Throws<ArgumentNullException>(() => SessionSuggester.Suggest(pairs, [], null!, Balanced, Today));
        Assert.Throws<ArgumentNullException>(() => SessionSuggester.Suggest(pairs, [], none, null!, Today));

        pairs.Add(null!);
        var result = SessionSuggester.Suggest(pairs, [null!, S("s1", F(1), F(2), F(9))], none, Balanced, Today);
        Assert.Equal("s1", Assert.Single(result).TargetSessionId);
    }

    [Fact]
    public void ACardMinGroupFilesHides_DoesNotHideAVisibleOne()
    {
        // The stronger pair {D:\a, F3} is hidden by MinGroupFiles = 3 and must not claim F3 from the triangle.
        var pairs = Triangle();
        pairs.Add(A(@"D:\a.pdf", F(3), days: 20));

        var result = Suggest(pairs, options: Balanced with { MinGroupFiles = 3 });

        Assert.Equal([F(1), F(2), F(3)], Assert.Single(result).Files);
    }

    [Fact]
    public void MinGroupFiles_FiltersTheFilesShown()
    {
        var pairs = Triangle();
        pairs.Add(A(@"D:\x.pdf", @"D:\y.pdf", days: 2));

        var result = Suggest(pairs, options: Balanced with { MinGroupFiles = 3 });

        Assert.Equal([F(1), F(2), F(3)], Assert.Single(result).Files);
    }
}
