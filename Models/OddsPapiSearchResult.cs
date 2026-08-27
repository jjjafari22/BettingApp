using System;
using System.Collections.Generic;

namespace BettingApp.Models;

public class OddsPapiMarket
{
    public string MarketId { get; set; } = "";
    public string MarketName { get; set; } = "";
    // OutcomeId -> OutcomeName
    public Dictionary<string, string> OutcomeNames { get; set; } = new();
}

public class OddsData
{
    public double Price { get; set; }
    public DateTime? ChangedAt { get; set; }
    public string? BetslipUrl { get; set; }
    public bool IsSuspended { get; set; }
    public double? Limit { get; set; }
}

public class OddsPapiSearchResult
{
    public string MatchName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public bool IsLive { get; set; }
    public string? FlashscoreId { get; set; }
    
    // List of all markets found for this match
    public List<OddsPapiMarket> Markets { get; set; } = new();
    
    // MarketId -> (Bookmaker -> (OutcomeName -> OddsData))
    public Dictionary<string, Dictionary<string, Dictionary<string, OddsData>>> BookmakerOdds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Bookmaker -> URL to the match
    public Dictionary<string, string> BookmakerUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
