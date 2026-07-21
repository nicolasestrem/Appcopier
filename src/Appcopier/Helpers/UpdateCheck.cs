using System;
using System.Net;
using System.Windows.Forms;
using DataHelper;

namespace Appcopier
{
    /// <summary>
    /// The interactive half of the update check: everything that talks to the user, and the version
    /// comparison that decides what it says.
    /// </summary>
    /// <remarks>
    /// This lived on <see cref="Data"/> until Phase 4 PR 2, for two reasons that both point the same
    /// way. It is almost entirely MessageBoxes, and Data is engine code that no longer references
    /// WinForms; and it calls <see cref="Program"/>, which is the application entry point and cannot
    /// be reached from a library the application itself depends on.
    ///
    /// It moved verbatim, and then took exactly one change: the WebClient below is now disposed.
    /// Everything a deployed client depends on is untouched - the URL, the parse, and all five
    /// message texts.
    ///
    /// What stayed behind on Data is the part with no UI and no back-reference: ParseLatestVersion,
    /// IsInet, and the Uri constants. ParseLatestVersion in particular is pinned by
    /// VersionParsingTests against the real Properties/AssemblyInfo.cs, and that pairing - remote
    /// parse against local reflection - is the invariant that keeps the update checker honest.
    /// </remarks>
    internal static class UpdateCheck
    {
        public static void CheckForUpdates()
        {
            if (Data.IsInet() == true)
            {
                try
                {
                    // Disposed, which the pre-move code did not do. DownloadString has already
                    // returned the whole body by the time this block exits, so nothing here reads
                    // the client afterwards - and Data.IsInet, ten lines below in the file this
                    // moved out of, already wrapped its own WebClient exactly this way.
                    string assemblyInfo;

                    using (WebClient client = new WebClient())
                    {
                        assemblyInfo = client.DownloadString(Data.Uri.URL_ASSEMBLY);
                    }

                    string parsed = Data.ParseLatestVersion(assemblyInfo);

                    if (string.IsNullOrWhiteSpace(parsed))
                    {
                        // The file downloaded but held no AssemblyFileVersion we could read. Saying
                        // so beats the old behavior, which compared "" against the current version,
                        // found them unequal, and offered a download for a nonexistent release.
                        MessageBox.Show(
                            "Could not read the latest version number from the update file.",
                            "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    // Both sides go through the same normalization. Comparing a raw remote string
                    // against a normalized local one means a four-part or suffixed version upstream
                    // would never match, and every up-to-date user would be offered a phantom
                    // update on every check, forever.
                    string latestVersion = Program.NormalizeVersion(parsed);
                    string currentVersion = Program.GetCurrentVersionTostring();

                    if (latestVersion == currentVersion)                          // Up-to-date
                    {
                        MessageBox.Show($"No new updates available.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else                                                          // Update available
                    {
                        if (MessageBox.Show($"App version {latestVersion} available.\nDo you want to open the Download page?", "App update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
                            Utils.OpenUrl(Data.Uri.URL_GITLATEST);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Checking for App updates failed.\n{ex.Message}");
                }
            }
            else if (Data.IsInet() == false)
            {
                MessageBox.Show($"Problem on Internet connection: Checking for App updates failed");
            }
        }
    }
}
