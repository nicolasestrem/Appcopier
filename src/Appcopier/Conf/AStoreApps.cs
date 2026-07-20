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
        /// <summary>
        /// The one spelling of this module's name, shared by the file it writes.
        /// </summary>
        public const string ModuleTitle = "Remember installed apps";

        /// <summary>
        /// The file this module writes, and the only name any reader may look for.
        /// </summary>
        /// <remarks>
        /// This name existed in four spellings at once: the producer wrote ".json", one reader
        /// looked for ".JSON", another for ".json", and the Info text shown to the user said
        /// ".JSON". On a case-insensitive filesystem three of those happen to agree, which is
        /// exactly why the disagreement survived - it is a latent bug that becomes real the moment
        /// a backup folder is read from anything that respects case. The extension is lowercase
        /// because that is what winget itself writes.
        ///
        /// It lives here, on the class that WRITES the file, rather than on BackupBase.RegFileNameFor:
        /// that seam derives .reg names from a registry key, and this artifact has no key.
        /// </remarks>
        public const string ExportFileName = ModuleTitle + ".json";

        /// <summary>
        /// Where this module's export lives inside <paramref name="backupFolder"/>.
        /// </summary>
        /// <remarks>
        /// The restore dialog composes its path through this method rather than repeating the
        /// Path.Combine, so producer and reader cannot disagree about the filename again.
        /// </remarks>
        public static string ExportPathIn(string backupFolder)
            => Path.Combine(backupFolder, ExportFileName);

        public AppStoreApps()
        {
            Title = ModuleTitle;
            Info = "This will export all installed winget package identifiers as a .json file.\nThe import process allows you to restore specific apps themselves based on this file.";
        }

        // HasBackupIn is deliberately NOT overridden to test for ExportPathIn(restorePath).
        //
        // It looks like the obvious use of the new seam, and it would be wrong twice over. This
        // module's restore does not read the export at all - it opens RestAppsForm, which lets the
        // user pick ANY backup folder from its own dropdown, not the one being restored. Answering
        // "no" here would make RestoreScope drop the module, so the dialog would never open for a
        // user whose selected folder happens to hold no export, while the dialog itself would have
        // been perfectly able to offer every other backup. It would also break
        // RestoreDeclarationTests.ModulesThatCloseNothing_AssumeTheBackupHasSomethingForThem.

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
            string outputFilePath = ExportPathIn(path);

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
        ///
        /// internal static rather than private so the ladder can be exercised without a real winget,
        /// following the precedent of RestAppsForm.Describe. It reads ModuleTitle instead of the
        /// Title instance property, which is the same string by construction.
        /// </remarks>
        internal static StepResult Verify(ProcessOutcome outcome, string outputFilePath)
        {
            if (outcome == null)
                return StepResult.Failed(ModuleTitle, "the winget export returned no outcome");

            if (!outcome.Started)
                return StepResult.Failed(ModuleTitle, "could not run the winget export: " + outcome.Error);

            if (outcome.TimedOut)
                return StepResult.Failed(ModuleTitle, "the winget export did not finish");

            if (outcome.Error != null)
                return StepResult.Failed(ModuleTitle, "winget ran but its outcome could not be determined: " + outcome.Error);

            if (outcome.ExitCode != 0)
                return StepResult.Failed(ModuleTitle, "winget exited with code " + outcome.ExitCode);

            if (!File.Exists(outputFilePath))
                return StepResult.Failed(ModuleTitle, "winget reported success but wrote no file");

            string json;

            try
            {
                json = File.ReadAllText(outputFilePath);
            }
            catch (Exception ex)
            {
                // Could not read it, so nothing is known about its contents - deliberately not
                // reported as an invalid file.
                return StepResult.Failed(ModuleTitle, "could not read back the exported file: " + ex.Message);
            }

            if (string.IsNullOrWhiteSpace(json))
                return StepResult.Failed(ModuleTitle, "winget wrote an empty file");

            try
            {
                // Sources[0].Packages is exactly the shape RestAppsForm reads. Anything else is a
                // file the restore dialog will show as empty, so accepting it would put a green
                // tick on a backup that restores nothing.
                JArray packages = JObject.Parse(json)["Sources"]?.FirstOrDefault()?["Packages"] as JArray;

                if (packages == null)
                    return StepResult.Failed(ModuleTitle, "the exported file has no list of packages in it, so nothing could be restored from it");

                return StepResult.Succeeded(ModuleTitle, $"exported {packages.Count} package identifier(s)");
            }
            catch (Exception ex)
            {
                return StepResult.Failed(ModuleTitle, "the exported file is not valid JSON: " + ex.Message);
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
