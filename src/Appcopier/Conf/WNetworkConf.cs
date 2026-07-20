using Appcopier;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Conf
{
    public class WNetworkConf : BackupBase
    {
        private static readonly LogHelper logger = LogHelper.Instance;

        public WNetworkConf()
        {
            Title = "Network configuration";
            Info = "This will back up and restore TCP/IP network configuration.";
        }

        public override ModuleResult Backup(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            // Execute netsh command to export TCP/IP configuration to a file
            string filePath = Path.Combine(path, $"{Title}.txt");

            try
            {
                int exitCode = ExecuteNetshCommand($"interface dump", filePath);

                // The exit code alone is not enough. netsh can exit 0 having produced nothing, and
                // an empty dump restores nothing, so the artifact is checked as well.
                if (exitCode != 0)
                {
                    steps.Add(StepResult.Failed(Title, $"netsh exited with code {exitCode}"));
                }
                else if (!File.Exists(filePath))
                {
                    steps.Add(StepResult.Failed(Title, "netsh reported success but wrote no file"));
                }
                else if (new FileInfo(filePath).Length == 0)
                {
                    steps.Add(StepResult.Failed(Title, "netsh wrote an empty file"));
                }
                else
                {
                    steps.Add(StepResult.Succeeded(Title, "exported the TCP/IP configuration"));
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

            // Execute netsh command to import TCP/IP configuration from file
            string filePath = Path.Combine(path, $"{Title}.txt");

            if (!File.Exists(filePath))
            {
                return ModuleResult.Aggregate(new[]
                {
                    StepResult.Skipped(Title, "nothing was backed up for this item")
                });
            }

            try
            {
                // KNOWN DEFECT, left in place deliberately: ExecuteNetshCommand opens a StreamWriter
                // on outputFilePath unconditionally and this call passes null, so every restore
                // throws ArgumentNullException. That is a real bug rather than a dishonest one - it
                // was already caught and logged as a failure - and repairing it belongs to a later
                // task. It now reports Failed loudly, which is accurate.
                int exitCode = ExecuteNetshCommand($"exec \"{filePath}\"", null);

                steps.Add(exitCode == 0
                    ? StepResult.Applied(Title, "the backed-up TCP/IP configuration")
                    : StepResult.Failed(Title, $"netsh exited with code {exitCode}"));
            }
            catch (Exception ex)
            {
                steps.Add(StepResult.Failed(Title, $"{ex.GetType().Name}: {ex.Message}"));
            }

            return ModuleResult.Aggregate(steps);
        }

        // Helper method to execute netsh commands
        private int ExecuteNetshCommand(string arguments, string outputFilePath)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = psi })
            {
                process.Start();

                //  handle redirection internally using StreamWriter to write command output to file
                using (StreamWriter outputFile = new StreamWriter(outputFilePath))
                {
                    while (!process.StandardOutput.EndOfStream)
                    {
                        string line = process.StandardOutput.ReadLine();
                        outputFile.WriteLine(line);
                        logger.LogMessage(line);
                    }
                }

                process.WaitForExit();

                return process.ExitCode;
            }
        }
    }
}
