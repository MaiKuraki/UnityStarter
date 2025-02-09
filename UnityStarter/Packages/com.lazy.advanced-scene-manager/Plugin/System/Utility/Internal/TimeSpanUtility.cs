using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedSceneManager.Utility
{

    static class TimeSpanUtility
    {

        static readonly List<float> intervals = new List<float>
        {
            1,
            1000,
            60 * 1000,
            60 * 60 * 1000
        };

        static readonly List<string> units = new List<string>
        {
            "ms",
            "s",
            "min",
            "h"
        };

        public static string ToDisplayString(this TimeSpan timeSpan, string format = "#.###")
        {
            (string time, string unit) = ToDisplayString_Components(timeSpan, format);
            return string.Join(" ", time, unit);
        }

        public static string ToDisplayString(this float milliseconds, string format = "#.###")
        {
            (string time, string unit) = FormatUnits_Components(milliseconds, format);
            return string.Join(" ", time, unit);
        }

        public static (string time, string unit) ToDisplayString_Components(this TimeSpan timeSpan, string format = "#.###") =>
            FormatUnits_Components((float)timeSpan.TotalMilliseconds, format);

        public static (string time, string unit) FormatUnits_Components(float milliseconds, string format = "#.###")
        {

            var interval = intervals.LastOrDefault(i => i <= milliseconds);
            var index = intervals.IndexOf(interval);
            var unit = units[Math.Max(0, index)];

            var time = (milliseconds / Math.Max(interval, 1)).ToString(format);
            if (time.StartsWith("."))
                time = "0" + time;
            return (time, unit);

        }

    }

}
