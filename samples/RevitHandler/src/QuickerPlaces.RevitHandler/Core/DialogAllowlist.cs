using System.Collections.Generic;

namespace QuickerPlaces.RevitHandler.Core
{
    /// <summary>
    /// The only dialogs this handler answers: Revit <c>DialogId</c> to the value passed to <c>OverrideResult</c>.
    /// Matching is by DialogId only, never by message text (the text depends on Revit's language).
    ///
    /// Rules (protocol, "Dialogs inside Revit"):
    ///  - An entry must come from observing that dialog in a real open in that release. The "dialogs" list in a
    ///    result file is how evidence is collected.
    ///  - No entry may answer with a choice that saves, synchronises, relinquishes, detaches, upgrades, deletes or closes.
    /// </summary>
    public sealed class DialogAllowlist
    {
        private readonly Dictionary<string, int> _answers;

        public DialogAllowlist(IEnumerable<KeyValuePair<string, int>> entries)
        {
            _answers = new Dictionary<string, int>(System.StringComparer.Ordinal);
            foreach (var entry in entries) _answers[entry.Key] = entry.Value;
        }

        /// <summary>What the sample ships with: no entries. Every dialog is recorded and left to the user.</summary>
        public static DialogAllowlist Default => new DialogAllowlist(DefaultEntries());

        public int Count => _answers.Count;

        public bool TryGetAnswer(string? dialogId, out int answer)
        {
            answer = 0;
            return !string.IsNullOrEmpty(dialogId) && _answers.TryGetValue(dialogId!, out answer);
        }

        private static IEnumerable<KeyValuePair<string, int>> DefaultEntries()
        {
            // The list ships empty.
            //
            // First candidate, to be confirmed by observation in each Revit release before it is enabled:
            // the "missing third-party updater" dialog, answered 1001 ("Continue working with the file").
            //
            // yield return new KeyValuePair<string, int>("TaskDialog_Missing_Third_Party_Updater", 1001);
            yield break;
        }
    }
}
