using System.IO;
using System.Linq;
using QuickerPlaces.Models;
using QuickerPlaces.Models.Workspace;
using QuickerPlaces.Services.Workspace;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// workspace-layouts.json (configurable canvas plan §4): loading a file that
/// is missing, damaged, unreadable or from a newer version without ever
/// silently resetting personal layouts; repairs that say what they did;
/// unknown panels and fields kept; the backup offered after damage.
/// </summary>
public sealed class WorkspaceStoreTests
{
    private static FakePlacesStorage NewStorage(string? contents = null)
        => new() { StoreFilePath = @"C:\fake\workspace-layouts.json", ContentsToReturn = contents };

    private static WorkspaceStore NewStore(FakePlacesStorage storage, FakePlacesStorage? backup = null)
        => new(storage, backup, new ManualTimeProvider());

    private const string OneLayout = """
        {
          "schemaVersion": 1,
          "presets": [
            { "id": "p1", "name": "Mine", "panels": [ { "id": "shelf", "type": "shelf", "span": 8 } ] }
          ],
          "working": [],
          "activePresetId": "p1"
        }
        """;

    [Fact]
    public void NoFile_IsFirstRun_Writable_WithNothingToSay()
    {
        var store = NewStore(NewStorage());

        Assert.Equal(StoreLoadOutcome.NotPresent, store.LoadOutcome);
        Assert.True(store.CanWrite);
        Assert.Null(store.Notice);
        Assert.Empty(store.Document.Presets);
    }

    [Fact]
    public void AGoodFile_Loads()
    {
        var store = NewStore(NewStorage(OneLayout));

        Assert.Equal(StoreLoadOutcome.Ok, store.LoadOutcome);
        Assert.Null(store.Notice);
        var preset = Assert.Single(store.Document.Presets);
        Assert.Equal("Mine", preset.Name);
        Assert.Equal("p1", store.Document.ActivePresetId);
    }

    [Fact]
    public void ADamagedFile_IsSetAside_AndTheBackupIsOffered_NotRestoredUnasked()
    {
        var storage = NewStorage("{ not json");
        var backup = NewStorage(OneLayout);
        var store = NewStore(storage, backup);

        Assert.Equal(StoreLoadOutcome.Damaged, store.LoadOutcome);
        Assert.Equal(1, storage.QuarantineCount);
        Assert.True(store.CanWrite);
        Assert.True(store.HasBackup);
        Assert.Contains("can be restored", store.Notice);
        Assert.Empty(store.Document.Presets);
        Assert.Equal(0, storage.WriteCount);

        var restored = store.RestoreBackup(out var persistence);

        Assert.True(persistence.Saved);
        Assert.Equal("Mine", Assert.Single(restored!.Presets).Name);
        Assert.Equal(1, storage.WriteCount);
        Assert.False(store.HasBackup);
        Assert.Null(store.Notice);
    }

    [Fact]
    public void ADamagedFile_WithNoReadableBackup_SaysSo()
    {
        var store = NewStore(NewStorage("[]"), NewStorage("also damaged"));

        Assert.False(store.HasBackup);
        Assert.Contains("no earlier copy", store.Notice);
        Assert.Null(store.RestoreBackup(out var persistence));
        Assert.False(persistence.Saved);
    }

    [Fact]
    public void ADamagedFile_ThatCannotBeSetAside_IsNeverWritten()
    {
        var storage = NewStorage("{ not json");
        storage.QuarantineThrows = new IOException("locked");
        var store = NewStore(storage);

        Assert.False(store.CanWrite);
        Assert.False(store.Save(new WorkspaceDocument()).Saved);
        Assert.Equal(0, storage.WriteCount);
        Assert.Equal("{ not json", storage.ContentsToReturn);
    }

    [Fact]
    public void AnUnreadableFile_IsLeftUntouched_AndReadOnly()
    {
        var storage = NewStorage(OneLayout);
        storage.ReadThrows = new IOException("in use");
        var store = NewStore(storage);

        Assert.Equal(StoreLoadOutcome.Unreadable, store.LoadOutcome);
        Assert.False(store.CanWrite);
        Assert.False(store.Save(new WorkspaceDocument()).Saved);
        Assert.True(store.HasUnsavedChanges);
        Assert.Equal(0, storage.WriteCount);
        Assert.Equal(0, storage.QuarantineCount);
    }

    [Fact]
    public void ANewerVersionsFile_IsLeftUntouched_AndReadOnly()
    {
        var storage = NewStorage("""{ "schemaVersion": 2, "presets": [] }""");
        var store = NewStore(storage);

        Assert.Equal(StoreLoadOutcome.WrittenByNewerVersion, store.LoadOutcome);
        Assert.False(store.CanWrite);
        Assert.Contains("newer version", store.Notice);
        Assert.Equal(0, storage.QuarantineCount);
    }

    [Fact]
    public void AFailedWrite_IsReported_AndRetried()
    {
        var storage = NewStorage();
        var store = NewStore(storage);
        storage.FailNextWrite = true;

        var failed = store.Save(new WorkspaceDocument { ActivePresetId = BuiltInLayouts.DefaultId });

        Assert.False(failed.Saved);
        Assert.Contains(storage.StoreFilePath, failed.UserMessage);
        Assert.True(store.HasUnsavedChanges);
        Assert.True(store.RetrySave().Saved);
        Assert.False(store.HasUnsavedChanges);
        Assert.Contains(BuiltInLayouts.DefaultId, storage.LastWritten);
    }

    [Fact]
    public void BadValues_AreRepaired_AndTheRepairsAreListed()
    {
        var storage = NewStorage("""
            {
              "schemaVersion": 1,
              "presets": [
                { "id": "p1", "name": "  ", "panels": [ { "id": "a", "type": "shelf", "span": 5 }, { "id": "a", "type": "activity", "span": 12 }, { "type": "" } ] },
                { "id": "p1", "name": "Mine", "panels": [] },
                { "id": "builtin.activity-atlas", "name": "mine", "panels": [] }
              ],
              "working": [ { "presetId": "gone" }, { "presetId": "p1" }, { "presetId": "p1" } ],
              "activePresetId": "gone",
              "startup": { "mode": "preset", "presetId": "gone" }
            }
            """);
        var store = NewStore(storage);
        var document = store.Document;

        Assert.Equal(StoreLoadOutcome.Ok, store.LoadOutcome);
        Assert.NotEmpty(store.Repairs);
        Assert.StartsWith("Some of your saved layouts had problems", store.Notice);

        Assert.Equal(3, document.Presets.Select(p => p.Id).Distinct().Count());
        Assert.DoesNotContain(document.Presets, p => BuiltInLayouts.IsBuiltInId(p.Id));
        Assert.Equal(new[] { "Untitled layout", "Mine", "mine (2)" }, document.Presets.Select(p => p.Name));

        var panels = document.Presets[0].Panels;
        Assert.Equal(2, panels.Count);
        Assert.Equal(6, panels[0].Span);
        Assert.Equal("activity", panels[1].Id);

        Assert.Single(document.Working);
        Assert.Null(document.ActivePresetId);
        Assert.True(document.Startup.ResumesLast);

        // Nothing is written until the user changes something.
        Assert.Equal(0, storage.WriteCount);
    }

    [Fact]
    public void UnknownPanelsAndFields_SurviveARoundTrip()
    {
        var storage = NewStorage("""
            {
              "schemaVersion": 1,
              "futureSetting": { "x": 1 },
              "presets": [
                { "id": "p1", "name": "Mine", "colour": "teal",
                  "panels": [ { "id": "map", "type": "map-view", "span": 6, "zoom": 3 }, { "id": "shelf", "type": "shelf", "span": 6 } ] }
              ],
              "working": []
            }
            """);
        var store = NewStore(storage);
        var service = new WorkspaceLayoutService(store);
        Assert.True(service.Activate("p1").Saved);

        var written = storage.LastWritten!;
        Assert.Contains("futureSetting", written);
        Assert.Contains("\"colour\": \"teal\"", written);
        Assert.Contains("map-view", written);
        Assert.Contains("\"zoom\": 3", written);

        Assert.Equal("map-view", service.Panels[0].Type);
        Assert.False(PanelTypes.IsKnown(service.Panels[0].Type));
        Assert.Equal("Unavailable panel", PanelTypes.DisplayName(service.Panels[0].Type));
    }

    [Fact]
    public void AnUnknownDateRuleOrStartupMode_ReadsAsTheDefault()
    {
        var storage = NewStorage("""
            {
              "schemaVersion": 1,
              "presets": [ { "id": "p1", "name": "Mine", "panels": [], "filters": { "text": "x", "date": { "rule": "lastFortnight" } } } ],
              "startup": { "mode": "someday", "presetId": "p1" }
            }
            """);
        var store = NewStore(storage);

        Assert.Equal(StoreLoadOutcome.Ok, store.LoadOutcome);
        Assert.Equal(DateRuleKind.All, store.Document.Presets[0].Filters!.Date.Kind);
        Assert.True(store.Document.Startup.ResumesLast);
    }

    [Fact]
    public void OnDisk_AWriteKeepsABackup_ThatARestartCanRestoreAfterDamage()
    {
        using var temp = new TempDirectory();
        var first = WorkspaceStore.CreateIn(temp.Path, new ManualTimeProvider());
        var service = new WorkspaceLayoutService(first);
        Assert.True(service.SaveAsNew("Mine", false, out _, out _).Success);
        Assert.True(service.Rename(service.ActivePresetId, "Mine too", out _).Success);

        File.WriteAllText(temp.File(WorkspaceStore.FileName), "{ damaged");
        var second = WorkspaceStore.CreateIn(temp.Path, new ManualTimeProvider());

        Assert.Equal(StoreLoadOutcome.Damaged, second.LoadOutcome);
        Assert.True(second.HasBackup);
        Assert.Single(Directory.GetFiles(temp.Path, "workspace-layouts.corrupt-*.json"));

        var restoredService = new WorkspaceLayoutService(second);
        Assert.True(restoredService.RestoreBackup().Saved);
        Assert.Equal("Mine", restoredService.ActiveName);

        var third = WorkspaceStore.CreateIn(temp.Path, new ManualTimeProvider());
        Assert.Equal(StoreLoadOutcome.Ok, third.LoadOutcome);
        Assert.Equal("Mine", Assert.Single(third.Document.Presets).Name);
    }
}
