using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QuickerPlaces.Services.Sessions;

/// <summary>
/// Decides which PDFs are open from what Windows will tell an ordinary
/// program (sessions plan §4, D6–D9). No documented Windows API lists the
/// documents another application has open, so the answer is assembled from
/// three best-effort clues and handed to the user to review, never saved
/// unseen:
///
/// - <b>Window titles.</b> PDF viewers put the active document's file name
///   in their title ("A-101.pdf - Adobe Acrobat Pro", "A-101.pdf - Personal
///   - Microsoft Edge"), and some the full path. A title shows only the
///   active tab.
/// - <b>Command lines.</b> The file a viewer was started with, for a
///   viewer started by opening a PDF. A single-instance viewer keeps that
///   command line after the file is closed, so on its own it only suggests.
/// - <b>Files in use.</b> Restart Manager's answer to "is anything using
///   this file", asked of the candidates found here. Viewers that hold
///   their documents open (Acrobat, Revu) answer for every tab; ones that
///   read a file and let go (browsers, SumatraPDF) never do.
///
/// A title gives a file name, not a path, so it is matched to a path from
/// the other clues and Windows' Recent Items, which also supply the
/// "recently opened" suggestions. A title that matches no known path is
/// reported so the user can add that file by hand.
///
/// Pure logic over plain inputs: WindowsOpenPdfProbe gathers them. UI-free
/// and linked into the test project.
/// </summary>
public static class OpenPdfResolver
{
    /// <summary>How many recently opened PDFs are offered at most, newest first.</summary>
    public const int MaxRecentSuggestions = 30;

    /// <summary>
    /// Every PDF path the evidence names, before asking which are in use:
    /// full paths in titles, paths on command lines, and recent documents.
    /// Each once, in that order.
    /// </summary>
    public static IReadOnlyList<string> CandidatePaths(OpenPdfEvidence evidence)
    {
        var paths = new List<string>();
        foreach (var window in evidence.Windows)
        {
            paths.AddRange(PathsInTitle(window.Title));
            paths.AddRange(PathsInCommandLine(window.CommandLine));
        }

        paths.AddRange(evidence.RecentDocuments.Select(r => SessionPaths.NormalizePdf(r.Path)).OfType<string>());
        return Distinct(paths);
    }

    /// <summary>
    /// Resolves the evidence into the review list: files judged open first,
    /// in the order found, then recently opened ones, newest first. Files in
    /// <paramref name="inUse"/> count as open.
    /// </summary>
    public static OpenPdfScan Resolve(OpenPdfEvidence evidence, IEnumerable<string> inUse)
    {
        var inUseSet = new HashSet<string>(inUse.Select(p => SessionPaths.NormalizePdf(p) ?? p), StringComparer.OrdinalIgnoreCase);
        var recents = evidence.RecentDocuments
            .Select(r => (Path: SessionPaths.NormalizePdf(r.Path), r.LastOpenedAt))
            .Where(r => r.Path is not null)
            .GroupBy(r => r.Path!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(r => r.LastOpenedAt), StringComparer.OrdinalIgnoreCase);

        var open = new List<PdfCandidate>();
        var openPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unmatched = new List<string>();

        void MarkOpen(string path, string reason)
        {
            if (openPaths.Add(path))
                open.Add(new PdfCandidate(path, true, reason, recents.TryGetValue(path, out var at) ? at : null));
        }

        // Every path any clue names, so a title's bare file name can be matched to one.
        var known = CandidatePaths(evidence);

        foreach (var window in evidence.Windows)
        {
            var commandLinePaths = PathsInCommandLine(window.CommandLine);
            var matchedInTitle = false;

            var titlePaths = PathsInTitle(window.Title);
            foreach (var path in titlePaths)
            {
                MarkOpen(path, $"Open in {window.AppName}");
                matchedInTitle = true;
            }

            // Longest file name first, so "Set - A-101.pdf" wins over "A-101.pdf" in the same title.
            // A name the title already spells out as a full path is settled: another folder's file of that name isn't open.
            var names = known
                .Select(SessionPaths.FileName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(n => !titlePaths.Any(p => string.Equals(SessionPaths.FileName(p), n, StringComparison.OrdinalIgnoreCase)))
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

            if (!matchedInTitle && GuessNameInTitle(window.Title) is { } guess)
                unmatched.Add($"{guess} ({window.AppName})");
        }

        foreach (var path in known.Where(inUseSet.Contains))
            MarkOpen(path, "In use by an open program");

        // Named on a command line but not confirmed: shown as a suggestion, as a recent file is.
        var suggestions = new List<PdfCandidate>();
        var suggested = new HashSet<string>(openPaths, StringComparer.OrdinalIgnoreCase);
        foreach (var window in evidence.Windows)
        {
            foreach (var path in PathsInCommandLine(window.CommandLine))
            {
                if (suggested.Add(path))
                    suggestions.Add(new PdfCandidate(path, false, $"{window.AppName} was started with it", recents.TryGetValue(path, out var at) ? at : null));
            }
        }

        foreach (var (path, at) in recents.OrderByDescending(r => r.Value))
        {
            if (suggestions.Count >= MaxRecentSuggestions)
                break;
            if (suggested.Add(path))
                suggestions.Add(new PdfCandidate(path, false, "Recently opened", at));
        }

        return new OpenPdfScan(open.Concat(suggestions).ToList(), unmatched.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Full drive or UNC paths to PDFs written out in a window title, as some viewers do.</summary>
    public static IReadOnlyList<string> PathsInTitle(string? title)
    {
        var paths = new List<string>();
        if (string.IsNullOrEmpty(title))
            return paths;

        foreach (var end in PdfEnds(title))
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

                if (SessionPaths.NormalizePdf(title[start..end]) is { } path)
                {
                    paths.Add(path);
                    break;
                }
            }
        }

        return Distinct(paths);
    }

    /// <summary>
    /// PDF paths among a command line's arguments, split as Windows splits
    /// them (double quotes group, backslashes are literal), including
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

            if (SessionPaths.NormalizePdf(candidate) is { } path)
                paths.Add(path);
        }

        return Distinct(paths);
    }

    /// <summary>The PDF file name a title seems to show, for the "couldn't match" line, or null if it shows none.</summary>
    public static string? GuessNameInTitle(string? title)
    {
        if (string.IsNullOrEmpty(title))
            return null;

        foreach (var end in PdfEnds(title))
        {
            // Back to the nearest thing a viewer puts around a name: " - ", a bracket, a quote, a bar, a separator.
            var start = end - ".pdf".Length;
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
            if (name.Length > ".pdf".Length)
                return name;
        }

        return null;
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    /// <summary>The index just past each ".pdf" in <paramref name="text"/> that ends a name (not "x.pdfs" or "x.pdf2").</summary>
    private static IEnumerable<int> PdfEnds(string text)
    {
        var from = 0;
        while (from < text.Length)
        {
            var at = text.IndexOf(".pdf", from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                yield break;

            var end = at + ".pdf".Length;
            if (end == text.Length || !char.IsLetterOrDigit(text[end]))
                yield return end;
            from = end;
        }
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
        bool Named(string path) => string.Equals(SessionPaths.FileName(path), name, StringComparison.OrdinalIgnoreCase);

        return commandLinePaths.FirstOrDefault(Named)
            ?? known.Where(Named).FirstOrDefault(inUse.Contains)
            ?? known.Where(Named).Where(recents.ContainsKey).OrderByDescending(p => recents[p]).FirstOrDefault()
            ?? known.First(Named);
    }

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
            // Left as it is: NormalizePdf refuses it if it isn't a path.
        }

        return path.Replace('/', '\\');
    }

    private static IReadOnlyList<string> Distinct(IEnumerable<string> paths)
        => paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>What <see cref="OpenPdfResolver"/> works from, gathered by the probe.</summary>
/// <param name="Windows">Visible top-level windows whose titles mention a PDF, with their program's name and command line.</param>
/// <param name="RecentDocuments">PDFs in Windows' Recent Items, with when each was last opened.</param>
public sealed record OpenPdfEvidence(IReadOnlyList<ViewerWindow> Windows, IReadOnlyList<RecentDocument> RecentDocuments)
{
    public static OpenPdfEvidence Empty { get; } = new(Array.Empty<ViewerWindow>(), Array.Empty<RecentDocument>());
}

/// <param name="Title">The window's title as Windows reports it.</param>
/// <param name="AppName">A readable name for the program that owns it ("Adobe Acrobat").</param>
/// <param name="CommandLine">That program's command line, or null if it couldn't be read.</param>
public sealed record ViewerWindow(string Title, string AppName, string? CommandLine);

/// <summary>A PDF from Windows' Recent Items.</summary>
public sealed record RecentDocument(string Path, DateTimeOffset LastOpenedAt);

/// <summary>One file in the review list.</summary>
/// <param name="Path">The PDF's full path.</param>
/// <param name="IsLikelyOpen">True when a clue says it is open now; these start ticked.</param>
/// <param name="Reason">Why it is listed, for the user: "Open in Adobe Acrobat", "Recently opened".</param>
/// <param name="LastOpenedAt">When Recent Items says it was last opened, if it does.</param>
public sealed record PdfCandidate(string Path, bool IsLikelyOpen, string Reason, DateTimeOffset? LastOpenedAt);

/// <summary>The result of a scan.</summary>
/// <param name="Candidates">Files judged open, then suggestions.</param>
/// <param name="UnmatchedTitles">PDF names seen in window titles that no known path matched, with the program, for the user to add by hand.</param>
public sealed record OpenPdfScan(IReadOnlyList<PdfCandidate> Candidates, IReadOnlyList<string> UnmatchedTitles)
{
    public static OpenPdfScan Empty { get; } = new(Array.Empty<PdfCandidate>(), Array.Empty<string>());

    /// <summary>Set by the probe when part of the scan failed or ran out of time, for one line under the list.</summary>
    public string? Warning { get; init; }
}
