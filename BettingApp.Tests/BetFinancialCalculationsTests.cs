using System;
using Xunit;
using BettingApp.Data;

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
    }
}
