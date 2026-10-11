using System;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using QuickerPlaces.RevitHandler.Core;

namespace QuickerPlaces.RevitHandler
{
    /// <summary>
    /// The Revit calls of the protocol, and nothing else. Created inside <c>Execute</c> and used on Revit's main thread.
    /// Every member used here exists in Revit 2022 to 2026.
    /// </summary>
    internal sealed class RevitSession : IRevitSession
    {
        private readonly UIApplication _uiApp;
        private readonly HandlerLog _log;

        public RevitSession(UIApplication uiApp, HandlerLog log)
        {
            _uiApp = uiApp;
            _log = log;
        }

        public string Username => _uiApp.Application.Username;

        public CentralInfo ReadCentralInfo(string centralPath)
        {
            BasicFileInfo info;
            try
            {
                info = BasicFileInfo.Extract(centralPath);
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                throw new CentralNotFoundException("The central model does not exist or cannot be reached: " + centralPath, ex);
            }

            return new CentralInfo
            {
                IsWorkshared = info.IsWorkshared,
                IsLocal = info.IsLocal,
                IsCentral = info.IsCentral,
                Format = info.Format,
            };
        }

        public IDisposable BeginDialogCapture(DialogRecorder recorder) => new DialogScope(_uiApp, recorder, _log);

        public void CreateNewLocal(string centralPath, string localPath)
        {
            // The only way this handler makes a local. It never copies the file.
            WorksharingUtils.CreateNewLocal(
                ModelPathUtils.ConvertUserVisiblePathToModelPath(centralPath),
                ModelPathUtils.ConvertUserVisiblePathToModelPath(localPath));
        }

        public void OpenLocal(string localPath, WorksetsChoice worksets)
        {
            var options = new OpenOptions();
            options.SetOpenWorksetsConfiguration(new WorksetConfiguration(ToOption(worksets)));

            // detachAndPrompt: false. The local is opened as it is; nothing is detached, upgraded, saved or synchronised.
            _uiApp.OpenAndActivateDocument(ModelPathUtils.ConvertUserVisiblePathToModelPath(localPath), options, false);
        }

        private static WorksetConfigurationOption ToOption(WorksetsChoice worksets)
        {
            switch (worksets)
            {
                case WorksetsChoice.All: return WorksetConfigurationOption.OpenAllWorksets;
                case WorksetsChoice.None: return WorksetConfigurationOption.CloseAllWorksets;
                default: return WorksetConfigurationOption.OpenLastViewed;
            }
        }

        /// <summary>
        /// <c>DialogBoxShowing</c> is subscribed only between construction and disposal, which is step 5 of one request.
        /// A dialog is answered only when <see cref="DialogRecorder"/> says its DialogId is on the allowlist.
        /// </summary>
        private sealed class DialogScope : IDisposable
        {
            private readonly UIApplication _uiApp;
            private readonly DialogRecorder _recorder;
            private readonly HandlerLog _log;

            public DialogScope(UIApplication uiApp, DialogRecorder recorder, HandlerLog log)
            {
                _uiApp = uiApp;
                _recorder = recorder;
                _log = log;
                _uiApp.DialogBoxShowing += OnDialogBoxShowing;
            }

            public void Dispose() => _uiApp.DialogBoxShowing -= OnDialogBoxShowing;

            private void OnDialogBoxShowing(object? sender, DialogBoxShowingEventArgs e)
            {
                try
                {
                    string kind;
                    string? message = null;
                    if (e is TaskDialogShowingEventArgs task) { kind = DialogKinds.TaskDialog; message = task.Message; }
                    else if (e is MessageBoxShowingEventArgs box) { kind = DialogKinds.MessageBox; message = box.Message; }
                    else kind = DialogKinds.DialogBox;

                    string? dialogId = e.DialogId;
                    int? answer = _recorder.OnDialog(dialogId, kind, message);
                    _log.Write("Dialog " + kind + " " + (dialogId ?? "(no id)") + (answer.HasValue ? " answered " + answer.Value : " left to the user"));

                    if (answer.HasValue) e.OverrideResult(answer.Value);
                }
                catch (Exception ex)
                {
                    _log.Write("Dialog handling failed: " + ex.Message); // leave the dialog to Revit
                }
            }
        }
    }
}
