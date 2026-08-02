using System;
using System.Collections.Generic;

namespace Appcopier
{
    /// <summary>
    /// One module's view-model row for the restore wizard's "contents &amp; portability" step: whether
    /// the backup folder actually holds something for it, what the manifest recorded about the run,
    /// and the warning to show inline.
    /// </summary>
    internal sealed class RestoreContentsRow
    {
        internal BackupBase Module { get; }
        internal bool HasBackup { get; }
        internal string ManifestState { get; }
        internal string Warning { get; }

        internal RestoreContentsRow(BackupBase module, bool hasBackup, string manifestState, string warning)
        {
            Module = module;
            HasBackup = hasBackup;
            ManifestState = manifestState;
            Warning = warning;
        }
    }

    /// <summary>
    /// Builds the wizard's step-2 rows and the provenance banner, both read straight off the engine
    /// seams: <see cref="RestoreScope.HasBackup"/> for presence and the parsed manifest for state.
    /// </summary>
    /// <remarks>
    /// Unknown renders as unknown, never inferred: a module absent from the manifest (every backup
    /// taken before the manifest existed, or a type this build retired) gets a null state and the view
    /// shows no chip. Provenance is null unless something actually differs, so a same-machine backup
    /// shows no scary banner.
    /// </remarks>
    internal static class RestoreContents
    {
        internal static IReadOnlyList<RestoreContentsRow> For(IReadOnlyList<BackupBase> modules,
                                                              string restoreSourcePath, ManifestData manifest)
        {
            List<RestoreContentsRow> rows = new List<RestoreContentsRow>();

            if (modules == null)
                return rows;

            foreach (BackupBase module in modules)
            {
                if (module == null)
                    continue;

                rows.Add(new RestoreContentsRow(
                    module,
                    RestoreScope.HasBackup(module, restoreSourcePath),
                    ManifestStateFor(manifest, module.GetType().Name),
                    module.WarningMessage ?? ""));
            }

            return rows;
        }

        /// <summary>
        /// Matches on the module's CLR type name - what the manifest records - so a reworded Title
        /// never breaks the join. Null when the manifest is absent or the type is not in it.
        /// </summary>
        private static string ManifestStateFor(ManifestData manifest, string typeName)
        {
            if (manifest == null)
                return null;

            IReadOnlyList<ManifestModule> entries = manifest.Modules;
            if (entries == null)
                return null;

            foreach (ManifestModule entry in entries)
            {
                if (entry != null && string.Equals(entry.Type, typeName, StringComparison.Ordinal))
                    return entry.State;
            }

            return null;
        }

        /// <summary>
        /// One sentence when the backup's machine or user differs from the current one, else null.
        /// </summary>
        internal static string DescribeProvenance(ManifestData manifest, string machineName, string userName)
        {
            if (manifest == null)
                return null;

            bool machineDiffers = !string.Equals(manifest.MachineName, machineName, StringComparison.OrdinalIgnoreCase);
            bool userDiffers = !string.Equals(manifest.UserName, userName, StringComparison.OrdinalIgnoreCase);

            if (!machineDiffers && !userDiffers)
                return null;

            List<string> parts = new List<string>(2);
            if (machineDiffers)
                parts.Add("machine " + NameOrUnknown(manifest.MachineName));
            if (userDiffers)
                parts.Add("user " + NameOrUnknown(manifest.UserName));

            return "This backup was made on a different " + string.Join(" and ", parts) +
                   "; some paths may not resolve on this PC.";
        }

        private static string NameOrUnknown(string value)
            => string.IsNullOrEmpty(value) ? "(unknown)" : "\"" + value + "\"";
    }
}
