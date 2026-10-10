using System;
using System.Linq;
using BettingApp.Data;

namespace BettingApp.Services
{
    public static class BetSchedulingLogic
    {
        public static DateTime? CalculateNextCheckTime(Bet dbBet)
        {
            // 1. If the bet is already fully settled, stop checking
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
                    return DateTime.UtcNow.AddMinutes(60);
                }
            }

            // 5. Determine if the match has started yet
            bool isMatchNotStarted = earliestStart.Value > DateTime.UtcNow;

            if (isMatchNotStarted)
            {
                // Find the sport of the next relevant leg
                var nextLeg = pendingLegs.OrderBy(l => l.StartTime ?? DateTime.MaxValue).FirstOrDefault();
                string sport = nextLeg?.Sport ?? "Soccer";
                
                int delayHours = sport.Contains("Hockey", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
                
                var delayTime = earliestStart.Value.AddHours(delayHours);
                return delayTime <= DateTime.UtcNow ? DateTime.UtcNow : delayTime;
            }
            else
            {
                // MATCH IS IN PROGRESS (Earliest start time is in the past)
                // Results are not in yet, so we check every 15 minutes.
                return DateTime.UtcNow.AddMinutes(15);
            }
        }

        public static string? DetermineAutoSettleStatus(string? overallStatus, System.Collections.Generic.List<BettingApp.Services.AiOutcomeLegResult>? legs)
        {
            if (legs == null || legs.Count == 0) return null;

            bool hasAnyVoid = legs.Any(l => string.Equals(l.Outcome, "Void", StringComparison.OrdinalIgnoreCase));
            if (hasAnyVoid) return null;

            string status = overallStatus?.ToUpper() ?? "";

            if (status.Contains("WON"))
            {
                if (legs.All(l => l.VerificationSource == "FotMob_Verified"))
                    return "Won";
            }
            else if (status.Contains("LOST"))
            {
                if (legs.Any(l => string.Equals(l.Outcome, "Lost", StringComparison.OrdinalIgnoreCase) && l.VerificationSource == "FotMob_Verified"))
                    return "Lost";
            }

            return null;
        }
    }
}
