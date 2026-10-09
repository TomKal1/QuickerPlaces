using System;

namespace QuickerPlaces.RevitHandler.Core
{
    /// <summary>What <c>BasicFileInfo</c> says about a file.</summary>
    public sealed class CentralInfo
    {
        public bool IsWorkshared { get; set; }
        public bool IsLocal { get; set; }
        public bool IsCentral { get; set; }
        /// <summary>The release the file was saved in (<c>BasicFileInfo.Format</c>), for example "2025".</summary>
        public string? Format { get; set; }
    }

    /// <summary>Thrown by <see cref="IRevitSession.ReadCentralInfo"/> when the file is not there.</summary>
    public sealed class CentralNotFoundException : Exception
    {
        public CentralNotFoundException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// The few things that need the Revit API, so the rest of the request handling can run (and be tested) without it.
    /// The Revit layer implements it; every member is called from <c>IExternalEventHandler.Execute</c>.
    /// </summary>
    public interface IRevitSession
    {
        /// <summary><c>Application.Username</c>.</summary>
        string Username { get; }

        /// <summary><c>BasicFileInfo.Extract</c>. Throws <see cref="CentralNotFoundException"/> when the file does not exist.</summary>
        CentralInfo ReadCentralInfo(string centralPath);

        /// <summary>Subscribes <c>DialogBoxShowing</c> until the returned object is disposed.</summary>
        IDisposable BeginDialogCapture(DialogRecorder recorder);

        /// <summary><c>WorksharingUtils.CreateNewLocal</c>.</summary>
        void CreateNewLocal(string centralPath, string localPath);

        /// <summary><c>UIApplication.OpenAndActivateDocument</c> with the requested worksets.</summary>
        void OpenLocal(string localPath, WorksetsChoice worksets);
    }
}
