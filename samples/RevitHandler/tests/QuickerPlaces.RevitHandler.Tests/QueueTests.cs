using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using QuickerPlaces.RevitHandler.Core;
using Xunit;

namespace QuickerPlaces.RevitHandler.Tests
{
    public class QueueTests
    {
        private const string Id1 = "11111111111111111111111111111111";
        private const string Id2 = "22222222222222222222222222222222";

        [Fact]
        public void ListsOnlyWaitingRequests()
        {
            using var scratch = new Scratch();
            var context = scratch.NewContext();
            var queue = new RequestQueue(context);
            scratch.WriteRequestFile(context, Id1 + ".json", "{}");
            scratch.WriteRequestFile(context, Id2 + ".result.json", "{}");
            scratch.WriteRequestFile(context, Id2 + ".claimed-77", "{}");
            scratch.WriteRequestFile(context, "." + Id2 + ".json." + new string('a', 32) + ".tmp", "{}");
            scratch.WriteRequestFile(context, "notes.json", "{}");
            scratch.WriteRequestFile(context, new string('A', 32) + ".json", "{}");

            Assert.Equal(new[] { Id1 }, queue.ListWaiting());
            Assert.True(queue.HasWaiting());
        }

        [Fact]
        public void MissingFolderMeansNothingWaiting()
        {
            using var scratch = new Scratch();
            var queue = new RequestQueue(scratch.NewContext());

            Assert.Empty(queue.ListWaiting());
            Assert.False(queue.HasWaiting());
        }

        [Fact]
        public void ClaimRenamesToClaimedWithProcessId()
        {
            using var scratch = new Scratch();
            var context = scratch.NewContext(processId: 4242);
            var queue = new RequestQueue(context);
            scratch.WriteRequestFile(context, Id1 + ".json", "{\"a\":1}");

            Assert.Equal(ClaimOutcome.Claimed, queue.TryClaim(Id1));

            Assert.False(File.Exists(queue.WaitingPath(Id1)));
            Assert.True(File.Exists(Path.Combine(context.RequestFolder, Id1 + ".claimed-4242")));
            Assert.Equal("{\"a\":1}", File.ReadAllText(queue.ClaimedPath(Id1)));
            Assert.Empty(queue.ListWaiting());
        }

        [Fact]
        public void ClaimOfAVanishedRequestIsGone()
        {
            using var scratch = new Scratch();
            var context = scratch.NewContext();
            var queue = new RequestQueue(context);

            Assert.Equal(ClaimOutcome.Gone, queue.TryClaim(Id1)); // no folder at all
            Directory.CreateDirectory(context.RequestFolder);
            Assert.Equal(ClaimOutcome.Gone, queue.TryClaim(Id1)); // folder, no file
        }

        [Fact]
        public void ClaimNeverOverwritesAnExistingClaimedFile()
        {
            using var scratch = new Scratch();
            var context = scratch.NewContext(processId: 5);
            var queue = new RequestQueue(context);
            scratch.WriteRequestFile(context, Id1 + ".json", "new");
            scratch.WriteRequestFile(context, Id1 + ".claimed-5", "old");

            Assert.Equal(ClaimOutcome.Failed, queue.TryClaim(Id1));

            Assert.Equal("old", File.ReadAllText(queue.ClaimedPath(Id1)));
            Assert.Equal("new", File.ReadAllText(queue.WaitingPath(Id1)));
        }

        [Fact]
        public async Task TwoClaimersExactlyOneWins()
        {
            using var scratch = new Scratch();
            for (int round = 0; round < 50; round++)
            {
                var a = scratch.NewContext(processId: 100);
                var b = scratch.NewContext(processId: 200);
                var queueA = new RequestQueue(a);
                var queueB = new RequestQueue(b);
                string id = round.ToString("x32");
                scratch.WriteRequestFile(a, id + ".json", "{}");

                ClaimOutcome[] outcomes = await Task.WhenAll(
                    Task.Run(() => queueA.TryClaim(id)),
                    Task.Run(() => queueB.TryClaim(id)));

                Assert.Equal(1, outcomes.Count(o => o == ClaimOutcome.Claimed));
                Assert.Equal(1, outcomes.Count(o => o == ClaimOutcome.Gone));
                Assert.Single(Directory.GetFiles(a.RequestFolder, id + ".claimed-*"));
            }
        }
    }
}
