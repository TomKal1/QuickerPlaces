using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace QuickerPlaces.Services.Revit.Handlers;

/// <summary>
/// A protocol file's contents. <see cref="Problem"/> is null when the
/// document is complete and well formed, else why not; it never throws, and
/// is what <see cref="HandlerJson"/> applies to everything it reads.
/// </summary>
public interface IHandlerDocument
{
    string? Problem();
}

/// <summary><c>handlers\&lt;handlerId&gt;-&lt;release&gt;.json</c>: what a handler can do in one release.</summary>
public sealed record HandlerRegistration : IHandlerDocument
{
    public int Protocol { get; init; }
    public string HandlerId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? HandlerVersion { get; init; }
    public string RevitRelease { get; init; } = "";

    private IReadOnlyList<string>? _actions;

    public IReadOnlyList<string> Actions
    {
        get => _actions ?? [];
        init => _actions = value;
    }

    public DateTime WrittenUtc { get; init; }

    public string? Problem() =>
        HandlerDocumentRules.Protocol(Protocol)
        ?? HandlerDocumentRules.HandlerId(HandlerId)
        ?? HandlerDocumentRules.Release(RevitRelease)
        ?? (string.IsNullOrWhiteSpace(DisplayName) ? "displayName is missing" : null)
        ?? (_actions is null ? "actions is missing" : null)
        ?? HandlerDocumentRules.Time(WrittenUtc, "writtenUtc");
}

/// <summary><c>instances\&lt;handlerId&gt;-&lt;release&gt;-&lt;processId&gt;.json</c>: a handler loaded in a running Revit.</summary>
public sealed record HandlerInstance : IHandlerDocument
{
    public int Protocol { get; init; }
    public string HandlerId { get; init; } = "";
    public string RevitRelease { get; init; } = "";
    public int ProcessId { get; init; }

    /// <summary>With <see cref="ProcessId"/>, identifies the process even after Windows reuses the id.</summary>
    public DateTime ProcessStartUtc { get; init; }

    public DateTime LoadedUtc { get; init; }

    /// <summary>When the handler could take requests; null means loaded, Revit still starting.</summary>
    public DateTime? ReadyUtc { get; init; }

    public string? Problem() =>
        HandlerDocumentRules.Protocol(Protocol)
        ?? HandlerDocumentRules.HandlerId(HandlerId)
        ?? HandlerDocumentRules.Release(RevitRelease)
        ?? (ProcessId <= 0 ? "processId is missing" : null)
        ?? HandlerDocumentRules.Time(ProcessStartUtc, "processStartUtc")
        ?? HandlerDocumentRules.Time(LoadedUtc, "loadedUtc");
}

/// <summary><c>requests\&lt;release&gt;\&lt;handlerId&gt;\&lt;requestId&gt;.json</c>: one job from QuickerPlaces.</summary>
public sealed record HandlerRequest : IHandlerDocument
{
    public int Protocol { get; init; }
    public string RequestId { get; init; } = "";
    public string Action { get; init; } = "";
    public string HandlerId { get; init; } = "";
    public string RevitRelease { get; init; } = "";
    public DateTime CreatedUtc { get; init; }
    public DateTime ExpiresUtc { get; init; }

    /// <summary>The central model as the user sees it; the handler uses it as given.</summary>
    public string CentralPath { get; init; } = "";

    public string LocalFolder { get; init; } = "";

    /// <summary>One of <see cref="HandlerProtocol.Worksets"/>.</summary>
    public string Worksets { get; init; } = "";

    public string? Problem() =>
        HandlerDocumentRules.Protocol(Protocol)
        ?? HandlerDocumentRules.RequestId(RequestId)
        ?? (string.IsNullOrEmpty(Action) ? "action is missing" : null)
        ?? HandlerDocumentRules.HandlerId(HandlerId)
        ?? HandlerDocumentRules.Release(RevitRelease)
        ?? HandlerDocumentRules.Time(CreatedUtc, "createdUtc")
        ?? HandlerDocumentRules.Time(ExpiresUtc, "expiresUtc")
        ?? (string.IsNullOrWhiteSpace(CentralPath) ? "centralPath is missing" : null)
        ?? (string.IsNullOrWhiteSpace(LocalFolder) ? "localFolder is missing" : null)
        ?? (HandlerProtocol.Worksets.IsKnown(Worksets) ? null : "worksets is not lastViewed, all or none");
}

/// <summary>A dialog Revit raised while a request was being handled, and whether the handler answered it.</summary>
public sealed record HandlerDialog
{
    public string? DialogId { get; init; }

    /// <summary>One of <see cref="HandlerProtocol.DialogKinds"/>; kept as text so a kind from a newer handler is still shown.</summary>
    public string? Kind { get; init; }

    public string? Message { get; init; }
    public bool Answered { get; init; }
    public int? Answer { get; init; }
}

/// <summary><c>&lt;requestId&gt;.result.json</c>: how a request ended.</summary>
public sealed record HandlerResult : IHandlerDocument
{
    public int Protocol { get; init; }
    public string RequestId { get; init; } = "";
    public string HandlerId { get; init; } = "";
    public string RevitRelease { get; init; } = "";
    public int ProcessId { get; init; }
    public DateTime FinishedUtc { get; init; }

    /// <summary>True only when the local was created and opened.</summary>
    public bool Ok { get; init; }

    /// <summary>The new local; also set on <c>openFailed</c>, when it was created but not opened.</summary>
    public string? LocalPath { get; init; }

    /// <summary>As written, so a code this build does not know is still representable; see <see cref="EffectiveErrorCode"/>.</summary>
    public string? ErrorCode { get; init; }

    public string? Message { get; init; }

    private IReadOnlyList<HandlerDialog>? _dialogs;

    public IReadOnlyList<HandlerDialog> Dialogs
    {
        get => _dialogs ?? [];
        init => _dialogs = value;
    }

    /// <summary>Null when <see cref="Ok"/>; otherwise a known error code, with unknown, missing or empty ones read as <c>internalError</c>.</summary>
    [JsonIgnore]
    public string? EffectiveErrorCode => Ok ? null : HandlerProtocol.ErrorCodes.Effective(ErrorCode);

    public string? Problem() =>
        HandlerDocumentRules.Protocol(Protocol)
        ?? HandlerDocumentRules.RequestId(RequestId)
        ?? HandlerDocumentRules.HandlerId(HandlerId)
        ?? HandlerDocumentRules.Release(RevitRelease)
        ?? HandlerDocumentRules.Time(FinishedUtc, "finishedUtc");
}

internal static class HandlerDocumentRules
{
    public static string? Protocol(int protocol) => protocol >= 1 ? null : "protocol is missing";

    public static string? HandlerId(string? id) => HandlerProtocol.IsValidHandlerId(id) ? null : "handlerId is missing or invalid";

    public static string? RequestId(string? id) => HandlerProtocol.IsValidRequestId(id) ? null : "requestId is missing or invalid";

    public static string? Release(string? release) => HandlerProtocol.IsValidRelease(release) ? null : "revitRelease is missing or invalid";

    public static string? Time(DateTime value, string name) => value == default ? $"{name} is missing" : null;
}
