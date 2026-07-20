using Appcopier;
using DataHelper;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Conf
{
    public class APinnedApps : BackupBase
    {
        public string Folder = Data.LocalAppData + "\\Packages\\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\\LocalState";

        public APinnedApps()
        {
            Title = "Remember pinned app preferences";
            Info = "The Start menu is comprised of three sections: Pinned, All apps, and Recommended.\nThis will back up pinned items on the Start menu and restore the pinned items to the Start menu.";
            IsWarning();
        }


        private void IsWarning()
        {
            WarningMessage = "This is reserved for Windows 11.";
        }

        public override bool IsInstalled()
        {
            return Directory.Exists(Folder);
        }

        public override IReadOnlyList<RestoreTarget> RestoreTargets
            => new[] { RestoreTarget.Folder(Folder) };

        // No consent: Windows brings StartMenuExperienceHost back within seconds on its own, so the
        // user is not being asked to give up anything they can see. A checkbox here would spend the
        // dialog's attention budget on the one close that costs nothing, at the expense of the
        // browser closes that do.
        public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
            => new[]
            {
                new RestoreCloseRequirement("StartMenuExperienceHost", "the Start menu", false)
            };

        public override async Task<ModuleResult> BackupAsync(string path)
        {
            CopyResult copy = await Utils.CopyFolder(Folder, Path.Combine(path, Title));

            return ModuleResult.Aggregate(new[] { copy.ToStep(Title, true) });
        }

        public override async Task<ModuleResult> RestoreAsync(string path)
        {
            CopyResult copy = await Utils.CopyFolder(Path.Combine(path, Title), Folder);

            return ModuleResult.Aggregate(new[] { copy.ToStep(Title, true, NothingBackedUp) });
        }
    }
}
