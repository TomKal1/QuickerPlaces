using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Models.Sessions;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Session sharing plan §5, §6: the Share Session and Open Shared Session
/// dialogs' view models, from a saved session to a .qpsession and back into
/// a saved session on another PC.
/// </summary>
public sealed class SharedSessionViewModelTests
{
    private const string AliceLibraryPath = @"C:\Users\alice\Contoso\Tower B - Documents";
    private const string BobLibraryPath = @"D:\Contoso\Tower B - Documents";
    private const string LibraryUrl = "https://contoso.sharepoint.com/sites/TowerB/Shared%20Documents";

    private static readonly CloudSyncRoot AliceLibrary = new(AliceLibraryPath, LibraryUrl, CloudLibraryKind.Library);
    private static readonly CloudSyncRoot AliceOneDrive = new(
        @"C:\Users\alice\OneDrive - Contoso", "https://contoso-my.sharepoint.com/personal/alice_contoso_com/Documents", CloudLibraryKind.Personal);
    private static readonly CloudSyncRoot BobLibrary = new(BobLibraryPath, LibraryUrl, CloudLibraryKind.Library);

    private static SessionStore NewStore() => new(new FakePlacesStorage { StoreFilePath = @"C:\fake\sessions.json" }, new ManualTimeProvider());

    private static SessionSnapshot AliceSession(SessionStore store, params string[] files)
    {
        Assert.True(store.TryCreate("Tower B", new[] { "markups" }, files, out var created, out _).Success);
        return created!;
    }

    // ---------------------------------------------------------------
    // Share
    // ---------------------------------------------------------------

    [Fact]
    public void Share_TicksReachableFiles_AndLeavesThisPcOnlyFilesOut()
    {
        var session = AliceSession(NewStore(),
            AliceLibraryPath + @"\A-101.pdf",
            @"C:\Users\alice\OneDrive - Contoso\Notes.docx",
            @"C:\Users\alice\Desktop\Scratch.xlsx");

        var share = new ShareSessionViewModel(session, new[] { AliceLibrary, AliceOneDrive }, null, new ManualTimeProvider());

        Assert.Equal(new[] { true, true, false }, share.Files.Select(f => f.IsIncluded));
        Assert.Equal(new[] { "SharePoint or Teams", "Personal OneDrive", "This PC only" }, share.Files.Select(f => f.LocationText));
        Assert.Equal("2 of 3 files will be shared", share.IncludedText);
        Assert.Contains("1 file is only on this PC", share.Advice);
        Assert.Contains("1 file is in a personal OneDrive", share.Advice);
        Assert.Equal("Tower B.qpsession", share.SuggestedFileName);
    }

    [Fact]
    public void Share_HasNoAdviceWhenEveryFileIsInALibrary()
    {
        var session = AliceSession(NewStore(), AliceLibraryPath + @"\A-101.pdf");

        Assert.Null(new ShareSessionViewModel(session, new[] { AliceLibrary }, null, new ManualTimeProvider()).Advice);
    }

    [Fact]
    public void Share_BuildsTheTickedFilesWithTheNameAndTags_AndNoHistory()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 5, 3, 0, 0, TimeSpan.Zero));
        var session = AliceSession(NewStore(), AliceLibraryPath + @"\A-101.pdf", @"C:\Users\alice\Desktop\Scratch.xlsx");
        var share = new ShareSessionViewModel(session, new[] { AliceLibrary }, null, time);

        var document = share.BuildDocument()!;

        Assert.Equal("Tower B", document.Name);
        Assert.Equal(new[] { "markups" }, document.Tags);
        Assert.Equal(time.UtcNow, document.SharedAt);
        var file = Assert.Single(document.Files);
        Assert.Equal(LibraryUrl + "/A-101.pdf", file.Url);
        Assert.DoesNotContain("openedAt", SharedSessionFormat.Serialize(document), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Share_RefusesWhenNothingIsTicked()
    {
        var share = new ShareSessionViewModel(AliceSession(NewStore(), AliceLibraryPath + @"\A-101.pdf"), new[] { AliceLibrary }, null, new ManualTimeProvider());
        share.SetAllIncluded(false);

        Assert.Null(share.BuildDocument());
        Assert.Equal("Tick at least one file to share.", share.ErrorMessage);

        share.Files[0].IsIncluded = true;
        Assert.Null(share.ErrorMessage);
    }

    // ---------------------------------------------------------------
    // Open shared
    // ---------------------------------------------------------------

    /// <summary>What Alice's PC writes for <paramref name="files"/>, read back as Bob's PC reads it.</summary>
    private static SharedSessionDocument SharedFromAlice(params string[] files)
    {
        var session = AliceSession(NewStore(), files);
        var share = new ShareSessionViewModel(session, new[] { AliceLibrary, AliceOneDrive }, null, new ManualTimeProvider());
        share.SetAllIncluded(true);
        return SharedSessionFormat.Parse(SharedSessionFormat.Serialize(share.BuildDocument()!)).Document!;
    }

    private static ImportSharedSessionViewModel Open(SharedSessionDocument document, FakeShell shell, SessionStore? store = null, params CloudSyncRoot[] roots)
        => new(store ?? NewStore(), shell, roots, document, TestZones.PlusTen);

    [Fact]
    public void Open_FindsFilesInTheRecipientsSyncedLibrary_AndTicksThem()
    {
        var document = SharedFromAlice(AliceLibraryPath + @"\A-101.pdf", AliceLibraryPath + @"\A-102.pdf");
        var shell = new FakeShell { ExistingFiles = { BobLibraryPath + @"\A-101.pdf" } };

        var open = Open(document, shell, null, BobLibrary);

        Assert.Equal(new[] { SharedFileStatus.SyncedLibrary, SharedFileStatus.NotInSyncedLibrary }, open.Rows.Select(r => r.Status));
        Assert.Equal(new[] { true, false }, open.Rows.Select(r => r.IsIncluded));
        Assert.Equal("1 of 2 files found on this PC; 1 will be saved", open.IncludedText);
        Assert.Equal("Tower B", open.Name);
        Assert.Equal("markups", open.TagsText);
    }

    [Fact]
    public void Open_AFileNotFoundCannotBeTicked()
    {
        var open = Open(SharedFromAlice(AliceLibraryPath + @"\A-101.pdf"), new FakeShell());

        open.Rows[0].IsIncluded = true;

        Assert.False(open.Rows[0].IsIncluded);
        Assert.Equal(SharedFileStatus.OnlineOnly, open.Rows[0].Status);
        Assert.True(open.Rows[0].CanOpenOnline);
        Assert.Contains("doesn't sync", open.Advice);
    }

    [Fact]
    public void Open_SaveCreatesTheSessionWithTheFoundPaths()
    {
        var store = NewStore();
        var shell = new FakeShell { ExistingFiles = { BobLibraryPath + @"\A-101.pdf" } };
        var open = Open(SharedFromAlice(AliceLibraryPath + @"\A-101.pdf", AliceLibraryPath + @"\Gone.pdf"), shell, store, BobLibrary);

        Assert.True(open.Save());

        var saved = store.Find(open.SavedId!)!;
        Assert.Equal("Tower B", saved.Name);
        Assert.Equal(new[] { "markups" }, saved.Tags);
        Assert.Equal(new[] { BobLibraryPath + @"\A-101.pdf" }, saved.Files);
        Assert.Null(open.SavePersistenceMessage);
    }

    [Fact]
    public void Open_SaveRefusesWhenNothingWasFound()
    {
        var store = NewStore();
        var open = Open(SharedFromAlice(AliceLibraryPath + @"\A-101.pdf"), new FakeShell(), store);

        Assert.False(open.Save());
        Assert.Contains("nothing to save", open.ErrorMessage);
        Assert.Empty(store.Sessions);
    }

    [Fact]
    public void Open_GivesATakenNameASuffix()
    {
        var store = NewStore();
        Assert.True(store.TryCreate("Tower B", null, new[] { @"C:\x.pdf" }, out _, out _).Success);
        Assert.True(store.TryCreate("Tower B (shared)", null, new[] { @"C:\x.pdf" }, out _, out _).Success);

        var open = Open(SharedFromAlice(AliceLibraryPath + @"\A-101.pdf"), new FakeShell(), store);

        Assert.Equal("Tower B (shared 2)", open.Name);
    }

    [Fact]
    public void Open_ANameAtTheLimitStaysWithinItWithItsSuffix()
    {
        var store = NewStore();
        var longName = new string('n', SessionStore.MaxNameLength);
        Assert.True(store.TryCreate(longName, null, new[] { @"C:\x.pdf" }, out _, out _).Success);
        var document = SharedFromAlice(AliceLibraryPath + @"\A-101.pdf");
        document.Name = longName;

        var open = Open(document, new FakeShell(), store);

        Assert.Equal(SessionStore.MaxNameLength, open.Name.Length);
        Assert.EndsWith(" (shared)", open.Name);
    }

    [Fact]
    public void Open_ShowsTheStoresRefusalWhenTheNameIsTaken()
    {
        var store = NewStore();
        var shell = new FakeShell { ExistingFiles = { BobLibraryPath + @"\A-101.pdf" } };
        var open = Open(SharedFromAlice(AliceLibraryPath + @"\A-101.pdf"), shell, store, BobLibrary);
        Assert.True(store.TryCreate("Tower B", null, new[] { @"C:\x.pdf" }, out _, out _).Success);

        Assert.False(open.Save());
        Assert.Contains("already a session called", open.ErrorMessage);
    }

    [Fact]
    public void Open_LocateFindsTheOtherFilesInTheSameFolders()
    {
        var document = SharedFromAlice(
            @"C:\Users\alice\Desktop\Tower B\A-101.pdf",
            @"C:\Users\alice\Desktop\Tower B\Specs\Spec.docx",
            @"C:\Users\alice\Desktop\Tower B\Gone.pdf",
            @"C:\Elsewhere\Other.pdf");
        var shell = new FakeShell
        {
            ExistingFiles =
            {
                @"E:\From Alice\Tower B\A-101.pdf",
                @"E:\From Alice\Tower B\Specs\Spec.docx",
            },
        };
        var open = Open(document, shell);
        Assert.All(open.Rows, r => Assert.Equal(SharedFileStatus.Missing, r.Status));

        var others = open.Locate(open.Rows[0], @"E:\From Alice\Tower B\A-101.pdf");

        Assert.Equal(1, others);
        Assert.Equal(new[] { true, true, false, false }, open.Rows.Select(r => r.IsIncluded));
        Assert.Equal(@"E:\From Alice\Tower B\Specs\Spec.docx", open.Rows[1].LocalPath);
        Assert.Equal(SharedFileStatus.Located, open.Rows[1].Status);
        Assert.Equal("Found that file, and 1 more in the same folders.", open.StatusMessage);
    }

    [Fact]
    public void Open_LocateRefusesAFileThatIsNotADocument()
    {
        var open = Open(SharedFromAlice(@"C:\Users\alice\Desktop\A.pdf"), new FakeShell());

        Assert.Equal(0, open.Locate(open.Rows[0], @"C:\Temp\notes.zip"));
        Assert.False(open.Rows[0].IsFound);
        Assert.Contains("isn't a PDF, Office, text, Revit or AutoCAD file", open.ErrorMessage);
    }

    [Fact]
    public void Open_NetworkFilesWaitForTheUser_ThenUseTheAnswers()
    {
        var document = new SharedSessionDocument
        {
            Name = "Shares",
            Files =
            {
                new SharedSessionFile { Path = @"Z:\Spec.pdf", NetworkPath = @"\\files\projects\Spec.pdf", Location = SharedFileLocation.Network },
                new SharedSessionFile { Path = @"\\archive\old\Old.pdf", NetworkPath = @"\\archive\old\Old.pdf", Location = SharedFileLocation.Network },
            },
        };
        var open = Open(document, new FakeShell());

        Assert.True(open.HasUncheckedNetwork);
        Assert.Equal(new[] { @"\\files\projects\Spec.pdf", @"\\archive\old\Old.pdf" }, open.NetworkPathsToCheck);
        Assert.Equal(@"\\files, \\archive", open.NetworkServersText);
        Assert.Equal(@"On \\files, not checked yet", open.Rows[0].StatusText);

        open.ApplyNetworkCheck(new Dictionary<string, bool>
        {
            [@"\\files\projects\Spec.pdf"] = true,
            [@"\\archive\old\Old.pdf"] = false,
        });

        Assert.False(open.HasUncheckedNetwork);
        Assert.Equal(new[] { SharedFileStatus.Network, SharedFileStatus.Missing }, open.Rows.Select(r => r.Status));
        Assert.Equal(new[] { true, false }, open.Rows.Select(r => r.IsIncluded));
        Assert.Equal("Found 1 file on the network.", open.StatusMessage);
    }

    [Fact]
    public void Open_CheckAgainFindsALibrarySyncedSince()
    {
        var shell = new FakeShell();
        var open = Open(SharedFromAlice(AliceLibraryPath + @"\A-101.pdf"), shell);
        Assert.Equal(SharedFileStatus.OnlineOnly, open.Rows[0].Status);

        shell.ExistingFiles.Add(BobLibraryPath + @"\A-101.pdf");
        open.Recheck(new[] { BobLibrary });

        Assert.Equal(SharedFileStatus.SyncedLibrary, open.Rows[0].Status);
        Assert.True(open.Rows[0].IsIncluded);
        Assert.Equal("Found 1 more file.", open.StatusMessage);
    }

    [Fact]
    public void Open_UntickedFoundFilesStayUntickedOnCheckAgain()
    {
        var shell = new FakeShell { ExistingFiles = { BobLibraryPath + @"\A-101.pdf" } };
        var open = Open(SharedFromAlice(AliceLibraryPath + @"\A-101.pdf"), shell, null, BobLibrary);
        open.Rows[0].IsIncluded = false;

        open.Recheck(new[] { BobLibrary });

        Assert.False(open.Rows[0].IsIncluded);
    }

    [Fact]
    public void Open_OpensOnlyHttpsAddresses_FileAndFolder()
    {
        var shell = new FakeShell();
        var open = Open(SharedFromAlice(AliceLibraryPath + @"\A-101.pdf", @"C:\Users\alice\Desktop\B.pdf"), shell);

        Assert.True(open.OpenOnline(open.Rows[0]));
        Assert.True(open.OpenFolderOnline(open.Rows[0]));
        Assert.False(open.OpenOnline(open.Rows[1]));

        Assert.Equal(new[] { LibraryUrl + "/A-101.pdf", LibraryUrl }, shell.Opened);
    }

    [Fact]
    public void Open_ReportsAnAddressWindowsRefused()
    {
        var shell = new FakeShell { ThrowOnOpen = new InvalidOperationException("No browser.") };
        var open = Open(SharedFromAlice(AliceLibraryPath + @"\A-101.pdf"), shell);

        Assert.False(open.OpenOnline(open.Rows[0]));
        Assert.Contains("No browser.", open.ErrorMessage);
    }

    [Fact]
    public void Open_HeadingNamesTheSessionAndWhenItWasShared()
    {
        var document = SharedFromAlice(AliceLibraryPath + @"\A-101.pdf");
        document.SharedAt = new DateTimeOffset(2026, 10, 4, 20, 0, 0, TimeSpan.Zero);

        var heading = Open(document, new FakeShell()).Heading;

        Assert.StartsWith("\"Tower B\", shared ", heading);
        Assert.EndsWith(" with 1 file", heading);
        Assert.Contains(new DateTime(2026, 10, 5).ToString("d"), heading);
    }
}
