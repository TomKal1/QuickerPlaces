using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using OpenMcdf;

namespace QuickerPlaces.Services.Revit;

/// <summary>Whether a Revit file is workshared, and if so whether it is the central model or someone's local copy.</summary>
public enum RevitWorksharing
{
    NotWorkshared,
    Central,
    Local,

    /// <summary>Workshared, but in a state this reader doesn't recognise (Revit's "in progress").</summary>
    Unknown,
}

/// <summary>Why a file's release couldn't be read; <see cref="None"/> when it was.</summary>
public enum RevitFileProblem
{
    None,
    NotFound,

    /// <summary>Another program has the file locked against reading.</summary>
    InUse,

    /// <summary>Access was refused, or the disk or network failed mid-read.</summary>
    Unreadable,

    /// <summary>The read took too long (a slow or offline network share); <see cref="RevitReleaseCache"/> gave up waiting.</summary>
    TimedOut,

    /// <summary>Not an OLE compound file, or one without a BasicFileInfo stream.</summary>
    NotRevitFile,

    /// <summary>The BasicFileInfo stream isn't laid out as expected (corrupt, truncated, or a layout from the future).</summary>
    UnrecognisedLayout,

    /// <summary>The stream was read but its format year is missing or unreadable.</summary>
    ReleaseNotRecorded,

    /// <summary>
    /// Saved before Revit 2022, which QuickerPlaces doesn't support.
    /// <see cref="RevitFileInfo.Release"/> still says which release when the
    /// file records it (2019 to 2021); older layouts don't, and give null.
    /// </summary>
    TooOld,
}

/// <summary>
/// What a Revit file says about itself. <see cref="Release"/> is null when
/// it can't be known, and an unknown release means ask the user, never "use
/// the newest Revit".
/// </summary>
public sealed record RevitFileInfo(
    int? Release,
    string? Build,
    RevitWorksharing Worksharing,
    string? CentralModelPath,
    string? LastSavePath,
    int? LayoutVersion,
    RevitFileProblem Problem)
{
    public static RevitFileInfo Failed(RevitFileProblem problem) =>
        new(null, null, RevitWorksharing.Unknown, null, null, null, problem);
}

/// <summary>
/// Reads the release a Revit file (.rvt, .rfa, .rte) was saved in, and its
/// worksharing state, without Revit (roadmap §4.21, "Reading the saved
/// release"). QuickerPlaces needs the release before it can choose which
/// Revit to launch, so this can't be left to an add-in.
///
/// A Revit file is an OLE compound file; this opens it read-only, sharing
/// with Revit, and reads only the small <c>BasicFileInfo</c> stream. The one
/// exception to "QuickerPlaces never opens a document to look inside it".
///
/// The stream starts:
/// <code>
/// Int32   layout version (14 in Revit 2025; not the release)
/// Byte    is workshared
/// Byte    worksharing type: 0 central, 1 local, 2 in progress, 3 local just created
/// String  username
/// String  central model path
/// String  format: the release, "2025" (layout 12 and later, Revit 2019 on)
/// String  build
/// String  last save path
/// </code>
/// where each String is an Int32 count of UTF-16 characters followed by
/// those characters, and a count of 0 or less means none. Reading stops
/// there; Revit writes more fields after these. The layout is as documented
/// by dosymep.Revit.FileInfo (MIT) and was checked against Revit 2025 files
/// on 2026-10-08.
///
/// Only Revit 2022 and later are supported (the user's decision,
/// 2026-10-08). Anything older is <see cref="RevitFileProblem.TooOld"/>,
/// and a layout before 12 isn't parsed past its version.
/// </summary>
public static class RevitFileInfoReader
{
    public const string StreamName = "BasicFileInfo";

    /// <summary>Real streams are about 2.4 KB; anything past this isn't one.</summary>
    public const int MaxStreamBytes = 64 * 1024;

    /// <summary>The oldest release QuickerPlaces supports.</summary>
    public const int OldestSupportedRelease = 2022;

    /// <summary>Where the format-year field begins (Revit 2019); every supported file has it.</summary>
    private const int FormatLayoutVersion = 12;

    /// <summary>Layouts grow by one when Revit adds a field; this bounds what's plausible.</summary>
    private const int MaxLayoutVersion = 255;

    /// <summary>A Revit path or build string is never near this long; a larger count means the bytes aren't what we think.</summary>
    private const int MaxStringChars = 32 * 1024;

    public static RevitFileInfo Read(string path)
    {
        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return RevitFileInfo.Failed(RevitFileProblem.NotFound);
        }
        catch (IOException ex) when (IsSharingViolation(ex))
        {
            return RevitFileInfo.Failed(RevitFileProblem.InUse);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return RevitFileInfo.Failed(RevitFileProblem.Unreadable);
        }

        using (file)
            return Read(file);
    }

    /// <summary>Reads from an open compound file. The stream is left open.</summary>
    public static RevitFileInfo Read(Stream compoundFile)
    {
        byte[] bytes;
        try
        {
            using var root = RootStorage.Open(compoundFile, StorageModeFlags.LeaveOpen);
            if (!root.TryOpenStream(StreamName, out var stream))
                return RevitFileInfo.Failed(RevitFileProblem.NotRevitFile);

            using (stream)
            {
                if (stream.Length > MaxStreamBytes)
                    return RevitFileInfo.Failed(RevitFileProblem.UnrecognisedLayout);
                bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or EndOfStreamException or ArgumentException)
        {
            // OpenMcdf's complaint that this isn't a compound file, or a damaged one.
            return RevitFileInfo.Failed(RevitFileProblem.NotRevitFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return RevitFileInfo.Failed(RevitFileProblem.Unreadable);
        }

        return Parse(bytes);
    }

    /// <summary>Parses the start of a BasicFileInfo stream, laid out as the class summary describes.</summary>
    public static RevitFileInfo Parse(ReadOnlySpan<byte> bytes)
    {
        var reader = new FieldReader(bytes);
        if (!reader.TryInt32(out var layout) || layout < 1 || layout > MaxLayoutVersion)
            return RevitFileInfo.Failed(RevitFileProblem.UnrecognisedLayout);
        if (layout < FormatLayoutVersion)
            return RevitFileInfo.Failed(RevitFileProblem.TooOld) with { LayoutVersion = layout };

        if (!reader.TryByte(out var workshared) || !reader.TryByte(out var type)
            || !reader.TryString(out _) || !reader.TryString(out var centralPath)
            || !reader.TryString(out var format) || !reader.TryString(out var build))
            return RevitFileInfo.Failed(RevitFileProblem.UnrecognisedLayout);

        // Optional to this reader: a stream that ends here still gave the release.
        reader.TryString(out var lastSavePath);

        var release = ReleaseFromFormat(format);
        var worksharing = workshared == 0 ? RevitWorksharing.NotWorkshared : type switch
        {
            0 => RevitWorksharing.Central,
            1 or 3 => RevitWorksharing.Local,
            _ => RevitWorksharing.Unknown,
        };

        return new RevitFileInfo(
            release,
            build,
            worksharing,
            worksharing == RevitWorksharing.NotWorkshared ? null : centralPath,
            lastSavePath,
            layout,
            release is null ? RevitFileProblem.ReleaseNotRecorded
                : release < OldestSupportedRelease ? RevitFileProblem.TooOld
                : RevitFileProblem.None);
    }

    /// <summary>"2025" → 2025. Nothing else is accepted: the format field holds exactly the year.</summary>
    private static int? ReleaseFromFormat(string? format) =>
        format is { Length: 4 } && int.TryParse(format, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var year) && year is >= 2000 and <= 2099
            ? year
            : null;

    private static bool IsSharingViolation(IOException ex)
    {
        const int sharingViolation = 32, lockViolation = 33;
        var code = ex.HResult & 0xFFFF;
        return code is sharingViolation or lockViolation;
    }

    /// <summary>Reads the stream's little-endian fields in order, refusing anything that would run past its end.</summary>
    private ref struct FieldReader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _position;

        public FieldReader(ReadOnlySpan<byte> bytes)
        {
            _bytes = bytes;
            _position = 0;
        }

        public bool TryInt32(out int value)
        {
            if (_bytes.Length - _position < 4)
            {
                value = 0;
                return false;
            }
            value = BinaryPrimitives.ReadInt32LittleEndian(_bytes[_position..]);
            _position += 4;
            return true;
        }

        public bool TryByte(out byte value)
        {
            if (_position >= _bytes.Length)
            {
                value = 0;
                return false;
            }
            value = _bytes[_position++];
            return true;
        }

        public bool TryString(out string? value)
        {
            value = null;
            var start = _position;
            if (!TryInt32(out var chars))
                return false;
            if (chars <= 0)
                return true;
            if (chars > MaxStringChars || (long)chars * 2 > _bytes.Length - _position)
            {
                _position = start;
                return false;
            }
            value = Encoding.Unicode.GetString(_bytes.Slice(_position, chars * 2));
            _position += chars * 2;
            return true;
        }
    }
}
