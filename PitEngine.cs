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

        // --- Public properties for our calculated time loss values ---
        public double LastDirectTravelTime { get; private set; } = 0.0;
        public double LastTotalPitCycleTimeLoss { get; private set; } = 0.0;
        public double LastPaceDeltaNetLoss { get; private set; } = 0.0;

        // --- Event to notify when the Pace Delta calculation is complete and valid ---
        public event Action<double> OnValidPitStopTimeLossCalculated;

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
        private double _inLapTime = 0.0;
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
            _inLapTime = 0.0;
            _avgPaceAtPit = 0.0;
            _lastTimeOnPitRoad = TimeSpan.Zero;
        }

        public void PrimeInLapTime(double inLapSeconds)
        {
            if (inLapSeconds > 20 && inLapSeconds < 900)  // sanity
            {
                _inLapTime = inLapSeconds;
                SimHub.Logging.Current.Debug($"PitEngine: Primed in-lap via previous-lap time = {_inLapTime:F2}s");
            }
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
                        SimHub.Logging.Current.Info($"PitEngine: Direct Pit Lane Travel Time calculated: {LastDirectTravelTime:F2}s");
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
                TimeSpan simhubStopTime = (stopTimeProp is TimeSpan)
                    ? (TimeSpan)stopTimeProp
                    : TimeSpan.FromSeconds(Convert.ToDouble(stopTimeProp ?? 0.0));
                SimHub.Logging.Current.Debug($"PitEngine: Stop Time Validation -> Internal: {_lastPitStopDuration.TotalSeconds:F2}s, SimHub: {simhubStopTime.TotalSeconds:F2}s");

                _pitStopTimer.Reset();
            }

            // --- Store the previous phase before updating to the new one ---
            var previousPhase = CurrentPitPhase;
            UpdatePitPhase(data, pluginManager);

            // --- Logic for the "Race Pace Delta" state machine ---
            // If the phase just changed to InBox, capture the In-Lap time.
            if (CurrentPitPhase == PitPhase.InBox && previousPhase != PitPhase.InBox)
            {
                if (data.NewData.LastLapTime.TotalSeconds > 0)
                {
                    //_inLapTime = data.NewData.LastLapTime.TotalSeconds;
                    // We'll need to pass the average pace from the main plugin. For now, we'll placeholder it.
                    // This will be the next step.
                    //SimHub.Logging.Current.Info($"PitEngine: In-Lap time captured: {_inLapTime:F2}s");
                }
            }

            // If we have just left the pits, start waiting for the out-lap.
            if (justExitedPits)
            {
                // Only arm if we've actually started racing
                var lapsCompleted = data?.NewData?.CompletedLaps ?? 0;
                if (lapsCompleted >= 1)
                {
                    SimHub.Logging.Current.Info("PitEngine: Pit exit detected. Awaiting pit-lap completion.");
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
                    SimHub.Logging.Current.Info("PitEngine: Pit-lap invalid, aborting pit-cycle evaluation.");
                    ResetPaceDelta();
                    return;
                }

                _avgPaceAtPit = averagePace;
                _pitLapSeconds = outLapTime;   // this first finalize call is the PIT LAP

                SimHub.Logging.Current.Info($"PitEngine: Pit-lap captured = {_pitLapSeconds:F2}s. Awaiting out-lap completion.");
                _paceDeltaState = PaceDeltaState.AwaitingOutLap;
                return; // wait for next S/F
            }

            if (_paceDeltaState != PaceDeltaState.AwaitingOutLap)
                return;

            // This lap is the OUT-LAP
            if (!isLapValid)
            {
                SimHub.Logging.Current.Info("PitEngine: Out-lap invalid, aborting pit-cycle evaluation.");
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
                $"PitEngine: DTL computed (formula): Total={LastTotalPitCycleTimeLoss:F2}s, NetMinusStop={LastPaceDeltaNetLoss:F2}s " +
                $"(avg={avg:F2}s, pitLap={_pitLapSeconds:F2}s, outLap={outLapSec:F2}s, stop={stopSeconds:F2}s)");

            // Fire existing callback(s); LalaLaunch handler is debounced and will pick DTL with Direct fallback
            OnValidPitStopTimeLossCalculated?.Invoke(LastTotalPitCycleTimeLoss);
            OnValidPitStopTimeLossCalculated?.Invoke(LastPaceDeltaNetLoss);

            ResetPaceDelta(); // back to Idle until next pit entry
        }


        private void ResetPaceDelta()
        {
            _paceDeltaState = PaceDeltaState.Idle;
            _inLapTime = 0.0;
            _avgPaceAtPit = 0.0;
            _pitLapSeconds = 0.0;
        }

        private void UpdatePitPhase(GameData data, PluginManager pluginManager)
        {
            var isInPitLane = data.NewData.IsInPitLane != 0;
            var isInPitStall = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PlayerCarInPitStall") ?? false);
            var pitLimiterOn = data.NewData.PitLimiterOn != 0;
            var trackLocation = Convert.ToInt32(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PlayerTrackSurface") ?? 0);
            var stintLength = data.NewData.StintOdo;

            if ((pitLimiterOn || trackLocation == 2) && !isInPitLane && stintLength > 100)
            {
                CurrentPitPhase = PitPhase.EnteringPits;
                return;
            }

            if (_pitExitTimer.IsRunning)
            {
                CurrentPitPhase = PitPhase.ExitingPits;
                return;
            }

            if (isInPitStall)
            {
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
                var lapDistPct = data.NewData.TrackPositionPercent;
                var pitBoxLocation = Convert.ToDouble(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.DriverInfo.DriverPitTrkPct") ?? -1.0);
                var trackLength = data.NewData.TrackLength;

                double distanceToPitBox = (pitBoxLocation - lapDistPct) * trackLength;
                if (pitBoxLocation < 0.2 && lapDistPct > 0.8)
                    distanceToPitBox = (pitBoxLocation + (1 - lapDistPct)) * trackLength;

                CurrentPitPhase = (distanceToPitBox >= 0) ? PitPhase.ApproachingBox : PitPhase.LeavingBox;
            }
            else
            {
                CurrentPitPhase = PitPhase.None;
            }
        }
    }
}