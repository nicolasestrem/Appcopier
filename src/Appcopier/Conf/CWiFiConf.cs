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
            IsWarning();
        }

        private void IsWarning()
        {
            WarningMessage = "Restoring this backup adds every saved network in it back to this machine, for all accounts, not just yours. This includes networks you may have since forgotten.";
        }

        public override ModuleResult Backup(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            try
            {
                // ConfPageView passes the shared backup root, and other modules write their own
                // files into the same folder, so a bare "how many .xml files are there now" count
                // is meaningless. Snapshot before and after the export and count only what netsh
                // itself wrote this run.
                //
                // Snapshotting by last-write-time, not just presence: CurrentBackupPath is the same
                // folder for every Backup click within one app session (ConfPageView.cs builds it
                // from Data.NowShort, a static field stamped once at process start), so a second
                // Backup click re-exports into files that already exist from the first. A pure
                // "did this filename exist before" check would then see zero new files and report a
                // real, successful re-export as Failed.
                Dictionary<string, DateTime> before = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in Directory.GetFiles(path, "*.xml"))
                    before[file] = File.GetLastWriteTimeUtc(file);

                int exitCode = ExecuteNetshCommand($"wlan export profile key=clear folder=\"{path.TrimEnd('\\')}\""); // remove trailing backslash from path

                string[] after = Directory.GetFiles(path, "*.xml");
                int added = 0;
                foreach (string file in after)
                {
                    DateTime previousWrite;
                    bool existedBefore = before.TryGetValue(file, out previousWrite);

                    if (!existedBefore || File.GetLastWriteTimeUtc(file) > previousWrite)
                        added++;
                }

                if (exitCode != 0)
                {
                    steps.Add(StepResult.Failed(Title, $"netsh exited with code {exitCode}"));
                }
                else if (added == 0)
                {
                    // netsh has been measured printing "saved successfully" with exit code 0 while
                    // writing nothing (the export path was too long for it). A zero exit code alone
                    // is not evidence of success.
                    steps.Add(StepResult.Failed(Title, "netsh reported success but wrote no Wi-Fi profile files"));
                }
                else
                {
                    steps.Add(StepResult.Succeeded(Title, $"exported {added} Wi-Fi profile(s)"));
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
                // Selection is by content, not filename: netsh names exports
                // "<interface name>-<SSID>.xml", and the interface name is machine-specific and
                // localised, so no filename pattern can be relied on. WlanProfile.FindIn parses
                // each *.xml and keeps only the ones whose root element is WLANProfile.
                string[] xmlFiles = WlanProfile.FindIn(path);

                if (xmlFiles.Length == 0)
                {
                    // The user selected this module for restore; there being nothing usable in the
                    // backup is a failure to deliver what they asked for, not something to skip.
                    steps.Add(StepResult.Failed(Title, "no Wi-Fi profile files were found in this backup"));
                }
                else
                {
                    // Import every matching profile, not just the first - a backup folder holds one
                    // XML per network, and stopping at the first entry discarded all the others.
                    foreach (string xmlFile in xmlFiles)
                    {
                        int exitCode = ExecuteNetshCommand($"wlan add profile filename=\"{xmlFile}\"");

                        steps.Add(exitCode == 0
                            ? StepResult.Applied(Title, Path.GetFileName(xmlFile))
                            : StepResult.Failed(Path.GetFileName(xmlFile), $"netsh exited with code {exitCode}"));
                    }
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
