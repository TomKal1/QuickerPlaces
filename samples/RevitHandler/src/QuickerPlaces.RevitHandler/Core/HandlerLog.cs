using System;
using System.Diagnostics;
using System.IO;

namespace QuickerPlaces.RevitHandler.Core
{
    /// <summary>
    /// A modest text log inside the protocol root (<c>logs\&lt;handlerId&gt;-&lt;release&gt;.log</c>).
    /// It never throws. When it grows past 256 KB it starts over, so it needs no maintenance.
    /// </summary>
    public sealed class HandlerLog
    {
        private const long MaxBytes = 256 * 1024;
        private readonly object _gate = new object();
        private readonly string? _path;
        private readonly Func<DateTime> _utcNow;

        public HandlerLog(string? path, Func<DateTime> utcNow)
        {
            _path = path;
            _utcNow = utcNow;
        }

        public void Write(string message)
        {
            string line = ProtocolFile.FormatUtc(_utcNow()) + " " + message;
            Debug.WriteLine("[QuickerPlaces handler] " + line);
            if (_path == null) return;

            try
            {
                lock (_gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes) File.Delete(_path);
                    File.AppendAllText(_path, line + Environment.NewLine);
                }
            }
            catch (Exception)
            {
                // Logging must never disturb Revit.
            }
        }
    }
}
