using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Appcopier
{
    public abstract class BackupBase
    {
        // Property to indicate whether a restart is required
        public virtual bool RequiresExplorerRestart { get; protected set; } = false;

        // Property to display Hints
        public virtual string WarningMessage { get; protected set; } = "";

        /// <summary>
        /// What an absent source means on the RESTORE side, where the source is the backup folder.
        /// </summary>
        /// <remarks>
        /// Shared so the restore side cannot drift back into backup-side wording. Saying "not
        /// present on this system" during a restore describes the wrong machine: the thing that is
        /// missing is the backup, and the live machine was never examined. Several modules already
        /// spelled this sentence out by hand; this is the same sentence, in one place.
        /// </remarks>
        protected const string NothingBackedUp = "nothing was backed up for this item";

        /// <summary>
        /// Whether this module may ask the user a question while backing up.
        /// </summary>
        /// <remarks>
        /// The pre-restore snapshot sets this false, and that is not a convenience. A module that
        /// prompts there raises an ownerless MessageBox from a thread-pool thread while the window
        /// is already disabled, so it can paint behind the app and strand the restore - and a "no"
        /// answer returns Skipped, which is indistinguishable at the gate from "there was nothing to
        /// back up". The module would then be left uncaptured and restored anyway, which is the
        /// whole defect this phase exists to remove.
        ///
        /// Consent for closing these processes is gathered once, on the UI thread, before the
        /// snapshot starts. Asking again from module code would be asking a second time for
        /// something already agreed.
        /// </remarks>
        internal bool AllowPrompts { get; set; } = true;

        // Property to display Info
        public string Title { get; set; }

        public string Info { get; set; }
        public string Version { get; set; }

        public virtual bool IsInstalled()
        { return false; }

        /// <summary>
        /// What this module's restore overwrites, in the words the confirmation dialog shows.
        /// </summary>
        /// <remarks>
        /// The default is a loud marker for the same reason the Backup/Restore defaults below are a
        /// failure rather than a skip: a future author who forgets produces something visible in the
        /// text users read before consenting, not silence. Silence here is worse than a wrong entry,
        /// because it asks for consent to an operation whose scope was never stated.
        /// </remarks>
        public virtual IReadOnlyList<RestoreTarget> RestoreTargets
            => RestoreTarget.Undeclared(GetType().Name);

        /// <summary>
        /// Processes that must not be running while this module's restore writes their files.
        /// </summary>
        /// <remarks>
        /// A declaration, not an action. The orchestrator decides and closes; module signatures do
        /// not change, so no module opens a dialog from the thread it happens to be running on.
        /// </remarks>
        public virtual IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
            => new RestoreCloseRequirement[0];

        /// <summary>
        /// Whether this module's restore writes anything, and so needs a pre-restore snapshot.
        /// </summary>
        /// <remarks>
        /// Defaults to true so a future module is snapshotted unless its author deliberately opts
        /// out. This is the same judgement call as absenceIsNormal and fails the same way in both
        /// directions: set wrong here, a module either pays for a snapshot it cannot use or is
        /// silently exempted from the one thing that would undo it.
        /// </remarks>
        public virtual bool RestoreMakesChanges => true;

        /// <remarks>
        /// The default is a FAILURE, not a Skip. It is unreachable for all 23 shipped modules -
        /// ConfPageView only ever calls the async pair, and every module implements one side or the
        /// other - so it fires only for a future module whose author forgot to implement backup.
        /// That is a bug, and a bug that announces itself beats one that returns a reassuring
        /// "nothing to do" and is never noticed.
        /// </remarks>
        public virtual ModuleResult Backup(string path)
            => ModuleResult.Aggregate(new[]
            {
                StepResult.Failed(GetType().Name, "this module does not implement backup")
            });

        public virtual ModuleResult Restore(string path)
            => ModuleResult.Aggregate(new[]
            {
                StepResult.Failed(GetType().Name, "this module does not implement restore")
            });

        public virtual async Task<ModuleResult> BackupAsync(string path)
        {
            return await Task.Run(() => Backup(path)).ConfigureAwait(true);
        }

        public virtual async Task<ModuleResult> RestoreAsync(string path)
        {
            return await Task.Run(() => Restore(path)).ConfigureAwait(true);
        }
    }
}
