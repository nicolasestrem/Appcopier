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
            => NormalizeVersion(
                typeof(Program).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
                ?? Application.ProductVersion);

        /// <summary>
        /// Reduces a raw version string to the three-part form used throughout the app.
        /// </summary>
        /// <remarks>
        /// This runs during MainForm construction, so it must not throw: an exception here is a
        /// startup crash with no UI to report it. Every input that cannot be reduced to three
        /// components is therefore passed through unchanged rather than parsed.
        ///
        /// Deliberately NOT substituted with a realistic-looking placeholder like "0.0.0" on
        /// failure. Any unusable version makes the update check offer a phantom update - that much
        /// is unavoidable here, since the comparison in CheckForUpdates is a string ==. What the
        /// choice buys is diagnosis: "unknown" in the title bar says the app cannot determine its
        /// own version, whereas "0.0.0" reads as a real installed version and sends whoever
        /// investigates the repeating update prompt looking in the wrong place entirely.
        /// </remarks>
        internal static string NormalizeVersion(string rawVersion)
        {
            if (string.IsNullOrWhiteSpace(rawVersion))
                return UnknownVersion;

            string raw = rawVersion.Trim();

            // Strip any SemVer build/prerelease suffix ("1.2.3+sha", "1.2.3-preview") before parsing.
            int suffix = raw.IndexOfAny(new[] { '+', '-' });
            string candidate = suffix >= 0 ? raw.Substring(0, suffix) : raw;

            // Build is -1 when fewer than three components were supplied, and ToString(3) throws on
            // those - so "1.2" has to fail this check, not just parse successfully.
            return Version.TryParse(candidate, out Version parsed) && parsed.Build >= 0
                ? parsed.ToString(3)
                : raw;
        }

        /// <summary>
        /// Shown when the assembly carries no usable version at all. Chosen so it cannot be mistaken
        /// for a real version number by a user reading the title bar.
        /// </summary>
        internal const string UnknownVersion = "unknown";

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
