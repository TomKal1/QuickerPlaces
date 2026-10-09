using System;
using System.Text.RegularExpressions;

namespace QuickerPlaces.RevitHandler.Core
{
    public enum WorksetsChoice { LastViewed, All, None }

    /// <summary>A request that passed step 2 of "Handling a request".</summary>
    public sealed class ValidRequest
    {
        public ValidRequest(string requestId, string centralPath, string localFolder, WorksetsChoice worksets, DateTime expiresUtc)
        {
            RequestId = requestId;
            CentralPath = centralPath;
            LocalFolder = localFolder;
            Worksets = worksets;
            ExpiresUtc = expiresUtc;
        }

        public string RequestId { get; }
        public string CentralPath { get; }
        public string LocalFolder { get; }
        public WorksetsChoice Worksets { get; }
        public DateTime ExpiresUtc { get; }
    }

    public sealed class ValidationResult
    {
        private ValidationResult(ValidRequest? request, string? errorCode, string? message)
        {
            Request = request;
            ErrorCode = errorCode;
            Message = message;
        }

        public bool Ok => Request != null;
        public ValidRequest? Request { get; }
        public string? ErrorCode { get; }
        public string? Message { get; }

        public static ValidationResult Valid(ValidRequest request) => new ValidationResult(request, null, null);
        public static ValidationResult Refused(string errorCode, string message) => new ValidationResult(null, errorCode, message);
    }

    /// <summary>Step 2: read the claimed file and refuse what the protocol says to refuse. No Revit types.</summary>
    public static class RequestValidator
    {
        private static readonly Regex RequestIdPattern = new Regex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);

        /// <summary>Reads and validates a claimed request file. An unreadable or malformed file is an invalidRequest, never an exception.</summary>
        public static ValidationResult Load(string claimedPath, string fileRequestId, HandlerContext context, Func<string, bool> fileExists)
        {
            RequestFile? request;
            try
            {
                request = ProtocolFile.FromJson<RequestFile>(ProtocolFile.ReadLimited(claimedPath));
            }
            catch (Exception ex)
            {
                return ValidationResult.Refused(ErrorCodes.InvalidRequest, "The request file could not be read: " + ex.Message);
            }

            return Validate(request, fileRequestId, context, fileExists);
        }

        /// <summary>
        /// The checks, in the order the protocol lists them. Cheap checks come first and the
        /// existence check of the central (which may touch a network share) comes last.
        /// </summary>
        public static ValidationResult Validate(RequestFile request, string fileRequestId, HandlerContext context, Func<string, bool> fileExists)
        {
            if (request.Protocol == null || request.Protocol < 1)
                return Invalid("\"protocol\" is missing or not a version number.");
            if (request.Protocol > HandlerContext.ProtocolVersion)
                return ValidationResult.Refused(ErrorCodes.UnsupportedProtocol,
                    "The request uses protocol version " + request.Protocol + "; this handler supports up to " + HandlerContext.ProtocolVersion + ".");

            if (string.IsNullOrEmpty(request.Action))
                return Invalid("\"action\" is missing.");
            if (request.Action != HandlerContext.OpenNewLocalAction)
                return ValidationResult.Refused(ErrorCodes.UnsupportedAction, "Unknown action \"" + request.Action + "\".");

            if (string.IsNullOrEmpty(request.RequestId) || !RequestIdPattern.IsMatch(request.RequestId!))
                return Invalid("\"requestId\" is missing or is not 32 lower-case hex digits.");
            if (request.RequestId != fileRequestId)
                return Invalid("\"requestId\" does not match the file name.");

            if (string.IsNullOrEmpty(request.HandlerId))
                return Invalid("\"handlerId\" is missing.");
            if (request.HandlerId != context.HandlerId)
                return Invalid("The request is for handler \"" + request.HandlerId + "\", not \"" + context.HandlerId + "\".");

            if (string.IsNullOrEmpty(request.RevitRelease))
                return Invalid("\"revitRelease\" is missing.");
            if (request.RevitRelease != context.Release)
                return Invalid("The request is for Revit " + request.RevitRelease + ", not Revit " + context.Release + ".");

            if (!ProtocolFile.TryParseUtc(request.CreatedUtc, out _))
                return Invalid("\"createdUtc\" is missing or is not a time.");
            if (!ProtocolFile.TryParseUtc(request.ExpiresUtc, out DateTime expiresUtc))
                return Invalid("\"expiresUtc\" is missing or is not a time.");
            if (context.UtcNow() > expiresUtc)
                return ValidationResult.Refused(ErrorCodes.Expired,
                    "The request expired at " + ProtocolFile.FormatUtc(expiresUtc) + " and was not acted on.");

            string? central = request.CentralPath;
            if (string.IsNullOrEmpty(central) || !PathRules.IsAbsolute(central!) || !PathRules.HasExtension(central!, ".rvt"))
                return Invalid("\"centralPath\" must be an absolute path to a .rvt file.");

            string? localFolder = request.LocalFolder;
            if (string.IsNullOrEmpty(localFolder) || !PathRules.IsAbsolute(localFolder!))
                return Invalid("\"localFolder\" must be an absolute path.");

            if (!TryParseWorksets(request.Worksets, out WorksetsChoice worksets))
                return Invalid("\"worksets\" must be \"lastViewed\", \"all\" or \"none\".");

            if (!fileExists(central!))
                return ValidationResult.Refused(ErrorCodes.CentralNotFound, "The central model does not exist or cannot be reached: " + central);

            return ValidationResult.Valid(new ValidRequest(fileRequestId, central!, localFolder!, worksets, expiresUtc));
        }

        public static bool TryParseWorksets(string? text, out WorksetsChoice worksets)
        {
            switch (text)
            {
                case "lastViewed": worksets = WorksetsChoice.LastViewed; return true;
                case "all": worksets = WorksetsChoice.All; return true;
                case "none": worksets = WorksetsChoice.None; return true;
                default: worksets = WorksetsChoice.LastViewed; return false;
            }
        }

        private static ValidationResult Invalid(string message) => ValidationResult.Refused(ErrorCodes.InvalidRequest, message);
    }
}
