using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Diagnostics;

namespace LaunchPlugin
{
    public class PitEngine
    {
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


        // --- State management for the Pace Delta calculation ---

        public enum PaceDeltaState { Idle, AwaitingPitLap, AwaitingOutLap, Complete }
        private PaceDeltaState _paceDeltaState = PaceDeltaState.Idle;
        public PaceDeltaState CurrentState => _paceDeltaState;
        private double _avgPaceAtPit = 0.0;
        private double _pitLapSeconds = 0.0; // stores the actual pit lap (includes stop)

        private bool _wasInPitLane = false;
        private bool _wasInPitStall = false;

        private readonly Func<double> _getLingerTime;
        public PitEngine() : this(null) { }
        public PitEngine(Func<double> getLingerTime)
        {
            _getLingerTime = getLingerTime;
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
        }

        public void Update(GameData data, PluginManager pluginManager)
        {
            bool isInPitLane = data.NewData.IsInPitLane != 0;
            bool isInPitStall = Convert.ToBoolean(
                pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PlayerCarInPitStall") ?? false);

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
                        SimHub.Logging.Current.Warn($"PitEngine: Ignoring invalid Direct Travel Time ({direct:F2}s)");
                    }
                    else
                    {
                        LastDirectTravelTime = direct;
                        SimHub.Logging.Current.Info(
                            $"[PitEngine] Direct lane travel computed -> lane={_lastTimeOnPitRoad.TotalSeconds:F2}s, stop={_lastPitStopDuration.TotalSeconds:F2}s, direct={LastDirectTravelTime:F2}s");
                    }
                }
            }

            double linger = _getLingerTime != null ? Math.Max(0.5, _getLingerTime()) : 3.0;
            if (_pitExitTimer.IsRunning && _pitExitTimer.Elapsed.TotalSeconds >= linger)
                _pitExitTimer.Reset();

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
                    _paceDeltaState = PaceDeltaState.Idle;
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
                SimHub.Logging.Current.Debug($"PitEngine: Stop Time Validation -> Internal: {_lastPitStopDuration.TotalSeconds:F2}s, SimHub: {simhubStopTime.TotalSeconds:F2}s");

                _pitStopTimer.Reset();
            }

            // --- Store the previous phase before updating to the new one ---
            //var previousPhase = CurrentPitPhase;
            UpdatePitPhase(data, pluginManager);

            // If we have just left the pits, start waiting for the out-lap.
            if (justExitedPits)
            {
                // Only arm if we've actually started racing
                var lapsCompleted = data?.NewData?.CompletedLaps ?? 0;
                if (lapsCompleted >= 1)
                {
                    SimHub.Logging.Current.Info(
                        $"[PitEngine] Pit exit detected – lane={_lastTimeOnPitRoad.TotalSeconds:F2}s, stop={_lastPitStopDuration.TotalSeconds:F2}s, direct={LastDirectTravelTime:F2}s. Awaiting pit-lap completion.");
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

        // --- Method to be called from LalaLaunch.cs when the out-lap is complete ---
        public void FinalizePaceDeltaCalculation(double outLapTime, double averagePace, bool isLapValid)
        {
            // We call this at every S/F crossing; act only when armed.
            if (_paceDeltaState == PaceDeltaState.AwaitingPitLap)
            {
                // First lap after pit exit = PIT LAP (includes the stop)
                if (!isLapValid)
                {
                    SimHub.Logging.Current.Info("[PitEngine] Pit-lap invalid – aborting pit-cycle evaluation.");
                    ResetPaceDelta();
                    return;
                }
              
                _avgPaceAtPit = averagePace;
                _pitLapSeconds = outLapTime;   // this first finalize call is the PIT LAP

                SimHub.Logging.Current.Info($"[PitEngine] Pit-lap captured = {_pitLapSeconds:F2}s – awaiting out-lap completion.");
                _paceDeltaState = PaceDeltaState.AwaitingOutLap;
                return; // wait for next S/F
            }

            if (_paceDeltaState != PaceDeltaState.AwaitingOutLap)
                return;

            // This lap is the OUT-LAP
            if (!isLapValid)
            {
                SimHub.Logging.Current.Info("[PitEngine] Out-lap invalid – aborting pit-cycle evaluation.");
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
                $"[PitEngine] DTL computed (formula): Total={LastTotalPitCycleTimeLoss:F2}s, NetMinusStop={LastPaceDeltaNetLoss:F2}s " +
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