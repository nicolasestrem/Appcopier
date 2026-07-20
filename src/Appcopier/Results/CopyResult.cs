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
        public bool SourceMissing { get; set; }
        public int FilesCopied { get; set; }
        public int FilesFailed { get; set; }
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
        public StepResult ToStep(string target, bool absenceIsNormal)
        {
            if (SourceMissing)
            {
                return absenceIsNormal
                    ? StepResult.Skipped(target, "not present on this system")
                    : StepResult.Failed(target, "expected folder for " + target + " is missing");
            }

            if (FilesFailed > 0)
            {
                string reason = string.Format(
                    "{0} of {1} files could not be copied: {2}",
                    FilesFailed, FilesFailed + FilesCopied, FirstError);

                return StepResult.Failed(target, reason);
            }

            if (FilesCopied == 0)
                return StepResult.Skipped(target, "there was nothing to copy");

            return StepResult.Succeeded(target,
                string.Format("copied {0} file(s)", FilesCopied));
        }
    }
}
