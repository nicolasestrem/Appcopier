using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Appcopier
{
    internal static class Program
    {
        /// <summary>
        /// Get app version
        /// </summary>
        /// <remarks>
        /// Reads AssemblyFileVersion directly, which is the exact attribute DataHelper.CheckForUpdates
        /// scrapes out of the remote Properties/AssemblyInfo.cs - so the local and remote sides of the
        /// version comparison can never disagree. Application.ProductVersion is deliberately not used
        /// as the primary source: on .NET 5+ it prefers AssemblyInformationalVersion, which the SDK may
        /// decorate with a "+&lt;commit-sha&gt;" suffix that would make new Version(...) throw.
        /// </remarks>
        internal static string GetCurrentVersionTostring()
        {
            string version = typeof(Program).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
                             ?? Application.ProductVersion;

            // Defensive: strip any SemVer build/prerelease suffix before Version can choke on it.
            int suffix = version.IndexOfAny(new[] { '+', '-' });
            if (suffix >= 0)
                version = version.Substring(0, suffix);

            return new Version(version).ToString(3);
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
