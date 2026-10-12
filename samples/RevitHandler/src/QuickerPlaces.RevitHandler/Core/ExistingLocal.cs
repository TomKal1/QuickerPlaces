using System;
using System.Globalization;
using System.IO;

namespace QuickerPlaces.RevitHandler.Core
{
    /// <summary>The request's <c>existingLocal</c>: what step 4 does with an old local at the new local's path.</summary>
    public enum ExistingLocalMode
    {
        /// <summary>Property missing or null: keep the old local and date the new one (the original version-1 rule).</summary>
        Keep,

        /// <summary><c>"overwrite"</c>: delete the old local and its _backup folder (Revit's "Overwrite existing file").</summary>
        Overwrite,

        /// <summary><c>"appendTimestamp"</c>: rename both with the old local's date (Revit's "Append timestamp to existing file name").</summary>
        AppendTimestamp,
    }

    /// <summary>The wire values of <c>existingLocal</c> and <c>previousLocal</c>.</summary>
    public static class ExistingLocalValues
    {
        public const string Overwrite = "overwrite";
        public const string AppendTimestamp = "appendTimestamp";
        public const string Overwritten = "overwritten";
        public const string Renamed = "renamed";
    }

    /// <summary>Where the new local goes, and what happened to an old one (for the result's previousLocal and previousLocalPath).</summary>
    public sealed class PreparedLocal
    {
        public PreparedLocal(string localPath, string? previousLocal = null, string? previousLocalPath = null)
        {
            LocalPath = localPath;
            PreviousLocal = previousLocal;
            PreviousLocalPath = previousLocalPath;
        }

        public string LocalPath { get; }
        public string? PreviousLocal { get; }
        public string? PreviousLocalPath { get; }
    }

    /// <summary>The old local (or its backup folder) is open in Revit or locked; nothing was changed. Becomes <c>localInUse</c>.</summary>
    public sealed class LocalInUseException : Exception
    {
        public LocalInUseException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Step 4 of "Handling a request" when the local's own path is taken. The only code in this handler that
    /// deletes or renames anything outside the protocol folder (safety rule 2's exception), and only the old local at
    /// the new local's own path and its <c>_backup</c> folder, as Revit's own Create New Local does. Every change is
    /// undone when a later part fails, so a refusal leaves both as they were. No Revit types.
    /// </summary>
    public static class ExistingLocalPreparer
    {
        /// <param name="localNow">Local time now, for <see cref="ExistingLocalMode.Keep"/>'s dated new local.</param>
        /// <param name="isOpenInRevit">True when a document open in this Revit has the path.</param>
        public static PreparedLocal Prepare(string localFolder, string centralPath, string username, ExistingLocalMode mode,
            DateTime localNow, Func<string, bool> isOpenInRevit)
        {
            string local = LocalPathChooser.PlainPath(localFolder, centralPath, username);
            if (!File.Exists(local))
                return new PreparedLocal(mode == ExistingLocalMode.Keep
                    ? LocalPathChooser.Choose(localFolder, centralPath, username, localNow, p => File.Exists(p) || Directory.Exists(p))
                    : local);

            if (mode == ExistingLocalMode.Keep)
                return new PreparedLocal(LocalPathChooser.Choose(localFolder, centralPath, username, localNow, p => File.Exists(p) || Directory.Exists(p)));

            string name = Path.GetFileName(local);
            if (isOpenInRevit(local))
                throw new LocalInUseException(name + " is open in this Revit. Close it, then try again.");

            string backup = BackupFolder(local);
            return mode == ExistingLocalMode.Overwrite ? Overwrite(local, backup, name) : AppendTimestamp(local, backup, name);
        }

        /// <summary>Revit's name for a model's backup folder: the path without <c>.rvt</c>, plus <c>_backup</c>.</summary>
        public static string BackupFolder(string modelPath)
            => Path.Combine(Path.GetDirectoryName(modelPath) ?? "", Path.GetFileNameWithoutExtension(modelPath) + "_backup");

        private static PreparedLocal Overwrite(string local, string backup, string name)
        {
            // Move both aside first: a rename fails cleanly when another Revit has a file open, and is undone if the other fails.
            string folder = Path.GetDirectoryName(local) ?? "";
            string tag = Guid.NewGuid().ToString("N");
            string localAside = Path.Combine(folder, "." + name + "." + tag + ".deleting");
            string backupAside = Path.Combine(folder, "." + Path.GetFileName(backup) + "." + tag + ".deleting");

            Rename(() => File.Move(local, localAside), name);
            if (Directory.Exists(backup))
            {
                try
                {
                    Directory.Move(backup, backupAside);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    File.Move(localAside, local);
                    throw new LocalInUseException("The backup folder of " + name + " is in use, so the local wasn't replaced. Close it in Revit, then try again.", ex);
                }
            }

            // Both are out of the way; deleting them may still leave a stray hidden item, which never blocks the new local.
            TryDelete(() => File.Delete(localAside));
            if (Directory.Exists(backupAside)) TryDelete(() => Directory.Delete(backupAside, true));
            return new PreparedLocal(local, ExistingLocalValues.Overwritten);
        }

        private static PreparedLocal AppendTimestamp(string local, string backup, string name)
        {
            string folder = Path.GetDirectoryName(local) ?? "";
            string stamped = Path.GetFileNameWithoutExtension(local) + "_"
                + File.GetLastWriteTime(local).ToString("ddMMMyyyy_HHmmss", CultureInfo.InvariantCulture);

            string target = Path.Combine(folder, stamped + ".rvt");
            for (int n = 2; File.Exists(target) || Directory.Exists(BackupFolder(target)); n++)
            {
                if (n >= 10000) throw new IOException("No free name was found for the old local in " + folder + ".");
                target = Path.Combine(folder, stamped + "-" + n.ToString(CultureInfo.InvariantCulture) + ".rvt");
            }

            Rename(() => File.Move(local, target), name);
            if (Directory.Exists(backup))
            {
                try
                {
                    Directory.Move(backup, BackupFolder(target));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    File.Move(target, local);
                    throw new LocalInUseException("The backup folder of " + name + " is in use, so the old local wasn't set aside. Close it in Revit, then try again.", ex);
                }
            }

            return new PreparedLocal(local, ExistingLocalValues.Renamed, target);
        }

        private static void Rename(Action rename, string name)
        {
            try
            {
                rename();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new LocalInUseException(name + " is open in Revit or locked, so it wasn't replaced. Close it, then try again.", ex);
            }
        }

        private static void TryDelete(Action delete)
        {
            try { delete(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }
    }
}
