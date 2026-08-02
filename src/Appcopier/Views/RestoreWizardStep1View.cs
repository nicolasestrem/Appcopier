using Appcopier;
using DataHelper;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Views
{
    /// <summary>
    /// Restore wizard step 1: pick a backup (or an Undo point) as the restore source. Cards list
    /// Backups and Snapshots separately, newest first.
    /// </summary>
    internal sealed partial class RestoreWizardStep1View : UserControl, IRefreshableView
    {
        private readonly NavigationService navigation;
        private readonly Action<BackupFolder> onPicked;
        private readonly Action goToBackup;

        private readonly Label headerLabel;
        private readonly FlowLayoutPanel cards;
        private readonly Button btnBack;

        public RestoreWizardStep1View(NavigationService navigation, Action<BackupFolder> onPicked, Action goToBackup)
        {
            this.navigation = navigation;
            this.onPicked = onPicked;
            this.goToBackup = goToBackup;

            BackColor = Ui.Surface;
            AutoScroll = true;

            headerLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = Ui.Title(),
                ForeColor = Color.Black,
                Text = "Restore from a backup",
                Margin = new Padding(Ui.SpaceM, Ui.SpaceM, Ui.SpaceM, Ui.SpaceS),
            };

            btnBack = new Button
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = Ui.Body(),
                Text = "\u2190 Back",
                UseVisualStyleBackColor = true,
                Margin = new Padding(Ui.SpaceM, 0, Ui.SpaceM, 0),
            };
            btnBack.Click += (s, e) => navigation.Pop();

            cards = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(Ui.SpaceM),
            };

            Controls.Add(cards);
            Controls.Add(headerLabel);
            Controls.Add(btnBack);
        }

        void IRefreshableView.RefreshView(ViewEntry entry)
        {
            cards.Controls.Clear();

            BackupFolders folders = BackupFolders.Read();

            if (folders.UnreadableReason != null)
            {
                cards.Controls.Add(MakeNote(folders.UnreadableReason));
                return;
            }

            if (folders.Backups.Count == 0 && folders.Snapshots.Count == 0)
            {
                Panel empty = new Panel { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, Ui.SpaceS, 0, 0) };
                Label note = new Label
                {
                    AutoSize = true,
                    Dock = DockStyle.Top,
                    Font = Ui.Body(),
                    ForeColor = Ui.Muted,
                    Text = "No backups yet.",
                };
                LinkLabel link = new LinkLabel
                {
                    AutoSize = true,
                    Dock = DockStyle.Top,
                    Font = Ui.Body(),
                    LinkColor = Color.Black,
                    Text = "Go to Back up to create one",
                    Margin = new Padding(0, Ui.SpaceXs, 0, 0),
                };
                link.LinkClicked += (s, e) => goToBackup?.Invoke();
                empty.Controls.Add(link);
                empty.Controls.Add(note);
                cards.Controls.Add(empty);
                return;
            }

            if (folders.Backups.Count > 0)
            {
                cards.Controls.Add(MakeSection("Backups"));
                foreach (BackupFolder folder in folders.Backups)
                    cards.Controls.Add(MakeCard(folder, undoPoint: false));
            }

            if (folders.Snapshots.Count > 0)
            {
                cards.Controls.Add(MakeSection("Undo points"));
                foreach (BackupFolder folder in folders.Snapshots)
                    cards.Controls.Add(MakeCard(folder, undoPoint: true));
            }
        }

        private static Label MakeSection(string title)
            => new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = Ui.BodyBold(),
                ForeColor = Ui.Muted,
                Margin = new Padding(0, Ui.SpaceM, 0, Ui.SpaceXs),
                Text = title,
            };

        private static Control MakeNote(string text)
            => new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = Ui.Body(),
                ForeColor = Ui.Muted,
                Margin = new Padding(0, Ui.SpaceS, 0, 0),
                Text = text,
            };

        private Control MakeCard(BackupFolder folder, bool undoPoint)
        {
            ManifestData manifest = folder.ReadManifest();

            string title = undoPoint ? "Undo point" : folder.Name;
            string created = folder.Created == DateTime.MinValue ? "unknown date" : folder.Created.ToString("yyyy-MM-dd HH.mm");
            string summary = undoPoint
                ? "Pre-restore snapshot \u2013 restores it to roll back the change it recorded."
                : DescribeManifest(manifest);

            Button card = new Button
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.FromArgb(250, 250, 250),
                Dock = DockStyle.Top,
                FlatStyle = FlatStyle.Flat,
                Font = Ui.Body(),
                ForeColor = Color.Black,
                Margin = new Padding(0, 0, 0, Ui.SpaceS),
                Padding = new Padding(Ui.SpaceM),
                Tag = folder,
                TextAlign = ContentAlignment.TopLeft,
                Text = title + "\r\n" + created + "\r\n" + summary,
            };
            card.FlatAppearance.BorderColor = Color.FromArgb(220, 220, 220);
            card.Click += (s, e) => onPicked(folder);

            return card;
        }

        private static string DescribeManifest(ManifestData manifest)
        {
            if (manifest == null)
                return "details unavailable";

            int items = manifest.Modules == null ? 0 : manifest.Modules.Count;
            int failed = 0;

            if (manifest.Modules != null)
            {
                foreach (ManifestModule module in manifest.Modules)
                {
                    if (module != null && module.State == BackupManifest.StateFailed)
                        failed++;
                }
            }

            string badge = "from " + (string.IsNullOrEmpty(manifest.MachineName) ? "?" : manifest.MachineName)
                         + " / " + (string.IsNullOrEmpty(manifest.UserName) ? "?" : manifest.UserName);

            return items + " item(s)" + (failed > 0 ? " \u00b7 " + failed + " failed" : "") + "   " + badge;
        }
    }
}
