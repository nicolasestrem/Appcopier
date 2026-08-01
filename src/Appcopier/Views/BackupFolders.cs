using Appcopier;
using DataHelper;
using System;
using System.Collections.Generic;
using System.IO;

namespace Views
{
    /// <summary>
    /// The backup directory, split into the user's own backups and the pre-restore snapshots.
    /// </summary>
    /// <remarks>
    /// RestPageView lists these folder names verbatim in one flat list, which is fine for picking a
    /// folder to restore from but wrong for a dashboard: "last backup: 4 minutes ago" naming a
    /// snapshot the app took by itself, immediately before overwriting something, answers a question
    /// nobody asked. SnapshotNaming is the sole authority on which is which.
    /// </remarks>
    internal sealed class BackupFolders
    {
        private BackupFolders(IReadOnlyList<BackupFolder> backups, IReadOnlyList<BackupFolder> snapshots)
        {
            Backups = backups;
            Snapshots = snapshots;
        }

        /// <summary>User backups, newest first.</summary>
        internal IReadOnlyList<BackupFolder> Backups { get; }

        /// <summary>Pre-restore snapshots, newest first.</summary>
        internal IReadOnlyList<BackupFolder> Snapshots { get; }

        internal static BackupFolders Read()
        {
            List<BackupFolder> backups = new List<BackupFolder>();
            List<BackupFolder> snapshots = new List<BackupFolder>();

            if (Directory.Exists(Data.DataRootDir))
            {
                foreach (string path in Directory.GetDirectories(Data.DataRootDir))
                {
                    BackupFolder folder = new BackupFolder(path);

                    if (IsSnapshot(folder.Name))
                        snapshots.Add(folder);
                    else
                        backups.Add(folder);
                }
            }

            backups.Sort(NewestFirst);
            snapshots.Sort(NewestFirst);

            return new BackupFolders(backups, snapshots);
        }

        /// <summary>
        /// Whether a folder name is a pre-restore snapshot.
        /// </summary>
        /// <remarks>
        /// Contains, not EndsWith. SnapshotNaming.Unique appends " (2)", " (3)" AFTER the suffix when
        /// two restores land in the same second, so the marker is not guaranteed to be terminal - and
        /// those collision folders are exactly the ones taken when a restore went wrong, which is
        /// when misfiling one as a user backup would matter most.
        /// </remarks>
        internal static bool IsSnapshot(string folderName)
            => folderName != null
                && folderName.IndexOf(SnapshotNaming.Suffix, StringComparison.OrdinalIgnoreCase) >= 0;

        private static int NewestFirst(BackupFolder a, BackupFolder b)
            => b.Created.CompareTo(a.Created);
    }

    /// <summary>One folder under the backup root.</summary>
    internal sealed class BackupFolder
    {
        internal BackupFolder(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
            Created = ReadCreated(path);
        }

        internal string Path { get; }

        internal string Name { get; }

        internal DateTime Created { get; }

        /// <summary>
        /// The folder's manifest, or null when there is not a trustworthy one.
        /// </summary>
        /// <remarks>
        /// Absent file, unreadable file and a document TryParse refuses all collapse to the same
        /// null, because they are the same answer to the caller: this app does not know what is in
        /// here. Callers render that as "details unavailable".
        /// </remarks>
        internal ManifestData ReadManifest()
        {
            try
            {
                string file = System.IO.Path.Combine(Path, BackupManifest.FileName);

                if (!File.Exists(file))
                    return null;

                return BackupManifest.TryParse(File.ReadAllText(file));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static DateTime ReadCreated(string path)
        {
            try
            {
                return Directory.GetCreationTime(path);
            }
            catch (Exception)
            {
                // MinValue sorts the folder last rather than pretending it is the newest thing on
                // disk, which is what DateTime.Now here would have done.
                return DateTime.MinValue;
            }
        }
    }
}
