using Appcopier;

namespace Conf
{
    public class DUSB : RegistryModule
    {
        protected override string Key => @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Shell\USB";

        // Narrow shell-notification key that many installs never create.
        protected override bool AbsenceIsNormal => true;

        public DUSB()
        {
            Title = "USB Devices";
            Info = "This will backup the Windows USB Devices settings.";
        }
    }
}
