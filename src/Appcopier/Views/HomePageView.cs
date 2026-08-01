using Appcopier;
using DataHelper;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Views
{
    /// <summary>
    /// Answers "am I okay?" - the last backup, what failed in it, and what can be undone.
    /// </summary>
    /// <remarks>
    /// The screen's whole value is that it may be trusted, so it is built around one rule: a status
    /// claim comes from backup_manifest.json or it is not made at all. A folder with no manifest, an
    /// unreadable one, or one TryParse refuses reads as "details unavailable" - never as a count, and
    /// never as a green tick. Every backup taken before the manifest existed is in that category, and
    /// inferring success for those is the cry-wolf failure running in the dangerous direction.
    ///
    /// Failure reasons are rendered verbatim, pinned above everything else, in read-only TextBoxes
    /// rather than Labels so they can be selected and pasted into a bug report. That is an honesty
    /// rule that happens to be implemented as a styling one.
    ///
    /// Laid out with TableLayoutPanel and Dock throughout, no absolute positions: PR 9 flips
    /// HighDpiMode to PerMonitorV2, and absolute coordinates do not survive a WM_DPICHANGED rescale.
    /// Built in code rather than in a Designer file because almost every row is conditional on what is
    /// on disk.
    /// </remarks>
    internal sealed class HomePageView : UserControl, IRefreshableView
    {
        private readonly Action<IReadOnlyList<string>> backUpAgain;
        private readonly Action<string> viewDetails;

        private readonly TableLayoutPanel rows;

        internal HomePageView(Action<IReadOnlyList<string>> backUpAgain, Action<string> viewDetails)
        {
            this.backUpAgain = backUpAgain;
            this.viewDetails = viewDetails;

            BackColor = Ui.Surface;
            Padding = new Padding(Ui.SpaceL);
            AutoScroll = true;

            rows = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1
            };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            Controls.Add(rows);

            RefreshView();
        }

        public void RefreshView()
        {
            rows.SuspendLayout();

            // Disposed, not merely removed: this runs on every visit to Home, and the rows own fonts
            // and brushes. Leaking a screenful of controls per navigation is the kind of thing that
            // only shows up after an hour of use.
            for (int i = rows.Controls.Count - 1; i >= 0; i--)
            {
                Control old = rows.Controls[i];
                rows.Controls.RemoveAt(i);
                old.Dispose();
            }

            try
            {
                Build();
            }
            catch (Exception ex)
            {
                // Home is the startup view. It reads the file system, and a denied or vanished
                // backup directory must degrade to a sentence rather than take the app down before
                // its window is usable.
                rows.Controls.Add(Line("This screen could not be built: " + ex.Message, Ui.Body(), Ui.Danger));
            }

            rows.ResumeLayout(true);
        }

        private void Build()
        {
            rows.Controls.Add(Line("This PC: " + Environment.MachineName, Ui.Title(), Color.Black));

            BackupFolders folders = BackupFolders.Read();

            if (folders.Backups.Count == 0)
                BuildNoBackups();
            else
                BuildLatestBackup(folders.Backups[0]);

            rows.Controls.Add(Separator());
            rows.Controls.Add(Line(DescribeUndoPoints(folders.Snapshots), Ui.Body(), Ui.Muted));
            rows.Controls.Add(Line(DescribeDisk(), Ui.Body(), Ui.Muted));
        }

        private void BuildNoBackups()
        {
            rows.Controls.Add(Line("No backups yet.", Ui.Heading(), Color.Black));
            rows.Controls.Add(Line("Nothing on this PC has been backed up with Appcopier.", Ui.Body(), Ui.Muted));
            rows.Controls.Add(Button("Back up this PC", (s, e) => backUpAgain(null)));
        }

        private void BuildLatestBackup(BackupFolder latest)
        {
            ManifestData manifest = latest.ReadManifest();

            rows.Controls.Add(Line("Last backup: " + Ago(latest.Created), Ui.Heading(), Color.Black));
            rows.Controls.Add(Line(latest.Name, Ui.Body(), Ui.Muted));

            if (manifest == null)
            {
                // Absent, unreadable, or refused by TryParse - all the same answer. Saying anything
                // else here would mean deriving a verdict from a file this app is not willing to
                // trust, which is the one thing the manifest exists to prevent.
                rows.Controls.Add(Line("Details unavailable for this backup.", Ui.BodyBold(), Color.Black));
                rows.Controls.Add(Line(
                    "It carries no readable record of what was captured - backups made before this "
                        + "version have none, and neither does a run that was interrupted. The backup "
                        + "itself is intact and can still be restored.",
                    Ui.Body(), Ui.Muted));
            }
            else
            {
                BuildManifestSummary(manifest);
            }

            rows.Controls.Add(Actions(latest, manifest));
        }

        private void BuildManifestSummary(ManifestData manifest)
        {
            List<ManifestModule> failed = new List<ManifestModule>();

            foreach (ManifestModule module in manifest.Modules)
            {
                if (module.State == BackupManifest.StateFailed)
                    failed.Add(module);
            }

            string counts = manifest.Modules.Count + " item" + (manifest.Modules.Count == 1 ? "" : "s");

            if (failed.Count == 0)
            {
                rows.Controls.Add(Line(counts + " · none failed", Ui.Body(), Color.Black));
                return;
            }

            rows.Controls.Add(Line(counts + " · " + failed.Count + " failed", Ui.BodyBold(), Ui.Danger));

            // Pinned above everything else and quoted verbatim. A rollup here would hide the only
            // text that says what actually went wrong.
            foreach (ManifestModule module in failed)
                rows.Controls.Add(Reason(module));
        }

        private Control Actions(BackupFolder latest, ManifestData manifest)
        {
            FlowLayoutPanel panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, Ui.SpaceM, 0, 0),
                Padding = new Padding(0)
            };

            panel.Controls.Add(Button("View details", (s, e) => viewDetails(latest.Name)));

            // With no manifest there is no list of what the run selected, so this is a plain
            // navigation. Guessing the selection from folder contents would re-tick items the user
            // never chose, on the screen whose button says "again".
            IReadOnlyList<string> types = manifest == null ? null : TypeNames(manifest);

            panel.Controls.Add(Button("Back up again", (s, e) => backUpAgain(types)));

            return panel;
        }

        private static IReadOnlyList<string> TypeNames(ManifestData manifest)
        {
            List<string> names = new List<string>(manifest.Modules.Count);

            foreach (ManifestModule module in manifest.Modules)
            {
                if (!string.IsNullOrEmpty(module.Type))
                    names.Add(module.Type);
            }

            return names;
        }

        // -----------------------------------------------------------------------------------------
        // Rendering helpers
        // -----------------------------------------------------------------------------------------

        private static Label Line(string text, Font font, Color color)
            => new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                AutoSize = true,
                MaximumSize = new Size(720, 0),
                Margin = new Padding(0, 0, 0, Ui.SpaceXs),
                Dock = DockStyle.Top
            };

        /// <summary>
        /// One failed module, as a selectable read-only row.
        /// </summary>
        /// <remarks>
        /// A TextBox and not a Label, on purpose: the reason is the text a user needs to paste into
        /// an issue, and Label text cannot be selected. ReadOnly rather than disabled so the caret
        /// and Ctrl+C still work; BorderStyle.None and the parent colour so it does not read as an
        /// input someone is meant to type into.
        /// </remarks>
        private static TextBox Reason(ManifestModule module)
            => new TextBox
            {
                Text = "! " + (module.Title ?? module.Type ?? "Unknown item") + " FAILED - "
                    + (module.Reason ?? "no reason was recorded"),
                Font = Ui.Body(),
                ForeColor = Ui.Danger,
                BackColor = Ui.Surface,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                Multiline = true,
                ScrollBars = ScrollBars.None,
                WordWrap = true,
                Height = 40,
                Width = 720,
                Margin = new Padding(Ui.SpaceM, 0, 0, Ui.SpaceXs),
                Dock = DockStyle.Top
            };

        private static Button Button(string text, EventHandler onClick)
        {
            Button button = new Button
            {
                Text = text,
                Font = Ui.Body(),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(Ui.SpaceM, Ui.SpaceXs, Ui.SpaceM, Ui.SpaceXs),
                Margin = new Padding(0, 0, Ui.SpaceS, 0),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = true
            };

            button.Click += onClick;

            return button;
        }

        private static Control Separator()
            => new Panel
            {
                Height = 1,
                Dock = DockStyle.Top,
                BackColor = Color.Gainsboro,
                Margin = new Padding(0, Ui.SpaceL, 0, Ui.SpaceM)
            };

        // -----------------------------------------------------------------------------------------
        // Wording
        // -----------------------------------------------------------------------------------------

        internal static string Ago(DateTime created)
        {
            int days = (int)(DateTime.Now.Date - created.Date).TotalDays;

            if (days <= 0)
                return "today";

            if (days == 1)
                return "yesterday";

            return days + " days ago";
        }

        private static string DescribeUndoPoints(IReadOnlyList<BackupFolder> snapshots)
        {
            if (snapshots.Count == 0)
                return "Undo points: none";

            return "Undo points: " + snapshots.Count + " pre-restore snapshot"
                + (snapshots.Count == 1 ? "" : "s")
                + " (newest " + snapshots[0].Created.ToString("d MMM yyyy") + ")";
        }

        private static string DescribeDisk()
        {
            try
            {
                DriveInfo drive = new DriveInfo(Path.GetPathRoot(Data.DataRootDir));

                return "Disk: " + (drive.AvailableFreeSpace / 1024 / 1024 / 1024) + " GB free on "
                    + drive.Name;
            }
            catch (Exception ex)
            {
                // A network or removed volume under the backup path. Naming the failure beats an
                // omitted line that reads as "plenty of room".
                return "Disk: free space unavailable (" + ex.Message + ")";
            }
        }
    }
}
