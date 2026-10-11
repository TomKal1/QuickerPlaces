using System;
using Autodesk.Revit.UI;
using QuickerPlaces.RevitHandler.Core;

namespace QuickerPlaces.RevitHandler
{
    /// <summary>
    /// Runs in Revit's API context when <see cref="ExternalEvent.Raise"/> has been called.
    /// Handles every waiting request; with none waiting it does nothing.
    /// </summary>
    internal sealed class OpenNewLocalHandler : IExternalEventHandler
    {
        private readonly HandlerContext _context;
        private readonly RequestQueue _queue;
        private readonly HandlerLog _log;

        public OpenNewLocalHandler(HandlerContext context, RequestQueue queue, HandlerLog log)
        {
            _context = context;
            _queue = queue;
            _log = log;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var processor = new RequestProcessor(_context, _queue, new RevitSession(app, _log), _log);
                processor.ProcessWaiting();
            }
            catch (Exception ex)
            {
                // Nothing may escape into Revit.
                _log.Write("Execute failed: " + ex);
            }
        }

        public string GetName() => _context.DisplayName;
    }
}
