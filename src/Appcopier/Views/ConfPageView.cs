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
            rtbLog.Text = $"This app supports you in backing up, sharing, and restoring your key settings of your Windows 11 {OsHelper.GetVersion()} on this or another system.";
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
            AddConfiguration(new BMozillaFirefox(), "Browser");
            AddConfiguration(new BMicrosoftEdge(), "Browser");
            AddConfiguration(new BGoogleChrome(), "Browser");
            AddConfiguration(new DPrinters(), "Devices");
            AddConfiguration(new DMouse(), "Devices");
            AddConfiguration(new DKeyboard(), "Devices");
            AddConfiguration(new DUSB(), "Devices");
            AddConfiguration(new DTouchpad(), "Devices");
            AddConfiguration(new GGaming(), "Gaming");
            AddConfiguration(new CWiFiConf(), "Credentials");

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
        private async Task<ModuleResult> RestoreOne(BackupBase config, IReadOnlyList<string> consented)
        {
            try
            {
                IReadOnlyList<RestoreCloseRequirement> requirements =
                    config.ProcessesToCloseBeforeRestore ?? new RestoreCloseRequirement[0];

                List<StepResult> closeSteps = new List<StepResult>();
                List<RestoreCloseRequirement> justInTime = new List<RestoreCloseRequirement>();

                foreach (RestoreCloseRequirement requirement in requirements)
                {
                    bool consentGiven = requirement.NeedsConsent && IsConsented(consented, requirement.ProcessName);
                    bool isRunning = false;
                    CloseResult closeResult = CloseResult.NotRunning;

                    if (requirement.NeedsConsent)
                    {
                        string processName = requirement.ProcessName;
                        isRunning = await Task.Run(() => Utils.IsProcessRunning(processName));

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

                    logger.LogMessage("Closed " + requirement.DisplayName + " before restoring " +
                                      config.Title + ": " + closed);
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

        private static bool IsConsented(IReadOnlyList<string> consented, string processName)
            => consented != null
               && consented.Any(p => string.Equals(p, processName, StringComparison.OrdinalIgnoreCase));

        // Restoration logic with selected configurations
        private async Task<List<ModuleResult>> PerformRestoration(List<BackupBase> configs,
                                                                  IReadOnlyList<string> consented)
        {
            List<ModuleResult> results = new List<ModuleResult>();

            foreach (BackupBase config in configs)
            {
                linkSubHeader.Text = "Restoring: " + config.Title;

                results.Add(await RestoreOne(config, consented));

                linkSubHeader.Text = "Choose settings";
            }

            return results;
        }

        /// <summary>
        /// Whether this module is actually going to overwrite anything, and so is worth snapshotting.
        /// </summary>
        /// <remarks>
        /// A module the user declined to close, or whose process refused to close, will be skipped or
        /// failed at dispatch. Snapshotting it would spend a browser-profile copy protecting files
        /// nothing is going to write to, and a locked profile would only manufacture a gate failure
        /// out of a restore that was never going to touch it.
        /// </remarks>
        private static bool WillOverwrite(BackupBase module, IReadOnlyList<string> consented,
                                          IDictionary<string, CloseResult> closedUpFront)
        {
            if (!module.RestoreMakesChanges)
                return false;

            IReadOnlyList<RestoreCloseRequirement> requirements =
                module.ProcessesToCloseBeforeRestore ?? new RestoreCloseRequirement[0];

            foreach (RestoreCloseRequirement requirement in requirements)
            {
                if (!requirement.NeedsConsent)
                    continue;

                if (!IsConsented(consented, requirement.ProcessName))
                    return false;

                CloseResult closed;

                if (closedUpFront.TryGetValue(requirement.ProcessName, out closed)
                    && (closed == CloseResult.StillRunning || closed == CloseResult.AccessDenied))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Takes the pre-restore snapshot and reports whether the restore may go ahead on it.
        /// </summary>
        private async Task<SnapshotDecision> TakeSnapshot(IReadOnlyList<BackupBase> snapshotSet,
                                                          string snapshotFolderPath)
        {
            if (snapshotFolderPath == null)
                return SnapshotGate.FolderNotCreated("a snapshot folder name could not be chosen");

            if (snapshotSet.Count == 0)
                return SnapshotGate.Evaluate(new List<ModuleOutcome>());

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

            RestorePlan plan = new RestorePlan(selectedConfigs, CurrentRestorePath,
                snapshotFolderPath ?? "(no snapshot folder could be named)");

            // Stage 2: informed consent, on the UI thread, before anything is created.
            IReadOnlyList<string> consented;

            using (RestoreConfirmForm confirm = new RestoreConfirmForm(plan))
            {
                if (confirm.ShowDialog(this) != DialogResult.OK)
                {
                    logger.LogMessage("Restore cancelled - nothing was changed.");
                    return;
                }

                consented = confirm.ConsentedProcessNames;
            }

            // Stage 3: close the consented processes once, up front, so the snapshot's own backup
            // does not prompt about the same browser the user has already answered for.
            Dictionary<string, CloseResult> closedUpFront =
                new Dictionary<string, CloseResult>(StringComparer.OrdinalIgnoreCase);

            foreach (string processName in consented)
            {
                string name = processName;
                CloseResult closed = await Task.Run(() => Utils.CloseProcess(name));

                closedUpFront[name] = closed;
                logger.LogMessage("Closing " + name + " before the restore: " + closed);
            }

            // Stages 4 and 5: snapshot, then decide whether the restore may go ahead on it.
            List<BackupBase> snapshotSet = selectedConfigs
                .Where(m => WillOverwrite(m, consented, closedUpFront))
                .ToList();

            SnapshotDecision snapshot = await TakeSnapshot(snapshotSet, snapshotFolderPath);

            logger.LogMessage(snapshot.Summary);

            if (snapshot.RequiresOverride)
            {
                DialogResult answer = MessageBox.Show(this,
                    snapshot.Describe() + "\r\n" + RestorePlan.FidelityCaveat +
                    "\r\n\r\nRestore anyway, without being able to undo it?",
                    "Pre-restore snapshot", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (answer != DialogResult.Yes)
                {
                    ShowSummary(RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Restore,
                        "the pre-restore snapshot did not complete and you chose not to continue."), "Restore");
                    return;
                }
            }

            // Stage 6.
            List<ModuleResult> results = await PerformRestoration(selectedConfigs, consented);

            // Stage 7.
            LogRestoredElements(selectedConfigs, results, snapshot, snapshotFolderPath);

            // Stage 8. Gated on a successful restore of a module that declares
            // RequiresExplorerRestart, not merely on the declaration: a module that failed or was
            // skipped never touched Explorer state, so offering to restart it would be a no-op
            // dressed up as a fix.
            bool requiresRestart = selectedConfigs
                .Zip(results, (config, result) => new { config, result })
                .Any(x => x.config.RequiresExplorerRestart && x.result.State == ResultState.Succeeded);

            btnRestartExplorer.Visible = requiresRestart;

            ShowSummary(
                RunSummary.For(ModuleOutcome.Pair(selectedConfigs, results), true, RunVerb.Restore),
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