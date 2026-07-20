using Appcopier;
using System.Collections.Generic;
using System.IO;

namespace Conf
{
    public class DPrinters : BackupBase
    {
        public List<string> Keys = new List<string>();

        public DPrinters()
        {
            Title = "Printers";
            Info = "This will backup the Windows Printers configuration.";
            IsWarning();
            LoadSettings();
        }

        private void IsWarning()
        {
            WarningMessage = "The restoration of this backup could affect your printer configurations. Proceed with caution.";
        }

        private void LoadSettings()
        {
            Keys.Add(@"HKEY_CURRENT_USER\Printers");
            Keys.Add(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Print\Printers");
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

        // The per-user HKCU\Printers key is populated lazily and is legitimately absent on an
        // account that has never added a printer. The HKLM key under Print\Printers is created by
        // the spooler on every Windows install, so its absence means something is wrong.
        private static bool AbsenceIsNormal(string key)
            => key.StartsWith(@"HKEY_CURRENT_USER\", System.StringComparison.OrdinalIgnoreCase);

        // Helper method to create a safe file name from registry key
        private string GetSafeFileName(string registryKey)
        {
            return registryKey.Replace("\\", "_").Replace(":", "_").Replace("/", "_").Replace("*", "_").Replace("?", "_").Replace("\"", "_");
        }
    }
}
