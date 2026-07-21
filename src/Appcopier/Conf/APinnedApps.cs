using Appcopier;
using DataHelper;
using System.Collections.Generic;

namespace Conf
{
    public class APinnedApps : FolderModule
    {
        public APinnedApps()
            : base(Data.LocalAppData + "\\Packages\\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\\LocalState")
        {
            Title = "Remember pinned app preferences";
            Info = "The Start menu is comprised of three sections: Pinned, All apps, and Recommended.\nThis will back up pinned items on the Start menu and restore the pinned items to the Start menu.";
            WarningMessage = "This is reserved for Windows 11.";
        }

        // No consent: Windows brings StartMenuExperienceHost back within seconds on its own, so the
        // user is not being asked to give up anything they can see. A checkbox here would spend the
        // dialog's attention budget on the one close that costs nothing, at the expense of the
        // closes that do.
        public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
            => new[]
            {
                new RestoreCloseRequirement("StartMenuExperienceHost", "the Start menu", false)
            };
    }
}
