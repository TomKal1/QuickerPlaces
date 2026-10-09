using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using QuickerPlaces.RevitHandler.Core;
using Xunit;

namespace QuickerPlaces.RevitHandler.Tests
{
    public class RequestProcessorTests : IDisposable
    {
        private readonly Scratch _scratch = new Scratch();
        private readonly FakeRevit _revit = new FakeRevit();
        private readonly HandlerContext _context;
        private readonly RequestQueue _queue;
        private readonly string _central;
        private readonly string _localFolder;

        public RequestProcessorTests()
        {
            _context = _scratch.NewContext();
            _queue = new RequestQueue(_context);
            _central = _scratch.MakeFile("server/1234_Arch_Central.rvt", "central bytes");
            _localFolder = Path.Combine(_scratch.Folder, "REVIT_LOCAL2025");
        }

        public void Dispose() => _scratch.Dispose();

        private RequestProcessor NewProcessor(HandlerContext? context = null)
            => new RequestProcessor(context ?? _context, new RequestQueue(context ?? _context), _revit,
                                    new HandlerLog(_context.LogPath, _context.UtcNow));

        private void Submit(string? json = null)
            => _scratch.WriteRequestFile(_context, Scratch.RequestId + ".json", json ?? Requests.For(_central, _localFolder));

        private JsonDocument ReadResult()
        {
            string path = _queue.ResultPath(Scratch.RequestId);
            Assert.True(File.Exists(path), "a result file was written");
            return JsonDocument.Parse(File.ReadAllBytes(path));
        }

        private static string? Str(JsonElement e, string name)
            => e.GetProperty(name).ValueKind == JsonValueKind.Null ? null : e.GetProperty(name).GetString();

        [Fact]
        public void SuccessCreatesAndOpensTheLocalAndWritesTheSpecShapedResult()
        {
            Submit();
            _revit.Dialogs.Add(("TaskDialog_Something", "taskDialog", "Some message"));

            NewProcessor().ProcessWaiting();

            string expectedLocal = Path.Combine(_localFolder, "1234_Arch_Central_tkal.rvt");
            Assert.Equal(new[] { "read " + _central, "create " + _central + " -> " + expectedLocal, "open " + expectedLocal + " LastViewed" }, _revit.Calls);

            using JsonDocument result = ReadResult();
            JsonElement r = result.RootElement;
            Assert.Equal(
                new[] { "protocol", "requestId", "handlerId", "revitRelease", "processId", "finishedUtc", "ok", "localPath", "errorCode", "message", "dialogs" },
                r.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.Equal(1, r.GetProperty("protocol").GetInt32());
            Assert.Equal(Scratch.RequestId, Str(r, "requestId"));
            Assert.Equal("quickerplaces.sample", Str(r, "handlerId"));
            Assert.Equal("2025", Str(r, "revitRelease"));
            Assert.Equal(23816, r.GetProperty("processId").GetInt32());
            Assert.Equal("2026-10-08T21:15:00.0000000Z", Str(r, "finishedUtc"));
            Assert.True(r.GetProperty("ok").GetBoolean());
            Assert.Equal(expectedLocal, Str(r, "localPath"));
            Assert.Null(Str(r, "errorCode"));
            Assert.Null(Str(r, "message"));

            JsonElement dialog = Assert.Single(r.GetProperty("dialogs").EnumerateArray());
            Assert.Equal(new[] { "dialogId", "kind", "message", "answered", "answer" }, dialog.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.Equal("TaskDialog_Something", Str(dialog, "dialogId"));
            Assert.Equal("taskDialog", Str(dialog, "kind"));
            Assert.Equal("Some message", Str(dialog, "message"));
            Assert.False(dialog.GetProperty("answered").GetBoolean());
            Assert.Equal(JsonValueKind.Null, dialog.GetProperty("answer").ValueKind);
        }

        [Fact]
        public void ClaimedFileIsDeletedAfterTheResultAndNothingElseIsTouched()
        {
            Submit();

            NewProcessor().ProcessWaiting();

            Assert.Empty(Directory.GetFiles(_context.RequestFolder, "*.claimed-*"));
            Assert.False(File.Exists(_queue.WaitingPath(Scratch.RequestId)));
            Assert.Equal("central bytes", File.ReadAllText(_central));
            Assert.Equal(new[] { Scratch.RequestId + ".result.json" }, Directory.GetFiles(_context.RequestFolder).Select(Path.GetFileName).ToArray());
        }

        [Fact]
        public void NothingWaitingMeansNothingHappens()
        {
            NewProcessor().ProcessWaiting();

            Assert.Empty(_revit.Calls);
            Assert.False(_revit.CaptureEverStarted);
            Assert.False(Directory.Exists(_context.RequestFolder));
        }

        [Fact]
        public void RequestGoneAfterListingStopsWithoutAResult()
        {
            Submit();
            var stolen = _scratch.NewContext(processId: 999);
            Assert.Equal(ClaimOutcome.Claimed, new RequestQueue(stolen).TryClaim(Scratch.RequestId)); // another instance got it

            NewProcessor().ProcessWaiting();

            Assert.Empty(_revit.Calls);
            Assert.False(File.Exists(_queue.ResultPath(Scratch.RequestId)));
        }

        [Fact]
        public void TwoInstancesOfOneReleaseHandleARequestExactlyOnce()
        {
            Submit();
            var other = new FakeRevit();
            var otherContext = _scratch.NewContext(processId: 31337);

            NewProcessor().ProcessWaiting();
            new RequestProcessor(otherContext, new RequestQueue(otherContext), other, new HandlerLog(null, otherContext.UtcNow)).ProcessWaiting();

            Assert.Equal(3, _revit.Calls.Count);
            Assert.Empty(other.Calls);
        }

        [Fact]
        public void ExpiredRequestIsRefusedWithoutTouchingRevit()
        {
            Submit(Requests.For(_central, _localFolder, expiresUtc: "2026-10-08T21:14:59.0000000Z"));

            NewProcessor().ProcessWaiting();

            using JsonDocument result = ReadResult();
            Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(ErrorCodes.Expired, Str(result.RootElement, "errorCode"));
            Assert.Empty(_revit.Calls);
            Assert.False(Directory.Exists(_localFolder));
        }

        [Fact]
        public void RequestForAnotherHandlerIsRefused()
        {
            Submit(Requests.For(_central, _localFolder).Replace("quickerplaces.sample", "contoso.revittools"));

            NewProcessor().ProcessWaiting();

            using JsonDocument result = ReadResult();
            Assert.Equal(ErrorCodes.InvalidRequest, Str(result.RootElement, "errorCode"));
            Assert.Empty(_revit.Calls);
        }

        [Fact]
        public void UnreadableRequestIsRefusedUsingTheFileNameAsId()
        {
            Submit("{ this is not json");

            NewProcessor().ProcessWaiting();

            using JsonDocument result = ReadResult();
            Assert.Equal(ErrorCodes.InvalidRequest, Str(result.RootElement, "errorCode"));
            Assert.Equal(Scratch.RequestId, Str(result.RootElement, "requestId"));
        }

        [Fact]
        public void MissingCentralIsCentralNotFound()
        {
            Submit(Requests.For(Path.Combine(_scratch.Folder, "nowhere", "gone.rvt"), _localFolder));

            NewProcessor().ProcessWaiting();

            using JsonDocument result = ReadResult();
            Assert.Equal(ErrorCodes.CentralNotFound, Str(result.RootElement, "errorCode"));
            Assert.Empty(_revit.Calls);
        }

        [Fact]
        public void RevitSayingTheFileIsNotThereIsCentralNotFound()
        {
            Submit();
            _revit.ReadFails = new CentralNotFoundException("gone");

            NewProcessor().ProcessWaiting();

            using JsonDocument result = ReadResult();
            Assert.Equal(ErrorCodes.CentralNotFound, Str(result.RootElement, "errorCode"));
        }

        [Theory]
        [InlineData(false, false, true)]  // not workshared
        [InlineData(true, true, false)]   // a local
        [InlineData(true, false, false)]  // workshared but not central
        public void NotACentralIsRefused(bool workshared, bool local, bool central)
        {
            Submit();
            _revit.Info = new CentralInfo { IsWorkshared = workshared, IsLocal = local, IsCentral = central, Format = "2025" };

            NewProcessor().ProcessWaiting();

            using JsonDocument result = ReadResult();
            Assert.Equal(ErrorCodes.NotCentral, Str(result.RootElement, "errorCode"));
            Assert.Single(_revit.Calls); // only the read
            Assert.False(Directory.Exists(_localFolder));
        }

        [Fact]
        public void CentralFromAnotherReleaseIsRefusedAndNamesBoth()
        {
            Submit();
            _revit.Info = new CentralInfo { IsWorkshared = true, IsCentral = true, Format = "2024" };

            NewProcessor().ProcessWaiting();

            using JsonDocument result = ReadResult();
            Assert.Equal(ErrorCodes.ReleaseMismatch, Str(result.RootElement, "errorCode"));
            string message = Str(result.RootElement, "message")!;
            Assert.Contains("2024", message);
            Assert.Contains("2025", message);
            Assert.Single(_revit.Calls);
        }

        [Fact]
        public void LocalFolderThatCannotBeCreatedIsRefused()
        {
            string file = _scratch.MakeFile("in-the-way");
            Submit(Requests.For(_central, Path.Combine(file, "REVIT_LOCAL2025")));

            NewProcessor().ProcessWaiting();

            using JsonDocument result = ReadResult();
            Assert.Equal(ErrorCodes.LocalFolderUnavailable, Str(result.RootElement, "errorCode"));
            Assert.Single(_revit.Calls);
        }

        [Fact]
        public void LocalFolderIsCreatedWhenMissing()
        {
            Submit();
            Assert.False(Directory.Exists(_localFolder));

            NewProcessor().ProcessWaiting();

            Assert.True(Directory.Exists(_localFolder));
        }

        [Fact]
        public void ExistingLocalIsNeverOverwrittenAndTheNewOneGetsATimestamp()
        {
            Directory.CreateDirectory(_localFolder);
            string existing = Path.Combine(_localFolder, "1234_Arch_Central_tkal.rvt");
            File.WriteAllText(existing, "unsynchronised work");
            Submit();

            NewProcessor().ProcessWaiting();

            Assert.Equal("unsynchronised work", File.ReadAllText(existing));
            using JsonDocument result = ReadResult();
            string local = Str(result.RootElement, "localPath")!;
            Assert.NotEqual(existing, local);
            Assert.Matches(@"1234_Arch_Central_tkal_\d{8}-\d{6}\.rvt$", local);
        }

        [Theory]
        [InlineData("lastViewed", WorksetsChoice.LastViewed)]
        [InlineData("all", WorksetsChoice.All)]
        [InlineData("none", WorksetsChoice.None)]
        public void WorksetsAreHandedToRevit(string worksets, WorksetsChoice expected)
        {
            Submit(Requests.For(_central, _localFolder, worksets));

            NewProcessor().ProcessWaiting();

            Assert.EndsWith(" " + expected, _revit.Calls.Last());
        }

        [Fact]
        public void CreateNewLocalFailureIsReportedWithRevitsMessage()
        {
            Submit();
            _revit.CreateFails = new InvalidOperationException("Revit says no");

            NewProcessor().ProcessWaiting();

            using JsonDocument result = ReadResult();
            Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(ErrorCodes.CreateLocalFailed, Str(result.RootElement, "errorCode"));
            Assert.Equal("Revit says no", Str(result.RootElement, "message"));
            Assert.Null(Str(result.RootElement, "localPath"));
            Assert.DoesNotContain(_revit.Calls, c => c.StartsWith("open"));
        }

        [Fact]
        public void OpenFailureKeepsTheLocalPathAndTheDialogs()
        {
            Submit();
            _revit.OpenFails = new OperationCanceledException("cancelled by the user");
            _revit.Dialogs.Add((null, "dialogBox", "ignored"));

            NewProcessor().ProcessWaiting();

            using JsonDocument result = ReadResult();
            Assert.Equal(ErrorCodes.OpenFailed, Str(result.RootElement, "errorCode"));
            Assert.Equal(Path.Combine(_localFolder, "1234_Arch_Central_tkal.rvt"), Str(result.RootElement, "localPath"));
            JsonElement dialog = Assert.Single(result.RootElement.GetProperty("dialogs").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, dialog.GetProperty("dialogId").ValueKind);
            Assert.Equal("dialogBox", Str(dialog, "kind"));
        }

        [Fact]
        public void UnexpectedExceptionBecomesInternalError()
        {
            Submit();
            _revit.ReadFails = new InvalidOperationException("boom");

            NewProcessor().ProcessWaiting();

            using JsonDocument result = ReadResult();
            Assert.Equal(ErrorCodes.InternalError, Str(result.RootElement, "errorCode"));
            Assert.Contains("boom", Str(result.RootElement, "message"));
            Assert.Empty(Directory.GetFiles(_context.RequestFolder, "*.claimed-*"));
        }

        [Fact]
        public void LongMessagesAreCutAt1000Characters()
        {
            Submit();
            _revit.CreateFails = new InvalidOperationException(new string('m', 5000));

            NewProcessor().ProcessWaiting();

            using JsonDocument result = ReadResult();
            Assert.Equal(1000, Str(result.RootElement, "message")!.Length);
        }

        [Fact]
        public void DialogCaptureCoversStepFiveOnlyAndEndsAfterwards()
        {
            Submit();

            NewProcessor().ProcessWaiting();

            Assert.True(_revit.CaptureEverStarted);
            Assert.False(_revit.CaptureActive);
        }

        [Fact]
        public void DialogCaptureIsNotStartedWhenTheRequestIsRefusedEarlier()
        {
            Submit();
            _revit.Info = new CentralInfo { IsWorkshared = false };

            NewProcessor().ProcessWaiting();

            Assert.False(_revit.CaptureEverStarted);
        }

        [Fact]
        public void OnlyAllowlistedDialogsAreAnswered()
        {
            var allowlist = new DialogAllowlist(new[] { new System.Collections.Generic.KeyValuePair<string, int>("TaskDialog_Missing_Third_Party_Updater", 1001) });
            var context = _scratch.NewContext(allowlist: allowlist);
            Submit();
            _revit.Dialogs.Add(("TaskDialog_Missing_Third_Party_Updater", "taskDialog", "updater"));
            _revit.Dialogs.Add(("TaskDialog_Save_Changes", "taskDialog", "save?"));

            NewProcessor(context).ProcessWaiting();

            Assert.Equal(new int?[] { 1001, null }, _revit.Answers);
            using JsonDocument result = ReadResult();
            JsonElement[] dialogs = result.RootElement.GetProperty("dialogs").EnumerateArray().ToArray();
            Assert.True(dialogs[0].GetProperty("answered").GetBoolean());
            Assert.Equal(1001, dialogs[0].GetProperty("answer").GetInt32());
            Assert.False(dialogs[1].GetProperty("answered").GetBoolean());
        }

        [Fact]
        public void SeveralWaitingRequestsAreHandledOldestFirst()
        {
            string idA = new string('a', 32), idB = new string('b', 32);
            string a = _scratch.WriteRequestFile(_context, idA + ".json", Requests.For(_central, _localFolder, requestId: idA));
            string b = _scratch.WriteRequestFile(_context, idB + ".json", Requests.For(_central, _localFolder, requestId: idB));
            File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddMinutes(-2));
            File.SetLastWriteTimeUtc(b, DateTime.UtcNow.AddMinutes(-1));

            NewProcessor().ProcessWaiting();

            Assert.True(File.Exists(_queue.ResultPath(idA)));
            Assert.True(File.Exists(_queue.ResultPath(idB)));
            Assert.Equal(6, _revit.Calls.Count); // read, create, open for each
        }
    }
}
