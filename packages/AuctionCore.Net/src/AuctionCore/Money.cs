using System;
using System.Globalization;

namespace Apl.Core
{
    /// <summary>All money is whole lakhs (1 Cr = 100 L).</summary>
    public static class Money
    {
        public const int LakhsPerCrore = 100;

        /// <summary>"₹2 Cr", "₹12.25 Cr", "₹75 L".</summary>
        public static string Format(int lakhs)
        {
            var sign = lakhs < 0 ? "-" : "";
            var abs = Math.Abs(lakhs);
            if (abs < LakhsPerCrore) return sign + "₹" + abs + " L";
            var text = (abs / (double)LakhsPerCrore).ToString("0.##", CultureInfo.InvariantCulture);
            return sign + "₹" + text + " Cr";
        }
    }
}
