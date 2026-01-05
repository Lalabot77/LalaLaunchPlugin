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
                string key = sessionKey ?? string.Empty;

                // If we already started this session and haven't emitted the summary,
                // ignore repeated calls (prevents resetting and re-creating trace files).
                if (_greenSeen &&
                    !_summaryEmitted &&
                    string.Equals(key, _activeSessionKey, StringComparison.Ordinal))
                {
                    // Optional: refresh identity fields if they were blank at initial start
                    if (_summary != null)
                    {
                        if (string.IsNullOrWhiteSpace(_summary.CarIdentifier) && !string.IsNullOrWhiteSpace(carIdentifier)) _summary.CarIdentifier = carIdentifier;
                        if (string.IsNullOrWhiteSpace(_summary.TrackKey) && !string.IsNullOrWhiteSpace(trackKey)) _summary.TrackKey = trackKey;
                        if (string.IsNullOrWhiteSpace(_summary.PresetName) && !string.IsNullOrWhiteSpace(presetName)) _summary.PresetName = presetName;
                        if (string.IsNullOrWhiteSpace(_summary.SessionType) && !string.IsNullOrWhiteSpace(sessionType)) _summary.SessionType = sessionType;
                    }

                    return;
                }

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

                if (string.IsNullOrWhiteSpace(_activeTraceFile))
                {
                    bool hasCar = !string.IsNullOrWhiteSpace(carIdentifier);
                    bool hasTrack = !string.IsNullOrWhiteSpace(trackKey);

                    // If we don't have identity yet, leave it blank and let OnLapCrossed set it later.
                    if (hasCar && hasTrack)
                    {
                        _activeTraceFile = Logger.BuildTraceFilename(
                            Logger.ResolveTraceDirectory(string.Empty),
                            carIdentifier ?? string.Empty,
                            trackKey ?? string.Empty,
                            DateTime.UtcNow);
                    }
                }

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
    double afterZeroUsedSeconds,
    string carIdentifier,
    string trackKey,
    string presetName)
        {
            lock (Sync)
            {
                string key = sessionKey ?? string.Empty;

                if (!_greenSeen || _summaryEmitted || !string.Equals(key, _activeSessionKey, StringComparison.Ordinal))
                {
                    return;
                }

                if (lapNumber <= _lastLapWritten)
                {
                    return;
                }

                // ---- Identity refresh belongs HERE (not by re-calling OnRaceSessionStart) ----
                bool hasCar = !string.IsNullOrWhiteSpace(carIdentifier);
                bool hasTrack = !string.IsNullOrWhiteSpace(trackKey);
                bool hasPreset = !string.IsNullOrWhiteSpace(presetName);

                if (_summary == null)
                {
                    _summary = new SessionSummaryModel();
                }

                if (IsBlankOrUnknown(_summary.CarIdentifier) && hasCar)
                {
                    _summary.CarIdentifier = carIdentifier;
                }

                if (IsBlankOrUnknown(_summary.TrackKey) && hasTrack)
                {
                    _summary.TrackKey = trackKey;
                }

                if (IsBlankOrUnknown(_summary.PresetName) && hasPreset)
                {
                    _summary.PresetName = presetName;
                }

                // Build or repair the trace filename ONCE when identity becomes available.
                // - If blank: we haven't created it yet (startup-in-race early identity).
                // - If "Unknown_Unknown": we created too early and want to correct it deterministically.
                if ((string.IsNullOrWhiteSpace(_activeTraceFile) || IsUnknownTraceFile(_activeTraceFile)) && hasCar && hasTrack)
                {
                    _activeTraceFile = Logger.BuildTraceFilename(
                        Logger.ResolveTraceDirectory(string.Empty),
                        carIdentifier ?? string.Empty,
                        trackKey ?? string.Empty,
                        DateTime.UtcNow);
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

                // Logger has a circuit breaker: if _activeTraceFile is blank, it returns (no per-lap file spam).
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
        private static bool IsBlankOrUnknown(string value)
        {
            return string.IsNullOrWhiteSpace(value) ||
                   string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "n/a", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnknownTraceFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return true;
            }

            // Matches the sanitize fallback in SessionFileManager ("Unknown")
            return path.IndexOf("SessionTrace_Unknown_Unknown_", StringComparison.OrdinalIgnoreCase) >= 0;
        }

    }
}
