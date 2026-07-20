using Appcopier;
using Conf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Appcopier.Tests
{
    /// <summary>
    /// A module that captures N registry keys must write N distinct files.
    /// </summary>
    /// <remarks>
    /// Written for a defect that had not fired yet. <c>WThemes</c> built its .reg filename from the
    /// module Title inside a <c>foreach</c> over <c>Keys</c>, so every key resolved to the same
    /// <c>Themes.reg</c>. That was harmless only while <c>Keys.Count == 1</c>, and the obvious fix
    /// for the module's other defect - capturing the wallpaper pointer - was to add a second key.
    ///
    /// The failure it would have caused is the one this project exists to remove, not a crash: on
    /// backup, the second export deletes the first via TryDeleteExport and writes over it while both
    /// steps report Succeeded; on restore, that one file is imported once per key and the post-import
    /// probe finds every key present on any live machine, because the keys exist there anyway. Two
    /// green rows, one key never captured.
    ///
    /// Nothing here runs regedit. The restore-side assertions use zero-byte files, which RegFile
    /// classifies as Empty and ImportRegistryKey rejects before reaching the tool
    /// (WindowsHelper.cs:267-268), so these are unelevated and touch no hive.
    /// </remarks>
    public class BackupFileNamingTests
    {
        private const string SyntheticKey = @"HKEY_CURRENT_USER\Software\Appcopier\SyntheticSecondKey";

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "appcopier-naming-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        // The modules that hold a public List<string> Keys. Read by reflection rather than listed,
        // for the reason RestoreDeclarationTests gives: a hand-written list is what a forgetful
        // author also forgets to extend.
        private static IEnumerable<Type> MultiKeyModuleTypes()
            => typeof(BackupBase).Assembly
                .GetTypes()
                .Where(t => typeof(BackupBase).IsAssignableFrom(t) && !t.IsAbstract)
                .Where(t => t.GetField("Keys") != null)
                .OrderBy(t => t.Name);

        public static IEnumerable<object[]> MultiKeyModules()
            => MultiKeyModuleTypes().Select(t => new object[] { t });

        private static List<string> KeysOf(BackupBase module)
            => (List<string>)module.GetType().GetField("Keys").GetValue(module);

        /// <summary>
        /// The landmine test. Observes the filename RestoreAsync actually computes, not a helper.
        /// </summary>
        /// <remarks>
        /// Deliberately NOT written against the naming seam by reflection. A test that calls
        /// RegFileNameFor directly passes even if a module's call site still concatenates its own
        /// filename, which is precisely where the defect lived - so it would have gone green while
        /// the bug shipped.
        ///
        /// Only the first key's file is placed on disk, and it is empty. One key must therefore
        /// report the file as empty, and the other must report that nothing was backed up for it.
        /// Before the fix both keys resolved to the same name, so BOTH reported "empty" and the
        /// second key's missing file was invisible.
        /// </remarks>
        [Theory]
        [MemberData(nameof(MultiKeyModules))]
        public async Task AModuleGivenAnExtraKey_ReadsADifferentFileForIt(Type type)
        {
            BackupBase module = (BackupBase)Activator.CreateInstance(type);
            List<string> keys = KeysOf(module);

            Assert.NotEmpty(keys);

            string firstKey = keys[0];
            keys.Add(SyntheticKey);

            string dir = NewTempDir();

            try
            {
                File.WriteAllBytes(Path.Combine(dir, FileNameFor(module, firstKey)), new byte[0]);

                ModuleResult result = await module.RestoreAsync(dir);

                StepResult[] keySteps = result.Steps
                    .Where(s => keys.Contains(s.Target))
                    .ToArray();

                Assert.Equal(keys.Count, keySteps.Length);

                Assert.Single(keySteps, s => s.Reason.Contains("is empty"));
                Assert.Equal(
                    keys.Count - 1,
                    keySteps.Count(s => s.Reason == "nothing was backed up for this item"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // Vacuous for a module that ships one key - which is every reason the mutation test above
        // exists. Kept because it is the invariant a reader looks for, and it catches a module that
        // ships two colliding keys outright.
        [Theory]
        [MemberData(nameof(MultiKeyModules))]
        public void EveryKeyOfAMultiKeyModule_GetsItsOwnFileName(Type type)
        {
            BackupBase module = (BackupBase)Activator.CreateInstance(type);
            List<string> keys = KeysOf(module);

            string[] names = keys.Select(k => FileNameFor(module, k)).ToArray();

            Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
            Assert.All(names, n => Assert.EndsWith(".reg", n, StringComparison.Ordinal));
            Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        /// <summary>
        /// Two modules must not write the same file either - they share one backup folder.
        /// </summary>
        /// <remarks>
        /// Sweeps modules exposing a Keys field AND the ten RegistryModule subclasses, which expose
        /// a protected Key property instead. Not "all 23": seven write no .reg file at all, and
        /// asking them for a registry filename would assert something they never do.
        /// </remarks>
        [Fact]
        public void NoTwoModulesWriteTheSameRegFileName()
        {
            List<string> names = new List<string>();

            foreach (BackupBase module in RegistryWritingModules())
                foreach (string key in KeysDeclaredBy(module))
                    names.Add(FileNameFor(module, key));

            // Guards against the sweep silently going vacuous if the module shapes change.
            Assert.True(names.Count >= 15, "only found " + names.Count + " registry filenames");
            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        /// <summary>
        /// The compatibility promise, pinned as literals rather than as a comment.
        /// </summary>
        /// <remarks>
        /// Themes keeps the filename its existing backups already carry for the key it always had.
        /// A change to either the Title or the key-to-filename transform would orphan every backup
        /// on disk, so it should have to break a test that spells the consequence out.
        /// </remarks>
        [Fact]
        public void TheThemesFileNameIsKeptForTheKeyThatAlreadyUsesIt()
        {
            WThemes m = new WThemes();

            Assert.Equal(
                "Themes.reg",
                FileNameFor(m, @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes"));

            // Any other key is named after itself, which is what keeps it distinguishable.
            Assert.Equal(
                @"Themes_HKEY_CURRENT_USER_Control Panel_Desktop.reg",
                FileNameFor(m, @"HKEY_CURRENT_USER\Control Panel\Desktop"));
        }

        // The reader must start from what the writer produces. A module offering older names does
        // so by appending candidates, never by replacing element 0.
        [Fact]
        public void RegFileNamesToTryOnRestore_StartsWithTheNameTheWriterProduces()
        {
            foreach (BackupBase module in RegistryWritingModules())
            {
                foreach (string key in KeysDeclaredBy(module))
                {
                    IReadOnlyList<string> candidates = NamesToTryFor(module, key);

                    Assert.NotEmpty(candidates);
                    Assert.Equal(FileNameFor(module, key), candidates[0]);
                }
            }
        }

        private static IEnumerable<BackupBase> RegistryWritingModules()
            => typeof(BackupBase).Assembly
                .GetTypes()
                .Where(t => typeof(BackupBase).IsAssignableFrom(t) && !t.IsAbstract)
                .Where(t => t.GetField("Keys") != null || HasKeyProperty(t))
                .OrderBy(t => t.Name)
                .Select(t => (BackupBase)Activator.CreateInstance(t));

        private static bool HasKeyProperty(Type type)
        {
            for (Type t = type; t != null; t = t.BaseType)
                if (t.GetProperty("Key", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null)
                    return true;

            return false;
        }

        // Keys field for the multi-key modules, the protected Key property for the RegistryModule
        // subclasses. The hierarchy walk mirrors RestoreDeclarationTests.KeyOf and exists for the
        // same reason: the property is declared as an override on each subclass.
        private static IEnumerable<string> KeysDeclaredBy(BackupBase module)
        {
            FieldInfo keys = module.GetType().GetField("Keys");

            if (keys != null)
                return (List<string>)keys.GetValue(module);

            for (Type t = module.GetType(); t != null; t = t.BaseType)
            {
                PropertyInfo p = t.GetProperty("Key",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                if (p != null)
                    return new[] { (string)p.GetValue(module) };
            }

            throw new InvalidOperationException("No keys on " + module.GetType().Name);
        }

        private static IReadOnlyList<string> NamesToTryFor(BackupBase module, string key)
        {
            for (Type t = module.GetType(); t != null; t = t.BaseType)
            {
                MethodInfo m = t.GetMethod("RegFileNamesToTryOnRestore",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                if (m != null)
                    return (IReadOnlyList<string>)m.Invoke(module, new object[] { key });
            }

            throw new InvalidOperationException("No RegFileNamesToTryOnRestore on " + module.GetType().Name);
        }

        /// <summary>
        /// The name this build writes for one key, read through the seam the modules use.
        /// </summary>
        /// <remarks>
        /// Protected, so reflection is the only way a test can reach it. The hierarchy is walked
        /// rather than queried once because a module may override it - WThemes does, to keep the
        /// filename its existing backups already carry.
        /// </remarks>
        private static string FileNameFor(BackupBase module, string key)
        {
            for (Type t = module.GetType(); t != null; t = t.BaseType)
            {
                MethodInfo m = t.GetMethod("RegFileNameFor",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                if (m != null)
                    return (string)m.Invoke(module, new object[] { key });
            }

            throw new InvalidOperationException("No RegFileNameFor on " + module.GetType().Name);
        }
    }
}
