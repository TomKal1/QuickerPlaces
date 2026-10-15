using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using QuickerPlaces.Services.Documents;

namespace QuickerPlaces.Services.Sessions.Suggestions;

/// <summary>
/// The stable identity of a suggested set of files (session suggestions plan
/// G9): the same files in any order or case give the same key, so a dismissed
/// suggestion stays dismissed when it is found again. UI-free and linked into
/// the test project.
/// </summary>
public static class SessionSuggestionKey
{
    public static string For(IEnumerable<string> paths)
    {
        var canonical = paths
            .Select(p => (DocumentPaths.Normalize(p) ?? p).ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", canonical)));
        return Convert.ToHexStringLower(hash)[..32];
    }
}
