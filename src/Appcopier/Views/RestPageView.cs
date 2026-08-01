using Appcopier;
using DataHelper;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Views
{
    public partial class RestPageView : UserControl, IRefreshableView
    {
        private readonly ConfPageView configPage;
        private readonly NavigationService navigation;

        /// <summary>
        /// A folder name to select once the list has been rebuilt, or null.
        /// </summary>
        /// <remarks>
        /// Deferred rather than applied directly, because the list is reloaded every time this view
        /// is shown and that reload clears the selection. Home asks for a folder BEFORE navigating;
        /// the request is honoured on the far side of the refresh.
        /// </remarks>
        private string pendingSelection;

        internal RestPageView(ConfPageView cp, NavigationService navigation)
        {
            InitializeComponent();
            configPage = cp;
            this.navigation = navigation;

            LoadBackups();
            SetStyle();
        }

        /// <summary>
        /// Re-reads the backup directory. Called by NavigationService on every visit.
        /// </summary>
        /// <remarks>
        /// This view used to be constructed fresh on each navigation, so its constructor's
        /// LoadBackups was also its refresh. It is now a reused instance, and without this a backup
        /// taken after startup would not appear in the list until the app was restarted - the folder
        /// the user just created being precisely the one they are most likely to want.
        /// </remarks>
        public void RefreshView()
        {
            LoadBackups();

            // The list reload clears the selection, but the log pane and its header are not part of
            // the list and would otherwise still show the folder chosen on a previous visit - beside
            // an empty selection, and possibly for a folder since deleted. Reset them so the screen
            // never describes a backup the user is not looking at.
            rtbLog.Clear();
            linkISubHeader.Visible = false;

            string wanted = pendingSelection;
            pendingSelection = null;

            ApplySelection(wanted);
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

        /// <summary>
        /// Fills the list from the backup directory. Total: it never throws.
        /// </summary>
        /// <remarks>
        /// This runs from the constructor, and the constructor now runs from MainForm's, which is
        /// evaluated as the ARGUMENT to Application.Run - outside the message pump, where an
        /// exception is not caught by WinForms and kills the process before any window exists. The
        /// user would see the app fail to start. Directory.GetDirectories genuinely throws for an
        /// unreadable root: a changed ACL, or the executable sitting on a volume that has gone away.
        /// Reporting an empty list plus a log line is the same degradation OsHelper.GetVersion
        /// makes for the same reason.
        /// </remarks>
        internal void LoadBackups()
        {
            listRestoration.Items.Clear();

            try
            {
                if (!Directory.Exists(Data.DataRootDir))
                    return;

                foreach (string backup in Directory.GetDirectories(Data.DataRootDir))
                {
                    listRestoration.Items.Add(Path.GetFileName(backup));
                }
            }
            catch (Exception ex)
            {
                LogHelper.Instance.LogMessage(
                    "The backup folder " + Data.DataRootDir + " could not be read: " + ex.Message);
            }
        }

        /// <summary>
        /// Selects a backup folder by name, if it is still in the list.
        /// </summary>
        /// <remarks>
        /// Silent when the name is absent rather than throwing or clearing: Home offers this for the
        /// newest folder it saw, and the folder can be renamed or deleted in Explorer between Home
        /// reading the directory and the user clicking through. Leaving nothing selected puts the
        /// user in front of the list they were going to see anyway.
        /// </remarks>
        internal void SelectBackup(string folderName)
        {
            pendingSelection = folderName;

            // Also applied immediately, so the method still works on a view that is already on
            // screen rather than only on the way in.
            ApplySelection(folderName);
        }

        private void ApplySelection(string folderName)
        {
            if (string.IsNullOrEmpty(folderName))
                return;

            int index = listRestoration.Items.IndexOf(folderName);

            if (index >= 0)
                listRestoration.SelectedIndex = index;
        }

        private async void btnOK_Click(object sender, EventArgs e)
        {
            if (listRestoration.SelectedItems.Count != 1)
            {
                MessageBox.Show("Please select exactly one backup folder for restore.", "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // The module SET is chosen on the backup page and the FOLDER here. Enforced at the point
            // the restore actually starts rather than only on the paths that lead here: this is the
            // single place HandleRestorationAfterSelection is invoked, so guarding it covers every
            // entry route - the rail, the backup page's own button, and Home's "View details",
            // which reaches this view for reading and not for restoring. Without it, OK from a
            // fresh launch walks the user through snapshot creation and the consent dialog to
            // restore nothing at all.
            if (!configPage.TryCollectRestoreSelection())
            {
                // Navigate first, so the warning is dismissed onto the page that answers it.
                navigation.Show(configPage);

                MessageBox.Show("Please choose a configuration to restore beforehand.", "",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            configPage.CurrentRestorePath = Data.DataRootDir + listRestoration.SelectedItem.ToString() + "\\";

            // Back to the backup page, which owns the log pane the restore reports progress into.
            navigation.Show(configPage);

            // Call restoration logic after setting path
            await configPage.HandleRestorationAfterSelection();
        }

        private void btnBack_Click(object sender, EventArgs e)
           => navigation.Pop();

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