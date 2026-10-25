using System;

namespace NewAgeQoL
{
    internal static class Kyiv
    {
        internal static DateTime Now => From(DateTime.UtcNow);

        internal static DateTime From(DateTime utc)
        {
            return utc.AddHours(Summer(utc) ? 3 : 2);
        }

        private static bool Summer(DateTime utc)
        {
            return utc >= Shift(utc.Year, 3) && utc < Shift(utc.Year, 10);
        }

        private static DateTime Shift(int year, int month)
        {
            var day = new DateTime(year, month, DateTime.DaysInMonth(year, month), 1, 0, 0, DateTimeKind.Utc);
            return day.AddDays(-(int)day.DayOfWeek);
        }
    }
}
