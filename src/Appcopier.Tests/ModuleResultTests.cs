using Appcopier;
using System;
using System.Collections.Generic;
using Xunit;

namespace Appcopier.Tests
{
    public class ModuleResultTests
    {
        private static StepResult Ok(string t = "key") => StepResult.Succeeded(t, "exported 1 key");
        private static StepResult Skip(string t = "key") => StepResult.Skipped(t, "not present on this system");
        private static StepResult Bad(string t = "key") => StepResult.Failed(t, "access denied");

        // --- Aggregation rule 1: no steps ---

        [Fact]
        public void Aggregate_NoSteps_IsSkipped()
        {
            ModuleResult r = ModuleResult.Aggregate(new StepResult[0]);
            Assert.Equal(ResultState.Skipped, r.State);
            Assert.False(string.IsNullOrWhiteSpace(r.Reason));
        }

        // --- Rule 2: any failure dominates ---

        [Fact]
        public void Aggregate_AnyFailed_IsFailed()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("a"), Bad("b"), Skip("c") });
            Assert.Equal(ResultState.Failed, r.State);
        }

        [Fact]
        public void Aggregate_Failed_ReasonNamesCountAndFirstFailure()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("a"), Bad("b"), Bad("c") });
            Assert.Contains("2 of 3", r.Reason);
            Assert.Contains("access denied", r.Reason);
        }

        // --- Rule 3: all skipped stays skipped (the rule the inventory forced) ---

        [Fact]
        public void Aggregate_AllSkipped_IsSkippedNotSucceeded()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Skip("a"), Skip("b") });
            Assert.Equal(ResultState.Skipped, r.State);
        }

        // --- Rule 4: a mix of success and legitimate absence is success ---

        [Fact]
        public void Aggregate_SucceededPlusSkipped_IsSucceeded()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("Personalize"), Skip("Accent") });
            Assert.Equal(ResultState.Succeeded, r.State);
        }

        [Fact]
        public void Aggregate_SucceededPlusSkipped_ReasonNamesTheSkippedTarget()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("Personalize"), Skip("Accent") });
            Assert.Contains("Accent", r.Reason);
        }

        // Rule 4 must not read as a bare ratio - "1 of 2" under a "Done" heading reads as
        // partial failure, which is the ambiguity that justified dropping a Partial state.
        [Fact]
        public void Aggregate_SucceededPlusSkipped_ReasonIsNotABareRatio()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("Personalize"), Skip("Accent") });
            Assert.DoesNotContain("1 of 2", r.Reason);
        }

        // --- Rule 5: all succeeded ---

        [Fact]
        public void Aggregate_AllSucceeded_IsSucceeded()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("a"), Ok("b") });
            Assert.Equal(ResultState.Succeeded, r.State);
        }

        [Fact]
        public void Aggregate_PreservesSteps()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("a"), Skip("b") });
            Assert.Equal(2, r.Steps.Count);
        }

        [Fact]
        public void Aggregate_NullSteps_IsSkippedNotCrash()
        {
            ModuleResult r = ModuleResult.Aggregate(null);
            Assert.Equal(ResultState.Skipped, r.State);
        }

        // --- Factory invariants ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void StepResult_SkippedWithoutReason_Throws(string reason)
            => Assert.Throws<ArgumentException>(() => StepResult.Skipped("t", reason));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void StepResult_FailedWithoutReason_Throws(string reason)
            => Assert.Throws<ArgumentException>(() => StepResult.Failed("t", reason));

        [Fact]
        public void StepResult_SucceededWithoutReason_Throws()
            => Assert.Throws<ArgumentException>(() => StepResult.Succeeded("t", ""));

        [Fact]
        public void StepResult_NullTarget_Throws()
            => Assert.Throws<ArgumentException>(() => StepResult.Succeeded(null, "fine"));

        // --- The restore-side wording rule ---

        [Fact]
        public void StepResult_Applied_IsSucceeded()
            => Assert.Equal(ResultState.Succeeded, StepResult.Applied("t", "1 key").State);

        [Fact]
        public void StepResult_Applied_ReasonSaysAppliedAndNeverVerified()
        {
            StepResult s = StepResult.Applied("Mouse.reg", "1 key");
            Assert.Contains("applied", s.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("verified", s.Reason, StringComparison.OrdinalIgnoreCase);
        }
    }
}
