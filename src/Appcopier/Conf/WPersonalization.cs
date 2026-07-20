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

        // Read from Keys on every access rather than captured once: Keys is a mutable public field
        // filled by LoadSettings, so a snapshot taken at construction would silently describe a
        // different set of keys than the one Restore actually imports.
        public override IReadOnlyList<RestoreTarget> RestoreTargets
        {
            get
            {
                List<RestoreTarget> targets = new List<RestoreTarget>();

                foreach (string k in Keys)
                    targets.Add(RestoreTarget.RegistryKey(k));

                return targets;
            }
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
        // one key exists, so "installed" says nothing about the others. Explorer\Accent is treated
        // as legitimately absent because it is REMOVABLE - a key Windows writes on demand and that
        // policy or a cleanup tool can take away. It was probed on the development machine and
        // found PRESENT; the flag exists for the machines where it is not, so that a healthy one
        // is never marked red.
        private static bool AbsenceIsNormal(string key)
            => key.EndsWith(@"\Accent", System.StringComparison.OrdinalIgnoreCase);

        // Helper method to create a safe file name from registry key
        private string GetSafeFileName(string registryKey)
        {
            return registryKey.Replace("\\", "_").Replace(":", "_").Replace("/", "_").Replace("*", "_").Replace("?", "_").Replace("\"", "_");
        }
    }
}
