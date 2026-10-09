using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuickerPlaces.Models.Activity;
using QuickerPlaces.Models.History;
using QuickerPlaces.Models.RecentFiles;
using QuickerPlaces.Services.History;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// History plan §3, §4: the month files that keep folder and file activity
/// forever. Saving adds or replaces days and never deletes; reading merges
/// every PC's files.
/// </summary>
public sealed class ActivityHistoryTests
{
    private const string Jobs = @"C:\Jobs";
    private const string Acme = @"C:\Jobs\Acme";
    private const string Beta = @"C:\Jobs\Beta";
    private const string A101 = @"C:\Jobs\Acme\A-101.pdf";
    private const string Report = @"C:\Jobs\Beta\Report.docx";

    private static readonly DateOnly Sep1 = new(2026, 9, 1);
    private static readonly DateOnly Sep2 = new(2026, 9, 2);
    private static readonly DateOnly Aug31 = new(2026, 8, 31);

    private readonly FakeHistoryFolder _folder = new();
    private readonly ManualTimeProvider _time = new();

    private ActivityHistory NewHistory(string machine = "DESK-1") => new(_folder, _time, machine);

    private static FolderTotal Total(double minutes, int visits)
        => new() { Milliseconds = (long)(minutes * 60_000), Visits = visits, LastSeenAt = new DateTimeOffset(2026, 9, 1, 2, 0, 0, TimeSpan.Zero) };

    private static TrackedRoot Root(string path = Jobs, params (DateOnly Date, string Folder, double Minutes, int Visits)[] days)
    {
        var root = new TrackedRoot { RootId = "r1", Path = path };
        foreach (var (date, folder, minutes, visits) in days)
        {
            if (!root.Days.TryGetValue(date, out var day))
                root.Days[date] = day = new DayActivity();
            day.Folders[folder] = Total(minutes, visits);

            if (!root.DayTotals.TryGetValue(date, out var total))
                root.DayTotals[date] = total = new DayTotal();
            total.Milliseconds += (long)(minutes * 60_000);
            total.Visits += visits;
            total.Folders = day.Folders.Count;
        }

        return root;
    }

    private static RecentFileRecord File(string path, params DateTimeOffset[] opens) => new() { Path = path, Opens = opens.ToList() };

    private static DateTimeOffset Utc(int month, int day, int hour) => new(2026, month, day, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SaveFolders_WritesOneFilePerMonth_NamedForThisPc()
    {
        var history = NewHistory();

        Assert.True(history.SaveFolders(new[] { Root(Jobs, (Aug31, Acme, 30, 2), (Sep1, Acme, 60, 3), (Sep1, Beta, 15, 1)) }));

        Assert.Equal(new[] { "2026-08 (DESK-1).json", "2026-09 (DESK-1).json" }, _folder.Files.Keys.OrderBy(k => k));
        var september = history.ReadMonth(2026, 9)!;
        var root = Assert.Single(september.Roots);
        Assert.Equal(Jobs, root.Path);
        Assert.Equal(TimeSpan.FromMinutes(60), TimeSpan.FromMilliseconds(root.Days[Sep1].Folders[Acme].Milliseconds));
        Assert.Equal(3, root.Days[Sep1].Folders["c:\\jobs\\acme"].Visits);
        Assert.Equal(TimeSpan.FromMinutes(75), TimeSpan.FromMilliseconds(root.Totals[Sep1].Milliseconds));
        Assert.Equal(2, root.Totals[Sep1].Folders);
    }

    [Fact]
    public void SaveFolders_WritesNothingWhenNothingChanged()
    {
        var history = NewHistory();
        var root = Root(Jobs, (Sep1, Acme, 60, 3));
        Assert.True(history.SaveFolders(new[] { root }));
        var writes = _folder.WriteCount;

        Assert.True(history.SaveFolders(new[] { root }));
        Assert.True(NewHistory().SaveFolders(new[] { root }));

        Assert.Equal(writes, _folder.WriteCount);
    }

    [Fact]
    public void SaveFolders_ReplacesTheDaysItIsGiven_AndKeepsTheRest()
    {
        var history = NewHistory();
        Assert.True(history.SaveFolders(new[] { Root(Jobs, (Sep1, Acme, 60, 3), (Sep2, Acme, 10, 1)) }));

        // Later: Sep1 has been pruned from the store, and Sep2 has grown.
        Assert.True(history.SaveFolders(new[] { Root(Jobs, (Sep2, Acme, 45, 4)) }));

        var root = NewHistory().ReadMonth(2026, 9)!.Roots.Single();
        Assert.Equal(60 * 60_000, root.Days[Sep1].Folders[Acme].Milliseconds);
        Assert.Equal(45 * 60_000, root.Days[Sep2].Folders[Acme].Milliseconds);
        Assert.Equal(4, root.Totals[Sep2].Visits);
    }

    [Fact]
    public void SaveFolders_KeepsTotalsForDaysWhoseDetailIsAlreadyGone()
    {
        var root = new TrackedRoot { Path = Jobs };
        root.DayTotals[Sep1] = new DayTotal { Milliseconds = 120_000, Visits = 2, Folders = 2 };

        Assert.True(NewHistory().SaveFolders(new[] { root }));

        var saved = NewHistory().ReadMonth(2026, 9)!.Roots.Single();
        Assert.Empty(saved.Days);
        Assert.Equal(2, saved.Totals[Sep1].Folders);
    }

    [Fact]
    public void SaveFolders_CopiesTheDays_SoLaterChangesInTheStoreDontLeakIn()
    {
        var history = NewHistory();
        var root = Root(Jobs, (Sep1, Acme, 60, 3));
        Assert.True(history.SaveFolders(new[] { root }));

        root.Days[Sep1].Folders[Acme].Visits = 99;
        Assert.True(history.SaveFiles(new[] { File(A101, Utc(9, 1, 1)) }));

        Assert.Equal(3, NewHistory().ReadMonth(2026, 9)!.Roots.Single().Days[Sep1].Folders[Acme].Visits);
    }

    [Fact]
    public void SaveFiles_FilesEachOpenUnderItsLocalMonth_AndJoinsRepeats()
    {
        var history = NewHistory();

        // 15:00 UTC on 30 September is 1 October in UTC+10.
        Assert.True(history.SaveFiles(new[] { File(A101, Utc(9, 1, 1), Utc(9, 30, 15)) }));
        Assert.True(history.SaveFiles(new[] { File(A101, Utc(9, 1, 1), Utc(9, 2, 3)), File(Report, Utc(9, 2, 4)) }));

        var september = history.ReadMonth(2026, 9)!;
        Assert.Equal(new[] { Utc(9, 1, 1), Utc(9, 2, 3) }, september.Files.Single(f => f.Path == A101).Opens);
        Assert.Single(september.Files.Single(f => f.Path == Report).Opens);
        Assert.Equal(new[] { Utc(9, 30, 15) }, history.ReadMonth(2026, 10)!.Files.Single().Opens);
    }

    [Fact]
    public void FoldersAndFiles_ShareTheMonthFile()
    {
        var history = NewHistory();
        Assert.True(history.SaveFolders(new[] { Root(Jobs, (Sep1, Acme, 60, 3)) }));
        Assert.True(history.SaveFiles(new[] { File(A101, Utc(9, 1, 1)) }));

        var september = NewHistory().ReadMonth(2026, 9)!;
        Assert.Single(september.Roots);
        Assert.Single(september.Files);
        Assert.Single(_folder.Files);
    }

    [Theory]
    [InlineData("DESK-1", "DESK-1")]
    [InlineData("My PC (2)", "My-PC--2-")]
    [InlineData("  ", "PC")]
    public void FileName_UsesASafeMachineLabel(string machine, string label)
        => Assert.Equal($"2026-09 ({label}).json", NewHistory(machine).FileName(2026, 9));

    [Fact]
    public void AFileFromANewerVersion_IsLeftAlone_AndTheSaveFails()
    {
        const string newer = """{"schemaVersion":2,"month":"2026-09","machine":"DESK-1","roots":[]}""";
        _folder.Files["2026-09 (DESK-1).json"] = newer;

        Assert.False(NewHistory().SaveFolders(new[] { Root(Jobs, (Sep1, Acme, 60, 3)) }));
        Assert.Equal(newer, _folder.Files["2026-09 (DESK-1).json"]);
    }

    [Fact]
    public void AnUnreadableFile_IsSetAside_AndTheMonthStartsAgain()
    {
        _folder.Files["2026-09 (DESK-1).json"] = "{ not json";

        Assert.True(NewHistory().SaveFolders(new[] { Root(Jobs, (Sep1, Acme, 60, 3)) }));

        Assert.Equal("{ not json", _folder.Files["2026-09 (DESK-1).unreadable.txt"]);
        Assert.Single(NewHistory().ReadMonth(2026, 9)!.Roots);
    }

    [Fact]
    public void AFailedWrite_FailsTheSave_AndIsTriedAgainNextTime()
    {
        var history = NewHistory();
        _folder.FailWrites = true;
        Assert.False(history.SaveFolders(new[] { Root(Jobs, (Sep1, Acme, 60, 3)) }));

        _folder.FailWrites = false;
        Assert.True(history.SaveFolders(new[] { Root(Jobs, (Sep1, Acme, 60, 3)) }));
        Assert.Single(_folder.Files);
    }

    [Fact]
    public void ReadMonth_MergesEveryPc_AddingTimeAndJoiningOpens()
    {
        Assert.True(NewHistory("DESK-1").SaveFolders(new[] { Root(Jobs, (Sep1, Acme, 60, 3)) }));
        Assert.True(NewHistory("DESK-1").SaveFiles(new[] { File(A101, Utc(9, 1, 1)) }));
        Assert.True(NewHistory("LAPTOP").SaveFolders(new[] { Root(Jobs, (Sep1, Acme, 30, 1), (Sep1, Beta, 5, 1)) }));
        Assert.True(NewHistory("LAPTOP").SaveFiles(new[] { File(A101, Utc(9, 1, 1), Utc(9, 1, 5)) }));

        var merged = NewHistory().ReadMonth(2026, 9)!;

        var root = Assert.Single(merged.Roots);
        Assert.Equal(90 * 60_000, root.Days[Sep1].Folders[Acme].Milliseconds);
        Assert.Equal(4, root.Days[Sep1].Folders[Acme].Visits);
        Assert.Equal(95 * 60_000, root.Totals[Sep1].Milliseconds);
        Assert.Equal(2, root.Totals[Sep1].Folders);
        Assert.Equal(new[] { Utc(9, 1, 1), Utc(9, 1, 5) }, merged.Files.Single().Opens);
    }

    [Fact]
    public void ReadMonth_IsNullWithoutHistory_AndSkipsWhatIsntHistory()
    {
        _folder.Files["notes.json"] = "{}";
        _folder.Files["2026-09 (OTHER).json"] = "garbage";

        Assert.Null(NewHistory().ReadMonth(2026, 9));
        Assert.Null(NewHistory().ReadMonth(2026, 10));
    }

    [Fact]
    public void Months_ListsEveryMonthAnyPcRecorded()
    {
        Assert.True(NewHistory("DESK-1").SaveFolders(new[] { Root(Jobs, (Aug31, Acme, 1, 1)) }));
        Assert.True(NewHistory("LAPTOP").SaveFolders(new[] { Root(Jobs, (Sep1, Acme, 1, 1)) }));
        Assert.True(NewHistory("DESK-1").SaveFolders(new[] { Root(Jobs, (Sep2, Acme, 1, 1)) }));

        Assert.Equal(new[] { (2026, 8), (2026, 9) }, NewHistory().Months());
    }

    [Fact]
    public void TheRealFolder_IsCreatedOnlyByTheFirstWrite_AndListsOnlyHistory()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "History");
        var folder = new HistoryFolder(path);
        var history = new ActivityHistory(folder, _time, "DESK-1");

        Assert.Empty(folder.List());
        Assert.Null(history.ReadMonth(2026, 9));
        Assert.False(Directory.Exists(path));

        Assert.True(history.SaveFolders(new[] { Root(Jobs, (Sep1, Acme, 60, 3)) }));
        Assert.Equal(new[] { "2026-09 (DESK-1).json" }, folder.List());
        Assert.Empty(Directory.GetFiles(path, "*.tmp"));

        folder.SetAside("2026-09 (DESK-1).json", _time.GetLocalNow());
        Assert.Empty(folder.List());
        Assert.Single(Directory.GetFiles(path));
    }
}
