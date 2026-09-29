# Held Files Detection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status (2026-09-28):** Tasks 1–5 are implemented on `ccr-8d834d76-kqbdun`, and each was code-reviewed. The review changes go beyond the code blocks below, and the commits are the record:
- Excluded folders are also matched in their mapped-drive spelling.
- `WindowsHeldFiles` has a watchdog for stuck handles, uses `FILE_NAME_OPENED`, and skips Explorer and browsers.
- The resolver spells every clue with the mapped drive (`OpenDocumentEvidence.MappedDrives`, `OpenDocumentResolver.Spell`), so one file is listed once.
- The scan keeps slow-share detection for drive-letter paths.

Task 3 passed on the user's machine: a helper process holding test PDFs open for read and write was found in 139 ms, and Revu with several local tabs was found in about 125 ms, without elevation. Mapped drives, DFS and Studio Sessions are untested because the user's personal PC has none.

**Goal:** Make **Sessions → Save open files…** find every PDF (and Word and Excel document) that a program has open, with its full path, by asking the program which files it holds instead of relying on Windows' Recent Items.

**Architecture:** A new Windows-only class, `WindowsHeldFiles`, lists the file handles held by the processes that own document windows. It uses `NtQuerySystemInformation(SystemExtendedHandleInformation)`, `DuplicateHandle`, `GetFileType` and `GetFinalPathNameByHandle`, which is how Process Explorer and `handle.exe` work. A pure helper, `HeldFilePaths`, turns those raw paths into session paths: it removes the `\\?\` prefix, puts back the mapped drive letter, and drops program and cache folders, which leaves out Revu's Studio cache. `OpenDocumentResolver` gets them as a new clue, `HeldFiles`: every held file counts as open, and title names are matched to held paths first. The review dialog doesn't change.

**Tech Stack:** C# / .NET 10, WPF app, P/Invoke (ntdll, kernel32, mpr), xUnit. Tests run on any OS through linked source files (`src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj`).

**Why:** Restart Manager, which the probe already uses, can only answer "is *this* path in use?", so it needs paths to ask about, and those come almost entirely from Recent Items. Revu tabs opened from Revu's own Open dialog, files opened more than 14 days ago, or machines where Recent Items is off all end up in "couldn't match". Revu keeps every open PDF locked, so its handles name every tab.

**Decisions (agreed 2026-09-28):**
- **H1: Held files count as open.** They start ticked, with the reason "Open in <program>".
- **H2: Studio Session documents are left out.** So is anything else under `%AppData%`, `%LocalAppData%`, `%TEMP%`, `%ProgramData%`, Program Files or Windows. Revu keeps its Studio copies under `%LocalAppData%\Bluebeam`, and Excel keeps `PERSONAL.XLSB` under `%AppData%`.
- **H3: Mapped drives are spelled with their letter.** `GetFinalPathNameByHandle` returns `\\?\UNC\server\share\…`. If one of the user's mapped drives points at that share, the path becomes `P:\…`, and the longest mapped share wins.
- **H4: The handle pass has its own 3-second limit** inside the scan's existing 10 seconds, on its own background thread. A stall returns what was found so far, with a warning line.
- **H5: Counts only in logs, never a path** (Phase 3 D26). The developer probe prints paths to the console only.

**Branch:** keep working on `ccr-8d834d76-kqbdun`, which holds the unmerged sessions work this builds on. Baseline: `dotnet test src/QuickerPlaces.Tests` gives **745 passed** (checked 2026-09-28 on Windows).

---

## File map

| File | Change | Responsibility |
|---|---|---|
| `src/QuickerPlaces/Services/Documents/HeldFilePaths.cs` | Create | Pure: raw handle paths → session paths, plus the `HeldFile` record |
| `src/QuickerPlaces/Services/Documents/WindowsHeldFiles.cs` | Create | Windows-only: list the files that given processes hold open |
| `src/QuickerPlaces/Services/Documents/OpenDocumentResolver.cs` | Modify | `OpenDocumentEvidence.HeldFiles`; held files in `CandidatePaths` and `Resolve` |
| `src/QuickerPlaces/Services/Documents/WindowsOpenDocumentProbe.cs` | Modify | Collect the windows' process IDs, call `WindowsHeldFiles`, pass held files on, skip re-checking them |
| `src/QuickerPlaces.Tests/HeldFilePathsTests.cs` | Create | Tests for `HeldFilePaths` |
| `src/QuickerPlaces.Tests/OpenDocumentResolverTests.cs` | Modify | Tests for held files in the resolver |
| `src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj` | Modify | Link `HeldFilePaths.cs` |
| `src/QuickerPlaces.ActivityProbe/Program.cs`, `.csproj` | Modify | Menu option **4 Held files**, for checking on a real machine |
| `ai/260928_PDF Project Sessions Plan.md` | Modify | §4 clue table and §9 manual checklist |

---

### Task 1: `HeldFilePaths`: raw handle paths to session paths

**Files:**
- Create: `src/QuickerPlaces/Services/Documents/HeldFilePaths.cs`
- Create: `src/QuickerPlaces.Tests/HeldFilePathsTests.cs`
- Modify: `src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj` (the sessions `<Compile Include>` block)

- [ ] **Step 1: Link the new file into the test project**

In `src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj`, directly after the `DocumentPaths.cs` line, add:

```xml
    <Compile Include="..\QuickerPlaces\Services\Documents\HeldFilePaths.cs" Link="Linked\Services\Documents\HeldFilePaths.cs" />
```

- [ ] **Step 2: Write the failing tests**

Create `src/QuickerPlaces.Tests/HeldFilePathsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Services.Documents;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// Paths of files a program holds open, as GetFinalPathNameByHandle spells
/// them, turned into session paths (held-files plan H2, H3).
/// </summary>
public sealed class HeldFilePathsTests
{
    private static readonly IReadOnlyDictionary<string, string> NoDrives = new Dictionary<string, string>();

    private static readonly Dictionary<string, string> Drives = new(StringComparer.OrdinalIgnoreCase)
    {
        ["P:"] = @"\\files\projects",
        ["T:"] = @"\\files\projects\Tower B\",
        ["Z:"] = @"\\other\share",
    };

    private static readonly string[] AppFolders =
    {
        @"C:\Users\Tom\AppData\Local",
        @"C:\Users\Tom\AppData\Roaming\",
        @"C:\Program Files",
    };

    private static string[] Paths(IEnumerable<string> finalPaths, IReadOnlyDictionary<string, string>? drives = null, string[]? folders = null)
        => HeldFilePaths.Resolve(finalPaths.Select(p => (p, "Bluebeam Revu")), drives ?? NoDrives, folders ?? Array.Empty<string>())
            .Select(f => f.Path)
            .ToArray();

    [Theory]
    [InlineData(@"\\?\C:\Jobs\A-101.pdf", @"C:\Jobs\A-101.pdf")]
    [InlineData(@"\\?\UNC\files\projects\Spec.pdf", @"\\files\projects\Spec.pdf")]
    [InlineData(@"\\?\unc\files\projects\Spec.pdf", @"\\files\projects\Spec.pdf")]
    [InlineData(@"C:\Jobs\A-101.pdf", @"C:\Jobs\A-101.pdf")]
    public void FromFinalPath_RemovesTheLongPathPrefix(string finalPath, string expected)
        => Assert.Equal(expected, HeldFilePaths.FromFinalPath(finalPath));

    [Theory]
    [InlineData(@"\\?\Volume{0b1c2d3e-0000-0000-0000-100000000000}\Jobs\A-101.pdf")]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolume3\A-101.pdf")]
    [InlineData("")]
    public void FromFinalPath_RefusesAVolumeOrDevicePath(string finalPath)
        => Assert.Null(HeldFilePaths.FromFinalPath(finalPath));

    [Theory]
    [InlineData(@"\\files\projects\Tower A\A-101.pdf", @"P:\Tower A\A-101.pdf")]
    [InlineData(@"\\FILES\Projects\Tower A\A-101.pdf", @"P:\Tower A\A-101.pdf")]
    [InlineData(@"\\files\projects\Tower B\Spec.pdf", @"T:\Spec.pdf")]
    [InlineData(@"\\files\projects2\A-101.pdf", @"\\files\projects2\A-101.pdf")]
    [InlineData(@"\\files\projects", @"\\files\projects")]
    [InlineData(@"C:\Jobs\A-101.pdf", @"C:\Jobs\A-101.pdf")]
    public void ToMappedDrive_UsesTheLongestMappedShare(string path, string expected)
        => Assert.Equal(expected, HeldFilePaths.ToMappedDrive(path, Drives));

    [Fact]
    public void ToMappedDrive_TwoLettersForOneShare_TheFirstLetterWins()
    {
        var drives = new Dictionary<string, string> { ["Q:"] = @"\\files\projects", ["P:"] = @"\\files\projects" };

        Assert.Equal(@"P:\A-101.pdf", HeldFilePaths.ToMappedDrive(@"\\files\projects\A-101.pdf", drives));
    }

    [Fact]
    public void Resolve_KeepsOnlyPdfWordAndExcelFiles()
        => Assert.Equal(new[] { @"C:\Jobs\A-101.pdf", @"C:\Jobs\Budget.xlsx" },
            Paths(new[] { @"\\?\C:\Jobs\A-101.pdf", @"\\?\C:\Jobs\markups.bfx", @"\\?\C:\Jobs\Budget.xlsx", @"\\?\C:\Jobs" }));

    [Fact]
    public void Resolve_DropsOfficeOwnerFiles()
        => Assert.Equal(new[] { @"C:\Jobs\Report.docx" }, Paths(new[] { @"\\?\C:\Jobs\~$Report.docx", @"\\?\C:\Jobs\Report.docx" }));

    [Fact]
    public void Resolve_DropsFilesInProgramAndApplicationFolders()
        => Assert.Equal(new[] { @"C:\Users\Tom\AppDataCopy\A-101.pdf" }, Paths(new[]
        {
            @"\\?\C:\Users\Tom\AppData\Local\Bluebeam\Revu\21\Studio\A-101.pdf",
            @"\\?\C:\Users\Tom\AppData\Roaming\Microsoft\Excel\XLSTART\PERSONAL.XLSB",
            @"\\?\C:\PROGRAM FILES\Bluebeam Software\Help.pdf",
            @"\\?\C:\Users\Tom\AppDataCopy\A-101.pdf",
        }, folders: AppFolders));

    [Fact]
    public void Resolve_SpellsSharesWithTheirMappedDrive()
        => Assert.Equal(new[] { @"P:\Tower A\A-101.pdf" }, Paths(new[] { @"\\?\UNC\files\projects\Tower A\A-101.pdf" }, Drives));

    [Fact]
    public void Resolve_AFileHeldTwice_IsListedOnceWithTheFirstProgram()
    {
        var files = HeldFilePaths.Resolve(
            new[] { (@"\\?\C:\Jobs\A-101.pdf", "Bluebeam Revu"), (@"\\?\c:\jobs\a-101.PDF", "Adobe Acrobat") },
            NoDrives, Array.Empty<string>());

        Assert.Equal(new[] { new HeldFile(@"C:\Jobs\A-101.pdf", "Bluebeam Revu") }, files);
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail**

Run from `C:\QuickerPlaces\src`: `dotnet test QuickerPlaces.Tests --filter FullyQualifiedName~HeldFilePathsTests`
Expected: the build fails with `CS0103: The name 'HeldFilePaths' does not exist` or `CS2001: Source file ... HeldFilePaths.cs could not be found`.

- [ ] **Step 4: Write the implementation**

Create `src/QuickerPlaces/Services/Documents/HeldFilePaths.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.Services.Documents;

/// <summary>
/// Turns the paths Windows gives for files a program holds open
/// (GetFinalPathNameByHandle) into the paths a session keeps (held-files
/// plan H2, H3): the "\\?\" prefix removed, a share spelled with the
/// user's mapped drive letter when one points there, and anything that
/// isn't a document the user opened left out — other file types, Office's
/// "~$" owner files, and files in program, system and per-user application
/// folders, such as Revu's Studio cache.
///
/// Pure logic: WindowsHeldFiles gathers the raw paths. UI-free and linked
/// into the test project.
/// </summary>
public static class HeldFilePaths
{
    /// <summary>
    /// The held documents among <paramref name="raw"/>, each once, in the
    /// order found, with the first program that held it.
    /// <paramref name="mappedDrives"/> maps a drive ("P:") to its share
    /// ("\\files\projects"); <paramref name="excludedFolders"/> are folders
    /// whose files are never the user's documents.
    /// </summary>
    public static IReadOnlyList<HeldFile> Resolve(IEnumerable<(string FinalPath, string AppName)> raw,
        IReadOnlyDictionary<string, string> mappedDrives, IReadOnlyList<string> excludedFolders)
    {
        var excluded = excludedFolders
            .Select(RootPathMatcher.Normalize)
            .OfType<string>()
            .Select(f => f.TrimEnd('\\') + '\\')
            .ToList();

        var files = new List<HeldFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (finalPath, appName) in raw)
        {
            if (FromFinalPath(finalPath) is not { } plain)
                continue;
            if (DocumentPaths.Normalize(ToMappedDrive(plain, mappedDrives)) is not { } path)
                continue;
            if (DocumentPaths.FileName(path).StartsWith("~$", StringComparison.Ordinal))
                continue;
            if (excluded.Any(folder => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (seen.Add(path))
                files.Add(new HeldFile(path, appName));
        }

        return files;
    }

    /// <summary>
    /// "C:\Jobs\A-101.pdf" for "\\?\C:\Jobs\A-101.pdf", and
    /// "\\server\share\x.pdf" for "\\?\UNC\server\share\x.pdf". A path
    /// without the prefix is returned as it is; a volume or device path
    /// ("\\?\Volume{…}\…") is null, as it has no drive or share to keep.
    /// </summary>
    public static string? FromFinalPath(string? finalPath)
    {
        if (string.IsNullOrEmpty(finalPath))
            return null;

        if (finalPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + finalPath[8..];

        if (finalPath.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            var rest = finalPath[4..];
            return rest.Length >= 3 && char.IsAsciiLetter(rest[0]) && rest[1] == ':' && rest[2] == '\\' ? rest : null;
        }

        return finalPath;
    }

    /// <summary>
    /// <paramref name="path"/> with the longest mapped share it lies under
    /// replaced by that drive ("P:\Tower A\A-101.pdf"), or unchanged. When
    /// two drives map the same share, the first letter wins.
    /// </summary>
    public static string ToMappedDrive(string path, IReadOnlyDictionary<string, string> mappedDrives)
    {
        string? drive = null;
        var rootLength = -1;
        foreach (var (letter, remote) in mappedDrives.OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase))
        {
            var root = remote.TrimEnd('\\');
            if (root.Length <= rootLength)
                continue;
            if (path.Length > root.Length && path[root.Length] == '\\' && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                drive = letter.TrimEnd('\\');
                rootLength = root.Length;
            }
        }

        return drive is null ? path : drive + path[rootLength..];
    }
}

/// <summary>A document a running program holds open, found from that program's file handles.</summary>
/// <param name="Path">The document's path, as a session keeps it.</param>
/// <param name="AppName">A readable name for the program that holds it ("Bluebeam Revu").</param>
public sealed record HeldFile(string Path, string AppName);
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test QuickerPlaces.Tests --filter FullyQualifiedName~HeldFilePathsTests`
Expected: `Passed: 19`.

If `Resolve_DropsFilesInProgramAndApplicationFolders` fails because `RootPathMatcher.Normalize` returns null for a folder, check how it handles a trailing `\` and fix it in `Resolve`, not in `RootPathMatcher`. Its folder tests must not change.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test QuickerPlaces.Tests`
Expected: `Passed: 764` (745 + 19), 0 failed.

- [ ] **Step 7: Commit**

```bash
git add src/QuickerPlaces/Services/Documents/HeldFilePaths.cs src/QuickerPlaces.Tests/HeldFilePathsTests.cs src/QuickerPlaces.Tests/QuickerPlaces.Tests.csproj
git commit -m "Add HeldFilePaths: turn a program's open-file paths into session paths"
```

---

### Task 2: `WindowsHeldFiles`: ask Windows which files a program holds

**Files:**
- Create: `src/QuickerPlaces/Services/Documents/WindowsHeldFiles.cs`

This task is Windows-only and is not linked into the tests. Task 3 checks it on a real machine.

- [ ] **Step 1: Write the class**

Create `src/QuickerPlaces/Services/Documents/WindowsHeldFiles.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace QuickerPlaces.Services.Documents;

/// <summary>
/// Lists the PDF, Word and Excel files that running programs hold open
/// (held-files plan), by asking Windows for those programs' file handles
/// rather than guessing paths and asking about each: every tab Revu or
/// Acrobat has open and every document Word or Excel has open, with its
/// full path, whether or not it is in Recent Items.
///
/// - NtQuerySystemInformation(SystemExtendedHandleInformation) lists every
///   handle in the system; only the target programs' file handles are
///   kept. The "File" object type's number differs between Windows
///   versions, so it is read from a handle this process opens on its own
///   executable, in the same list.
/// - Each is copied into this process (DuplicateHandle), kept only if it is
///   a disk file (GetFileType, so a pipe is never asked for its name, which
///   can hang), and named by GetFinalPathNameByHandle.
/// - <see cref="HeldFilePaths"/> turns the names into session paths.
///
/// This is how Process Explorer and handle.exe list open files. It needs
/// no elevation for the user's own programs; an elevated program's files
/// are skipped. It runs on its own background thread with a time limit
/// (H4): if a handle stalls, what was found so far is returned and the
/// thread is left to finish on its own.
///
/// App-only: it calls Windows, so it is not linked into the test project.
/// </summary>
public static class WindowsHeldFiles
{
    /// <summary>
    /// The documents held by <paramref name="programs"/> and by every other
    /// process with the same executable name, as Revu and Acrobat split
    /// their work over several. Throws if the handle list can't be read.
    /// </summary>
    public static HeldFilesRead Read(IReadOnlyList<(uint ProcessId, string AppName)> programs, TimeSpan timeout)
    {
        var targets = WithSameNamedProcesses(programs);
        if (targets.Count == 0)
            return new HeldFilesRead(Array.Empty<HeldFile>(), IsComplete: true);

        var found = new ConcurrentQueue<(string FinalPath, string AppName)>();
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                Collect(targets, found);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
            Name = "QuickerPlaces held files",
        };
        worker.Start();

        var finished = worker.Join(timeout);
        if (finished && failure is not null)
            throw new InvalidOperationException("Listing held files failed.", failure);

        var files = HeldFilePaths.Resolve(found.ToArray(), MappedDrives(), ExcludedFolders());
        return new HeldFilesRead(files, finished);
    }

    private static Dictionary<uint, string> WithSameNamedProcesses(IReadOnlyList<(uint ProcessId, string AppName)> programs)
    {
        var targets = new Dictionary<uint, string>();
        foreach (var (processId, appName) in programs)
        {
            targets.TryAdd(processId, appName);

            string name;
            try
            {
                using var process = Process.GetProcessById((int)processId);
                name = process.ProcessName;
            }
            catch (Exception)
            {
                // Gone already: it holds nothing.
                continue;
            }

            foreach (var sibling in Process.GetProcessesByName(name))
            {
                targets.TryAdd((uint)sibling.Id, appName);
                sibling.Dispose();
            }
        }

        targets.Remove((uint)Environment.ProcessId);
        return targets;
    }

    private static void Collect(Dictionary<uint, string> targets, ConcurrentQueue<(string FinalPath, string AppName)> found)
    {
        var (handles, fileType) = SnapshotWithFileType();
        var self = GetCurrentProcess();

        var byProcess = handles
            .Where(h => h.ObjectTypeIndex == fileType && targets.ContainsKey((uint)h.UniqueProcessId))
            .GroupBy(h => (uint)h.UniqueProcessId);

        foreach (var group in byProcess)
        {
            var process = OpenProcess(ProcessDupHandle, false, group.Key);
            if (process == IntPtr.Zero)
                continue; // Elevated, or gone.

            try
            {
                foreach (var entry in group)
                {
                    if (!DuplicateHandle(process, entry.HandleValue, self, out var copy, 0, false, DuplicateSameAccess))
                        continue;

                    try
                    {
                        if (GetFileType(copy) == FileTypeDisk && FinalPath(copy) is { } path)
                            found.Enqueue((path, targets[group.Key]));
                    }
                    finally
                    {
                        CloseHandle(copy);
                    }
                }
            }
            finally
            {
                CloseHandle(process);
            }
        }
    }

    /// <summary>Every handle in the system, and the object type number of a file handle, read from one this process holds on its own executable.</summary>
    private static (SystemHandleEntry[] Handles, ushort FileType) SnapshotWithFileType()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("This process has no executable path.");
        using var own = File.OpenHandle(executable, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        var handles = SystemHandles();
        var ownId = (nuint)Environment.ProcessId;
        var ownHandle = own.DangerousGetHandle();
        foreach (var entry in handles)
        {
            if (entry.UniqueProcessId == ownId && entry.HandleValue == ownHandle)
                return (handles, entry.ObjectTypeIndex);
        }

        throw new InvalidOperationException("The file object type could not be found.");
    }

    private static SystemHandleEntry[] SystemHandles()
    {
        var size = 4 << 20;
        while (true)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, size, out var needed);
                if (status == StatusInfoLengthMismatch)
                {
                    // Handles come and go between calls, so leave room.
                    size = Math.Max(size * 2, needed + (1 << 20));
                    if (size > MaxHandleBuffer)
                        throw new InvalidOperationException("The system handle list is too large.");
                    continue;
                }

                if (status != 0)
                    throw new InvalidOperationException($"NtQuerySystemInformation returned 0x{status:X8}.");

                var count = (long)Marshal.ReadIntPtr(buffer);
                var entrySize = Marshal.SizeOf<SystemHandleEntry>();
                var header = 2 * IntPtr.Size;
                if (count < 0 || header + count * entrySize > size)
                    throw new InvalidOperationException("The system handle list is malformed.");

                var entries = new SystemHandleEntry[count];
                for (var i = 0; i < count; i++)
                    entries[i] = Marshal.PtrToStructure<SystemHandleEntry>(buffer + header + checked((int)(i * entrySize)));
                return entries;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>The handle's path as "\\?\C:\…" or "\\?\UNC\server\share\…", or null.</summary>
    private static string? FinalPath(IntPtr file)
    {
        var buffer = new StringBuilder(1024);
        var length = GetFinalPathNameByHandle(file, buffer, (uint)buffer.Capacity, FileNameNormalizedDos);
        if (length == 0)
            return null;

        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder((int)length + 1);
            length = GetFinalPathNameByHandle(file, buffer, (uint)buffer.Capacity, FileNameNormalizedDos);
            if (length == 0 || length >= buffer.Capacity)
                return null;
        }

        return buffer.ToString();
    }

    /// <summary>Each mapped network drive ("P:") and the share it points at ("\\files\projects").</summary>
    private static IReadOnlyDictionary<string, string> MappedDrives()
    {
        var drives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Network))
        {
            var letter = drive.Name.TrimEnd('\\');
            var remote = new StringBuilder(1024);
            var length = remote.Capacity;
            if (WNetGetConnection(letter, remote, ref length) == 0)
                drives[letter] = remote.ToString();
        }

        return drives;
    }

    /// <summary>
    /// Folders whose files are programs' own, never the user's documents (H2).
    /// The profile's whole AppData folder covers LocalLow, where Acrobat
    /// Reader's protected mode keeps its data; Roaming and Local are listed
    /// too in case either is redirected elsewhere. TEMP is expanded to its
    /// long name, since handles report long names and TEMP may be set to an
    /// 8.3 one ("THOMAS~1").
    /// </summary>
    private static IReadOnlyList<string> ExcludedFolders() => new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData"),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            LongPath(Path.GetTempPath()),
        }
        .Where(f => !string.IsNullOrEmpty(f))
        .ToList();

    /// <summary>The long form of a folder path ("C:\Users\Thomas\…" for "C:\Users\THOMAS~1\…"), or the path as given.</summary>
    private static string LongPath(string path)
    {
        var buffer = new StringBuilder(1024);
        var length = GetLongPathName(path, buffer, buffer.Capacity);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : path;
    }

    // ---------------------------------------------------------------
    // Interop
    // ---------------------------------------------------------------

    private const int SystemExtendedHandleInformation = 64;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int MaxHandleBuffer = 256 << 20;
    private const uint ProcessDupHandle = 0x0040;
    private const uint DuplicateSameAccess = 0x2;
    private const uint FileTypeDisk = 0x1;
    private const uint FileNameNormalizedDos = 0x0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemHandleEntry
    {
        public nint Object;
        public nuint UniqueProcessId;
        public nint HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int informationClass, IntPtr information, int length, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess,
        out IntPtr targetHandle, uint access, bool inheritHandle, uint options);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr file);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(IntPtr file, StringBuilder path, uint length, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetLongPathName(string shortPath, StringBuilder longPath, int length);
}

/// <summary>What <see cref="WindowsHeldFiles.Read"/> found.</summary>
/// <param name="Files">The held documents, as session paths.</param>
/// <param name="IsComplete">False when the time limit ran out first, so some may be missing.</param>
public sealed record HeldFilesRead(IReadOnlyList<HeldFile> Files, bool IsComplete);
```

- [ ] **Step 2: Build the app**

Run from `C:\QuickerPlaces\src`: `dotnet build QuickerPlaces/QuickerPlaces.csproj`
Expected: `Build succeeded`, `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/QuickerPlaces/Services/Documents/WindowsHeldFiles.cs
git commit -m "Add WindowsHeldFiles: list the documents a program holds open"
```

---

### Task 3: Check it on a real machine with Revu (gate)

**Files:**
- Modify: `src/QuickerPlaces.ActivityProbe/QuickerPlaces.ActivityProbe.csproj`
- Modify: `src/QuickerPlaces.ActivityProbe/Program.cs` (menu at lines 42-57; new method after `Live`)

Don't start Task 4 until this check passes. It is the only way to confirm Revu really holds its files and that the handle list can be read without elevation.

- [ ] **Step 1: Link the document files into the probe**

In `src/QuickerPlaces.ActivityProbe/QuickerPlaces.ActivityProbe.csproj`, inside the existing `<ItemGroup>`, after the `DiagnosticLog.cs` line, add:

```xml
    <Compile Include="../QuickerPlaces/Services/Documents/DocumentKind.cs" Link="Linked/Services/Documents/DocumentKind.cs" />
    <Compile Include="../QuickerPlaces/Services/Documents/DocumentPaths.cs" Link="Linked/Services/Documents/DocumentPaths.cs" />
    <Compile Include="../QuickerPlaces/Services/Documents/HeldFilePaths.cs" Link="Linked/Services/Documents/HeldFilePaths.cs" />
    <Compile Include="../QuickerPlaces/Services/Documents/WindowsHeldFiles.cs" Link="Linked/Services/Documents/WindowsHeldFiles.cs" />
```

- [ ] **Step 2: Add menu option 4**

In `src/QuickerPlaces.ActivityProbe/Program.cs`, add `using QuickerPlaces.Services.Documents;` after `using QuickerPlaces.Services.Activity;`.

Replace the menu line:

```csharp
                Console.WriteLine("\n1 Live view   2 Stress (20,000 passes)   3 Host/lock run   Q Quit");
```

with:

```csharp
                Console.WriteLine("\n1 Live view   2 Stress (20,000 passes)   3 Host/lock run   4 Held files   Q Quit");
```

After the `case "3": case "host": ...` line, add:

```csharp
                    case "4": case "held": HeldFiles(); break;
```

Replace `default: Console.WriteLine("Choose 1, 2, 3 or Q."); break;` with:

```csharp
                    default: Console.WriteLine("Choose 1, 2, 3, 4 or Q."); break;
```

Then add this method directly after `Live()`:

```csharp
    private static void HeldFiles()
    {
        Console.Write("Process name (for example Revu, Acrobat, WINWORD, EXCEL): ");
        var name = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(name))
            return;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        var processes = Process.GetProcessesByName(name);
        if (processes.Length == 0)
        {
            Console.WriteLine($"No process named {name} is running.");
            return;
        }

        var programs = new List<(uint ProcessId, string AppName)>();
        foreach (var process in processes)
        {
            programs.Add(((uint)process.Id, name));
            process.Dispose();
        }

        Console.WriteLine("Paths appear on screen only.");
        var clock = Stopwatch.StartNew();
        try
        {
            var read = WindowsHeldFiles.Read(programs, TimeSpan.FromSeconds(3));
            Console.WriteLine($"{read.Files.Count} document(s) held by {name} in {clock.ElapsedMilliseconds} ms" +
                              (read.IsComplete ? ":" : " (stopped at the time limit):"));
            foreach (var file in read.Files)
                Console.WriteLine("  " + file.Path);
            Log($"Held files: {read.Files.Count} document(s), {programs.Count} process(es), {clock.ElapsedMilliseconds} ms, complete={read.IsComplete}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Listing held files failed: {ex.GetType().Name}: {ex.Message}");
            Log($"Held files failed: {ex.GetType().Name}.");
        }
    }
```

- [ ] **Step 3: Build the probe**

Run from `C:\QuickerPlaces\src`: `dotnet build QuickerPlaces.ActivityProbe`
Expected: `Build succeeded`, `0 Error(s)`.

- [ ] **Step 4: Run it with Revu (the user does this)**

1. In Revu, open three PDFs as tabs: one on a local drive, one on a mapped network drive (for example `P:\…`), and one you opened from Revu's own **File → Open**. If you can, add a Studio Session document as a fourth tab.
2. Find Revu's process name:

```bash
powershell -NoProfile -Command "Get-Process | Where-Object ProcessName -match 'Revu|Bluebeam' | Select-Object ProcessName, Id"
```

3. Run the probe from `C:\QuickerPlaces\src`, press `4`, and type that process name:

```bash
dotnet run --project QuickerPlaces.ActivityProbe
```

**Pass:** all three PDFs are listed with full paths. The network one uses its drive letter (`P:\…`), not `\\server\share\…`. The Studio document is not listed. The time is well under 3 seconds and the list isn't cut off at the limit.

**If it fails, stop here and write down which of these happened:**
- *0 documents, no error:* Revu may not hold the files open, or it may be running elevated (check Task Manager → Details → "Elevated" column). Record the result in the sessions plan §4 and don't carry on with Tasks 4–6.
- *Stopped at the time limit:* a handle stalled in `GetFileType`. Record how many documents appeared anyway.
- *An exception:* record the message.

Also repeat the check with `WINWORD` (two documents open) and `EXCEL` (one workbook), and confirm `PERSONAL.XLSB` isn't listed.

If one of your drives is mapped to a DFS namespace (`net use` shows a path like `\\company\dfs\projects`), open a PDF from it too. Windows may report the file on its target server (`\\fs01\projects$\…`), which no drive letter matches. Write down which spelling the probe shows. If it's the server spelling, the same file could appear twice in the review list, once from Recent Items as `P:\…` and once held, and that needs handling before Task 4.

- [ ] **Step 5: Commit**

```bash
git add src/QuickerPlaces.ActivityProbe/QuickerPlaces.ActivityProbe.csproj src/QuickerPlaces.ActivityProbe/Program.cs
git commit -m "Add a Held files check to the developer probe"
```

---

### Task 4: Resolver: held files count as open and name title matches

**Files:**
- Modify: `src/QuickerPlaces/Services/Documents/OpenDocumentResolver.cs` (`CandidatePaths` ~line 47, `Resolve` ~line 65, `OpenDocumentEvidence` ~line 419)
- Modify: `src/QuickerPlaces.Tests/OpenDocumentResolverTests.cs`

- [ ] **Step 1: Write the failing tests**

In `src/QuickerPlaces.Tests/OpenDocumentResolverTests.cs`, add this helper after the existing `Resolve` helper:

```csharp
    private static OpenDocumentScan ResolveHeld(ViewerWindow[] windows, RecentDocument[] recents, params HeldFile[] held)
        => OpenDocumentResolver.Resolve(new OpenDocumentEvidence(windows, recents) { HeldFiles = held }, Array.Empty<string>());
```

Add these tests at the end of the class:

```csharp
    [Fact]
    public void AHeldFile_IsOpen_WithoutATitleOrARecentItem()
    {
        var scan = ResolveHeld(Array.Empty<ViewerWindow>(), Array.Empty<RecentDocument>(), new HeldFile(Spec, "Bluebeam Revu"));

        var candidate = Assert.Single(scan.Candidates);
        Assert.Equal(Spec, candidate.Path);
        Assert.True(candidate.IsLikelyOpen);
        Assert.Equal("Open in Bluebeam Revu", candidate.Reason);
        Assert.Null(candidate.LastOpenedAt);
    }

    [Fact]
    public void EveryHeldTab_IsOpen_AndTheTitlesNameMatchesAHeldPath_WithoutRecentItems()
    {
        var scan = ResolveHeld(new[] { Window("Bluebeam Revu x64 - [A-102.pdf]", "Bluebeam Revu") }, Array.Empty<RecentDocument>(),
            new HeldFile(A101, "Bluebeam Revu"), new HeldFile(A102, "Bluebeam Revu"));

        Assert.Equal(new[] { A102, A101 }, OpenPaths(scan));
        Assert.Empty(SuggestedPaths(scan));
        Assert.Empty(scan.UnmatchedTitles);
    }

    [Fact]
    public void AHeldFile_WinsOverAMoreRecentFileWithTheSameName()
    {
        var scan = ResolveHeld(new[] { Window("Bluebeam Revu x64 - [A-101.pdf]", "Bluebeam Revu") }, new[] { Recent(OldA101, 5) },
            new HeldFile(A101, "Bluebeam Revu"));

        Assert.Equal(new[] { A101 }, OpenPaths(scan));
        Assert.Equal(new[] { OldA101 }, SuggestedPaths(scan));
    }

    [Fact]
    public void AHeldFileAlsoInRecentItems_IsListedOnce_WithWhenItWasOpened()
    {
        var scan = ResolveHeld(Array.Empty<ViewerWindow>(), new[] { Recent(A101, 5) }, new HeldFile(A101, "Bluebeam Revu"));

        var candidate = Assert.Single(scan.Candidates);
        Assert.True(candidate.IsLikelyOpen);
        Assert.Equal(Now.AddMinutes(-5), candidate.LastOpenedAt);
    }

    [Fact]
    public void CandidatePaths_ListHeldFilesFirst()
    {
        var evidence = new OpenDocumentEvidence(new[] { Window(@"C:\Jobs\Tower B\A-102.pdf - Viewer", "Viewer") }, new[] { Recent(A101, 5) })
        {
            HeldFiles = new[] { new HeldFile(Spec, "Bluebeam Revu") },
        };

        Assert.Equal(new[] { Spec, A102, A101 }, OpenDocumentResolver.CandidatePaths(evidence));
    }
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test QuickerPlaces.Tests --filter FullyQualifiedName~OpenDocumentResolverTests`
Expected: the build fails with `CS0117: 'OpenDocumentEvidence' does not contain a definition for 'HeldFiles'`.

- [ ] **Step 3: Add `HeldFiles` to the evidence**

In `OpenDocumentResolver.cs`, replace the `OpenDocumentEvidence` record and its doc comment:

```csharp
/// <summary>What <see cref="OpenDocumentResolver"/> works from, gathered by the probe.</summary>
/// <param name="Windows">Visible top-level windows whose titles mention a document, or that belong to Word or Excel, with their program's name and command line.</param>
/// <param name="RecentDocuments">Documents in Windows' Recent Items, with when each was last opened.</param>
public sealed record OpenDocumentEvidence(IReadOnlyList<ViewerWindow> Windows, IReadOnlyList<RecentDocument> RecentDocuments)
{
    public static OpenDocumentEvidence Empty { get; } = new(Array.Empty<ViewerWindow>(), Array.Empty<RecentDocument>());

    /// <summary>Documents the windows' programs hold open, with full paths, from WindowsHeldFiles. Every one counts as open (held-files plan H1).</summary>
    public IReadOnlyList<HeldFile> HeldFiles { get; init; } = Array.Empty<HeldFile>();
}
```

- [ ] **Step 4: List held files first in `CandidatePaths`**

Replace the `CandidatePaths` method and its doc comment:

```csharp
    /// <summary>
    /// Every document path the evidence names, before asking which are in
    /// use: files programs hold open, full paths in titles, paths on command
    /// lines, and recent documents. Each once, in that order.
    /// </summary>
    public static IReadOnlyList<string> CandidatePaths(OpenDocumentEvidence evidence)
    {
        var paths = new List<string>();
        paths.AddRange(evidence.HeldFiles.Select(h => DocumentPaths.Normalize(h.Path)).OfType<string>());
        foreach (var window in evidence.Windows)
        {
            paths.AddRange(PathsInTitle(window.Title));
            paths.AddRange(PathsInCommandLine(window.CommandLine));
        }

        paths.AddRange(evidence.RecentDocuments.Select(r => DocumentPaths.Normalize(r.Path)).OfType<string>());
        return Distinct(paths);
    }
```

- [ ] **Step 5: Treat held files as open in `Resolve`**

In `Resolve`, replace the first line:

```csharp
        var inUseSet = new HashSet<string>(inUse.Select(p => DocumentPaths.Normalize(p) ?? p), StringComparer.OrdinalIgnoreCase);
```

with:

```csharp
        var held = evidence.HeldFiles
            .Select(h => (Path: DocumentPaths.Normalize(h.Path), h.AppName))
            .Where(h => h.Path is not null)
            .ToList();

        // A held file is in use by definition, so a title's name prefers it too (D6).
        var inUseSet = new HashSet<string>(
            inUse.Select(p => DocumentPaths.Normalize(p) ?? p).Concat(held.Select(h => h.Path!)),
            StringComparer.OrdinalIgnoreCase);
```

Then, directly before this existing loop:

```csharp
        foreach (var path in known.Where(inUseSet.Contains))
            MarkOpen(path, "In use by an open program");
```

insert:

```csharp
        // Background tabs and other documents the programs hold, after each window's front document.
        foreach (var (path, appName) in held)
            MarkOpen(path!, $"Open in {appName}");
```

In the class doc comment, add this bullet after the **Files in use** bullet:

```csharp
/// - <b>Files held open.</b> The documents the windows' programs hold,
///   with full paths, from their file handles (held-files plan). Revu,
///   Acrobat, Word and Excel hold every open document, so this finds
///   background tabs and files that aren't in Recent Items, and gives a
///   title's bare name its path.
```

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test QuickerPlaces.Tests --filter FullyQualifiedName~OpenDocumentResolverTests`
Expected: all pass, the 5 new tests included.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test QuickerPlaces.Tests`
Expected: `Passed: 769` (764 + 5), 0 failed.

- [ ] **Step 8: Commit**

```bash
git add src/QuickerPlaces/Services/Documents/OpenDocumentResolver.cs src/QuickerPlaces.Tests/OpenDocumentResolverTests.cs
git commit -m "Count files a program holds open as open, and match titles to them"
```

---

### Task 5: Wire held files into the session scan

**Files:**
- Modify: `src/QuickerPlaces/Services/Documents/WindowsOpenDocumentProbe.cs` (class comment lines 14-34, `Scan` lines 66-162, `FindViewerWindows` lines 168-207)

- [ ] **Step 1: Return the windows' programs from `FindViewerWindows`**

Replace the method's signature line:

```csharp
    private static IReadOnlyList<ViewerWindow> FindViewerWindows()
```

with:

```csharp
    /// <summary>The document windows, and each program that owns one, for WindowsHeldFiles.</summary>
    private static (IReadOnlyList<ViewerWindow> Windows, IReadOnlyList<(uint ProcessId, string AppName)> Programs) FindViewerWindows()
```

and its last line, `return windows;`, with:

```csharp
        return (windows, programs.Select(p => (p.Key, p.Value.AppName)).ToList());
```

- [ ] **Step 2: Read held files in `Scan`**

Add this constant after `ScanBudget`:

```csharp
    /// <summary>How long listing the files programs hold open may take, within the scan's budget (H4).</summary>
    private static readonly TimeSpan HeldFilesBudget = TimeSpan.FromSeconds(3);
```

In `Scan`, replace the window block:

```csharp
        IReadOnlyList<ViewerWindow> windows;
        try
        {
            windows = FindViewerWindows();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Listing document windows failed ({ex.GetType().Name}).");
            windows = Array.Empty<ViewerWindow>();
            warnings.Add("Open windows couldn't be read.");
        }
```

with:

```csharp
        IReadOnlyList<ViewerWindow> windows;
        IReadOnlyList<(uint ProcessId, string AppName)> programs;
        try
        {
            (windows, programs) = FindViewerWindows();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Listing document windows failed ({ex.GetType().Name}).");
            windows = Array.Empty<ViewerWindow>();
            programs = Array.Empty<(uint, string)>();
            warnings.Add("Open windows couldn't be read.");
        }

        IReadOnlyList<HeldFile> held;
        try
        {
            var read = WindowsHeldFiles.Read(programs, HeldFilesBudget);
            held = read.Files;
            if (!read.IsComplete)
                warnings.Add("Listing the files open in PDF, Word and Excel programs took too long, so some may be missing.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Listing held documents failed ({ex.GetType().Name}).");
            held = Array.Empty<HeldFile>();
            warnings.Add("The files open in PDF, Word and Excel programs couldn't be listed, so some may be missing.");
        }

        var heldPaths = new HashSet<string>(held.Select(h => h.Path), StringComparer.OrdinalIgnoreCase);
```

- [ ] **Step 3: Pass held files on and don't check them again**

Replace:

```csharp
        var evidence = new OpenDocumentEvidence(windows, recents);
```

with:

```csharp
        var evidence = new OpenDocumentEvidence(windows, recents) { HeldFiles = held };
```

At the top of the `foreach (var path in OpenDocumentResolver.CandidatePaths(evidence))` loop body, before the budget check, insert:

```csharp
            // A held file exists and is open: the program holding it said so.
            if (heldPaths.Contains(path))
                continue;
```

Replace:

```csharp
        var kept = new OpenDocumentEvidence(windows, recents.Where(r => !missing.Contains(DocumentPaths.Normalize(r.Path) ?? r.Path)).ToList());
```

with:

```csharp
        var kept = new OpenDocumentEvidence(windows, recents.Where(r => !missing.Contains(DocumentPaths.Normalize(r.Path) ?? r.Path)).ToList())
        {
            HeldFiles = held,
        };
```

Replace the log line's first part:

```csharp
        DiagnosticLog.Info($"Document scan: {windows.Count} window(s), {recents.Count} recent, {inUse.Count} in use, " +
```

with:

```csharp
        DiagnosticLog.Info($"Document scan: {windows.Count} window(s), {held.Count} held, {recents.Count} recent, {inUse.Count} in use, " +
```

- [ ] **Step 4: Update the class comment**

In the class doc comment, after the first bullet (top-level windows), insert:

```csharp
/// - The PDF, Word and Excel files those windows' programs hold open, with
///   full paths, through <see cref="WindowsHeldFiles"/>: every Revu or
///   Acrobat tab, whether or not it is in Recent Items.
```

and change the Restart Manager bullet's first line to:

```csharp
/// - For each other candidate that exists, whether a program has it open,
```

- [ ] **Step 5: Build and run all tests**

Run from `C:\QuickerPlaces\src`: `dotnet build QuickerPlaces.sln`
Expected: `Build succeeded`, `0 Warning(s)`, `0 Error(s)`.

Run: `dotnet test QuickerPlaces.Tests`
Expected: `Passed: 769`, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/QuickerPlaces/Services/Documents/WindowsOpenDocumentProbe.cs
git commit -m "Find open documents from the files their programs hold when saving a session"
```

---

### Task 6: Document it and check the app by hand

**Files:**
- Modify: `ai/260928_PDF Project Sessions Plan.md` (§4 table and paragraph; §9 checklist)

- [ ] **Step 1: Update §4**

In the §4 table, add this row directly after the **Command lines** row:

```markdown
| **Files held open** (`NtQuerySystemInformation` handle list, `GetFinalPathNameByHandle`) by those windows' programs — held-files plan | Every document Revu, Acrobat, Word or Excel holds, with its full path and mapped drive letter, including background tabs and files not in Recent Items | Programs that read a file and let go; an elevated program; Studio Session copies and program folders, left out on purpose |
```

Replace the "Expected by program" paragraph with:

```markdown
Expected by program. Revu, Acrobat, Reader, Word and Excel hold their files open, so every document is found with its path from the held-files pass, whether or not it is in Recent Items. Revu was checked with the developer probe (held-files plan Task 3) on <date>: <result>. Edge, Chrome and SumatraPDF can show only the front tab, and only when its file is in Recent Items. A title that matches no known file is listed under the review list, for the user to add by hand.
```

Fill in `<date>` and `<result>` from Task 3 Step 4.

- [ ] **Step 2: Add to the §9 checklist**

After the **Library** items, before "Record each item as passed…", add:

```markdown
**Held files**

16. **Revu with three tabs, opened from Revu's own File → Open** (not from Explorer), one on a mapped drive: **Save open files…** lists all three ticked, "Open in Bluebeam Revu", and the network one with its drive letter.
17. **Revu with a Studio Session document open**: it is not listed.
18. **Revu closed, nothing else open**: the scan finishes with no warning and lists only recent suggestions.
```

- [ ] **Step 3: Check the app by hand (the user does this)**

```bash
dotnet run --project QuickerPlaces
```

Work through checklist items 16–18, then item 1 (Acrobat or Revu tabs, Word with two documents, Excel with one workbook). Record the results in `ai/BUILD_SUMMARY.md` as the checklist says.

- [ ] **Step 4: Commit**

```bash
git add "ai/260928_PDF Project Sessions Plan.md" ai/BUILD_SUMMARY.md
git commit -m "Document the held-files clue and its manual checks"
```

---

## Risks

- **`GetFileType` can block** on a pipe with a synchronous read pending. After the Task 2 review, a watchdog handles this: a handle that takes more than 500 ms is abandoned and remembered for the rest of the app's life, and the walk continues from the next handle on a new thread, with at most 3 abandoned per scan. The programs' own processes are read before their siblings. A `GrantedAccess` "hang mask" filter was considered and rejected, because `0x12019F` is also what a file opened for read and write gets, which is how Revu holds its PDFs. The final code in commit history supersedes the Task 2 code block above.
- **An abandoned worker's duplicate keeps a stuck pipe's file object open** until its call returns. This is rare, and at most 3 such workers are abandoned per scan.
- **Endpoint protection** (Defender for Endpoint, CrowdStrike and similar) may flag `OpenProcess(PROCESS_DUP_HANDLE)` followed by many `DuplicateHandle` calls on Office or Acrobat as handle theft. Check for an alert during Task 3 on a managed PC.
- **Revu elevated** (Run as administrator): `OpenProcess(PROCESS_DUP_HANDLE)` fails, so its files are skipped without an error. Restart Manager and titles still work as before.
- **`SystemExtendedHandleInformation` is undocumented** but has been stable since Windows XP and is what Sysinternals tools rely on. If it fails, the scan carries on without held files and shows one warning line.
