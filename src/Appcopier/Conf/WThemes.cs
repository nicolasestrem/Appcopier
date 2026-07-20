using Appcopier;
using DataHelper;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Conf
{
    public class WThemes : BackupBase
    {
        public List<string> Folders = new List<string>();
        public List<string> Keys = new List<string>();

        public WThemes()
        {
            Title = "Themes";
            Info = "This will backup custom theme settings, default Windows wallpapers and a copy of your current Desktop background image.";
            // Version = "This is compatible with all versions of Windows.";
            RequiresExplorerRestart = true;

            LoadSettings();
        }

        private void LoadSettings()
        {
            Folders.Add(Data.WindowsFolder + "\\Web\\Wallpaper");
            Folders.Add(Data.RoamingAppData + "\\Microsoft\\Windows\\Themes");

            Keys.Add(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes");
        }

        public override bool IsInstalled()
        {
            bool b1 = false;
            bool b2 = false;

            foreach (string f in Folders)
            {
                if (Directory.Exists(f))
                {
                    b1 = true;
                    break;
                }
            }

            foreach (string k in Keys)
            {
                if (Utils.KeyExists(k))
                {
                    b2 = true;
                    break;
                }
            }

            return b1 || b2;
        }

        /// <remarks>
        /// The one module with heterogeneous sub-operations: two folder copies and a registry
        /// export, folded through a single Aggregate. The backup sources use absenceIsNormal=false
        /// because both ship with Windows or are created at first logon, so a missing one is a real
        /// fault rather than a machine that simply never had them.
        /// </remarks>
        public override async Task<ModuleResult> BackupAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            foreach (string folder in Folders)
            {
                string folderName = Path.GetFileName(folder);
                string backupFolderPath = Path.Combine(path, $"{Title}_{GetSafeFileName(folderName)}");

                // No try/catch here any more: CopyFolder does not throw, it returns counts. The
                // catch this replaced logged the failure and then let the module report success.
                CopyResult copy = await Utils.CopyFolder(folder, backupFolderPath).ConfigureAwait(true);
                // Title, not the full filesystem path: Aggregate renders the target into
                // user-facing text, and a path produces rows reading "captured C:\Windows\...".
                steps.Add(copy.ToStep(Title, false));
            }

            foreach (string k in Keys)
            {
                steps.Add(Utils.ExportRegistryKey(Path.Combine(path, Title + ".reg"), k, false));
            }

            return ModuleResult.Aggregate(steps);
        }

        public override async Task<ModuleResult> RestoreAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            foreach (string folder in Folders)
            {
                string folderName = Path.GetFileName(folder);
                string backupFolderPath = Path.Combine(path, $"{Title}_{GetSafeFileName(folderName)}");

                // absenceIsNormal is true on this side: the folder being read is one this app wrote,
                // and a backup taken before this module existed legitimately does not contain it.
                CopyResult copy = await Utils.CopyFolder(backupFolderPath, folder).ConfigureAwait(true);
                // Title, not the full filesystem path: see the matching comment in BackupAsync.
                steps.Add(copy.ToStep(Title, true));
            }

            foreach (string k in Keys)
            {
                steps.Add(Utils.ImportRegistryKey(Path.Combine(path, Title + ".reg"), k));
            }

            return ModuleResult.Aggregate(steps);
        }

        // Helper method to create a safe folder name from folder path
        private string GetSafeFileName(string folderPath)
        {
            return folderPath.Replace(":", "_").Replace("\\", "_").Replace("/", "_").Replace("*", "_").Replace("?", "_").Replace("\"", "_");
        }
    }
}
