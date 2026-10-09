using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace QuickerPlaces.RevitHandler.Core
{
    /// <summary>
    /// Reading and writing protocol files: JSON, UTF-8, atomic writes, the 64 KB read limit.
    ///
    /// JSON uses System.Runtime.Serialization.Json (part of .NET Framework 4.8 and .NET 8) on purpose:
    /// a newer System.Text.Json inside Revit 2022 to 2024 can clash with the copy another add-in loaded.
    /// </summary>
    public static class ProtocolFile
    {
        public const int MaxReadBytes = 64 * 1024;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        // ---- JSON ----

        public static string ToJson<T>(T value) where T : class
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return Utf8NoBom.GetString(stream.ToArray());
            }
        }

        /// <summary>Parses a JSON object. Accepts a byte-order mark; ignores properties it does not know. Throws if the content is not a JSON object of the right shape.</summary>
        public static T FromJson<T>(byte[] bytes) where T : class
        {
            int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(bytes, offset, bytes.Length - offset))
            {
                object? parsed = serializer.ReadObject(stream);
                if (parsed is T value) return value;
                throw new InvalidDataException("The file does not contain a JSON object.");
            }
        }

        // ---- Reading ----

        /// <summary>Reads a whole file, refusing one larger than 64 KB.</summary>
        public static byte[] ReadLimited(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length > MaxReadBytes)
                    throw new InvalidDataException("The file is larger than 64 KB.");

                var buffer = new byte[stream.Length];
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = stream.Read(buffer, read, buffer.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                if (read != buffer.Length) Array.Resize(ref buffer, read);
                return buffer;
            }
        }

        // ---- Writing ----

        /// <summary>
        /// Writes <paramref name="content"/> to <c>.&lt;final name&gt;.&lt;32 hex&gt;.tmp</c> in the same folder,
        /// then renames it to <paramref name="path"/>. With <paramref name="replace"/> false an existing target is an error.
        /// A reader never sees a half-written file, and a failed write leaves no temporary file behind.
        /// </summary>
        public static void WriteAtomic(string path, string content, bool replace)
        {
            string folder = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(folder);

            string temp = Path.Combine(folder, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(temp, Utf8NoBom.GetBytes(content));
                try
                {
                    File.Move(temp, path);
                }
                catch (IOException) when (replace && File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        // ---- Times: "O" format, UTC ----

        public static string FormatUtc(DateTime time)
            => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        public static bool TryParseUtc(string? text, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);
        }
    }
}
