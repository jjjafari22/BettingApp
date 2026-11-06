using System;
using System.Collections.Generic;
using Xunit;
using BettingApp.Data;
using BettingApp.Models;
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

        [Fact]
        public void TryCalculateTier2BookieAverage_RequiresMinimumTwoActiveBookmakers_IncludesAllFiveBookies()
        {
            // Only 1 bookmaker -> returns false
            var singleBookie = new Dictionary<string, OddsData>
            {
                ["unibet.se"] = new OddsData { Price = 2.00, IsSuspended = false }
            };
            Assert.False(AutoApprovalService.TryCalculateTier2BookieAverage(singleBookie, out _, out int count1));
            Assert.Equal(1, count1);

            // 4 bookmakers (like Bet #1204: 2.30, 2.16, 2.30, 2.30) -> average = 2.265 across all 4 displayed prices
            var fourBookies = new Dictionary<string, OddsData>
            {
                ["pinnacle+30"] = new OddsData { Price = 2.30, IsSuspended = false },
                ["unibet.se"] = new OddsData { Price = 2.16, IsSuspended = false },
                ["coolbet"] = new OddsData { Price = 2.30, IsSuspended = true },
                ["betsson"] = new OddsData { Price = 2.30, IsSuspended = false }
            };
            Assert.True(AutoApprovalService.TryCalculateTier2BookieAverage(fourBookies, out decimal avg4, out int count4));
            Assert.Equal(4, count4);
            Assert.Equal(2.265m, avg4);

            // Pinnacle + Unibet + Betsson -> 3 valid bookmakers, average = (1.90 + 2.00 + 2.10) / 3 = 2.00
            var threeBookies = new Dictionary<string, OddsData>
            {
                ["pinnacle+30"] = new OddsData { Price = 1.90, IsSuspended = false },
                ["unibet.se"] = new OddsData { Price = 2.00, IsSuspended = false },
                ["betsson"] = new OddsData { Price = 2.10, IsSuspended = false }
            };
            Assert.True(AutoApprovalService.TryCalculateTier2BookieAverage(threeBookies, out decimal avg, out int count3));
            Assert.Equal(3, count3);
            Assert.Equal(2.00m, avg);
        }

        [Fact]
        public void TierWaterfall_PlayerSpecialWithoutPinnacle_ApprovesViaTier2WhenTwoSoftBookiesMatch()
        {
            var settings = new SystemSetting
            {
                IsAutoApprovalEnabled = true,
                IsTier1PinnacleEnabled = true,
                AutoApprovalMaxStakeNOK = 1000m,
                AutoApprovalMaxNetLiabilityNOK = 5000m,
                AutoApprovalMaxOverPinnaclePercent = 3.0m,
                AutoApprovalMinPinnacleLimitEUR = 300m,

                IsTier2SoftBookiesEnabled = true,
                Tier2MaxStakeNOK = 500m,
                Tier2MaxNetLiabilityNOK = 3000m,
                Tier2MaxOverBookieAvgPercent = 3.0m,

                IsTier3SmallStakeEnabled = true,
                Tier3MaxStakeNOK = 200m,
                Tier3MaxNetLiabilityNOK = 2000m
            };

            var user = new ApplicationUser { AllowAutoApproval = true, Balance = 5000m, MaxNetLiability = 20000m };
            // 400 kr stake (> Tier 3's 200 kr max, within Tier 2's 500 kr max)
            var bet = new Bet { Id = 1, Status = "Pending", AmountNOK = 400, Odds = 2.00m };
            var vision = new AiVisionExtractionResult
            {
                Legs = new List<AiVisionLeg>
                {
                    new AiVisionLeg { Match = "Arsenal vs Chelsea", Market = "Player Shots on Target", Selection = "Bukayo Saka Over 1.5", Odds = "2.04" }
                }
            };

            Assert.True(AutoApprovalService.ValidatePreLookupGuardrails(bet, user, settings, vision, out var legOdds, out bool allValid, out string? legErr, out _));

            var eval = new AutoApprovalService.AutoApprovalEvaluationResult();
            eval.LegResults[0] = new AutoApprovalService.EvaluatedLegOdds
            {
                SearchResult = new OddsPapiSearchResult
                {
                    FixtureId = "fix1",
                    MatchName = "Arsenal vs Chelsea",
                    StatusId = 0,
                    StartTime = DateTime.UtcNow.AddHours(2)
                },
                SelectedMarketId = "m1",
                HighlightedOutcome = "Bukayo Saka Over 1.5",
                MatchedBookieOdds = new Dictionary<string, OddsData>
                {
                    // No Pinnacle odds! Only Unibet (1.98) and Bet365 (2.02) -> avg = 2.00. User odds 2.04 is +2.0% (within 3.0%)
                    ["unibet.se"] = new OddsData { Price = 1.98 },
                    ["bet365"] = new OddsData { Price = 2.02 }
                }
            };

            AutoApprovalService.EvaluateTierWaterfall(bet, settings, vision, legOdds, allValid, legErr, eval);

            Assert.True(eval.CanAutoApprove);
            Assert.Equal(2, eval.MatchedTier);
            Assert.Equal("Tier 2: Soft Bookies", eval.MatchedTierName);
        }

        [Fact]
        public void TierWaterfall_NoBookmakerOddsAtAll_ApprovesViaTier3OnlyWhenFixtureVerifiedInOddsPapi()
        {
            var settings = new SystemSetting
            {
                IsAutoApprovalEnabled = true,
                IsTier1PinnacleEnabled = true,
                AutoApprovalMaxStakeNOK = 1000m,
                AutoApprovalMaxNetLiabilityNOK = 5000m,
                IsTier2SoftBookiesEnabled = true,
                Tier2MaxStakeNOK = 500m,
                Tier2MaxNetLiabilityNOK = 3000m,
                IsTier3SmallStakeEnabled = true,
                Tier3MaxStakeNOK = 200m,
                Tier3MaxNetLiabilityNOK = 2000m
            };

            var user = new ApplicationUser { AllowAutoApproval = true, Balance = 1000m, MaxNetLiability = 20000m };
            var bet = new Bet { Id = 2, Status = "Pending", AmountNOK = 200, Odds = 3.50m };
            var vision = new AiVisionExtractionResult
            {
                Legs = new List<AiVisionLeg>
                {
                    new AiVisionLeg { Match = "Arsenal vs Chelsea", Market = "Player Special", Selection = "Player X", Odds = "3.50" }
                }
            };

            Assert.True(AutoApprovalService.ValidatePreLookupGuardrails(bet, user, settings, vision, out var legOdds, out bool allValid, out string? legErr, out _));

            // 1. Match not found in OddsPapi (e.g. NBA or old finished match) -> Rejected even in Tier 3!
            var unverifiedEval = new AutoApprovalService.AutoApprovalEvaluationResult();
            unverifiedEval.LegResults[0] = new AutoApprovalService.EvaluatedLegOdds
            {
                ErrorMessage = "No OddsPapi matches found."
            };

            AutoApprovalService.EvaluateTierWaterfall(bet, settings, vision, legOdds, allValid, legErr, unverifiedEval);
            Assert.False(unverifiedEval.CanAutoApprove);
            Assert.Contains("No OddsPapi matches found", unverifiedEval.Reason);

            // 2. Match fixture verified in OddsPapi as upcoming, even when market has no bookmaker odds -> Approved via Tier 3!
            var verifiedEval = new AutoApprovalService.AutoApprovalEvaluationResult();
            verifiedEval.LegResults[0] = new AutoApprovalService.EvaluatedLegOdds
            {
                SearchResult = new OddsPapiSearchResult
                {
                    FixtureId = "fix2",
                    MatchName = "Arsenal vs Chelsea",
                    StatusId = 0,
                    StartTime = DateTime.UtcNow.AddHours(2)
                },
                ErrorMessage = "Market not found in OddsPapi."
            };

            AutoApprovalService.EvaluateTierWaterfall(bet, settings, vision, legOdds, allValid, legErr, verifiedEval);

            Assert.True(verifiedEval.CanAutoApprove);
            Assert.Equal(3, verifiedEval.MatchedTier);
            Assert.Equal("Tier 3: Small Stake", verifiedEval.MatchedTierName);
        }

        [Fact]
        public void TierWaterfall_BetBuilderAndLiveToggles_EnforcedCleanly()
        {
            var settings = new SystemSetting
            {
                IsAutoApprovalEnabled = true,
                AutoApprovalAllowBetBuilders = false,
                AutoApprovalAllowLiveBets = false,
                IsTier3SmallStakeEnabled = true,
                Tier3MaxStakeNOK = 200m,
                Tier3MaxNetLiabilityNOK = 2000m
            };

            var user = new ApplicationUser { AllowAutoApproval = true, Balance = 1000m, MaxNetLiability = 20000m };
            var betBuilderBet = new Bet { Id = 3, Status = "Pending", AmountNOK = 150, Odds = 4.00m, IsBetBuilder = true };
            var betBuilderVision = new AiVisionExtractionResult
            {
                IsBetBuilder = true,
                Legs = new List<AiVisionLeg>
                {
                    // Bet Builders output the shared Bet Builder odds on the first leg and empty on subsequent legs
                    new AiVisionLeg { Match = "Arsenal vs Chelsea", Market = "1X2", Selection = "Arsenal", Odds = "4.00" },
                    new AiVisionLeg { Match = "Arsenal vs Chelsea", Market = "Over/Under", Selection = "Over 2.5", Odds = "" }
                }
            };

            // 1. Blocked when AutoApprovalAllowBetBuilders == false
            Assert.False(AutoApprovalService.ValidatePreLookupGuardrails(betBuilderBet, user, settings, betBuilderVision, out _, out _, out _, out string? failReason));
            Assert.Contains("Bet Builder", failReason);

            // 2. Allowed via Tier 3 when AutoApprovalAllowBetBuilders == true, fixture is verified, and stake <= 200 kr
            settings.AutoApprovalAllowBetBuilders = true;
            Assert.True(AutoApprovalService.ValidatePreLookupGuardrails(betBuilderBet, user, settings, betBuilderVision, out var legOdds, out bool allValid, out string? legErr, out _));

            var verifiedFixture = new OddsPapiSearchResult
            {
                FixtureId = "fix3",
                MatchName = "Arsenal vs Chelsea",
                StatusId = 0,
                StartTime = DateTime.UtcNow.AddHours(2)
            };

            var eval = new AutoApprovalService.AutoApprovalEvaluationResult();
            eval.LegResults[0] = new AutoApprovalService.EvaluatedLegOdds { SearchResult = verifiedFixture };
            eval.LegResults[1] = new AutoApprovalService.EvaluatedLegOdds { SearchResult = verifiedFixture };

            AutoApprovalService.EvaluateTierWaterfall(betBuilderBet, settings, betBuilderVision, legOdds, allValid, legErr, eval);
            Assert.True(eval.CanAutoApprove);
            Assert.Equal(3, eval.MatchedTier);
        }

        [Fact]
        public void TryValidateBetLegsOddsConsistency_HandlesStandardAndBetBuilderCombos()
        {
            // 1. Bet #1276 scenario: non-BetBuilder with 1 extracted leg (1.80) but total odds 8.55 -> fails
            var bet1276Legs = new List<BetLeg>
            {
                new BetLeg { Match = "AEK Athens vs Real Madrid", Odds = "1.80" }
            };
            Assert.False(AutoApprovalService.TryValidateBetLegsOddsConsistency(8.55m, bet1276Legs, out string? reason1276));
            Assert.Contains("8.55", reason1276);

            // 2. Bet #1277 scenario: 2-leg Bet Builder (4.75 shared odds) + 1 single leg (1.80) = 8.55 total odds -> passes!
            var bet1277Legs = new List<BetLeg>
            {
                new BetLeg { Match = "RB Leipzig vs Manchester City", Odds = "4.75" },
                new BetLeg { Match = "RB Leipzig vs Manchester City", Odds = "" },
                new BetLeg { Match = "AEK Athens vs Real Madrid", Odds = "1.80" }
            };
            Assert.True(AutoApprovalService.TryValidateBetLegsOddsConsistency(8.55m, bet1277Legs, out _));

            // 3. Partial screenshot of a Bet Builder combo (only 1.56 BB extracted, but total odds is 13.30) -> fails!
            var partialBbLegs = new List<BetLeg>
            {
                new BetLeg { Match = "SønderjyskE vs FC Copenhagen", Odds = "" },
                new BetLeg { Match = "SønderjyskE vs FC Copenhagen", Odds = "1.56" }
            };
            Assert.False(AutoApprovalService.TryValidateBetLegsOddsConsistency(13.30m, partialBbLegs, out _));

            // 4. Cut-off leg missing its match/team name (Bet #1285 scenario before multi-screenshot merge):
            //    - TryValidateBetLegsHaveMatchNames flags the missing match name
            //    - TryValidateBetLegsOddsConsistency STILL validates the product of all legs (2.28 * 1.40 = 3.19) and never bypasses the product check!
            var missingMatchLegs = new List<BetLeg>
            {
                new BetLeg { Match = "AEK Athens vs Real Madrid", Odds = "2.28" },
                new BetLeg { Match = "", Odds = "1.40" }
            };
            Assert.False(AutoApprovalService.TryValidateBetLegsHaveMatchNames(missingMatchLegs, out string? missingMatchReason));
            Assert.Contains("missing its match/team name", missingMatchReason);
            Assert.True(AutoApprovalService.TryValidateBetLegsOddsConsistency(3.19m, missingMatchLegs, out _));
            Assert.False(AutoApprovalService.TryValidateBetLegsOddsConsistency(46.84m, missingMatchLegs, out string? productReason));
            Assert.Contains("46.84", productReason);
        }
    }
}
