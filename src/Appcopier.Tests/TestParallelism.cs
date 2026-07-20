using Xunit;

// The suite runs sequentially, deliberately.
//
// CopyFolderTests.CopyFolder_SubdirectoryVanishesMidCopy_DoesNotReportSourceMissing hits a timing
// window on purpose: it needs the copy to still be busy inside an 8 MB file when the test thread
// resumes and deletes a folder the copy has not visited yet. Run alongside other collections, that
// test blocks indefinitely rather than failing - the thread pool is saturated by the parallel
// collections, so the copy's IO continuations are not scheduled and the await never returns.
//
// Measured: with parallelism on, an unfiltered `dotnet test` sat for over twenty minutes at roughly
// one second of CPU and had to be killed; --blame-hang named that single test. Sequential, the whole
// suite finishes in well under a second, so this costs nothing and makes the documented verification
// command trustworthy. A verification step that intermittently hangs is worse than a slow one: it
// trains whoever runs it to assume the hang is normal.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
