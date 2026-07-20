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

        // Read from Keys on every access: see the matching note in WPersonalization.
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
                string outputFileName = Path.Combine(path, RegFileNameFor(k));
                steps.Add(Utils.ExportRegistryKey(outputFileName, k, AbsenceIsNormal(k)));
            }

            return ModuleResult.Aggregate(steps);
        }

        public override ModuleResult Restore(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            foreach (string k in Keys)
            {
                string inputFileName = Path.Combine(path, RegFileNameFor(k));
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
    }
}
