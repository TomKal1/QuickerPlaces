using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using QuickerPlaces.Services.Documents;

namespace QuickerPlaces.Services.Sessions.Suggestions;

/// <summary>
/// Turns the strong pairs the two rules found into ranked, named suggestions
/// (session suggestions plan §6, §6.1, §7.1). Pure and UI-free, linked into
/// the test project. A file's identity is <c>DocumentPaths.Normalize(p) ?? p</c>
/// compared ignoring case, the rule <see cref="DocumentPaths.Same"/> uses.
/// </summary>
public static class SessionSuggester
{
    private static readonly Regex ProjectNumber = new(@"^\d{5,6}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The suggestions for <paramref name="pairs"/>, best first.
    /// <list type="bullet">
    /// <item>One relation per file pair holds the evidence of both rules (G7). "Strongest" always means: total evidence days, then longest overlap, then latest day, then the normalised keys.</item>
    /// <item>Groups grow greedily from the strongest unused relation; a file joins when its links to the group are at least <see cref="SessionSuggestionOptions.GroupJoinFraction"/> of the group's size (G7, §6 step 1). A group inside a larger kept one is dropped. A group always has at least 2 files by construction; <see cref="SessionSuggestionOptions.MinGroupFiles"/> is applied as a last filter on the files shown.</item>
    /// <item>A group inside a session is dropped; one sharing at least half its files with a session is an add suggestion for the session sharing most (the earlier on a tie), else a new-session one (G8).</item>
    /// <item>A dismissed key is dropped (G9).</item>
    /// <item>New-session suggestions never share a file: one sharing a file with a higher-ranked kept one is dropped. Add suggestions for the same session merge into one, ranked as its best member, and the merged key is checked for dismissal again (G18).</item>
    /// <item>Add suggestions come first, then group strength, then <see cref="SessionSuggestionOptions.MaxSuggestions"/> are kept (G10, G12).</item>
    /// <item>A new session is named from the project-number folder, else the common folder, else the commonest number, else the date, made unique among sessions and earlier suggestions (G11). The drive, or server and share, never names a session or counts as a project number.</item>
    /// </list>
    /// Each file is shown as <c>DocumentPaths.Normalize(path) ?? path</c>, spelled by its strongest relation. The reason's
    /// <c>LastTogether</c> is the latest rule A day among the group's relations; rule B days do not count towards it.
    /// Throws <see cref="ArgumentNullException"/> for a null argument; null elements of <paramref name="pairs"/> and
    /// <paramref name="sessions"/> are skipped. Empty input and sessions without files give an empty or smaller result.
    /// </summary>
    public static IReadOnlyList<SessionSuggestion> Suggest(
        IEnumerable<StrongPair> pairs,
        IReadOnlyList<SessionSnapshot> sessions,
        Func<string, bool> isDismissed,
        SessionSuggestionOptions options,
        DateOnly today,
        Func<IReadOnlyList<string>, bool>? areDismissed = null)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(isDismissed);
        ArgumentNullException.ThrowIfNull(options);

        var cmp = StringComparer.OrdinalIgnoreCase;
        var keys = new List<string>();
        var relations = Merge(pairs, keys);
        if (relations.Count == 0)
            return [];

        // Strongest first; a relation's rank is its place here, so "strongest link" is the lowest rank.
        relations.Sort((x, y) => CompareStrength(x, y, keys));
        var adjacent = new List<(int Other, int Rank)>[keys.Count];
        var spelling = new string?[keys.Count];
        for (var rank = 0; rank < relations.Count; rank++)
        {
            var r = relations[rank];
            (adjacent[r.A] ??= []).Add((r.B, rank));
            (adjacent[r.B] ??= []).Add((r.A, rank));
            spelling[r.A] ??= Key(r.SpellingA);
            spelling[r.B] ??= Key(r.SpellingB);
        }

        var groups = Subsets(Grow(relations, adjacent, keys, options));

        // A null session keeps its place (the target is found by index) but holds no files.
        var sessionKeys = sessions
            .Select(s => (s?.Files ?? []).Where(f => !string.IsNullOrEmpty(f)).Select(Key).ToHashSet(cmp))
            .ToList();
        var sessionsOf = new Dictionary<string, List<int>>(cmp);
        for (var i = 0; i < sessionKeys.Count; i++)
            foreach (var key in sessionKeys[i])
                (sessionsOf.TryGetValue(key, out var list) ? list : sessionsOf[key] = []).Add(i);

        var found = new List<Candidate>();
        foreach (var group in groups)
        {
            var members = group.Members.OrderBy(m => keys[m], cmp).ToList();
            var files = members.Select(m => spelling[m]!).ToList();

            var shared = new Dictionary<int, int>();
            foreach (var m in members)
                if (sessionsOf.TryGetValue(keys[m], out var inSessions))
                    foreach (var s in inSessions)
                        shared[s] = shared.GetValueOrDefault(s) + 1;

            if (shared.Any(kv => kv.Value == members.Count))
                continue;

            var key = SessionSuggestionKey.For(files);
            if (isDismissed(key) || areDismissed?.Invoke(files) == true)
                continue;

            var target = shared.Count == 0 ? -1 : shared.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
            var isAdd = target >= 0 && shared[target] * 2 >= members.Count;
            var newFiles = isAdd
                ? files.Where(f => !sessionKeys[target].Contains(Key(f))).ToList()
                : files;

            found.Add(new Candidate(key, files, newFiles, isAdd ? sessions[target].Id : null, ReasonFor(group), group));
        }

        var ranked = found
            .OrderByDescending(c => c.TargetSessionId is not null)
            .ThenByDescending(c => c.Group.Days)
            .ThenByDescending(c => c.Group.Overlap)
            .ThenByDescending(c => c.Group.LastDay)
            .ThenBy(c => c.Key, StringComparer.Ordinal)
            .ToList();

        // Add suggestions for one session become one, at the place of the best of them (G18).
        var merged = new List<Candidate>();
        var addAt = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        foreach (var c in ranked)
        {
            if (c.TargetSessionId is null)
                merged.Add(c);
            else if (addAt.TryGetValue(c.TargetSessionId, out var first))
                first.Absorb(c);
            else
                merged.Add(addAt[c.TargetSessionId] = c);
        }

        // New-session suggestions never share a file: the higher-ranked one keeps it (G18).
        var claimed = new HashSet<string>(cmp);
        var shown = new List<Candidate>();
        foreach (var c in merged)
        {
            if (c.IsMerged && (isDismissed(c.Key) || areDismissed?.Invoke(c.Files) == true))
                continue;
            // A card MinGroupFiles hides must not claim files from a visible one.
            if (c.Files.Count < options.MinGroupFiles)
                continue;
            if (c.TargetSessionId is null)
            {
                if (c.Files.Any(f => claimed.Contains(Key(f))))
                    continue;
                foreach (var f in c.Files)
                    claimed.Add(Key(f));
            }

            shown.Add(c);
        }

        // Names are given in the final order, so a clash always goes to the later suggestion.
        var taken = new HashSet<string>(sessions.Select(s => (s?.Name ?? "").Trim()), cmp);
        var result = new List<SessionSuggestion>();
        foreach (var c in shown.Take(Math.Max(0, options.MaxSuggestions)))
        {
            var name = c.TargetSessionId is null ? Unique(NameFor(c.Files, today), taken) : null;
            result.Add(new SessionSuggestion(c.Key, c.Files, c.NewFiles, c.TargetSessionId, name, c.Reason));
        }

        return result;
    }

    private static string Key(string path) => DocumentPaths.Normalize(path) ?? path;

    // ---------------------------------------------------------------
    // Relations
    // ---------------------------------------------------------------

    private sealed class Relation(int a, int b, string spellingA, string spellingB)
    {
        /// <summary>The two file ids, <see cref="A"/> the one whose key sorts first.</summary>
        public int A { get; } = a;
        public int B { get; } = b;
        public string SpellingA { get; set; } = spellingA;
        public string SpellingB { get; set; } = spellingB;

        public int UsedDays { get; set; }
        public DateOnly? UsedLast { get; set; }
        public bool HasUsed { get; set; }
        public bool HasOpen { get; set; }
        public int OpenDays { get; set; }
        public TimeSpan Overlap { get; set; }
        public DateOnly? OverlapOn { get; set; }
        public DateOnly? OpenLast { get; set; }

        public int Days => UsedDays + OpenDays;
        public DateOnly LastDay => UsedLast is { } u && (OpenLast is not { } o || u >= o) ? u : OpenLast ?? default;
    }

    /// <summary>One relation per file pair, holding what each rule found (G7).</summary>
    private static List<Relation> Merge(IEnumerable<StrongPair> pairs, List<string> keys)
    {
        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var slots = new Dictionary<long, Relation>();
        var relations = new List<Relation>();

        int Id(string key)
        {
            if (!ids.TryGetValue(key, out var id))
            {
                id = keys.Count;
                ids[key] = id;
                keys.Add(key);
            }
            return id;
        }

        foreach (var pair in pairs)
        {
            if (pair is null || string.IsNullOrEmpty(pair.First) || string.IsNullOrEmpty(pair.Second))
                continue;

            var first = Key(pair.First);
            var second = Key(pair.Second);
            if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
                continue;

            // Order by key, not by id, so the same pair merges to the same relation for any input order.
            var swap = StringComparer.OrdinalIgnoreCase.Compare(first, second) > 0;
            var a = Id(swap ? second : first);
            var b = Id(swap ? first : second);
            var slot = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
            if (!slots.TryGetValue(slot, out var relation))
            {
                relation = new Relation(a, b, swap ? pair.Second : pair.First, swap ? pair.First : pair.Second);
                slots[slot] = relation;
                relations.Add(relation);
            }
            else
            {
                // Different spellings of the same file: the same one wins for any input order.
                var spellA = swap ? pair.Second : pair.First;
                var spellB = swap ? pair.First : pair.Second;
                if (string.CompareOrdinal(spellA, relation.SpellingA) < 0) relation.SpellingA = spellA;
                if (string.CompareOrdinal(spellB, relation.SpellingB) < 0) relation.SpellingB = spellB;
            }

            if (pair.Rule == SuggestionRule.UsedTogether)
            {
                relation.UsedDays = Math.Max(relation.UsedDays, pair.DaysTogether);
                relation.UsedLast = Later(relation.UsedLast, pair.LastDay);
                relation.HasUsed = true;
            }
            else
            {
                relation.HasOpen = true;
                relation.OpenDays = Math.Max(relation.OpenDays, pair.DaysTogether);
                relation.OpenLast = Later(relation.OpenLast, pair.LastDay);
                var on = pair.LongestOverlapOn ?? pair.LastDay;
                if (pair.LongestOverlap > relation.Overlap
                    || (pair.LongestOverlap == relation.Overlap && (relation.OverlapOn is not { } had || on > had)))
                {
                    relation.Overlap = pair.LongestOverlap;
                    relation.OverlapOn = on;
                }
            }
        }

        return relations;
    }

    private static DateOnly Later(DateOnly? current, DateOnly day) => current is { } c && c >= day ? c : day;

    private static int CompareStrength(Relation x, Relation y, List<string> keys)
    {
        var c = y.Days.CompareTo(x.Days);
        if (c != 0) return c;
        c = y.Overlap.CompareTo(x.Overlap);
        if (c != 0) return c;
        c = y.LastDay.CompareTo(x.LastDay);
        if (c != 0) return c;
        c = StringComparer.OrdinalIgnoreCase.Compare(keys[x.A], keys[y.A]);
        return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(keys[x.B], keys[y.B]);
    }

    // ---------------------------------------------------------------
    // Groups
    // ---------------------------------------------------------------

    private sealed class Group(List<int> members)
    {
        public List<int> Members { get; } = members;
        public int Days { get; set; }
        public TimeSpan Overlap { get; set; }
        public DateOnly LastDay { get; set; }
        public List<Relation> Inside { get; } = [];
    }

    /// <summary>Greedy growth from the strongest unused relation until every relation sits in some group (§6 step 1).</summary>
    private static List<Group> Grow(List<Relation> relations, List<(int Other, int Rank)>[] adjacent, List<string> keys, SessionSuggestionOptions options)
    {
        var used = new bool[relations.Count];
        var inGroup = new int[keys.Count];
        var stamp = 0;
        var groups = new List<Group>();
        var maxFiles = Math.Max(2, options.MaxGroupFiles);

        for (var seed = 0; seed < relations.Count; seed++)
        {
            if (used[seed])
                continue;

            stamp++;
            var members = new List<int> { relations[seed].A, relations[seed].B };
            // Candidate -> (links to the members, rank of its strongest link).
            var links = new Dictionary<int, (int Count, int Best)>();
            inGroup[relations[seed].A] = stamp;
            inGroup[relations[seed].B] = stamp;
            foreach (var m in members)
                AddLinks(m);

            while (members.Count < maxFiles && links.Count > 0)
            {
                var pick = -1;
                (int Count, int Best) best = default;
                foreach (var (candidate, link) in links)
                {
                    if (link.Count / (double)members.Count < options.GroupJoinFraction)
                        continue;
                    // Most links, then the strongest single link. Ranks are unique per relation, so this is already total.
                    if (pick < 0 || link.Count > best.Count || (link.Count == best.Count && link.Best < best.Best))
                    {
                        pick = candidate;
                        best = link;
                    }
                }

                if (pick < 0)
                    break;

                members.Add(pick);
                inGroup[pick] = stamp;
                links.Remove(pick);
                AddLinks(pick);
            }

            var group = new Group(members);
            foreach (var m in members)
            {
                foreach (var (other, rank) in adjacent[m])
                {
                    if (inGroup[other] != stamp)
                        continue;
                    if (relations[rank].A != m)
                        continue;
                    used[rank] = true;
                    group.Inside.Add(relations[rank]);
                }
            }

            foreach (var r in group.Inside)
            {
                group.Days = Math.Max(group.Days, r.Days);
                group.Overlap = r.Overlap > group.Overlap ? r.Overlap : group.Overlap;
                group.LastDay = r.LastDay > group.LastDay ? r.LastDay : group.LastDay;
            }

            groups.Add(group);

            void AddLinks(int member)
            {
                foreach (var (other, rank) in adjacent[member])
                {
                    if (inGroup[other] == stamp)
                        continue;
                    links[other] = links.TryGetValue(other, out var have)
                        ? (have.Count + 1, Math.Min(have.Best, rank))
                        : (1, rank);
                }
            }
        }

        return groups;
    }

    /// <summary>Drops a group whose files all sit in a larger group already kept, equal sets included.</summary>
    private static List<Group> Subsets(List<Group> groups)
    {
        var kept = new List<Group>();
        var keptSets = new List<HashSet<int>>();
        var byFile = new Dictionary<int, List<int>>();

        // Larger first; the stable sort keeps creation order, which is strength order, among equals.
        foreach (var group in groups.Select((g, i) => (g, i)).OrderByDescending(x => x.g.Members.Count).ThenBy(x => x.i).Select(x => x.g))
        {
            // Any superset holds the group's rarest file, so only that file's groups need checking.
            var rarest = group.Members.OrderBy(m => byFile.TryGetValue(m, out var l) ? l.Count : 0).First();
            var covered = byFile.TryGetValue(rarest, out var candidates)
                && candidates.Any(k => group.Members.All(keptSets[k].Contains));
            if (covered)
                continue;

            var index = kept.Count;
            kept.Add(group);
            keptSets.Add(group.Members.ToHashSet());
            foreach (var m in group.Members)
                (byFile.TryGetValue(m, out var list) ? list : byFile[m] = []).Add(index);
        }

        return kept;
    }

    private static SuggestionReason ReasonFor(Group group)
    {
        var days = 0;
        DateOnly? last = null;
        TimeSpan? overlap = null;
        DateOnly? overlapOn = null;
        foreach (var r in group.Inside)
        {
            if (r.HasUsed)
            {
                days = Math.Max(days, r.UsedDays);
                last = Later(last, r.UsedLast!.Value);
            }

            if (r.HasOpen && (overlap is null || r.Overlap > overlap
                || (r.Overlap == overlap && r.OverlapOn is { } on && (overlapOn is not { } had || on > had))))
            {
                overlap = r.Overlap;
                overlapOn = r.OverlapOn;
            }
        }

        return new SuggestionReason(days, last, overlap, overlapOn);
    }

    private sealed class Candidate(
        string key,
        List<string> files,
        List<string> newFiles,
        string? targetSessionId,
        SuggestionReason reason,
        Group group)
    {
        public string Key { get; private set; } = key;
        public List<string> Files { get; private set; } = files;
        public List<string> NewFiles { get; private set; } = newFiles;
        public string? TargetSessionId { get; } = targetSessionId;
        public SuggestionReason Reason { get; private set; } = reason;
        public Group Group { get; } = group;
        public bool IsMerged { get; private set; }

        /// <summary>Folds in another add suggestion for the same session (G18); this one keeps its rank.</summary>
        public void Absorb(Candidate other)
        {
            Files = Union(Files, other.Files);
            NewFiles = Union(NewFiles, other.NewFiles);
            Key = SessionSuggestionKey.For(Files);
            IsMerged = true;

            DateOnly? last = Reason.LastTogether is { } a && (other.Reason.LastTogether is not { } b || a >= b) ? a : other.Reason.LastTogether;
            var (overlap, overlapOn) = Reason.LongestOpenTogether is not { } mine
                || (other.Reason.LongestOpenTogether is { } theirs
                    && (theirs > mine || (theirs == mine && other.Reason.LongestOpenOn > Reason.LongestOpenOn)))
                ? (other.Reason.LongestOpenTogether, other.Reason.LongestOpenOn)
                : (Reason.LongestOpenTogether, Reason.LongestOpenOn);
            Reason = new SuggestionReason(Math.Max(Reason.DaysTogether, other.Reason.DaysTogether), last, overlap, overlapOn);
        }

        private static List<string> Union(List<string> x, List<string> y) =>
            x.Concat(y)
                .GroupBy(SessionSuggester.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(SessionSuggester.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    // ---------------------------------------------------------------
    // Names (G11)
    // ---------------------------------------------------------------

    private static string NameFor(IReadOnlyList<string> files, DateOnly today)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        var paths = files.Select(Segments).ToList();
        var folders = paths.Select(p => p.Folders).ToList();

        // Files on different drives or shares share no folder, whatever the folders below are called.
        var common = new List<string>(folders[0]);
        if (paths.Any(p => !cmp.Equals(p.Head, paths[0].Head)))
            common.Clear();
        foreach (var folder in folders.Skip(1))
        {
            var length = 0;
            while (length < common.Count && length < folder.Count && cmp.Equals(common[length], folder[length]))
                length++;
            common.RemoveRange(length, common.Count - length);
        }

        // A project number anywhere in the shared path names the job better than a folder under it.
        var numbered = common.LastOrDefault(s => ProjectNumber.IsMatch(s));
        if (numbered is not null)
            return Fit(numbered);

        // The deepest shared folder; the drive or server and share are the head, never a folder.
        if (common.Count > 0)
            return Fit(common[^1]);

        // No shared folder: the project number most of the files sit under.
        var counts = new Dictionary<string, int>(cmp);
        foreach (var folder in folders)
            foreach (var segment in folder.Where(s => ProjectNumber.IsMatch(s)).Distinct(cmp))
                counts[segment] = counts.GetValueOrDefault(segment) + 1;
        if (counts.Count > 0)
            return Fit(counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, cmp).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key);

        return Fit(string.Create(CultureInfo.InvariantCulture, $"Suggested session {today:d MMM}"));
    }

    /// <summary>
    /// A file's folders without the file name, apart from its head: "C:" for a drive path,
    /// "server\share" for a UNC one, so neither can be named or read as a project number.
    /// </summary>
    private static (string Head, List<string> Folders) Segments(string path)
    {
        var key = Key(path);
        var parts = key.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        var unc = key.StartsWith(@"\\", StringComparison.Ordinal) || key.StartsWith("//", StringComparison.Ordinal);
        var headLength = Math.Min(parts.Length, unc ? 2 : key.Length > 1 && key[1] == ':' ? 1 : 0);
        var head = string.Join('\\', parts.Take(headLength));
        var folders = parts.Skip(headLength).Take(Math.Max(0, parts.Length - headLength - 1)).ToList();
        return (head, folders);
    }

    private static string Fit(string name)
    {
        name = name.Trim();
        return name.Length > SessionStore.MaxNameLength ? name[..SessionStore.MaxNameLength].TrimEnd() : name;
    }

    /// <summary>The name, or the name with " (2)", " (3)", … until no session or earlier suggestion has it.</summary>
    private static string Unique(string name, HashSet<string> taken)
    {
        var candidate = name;
        for (var n = 2; !taken.Add(candidate); n++)
        {
            var suffix = $" ({n.ToString(CultureInfo.InvariantCulture)})";
            candidate = Fit(name[..Math.Min(name.Length, SessionStore.MaxNameLength - suffix.Length)]) + suffix;
        }

        return candidate;
    }
}
