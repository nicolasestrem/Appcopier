using Appcopier;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Views;

namespace Conf
{
    public class AppStoreApps : BackupBase
    {
        public AppStoreApps()
        {
            Title = "Remember installed apps";
            Info = "This will export all installed winget package identifiers as a .JSON file.\nThe import process allows you to restore specific apps themselves based on this file.";
        }

        public override async Task<ModuleResult> BackupAsync(string path)
        {
            // Execute winget command to list installed apps
            string outputFilePath = Path.Combine(path, $"{Title}.json");

            ProcessOutcome outcome = await Utils.RunWTAsync($"winget export -o \"{outputFilePath}\"");

            return ModuleResult.Aggregate(new[] { Verify(outcome, outputFilePath) });
        }

        /// <summary>
        /// Checks that winget produced a file RestAppsForm can actually read back.
        /// </summary>
        /// <remarks>
        /// The artifact is verified, not just the exit code. The previous version awaited nothing -
        /// RunWT was async void - and logged "Backup successful" before winget had started, so the
        /// message was written before the fact it described could be known. Even with the exit code
        /// now available it is not sufficient: winget exits 0 having written nothing when it has no
        /// source configured, and a file with no Packages array restores nothing.
        /// </remarks>
        private StepResult Verify(ProcessOutcome outcome, string outputFilePath)
        {
            if (outcome == null)
                return StepResult.Failed(Title, "the winget export returned no outcome");

            if (!outcome.Started)
                return StepResult.Failed(Title, "could not run the winget export: " + outcome.Error);

            if (outcome.TimedOut)
                return StepResult.Failed(Title, "the winget export did not finish");

            if (outcome.Error != null)
                return StepResult.Failed(Title, "winget ran but its outcome could not be determined: " + outcome.Error);

            if (outcome.ExitCode != 0)
                return StepResult.Failed(Title, "winget exited with code " + outcome.ExitCode);

            if (!File.Exists(outputFilePath))
                return StepResult.Failed(Title, "winget reported success but wrote no file");

            string json;

            try
            {
                json = File.ReadAllText(outputFilePath);
            }
            catch (Exception ex)
            {
                // Could not read it, so nothing is known about its contents - deliberately not
                // reported as an invalid file.
                return StepResult.Failed(Title, "could not read back the exported file: " + ex.Message);
            }

            if (string.IsNullOrWhiteSpace(json))
                return StepResult.Failed(Title, "winget wrote an empty file");

            try
            {
                // Sources[0].Packages is exactly the shape RestAppsForm reads. Anything else is a
                // file the restore dialog will show as empty, so accepting it would put a green
                // tick on a backup that restores nothing.
                JArray packages = JObject.Parse(json)["Sources"]?.FirstOrDefault()?["Packages"] as JArray;

                if (packages == null)
                    return StepResult.Failed(Title, "the exported file has no list of packages in it, so nothing could be restored from it");

                return StepResult.Succeeded(Title, $"exported {packages.Count} package identifier(s)");
            }
            catch (Exception ex)
            {
                return StepResult.Failed(Title, "the exported file is not valid JSON: " + ex.Message);
            }
        }

        /// <remarks>
        /// This module restores nothing itself. It opens RestAppsForm, and the installs happen
        /// later from inside that dialog, so Skipped is the only honest answer available here -
        /// claiming a result it does not have would be a new lie in a phase built to remove them.
        /// </remarks>
        public override ModuleResult Restore(string path)
        {
            // Switch to instance of RestoreAppsForm
            RestAppsForm restoreApps = new RestAppsForm();
            restoreApps.ShowDialog();

            return ModuleResult.Aggregate(new[]
            {
                StepResult.Skipped(Title, "handled interactively in the app restore dialog")
            });
        }
    }
}
