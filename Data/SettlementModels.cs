using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BettingApp.Data
{
    public class SettlementSnapshot
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // Storing the calculation result as JSON to keep history immutable and simple
        public string SettlementJson { get; set; } = string.Empty;
        
        // Metadata for quick display
        public int UserCount { get; set; }
        public int TransactionCount { get; set; }
        public decimal TotalVolume { get; set; }
    }

    // Helper classes for the JSON logic (not stored in DB as tables)
    public class SettlementResult
    {
        public DateTime Date { get; set; }
        
        // --- NEW: List to capture historical balances exactly as they were ---
        public List<SettlementUserBalance> UserBalances { get; set; } = new();
        
        public List<SettlementInstruction> Instructions { get; set; } = new();
        public List<SettlementAdjustment> Adjustments { get; set; } = new();
    }

    // --- NEW: Helper class to store individual user balances ---
    public class SettlementUserBalance
    {
        public string UserName { get; set; } = string.Empty;
        public decimal Balance { get; set; }
    }

    public class SettlementInstruction
    {
        public string FromUser { get; set; } = string.Empty;
        public string ToUser { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class SettlementAdjustment
    {
        public string UserName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}