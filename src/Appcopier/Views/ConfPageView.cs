using Appcopier;
using Conf;
using DataHelper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
        /// control as the UI surface.
        /// </summary>
        private readonly BackupRestoreOrchestrator runner;

        // root TableLayoutPanel row indices for the collapsible sections.
        private const int ResultsRow = 4;
        private const int LogRow = 6;

        private bool isSelectAll = false;

        public ConfPageView()
        {
            InitializeComponent();
            InitializeConfigurations();
            SetStyle();

            runner = new BackupRestoreOrchestrator(this);

            // A hidden docked/row control must not reserve space. Both results and the activity log
            // are collapsed by default and expand only when shown, so the row they live in follows.
            resultsPanel.VisibleChanged += (s, e) => SetRowCollapsed(root, ResultsRow, !resultsPanel.Visible);
            rtbLog.VisibleChanged += (s, e) => SetRowCollapsed(root, LogRow, !rtbLog.Visible);
            rtbLog.Visible = false;
            SetRowCollapsed(root, ResultsRow, !resultsPanel.Visible);
        }

        private void SetStyle()
        {
            BackColor = Ui.Surface;

            headerLabel.Font = Ui.Title();
            headerLabel.ForeColor = Color.Black;

            linkSubHeader.Font = Ui.BodyBold();
            linkSubHeader.ForeColor = Color.Black;

            treeConfigurations.Font = Ui.Body();

            txtInfo.Font = Ui.Body();
            txtInfo.BackColor = Ui.Surface;
            txtInfo.ForeColor = Color.Black;

            logToggle.Font = Ui.Body();

            // Segoe MDL2 Assets glyphs on the icon buttons.
            btnMenuMore.Text = "\uE712";
            btnMenuRestore.Text = "\uE777";

            rtbLog.BackColor = Ui.Surface;
            txtInfo.Text = string.Format(IntroTemplate, OsHelper.GetVersion());
            logger.SetTarget(rtbLog);
        }

        private void InitializeConfigurations()
        {
            foreach (ModuleRegistration registration in ModuleCatalog.CreateAll())
            {
                AddConfiguration(registration.Module, registration.Category);
            }
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
                // Input validation, not a result: this stays a MessageBox.
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
        //  IRunUi - the UI surface the orchestrator talks back through. Results render in-page via
        //  resultsPanel; the summary MessageBox is gone. Consent prompts stay modal (consent modality
        //  is the feature; what was removed is modal spam).
        // ---------------------------------------------------------------------------------------------

        void IRunUi.SetProgressText(string text) => linkSubHeader.Text = text;

        IWin32Window IRunUi.Owner => FindForm();

        void IRunUi.ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes)
        {
            // Keep the log record (it is no longer the primary surface, but it is still the audit
            // trail), then render in-page. No MessageBox: a 24-ok/1-failed run must not read as green.
            logger.LogMessage(summary.Headline);
            logger.LogMessage(summary.Detail);

            resultsPanel.ShowRun(summary, caption, outcomes);
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

        void IRunUi.SetExplorerRestartVisible(bool visible) => resultsPanel.SetExplorerRestartVisible(visible);

        private void btnRestore_Click(object sender, EventArgs e)
        {
            if (TryCollectRestoreSelection())
            {
                ShowRestoreView();
                return;
            }

            // Input validation, not a result: this stays a MessageBox.
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

        // The info pane replaces the rtbLog dual-use for browsing: selecting a node shows its title,
        // info, version and - inline, no modal - its warning. Nothing consent-relevant is lost: the
        // consent dialog still re-carries every warning via RestorePlan.
        private void treeConfigurations_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (treeConfigurations.SelectedNode != null &&
                treeConfigurations.SelectedNode.Tag is BackupBase selected)
            {
                string warning = selected.WarningMessage;

                txtInfo.Text = selected.Title + "\r\n\r\n" +
                               selected.Info + "\r\n" +
                               selected.Version +
                               (string.IsNullOrEmpty(warning) ? "" : "\r\n\r\n\u26A0 " + warning);
            }
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

        private void treeConfigurations_AfterCheck(object sender, TreeViewEventArgs e)
        {
            foreach (TreeNode child in e.Node.Nodes)
            {
                child.Checked = e.Node.Checked;
            }
        }

        private void logToggle_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            rtbLog.Visible = !rtbLog.Visible;
        }

        private void menuOpenAppBackups_Click(object sender, EventArgs e)
        {
            RestAppsForm f = new RestAppsForm();
            f.ShowDialog();
        }

        private void menuOpenBackupFolder_Click(object sender, EventArgs e)
           => Process.Start(new ProcessStartInfo("explorer.exe", DataHelper.Data.DataRootDir) { UseShellExecute = true });

        /// <summary>
        /// Collapses (or expands) a row of a <see cref="TableLayoutPanel"/> by switching its row
        /// style, so a hidden results panel or activity log does not reserve blank space.
        /// </summary>
        private static void SetRowCollapsed(TableLayoutPanel tlp, int row, bool collapsed)
        {
            tlp.RowStyles[row].SizeType = collapsed ? SizeType.Absolute : SizeType.AutoSize;
            if (collapsed)
                tlp.RowStyles[row].Height = 0;
        }
    }
}
