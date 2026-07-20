using Appcopier;
using System.Collections.Generic;
using System.IO;

namespace Conf
{
    public class WTelemetry : BackupBase
    {
        public List<string> Keys = new List<string>();

        public WTelemetry()
        {
            Title = "Telemetry";
            Info = "This will export Diagnostic data settings and services.";

            LoadSettings();
        }

        private void LoadSettings()
        {
            Keys.Add(@"HKEY_LOCAL_MACHINE\Software\Policies\Microsoft\Windows\DataCollection");
            Keys.Add(@"HKEY_LOCAL_MACHINE\SYSTEM\ControlSet001\Services\DiagTrack");
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

        // True for both keys. The DataCollection policy key exists only where Group Policy set it,
        // so it is absent on a clean Home or Pro install, and the DiagTrack service key is routinely
        // removed by debloat scripts. This module therefore aggregates to Skipped on a stock
        // consumer machine - the correct answer, not a case to work around.
        private static bool AbsenceIsNormal(string key) => true;

        // Helper method to create a safe file name from registry key
        private string GetSafeFileName(string registryKey)
        {
            return registryKey.Replace("\\", "_").Replace(":", "_").Replace("/", "_").Replace("*", "_").Replace("?", "_").Replace("\"", "_");
        }
    }
}
