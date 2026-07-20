using Appcopier;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace Appcopier.Tests
{
    public class RegistryStepTests : IDisposable
    {
        private readonly string _dir;

        public RegistryStepTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "acstep_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private const string PresentKey = @"HKEY_CURRENT_USER\Control Panel\Mouse";
        private const string AbsentKey = @"HKEY_CURRENT_USER\Software\Appcopier\NoSuchKeyAtAll";

        // Backing up twice in one app session writes into the same folder, so the second run can
        // find the first run's export still sitting at the target path. If the key has since been
        // removed, the early return on Absent used to leave that file behind while the log said the
        // item was skipped - and a later restore would import registry state the user was told had
        // not been captured.
        [Fact]
        public void Export_KeyNowAbsent_RemovesTheEarlierRunsFile()
        {
            string path = Valid("stale.reg");
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));

            StepResult s = Utils.ExportRegistryKey(path, AbsentKey, true, tool);

            Assert.Equal(ResultState.Skipped, s.State);
            Assert.False(File.Exists(path));

            // The clear must not be a side effect of running the export - regedit was never invoked.
            Assert.False(tool.ExportCalled);
        }

        // Same stale file, but absence is NOT normal for this key. The file must still go: the
        // reason it is being removed has nothing to do with how the step is classified.
        [Fact]
        public void Export_KeyMissingAndNotNormal_StillRemovesTheEarlierRunsFile()
        {
            string path = Valid("stale2.reg");

            StepResult s = Utils.ExportRegistryKey(path, AbsentKey, false, new FakeTool(ProcessOutcome.Ran(0)));

            Assert.Equal(ResultState.Failed, s.State);
            Assert.False(File.Exists(path));
        }

        private string Valid(string name)
        {
            string p = Path.Combine(_dir, name);
            File.WriteAllText(p, RegFile.Header + "\r\n\r\n[HKEY_CURRENT_USER\\X]\r\n",
                new UnicodeEncoding(false, true));
            return p;
        }

        // A tool that reports whatever the test wants and records what it was asked to do.
        private sealed class FakeTool : IRegistryTool
        {
            private readonly ProcessOutcome _outcome;
            private readonly Action<string> _onExport;

            public bool ImportCalled;
            public bool ExportCalled;

            public FakeTool(ProcessOutcome outcome, Action<string> onExport = null)
            {
                _outcome = outcome;
                _onExport = onExport;
            }

            public ProcessOutcome Export(string filePath, string registryPath)
            {
                ExportCalled = true;
                if (_onExport != null) _onExport(filePath);
                return _outcome;
            }

            public ProcessOutcome Import(string filePath)
            {
                ImportCalled = true;
                return _outcome;
            }
        }

        // --- Export ---

        [Fact]
        public void Export_AbsentKey_AbsenceNormal_IsSkippedAndNeverLaunchesRegedit()
        {
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "x.reg"), AbsentKey, true, tool);

            Assert.Equal(ResultState.Skipped, s.State);
            Assert.False(tool.ExportCalled);
        }

        [Fact]
        public void Export_AbsentKey_AbsenceNotNormal_IsFailed()
        {
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "x.reg"), AbsentKey, false,
                new FakeTool(ProcessOutcome.Ran(0)));

            Assert.Equal(ResultState.Failed, s.State);
        }

        // The measured case: regedit exits 0 and writes nothing at all.
        [Fact]
        public void Export_ExitZeroButNoFile_IsFailed()
        {
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "never.reg"), PresentKey, false,
                new FakeTool(ProcessOutcome.Ran(0)));

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("no file", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Export_ExitZeroAndValidFile_IsSucceeded()
        {
            string path = Path.Combine(_dir, "good.reg");
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0), p =>
                File.WriteAllText(p, RegFile.Header + "\r\n\r\n[HKEY_CURRENT_USER\\X]\r\n",
                    new UnicodeEncoding(false, true)));

            StepResult s = Utils.ExportRegistryKey(path, PresentKey, false, tool);

            Assert.Equal(ResultState.Succeeded, s.State);
        }

        [Fact]
        public void Export_NonZeroExit_IsFailed()
        {
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "x.reg"), PresentKey, false,
                new FakeTool(ProcessOutcome.Ran(1)));

            Assert.Equal(ResultState.Failed, s.State);
        }

        [Fact]
        public void Export_Timeout_IsFailedAndSaysSo()
        {
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "x.reg"), PresentKey, false,
                new FakeTool(ProcessOutcome.Timeout()));

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("did not exit", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        // --- Import ---

        [Fact]
        public void Import_MissingFile_IsSkippedAndNeverLaunchesRegedit()
        {
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));
            StepResult s = Utils.ImportRegistryKey(Path.Combine(_dir, "gone.reg"), "HKEY_CURRENT_USER\\X", tool);

            Assert.Equal(ResultState.Skipped, s.State);
            Assert.False(tool.ImportCalled);
        }

        // The registry must not be touched by a file we already know is malformed.
        [Fact]
        public void Import_MalformedFile_IsFailedBeforeTouchingTheRegistry()
        {
            string bad = Path.Combine(_dir, "bad.reg");
            File.WriteAllText(bad, "REGEDIT4\r\n", new UnicodeEncoding(false, true));

            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));
            StepResult s = Utils.ImportRegistryKey(bad, "HKEY_CURRENT_USER\\X", tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.False(tool.ImportCalled);
        }

        [Fact]
        public void Import_EmptyFile_IsFailedBeforeTouchingTheRegistry()
        {
            string empty = Path.Combine(_dir, "empty.reg");
            File.WriteAllBytes(empty, new byte[0]);

            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));
            StepResult s = Utils.ImportRegistryKey(empty, "HKEY_CURRENT_USER\\X", tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.False(tool.ImportCalled);
        }

        [Fact]
        public void Import_ValidFileAndZeroExit_IsSucceeded()
        {
            StepResult s = Utils.ImportRegistryKey(Valid("in.reg"), "HKEY_CURRENT_USER\\X",
                new FakeTool(ProcessOutcome.Ran(0)));

            Assert.Equal(ResultState.Succeeded, s.State);
        }

        // The wording rule: regedit /s returns 0 on partially-applied files, so we can only claim
        // to have applied it.
        [Fact]
        public void Import_Success_SaysAppliedAndNeverVerified()
        {
            StepResult s = Utils.ImportRegistryKey(Valid("in2.reg"), "HKEY_CURRENT_USER\\X",
                new FakeTool(ProcessOutcome.Ran(0)));

            Assert.Contains("applied", s.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("verified", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Import_NonZeroExit_IsFailed()
        {
            StepResult s = Utils.ImportRegistryKey(Valid("in3.reg"), "HKEY_CURRENT_USER\\X",
                new FakeTool(ProcessOutcome.Ran(1)));

            Assert.Equal(ResultState.Failed, s.State);
        }

        // --- Branches that had no coverage, and cannot be reached until Task 8 without these ---

        [Fact]
        public void Export_NeverStarted_IsFailed()
        {
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "x.reg"), PresentKey, false,
                new FakeTool(ProcessOutcome.NeverStarted("boom")));

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("could not start", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        // If regedit STARTED, it may already have written to the registry. Reporting that as
        // "could not start" would be a false claim about whether the machine was modified.
        [Fact]
        public void Import_StartedButOutcomeUnknown_DoesNotClaimRegeditNeverRan()
        {
            StepResult s = Utils.ImportRegistryKey(Valid("unknown.reg"), "HKEY_CURRENT_USER\\X",
                new FakeTool(ProcessOutcome.OutcomeUnknown("handle closed")));

            Assert.Equal(ResultState.Failed, s.State);
            Assert.DoesNotContain("could not start", s.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("may have been partly changed", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Export_StartedButOutcomeUnknown_DoesNotClaimRegeditNeverRan()
        {
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "u.reg"), PresentKey, false,
                new FakeTool(ProcessOutcome.OutcomeUnknown("handle closed")));

            Assert.Equal(ResultState.Failed, s.State);
            Assert.DoesNotContain("could not start", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Export_ExitZeroButEmptyFile_IsFailed()
        {
            string path = Path.Combine(_dir, "empty-out.reg");
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0), p => File.WriteAllBytes(p, new byte[0]));

            StepResult s = Utils.ExportRegistryKey(path, PresentKey, false, tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("empty", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Export_ExitZeroButWrongHeader_IsFailed()
        {
            string path = Path.Combine(_dir, "bad-out.reg");
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0),
                p => File.WriteAllText(p, "REGEDIT4\r\n", new UnicodeEncoding(false, true)));

            StepResult s = Utils.ExportRegistryKey(path, PresentKey, false, tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("not a valid", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        // Provenance: a valid .reg already sitting at the target path must NOT be able to satisfy
        // verification for an export that wrote nothing. Without the pre-delete, regedit's measured
        // exit-0-writes-nothing behaviour would be reported as success off a stale artifact.
        [Fact]
        public void Export_StaleFileAtTarget_DoesNotCountAsThisRunsOutput()
        {
            string path = Valid("stale.reg");          // a valid .reg already present
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));   // writes nothing

            StepResult s = Utils.ExportRegistryKey(path, PresentKey, false, tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("no file", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        // --- A failed export must not leave a landmine behind ---
        //
        // RegFile.Validate is header-only by design, so a truncated export with an intact header
        // sails through ImportRegistryKey's pre-flight, reaches regedit /s, exits 0 and is reported
        // as applied. The user would be told the backup failed and then told the restore of that
        // same known-bad file worked. These three pin the delete on each abandoning branch.

        // A part-written export: correct header, then cut off mid-key. This is what regedit leaves
        // when it is killed or times out part-way through writing.
        private static void WriteTruncated(string path)
            => File.WriteAllText(path, RegFile.Header + "\r\n\r\n[HKEY_CURRENT_U",
                   new UnicodeEncoding(false, true));

        [Fact]
        public void Export_NonZeroExit_RemovesThePartWrittenFile()
        {
            string path = Path.Combine(_dir, "partial-exit.reg");
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(1), WriteTruncated);

            StepResult s = Utils.ExportRegistryKey(path, PresentKey, false, tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void Export_Timeout_RemovesThePartWrittenFile()
        {
            string path = Path.Combine(_dir, "partial-timeout.reg");
            FakeTool tool = new FakeTool(ProcessOutcome.Timeout(), WriteTruncated);

            StepResult s = Utils.ExportRegistryKey(path, PresentKey, false, tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void Export_OutcomeUnknown_RemovesThePartWrittenFile()
        {
            string path = Path.Combine(_dir, "partial-unknown.reg");
            FakeTool tool = new FakeTool(ProcessOutcome.OutcomeUnknown("handle closed"), WriteTruncated);

            StepResult s = Utils.ExportRegistryKey(path, PresentKey, false, tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.False(File.Exists(path));
        }

        // The end-to-end shape of the bug: without the delete, the truncated file left by a failed
        // export passes the import pre-flight and reaches the registry.
        [Fact]
        public void Export_FailedThenImport_HasNothingToImport()
        {
            string path = Path.Combine(_dir, "landmine.reg");

            Utils.ExportRegistryKey(path, PresentKey, false,
                new FakeTool(ProcessOutcome.Ran(1), WriteTruncated));

            FakeTool importer = new FakeTool(ProcessOutcome.Ran(0));
            StepResult s = Utils.ImportRegistryKey(path, "HKEY_CURRENT_USER\\X", importer);

            Assert.Equal(ResultState.Skipped, s.State);
            Assert.False(importer.ImportCalled);
        }

        [Fact]
        public void Import_UnreadableFile_IsFailedWithoutCallingItInvalid()
        {
            string path = Valid("locked-in.reg");

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));
                StepResult s = Utils.ImportRegistryKey(path, "HKEY_CURRENT_USER\\X", tool);

                Assert.Equal(ResultState.Failed, s.State);
                Assert.False(tool.ImportCalled);
                Assert.Contains("could not read", s.Reason, StringComparison.OrdinalIgnoreCase);
                // We never read it, so we must not assert anything about its contents.
                Assert.DoesNotContain("not a valid", s.Reason, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
