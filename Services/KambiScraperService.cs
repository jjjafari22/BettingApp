using System.Text.Json;
using System.Text.Json.Serialization;

namespace BettingApp.Services;

public class KambiEvent
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("homeName")]
    public string HomeName { get; set; } = "";
    
    [JsonPropertyName("awayName")]
    public string AwayName { get; set; } = "";
    
    [JsonPropertyName("start")]
    public DateTime Start { get; set; }
}

public class KambiSearchResult
{
    [JsonPropertyName("events")]
    public List<KambiEvent> Events { get; set; } = new();
}

public class KambiOutcome
{
    [JsonPropertyName("englishLabel")]
    public string EnglishLabel { get; set; } = "";
    
    [JsonPropertyName("participant")]
    public string? Participant { get; set; }
    
    [JsonPropertyName("odds")]
    public int Odds { get; set; }
    
    [JsonPropertyName("line")]
    public int? Line { get; set; }
    
    public decimal DecimalOdds => Odds / 1000m;
    public decimal? DecimalLine => Line.HasValue ? Line.Value / 1000m : null;
}

public class KambiCriterion
{
    [JsonPropertyName("englishLabel")]
    public string EnglishLabel { get; set; } = "";
}

public class KambiBetOffer
{
    [JsonPropertyName("criterion")]
    public KambiCriterion Criterion { get; set; } = new();
    
    [JsonPropertyName("outcomes")]
    public List<KambiOutcome> Outcomes { get; set; } = new();
}

public class KambiSearchResponse
{
    [JsonPropertyName("result")]
    public KambiSearchResult Result { get; set; } = new();
}

public class KambiScraperService
{
    private readonly HttpClient _httpClient;

    public KambiScraperService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/114.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.DefaultRequestHeaders.Add("Origin", "https://www.unibet.com");
    }

    public async Task<(List<KambiEvent> Results, string? ErrorMessage)> SearchEventsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return (new List<KambiEvent>(), null);

        var searchUrl = $"https://eu-offering-api.kambicdn.com/offering/v2018/ub/term/search.json?lang=en_GB&market=NO&term={Uri.EscapeDataString(query)}";
        
        try 
        {
            var searchResponse = await _httpClient.GetAsync(searchUrl);
            var searchContent = await searchResponse.Content.ReadAsStringAsync();
            
            if (!searchResponse.IsSuccessStatusCode)
            {
                return (new List<KambiEvent>(), $"Kambi Search Error: {searchResponse.StatusCode}");
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using var searchDoc = JsonDocument.Parse(searchContent);
            
            string? participantId = null;
            if (searchDoc.RootElement.TryGetProperty("resultTerms", out var resultTerms))
            {
                var participants = resultTerms.EnumerateArray()
                    .Where(t => t.TryGetProperty("type", out var typeNode) && typeNode.GetString() == "PARTICIPANT")
                    .ToList();

                var exactMatch = participants.FirstOrDefault(t => 
                    (t.TryGetProperty("englishName", out var nameNode) && nameNode.GetString()?.Equals(query, StringComparison.OrdinalIgnoreCase) == true) ||
                    (t.TryGetProperty("termKey", out var termNode) && termNode.GetString()?.Equals(query, StringComparison.OrdinalIgnoreCase) == true)
                );

                if (exactMatch.ValueKind != JsonValueKind.Undefined)
                {
                    participantId = exactMatch.GetProperty("id").GetString();
                }
                else if (participants.Any())
                {
                    participantId = participants.First().GetProperty("id").GetString();
                }
            }

            if (string.IsNullOrEmpty(participantId))
            {
                return (new List<KambiEvent>(), $"No team found for '{query}'.");
            }

            // Step 2: Fetch the actual events for this participant
            var listUrl = $"https://eu-offering-api.kambicdn.com/offering/v2018/ub/listView{participantId}.json?lang=en_GB&market=NO";
            var listResponse = await _httpClient.GetAsync(listUrl);
            var listContent = await listResponse.Content.ReadAsStringAsync();

            if (!listResponse.IsSuccessStatusCode)
            {
                return (new List<KambiEvent>(), $"Kambi List Error: {listResponse.StatusCode}");
            }

            using var listDoc = JsonDocument.Parse(listContent);
            var eventsList = new List<KambiEvent>();
            
            if (listDoc.RootElement.TryGetProperty("events", out var eventsNode))
            {
                foreach (var item in eventsNode.EnumerateArray())
                {
                    if (item.TryGetProperty("event", out var evNode))
                    {
                        eventsList.Add(JsonSerializer.Deserialize<KambiEvent>(evNode.GetRawText(), options)!);
                    }
                }
            }
            
            return (eventsList, null);
        }
        catch (Exception ex)
        {
            return (new List<KambiEvent>(), $"Exception: {ex.Message}");
        }
    }

    public async Task<(List<KambiBetOffer> Markets, string? ErrorMessage)> GetEventMarketsAsync(long eventId)
    {
        var url = $"https://eu-offering-api.kambicdn.com/offering/v2018/ub/betoffer/event/{eventId}.json?lang=en_GB&market=NO";
        try
        {
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return (new List<KambiBetOffer>(), $"Error fetching markets: {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(content);
            var offers = new List<KambiBetOffer>();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            if (doc.RootElement.TryGetProperty("betOffers", out var offersNode))
            {
                foreach (var item in offersNode.EnumerateArray())
                {
                    offers.Add(JsonSerializer.Deserialize<KambiBetOffer>(item.GetRawText(), options)!);
                }
            }
            
            return (offers, null);
        }
        catch (Exception ex)
        {
            return (new List<KambiBetOffer>(), $"Exception: {ex.Message}");
        }
    }
}
