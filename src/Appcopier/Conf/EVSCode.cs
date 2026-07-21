using Appcopier;
using DataHelper;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Conf
{
    /// <summary>
    /// Visual Studio Code's user profile: settings, key bindings and snippets.
    /// </summary>
    /// <remarks>
    /// Hand-rolled from BackupBase rather than inheriting <see cref="FileModule"/>, because two of
    /// its three targets are files and the third - snippets - is a directory of arbitrarily many
    /// user-named .json files. Teaching FileModule about folders to serve this one consumer is the
    /// mistake Phase 3a's critique caught with the dropped CommandModule: a base that fits one of
    /// its consumers is a worse seam than two honest ones. <see cref="WThemes"/> is the precedent
    /// for a heterogeneous module folding both kinds of sub-operation through one Aggregate.
    ///
    /// Stable VS Code only. Insiders (%APPDATA%\Code - Insiders) and VSCodium are deliberately out
    /// of scope for this phase - user decision, 2026-07-21 - as is the installed extension list,
    /// which the roadmap defers because reinstalling extensions needs an AStoreApps-style dialog
    /// and not a file copy. Per-profile settings under User\profiles\ are likewise not captured;
    /// this module carries the default profile, which is what the great majority of users have.
    /// </remarks>
    public class EVSCode : BackupBase
    {
        public List<string> Files = new List<string>();

        /// <remarks>
        /// Public readonly, the <see cref="FolderModule.Folder"/> arrangement: the tests read it to
        /// hold the declaration and the copy in agreement, and readonly keeps the folder named in
        /// the confirmation dialog identical to the one RestoreAsync writes.
        /// </remarks>
        public readonly string SnippetsFolder;

        public EVSCode()
        {
            Title = "VS Code settings";
            Info = "This will back up your Visual Studio Code user settings (settings.json), your custom key bindings (keybindings.json) and your user snippets folder.\n\nInstalled extensions are not included - those are reinstalled from the Marketplace rather than copied. VS Code Insiders and VSCodium are not covered.";
            WarningMessage = "VS Code must be closed before restoring. It rewrites settings.json while running, so an open window would overwrite the restored file - and closing it discards changes in any editor you have not saved.";

            string userDir = Path.Combine(Data.RoamingAppData, "Code", "User");

            Files.Add(Path.Combine(userDir, "settings.json"));
            Files.Add(Path.Combine(userDir, "keybindings.json"));

            SnippetsFolder = Path.Combine(userDir, "snippets");
        }

        // The profile directory, not the executable: this module backs up the profile, and a VS
        // Code that was installed and then uninstalled leaves settings behind that are still worth
        // capturing. It also keeps the probe working for portable installs whose exe is elsewhere.
        public override bool IsInstalled()
            => Directory.Exists(Path.Combine(Data.RoamingAppData, "Code", "User"));

        // Files first, then the folder - the order RestoreAsync applies them, read from the fields
        // on every access so the declaration cannot describe a different set than the restore
        // writes. DeveloperModuleTests pins both the order and the kinds.
        public override IReadOnlyList<RestoreTarget> RestoreTargets
        {
            get
            {
                List<RestoreTarget> targets = new List<RestoreTarget>();

                foreach (string f in Files)
                    targets.Add(RestoreTarget.File(f));

                targets.Add(RestoreTarget.Folder(SnippetsFolder));

                return targets;
            }
        }

        /// <remarks>
        /// The earned check, as on FolderModule and FileModule: this module closes VS Code, so it
        /// must answer honestly whether the chosen backup holds anything for it. Getting it wrong
        /// costs the user every unsaved editor buffer for a restore that then copies nothing.
        /// </remarks>
        public override bool HasBackupIn(string restorePath)
            => !string.IsNullOrWhiteSpace(restorePath)
               && Directory.Exists(Path.Combine(restorePath, Title));

        /// <remarks>
        /// VS Code runs several processes all named Code (the window, the extension host, the
        /// renderers); closing by name closes all of them, which is the intent - the extension host
        /// is one of the things that writes settings.json.
        ///
        /// NeedsConsent: the cost is unsaved work, which the user is the only one who can weigh.
        /// </remarks>
        public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
            => new[]
            {
                new RestoreCloseRequirement("Code", "Visual Studio Code", true)
            };

        public override async Task<ModuleResult> BackupAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();
            string backupDir = Path.Combine(path, Title);

            foreach (string f in Files)
            {
                CopyResult copy = await Utils.CopyFile(f, Path.Combine(backupDir, Path.GetFileName(f)))
                    .ConfigureAwait(true);

                // True: VS Code writes settings.json on the first setting the user changes and
                // keybindings.json on the first binding, so a fresh install legitimately has
                // neither. Absent means "never customised", not "broken".
                steps.Add(copy.ToFileStep(f, true));
            }

            CopyResult snippets = await Utils.CopyFolder(SnippetsFolder, SnippetsBackupDir(path))
                .ConfigureAwait(true);
            steps.Add(snippets.ToStep(SnippetsStepName, true));

            return ModuleResult.Aggregate(steps);
        }

        public override async Task<ModuleResult> RestoreAsync(string path)
        {
            List<StepResult> steps = new List<StepResult>();
            string backupDir = Path.Combine(path, Title);

            foreach (string f in Files)
            {
                CopyResult copy = await Utils.CopyFile(Path.Combine(backupDir, Path.GetFileName(f)), f)
                    .ConfigureAwait(true);

                // Absence is always normal on this side and the reason is always NothingBackedUp:
                // the source is the backup folder, so "not present on this system" would describe
                // the wrong machine. Same rule as FileModule.RestoreAsync.
                steps.Add(copy.ToFileStep(f, true, NothingBackedUp));
            }

            CopyResult snippets = await Utils.CopyFolder(SnippetsBackupDir(path), SnippetsFolder)
                .ConfigureAwait(true);
            steps.Add(snippets.ToStep(SnippetsStepName, true, NothingBackedUp));

            return ModuleResult.Aggregate(steps);
        }

        // Inside {Title}\ with the two files, so HasBackupIn's single directory probe covers the
        // whole module and the artifacts stay grouped.
        private string SnippetsBackupDir(string path) => Path.Combine(path, Title, "snippets");

        // A name for the step row rather than the full profile path, which would render as
        // "captured C:\Users\...\Roaming\Code\User\snippets" in the summary. The two file steps
        // carry their paths because they need distinguishing from each other; this one does not.
        private const string SnippetsStepName = "VS Code snippets";
    }
}
