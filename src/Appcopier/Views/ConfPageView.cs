using Appcopier;
using Conf;
using DataHelper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Views
{
    public partial class ConfPageView : UserControl, IRunUi
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

        /// <summary>
        /// Runs a backup or restore, routing every progress update, result, consent prompt and
        /// Explorer-restart toggle back here through <see cref="IRunUi"/>. Constructed once with this
        /// control as the UI surface; Phase 4 PR 5 moved the orchestration bodies out verbatim.
        /// </summary>
        private readonly BackupRestoreOrchestrator runner;

        private bool isSelectAll = false;

        public ConfPageView()
        {
            InitializeComponent();
            InitializeConfigurations();
            SetStyle();
            runner = new BackupRestoreOrchestrator(this);
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
            AddConfiguration(new WMappedDrives(), "Settings");
            AddConfiguration(new WUpdates(), "Settings");
            AddConfiguration(new WPowerPlans(), "Settings");
            AddConfiguration(new WThemes(), "Settings");
            AddConfiguration(new WFonts(), "Settings");
            AddConfiguration(new WAccessibility(), "Settings");
            AddConfiguration(new WRegional(), "Settings");
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

            // Directly after EEnvironment on purpose: the two read one key and differ only in what
            // they keep, and the tree checkbox is the opt-in. Adjacent rows are what makes that a
            // choice the user can see rather than one buried in the list.
            AddConfiguration(new EEnvironmentFiltered(), "Developer");

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

        /// <summary>
        /// Raised with true while a backup or restore is running, and false when it ends.
        /// </summary>
        /// <remarks>
        /// This page disables ITSELF for the duration, which was sufficient when every control that
        /// could touch its state lived on it. The navigation rail does not: it stays live on the
        /// form while this control is disabled, so its buttons could navigate away mid-run or reach
        /// back into this page while a run was suspended at an await. The shell listens here and
        /// shuts the rail for the duration.
        /// </remarks>
        internal Action<bool> RunStateChanged = _ => { };

        private async void btnBackup_Click(object sender, EventArgs e)
        {
            btnBackup.Enabled = false;
            this.Enabled = false;
            RunStateChanged(true);

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
                RunStateChanged(false);
            }
        }

        private async Task RunBackup()
        {
            selectedConfigs.Clear();

            bool isAtLeastOneChecked = treeConfigurations.Nodes
                .Cast<TreeNode>()
                .Any(parentNode => parentNode.Nodes.Cast<TreeNode>().Any(childNode => childNode.Checked));

            // At least one node is checked, then proceed!
            if (!isAtLeastOneChecked)
            {
                MessageBox.Show("Nothing has been selected for backup. Please choose your settings to be backed up beforehand.", "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

            await runner.RunBackup(selectedConfigs, CurrentBackupPath);
        }

        // Asynchronous method to handle restoration after the user selects restoration path
        public async Task HandleRestorationAfterSelection()
        {
            // The window is disabled for the whole run. Without it a second restore can be started
            // while the first is still writing, and the snapshot this phase adds makes a restore
            // long enough for that to be reachable by hand rather than merely possible.
            this.Enabled = false;
            RunStateChanged(true);

            try
            {
                await runner.RunRestore(selectedConfigs, CurrentRestorePath);
            }
            finally
            {
                this.Enabled = true;
                RunStateChanged(false);
            }
        }

        // ---------------------------------------------------------------------------------------------
        //  IRunUi - the UI surface the orchestrator talks back through. Every member is a direct
        //  replacement for the inline view code Phase 4 PR 5 moved out; zero visual change.
        // ---------------------------------------------------------------------------------------------

        void IRunUi.SetProgressText(string text) => linkSubHeader.Text = text;

        IWin32Window IRunUi.Owner => FindForm();

        void IRunUi.ShowSummary(RunSummary summary, string caption)
        {
            logger.LogMessage(summary.Headline);
            logger.LogMessage(summary.Detail);

            MessageBox.Show(summary.Headline + "\r\n\r\n" + summary.Detail,
                caption, MessageBoxButtons.OK, summary.Icon);
        }

        IReadOnlyList<string> IRunUi.ShowConsentDialog(RestorePlan plan)
        {
            // The owner is the Form, not this control: a UserControl is not something CenterParent
            // can centre on, and a modal owned by a control this pipeline has already disabled is
            // the shape that fails to come forward.
            Form owner = FindForm();

            using (RestoreConfirmForm confirm = new RestoreConfirmForm(plan))
            {
                if (confirm.ShowDialog(owner) != DialogResult.OK)
                    return null;

                return confirm.ConsentedProcessNames;
            }
        }

        bool IRunUi.ConfirmSnapshotOverride(string text, string caption)
            => MessageBox.Show(FindForm(), text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;

        void IRunUi.ShowPlanCompositionError(string text, string caption)
            => MessageBox.Show(FindForm(), text, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);

        void IRunUi.SetExplorerRestartVisible(bool visible) => btnRestartExplorer.Visible = visible;

        private void btnRestore_Click(object sender, EventArgs e)
        {
            if (TryCollectRestoreSelection())
            {
                ShowRestoreView();
                return;
            }

            MessageBox.Show("Please choose a configuration to restore beforehand.", "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// Navigates to the folder picker. Supplied by the shell, which owns that view.
        /// </summary>
        /// <remarks>
        /// A delegate rather than a NavigationService reference, matching how the engine's other
        /// UI seams are wired: this page needs exactly one navigation, and handing it the whole
        /// service would let any future edit here reach every screen in the app.
        /// </remarks>
        internal Action ShowRestoreView = () => { };

        /// <summary>
        /// Populates <see cref="selectedConfigs"/> from the ticked tree nodes, reporting whether
        /// anything was ticked at all.
        /// </summary>
        /// <remarks>
        /// The restore SET is chosen here and the FOLDER on the next page, and RestPageView's OK
        /// button runs the restore against this list - so this must have succeeded before that page
        /// is shown, whether the user got there from this page's button or from the rail. Returning
        /// a bool rather than showing the warning itself: the two callers land the user in different
        /// places, and only one of them is already on this page.
        /// </remarks>
        internal bool TryCollectRestoreSelection()
        {
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

            return selectedConfigs.Count > 0;
        }

        /// <summary>
        /// Ticks exactly the modules named by their CLR type name, unticking everything else.
        /// </summary>
        /// <remarks>
        /// Drives Home's "Back up again" from the names in a backup_manifest.json. Unknown names are
        /// ignored in silence and that is deliberate, not lax: a manifest written by an older build
        /// can name a module this one retired - the browser modules Phase 3a removed, for instance -
        /// and a warning about it would be an error message about someone else's past decision, on a
        /// button whose entire promise is "the same as last time".
        ///
        /// Exact, ordinal type-name match. Titles are user-facing prose and have been reworded
        /// between releases; type names are what the manifest records for precisely this reason.
        /// </remarks>
        internal void SelectModulesByTypeName(IReadOnlyList<string> moduleTypeNames)
        {
            HashSet<string> wanted = new HashSet<string>(StringComparer.Ordinal);

            if (moduleTypeNames != null)
            {
                foreach (string name in moduleTypeNames)
                {
                    if (!string.IsNullOrEmpty(name))
                        wanted.Add(name);
                }
            }

            foreach (TreeNode parentNode in treeConfigurations.Nodes)
            {
                foreach (TreeNode childNode in parentNode.Nodes)
                {
                    BackupBase configuration = childNode.Tag as BackupBase;

                    childNode.Checked = configuration != null
                        && wanted.Contains(configuration.GetType().Name);
                }
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
