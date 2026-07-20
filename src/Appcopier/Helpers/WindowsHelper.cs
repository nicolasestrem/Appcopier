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

        internal static async Task CopyFolder(string source, string destination)
        {
            try
            {
                DirectoryInfo sourceDir = new DirectoryInfo(source);
                DirectoryInfo destinationDir = new DirectoryInfo(destination);

                if (!sourceDir.Exists)
                {
                    logger.Log($"Source directory does not exist: {source}");
                    return;
                }

                if (!destinationDir.Exists)
                {
                    destinationDir.Create();
                    logger.Log($"Destination directory created: {destination}");
                }

                foreach (FileInfo file in sourceDir.GetFiles())
                {
                    string destinationFilePath = Path.Combine(destinationDir.FullName, file.Name);

                    try
                    {
                        using (FileStream sourceStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                        using (FileStream destinationStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                        {
                            await sourceStream.CopyToAsync(destinationStream);
                        }

                        logger.Log($"Copied file: {file.FullName} to {destinationFilePath}");
                    }
                    catch (Exception ex)
                    {
                        logger.Log($"Error copying file {file.FullName}: {ex.Message}");
                    }
                }

                foreach (DirectoryInfo subDirectory in sourceDir.GetDirectories())
                {
                    string newDestinationPath = Path.Combine(destinationDir.FullName, subDirectory.Name);
                    await CopyFolder(subDirectory.FullName, newDestinationPath).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.Log($"Error copying folder {source} to {destination}: {ex.Message}");
            }
        }

        internal static void CopyFile(string source, string destination)
        {
            try
            {
                File.Copy(source, destination, true);
            }
            catch (Exception ex)
            {
                logger.Log(ex.Message);
            }
        }

        internal static void ExportImportRegistryKey(string filePath, string registryPath, bool import)
        {
            string path = $"\"{filePath}\"";
            string key = $"\"{registryPath}\"";

            using (Process proc = new Process())
            {
                try
                {
                    proc.StartInfo.FileName = "regedit.exe";
                    proc.StartInfo.UseShellExecute = false;
                    proc.StartInfo.Verb = "runas";

                    string arguments = import ? $"/s {path} {key}" : $"/e {path} {key}";

                    proc.StartInfo.Arguments = arguments;
                    proc.Start();

                    proc.WaitForExit();
                }
                catch (Exception ex)
                {
                    logger.Log(ex.Message);
                }
            }
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
            Process[] processes = Process.GetProcessesByName(processName);
            return processes.Length > 0;
        }

        // Close running processes in Confs
        public static void CloseProcess(string processName)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            foreach (Process process in processes)
            {
                process.Kill(); // Kill method to forcefully terminate process
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

        // Run Windows Terminal in Confs
        public static async void RunWT(string args)
        {
            var startInfo = new ProcessStartInfo()
            {
                FileName = DataHelper.Data.ShellWT,
                Arguments = args,
                WorkingDirectory = DataHelper.Data.DataRootDir,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            await Task.Run(() =>
            {
                Process.Start(startInfo).WaitForExit();
            });
        }
    }
}