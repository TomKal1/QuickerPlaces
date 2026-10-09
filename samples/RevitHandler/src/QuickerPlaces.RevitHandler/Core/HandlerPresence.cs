using System;
using System.Collections.Generic;
using System.IO;

namespace QuickerPlaces.RevitHandler.Core
{
    /// <summary>The registration file (stays) and the instance file (exists while Revit runs).</summary>
    public sealed class HandlerPresence
    {
        private readonly HandlerContext _context;
        private DateTime? _loadedUtc;

        public HandlerPresence(HandlerContext context) => _context = context;

        /// <summary>Writes or replaces <c>handlers\&lt;handlerId&gt;-&lt;release&gt;.json</c>.</summary>
        public void WriteRegistration()
        {
            var file = new RegistrationFile
            {
                Protocol = HandlerContext.ProtocolVersion,
                HandlerId = _context.HandlerId,
                DisplayName = _context.DisplayName,
                HandlerVersion = _context.HandlerVersion,
                RevitRelease = _context.Release,
                Actions = new List<string> { HandlerContext.OpenNewLocalAction },
                WrittenUtc = ProtocolFile.FormatUtc(_context.UtcNow()),
            };
            ProtocolFile.WriteAtomic(_context.RegistrationPath, ProtocolFile.ToJson(file), replace: true);
        }

        /// <summary>Writes the instance file. Without <paramref name="ready"/> it carries no readyUtc ("loaded, Revit still starting").</summary>
        public void WriteInstance(bool ready)
        {
            DateTime now = _context.UtcNow();
            if (_loadedUtc == null) _loadedUtc = now;

            var file = new InstanceFile
            {
                Protocol = HandlerContext.ProtocolVersion,
                HandlerId = _context.HandlerId,
                RevitRelease = _context.Release,
                ProcessId = _context.ProcessId,
                ProcessStartUtc = ProtocolFile.FormatUtc(_context.ProcessStartUtc),
                LoadedUtc = ProtocolFile.FormatUtc(_loadedUtc.Value),
                ReadyUtc = ready ? ProtocolFile.FormatUtc(now) : null,
            };
            ProtocolFile.WriteAtomic(_context.InstancePath, ProtocolFile.ToJson(file), replace: true);
        }

        public void DeleteInstance()
        {
            try { File.Delete(_context.InstancePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
