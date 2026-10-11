using System.Collections.Generic;
using QuickerPlaces.RevitHandler.Core;
using Xunit;

namespace QuickerPlaces.RevitHandler.Tests
{
    public class DialogTests
    {
        [Fact]
        public void AllowlistShipsEmpty()
        {
            DialogAllowlist list = DialogAllowlist.Default;

            Assert.Equal(0, list.Count);
            Assert.False(list.TryGetAnswer("TaskDialog_Missing_Third_Party_Updater", out _));
        }

        [Fact]
        public void EmptyAllowlistRecordsEverythingAndAnswersNothing()
        {
            var recorder = new DialogRecorder(DialogAllowlist.Default);

            int? answer = recorder.OnDialog("TaskDialog_Missing_Third_Party_Updater", DialogKinds.TaskDialog, "The model uses an updater.");

            Assert.Null(answer);
            DialogRecord record = Assert.Single(recorder.Records);
            Assert.Equal("TaskDialog_Missing_Third_Party_Updater", record.DialogId);
            Assert.Equal("taskDialog", record.Kind);
            Assert.Equal("The model uses an updater.", record.Message);
            Assert.False(record.Answered);
            Assert.Null(record.Answer);
        }

        [Fact]
        public void AllowlistedDialogIsAnsweredAndRecorded()
        {
            var list = new DialogAllowlist(new[] { new KeyValuePair<string, int>("TaskDialog_Missing_Third_Party_Updater", 1001) });
            var recorder = new DialogRecorder(list);

            Assert.Equal(1001, recorder.OnDialog("TaskDialog_Missing_Third_Party_Updater", DialogKinds.TaskDialog, "m"));
            Assert.Null(recorder.OnDialog("TaskDialog_Other", DialogKinds.TaskDialog, "m"));
            Assert.Null(recorder.OnDialog("task_dialog_missing_third_party_updater", DialogKinds.TaskDialog, "m")); // exact id only

            Assert.True(recorder.Records[0].Answered);
            Assert.Equal(1001, recorder.Records[0].Answer);
            Assert.False(recorder.Records[1].Answered);
            Assert.False(recorder.Records[2].Answered);
        }

        [Fact]
        public void MatchingNeverUsesTheMessageText()
        {
            var list = new DialogAllowlist(new[] { new KeyValuePair<string, int>("TaskDialog_Missing_Third_Party_Updater", 1001) });
            var recorder = new DialogRecorder(list);

            Assert.Null(recorder.OnDialog(null, DialogKinds.TaskDialog, "TaskDialog_Missing_Third_Party_Updater"));
            Assert.Null(recorder.OnDialog("", DialogKinds.MessageBox, "TaskDialog_Missing_Third_Party_Updater"));
            Assert.Null(recorder.Records[0].DialogId);
            Assert.Null(recorder.Records[1].DialogId);
        }

        [Fact]
        public void MessagesAreCutAt500CharactersAndOmittedForOtherDialogs()
        {
            var recorder = new DialogRecorder(DialogAllowlist.Default);

            recorder.OnDialog("a", DialogKinds.MessageBox, new string('x', 900));
            recorder.OnDialog("b", DialogKinds.DialogBox, "not reported");
            recorder.OnDialog("c", DialogKinds.TaskDialog, null);

            Assert.Equal(500, recorder.Records[0].Message!.Length);
            Assert.Equal("messageBox", recorder.Records[0].Kind);
            Assert.Null(recorder.Records[1].Message);
            Assert.Equal("dialogBox", recorder.Records[1].Kind);
            Assert.Null(recorder.Records[2].Message);
        }
    }
}
