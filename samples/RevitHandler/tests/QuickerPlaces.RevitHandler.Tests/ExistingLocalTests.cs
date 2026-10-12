using System;
using System.IO;
using System.Linq;
using QuickerPlaces.RevitHandler.Core;
using Xunit;

namespace QuickerPlaces.RevitHandler.Tests
{
    /// <summary>
    /// Step 4 with an old local at the new local's path, as Revit's own Create New Local does it:
    /// overwrite deletes the old local and its _backup folder; append timestamp renames both
    /// with the old local's date; keep leaves it and dates the new one.
    /// </summary>
    public class ExistingLocalTests : IDisposable
    {
        private const string Central = @"\\server\projects\Central1.rvt";
        private static readonly DateTime OldLocalWritten = new DateTime(2026, 10, 11, 23, 32, 1, DateTimeKind.Local);
        private static readonly DateTime Now = new DateTime(2026, 10, 12, 8, 30, 0, DateTimeKind.Local);

        private readonly Scratch _scratch = new Scratch();
        private readonly string _folder;
        private readonly string _local;
        private readonly string _backup;

        public ExistingLocalTests()
        {
            _folder = Path.Combine(_scratch.Folder, "REVIT_LOCAL2025");
            Directory.CreateDirectory(_folder);
            _local = Path.Combine(_folder, "Central1_tkal.rvt");
            _backup = Path.Combine(_folder, "Central1_tkal_backup");
        }

        public void Dispose() => _scratch.Dispose();

        private void OldLocal(bool withBackup = true)
        {
            File.WriteAllText(_local, "old local");
            File.SetLastWriteTime(_local, OldLocalWritten);
            if (withBackup)
            {
                Directory.CreateDirectory(_backup);
                File.WriteAllText(Path.Combine(_backup, "dummy.txt"), "x");
            }
        }

        private PreparedLocal Prepare(ExistingLocalMode mode, Func<string, bool>? isOpen = null)
            => ExistingLocalPreparer.Prepare(_folder, Central, "tkal", mode, Now, isOpen ?? (_ => false));

        private string[] Names() => Directory.EnumerateFileSystemEntries(_folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;

        [Theory]
        [InlineData(ExistingLocalMode.Keep)]
        [InlineData(ExistingLocalMode.Overwrite)]
        [InlineData(ExistingLocalMode.AppendTimestamp)]
        public void WithNoOldLocal_TheNewOneTakesThePlainName_AndNothingIsTouched(ExistingLocalMode mode)
        {
            PreparedLocal prepared = Prepare(mode);

            Assert.Equal(_local, prepared.LocalPath);
            Assert.Null(prepared.PreviousLocal);
            Assert.Empty(Names());
        }

        [Fact]
        public void Overwrite_DeletesTheOldLocalAndItsBackupFolder()
        {
            OldLocal();

            PreparedLocal prepared = Prepare(ExistingLocalMode.Overwrite);

            Assert.Equal(_local, prepared.LocalPath);
            Assert.Equal("overwritten", prepared.PreviousLocal);
            Assert.Null(prepared.PreviousLocalPath);
            Assert.Empty(Names());
        }

        [Fact]
        public void Overwrite_WithoutABackupFolder_DeletesTheOldLocal()
        {
            OldLocal(withBackup: false);

            Assert.Equal("overwritten", Prepare(ExistingLocalMode.Overwrite).PreviousLocal);
            Assert.Empty(Names());
        }

        [Theory]
        [InlineData(ExistingLocalMode.Overwrite)]
        [InlineData(ExistingLocalMode.AppendTimestamp)]
        public void AnOldLocalOpenInThisRevit_IsLocalInUse_AndNothingChanges(ExistingLocalMode mode)
        {
            OldLocal();

            var ex = Assert.Throws<LocalInUseException>(() => Prepare(mode, p => string.Equals(p, _local, StringComparison.OrdinalIgnoreCase)));

            Assert.Contains("Central1_tkal.rvt", ex.Message);
            Assert.Equal(new[] { "Central1_tkal.rvt", "Central1_tkal_backup" }, Names());
        }

        [Fact]
        public void Overwrite_WhenTheOldLocalIsLocked_IsLocalInUse_AndNothingChanges()
        {
            if (!OperatingSystem.IsWindows()) return; // Windows refuses to move an open file; other systems don't.
            OldLocal();

            using (new FileStream(_local, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Throws<LocalInUseException>(() => Prepare(ExistingLocalMode.Overwrite));

            Assert.Equal(new[] { "Central1_tkal.rvt", "Central1_tkal_backup" }, Names());
            Assert.Equal("old local", File.ReadAllText(_local));
        }

        [Fact]
        public void Overwrite_WhenTheBackupFolderIsLocked_PutsTheOldLocalBack()
        {
            if (!OperatingSystem.IsWindows()) return;
            OldLocal();

            using (new FileStream(Path.Combine(_backup, "dummy.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Throws<LocalInUseException>(() => Prepare(ExistingLocalMode.Overwrite));

            Assert.Equal(new[] { "Central1_tkal.rvt", "Central1_tkal_backup" }, Names());
            Assert.Equal("old local", File.ReadAllText(_local));
            Assert.True(File.Exists(Path.Combine(_backup, "dummy.txt")));
        }

        [Fact]
        public void AppendTimestamp_RenamesTheOldLocalAndItsBackupWithTheOldLocalsDate()
        {
            OldLocal();

            PreparedLocal prepared = Prepare(ExistingLocalMode.AppendTimestamp);

            string kept = Path.Combine(_folder, "Central1_tkal_11Oct2026_233201.rvt");
            Assert.Equal(_local, prepared.LocalPath);
            Assert.Equal("renamed", prepared.PreviousLocal);
            Assert.Equal(kept, prepared.PreviousLocalPath);
            Assert.Equal(new[] { "Central1_tkal_11Oct2026_233201.rvt", "Central1_tkal_11Oct2026_233201_backup" }, Names());
            Assert.Equal("old local", File.ReadAllText(kept));
            Assert.True(File.Exists(Path.Combine(_folder, "Central1_tkal_11Oct2026_233201_backup", "dummy.txt")));
        }

        [Fact]
        public void AppendTimestamp_WhenThatNameIsTaken_AddsANumber()
        {
            OldLocal();
            File.WriteAllText(Path.Combine(_folder, "Central1_tkal_11Oct2026_233201.rvt"), "older");

            PreparedLocal prepared = Prepare(ExistingLocalMode.AppendTimestamp);

            Assert.Equal(Path.Combine(_folder, "Central1_tkal_11Oct2026_233201-2.rvt"), prepared.PreviousLocalPath);
            Assert.Contains("Central1_tkal_11Oct2026_233201-2_backup", Names());
        }

        [Fact]
        public void AppendTimestamp_WhenTheBackupFolderIsLocked_PutsTheOldLocalBack()
        {
            if (!OperatingSystem.IsWindows()) return;
            OldLocal();

            using (new FileStream(Path.Combine(_backup, "dummy.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Throws<LocalInUseException>(() => Prepare(ExistingLocalMode.AppendTimestamp));

            Assert.Equal(new[] { "Central1_tkal.rvt", "Central1_tkal_backup" }, Names());
        }

        [Fact]
        public void Keep_LeavesTheOldLocal_AndDatesTheNewOne()
        {
            OldLocal();

            PreparedLocal prepared = Prepare(ExistingLocalMode.Keep);

            Assert.Equal(Path.Combine(_folder, "Central1_tkal_20261012-083000.rvt"), prepared.LocalPath);
            Assert.Null(prepared.PreviousLocal);
            Assert.Equal(new[] { "Central1_tkal.rvt", "Central1_tkal_backup" }, Names());
        }
    }
}
