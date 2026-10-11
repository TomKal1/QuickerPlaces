using System;
using System.Collections.Generic;
using System.IO;
using QuickerPlaces.RevitHandler.Core;

namespace QuickerPlaces.RevitHandler.Tests
{
    /// <summary>A scratch protocol root, deleted on dispose, plus a context with a fixed clock and process.</summary>
    public sealed class Scratch : IDisposable
    {
        public const string HandlerId = "quickerplaces.sample";
        public const string Release = "2025";
        public const string RequestId = "4f1c2a9be0d34c7f8a61b0e5d27c9a13";

        public static readonly DateTime Now = new DateTime(2026, 10, 8, 21, 15, 0, DateTimeKind.Utc);

        public Scratch()
        {
            Folder = Path.Combine(Path.GetTempPath(), "qp-revit-handler-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
        }

        public string Folder { get; }
        public string Root => Path.Combine(Folder, "root");

        public HandlerContext NewContext(int processId = 23816, DialogAllowlist? allowlist = null, Func<DateTime>? now = null)
            => new HandlerContext(Root, HandlerId, "QuickerPlaces sample handler", "1.0.0", Release,
                                  processId, new DateTime(2026, 10, 8, 21, 13, 41, 517, DateTimeKind.Utc),
                                  now ?? (() => Now), allowlist ?? DialogAllowlist.Default);

        /// <summary>Writes a file into the context's request folder and returns its path.</summary>
        public string WriteRequestFile(HandlerContext context, string fileName, string content)
        {
            Directory.CreateDirectory(context.RequestFolder);
            string path = Path.Combine(context.RequestFolder, fileName);
            File.WriteAllText(path, content);
            return path;
        }

        public string MakeFile(string relative, string content = "x")
        {
            string path = Path.Combine(Folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Folder, true); } catch (IOException) { }
        }
    }

    public static class Requests
    {
        /// <summary>The example request from docs/revit-handler-protocol.md, verbatim.</summary>
        public const string SpecExample = """
            {
              "protocol": 1,
              "requestId": "4f1c2a9be0d34c7f8a61b0e5d27c9a13",
              "action": "open-new-local",
              "handlerId": "quickerplaces.sample",
              "revitRelease": "2025",
              "createdUtc": "2026-10-08T21:14:00.0000000Z",
              "expiresUtc": "2026-10-08T21:24:00.0000000Z",
              "centralPath": "\\\\server\\projects\\1234\\1234_Arch_Central.rvt",
              "localFolder": "C:\\REVIT_LOCAL2025",
              "worksets": "lastViewed"
            }
            """;

        /// <summary>A request that points at real scratch paths (so that "exists" checks work on any OS).</summary>
        public static string For(string centralPath, string localFolder, string worksets = "lastViewed",
                                 string expiresUtc = "2026-10-08T21:24:00.0000000Z", string requestId = Scratch.RequestId)
            => "{\"protocol\":1,\"requestId\":\"" + requestId + "\",\"action\":\"open-new-local\",\"handlerId\":\"quickerplaces.sample\","
             + "\"revitRelease\":\"2025\",\"createdUtc\":\"2026-10-08T21:14:00.0000000Z\",\"expiresUtc\":\"" + expiresUtc + "\","
             + "\"centralPath\":" + Quote(centralPath) + ",\"localFolder\":" + Quote(localFolder) + ",\"worksets\":\"" + worksets + "\"}";

        private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\") + "\"";
    }

    /// <summary>A stand-in for Revit that records what it was asked to do.</summary>
    public sealed class FakeRevit : IRevitSession
    {
        public string Username { get; set; } = "tkal";
        public CentralInfo Info { get; set; } = new CentralInfo { IsWorkshared = true, IsCentral = true, IsLocal = false, Format = "2025" };
        public Exception? ReadFails { get; set; }
        public Exception? CreateFails { get; set; }
        public Exception? OpenFails { get; set; }
        /// <summary>Dialogs "raised" by Revit while the local is made and opened: (dialogId, kind, message).</summary>
        public List<(string? Id, string Kind, string? Message)> Dialogs { get; } = new();
        public List<int?> Answers { get; } = new();
        public List<string> Calls { get; } = new();
        public bool CaptureActive { get; private set; }
        public bool CaptureEverStarted { get; private set; }

        private DialogRecorder? _recorder;

        public CentralInfo ReadCentralInfo(string centralPath)
        {
            Calls.Add("read " + centralPath);
            if (ReadFails != null) throw ReadFails;
            return Info;
        }

        public IDisposable BeginDialogCapture(DialogRecorder recorder)
        {
            _recorder = recorder;
            CaptureActive = true;
            CaptureEverStarted = true;
            return new Scope(this);
        }

        public void CreateNewLocal(string centralPath, string localPath)
        {
            Calls.Add("create " + centralPath + " -> " + localPath);
            RaiseDialogs();
            if (CreateFails != null) throw CreateFails;
        }

        public void OpenLocal(string localPath, WorksetsChoice worksets)
        {
            Calls.Add("open " + localPath + " " + worksets);
            if (OpenFails != null) throw OpenFails;
        }

        private void RaiseDialogs()
        {
            foreach (var d in Dialogs) Answers.Add(_recorder!.OnDialog(d.Id, d.Kind, d.Message));
        }

        private sealed class Scope : IDisposable
        {
            private readonly FakeRevit _owner;
            public Scope(FakeRevit owner) => _owner = owner;
            public void Dispose() => _owner.CaptureActive = false;
        }
    }
}
