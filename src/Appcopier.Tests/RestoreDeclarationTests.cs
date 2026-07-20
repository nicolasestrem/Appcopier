using Appcopier;
using Conf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Appcopier.Tests
{
    // Decision 8: every module states what its restore overwrites, and forgetting shows up.
    //
    // These enumerate the shipped assembly by reflection rather than listing modules by hand. A
    // hand-written list is exactly what a forgetful author also forgets to extend, which would let
    // a new module ship undeclared while the suite stayed green.
    public class RestoreDeclarationTests
    {
        // typeof(BackupBase).Assembly, not the test assembly: the fakes defined in these tests are
        // deliberately undeclared, and enumerating them would make the invariant unassertable.
        private static IEnumerable<Type> ModuleTypes()
            => typeof(BackupBase).Assembly
                .GetTypes()
                .Where(t => typeof(BackupBase).IsAssignableFrom(t) && !t.IsAbstract)
                .OrderBy(t => t.Name);

        private static IEnumerable<BackupBase> Modules()
            => ModuleTypes().Select(t => (BackupBase)Activator.CreateInstance(t));

        [Fact]
        public void EveryShippedModuleIsEnumeratedAndConstructible()
        {
            List<BackupBase> modules = Modules().ToList();

            Assert.Equal(23, modules.Count);
            Assert.All(modules, m => Assert.False(string.IsNullOrWhiteSpace(m.Title)));
        }

        [Fact]
        public void NoShippedModuleReturnsTheUndeclaredMarker()
        {
            foreach (BackupBase m in Modules())
            {
                Assert.NotEmpty(m.RestoreTargets);
                Assert.DoesNotContain(m.RestoreTargets, t => t.IsUndeclared);
            }
        }

        [Fact]
        public void EveryDeclaredTargetCarriesText()
        {
            foreach (BackupBase m in Modules())
                foreach (RestoreTarget t in m.RestoreTargets)
                    Assert.False(string.IsNullOrWhiteSpace(t.Path));
        }

        // The default has to stay loud. A module that declares nothing must still produce a line in
        // the dialog, or consent is asked for an operation whose scope was never stated.
        private sealed class ForgetfulModule : BackupBase
        {
            public ForgetfulModule() { Title = "Forgetful"; }
        }

        [Fact]
        public void UndeclaredModule_ProducesTheVisibleMarker()
        {
            RestoreTarget only = Assert.Single(new ForgetfulModule().RestoreTargets);

            Assert.True(only.IsUndeclared);
            Assert.Equal(RestoreTargetKind.Command, only.Kind);
            Assert.Equal("(this item does not declare what it overwrites)", only.Path);
        }

        [Fact]
        public void UndeclaredModule_DeclaresNoCloseAndMakesChanges()
        {
            ForgetfulModule m = new ForgetfulModule();

            Assert.Empty(m.ProcessesToCloseBeforeRestore);
            Assert.True(m.RestoreMakesChanges);
        }

        [Theory]
        [InlineData(typeof(BGoogleChrome), "chrome")]
        [InlineData(typeof(BMicrosoftEdge), "msedge")]
        [InlineData(typeof(BMozillaFirefox), "firefox")]
        public void Browsers_DeclareAConsentedCloseOfTheProcessTheirBackupCloses(Type type, string processName)
        {
            BackupBase m = (BackupBase)Activator.CreateInstance(type);

            RestoreCloseRequirement req = Assert.Single(m.ProcessesToCloseBeforeRestore);

            Assert.Equal(processName, req.ProcessName);
            Assert.True(req.NeedsConsent);
            Assert.False(string.IsNullOrWhiteSpace(req.DisplayName));
        }

        [Fact]
        public void PinnedApps_DeclaresANonConsentedCloseOfTheStartMenu()
        {
            RestoreCloseRequirement req = Assert.Single(new APinnedApps().ProcessesToCloseBeforeRestore);

            Assert.Equal("StartMenuExperienceHost", req.ProcessName);
            Assert.False(req.NeedsConsent);
        }

        // Every other module closes nothing, so a close requirement appearing anywhere else is a
        // new prompt nobody decided to add.
        [Fact]
        public void OnlyTheBrowsersAndPinnedApps_DeclareCloseRequirements()
        {
            string[] declaring = Modules()
                .Where(m => m.ProcessesToCloseBeforeRestore.Count > 0)
                .Select(m => m.GetType().Name)
                .OrderBy(n => n)
                .ToArray();

            Assert.Equal(
                new[] { "APinnedApps", "BGoogleChrome", "BMicrosoftEdge", "BMozillaFirefox" },
                declaring);
        }

        [Fact]
        public void AppStoreApps_IsTheOnlyModuleThatMakesNoChanges()
        {
            string[] exempt = Modules()
                .Where(m => !m.RestoreMakesChanges)
                .Select(m => m.GetType().Name)
                .ToArray();

            Assert.Equal(new[] { "AppStoreApps" }, exempt);
        }

        // The declared key and the imported key come from one property, so this fails if a subclass
        // ever hand-rolls RestoreTargets and names a different key than the one it writes.
        [Fact]
        public void EveryRegistryModule_DeclaresExactlyItsOwnKey()
        {
            List<BackupBase> registryModules = Modules()
                .Where(m => m is RegistryModule)
                .ToList();

            Assert.Equal(10, registryModules.Count);

            foreach (BackupBase m in registryModules)
            {
                RestoreTarget only = Assert.Single(m.RestoreTargets);

                Assert.Equal(RestoreTargetKind.RegistryKey, only.Kind);
                Assert.Equal(KeyOf(m), only.Path);
            }
        }

        // Key is protected, so the assertion reads it the only way a test can. Walking the
        // hierarchy rather than a single GetProperty call: the property is declared on each
        // subclass as an override, and a future subclass that inherits it instead would otherwise
        // silently produce a null here and pass against an equally null target path.
        private static string KeyOf(BackupBase module)
        {
            for (Type t = module.GetType(); t != null; t = t.BaseType)
            {
                PropertyInfo p = t.GetProperty("Key",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                if (p != null)
                {
                    string key = (string)p.GetValue(module);
                    Assert.False(string.IsNullOrWhiteSpace(key));
                    return key;
                }
            }

            throw new InvalidOperationException("No Key property on " + module.GetType().Name);
        }

        [Theory]
        [InlineData(typeof(WPersonalization))]
        [InlineData(typeof(WTelemetry))]
        [InlineData(typeof(WUpdates))]
        [InlineData(typeof(DPrinters))]
        [InlineData(typeof(GGaming))]
        public void MultiKeyModules_DeclareOneTargetPerKeyInOrder(Type type)
        {
            BackupBase m = (BackupBase)Activator.CreateInstance(type);

            List<string> keys = (List<string>)type.GetField("Keys").GetValue(m);

            Assert.Equal(keys, m.RestoreTargets.Select(t => t.Path).ToArray());
            Assert.All(m.RestoreTargets, t => Assert.Equal(RestoreTargetKind.RegistryKey, t.Kind));
        }

        // Keys is a mutable public field filled by LoadSettings, so the declaration must be read
        // from it rather than captured. Mutating it after construction is how a test can tell those
        // two implementations apart at all.
        [Fact]
        public void MultiKeyModules_ReadTheirKeysAtAccessTimeNotAtConstruction()
        {
            WUpdates m = new WUpdates();
            m.Keys.Add(@"HKEY_CURRENT_USER\Software\Appcopier\AddedAfterConstruction");

            Assert.Equal(m.Keys, m.RestoreTargets.Select(t => t.Path).ToArray());
        }

        [Fact]
        public void Themes_DeclaresBothFoldersThenItsKey()
        {
            WThemes m = new WThemes();

            IReadOnlyList<RestoreTarget> targets = m.RestoreTargets;

            Assert.Equal(m.Folders.Count + m.Keys.Count, targets.Count);
            Assert.Equal(m.Folders, targets.Take(m.Folders.Count).Select(t => t.Path).ToArray());
            Assert.All(targets.Take(m.Folders.Count), t => Assert.Equal(RestoreTargetKind.Folder, t.Kind));
            Assert.Equal(m.Keys, targets.Skip(m.Folders.Count).Select(t => t.Path).ToArray());
            Assert.All(targets.Skip(m.Folders.Count), t => Assert.Equal(RestoreTargetKind.RegistryKey, t.Kind));
        }

        [Theory]
        [InlineData(typeof(APinnedApps))]
        [InlineData(typeof(BGoogleChrome))]
        [InlineData(typeof(BMicrosoftEdge))]
        [InlineData(typeof(BMozillaFirefox))]
        public void FolderModules_DeclareTheFolderTheyOverwrite(Type type)
        {
            BackupBase m = (BackupBase)Activator.CreateInstance(type);

            string folder = (string)type.GetField("Folder").GetValue(m);

            RestoreTarget only = Assert.Single(m.RestoreTargets);

            Assert.Equal(RestoreTargetKind.Folder, only.Kind);
            Assert.Equal(folder, only.Path);
        }

        [Theory]
        [InlineData(typeof(WNetworkConf))]
        [InlineData(typeof(CWiFiConf))]
        [InlineData(typeof(AppStoreApps))]
        public void CommandModules_DescribeWhatRunsInPlainLanguage(Type type)
        {
            BackupBase m = (BackupBase)Activator.CreateInstance(type);

            RestoreTarget only = Assert.Single(m.RestoreTargets);

            Assert.Equal(RestoreTargetKind.Command, only.Kind);
            Assert.False(only.IsUndeclared);

            // A description, not a key path or a bare command line: this text is the only thing the
            // user is given to judge a command whose scope they cannot otherwise see.
            Assert.True(only.Path.Length > 30, only.Path);
        }

        // Wi-Fi's restore is machine-wide, which is the part a user would not otherwise expect and
        // the reason this module carries a WarningMessage at all.
        [Fact]
        public void WiFi_DeclarationStatesTheRestoreIsMachineWide()
            => Assert.Contains("all accounts", new CWiFiConf().RestoreTargets.Single().Path);

        [Fact]
        public void RestoreTarget_RejectsEmptyPaths()
        {
            Assert.Throws<ArgumentException>(() => RestoreTarget.RegistryKey(null));
            Assert.Throws<ArgumentException>(() => RestoreTarget.Folder(""));
            Assert.Throws<ArgumentException>(() => RestoreTarget.Command("   "));
            Assert.Throws<ArgumentException>(() => RestoreTarget.Undeclared(""));
        }

        [Fact]
        public void RestoreCloseRequirement_RejectsEmptyNames()
        {
            Assert.Throws<ArgumentException>(() => new RestoreCloseRequirement("", "Chrome", true));
            Assert.Throws<ArgumentException>(() => new RestoreCloseRequirement("chrome", null, true));
        }

        [Fact]
        public void RestoreCloseRequirement_KeepsWhatItWasGiven()
        {
            RestoreCloseRequirement req = new RestoreCloseRequirement("firefox", "Mozilla Firefox", false);

            Assert.Equal("firefox", req.ProcessName);
            Assert.Equal("Mozilla Firefox", req.DisplayName);
            Assert.False(req.NeedsConsent);
        }

        // A declared target must not read as the marker by accident, or the dialog would show a
        // real declaration as a missing one.
        [Fact]
        public void OnlyTheUndeclaredMarkerIsUndeclared()
        {
            Assert.False(RestoreTarget.Command("runs netsh").IsUndeclared);
            Assert.False(RestoreTarget.Folder(RestoreTarget.UndeclaredMarker).IsUndeclared);
            Assert.True(RestoreTarget.Undeclared("SomeModule").Single().IsUndeclared);
        }

        // Every module that closes something must be able to say whether the backup holds anything
        // for it, because closing is what costs the user work. Answering against a real folder
        // rather than a fake: the whole point is the on-disk layout the module's own restore reads.
        [Theory]
        [InlineData(typeof(Conf.BGoogleChrome))]
        [InlineData(typeof(Conf.BMicrosoftEdge))]
        [InlineData(typeof(Conf.BMozillaFirefox))]
        [InlineData(typeof(Conf.APinnedApps))]
        public void ModulesThatCloseSomething_KnowWhetherTheBackupHoldsAnythingForThem(Type type)
        {
            BackupBase module = (BackupBase)Activator.CreateInstance(type);

            string root = Path.Combine(Path.GetTempPath(), "achas_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                Assert.False(module.HasBackupIn(root));

                Directory.CreateDirectory(Path.Combine(root, module.Title));
                Assert.True(module.HasBackupIn(root));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ModulesThatCloseSomething_TreatAMissingPathAsNothingToRestore(string path)
            => Assert.False(new Conf.BGoogleChrome().HasBackupIn(path));

        // The default answers yes, so a module that has not been taught to check is never silently
        // skipped - being wrong that way costs a close, the other way cancels the user's restore.
        [Fact]
        public void ModulesThatCloseNothing_AssumeTheBackupHasSomethingForThem()
        {
            foreach (BackupBase module in Modules())
            {
                if (module.ProcessesToCloseBeforeRestore.Count == 0)
                    Assert.True(module.HasBackupIn(Path.GetTempPath()), module.Title);
            }
        }
    }
}
