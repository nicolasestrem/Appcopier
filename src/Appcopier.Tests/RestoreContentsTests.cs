using Appcopier;
using System.Collections.Generic;
using Xunit;

namespace Appcopier.Tests
{
    // RestoreContents is the restore wizard's step-2 view-model builder (Phase 4 PR 7). Presence is
    // RestoreScope.HasBackup (so a module the folder holds nothing for greys out before the run, not
    // after); state is joined by CLR type name; provenance is null unless something differs.
    public class RestoreContentsTests
    {
        private sealed class PresentModule : BackupBase
        {
            public PresentModule() { Title = "Present"; }
            public override bool HasBackupIn(string restorePath) => true;
        }

        private sealed class AbsentModule : BackupBase
        {
            public AbsentModule() { Title = "Absent"; Info = "an item"; }
            public override bool HasBackupIn(string restorePath) => false;
        }

        private static ManifestData Manifest(string machine, string user, params ManifestModule[] modules)
            => new ManifestData(1, "0.31.0", "now", machine, user, "build", modules);

        [Fact]
        public void For_ReflectsHasBackupPerModule()
        {
            IReadOnlyList<RestoreContentsRow> rows = RestoreContents.For(
                new BackupBase[] { new PresentModule(), new AbsentModule() }, @"X:\backup\", null);

            Assert.Equal(2, rows.Count);
            Assert.True(rows[0].HasBackup);
            Assert.False(rows[1].HasBackup);
        }

        [Fact]
        public void For_ManifestStateMapsByTypeName()
        {
            PresentModule present = new PresentModule();
            ManifestData manifest = Manifest("M", "U",
                new ManifestModule(present.GetType().Name, "Present", BackupManifest.StateSucceeded, ""));

            IReadOnlyList<RestoreContentsRow> rows = RestoreContents.For(new[] { (BackupBase)present }, @"X:\backup\", manifest);

            Assert.Equal(BackupManifest.StateSucceeded, rows[0].ManifestState);
        }

        [Fact]
        public void For_TypeAbsentFromManifest_HasNullState()
        {
            ManifestData manifest = Manifest("M", "U",
                new ManifestModule("SomethingElse", "t", BackupManifest.StateSucceeded, ""));

            IReadOnlyList<RestoreContentsRow> rows = RestoreContents.For(new[] { new PresentModule() }, @"X:\backup\", manifest);

            Assert.Null(rows[0].ManifestState);
        }

        [Fact]
        public void For_NullManifest_AllStatesNull()
        {
            IReadOnlyList<RestoreContentsRow> rows = RestoreContents.For(new[] { new PresentModule() }, @"X:\backup\", null);

            Assert.Null(rows[0].ManifestState);
        }

        [Fact]
        public void For_NullModules_ReturnsEmpty()
        {
            Assert.Empty(RestoreContents.For(null, @"X:\backup\", null));
        }

        [Fact]
        public void DescribeProvenance_NullManifest_ReturnsNull()
        {
            Assert.Null(RestoreContents.DescribeProvenance(null, "M", "U"));
        }

        [Fact]
        public void DescribeProvenance_SameMachineAndUser_ReturnsNull()
        {
            Assert.Null(RestoreContents.DescribeProvenance(Manifest("M", "U"), "M", "U"));
        }

        [Fact]
        public void DescribeProvenance_IgnoresCase()
        {
            Assert.Null(RestoreContents.DescribeProvenance(Manifest("desk-top", "nicol"), "DESK-TOP", "NICOL"));
        }

        [Fact]
        public void DescribeProvenance_DifferingMachine_NamesIt()
        {
            string sentence = RestoreContents.DescribeProvenance(Manifest("OTHER-PC", "U"), "THIS-PC", "U");

            Assert.NotNull(sentence);
            Assert.Contains("OTHER-PC", sentence);
            Assert.DoesNotContain("user", sentence);
        }

        [Fact]
        public void DescribeProvenance_DifferingUser_NamesIt()
        {
            string sentence = RestoreContents.DescribeProvenance(Manifest("M", "alice"), "M", "bob");

            Assert.NotNull(sentence);
            Assert.Contains("alice", sentence);
            Assert.DoesNotContain("machine", sentence);
        }

        [Fact]
        public void DescribeProvenance_DifferingBoth_NamesBoth()
        {
            string sentence = RestoreContents.DescribeProvenance(Manifest("OTHER", "alice"), "THIS", "bob");

            Assert.Contains("OTHER", sentence);
            Assert.Contains("alice", sentence);
        }
    }
}
