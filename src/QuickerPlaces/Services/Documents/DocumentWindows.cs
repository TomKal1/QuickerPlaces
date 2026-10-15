using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Runtime.Versioning;

namespace QuickerPlaces.Services.Documents;

/// <summary>
/// Lists the document windows on the desktop: top-level windows whose titles
/// mention a PDF, Word, Excel or other document file, and Word's, Excel's and
/// PowerPoint's main windows whatever their titles say (by window class), with
/// each owning program's description and command line. It reads titles, window
/// classes and process details only, never file handles (sessions plan G6).
///
/// Shared by the on-demand <see cref="WindowsOpenDocumentProbe"/> and, later,
/// the background sampler, so both see the same windows.
///
/// App-only: it calls Windows, so no test runs it.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DocumentWindows
{
    /// <summary>The document windows, and each program that owns one, for WindowsHeldFiles.</summary>
    public static (IReadOnlyList<ViewerWindow> Windows, IReadOnlyList<(uint ProcessId, string AppName)> Programs) Find()
    {
        var found = new List<(string Title, uint ProcessId, DocumentKind? OfficeKind)>();
        var own = (uint)Environment.ProcessId;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            var length = GetWindowTextLength(hwnd);
            if (length <= 4)
                return true;

            var text = new StringBuilder(length + 1);
            if (GetWindowText(hwnd, text, text.Capacity) <= 0)
                return true;

            var title = text.ToString();
            var officeKind = OfficeKindOf(hwnd);
            if (officeKind is null && !DocumentKinds.Extensions.Any(e => title.Contains(e, StringComparison.OrdinalIgnoreCase)))
                return true;

            GetWindowThreadProcessId(hwnd, out var processId);
            if (processId != own)
                found.Add((title, processId, officeKind));
            return true;
        }, IntPtr.Zero);

        var programs = new Dictionary<uint, (string AppName, string? CommandLine)>();
        var windows = new List<ViewerWindow>();
        foreach (var (title, processId, officeKind) in found)
        {
            if (!programs.TryGetValue(processId, out var program))
                programs[processId] = program = DescribeProcess(processId);
            windows.Add(new ViewerWindow(title, program.AppName, program.CommandLine, officeKind));
        }

        return (windows, programs.Select(p => (p.Key, p.Value.AppName)).ToList());
    }

    /// <summary>The program's own description ("Adobe Acrobat"), or its process name, and its command line if it can be read.</summary>
    private static (string AppName, string? CommandLine) DescribeProcess(uint processId)
    {
        var appName = "another program";
        string? commandLine = null;

        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
            return (appName, null);

        try
        {
            var imagePath = new StringBuilder(1024);
            var size = (uint)imagePath.Capacity;
            if (QueryFullProcessImageName(handle, 0, imagePath, ref size))
            {
                var image = imagePath.ToString();
                appName = Path.GetFileNameWithoutExtension(image);
                try
                {
                    var description = FileVersionInfo.GetVersionInfo(image).FileDescription;
                    if (!string.IsNullOrWhiteSpace(description))
                        appName = description.Trim();
                }
                catch (Exception)
                {
                    // The file name will do.
                }
            }

            commandLine = ReadCommandLine(handle);
        }
        finally
        {
            CloseHandle(handle);
        }

        return (appName, commandLine);
    }

    /// <summary>A process's command line (Windows 8.1 and later), or null if it can't be read.</summary>
    private static string? ReadCommandLine(IntPtr process)
    {
        // The first call only asks for the size; it fails with STATUS_INFO_LENGTH_MISMATCH by design.
        _ = NtQueryInformationProcess(process, ProcessCommandLineInformation, IntPtr.Zero, 0, out var needed);
        if (needed <= 0 || needed > 1 << 20)
            return null;

        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (NtQueryInformationProcess(process, ProcessCommandLineInformation, buffer, needed, out _) != 0)
                return null;

            var text = Marshal.PtrToStructure<UnicodeString>(buffer);
            return text.Buffer == IntPtr.Zero ? null : Marshal.PtrToStringUni(text.Buffer, text.Length / 2);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Word, Excel or PowerPoint when <paramref name="hwnd"/> is that program's main window, by its window class, which doesn't change with language or title.</summary>
    private static DocumentKind? OfficeKindOf(IntPtr hwnd)
    {
        var name = new StringBuilder(64);
        if (GetClassName(hwnd, name, name.Capacity) <= 0)
            return null;

        return name.ToString() switch
        {
            "OpusApp" => DocumentKind.Word,
            "XLMAIN" => DocumentKind.Excel,
            "PPTFrameClass" => DocumentKind.PowerPoint,
            _ => null,
        };
    }

    // ---------------------------------------------------------------
    // Interop
    // ---------------------------------------------------------------

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessCommandLineInformation = 60;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint size);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr information, int length, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }
}
