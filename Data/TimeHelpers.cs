using System;

namespace BettingApp.Data
{
    public static class TimeHelpers
    {
        public static TimeZoneInfo GetNorwayTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
            }
            catch
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
                }
                catch
                {
                    // Fallback to UTC if both IDs fail on this OS
                    return TimeZoneInfo.Utc;
                }
            }
        }

        public static DateTime GetNorwayTime(DateTime utcTime)
        {
            if (utcTime == DateTime.MinValue || utcTime == DateTime.MaxValue) return utcTime;
            var tz = GetNorwayTimeZone();
            if (tz.Id == "UTC") return utcTime.AddHours(1); // Crude +1 hr fallback if timezone is completely broken
            return TimeZoneInfo.ConvertTimeFromUtc(utcTime, tz);
        }

        public static DateTime GetUtcFromNorwayTime(DateTime norwayTime)
        {
            if (norwayTime == DateTime.MinValue || norwayTime == DateTime.MaxValue) return norwayTime;
            var tz = GetNorwayTimeZone();
            if (tz.Id == "UTC") return norwayTime.AddHours(-1); // Crude fallback
            
            // Ensure DateTimeKind is Unspecified before converting to UTC
            if (norwayTime.Kind != DateTimeKind.Unspecified)
            {
                norwayTime = DateTime.SpecifyKind(norwayTime, DateTimeKind.Unspecified);
            }
            return TimeZoneInfo.ConvertTimeToUtc(norwayTime, tz);
        }
    }
}