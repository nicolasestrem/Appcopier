using Appcopier;
using DataHelper;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Views
{
    public partial class RestAppsForm : Form
    {
        private List<string> packageIdentifiers;
        private static readonly LogHelper logger = LogHelper.Instance;

        public RestAppsForm()
        {
            InitializeComponent();

            // Load back ups
            LoadBackups();

            // Add event handler to Load PackageIdentifiers from JSON file
            comboBackups.Click += comboBackups_SelectedIndexChanged;

            // Load styling
            SetStyle();
        }

        // Some UI nicety
        private void SetStyle()
        {
            // Some color styling
            BackColor =
            listApps.BackColor =
                Color.FromArgb(245, 241, 249);
        }

        internal void LoadBackups()
        {
            comboBackups.Items.Clear();

            if (Directory.Exists(Data.DataRootDir))
            {
                string[] backups = Directory.GetDirectories(Data.DataRootDir);

                foreach (string backup in backups)
                {
                    comboBackups.Items.Add(Path.GetFileName(backup));
                }

                if (comboBackups.Items.Count > 0)
                {
                    comboBackups.SelectedIndex = 0;
                }
            }
        }

        private void LoadPackageIdentifiers()
        {
            // Load JSON file from app data directory
            string selectedBackup = comboBackups.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedBackup))
            {
                return;
            }

            string inputFilePath = Path.Combine(Data.DataRootDir + "\\" + selectedBackup, "Remember installed apps.JSON");

            // Parse JSON file and extract PackageIdentifiers
            try
            {
                string jsonContent = File.ReadAllText(inputFilePath);
                var json = JObject.Parse(jsonContent);

                // Extract PackageIdentifiers from JSON structure
                packageIdentifiers = json["Sources"]?.FirstOrDefault()?["Packages"]
                    ?.Select(package => package.Value<string>("PackageIdentifier"))
                    .ToList();
            }
            catch (Exception ex)
            {
                logger.Log($"Error loading or parsing JSON file: {ex.Message}");
            }

            // Ensure the list is initialized even if an error occurs
            if (packageIdentifiers == null)
            {
                packageIdentifiers = new List<string>();
            }
        }

        private void comboBackups_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                listApps.Items.Clear();
                // Load PackageIdentifiers from JSON file
                LoadPackageIdentifiers();
                // Add PackageIdentifiers to ListBox
                foreach (var packageIdentifier in packageIdentifiers)
                {
                    listApps.Items.Add(packageIdentifier);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading or parsing JSON file: {ex.Message}");
            }
        }

        private async void RestorePackages(List<string> packageIdentifiers)
        {
            try
            {
                // Get selected items from listApps
                var selectedPackages = listApps.CheckedItems.Cast<string>().ToList();

                // Every package's outcome is inspected. This used to discard the returned
                // ProcessOutcome entirely, so with winget missing the loop completed instantly
                // having installed nothing and told the user nothing - a dialog that closed as
                // though every package had been installed.
                List<string> failures = new List<string>();

                foreach (string packageIdentifier in selectedPackages)
                {
                    // RunWingetAsync already runs on a background thread, so no outer Task.Run is
                    // needed. The window is shown: an install is long, and winget's own progress is
                    // the only progress reporting this dialog has.
                    ProcessOutcome outcome = await Utils.RunWingetAsync(
                        true,
                        "install",
                        "--id", packageIdentifier,
                        "--accept-source-agreements",
                        "--accept-package-agreements");

                    string problem = Describe(outcome);

                    if (problem != null)
                        failures.Add(packageIdentifier + ": " + problem);
                }

                ReportOutcome(selectedPackages.Count, failures);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Restoration failed. Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// What went wrong with one package, or null if it installed.
        /// </summary>
        /// <remarks>
        /// Mirrors AStoreApps.Verify branch for branch, and for the same reason: "did not start" and
        /// "started and we lost track of it" are different facts about whether this machine was
        /// changed. A package winget may have half-installed must not be reported as untouched.
        /// </remarks>
        internal static string Describe(ProcessOutcome outcome)
        {
            if (outcome == null)
                return "winget returned no outcome";

            if (!outcome.Started)
                return "could not run winget: " + outcome.Error;

            if (outcome.TimedOut)
                return "winget did not finish, and was stopped";

            if (outcome.Error != null)
                return "winget ran but its outcome could not be determined: " + outcome.Error;

            if (outcome.ExitCode != 0)
                return "winget exited with code " + outcome.ExitCode;

            return null;
        }

        /// <remarks>
        /// Says "installed" rather than "restored": winget install on a package that is already
        /// present upgrades it to the current version, so a restore does not necessarily put the
        /// machine back the way it was. Claiming otherwise would be the kind of unverified sentence
        /// the rest of this work removed.
        /// </remarks>
        private static void ReportOutcome(int attempted, List<string> failures)
        {
            if (attempted == 0)
            {
                MessageBox.Show("No apps were ticked, so nothing was installed.", "Nothing to do",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (failures.Count == 0)
            {
                MessageBox.Show(
                    $"winget installed {attempted} app(s).\n\nAlready-installed apps are upgraded to the current version rather than put back at the backed-up one.",
                    "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            MessageBox.Show(
                $"{failures.Count} of {attempted} app(s) did not install:\n\n{string.Join("\n", failures)}",
                "Some apps did not install", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void btnRestore_Click(object sender, EventArgs e)
        {
            // Let us ensure backup is selected
            string selectedBackup = comboBackups.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedBackup))
            {
                MessageBox.Show("Please select a backup to restore.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                // Load JSON file from selected backup
                string inputFilePath = Path.Combine(Data.DataRootDir, selectedBackup, "Remember installed apps.json");

                // Parse JSON file and extract PackageIdentifiers
                string jsonContent = File.ReadAllText(inputFilePath);
                var json = JObject.Parse(jsonContent);

                // Extract PackageIdentifiers from JSON structure
                List<string> packageIdentifiers = json["Sources"]?.FirstOrDefault()?["Packages"]
                    ?.Select(package => package.Value<string>("PackageIdentifier"))
                    .ToList();

                // Perform restoration process using extracted packageIdentifiers
                RestorePackages(packageIdentifiers);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Restoration failed. Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
           => this.Close();
    }
}