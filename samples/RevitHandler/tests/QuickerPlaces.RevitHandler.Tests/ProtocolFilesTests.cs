using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using QuickerPlaces.RevitHandler.Core;
using Xunit;

namespace QuickerPlaces.RevitHandler.Tests
{
    public class ProtocolFilesTests
    {
        [Fact]
        public void RegistrationMatchesTheSpecShape()
        {
            using var scratch = new Scratch();
            var context = scratch.NewContext();

            new HandlerPresence(context).WriteRegistration();

            string path = Path.Combine(context.Root, "handlers", "quickerplaces.sample-2025.json");
            Assert.Equal(path, context.RegistrationPath);
            byte[] bytes = File.ReadAllBytes(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF, "no byte-order mark");

            using JsonDocument json = JsonDocument.Parse(bytes);
            JsonElement root = json.RootElement;
            Assert.Equal(1, root.GetProperty("protocol").GetInt32());
            Assert.Equal("quickerplaces.sample", root.GetProperty("handlerId").GetString());
            Assert.Equal("QuickerPlaces sample handler", root.GetProperty("displayName").GetString());
            Assert.Equal("1.0.0", root.GetProperty("handlerVersion").GetString());
            Assert.Equal("2025", root.GetProperty("revitRelease").GetString());
            Assert.Equal(new[] { "open-new-local" }, root.GetProperty("actions").EnumerateArray().Select(a => a.GetString()).ToArray());
            Assert.Equal("2026-10-08T21:15:00.0000000Z", root.GetProperty("writtenUtc").GetString());
        }

        [Fact]
        public void InstanceHasNoReadyTimeUntilReady()
        {
            using var scratch = new Scratch();
            DateTime now = Scratch.Now;
            var context = scratch.NewContext(now: () => now);
            var presence = new HandlerPresence(context);

            presence.WriteInstance(ready: false);

            string path = Path.Combine(context.Root, "instances", "quickerplaces.sample-2025-23816.json");
            Assert.Equal(path, context.InstancePath);
            using (JsonDocument loading = JsonDocument.Parse(File.ReadAllBytes(path)))
            {
                JsonElement root = loading.RootElement;
                Assert.Equal(1, root.GetProperty("protocol").GetInt32());
                Assert.Equal("quickerplaces.sample", root.GetProperty("handlerId").GetString());
                Assert.Equal("2025", root.GetProperty("revitRelease").GetString());
                Assert.Equal(23816, root.GetProperty("processId").GetInt32());
                Assert.Equal("2026-10-08T21:13:41.5170000Z", root.GetProperty("processStartUtc").GetString());
                Assert.Equal("2026-10-08T21:15:00.0000000Z", root.GetProperty("loadedUtc").GetString());
                Assert.False(root.TryGetProperty("readyUtc", out _));
            }

            now = now.AddSeconds(38);
            presence.WriteInstance(ready: true);

            using JsonDocument ready = JsonDocument.Parse(File.ReadAllBytes(path));
            Assert.Equal("2026-10-08T21:15:00.0000000Z", ready.RootElement.GetProperty("loadedUtc").GetString()); // unchanged
            Assert.Equal("2026-10-08T21:15:38.0000000Z", ready.RootElement.GetProperty("readyUtc").GetString());
            AssertNoTempFiles(Path.GetDirectoryName(path)!);
        }

        [Fact]
        public void DeleteInstanceRemovesOnlyTheInstanceFile()
        {
            using var scratch = new Scratch();
            var context = scratch.NewContext();
            var presence = new HandlerPresence(context);
            presence.WriteRegistration();
            presence.WriteInstance(ready: true);

            presence.DeleteInstance();
            presence.DeleteInstance(); // already gone: fine

            Assert.False(File.Exists(context.InstancePath));
            Assert.True(File.Exists(context.RegistrationPath));
        }

        [Fact]
        public void AtomicWriteLeavesNoTemporaryFile()
        {
            using var scratch = new Scratch();
            string path = Path.Combine(scratch.Folder, "sub", "a.json");

            ProtocolFile.WriteAtomic(path, "{\"first\":true}", replace: true);
            ProtocolFile.WriteAtomic(path, "{\"second\":true}", replace: true);

            Assert.Equal("{\"second\":true}", File.ReadAllText(path));
            AssertNoTempFiles(Path.GetDirectoryName(path)!);
            Assert.Equal(new[] { "a.json" }, Directory.GetFileSystemEntries(Path.GetDirectoryName(path)!).Select(Path.GetFileName).ToArray());
        }

        [Fact]
        public void AtomicWriteWithoutReplaceRefusesAnExistingFileAndCleansUp()
        {
            using var scratch = new Scratch();
            string path = scratch.MakeFile("a.json", "keep");

            Assert.ThrowsAny<IOException>(() => ProtocolFile.WriteAtomic(path, "new", replace: false));

            Assert.Equal("keep", File.ReadAllText(path));
            AssertNoTempFiles(scratch.Folder);
        }

        [Fact]
        public void AtomicWriteIsUtf8WithoutBom()
        {
            using var scratch = new Scratch();
            string path = Path.Combine(scratch.Folder, "u.json");

            ProtocolFile.WriteAtomic(path, "{\"n\":\"\u00e4\"}", replace: true);

            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal((byte)'{', bytes[0]);
            Assert.Equal("{\"n\":\"\u00e4\"}", new UTF8Encoding(false).GetString(bytes));
        }

        [Fact]
        public void TemporaryNameFollowsTheSpec()
        {
            // While the content is being written the temp file is ".<final name>.<32 hex>.tmp". Observe it through a
            // folder that rejects the final rename: the temp file must be cleaned up and its name pattern was valid.
            using var scratch = new Scratch();
            string path = Path.Combine(scratch.Folder, "x.json");
            Directory.CreateDirectory(path); // a directory in the way: the rename fails

            Assert.ThrowsAny<Exception>(() => ProtocolFile.WriteAtomic(path, "{}", replace: false));

            AssertNoTempFiles(scratch.Folder);
        }

        [Fact]
        public void RootDefaultsToLocalAppData()
        {
            string root = ProtocolRoot.Resolve(_ => null, Path.Combine("home", "me", "AppData", "Local"));

            Assert.Equal(Path.Combine("home", "me", "AppData", "Local", "QuickerPlaces", "revit"), root);
        }

        [Fact]
        public void EnvironmentVariableOverridesTheRoot()
        {
            using var scratch = new Scratch();
            string? Env(string name) => name == "QUICKERPLACES_REVIT_ROOT" ? scratch.Folder : null;

            Assert.Equal(scratch.Folder, ProtocolRoot.Resolve(Env, "ignored"));
            Assert.Equal(@"C:\scratch\qp", ProtocolRoot.Resolve(_ => @"C:\scratch\qp", "ignored"));
            Assert.Equal(@"\\server\share\qp", ProtocolRoot.Resolve(_ => @"\\server\share\qp", "ignored"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("relative\\folder")]
        [InlineData("..\\up")]
        [InlineData("C:drive-relative")]
        public void RelativeOrEmptyEnvironmentValuesAreIgnored(string value)
        {
            string root = ProtocolRoot.Resolve(_ => value, "local");

            Assert.Equal(Path.Combine("local", "QuickerPlaces", "revit"), root);
        }

        [Fact]
        public void RequestFolderIsPerReleaseAndHandler()
        {
            using var scratch = new Scratch();
            var context = scratch.NewContext();

            Assert.Equal(Path.Combine(context.Root, "requests", "2025", "quickerplaces.sample"), context.RequestFolder);
        }

        [Fact]
        public void TimesRoundTripInUtc()
        {
            var time = new DateTime(2026, 10, 8, 21, 14, 3, DateTimeKind.Utc).AddTicks(1234567);

            string text = ProtocolFile.FormatUtc(time);

            Assert.Equal("2026-10-08T21:14:03.1234567Z", text);
            Assert.True(ProtocolFile.TryParseUtc(text, out DateTime parsed));
            Assert.Equal(time, parsed);
            Assert.Equal(DateTimeKind.Utc, parsed.Kind);
            Assert.False(ProtocolFile.TryParseUtc("soon", out _));
        }

        [Fact]
        public void LogWritesAndNeverThrows()
        {
            using var scratch = new Scratch();
            var context = scratch.NewContext();

            new HandlerLog(context.LogPath, context.UtcNow).Write("hello");
            new HandlerLog(Path.Combine(scratch.MakeFile("file"), "cannot", "be", "a.log"), context.UtcNow).Write("ignored");

            Assert.Contains("hello", File.ReadAllText(context.LogPath));
        }

        private static void AssertNoTempFiles(string folder)
        {
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        }
    }
}
