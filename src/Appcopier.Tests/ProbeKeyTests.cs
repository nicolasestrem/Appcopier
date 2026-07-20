using Appcopier;
using Xunit;

namespace Appcopier.Tests
{
    // These run unelevated against real HKCU/HKLM keys that exist on every Windows 11 install.
    public class ProbeKeyTests
    {
        [Fact]
        public void ProbeKey_CoreHkcuKey_IsPresent()
            => Assert.Equal(KeyProbe.Present, Utils.ProbeKey(@"HKEY_CURRENT_USER\Control Panel\Mouse"));

        [Fact]
        public void ProbeKey_CoreHklmKey_IsPresent()
            => Assert.Equal(KeyProbe.Present,
                   Utils.ProbeKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion"));

        [Fact]
        public void ProbeKey_NonexistentKey_IsAbsent()
            => Assert.Equal(KeyProbe.Absent,
                   Utils.ProbeKey(@"HKEY_CURRENT_USER\Software\Appcopier\NoSuchKeyAtAll"));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ProbeKey_NoKey_IsAbsent(string key)
            => Assert.Equal(KeyProbe.Absent, Utils.ProbeKey(key));

        // The HKCU-probed-under-HKLM bug: the old prefix strip only removed the MATCHING base name,
        // so an HKCU path was additionally probed under HKLM with its full prefix still attached.
        [Fact]
        public void ProbeKey_HkcuPath_IsNotMatchedUnderHklm()
            => Assert.Equal(KeyProbe.Absent,
                   Utils.ProbeKey(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows NT\CurrentVersion\NoSuchSubkey"));

        [Fact]
        public void KeyExists_ShimAgreesWithProbeOnPresent()
            => Assert.True(Utils.KeyExists(@"HKEY_CURRENT_USER\Control Panel\Mouse"));

        [Fact]
        public void KeyExists_ShimAgreesWithProbeOnAbsent()
            => Assert.False(Utils.KeyExists(@"HKEY_CURRENT_USER\Software\Appcopier\NoSuchKeyAtAll"));

        // The shim must never throw - SelectInstalled calls it for every module at tree-build time.
        [Fact]
        public void KeyExists_MalformedKey_ReturnsFalseInsteadOfThrowing()
            => Assert.False(Utils.KeyExists(@"NOT_A_HIVE\whatever"));
    }
}
