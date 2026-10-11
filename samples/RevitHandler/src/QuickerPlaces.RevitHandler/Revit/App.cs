using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using QuickerPlaces.RevitHandler.Core;

namespace QuickerPlaces.RevitHandler
{
    /// <summary>
    /// The add-in. It announces itself with files, then waits for QuickerPlaces' requests.
    ///
    /// To make this your own handler, change <see cref="HandlerId"/> and <see cref="DisplayName"/>
    /// (and the AddInId in the .addin file).
    ///
    /// Threads: the file watcher and the timer only call <see cref="ExternalEvent.Raise"/>. All work happens in
    /// <see cref="OpenNewLocalHandler.Execute"/>, on Revit's thread. <see cref="OnStartup"/> only writes two small files.
    /// </summary>
    public sealed class App : IExternalApplication
    {
        public const string HandlerId = "quickerplaces.sample";
        public const string DisplayName = "QuickerPlaces sample handler";

        private const string RequestHintVariable = "QUICKERPLACES_REVIT_REQUEST";

        private UIControlledApplication? _uiApp;
        private HandlerContext? _context;
        private HandlerPresence? _presence;
        private RequestQueue? _queue;
        private HandlerLog? _log;
        private ExternalEvent? _event;
        private FileSystemWatcher? _watcher;
        private Timer? _timer;
        private volatile bool _stopped;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                _uiApp = application;
                string release = application.ControlledApplication.VersionNumber;

                Process process = Process.GetCurrentProcess();
                string version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
                _context = new HandlerContext(
                    ProtocolRoot.Resolve(), HandlerId, DisplayName, version, release,
                    process.Id, process.StartTime.ToUniversalTime(), () => DateTime.UtcNow, DialogAllowlist.Default);
                _log = new HandlerLog(_context.LogPath, _context.UtcNow);
                _queue = new RequestQueue(_context);
                _presence = new HandlerPresence(_context);

                // Registration and instance file (without readyUtc: loaded, Revit still starting).
                _presence.WriteRegistration();
                _presence.WriteInstance(ready: false);

                _event = ExternalEvent.Create(new OpenNewLocalHandler(_context, _queue, _log));
                application.ControlledApplication.ApplicationInitialized += OnApplicationInitialized;

                _log.Write("Loaded in Revit " + release + " (process " + process.Id + "), protocol root " + _context.Root);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                _log?.Write("OnStartup failed: " + ex);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            _stopped = true;
            try
            {
                application.ControlledApplication.ApplicationInitialized -= OnApplicationInitialized;
                application.Idling -= OnFirstIdling;
            }
            catch (Exception) { }

            try { _timer?.Dispose(); } catch (Exception) { }
            try { _watcher?.Dispose(); } catch (Exception) { }
            try { _presence?.DeleteInstance(); } catch (Exception) { }
            try { _event?.Dispose(); } catch (Exception) { }

            _log?.Write("Unloaded.");
            return Result.Succeeded;
        }

        private void OnApplicationInitialized(object? sender, ApplicationInitializedEventArgs e)
        {
            // Revit has finished starting. "Ready" is the first Idling after this.
            _uiApp!.ControlledApplication.ApplicationInitialized -= OnApplicationInitialized;
            _uiApp.Idling += OnFirstIdling;
        }

        private void OnFirstIdling(object? sender, IdlingEventArgs e)
        {
            _uiApp!.Idling -= OnFirstIdling; // once only: Idling also makes Revit call back more often while subscribed

            try
            {
                _presence!.WriteInstance(ready: true);
                Directory.CreateDirectory(_queue!.Folder);

                // Launched by QuickerPlaces: the hint names the request that started this Revit. It is only a hint;
                // the request is handled from the folder like any other.
                string? hint = Environment.GetEnvironmentVariable(RequestHintVariable);
                if (!string.IsNullOrEmpty(hint) && !(RequestQueue.IsRequestId(hint!) && File.Exists(_queue.WaitingPath(hint!))))
                    _log!.Write("Hinted request " + hint + " is not in " + _queue.Folder + "; nothing else to do for it.");

                StartWatching();
                RaiseEvent(); // cold start: handles whatever is already waiting
                _log!.Write("Ready.");
            }
            catch (Exception ex)
            {
                _log?.Write("Becoming ready failed: " + ex);
            }
        }

        private void StartWatching()
        {
            _watcher = new FileSystemWatcher(_queue!.Folder)
            {
                Filter = "*.json",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
            };
            _watcher.Created += (s, e) => OnRequestFileEvent(e.Name);
            _watcher.Renamed += (s, e) => OnRequestFileEvent(e.Name); // a request is written as a temp file, then renamed
            _watcher.EnableRaisingEvents = true;

            _timer = new Timer(_ => OnTimer(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }

        // Watcher thread: raise, nothing else. (Results and temporary files are not requests.)
        private void OnRequestFileEvent(string? name)
        {
            if (name == null || name.StartsWith(".") || name.EndsWith(".result.json")) return;
            RaiseEvent();
        }

        // Timer thread: the backup for missed watcher events. A directory listing and a Raise; no Revit API.
        private void OnTimer()
        {
            try
            {
                if (!_stopped && _queue!.HasWaiting()) RaiseEvent();
            }
            catch (Exception) { }
        }

        private void RaiseEvent()
        {
            try { if (!_stopped) _event?.Raise(); }
            catch (Exception) { } // disposed during shutdown
        }
    }
}
