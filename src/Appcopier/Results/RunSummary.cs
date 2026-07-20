using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Appcopier
{
    /// <summary>
    /// The word forms needed to describe a run in every grammatical position its sentences use.
    /// One string cannot serve all of them - "Restored" reads fine in "Restored 3 items" but
    /// produces "Restored did not run." where "Restore did not run." is needed, and "Nothing was
    /// restore." is not a sentence either. Add a form here rather than a literal if a new sentence
    /// needs the verb in a position these four don't cover.
    /// </summary>
    internal sealed class RunVerb
    {
        public string Past { get; }        // "Backed up"  / "Restored"   - starts a headline
        public string PastLower { get; }   // "backed up"  / "restored"   - mid-sentence
        public string Noun { get; }        // "Backup"     / "Restore"    - subject of a sentence
        public string Infinitive { get; }  // "back up"    / "restore"    - after "nothing to"

        private RunVerb(string past, string pastLower, string noun, string infinitive)
        {
            Past = past;
            PastLower = pastLower;
            Noun = noun;
            Infinitive = infinitive;
        }

        public static readonly RunVerb Backup =
            new RunVerb("Backed up", "backed up", "Backup", "back up");

        public static readonly RunVerb Restore =
            new RunVerb("Restored", "restored", "Restore", "restore");
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

        // DidNotRun is a warning, not information: the user picked a backup folder and it was not
        // there, so they asked for something and did not get it. Only Done and NothingDone are
        // genuinely informational.
        public MessageBoxIcon Icon
            => State == RunState.Problems || State == RunState.DidNotRun
                ? MessageBoxIcon.Warning
                : MessageBoxIcon.Information;

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
                    Headline = "Nothing was " + verb.PastLower + ".",
                    Detail = "None of the selected items had anything to " + verb.Infinitive + "."
                };
            }

            // Skipped items are reported, but never as a problem and never added to a failure
            // count. Absences are the normal state of a real machine.
            string detail = string.Join("\r\n", ok.Select(r => "  - " + r.Reason));

            if (skipped.Length > 0)
            {
                detail += string.Format("\r\n\r\n{0} item(s) had nothing to {1}.",
                    skipped.Length, verb.Infinitive);
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
