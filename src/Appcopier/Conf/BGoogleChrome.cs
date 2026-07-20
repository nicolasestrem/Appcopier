using Appcopier;
using DataHelper;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Conf
{
    public class BGoogleChrome : BackupBase
    {
        public string Folder = Data.LocalAppData + "\\Google\\Chrome";

        public BGoogleChrome()
        {
            Title = "Google Chrome";
            Info = "This will back up the complete Google Chrome profile.";
        }

        public override bool IsInstalled()
        {
            return Directory.Exists(Folder);
        }

        public override IReadOnlyList<RestoreTarget> RestoreTargets
            => new[] { RestoreTarget.Folder(Folder) };

        // "chrome" is the same process name the backup path above closes; the two must not drift,
        // or the restore would overwrite a profile whose owning process is still holding it open.
        // Consent is required because closing it discards open tabs the user can see.
        public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
            => new[] { new RestoreCloseRequirement("chrome", "Google Chrome", true) };

        // The folder RestoreAsync reads from. Without this the orchestrator would close Chrome -
        // losing every open tab - before RestoreAsync discovered there was nothing here to copy.
        public override bool HasBackupIn(string restorePath)
            => !string.IsNullOrWhiteSpace(restorePath)
               && Directory.Exists(Path.Combine(restorePath, Title));

        public override async Task<ModuleResult> BackupAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            // Check if process is running
            if (Utils.IsProcessRunning("chrome"))
            {
                // Not asked again when the caller has already gathered consent - the pre-restore
                // snapshot has, on the UI thread, and asking here would both duplicate it and raise
                // an ownerless dialog from a thread-pool thread. See BackupBase.AllowPrompts.
                if (AllowPrompts)
                {
                    DialogResult answer = MessageBox.Show(
                        "The Chrome process is currently running. Do you want to close it before backup?",
                        "Process Running", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (answer != DialogResult.Yes)
                    {
                        // Previously a bare "return", reported to the user as "Back up done." This is
                        // the canonical Skipped case in the codebase: a deliberate user choice, not an
                        // error, and not a success either.
                        steps.Add(StepResult.Skipped(Title, "you chose not to close Chrome, so it was not backed up"));
                        return ModuleResult.Aggregate(steps);
                    }
                }

                CloseResult closed = Utils.CloseProcess("chrome");

                if (closed == CloseResult.AccessDenied || closed == CloseResult.StillRunning)
                {
                    steps.Add(StepResult.Failed(Title, "Chrome could not be closed, so its files are still locked"));
                    return ModuleResult.Aggregate(steps);
                }
            }

            CopyResult copy = await Utils.CopyFolder(Folder, Path.Combine(path, Title));
            steps.Add(copy.ToStep(Title, true));

            return ModuleResult.Aggregate(steps);
        }

        public override async Task<ModuleResult> RestoreAsync(string path)
        {
            CopyResult copy = await Utils.CopyFolder(Path.Combine(path, Title), Folder);

            return ModuleResult.Aggregate(new[] { copy.ToStep(Title, true, NothingBackedUp) });
        }
    }
}
