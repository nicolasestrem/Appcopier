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

        public override async Task<ModuleResult> BackupAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            // Check if process is running
            if (Utils.IsProcessRunning("chrome"))
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
