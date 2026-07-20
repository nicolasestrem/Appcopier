using Appcopier;
using DataHelper;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Conf
{
    public class BMozillaFirefox : BackupBase
    {
        public string Folder = Data.RoamingAppData + "\\Mozilla";

        public BMozillaFirefox()
        {
            Title = "Mozilla Firefox";
            Info = "This will back up the complete Mozilla Firefox profile.";
        }

        public override bool IsInstalled()
        {
            return Directory.Exists(Folder);
        }

        public override IReadOnlyList<RestoreTarget> RestoreTargets
            => new[] { RestoreTarget.Folder(Folder) };

        // "firefox" matches the backup path above: see the matching note in BGoogleChrome.
        public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
            => new[] { new RestoreCloseRequirement("firefox", "Mozilla Firefox", true) };

        public override async Task<ModuleResult> BackupAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            // Check if process is running
            if (Utils.IsProcessRunning("firefox"))
            {
                DialogResult answer = MessageBox.Show(
                    "The Firefox process is currently running. Do you want to close it before backup?",
                    "Process Running", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (answer != DialogResult.Yes)
                {
                    steps.Add(StepResult.Skipped(Title, "you chose not to close Firefox, so it was not backed up"));
                    return ModuleResult.Aggregate(steps);
                }

                CloseResult closed = Utils.CloseProcess("firefox");

                if (closed == CloseResult.AccessDenied || closed == CloseResult.StillRunning)
                {
                    steps.Add(StepResult.Failed(Title, "Firefox could not be closed, so its files are still locked"));
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
