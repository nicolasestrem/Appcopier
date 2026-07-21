using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;

namespace DataHelper
{
    internal class Data
    {
        internal static string RoamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        internal static string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        internal static string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        internal static string WindowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        // %USERPROFILE%. Added in Phase 3b for .ssh, which lives at the profile root rather than
        // under AppData. GetFolderPath rather than ExpandEnvironmentVariables to match every other
        // root here, and because the environment variable is user-writable.
        internal static string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Application.StartupPath has no trailing separator on .NET Framework but does on .NET 5+,
        // so plain concatenation would yield "...\\app\". Path.Combine normalizes both cases.
        // The trailing separator is part of this field's contract - callers concatenate onto it.
        public static string DataRootDir = Path.Combine(Application.StartupPath, "app") +
                                            @"\";

        // winget. Same App Execution Alias directory Windows Terminal lives in.
        //
        // Deliberately winget.exe and not wt.exe: the app used to run winget THROUGH Windows
        // Terminal, and wt is a launcher that exits as soon as it has handed the command over, so
        // waiting on it told us nothing about winget. See Utils.RunWingetAsync for the measurement.
        public static string ShellWinget = LocalAppData +
                                        @"\Microsoft\WindowsApps\winget.exe";

        // Obtain current date and time
        internal static string Now = $"{DateTime.Now:G}";

        internal static string NowShort = $"{DateTime.Now:yyyy-MM-dd - HH.mm}";

        public static class Uri
        {
            public const string URL_BUILTBYBEL = "https://www.builtbybel.com";
            public const string URL_ASSEMBLY = "https://raw.githubusercontent.com/builtbybel/Appcopier/main/src/Appcopier/Properties/AssemblyInfo.cs";
            public const string URL_GITREPO = "https://github.com/builtbybel/Appcopier";
            public const string URL_ICONATTRIBUTION = "https://www.flaticon.com/free-icon/backup_10426480";
            public const string URL_GITLATEST = URL_GITREPO + "/releases/latest";
        }

        /// <summary>
        /// Extracts the AssemblyFileVersion value out of the raw text of a Properties/AssemblyInfo.cs.
        /// </summary>
        /// <remarks>
        /// Pulled verbatim out of <see cref="Appcopier.UpdateCheck.CheckForUpdates"/> so the parse can
        /// be unit tested without network I/O or MessageBoxes. The logic is intentionally
        /// byte-for-byte identical to what the
        /// deployed v0.30.0 client does, including its quirks: only lines containing the literal
        /// "[assembly: AssemblyFileVersion" are considered (so AssemblyVersion is correctly ignored),
        /// the LAST such line wins, no match yields an empty string, and malformed lines throw.
        /// Do not "harden" this without also considering that the remote file format is the contract.
        /// </remarks>
        internal static string ParseLatestVersion(string assemblyInfoText)
        {
            var readVersion = assemblyInfoText.Split('\n');
            var infoVersion = readVersion.Where(t => t.Contains("[assembly: AssemblyFileVersion"));
            var latestVersion = "";
            foreach (var item in infoVersion)
            {
                latestVersion = item.Substring(item.IndexOf('(') + 2, item.LastIndexOf(')') - item.IndexOf('(') - 3);
            }

            return latestVersion;
        }

        // Check Inet
        public static bool IsInet()
        {
            try
            {
                using (var CheckInternet = new WebClient())
                using (CheckInternet.OpenRead("http://clients3.google.com/generate_204"))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}