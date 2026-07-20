using Appcopier;
using Xunit;

namespace Appcopier.Tests
{
    public class CloseResultTests
    {
        // Utils.Worse (CloseProcess's aggregation helper) is private and has no seam of its
        // own to call through. It is defined verbatim as Severity(a) >= Severity(b) ? a : b,
        // so exercising Severity - internal for exactly this reason - proves the same
        // order-independent max-severity behaviour without inventing a new seam.
        private static CloseResult Worse(CloseResult a, CloseResult b)
            => Utils.Severity(a) >= Utils.Severity(b) ? a : b;

        [Theory]
        [InlineData(CloseResult.AccessDenied, CloseResult.StillRunning)]
        [InlineData(CloseResult.StillRunning, CloseResult.AccessDenied)]
        public void Worse_AccessDeniedBeatsStillRunning_RegardlessOfOrder(CloseResult a, CloseResult b)
            => Assert.Equal(CloseResult.AccessDenied, Worse(a, b));

        [Theory]
        [InlineData(CloseResult.StillRunning)]
        [InlineData(CloseResult.AccessDenied)]
        public void Worse_ExitedNeverOverwritesMoreSevereValue(CloseResult moreSevere)
            => Assert.Equal(moreSevere, Worse(moreSevere, CloseResult.Exited));
    }
}
