using Appcopier;
using Conf;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Views
{
    /// <summary>
    /// Restore wizard step 2: contents &amp; portability. One row per module showing whether the
    /// backup holds it and what the manifest recorded, then the consent dialog and the in-page result.
    /// </summary>
    /// <remarks>
    /// Implements <see cref="IRunUi"/> exactly as the backup page does - same modal consent, same
    /// default-No snapshot override, results into its own <see cref="RunResultsPanel"/> - and owns a
    /// <see cref="BackupRestoreOrchestrator"/> so a restore runs here rather than back on the backup
    /// page. Default ticks every module the folder actually holds (restore-what's-in-it).
    /// </remarks>
    internal sealed partial class RestoreWizardStep2View : UserControl, IRunUi
    {
        private readonly NavigationService navigation;
        private readonly Action<bool> runStateChanged;
        private readonly BackupRestoreOrchestrator runner;

        private readonly Label headerLabel;
        private readonly Label provenanceBanner;
        private readonly FlowLayoutPanel rows;
        private readonly Label progressLabel;
        private readonly FlowLayoutPanel actionRow;
        private readonly Button btnBack;
        private readonly Button btnNext;
        private readonly RunResultsPanel resultsPanel;

        private BackupFolder folder;
        private readonly List<CheckBox> rowChecks = new List<CheckBox>();

        public RestoreWizardStep2View(NavigationService navigation, Action<bool> runStateChanged)
        {
            this.navigation = navigation;
            this.runStateChanged = runStateChanged;
            runner = new BackupRestoreOrchestrator(this);

            BackColor = Ui.Surface;
            AutoScroll = true;

            headerLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = Ui.Title(),
                ForeColor = Ui.TextPrimary,
                Margin = new Padding(Ui.SpaceM, Ui.SpaceM, Ui.SpaceM, Ui.SpaceXs),
                Text = "Restore contents",
            };

            provenanceBanner = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = Ui.BodyBold(),
                ForeColor = Ui.Caution,
                Margin = new Padding(Ui.SpaceM, 0, Ui.SpaceM, Ui.SpaceS),
                Visible = false,
            };

            progressLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = Ui.Body(),
                ForeColor = Ui.Muted,
                Margin = new Padding(Ui.SpaceM, 0, Ui.SpaceM, Ui.SpaceS),
                Text = "Choose what to restore",
            };

            rows = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(Ui.SpaceM),
            };

            btnBack = new Button { AutoSize = true, Font = Ui.Body(), Text = "\u2190 Back", UseVisualStyleBackColor = true };
            btnBack.Click += (s, e) => navigation.Pop();

            btnNext = new Button
            {
                AutoSize = true,
                Enabled = false,
                Font = Ui.BodyBold(),
                Text = "Next",
                UseVisualStyleBackColor = true,
            };
            btnNext.Click += btnNext_Click;

            // FlowLayoutPanel, not Panel. A plain Panel lays nothing out, so both buttons kept their
            // default (0,0) and Next - added first, so topmost in z-order - covered Back completely,
            // leaving no way back to the picker except the rail. Back is added first here so it
            // reads left-to-right.
            actionRow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(Ui.SpaceM),
                WrapContents = false,
            };
            actionRow.Controls.Add(btnBack);
            actionRow.Controls.Add(btnNext);

            resultsPanel = new RunResultsPanel { Dock = DockStyle.Top };

            Controls.Add(rows);
            Controls.Add(actionRow);
            Controls.Add(progressLabel);
            Controls.Add(provenanceBanner);
            Controls.Add(headerLabel);
            // resultsPanel is shown on top of the stack once a run reports (docked top, hidden until then).
            Controls.Add(resultsPanel);
        }

        internal void LoadFolder(BackupFolder folder)
        {
            this.folder = folder;
            rows.Controls.Clear();
            rowChecks.Clear();
            resultsPanel.Clear();
            resultsPanel.Visible = false;
            btnNext.Enabled = false;

            headerLabel.Text = "Restore from " + folder.Name;

            string provenance = RestoreContents.DescribeProvenance(
                folder.ReadManifest(), Environment.MachineName, Environment.UserName);
            provenanceBanner.Visible = provenance != null;
            provenanceBanner.Text = provenance ?? "";

            IReadOnlyList<BackupBase> modules = ModuleCatalog.CreateAll().Select(r => r.Module).ToList();
            IReadOnlyList<RestoreContentsRow> contents = RestoreContents.For(modules, folder.Path, folder.ReadManifest());

            foreach (RestoreContentsRow row in contents)
                rows.Controls.Add(MakeRow(row));

            RefreshNextEnabled();
        }

        private Control MakeRow(RestoreContentsRow row)
        {
            CheckBox check = new CheckBox
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = Ui.Body(),
                ForeColor = Ui.TextPrimary,
                Margin = new Padding(0, 0, Ui.SpaceS, 0),
                Text = row.Module.Title,
                Tag = row.Module,
                Enabled = row.HasBackup,
                Checked = row.HasBackup,
            };

            if (!row.HasBackup)
            {
                check.Checked = false;
                check.ForeColor = Ui.Muted;
                check.Text = row.Module.Title + "   (nothing in this backup)";
            }

            check.CheckedChanged += (s, e) => RefreshNextEnabled();
            rowChecks.Add(check);

            Control chip = MakeStateChip(row.ManifestState);

            TableLayoutPanel content = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = chip == null ? 1 : 2,
                RowCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
            };
            content.Controls.Add(check, 0, 0);
            if (chip != null)
            {
                content.Controls.Add(chip, 1, 0);
                content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            }
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            // Only for rows that can actually run. A warning describes what restoring this WOULD do,
            // which is noise under a row the folder holds nothing for - and expensive noise: before
            // the presence check was fixed nothing was ever greyed, so this never showed, and now
            // twelve inert warnings would push the one restorable row off the bottom of the screen.
            if (row.HasBackup && !string.IsNullOrEmpty(row.Warning))
            {
                TextBox warning = new TextBox
                {
                    BorderStyle = BorderStyle.None,
                    Dock = DockStyle.Top,
                    Font = Ui.Body(),
                    ForeColor = Ui.Caution,
                    Margin = new Padding(24, Ui.SpaceXs, 0, 0),
                    Multiline = true,
                    ReadOnly = true,
                    Text = "\u26A0 " + row.Warning,
                    Width = 360,
                };
                TableLayoutPanel wrap = new TableLayoutPanel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 1,
                    RowCount = 2,
                    Dock = DockStyle.Top,
                    Margin = new Padding(0, 0, 0, Ui.SpaceS),
                };
                wrap.Controls.Add(content, 0, 0);
                wrap.Controls.Add(warning, 0, 1);
                wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                wrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                wrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                return wrap;
            }

            content.Margin = new Padding(0, 0, 0, Ui.SpaceS);
            return content;
        }

        private static Control MakeStateChip(string state)
        {
            Color back;
            Color fore;
            string text;

            switch (state)
            {
                case BackupManifest.StateSucceeded:
                    back = Ui.ChipSucceededBack;
                    fore = Ui.ChipSucceededFore;
                    text = "OK in backup";
                    break;
                case BackupManifest.StateFailed:
                    back = Ui.ChipFailedBack;
                    fore = Ui.ChipFailedFore;
                    text = "failed in backup";
                    break;
                case BackupManifest.StateSkipped:
                    // Amber, never green.
                    back = Ui.ChipSkippedBack;
                    fore = Ui.ChipSkippedFore;
                    text = "skipped";
                    break;
                default:
                    // Unknown (no manifest, retired type, "unknown" state) shows no chip.
                    return null;
            }

            // AccentLabel so the theme walker steps over the chip instead of flattening it.
            return new AccentLabel
            {
                AutoSize = false,
                BackColor = back,
                BorderStyle = BorderStyle.None,
                Font = Ui.BodyBold(),
                ForeColor = fore,
                Margin = new Padding(0, 2, 0, 2),
                Size = new Size(112, 22),
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter,
            };
        }

        private void RefreshNextEnabled()
        {
            foreach (CheckBox check in rowChecks)
            {
                if (check.Enabled && check.Checked)
                {
                    btnNext.Enabled = true;
                    return;
                }
            }

            btnNext.Enabled = false;
        }

        private IReadOnlyList<BackupBase> SelectedModules()
        {
            List<BackupBase> selected = new List<BackupBase>();

            for (int i = 0; i < rowChecks.Count; i++)
            {
                CheckBox check = rowChecks[i];
                if (check.Checked && check.Tag is BackupBase module)
                    selected.Add(module);
            }

            return selected;
        }

        private async void btnNext_Click(object sender, EventArgs e)
        {
            // Deleted between step 1 and Next: fail back to the picker rather than into a restore.
            if (folder == null || !Directory.Exists(folder.Path))
            {
                MessageBox.Show(this, "This backup folder no longer exists.", "Restore",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                navigation.Pop();
                return;
            }

            IReadOnlyList<BackupBase> selection = SelectedModules();
            if (selection.Count == 0)
                return;

            btnNext.Enabled = false;
            Enabled = false;
            runStateChanged?.Invoke(true);

            try
            {
                await runner.RunRestore(selection, folder.Path + "\\");
            }
            finally
            {
                Enabled = true;
                // Next has to come back too - the backup page already does this for its own button.
                // Cancelling the consent dialog returns here normally, and without this the user
                // lands back on their still-valid ticked list with the only way forward greyed out,
                // recoverable only by leaving the wizard and re-picking the folder. Via
                // RefreshNextEnabled rather than a bare true so an empty selection stays disabled.
                RefreshNextEnabled();
                runStateChanged?.Invoke(false);
            }
        }

        // ---------------------------------------------------------------------------------------------
        //  IRunUi - identical contract to the backup page: consent stays modal and Cancel-focused,
        //  the snapshot override defaults to No, results render in-page.
        // ---------------------------------------------------------------------------------------------

        void IRunUi.SetProgressText(string text) => progressLabel.Text = text;

        IWin32Window IRunUi.Owner => FindForm();

        void IRunUi.ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes)
        {
            LogHelper.Instance.LogMessage(summary.Headline);
            LogHelper.Instance.LogMessage(summary.Detail);
            resultsPanel.ShowRun(summary, caption, outcomes);
        }

        IReadOnlyList<string> IRunUi.ShowConsentDialog(RestorePlan plan)
        {
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
    }
}
