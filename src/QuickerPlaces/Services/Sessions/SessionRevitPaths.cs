using System;
using System.Collections.Generic;
using System.Linq;
using QuickerPlaces.Services.Documents;
using QuickerPlaces.Services.Revit;

namespace QuickerPlaces.Services.Sessions;

/// <summary>
/// Saving open files as a session: Revit holds the user's local, not the
/// central, so an open local is listed as its central instead. Reopening the
/// session then makes a fresh local (roadmap §4.21) rather than reopening
/// yesterday's. The local stays when its central can't be found or no longer
/// reads as a central. Only files judged open are swapped; suggestions are
/// left as they are, so a scan doesn't read every recent Revit file.
///
/// UI-free and linked into the test project.
/// </summary>
public static class SessionRevitPaths
{
    /// <summary>
    /// <paramref name="scan"/> with each open Revit local replaced by its
    /// central, in the local's place; a central listed twice is kept once, at
    /// its first place, ticked if either was. Reads files: call it off the UI thread.
    /// </summary>
    public static OpenDocumentScan PreferCentral(OpenDocumentScan scan, RevitReleaseCache cache, Func<string, bool> fileExists)
    {
        var swapped = scan.Candidates.Select(c => c.IsLikelyOpen ? Swap(c, cache, fileExists) : c).ToList();
        if (swapped.SequenceEqual(scan.Candidates))
            return scan;

        var merged = new List<DocumentCandidate>();
        foreach (var candidate in swapped)
        {
            var index = merged.FindIndex(m => DocumentPaths.Same(m.Path, candidate.Path));
            if (index < 0)
                merged.Add(candidate);
            else if (candidate.IsLikelyOpen && !merged[index].IsLikelyOpen)
                merged[index] = candidate;
        }

        return scan with { Candidates = merged };
    }

    private static DocumentCandidate Swap(DocumentCandidate candidate, RevitReleaseCache cache, Func<string, bool> fileExists)
    {
        if (DocumentKinds.FromPath(candidate.Path) != DocumentKind.Revit)
            return candidate;

        // A local, or a file that reads as a central saved somewhere else: a new local
        // Revit hasn't saved yet (CreateNewLocal copies the central, and the header says
        // local only after the first save), or a plain copy of the central.
        var info = Read(cache, candidate.Path);
        if (info is not { Problem: RevitFileProblem.None, Worksharing: RevitWorksharing.Local or RevitWorksharing.Central, CentralModelPath: { } recorded } ||
            DocumentPaths.Normalize(recorded) is not { } central || DocumentPaths.Same(central, candidate.Path) || !fileExists(central) ||
            Read(cache, central) is not { Problem: RevitFileProblem.None, Worksharing: RevitWorksharing.Central })
            return candidate;

        return candidate with { Path = central, Reason = $"Central of open local {DocumentPaths.FileName(candidate.Path)}" };
    }

    private static RevitFileInfo? Read(RevitReleaseCache cache, string path) =>
        cache.Refresh(new[] { path }).TryGetValue(path, out var info) ? info : null;
}
