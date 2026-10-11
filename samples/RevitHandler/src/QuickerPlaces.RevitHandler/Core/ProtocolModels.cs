using System.Collections.Generic;
using System.Runtime.Serialization;

namespace QuickerPlaces.RevitHandler.Core
{
    // The wire shapes of docs/revit-handler-protocol.md, version 1.
    //
    // Property names are camelCase on the wire. Times travel as strings in .NET format "O" (UTC),
    // so the serializer never touches DateTime and the output is identical on every .NET version.
    // DataContractJsonSerializer ignores properties it does not know, which is what the protocol
    // requires of readers.

    [DataContract]
    public sealed class RegistrationFile
    {
        [DataMember(Name = "protocol", Order = 1)] public int Protocol { get; set; }
        [DataMember(Name = "handlerId", Order = 2)] public string? HandlerId { get; set; }
        [DataMember(Name = "displayName", Order = 3)] public string? DisplayName { get; set; }
        [DataMember(Name = "handlerVersion", Order = 4, EmitDefaultValue = false)] public string? HandlerVersion { get; set; }
        [DataMember(Name = "revitRelease", Order = 5)] public string? RevitRelease { get; set; }
        [DataMember(Name = "actions", Order = 6)] public List<string>? Actions { get; set; }
        [DataMember(Name = "writtenUtc", Order = 7)] public string? WrittenUtc { get; set; }
    }

    [DataContract]
    public sealed class InstanceFile
    {
        [DataMember(Name = "protocol", Order = 1)] public int Protocol { get; set; }
        [DataMember(Name = "handlerId", Order = 2)] public string? HandlerId { get; set; }
        [DataMember(Name = "revitRelease", Order = 3)] public string? RevitRelease { get; set; }
        [DataMember(Name = "processId", Order = 4)] public int ProcessId { get; set; }
        [DataMember(Name = "processStartUtc", Order = 5)] public string? ProcessStartUtc { get; set; }
        [DataMember(Name = "loadedUtc", Order = 6)] public string? LoadedUtc { get; set; }
        /// <summary>Missing until the handler can take requests.</summary>
        [DataMember(Name = "readyUtc", Order = 7, EmitDefaultValue = false)] public string? ReadyUtc { get; set; }
    }

    /// <summary>
    /// A request as read from disk. Everything is nullable so that a missing property is
    /// detected by the validator (and refused as invalidRequest) instead of silently defaulting.
    /// </summary>
    [DataContract]
    public sealed class RequestFile
    {
        [DataMember(Name = "protocol", Order = 1)] public int? Protocol { get; set; }
        [DataMember(Name = "requestId", Order = 2)] public string? RequestId { get; set; }
        [DataMember(Name = "action", Order = 3)] public string? Action { get; set; }
        [DataMember(Name = "handlerId", Order = 4)] public string? HandlerId { get; set; }
        [DataMember(Name = "revitRelease", Order = 5)] public string? RevitRelease { get; set; }
        [DataMember(Name = "createdUtc", Order = 6)] public string? CreatedUtc { get; set; }
        [DataMember(Name = "expiresUtc", Order = 7)] public string? ExpiresUtc { get; set; }
        [DataMember(Name = "centralPath", Order = 8)] public string? CentralPath { get; set; }
        [DataMember(Name = "localFolder", Order = 9)] public string? LocalFolder { get; set; }
        [DataMember(Name = "worksets", Order = 10)] public string? Worksets { get; set; }
    }

    [DataContract]
    public sealed class ResultFile
    {
        [DataMember(Name = "protocol", Order = 1)] public int Protocol { get; set; }
        [DataMember(Name = "requestId", Order = 2)] public string? RequestId { get; set; }
        [DataMember(Name = "handlerId", Order = 3)] public string? HandlerId { get; set; }
        [DataMember(Name = "revitRelease", Order = 4)] public string? RevitRelease { get; set; }
        [DataMember(Name = "processId", Order = 5)] public int ProcessId { get; set; }
        [DataMember(Name = "finishedUtc", Order = 6)] public string? FinishedUtc { get; set; }
        [DataMember(Name = "ok", Order = 7)] public bool Ok { get; set; }
        // Null values are written out (as the spec's example shows them).
        [DataMember(Name = "localPath", Order = 8)] public string? LocalPath { get; set; }
        [DataMember(Name = "errorCode", Order = 9)] public string? ErrorCode { get; set; }
        [DataMember(Name = "message", Order = 10)] public string? Message { get; set; }
        [DataMember(Name = "dialogs", Order = 11)] public List<DialogRecord> Dialogs { get; set; } = new List<DialogRecord>();
    }

    [DataContract]
    public sealed class DialogRecord
    {
        [DataMember(Name = "dialogId", Order = 1)] public string? DialogId { get; set; }
        [DataMember(Name = "kind", Order = 2)] public string? Kind { get; set; }
        [DataMember(Name = "message", Order = 3)] public string? Message { get; set; }
        [DataMember(Name = "answered", Order = 4)] public bool Answered { get; set; }
        [DataMember(Name = "answer", Order = 5)] public int? Answer { get; set; }
    }
}
