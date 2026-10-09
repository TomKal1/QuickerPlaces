using System.Collections.Generic;

namespace QuickerPlaces.RevitHandler.Core
{
    public static class DialogKinds
    {
        public const string TaskDialog = "taskDialog";
        public const string MessageBox = "messageBox";
        public const string DialogBox = "dialogBox";
    }

    /// <summary>
    /// Collects the "dialogs" list of one result and decides which dialogs to answer.
    /// The Revit layer feeds it each <c>DialogBoxShowing</c> event and calls <c>OverrideResult</c> only for a non-null answer.
    /// </summary>
    public sealed class DialogRecorder
    {
        private const int MaxMessageLength = 500;

        private readonly DialogAllowlist _allowlist;
        private readonly List<DialogRecord> _records = new List<DialogRecord>();

        public DialogRecorder(DialogAllowlist allowlist) => _allowlist = allowlist;

        public IReadOnlyList<DialogRecord> Records => _records;

        /// <summary>Records one dialog. Returns the answer to give, or null to leave the dialog to the user.</summary>
        public int? OnDialog(string? dialogId, string kind, string? message)
        {
            bool answered = _allowlist.TryGetAnswer(dialogId, out int answer);

            // The message is only reported for task dialogs and message boxes, at most 500 characters.
            string? reported = kind == DialogKinds.DialogBox ? null : message;
            if (reported != null && reported.Length > MaxMessageLength) reported = reported.Substring(0, MaxMessageLength);

            _records.Add(new DialogRecord
            {
                DialogId = string.IsNullOrEmpty(dialogId) ? null : dialogId,
                Kind = kind,
                Message = reported,
                Answered = answered,
                Answer = answered ? answer : (int?)null,
            });
            return answered ? answer : (int?)null;
        }
    }
}
