using System;
using System.Globalization;

namespace LaunchPlugin
{
    internal static class TrackLengthHelper
    {
        /// <summary>
        /// Parse a WeekendInfo.TrackLength-style value into kilometers.
        /// Accepts numeric types, plain numeric strings, or strings with "km"/"mi" suffixes.
        /// </summary>
        internal static double ParseTrackLengthKm(object rawValue, double fallbackKm = double.NaN)
        {
            if (rawValue == null) return fallbackKm;

            try
            {
                switch (rawValue)
                {
                    case double d: return d;
                    case float f: return f;
                    case int i: return i;
                    case long l: return l;
                    case decimal m: return (double)m;
                }

                string s = Convert.ToString(rawValue);
                if (string.IsNullOrWhiteSpace(s)) return fallbackKm;

                s = s.Trim().ToLowerInvariant().Replace(",", ".");

                if (s.EndsWith("km"))
                {
                    var body = s.Replace("km", "").Trim();
                    if (double.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out var kmVal))
                        return kmVal;
                }
                else if (s.EndsWith("mi"))
                {
                    var body = s.Replace("mi", "").Trim();
                    if (double.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out var miVal))
                        return miVal * 1.609344;
                }
                else
                {
                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
                        return numeric;
                }
            }
            catch
            {
                // fall through to fallback
            }

            return fallbackKm;
        }
    }
}
