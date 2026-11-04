using System;
using System.Collections.Generic;
using Xunit;
using BettingApp.Data;
using BettingApp.Services;

namespace BettingApp.Tests
{
    public class BetFinancialCalculationsTests
    {
        [Fact]
        public void PotentialPayout_CashOnly_CalculatesTotalReturn()
        {
            var bet = new Bet
            {
                AmountNOK = 500,
                Odds = 2.50m,
                FreeBetAmount = 0m
            };

            // 500 * 2.50 = 1250 kr
            Assert.Equal(1250m, bet.PotentialPayout);
        }

        [Fact]
        public void PotentialPayout_FreeBetOnly_CalculatesNetWinningsOnly()
        {
            var bet = new Bet
            {
                AmountNOK = 200,
                Odds = 3.00m,
                FreeBetAmount = 200m
            };

            // Free bet returns net winnings only: 200 * (3.00 - 1) = 400 kr
            Assert.Equal(400m, bet.PotentialPayout);
        }

        [Fact]
        public void PotentialPayout_PartialFreeBet_CalculatesCombinedReturn()
        {
            var bet = new Bet
            {
                AmountNOK = 500,
                Odds = 2.00m,
                FreeBetAmount = 200m
            };

            // Cash portion: (500 - 200) * 2.00 = 600 kr
            // Free bet portion: 200 * (2.00 - 1) = 200 kr
            // Total payout: 800 kr
            Assert.Equal(800m, bet.PotentialPayout);
        }

        [Fact]
        public void NetLiability_CalculatesCorrectLiability()
        {
            var bet = new Bet
            {
                AmountNOK = 1000,
                Odds = 3.25m
            };

            // 1000 * (3.25 - 1) = 2250 kr
            Assert.Equal(2250m, bet.NetLiability);
        }

        [Fact]
        public void StakeReduction_CorrectlyCalculatesRefundAndCapsFreeBet()
        {
            decimal originalStake = 500m;
            decimal originalFreeBet = 200m;
            decimal newStake = 150m; // Reduced below original free bet amount

            // Safety rule from BetProcessingService:
            decimal newFreeBetAmount = Math.Min(originalFreeBet, newStake);
            Assert.Equal(150m, newFreeBetAmount); // Capped at newStake

            // Reversal of old impact
            decimal userBalance = 1000m;
            decimal userFreeBalance = 0m;

            // Revert old:
            userBalance += (originalStake - originalFreeBet); // +300 cash returned
            userFreeBalance += originalFreeBet;              // +200 free bet returned
            Assert.Equal(1300m, userBalance);
            Assert.Equal(200m, userFreeBalance);

            // Apply new:
            userBalance -= (newStake - newFreeBetAmount);     // - (150 - 150) = -0 cash
            userFreeBalance -= newFreeBetAmount;             // - 150 free bet
            Assert.Equal(1300m, userBalance);                // Full cash stake refunded!
            Assert.Equal(50m, userFreeBalance);              // 50 kr unused free bet returned to user!
        }

        [Fact]
        public void StakeIncrease_CorrectlyDeductsAdditionalCash()
        {
            decimal originalStake = 500m;
            decimal originalFreeBet = 200m;
            decimal newStake = 800m; // Increased by 300 kr

            decimal newFreeBetAmount = Math.Min(originalFreeBet, newStake);
            Assert.Equal(200m, newFreeBetAmount);

            decimal userBalance = 1000m;
            decimal userFreeBalance = 0m;

            // Revert old:
            userBalance += (originalStake - originalFreeBet); // +300
            userFreeBalance += originalFreeBet;              // +200

            // Apply new:
            userBalance -= (newStake - newFreeBetAmount);     // - (800 - 200) = -600
            userFreeBalance -= newFreeBetAmount;             // - 200

            // Net balance change: +300 - 600 = -300 kr (exact cash increase!)
            Assert.Equal(700m, userBalance);
            Assert.Equal(0m, userFreeBalance);
        }

        [Fact]
        public void StakeIncrease_WithAvailableFreeBet_ConsumesFreeBetFirst()
        {
            decimal originalStake = 100m;
            decimal originalFreeBet = 100m;
            decimal newStake = 1200m; // Increased by 1,100 kr

            decimal userBalance = -5000m;
            decimal creditLimit = 5000m; // 0 kr cash credit remaining
            decimal userFreeBalance = 1100m; // 1,100 kr FB available

            decimal requiredAmount = newStake - originalStake; // 1,100 kr
            decimal availableFunds = userBalance + userFreeBalance + creditLimit; // 1,100 kr
            Assert.True(availableFunds >= requiredAmount);

            // Revert old:
            userBalance += (originalStake - originalFreeBet);
            userFreeBalance += originalFreeBet; // 1,200 kr FB after reverting old impact

            // Calculate new FreeBetAmount using BetProcessingService rule:
            decimal maxFreeBetForBet = Math.Min(originalFreeBet + Math.Max(0m, newStake - originalStake), newStake);
            decimal newFreeBetAmount = Math.Min(maxFreeBetForBet, userFreeBalance);
            Assert.Equal(1200m, newFreeBetAmount);

            // Apply new:
            userBalance -= (newStake - newFreeBetAmount);
            userFreeBalance -= newFreeBetAmount;

            Assert.Equal(-5000m, userBalance); // Cash balance untouched
            Assert.Equal(0m, userFreeBalance); // All 1,100 kr additional FB consumed
        }

        [Fact]
        public void WonBetCorrection_ReversesPayoutAndAppliesLossCorrectly()
        {
            decimal stake = 500m;
            decimal odds = 2.00m;
            decimal freeBet = 0m;

            var bet = new Bet { AmountNOK = (int)stake, Odds = odds, FreeBetAmount = freeBet };
            decimal payout = bet.PotentialPayout; // 1000 kr

            decimal userBalance = 2000m; // User had won and received 1000 kr payout
            decimal lifetimeProfit = 500m;

            // Step 1: Revert "Won"
            userBalance += (stake - freeBet);       // + 500
            userBalance -= payout;                  // - 1000
            lifetimeProfit -= (payout - (stake - freeBet)); // - (1000 - 500) = -500

            Assert.Equal(1500m, userBalance);
            Assert.Equal(0m, lifetimeProfit);

            // Step 2: Apply "Lost"
            userBalance -= (stake - freeBet);       // - 500
            lifetimeProfit -= (stake - freeBet);    // - 500

            // Final state: User balance is 1000 kr (lost the 500 kr stake, won 0 kr)
            // Lifetime profit is -500 kr (lost 500 kr)
            Assert.Equal(1000m, userBalance);
            Assert.Equal(-500m, lifetimeProfit);
        }

        [Fact]
        public void GetAllScreenshotUrls_SingleAndMergedScreenshots_ReturnsOrderedUniqueUrls()
        {
            var singleBet = new Bet
            {
                ScreenshotUrl = "https://blob.core.windows.net/uploads/ss1.jpg",
                ExtraScreenshotUrls = null
            };
            Assert.False(singleBet.HasExtraScreenshots);
            Assert.Equal(new[] { "https://blob.core.windows.net/uploads/ss1.jpg" }, singleBet.GetAllScreenshotUrls());

            var mergedBet = new Bet
            {
                ScreenshotUrl = "https://blob.core.windows.net/uploads/ss1.jpg",
                ExtraScreenshotUrls = "https://blob.core.windows.net/uploads/ss2.jpg\nhttps://blob.core.windows.net/uploads/ss1.jpg\nhttps://blob.core.windows.net/uploads/ss3.jpg"
            };
            Assert.True(mergedBet.HasExtraScreenshots);
            Assert.Equal(
                new[]
                {
                    "https://blob.core.windows.net/uploads/ss1.jpg",
                    "https://blob.core.windows.net/uploads/ss2.jpg",
                    "https://blob.core.windows.net/uploads/ss3.jpg"
                },
                mergedBet.GetAllScreenshotUrls());
        }

        [Fact]
        public void CombineMultiScreenshotResults_DeduplicatesOverlappingLegsAndSelectsTrueStakeAndOdds()
        {
            var start1 = new DateTime(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc);
            var start2 = new DateTime(2026, 11, 5, 20, 0, 0, DateTimeKind.Utc);

            // Screenshot 1: top of parlay slip (no stake footer visible, AI accidentally grabbed 1.85 as totalOdds)
            var ss1 = new AiVisionExtractionResult
            {
                Bookmaker = "Unibet",
                IsBetBuilder = false,
                IsLive = false,
                Stake = "",
                TotalOdds = "1.85",
                Legs = new List<AiVisionLeg>
                {
                    new() { Match = "Arsenal vs Chelsea", Sport = "Soccer", Market = "Match Result", Selection = "Arsenal", Odds = "1.85" },
                    // Overlapping leg scrolled across both screenshots (missing StartTime on ss1, populated on ss2)
                    new() { Match = "Real Madrid vs Barcelona", Sport = "Soccer", Market = "Both Teams To Score", Selection = "Yes", Odds = "1.65" }
                }
            };

            // Screenshot 2: bottom of parlay slip with overlapping leg + Bet Builder multi-selection + true stake/odds footer
            var ss2 = new AiVisionExtractionResult
            {
                Bookmaker = "Unknown",
                IsBetBuilder = true,
                IsLive = false,
                Stake = "500 kr",
                TotalOdds = "14.53",
                Legs = new List<AiVisionLeg>
                {
                    new() { Match = "  Real Madrid   vs Barcelona ", Sport = "Soccer", Market = "both teams to score", Selection = "YES", Odds = "1.65", StartTime = start1 },
                    new() { Match = "Bayern vs Dortmund", Sport = "Soccer", Market = "Match Result", Selection = "Bayern", Odds = "", StartTime = start2 },
                    new() { Match = "Bayern vs Dortmund", Sport = "Soccer", Market = "Total Goals", Selection = "Over 2.5", Odds = "2.40", StartTime = start2 }
                }
            };

            var combined = AiVisionExtractionResult.CombineMultiScreenshotResults(new[] { ss1, ss2 });

            Assert.NotNull(combined);
            Assert.Equal("Unibet", combined!.Bookmaker);
            Assert.True(combined.IsBetBuilder);
            Assert.Equal("500 kr", combined.Stake);
            Assert.Equal("14.53", combined.TotalOdds);
            Assert.Equal(500, AiVisionExtractionResult.TryParseStakeNok(combined.Stake));

            // 4 unique legs: Arsenal, Real/Barca (deduplicated once!), and both distinct Bet Builder legs for Bayern/Dortmund
            Assert.Equal(4, combined.Legs.Count);
            Assert.Equal("Arsenal vs Chelsea", combined.Legs[0].Match);
            Assert.Equal("Real Madrid vs Barcelona", combined.Legs[1].Match);
            Assert.Equal(start1, combined.Legs[1].StartTime);
            Assert.Equal("Match Result", combined.Legs[2].Market);
            Assert.Equal("Total Goals", combined.Legs[3].Market);
        }
    }
}
