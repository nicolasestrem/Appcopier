using System;

namespace Conf
{
    public class WUpdates : MultiKeyRegistryModule
    {
        public WUpdates()
        {
            Title = "Windows Update";
            Info = "This will back up Windows update settings (when to install automatic updates, when to reboot after installing updates, DetectionFrequency, AutoInstallMinorUpdates etc).";

            Keys.Add(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate");
            Keys.Add(@"HKEY_LOCAL_MACHINE\Software\Policies\Microsoft\Windows\WindowsUpdate\AU");
        }

        // The CurrentVersion\WindowsUpdate key is core servicing state present on every install, so
        // its absence is a real fault. The policy key under \AU exists only where WSUS or Group
        // Policy configured it, which is a minority of machines - this module therefore lands on
        // aggregation rule 4 (captured one, skipped one) on a large share of healthy systems.
        protected override bool AbsenceIsNormal(string key)
            => key.EndsWith(@"\AU", StringComparison.OrdinalIgnoreCase);
    }
}
