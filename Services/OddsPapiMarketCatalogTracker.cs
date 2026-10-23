using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BettingApp.Services;

public static class OddsPapiMarketCatalogTracker
{
    private static readonly object _syncLock = new();
    private static Dictionary<int, HashSet<string>>? _knownMarketsBySport;

    // Baseline of all OddsPapi markets known as of 2026-10-23 across Soccer (10), Ice Hockey (15), and Tennis (12).
    // Prevents false "new market" alerts on initial startup or fresh deployments while immediately catching any newly added market.
    private static readonly Dictionary<int, string[]> BaselineMarkets = new()
    {
        [10] = new[]
        {
            "2Up - Full Time Result", "Anytime Goal Scorer", "Asian Handicap", "Asian Handicap First Half", "Asian Handicap Second Half",
            "Bookings - 1X2", "Bookings - 1X2 First Half", "Bookings - 1X2 Second Half", "Bookings - Handicap", "Bookings - Handicap First Half",
            "Bookings - Handicap Second Half", "Bookings - Odd Even", "Bookings - Odd Even First Half", "Bookings - Odd Even Second Half",
            "Bookings - Odd Even Team 1", "Bookings - Odd Even Team 1 First Half", "Bookings - Odd Even Team 1 Second Half", "Bookings - Odd Even Team 2",
            "Bookings - Odd Even Team 2 First Half", "Bookings - Odd Even Team 2 Second Half", "Bookings - Over Under First Half",
            "Bookings - Over Under Full Time", "Bookings - Over Under Second Half", "Bookings - Over Under Team 1", "Bookings - Over Under Team 1 First Half",
            "Bookings - Over Under Team 1 Second Half", "Bookings - Over Under Team 2", "Bookings - Over Under Team 2 First Half",
            "Bookings - Over Under Team 2 Second Half", "Both Teams To Score", "Both Teams To Score First Half", "Both Teams To Score Second Half",
            "Corners - 1X2", "Corners - 1X2 First Half", "Corners - 1X2 Second Half", "Corners - Handicap", "Corners - Handicap First Half",
            "Corners - Handicap Second Half", "Corners - Odd Even", "Corners - Odd Even First Half", "Corners - Odd Even Second Half",
            "Corners - Odd Even Team 1", "Corners - Odd Even Team 1 First Half", "Corners - Odd Even Team 1 Second Half", "Corners - Odd Even Team 2",
            "Corners - Odd Even Team 2 First Half", "Corners - Odd Even Team 2 Second Half", "Corners - Over Under First Half",
            "Corners - Over Under Full Time", "Corners - Over Under Second Half", "Corners - Over Under Team 1", "Corners - Over Under Team 1 First Half",
            "Corners - Over Under Team 1 Second Half", "Corners - Over Under Team 2", "Corners - Over Under Team 2 First Half",
            "Corners - Over Under Team 2 Second Half", "Correct Score First Half", "Correct Score Full Time", "Correct Score Second Half",
            "Double Chance First Half", "Double Chance Full Time", "Double Chance Second Half", "Draw No Bet", "Draw No Bet First Half",
            "Draw No Bet Second Half", "European Handicap", "European Handicap First Half", "European Handicap Second Half",
            "Exact Score First Half", "Exact Score Full Time", "Exact Score Second Half", "First Goal Full Time", "First Goal Scorer",
            "First Half Result", "Full Time Result", "Half Time / Full Time", "Highest Scoring Half", "Last Goal Full Time", "Last Goal Scorer",
            "Odd Even First Half", "Odd Even Full Time", "Odd Even Second Half", "Over Under First Half", "Over Under Full Time",
            "Over Under Player Assists (incl. overtime)", "Over Under Player Fouls Committed (incl. overtime)",
            "Over Under Player Goalkeeper Saves (incl. overtime)", "Over Under Player Goals (incl. overtime)",
            "Over Under Player Offsides (incl. overtime)", "Over Under Player Shots (incl. overtime)",
            "Over Under Player Shots On Goal (incl. overtime)", "Over Under Player Tackles (incl. overtime)",
            "Over Under Player To Be Carded (incl. overtime)", "Over Under Second Half", "Over Under Team 1", "Over Under Team 1 First Half",
            "Over Under Team 1 Second Half", "Over Under Team 2", "Over Under Team 2 First Half", "Over Under Team 2 Second Half",
            "Player Assists (incl. overtime)", "Player Fouls Committed (incl. overtime)", "Player Goalkeeper Saves (incl. overtime)",
            "Player Goals (incl. overtime)", "Player Offsides (incl. overtime)", "Player Shots (incl. overtime)",
            "Player Shots On Goal (incl. overtime)", "Player Tackles (incl. overtime)", "Player To Be Carded (incl. overtime)",
            "Player To Commit A Foul (incl. overtime)", "Second Half Result", "Team 1 Clean Sheet", "Team 1 Clean Sheet First Half",
            "Team 1 Clean Sheet Second Half", "Team 1 Exact Score First Half", "Team 1 Exact Score Full Time", "Team 1 Exact Score Second Half",
            "Team 1 Highest Scoring Half", "Team 1 No Bet", "Team 1 Odd Even", "Team 1 To Score", "Team 1 To Score First Half",
            "Team 1 To Score in Both Halves", "Team 1 To Score Second Half", "Team 1 To Win Both Halves", "Team 1 To Win Either Halves",
            "Team 1 Win To Nil", "Team 1 Win To Nil First Half", "Team 1 Win To Nil Second Half", "Team 2 Clean Sheet",
            "Team 2 Clean Sheet First Half", "Team 2 Clean Sheet Second Half", "Team 2 Exact Score First Half", "Team 2 Exact Score Full Time",
            "Team 2 Exact Score Second Half", "Team 2 Highest Scoring Half", "Team 2 No Bet", "Team 2 Odd Even", "Team 2 To Score",
            "Team 2 To Score First Half", "Team 2 To Score in Both Halves", "Team 2 To Score Second Half", "Team 2 To Win Both Halves",
            "Team 2 To Win Either Halves", "Team 2 Win To Nil", "Team 2 Win To Nil First Half", "Team 2 Win To Nil Second Half",
            "To Qualify", "To Win From Behind Full Time", "Winning Margin First Half", "Winning Margin Full Time", "Winning Margin Second Half"
        },
        [15] = new[]
        {
            "Anytime Goal Scorer", "Anytime Goal Scorer First Period", "Anytime Goal Scorer Second Period", "Anytime Goal Scorer Third Period",
            "Both Teams To Score", "Both Teams To Score (incl. overtime and penalties)", "Correct Score", "Correct Score (incl. overtime and penalties)",
            "European Handicap", "European Handicap (incl. overtime and penalties)", "Exact Goals", "Exact Goals (incl. overtime and penalties)",
            "First Goal", "First Goal (incl. overtime and penalties)", "First Goal Scorer", "First Goal Scorer First Period",
            "First Goal Scorer Second Period", "First Goal Scorer Third Period", "First Period Both Teams To Score", "First Period Correct Score",
            "First Period European Handicap", "First Period Exact Goals", "First Period First Goal", "First Period Handicap",
            "First Period Last Goal", "First Period Odd Even", "First Period Result", "First Period Team 1 Exact Goals",
            "First Period Team 1 To Score", "First Period Team 1 Total", "First Period Team 2 Exact Goals", "First Period Team 2 To Score",
            "First Period Team 2 Total", "First Period Total", "First Period Winner", "Handicap", "Handicap (incl. overtime and penalties)",
            "Highest Scoring Period", "Last Goal", "Last Goal (incl. overtime and penalties)", "Last Goal Scorer", "Next Goal Scorer",
            "Odd Even", "Odd Even (incl. overtime and penalties)", "Over Under Player Assists (incl. overtime)",
            "Over Under Player Blocks (incl. overtime)", "Over Under Player Goals (incl. overtime)",
            "Over Under Player Goals Against (incl. overtime)", "Over Under Player Goals First Period", "Over Under Player Goals Second Period",
            "Over Under Player Goals Third Period", "Over Under Player Points (incl. overtime)", "Over Under Player Points First Period",
            "Over Under Player Points Second Period", "Over Under Player Points Third Period",
            "Over Under Player Power Play Points (incl. overtime)", "Over Under Player Saves (incl. overtime)",
            "Over Under Player Shorthanded Goals (incl. overtime)", "Over Under Player Shots (incl. overtime)",
            "Over Under Player Shots On Goal (incl. overtime)", "Over Under Player Shots On Goal First Period",
            "Over Under Player Shots On Goal Second Period", "Over Under Player Shots On Goal Third Period",
            "Over Under Player Shutouts (incl. overtime)", "Player Assists (incl. overtime)", "Player Blocks (incl. overtime)",
            "Player Goals (incl. overtime)", "Player Goals Against (incl. overtime)", "Player Goals First Period", "Player Goals Second Period",
            "Player Goals Third Period", "Player Points (incl. overtime)", "Player Points First Period", "Player Points Second Period",
            "Player Points Third Period", "Player Power Play Points (incl. overtime)", "Player Saves (incl. overtime)",
            "Player Shorthanded Goals (incl. overtime)", "Player Shots (incl. overtime)", "Player Shots On Goal (incl. overtime)",
            "Player Shots On Goal First Period", "Player Shots On Goal Second Period", "Player Shots On Goal Third Period",
            "Player Shutouts (incl. overtime)", "Player To Commit A Penalty (incl. overtime)", "Player To Score A Shorthanded Goal",
            "Regular Time Result", "Second Goal Scorer", "Second Period Both Teams To Score", "Second Period Correct Score",
            "Second Period European Handicap", "Second Period Exact Goals", "Second Period First Goal", "Second Period Handicap",
            "Second Period Last Goal", "Second Period Odd Even", "Second Period Result", "Second Period Team 1 Exact Goals",
            "Second Period Team 1 To Score", "Second Period Team 1 Total", "Second Period Team 2 Exact Goals", "Second Period Team 2 To Score",
            "Second Period Team 2 Total", "Second Period Total", "Second Period Winner", "Team 1 Exact Goals",
            "Team 1 Exact Goals (incl. overtime and penalties)", "Team 1 To Score", "Team 1 To Score (incl. overtime and penalties)",
            "Team 1 Total", "Team 1 Total (incl. overtime and penalties)", "Team 1 Win To Nil",
            "Team 1 Win To Nil (incl. overtime and penalties)", "Team 2 Exact Goals", "Team 2 Exact Goals (incl. overtime and penalties)",
            "Team 2 To Score", "Team 2 To Score (incl. overtime and penalties)", "Team 2 Total",
            "Team 2 Total (incl. overtime and penalties)", "Team 2 Win To Nil", "Team 2 Win To Nil (incl. overtime and penalties)",
            "Third Goal Scorer", "Third Period Both Teams To Score", "Third Period Correct Score", "Third Period European Handicap",
            "Third Period Exact Goals", "Third Period First Goal", "Third Period Handicap", "Third Period Last Goal",
            "Third Period Odd Even", "Third Period Result", "Third Period Team 1 Exact Goals", "Third Period Team 1 To Score",
            "Third Period Team 1 Total", "Third Period Team 2 Exact Goals", "Third Period Team 2 To Score", "Third Period Team 2 Total",
            "Third Period Total", "Third Period Winner", "Total", "Total (incl. overtime and penalties)", "Will There Be Overtime",
            "Winner (incl. overtime and penalties)", "Winning Margin", "Winning Margin (incl. overtime and penalties)"
        },
        [12] = new[]
        {
            "Aces Draw No Bet", "Aces Handicap", "Aces Result", "Any Set To Nil", "Both Participants To Win a Set",
            "Breaks Draw No Bet", "Breaks Handicap", "Breaks Result", "Correct Score", "Correct Score Fifth Set",
            "Correct Score Fifth Set After Four Games", "Correct Score Fifth Set After Six Games", "Correct Score Fifth Set After Two Games",
            "Correct Score First Set", "Correct Score First Set After Four Games", "Correct Score First Set After Six Games",
            "Correct Score First Set After Two Games", "Correct Score Fourth Set", "Correct Score Fourth Set After Four Games",
            "Correct Score Fourth Set After Six Games", "Correct Score Fourth Set After Two Games", "Correct Score Second Set",
            "Correct Score Second Set After Four Games", "Correct Score Second Set After Six Games", "Correct Score Second Set After Two Games",
            "Correct Score Third Set", "Correct Score Third Set After Four Games", "Correct Score Third Set After Six Games",
            "Correct Score Third Set After Two Games", "Double Faults Draw No Bet", "Double Faults Handicap", "Double Faults Result",
            "Exact Aces", "Exact Double Faults", "Exact Sets", "Fifth Set Winner", "First Break", "First Set Winner",
            "Fourth Set Winner", "Game Correct Score", "Game Handicap", "Game Handicap Fifth Set", "Game Handicap First Set",
            "Game Handicap Fourth Set", "Game Handicap Second Set", "Game Handicap Third Set", "Game To Deuce", "Game Winner",
            "Most Games", "Odd Even Games", "Odd Even Games Fifth Set", "Odd Even Games First Set", "Odd Even Games Fourth Set",
            "Odd Even Games Second Set", "Odd Even Games Third Set", "Odd Even Points", "Odd Even Sets",
            "Participant 1 To Exactly Win One Set", "Participant 1 To Exactly Win Three Sets", "Participant 1 To Exactly Win Two Sets",
            "Participant 1 To Exactly Win Zero Sets", "Participant 1 To Win a Set", "Participant 1 To Win a Set To Nil",
            "Participant 1 To Win First Service Game", "Participant 1 Total Aces", "Participant 1 Total Break Points",
            "Participant 1 Total Breaks", "Participant 1 Total Double Faults", "Participant 1 Total Games",
            "Participant 1 Total Service Games", "Participant 2 To Exactly Win One Set", "Participant 2 To Exactly Win Three Sets",
            "Participant 2 To Exactly Win Two Sets", "Participant 2 To Exactly Win Zero Sets", "Participant 2 To Win a Set",
            "Participant 2 To Win a Set To Nil", "Participant 2 To Win First Service Game", "Participant 2 Total Aces",
            "Participant 2 Total Break Points", "Participant 2 Total Breaks", "Participant 2 Total Double Faults",
            "Participant 2 Total Games", "Participant 2 Total Service Games", "Point Handicap", "Race To Games",
            "Second Set Winner", "Set Handicap", "Third Set Winner", "Tie Break Fifth Set", "Tie Break First Set",
            "Tie Break Fourth Set", "Tie Break Second Set", "Tie Break Third Set", "To Win From Behind", "Total Aces",
            "Total Break Points", "Total Breaks", "Total Double Faults", "Total Duration", "Total Games Fifth Set",
            "Total Games First Set", "Total Games Fourth Set", "Total Games Over Under", "Total Games Second Set",
            "Total Games Third Set", "Total Points", "Total Service Games", "Total Sets Over Under", "Total Tie Breaks", "Winner"
        }
    };

    private static string GetPersistenceFilePath()
    {
        string baseDir = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir = Path.Combine(baseDir, ".aspnet", "BettingApp-Catalog");
        return Path.Combine(dir, "oddspapi-known-markets.json");
    }

    public static IReadOnlyList<string> DetectAndRecordNewMarkets(int sportId, IEnumerable<string> currentMarketNames, string? customFilePath = null)
    {
        if (currentMarketNames == null) return Array.Empty<string>();

        lock (_syncLock)
        {
            string filePath = customFilePath ?? GetPersistenceFilePath();
            var catalog = customFilePath != null
                ? LoadCatalogFromDisk(filePath)
                : (_knownMarketsBySport ??= LoadCatalogFromDisk(filePath));

            bool isNewSportNotInBaseline = !catalog.TryGetValue(sportId, out var knownSet);
            if (knownSet == null)
            {
                knownSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                catalog[sportId] = knownSet;
            }

            var newlyDiscovered = new List<string>();
            foreach (var raw in currentMarketNames)
            {
                string trimmed = (raw ?? "").Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (knownSet.Add(trimmed))
                {
                    // If this sport had a prior baseline/catalog, any added market is genuinely new.
                    if (!isNewSportNotInBaseline)
                    {
                        newlyDiscovered.Add(trimmed);
                    }
                }
            }

            if (newlyDiscovered.Count > 0 || isNewSportNotInBaseline)
            {
                SaveCatalogToDisk(filePath, catalog);
            }

            return newlyDiscovered.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    private static Dictionary<int, HashSet<string>> LoadCatalogFromDisk(string filePath)
    {
        var result = new Dictionary<int, HashSet<string>>();
        foreach (var kvp in BaselineMarkets)
        {
            result[kvp.Key] = new HashSet<string>(kvp.Value, StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                var persisted = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                if (persisted != null)
                {
                    foreach (var kvp in persisted)
                    {
                        if (int.TryParse(kvp.Key, out int sportId) && kvp.Value != null)
                        {
                            if (!result.TryGetValue(sportId, out var set))
                            {
                                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                result[sportId] = set;
                            }
                            foreach (var m in kvp.Value)
                            {
                                if (!string.IsNullOrWhiteSpace(m)) set.Add(m.Trim());
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Fall back gracefully to in-memory baseline if disk read fails
        }

        return result;
    }

    private static void SaveCatalogToDisk(string filePath, Dictionary<int, HashSet<string>> catalog)
    {
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var serializable = catalog.ToDictionary(
                k => k.Key.ToString(),
                v => v.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());

            string json = JsonSerializer.Serialize(serializable, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Ignore disk write errors in restricted environments
        }
    }
}
