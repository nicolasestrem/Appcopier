using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Appcopier
{
    public static class Utils
    {
        private static readonly LogHelper logger = LogHelper.Instance;

        internal static async Task<CopyResult> CopyFolder(string source, string destination)
        {
            CopyResult result = new CopyResult();
            await CopyFolderInto(source, destination, result, isRoot: true).ConfigureAwait(false);
            return result;
        }

        private static async Task CopyFolderInto(string source, string destination,
                                                 CopyResult result, bool isRoot)
        {
            try
            {
                DirectoryInfo sourceDir = new DirectoryInfo(source);

                if (!sourceDir.Exists)
                {
                    if (isRoot)
                    {
                        result.SourceMissing = true;
                        logger.LogMessage("Source directory does not exist: " + source);
                        return;
                    }

                    // A subdirectory that vanished between enumeration and this visit. Browsers
                    // delete cache folders constantly, so this is ordinary, not exotic. It is a
                    // folder we failed to copy - NOT evidence that the backup source was absent.
                    // Setting SourceMissing here would make ToStep discard a copy that had already
                    // moved hundreds of files and report "not present on this system".
                    result.FoldersFailed++;
                    if (result.FirstError == null)
                        result.FirstError = source + ": the folder disappeared during the copy";

                    logger.LogMessage("Subdirectory vanished during copy: " + source);
                    return;
                }

                DirectoryInfo destinationDir = new DirectoryInfo(destination);

                if (!destinationDir.Exists)
                    destinationDir.Create();

                foreach (FileInfo file in sourceDir.GetFiles())
                {
                    string destinationFilePath = Path.Combine(destinationDir.FullName, file.Name);

                    try
                    {
                        using (FileStream sourceStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                        using (FileStream destinationStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                        {
                            // ConfigureAwait(false) so an aggregating caller is not marshalled back
                            // to the UI thread once per file across a browser profile.
                            await sourceStream.CopyToAsync(destinationStream).ConfigureAwait(false);
                        }

                        result.FilesCopied++;
                        result.BytesCopied += file.Length;
                    }
                    catch (Exception ex)
                    {
                        result.FilesFailed++;
                        if (result.FirstError == null)
                            result.FirstError = file.Name + ": " + ex.Message;

                        logger.LogMessage("Error copying file " + file.FullName + ": " + ex.Message);
                    }
                }

                foreach (DirectoryInfo subDirectory in sourceDir.GetDirectories())
                {
                    string newDestinationPath = Path.Combine(destinationDir.FullName, subDirectory.Name);
                    await CopyFolderInto(subDirectory.FullName, newDestinationPath, result, isRoot: false)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // Enumeration or directory creation failed, so this folder's whole subtree was
                // never attempted. Counted as a FOLDER failure, not a file one: incrementing
                // FilesFailed here would produce "1 of 1 files could not be copied" having tried
                // exactly zero files.
                result.FoldersFailed++;
                if (result.FirstError == null)
                    result.FirstError = source + ": " + ex.Message;

                logger.LogMessage("Error copying folder " + source + " to " + destination + ": " + ex.Message);
            }
        }

        private static readonly IRegistryTool DefaultRegistryTool = new RegeditTool();

        /// <summary>
        /// Exports one registry key and verifies the artifact it was supposed to produce.
        /// </summary>
        /// <remarks>
        /// The verification is not belt-and-braces. Measured on Windows 11, 2026-07-20: regedit /e
        /// against a key that does not exist returns exit code 0 and writes no file. Checking the
        /// exit code alone would report success for a backup containing nothing.
        /// </remarks>
        internal static StepResult ExportRegistryKey(string filePath, string registryPath,
                                                     bool absenceIsNormal, IRegistryTool tool = null)
        {
            tool = tool ?? DefaultRegistryTool;

            KeyProbe probe = ProbeKey(registryPath);

            if (probe == KeyProbe.Indeterminate)
                return StepResult.Failed(registryPath, "could not read " + registryPath + " to check whether it exists");

            if (probe == KeyProbe.Absent)
            {
                return absenceIsNormal
                    ? StepResult.Skipped(registryPath, "not present on this system")
                    : StepResult.Failed(registryPath, "expected " + registryPath + " is missing");
            }

            // Clear any file already at the target path FIRST. Otherwise the verification below
            // can be satisfied by a file this run did not write, and the method's promise to
            // verify what it produced would be false. Not reachable in today's modules - WThemes
            // is the only one looping several keys into a single filename and it holds exactly one
            // key - but it becomes live the moment a second key is added, silently.
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex)
            {
                return StepResult.Failed(registryPath, "could not clear the previous export at " + filePath + ": " + ex.Message);
            }

            ProcessOutcome outcome = tool.Export(filePath, registryPath);

            if (outcome == null)
                return StepResult.Failed(registryPath, "the registry tool returned no outcome");

            if (!outcome.Started)
                return StepResult.Failed(registryPath, "could not start regedit: " + outcome.Error);

            if (outcome.TimedOut)
                return StepResult.Failed(registryPath, "regedit did not exit - it may be showing an error dialog");

            if (outcome.Error != null)
                return StepResult.Failed(registryPath, "regedit ran but its outcome could not be determined: " + outcome.Error);

            if (outcome.ExitCode != 0)
                return StepResult.Failed(registryPath, "regedit exited with code " + outcome.ExitCode);

            string readError;

            switch (RegFile.Validate(filePath, out readError))
            {
                case RegFileCheck.Valid:
                    return StepResult.Succeeded(registryPath, "exported " + registryPath);
                case RegFileCheck.Missing:
                    return StepResult.Failed(registryPath, "regedit reported success but wrote no file");
                case RegFileCheck.Empty:
                    return StepResult.Failed(registryPath, "regedit wrote an empty file");
                case RegFileCheck.BadHeader:
                    return StepResult.Failed(registryPath, "the exported file is not a valid .reg file");
                case RegFileCheck.Unreadable:
                    return StepResult.Failed(registryPath, "could not read back the exported file: " + readError);
                default:
                    // Fail closed. A RegFileCheck member added later must not silently pass here.
                    return StepResult.Failed(registryPath, "the exported file could not be classified");
            }
        }

        /// <summary>
        /// Imports one .reg file, having first checked it is worth importing.
        /// </summary>
        /// <remarks>
        /// The pre-flight matters more than the exit code here. regedit /s returns 0 on a file it
        /// only partially applied, so a successful run is reported as "applied", never "verified" -
        /// reading the keys back to prove an import took is Phase 2b. Refusing a malformed file
        /// BEFORE launching regedit is the one strong guarantee available on this path.
        /// </remarks>
        internal static StepResult ImportRegistryKey(string filePath, string registryPath,
                                                     IRegistryTool tool = null)
        {
            tool = tool ?? DefaultRegistryTool;

            string readError;

            switch (RegFile.Validate(filePath, out readError))
            {
                case RegFileCheck.Valid:
                    break;   // the only case that may proceed to the registry
                case RegFileCheck.Missing:
                    return StepResult.Skipped(registryPath, "nothing was backed up for this item");
                case RegFileCheck.Empty:
                    return StepResult.Failed(registryPath, "the backed-up file is empty - not importing it");
                case RegFileCheck.BadHeader:
                    return StepResult.Failed(registryPath, "the backed-up file is not a valid .reg file - not importing it");
                case RegFileCheck.Unreadable:
                    // Deliberately NOT worded as "invalid". We could not read it, so we do not know
                    // whether it is valid - and a locked or ACL-denied file is a different problem
                    // for the user to fix than a corrupt one.
                    return StepResult.Failed(registryPath, "could not read the backed-up file: " + readError);
                default:
                    // Fail CLOSED. Without this, a RegFileCheck member added later falls through to
                    // regedit /s unexamined, which would invert this method's one real guarantee:
                    // that a file we cannot vouch for never reaches the registry.
                    return StepResult.Failed(registryPath, "the backed-up file could not be classified - not importing it");
            }

            ProcessOutcome outcome = tool.Import(filePath);

            if (outcome == null)
                return StepResult.Failed(registryPath, "the registry tool returned no outcome");

            if (!outcome.Started)
                return StepResult.Failed(registryPath, "could not start regedit: " + outcome.Error);

            if (outcome.TimedOut)
                return StepResult.Failed(registryPath, "regedit did not exit - it may be showing an error dialog");

            if (outcome.Error != null)
            {
                // regedit started, so the registry may already have been written to. Saying it
                // could not start would be a false claim about whether the machine changed.
                return StepResult.Failed(registryPath,
                    "regedit ran but its outcome could not be determined, so the registry may have been partly changed: " + outcome.Error);
            }

            if (outcome.ExitCode != 0)
                return StepResult.Failed(registryPath, "regedit exited with code " + outcome.ExitCode);

            return StepResult.Applied(registryPath, registryPath);
        }

        // Reg operations

        /// <summary>
        /// Whether a registry key is present, absent, or could not be determined.
        /// </summary>
        /// <remarks>
        /// The third state is the point. This method is the Skipped-vs-Failed discriminator for the
        /// whole backup path, and "I could not tell" is a failure of the tool, not an absence of the
        /// data - reporting a permission-denied probe as Absent would silently downgrade a real
        /// failure into a reassuring "not present on this system".
        /// </remarks>
        public static KeyProbe ProbeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return KeyProbe.Absent;

            KeyProbe hkcu = ProbeUnder(key, Registry.CurrentUser);
            if (hkcu != KeyProbe.Absent)
                return hkcu;

            return ProbeUnder(key, Registry.LocalMachine);
        }

        /// <summary>
        /// Convenience wrapper for callers that only need a yes/no and must never throw.
        /// </summary>
        /// <remarks>
        /// Indeterminate deliberately maps to FALSE here, the opposite of the backup path's mapping.
        /// The only caller is the IsInstalled() tree-build (ConfPageView.SelectInstalled), and
        /// auto-checking a module whose keys could not be probed would manufacture a Failed row in
        /// the very summary this phase exists to make trustworthy.
        /// </remarks>
        public static bool KeyExists(string key)
            => ProbeKey(key) == KeyProbe.Present;

        private static KeyProbe ProbeUnder(string key, RegistryKey baseKey)
        {
            string prefix = baseKey.Name + "\\";

            // Only probe under this hive if the path actually names it. The previous implementation
            // stripped only the matching prefix and then probed the remainder under BOTH hives, so
            // an HKCU path was also looked up under HKLM with "HKEY_CURRENT_USER\" still attached -
            // always null, so the HKLM half of the check was dead for every HKCU input.
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return KeyProbe.Absent;

            string subKey = key.Substring(prefix.Length);

            try
            {
                using (RegistryKey opened = baseKey.OpenSubKey(subKey))
                {
                    return opened != null ? KeyProbe.Present : KeyProbe.Absent;
                }
            }
            catch (System.Security.SecurityException ex)
            {
                return Undetermined(key, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Undetermined(key, ex);
            }
            catch (Exception ex)
            {
                // A malformed path or an unexpected provider error. Not knowing is the honest answer.
                return Undetermined(key, ex);
            }
        }

        /// <summary>
        /// Records why a key could not be probed, then reports that we could not tell.
        /// </summary>
        /// <remarks>
        /// The logging is the point of the helper. Returning Indeterminate without recording the
        /// cause would leave the user with "could not read this key" and no way to learn whether
        /// that was a permission problem, a malformed path, or a provider fault - which is the same
        /// silent discard this whole phase exists to remove.
        /// </remarks>
        private static KeyProbe Undetermined(string key, Exception ex)
        {
            logger.LogMessage("Could not probe " + key + ": " + ex.Message);
            return KeyProbe.Indeterminate;
        }

        // Restart explorer.exe if required for back up closure
        public static void RestartExplorer()
        {
            // Retrieve explorer.exe process
            Process[] explorerProcesses = Process.GetProcessesByName("explorer");

            foreach (Process explorerProcess in explorerProcesses)
            {
                try
                {
                    // Kill explorer process
                    explorerProcess.Kill();
                    explorerProcess.WaitForExit();

                    // Start new explorer process
                    Process.Start("explorer.exe");
                }
                catch (Exception ex)
                {
                    logger.Log($"Error restarting explorer.exe: {ex.Message}");
                }
            }
        }

        // Show disk space info on ConfPage
        public static string GetSystemPartitionDiskSpaceInfo()
        {
            try
            {
                string systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
                DriveInfo driveInfo = new DriveInfo(systemDrive);

                long availableSpace = driveInfo.AvailableFreeSpace;
                long totalSpace = driveInfo.TotalSize;

                string info = $"Available Space: {FormatBytes(availableSpace)} | Total Space: {FormatBytes(totalSpace)}";
                return info;
            }
            catch (Exception ex)
            {
                return $"Error retrieving disk space information: {ex.Message}";
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            int suffixIndex = 0;

            while (bytes >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                bytes /= 1024;
                suffixIndex++;
            }

            return $"{bytes} {suffixes[suffixIndex]}";
        }

        // Check for running processes in Confs
        public static bool IsProcessRunning(string processName)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(processName);

                try
                {
                    return processes.Length > 0;
                }
                finally
                {
                    foreach (Process p in processes)
                        p.Dispose();
                }
            }
            catch (Exception)
            {
                // A false negative here only means the user is not prompted to close the app.
                return false;
            }
        }

        /// <summary>
        /// The shared budget for the wait pass in <see cref="CloseProcess"/>, in milliseconds.
        /// </summary>
        /// <remarks>
        /// Shared across every process in the tree, not five seconds each - a bounded per-process
        /// wait is still unbounded in aggregate when Chrome yields 10-30 chrome.exe entries, and that
        /// unbounded wait runs synchronously under an async void click handler with no dispatch off
        /// the UI thread, so it froze the window instead of the process it was meant to bound.
        /// </remarks>
        private const int CloseTimeoutMs = 5000;

        /// <summary>
        /// Asks every instance of a process to terminate.
        /// </summary>
        /// <remarks>
        /// Two passes over the same process list, sharing one deadline, rather than kill-then-wait
        /// per process. The guard is still the point: Kill() throws InvalidOperationException when
        /// the process exited between enumeration and the call - likely, not exotic, because Chrome
        /// is a whole tree of child processes that come and go - and Win32Exception when access is
        /// denied. The browser modules reach this from an async void click handler, so an escape
        /// here took down the entire run and every result collected with it.
        ///
        /// Pass 1 kills every process with no waiting. Pass 2 waits on each against the remaining
        /// shared budget. Kill() is asynchronous, so without waiting at all the caller starts copying
        /// while the process is still flushing and releasing file handles - a just-killed Chrome
        /// still holds its SQLite files, so the copy that follows hits locked files. See
        /// <see cref="CloseTimeoutMs"/> for why the budget is shared instead of per process.
        /// </remarks>
        public static CloseResult CloseProcess(string processName)
        {
            Process[] processes;

            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch (Exception)
            {
                return CloseResult.AccessDenied;
            }

            if (processes.Length == 0)
                return CloseResult.NotRunning;

            CloseResult worst = CloseResult.Exited;

            // Pass 1: kill everything, no waiting. Handles stay open - pass 2 needs them.
            foreach (Process process in processes)
            {
                try
                {
                    process.Kill();
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    logger.LogMessage("Could not close " + processName + ": " + ex.Message);
                    worst = Worse(worst, CloseResult.AccessDenied);
                }
                catch (InvalidOperationException)
                {
                    // Already gone between enumeration and Kill. Nothing to report.
                }
                catch (Exception ex)
                {
                    logger.LogMessage("Could not close " + processName + ": " + ex.Message);
                    worst = Worse(worst, CloseResult.StillRunning);
                }
            }

            // Pass 2: wait on each against the remaining shared budget, then dispose.
            Stopwatch waited = Stopwatch.StartNew();

            foreach (Process process in processes)
            {
                try
                {
                    int remaining = CloseTimeoutMs - (int)waited.ElapsedMilliseconds;

                    // When the budget is spent, still ASK whether it exited rather than assuming it
                    // did not. Killing 30 Chrome children can exhaust the budget while every one of
                    // them died, and reporting those as still-running manufactures a failed backup
                    // out of a completely successful close.
                    bool exited = remaining > 0
                        ? process.WaitForExit(remaining)
                        : process.HasExited;

                    if (!exited)
                        worst = Worse(worst, CloseResult.StillRunning);
                }
                catch (Exception)
                {
                    // Deliberately silent. The likely trigger here is a process that was already
                    // gone before pass 1 could kill it - pass 1 treats that as nothing to report,
                    // and reaching the opposite conclusion about the same process would turn a
                    // clean browser close into a failed backup.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return worst;
        }

        /// <summary>
        /// Keeps the more severe of two close outcomes.
        /// </summary>
        /// <remarks>
        /// Plain assignment would be last-write-wins, which quietly downgrades. A browser is a tree
        /// of child processes and mixed outcomes across them are ordinary: if one child is
        /// access-denied and a later one merely fails to die, straight assignment reports the
        /// milder result and the caller decides it may safely copy files that are still locked.
        /// </remarks>
        /// <remarks>
        /// Internal rather than private so tests can call THIS function. A test that reimplements
        /// the comparison locally and asserts on its own copy passes even when the production code
        /// reverts to plain assignment - it verifies the reimplementation, not the shipped
        /// behaviour, which is precisely the bug this method exists to prevent.
        /// </remarks>
        internal static CloseResult Worse(CloseResult a, CloseResult b)
            => Severity(a) >= Severity(b) ? a : b;

        internal static int Severity(CloseResult r)
        {
            switch (r)
            {
                case CloseResult.NotRunning: return 0;
                case CloseResult.Exited: return 1;
                case CloseResult.StillRunning: return 2;
                case CloseResult.AccessDenied: return 3;
                default: return 3;
            }
        }

        /// <summary>
        /// Opens an http/https URL in the user's default browser at their normal privilege level.
        /// </summary>
        /// <remarks>
        /// Routed through explorer.exe deliberately. Appcopier's manifest requests highestAvailable
        /// because registry export needs it, and ShellExecute hands the parent's elevated token to
        /// the child - so launching the browser directly opens it as Administrator. That leaves
        /// admin-owned files in the browser profile (which can stop it starting normally afterwards)
        /// and silently grants admin rights to anything downloaded and run from that window.
        /// explorer.exe hands the request to the already-running shell, which runs as the user.
        ///
        /// This is also why the URL is checked first: with a shell launch, a non-web string is not
        /// an invalid argument, it is an arbitrary file or program to execute - at admin integrity.
        /// Callers pass constants today, so the check is here to keep that true.
        ///
        /// The remaining failure modes are environmental rather than programming errors - no default
        /// browser registered, a broken protocol association, a locked-down machine - so they are
        /// surfaced to the user and logged, not rethrown.
        /// </remarks>
        internal static void OpenUrl(string url)
        {
            if (!IsWebUrl(url))
            {
                logger.Log("Refused to open {0}: not an http/https URL.", url ?? "(null)");
                return;
            }

            try
            {
                // ArgumentList quotes the value properly rather than pasting it into a command line.
                var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                startInfo.ArgumentList.Add(url);

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                ReportUrlFailure(url, ex);
            }
        }

        /// <summary>
        /// True only for absolute http/https URLs. See <see cref="OpenUrl"/> for why this matters.
        /// </summary>
        internal static bool IsWebUrl(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out Uri parsed)
               && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

        /// <summary>
        /// Logs a message without ever throwing, for the timer and thread-pool paths where an
        /// escaping exception would terminate the process.
        /// </summary>
        /// <remarks>
        /// Passing the message as a format ARGUMENT rather than as the format string matters: the
        /// underlying logger runs string.Format, and an exception message containing braces would
        /// otherwise throw inside the very handler meant to contain a failure.
        /// </remarks>
        internal static void LogQuietly(string message)
        {
            try
            {
                logger.Log("{0}", message);
            }
            catch
            {
            }
        }

        private static void ReportUrlFailure(string url, Exception ex)
        {
            // OpenUrl is called from a System.Timers.Timer thread, and .NET 8 no longer swallows
            // exceptions thrown by Elapsed handlers the way .NET Framework did - anything that
            // escapes takes the process down. So the reporting needs containing too: showing a
            // dialog can itself fail on exactly the locked-down machines this catch exists for.
            // Swallowing is the last resort here, not a shortcut; the alternative is a crash whose
            // only cause was that we could not display a warning about a link.
            try
            {
                logger.Log("Failed to open {0}: {1}", url, ex.Message);

                MessageBox.Show(
                    $"Could not open this link in your browser:\n\n{url}\n\n{ex.Message}",
                    "Unable to open link",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Runs Windows Terminal and waits for it, reporting how it went.
        /// </summary>
        /// <remarks>
        /// Replaces an "async void" version that returned to its caller at the first await, so
        /// AStoreApps logged "Backup successful" before winget had started. async void cannot feed a
        /// result into anything - that is not a style preference here, it is the reason the module
        /// could not report the truth.
        /// </remarks>
        internal static async Task<ProcessOutcome> RunWTAsync(string args)
        {
            if (!File.Exists(DataHelper.Data.ShellWT))
                return ProcessOutcome.NeverStarted("Windows Terminal is not installed");

            return await Task.Run(() =>
            {
                // Tracks whether Process.Start succeeded, mirroring RegeditTool.Run in
                // IRegistryTool.cs: once Start() has returned, Windows Terminal may already be
                // running, so an exception from WaitForExit must not be reported as "never started".
                bool started = false;

                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = DataHelper.Data.ShellWT,
                        Arguments = args,
                        // The old WorkingDirectory was Data.DataRootDir, which may not exist yet -
                        // Process.Start then threw Win32Exception onto the sync context.
                        UseShellExecute = false,
                        CreateNoWindow = false
                    };

                    using (Process proc = Process.Start(startInfo))
                    {
                        if (proc == null)
                            return ProcessOutcome.NeverStarted("Windows Terminal did not start");

                        started = true;
                        proc.WaitForExit();
                        return ProcessOutcome.Ran(proc.ExitCode);
                    }
                }
                catch (Exception ex)
                {
                    return started
                        ? ProcessOutcome.OutcomeUnknown(ex.Message)
                        : ProcessOutcome.NeverStarted(ex.Message);
                }
            }).ConfigureAwait(false);
        }
    }
}