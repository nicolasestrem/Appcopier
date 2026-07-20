using Appcopier;
using Conf;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Appcopier.Tests
{
    // These exercise the module shapes against the real registry, unelevated, using keys whose
    // presence or absence is knowable. They cover the SHAPE of a module's decision, not regedit.
    public class ModuleShapeTests
    {
        [Fact]
        public void EveryRegisteredModule_HasATitle()
        {
            foreach (BackupBase m in new BackupBase[]
            {
                new WAccessibility(), new DMouse(), new DKeyboard(), new WTaskbar(),
                new WAPrivacy(), new WOther(), new WPrivacy(), new WVisualEffects(),
                new DUSB(), new DTouchpad(), new WPersonalization(), new WTelemetry(),
                new WUpdates(), new DPrinters(), new GGaming(), new APinnedApps(),
                new BMozillaFirefox(), new BMicrosoftEdge(), new BGoogleChrome(),
                new WThemes(), new WNetworkConf(), new CWiFiConf(), new AppStoreApps()
            })
            {
                Assert.False(string.IsNullOrWhiteSpace(m.Title));
            }
        }

        // The base default must be a failure, not a reassuring skip.
        private sealed class ForgetfulModule : BackupBase
        {
            public ForgetfulModule() { Title = "Forgetful"; }
        }

        [Fact]
        public void BackupBase_UnimplementedBackup_IsFailed()
            => Assert.Equal(ResultState.Failed, new ForgetfulModule().Backup("C:\\nowhere").State);

        [Fact]
        public void BackupBase_UnimplementedRestore_IsFailed()
            => Assert.Equal(ResultState.Failed, new ForgetfulModule().Restore("C:\\nowhere").State);

        [Fact]
        public async Task BackupBase_AsyncWrapper_CarriesTheResultThrough()
        {
            ModuleResult r = await new ForgetfulModule().BackupAsync("C:\\nowhere");
            Assert.Equal(ResultState.Failed, r.State);
        }

        // Restoring from a folder containing no .reg file must be Skipped, not a false success.
        //
        // A freshly created, uniquely named folder rather than %TEMP% itself: this used to pass only
        // while the machine's temp directory happened not to contain a Mouse.reg, so a stray file
        // left there by anything at all would silently invert the assertion.
        [Fact]
        public void S1Module_RestoreWithNoBackedUpFile_IsSkipped()
        {
            string dir = Path.Combine(Path.GetTempPath(), "acshape_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                ModuleResult r = new DMouse().Restore(dir);
                Assert.Equal(ResultState.Skipped, r.State);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
