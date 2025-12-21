using System;
using System.Collections.Generic;
using SimHub.Plugins;
using LaunchPlugin;

namespace LaunchPlugin.Messaging
{
    public interface ISignalProvider
    {
        bool TryGet<T>(string signalId, out T value);
    }

    public class SignalProvider : ISignalProvider
    {
        private readonly PluginManager _pluginManager;
        private readonly LalaLaunch _plugin;
        private readonly Dictionary<string, Func<object>> _accessors;

        public SignalProvider(PluginManager pluginManager, LalaLaunch plugin)
        {
            _pluginManager = pluginManager;
            _plugin = plugin;
            _accessors = BuildAccessors();
        }

        public bool TryGet<T>(string signalId, out T value)
        {
            value = default;

            if (string.IsNullOrWhiteSpace(signalId)) return false;
            if (!_accessors.TryGetValue(signalId, out var getter)) return false;

            try
            {
                var raw = getter?.Invoke();
                if (raw == null) return false;

                if (raw is T direct)
                {
                    value = direct;
                    return true;
                }

                try
                {
                    value = (T)Convert.ChangeType(raw, typeof(T));
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private Dictionary<string, Func<object>> BuildAccessors()
        {
            return new Dictionary<string, Func<object>>(StringComparer.OrdinalIgnoreCase)
            {
                // Fuel signals (PluginCalc)
                { "FuelDeltaL_Current", () => _plugin?.Fuel_Delta_LitresCurrent },
                { "FuelLapsRemaining", () => _plugin?.LiveLapsRemainingInRace },
                { "FuelDeltaLaps", () => _plugin?.DeltaLaps },
                { "FuelCanPush", () => _plugin?.CanAffordToPush },
                { "PitWindowOpen", () => _plugin?.IsPitWindowOpen },

                // Rejoin
                { "RejoinThreatLevel", () => (int)(_plugin?.CurrentRejoinThreat ?? ThreatLevel.CLEAR) },
                { "RejoinReasonCode", () => (int)(_plugin?.CurrentRejoinReason ?? RejoinReason.None) },
                { "RejoinTimeToThreat", () => _plugin?.CurrentRejoinTimeToThreat ?? double.NaN },

                // Flags and sessions (SimHub properties)
                { "FlagSessionFlags", () => _pluginManager?.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.SessionFlags") },
                { "PaceMode", () => _pluginManager?.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PaceMode") },
                { "CompletedLaps", () => _pluginManager?.GetPropertyValue("DataCorePlugin.GameData.CompletedLaps") },
                { "PitServiceFuelDone", () => ReadPitServiceFuelDone() },

                // Traffic / iRacingExtraProperties
                { "TrafficBehindGapSeconds", () => _pluginManager?.GetPropertyValue("IRacingExtraProperties.iRacing_DriverBehind_00_RelativeGapToPlayer") },
                { "TrafficBehindDistanceM", () => _pluginManager?.GetPropertyValue("IRacingExtraProperties.iRacing_DriverBehind_00_DistanceToPlayer") },
                { "TrafficBehindClass", () => _pluginManager?.GetPropertyValue("IRacingExtraProperties.iRacing_DriverBehind_00_ClassName") },
                { "PlayerClassName", () => _pluginManager?.GetPropertyValue("IRacingExtraProperties.iRacing_Player_ClassName") },
                { "DriverAheadGapSeconds", () => _pluginManager?.GetPropertyValue("IRacingExtraProperties.iRacing_DriverAhead_00_RelativeGapToPlayer") },

                // Pace / incident
                { "PlayerPaceLast5LapAvg", () => _plugin?.Pace_Last5LapAvgSec },
                { "PlayerClassPosition", () => _pluginManager?.GetPropertyValue("DataCorePlugin.GameData.PositionInClass") },
                { "IncidentCount", () => _pluginManager?.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PlayerCarDriverIncidentCount") },
                { "SlowDownTimeRemaining", () => _pluginManager?.GetPropertyValue("IRacingExtraProperties.iRacing_SlowDownTime") },
                { "IncidentAheadWarning", () => false } // placeholder until implemented
            };
        }

        private bool ReadPitServiceFuelDone()
        {
            try
            {
                var raw = _pluginManager?.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PlayerCarPitSvFlags");
                if (raw == null) return false;
                int flags = Convert.ToInt32(raw);
                // irsdk PitSvFlags.fuel_fill == 0x10
                return (flags & 0x10) != 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
