using Appcopier;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Appcopier.Tests
{
    // Utils.CopyFile and the ToFileStep ladder it feeds. Reached directly through
    // InternalsVisibleTo, so the primitive's own promises are held here rather than only through
    // whichever module happens to exercise them.
    public class CopyFileTests
    {
        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "accopyfile_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public async Task MissingSource_ReportsSourceMissingAndCopiesNothing()
        {
            string dir = NewTempDir();

            try
            {
                CopyResult r = await Utils.CopyFile(Path.Combine(dir, "gone"), Path.Combine(dir, "dest"));

                Assert.True(r.SourceMissing);
                Assert.Equal(0, r.FilesCopied);
                Assert.Equal(0, r.FilesFailed);
                Assert.False(File.Exists(Path.Combine(dir, "dest")));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task CopiesContentsAndCountsOneFile()
        {
            string dir = NewTempDir();

            try
            {
                string source = Path.Combine(dir, "config");
                File.WriteAllText(source, "Host example");

                string dest = Path.Combine(dir, "copy", "config");
                CopyResult r = await Utils.CopyFile(source, dest);

                Assert.False(r.SourceMissing);
                Assert.Equal(1, r.FilesCopied);
                Assert.Equal(0, r.FilesFailed);
                Assert.Equal("Host example", File.ReadAllText(dest));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // Load-bearing rather than convenient: the machine being restored onto may never have run
        // ssh, so %USERPROFILE%\.ssh does not exist yet.
        [Fact]
        public async Task CreatesTheDestinationDirectoryTree()
        {
            string dir = NewTempDir();

            try
            {
                string source = Path.Combine(dir, "known_hosts");
                File.WriteAllText(source, "example.com ssh-ed25519 AAAA");

                string dest = Path.Combine(dir, "a", "b", "c", "known_hosts");
                CopyResult r = await Utils.CopyFile(source, dest);

                Assert.Equal(1, r.FilesCopied);
                Assert.True(File.Exists(dest));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // It does not throw. A module that cannot distinguish "copied" from "threw and was caught"
        // is the failure mode Phase 2a exists to remove, so the failure comes back as a count.
        [Fact]
        public async Task LockedDestination_IsAFailureWithAReasonRatherThanAThrow()
        {
            string dir = NewTempDir();

            try
            {
                string source = Path.Combine(dir, "config");
                File.WriteAllText(source, "Host example");

                string dest = Path.Combine(dir, "locked");
                File.WriteAllText(dest, "original");

                CopyResult r;

                using (new FileStream(dest, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    r = await Utils.CopyFile(source, dest);
                }

                Assert.False(r.SourceMissing);
                Assert.Equal(0, r.FilesCopied);
                Assert.Equal(1, r.FilesFailed);
                Assert.False(string.IsNullOrWhiteSpace(r.FirstError));

                // The destination was not truncated on the way to failing.
                Assert.Equal("original", File.ReadAllText(dest));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // A locked SOURCE is a failure, not an absence. Absence maps to Skipped for most modules,
        // so reporting a file we could not read as "not present" is the "I could not tell" ->
        // "nothing was there" slide the reporting rules exist to prevent.
        [Fact]
        public async Task UnreadableSource_IsFailedAndNotReportedAsAbsent()
        {
            string dir = NewTempDir();

            try
            {
                string source = Path.Combine(dir, "config");
                File.WriteAllText(source, "Host example");

                CopyResult r;

                using (new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    r = await Utils.CopyFile(source, Path.Combine(dir, "dest"));
                }

                Assert.False(r.SourceMissing);
                Assert.Equal(1, r.FilesFailed);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // --- ToFileStep: the same ladder as ToStep, differing only in the nouns ---

        [Fact]
        public void ToFileStep_AbsentAndNormal_IsSkipped()
        {
            StepResult s = new CopyResult { SourceMissing = true }.ToFileStep("hosts", true);

            Assert.Equal(ResultState.Skipped, s.State);
            Assert.Equal("not present on this system", s.Reason);
        }

        [Fact]
        public void ToFileStep_AbsentAndNotNormal_IsFailedAndNamesAFileNotAFolder()
        {
            StepResult s = new CopyResult { SourceMissing = true }.ToFileStep("hosts", false);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("expected file", s.Reason);
            Assert.DoesNotContain("folder", s.Reason);
        }

        // Restore callers supply their own reason because their source is the backup folder, and
        // the default wording would describe the live machine, which was never examined.
        [Fact]
        public void ToFileStep_AbsentOnTheRestoreSide_UsesTheCallersReason()
        {
            StepResult s = new CopyResult { SourceMissing = true }
                .ToFileStep("hosts", true, "nothing was backed up for this item");

            Assert.Equal(ResultState.Skipped, s.State);
            Assert.Equal("nothing was backed up for this item", s.Reason);
        }

        [Fact]
        public void ToFileStep_Failure_CarriesTheUnderlyingError()
        {
            StepResult s = new CopyResult { FilesFailed = 1, FirstError = "access denied" }
                .ToFileStep("hosts", true);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("access denied", s.Reason);
        }

        [Fact]
        public void ToFileStep_Success_ClaimsOnlyThatItCopied()
        {
            StepResult s = new CopyResult { FilesCopied = 1, BytesCopied = 12 }.ToFileStep("hosts", false);

            Assert.Equal(ResultState.Succeeded, s.State);
            Assert.Equal("copied 1 file", s.Reason);
        }

        // Unreachable through CopyFile, which always sets exactly one of the three. Held as a
        // failure so a future caller folding a hand-built tally through here cannot get silence
        // out of a copy that never happened.
        [Fact]
        public void ToFileStep_EmptyTally_IsFailedRatherThanSilent()
        {
            StepResult s = new CopyResult().ToFileStep("hosts", true);

            Assert.Equal(ResultState.Failed, s.State);
        }
    }
}
