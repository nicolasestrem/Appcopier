using Appcopier;
using System.Collections.Generic;
using System.IO;

namespace Conf
{
    public class WUpdates : BackupBase
    {
        public List<string> Keys = new List<string>();

        public WUpdates()
        {
            Title = "Windows Update";
            Info = "This will back up Windows update settings (when to install automatic updates, when to reboot after installing updates, DetectionFrequency, AutoInstallMinorUpdates etc).";

            LoadSettings();
        }

        private void LoadSettings()
        {
            Keys.Add(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate");
            Keys.Add(@"HKEY_LOCAL_MACHINE\Software\Policies\Microsoft\Windows\WindowsUpdate\AU");
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

        // The CurrentVersion\WindowsUpdate key is core servicing state present on every install, so
        // its absence is a real fault. The policy key under \AU exists only where WSUS or Group
        // Policy configured it, which is a minority of machines - this module therefore lands on
        // aggregation rule 4 (captured one, skipped one) on a large share of healthy systems.
        private static bool AbsenceIsNormal(string key)
            => key.EndsWith(@"\AU", System.StringComparison.OrdinalIgnoreCase);

        // Helper method to create a safe file name from registry key
        private string GetSafeFileName(string registryKey)
        {
            return registryKey.Replace("\\", "_").Replace(":", "_").Replace("/", "_").Replace("*", "_").Replace("?", "_").Replace("\"", "_");
        }
    }
}
