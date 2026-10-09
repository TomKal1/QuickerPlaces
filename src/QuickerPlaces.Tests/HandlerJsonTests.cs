using System;
using System.IO;
using System.Linq;
using System.Text;
using QuickerPlaces.Services.Revit.Handlers;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The Revit handler protocol's files (docs/revit-handler-protocol.md): the
/// spec's own example documents read verbatim, round-trips, and every way
/// a file can be wrong being a failure rather than an exception.
/// </summary>
public sealed class HandlerJsonTests
{
    private const string RegistrationJson = """
        {
          "protocol": 1,
          "handlerId": "quickerplaces.sample",
          "displayName": "QuickerPlaces sample handler",
          "handlerVersion": "1.0.0",
          "revitRelease": "2025",
          "actions": ["open-new-local"],
          "writtenUtc": "2026-10-08T21:14:03.1234567Z"
        }
        """;

    private const string InstanceJson = """
        {
          "protocol": 1,
          "handlerId": "quickerplaces.sample",
          "revitRelease": "2025",
          "processId": 23816,
          "processStartUtc": "2026-10-08T21:13:41.5170000Z",
          "loadedUtc": "2026-10-08T21:14:03.1234567Z",
          "readyUtc": "2026-10-08T21:14:39.8800000Z"
        }
        """;

    private const string RequestJson = """
        {
          "protocol": 1,
          "requestId": "4f1c2a9be0d34c7f8a61b0e5d27c9a13",
          "action": "open-new-local",
          "handlerId": "quickerplaces.sample",
          "revitRelease": "2025",
          "createdUtc": "2026-10-08T21:14:00.0000000Z",
          "expiresUtc": "2026-10-08T21:24:00.0000000Z",
          "centralPath": "\\\\server\\projects\\1234\\1234_Arch_Central.rvt",
          "localFolder": "C:\\REVIT_LOCAL2025",
          "worksets": "lastViewed"
        }
        """;

    private const string ResultJson = """
        {
          "protocol": 1,
          "requestId": "4f1c2a9be0d34c7f8a61b0e5d27c9a13",
          "handlerId": "quickerplaces.sample",
          "revitRelease": "2025",
          "processId": 23816,
          "finishedUtc": "2026-10-08T21:15:12.0000000Z",
          "ok": true,
          "localPath": "C:\\REVIT_LOCAL2025\\1234_Arch_Central_tkal.rvt",
          "errorCode": null,
          "message": null,
          "dialogs": [
            {
              "dialogId": "TaskDialog_Missing_Third_Party_Updater",
              "kind": "taskDialog",
              "message": "The model uses a third-party updater that is not installed...",
              "answered": true,
              "answer": 1001
            }
          ]
        }
        """;

    private static readonly DateTime Utc = new(2026, 10, 8, 21, 14, 3, DateTimeKind.Utc);

    [Fact]
    public void SpecRegistration_IsRead()
    {
        var parse = HandlerJson.Parse<HandlerRegistration>(RegistrationJson);

        var registration = Assert.IsType<HandlerRegistration>(parse.Value);
        Assert.Equal(1, registration.Protocol);
        Assert.Equal("quickerplaces.sample", registration.HandlerId);
        Assert.Equal("QuickerPlaces sample handler", registration.DisplayName);
        Assert.Equal("1.0.0", registration.HandlerVersion);
        Assert.Equal("2025", registration.RevitRelease);
        Assert.Equal(["open-new-local"], registration.Actions);
        Assert.Equal(Utc.AddTicks(1234567), registration.WrittenUtc);
        Assert.Equal(DateTimeKind.Utc, registration.WrittenUtc.Kind);
    }

    [Fact]
    public void SpecInstance_IsRead()
    {
        var instance = Assert.IsType<HandlerInstance>(HandlerJson.Parse<HandlerInstance>(InstanceJson).Value);

        Assert.Equal(23816, instance.ProcessId);
        Assert.Equal(new DateTime(2026, 10, 8, 21, 13, 41, 517, DateTimeKind.Utc), instance.ProcessStartUtc);
        Assert.Equal(new DateTime(2026, 10, 8, 21, 14, 39, 880, DateTimeKind.Utc), instance.ReadyUtc);
    }

    [Fact]
    public void Instance_WithoutReadyUtc_IsStillStarting()
    {
        var json = InstanceJson.Replace("""
              "readyUtc": "2026-10-08T21:14:39.8800000Z"
            """, """
              "readyUtc": null
            """);

        var instance = Assert.IsType<HandlerInstance>(HandlerJson.Parse<HandlerInstance>(json).Value);

        Assert.Null(instance.ReadyUtc);
    }

    [Fact]
    public void SpecRequest_IsRead()
    {
        var request = Assert.IsType<HandlerRequest>(HandlerJson.Parse<HandlerRequest>(RequestJson).Value);

        Assert.Equal("4f1c2a9be0d34c7f8a61b0e5d27c9a13", request.RequestId);
        Assert.Equal(HandlerProtocol.ActionOpenNewLocal, request.Action);
        Assert.Equal(@"\\server\projects\1234\1234_Arch_Central.rvt", request.CentralPath);
        Assert.Equal(@"C:\REVIT_LOCAL2025", request.LocalFolder);
        Assert.Equal(HandlerProtocol.Worksets.LastViewed, request.Worksets);
        Assert.Equal(TimeSpan.FromMinutes(10), request.ExpiresUtc - request.CreatedUtc);
    }

    [Fact]
    public void SpecResult_IsReadWithItsDialog()
    {
        var result = Assert.IsType<HandlerResult>(HandlerJson.Parse<HandlerResult>(ResultJson).Value);

        Assert.True(result.Ok);
        Assert.Equal(@"C:\REVIT_LOCAL2025\1234_Arch_Central_tkal.rvt", result.LocalPath);
        Assert.Null(result.EffectiveErrorCode);
        var dialog = Assert.Single(result.Dialogs);
        Assert.Equal("TaskDialog_Missing_Third_Party_Updater", dialog.DialogId);
        Assert.Equal(HandlerProtocol.DialogKinds.TaskDialog, dialog.Kind);
        Assert.True(dialog.Answered);
        Assert.Equal(1001, dialog.Answer);
    }

    [Fact]
    public void Request_RoundTrips_AsCamelCaseWithFullPrecisionUtcTimes()
    {
        var request = Assert.IsType<HandlerRequest>(HandlerJson.Parse<HandlerRequest>(RequestJson).Value);

        var json = HandlerJson.ToJson(request);

        Assert.Contains("\"requestId\": \"4f1c2a9be0d34c7f8a61b0e5d27c9a13\"", json);
        Assert.Contains("\"createdUtc\": \"2026-10-08T21:14:00.0000000Z\"", json);
        Assert.Equal(request, HandlerJson.Parse<HandlerRequest>(json).Value);
    }

    [Fact]
    public void FailedResult_RoundTrips_WithItsDialogs()
    {
        var result = new HandlerResult
        {
            Protocol = 1,
            RequestId = "4f1c2a9be0d34c7f8a61b0e5d27c9a13",
            HandlerId = "quickerplaces.sample",
            RevitRelease = "2025",
            ProcessId = 12,
            FinishedUtc = Utc,
            Ok = false,
            LocalPath = @"C:\L\a.rvt",
            ErrorCode = HandlerProtocol.ErrorCodes.OpenFailed,
            Message = "Cancelled.",
            Dialogs = [new HandlerDialog { DialogId = null, Kind = "messageBox", Message = "m", Answered = false, Answer = null }],
        };

        var read = Assert.IsType<HandlerResult>(HandlerJson.Parse<HandlerResult>(HandlerJson.ToUtf8(result)).Value);

        Assert.Equal(result.ErrorCode, read.ErrorCode);
        Assert.Equal(result.Message, read.Message);
        Assert.Equal(result.LocalPath, read.LocalPath);
        Assert.Equal(result.Dialogs, read.Dialogs);
        Assert.Equal(result.FinishedUtc, read.FinishedUtc);
    }

    [Fact]
    public void Registration_RoundTrips()
    {
        var registration = new HandlerRegistration
        {
            Protocol = 1, HandlerId = "contoso.revittools", DisplayName = "Contoso", RevitRelease = "2024",
            Actions = ["open-new-local"], WrittenUtc = Utc,
        };

        var read = HandlerJson.Parse<HandlerRegistration>(HandlerJson.ToUtf8(registration)).Value;

        Assert.NotNull(read);
        Assert.Equal(registration.HandlerId, read.HandlerId);
        Assert.Equal(registration.Actions, read.Actions);
        Assert.Equal(registration.WrittenUtc, read.WrittenUtc);
        Assert.Null(read.HandlerVersion);
    }

    [Fact]
    public void UnknownProperties_AreIgnored()
    {
        var json = RegistrationJson.Replace("\"protocol\": 1,", "\"protocol\": 1, \"futureThing\": {\"a\": [1, 2]}, \"another\": 5,");

        Assert.True(HandlerJson.Parse<HandlerRegistration>(json).IsValid);
    }

    [Fact]
    public void ErrorCodeNotInTheSpec_IsKeptAndActedOnAsInternalError()
    {
        var json = ResultJson
            .Replace("\"ok\": true", "\"ok\": false")
            .Replace("\"errorCode\": null", "\"errorCode\": \"quotaExceeded\"")
            .Replace("\"message\": null", "\"message\": \"Too many.\"");

        var result = Assert.IsType<HandlerResult>(HandlerJson.Parse<HandlerResult>(json).Value);

        Assert.Equal("quotaExceeded", result.ErrorCode);
        Assert.Equal(HandlerProtocol.ErrorCodes.InternalError, result.EffectiveErrorCode);
        Assert.Equal("Too many.", result.Message);
    }

    [Fact]
    public void FailedResult_WithNoErrorCode_IsInternalError()
    {
        var json = ResultJson.Replace("\"ok\": true", "\"ok\": false");

        var result = Assert.IsType<HandlerResult>(HandlerJson.Parse<HandlerResult>(json).Value);

        Assert.Equal(HandlerProtocol.ErrorCodes.InternalError, result.EffectiveErrorCode);
    }

    [Fact]
    public void KnownErrorCodes_AreAllTheSpecsEleven()
    {
        Assert.Equal(11, HandlerProtocol.ErrorCodes.All.Count);
        foreach (var code in HandlerProtocol.ErrorCodes.All)
            Assert.Equal(code, HandlerProtocol.ErrorCodes.Effective(code));
    }

    [Fact]
    public void ResultWithoutDialogs_HasAnEmptyList()
    {
        var json = ResultJson[..ResultJson.IndexOf("\"dialogs\"", StringComparison.Ordinal)].TrimEnd().TrimEnd(',') + "}";

        var result = Assert.IsType<HandlerResult>(HandlerJson.Parse<HandlerResult>(json).Value);

        Assert.Empty(result.Dialogs);
    }

    [Fact]
    public void ByteOrderMark_IsAccepted()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(RegistrationJson)).ToArray();

        Assert.True(HandlerJson.Parse<HandlerRegistration>(bytes).IsValid);
    }

    [Fact]
    public void Written_HasNoByteOrderMark()
    {
        var bytes = HandlerJson.ToUtf8(Assert.IsType<HandlerRequest>(HandlerJson.Parse<HandlerRequest>(RequestJson).Value));

        Assert.Equal((byte)'{', bytes[0]);
    }

    [Fact]
    public void TimeWithAnOffset_IsReadAsUtc()
    {
        var json = RegistrationJson.Replace("2026-10-08T21:14:03.1234567Z", "2026-10-09T07:14:03+10:00");

        var registration = Assert.IsType<HandlerRegistration>(HandlerJson.Parse<HandlerRegistration>(json).Value);

        Assert.Equal(new DateTime(2026, 10, 8, 21, 14, 3, DateTimeKind.Utc), registration.WrittenUtc);
        Assert.Equal(DateTimeKind.Utc, registration.WrittenUtc.Kind);
    }

    [Fact]
    public void FileOver64KB_IsRefused_AndExactly64KBIsNot()
    {
        using var dir = new TempDirectory();
        var padding = new string(' ', HandlerProtocol.MaxFileBytes);
        var big = RegistrationJson + padding;
        var exact = RegistrationJson + new string(' ', HandlerProtocol.MaxFileBytes - RegistrationJson.Length);
        File.WriteAllText(dir.File("big.json"), big);
        File.WriteAllText(dir.File("exact.json"), exact);

        Assert.False(HandlerJson.ReadFile<HandlerRegistration>(dir.File("big.json")).IsValid);
        Assert.Contains("64 KB", HandlerJson.ReadFile<HandlerRegistration>(dir.File("big.json")).Failure);
        Assert.False(HandlerJson.Parse<HandlerRegistration>(Encoding.UTF8.GetBytes(big)).IsValid);
        Assert.True(HandlerJson.ReadFile<HandlerRegistration>(dir.File("exact.json")).IsValid);
    }

    [Fact]
    public void MissingFile_IsAFailure()
    {
        using var dir = new TempDirectory();

        var parse = HandlerJson.ReadFile<HandlerRegistration>(dir.File("nope.json"));

        Assert.False(parse.IsValid);
        Assert.NotNull(parse.Failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{\"protocol\": \"one\"}")]
    [InlineData("{\"protocol\": 1, \"writtenUtc\": \"yesterday\"}")]
    [InlineData("{\"protocol\": 1, \"actions\": 5}")]
    public void Garbage_IsAFailureNotAnException(string text)
    {
        Assert.False(HandlerJson.Parse<HandlerRegistration>(text).IsValid);
        Assert.False(HandlerJson.Parse<HandlerInstance>(text).IsValid);
        Assert.False(HandlerJson.Parse<HandlerRequest>(text).IsValid);
        Assert.False(HandlerJson.Parse<HandlerResult>(text).IsValid);
    }

    [Fact]
    public void BinaryGarbage_IsAFailureNotAnException()
    {
        Assert.False(HandlerJson.Parse<HandlerResult>(new byte[] { 0xFF, 0xFE, 0x00, 0x01, 0x80 }).IsValid);
    }

    [Theory]
    [InlineData("\"handlerId\": \"quickerplaces.sample\"", "\"handlerId\": \"Upper\"")]
    [InlineData("\"handlerId\": \"quickerplaces.sample\"", "\"handlerId\": \"..\\\\x\"")]
    [InlineData("\"handlerId\": \"quickerplaces.sample\"", "\"handlerId\": \"\"")]
    [InlineData("\"handlerId\": \"quickerplaces.sample\"", "\"handlerId\": \"-leading\"")]
    [InlineData("\"revitRelease\": \"2025\"", "\"revitRelease\": \"..\\\\..\"")]
    [InlineData("\"protocol\": 1", "\"protocol\": 0")]
    [InlineData("\"displayName\": \"QuickerPlaces sample handler\",", "")]
    [InlineData("\"actions\": [\"open-new-local\"],", "")]
    [InlineData("\"writtenUtc\": \"2026-10-08T21:14:03.1234567Z\"", "\"writtenUtc\": null")]
    public void InvalidRegistration_IsRefused(string find, string replace)
    {
        var parse = HandlerJson.Parse<HandlerRegistration>(RegistrationJson.Replace(find, replace));

        Assert.False(parse.IsValid);
        Assert.NotEmpty(parse.Failure!);
    }

    [Fact]
    public void InvalidRequestId_IsRefused()
    {
        foreach (var bad in new[] { "4F1C2A9BE0D34C7F8A61B0E5D27C9A13", "4f1c2a9b", "4f1c2a9be0d34c7f8a61b0e5d27c9a1g", "4f1c2a9b-e0d3-4c7f-8a61-b0e5d27c9a13" })
        {
            Assert.False(HandlerJson.Parse<HandlerRequest>(RequestJson.Replace("4f1c2a9be0d34c7f8a61b0e5d27c9a13", bad)).IsValid);
            Assert.False(HandlerJson.Parse<HandlerResult>(ResultJson.Replace("4f1c2a9be0d34c7f8a61b0e5d27c9a13", bad)).IsValid);
        }
    }

    [Fact]
    public void RequestWithUnknownWorksets_IsRefused()
    {
        Assert.False(HandlerJson.Parse<HandlerRequest>(RequestJson.Replace("lastViewed", "some")).IsValid);
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData("0", true)]
    [InlineData("quickerplaces.sample", true)]
    [InlineData("contoso-revit.tools-2", true)]
    [InlineData("", false)]
    [InlineData(".a", false)]
    [InlineData("-a", false)]
    [InlineData("A", false)]
    [InlineData("a_b", false)]
    [InlineData("a b", false)]
    [InlineData("a/b", false)]
    [InlineData("é", false)]
    public void HandlerIds(string id, bool valid) => Assert.Equal(valid, HandlerProtocol.IsValidHandlerId(id));

    [Fact]
    public void HandlerId_IsAtMost64Characters()
    {
        Assert.True(HandlerProtocol.IsValidHandlerId(new string('a', 64)));
        Assert.False(HandlerProtocol.IsValidHandlerId(new string('a', 65)));
        Assert.False(HandlerProtocol.IsValidHandlerId(null));
    }

    [Fact]
    public void RequestIds_AreThirtyTwoLowerCaseHexDigits()
    {
        Assert.True(HandlerProtocol.IsValidRequestId(Guid.NewGuid().ToString("N")));
        Assert.False(HandlerProtocol.IsValidRequestId(Guid.NewGuid().ToString("D")));
        Assert.False(HandlerProtocol.IsValidRequestId(Guid.NewGuid().ToString("N").ToUpperInvariant()));
        Assert.False(HandlerProtocol.IsValidRequestId(null));
    }

    [Fact]
    public void Worksets_AreTheThreeValues()
    {
        Assert.True(HandlerProtocol.Worksets.IsKnown("lastViewed"));
        Assert.True(HandlerProtocol.Worksets.IsKnown("all"));
        Assert.True(HandlerProtocol.Worksets.IsKnown("none"));
        Assert.False(HandlerProtocol.Worksets.IsKnown("LastViewed"));
        Assert.False(HandlerProtocol.Worksets.IsKnown(null));
    }
}
