using Appcopier;
using System.Collections.Generic;
using System.IO;

namespace Conf
{
    /// <summary>
    /// A module that backs up exactly one registry key to <c>{Title}.reg</c>.
    /// </summary>
    /// <remarks>
    /// Ten modules share this shape. The subclass supplies data - a key, whether that key can
    /// legitimately be absent - and inherits the decision logic, so the Skipped-vs-Failed rule is
    /// written once and cannot drift between modules that are supposed to behave identically.
    /// </remarks>
    public abstract class RegistryModule : BackupBase
    {
        /// <summary>The single registry key this module captures.</summary>
        protected abstract string Key { get; }

        /// <summary>
        /// Whether this key can legitimately be missing on a healthy Windows 11 install.
        /// </summary>
        /// <remarks>
        /// Getting this wrong is the cry-wolf failure in either direction: false on a key that is
        /// often absent marks healthy machines red, true on a core key hides a real problem.
        /// </remarks>
        protected abstract bool AbsenceIsNormal { get; }

        public override bool IsInstalled() => Utils.KeyExists(Key);

        // Written once for all ten subclasses, from the same Key the import uses, so a subclass
        // cannot declare one key and overwrite another.
        public override IReadOnlyList<RestoreTarget> RestoreTargets
            => new[] { RestoreTarget.RegistryKey(Key) };

        public override ModuleResult Backup(string path)
            => ModuleResult.Aggregate(new[]
            {
                Utils.ExportRegistryKey(FileFor(path), Key, AbsenceIsNormal)
            });

        public override ModuleResult Restore(string path)
            => ModuleResult.Aggregate(new[]
            {
                Utils.ImportRegistryKey(FileFor(path), Key)
            });

        // Path.Combine rather than concatenation. Produces byte-identical paths today because
        // Data.DataRootDir and RestPageView both hand us a trailing separator, but that is a field
        // contract to honour, not a coincidence to depend on.
        private string FileFor(string path) => Path.Combine(path, Title + ".reg");
    }
}
