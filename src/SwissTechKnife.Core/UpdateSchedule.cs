using System;
using System.Globalization;

namespace SwissTechKnife.Core
{
    /// <summary>When the catalog should be checked again for updates, from the user's frequency setting.</summary>
    public static class UpdateSchedule
    {
        public const string Daily = "daily";
        public const string Weekly = "weekly";
        public const string Monthly = "monthly";
        public const string Never = "never";

        public static string Normalize(string frequency)
        {
            switch (frequency)
            {
                case Weekly:
                case Monthly:
                case Never:
                    return frequency;
                default:
                    return Daily;
            }
        }

        /// <summary>Null for "never".</summary>
        public static TimeSpan? Interval(string frequency)
        {
            switch (Normalize(frequency))
            {
                case Weekly: return TimeSpan.FromDays(7);
                case Monthly: return TimeSpan.FromDays(30);
                case Never: return null;
                default: return TimeSpan.FromDays(1);
            }
        }

        public static DateTime? ParseUtc(string text) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime value) ? value : (DateTime?)null;

        /// <summary>True when an automatic check should run now.</summary>
        public static bool IsDue(string frequency, string lastCheckUtc, DateTime nowUtc)
        {
            TimeSpan? interval = Interval(frequency);
            if (interval == null)
            {
                return false;
            }
            DateTime? last = ParseUtc(lastCheckUtc);
            return last == null || nowUtc - last.Value >= interval.Value || last.Value > nowUtc.AddMinutes(5);
        }
    }
}
