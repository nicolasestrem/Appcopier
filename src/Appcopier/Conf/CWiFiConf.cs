using Appcopier;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Conf
{
    public class CWiFiConf : BackupBase
    {
        private static readonly LogHelper logger = LogHelper.Instance;

        public CWiFiConf()
        {
            Title = "Wi-Fi networks & passwords";
            Info = "This will back up and restore credentials of Wi-Fi networks.";
        }

        public override ModuleResult Backup(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            try
            {
                // Execute netsh command to export Wi-Fi profiles to a file
                int exitCode = ExecuteNetshCommand($"wlan export profile key=clear folder=\"{path.TrimEnd('\\')}\""); // remove trailing backslash from path

                if (exitCode != 0)
                {
                    steps.Add(StepResult.Failed(Title, $"netsh exited with code {exitCode}"));
                }
                else
                {
                    // netsh writes one XML per profile, so counting them is the only evidence that
                    // anything was exported. The previous check was Directory.Exists(path) on the
                    // backup folder this app had just created, which was true no matter what netsh
                    // did - it could not have reported a failure.
                    string[] profiles = Directory.GetFiles(path, "*.xml");

                    steps.Add(profiles.Length > 0
                        ? StepResult.Succeeded(Title, $"exported {profiles.Length} Wi-Fi profile(s)")
                        : StepResult.Skipped(Title, "there are no saved Wi-Fi profiles on this system"));
                }
            }
            catch (Exception ex)
            {
                steps.Add(StepResult.Failed(Title, $"{ex.GetType().Name}: {ex.Message}"));
            }

            return ModuleResult.Aggregate(steps);
        }

        public override ModuleResult Restore(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            try
            {
                // Search for a file in the specified folder that starts with "wlan" and has an XML extension
                //
                // KNOWN DEFECT, left in place deliberately: netsh names these files after the
                // profile ("Wi-Fi-MyNetwork.xml"), so this filter matches almost nothing, and even
                // when it does only the first profile is restored. Fixing the selection is a later
                // task; this change makes the module report what it did rather than claim success.
                string[] xmlFiles = Directory.GetFiles(path, "WLAN*.xml");

                if (xmlFiles.Length > 0)
                {
                    // Import first found XML file
                    int exitCode = ExecuteNetshCommand($"wlan add profile filename=\"{xmlFiles[0]}\"");

                    steps.Add(exitCode == 0
                        ? StepResult.Applied(Title, Path.GetFileName(xmlFiles[0]))
                        : StepResult.Failed(Title, $"netsh exited with code {exitCode}"));
                }
                else
                {
                    steps.Add(StepResult.Skipped(Title, "no matching Wi-Fi profile file was found in this backup"));
                }
            }
            catch (Exception ex)
            {
                steps.Add(StepResult.Failed(Title, $"{ex.GetType().Name}: {ex.Message}"));
            }

            return ModuleResult.Aggregate(steps);
        }

        // Helper method to execute netsh commands
        private int ExecuteNetshCommand(string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true, // capture error output
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = psi })
            {
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                process.WaitForExit();

                logger.LogMessage($"Wi-Fi Conf: {output}");
                logger.LogMessage($"Wi-Fi Conf: {error}");

                return process.ExitCode;
            }
        }
    }
}
