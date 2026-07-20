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

                if (!TryCreateBackupFolder(out createError))
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

                List<ModuleResult> results = new List<ModuleResult>();

                foreach (BackupBase a in selectedConfigs)
                {
                    linkSubHeader.Text = $"Backing up: {a.Title}";

                    ModuleResult outcome;

                    try
                    {
                        // Use asynchronous BackupAsync method and await its completion
                        outcome = await a.BackupAsync(CurrentBackupPath);
                    }
                    catch (Exception ex)
                    {
                        // Rule 6. Mandatory, not defensive style: this loop is driven by an
                        // async void click handler, so an escaping exception is unhandled and
                        // takes the process down along with every result gathered so far.
                        outcome = ModuleResult.Aggregate(new[]
                        {
                            StepResult.Failed(a.Title, "unhandled error: " + ex.GetType().Name + ": " + ex.Message)
                        });
                    }

                    results.Add(outcome);

                    linkSubHeader.Text = "Choose settings";
                }

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
        private bool TryCreateBackupFolder(out string error)
        {
            error = null;

            try
            {
                if (!Directory.Exists(CurrentBackupPath))
                    Directory.CreateDirectory(CurrentBackupPath);

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                logger.LogMessage("Could not create the backup folder " + CurrentBackupPath + ": " + ex.Message);
                return false;
            }
        }

        private static void ShowSummary(RunSummary summary, string caption)
        {
            logger.LogMessage(summary.Headline);
            logger.LogMessage(summary.Detail);

            MessageBox.Show(summary.Headline + "\r\n\r\n" + summary.Detail,
                caption, MessageBoxButtons.OK, summary.Icon);
        }

        // Write a backup_log.txt that records outcomes, not just the selection.
        private void LogBackedUpElements(string backupFolderPath, List<BackupBase> configurations, List<ModuleResult> results)
        {
            string logFilePath = Path.Combine(backupFolderPath, "backup_log.txt");

            try
            {
                string text = BackupLog.Compose(configurations, results, DateTime.Now.ToString());
                File.WriteAllText(logFilePath, text);
            }
            catch (Exception ex)
            {
                logger.LogMessage("Failed to create backup log file: " + ex.Message);
            }
        }

        // Restoration logic with selected configurations
        private async Task<List<ModuleResult>> PerformRestoration(List<BackupBase> selectedConfigs)
        {
            List<ModuleResult> results = new List<ModuleResult>();

            if (CurrentRestorePath != "" && Directory.Exists(CurrentRestorePath))
            {
                foreach (BackupBase config in selectedConfigs)
                {
                    ModuleResult outcome;

                    try
                    {
                        outcome = await config.RestoreAsync(CurrentRestorePath);
                    }
                    catch (Exception ex)
                    {
                        // Rule 6, and it matters more here than on the backup path: this method is
                        // awaited by HandleRestorationAfterSelection, which is itself awaited from
                        // an async void handler in RestPageView, and AppStoreApps.Restore opens a
                        // dialog from a thread-pool thread with no message pump.
                        outcome = ModuleResult.Aggregate(new[]
                        {
                            StepResult.Failed(config.Title, "unhandled error: " + ex.GetType().Name + ": " + ex.Message)
                        });
                    }

                    results.Add(outcome);
                }
            }

            return results;
        }

        // Asynchronous method to handle restoration after the user selects restoration path
        public async Task HandleRestorationAfterSelection()
        {
            bool ran = CurrentRestorePath != "" && Directory.Exists(CurrentRestorePath);

            List<ModuleResult> results = await PerformRestoration(selectedConfigs);

            // Gated on a successful restore of a module that declares RequiresExplorerRestart, not
            // merely on the declaration: a module that failed or was skipped never touched Explorer
            // state, so offering to restart it would be a no-op dressed up as a fix.
            bool requiresRestart = selectedConfigs
                .Zip(results, (config, result) => new { config, result })
                .Any(x => x.config.RequiresExplorerRestart && x.result.State == ResultState.Succeeded);

            // Show or hide restart button based on requirement
            btnRestartExplorer.Visible = requiresRestart;

            ShowSummary(
                RunSummary.For(ModuleOutcome.Pair(selectedConfigs, results), ran, RunVerb.Restore),
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

        private void btnRestartExplorer_Click(object sender, EventArgs e)
        {
            Utils.RestartExplorer();
            btnRestartExplorer.Visible = false;
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