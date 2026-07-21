using Appcopier;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Conf
{
    public class CWiFiConf : BackupBase
    {
        public CWiFiConf()
        {
            Title = "Wi-Fi networks & passwords";
            Info = "This will back up and restore credentials of Wi-Fi networks.";
            WarningMessage = "Restoring this backup adds every saved network in it back to this machine, for all accounts, not just yours. This includes networks you may have since forgotten.";
        }

        // A command, not a file list: what gets added is one Wi-Fi profile per XML found in the
        // backup, and netsh installs each machine-wide. The wording says "for all accounts" because
        // that is the part of this restore a user would not otherwise expect.
        public override IReadOnlyList<RestoreTarget> RestoreTargets
            => new[]
            {
                RestoreTarget.Command(
                    "runs netsh to add every saved Wi-Fi network in the backup, with its password, " +
                    "to this machine for all accounts")
            };

        public override async Task<ModuleResult> BackupAsync(string path)
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

                ProcessOutcome outcome = await Utils.RunToolAsync(
                    "netsh",
                    new[] { "wlan", "export", "profile", "key=clear", "folder=" + path.TrimEnd('\\') });

                string[] after = Directory.GetFiles(path, "*.xml");
                int added = 0;
                foreach (string file in after)
                {
                    DateTime previousWrite;
                    bool existedBefore = before.TryGetValue(file, out previousWrite);

                    if (!existedBefore || File.GetLastWriteTimeUtc(file) > previousWrite)
                        added++;
                }

                if (outcome == null || !outcome.Started)
                {
                    steps.Add(StepResult.Failed(Title, "could not run netsh: " + (outcome == null ? "no outcome" : outcome.Error)));
                }
                else if (outcome.TimedOut)
                {
                    steps.Add(StepResult.Failed(Title, "netsh did not finish"));
                }
                else if (outcome.Error != null)
                {
                    steps.Add(StepResult.Failed(Title, "netsh ran but its outcome could not be determined: " + outcome.Error));
                }
                else if (outcome.ExitCode != 0)
                {
                    steps.Add(StepResult.Failed(Title, $"netsh exited with code {outcome.ExitCode}"));
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

        public override async Task<ModuleResult> RestoreAsync(string path)
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
                        ProcessOutcome outcome = await Utils.RunToolAsync(
                            "netsh", new[] { "wlan", "add", "profile", "filename=" + xmlFile });

                        string name = Path.GetFileName(xmlFile);

                        if (outcome != null && outcome.Started && !outcome.TimedOut
                            && outcome.Error == null && outcome.ExitCode == 0)
                        {
                            steps.Add(StepResult.Applied(Title, name));
                        }
                        else if (outcome == null || !outcome.Started)
                        {
                            steps.Add(StepResult.Failed(name, "could not run netsh: " + (outcome == null ? "no outcome" : outcome.Error)));
                        }
                        else if (outcome.TimedOut)
                        {
                            steps.Add(StepResult.Failed(name, "netsh did not finish"));
                        }
                        else if (outcome.Error != null)
                        {
                            steps.Add(StepResult.Failed(name, "netsh ran but its outcome could not be determined: " + outcome.Error));
                        }
                        else
                        {
                            steps.Add(StepResult.Failed(name, $"netsh exited with code {outcome.ExitCode}"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                steps.Add(StepResult.Failed(Title, $"{ex.GetType().Name}: {ex.Message}"));
            }

            return ModuleResult.Aggregate(steps);
        }
    }
}
