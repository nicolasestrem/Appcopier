using Appcopier;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
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

        public override IReadOnlyList<RestoreTarget> RestoreTargets
            => new[]
            {
                RestoreTarget.Command(
                    "opens the app reinstall dialog; this item changes nothing by itself, and any " +
                    "installs happen only from choices made inside that dialog")
            };

        /// <remarks>
        /// The one module that opts out, and the reason is Restore returning Skipped: it writes
        /// nothing, so snapshotting it would spend a full winget export - measured at ~29 s, and
        /// allowed up to ten minutes - protecting a restore that cannot change anything.
        /// </remarks>
        public override bool RestoreMakesChanges => false;

        public override async Task<ModuleResult> BackupAsync(string path)
        {
            // Execute winget command to list installed apps
            string outputFilePath = Path.Combine(path, $"{Title}.json");

            // Clear the target before running winget. ConfPageView reuses one timestamped folder for
            // every Backup click in an app session, so a second click can find a valid export from
            // the first still sitting there. winget has a documented-here failure mode of exiting 0
            // having written nothing (no source configured), and Verify would then be handed last
            // run's file: every check passes, the run reports success, and the user keeps an
            // outdated package list believing it was refreshed.
            try
            {
                if (File.Exists(outputFilePath))
                    File.Delete(outputFilePath);
            }
            catch (Exception ex)
            {
                return ModuleResult.Aggregate(new[]
                {
                    StepResult.Failed(Title, "could not clear the previous export at " + outputFilePath + ": " + ex.Message)
                });
            }

            ProcessOutcome outcome = await Utils.RunWingetAsync(false, "export", "-o", outputFilePath);

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

        /// <summary>
        /// Runs the restore on the caller's thread instead of a thread-pool thread.
        /// </summary>
        /// <remarks>
        /// The base RestoreAsync wraps Restore in Task.Run, and this is the one module whose Restore
        /// opens a window. Thread-pool threads are MTA; Windows Forms requires STA, which Program.Main
        /// declares. ShowDialog from the pool therefore spins up a second message loop on a thread
        /// that is not apartment-correct: the dialog has no owner and can paint behind the main
        /// window, and the COM-backed parts of it (clipboard, drag and drop, shell dialogs) are
        /// unreliable. Every caller reaches this through an await on the UI thread, so returning a
        /// completed Task keeps the dialog on the thread that owns the window.
        ///
        /// Not marked async on purpose: there is nothing to await, and async here would move the
        /// body back off the caller's thread in every case but the first.
        /// </remarks>
        public override Task<ModuleResult> RestoreAsync(string path)
            => Task.FromResult(Restore(path));

        /// <remarks>
        /// This module restores nothing itself. It opens RestAppsForm, and the installs happen
        /// later from inside that dialog, so Skipped is the only honest answer available here -
        /// claiming a result it does not have would be a new lie in a phase built to remove them.
        ///
        /// Call this only from the UI thread - see RestoreAsync above.
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
