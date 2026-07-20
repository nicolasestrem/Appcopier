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

        // True for both keys, because both are REMOVABLE without anything being wrong: the
        // DataCollection policy key exists only where Group Policy or an edition difference put it
        // there, and the DiagTrack service key is a routine target of debloat scripts. Both were
        // probed on the development machine and found PRESENT, so this is not a claim that they are
        // typically missing - only that their absence is a legitimate state and not a failure.
        private static bool AbsenceIsNormal(string key) => true;
    }
}
