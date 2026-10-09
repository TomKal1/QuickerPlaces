using System;
using System.IO;
using System.Linq;
using QuickerPlaces.RevitHandler.Core;
using Xunit;

namespace QuickerPlaces.RevitHandler.Tests
{
    public class LocalPathChooserTests
    {
        private static readonly DateTime Local = new DateTime(2026, 10, 8, 23, 14, 5, DateTimeKind.Local);

        private static string Name(string path) => Path.GetFileName(path);

        [Fact]
        public void PlainName()
        {
            string path = LocalPathChooser.Choose("/loc", @"\\server\projects\1234_Arch_Central.rvt", "tkal", Local, _ => false);

            Assert.Equal(Path.Combine("/loc", "1234_Arch_Central_tkal.rvt"), path);
        }

        [Fact]
        public void InvalidFileNameCharactersBecomeUnderscores()
        {
            string path = LocalPathChooser.Choose("/loc", @"C:\m\A:B*C.rvt", "dom\\user<1>", Local, _ => false);

            Assert.Equal("A_B_C_dom_user_1_.rvt", Name(path));
        }

        [Fact]
        public void CollisionInsertsLocalTimestamp()
        {
            string plain = Path.Combine("/loc", "M_tkal.rvt");

            string path = LocalPathChooser.Choose("/loc", @"C:\x\M.rvt", "tkal", Local, p => p == plain);

            Assert.Equal("M_tkal_20261008-231405.rvt", Name(path));
        }

        [Fact]
        public void CollisionOfTheTimestampedNameAddsCounters()
        {
            var taken = new[] { "M_tkal.rvt", "M_tkal_20261008-231405.rvt", "M_tkal_20261008-231405-2.rvt", "M_tkal_20261008-231405-3.rvt" }
                .Select(n => Path.Combine("/loc", n)).ToHashSet();

            string path = LocalPathChooser.Choose("/loc", @"C:\x\M.rvt", "tkal", Local, taken.Contains);

            Assert.Equal("M_tkal_20261008-231405-4.rvt", Name(path));
        }

        [Fact]
        public void NeverReturnsAnExistingFile()
        {
            using var scratch = new Scratch();
            string folder = Path.Combine(scratch.Folder, "local");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "M_tkal.rvt"), "mine");

            for (int i = 0; i < 4; i++)
            {
                string path = LocalPathChooser.Choose(folder, @"C:\x\M.rvt", "tkal", Local, File.Exists);
                Assert.False(File.Exists(path));
                File.WriteAllText(path, "mine too");
            }

            Assert.Equal(5, Directory.GetFiles(folder).Length);
            Assert.All(Directory.GetFiles(folder), f => Assert.StartsWith("mine", File.ReadAllText(f)));
        }
    }
}
