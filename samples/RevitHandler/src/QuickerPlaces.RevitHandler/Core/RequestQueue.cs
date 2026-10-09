using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace QuickerPlaces.RevitHandler.Core
{
    public enum ClaimOutcome
    {
        /// <summary>The rename succeeded; this instance owns the request.</summary>
        Claimed,
        /// <summary>The source is gone: another instance claimed it, or QuickerPlaces cancelled it. Stop, no result.</summary>
        Gone,
        /// <summary>The rename failed for another reason (for example the file is locked). Try again later.</summary>
        Failed,
    }

    /// <summary>This handler's own request folder: <c>requests\&lt;release&gt;\&lt;handlerId&gt;\</c>.</summary>
    public sealed class RequestQueue
    {
        private static readonly Regex WaitingName = new Regex("^[0-9a-f]{32}\\.json$", RegexOptions.CultureInvariant);

        private readonly HandlerContext _context;

        public RequestQueue(HandlerContext context) => _context = context;

        public static bool IsRequestId(string text) => Regex.IsMatch(text, "^[0-9a-f]{32}$", RegexOptions.CultureInvariant);

        public string Folder => _context.RequestFolder;

        public string WaitingPath(string requestId) => Path.Combine(Folder, requestId + ".json");
        public string ClaimedPath(string requestId) => Path.Combine(Folder, requestId + ".claimed-" + _context.ProcessId);
        public string ResultPath(string requestId) => Path.Combine(Folder, requestId + ".result.json");

        /// <summary>
        /// Request ids waiting to be claimed, oldest first. Only <c>&lt;32 hex&gt;.json</c> counts: temporary files
        /// (leading '.', ending '.tmp'), results and claimed files are ignored.
        /// </summary>
        public IReadOnlyList<string> ListWaiting()
        {
            if (!Directory.Exists(Folder)) return new string[0];

            try
            {
                return new DirectoryInfo(Folder).EnumerateFiles("*.json")
                    .Where(f => WaitingName.IsMatch(f.Name))
                    .OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.Ordinal)
                    .Select(f => f.Name.Substring(0, 32))
                    .ToList();
            }
            catch (IOException) { return new string[0]; }
            catch (UnauthorizedAccessException) { return new string[0]; }
        }

        public bool HasWaiting() => ListWaiting().Count > 0;

        /// <summary>Claims a request by renaming it to <c>&lt;requestId&gt;.claimed-&lt;processId&gt;</c>. Never overwrites.</summary>
        public ClaimOutcome TryClaim(string requestId)
        {
            try
            {
                File.Move(WaitingPath(requestId), ClaimedPath(requestId)); // throws if the target exists: never overwrites
                return ClaimOutcome.Claimed;
            }
            catch (FileNotFoundException) { return ClaimOutcome.Gone; }
            catch (DirectoryNotFoundException) { return ClaimOutcome.Gone; }
            catch (IOException) { return ClaimOutcome.Failed; }
            catch (UnauthorizedAccessException) { return ClaimOutcome.Failed; }
        }

        public void DeleteClaimed(string requestId)
        {
            File.Delete(ClaimedPath(requestId));
        }
    }
}
