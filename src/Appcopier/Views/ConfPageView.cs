using Appcopier;
using Conf;
using DataHelper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Views
{
    public partial class ConfPageView : UserControl
    {
        private static readonly LogHelper logger = LogHelper.Instance;

        /// <summary>
        /// The greeting shown in the log pane. {0} is the OS build string from OsHelper.GetVersion.
        /// </summary>
        /// <remarks>
        /// A const rather than an inline interpolation so the composition is testable without
        /// constructing the control. GetVersion degrades to a self-describing token rather than to
        /// an empty string precisely so this sentence cannot come out with a double space or a
        /// stray " ." in it; OsVersionTests asserts that for every degraded shape.
        /// </remarks>
        internal const string IntroTemplate =
            "This app supports you in backing up, sharing, and restoring your key settings of your Windows 11 {0} on this or another system.";

        internal string CurrentBackupPath = Data.DataRootDir + Data.NowShort + "\\";
        internal string CurrentRestorePath = "";

        internal List<BackupBase> selectedConfigs = new List<BackupBase>();

        private bool isSelectAll = false;

        public ConfPageView()
        {
            InitializeComponent();
            InitializeConfigurations();
            SetStyle();
        }

        private void SetStyle()
        {
            // Segoe MDL2 Assets
            btnMenu.Text = "\uEA8A";
            btnMenuMore.Text = "\uE712";
            btnMenuRestore.Text = "\uE777";
            // Some color styling
            pnlNav.BackColor = Color.FromArgb(245, 241, 249);
            BackColor =
            rtbLog.BackColor = Color.FromArgb(250, 250, 250);
            // Dynamically set OS information
            rtbLog.Text = string.Format(IntroTemplate, OsHelper.GetVersion());
            // Log messages to target rtbLog
            logger.SetTarget(rtbLog);
        }

        private void InitializeConfigurations()
        {
            AddConfiguration(new WPersonalization(), "Settings");
            AddConfiguration(new WVisualEffects(), "Settings");
            AddConfiguration(new WTaskbar(), "Settings");
            AddConfiguration(new WPrivacy(), "Settings");
            AddConfiguration(new WAPrivacy(), "Settings");
            AddConfiguration(new WTelemetry(), "Settings");
            AddConfiguration(new WNetworkConf(), "Settings");
            AddConfiguration(new WUpdates(), "Settings");
            AddConfiguration(new WThemes(), "Settings");
            AddConfiguration(new WAccessibility(), "Settings");
            AddConfiguration(new WOther(), "Settings");
            AddConfiguration(new AppStoreApps(), "Apps");
            AddConfiguration(new APinnedApps(), "Apps");
            // The browser modules (Chrome, Edge, Firefox) were retired in Phase 3a: they copied
            // whole profile directories - caches, GPU data, live locked databases - and browser
            // sync solves the problem better than a local export can. Backups made with earlier
            // versions keep their browser folders on disk; this app no longer restores them.
            AddConfiguration(new DPrinters(), "Devices");
            AddConfiguration(new DMouse(), "Devices");
            AddConfiguration(new DKeyboard(), "Devices");
            AddConfiguration(new DTouchpad(), "Devices");
            AddConfiguration(new GGaming(), "Gaming");
            AddConfiguration(new CWiFiConf(), "Credentials");
            AddConfiguration(new ETerminal(), "Developer");
            AddConfiguration(new EVSCode(), "Developer");
            AddConfiguration(new ESsh(), "Developer");
            AddConfiguration(new EEnvironment(), "Developer");
            AddConfiguration(new EHosts(), "Developer");

            // Add event handler for button click
            btnRestartExplorer.Click += btnRestartExplorer_Click;
        }

        private void AddConfiguration(BackupBase configuration, string parentNodeText)
        {
            TreeNode parentNode = FindOrCreateNode(parentNodeText);
            TreeNode childNode = new TreeNode(configuration.Title);
            childNode.Tag = configuration;
            parentNode.Nodes.Add(childNode);
        }

        private TreeNode FindOrCreateNode(string text)
        {
            TreeNode parentNode = treeConfigurations.Nodes.Cast<TreeNode>()
                .FirstOrDefault(node => node.Text == text);

            if (parentNode == null)
            {
                parentNode = new TreeNode(text);
                treeConfigurations.Nodes.Add(parentNode);
            }

            return parentNode;
        }

        private async void btnBackup_Click(object sender, EventArgs e)
        {
            btnBackup.Enabled = false;
            this.Enabled = false;

            // The whole body is wrapped so the window is re-enabled in a finally. This is an async
            // void handler that disables the form on its first two lines: anything escaping it is
            // unhandled AND leaves the main window permanently dead, with no way back short of
            // killing the process.
            try
            {
                await RunBackup();
            }
            finally
            {
                this.Enabled = true;
                btnBackup.Enabled = true;
            }
        }

        private async Task RunBackup()
        {
            selectedConfigs.Clear();

            bool isAtLeastOneChecked = treeConfigurations.Nodes
                .Cast<TreeNode>()
                .Any(parentNode => parentNode.Nodes.Cast<TreeNode>().Any(childNode => childNode.Checked));

            // At least one node is checked, then proceed!
            if (isAtLeastOneChecked)
            {
                string createError;

                if (!TryCreateBackupFolder(CurrentBackupPath, out createError))
                {
                    // Reported as a run that DID NOT RUN, not as a crash and not as a silent
                    // no-op: the user asked for a backup and got nothing, and they need to be
                    // told which of those two it was.
                    ShowSummary(RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Backup,
                        "the backup folder could not be created: " + createError), "Backup");
                    return;
                }

                foreach (TreeNode parentNode in treeConfigurations.Nodes)
                {
                    foreach (TreeNode childNode in parentNode.Nodes)
                    {
                        if (childNode.Checked)
                        {
                            BackupBase configuration = childNode.Tag as BackupBase;
                            if (configuration != null)
                            {
                                selectedConfigs.Add(configuration);
                            }
                        }
                    }
                }

                List<ModuleResult> results =
                    await RunModulesBackup(selectedConfigs, CurrentBackupPath, "Backing up");

                // Log backed-up elements
                LogBackedUpElements(CurrentBackupPath, selectedConfigs, results);

                ShowSummary(
                    RunSummary.For(ModuleOutcome.Pair(selectedConfigs, results), true, RunVerb.Backup),
                    "Backup");
            }
            else
            {
                MessageBox.Show("Nothing has been selected for backup. Please choose your settings to be backed up beforehand.", "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Creates the backup folder, reporting rather than throwing if it cannot.
        /// </summary>
        /// <remarks>
        /// Ordinary failures, not exotic ones: the exe under Program Files on a standard-user
        /// account, a full disk, a path over the length limit. This used to be a bare
        /// Directory.CreateDirectory outside any try, in an async void handler.
        /// </remarks>
        private bool TryCreateBackupFolder(string path, out string error)
        {
            error = null;

            try
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                logger.LogMessage("Could not create the backup folder " + path + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Backs up a set of modules into a folder, reporting one result per module.
        /// </summary>
        /// <remarks>
        /// Shared by the Backup button and by the pre-restore snapshot, which is the point: the
        /// snapshot is an ordinary backup, so it inherits the export verification and the honest
        /// per-module results rather than growing a second, less-tested capture path.
        /// </remarks>
        private async Task<List<ModuleResult>> RunModulesBackup(IReadOnlyList<BackupBase> modules,
                                                                string folder, string progressVerb)
        {
            List<ModuleResult> results = new List<ModuleResult>();

            foreach (BackupBase module in modules)
            {
                linkSubHeader.Text = progressVerb + ": " + module.Title;

                ModuleResult outcome;

                try
                {
                    outcome = await module.BackupAsync(folder);
                }
                catch (Exception ex)
                {
                    // Rule 6. Mandatory, not defensive style: this loop is driven by an async void
                    // click handler, so an escaping exception is unhandled and takes the process
                    // down along with every result gathered so far.
                    outcome = ModuleResult.Aggregate(new[]
                    {
                        StepResult.Failed(module.Title, "unhandled error: " + ex.GetType().Name + ": " + ex.Message)
                    });
                }

                results.Add(outcome);

                linkSubHeader.Text = "Choose settings";
            }

            return results;
        }

        private static void ShowSummary(RunSummary summary, string caption)
        {
            logger.LogMessage(summary.Headline);
            logger.LogMessage(summary.Detail);

            MessageBox.Show(summary.Headline + "\r\n\r\n" + summary.Detail,
                caption, MessageBoxButtons.OK, summary.Icon);
        }

        // Write a backup_log.txt that records outcomes, not just the selection.
        private void LogBackedUpElements(string backupFolderPath, IReadOnlyList<BackupBase> configurations,
                                         IReadOnlyList<ModuleResult> results,
                                         IEnumerable<string> extraHeaderLines = null)
        {
            string logFilePath = Path.Combine(backupFolderPath, "backup_log.txt");

            try
            {
                string text = BackupLog.Compose(configurations, results, DateTime.Now.ToString(), extraHeaderLines);
                File.WriteAllText(logFilePath, text);
            }
            catch (Exception ex)
            {
                logger.LogMessage("Failed to create backup log file: " + ex.Message);
            }
        }

        /// <summary>
        /// Runs one module's restore, having first dealt with the process that owns the files it is
        /// about to overwrite.
        /// </summary>
        /// <remarks>
        /// The process state is re-read here rather than reused from consent time. Consent persists
        /// for the run; whether Chrome is open does not, and the user can reopen it while the
        /// snapshot is still being taken.
        /// </remarks>
        private async Task<ModuleResult> RestoreOne(RestoreScopeEntry entry, IReadOnlyList<string> consented)
        {
            BackupBase config = entry.Module;

            try
            {
                // Refused on the same observation the snapshot set was chosen from, rather than on a
                // fresh one. RestoreScope holds the reasoning; the short version is that a module the
                // snapshot left out must not be restored, and re-reading the process state here is
                // exactly how the two halves came to disagree.
                if (!entry.WillBeRestored)
                    return ModuleResult.Aggregate(new[] { RestoreScope.DescribeBlock(entry) });

                IReadOnlyList<RestoreCloseRequirement> requirements =
                    config.ProcessesToCloseBeforeRestore ?? new RestoreCloseRequirement[0];

                List<StepResult> closeSteps = new List<StepResult>();
                List<RestoreCloseRequirement> justInTime = new List<RestoreCloseRequirement>();

                foreach (RestoreCloseRequirement requirement in requirements)
                {
                    // Skipped exactly as RestoreScope.Evaluate and RestorePlan.CollectCloses skip it.
                    // Those two treat a null entry as a supported degenerate declaration; this loop
                    // was the only one of the three reading the list without the guard, so a module
                    // they both passed over as harmless reached here and dereferenced it. The catch
                    // below would have caught the NullReferenceException, so the cost was not a crash
                    // but a worse lie than one: the module was scoped as unblocked, was snapshotted,
                    // had its process closed - and was then reported as an unhandled error.
                    if (requirement == null)
                        continue;

                    bool consentGiven = requirement.NeedsConsent
                        && RestoreScope.IsConsented(consented, requirement.ProcessName);
                    bool isRunning = false;
                    CloseResult closeResult = CloseResult.NotRunning;

                    if (requirement.NeedsConsent)
                    {
                        string processName = requirement.ProcessName;

                        isRunning = await Task.Run(() => Utils.IsProcessRunning(processName));

                        // Re-closed rather than trusted, because consent persists for the run and the
                        // process state does not: the user can reopen a browser while the snapshot is
                        // still being taken. Failing here is safe in a way that failing the other way
                        // round is not - this module WAS snapshotted, so refusing it now leaves a
                        // usable fallback on disk.
                        if (consentGiven && isRunning)
                            closeResult = await Task.Run(() => Utils.CloseProcess(processName));
                    }

                    RestoreDecision decision = RestoreDispatch.Decide(
                        config.Title, requirement, consentGiven, isRunning, closeResult);

                    if (decision.CloseStep != null)
                        closeSteps.Add(decision.CloseStep);

                    if (decision.JustInTimeClose != null)
                        justInTime.Add(decision.JustInTimeClose);

                    // Skip and Fail are both refusals to overwrite, so nothing after this point may
                    // run - including the remaining requirements, whose closes would be pointless.
                    if (decision.Action != RestoreAction.Run)
                        return ModuleResult.Aggregate(closeSteps);
                }

                // Closed here rather than up front: StartMenuExperienceHost respawns within seconds,
                // so a close performed at consent time is gone again before the copy starts.
                foreach (RestoreCloseRequirement requirement in justInTime)
                {
                    string processName = requirement.ProcessName;
                    CloseResult closed = await Task.Run(() => Utils.CloseProcess(processName));

                    closeSteps.Add(DescribeJustInTimeClose(config.Title, requirement, closed));
                }

                ModuleResult outcome = await config.RestoreAsync(CurrentRestorePath);

                foreach (StepResult closeStep in closeSteps)
                    outcome = RestoreDispatch.Fold(closeStep, outcome);

                return outcome;
            }
            catch (Exception ex)
            {
                // Rule 6, and it matters more here than on the backup path: this method is awaited
                // by HandleRestorationAfterSelection, which is itself awaited from an async void
                // handler in RestPageView, and AppStoreApps.Restore opens a dialog from a
                // thread-pool thread with no message pump.
                return ModuleResult.Aggregate(new[]
                {
                    StepResult.Failed(config.Title, "unhandled error: " + ex.GetType().Name + ": " + ex.Message)
                });
            }
        }

        /// <summary>
        /// The consented processes that some module actually about to be restored owns.
        /// </summary>
        /// <remarks>
        /// Consent is gathered from the tree selection, which says nothing about what the chosen
        /// backup folder contains. Closing on consent alone therefore kills a browser for a module
        /// whose restore will report "nothing was backed up for this item" - real, visible work
        /// destroyed for an operation knowable in advance to be a no-op.
        /// </remarks>
        private IEnumerable<string> ProcessesWorthClosing(IReadOnlyList<BackupBase> modules,
                                                          IReadOnlyList<string> consented)
        {
            HashSet<string> worth = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (BackupBase module in modules)
            {
                // RestoreScope's, deliberately not a second copy: this asks the same question of
                // the same modules that Evaluate asks moments later, and the two must not be able
                // to disagree. See the remarks on RestoreScope.HasBackup.
                if (!RestoreScope.HasBackup(module, CurrentRestorePath))
                    continue;

                foreach (RestoreCloseRequirement requirement in
                         module.ProcessesToCloseBeforeRestore ?? new RestoreCloseRequirement[0])
                {
                    if (requirement != null && requirement.NeedsConsent)
                        worth.Add(requirement.ProcessName);
                }
            }

            return consented.Where(worth.Contains);
        }

        /// <summary>
        /// The sentence naming processes this run has already closed, or nothing when it closed none.
        /// </summary>
        private static string DescribeAlreadyClosed(IDictionary<string, CloseResult> closedUpFront)
        {
            string[] closed = closedUpFront
                .Where(entry => entry.Value == CloseResult.Exited)
                .Select(entry => entry.Key)
                .ToArray();

            if (closed.Length == 0)
                return "";

            return " Note that " + string.Join(", ", closed) +
                   " had already been closed in order to take the snapshot.";
        }

        /// <summary>
        /// The just-in-time close, as a step so it survives into restore_log.txt.
        /// </summary>
        /// <remarks>
        /// A process that would not close is reported as Skipped rather than Failed on purpose. These
        /// are the processes Windows restarts by itself - StartMenuExperienceHost is back within
        /// seconds - so it is running again during the copy on a healthy machine, and failing the
        /// module for that would cry wolf on nearly every run.
        ///
        /// That is a judgement about noise, not a guarantee of correctness, and the step wording says
        /// only what is known: files it held open may not have been replaced. A locked file does
        /// surface, because the copy fails on it and Aggregate fails the module. A process that keeps
        /// its state in memory and flushes on exit does NOT - it can let every file copy cleanly and
        /// then write its own version back over the restore. The Start menu layout store behaves that
        /// way, which is why the reason is worded as a caveat rather than an all-clear.
        /// </remarks>
        private static StepResult DescribeJustInTimeClose(string moduleTitle,
                                                          RestoreCloseRequirement requirement,
                                                          CloseResult closed)
        {
            switch (closed)
            {
                case CloseResult.Exited:
                    return StepResult.Succeeded(moduleTitle,
                        "closed " + requirement.DisplayName + " before writing its files");

                case CloseResult.NotRunning:
                    return StepResult.Skipped(moduleTitle,
                        requirement.DisplayName + " was not running, so nothing had to be closed");

                default:
                    return StepResult.Skipped(moduleTitle,
                        requirement.DisplayName + " could not be closed first (" + closed +
                        "), so any files it was holding open may not have been replaced");
            }
        }

        // Restoration logic with selected configurations
        private async Task<List<ModuleResult>> PerformRestoration(IReadOnlyList<RestoreScopeEntry> scope,
                                                                  IReadOnlyList<string> consented)
        {
            List<ModuleResult> results = new List<ModuleResult>();

            foreach (RestoreScopeEntry entry in scope)
            {
                linkSubHeader.Text = "Restoring: " + entry.Module.Title;

                results.Add(await RestoreOne(entry, consented));

                linkSubHeader.Text = "Choose settings";
            }

            return results;
        }

        /// <summary>
        /// Takes the pre-restore snapshot and reports whether the restore may go ahead on it.
        /// </summary>
        private async Task<SnapshotDecision> TakeSnapshot(IReadOnlyList<BackupBase> snapshotSet,
                                                          string snapshotFolderPath, int blockedCount)
        {
            if (snapshotFolderPath == null)
                return SnapshotGate.FolderNotCreated("a snapshot folder name could not be chosen");

            if (snapshotSet.Count == 0)
                return SnapshotGate.Evaluate(new List<ModuleOutcome>(), blockedCount);

            string createError;

            if (!TryCreateBackupFolder(snapshotFolderPath, out createError))
                return SnapshotGate.FolderNotCreated(createError);

            List<ModuleResult> results =
                await RunModulesBackup(snapshotSet, snapshotFolderPath, "Snapshotting");

            LogBackedUpElements(snapshotFolderPath, snapshotSet, results, new[]
            {
                "# Pre-restore snapshot, taken before restoring from " + CurrentRestorePath,
                "# " + RestorePlan.FidelityCaveat
            });

            return SnapshotGate.Evaluate(ModuleOutcome.Pair(snapshotSet, results));
        }

        // Write a restore_log.txt recording what this restore changed and what could undo it.
        private void LogRestoredElements(IReadOnlyList<BackupBase> configurations,
                                         IReadOnlyList<ModuleResult> results,
                                         SnapshotDecision snapshot, string snapshotFolderPath)
        {
            bool haveSnapshotFolder = snapshotFolderPath != null && Directory.Exists(snapshotFolderPath);

            string text = RestoreLog.Compose(configurations, results, DateTime.Now.ToString(),
                CurrentRestorePath, snapshot, haveSnapshotFolder ? snapshotFolderPath : null);

            // Beside the rollback artifact when there is one. When the gate was overridden after the
            // folder could not be created there is nowhere else but the folder just restored from.
            string logFilePath = haveSnapshotFolder
                ? Path.Combine(snapshotFolderPath, RestoreLog.FileName)
                : Path.Combine(CurrentRestorePath, RestoreLog.FallbackFileName(DateTime.Now));

            try
            {
                File.WriteAllText(logFilePath, text);
            }
            catch (Exception ex)
            {
                logger.LogMessage("Failed to create restore log file: " + ex.Message);
            }
        }

        // Asynchronous method to handle restoration after the user selects restoration path
        public async Task HandleRestorationAfterSelection()
        {
            // The window is disabled for the whole run. Without it a second restore can be started
            // while the first is still writing, and the snapshot this phase adds makes a restore
            // long enough for that to be reachable by hand rather than merely possible.
            this.Enabled = false;

            try
            {
                await RunRestore();
            }
            finally
            {
                this.Enabled = true;
            }
        }

        private async Task RunRestore()
        {
            if (CurrentRestorePath == "" || !Directory.Exists(CurrentRestorePath))
            {
                ShowSummary(RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Restore), "Restore");
                return;
            }

            // Stage 1: name the snapshot before asking, so the dialog can say where it will go.
            // A fresh timestamp, never Data.NowShort - that is stamped once per process.
            string snapshotFolderPath = null;

            try
            {
                string name = SnapshotNaming.Unique(SnapshotNaming.NameFor(DateTime.Now),
                    n => Directory.Exists(Path.Combine(Data.DataRootDir, n)));

                snapshotFolderPath = Path.Combine(Data.DataRootDir, name);
            }
            catch (Exception ex)
            {
                logger.LogMessage("Could not choose a snapshot folder name: " + ex.Message);
            }

            // Composing the plan reads four virtual members off every selected module -
            // RestoreTargets, ProcessesToCloseBeforeRestore, Title and WarningMessage - and any of
            // the four can throw from a module written later. This stage sits between the try above
            // and the confirmation dialog, and the whole chain up to the async void click handler
            // has no catch, so an escaping exception here would surface as WinForms' unhandled
            // exception dialog mid-restore.
            //
            // Fail closed: the plan IS the description the user consents against, so no description
            // means no consent, and no consent means nothing is touched.
            RestorePlan plan;

            try
            {
                plan = new RestorePlan(selectedConfigs, CurrentRestorePath,
                    snapshotFolderPath ?? "(no snapshot folder could be named)");
            }
            catch (Exception ex)
            {
                logger.LogMessage("Could not describe what this restore would overwrite: " + ex.Message);

                MessageBox.Show(FindForm(),
                    "What this restore would overwrite could not be described, so you were not asked " +
                    "to confirm it and nothing has been changed.\r\n\r\n" + ex.Message,
                    "Restore", MessageBoxButtons.OK, MessageBoxIcon.Error);

                return;
            }

            // Stage 2: informed consent, on the UI thread, before anything is created.
            IReadOnlyList<string> consented;

            // The owner is the Form, not this control: a UserControl is not something CenterParent
            // can centre on, and a modal owned by a control this pipeline has already disabled is
            // the shape that fails to come forward.
            Form owner = FindForm();

            using (RestoreConfirmForm confirm = new RestoreConfirmForm(plan))
            {
                if (confirm.ShowDialog(owner) != DialogResult.OK)
                {
                    logger.LogMessage("Restore cancelled - nothing was changed.");
                    return;
                }

                consented = confirm.ConsentedProcessNames;
            }

            // Stage 3: close the consented processes once, up front, so the snapshot's own backup
            // does not prompt about the same browser the user has already answered for.
            //
            // Only for processes some module is actually going to be restored from. A module the
            // backup folder holds nothing for is refused before this point, so its browser is not
            // killed for a restore that was always going to write nothing - which cost the user
            // every open tab, and cost it in a way the pre-2b code did not, because that closed
            // nothing at all.
            Dictionary<string, CloseResult> closedUpFront =
                new Dictionary<string, CloseResult>(StringComparer.OrdinalIgnoreCase);

            foreach (string processName in ProcessesWorthClosing(selectedConfigs, consented))
            {
                string name = processName;
                CloseResult closed = await Task.Run(() => Utils.CloseProcess(name));

                closedUpFront[name] = closed;
                logger.LogMessage("Closing " + name + " before the restore: " + closed);
            }

            // Stages 4 and 5: snapshot, then decide whether the restore may go ahead on it.
            //
            // Worked out ONCE, here, and used by both the snapshot and the dispatch loop. Deciding
            // twice from two readings of the process state is what previously let a module be left
            // out of the snapshot and then restored anyway.
            IReadOnlyList<RestoreScopeEntry> scope =
                RestoreScope.For(selectedConfigs, consented, closedUpFront, CurrentRestorePath);

            List<BackupBase> snapshotSet = scope
                .Where(entry => entry.NeedsSnapshot)
                .Select(entry => entry.Module)
                .ToList();

            int blockedCount = scope.Count(entry => !entry.WillBeRestored);

            SnapshotDecision snapshot = await TakeSnapshot(snapshotSet, snapshotFolderPath, blockedCount);

            logger.LogMessage(snapshot.Summary);

            if (snapshot.RequiresOverride)
            {
                DialogResult answer = MessageBox.Show(owner,
                    snapshot.Describe() + "\r\n" + RestorePlan.FidelityCaveat +
                    "\r\n\r\nRestore anyway, without being able to undo it?",
                    "Pre-restore snapshot", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (answer != DialogResult.Yes)
                {
                    // Names the processes that were already closed. They were closed to take the
                    // snapshot, and the snapshot is what just failed - so the user gave up an open
                    // browser for a restore that then did not happen, and "nothing ran" on its own
                    // would be the misreport this phase exists to remove.
                    ShowSummary(RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Restore,
                        "the pre-restore snapshot did not complete and you chose not to continue." +
                        DescribeAlreadyClosed(closedUpFront)), "Restore");
                    return;
                }
            }

            // Stage 6.
            List<ModuleResult> results = await PerformRestoration(scope, consented);

            // Stage 7. Reported against the modules the restore actually walked, not against the
            // list it was asked to walk. `results` is built one-per-scope-entry, and RestoreScope.For
            // drops nulls - so pairing them with selectedConfigs pairs two lists that are only equal
            // in length by coincidence of upstream filtering. Whenever they were not, every outcome
            // after the dropped module would be attributed to the wrong one, in the summary and in
            // restore_log.txt both. Projecting from scope makes the alignment structural.
            List<BackupBase> restoredModules = scope.Select(entry => entry.Module).ToList();

            LogRestoredElements(restoredModules, results, snapshot, snapshotFolderPath);

            // Stage 8. Gated on a successful restore of a module that declares
            // RequiresExplorerRestart, not merely on the declaration: a module that failed or was
            // skipped never touched Explorer state, so offering to restart it would be a no-op
            // dressed up as a fix.
            bool requiresRestart = restoredModules
                .Zip(results, (config, result) => new { config, result })
                .Any(x => x.config.RequiresExplorerRestart && x.result.State == ResultState.Succeeded);

            btnRestartExplorer.Visible = requiresRestart;

            ShowSummary(
                RunSummary.For(ModuleOutcome.Pair(restoredModules, results), true, RunVerb.Restore),
                "Restore");
        }

        private void btnRestore_Click(object sender, EventArgs e)
        {
            bool isAtLeastOneChecked = treeConfigurations.Nodes
                   .Cast<TreeNode>()
                   .Any(parentNode => parentNode.Nodes.Cast<TreeNode>().Any(childNode => childNode.Checked));

            // At least one node is checked, then proceed!
            if (isAtLeastOneChecked)
            {
                // Clear selectedConfigs list before populating it
                selectedConfigs.Clear();

                foreach (TreeNode parentNode in treeConfigurations.Nodes)
                {
                    foreach (TreeNode childNode in parentNode.Nodes)
                    {
                        if (childNode.Checked)
                        {
                            BackupBase configuration = childNode.Tag as BackupBase;
                            if (configuration != null)
                            {
                                selectedConfigs.Add(configuration);
                            }
                        }
                    }
                }

                ViewHelper.SwitchView.SetView(new RestPageView(this));
            }
            else
            {
                MessageBox.Show("Please choose a configuration to restore beforehand.", "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SelectInstalled()
        {
            foreach (TreeNode parentNode in treeConfigurations.Nodes)
            {
                foreach (TreeNode childNode in parentNode.Nodes)
                {
                    BackupBase configuration = childNode.Tag as BackupBase;
                    if (configuration != null)
                    {
                        bool isConfigInstalled = configuration.IsInstalled();
                        childNode.Checked = isConfigInstalled;
                    }
                }
            }
        }

        private void SelectAll(bool flag)
        {
            foreach (TreeNode parentNode in treeConfigurations.Nodes)
            {
                foreach (TreeNode childNode in parentNode.Nodes)
                {
                    childNode.Checked = flag;
                }
            }
        }

        private void treeConfigurations_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (treeConfigurations.SelectedNode != null && treeConfigurations.SelectedNode.Tag is BackupBase selectedConfiguration)
            {
                // Display the warning message
                if (!string.IsNullOrEmpty(selectedConfiguration.WarningMessage))
                {
                    ShowWarningMessage(selectedConfiguration.WarningMessage);
                }

                logger.ClearLog();

                BackupBase selectedConfig = treeConfigurations.SelectedNode.Tag as BackupBase;
                if (selectedConfig != null)
                {
                    logger.Log((selectedConfig.Title + "\r\n\n" +
                        selectedConfig.Info + "\r\n" +
                        selectedConfig.Version));
                }
            }
        }

        private void ShowWarningMessage(string warningMessage)
        {
            MessageBox.Show(warningMessage, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void btnMenuMore_Click(object sender, EventArgs e)
            => this.contextMenu.Show(Cursor.Position.X, Cursor.Position.Y);

        private void menuSelectInstalled_Click(object sender, EventArgs e)
        {
            SelectAll(false);
            SelectInstalled();
        }

        private void menuSelectAll_Click(object sender, EventArgs e)
        {
            isSelectAll = !isSelectAll;
            SelectAll(isSelectAll);
        }

        /// <remarks>
        /// Off the UI thread because the close carries a five-second budget, and the button is hidden
        /// only when a shell actually came back: hiding it unconditionally, as this did, removed the
        /// user's only way to retry precisely when the retry was needed.
        /// </remarks>
        private async void btnRestartExplorer_Click(object sender, EventArgs e)
        {
            btnRestartExplorer.Enabled = false;

            try
            {
                ExplorerRestartResult outcome = await Task.Run(() => Utils.RestartExplorer());

                if (outcome.Shell == ShellOutcome.Restarted || outcome.Shell == ShellOutcome.RestartedByWindows)
                    btnRestartExplorer.Visible = false;
                else
                    MessageBox.Show(this, outcome.Describe(), "Restart File Explorer",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                btnRestartExplorer.Enabled = true;
            }
        }

        private void treeConfigurations_AfterCheck(object sender, TreeViewEventArgs e)
        {
            foreach (TreeNode child in e.Node.Nodes)
            {
                child.Checked = e.Node.Checked;
            }
        }

        private void menuOpenAppBackups_Click(object sender, EventArgs e)
        {
            RestAppsForm f = new RestAppsForm();
            f.ShowDialog();
        }

        private void menuOpenBackupFolder_Click(object sender, EventArgs e)

           => Process.Start(new ProcessStartInfo("explorer.exe", DataHelper.Data.DataRootDir) { UseShellExecute = true });
    }
}