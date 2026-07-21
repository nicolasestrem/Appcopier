using Appcopier;

namespace Conf
{
    /// <summary>
    /// The current user's environment variables (<c>HKCU\Environment</c>).
    /// </summary>
    /// <remarks>
    /// A plain single-key registry module - no file handling - even though it ships with the
    /// file-based developer modules. The category is about what the user is backing up, not about
    /// which base class it happens to need.
    ///
    /// Two limitations are disclosed in Info rather than engineered around:
    ///
    /// 1. A restore is an additive MERGE, like every other registry import in this app. A variable
    ///    that exists on this machine but not in the backup survives the restore; only variables
    ///    the backup names are overwritten. This is the Phase 2b fidelity stance, stated here
    ///    because PATH is exactly the value where a user is most likely to expect otherwise.
    ///
    /// 2. No WM_SETTINGCHANGE broadcast is sent, so already-running shells and editors keep the
    ///    variables they started with; new processes see the restored values. Broadcasting is
    ///    deliberately not built here - it would be this app's first message sent to every top-level
    ///    window, which is a different kind of operation from writing a key and belongs to its own
    ///    review, not to a module added in a coverage phase.
    /// </remarks>
    public class EEnvironment : RegistryModule
    {
        public EEnvironment()
        {
            Title = "Environment variables";
            Info = "This will back up the environment variables belonging to your Windows account, including your user PATH. Restoring merges them back: variables saved in the backup are overwritten, and any variable you have added since is left alone. Programs already running keep the values they started with until you restart them.";
            WarningMessage = "Restoring replaces your user PATH with the one from the backup. If a program you installed since the backup added itself to PATH, its entry is overwritten and that program may stop being found from a command prompt until you reinstall or re-add it.";
        }

        protected override string Key => @"HKEY_CURRENT_USER\Environment";

        // False: the key exists in every loaded user profile - Windows creates it with the account.
        // If it is absent, either the probe failed or the profile is broken, and both are faults
        // worth showing red rather than a machine that simply never used the feature.
        protected override bool AbsenceIsNormal => false;
    }
}
