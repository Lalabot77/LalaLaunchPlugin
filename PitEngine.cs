using GameReaderCommon;
using Newtonsoft.Json;
using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace LaunchPlugin
{
    public class PitEngine
    {
        private const double MinTrackLengthM = 500.0;
        private const double MaxTrackLengthM = 20000.0;

        // --- Public surface (used by MSG.PitPhaseDebug) ---
        public PitPhase CurrentPitPhase { get; private set; } = PitPhase.None;
        public bool IsOnPitRoad { get; private set; } = false;
        // Live while in lane; frozen (latched) after exit until next pit event
        public TimeSpan TimeOnPitRoad => _pitRoadTimer.IsRunning ? _pitRoadTimer.Elapsed : _lastTimeOnPitRoad;
        public TimeSpan PitStopDuration => _lastPitStopDuration;

        public double PitStopElapsedSec
        {
            get
            {
                if (_pitStopTimer != null && _pitStopTimer.IsRunning)
                    return _pitStopTimer.Elapsed.TotalSeconds;

                return _lastPitStopDuration.TotalSeconds; // 0 if we’ve never had a stop
            }
        }

        // --- Public properties for our calculated time loss values ---
        public double LastDirectTravelTime { get; private set; } = 0.0;
        public double LastTotalPitCycleTimeLoss { get; private set; } = 0.0;
        public double LastPaceDeltaNetLoss { get; private set; } = 0.0;

        // --- Event to notify when the Pace Delta calculation is complete and valid ---
        public event Action<double, string> OnValidPitStopTimeLossCalculated;

        // --- Timers/State mirrors of the rejoin engine ---
        private readonly Stopwatch _pitExitTimer = new Stopwatch();   // shows ExitingPits for a short time after lane exit
        private readonly Stopwatch _pitRoadTimer = new Stopwatch();   // time spent in pit lane
        private readonly Stopwatch _pitStopTimer = new Stopwatch();   // time spent in stall
        private TimeSpan _lastPitStopDuration = TimeSpan.Zero;
        private TimeSpan _lastTimeOnPitRoad = TimeSpan.Zero; // <-- NEW (latched tPit)
        // --- Decel Calcs ---
        public double ConfigPitEntryDecelMps2 { get; set; } = 13.5;
        public double ConfigPitEntryBufferM { get; set; } = 15.0;
        // --- Pit Entry Assist outputs (for dash) ---
        public bool PitEntryAssistActive { get; private set; } = false;
        public double PitEntryDistanceToLine_m { get; private set; } = 0.0;
        public double PitEntryRequiredDistance_m { get; private set; } = 0.0;
        public double PitEntryMargin_m { get; private set; } = 0.0;
        public int PitEntryCue { get; private set; } = 0; // 0 Off, 1 OK, 2 BrakeSoon, 3 BrakeNow, 4 Late

        public double PitEntrySpeedDelta_kph { get; private set; } = 0.0;
        public double PitEntryDecelProfile_mps2 { get; private set; } = 0.0;
        public double PitEntryBuffer_m { get; private set; } = 0.0;
        private bool _pitEntryAssistWasActive;
        private bool _pitEntryFirstCompliantCaptured;
        private double _pitEntryFirstCompliantDToLine_m;


        // --- State management for the Pace Delta calculation ---

        public enum PaceDeltaState { Idle, AwaitingPitLap, AwaitingOutLap, Complete }
        private PaceDeltaState _paceDeltaState = PaceDeltaState.Idle;
        public PaceDeltaState CurrentState => _paceDeltaState;
        private double _avgPaceAtPit = 0.0;
        private double _pitLapSeconds = 0.0; // stores the actual pit lap (includes stop)

        private bool _wasInPitLane = false;
        private bool _wasInPitStall = false;
        private string _trackMarkersLastKey = "unknown";
        private readonly Dictionary<string, TrackMarkerRecord> _trackMarkerStore = new Dictionary<string, TrackMarkerRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TrackMarkerSessionState> _trackMarkerSessionState = new Dictionary<string, TrackMarkerSessionState>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<TrackMarkerTriggerEvent> _trackMarkerTriggers = new Queue<TrackMarkerTriggerEvent>();
        private bool _trackMarkersLoaded = false;

        private const double TrackMarkerDeltaTolerancePct = 0.001; // 0.1%
        private const double TrackLengthChangeThresholdM = 50.0;

        private readonly Func<double> _getLingerTime;
        public PitEngine() : this(null) { }
        public PitEngine(Func<double> getLingerTime)
        {
            _getLingerTime = getLingerTime;
            EnsureTrackMarkerStoreLoaded();
        }

        public void Reset()
        {
            CurrentPitPhase = PitPhase.None;
            IsOnPitRoad = false;

            _pitExitTimer.Reset();
            _pitRoadTimer.Reset();
            _pitStopTimer.Reset();
            _lastPitStopDuration = TimeSpan.Zero;

            _wasInPitLane = false;
            _wasInPitStall = false;

            // --- NEW: Reset new state properties ---
            LastDirectTravelTime = 0.0;
            LastTotalPitCycleTimeLoss = 0.0;
            _paceDeltaState = PaceDeltaState.Idle;
            _avgPaceAtPit = 0.0;
            _lastTimeOnPitRoad = TimeSpan.Zero;
            _trackMarkersLastKey = "unknown";
            _trackMarkerSessionState.Clear();
            _trackMarkerTriggers.Clear();
        }

        public string PitEntryCueText
        {
            get
            {
                switch (PitEntryCue)
                {
                    case 1: return "OK";
                    case 2: return "BRAKE SOON";
                    case 3: return "BRAKE NOW";
                    case 4: return "LATE";
                    default: return "OFF";
                }
            }
        }

        public void Update(GameData data, PluginManager pluginManager)
        {
            bool isInPitLane = data.NewData.IsInPitLane != 0;
            bool isInPitStall = Convert.ToBoolean(
                pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PlayerCarInPitStall") ?? false);
            string trackKey = GetCanonicalTrackKey(pluginManager);
            _trackMarkersLastKey = trackKey;
            var sessionState = GetSessionState(trackKey, create: true);
            double carPct = NormalizeTrackPercent(data?.NewData?.TrackPositionPercent ?? double.NaN);

            if (double.IsNaN(sessionState.SessionStartTrackLengthM))
            {
                double trackLenKm = TrackLengthHelper.ParseTrackLengthKm(
                    pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.WeekendInfo.TrackLength"),
                    double.NaN);
                if (!double.IsNaN(trackLenKm))
                {
                    double lenM = trackLenKm * 1000.0;
                    sessionState.SessionStartTrackLengthM = lenM;
                    sessionState.SessionTrackLengthM = lenM;
                }
            }

            double trackLenM = sessionState.SessionTrackLengthM;

            bool justExitedPits = _wasInPitLane && !isInPitLane;
            if (justExitedPits)
            {
                _pitExitTimer.Restart();

                // --- NEW: Calculate the Direct Travel Time on pit exit ---
                // This is the "Direct Stopwatch" method.
                if (_pitRoadTimer.IsRunning)
                {
                    _lastTimeOnPitRoad = _pitRoadTimer.Elapsed; // <-- NEW: latch tPit

                    double direct = _pitRoadTimer.Elapsed.TotalSeconds - _lastPitStopDuration.TotalSeconds;

                    // Discard impossible values (e.g., sitting in pits, resets, telemetry oddities)
                    if (direct < 0 || direct > 300)
                    {
                        SimHub.Logging.Current.Warn($"[LalaPlugin:Pit Cycle] Ignoring invalid Direct Travel Time ({direct:F2}s)");
                    }
                    else
                    {
                        LastDirectTravelTime = direct;
                        SimHub.Logging.Current.Info(
                            $"[LalaPlugin:Pit Cycle] Direct lane travel computed -> lane={_lastTimeOnPitRoad.TotalSeconds:F2}s, stop={_lastPitStopDuration.TotalSeconds:F2}s, direct={LastDirectTravelTime:F2}s");
                    }
                }
            }

            double linger = _getLingerTime != null ? Math.Max(0.5, _getLingerTime()) : 3.0;
            if (_pitExitTimer.IsRunning && _pitExitTimer.Elapsed.TotalSeconds >= linger)
                _pitExitTimer.Reset();

            double speedKph = data?.NewData?.SpeedKmh ?? 0.0;

            if (isInPitLane)
            {
                if (!_pitRoadTimer.IsRunning)
                {
                    _pitRoadTimer.Restart();
                    // --- Reset last stop duration on entry to prevent using stale data for drive-throughs ---
                    _lastPitStopDuration = TimeSpan.Zero;
                }
                // IGNORE early pit-outs before any valid racing lap
                var lapsCompleted = data?.NewData?.CompletedLaps ?? 0;
                if (lapsCompleted < 1)
                {
                    UpdateTrackMarkers(trackKey, carPct, trackLenM, isInPitLane, justExitedPits, isInPitStall, speedKph);
                    _paceDeltaState = PaceDeltaState.Idle;
                    IsOnPitRoad = isInPitLane;
                    _wasInPitLane = isInPitLane;
                    _wasInPitStall = isInPitStall;
                    return;
                }
            }
            else
            {
                if (_pitRoadTimer.IsRunning) _pitRoadTimer.Reset();
            }

            if (isInPitStall && !_wasInPitStall)
            {
                _pitStopTimer.Restart();
            }
            else if (!isInPitStall && _wasInPitStall)
            {
                _pitStopTimer.Stop();
                _lastPitStopDuration = _pitStopTimer.Elapsed;

                // --- NEW: Add validation check for our internal tStop timer ---
                object stopTimeProp = pluginManager.GetPropertyValue("DataCorePlugin.GameData.LastPitStopDuration");
                TimeSpan simhubStopTime = (stopTimeProp is TimeSpan span)
                    ? span
                    : TimeSpan.FromSeconds(Convert.ToDouble(stopTimeProp ?? 0.0));
                SimHub.Logging.Current.Debug($"[LalaPlugin:Pit Cycle] Stop Time Validation -> Internal: {_lastPitStopDuration.TotalSeconds:F2}s, SimHub: {simhubStopTime.TotalSeconds:F2}s");

                _pitStopTimer.Reset();
            }

            // --- Store the previous phase before updating to the new one ---
            //var previousPhase = CurrentPitPhase;
            UpdatePitPhase(data, pluginManager);
           
            UpdatePitEntryAssist(data, pluginManager, ConfigPitEntryDecelMps2, ConfigPitEntryBufferM);
            UpdateTrackMarkers(trackKey, carPct, trackLenM, isInPitLane, justExitedPits, isInPitStall, speedKph);

            // If we have just left the pits, start waiting for the out-lap.
            if (justExitedPits)
            {
                // Only arm if we've actually started racing
                var lapsCompleted = data?.NewData?.CompletedLaps ?? 0;
                if (lapsCompleted >= 1)
                {
                    SimHub.Logging.Current.Info(
                        $"[LalaPlugin:Pit Cycle] Pit exit detected – lane={_lastTimeOnPitRoad.TotalSeconds:F2}s, stop={_lastPitStopDuration.TotalSeconds:F2}s, direct={LastDirectTravelTime:F2}s. Awaiting pit-lap completion.");
                    _paceDeltaState = PaceDeltaState.AwaitingPitLap;
                    _pitLapSeconds = 0.0;
                }
                else
                {
                    _paceDeltaState = PaceDeltaState.Idle;
                }
            }


            IsOnPitRoad = isInPitLane;
            _wasInPitLane = isInPitLane;
            _wasInPitStall = isInPitStall;
        }

        private void UpdatePitEntryAssist(GameData data, PluginManager pluginManager, double profileDecel_mps2, double profileBuffer_m)
        {
            // Clamp profile values to sane range
            double a = Math.Max(5.0, Math.Min(25.0, profileDecel_mps2));
            double buffer = Math.Max(0.0, Math.Min(50.0, profileBuffer_m));

            PitEntryDecelProfile_mps2 = a;
            PitEntryBuffer_m = buffer;

            // Inputs
            double speedKph = data?.NewData?.SpeedKmh ?? 0.0;
            bool isInPitLane = (data?.NewData?.IsInPitLane ?? 0) != 0;

            // IMPORTANT: use the engine’s pit-lane edge (set in Update())
            bool crossedPitLineThisTick = isInPitLane && !_wasInPitLane;

            // Pit limit (prefer session data, fallback to iRacingExtra)
            double pitLimitKph =
                ReadDouble(pluginManager, "DataCorePlugin.GameRawData.SessionData.WeekendInfo.TrackPitSpeedLimit", double.NaN);

            if (double.IsNaN(pitLimitKph) || pitLimitKph <= 0.1)
                pitLimitKph = ReadDouble(pluginManager, "IRacingExtraProperties.iRacing_PitSpeedLimitKph", double.NaN);

            if (double.IsNaN(pitLimitKph) || pitLimitKph <= 0.1)
            {
                ResetPitEntryAssistOutputs();
                return;
            }

            PitEntrySpeedDelta_kph = speedKph - pitLimitKph;

            // Arming (EnteringPits OR limiter ON and overspeed > +2kph)
            bool limiterOn = (data?.NewData?.PitLimiterOn ?? 0) != 0;
            bool armed = (CurrentPitPhase == PitPhase.EnteringPits) || (limiterOn && PitEntrySpeedDelta_kph > 2.0);

            // If we are NOT armed and did NOT cross the line this tick -> fully reset and exit
            if (!armed && !crossedPitLineThisTick)
            {
                ResetPitEntryAssistOutputs();
                return;
            }

            // Distance to pit entry (prefer iRacingExtra distance)
            double dToEntry_m = ReadDouble(pluginManager, "IRacingExtraProperties.iRacing_DistanceToPitEntry", double.NaN);
            bool dOk = !double.IsNaN(dToEntry_m) && dToEntry_m >= 0.0 && dToEntry_m <= 5000.0;

            if (!dOk)
            {
                // Fallback: stored markers -> percent + track length
                double carPct = NormalizeTrackPercent(data?.NewData?.TrackPositionPercent ?? double.NaN);

                // Pit entry line % (prefer stored markers)
                var stored = GetStoredTrackMarkers(_trackMarkersLastKey);
                double storedEntryPct = stored?.PitEntryTrkPct ?? double.NaN;
                var session = GetSessionState(_trackMarkersLastKey);
                double sessionTrackLenM = session?.SessionTrackLengthM ?? double.NaN;
                bool useStored = !double.IsNaN(storedEntryPct) && storedEntryPct >= 0.0 && storedEntryPct <= 1.0 &&
                                 !double.IsNaN(sessionTrackLenM) && sessionTrackLenM >= MinTrackLengthM && sessionTrackLenM <= MaxTrackLengthM &&
                                 !string.Equals(_trackMarkersLastKey, "unknown", StringComparison.OrdinalIgnoreCase);

                double pitEntryPct = useStored
                    ? storedEntryPct
                    : ReadDouble(pluginManager, "IRacingExtraProperties.iRacing_PitEntryTrkPct", double.NaN);

                // Track length in km (session cached)
                double trackLenKm = sessionTrackLenM / 1000.0;

                if (double.IsNaN(carPct) || double.IsNaN(pitEntryPct) || double.IsNaN(trackLenKm) || trackLenKm <= 0)
                {
                    // If we crossed the line but can’t compute distance, still allow END/reset and exit
                    ResetPitEntryAssistOutputs();
                    return;
                }

                double trackLen_m = trackLenKm * 1000.0;
                double dp = pitEntryPct - carPct;
                if (dp < 0) dp += 1.0;
                dToEntry_m = dp * trackLen_m;
            }

            // Window clamp (your spec)
            dToEntry_m = Math.Max(0.0, Math.Min(500.0, dToEntry_m));

            // If we’re armed: keep the 500m inhibit behaviour.
            // If we crossed the line: DO NOT early-return; we want LINE to log.
            if (dToEntry_m >= 500.0 && !crossedPitLineThisTick)
            {
                ResetPitEntryAssistOutputs();
                return;
            }

            // Required distance under constant decel to reach pit speed at the line
            double v = Math.Max(0.0, speedKph / 3.6);
            double vT = Math.Max(0.0, pitLimitKph / 3.6);

            double dReq = 0.0;
            if (v > vT + 0.05)
                dReq = (v * v - vT * vT) / (2.0 * a);

            double margin = dToEntry_m - dReq;

            // Publish
            PitEntryAssistActive = true;
            PitEntryDistanceToLine_m = dToEntry_m;
            PitEntryRequiredDistance_m = dReq;
            PitEntryMargin_m = margin;

            // Cue thresholds (as agreed)
            if (margin < -buffer) PitEntryCue = 4;          // Late
            else if (margin <= 0) PitEntryCue = 3;          // BrakeNow
            else if (margin <= buffer) PitEntryCue = 2;     // BrakeSoon
            else PitEntryCue = 1;                           // OK

            // --- Edge-triggered logging (no spam) ---
            if (PitEntryAssistActive && !_pitEntryAssistWasActive)
            {
                // Reset firstOK tracking at activation start
                _pitEntryFirstCompliantCaptured = false;
                _pitEntryFirstCompliantDToLine_m = double.NaN;

                SimHub.Logging.Current.Info(
                    $"[LalaPlugin:PitEntryAssist] ACTIVATE " +
                    $"dToLine={PitEntryDistanceToLine_m:F1}m " +
                    $"dReq={PitEntryRequiredDistance_m:F1}m " +
                    $"margin={PitEntryMargin_m:F1}m " +
                    $"spdΔ={PitEntrySpeedDelta_kph:F1}kph " +
                    $"decel={PitEntryDecelProfile_mps2:F1} " +
                    $"buffer={PitEntryBuffer_m:F1} " +
                    $"cue={PitEntryCue}"
                );
            }

            // Capture first compliant point AFTER ACTIVATE reset
            if (!_pitEntryFirstCompliantCaptured && PitEntrySpeedDelta_kph <= 1.0)
            {
                _pitEntryFirstCompliantCaptured = true;
                _pitEntryFirstCompliantDToLine_m = PitEntryDistanceToLine_m;
            }

            // LINE log (exactly once on pit-lane entry)
            if (crossedPitLineThisTick)
            {
                string firstOkText = _pitEntryFirstCompliantCaptured
                    ? (_pitEntryFirstCompliantDToLine_m.ToString("F1") + "m")
                    : "n/a";

                SimHub.Logging.Current.Info(
                    $"[LalaPlugin:PitEntryAssist] LINE " +
                    $"dToLine={PitEntryDistanceToLine_m:F1}m " +
                    $"dReq={PitEntryRequiredDistance_m:F1}m " +
                    $"margin={PitEntryMargin_m:F1}m " +
                    $"spdΔ={PitEntrySpeedDelta_kph:F1}kph " +
                    $"firstOK={firstOkText} " +
                    $"okBefore={firstOkText} " +
                    $"decel={PitEntryDecelProfile_mps2:F1} " +
                    $"buffer={PitEntryBuffer_m:F1} " +
                    $"cue={PitEntryCue}"
                );
            }

            _pitEntryAssistWasActive = PitEntryAssistActive;
        }

        private static double ReadDouble(PluginManager pluginManager, string prop, double fallback)
        {
            try
            {
                var v = pluginManager.GetPropertyValue(prop);
                if (v == null) return fallback;
                return Convert.ToDouble(v);
            }
            catch { return fallback; }
        }

        private void ResetPitEntryAssistOutputs()
        {
            // Log END once per activation
            if (_pitEntryAssistWasActive)
            {
                SimHub.Logging.Current.Info("[LalaPlugin:PitEntryAssist] END");
                _pitEntryFirstCompliantCaptured = false;
                _pitEntryFirstCompliantDToLine_m = double.NaN;
                _pitEntryAssistWasActive = false;
            }

            PitEntryAssistActive = false;
            PitEntryDistanceToLine_m = 0.0;
            PitEntryRequiredDistance_m = 0.0;
            PitEntryMargin_m = 0.0;
            PitEntryCue = 0;
            PitEntrySpeedDelta_kph = 0.0;
            // keep PitEntryDecelProfile_mps2 / PitEntryBuffer_m as last-used (useful for debugging)
        }


        // --- Method to be called from LalaLaunch.cs when the out-lap is complete ---
        public void FinalizePaceDeltaCalculation(double outLapTime, double averagePace, bool isLapValid)
        {
            // We call this at every S/F crossing; act only when armed.
            if (_paceDeltaState == PaceDeltaState.AwaitingPitLap)
            {
                // First lap after pit exit = PIT LAP (includes the stop)
                if (!isLapValid)
                {
                    SimHub.Logging.Current.Debug("[LalaPlugin:Pit Cycle] Pit-lap invalid – aborting pit-cycle evaluation.");
                    ResetPaceDelta();
                    return;
                }
              
                _avgPaceAtPit = averagePace;
                _pitLapSeconds = outLapTime;   // this first finalize call is the PIT LAP

                SimHub.Logging.Current.Info($"[LalaPlugin:Pit Cycle] Pit-lap captured = {_pitLapSeconds:F2}s – awaiting out-lap completion.");
                _paceDeltaState = PaceDeltaState.AwaitingOutLap;
                return; // wait for next S/F
            }

            if (_paceDeltaState != PaceDeltaState.AwaitingOutLap)
                return;

            // This lap is the OUT-LAP
            if (!isLapValid)
            {
                SimHub.Logging.Current.Debug("[LalaPlugin:Pit Cycle] Out-lap invalid – aborting pit-cycle evaluation.");
                ResetPaceDelta();
                return;
            }

            double outLapSec = outLapTime; // this finalize call is the OUT LAP
            double avg = averagePace;
            double stopSeconds = _lastPitStopDuration.TotalSeconds;

            // Canonical DTL (drive-through loss vs race pace), flooring tiny negatives
            // DTL = (Lpit - Stop + Lout) - 2*Avg
            double dtl = (_pitLapSeconds - stopSeconds + outLapSec) - (2.0 * avg);
            LastTotalPitCycleTimeLoss = Math.Max(0.0, dtl);

            // Keep NetMinusStop for diagnostics (DTL - stop), floored
            LastPaceDeltaNetLoss = Math.Max(0.0, LastTotalPitCycleTimeLoss - stopSeconds);

            SimHub.Logging.Current.Info(
                $"[LalaPlugin:Pit Cycle] DTL computed (formula): Total={LastTotalPitCycleTimeLoss:F2}s, NetMinusStop={LastPaceDeltaNetLoss:F2}s " +
                $"(avg={avg:F2}s, pitLap={_pitLapSeconds:F2}s, outLap={outLapSec:F2}s, stop={stopSeconds:F2}s)");

            // Fire a single, typed callback to avoid double notifications
            OnValidPitStopTimeLossCalculated?.Invoke(LastTotalPitCycleTimeLoss, "total");

            ResetPaceDelta(); // back to Idle until next pit entry
        }


        private void ResetPaceDelta()
        {
            _paceDeltaState = PaceDeltaState.Idle;
            _avgPaceAtPit = 0.0;
            _pitLapSeconds = 0.0;
        }

        // === Track Markers (auto-learn + store) ===
        public double TrackMarkersStoredEntryPct => GetStoredTrackMarkers(_trackMarkersLastKey)?.PitEntryTrkPct ?? double.NaN;
        public double TrackMarkersStoredExitPct => GetStoredTrackMarkers(_trackMarkersLastKey)?.PitExitTrkPct ?? double.NaN;
        public bool TrackMarkersStoredLocked => GetStoredTrackMarkers(_trackMarkersLastKey)?.Locked ?? true;
        public string TrackMarkersTrackKey => _trackMarkersLastKey ?? "unknown";
        public string TrackMarkersStoredLastUpdatedUtc
        {
            get
            {
                var dt = GetStoredTrackMarkers(_trackMarkersLastKey)?.LastUpdatedUtc ?? DateTime.MinValue;
                return dt == DateTime.MinValue ? string.Empty : dt.ToString("o");
            }
        }

        public double TrackMarkersSessionTrackLengthM => GetSessionState(_trackMarkersLastKey)?.SessionTrackLengthM ?? double.NaN;
        public bool TrackMarkersSessionTrackLengthChanged => GetSessionState(_trackMarkersLastKey)?.TrackLengthChanged ?? false;
        public bool TrackMarkersSessionNeedsEntryRefresh => GetSessionState(_trackMarkersLastKey)?.NeedsEntryRefresh ?? false;
        public bool TrackMarkersSessionNeedsExitRefresh => GetSessionState(_trackMarkersLastKey)?.NeedsExitRefresh ?? false;

        public bool TryGetStoredTrackMarkers(string trackKey, out double entryPct, out double exitPct, out DateTime lastUpdatedUtc, out bool locked)
        {
            EnsureTrackMarkerStoreLoaded();
            string key = NormalizeTrackKey(trackKey);

            if (string.IsNullOrWhiteSpace(key) || string.Equals(key, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                entryPct = double.NaN;
                exitPct = double.NaN;
                locked = true;
                lastUpdatedUtc = DateTime.MinValue;
                return false;
            }

            var record = GetStoredTrackMarkers(key);
            if (record == null)
            {
                entryPct = double.NaN;
                exitPct = double.NaN;
                locked = true;
                lastUpdatedUtc = DateTime.MinValue;
                return false;
            }

            entryPct = record.PitEntryTrkPct;
            exitPct = record.PitExitTrkPct;
            locked = record.Locked;
            lastUpdatedUtc = record.LastUpdatedUtc;
            return true;
        }

        public bool TryDequeueTrackMarkerTrigger(out TrackMarkerTriggerEvent trigger)
        {
            if (_trackMarkerTriggers.Count > 0)
            {
                trigger = _trackMarkerTriggers.Dequeue();
                return true;
            }

            trigger = default;
            return false;
        }

        public void SetTrackMarkersLock(string trackKey, bool locked)
        {
            EnsureTrackMarkerStoreLoaded();
            string key = NormalizeTrackKey(trackKey);
            if (string.Equals(key, "unknown", StringComparison.OrdinalIgnoreCase))
                return;
            var record = GetOrCreateTrackMarkerRecord(key);
            if (record.Locked == locked)
                return;
            record.Locked = locked;
            SaveTrackMarkers();
            SimHub.Logging.Current.Info($"[LalaPlugin:TrackMarkers] lock trackKey={key} locked={locked}");
        }

        private void UpdateTrackMarkers(string trackKey, double carPct, double trackLenM, bool isInPitLane, bool justExitedPits, bool isInPitStall, double speedKph)
        {
            EnsureTrackMarkerStoreLoaded();

            string key = NormalizeTrackKey(trackKey);
            if (string.Equals(key, "unknown", StringComparison.OrdinalIgnoreCase))
                return;

            var record = GetOrCreateTrackMarkerRecord(key);
            var session = GetSessionState(key, create: true);

            UpdateSessionTrackLength(session, record, trackLenM, key);

            bool entryEdge = isInPitLane && !_wasInPitLane;
            bool exitEdge = justExitedPits;

            if (entryEdge)
            {
                if (!isInPitStall && speedKph > 5.0)
                {
                    HandlePitLineEdge(record, session, key, carPct, isEntry: true);
                }
                else
                {
                    SimHub.Logging.Current.Debug(
                        $"[LalaPlugin:TrackMarkers] block entry capture track='{key}' pitStall={isInPitStall} speed={speedKph:F1}kph");
                }
            }

            if (exitEdge)
            {
                if (!isInPitStall && speedKph > 10.0)
                {
                    HandlePitLineEdge(record, session, key, carPct, isEntry: false);
                }
                else
                {
                    SimHub.Logging.Current.Debug(
                        $"[LalaPlugin:TrackMarkers] block exit capture track='{key}' pitStall={isInPitStall} speed={speedKph:F1}kph");
                }
            }

            TryFireFirstCapture(record, session, key);
            TryFireLinesRefreshed(session, key);
        }

        private void UpdateSessionTrackLength(TrackMarkerSessionState session, TrackMarkerRecord record, double trackLenM, string key)
        {
            if (double.IsNaN(trackLenM) || trackLenM < MinTrackLengthM || trackLenM > MaxTrackLengthM)
                return;

            if (double.IsNaN(session.SessionStartTrackLengthM))
            {
                session.SessionStartTrackLengthM = trackLenM;
                session.SessionTrackLengthM = trackLenM;
                return;
            }

            session.SessionTrackLengthM = trackLenM;

            if (!session.TrackLengthChanged)
            {
                double delta = Math.Abs(trackLenM - session.SessionStartTrackLengthM);
                if (delta > TrackLengthChangeThresholdM)
                {
                    session.TrackLengthChanged = true;
                    session.NeedsEntryRefresh = true;
                    session.NeedsExitRefresh = true;

                    if (record.Locked)
                    {
                        record.Locked = false;
                        SaveTrackMarkers();
                    }

                    SimHub.Logging.Current.Info(
                        $"[LalaPlugin:TrackMarkers] track length change detected for '{key}' ({session.SessionStartTrackLengthM:F1}m -> {trackLenM:F1}m). Unlocking and forcing refresh.");
                    EnqueueTrackMarkerTrigger(new TrackMarkerTriggerEvent
                    {
                        TrackKey = key,
                        Trigger = TrackMarkerTriggerType.TrackLengthChanged
                    });
                }
            }
        }

        private void HandlePitLineEdge(TrackMarkerRecord record, TrackMarkerSessionState session, string key, double carPct, bool isEntry)
        {
            double pct = NormalizeTrackPercent(carPct);
            if (double.IsNaN(pct))
                return;

            if (isEntry && pct < 0.50)
            {
                SimHub.Logging.Current.Debug(
                    $"[LalaPlugin:TrackMarkers] block entry capture track='{key}' pct={pct:F4} (below min bound)");
                return;
            }

            if (!isEntry && pct > 0.50)
            {
                SimHub.Logging.Current.Debug(
                    $"[LalaPlugin:TrackMarkers] block exit capture track='{key}' pct={pct:F4} (above max bound)");
                return;
            }

            double stored = isEntry ? record.PitEntryTrkPct : record.PitExitTrkPct;
            bool missing = double.IsNaN(stored) || stored == 0.0;
            bool refreshOverride = session.TrackLengthChanged &&
                                   ((isEntry && session.NeedsEntryRefresh) || (!isEntry && session.NeedsExitRefresh));
            bool locked = record.Locked && !missing && !refreshOverride;

            bool shouldOverwrite = false;
            if (missing)
            {
                shouldOverwrite = true;
            }
            else if (refreshOverride)
            {
                shouldOverwrite = true;
            }
            else if (!locked && WrapAbsDeltaPct(pct, stored) > TrackMarkerDeltaTolerancePct)
            {
                shouldOverwrite = true;
            }

            if (!shouldOverwrite)
                return;

            if (isEntry)
            {
                record.PitEntryTrkPct = pct;
                session.NeedsEntryRefresh = refreshOverride ? false : session.NeedsEntryRefresh;
            }
            else
            {
                record.PitExitTrkPct = pct;
                session.NeedsExitRefresh = refreshOverride ? false : session.NeedsExitRefresh;
            }

            record.LastUpdatedUtc = DateTime.UtcNow;
            SaveTrackMarkers();

            string edgeName = isEntry ? "entry" : "exit";
            string reason = missing ? "capture" : refreshOverride ? "refresh" : "update";
            SimHub.Logging.Current.Info(
                $"[LalaPlugin:TrackMarkers] {reason} {edgeName} pct track='{key}' pct={pct:F4} locked={record.Locked}");
        }

        private void TryFireFirstCapture(TrackMarkerRecord record, TrackMarkerSessionState session, string key)
        {
            if (session.FirstCaptureTriggered)
                return;

            if (IsValidPct(record.PitEntryTrkPct) && IsValidPct(record.PitExitTrkPct))
            {
                session.FirstCaptureTriggered = true;
                EnqueueTrackMarkerTrigger(new TrackMarkerTriggerEvent
                {
                    TrackKey = key,
                    Trigger = TrackMarkerTriggerType.FirstCapture
                });
            }
        }

        private void TryFireLinesRefreshed(TrackMarkerSessionState session, string key)
        {
            if (!session.TrackLengthChanged || session.NeedsEntryRefresh || session.NeedsExitRefresh || session.LinesRefreshedTriggered)
                return;

            session.LinesRefreshedTriggered = true;
            EnqueueTrackMarkerTrigger(new TrackMarkerTriggerEvent
            {
                TrackKey = key,
                Trigger = TrackMarkerTriggerType.LinesRefreshed
            });
        }

        private void EnqueueTrackMarkerTrigger(TrackMarkerTriggerEvent trigger)
        {
            _trackMarkerTriggers.Enqueue(trigger);
        }

        private TrackMarkerRecord GetStoredTrackMarkers(string trackKey)
        {
            string key = NormalizeTrackKey(trackKey);
            if (string.IsNullOrWhiteSpace(key))
                return null;
            _trackMarkerStore.TryGetValue(key, out var rec);
            return rec;
        }

        private TrackMarkerRecord GetOrCreateTrackMarkerRecord(string trackKey)
        {
            string key = NormalizeTrackKey(trackKey);
            if (!_trackMarkerStore.TryGetValue(key, out var rec))
            {
                rec = new TrackMarkerRecord { Locked = true };
                _trackMarkerStore[key] = rec;
            }

            return rec;
        }

        private TrackMarkerSessionState GetSessionState(string trackKey, bool create = false)
        {
            string key = NormalizeTrackKey(trackKey);
            if (string.IsNullOrWhiteSpace(key))
                return null;

            if (_trackMarkerSessionState.TryGetValue(key, out var state))
                return state;

            if (!create)
                return null;

            state = new TrackMarkerSessionState();
            _trackMarkerSessionState[key] = state;
            return state;
        }

        private string GetCanonicalTrackKey(PluginManager pluginManager)
        {
            try
            {
                string rawKey = Convert.ToString(pluginManager.GetPropertyValue("DataCorePlugin.GameData.TrackCode") ?? string.Empty);
                string rawName = Convert.ToString(
                    pluginManager.GetPropertyValue("IRacingExtraProperties.iRacing_TrackDisplayName") ??
                    pluginManager.GetPropertyValue("DataCorePlugin.GameData.TrackNameWithConfig") ??
                    pluginManager.GetPropertyValue("DataCorePlugin.GameData.TrackName") ??
                    string.Empty);

                rawKey = NormalizeTrackKey(rawKey);
                rawName = NormalizeTrackKey(rawName);

                if (!string.IsNullOrWhiteSpace(rawKey))
                    return rawKey;
                if (!string.IsNullOrWhiteSpace(rawName))
                    return rawName;

                return "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private string NormalizeTrackKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "unknown";
            var trimmed = key.Trim();
            if (trimmed.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                return "unknown";
            return trimmed;
        }

        private double NormalizeTrackPercent(double pct)
        {
            if (double.IsNaN(pct)) return double.NaN;
            if (pct > 1.0001 && pct <= 100.0) pct /= 100.0;
            if (pct < 0.0 || pct > 1.0) return double.NaN;
            return pct;
        }

        private double WrapAbsDeltaPct(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
            double diff = a - b;
            diff = (diff + 1.0) % 1.0;
            if (diff > 0.5) diff -= 1.0;
            return Math.Abs(diff);
        }

        private bool IsValidPct(double pct)
        {
            return !double.IsNaN(pct) && pct > 0.0 && pct <= 1.0;
        }

        private string GetTrackMarkersFolderPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory?.TrimEnd('\\', '/');
            return Path.Combine(baseDir ?? "", "PluginsData", "Common", "LalaLaunch");
        }

        private string GetTrackMarkersFilePath()
        {
            return Path.Combine(GetTrackMarkersFolderPath(), "LalaLaunch.TrackMarkers.json");
        }

        private void EnsureTrackMarkerStoreLoaded()
        {
            if (_trackMarkersLoaded) return;

            var path = GetTrackMarkersFilePath();
            var folder = GetTrackMarkersFolderPath();

            try
            {
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                if (!File.Exists(path))
                {
                    _trackMarkersLoaded = true;
                    SimHub.Logging.Current.Info($"[LalaPlugin:TrackMarkers] load (new) path='{path}'");
                    return;
                }

                var json = File.ReadAllText(path);
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, TrackMarkerRecord>>(json)
                             ?? new Dictionary<string, TrackMarkerRecord>(StringComparer.OrdinalIgnoreCase);
                _trackMarkerStore.Clear();
                foreach (var kvp in loaded)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                    _trackMarkerStore[kvp.Key] = kvp.Value ?? new TrackMarkerRecord { Locked = true };
                }

                SimHub.Logging.Current.Info($"[LalaPlugin:TrackMarkers] load ok ({_trackMarkerStore.Count} track(s)) path='{path}'");
                _trackMarkersLoaded = true;
            }
            catch (Exception ex)
            {
                _trackMarkerStore.Clear();
                _trackMarkersLoaded = true;
                SimHub.Logging.Current.Warn($"[LalaPlugin:TrackMarkers] load fail path='{path}' err='{ex.Message}'");
            }
        }

        private void SaveTrackMarkers()
        {
            var path = GetTrackMarkersFilePath();
            var folder = GetTrackMarkersFolderPath();
            try
            {
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                var json = JsonConvert.SerializeObject(_trackMarkerStore, Formatting.Indented);
                File.WriteAllText(path, json);
                SimHub.Logging.Current.Info($"[LalaPlugin:TrackMarkers] save ok path='{path}'");
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn($"[LalaPlugin:TrackMarkers] save fail path='{path}' err='{ex.Message}'");
            }
        }

        private class TrackMarkerRecord
        {
            public double PitEntryTrkPct { get; set; } = double.NaN;
            public double PitExitTrkPct { get; set; } = double.NaN;
            public DateTime LastUpdatedUtc { get; set; } = DateTime.MinValue;
            public bool Locked { get; set; } = true;
        }

        private class TrackMarkerSessionState
        {
            public double SessionStartTrackLengthM { get; set; } = double.NaN;
            public double SessionTrackLengthM { get; set; } = double.NaN;
            public bool TrackLengthChanged { get; set; }
            public bool NeedsEntryRefresh { get; set; }
            public bool NeedsExitRefresh { get; set; }
            public bool FirstCaptureTriggered { get; set; }
            public bool LinesRefreshedTriggered { get; set; }
        }

        public enum TrackMarkerTriggerType
        {
            FirstCapture,
            TrackLengthChanged,
            LinesRefreshed
        }

        public struct TrackMarkerTriggerEvent
        {
            public string TrackKey { get; set; }
            public TrackMarkerTriggerType Trigger { get; set; }
        }

        // Call this when a new session starts, car changes, or the sim connects.
        // === PIT PHASE UPDATE ===
        private bool _prevInPitLane;
        private bool _prevInPitStall;
        private bool _afterBoxThisLane;
        private bool _pitPhaseSeeded;

        public void ResetPitPhaseState()
        {
            _prevInPitLane = false;
            _prevInPitStall = false;
            _afterBoxThisLane = false;
            _pitPhaseSeeded = false;
            CurrentPitPhase = PitPhase.None;
            if (_pitExitTimer != null) _pitExitTimer.Reset();
        }

        // Called once on the first tick of a session, or when car is already in the lane on startup
        private void SeedPitPhaseIfNeeded(GameData data, PluginManager pluginManager)
        {
            if (_pitPhaseSeeded) return;

            var isInPitLane = data.NewData.IsInPitLane != 0;
            var isInPitStall = Convert.ToBoolean(
                pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PlayerCarInPitStall") ?? false);

            _prevInPitLane = isInPitLane;
            _prevInPitStall = isInPitStall;

            if (isInPitLane)
            {
                if (isInPitStall)
                {
                    _afterBoxThisLane = true; // already in the box
                }
                else
                {
                    double carPct = data.NewData.TrackPositionPercent;
                    var boxObj = pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.DriverInfo.DriverPitTrkPct");
                    double boxPct = (boxObj != null) ? Convert.ToDouble(boxObj) : -1.0;

                    if (boxPct >= 0.0)
                    {
                        double delta = (boxPct - carPct + 1.0) % 1.0;
                        _afterBoxThisLane = (delta >= 0.5); // box is behind → already passed it
                    }
                    else
                    {
                        _afterBoxThisLane = false;
                    }
                }
            }
            else
            {
                _afterBoxThisLane = false;
            }

            _pitPhaseSeeded = true;
        }

        private void UpdatePitPhase(GameData data, PluginManager pluginManager)
        {
            // Ensure the state is initialized
            SeedPitPhaseIfNeeded(data, pluginManager);

            var isInPitLane = data.NewData.IsInPitLane != 0;
            var isInPitStall = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PlayerCarInPitStall") ?? false);

            // Reset "after box" latch on lane entry/exit
            if (!_prevInPitLane && isInPitLane) _afterBoxThisLane = false; // entered lane
            if (_prevInPitLane && !isInPitLane) _afterBoxThisLane = false; // exited lane

            var pitLimiterOn = data.NewData.PitLimiterOn != 0;
            var trackLocation = Convert.ToInt32(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PlayerTrackSurface") ?? 0);
            var stintLength = data.NewData.StintOdo;

            // Entering pits (only if previously off-lane)
            if (!_prevInPitLane && !isInPitLane && (pitLimiterOn || trackLocation == 2) && stintLength > 100)
            {
                CurrentPitPhase = PitPhase.EnteringPits;
                _prevInPitLane = isInPitLane;
                _prevInPitStall = isInPitStall;
                return;
            }

            // Exiting linger
            if (_pitExitTimer.IsRunning)
            {
                CurrentPitPhase = PitPhase.ExitingPits;
                _prevInPitLane = isInPitLane;
                _prevInPitStall = isInPitStall;
                return;
            }

            // Stall phases (take precedence)
            if (isInPitStall)
            {
                _afterBoxThisLane = true;
                var pitSvStatus = Convert.ToInt32(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PlayerCarPitSvStatus") ?? -1);
                switch (pitSvStatus)
                {
                    case 100: CurrentPitPhase = PitPhase.MissedBoxRight; break;
                    case 101: CurrentPitPhase = PitPhase.MissedBoxLeft; break;
                    case 102: CurrentPitPhase = PitPhase.MissedBoxShort; break;
                    case 103: CurrentPitPhase = PitPhase.MissedBoxLong; break;
                    default: CurrentPitPhase = PitPhase.InBox; break;
                }
            }
            else if (isInPitLane)
            {
                // Left stall this tick → definitely after box
                if (_prevInPitStall && !isInPitStall)
                    _afterBoxThisLane = true;

                if (_afterBoxThisLane)
                {
                    CurrentPitPhase = PitPhase.LeavingBox;
                }
                else
                {
                    double carPct = data.NewData.TrackPositionPercent;
                    var boxObj = pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.DriverInfo.DriverPitTrkPct");
                    double boxPct = (boxObj != null) ? Convert.ToDouble(boxObj) : -1.0;

                    if (boxPct >= 0.0)
                    {
                        double delta = (boxPct - carPct + 1.0) % 1.0;
                        CurrentPitPhase = (delta < 0.5)
                            ? PitPhase.ApproachingBox
                            : PitPhase.LeavingBox;
                    }
                    else
                    {
                        CurrentPitPhase = PitPhase.ApproachingBox;
                    }
                }
            }
            else
            {
                CurrentPitPhase = PitPhase.None;
            }

            _prevInPitLane = isInPitLane;
            _prevInPitStall = isInPitStall;
        }

    }
}
