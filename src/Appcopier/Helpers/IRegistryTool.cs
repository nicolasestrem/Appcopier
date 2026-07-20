using System;
using System.Diagnostics;

namespace Appcopier
{
    /// <summary>
    /// What happened when we tried to run an external tool.
    /// </summary>
    internal sealed class ProcessOutcome
    {
        public bool Started { get; private set; }
        public bool TimedOut { get; private set; }
        public int ExitCode { get; private set; }
        public string Error { get; private set; }

        public static ProcessOutcome Ran(int exitCode)
            => new ProcessOutcome { Started = true, ExitCode = exitCode };

        public static ProcessOutcome Timeout()
            => new ProcessOutcome { Started = true, TimedOut = true };

        public static ProcessOutcome Failed(string error)
            => new ProcessOutcome { Started = false, Error = error };
    }

    /// <summary>
    /// The registry export/import launch, behind an interface purely so module logic can be tested.
    /// </summary>
    /// <remarks>
    /// Nothing in the test suite can assert what regedit.exe returns for a denied key or a
    /// partially-applied file - those need elevation and a real hive. This seam does not fix that;
    /// it confines it, so everything ABOVE the launch is covered and the uncovered surface is one
    /// small class.
    /// </remarks>
    internal interface IRegistryTool
    {
        ProcessOutcome Export(string filePath, string registryPath);

        ProcessOutcome Import(string filePath);
    }

    internal sealed class RegeditTool : IRegistryTool
    {
        // regedit blocking on a modal error dialog used to hang the backup thread forever, because
        // the old WaitForExit() had no timeout and nothing read the exit code afterwards.
        private const int TimeoutMs = 60000;

        public ProcessOutcome Export(string filePath, string registryPath)
            => Run(string.Format("/e \"{0}\" \"{1}\"", filePath, registryPath));

        // Note: no registry path argument. The old code appended one to /s, which documented regedit
        // syntax does not define.
        public ProcessOutcome Import(string filePath)
            => Run(string.Format("/s \"{0}\"", filePath));

        private static ProcessOutcome Run(string arguments)
        {
            try
            {
                using (Process proc = new Process())
                {
                    proc.StartInfo.FileName = "regedit.exe";
                    proc.StartInfo.Arguments = arguments;
                    proc.StartInfo.UseShellExecute = false;

                    // Deliberately no StartInfo.Verb = "runas": Verb is ignored while
                    // UseShellExecute is false, so the old line granted nothing and merely implied
                    // an elevation request that was not happening. Elevation comes from app.manifest.

                    proc.Start();

                    if (!proc.WaitForExit(TimeoutMs))
                    {
                        try { proc.Kill(); } catch (Exception) { }
                        return ProcessOutcome.Timeout();
                    }

                    return ProcessOutcome.Ran(proc.ExitCode);
                }
            }
            catch (Exception ex)
            {
                return ProcessOutcome.Failed(ex.Message);
            }
        }
    }
}
