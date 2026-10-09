using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Revit.AddIns;
using QuickerPlaces.Services.Revit.Handlers;
using QuickerPlaces.Services.Revit.Opening;
using QuickerPlaces.Tests.Fakes;
using QuickerPlaces.ViewModels;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>The Revit settings draft: the releases it lists, the handler and folder choices, Load Once, and when Save writes.</summary>
public sealed class RevitSettingsViewModelTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);

    private readonly TempDirectory _dir = new();
    private readonly RevitProtocolFolder _folder;
    private readonly FakeProcessProbe _probe = new();
    private readonly FakeInstallSource _installs = new();
    private readonly AppSettings _settings = new();
    private readonly SettingsService _service;
    private int _saved;

    public RevitSettingsViewModelTests()
    {
        _folder = new RevitProtocolFolder(Path.Combine(_dir.Path, "revit"));
        _service = new SettingsService(_dir.File("settings.json"));
        _installs.AddDefault(2024);
        _installs.AddDefault(2025);
    }

    public void Dispose() => _dir.Dispose();

    private RevitSettingsViewModel New() => new(_settings, _service,
        new RevitMachine
        {
            InstallSource = _installs, Processes = new FakeProcessLister(), Launcher = new FakeLauncher(), Probe = _probe,
        },
        new RevitHandlerRegistry(_folder, _probe, new ManualTimeProvider(new DateTimeOffset(Start))),
        () => _saved++, CultureInfo.InvariantCulture);

    private void Register(string release, string id, string name, string[]? actions = null, int? loadedPid = null, DateTime? lastSeen = null)
    {
        var registration = new HandlerRegistration
        {
            Protocol = 1, HandlerId = id, DisplayName = name, RevitRelease = release,
            Actions = actions ?? [HandlerProtocol.ActionOpenNewLocal], WrittenUtc = Start,
        };
        RevitProtocolFolder.WriteAtomic(_folder.RegistrationFile(release, id), HandlerJson.ToUtf8(registration));
        if (loadedPid is { } pid)
        {
            _probe.Running[pid] = Start;
            Instance(release, id, pid, Start);
        }
        else if (lastSeen is { } seen)
        {
            Instance(release, id, 99999, seen); // its process is gone
        }
    }

    private void Instance(string release, string id, int pid, DateTime when)
    {
        var instance = new HandlerInstance
        {
            Protocol = 1, HandlerId = id, RevitRelease = release, ProcessId = pid, ProcessStartUtc = when, LoadedUtc = when, ReadyUtc = when,
        };
        RevitProtocolFolder.WriteAtomic(_folder.InstanceFile(release, id, pid), HandlerJson.ToUtf8(instance));
    }

    private static LoadOnceEntry Entry(string name, int release = 2025) => new()
    {
        Release = release, Name = name, DllPath = @"C:\Addins\" + name + ".dll", DllSha256 = "aa", AllowedUtc = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
    };

    // --- Releases and handlers ------------------------------------------------------------

    [Fact]
    public void ListsInstalledReleases_AndAnyWithAHandler_MarkingTheUninstalled()
    {
        Register("2023", "old", "Old Tool");

        var vm = New();

        Assert.Equal(new[] { "2023", "2024", "2025" }, vm.Releases.Select(r => r.Release));
        Assert.False(vm.Releases[0].IsInstalled);
        Assert.Contains("Not installed", vm.Releases[0].InstalledNote);
        Assert.True(vm.Releases[1].IsInstalled);
        Assert.Equal("", vm.Releases[1].InstalledNote);
    }

    [Fact]
    public void NothingInstalledOrRegistered_SaysSo()
    {
        var empty = new FakeInstallSource();
        var vm = new RevitSettingsViewModel(_settings, _service,
            new RevitMachine { InstallSource = empty, Processes = new FakeProcessLister(), Launcher = new FakeLauncher(), Probe = _probe },
            new RevitHandlerRegistry(_folder, _probe));

        Assert.False(vm.HasReleases);
        Assert.Contains("No Revit 2022 or later", vm.NoReleasesText);
    }

    [Fact]
    public void ChoicesAreNone_AndTheHandlersThatCanOpenACentral_LabelledByState()
    {
        Register("2025", "contoso", "Contoso Tools", loadedPid: 4000);
        Register("2025", "seen", "Seen Tool", lastSeen: new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));
        Register("2025", "other", "Other Tool", actions: ["something-else"], loadedPid: 4001);

        var row = New().Releases.Single(r => r.Release == "2025");

        Assert.Equal(new[] { "None", "Contoso Tools (loaded)", "Seen Tool (not seen since 1 Sep 2026)" }, row.Choices.Select(c => c.Label));
        Assert.Null(row.SelectedChoice.HandlerId);
        Assert.Contains("Only handlers that can open", row.HandlerHint);
    }

    [Fact]
    public void AReleaseWithNoHandlers_ExplainsWhenOneAppears()
    {
        var row = New().Releases.Single(r => r.Release == "2024");

        Assert.Single(row.Choices);
        Assert.Contains("appears here after Revit 2024 has run once with it loaded", row.HandlerHint);
    }

    [Fact]
    public void ASavedChoiceThatIsNotOnOffer_StaysVisible()
    {
        _settings.RevitReleases!["2024"] = new RevitReleaseSettings { HandlerId = "gone" };
        Register("2025", "plain", "Plain", actions: ["something-else"]);
        _settings.RevitReleases["2025"] = new RevitReleaseSettings { HandlerId = "plain" };

        var vm = New();

        Assert.Equal("gone (not registered)", vm.Releases.Single(r => r.Release == "2024").SelectedChoice.Label);
        Assert.Equal("Plain (can't open centrals)", vm.Releases.Single(r => r.Release == "2025").SelectedChoice.Label);
    }

    [Fact]
    public void TheLocalFolderStartsEmpty_WithTheDefaultToShow_AndShowsAChosenOne()
    {
        _settings.RevitReleases!["2025"] = new RevitReleaseSettings { LocalFolder = @"D:\Locals" };

        var vm = New();

        var r2024 = vm.Releases.Single(r => r.Release == "2024");
        Assert.Equal("", r2024.LocalFolder);
        Assert.Equal(@"C:\REVIT_LOCAL2024", r2024.DefaultFolder);
        Assert.False(r2024.CanReset);
        var r2025 = vm.Releases.Single(r => r.Release == "2025");
        Assert.Equal(@"D:\Locals", r2025.LocalFolder);
        Assert.True(r2025.CanReset);
    }

    // --- Editing and saving ---------------------------------------------------------------

    [Fact]
    public void NothingChanged_MeansNothingToSave()
    {
        var vm = New();

        Assert.False(vm.HasChanges);
        Assert.False(vm.CanSave);
        Assert.False(vm.Save());
        Assert.Equal("No unsaved changes.", vm.SaveStatus);
        Assert.Equal(0, _saved);
    }

    [Fact]
    public void ChoosingAHandlerAndAFolder_SavesThemForThatRelease()
    {
        Register("2025", "contoso", "Contoso Tools", loadedPid: 4000);
        var vm = New();
        var row = vm.Releases.Single(r => r.Release == "2025");

        row.SelectedChoice = row.Choices.Single(c => c.HandlerId == "contoso");
        row.LocalFolder = @"  D:\Locals  ";

        Assert.True(vm.HasChanges);
        Assert.True(vm.CanSave);
        Assert.Equal("Unsaved changes — select Save to apply.", vm.SaveStatus);
        Assert.True(vm.Save());

        var loaded = _service.Load().RevitReleases!["2025"];
        Assert.Equal("contoso", loaded.HandlerId);
        Assert.Equal(@"D:\Locals", loaded.LocalFolder);
        Assert.False(vm.HasChanges);
        Assert.Equal(1, _saved);
    }

    [Fact]
    public void PuttingBothBackToTheDefaults_RemovesTheReleaseEntry()
    {
        _settings.RevitReleases!["2025"] = new RevitReleaseSettings { HandlerId = "contoso", LocalFolder = @"D:\Locals" };
        Register("2025", "contoso", "Contoso Tools");
        var vm = New();
        var row = vm.Releases.Single(r => r.Release == "2025");

        row.SelectedChoice = row.Choices[0];
        row.ResetFolder();
        vm.Save();

        Assert.DoesNotContain("2025", _service.Load().RevitReleases!.Keys);
    }

    [Fact]
    public void ReleasesNotShown_KeepTheirSettings()
    {
        _settings.RevitReleases!["2019"] = new RevitReleaseSettings { HandlerId = "legacy" };
        var vm = New();
        vm.Releases.Single(r => r.Release == "2024").LocalFolder = @"E:\L";

        vm.Save();

        Assert.Equal("legacy", _service.Load().RevitReleases!["2019"].HandlerId);
    }

    [Theory]
    [InlineData(@"D:\Locals", true)]
    [InlineData(@"d:/locals", true)]
    [InlineData(@"\\server\share\locals", true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData(@"Locals", false)]
    [InlineData(@"..\Locals", false)]
    [InlineData(@"C:Locals", false)]
    [InlineData(@"D:\Loc|als", false)]
    [InlineData(@"D:\Lo:cals", false)]
    public void ALocalFolderMustBeAFullPath(string text, bool valid)
    {
        var vm = New();
        var row = vm.Releases.Single(r => r.Release == "2025");

        row.LocalFolder = text;

        Assert.Equal(valid, row.FolderError.Length == 0);
        Assert.Equal(valid, vm.ErrorMessage is null);
        if (!valid)
        {
            Assert.False(vm.CanSave);
            Assert.False(vm.Save());
            Assert.Null(_service.Load().RevitReleases!.GetValueOrDefault("2025"));
            Assert.Contains("full path", vm.ErrorMessage);
        }
    }

    // --- Load Once --------------------------------------------------------------------------

    [Fact]
    public void AllowLoadOnce_IsOffByDefault_WithAnEmptyList()
    {
        var vm = New();

        Assert.False(vm.AllowLoadOnce);
        Assert.False(vm.HasEntries);
        Assert.Contains("No add-ins are allowed yet", vm.NoEntriesText);
        Assert.Contains("never Always Load", vm.AllowLoadOnceHint);
        Assert.Contains("only in a Revit that QuickerPlaces started", vm.AllowLoadOnceHint);
    }

    [Fact]
    public void TheSwitch_IsSavedWithTheSettings()
    {
        var vm = New();

        vm.AllowLoadOnce = true;
        Assert.True(vm.HasChanges);
        vm.Save();

        Assert.True(_service.Load().AllowLoadOnce);
        Assert.True(_settings.AllowLoadOnce);
    }

    [Fact]
    public void Entries_AreListedWithTheirDetails_AndRemovedOnSave()
    {
        _settings.LoadOnceEntries!.Add(Entry("DuctExporter"));
        _settings.LoadOnceEntries.Add(Entry("Other", 2024));
        _settings.AllowLoadOnce = true;
        var vm = New();
        var first = vm.Entries[0];

        Assert.Equal(("DuctExporter", "Revit 2025", @"C:\Addins\DuctExporter.dll"), (first.Name, first.ReleaseText, first.DllPath));
        Assert.Equal("Allowed 1 Oct 2026", first.AllowedText);
        Assert.Equal("Remove DuctExporter for Revit 2025", first.RemoveName);

        vm.Remove(first);

        Assert.True(vm.HasChanges);
        Assert.Equal(2, _settings.LoadOnceEntries.Count); // a draft until Save
        Assert.Single(vm.Entries);
        Assert.True(vm.Save());
        var saved = Assert.Single(_service.Load().LoadOnceEntries!);
        Assert.Equal("Other", saved.Name);
        Assert.True(_service.Load().AllowLoadOnce);
    }

    [Fact]
    public void RemovingTheLastEntry_ShowsTheHintAgain()
    {
        _settings.LoadOnceEntries!.Add(Entry("Tool"));
        var vm = New();

        vm.Remove(vm.Entries[0]);

        Assert.True(vm.NoEntriesText.Length > 0);
        Assert.False(vm.HasEntries);
    }

    [Fact]
    public void Remove_OfARowThatIsNotThere_ChangesNothing()
    {
        var vm = New();

        vm.Remove(null);
        vm.Remove(new LoadOnceEntryRowViewModel(Entry("Tool"), CultureInfo.InvariantCulture));

        Assert.False(vm.HasChanges);
    }
}
