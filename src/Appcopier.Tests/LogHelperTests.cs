using Appcopier;
using System.Windows.Forms;
using Xunit;

namespace Appcopier.Tests
{
    public class LogHelperTests
    {
        // A RichTextBox with a forced handle so InvokeRequired answers honestly and AppendText works.
        private static RichTextBox NewTarget()
        {
            RichTextBox box = new RichTextBox();
            System.IntPtr unused = box.Handle;   // force handle creation
            return box;
        }

        [Fact]
        public void LogMessage_TextContainingBraces_ReachesTheTarget()
        {
            RichTextBox box = NewTarget();
            LogHelper.Instance.SetTarget(box);

            // A real reason string: a registry path plus exception text with braces in it.
            const string reason = @"could not export HKEY_CURRENT_USER\Software\{4D36E96B}: access denied";
            LogHelper.Instance.LogMessage(reason);

            Assert.Contains("4D36E96B", box.Text);
            Assert.Contains("access denied", box.Text);
        }

        [Fact]
        public void LogMessage_UnmatchedBrace_DoesNotThrowAndStillLogs()
        {
            RichTextBox box = NewTarget();
            LogHelper.Instance.SetTarget(box);

            LogHelper.Instance.LogMessage("failed on {0 unbalanced");

            Assert.Contains("unbalanced", box.Text);
        }

        [Fact]
        public void Log_WithFormatArguments_StillFormats()
        {
            RichTextBox box = NewTarget();
            LogHelper.Instance.SetTarget(box);

            LogHelper.Instance.Log("exported {0} keys", 3);

            Assert.Contains("exported 3 keys", box.Text);
        }
    }
}
