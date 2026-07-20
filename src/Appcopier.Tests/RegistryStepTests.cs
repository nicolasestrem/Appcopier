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
    }
}
