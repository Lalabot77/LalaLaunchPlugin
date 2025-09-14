using System;
using System.Globalization;
using System.Windows.Controls;

namespace LaunchPlugin
{
    public class LapTimeValidationRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            // Original behavior: empty/whitespace is invalid
            string timeString = value as string;
            if (string.IsNullOrWhiteSpace(timeString))
            {
                return new ValidationResult(false, "Lap time cannot be empty.");
            }

            try
            {
                // Keep the original "m:ss" shape enforcement
                var parts = timeString.Split(':');
                if (parts.Length != 2)
                {
                    return new ValidationResult(false, "Format must be m:ss(.f..fff).");
                }

                // Minutes must be a non-negative integer (original spirit)
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) || minutes < 0)
                {
                    return new ValidationResult(false, "Minutes must be a non-negative integer.");
                }

                // Seconds can be 0–59.999 with 0–3 decimals
                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                {
                    return new ValidationResult(false, "Invalid seconds format.");
                }
                if (seconds < 0 || seconds >= 60)
                {
                    return new ValidationResult(false, "Seconds must be between 0 and 59.999.");
                }

                // Final parse check (now accept 1, 2, or 3 decimals, or none)
                string[] formats =
                {
                    @"m\:ss\.fff",
                    @"m\:ss\.ff",
                    @"m\:ss\.f",
                    @"m\:ss"
                };

                foreach (var fmt in formats)
                {
                    if (TimeSpan.TryParseExact(timeString, fmt, CultureInfo.InvariantCulture, TimeSpanStyles.None, out _))
                    {
                        return ValidationResult.ValidResult;
                    }
                }

                return new ValidationResult(false, "Invalid time format (use m:ss, m:ss.f, m:ss.ff, or m:ss.fff).");
            }
            catch
            {
                // Keep your original catch/fallback behavior
                return new ValidationResult(false, "Invalid lap time.");
            }
        }
    }
    public class PositiveDoubleValidationRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                return new ValidationResult(false, "Value required.");

            if (double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                if (val > 0) return ValidationResult.ValidResult;
                return new ValidationResult(false, "Must be greater than zero.");
            }

            return new ValidationResult(false, "Invalid number format.");
        }
    }

}
