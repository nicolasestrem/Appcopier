using Appcopier;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Appcopier.Tests
{
    public class CopyFolderTests : IDisposable
    {
        private readonly string _root;

        public CopyFolderTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "accopy_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string Dir(string name)
        {
            string p = Path.Combine(_root, name);
            Directory.CreateDirectory(p);
            return p;
        }

        [Fact]
        public async Task CopyFolder_MissingSource_ReportsSourceMissing()
        {
            CopyResult r = await Utils.CopyFolder(Path.Combine(_root, "nope"), Dir("dst1"));

            Assert.True(r.SourceMissing);
            Assert.Equal(0, r.FilesCopied);
        }

        [Fact]
        public async Task CopyFolder_MissingSource_MapsToSkippedWhenAbsenceIsNormal()
        {
            CopyResult r = await Utils.CopyFolder(Path.Combine(_root, "nope"), Dir("dst2"));

            Assert.Equal(ResultState.Skipped, r.ToStep("Chrome", true).State);
        }

        [Fact]
        public async Task CopyFolder_MissingSource_MapsToFailedWhenAbsenceIsNotNormal()
        {
            CopyResult r = await Utils.CopyFolder(Path.Combine(_root, "nope"), Dir("dst3"));

            Assert.Equal(ResultState.Failed, r.ToStep("Themes", false).State);
        }

        [Fact]
        public async Task CopyFolder_EmptySource_CopiesNothingAndIsSkipped()
        {
            CopyResult r = await Utils.CopyFolder(Dir("emptysrc"), Dir("dst4"));

            Assert.Equal(0, r.FilesCopied);
            Assert.Equal(0, r.FilesFailed);
            Assert.Equal(ResultState.Skipped, r.ToStep("Empty", true).State);
        }

        [Fact]
        public async Task CopyFolder_NestedTree_CopiesEveryFile()
        {
            string src = Dir("src5");
            Directory.CreateDirectory(Path.Combine(src, "a", "b"));
            File.WriteAllText(Path.Combine(src, "top.txt"), "1");
            File.WriteAllText(Path.Combine(src, "a", "mid.txt"), "22");
            File.WriteAllText(Path.Combine(src, "a", "b", "deep.txt"), "333");

            string dst = Path.Combine(_root, "dst5");
            CopyResult r = await Utils.CopyFolder(src, dst);

            Assert.Equal(3, r.FilesCopied);
            Assert.Equal(0, r.FilesFailed);
            Assert.Equal(6, r.BytesCopied);
            Assert.True(File.Exists(Path.Combine(dst, "a", "b", "deep.txt")));
            Assert.Equal(ResultState.Succeeded, r.ToStep("Tree", false).State);
        }

        // A locked file is the browser-profile case, made deterministic.
        [Fact]
        public async Task CopyFolder_LockedFile_CountsTheFailureAndKeepsGoing()
        {
            string src = Dir("src6");
            File.WriteAllText(Path.Combine(src, "fine.txt"), "ok");
            string locked = Path.Combine(src, "locked.txt");
            File.WriteAllText(locked, "held");

            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                CopyResult r = await Utils.CopyFolder(src, Path.Combine(_root, "dst6"));

                Assert.Equal(1, r.FilesCopied);
                Assert.Equal(1, r.FilesFailed);
                Assert.False(string.IsNullOrWhiteSpace(r.FirstError));
            }
        }

        // Decision 2 of the spec: any file failure is a failed module. No threshold.
        [Fact]
        public async Task CopyFolder_OneLockedFileAmongMany_IsFailedNotPartial()
        {
            string src = Dir("src7");
            for (int i = 0; i < 5; i++)
                File.WriteAllText(Path.Combine(src, "f" + i + ".txt"), "x");

            string locked = Path.Combine(src, "locked.txt");
            File.WriteAllText(locked, "held");

            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                CopyResult r = await Utils.CopyFolder(src, Path.Combine(_root, "dst7"));
                StepResult s = r.ToStep("Chrome", true);

                Assert.Equal(ResultState.Failed, s.State);
                Assert.Contains("1", s.Reason);
            }
        }
    }
}
