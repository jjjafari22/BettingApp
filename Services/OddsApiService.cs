using System.Text.Json;
using System.Text.Json.Serialization;

namespace BettingApp.Services;

public class OddsApiSport
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("group")]
    public string Group { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("active")]
    public bool Active { get; set; }
}

public class OddsApiOutcome
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("price")]
    public decimal Price { get; set; }
    
    [JsonPropertyName("point")]
    public decimal? Point { get; set; }
}

public class OddsApiMarket
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("last_update")]
    public DateTime? LastUpdate { get; set; }

    [JsonPropertyName("outcomes")]
    public List<OddsApiOutcome> Outcomes { get; set; } = new();
}

public class OddsApiBookmaker
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("last_update")]
    public DateTime? LastUpdate { get; set; }

    [JsonPropertyName("markets")]
    public List<OddsApiMarket> Markets { get; set; } = new();
}

public class OddsApiEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("sport_key")]
    public string SportKey { get; set; } = "";

    [JsonPropertyName("sport_title")]
    public string SportTitle { get; set; } = "";

    [JsonPropertyName("commence_time")]
    public DateTime CommenceTime { get; set; }

    [JsonPropertyName("home_team")]
    public string HomeTeam { get; set; } = "";

    [JsonPropertyName("away_team")]
    public string AwayTeam { get; set; } = "";

    [JsonPropertyName("bookmakers")]
    public List<OddsApiBookmaker> Bookmakers { get; set; } = new();
}

public class OddsApiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public OddsApiService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _apiKey = config["OddsApi:ApiKey"] ?? "";
    }

    public async Task<List<OddsApiSport>> GetActiveSportsAsync()
    {
        if (string.IsNullOrEmpty(_apiKey)) return new List<OddsApiSport>();
        
        var url = $"https://api.the-odds-api.com/v4/sports?apiKey={_apiKey}";
        try 
        {
            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<OddsApiSport>>(content) ?? new List<OddsApiSport>();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching sports: {ex.Message}");
        }
        return new List<OddsApiSport>();
    }

    public async Task<(List<OddsApiEvent> Results, string? ErrorMessage)> GetOddsAsync(string sportKey, string markets = "h2h,spreads,totals")
    {
        if (string.IsNullOrEmpty(_apiKey)) return (new List<OddsApiEvent>(), "API key is missing.");

        // We use region 'eu' to get Unibet EU, Coolbet, Betsson, Bet365 etc.
        var url = $"https://api.the-odds-api.com/v4/sports/{sportKey}/odds/?apiKey={_apiKey}&regions=eu&markets={markets}&oddsFormat=decimal";
        
        try 
        {
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var results = JsonSerializer.Deserialize<List<OddsApiEvent>>(content, options) ?? new List<OddsApiEvent>();
                return (results, null);
            }
            else
            {
                Console.WriteLine($"Odds API Error: {response.StatusCode} - {content}");
                return (new List<OddsApiEvent>(), $"API Error: {response.StatusCode}. {content}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception fetching odds: {ex.Message}");
            return (new List<OddsApiEvent>(), $"Exception: {ex.Message}");
        }
    }
}
