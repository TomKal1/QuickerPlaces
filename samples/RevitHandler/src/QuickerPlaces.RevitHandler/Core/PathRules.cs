using System;
using System.IO;
using System.Text;

namespace QuickerPlaces.RevitHandler.Core
{
    /// <summary>Path rules that behave the same on every platform, so they can be tested anywhere.</summary>
    public static class PathRules
    {
        /// <summary>
        /// True for a drive-letter path (<c>C:\x</c>) or a UNC path (<c>\\server\share</c>).
        /// Where the directory separator is '/', a path starting with '/' counts too, so tests can use scratch folders.
        /// </summary>
        public static bool IsAbsolute(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            if (path.Length >= 3 && IsAsciiLetter(path[0]) && path[1] == ':' && IsSeparator(path[2])) return true;
            if (path.Length >= 3 && path[0] == '\\' && path[1] == '\\' && path[2] != '\\') return true;
            return Path.DirectorySeparatorChar == '/' && path[0] == '/';
        }

        /// <summary>The file name without its extension, splitting on '\' and '/' whatever the platform.</summary>
        public static string GetFileNameWithoutExtension(string path)
        {
            int cut = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            string name = cut >= 0 ? path.Substring(cut + 1) : path;
            int dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        public static bool HasExtension(string path, string extension)
            => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Replaces every character that is not valid in a Windows file name with '_'.
        /// The set is fixed (not <c>Path.GetInvalidFileNameChars</c>) so the result does not depend on the OS running it.
        /// </summary>
        public static string SanitizeFileName(string name)
        {
            const string invalid = "<>:\"/\\|?*";
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(c < ' ' || invalid.IndexOf(c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        private static bool IsSeparator(char c) => c == '\\' || c == '/';
        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }
}
