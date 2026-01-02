using System;

namespace LaunchPlugin
{
    internal static class SessionSummaryRuntime
    {
        private static readonly object Sync = new object();
        private static readonly SessionSummaryLogger Logger = new SessionSummaryLogger(new SessionFileManager());

        private static bool _greenSeen;
        private static bool _summaryEmitted;
        private static string _activeSessionKey = string.Empty;
        private static string _activeTraceFile = string.Empty;
        private static int _lastLapWritten;
        private static SessionSummaryModel _summary = new SessionSummaryModel();

        public static void OnRaceSessionStart(
            string sessionKey,
            string sessionType,
            string carIdentifier,
            string trackKey,
            string presetName,
            FuelCalcs planner,
            bool isReplay,
            double fuelAtGreen)
        {
            lock (Sync)
            {
                _activeSessionKey = sessionKey ?? string.Empty;
                _greenSeen = true;
                _summaryEmitted = false;
                _lastLapWritten = 0;

                var snapshot = BuildPlannerSnapshot(planner, presetName, carIdentifier, trackKey, sessionType);

                _summary = new SessionSummaryModel
                {
                    CarIdentifier = carIdentifier ?? string.Empty,
                    TrackKey = trackKey ?? string.Empty,
                    PresetName = presetName ?? string.Empty,
                    SessionType = sessionType ?? string.Empty,
                    PlannerSnapshot = snapshot,
                    GreenSeen = true,
                    CheckeredSeen = false,
                    IsReplay = isReplay
                };

                _activeTraceFile = Logger.BuildTraceFilename(
                    Logger.ResolveTraceDirectory(string.Empty),
                    carIdentifier ?? string.Empty,
                    trackKey ?? string.Empty,
                    DateTime.UtcNow);
            }
        }

        public static void OnLapCrossed(
            string sessionKey,
            int lapNumber,
            TimeSpan lapTime,
            double fuelRemaining,
            double stableFuelPerLap,
            int fuelConfidence,
            double lapsRemainingEstimate,
            int? pitStopIndex,
            string pitStopPhase,
            double afterZeroUsedSeconds)
        {
            lock (Sync)
            {
                if (!_greenSeen || _summaryEmitted || !string.Equals(sessionKey ?? string.Empty, _activeSessionKey, StringComparison.Ordinal))
                {
                    return;
                }

                if (lapNumber <= _lastLapWritten)
                {
                    return;
                }

                _lastLapWritten = lapNumber;

                var lapRow = new SessionTraceLapRow
                {
                    LapNumber = lapNumber,
                    LapTime = lapTime,
                    FuelRemaining = fuelRemaining,
                    StableFuelPerLap = stableFuelPerLap,
                    FuelConfidence = fuelConfidence,
                    LapsRemainingEstimate = lapsRemainingEstimate,
                    PitStopIndex = pitStopIndex,
                    PitStopPhase = pitStopPhase ?? string.Empty,
                    AfterZeroUsageSeconds = afterZeroUsedSeconds
                };

                Logger.AppendLapTraceRows(new[] { lapRow }, string.Empty, _activeTraceFile);
            }
        }

        public static void OnDriverCheckered(
            string sessionKey,
            int completedLaps,
            double fuelRemaining,
            double observedAfterZeroSeconds,
            int? pitStopsDone,
            bool isReplay)
        {
            lock (Sync)
            {
                if (_summaryEmitted || !string.Equals(sessionKey ?? string.Empty, _activeSessionKey, StringComparison.Ordinal))
                {
                    return;
                }

                if (_summary == null)
                {
                    _summary = new SessionSummaryModel();
                }

                _summary.GreenSeen = _greenSeen;
                _summary.CheckeredSeen = true;
                _summary.IsReplay = isReplay;
                _summary.ActualLapsCompleted = completedLaps;
                _summary.ActualPitStops = pitStopsDone;
                _summary.ActualAfterZeroSeconds = observedAfterZeroSeconds;
                _summary.ActualFuelUsed = null;

                Logger.AppendSummaryRow(_summary, string.Empty);
                _summaryEmitted = true;
            }
        }

        private static SessionPlannerSnapshot BuildPlannerSnapshot(
            FuelCalcs planner,
            string presetName,
            string carIdentifier,
            string trackKey,
            string sessionType)
        {
            if (planner == null)
            {
                return new SessionPlannerSnapshot
                {
                    PresetName = presetName ?? string.Empty,
                    CarIdentifier = carIdentifier ?? string.Empty,
                    TrackKey = trackKey ?? string.Empty,
                    SessionType = sessionType ?? string.Empty
                };
            }

            return new SessionPlannerSnapshot
            {
                PresetName = presetName ?? string.Empty,
                CarIdentifier = carIdentifier ?? string.Empty,
                TrackKey = trackKey ?? string.Empty,
                SessionType = sessionType ?? string.Empty,
                PlannerFuelPerLap = planner.FuelPerLap,
                PlannerLapTime = TimeSpan.Zero,
                TotalFuelRequired = planner.TotalFuelNeeded,
                PlannedPitStops = planner.RequiredPitStops,
                PlannedAfterZeroAllowance = planner.StrategyDriverExtraSecondsAfterZero,
                CondensedStintPlanSummary = string.Empty
            };
        }

        private static string BuildSummaryTraceLine(SessionSummaryModel summary)
        {
            return $"schema=v1 session_type={summary.SessionType ?? string.Empty} car={summary.CarIdentifier ?? string.Empty} track={summary.TrackKey ?? string.Empty} laps={summary.ActualLapsCompleted?.ToString() ?? string.Empty} pit_stops={summary.ActualPitStops?.ToString() ?? string.Empty} after0_s={summary.ActualAfterZeroSeconds?.ToString("F1") ?? string.Empty} fuel_used_l=n/a";
        }
    }
}
