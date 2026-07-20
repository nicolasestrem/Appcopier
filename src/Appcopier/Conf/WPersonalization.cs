using Appcopier;
using System.Collections.Generic;
using System.IO;

namespace Conf
{
    public class WPersonalization : BackupBase
    {
        public List<string> Keys = new List<string>();

        public WPersonalization()
        {
            Title = "Personalization";
            Info = "This will export settings related to Themes and Personalization (Default app mode, Color prevalence, Transparency etc).";
            RequiresExplorerRestart = true;

            LoadSettings();
        }

        private void LoadSettings()
        {
            Keys.Add(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            Keys.Add(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent");
        }

        public override bool IsInstalled()
        {
            bool b1 = false;

            foreach (string k in Keys)
            {
                if (Utils.KeyExists(k))
                {
                    b1 = true;
                    break;
                }
            }

            return b1;
        }

        public override ModuleResult Backup(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            foreach (string k in Keys)
            {
                string outputFileName = Path.Combine(path, $"{Title}_{GetSafeFileName(k)}.reg");
                steps.Add(Utils.ExportRegistryKey(outputFileName, k, AbsenceIsNormal(k)));
            }

            return ModuleResult.Aggregate(steps);
        }

        public override ModuleResult Restore(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            foreach (string k in Keys)
            {
                string inputFileName = Path.Combine(path, $"{Title}_{GetSafeFileName(k)}.reg");
                steps.Add(Utils.ImportRegistryKey(inputFileName, k));
            }

            return ModuleResult.Aggregate(steps);
        }

        // Per-key, and it CANNOT be inferred from IsInstalled(): that returns true as soon as any
        // one key exists, so "installed" says nothing about the others. Explorer\Accent is the
        // canonical legitimately-absent key - treating it as a failure would mark this module red
        // on a large share of perfectly healthy machines.
        private static bool AbsenceIsNormal(string key)
            => key.EndsWith(@"\Accent", System.StringComparison.OrdinalIgnoreCase);

        // Helper method to create a safe file name from registry key
        private string GetSafeFileName(string registryKey)
        {
            return registryKey.Replace("\\", "_").Replace(":", "_").Replace("/", "_").Replace("*", "_").Replace("?", "_").Replace("\"", "_");
        }
    }
}
