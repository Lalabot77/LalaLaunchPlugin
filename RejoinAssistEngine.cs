// RejoinAssistEngine.cs
// Clean modular reimplementation of rejoin logic
using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Diagnostics;
using System.Collections.Generic;

namespace LaunchPlugin
{
    public enum RejoinReason
    {
        // --- Suppressed states (system off) ---
        SettingDisabled = 0,
        NotInCar = 1,
        InPit = 2,
        OfflinePractice = 3,
        RaceStart = 4,
        LaunchModeActive = 5,
        MsgCxPressed = 6,

        // --- Neutral state (normal driving) ---
        None = 50,

        // --- Transitional states ---
        PitExit = 60,

        // --- Alert states ---
        StoppedOnTrack = 100,
        OffTrackLowSpeed = 110,
        OffTrackHighSpeed = 120,
        Spin = 130,
        WrongWay = 140
    }

    public enum PitPhase
    {
        None,
        EnteringPits,
        ApproachingBox,
        InBox,
        LeavingBox,
        ExitingPits,
        MissedBoxShort,
        MissedBoxLong,
        MissedBoxLeft,
        MissedBoxRight
    }

    public enum ThreatLevel
    {
        CLEAR,    // No cars within 10s
        CAUTION,  // Car within 10s
        WARNING,  // Car within 5s
        DANGER    // Car within 2s
    }
    public class RejoinAssistEngine
    {
        private RejoinReason _currentLogicReason = RejoinReason.None;
        private RejoinReason _detectedReason = RejoinReason.None;
        private double _rejoinSpeed = 0.0;
        private double _previousLapDistPct = -1.0;
        private double _lastGoodTtc = 99.0;
        private DateTime _lastTtcSeenUtc = DateTime.MinValue;
        private const double SpinMessageSpeedGateKmh = 50.0;
        private const double SpinHoldThreatGateSeconds = 6.0; // show "HOLD BRAKES" only if threat < 6s
        private double _lastSpeedKmh = 0.0;

        // --- PIT STATE (via PitEngine) ---
        private PitEngine _pit;
        public void SetPitEngine(PitEngine pit) => _pit = pit;

        // Mirror PitEngine state to existing properties used by dashboards/logic
        public PitPhase CurrentPitPhase => _pit?.CurrentPitPhase ?? PitPhase.None;
        public bool IsExitingPits => _pit != null && _pit.CurrentPitPhase == PitPhase.ExitingPits;

        // Kept only for compatibility with existing dashes; PitEngine owns this timer now.
        public double PitExitTimerSeconds => 0.0;

        // --- Timers ---
        private readonly Stopwatch _delayTimer = new Stopwatch();
        private readonly Stopwatch _lingerTimer = new Stopwatch();
        private readonly Stopwatch _msgCxTimer = new Stopwatch();
        private readonly Stopwatch _spinHoldTimer = new Stopwatch();
        private readonly Stopwatch _stoppedClearTimer = new Stopwatch();
        private const double StoppedClearHoldSeconds = 0.4;
        private bool _suppressedStoppedUntilSpeedClear = false;

        // --- User Settings ---
        private readonly Func<double> _getSpeedThreshold;
        private readonly Func<double> _getLingerTime;
        private readonly Func<double> _getYawThreshold;

        // --- THREAT ASSESSOR MEMBERS ---
        public ThreatLevel CurrentThreatLevel { get; private set; } = ThreatLevel.CLEAR;
        public double TimeToThreatSeconds { get; private set; } = 99.0;

        // --- Debugging and Dash Properties ---
        public double LingerTimeSeconds => _lingerTimer.Elapsed.TotalSeconds;
        public double OverrideTimeSeconds => _msgCxTimer.Elapsed.TotalSeconds;
        public double DelayTimerSeconds => _delayTimer.Elapsed.TotalSeconds;
        public RejoinReason DetectedReason => _detectedReason;

        public bool IsSeriousIncidentActive =>
            IsSeriousIncidentReason(_currentLogicReason) ||
            IsSeriousIncidentReason(_detectedReason);

        public static bool IsSeriousIncidentReason(RejoinReason reason)
        {
            switch (reason)
            {
                case RejoinReason.Spin:
                case RejoinReason.StoppedOnTrack:
                case RejoinReason.WrongWay:
                    return true;
                default:
                    return false;
            }
        }
        //public bool IsEnteringPits => _isEnteringPits;

        public RejoinAssistEngine(Func<double> getSpeedThreshold, Func<double> getLingerTime, Func<double> getYawThreshold)
        {
            _getSpeedThreshold = getSpeedThreshold;
            _getLingerTime = getLingerTime;
            _getYawThreshold = getYawThreshold;
        }

        public RejoinReason CurrentLogicCode => _currentLogicReason;

        public string CurrentMessage
        {
            get
            {
                // RULE: If the main alert is lingering but the situation is now clear,
                // suppress the text message early.
                if (_lingerTimer.IsRunning && _detectedReason == RejoinReason.None)
                {
                    return string.Empty; // Return a blank message
                }

                // Otherwise, show the message based on the current logic reason as normal.
                switch (_currentLogicReason)
                {
                    case RejoinReason.StoppedOnTrack:
                        return "STOPPED ON TRACK - HAZARD!";
                    case RejoinReason.OffTrackLowSpeed:
                        return "OFF TRACK - REJOIN WHEN SAFE";
                    case RejoinReason.OffTrackHighSpeed:
                        return "OFF TRACK - CHECK TRAFFIC";
                    case RejoinReason.PitExit:
                        return "PIT EXIT - WATCH TRAFFIC";
                    case RejoinReason.Spin:
                        // If we're already in the recovery linger, always show the softer message.
                        if (_lingerTimer.IsRunning)
                            return "SPIN RECOVERY - REJOIN WHEN SAFE";

                        // Only show HOLD BRAKES if (a) we're slow AND (b) traffic is genuinely imminent.
                        bool slowEnough = _lastSpeedKmh <= SpinMessageSpeedGateKmh;
                        bool imminentTraffic =
                            TimeToThreatSeconds < SpinHoldThreatGateSeconds ||
                            CurrentThreatLevel == ThreatLevel.DANGER ||
                            CurrentThreatLevel == ThreatLevel.WARNING;

                        return (slowEnough && imminentTraffic)
                            ? "SPIN - HOLD BRAKES!"
                            : "SPIN - REJOIN WHEN SAFE";


                    case RejoinReason.WrongWay:
                        return "WRONG WAY - TURN AROUND SAFELY!";
                    default:
                        return string.Empty;
                }
            }
        }


        public void Reset()
        {
            _currentLogicReason = RejoinReason.None;
            _detectedReason = RejoinReason.None;
            _suppressedStoppedUntilSpeedClear = false;

            _delayTimer.Reset();
            _lingerTimer.Reset();
            _msgCxTimer.Reset();
            _spinHoldTimer.Reset();
            _stoppedClearTimer.Reset();

            _previousLapDistPct = -1.0;

            // Threat/scan state
            _rejoinSpeed = 0.0;
            TimeToThreatSeconds = 99.0;
            _smoothedTtc = 99.0;
            CurrentThreatLevel = ThreatLevel.CLEAR;
            _threatDemoteTarget = ThreatLevel.CLEAR;
            _threatDemoteSinceUtc = DateTime.MinValue;
            _threatInit = false;
            ThreatDebug = string.Empty;
        }



        private double GetDynamicLingerTime()
        {
            double maxLingerTime = _getLingerTime();
            const double minLingerTime = 2.0;

            // --- Threat-Based Override ---
            // Check if there is a clear and immediate threat.
            if (TimeToThreatSeconds < maxLingerTime)
            {
                // If the threat is more urgent than the max linger time, use it.
                // Clamp it to our readable minimum.
                return Math.Max(minLingerTime, TimeToThreatSeconds);
            }

            // --- Fallback to Speed-Based Linger ---
            // If no immediate threat, use the original speed scaling logic.
            double speedThreshold = _getSpeedThreshold();
            const double maxSpeedForScaling = 200.0;

            if (_rejoinSpeed < speedThreshold)
            {
                return maxLingerTime; // Return the full user setting for low-speed rejoins
            }

            // Scale the time down for high-speed rejoins
            double t = (_rejoinSpeed - speedThreshold) / (maxSpeedForScaling - speedThreshold);
            t = Math.Max(0.0, Math.Min(1.0, t));
            double speedBasedLinger = maxLingerTime + t * (minLingerTime - maxLingerTime);

            return speedBasedLinger;
        }

        

        // THREAT ASSESSMENT MODULE //

        // --- Tunable Parameters for Threat Assessment ---
        private const double DangerTime = 2.0;
        private const double WarningTime = 5.0;
        private const double CautionTime = 10.0;

        private const double DangerDist = 5.0;   // meters
        private const double WarningDist = 15.0;
        private const double CautionDist = 30.0;

        // Demotion hysteresis holds (seconds)
        private const double HoldDangerToWarningSec = 0.60;
        private const double HoldWarningToCautionSec = 0.80;
        private const double HoldCautionToClearSec = 1.00;

        // Threat module state (you already have these if you pasted earlier)
        private ThreatLevel _threatDemoteTarget = ThreatLevel.CLEAR;
        private DateTime _threatDemoteSinceUtc = DateTime.MinValue;
        private bool _threatInit = false;
        private double _smoothedTtc = 99.0;
        public string ThreatDebug { get; private set; } = "";

        // ----- Typed wrappers around PluginManager.GetPropertyValue -----
        
        private static double GetDemoteHoldSeconds(ThreatLevel from, ThreatLevel to,
                                                   double d2w, double w2c, double c2l)
        {
            if (from == ThreatLevel.DANGER && to == ThreatLevel.WARNING) return d2w;
            if (from == ThreatLevel.WARNING && to == ThreatLevel.CAUTION) return w2c;
            if (from == ThreatLevel.CAUTION && to == ThreatLevel.CLEAR) return c2l;
            return 0.50;
        }

        private void UpdateThreatAssessment(GameData data, PluginManager pluginManager)
        {
            // --- constants (method-local) ---
            const double EmaAlpha = 0.30;          // smoothing (only when improving)
            const double NoCandidateHoldSec = 0.80;
            const double TinyEstTimeSec = 3.0;     // accept EstTime-only when <= this
            const double FarDistanceGateM = 200.0; // gate small TTC when nobody is near
            const double SpikeIgnoreSec = 0.45;    // ignore sub-spikes unless very close
            const double VeryCloseM = 35.0;        // "very close" for spike allow
            const double LowSpeedMps = 15.0;       // ~55 km/h

            // --- defaults ---
            TimeToThreatSeconds = 99.0;
            CurrentThreatLevel = ThreatLevel.CLEAR;

            if (data == null || pluginManager == null ||
                !string.Equals(data.GameName, "IRacing", StringComparison.OrdinalIgnoreCase))
            {
                ThreatDebug = "guard:GameName";
                return;
            }

            // --- arrays from iRacing SDK (CarIdx*) ---
            float[] estTimeArr = pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.CarIdxEstTime") as float[];

            if (!(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.CarIdxLapDistPct") is float[] lapPctArr) || lapPctArr.Length == 0)
            {
                ThreatDebug = "guard:LapDistPct";
                return;
            }

            // --- player+track info ---
            int playerIdx = 0;
            var oPi = pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PlayerCarIdx");
            if (oPi is int pi) playerIdx = pi; else if (oPi is long pl) playerIdx = (int)pl;

            double mePct = Convert.ToDouble(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.LapDistPct") ?? 0.0);

            double trackLenKm = TrackLengthHelper.ParseTrackLengthKm(
                pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.WeekendInfo.TrackLength"),
                double.NaN);
            double trackLenM = double.IsNaN(trackLenKm) ? 0.0 : trackLenKm * 1000.0;
            if (trackLenM <= 0) trackLenM = 5000.0; // safe default

            // --- scan all slots (no car-number mapping, no Cars[ ]) ---
            int slotCount = lapPctArr.Length;
            if (estTimeArr != null) slotCount = Math.Min(slotCount, estTimeArr.Length);

            double minDistM = double.MaxValue;
            double minTtc = 99.0;
            int accepted = 0, estOnly = 0;

            for (int idx = 0; idx < slotCount; idx++)
            {
                if (idx == playerIdx) continue;

                // Filter NIW / pit
                if (pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.CarIdxTrackSurface") is int[] surfaceArr && idx < surfaceArr.Length && surfaceArr[idx] < 0) continue; // -1 = NotInWorld
                if (pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.CarIdxOnPitRoad") is bool[] onPitArr && idx < onPitArr.Length && onPitArr[idx]) continue;

                double oppPct = lapPctArr[idx];
                if (!(oppPct > 0.0)) continue;

                // Compute "behind distance" (wrap-safe); accept up to 60% of lap behind
                double dBehindPct = mePct - oppPct;
                if (dBehindPct < 0) dBehindPct += 1.0;
                if (dBehindPct <= 0.0 || dBehindPct > 0.6) continue;

                double distM = dBehindPct * trackLenM;
                if (distM > 0 && distM < minDistM) minDistM = distM;

                // TTC from CarIdxEstTime (if present and positive)
                double gapSec = (estTimeArr != null && idx < estTimeArr.Length) ? estTimeArr[idx] : 0.0;

                // Accept slot if we have distance OR tiny EstTime (<= 3s)
                bool hasDistance = distM > 0.0;
                bool tinyEst = gapSec > 0.0 && gapSec <= TinyEstTimeSec;
                if (!hasDistance && !tinyEst) continue;

                double thisTtc;
                if (gapSec > 0.0) thisTtc = gapSec;
                else
                {
                    double yourMps = (data.NewData?.SpeedKmh ?? 0.0) / 3.6;
                    thisTtc = (yourMps > 0.5) ? distM / Math.Max(1.0, yourMps * 0.5) : 99.0;
                    estOnly++;
                }


                if (thisTtc < minTtc) minTtc = thisTtc;
                accepted++;
            }

            // ----- TTC stability guards -----
            double minDist = (minDistM == double.MaxValue) ? double.MaxValue : minDistM;
            double ttcCandidate = (minTtc >= 0.0 && minTtc <= 99.0) ? minTtc : 99.0;

            // Gate small TTC if nobody is near
            if (ttcCandidate < 99.0 && minDist > FarDistanceGateM) ttcCandidate = 99.0;
            // Ignore <0.35s spikes unless very close
            if (ttcCandidate < SpikeIgnoreSec && minDist > VeryCloseM) ttcCandidate = 99.0;

            // Brief dropout hold
            var now = DateTime.UtcNow;
            if (accepted > 0)
            {
                _lastGoodTtc = ttcCandidate;
                _lastTtcSeenUtc = now;
            }
            else
            {
                if ((now - _lastTtcSeenUtc).TotalSeconds < NoCandidateHoldSec)
                    ttcCandidate = _lastGoodTtc;
                else
                    ttcCandidate = 99.0;
            }

            // Publish TTC with EMA on improvements
            TimeToThreatSeconds = ttcCandidate;
            if (!_threatInit) { _smoothedTtc = TimeToThreatSeconds; _threatInit = true; }
            else
            {
                if (TimeToThreatSeconds < _smoothedTtc) _smoothedTtc = TimeToThreatSeconds;
                else _smoothedTtc = EmaAlpha * TimeToThreatSeconds + (1.0 - EmaAlpha) * _smoothedTtc;
            }

            // --- dynamic thresholds (your class-level tunables) ---
            double yourMps2 = (data.NewData?.SpeedKmh ?? 0.0) / 3.6;
            bool isRejoining = _currentLogicReason >= RejoinReason.StoppedOnTrack || _lingerTimer.IsRunning;
            bool isSlow = yourMps2 < LowSpeedMps;
            bool sensitive = isRejoining || isSlow;

            double timeFactor = sensitive ? 0.67 : 1.0;
            double distInflate = sensitive ? 1.5 : 1.0;
            double speedScale = Math.Max(0.85, Math.Min(1.60, 0.85 + (((data.NewData?.SpeedKmh ?? 0.0) / 80.0) * 0.75)));

            double dangerTime = DangerTime * timeFactor;
            double warningTime = WarningTime * timeFactor;
            double cautionTime = CautionTime * timeFactor;

            double dangerDist = DangerDist * distInflate * speedScale;
            double warningDist = WarningDist * distInflate * speedScale;
            double cautionDist = CautionDist * distInflate * speedScale;

            // Instantaneous level from TTC or distance
            ThreatLevel instant =
                (TimeToThreatSeconds < dangerTime) || (minDist > 0 && minDist < dangerDist) ? ThreatLevel.DANGER :
                (TimeToThreatSeconds < warningTime) || (minDist > 0 && minDist < warningDist) ? ThreatLevel.WARNING :
                (TimeToThreatSeconds < cautionTime) || (minDist > 0 && minDist < cautionDist) ? ThreatLevel.CAUTION :
                                                                                                ThreatLevel.CLEAR;

            // Hysteresis (uses your class-level Hold*Sec)
            ThreatLevel nextLevel = CurrentThreatLevel;
            if (instant >= CurrentThreatLevel)
            {
                nextLevel = instant;
                _threatDemoteTarget = instant;
                _threatDemoteSinceUtc = now;
            }
            else
            {
                if (_threatDemoteTarget != instant)
                {
                    _threatDemoteTarget = instant;
                    _threatDemoteSinceUtc = now;
                }
                double hold = GetDemoteHoldSeconds(CurrentThreatLevel, _threatDemoteTarget,
                                                   HoldDangerToWarningSec, HoldWarningToCautionSec, HoldCautionToClearSec);
                if ((now - _threatDemoteSinceUtc).TotalSeconds >= hold)
                    nextLevel = _threatDemoteTarget;
            }
            CurrentThreatLevel = nextLevel;

            // Debug
            ThreatDebug = $"slots:{accepted} estOnly:{estOnly} TTC:{TimeToThreatSeconds:F1}s Min:{(minDist == double.MaxValue ? 0 : minDist):F0}m Lvl:{CurrentThreatLevel}";
        }



        public void Update(GameData data, PluginManager pluginManager, bool isLaunchModeActive)
        {
            if (!data.GameRunning || data.NewData == null) return;

            // --- 1. DEFINE ALL SHORTCUTS ONCE AT THE TOP ---
            var speed = data.NewData.SpeedKmh;
            _lastSpeedKmh = speed;
            var isInPitLane = data.NewData.IsInPitLane != 0;
            var isOnTrack = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.IsOnTrack") ?? false);
            var isStartReady = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.SessionFlagsDetails.IsStartReady") ?? false);
            var isStartSet = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.SessionFlagsDetails.IsStartSet") ?? false);
            var isStartGo = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.SessionFlagsDetails.IsStartGo") ?? false);
            var session = data.NewData.SessionTypeName;
            var surfaceMaterial = Convert.ToInt32(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PlayerTrackSurfaceMaterial") ?? 0);
            var yawRate = Convert.ToDouble(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.YawRate") ?? 0.0);
            var lapDistPct = data.NewData.TrackPositionPercent;
            var gear = data.NewData.Gear;

            // --- 1b. HANDLE STOPPED-ON-TRACK SUPPRESSION CLEAR CONDITION ---
            bool aboveClearSpeed = speed > _getSpeedThreshold();
            if (aboveClearSpeed)
            {
                if (!_stoppedClearTimer.IsRunning) _stoppedClearTimer.Restart();
            }
            else if (_stoppedClearTimer.IsRunning)
            {
                _stoppedClearTimer.Reset();
            }

            if (_suppressedStoppedUntilSpeedClear &&
                _stoppedClearTimer.IsRunning &&
                _stoppedClearTimer.Elapsed.TotalSeconds >= StoppedClearHoldSeconds)
            {
                _suppressedStoppedUntilSpeedClear = false;
            }

            // --- 2. READ PIT STATE FROM PITENGINE (no local timers) ---
            bool exitingPitsActive = _pit != null && _pit.CurrentPitPhase == PitPhase.ExitingPits;

            // --- 3. DETERMINE THE CURRENT REJOIN REASON ---
            _detectedReason = DetectReason(pluginManager, isOnTrack, isInPitLane, session, isStartReady, isStartSet, isStartGo, speed, surfaceMaterial, yawRate, lapDistPct, gear);

            if (_suppressedStoppedUntilSpeedClear && _detectedReason == RejoinReason.StoppedOnTrack)
            {
                _detectedReason = RejoinReason.None;
            }

            // --- 4. APPLY LOGIC WITH A STRICT PRIORITY ORDER ---

            // PRIORITY 1: Manual Override is always first.
            if (_msgCxTimer.IsRunning)
            {
                if (_msgCxTimer.Elapsed.TotalSeconds < 1.0)
                {
                    Reset();
                    _currentLogicReason = RejoinReason.MsgCxPressed;
                }
                else if (_msgCxTimer.Elapsed.TotalSeconds < 30.0)
                {
                    _currentLogicReason = RejoinReason.MsgCxPressed;
                }
            }
            // PRIORITY 2: Suppressed states like InPit and RaceStart override everything else.
            else if ((int)_detectedReason < 50 || isLaunchModeActive)
            {
                if (_detectedReason == RejoinReason.NotInCar)
                {
                    Reset();
                }
                else
                {
                    if ((int)_currentLogicReason >= 100) { _currentLogicReason = RejoinReason.None; }
                    _delayTimer.Reset();
                    _lingerTimer.Reset();
                    _currentLogicReason = _detectedReason; // Ensure logic reflects the suppressed state
                }
            }
            // PRIORITY 3: Active timer states (Spin, PitExit).
            else if (_spinHoldTimer.IsRunning && _spinHoldTimer.Elapsed.TotalSeconds < 3.0)
            {
                _currentLogicReason = RejoinReason.Spin;
                _delayTimer.Reset();
                _lingerTimer.Reset();
            }
            else if (exitingPitsActive)
            {
                _currentLogicReason = RejoinReason.PitExit;
                _delayTimer.Reset();
                _lingerTimer.Reset();
            }
            // PRIORITY 4: Linger logic for recovering from an alert.
            else if ((int)_currentLogicReason >= 100 && _detectedReason == RejoinReason.None)
            {
                _delayTimer.Reset();
                if (!_lingerTimer.IsRunning) { _lingerTimer.Restart(); _rejoinSpeed = speed; }

                double requiredLingerTime = GetDynamicLingerTime();
                bool timeUp = _lingerTimer.Elapsed.TotalSeconds >= requiredLingerTime;
                bool speedSafe = speed > _getSpeedThreshold();

                if (timeUp && speedSafe) { _lingerTimer.Reset(); _currentLogicReason = RejoinReason.None; }
            }
            // PRIORITY 5: Detecting a new alert.
            else if ((int)_detectedReason >= 100)
            {
                if (_detectedReason == RejoinReason.Spin)
                {
                    _currentLogicReason = RejoinReason.Spin;
                    if (!_spinHoldTimer.IsRunning) { _spinHoldTimer.Restart(); }
                    _delayTimer.Reset();
                    _lingerTimer.Reset();
                }
                else
                {
                    double requiredDelay;
                    switch (_detectedReason)
                    {
                        case RejoinReason.OffTrackHighSpeed: requiredDelay = 1.5; break;
                        case RejoinReason.StoppedOnTrack: requiredDelay = 2.0; break;
                        default: requiredDelay = 1.0; break;
                    }

                    if (!_delayTimer.IsRunning) { _delayTimer.Restart(); }

                    if (_delayTimer.Elapsed.TotalSeconds >= requiredDelay)
                    {
                        _currentLogicReason = _detectedReason;
                        _delayTimer.Reset(); _lingerTimer.Reset();
                    }
                }
            }
            // PRIORITY 6: Default to None.
            else
            {
                _currentLogicReason = RejoinReason.None;
            }

            // --- 5. MANAGE TIMER EXPIRY SEPARATELY ---
            if (_spinHoldTimer.IsRunning && _spinHoldTimer.Elapsed.TotalSeconds >= 3.0) { _spinHoldTimer.Reset(); }
            if (_msgCxTimer.IsRunning && _msgCxTimer.Elapsed.TotalSeconds >= 30.0) { _msgCxTimer.Reset(); }

            UpdateThreatAssessment(data, pluginManager);
            // --- 6. UPDATE "PREVIOUS STATE" FLAGS AT THE VERY END ---
            _previousLapDistPct = lapDistPct;
        }

        private RejoinReason DetectReason(PluginManager pluginManager, bool isOnTrack, bool isInPit, string session, bool startReady, bool startSet, bool startGo, double speed, int surface, double yaw, double lapDistPct, string gear)
        {
            if (!isOnTrack) return RejoinReason.NotInCar;
            if (isInPit)
            {
                return RejoinReason.InPit;
            }
            if (session == "Offline Testing") return RejoinReason.OfflinePractice;
            var sessionState = Convert.ToInt32(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.SessionState") ?? 0);
            if ((sessionState > 0 && sessionState < 4) || startReady || startSet || startGo)
            {
                return RejoinReason.RaceStart;
            }
            // Check for a lap crossing to prevent a false-positive
            bool isLapCrossing = _previousLapDistPct > 0.95 && lapDistPct < 0.05;

            // If lap distance is decreasing while moving and not crossing the start/finish line
            if (lapDistPct < _previousLapDistPct && speed > 5 && !isLapCrossing && gear != "R")
            {
                return RejoinReason.WrongWay;
            }
            if (Math.Abs(yaw) > _getYawThreshold()) return RejoinReason.Spin;
            if (surface >= 15)
            {
                if (speed < _getSpeedThreshold()) return RejoinReason.OffTrackLowSpeed;
                else return RejoinReason.OffTrackHighSpeed;
            }
            if (speed < 20) return RejoinReason.StoppedOnTrack;
            return RejoinReason.None;
        }

        public void TriggerMsgCxOverride()
        {
            if (_currentLogicReason == RejoinReason.StoppedOnTrack || _detectedReason == RejoinReason.StoppedOnTrack)
            {
                _suppressedStoppedUntilSpeedClear = true;
                _stoppedClearTimer.Reset();
            }
            _msgCxTimer.Restart();
            SimHub.Logging.Current.Info("[LalaPlugin:Rejoin Assist] MsgCx override triggered.");
        }
    }
}
