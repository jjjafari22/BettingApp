using System;
using System.Collections.Generic;
using Xunit;
using BettingApp.Data;
using BettingApp.Services;

namespace BettingApp.Tests
{
    public class BetSchedulingLogicTests
    {
        [Fact]
        public void FullySettledBet_ReturnsNull()
        {
            var bet = new Bet
            {
                Status = "Won",
                Legs = new List<BetLeg>
                {
                    new BetLeg { Outcome = "Won", StartTime = DateTime.UtcNow.AddHours(-1) }
                }
            };

            var nextCheck = BetSchedulingLogic.CalculateNextCheckTime(bet);
            Assert.Null(nextCheck);
        }

        [Fact]
        public void NoPendingLegs_ReturnsNull()
        {
            var bet = new Bet
            {
                Status = "Approved",
                Legs = new List<BetLeg>
                {
                    new BetLeg { Outcome = "Won", StartTime = DateTime.UtcNow.AddHours(-2) }
                }
            };

            var nextCheck = BetSchedulingLogic.CalculateNextCheckTime(bet);
            Assert.Null(nextCheck);
        }

        [Fact]
        public void PendingLegMissingStartTime_ReturnsUtcNowImmediately()
        {
            var beforeCall = DateTime.UtcNow;
            var bet = new Bet
            {
                Status = "Approved",
                Legs = new List<BetLeg>
                {
                    new BetLeg { Outcome = "Pending", StartTime = null, Sport = "Soccer" }
                }
            };

            var nextCheck = BetSchedulingLogic.CalculateNextCheckTime(bet);
            Assert.NotNull(nextCheck);
            Assert.True(nextCheck.Value >= beforeCall.AddSeconds(-1) && nextCheck.Value <= DateTime.UtcNow.AddSeconds(1));
        }

        [Fact]
        public void LiveSoccerBet_Started30MinsAgo_ReturnsStartTimePlusTwoHours()
        {
            var startTime = DateTime.UtcNow.AddMinutes(-30);
            var expectedFinish = startTime.AddHours(2); // 90 mins in the future

            var bet = new Bet
            {
                Status = "Approved",
                Legs = new List<BetLeg>
                {
                    new BetLeg { Outcome = "Pending", StartTime = startTime, Sport = "Soccer" }
                }
            };

            var nextCheck = BetSchedulingLogic.CalculateNextCheckTime(bet);
            Assert.NotNull(nextCheck);
            Assert.Equal(expectedFinish, nextCheck.Value);
        }

        [Fact]
        public void LiveHockeyBet_Started30MinsAgo_ReturnsStartTimePlusThreeHours()
        {
            var startTime = DateTime.UtcNow.AddMinutes(-30);
            var expectedFinish = startTime.AddHours(3); // 150 mins in the future

            var bet = new Bet
            {
                Status = "Approved",
                Legs = new List<BetLeg>
                {
                    new BetLeg { Outcome = "Pending", StartTime = startTime, Sport = "Ice Hockey" }
                }
            };

            var nextCheck = BetSchedulingLogic.CalculateNextCheckTime(bet);
            Assert.NotNull(nextCheck);
            Assert.Equal(expectedFinish, nextCheck.Value);
        }

        [Fact]
        public void MatchOverdue_ExpectedFinishInPast_Returns15MinutesAfterUtcNow()
        {
            // Soccer match started 2 hours and 10 minutes ago -> expected finish was 10 mins ago
            var startTime = DateTime.UtcNow.AddHours(-2).AddMinutes(-10);

            var bet = new Bet
            {
                Status = "Approved",
                Legs = new List<BetLeg>
                {
                    new BetLeg { Outcome = "Pending", StartTime = startTime, Sport = "Soccer" }
                }
            };

            var before = DateTime.UtcNow.AddMinutes(15);
            var nextCheck = BetSchedulingLogic.CalculateNextCheckTime(bet);
            var after = DateTime.UtcNow.AddMinutes(15);

            Assert.NotNull(nextCheck);
            Assert.True(nextCheck.Value >= before.AddSeconds(-1) && nextCheck.Value <= after.AddSeconds(1));
        }

        [Fact]
        public void UserScenario_TwoLegs_Leg1MarkedWon_Leg2Tomorrow_SchedulesAtLeg2FinishTime()
        {
            // Leg 1: Started 2 hours ago, finished, marked "Won"
            var leg1StartTime = DateTime.UtcNow.AddHours(-2);
            // Leg 2: Starts tomorrow at 18:00 UTC (Soccer)
            var leg2StartTime = DateTime.UtcNow.AddDays(1);
            var leg2ExpectedFinish = leg2StartTime.AddHours(2);

            var bet = new Bet
            {
                Status = "Approved",
                Legs = new List<BetLeg>
                {
                    new BetLeg 
                    { 
                        Match = "Team A vs Team B", 
                        Outcome = "Won", 
                        StartTime = leg1StartTime, 
                        Sport = "Soccer" 
                    },
                    new BetLeg 
                    { 
                        Match = "Team C vs Team D", 
                        Outcome = "Pending", 
                        StartTime = leg2StartTime, 
                        Sport = "Soccer" 
                    }
                }
            };

            var nextCheck = BetSchedulingLogic.CalculateNextCheckTime(bet);

            Assert.NotNull(nextCheck);
            // Must be Leg 2 start time + 2h, NOT 15 minutes after!
            Assert.Equal(leg2ExpectedFinish, nextCheck.Value);
        }

        [Fact]
        public void UserScenario_BeforeLeg1Finishes_LiveLeg1Evaluated()
        {
            // Leg 1: Soccer match started 1 hour ago, still Pending
            var leg1StartTime = DateTime.UtcNow.AddHours(-1);
            var leg1ExpectedFinish = leg1StartTime.AddHours(2); // 1 hour in the future

            // Leg 2: Soccer match tomorrow, still Pending
            var leg2StartTime = DateTime.UtcNow.AddDays(1);

            var bet = new Bet
            {
                Status = "Approved",
                Legs = new List<BetLeg>
                {
                    new BetLeg 
                    { 
                        Match = "Team A vs Team B", 
                        Outcome = "Pending", 
                        StartTime = leg1StartTime, 
                        Sport = "Soccer" 
                    },
                    new BetLeg 
                    { 
                        Match = "Team C vs Team D", 
                        Outcome = "Pending", 
                        StartTime = leg2StartTime, 
                        Sport = "Soccer" 
                    }
                }
            };

            var nextCheck = BetSchedulingLogic.CalculateNextCheckTime(bet);

            Assert.NotNull(nextCheck);
            // Must evaluate Leg 1's expected finish time (1 hour from now), not Leg 2!
            Assert.Equal(leg1ExpectedFinish, nextCheck.Value);
        }

        [Fact]
        public void DetermineAutoSettleStatus_ParlayWithOneVoidAndIndependentVerifiedLoss_ReturnsLost()
        {
            // Mirrors Bet #9172: 1 Void leg (Draw No Bet push) + 2 Lost legs + 1 Won leg on different matches
            var legs = new List<AiOutcomeLegResult>
            {
                new AiOutcomeLegResult { Match = "Besiktas vs Crystal Palace", Outcome = "Void", VerificationSource = "FotMob_Verified" },
                new AiOutcomeLegResult { Match = "1899 Hoffenheim vs Lyon", Outcome = "Lost", VerificationSource = "FotMob_Verified" },
                new AiOutcomeLegResult { Match = "Bournemouth vs AC Milan", Outcome = "Lost", VerificationSource = "FotMob_Verified" },
                new AiOutcomeLegResult { Match = "Lillestrom vs Real Sociedad", Outcome = "Won", VerificationSource = "FotMob_Verified" }
            };

            var status = BetSchedulingLogic.DetermineAutoSettleStatus("LOST", legs);
            Assert.Equal("Lost", status);
        }

        [Fact]
        public void DetermineAutoSettleStatus_SameMatchWithVoidAndLost_ReturnsNull()
        {
            // Bet Builder on a single match where one leg is Void and another is Lost -> requires manual review
            var legs = new List<AiOutcomeLegResult>
            {
                new AiOutcomeLegResult { Match = "Besiktas vs Crystal Palace", Outcome = "Void", VerificationSource = "FotMob_Verified" },
                new AiOutcomeLegResult { Match = "Besiktas vs Crystal Palace", Outcome = "Lost", VerificationSource = "FotMob_Verified" }
            };

            var status = BetSchedulingLogic.DetermineAutoSettleStatus("LOST", legs);
            Assert.Null(status);
        }

        [Fact]
        public void DetermineAutoSettleStatus_WinningParlayWithOneVoid_ReturnsNull()
        {
            // Winning parlay with a Void leg requires manual odds recalculation
            var legs = new List<AiOutcomeLegResult>
            {
                new AiOutcomeLegResult { Match = "Alexander Bublik vs Fabian Marozsan", Outcome = "Void", VerificationSource = "ESPN_Verified" },
                new AiOutcomeLegResult { Match = "Raphael Collignon vs Jiri Lehecka", Outcome = "Won", VerificationSource = "ESPN_Verified" }
            };

            var status = BetSchedulingLogic.DetermineAutoSettleStatus("WON", legs);
            Assert.Null(status);
        }

        [Fact]
        public void PostSettlement_AllLegsAlreadyGraded_ReturnsNull()
        {
            var bet = new Bet
            {
                Status = "Lost",
                Legs = new List<BetLeg>
                {
                    new BetLeg { Outcome = "Lost", StartTime = DateTime.UtcNow.AddHours(-3), Sport = "Soccer" },
                    new BetLeg { Outcome = "Won", StartTime = DateTime.UtcNow.AddHours(-3), Sport = "Soccer" }
                }
            };

            Assert.Null(BetSchedulingLogic.CalculatePostSettlementCheckTime(bet, isAutoSettled: true));
            Assert.Null(BetSchedulingLogic.CalculatePostSettlementCheckTime(bet, isAutoSettled: false));
        }

        [Fact]
        public void PostSettlement_EarlyLostParlay_SchedulesOnceAfterLatestPendingLegWithTwoHourBuffer()
        {
            var leg1Start = DateTime.UtcNow.AddHours(-3);
            var leg2SoccerStart = DateTime.UtcNow.AddHours(6);
            var leg3SoccerStart = DateTime.UtcNow.AddDays(1);
            var leg4BasketballStart = DateTime.UtcNow.AddDays(2);

            // Basketball = 3h sport delay + 2h post-settlement buffer = +5h after kickoff
            var expectedSweepTime = leg4BasketballStart.AddHours(5);

            var bet = new Bet
            {
                Status = "Lost",
                Legs = new List<BetLeg>
                {
                    new BetLeg { Match = "Leg 1", Outcome = "Lost", StartTime = leg1Start, Sport = "Soccer" },
                    new BetLeg { Match = "Leg 2", Outcome = "Pending", StartTime = leg2SoccerStart, Sport = "Soccer" },
                    new BetLeg { Match = "Leg 3", Outcome = "Pending", StartTime = leg3SoccerStart, Sport = "Soccer" },
                    new BetLeg { Match = "Leg 4", Outcome = "Pending", StartTime = leg4BasketballStart, Sport = "Basketball" }
                }
            };

            var nextCheck = BetSchedulingLogic.CalculatePostSettlementCheckTime(bet, isAutoSettled: true);
            Assert.NotNull(nextCheck);
            Assert.Equal(expectedSweepTime, nextCheck.Value);
        }

        [Fact]
        public void PostSettlement_ManualSettleBeforeAiGradesFinishedMatch_Schedules60MinCheck()
        {
            var bet = new Bet
            {
                Status = "Won",
                Legs = new List<BetLeg>
                {
                    new BetLeg { Match = "Leg 1", Outcome = "Pending", StartTime = DateTime.UtcNow.AddHours(-3), Sport = "Soccer" }
                }
            };

            var before = DateTime.UtcNow.AddMinutes(60);
            var nextCheck = BetSchedulingLogic.CalculatePostSettlementCheckTime(bet, isAutoSettled: false);
            var after = DateTime.UtcNow.AddMinutes(60);

            Assert.NotNull(nextCheck);
            Assert.True(nextCheck.Value >= before.AddSeconds(-1) && nextCheck.Value <= after.AddSeconds(1));
        }

        [Fact]
        public void PostSettlement_AutoSettledWithUnknownPendingStartTime_Schedules24HourFallback()
        {
            var bet = new Bet
            {
                Status = "Lost",
                Legs = new List<BetLeg>
                {
                    new BetLeg { Match = "Leg 1", Outcome = "Lost", StartTime = DateTime.UtcNow.AddHours(-3), Sport = "Soccer" },
                    new BetLeg { Match = "Leg 2", Outcome = "Pending", StartTime = null, Sport = "Soccer" }
                }
            };

            var before = DateTime.UtcNow.AddHours(24);
            var nextCheck = BetSchedulingLogic.CalculatePostSettlementCheckTime(bet, isAutoSettled: true);
            var after = DateTime.UtcNow.AddHours(24);

            Assert.NotNull(nextCheck);
            Assert.True(nextCheck.Value >= before.AddSeconds(-1) && nextCheck.Value <= after.AddSeconds(1));
        }

        [Fact]
        public void FollowUpCheckForSettledBet_WhenCheckRunsAtScheduledTime_EvenIfLegStaysPending_ReturnsNull_NoInfiniteLoop()
        {
            // Match started 4 hours and 1 minute ago (Soccer: 2h delay + 2h buffer = 4h, so scheduled time has arrived)
            var bet = new Bet
            {
                Status = "Lost",
                Legs = new List<BetLeg>
                {
                    new BetLeg { Match = "Leg 1", Outcome = "Lost", StartTime = DateTime.UtcNow.AddDays(-1), Sport = "Soccer" },
                    new BetLeg { Match = "Leg 2", Outcome = "Pending", StartTime = DateTime.UtcNow.AddHours(-4).AddMinutes(-1), Sport = "Soccer" }
                }
            };

            // Must return null so the settled bet NEVER loops even if Leg 2 could not be graded
            var followUp = BetSchedulingLogic.CalculateFollowUpCheckTimeForSettledBet(bet);
            Assert.Null(followUp);
        }
    }
}
