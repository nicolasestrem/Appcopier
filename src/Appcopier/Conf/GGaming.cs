using Appcopier;
using System.Collections.Generic;
using System.IO;

namespace Conf
{
    public class GGaming : BackupBase
    {
        public List<string> Keys = new List<string>();

        public GGaming()
        {
            Title = "Gaming settings";
            Info = "This will export settings related to Windows Game Bar DVR (Game Recorder).";

            LoadSettings();
        }

        private void LoadSettings()

        {
            Keys.Add(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\GameBar");
            Keys.Add(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR");
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

        // True for both keys, because both are REMOVABLE without anything being wrong: GameBar and
        // GameDVR can be disabled by policy or stripped by debloat scripts, and an absent key then
        // means nothing is configured rather than that something broke. Both were probed on the
        // development machine and found PRESENT - the flag covers the machines where they are not,
        // it does not assert that absence is the common case.
        private static bool AbsenceIsNormal(string key) => true;

        // Helper method to create a safe file name from registry key
        private string GetSafeFileName(string registryKey)
        {
            return registryKey.Replace("\\", "_").Replace(":", "_").Replace("/", "_").Replace("*", "_").Replace("?", "_").Replace("\"", "_");
        }
    }
}
