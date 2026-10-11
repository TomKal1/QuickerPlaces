using System;
using System.IO;
using System.Text;
using QuickerPlaces.RevitHandler.Core;
using Xunit;

namespace QuickerPlaces.RevitHandler.Tests
{
    public class RequestParsingTests
    {
        private static ValidationResult Validate(string json, Func<string, bool>? exists = null, Func<DateTime>? now = null, string? fileId = null)
        {
            using var scratch = new Scratch();
            var context = scratch.NewContext(now: now);
            var request = ProtocolFile.FromJson<RequestFile>(Encoding.UTF8.GetBytes(json));
            return RequestValidator.Validate(request, fileId ?? Scratch.RequestId, context, exists ?? (_ => true));
        }

        [Fact]
        public void SpecExampleParsesVerbatim()
        {
            var request = ProtocolFile.FromJson<RequestFile>(Encoding.UTF8.GetBytes(Requests.SpecExample));

            Assert.Equal(1, request.Protocol);
            Assert.Equal(Scratch.RequestId, request.RequestId);
            Assert.Equal("open-new-local", request.Action);
            Assert.Equal("quickerplaces.sample", request.HandlerId);
            Assert.Equal("2025", request.RevitRelease);
            Assert.Equal("2026-10-08T21:14:00.0000000Z", request.CreatedUtc);
            Assert.Equal("2026-10-08T21:24:00.0000000Z", request.ExpiresUtc);
            Assert.Equal(@"\\server\projects\1234\1234_Arch_Central.rvt", request.CentralPath);
            Assert.Equal(@"C:\REVIT_LOCAL2025", request.LocalFolder);
            Assert.Equal("lastViewed", request.Worksets);
        }

        [Fact]
        public void SpecExampleValidates()
        {
            ValidationResult result = Validate(Requests.SpecExample);

            Assert.True(result.Ok, result.Message);
            Assert.Equal(WorksetsChoice.LastViewed, result.Request!.Worksets);
            Assert.Equal(new DateTime(2026, 10, 8, 21, 24, 0, DateTimeKind.Utc), result.Request.ExpiresUtc);
        }

        [Fact]
        public void ReaderAcceptsBomAndIgnoresUnknownProperties()
        {
            string json = Requests.SpecExample.Replace("\"worksets\"", "\"future\": {\"a\": [1, 2]}, \"worksets\"");
            byte[] bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(json));

            var request = ProtocolFile.FromJson<RequestFile>(bytes);

            Assert.Equal("lastViewed", request.Worksets);
        }

        [Fact]
        public void ReaderRefusesFilesOver64Kb()
        {
            using var scratch = new Scratch();
            string path = scratch.MakeFile("big.json", new string(' ', 64 * 1024 + 1));
            string small = scratch.MakeFile("small.json", new string(' ', 64 * 1024));

            Assert.Throws<InvalidDataException>(() => ProtocolFile.ReadLimited(path));
            Assert.Equal(64 * 1024, ProtocolFile.ReadLimited(small).Length);
        }

        [Theory]
        [InlineData("not json at all")]
        [InlineData("")]
        [InlineData("[1,2,3]")]
        [InlineData("{\"protocol\": \"one\"}")]
        public void UnreadableOrMalformedFileIsInvalidRequest(string content)
        {
            using var scratch = new Scratch();
            var context = scratch.NewContext();
            string path = scratch.WriteRequestFile(context, Scratch.RequestId + ".claimed-1", content);

            ValidationResult result = RequestValidator.Load(path, Scratch.RequestId, context, _ => true);

            Assert.Equal(ErrorCodes.InvalidRequest, result.ErrorCode);
        }

        [Fact]
        public void MissingFileIsInvalidRequest()
        {
            using var scratch = new Scratch();
            var result = RequestValidator.Load(Path.Combine(scratch.Folder, "nope"), Scratch.RequestId, scratch.NewContext(), _ => true);
            Assert.Equal(ErrorCodes.InvalidRequest, result.ErrorCode);
        }

        [Fact]
        public void FileOver64KbIsInvalidRequest()
        {
            using var scratch = new Scratch();
            var context = scratch.NewContext();
            string path = scratch.WriteRequestFile(context, Scratch.RequestId + ".claimed-1", Requests.SpecExample + new string(' ', 70000));

            Assert.Equal(ErrorCodes.InvalidRequest, RequestValidator.Load(path, Scratch.RequestId, context, _ => true).ErrorCode);
        }

        [Theory]
        // protocol
        [InlineData("\"protocol\": 1", "\"protocol\": 2", ErrorCodes.UnsupportedProtocol)]
        [InlineData("\"protocol\": 1,", "", ErrorCodes.InvalidRequest)]
        [InlineData("\"protocol\": 1", "\"protocol\": 0", ErrorCodes.InvalidRequest)]
        // action
        [InlineData("\"open-new-local\"", "\"close-everything\"", ErrorCodes.UnsupportedAction)]
        [InlineData("\"action\": \"open-new-local\",", "", ErrorCodes.InvalidRequest)]
        // another handler's or release's request
        [InlineData("\"handlerId\": \"quickerplaces.sample\"", "\"handlerId\": \"contoso.other\"", ErrorCodes.InvalidRequest)]
        [InlineData("\"revitRelease\": \"2025\"", "\"revitRelease\": \"2024\"", ErrorCodes.InvalidRequest)]
        [InlineData("\"handlerId\": \"quickerplaces.sample\",", "", ErrorCodes.InvalidRequest)]
        // request id
        [InlineData("4f1c2a9be0d34c7f8a61b0e5d27c9a13\"", "00000000000000000000000000000000\"", ErrorCodes.InvalidRequest)]
        // times
        [InlineData("2026-10-08T21:24:00.0000000Z", "2026-10-08T21:14:59.9999999Z", ErrorCodes.Expired)]
        [InlineData("2026-10-08T21:24:00.0000000Z", "yesterday-ish", ErrorCodes.InvalidRequest)]
        [InlineData("\"createdUtc\": \"2026-10-08T21:14:00.0000000Z\",", "", ErrorCodes.InvalidRequest)]
        // central path
        [InlineData("\\\\\\\\server\\\\projects\\\\1234\\\\1234_Arch_Central.rvt", "projects\\\\1234_Arch_Central.rvt", ErrorCodes.InvalidRequest)]
        [InlineData("1234_Arch_Central.rvt", "1234_Arch_Central.rvt.bak", ErrorCodes.InvalidRequest)]
        [InlineData("1234_Arch_Central.rvt", "1234_Arch_Central.rfa", ErrorCodes.InvalidRequest)]
        [InlineData("\"centralPath\"", "\"centralPathX\"", ErrorCodes.InvalidRequest)]
        // local folder
        [InlineData("C:\\\\REVIT_LOCAL2025", "REVIT_LOCAL2025", ErrorCodes.InvalidRequest)]
        // worksets
        [InlineData("\"lastViewed\"", "\"some\"", ErrorCodes.InvalidRequest)]
        [InlineData("\"worksets\": \"lastViewed\"", "\"worksets\": null", ErrorCodes.InvalidRequest)]
        public void EachRefusalMapsToItsErrorCode(string find, string replace, string expectedCode)
        {
            string json = Requests.SpecExample.Replace(find, replace);
            Assert.NotEqual(Requests.SpecExample, json);

            ValidationResult result = Validate(json);

            Assert.False(result.Ok);
            Assert.Equal(expectedCode, result.ErrorCode);
            Assert.False(string.IsNullOrWhiteSpace(result.Message));
        }

        [Fact]
        public void RequestIdMustMatchTheFileName()
        {
            ValidationResult result = Validate(Requests.SpecExample, fileId: "00000000000000000000000000000000");
            Assert.Equal(ErrorCodes.InvalidRequest, result.ErrorCode);
        }

        [Fact]
        public void MissingCentralFileIsCentralNotFound()
        {
            ValidationResult result = Validate(Requests.SpecExample, exists: _ => false);
            Assert.Equal(ErrorCodes.CentralNotFound, result.ErrorCode);
        }

        [Fact]
        public void ExpiryIsExclusiveAtTheInstant()
        {
            var at = new DateTime(2026, 10, 8, 21, 24, 0, DateTimeKind.Utc);
            Assert.True(Validate(Requests.SpecExample, now: () => at).Ok);
            Assert.Equal(ErrorCodes.Expired, Validate(Requests.SpecExample, now: () => at.AddTicks(1)).ErrorCode);
        }

        [Theory]
        [InlineData("lastViewed", WorksetsChoice.LastViewed)]
        [InlineData("all", WorksetsChoice.All)]
        [InlineData("none", WorksetsChoice.None)]
        public void WorksetsAreParsed(string text, WorksetsChoice expected)
        {
            ValidationResult result = Validate(Requests.SpecExample.Replace("\"lastViewed\"", "\"" + text + "\""));
            Assert.Equal(expected, result.Request!.Worksets);
        }

        [Fact]
        public void CentralNotFoundCheckComesAfterTheCheapChecks()
        {
            // A malformed worksets value is reported even though the file is also missing: no network access is needed to refuse it.
            ValidationResult result = Validate(Requests.SpecExample.Replace("\"lastViewed\"", "\"x\""), exists: _ => false);
            Assert.Equal(ErrorCodes.InvalidRequest, result.ErrorCode);
        }
    }

    internal static class ByteArrayExtensions
    {
        public static byte[] Concat(this byte[] first, byte[] second)
        {
            var all = new byte[first.Length + second.Length];
            first.CopyTo(all, 0);
            second.CopyTo(all, first.Length);
            return all;
        }
    }
}
