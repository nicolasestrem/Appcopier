using Appcopier;
using System.Collections.Generic;
using System.Windows.Forms;
using Xunit;

namespace Appcopier.Tests
{
    public class RunSummaryTests
    {
        private static ModuleResult Ok() => ModuleResult.Aggregate(new[] { StepResult.Succeeded("k", "exported k") });
        private static ModuleResult Skip() => ModuleResult.Aggregate(new[] { StepResult.Skipped("k", "not present on this system") });
        private static ModuleResult Bad() => ModuleResult.Aggregate(new[] { StepResult.Failed("k", "access denied") });

        [Fact]
        public void AnyFailure_IsProblems()
            => Assert.Equal(RunState.Problems,
                   RunSummary.For(new List<ModuleResult> { Ok(), Bad() }, true, RunVerb.Backup).State);

        [Fact]
        public void AllSucceeded_IsDone()
            => Assert.Equal(RunState.Done,
                   RunSummary.For(new List<ModuleResult> { Ok(), Ok() }, true, RunVerb.Backup).State);

        [Fact]
        public void SucceededPlusSkipped_IsDoneNotProblems()
            => Assert.Equal(RunState.Done,
                   RunSummary.For(new List<ModuleResult> { Ok(), Skip() }, true, RunVerb.Backup).State);

        // The whole point: absences must never be counted as failures.
        [Fact]
        public void SucceededPlusSkipped_HeadlineDoesNotClaimAProblem()
        {
            RunSummary s = RunSummary.For(new List<ModuleResult> { Ok(), Skip() }, true, RunVerb.Backup);

            Assert.DoesNotContain("problem", s.Headline, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("fail", s.Headline, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AllSkipped_IsNothingDone()
            => Assert.Equal(RunState.NothingDone,
                   RunSummary.For(new List<ModuleResult> { Skip(), Skip() }, true, RunVerb.Backup).State);

        // The old code said "Back up done." here. It must not.
        [Fact]
        public void AllSkipped_NeverSaysDone()
        {
            RunSummary s = RunSummary.For(new List<ModuleResult> { Skip(), Skip() }, true, RunVerb.Backup);
            Assert.DoesNotContain("done", s.Headline, System.StringComparison.OrdinalIgnoreCase);
        }

        // The silent no-op at ConfPageView.cs:185.
        [Fact]
        public void NotRun_IsDidNotRun()
            => Assert.Equal(RunState.DidNotRun,
                   RunSummary.For(new List<ModuleResult>(), false, RunVerb.Restore).State);

        [Fact]
        public void NotRun_SaysItDidNotRun()
        {
            RunSummary s = RunSummary.For(new List<ModuleResult>(), false, RunVerb.Restore);
            Assert.Contains("did not run", s.Detail, System.StringComparison.OrdinalIgnoreCase);
        }

        // The verb must read correctly in BOTH sentences. A single string cannot do it:
        // the past tense that makes "Backed up 3 items" work yields "Restored did not run."
        [Fact]
        public void NotRun_HeadlineReadsAsASentence()
        {
            Assert.Equal("Restore did not run.",
                RunSummary.For(new List<ModuleResult>(), false, RunVerb.Restore).Headline);
        }

        [Fact]
        public void Done_HeadlineUsesThePastTense()
        {
            RunSummary s = RunSummary.For(new List<ModuleResult> { Ok() }, true, RunVerb.Restore);
            Assert.StartsWith("Restored", s.Headline);
        }

        // Every user-facing sentence runs for BOTH directions. Three separate bugs came from
        // hardcoding a backup verb into one of them, so each is pinned against the restore verb.

        [Fact]
        public void AllSkipped_Restore_DoesNotSayBackedUp()
        {
            RunSummary s = RunSummary.For(new List<ModuleResult> { Skip(), Skip() }, true, RunVerb.Restore);

            Assert.DoesNotContain("backed up", s.Headline, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("back up", s.Detail, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("restored", s.Headline, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SucceededPlusSkipped_Restore_FootnoteDoesNotSayBackUp()
        {
            RunSummary s = RunSummary.For(new List<ModuleResult> { Ok(), Skip() }, true, RunVerb.Restore);

            Assert.DoesNotContain("back up", s.Detail, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("restore", s.Detail, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DidNotRun_IsAWarningNotInformation()
            => Assert.Equal(MessageBoxIcon.Warning,
                   RunSummary.For(new List<ModuleResult>(), false, RunVerb.Restore).Icon);

        [Fact]
        public void Problems_DetailNamesEveryFailedModule()
        {
            RunSummary s = RunSummary.For(new List<ModuleResult> { Bad(), Bad(), Ok() }, true, RunVerb.Backup);
            Assert.Contains("access denied", s.Detail);
            Assert.Contains("2", s.Headline);
        }
    }
}
