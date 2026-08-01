using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Views;

namespace Appcopier
{
    /// <summary>
    /// The shell: a left rail and a content host. It owns the views and the navigation, nothing else.
    /// </summary>
    /// <remarks>
    /// Rail entries are constructed once and reused rather than rebuilt on each visit. That is what
    /// makes ConfPageView's checkbox selection, its log pane and its LogHelper target survive a trip
    /// to Home and back - the same lifetime the single configPage field already had before the rail
    /// existed. Home is the exception in spirit only: the instance is reused, but it re-reads the
    /// backup directory every time it is shown, via IRefreshableView.
    ///
    /// Gone with this change: the wallpaper splash (an Image.FromFile of the user's desktop background,
    /// shown for a hardcoded 500 ms Task.Delay before the real UI appeared) and the QR-code easter egg
    /// with its timer, hover handlers and MessageBox. Both were removed by the design rather than
    /// broken. The update check and its version checkbox are untouched.
    /// </remarks>
    public partial class MainForm : Form
    {
        private readonly NavigationService navigation;

        private readonly ConfPageView configPage;
        private readonly HomePageView homePage;
        private readonly RestPageView restorePage;

        /// <summary>
        /// Built on first use, unlike the rail pages.
        /// </summary>
        /// <remarks>
        /// Its constructor fetches the GitHub stargazer count. Building it up front would put a
        /// network request in every app start for a page most sessions never open - and the old
        /// code, which built it inside the click handler, did not.
        /// </remarks>
        private AboutPageView aboutPage;

        public MainForm()
        {
            InitializeComponent();

            navigation = new NavigationService(pnlForm);

            configPage = new ConfPageView();
            restorePage = new RestPageView(configPage, navigation);

            homePage = new HomePageView(GoToBackUp, GoToRestoreFor);

            // ConfPageView's own Restore button picks the module SET; the page that follows picks the
            // folder. A delegate rather than a reference to this form: the view needs one navigation,
            // not the shell.
            configPage.ShowRestoreView = () => navigation.Push(restorePage);

            navigation.Root = homePage;

            SetStyle();
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            checkVersion.Text = GetMinorVersion(Program.GetCurrentVersionTostring());
            lblDiskSpace.Text = Utils.GetSystemPartitionDiskSpaceInfo();

            navigation.Show(homePage);
        }

        private void SetStyle()
        {
            BackColor = Ui.Surface;
            pnlRail.BackColor = Ui.RailSurface;
            pnlStatusBar.BackColor = Ui.RailSurface;

            // The form's own Font is deliberately NOT set. Assigning it here cascades into every
            // child that inherits, and the hosted UserControls scale their layout against the font
            // they were designed with - ConfPageView's tree and log pane collapsed to a narrow
            // column when it was set. Fonts are applied per control instead, and PR 9's theme pass
            // is where a coordinated sweep belongs.

            lblDiskSpace.Font = Ui.Body();
            lblDiskSpace.ForeColor = Ui.Muted;

            checkVersion.Font = Ui.Body();
            checkVersion.ForeColor = Color.Black;
            checkVersion.BackColor = Ui.RailSurface;
            checkVersion.FlatAppearance.CheckedBackColor = Ui.RailSurface;

            // Text only. A Button draws its whole caption in one font, so a Segoe Fluent Icons glyph
            // prefixed to a word either renders the word in the icon font or renders the glyph as a
            // fallback square - which is exactly what it did. Icons on the rail are PR 9's problem,
            // when the labels can carry a real glyph control beside them.
            StyleRailButton(btnHome, "Home");
            StyleRailButton(btnBackUp, "Back up");
            StyleRailButton(btnRestore, "Restore");
            StyleRailButton(btnAbout, "About");
        }

        private static void StyleRailButton(Button button, string text)
        {
            button.Text = text;
            button.Font = Ui.Body();
            button.ForeColor = Color.Black;
            button.BackColor = Ui.RailSurface;
            button.FlatAppearance.BorderSize = 0;
        }

        // -----------------------------------------------------------------------------------------
        // Rail
        // -----------------------------------------------------------------------------------------

        private void btnHome_Click(object sender, EventArgs e)
            => navigation.Show(homePage);

        private void btnBackUp_Click(object sender, EventArgs e)
            => navigation.Show(configPage);

        /// <summary>
        /// Restore from the rail, which still has to pass through choosing what to restore.
        /// </summary>
        /// <remarks>
        /// The restore SET is chosen in ConfPageView and the FOLDER in RestPageView, in that order -
        /// RestPageView's OK button runs the restore against configPage.selectedConfigs. Sending the
        /// rail straight to the folder picker would therefore offer to run a restore of nothing.
        /// So the rail asks the backup page for its current selection first and, when there is none,
        /// lands the user on the page where the choice is made, with the same message that button has
        /// always shown.
        /// </remarks>
        private void btnRestore_Click(object sender, EventArgs e)
        {
            if (configPage.TryCollectRestoreSelection())
            {
                navigation.Show(restorePage);
                return;
            }

            navigation.Show(configPage);

            MessageBox.Show("Please choose a configuration to restore beforehand.", "",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void btnAbout_Click(object sender, EventArgs e)
        {
            if (aboutPage == null)
                aboutPage = new AboutPageView(navigation);

            navigation.Push(aboutPage);
        }

        // -----------------------------------------------------------------------------------------
        // Home's actions
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Home's "Back up again": go to the backup page, re-ticking what the last run recorded.
        /// </summary>
        /// <remarks>
        /// A null list means the last backup had no readable manifest, so there is nothing to
        /// re-select and the navigation is plain. Inventing a selection there would tick items the
        /// user never chose.
        /// </remarks>
        private void GoToBackUp(IReadOnlyList<string> moduleTypeNames)
        {
            if (moduleTypeNames != null)
                configPage.SelectModulesByTypeName(moduleTypeNames);

            navigation.Show(configPage);
        }

        /// <summary>
        /// Home's "View details": open the backup list with that folder selected.
        /// </summary>
        /// <remarks>
        /// Reading, not restoring - so no module selection is required to get here, and none is
        /// invented. The restore itself is gated inside RestPageView's OK, which is the single place
        /// it can start; a gate on this navigation would refuse to SHOW a backup because of what the
        /// user had not yet ticked.
        ///
        /// The list is reloaded by NavigationService through IRefreshableView on the way in, which
        /// is why the selection is requested first and applied on the far side of that refresh.
        /// </remarks>
        private void GoToRestoreFor(string backupFolderName)
        {
            restorePage.SelectBackup(backupFolderName);

            navigation.Show(restorePage);
        }

        // -----------------------------------------------------------------------------------------
        // Version and update check - unchanged behavior
        // -----------------------------------------------------------------------------------------

        private string GetMinorVersion(string version)
        {
            // Display everything until the second dot without the dot
            int secondDotIndex = version.IndexOf('.', version.IndexOf('.') + 1);
            if (secondDotIndex != -1)
            {
                version = version.Substring(0, secondDotIndex);
            }
            return $"Version {version}";
        }

        private void checkVersion_CheckedChanged(object sender, EventArgs e)
        {
            // Get full version
            string fullVersion = Program.GetCurrentVersionTostring();

            // Display version based on the CheckBox state
            checkVersion.Text = checkVersion.Checked ? fullVersion : GetMinorVersion(fullVersion);

            // Optionally, check for updates when checked
            if (checkVersion.Checked)
            {
                UpdateCheck.CheckForUpdates();
            }
        }
    }
}
