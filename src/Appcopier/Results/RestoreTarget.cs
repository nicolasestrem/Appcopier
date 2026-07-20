using System;
using System.Collections.Generic;

namespace Appcopier
{
    public enum RestoreTargetKind
    {
        RegistryKey,
        Folder,
        Command
    }

    /// <summary>
    /// One thing a module's restore writes to: a registry key, a folder, or an external command.
    /// </summary>
    /// <remarks>
    /// Declarative only. Nothing here performs or checks an operation - it is the text the user is
    /// shown before consenting, so it must describe what the restore will touch rather than what it
    /// found. A target the user cannot recognise is the same defect as a missing one.
    /// </remarks>
    public sealed class RestoreTarget
    {
        /// <summary>
        /// The text shown for a module that declared nothing.
        /// </summary>
        /// <remarks>
        /// Deliberately a visible wart rather than an empty block. A future author who forgets to
        /// declare produces a line in the dialog users read before consenting, which is noticed;
        /// a module that silently contributes no lines reads exactly like one that touches nothing.
        /// </remarks>
        public const string UndeclaredMarker = "(this item does not declare what it overwrites)";

        public RestoreTargetKind Kind { get; }

        /// <summary>Never null, never empty. A key path, a folder path, or a command description.</summary>
        public string Path { get; }

        private RestoreTarget(RestoreTargetKind kind, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A restore target must name what it touches.", nameof(path));

            Kind = kind;
            Path = path;
        }

        public static RestoreTarget RegistryKey(string path)
            => new RestoreTarget(RestoreTargetKind.RegistryKey, path);

        public static RestoreTarget Folder(string path)
            => new RestoreTarget(RestoreTargetKind.Folder, path);

        public static RestoreTarget Command(string description)
            => new RestoreTarget(RestoreTargetKind.Command, description);

        public bool IsUndeclared
            => Kind == RestoreTargetKind.Command && Path == UndeclaredMarker;

        /// <summary>
        /// The declaration BackupBase falls back to when a module supplies none.
        /// </summary>
        /// <remarks>
        /// The module name is required so a caller cannot reach this by accident from a context that
        /// does not know which module it is describing; GetType().Name is never empty, so a throw
        /// here means the call site is wrong, not that a module is unusual.
        /// </remarks>
        public static IReadOnlyList<RestoreTarget> Undeclared(string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
                throw new ArgumentException("An undeclared marker must name its module.", nameof(moduleName));

            return new[] { Command(UndeclaredMarker) };
        }
    }
}
