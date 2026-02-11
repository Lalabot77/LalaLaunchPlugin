using System;
using System.Collections.Generic;

namespace LaunchPlugin
{
    public static class CarSAStyleResolver
    {
        public const string BorderModeFriend = "FRIEND";
        public const string BorderModeTeam = "TEAM";
        public const string BorderModeLead = "LEAD";
        public const string BorderModeBad = "BAD";
        public const string BorderModeOtherClass = "OCLS";
        public const string BorderModeDefault = "DEF";
        private const string FriendBorderHex = "#00FF00";

        public static void Resolve(
            int statusE,
            string classColorHex,
            bool isFriend,
            bool isManualTeammate,
            bool isTelemetryTeammate,
            bool isBad,
            bool isClassLeader,
            bool isOtherClass,
            IDictionary<int, string> statusEColorMap,
            IDictionary<string, string> borderColorMap,
            out string statusBgHex,
            out string borderMode,
            out string borderHex)
        {
            borderMode = ResolveBorderMode(isFriend, isManualTeammate, isTelemetryTeammate, isBad, isClassLeader, isOtherClass);
            statusBgHex = ResolveStatusBackgroundHex(statusE, classColorHex, statusEColorMap);
            borderHex = ResolveBorderHex(borderMode, borderColorMap);
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

        private static string ResolveBorderMode(bool isFriend, bool isManualTeammate, bool isTelemetryTeammate, bool isBad, bool isClassLeader, bool isOtherClass)
        {
            if (isManualTeammate)
            {
                return BorderModeTeam;
            }

            if (isBad)
            {
                return BorderModeBad;
            }

            if (isFriend)
            {
                return BorderModeFriend;
            }

            if (isTelemetryTeammate)
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
            if (string.Equals(borderMode, BorderModeFriend, StringComparison.OrdinalIgnoreCase))
            {
                return FriendBorderHex;
            }

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
