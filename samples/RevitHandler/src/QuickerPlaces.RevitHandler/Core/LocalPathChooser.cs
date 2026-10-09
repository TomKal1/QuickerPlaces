using System;
using System.Globalization;
using System.IO;

namespace QuickerPlaces.RevitHandler.Core
{
    /// <summary>Step 4: choose the path of the new local. Never returns a path that exists.</summary>
    public static class LocalPathChooser
    {
        /// <summary>
        /// <c>&lt;localFolder&gt;\&lt;central name&gt;_&lt;username&gt;.rvt</c>; invalid file-name characters become '_'.
        /// If that exists: <c>..._&lt;yyyyMMdd-HHmmss&gt;.rvt</c> (<paramref name="localNow"/>), then <c>...-2.rvt</c>, <c>-3</c>, and so on.
        /// </summary>
        public static string Choose(string localFolder, string centralPath, string username, DateTime localNow, Func<string, bool> exists)
        {
            string baseName = PathRules.SanitizeFileName(PathRules.GetFileNameWithoutExtension(centralPath) + "_" + username);

            string candidate = Path.Combine(localFolder, baseName + ".rvt");
            if (!exists(candidate)) return candidate;

            string stamped = baseName + "_" + localNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            candidate = Path.Combine(localFolder, stamped + ".rvt");
            if (!exists(candidate)) return candidate;

            for (int n = 2; n < 10000; n++)
            {
                candidate = Path.Combine(localFolder, stamped + "-" + n.ToString(CultureInfo.InvariantCulture) + ".rvt");
                if (!exists(candidate)) return candidate;
            }
            throw new IOException("No free local file name was found in " + localFolder + ".");
        }
    }
}
