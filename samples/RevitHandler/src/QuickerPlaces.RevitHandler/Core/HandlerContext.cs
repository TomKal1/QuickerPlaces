using System;

namespace QuickerPlaces.RevitHandler.Core
{
    /// <summary>
    /// Everything that identifies this handler and its environment. Built once by the add-in;
    /// tests build their own with a fixed clock, process id and scratch root.
    /// To adapt the sample, change <see cref="HandlerId"/> and <see cref="DisplayName"/> where the add-in creates it.
    /// </summary>
    public sealed class HandlerContext
    {
        public const int ProtocolVersion = 1;
        public const string OpenNewLocalAction = "open-new-local";

        public HandlerContext(
            string root, string handlerId, string displayName, string handlerVersion, string release,
            int processId, DateTime processStartUtc, Func<DateTime> utcNow, DialogAllowlist allowlist)
        {
            Root = root;
            HandlerId = handlerId;
            DisplayName = displayName;
            HandlerVersion = handlerVersion;
            Release = release;
            ProcessId = processId;
            ProcessStartUtc = processStartUtc;
            UtcNow = utcNow;
            Allowlist = allowlist;
        }

        public string Root { get; }
        public string HandlerId { get; }
        public string DisplayName { get; }
        public string HandlerVersion { get; }
        /// <summary>The running Revit's release as a four-digit year, for example "2025".</summary>
        public string Release { get; }
        public int ProcessId { get; }
        public DateTime ProcessStartUtc { get; }
        public Func<DateTime> UtcNow { get; }
        public DialogAllowlist Allowlist { get; }

        // Folder layout: see "Folder layout" in the protocol document.
        public string HandlersFolder => Combine(Root, "handlers");
        public string InstancesFolder => Combine(Root, "instances");
        public string RequestFolder => Combine(Combine(Combine(Root, "requests"), Release), HandlerId);
        public string LogsFolder => Combine(Root, "logs");

        public string RegistrationPath => Combine(HandlersFolder, HandlerId + "-" + Release + ".json");
        public string InstancePath => Combine(InstancesFolder, HandlerId + "-" + Release + "-" + ProcessId + ".json");
        public string LogPath => Combine(LogsFolder, HandlerId + "-" + Release + ".log");

        private static string Combine(string a, string b) => System.IO.Path.Combine(a, b);
    }
}
