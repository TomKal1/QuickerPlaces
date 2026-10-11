using System;
using System.Collections.Generic;

namespace QuickerPlaces.Services.Revit.Handlers;

/// <summary>
/// The constants and identifier rules of the Revit handler protocol, version
/// 1 (docs/revit-handler-protocol.md, the authority for every name here).
/// UI-free and free of any Autodesk assembly; linked into the test project
/// and the qp command line.
/// </summary>
public static class HandlerProtocol
{
    /// <summary>The highest protocol version this build of QuickerPlaces speaks.</summary>
    public const int Version = 1;

    /// <summary>The only action version 1 defines.</summary>
    public const string ActionOpenNewLocal = "open-new-local";

    /// <summary>Largest protocol file a reader accepts, in bytes.</summary>
    public const int MaxFileBytes = 64 * 1024;

    public const int MaxHandlerIdLength = 64;

    /// <summary>The request's lifetime unless the caller says otherwise: long enough for a cold start of Revit with a dialog or two.</summary>
    public static readonly TimeSpan DefaultRequestLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Stale instance files are deleted once this old (and not live).</summary>
    public static readonly TimeSpan StaleInstanceAge = TimeSpan.FromHours(1);

    /// <summary>Results and claimed files nobody is waiting for are deleted once this old.</summary>
    public static readonly TimeSpan StaleRequestFileAge = TimeSpan.FromDays(1);

    /// <summary>How the new local's worksets are opened (request <c>worksets</c>).</summary>
    public static class Worksets
    {
        public const string LastViewed = "lastViewed";
        public const string All = "all";
        public const string None = "none";

        public static bool IsKnown(string? value) => value is LastViewed or All or None;
    }

    /// <summary>The <c>errorCode</c> values of a failed result.</summary>
    public static class ErrorCodes
    {
        public const string InvalidRequest = "invalidRequest";
        public const string UnsupportedProtocol = "unsupportedProtocol";
        public const string UnsupportedAction = "unsupportedAction";
        public const string Expired = "expired";
        public const string CentralNotFound = "centralNotFound";
        public const string NotCentral = "notCentral";
        public const string ReleaseMismatch = "releaseMismatch";
        public const string LocalFolderUnavailable = "localFolderUnavailable";
        public const string CreateLocalFailed = "createLocalFailed";
        public const string OpenFailed = "openFailed";
        public const string InternalError = "internalError";

        public static IReadOnlyList<string> All { get; } =
        [
            InvalidRequest, UnsupportedProtocol, UnsupportedAction, Expired, CentralNotFound, NotCentral,
            ReleaseMismatch, LocalFolderUnavailable, CreateLocalFailed, OpenFailed, InternalError,
        ];

        private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

        public static bool IsKnown(string? code) => code is not null && Known.Contains(code);

        /// <summary>The code to act on: a known code as it is, anything else (a newer handler's, or none) as <see cref="InternalError"/>. The result's message is still shown.</summary>
        public static string Effective(string? code) => IsKnown(code) ? code! : InternalError;
    }

    /// <summary>The <c>kind</c> values of a result's dialog entries.</summary>
    public static class DialogKinds
    {
        public const string TaskDialog = "taskDialog";
        public const string MessageBox = "messageBox";
        public const string DialogBox = "dialogBox";
    }

    /// <summary>True for 1 to 64 characters from a-z, 0-9, '.' and '-', starting with a letter or digit.</summary>
    public static bool IsValidHandlerId(string? id)
    {
        if (id is null || id.Length is 0 or > MaxHandlerIdLength)
            return false;
        if (!IsLowerAlphaNumeric(id[0]))
            return false;
        foreach (var c in id)
        {
            if (!IsLowerAlphaNumeric(c) && c != '.' && c != '-')
                return false;
        }
        return true;
    }

    /// <summary>True for 32 lower-case hex digits (a GUID in format "N").</summary>
    public static bool IsValidRequestId(string? id)
    {
        if (id is null || id.Length != 32)
            return false;
        foreach (var c in id)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
                return false;
        }
        return true;
    }

    /// <summary>True for a release as its four-digit year, such as "2025".</summary>
    public static bool IsValidRelease(string? release)
    {
        if (release is null || release.Length != 4)
            return false;
        foreach (var c in release)
        {
            if (c is < '0' or > '9')
                return false;
        }
        return true;
    }

    private static bool IsLowerAlphaNumeric(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9');
}
