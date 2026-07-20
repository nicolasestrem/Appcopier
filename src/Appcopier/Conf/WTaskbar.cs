using Appcopier;

namespace Conf
{
    public class WTaskbar : RegistryModule
    {
        protected override string Key => @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        // Core shell key; its absence means a broken profile, not a machine without a taskbar.
        protected override bool AbsenceIsNormal => false;

        public WTaskbar()
        {
            Title = "Taskbar";
            Info = "This will export settings related to Taskbar and behaviors (Taskbar alignment, size and layout, Widgets etc).";
        }
    }
}
