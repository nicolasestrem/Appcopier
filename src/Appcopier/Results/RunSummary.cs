using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Appcopier
{
    /// <summary>
    /// The two words needed to describe a run: a past-tense verb for the success headline and a
    /// noun for the did-not-run message. One string cannot serve both - "Restored" reads fine in
    /// "Restored 3 items" but produces "Restored did not run." where "Restore did not run." is needed.
    /// </summary>
    internal sealed class RunVerb
    {
        public string Past { get; }   // "Backed up" / "Restored"
        public string Noun { get; }   // "Backup"    / "Restore"

        private RunVerb(string past, string noun) { Past = past; Noun = noun; }

        public static readonly RunVerb Backup = new RunVerb("Backed up", "Backup");
        public static readonly RunVerb Restore = new RunVerb("Restored", "Restore");
    }

    internal enum RunState
    {
        Problems,
        Done,
        NothingDone,
        DidNotRun
    }

    /// <summary>
    /// What to tell the user after a whole backup or restore run.
    /// </summary>
    /// <remarks>
    /// Four states where the app previously had one message. Kept out of the view so it can be
    /// tested: the wording IS the deliverable of this phase, and asserting on it in xUnit is the
    /// only way it stays honest as modules change.
    /// </remarks>
    internal sealed class RunSummary
    {
        public RunState State { get; private set; }
        public string Headline { get; private set; }
        public string Detail { get; private set; }

        public MessageBoxIcon Icon
            => State == RunState.Problems ? MessageBoxIcon.Warning : MessageBoxIcon.Information;

        internal static RunSummary For(IReadOnlyList<ModuleResult> results, bool ran, RunVerb verb)
        {
            if (!ran)
            {
                return new RunSummary
                {
                    State = RunState.DidNotRun,
                    Headline = verb.Noun + " did not run.",
                    Detail = verb.Noun + " did not run because the backup folder could not be found."
                };
            }

            ModuleResult[] all = (results ?? new List<ModuleResult>()).Where(r => r != null).ToArray();

            ModuleResult[] failed = all.Where(r => r.State == ResultState.Failed).ToArray();
            ModuleResult[] ok = all.Where(r => r.State == ResultState.Succeeded).ToArray();
            ModuleResult[] skipped = all.Where(r => r.State == ResultState.Skipped).ToArray();

            if (failed.Length > 0)
            {
                return new RunSummary
                {
                    State = RunState.Problems,
                    Headline = string.Format("{0} of {1} items had problems.", failed.Length, all.Length),
                    Detail = string.Join("\r\n", failed.Select(r => "  - " + r.Reason))
                };
            }

            if (ok.Length == 0)
            {
                return new RunSummary
                {
                    State = RunState.NothingDone,
                    Headline = "Nothing was backed up.",
                    Detail = "None of the selected items were present on this system."
                };
            }

            // Skipped items are reported, but never as a problem and never added to a failure
            // count. Absences are the normal state of a real machine.
            string detail = string.Join("\r\n", ok.Select(r => "  - " + r.Reason));

            if (skipped.Length > 0)
            {
                detail += string.Format("\r\n\r\n{0} item(s) had nothing to back up on this system.",
                    skipped.Length);
            }

            return new RunSummary
            {
                State = RunState.Done,
                Headline = string.Format("{0} {1} item(s).", verb.Past, ok.Length),
                Detail = detail
            };
        }
    }
}
