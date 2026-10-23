using System;
using System.Linq;
using BettingApp.Data;

namespace BettingApp.Services
{
    public static class BetSchedulingLogic
    {
        private const int PostSettlementBufferHours = 2;

        public static int GetSportDelayHours(string? sport)
        {
            if (string.IsNullOrEmpty(sport)) return 2;
            if (sport.Contains("Hockey", StringComparison.OrdinalIgnoreCase)) return 3;
            if (sport.Contains("American Football", StringComparison.OrdinalIgnoreCase) || sport.Contains("Football", StringComparison.OrdinalIgnoreCase)) return 4;
            if (sport.Contains("Baseball", StringComparison.OrdinalIgnoreCase)) return 3;
            if (sport.Contains("Basketball", StringComparison.OrdinalIgnoreCase)) return 3;
            return 2;
        }

        public static DateTime? CalculateNextCheckTime(Bet dbBet)
        {
            // 1. If the bet is already settled/terminal, active polling does not apply
            var finishedStatuses = new[] { "Won", "Lost", "Void", "Completed", "Cancelled", "Rejected" };
            if (finishedStatuses.Contains(dbBet.Status, StringComparer.OrdinalIgnoreCase))
            {
                return null;
            }

            // 2. Identify the pending legs
            var pendingLegs = dbBet.Legs?.Where(l => string.Equals(l.Outcome, "Pending", StringComparison.OrdinalIgnoreCase)).ToList();

            // If there are no legs or no pending legs, it means the match is finished but the auto-settler
            // might have refused to settle it (e.g. manual review needed). Stop auto-checking.
            if (pendingLegs == null || !pendingLegs.Any())
            {
                return null;
            }

            // 3. If ANY pending leg is completely missing a start time, we need to schedule a check immediately 
            // so the FotMob AI service can wake up and find the start times.
            if (pendingLegs.Any(l => !l.StartTime.HasValue))
            {
                return DateTime.UtcNow;
            }

            // 4. Determine the earliest start time among pending legs.
            // We ONLY look at pending legs to avoid continuous polling if an early leg is already finished
            // and the next leg doesn't start until much later.
            DateTime? earliestStart = null;
            var legStarts = pendingLegs.Where(l => l.StartTime.HasValue).Select(l => l.StartTime!.Value).ToList();
            
            if (legStarts.Any())
            {
                earliestStart = legStarts.Min();
            }
            else
            {
                // Fallback: If for some reason we have pending legs but no start times, and step 3 didn't catch it
                // (e.g., if there's no start time and we couldn't schedule), we fallback to MatchStartTime or 1h check.
                earliestStart = dbBet.MatchStartTime;
                if (!earliestStart.HasValue)
                {
                    return DateTime.UtcNow;
                }
            }

            // 5. Determine the expected finish time
            var nextLeg = pendingLegs.OrderBy(l => l.StartTime ?? DateTime.MaxValue).FirstOrDefault();
            int delayHours = GetSportDelayHours(nextLeg?.Sport);

            var expectedFinishTime = earliestStart.Value.AddHours(delayHours);

            if (expectedFinishTime > DateTime.UtcNow)
            {
                // The match is either not started yet or is currently in progress.
                // Wait until the match is expected to be finished.
                return expectedFinishTime;
            }
            else
            {
                // The expected finish time is in the past, but results are not in yet.
                // Check every 15 minutes.
                return DateTime.UtcNow.AddMinutes(15);
            }
        }

        /// <summary>
        /// Calculates the one-time post-settlement check time when a bet transitions to Won, Lost, or Void.
        /// Ensures any remaining Pending legs (e.g. Legs 2-4 on an early-lost parlay) are graded in a single batch
        /// after the latest pending leg finishes (+ 2h buffer), while returning null if all legs are already graded.
        /// </summary>
        public static DateTime? CalculatePostSettlementCheckTime(Bet dbBet, bool isAutoSettled)
        {
            if (dbBet.AiEvaluation != null && dbBet.AiEvaluation.AiOutcomeResult == "Admin Override")
            {
                return null;
            }

            var pendingLegs = dbBet.Legs?
                .Where(l => string.Equals(l.Outcome, "Pending", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (dbBet.Legs != null && dbBet.Legs.Count > 0)
            {
                // All legs are already graded (Won, Lost, or Void) -> no post-settlement check needed!
                if (pendingLegs == null || !pendingLegs.Any())
                {
                    return null;
                }

                var legsWithStart = pendingLegs.Where(l => l.StartTime.HasValue && l.StartTime.Value > DateTime.MinValue).ToList();
                if (legsWithStart.Any())
                {
                    DateTime latestFinishWithBuffer = legsWithStart
                        .Max(l => l.StartTime!.Value.AddHours(GetSportDelayHours(l.Sport) + PostSettlementBufferHours));

                    if (latestFinishWithBuffer > DateTime.UtcNow.AddMinutes(60))
                    {
                        return latestFinishWithBuffer;
                    }

                    if (!isAutoSettled)
                    {
                        // Admin manually settled a bet whose matches recently finished -> run the 1-hour verification check
                        return DateTime.UtcNow.AddMinutes(60);
                    }

                    // Auto-settled, and all known start times + buffer are already in the past.
                    // Only schedule a 24h fallback if there is another pending leg with no known StartTime.
                    return pendingLegs.Any(l => !l.StartTime.HasValue || l.StartTime.Value == DateTime.MinValue)
                        ? DateTime.UtcNow.AddHours(24)
                        : null;
                }

                // None of the pending legs have a StartTime yet
                if (dbBet.MatchStartTime.HasValue && dbBet.MatchStartTime.Value > DateTime.MinValue)
                {
                    DateTime fallbackFinish = dbBet.MatchStartTime.Value.AddHours(2 + PostSettlementBufferHours);
                    if (fallbackFinish > DateTime.UtcNow.AddMinutes(60))
                    {
                        return fallbackFinish;
                    }
                }

                // If auto-settled and remaining pending legs had no StartTime found, check once in 24h.
                // If manually settled by admin before AI ran, check in 60 minutes.
                return isAutoSettled ? DateTime.UtcNow.AddHours(24) : DateTime.UtcNow.AddMinutes(60);
            }

            // Legacy / empty-legs fallback when an admin manually settles before AI has run
            if (!isAutoSettled && !IsAiOutcomeFinished(dbBet.AiEvaluation?.AiOutcomeResult))
            {
                return DateTime.UtcNow.AddMinutes(60);
            }

            return null;
        }

        /// <summary>
        /// Called after ConfirmOutcomeAsync completes on a bet that is ALREADY settled (Won, Lost, or Void).
        /// Returns a future check time ONLY if a remaining Pending leg has a known future finish time (+2h buffer)
        /// more than 30 minutes from now; otherwise returns null so settled bets never loop.
        /// </summary>
        public static DateTime? CalculateFollowUpCheckTimeForSettledBet(Bet dbBet)
        {
            if (dbBet.AiEvaluation != null && dbBet.AiEvaluation.AiOutcomeResult == "Admin Override")
            {
                return null;
            }

            var pendingLegsWithStart = dbBet.Legs?
                .Where(l => string.Equals(l.Outcome, "Pending", StringComparison.OrdinalIgnoreCase) &&
                            l.StartTime.HasValue && l.StartTime.Value > DateTime.MinValue)
                .ToList();

            if (pendingLegsWithStart == null || !pendingLegsWithStart.Any())
            {
                return null;
            }

            DateTime latestFinishWithBuffer = pendingLegsWithStart
                .Max(l => l.StartTime!.Value.AddHours(GetSportDelayHours(l.Sport) + PostSettlementBufferHours));

            if (latestFinishWithBuffer > DateTime.UtcNow.AddMinutes(30))
            {
                return latestFinishWithBuffer;
            }

            return null;
        }

        private static bool IsAiOutcomeFinished(string? aiOutcomeResult)
        {
            if (string.IsNullOrEmpty(aiOutcomeResult)) return false;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(aiOutcomeResult);
                if (doc.RootElement.TryGetProperty("overallStatus", out var prop))
                {
                    var s = prop.GetString()?.Trim().ToUpperInvariant() ?? "";
                    return s == "MATCH FINISHED - WON" || s == "MATCH WON" || s == "WON" ||
                           s == "MATCH FINISHED - LOST" || s == "MATCH LOST" || s == "LOST" ||
                           s == "MATCH FINISHED - VOID" || s == "MATCH VOID" || s == "VOID" ||
                           s == "UNKNOWN";
                }
            }
            catch { }
            return false;
        }

        public static string? DetermineAutoSettleStatus(string? overallStatus, System.Collections.Generic.List<BettingApp.Services.AiOutcomeLegResult>? legs)
        {
            if (legs == null || legs.Count == 0) return null;

            bool hasAnyVoid = legs.Any(l => string.Equals(l.Outcome, "Void", StringComparison.OrdinalIgnoreCase));

            string status = overallStatus?.ToUpper() ?? "";

            static bool IsVerified(string? source) => source == "FotMob_Verified" || source == "ESPN_Verified";

            if (status.Contains("WON"))
            {
                if (hasAnyVoid) return null;
                if (legs.All(l => IsVerified(l.VerificationSource)))
                    return "Won";
            }
            else if (status.Contains("LOST"))
            {
                bool hasDefinitiveVerifiedLoss = legs.Any(l =>
                    string.Equals(l.Outcome, "Lost", StringComparison.OrdinalIgnoreCase) &&
                    IsVerified(l.VerificationSource) &&
                    !legs.Where(other => string.Equals(other.Match?.Trim(), l.Match?.Trim(), StringComparison.OrdinalIgnoreCase))
                         .Any(other => string.Equals(other.Outcome, "Void", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(other.Outcome, "Pending", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(other.Outcome, "Unknown", StringComparison.OrdinalIgnoreCase)));

                if (hasDefinitiveVerifiedLoss)
                    return "Lost";
            }

            return null;
        }
    }
}
