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
                // ExecuteNetshCommand now only creates its StreamWriter when an output path is
                // actually supplied, so this null is safe - the restore runs to completion and
                // reports its real exit code.
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
                // The writer is created ONLY when a path was supplied, and BEFORE Start(). It used
                // to be constructed unconditionally, after Start() - and Restore passes null - so
                // restoring network configuration threw once netsh was already applying the backup's
                // addresses, DNS servers and interface metrics. The user was told the restore failed
                // while their networking was being reconfigured, and netsh was left running unwaited.
                //
                // Opening the file first closes the remaining half of that: a locked file, a missing
                // directory or a denied path makes the constructor throw, and if netsh were already
                // running we would abandon it with nobody draining its stdout - the pipe fills, netsh
                // blocks on the write, and the process survives the Dispose below as an orphan.
                // Nothing has been started yet at this point, so the throw is just a failed backup.
                StreamWriter outputFile = outputFilePath == null
                    ? null
                    : new StreamWriter(outputFilePath);

                try
                {
                    process.Start();

                    // Drain stdout either way. Leaving it unread lets the pipe fill and block netsh.
                    while (!process.StandardOutput.EndOfStream)
                    {
                        string line = process.StandardOutput.ReadLine();

                        if (outputFile != null)
                            outputFile.WriteLine(line);

                        logger.LogMessage(line);
                    }
                }
                finally
                {
                    if (outputFile != null)
                        outputFile.Dispose();
                }

                process.WaitForExit();

                return process.ExitCode;
            }
        }
    }
}
