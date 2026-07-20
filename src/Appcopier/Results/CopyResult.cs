namespace Appcopier
{
    /// <summary>
    /// The tally from one folder copy.
    /// </summary>
    /// <remarks>
    /// CopyFolder previously returned a plain Task, so a missing source, a per-file exception and
    /// an outer exception were indistinguishable from a clean copy: all three completed normally.
    /// No module could be honest until this carried counts.
    /// </remarks>
    internal sealed class CopyResult
    {
        /// <summary>
        /// The top-level source folder did not exist. Set ONLY by the root call.
        /// </summary>
        /// <remarks>
        /// A subdirectory vanishing mid-copy must never set this. Browsers create and delete cache
        /// subdirectories continuously, and GetDirectories() snapshots names that are then visited
        /// after real I/O has elapsed - so a subdirectory can be gone by the time recursion reaches
        /// it. Since ToStep tests this flag first, letting a nested call set it would report a copy
        /// that moved hundreds of files as "not present on this system".
        /// </remarks>
        public bool SourceMissing { get; set; }

        public int FilesCopied { get; set; }
        public int FilesFailed { get; set; }

        /// <summary>
        /// A directory could not be created or enumerated, so its whole subtree was never attempted.
        /// </summary>
        /// <remarks>
        /// Counted separately from FilesFailed because they are different facts. Folding a directory
        /// failure into the file counter yields "1 of 1 files could not be copied" when zero files
        /// were ever tried - a sentence that misdescribes the failure, which is what StepResult.Reason
        /// exists to prevent.
        /// </remarks>
        public int FoldersFailed { get; set; }

        /// <summary>
        /// Sum of the source files' sizes as enumerated BEFORE copying, not bytes actually written.
        /// </summary>
        /// <remarks>
        /// For a file a running browser is still writing, the enumerated length can differ from what
        /// lands on disk. Adequate for an order-of-magnitude figure; do not present it as an exact
        /// transferred-byte count.
        /// </remarks>
        public long BytesCopied { get; set; }

        public string FirstError { get; set; }

        /// <summary>
        /// Maps the tally onto a step outcome.
        /// </summary>
        /// <remarks>
        /// Any failed file fails the whole step - deliberately, per the Phase 2a design. There is no
        /// tolerated-subtree allowlist and no ratio threshold: a browser profile missing Login Data
        /// and History is not a usable backup regardless of how few files that is. The browser
        /// modules will therefore read Failed whenever the browser was running, which is the
        /// intended signal, not a regression.
        /// </remarks>
        /// <param name="absentReason">
        /// What a missing source means for THIS direction. Restore callers must supply it: their
        /// source is the backup folder, so the default wording describes the wrong machine
        /// entirely - it would tell the user the item is "not present on this system" when the
        /// fact is that nothing was ever backed up for it. Backup callers leave it null.
        /// </param>
        public StepResult ToStep(string target, bool absenceIsNormal, string absentReason = null)
        {
            if (SourceMissing)
            {
                return absenceIsNormal
                    ? StepResult.Skipped(target, absentReason ?? "not present on this system")
                    : StepResult.Failed(target, "expected folder for " + target + " is missing");
            }

            // Folder-level failures are reported as folders, not as an invented file count.
            if (FoldersFailed > 0 && FilesFailed == 0)
            {
                return StepResult.Failed(target, string.Format(
                    "{0} folder(s) could not be read or created, so their contents were never attempted: {1}",
                    FoldersFailed, FirstError));
            }

            if (FilesFailed > 0)
            {
                string reason = string.Format(
                    "{0} of {1} files could not be copied: {2}",
                    FilesFailed, FilesFailed + FilesCopied, FirstError);

                if (FoldersFailed > 0)
                {
                    reason += string.Format(
                        " (and {0} folder(s) could not be read at all)", FoldersFailed);
                }

                return StepResult.Failed(target, reason);
            }

            if (FilesCopied == 0)
                return StepResult.Skipped(target, "there was nothing to copy");

            return StepResult.Succeeded(target,
                string.Format("copied {0} file(s)", FilesCopied));
        }
    }
}
