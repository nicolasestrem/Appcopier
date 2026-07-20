using Appcopier;
using DataHelper;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Conf
{
    public class BMicrosoftEdge : BackupBase
    {
        public string Folder = Data.LocalAppData + "\\Microsoft\\Edge";

        public BMicrosoftEdge()
        {
            Title = "Microsoft Edge";
            Info = "This will back up the complete Microsoft Edge profile.";
        }

        public override bool IsInstalled()
        {
            return Directory.Exists(Folder);
        }

        public override async Task<ModuleResult> BackupAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            // Check if process is running
            if (Utils.IsProcessRunning("msedge"))
            {
                DialogResult answer = MessageBox.Show(
                    "The Edge process is currently running. Do you want to close it before backup?",
                    "Process Running", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (answer != DialogResult.Yes)
                {
                    steps.Add(StepResult.Skipped(Title, "you chose not to close Edge, so it was not backed up"));
                    return ModuleResult.Aggregate(steps);
                }

                CloseResult closed = Utils.CloseProcess("msedge");

                if (closed == CloseResult.AccessDenied || closed == CloseResult.StillRunning)
                {
                    steps.Add(StepResult.Failed(Title, "Edge could not be closed, so its files are still locked"));
                    return ModuleResult.Aggregate(steps);
                }
            }

            CopyResult copy = await Utils.CopyFolder(Folder, Path.Combine(path, Title));
            steps.Add(ToStep(copy));

            return ModuleResult.Aggregate(steps);
        }

        public override async Task<ModuleResult> RestoreAsync(string path)
        {
            CopyResult copy = await Utils.CopyFolder(Path.Combine(path, Title), Folder);

            // No custom wording here: an absent source on restore means the BACKUP FOLDER has no
            // Edge data, not that Edge has never been launched on this machine - that claim about
            // the live machine was never checked, so it falls through to ToStep's default wording.
            return ModuleResult.Aggregate(new[] { copy.ToStep(Title, true) });
        }

        /// <remarks>
        /// Backup-only. Edge ships with Windows, so a missing profile folder almost never means
        /// "Edge is not installed" - it means the browser has never been launched on this account.
        /// Wording it as absent software would send the user looking for a problem that is not
        /// there. That reasoning applies only to the live machine BackupAsync just checked.
        /// </remarks>
        private StepResult ToStep(CopyResult copy)
        {
            if (copy.SourceMissing)
                return StepResult.Skipped(Title, "no Edge profile data found");

            return copy.ToStep(Title, true);
        }
    }
}
