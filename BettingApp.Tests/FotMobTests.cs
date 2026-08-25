using System;
using Xunit;
using BettingApp.Services;

namespace BettingApp.Tests
{
    public class FotMobTests
    {
        [Theory]
        [InlineData("Real Betis", "Osasuna", "Real Madrid", "Osasuna", false)] // Sibling Clash Real
        [InlineData("Manchester United", "Arsenal", "Manchester City", "Arsenal", false)] // Sibling Clash Manchester
        [InlineData("Wolverhampton Wanderers", "Chelsea", "Wolverhampton", "Chelsea", true)] // Verbose query
        [InlineData("West Ham", "Charlton Athletic", "West Ham United U21", "Charlton Athletic", false)] // U21 penalty
        [InlineData("West Ham", "Charlton Athletic", "West Ham", "Bournemouth", false)] // Away team mismatch
        [InlineData("Athletic Bilbao", "Sevilla", "Athletic Club", "Sevilla", true)] // Alias Bilbao
        [InlineData("Inter Milan", "Juventus", "Internazionale", "Juventus", true)] // Alias Inter
        [InlineData("Arsenal", "Man City", "Arsenal", "Manchester City", true)] // Alias Man City
        [InlineData("Spurs", "Chelsea", "Tottenham Hotspur", "Chelsea", true)] // Alias Spurs
        [InlineData("Man Utd", "Liverpool", "Manchester United", "Liverpool", true)] // Alias Man Utd
        [InlineData("Wolves", "Everton", "Wolverhampton Wanderers", "Everton", true)] // Alias Wolves
        [InlineData("FC Copenhagen", "Brondby", "FC København", "Brondby", true)] // Alias Copenhagen
        [InlineData("West Ham", "Charlton Athletic", "Charlton Athletic", "West Ham", true)] // Reversed order
        public void Test_AreTeamsMatching(string qHome, string qAway, string oHome, string oAway, bool expected)
        {
            bool result = FotMobScraperService.AreTeamsMatching(qHome, qAway, oHome, oAway);
            Assert.Equal(expected, result);
        }
    }
}
