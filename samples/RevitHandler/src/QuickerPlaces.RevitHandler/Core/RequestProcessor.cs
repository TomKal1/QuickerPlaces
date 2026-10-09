using System;
using System.Collections.Generic;
using System.IO;

namespace QuickerPlaces.RevitHandler.Core
{
    /// <summary>
    /// Steps 1 to 6 of "Handling a request": claim, check the request, check the central, choose the local path,
    /// make and open the local, write the result and delete the claimed file.
    /// Revit is reached only through <see cref="IRevitSession"/>.
    ///
    /// Safety rules (protocol): this class never copies, overwrites, renames, deletes (apart from its own claimed
    /// request file), saves, synchronises, relinquishes or closes anything.
    /// </summary>
    public sealed class RequestProcessor
    {
        private const int MaxMessageLength = 1000;

        private readonly HandlerContext _context;
        private readonly RequestQueue _queue;
        private readonly IRevitSession _revit;
        private readonly HandlerLog _log;
        private readonly Func<string, bool> _fileExists;

        public RequestProcessor(HandlerContext context, RequestQueue queue, IRevitSession revit, HandlerLog log, Func<string, bool>? fileExists = null)
        {
            _context = context;
            _queue = queue;
            _revit = revit;
            _log = log;
            _fileExists = fileExists ?? File.Exists;
        }

        /// <summary>Handles every request that is waiting now. Never throws. Does nothing when nothing is waiting.</summary>
        public void ProcessWaiting()
        {
            IReadOnlyList<string> waiting;
            try { waiting = _queue.ListWaiting(); }
            catch (Exception ex) { _log.Write("Could not list requests: " + ex.Message); return; }

            foreach (string requestId in waiting)
            {
                try { ProcessOne(requestId); }
                catch (Exception ex) { _log.Write("Request " + requestId + ": unexpected " + ex); }
            }
        }

        private void ProcessOne(string requestId)
        {
            // Step 1: claim.
            ClaimOutcome claim = _queue.TryClaim(requestId);
            if (claim == ClaimOutcome.Gone)
            {
                _log.Write("Request " + requestId + ": already claimed or cancelled; nothing to do.");
                return;
            }
            if (claim == ClaimOutcome.Failed)
            {
                _log.Write("Request " + requestId + ": could not claim it now; will try again.");
                return;
            }

            _log.Write("Request " + requestId + ": claimed.");
            ResultFile result;
            try
            {
                result = Handle(requestId);
            }
            catch (Exception ex)
            {
                _log.Write("Request " + requestId + ": " + ex);
                result = NewResult(requestId, ok: false, ErrorCodes.InternalError, "Unexpected error: " + ex.Message, null, null);
            }

            // Step 6: write the result, then delete the claimed file.
            try
            {
                ProtocolFile.WriteAtomic(_queue.ResultPath(requestId), ProtocolFile.ToJson(result), replace: true);
                _queue.DeleteClaimed(requestId);
                _log.Write("Request " + requestId + ": " + (result.Ok ? "opened " + result.LocalPath : "refused, " + result.ErrorCode + ": " + result.Message));
            }
            catch (Exception ex)
            {
                _log.Write("Request " + requestId + ": could not write the result: " + ex.Message);
            }
        }

        private ResultFile Handle(string requestId)
        {
            // Step 2: check the request.
            ValidationResult validation = RequestValidator.Load(_queue.ClaimedPath(requestId), requestId, _context, _fileExists);
            if (!validation.Ok) return Refuse(requestId, validation.ErrorCode!, validation.Message!);
            ValidRequest request = validation.Request!;

            // Step 3: check the central.
            CentralInfo info;
            try
            {
                info = _revit.ReadCentralInfo(request.CentralPath);
            }
            catch (CentralNotFoundException ex)
            {
                return Refuse(requestId, ErrorCodes.CentralNotFound, ex.Message);
            }

            if (!info.IsWorkshared || info.IsLocal || !info.IsCentral)
                return Refuse(requestId, ErrorCodes.NotCentral,
                    "The file is not a workshared central model (it is not workshared, or it is a local file).");

            string saved = (info.Format ?? "").Trim();
            if (saved != _context.Release)
                return Refuse(requestId, ErrorCodes.ReleaseMismatch,
                    "The central model was saved in Revit " + (saved.Length == 0 ? "(unknown release)" : saved)
                    + "; this is Revit " + _context.Release + ". It is not upgraded.");

            // Step 4: choose the local path.
            try
            {
                Directory.CreateDirectory(request.LocalFolder);
            }
            catch (Exception ex)
            {
                return Refuse(requestId, ErrorCodes.LocalFolderUnavailable, "The local folder cannot be created: " + request.LocalFolder + " (" + ex.Message + ")");
            }

            string localPath = LocalPathChooser.Choose(
                request.LocalFolder, request.CentralPath, _revit.Username, _context.UtcNow().ToLocalTime(),
                p => File.Exists(p) || Directory.Exists(p));

            // Step 5: make and open the local, with dialog handling active for this step only.
            var dialogs = new DialogRecorder(_context.Allowlist);
            using (_revit.BeginDialogCapture(dialogs))
            {
                try
                {
                    _revit.CreateNewLocal(request.CentralPath, localPath);
                }
                catch (Exception ex)
                {
                    return NewResult(requestId, false, ErrorCodes.CreateLocalFailed, ex.Message, null, dialogs);
                }

                try
                {
                    _revit.OpenLocal(localPath, request.Worksets);
                }
                catch (Exception ex)
                {
                    // The local exists but is not open: report its path so the user can open it.
                    return NewResult(requestId, false, ErrorCodes.OpenFailed, ex.Message, localPath, dialogs);
                }
            }

            return NewResult(requestId, true, null, null, localPath, dialogs);
        }

        private ResultFile Refuse(string requestId, string errorCode, string message)
            => NewResult(requestId, false, errorCode, message, null, null);

        private ResultFile NewResult(string requestId, bool ok, string? errorCode, string? message, string? localPath, DialogRecorder? dialogs)
        {
            if (message != null && message.Length > MaxMessageLength) message = message.Substring(0, MaxMessageLength);

            return new ResultFile
            {
                Protocol = HandlerContext.ProtocolVersion,
                RequestId = requestId,
                HandlerId = _context.HandlerId,
                RevitRelease = _context.Release,
                ProcessId = _context.ProcessId,
                FinishedUtc = ProtocolFile.FormatUtc(_context.UtcNow()),
                Ok = ok,
                LocalPath = localPath,
                ErrorCode = errorCode,
                Message = message,
                Dialogs = dialogs == null ? new List<DialogRecord>() : new List<DialogRecord>(dialogs.Records),
            };
        }
    }
}
