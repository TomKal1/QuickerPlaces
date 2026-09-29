using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace QuickerPlaces.Services.Documents;

/// <summary>
/// Decides which PDF, Word and Excel files are open from what Windows will
/// tell an ordinary program (sessions plan §4, D6–D9). No documented Windows
/// API lists the documents another application has open, so the answer is
/// assembled from best-effort clues and handed to the user to review, never
/// saved unseen:
///
/// - <b>Window titles.</b> Viewers put the active document's file name in
///   their title ("A-101.pdf - Adobe Acrobat Pro", "Budget.xlsx - Excel"),
///   and some the full path. Word and Excel leave the extension out when
///   Explorer hides extensions ("Report - Word"), so for their windows the
///   name without its extension counts too, at the start of the title. A
///   title shows only the active tab or document.
/// - <b>Command lines.</b> The file a program was started with. A
///   single-instance program keeps that command line after the file is
///   closed, so on its own it only suggests.
/// - <b>Files in use.</b> Restart Manager's answer to "is anything using
///   this file", asked of the candidates found here. Acrobat, Revu, Word and
///   Excel hold their documents open and answer for every one; browsers and
///   SumatraPDF read a file and let go, and never do.
/// - <b>Files held open.</b> The documents the windows' programs hold,
///   with full paths, from their file handles (held-files plan). Revu,
///   Acrobat, Word and Excel hold every open document, so this finds
///   background tabs and files that aren't in Recent Items, and gives a
///   title's bare name its path.
///
/// A title gives a file name, not a path, so it is matched to a path from
/// the other clues and Windows' Recent Items, which also supply the
/// "recently opened" suggestions. A title that matches no known path is
/// reported so the user can add that file by hand.
///
/// Pure logic over plain inputs: WindowsOpenDocumentProbe gathers them.
/// UI-free and linked into the test project.
/// </summary>
public static class OpenDocumentResolver
{
    /// <summary>How many recently opened documents are offered at most, newest first.</summary>
    public const int MaxRecentSuggestions = 30;

    /// <summary>
    /// Every document path the evidence names, before asking which are in
    /// use: files programs hold open, full paths in titles, paths on command
    /// lines, and recent documents. Each once, in that order.
    /// </summary>
    public static IReadOnlyList<string> CandidatePaths(OpenDocumentEvidence evidence)
    {
        var paths = new List<string>();
        paths.AddRange(HeldPaths(evidence).Select(h => h.Path));
        foreach (var window in evidence.Windows)
        {
            paths.AddRange(TitlePaths(window, evidence));
            paths.AddRange(CommandLinePaths(window, evidence));
        }

        paths.AddRange(evidence.RecentDocuments.Select(r => Spell(r.Path, evidence)).OfType<string>());
        return Distinct(paths);
    }

    /// <summary>
    /// The one spelling of a document path the resolver uses for every clue:
    /// normalized, and under a mapped drive's letter where it lies on that
    /// drive's share, as held files are spelled. Null when it isn't a document
    /// path. The probe spells the paths it asks about the same way.
    /// </summary>
    public static string? Spell(string? path, OpenDocumentEvidence evidence)
        => DocumentPaths.Normalize(path) is { } p ? DocumentPaths.Normalize(HeldFilePaths.ToMappedDrive(p, evidence.MappedDrives)) ?? p : null;

    /// <summary>
    /// Resolves the evidence into the review list: files judged open first,
    /// in the order found, then suggestions, newest first. Files in
    /// <paramref name="inUse"/> count as open.
    /// </summary>
    public static OpenDocumentScan Resolve(OpenDocumentEvidence evidence, IEnumerable<string> inUse)
    {
        var held = HeldPaths(evidence);

        // A held file is in use by definition, so a title's name prefers it too (D6).
        var inUseSet = new HashSet<string>(
            inUse.Select(p => Spell(p, evidence) ?? p).Concat(held.Select(h => h.Path)),
            StringComparer.OrdinalIgnoreCase);
        var recents = evidence.RecentDocuments
            .Select(r => (Path: Spell(r.Path, evidence), r.LastOpenedAt))
            .Where(r => r.Path is not null)
            .GroupBy(r => r.Path!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(r => r.LastOpenedAt), StringComparer.OrdinalIgnoreCase);

        var open = new List<DocumentCandidate>();
        var openPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unmatched = new List<string>();

        void MarkOpen(string path, string reason)
        {
            if (openPaths.Add(path))
                open.Add(new DocumentCandidate(path, true, reason, recents.TryGetValue(path, out var at) ? at : null));
        }

        // Every path any clue names, so a title's bare file name can be matched to one.
        var known = CandidatePaths(evidence);

        foreach (var window in evidence.Windows)
        {
            var commandLinePaths = CommandLinePaths(window, evidence);
            var matchedInTitle = false;

            var titlePaths = TitlePaths(window, evidence);
            foreach (var path in titlePaths)
            {
                MarkOpen(path, $"Open in {window.AppName}");
                matchedInTitle = true;
            }

            // Longest file name first, so "Set - A-101.pdf" wins over "A-101.pdf" in the same title.
            // A name the title already spells out as a full path is settled: another folder's file of that name isn't open.
            var names = known
                .Select(DocumentPaths.FileName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(n => !titlePaths.Any(p => string.Equals(DocumentPaths.FileName(p), n, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(n => n.Length);
            var claimed = new List<(int Start, int End)>();
            foreach (var name in names)
            {
                var at = FindName(window.Title, name, claimed);
                if (at < 0)
                    continue;

                claimed.Add((at, at + name.Length));
                MarkOpen(Choose(name, commandLinePaths, inUseSet, recents, known), $"Open in {window.AppName}");
                matchedInTitle = true;
            }

            // Word and Excel may show the name without its extension, and only at the start of the title.
            if (!matchedInTitle && window.OfficeKind is { } officeKind && OfficeTitleName(window.Title) is { } stem)
            {
                var path = ChooseByStem(stem, officeKind, commandLinePaths, inUseSet, recents, known);
                if (path is not null)
                {
                    MarkOpen(path, $"Open in {window.AppName}");
                    matchedInTitle = true;
                }
            }

            if (!matchedInTitle && GuessNameInTitle(window.Title, window.OfficeKind) is { } guess)
                unmatched.Add($"{guess} ({window.AppName})");
        }

        // Background tabs and other documents the programs hold, after each window's front document.
        foreach (var (path, appName) in held)
            MarkOpen(path, $"Open in {appName}");

        foreach (var path in known.Where(inUseSet.Contains))
            MarkOpen(path, "In use by an open program");

        // Named on a command line but not confirmed: shown as a suggestion, as a recent file is.
        var suggestions = new List<DocumentCandidate>();
        var suggested = new HashSet<string>(openPaths, StringComparer.OrdinalIgnoreCase);
        foreach (var window in evidence.Windows)
        {
            foreach (var path in CommandLinePaths(window, evidence))
            {
                if (suggested.Add(path))
                    suggestions.Add(new DocumentCandidate(path, false, $"{window.AppName} was started with it", recents.TryGetValue(path, out var at) ? at : null));
            }
        }

        foreach (var (path, at) in recents.OrderByDescending(r => r.Value))
        {
            if (suggestions.Count >= MaxRecentSuggestions)
                break;
            if (suggested.Add(path))
                suggestions.Add(new DocumentCandidate(path, false, "Recently opened", at));
        }

        return new OpenDocumentScan(open.Concat(suggestions).ToList(), unmatched.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static List<(string Path, string AppName)> HeldPaths(OpenDocumentEvidence evidence)
        => evidence.HeldFiles
            .Select(h => (Path: Spell(h.Path, evidence), h.AppName))
            .Where(h => h.Path is not null)
            .Select(h => (h.Path!, h.AppName))
            .ToList();

    private static IReadOnlyList<string> TitlePaths(ViewerWindow window, OpenDocumentEvidence evidence)
        => Distinct(PathsInTitle(window.Title).Select(p => Spell(p, evidence)).OfType<string>());

    private static IReadOnlyList<string> CommandLinePaths(ViewerWindow window, OpenDocumentEvidence evidence)
        => Distinct(PathsInCommandLine(window.CommandLine).Select(p => Spell(p, evidence)).OfType<string>());

    /// <summary>Full drive or UNC paths to documents written out in a window title, as some viewers do.</summary>
    public static IReadOnlyList<string> PathsInTitle(string? title)
    {
        var paths = new List<string>();
        if (string.IsNullOrEmpty(title))
            return paths;

        foreach (var end in DocumentNameEnds(title))
        {
            // The earliest start that makes a valid path wins: a drive letter or "\\" before it.
            for (var start = 0; start < end; start++)
            {
                var isDrive = start + 2 < end && char.IsAsciiLetter(title[start]) && title[start + 1] == ':' && title[start + 2] is '\\' or '/';
                var isUnc = start + 1 < end && title[start] == '\\' && title[start + 1] == '\\';
                if (!isDrive && !isUnc)
                    continue;
                if (start > 0 && char.IsLetterOrDigit(title[start - 1]))
                    continue;

                if (DocumentPaths.Normalize(title[start..end]) is { } path)
                {
                    paths.Add(path);
                    break;
                }
            }
        }

        return Distinct(paths);
    }

    /// <summary>
    /// Document paths among a command line's arguments, split as Windows
    /// splits them (double quotes group, backslashes are literal), including
    /// "file:" URLs that a browser was given.
    /// </summary>
    public static IReadOnlyList<string> PathsInCommandLine(string? commandLine)
    {
        var paths = new List<string>();
        foreach (var argument in SplitCommandLine(commandLine))
        {
            var candidate = argument;
            if (candidate.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                candidate = FromFileUrl(candidate);

            // A switch with a value ("/A", "--profile=x.pdf") is not a document.
            if (candidate.StartsWith('-') || (candidate.StartsWith('/') && !candidate.StartsWith("//")))
                continue;

            if (DocumentPaths.Normalize(candidate) is { } path)
                paths.Add(path);
        }

        return Distinct(paths);
    }

    /// <summary>
    /// The document name a title seems to show, for the "couldn't match"
    /// line, or null if it shows none. For a Word or Excel window, the name
    /// before " - Word" counts even without an extension.
    /// </summary>
    public static string? GuessNameInTitle(string? title, DocumentKind? officeKind = null)
    {
        if (string.IsNullOrEmpty(title))
            return null;

        foreach (var end in DocumentNameEnds(title))
        {
            // Back to the nearest thing a viewer puts around a name: " - ", a bracket, a quote, a bar, a separator.
            var start = end;
            while (start > 0 && title[start - 1] != '.')
                start--;
            start--;
            while (start > 0)
            {
                var c = title[start - 1];
                if (c is '[' or '(' or '"' or '|' or '\\' or '/' or '\u201C' or '\u00AB')
                    break;
                if (c is '-' or '\u2013' or '\u2014' && start >= 2 && title[start - 2] == ' ' && start < title.Length && title[start] == ' ')
                    break;
                start--;
            }

            var name = title[start..end].Trim();
            if (name.Length > 0 && name.LastIndexOf('.') > 0)
                return name;
        }

        return officeKind is null ? null : OfficeTitleName(title);
    }

    /// <summary>
    /// The document name at the start of a Word or Excel title, before
    /// " - " and any "[Read-Only]" or "[Compatibility Mode]" marker:
    /// "Report" for "Report [Read-Only] - Word". Null for a title with no
    /// " - ", such as Word's own start screen.
    /// </summary>
    public static string? OfficeTitleName(string? title)
    {
        if (string.IsNullOrEmpty(title))
            return null;

        var dash = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dash <= 0)
            return null;

        var name = title[..dash];
        var bracket = name.IndexOf(" [", StringComparison.Ordinal);
        if (bracket > 0)
            name = name[..bracket];

        name = name.Trim();
        return name.Length == 0 ? null : name;
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    /// <summary>The index just past each recognised extension in <paramref name="text"/> that ends a name (not "x.pdfs" or "x.docx2").</summary>
    private static IEnumerable<int> DocumentNameEnds(string text)
    {
        var ends = new SortedSet<int>();
        foreach (var extension in DocumentKinds.Extensions)
        {
            var from = 0;
            while (from < text.Length)
            {
                var at = text.IndexOf(extension, from, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                    break;

                var end = at + extension.Length;
                if (at > 0 && (end == text.Length || !char.IsLetterOrDigit(text[end])))
                    ends.Add(end);
                from = end;
            }
        }

        return ends;
    }

    /// <summary>
    /// Where <paramref name="name"/> appears in <paramref name="title"/> as a
    /// whole name — not inside a longer word, and not overlapping a longer
    /// name already found — or -1.
    /// </summary>
    private static int FindName(string title, string name, List<(int Start, int End)> claimed)
    {
        var from = 0;
        while (from <= title.Length - name.Length)
        {
            var at = title.IndexOf(name, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return -1;

            var end = at + name.Length;
            var startsWord = at == 0 || !(char.IsLetterOrDigit(title[at - 1]) || title[at - 1] is '-' or '_' or '.');
            var endsWord = end == title.Length || !(char.IsLetterOrDigit(title[end]) || title[end] == '.');
            var overlaps = claimed.Any(c => at < c.End && end > c.Start);
            if (startsWord && endsWord && !overlaps)
                return at;

            from = at + 1;
        }

        return -1;
    }

    /// <summary>
    /// The path a title's file name most likely means, when several known
    /// paths share it: one the same program was started with, then one in
    /// use, then the most recently opened, then the first found.
    /// </summary>
    private static string Choose(string name, IReadOnlyList<string> commandLinePaths, HashSet<string> inUse,
        Dictionary<string, DateTimeOffset> recents, IReadOnlyList<string> known)
    {
        bool Named(string path) => string.Equals(DocumentPaths.FileName(path), name, StringComparison.OrdinalIgnoreCase);
        return Pick(Named, commandLinePaths, inUse, recents, known)!;
    }

    /// <summary>As <see cref="Choose"/>, for a Word or Excel title's name without its extension; null when no known file of that kind has it.</summary>
    private static string? ChooseByStem(string stem, DocumentKind kind, IReadOnlyList<string> commandLinePaths, HashSet<string> inUse,
        Dictionary<string, DateTimeOffset> recents, IReadOnlyList<string> known)
    {
        bool Named(string path) => DocumentKinds.FromPath(path) == kind &&
            string.Equals(DocumentPaths.Stem(path), stem, StringComparison.OrdinalIgnoreCase);
        return Pick(Named, commandLinePaths, inUse, recents, known);
    }

    private static string? Pick(Func<string, bool> named, IReadOnlyList<string> commandLinePaths, HashSet<string> inUse,
        Dictionary<string, DateTimeOffset> recents, IReadOnlyList<string> known)
        => commandLinePaths.FirstOrDefault(named)
            ?? known.Where(named).FirstOrDefault(inUse.Contains)
            ?? known.Where(named).Where(recents.ContainsKey).OrderByDescending(p => recents[p]).FirstOrDefault()
            ?? known.FirstOrDefault(named);

    private static IEnumerable<string> SplitCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            yield break;

        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                    yield return current.ToString();
                current.Clear();
                hasToken = false;
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (hasToken)
            yield return current.ToString();
    }

    /// <summary>"file:///C:/Jobs/A%20101.pdf" as "C:\Jobs\A 101.pdf"; "file://server/share/x.pdf" as a UNC path.</summary>
    private static string FromFileUrl(string url)
    {
        var rest = url["file:".Length..];
        string path;
        if (rest.StartsWith("///"))
            path = rest[3..];
        else if (rest.StartsWith("//"))
            path = "//" + rest[2..];
        else
            path = rest;

        try
        {
            path = Uri.UnescapeDataString(path);
        }
        catch (Exception)
        {
            // Left as it is: Normalize refuses it if it isn't a path.
        }

        return path.Replace('/', '\\');
    }

    private static IReadOnlyList<string> Distinct(IEnumerable<string> paths)
        => paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>What <see cref="OpenDocumentResolver"/> works from, gathered by the probe.</summary>
/// <param name="Windows">Visible top-level windows whose titles mention a document, or that belong to Word or Excel, with their program's name and command line.</param>
/// <param name="RecentDocuments">Documents in Windows' Recent Items, with when each was last opened.</param>
public sealed record OpenDocumentEvidence(IReadOnlyList<ViewerWindow> Windows, IReadOnlyList<RecentDocument> RecentDocuments)
{
    public static OpenDocumentEvidence Empty { get; } = new(Array.Empty<ViewerWindow>(), Array.Empty<RecentDocument>());

    /// <summary>Documents the windows' programs hold open, with full paths, from WindowsHeldFiles. Every one counts as open (held-files plan H1).</summary>
    public IReadOnlyList<HeldFile> HeldFiles { get; init; } = Array.Empty<HeldFile>();

    /// <summary>Each mapped drive ("P:") and its share, so every clue's path is spelled with the drive letter, as held files are (held-files plan H3).</summary>
    public IReadOnlyDictionary<string, string> MappedDrives { get; init; } = ReadOnlyDictionary<string, string>.Empty;
}

/// <param name="Title">The window's title as Windows reports it.</param>
/// <param name="AppName">A readable name for the program that owns it ("Adobe Acrobat").</param>
/// <param name="CommandLine">That program's command line, or null if it couldn't be read.</param>
/// <param name="OfficeKind">Word or Excel when the window is that program's document window (by its window class), else null.</param>
public sealed record ViewerWindow(string Title, string AppName, string? CommandLine, DocumentKind? OfficeKind = null);

/// <summary>A document from Windows' Recent Items.</summary>
public sealed record RecentDocument(string Path, DateTimeOffset LastOpenedAt);

/// <summary>One file in the review list.</summary>
/// <param name="Path">The document's full path.</param>
/// <param name="IsLikelyOpen">True when a clue says it is open now; these start ticked.</param>
/// <param name="Reason">Why it is listed, for the user: "Open in Adobe Acrobat", "Recently opened".</param>
/// <param name="LastOpenedAt">When Recent Items says it was last opened, if it does.</param>
public sealed record DocumentCandidate(string Path, bool IsLikelyOpen, string Reason, DateTimeOffset? LastOpenedAt);

/// <summary>The result of a scan.</summary>
/// <param name="Candidates">Files judged open, then suggestions.</param>
/// <param name="UnmatchedTitles">Document names seen in window titles that no known path matched, with the program, for the user to add by hand.</param>
public sealed record OpenDocumentScan(IReadOnlyList<DocumentCandidate> Candidates, IReadOnlyList<string> UnmatchedTitles)
{
    public static OpenDocumentScan Empty { get; } = new(Array.Empty<DocumentCandidate>(), Array.Empty<string>());

    /// <summary>Set by the probe when part of the scan failed or ran out of time, for one line under the list.</summary>
    public string? Warning { get; init; }
}
