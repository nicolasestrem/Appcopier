using System.Collections.Generic;
using System.Text;

namespace Appcopier
{
    /// <summary>
    /// Composes backup_log.txt.
    /// </summary>
    /// <remarks>
    /// v1 listed what was SELECTED, which is the same category of lie as the old success dialog: it
    /// described an intention as though it were an outcome. v2 records what happened per module.
    ///
    /// Safe to change format: the only reader (RestPageView) dumps the file verbatim into a textbox
    /// and never parses it, and the restore SET is chosen before that view is shown. The version
    /// header is cheap insurance in case anything ever does parse it.
    /// </remarks>
    internal static class BackupLog
    {
        internal const string VersionHeader = "# Appcopier backup log v2";

        internal static string Compose(IReadOnlyList<BackupBase> modules,
                                       IReadOnlyList<ModuleResult> results,
                                       string when)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine(VersionHeader);
            sb.AppendLine("# " + when);
            sb.AppendLine();

            int count = modules == null ? 0 : modules.Count;

            for (int i = 0; i < count; i++)
            {
                BackupBase module = modules[i];

                // Counts can diverge if a module threw before producing a result. Report that
                // rather than indexing past the end.
                ModuleResult result = (results != null && i < results.Count) ? results[i] : null;

                if (result == null)
                {
                    sb.AppendLine(string.Format("{0} ({1})  UNKNOWN  no result was recorded",
                        module.Title, module.GetType().Name));
                    continue;
                }

                sb.AppendLine(string.Format("{0} ({1})  {2}  {3}",
                    module.Title, module.GetType().Name, Label(result.State), result.Reason));
            }

            return sb.ToString();
        }

        private static string Label(ResultState state)
        {
            switch (state)
            {
                case ResultState.Succeeded: return "OK";
                case ResultState.Skipped: return "SKIPPED";
                default: return "FAILED";
            }
        }
    }
}
