using System;
using System.Collections.Generic;
using System.Linq;

namespace Appcopier
{
    /// <summary>
    /// One module's verdict for one Backup or Restore call.
    /// </summary>
    /// <remarks>
    /// Immutable and returned by value, which is required rather than stylistic: five modules run
    /// their work on the UI thread (they override the async pair directly) and the rest run on a
    /// thread-pool thread via BackupBase's Task.Run wrapper, so no shared mutable accumulator is safe.
    /// </remarks>
    public sealed class ModuleResult
    {
        public ResultState State { get; }

        /// <summary>One line, shown to the user.</summary>
        public string Reason { get; }

        public IReadOnlyList<StepResult> Steps { get; }

        private ModuleResult(ResultState state, string reason, IReadOnlyList<StepResult> steps)
        {
            State = state;
            Reason = reason;
            Steps = steps;
        }

        /// <summary>
        /// The single construction path. Modules never fold by hand, and there are deliberately no
        /// public Succeeded/Skipped/Failed factories - one of them would be used to bypass these
        /// rules within a week, and the rules are the whole point.
        /// </summary>
        public static ModuleResult Aggregate(IReadOnlyList<StepResult> steps)
        {
            StepResult[] all = steps == null ? new StepResult[0] : steps.Where(s => s != null).ToArray();

            // Rule 1. A module that produced no steps did not decide anything. Reporting that as
            // success would be the original bug in miniature.
            if (all.Length == 0)
                return new ModuleResult(ResultState.Skipped, "nothing to do", all);

            StepResult[] failed = all.Where(s => s.State == ResultState.Failed).ToArray();
            StepResult[] skipped = all.Where(s => s.State == ResultState.Skipped).ToArray();
            StepResult[] ok = all.Where(s => s.State == ResultState.Succeeded).ToArray();

            // Rule 2. Any failure dominates. A backup missing one of its keys will restore wrong,
            // and calling that "partial" invites the user to treat it as good enough.
            if (failed.Length > 0)
            {
                string reason = string.Format(
                    "{0} of {1} operations failed: {2}",
                    failed.Length, all.Length, failed[0].Reason);

                return new ModuleResult(ResultState.Failed, reason, all);
            }

            // Rule 3. Everything was legitimately absent. This is Skipped, not Succeeded: folding
            // it up to success would claim a module was backed up having written zero bytes, which
            // is exactly what GGaming and WTelemetry do on a stock consumer machine.
            //
            // "nothing to do", not "nothing to back up": Aggregate serves both directions, and
            // AStoreApps' restore-side Skipped reads "nothing to back up: handled interactively in
            // the app restore dialog" otherwise - a backup verb on a restore.
            if (ok.Length == 0)
            {
                string reason = "nothing to do: " +
                    string.Join("; ", skipped.Select(s => s.Reason).Distinct());

                return new ModuleResult(ResultState.Skipped, reason, all);
            }

            // Rule 4. Some captured, some legitimately absent. This must read as success with a
            // note - WPersonalization and WUpdates hit it on a large share of healthy machines, and
            // rendering it as a warning is the cry-wolf failure this phase exists to remove.
            //
            // Worded by what was OBTAINED, never as a bare ratio: "1 of 2 captured" under a heading
            // of "Done" reads as partial failure, reintroducing the ambiguity that justified having
            // no Partial state.
            if (skipped.Length > 0)
            {
                string reason = string.Format(
                    "captured {0}; {1} not present on this system ({2})",
                    Describe(ok), skipped.Length,
                    string.Join(", ", skipped.Select(s => s.Target)));

                return new ModuleResult(ResultState.Succeeded, reason, all);
            }

            // Rule 5.
            return new ModuleResult(ResultState.Succeeded, "captured " + Describe(ok), all);
        }

        private static string Describe(StepResult[] ok)
            => ok.Length == 1 ? ok[0].Target : ok.Length + " items";
    }
}
