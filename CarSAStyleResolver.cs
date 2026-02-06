using System;
using System.Collections.Generic;

namespace LaunchPlugin
{
    public sealed class CarSAStyleResult
    {
        public string StatusBgHex { get; set; } = "#000000";
        public string BorderMode { get; set; } = "DEF";
        public string BorderHex { get; set; } = "#A9A9A9";
    }

    public static class CarSAStyleResolver
    {
        public const string BorderModeTeam = "TEAM";
        public const string BorderModeLead = "LEAD";
        public const string BorderModeOtherClass = "OCLS";
        public const string BorderModeDefault = "DEF";

        public static CarSAStyleResult Resolve(
            int statusE,
            string classColorHex,
            bool isTeammate,
            bool isClassLeader,
            bool isOtherClass,
            IDictionary<int, string> statusEColorMap,
            IDictionary<string, string> borderColorMap)
        {
            var borderMode = ResolveBorderMode(isTeammate, isClassLeader, isOtherClass);
            return new CarSAStyleResult
            {
                StatusBgHex = ResolveStatusBackgroundHex(statusE, classColorHex, statusEColorMap),
                BorderMode = borderMode,
                BorderHex = ResolveBorderHex(borderMode, borderColorMap)
            };
        }

        private static string ResolveStatusBackgroundHex(int statusE, string classColorHex, IDictionary<int, string> statusEColorMap)
        {
            if ((statusE == (int)CarSAStatusE.FasterClass || statusE == (int)CarSAStatusE.SlowerClass) && IsValidHexColor(classColorHex))
            {
                return classColorHex;
            }

            if (statusEColorMap != null && statusEColorMap.TryGetValue(statusE, out var color) && IsValidHexColor(color))
            {
                return color;
            }

            if (statusEColorMap != null && statusEColorMap.TryGetValue((int)CarSAStatusE.Unknown, out var fallback) && IsValidHexColor(fallback))
            {
                return fallback;
            }

            return "#000000";
        }

        private static string ResolveBorderMode(bool isTeammate, bool isClassLeader, bool isOtherClass)
        {
            if (isTeammate)
            {
                return BorderModeTeam;
            }

            if (isClassLeader)
            {
                return BorderModeLead;
            }

            if (isOtherClass)
            {
                return BorderModeOtherClass;
            }

            return BorderModeDefault;
        }

        private static string ResolveBorderHex(string borderMode, IDictionary<string, string> borderColorMap)
        {
            if (borderColorMap != null && !string.IsNullOrWhiteSpace(borderMode) &&
                borderColorMap.TryGetValue(borderMode, out var color) && IsValidHexColor(color))
            {
                return color;
            }

            if (borderColorMap != null &&
                borderColorMap.TryGetValue(BorderModeDefault, out var fallback) && IsValidHexColor(fallback))
            {
                return fallback;
            }

            return "#A9A9A9";
        }

        public static bool IsValidHexColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 7 || value[0] != '#')
            {
                return false;
            }

            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                bool isHex = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
