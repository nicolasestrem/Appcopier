using DataHelper;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Views
{
    public partial class RestPageView : UserControl
    {
        private ConfPageView configPage;

        public RestPageView(ConfPageView cp)
        {
            InitializeComponent();
            configPage = cp;

            LoadBackups();
            SetStyle();
        }

        // Some UI nicety
        private void SetStyle()
        {
            // Segoe MDL2 Assets
            btnBack.Text = "\uE72B";
            // Some color styling
            BackColor =
            rtbLog.BackColor =
                Color.FromArgb(245, 241, 249);
        }

        internal void LoadBackups()
        {
            listRestoration.Items.Clear();

            if (Directory.Exists(Data.DataRootDir))
            {
                string[] backups = Directory.GetDirectories(Data.DataRootDir);

                foreach (string backup in backups)
                {
                    listRestoration.Items.Add(Path.GetFileName(backup));
                }
            }
        }

        private async void btnOK_Click(object sender, EventArgs e)
        {
            if (listRestoration.SelectedItems.Count != 1)
            {
                MessageBox.Show("Please select exactly one backup folder for restore.", "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            configPage.CurrentRestorePath = Data.DataRootDir + listRestoration.SelectedItem.ToString() + "\\";

            ViewHelper.SwitchView.SetMainFormAsView();

            // Call restoration logic after setting path
            await configPage.HandleRestorationAfterSelection();
        }

        private void btnBack_Click(object sender, EventArgs e)
           => ViewHelper.SwitchView.SetMainFormAsView();

        private void linkOpenBackupsDirectory_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
             => Process.Start(new ProcessStartInfo("explorer.exe", Data.DataRootDir) { UseShellExecute = true });

        private void listRestoration_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selectedBackupPath = listRestoration.SelectedItem?.ToString();

            if (string.IsNullOrEmpty(selectedBackupPath))
                return;

            string folder = Data.DataRootDir + selectedBackupPath;

            string backupLog = ReadLogOrNull(Path.Combine(folder, "backup_log.txt"));

            // A pre-restore snapshot folder also holds the log of the restore it was taken for. That
            // log names what the restore changed, so it is the reason someone would be looking at
            // this folder at all - showing only the backup half would hide the half they came for.
            string restoreLog = ReadLogOrNull(Path.Combine(folder, "restore_log.txt"));

            if (backupLog == null && restoreLog == null)
            {
                rtbLog.Text = "No backup log available for this backup.";
                return;
            }

            linkISubHeader.Visible = true;

            rtbLog.Text = backupLog == null ? restoreLog
                : restoreLog == null ? backupLog
                : backupLog + "\r\n\r\n" + restoreLog;
        }

        /// <summary>
        /// The log's text, or null when there is none to show. Never throws: an unreadable log must
        /// not stop the user picking the backup it sits beside.
        /// </summary>
        private static string ReadLogOrNull(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception ex)
            {
                return "Could not read " + Path.GetFileName(path) + ": " + ex.Message;
            }
        }
    }
}