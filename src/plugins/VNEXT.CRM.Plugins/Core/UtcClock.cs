using System;

namespace VNEXT.CRM.Plugins.Core
{
    public static class UtcClock
    {
        public static DateTime NowUtc()
        {
            return DateTime.UtcNow;
        }

        public static DateTime TodayUtc(DateTime utcInstant)
        {
            var utc = EnsureUtc(utcInstant);
            return new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc);
        }

        public static DateTime NormalizeDateOnly(DateTime value)
        {
            if (value.Kind == DateTimeKind.Local)
            {
                var utc = value.ToUniversalTime();
                return new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc);
            }

            return new DateTime(value.Year, value.Month, value.Day, 0, 0, 0, DateTimeKind.Utc);
        }

        public static DateTime EnsureUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
            {
                return value;
            }

            if (value.Kind == DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }

            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
    }
}
