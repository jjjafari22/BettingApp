using System;

namespace BettingApp.Data
{
    public static class TimeHelpers
    {
        public static DateTime GetNorwayTime(DateTime utcTime)
        {
            if (utcTime == DateTime.MinValue || utcTime == DateTime.MaxValue) return utcTime;
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
                return TimeZoneInfo.ConvertTimeFromUtc(utcTime, tz);
            }
            catch
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
                    return TimeZoneInfo.ConvertTimeFromUtc(utcTime, tz);
                }
                catch
                {
                    return utcTime.AddHours(1);
                }
            }
        }
    }
}