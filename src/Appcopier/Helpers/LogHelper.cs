using System;
using System.Windows.Forms;

namespace Appcopier
{
    internal class LogHelper
    {
        private static readonly LogHelper instance = new LogHelper();
        private static RichTextBox target = null;

        private LogHelper()
        { }  // Private constructor to prevent external instantiation

        // Logger to target rtbLog
        public void SetTarget(RichTextBox richText)
        {
            target = richText;
        }

        public void Log(string format, params object[] args)
        {
            format += "\r\n";

            try
            {
                if (target != null)
                {
                    if (target.InvokeRequired)
                    {
                        target.Invoke(new Action(() => AppendLog(format, args)));
                    }
                    else
                    {
                        AppendLog(format, args);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error in log: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs an already-composed message, with no <see cref="string.Format"/> pass over it.
        /// </summary>
        /// <remarks>
        /// Use this for anything whose text is data rather than a template - result reason strings,
        /// registry paths, exception messages. Log(string, params object[]) treats its first
        /// argument as a format string, so a single brace in the text throws FormatException inside
        /// AppendLog, which routes the line to Console.WriteLine - invisible in a WinForms app.
        /// The message is not lost loudly; it is lost silently, which is worse.
        /// </remarks>
        public void LogMessage(string message)
        {
            // "{0}" as the template and the caller's text as an ARGUMENT: string.Format then has
            // nothing to parse in the untrusted half.
            Log("{0}", message ?? string.Empty);
        }

        private void AppendLog(string format, params object[] args)
        {
            try
            {
                target.AppendText(string.Format(format, args));
            }
            catch (FormatException ex)
            {
                LogError($"Exception in log: {ex.Message}");
                LogError($"Exception: {format}");
            }
            catch (Exception ex)
            {
                LogError($"Error in Log method: {ex.Message}");
            }
        }

        private void LogError(string message)
        {
            Console.WriteLine($"Error: {message}");
        }

        public void ClearLog()
        {
            try
            {
                if (target.InvokeRequired)
                {
                    target.Invoke(new Action(() => target.Clear()));
                }
                else
                {
                    target.Clear();
                }
            }
            catch { }
        }

        public static LogHelper Instance
        {
            get => instance;
        }
    }
}