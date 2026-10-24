using System.Collections.Generic;
using Xunit;
using BettingApp.Services;

namespace BettingApp.Tests
{
    public class AutoApprovalTests
    {
        [Fact]
        public void IsLegOddsWithinPinnacleTolerance_ExactDecimalMath_ValidatesCorrectly()
        {
            // Exact 3% boundary: Pinnacle = 2.00, 3% over = 2.06
            Assert.True(AutoApprovalService.IsLegOddsWithinPinnacleTolerance(2.06m, 2.00m, 3.0m, out decimal maxAllowed, out decimal overPct));
            Assert.Equal(2.06m, maxAllowed);
            Assert.Equal(3.0m, overPct);

            // Slightly above 3%: 2.0601 > 2.06
            Assert.False(AutoApprovalService.IsLegOddsWithinPinnacleTolerance(2.0601m, 2.00m, 3.0m, out _, out _));

            // Below Pinnacle is always allowed
            Assert.True(AutoApprovalService.IsLegOddsWithinPinnacleTolerance(1.85m, 2.00m, 3.0m, out _, out _));

            // Fractional percent (e.g., 2.5% over 1.91 = 1.95775)
            Assert.True(AutoApprovalService.IsLegOddsWithinPinnacleTolerance(1.95m, 1.91m, 2.5m, out decimal maxFrac, out _));
            Assert.Equal(1.95775m, maxFrac);
            Assert.False(AutoApprovalService.IsLegOddsWithinPinnacleTolerance(1.96m, 1.91m, 2.5m, out _, out _));
        }

        [Fact]
        public void IsTotalOddsConsistentWithLegs_SingleLeg_ValidatesWithFloorRoundingTolerance()
        {
            // Single leg 1.88 -> floored to 0.05 in Bet.Odds = 1.85
            Assert.True(AutoApprovalService.IsTotalOddsConsistentWithLegs(1.85m, new List<decimal> { 1.88m }, out _));

            // Manipulated total odds on single leg (leg is 1.85, total odds claims 2.50)
            Assert.False(AutoApprovalService.IsTotalOddsConsistentWithLegs(2.50m, new List<decimal> { 1.85m }, out _));
        }

        [Fact]
        public void IsTotalOddsConsistentWithLegs_MultiLegUnroundedBookmakerPrecision_AllowsWiggleRoom()
        {
            // User's example: bookmaker multiplies 1.515 * 1.725 = 2.613375, while displaying 1.51 and 1.72 on legs (1.51 * 1.72 = 2.5972)
            var legs = new List<decimal> { 1.51m, 1.72m };
            Assert.True(AutoApprovalService.IsTotalOddsConsistentWithLegs(2.61m, legs, out _));
            Assert.True(AutoApprovalService.IsTotalOddsConsistentWithLegs(2.60m, legs, out _));
            Assert.True(AutoApprovalService.IsTotalOddsConsistentWithLegs(2.55m, legs, out _));

            // Manipulated total odds (e.g. 3.10 when legs are 1.51 and 1.72) must be rejected
            Assert.False(AutoApprovalService.IsTotalOddsConsistentWithLegs(3.10m, legs, out _));
        }
    }
}
