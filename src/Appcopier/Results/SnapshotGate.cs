using System.Collections.Generic;
using System.Text;

namespace Appcopier
{
    /// <summary>
    /// What the pre-restore snapshot turned out to be.
    /// </summary>
    public enum SnapshotVerdict
    {
        /// <summary>Every item that needed snapshotting was captured.</summary>
        Complete,

        /// <summary>Nothing was captured, and nothing failed: an empty set, or every item skipped.</summary>
        NothingCaptured,

        /// <summary>At least one item failed to be captured.</summary>
        ModulesFailed,

        /// <summary>The snapshot folder itself could not be created, so nothing was attempted.</summary>
        FolderNotCreated
    }

    /// <summary>
    /// Whether a restore may proceed on the strength of the snapshot that was just taken.
    /// </summary>
    /// <remarks>
    /// Carries the failure text as data rather than showing anything. The dialog belongs to the
    /// caller on the UI thread; a decision type that raised its own MessageBox would be the
    /// worker-thread dialog pattern this phase exists to remove.
    ///
    /// <see cref="Summary"/> is written to restore_log.txt in every branch, including the ones that
    /// proceed, so the record says which path was taken rather than only that a restore happened.
    /// </remarks>
    internal sealed class SnapshotDecision
    {
        public SnapshotVerdict Verdict { get; }

        /// <summary>
        /// True when the user must confirm a second time before anything is overwritten.
        /// </summary>
        /// <remarks>
        /// Proceeding silently is never an option here: a restore with no working snapshot is the
        /// unsafe behaviour this phase removes, and doing it after OFFERING a snapshot is worse than
        /// never offering one, because the user believes they have a fallback.
        /// </remarks>
        public bool RequiresOverride { get; }

        /// <summary>One line per thing that went wrong. Empty when nothing did.</summary>
        public IReadOnlyList<string> Failures { get; }

        /// <summary>One line for the log and for the top of the override prompt.</summary>
        public string Summary { get; }

        private SnapshotDecision(SnapshotVerdict verdict, bool requiresOverride,
                                 string summary, IReadOnlyList<string> failures)
        {
            Verdict = verdict;
            RequiresOverride = requiresOverride;
            Summary = summary;
            Failures = failures;
        }

        /// <summary>The summary followed by one line per failure, ready to drop into a dialog.</summary>
        public string Describe()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Summary);

            for (int i = 0; i < Failures.Count; i++)
                sb.AppendLine("  " + Failures[i]);

            return sb.ToString();
        }

        internal static SnapshotDecision Create(SnapshotVerdict verdict, bool requiresOverride,
                                                string summary, IReadOnlyList<string> failures)
            => new SnapshotDecision(verdict, requiresOverride, summary,
                   failures ?? (IReadOnlyList<string>)new string[0]);
    }

    /// <summary>
    /// The pure decision Phase 2b's snapshot exists to make possible: given what the snapshot
    /// actually produced, may the restore go ahead?
    /// </summary>
    /// <remarks>
    /// A snapshot whose failure could not be DETECTED would be decoration. This is the detection;
    /// it reads the same ModuleResults the snapshot's own backup_log.txt is composed from, so there
    /// is no second notion of what "the snapshot worked" means.
    /// </remarks>
    internal static class SnapshotGate
    {
        /// <summary>
        /// The row for a snapshot that never started, because its folder could not be created.
        /// </summary>
        internal static SnapshotDecision FolderNotCreated(string error)
        {
            string detail = string.IsNullOrWhiteSpace(error)
                ? "the reason was not reported"
                : error.Trim();

            return SnapshotDecision.Create(
                SnapshotVerdict.FolderNotCreated,
                true,
                "The pre-restore snapshot folder could not be created, so nothing was backed up before this restore.",
                new[] { detail });
        }

        /// <summary>
        /// The row for a snapshot that ran. <paramref name="outcomes"/> holds one entry per module
        /// that was snapshotted, which is the selection minus the modules whose restore changes
        /// nothing - so an empty list is a legitimate state, not a missing argument.
        /// </summary>
        internal static SnapshotDecision Evaluate(IReadOnlyList<ModuleOutcome> outcomes)
        {
            List<string> failures = new List<string>();
            int considered = 0;
            int captured = 0;

            int count = outcomes == null ? 0 : outcomes.Count;

            for (int i = 0; i < count; i++)
            {
                ModuleOutcome outcome = outcomes[i];

                if (outcome == null)
                    continue;

                considered++;

                // A module that produced no result did not report success - it reported nothing,
                // and an unrun module is exactly the case the override prompt exists for.
                if (outcome.Result == null)
                {
                    failures.Add(Describe(outcome.Title, "no result was recorded"));
                    continue;
                }

                switch (outcome.Result.State)
                {
                    case ResultState.Failed:
                        failures.Add(Describe(outcome.Title, outcome.Result.Reason));
                        break;
                    case ResultState.Succeeded:
                        captured++;
                        break;
                }
            }

            if (failures.Count > 0)
            {
                string summary = string.Format(
                    "The pre-restore snapshot could not capture {0} of {1} item(s), so this restore cannot be fully undone:",
                    failures.Count, considered);

                return SnapshotDecision.Create(SnapshotVerdict.ModulesFailed, true, summary, failures);
            }

            // Nothing failed and nothing was captured. This proceeds, but it must SAY so: a user
            // told "snapshot taken" who then needs to roll back would find an empty folder.
            if (captured == 0)
            {
                string summary = considered == 0
                    ? "No pre-restore snapshot was taken: none of the selected items change anything when restored."
                    : string.Format(
                        "The pre-restore snapshot captured nothing: all {0} item(s) had nothing to back up.",
                        considered);

                return SnapshotDecision.Create(SnapshotVerdict.NothingCaptured, false, summary, null);
            }

            return SnapshotDecision.Create(
                SnapshotVerdict.Complete,
                false,
                string.Format("The pre-restore snapshot captured {0} of {1} item(s).", captured, considered),
                null);
        }

        private static string Describe(string title, string reason)
        {
            string named = string.IsNullOrWhiteSpace(title) ? "(unnamed item)" : title;

            return string.IsNullOrWhiteSpace(reason) ? named : named + ": " + reason;
        }
    }
}
