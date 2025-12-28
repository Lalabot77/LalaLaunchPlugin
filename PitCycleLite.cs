using System;

namespace LaunchPlugin
{
    /// <summary>
    /// PitCycleLite — minimal, deterministic pit-cycle surface.
    ///
    /// DEFINITIONS:
    ///  - In-lap  = lap in which pit ENTRY line was crossed.
    ///  - Out-lap = lap in which pit EXIT  line was crossed.
    ///
    /// RACE-PROOF LATCHING:
    ///  - Set per-lap flags on OnPitRoad edges during the current lap.
    ///  - When LastLapTime changes (previous lap just finished), use those flags
    ///    to classify that lap and latch the time.
    ///
    /// TIMERS (from PitEngine):
    ///  - TimePitLaneSec = _pit.TimeOnPitRoad.TotalSeconds   (latched on EXIT)
    ///  - TimePitBoxSec  = _pit.PitStopElapsedSec            (latched on EXIT)
    ///  - DirectSec      = TimePitLaneSec - TimePitBoxSec
    ///
    /// DTL (simple):
    ///  - DTLSec = (InLapSec + OutLapSec) - 2 * AvgLapSec - TimePitBoxSec
    /// </summary>
    public sealed class PitCycleLite
    {
        public enum StatusKind { None, Armed, StopValid, DriveThrough }
        public enum LapKind { None, InLap, OutLap, Normal }

        private readonly PitEngine _pit;

        // Rolling state
        private bool _wasInLane = false;

        // Per-lap flags (cleared when LastLapTime updates)
        private bool _entrySeenThisLap = false;
        private bool _exitSeenThisLap = false;
        
        // We key latching off LastLapTime changes
        private double _lastLapCached = 0.0;

        // Cycle state
        private bool _armed = false;

        // ---- Public, latched outputs ----
        public double InLapSec { get; private set; } = 0.0; // lap which had ENTRY
        public double OutLapSec { get; private set; } = 0.0; // lap which had EXIT
        public double TimePitLaneSec { get; private set; } = 0.0; // limiter-to-limiter (latched at EXIT)
        public double TimePitBoxSec { get; private set; } = 0.0; // stall total        (latched at EXIT)
        public double DirectSec { get; private set; } = 0.0; // lane - stop
        public double DTLSec { get; private set; } = 0.0; // (In + Out) - 2*Avg - Stop
        public double DeltaInSec { get; private set; } = 0.0;
        public double DeltaOutSec { get; private set; } = 0.0;

        private bool _candidateReady = false;
        public bool CandidateReady => _candidateReady;
        private double _totalLossSec = 0.0;
        public double TotalLossSec { get => _totalLossSec; }
        public double TotalLossPlusBoxSec { get => _totalLossSec + TimePitBoxSec; }
        private string _totalLossSource = "direct";
        public string TotalLossSource { get => _totalLossSource; }
        public StatusKind Status { get; private set; } = StatusKind.None;

        // Lap typing
        public LapKind LastLapType { get; private set; } = LapKind.None;
        public LapKind CurrentLapType { get; private set; } = LapKind.Normal;

        // Live, per-lap edges (true only until next S/F)
        public bool EntrySeenThisLap => _entrySeenThisLap;
        public bool ExitSeenThisLap => _exitSeenThisLap;

        // Optional debug
        public bool Armed => _armed;

        public PitCycleLite(PitEngine pit) => _pit = pit;

        public void ResetCycle()
        {
            _armed = false;
            _entrySeenThisLap = false;
            _exitSeenThisLap = false;
            // keep latched numbers visible until next entry
            Status = StatusKind.None;
            LastLapType = LapKind.None;
            CurrentLapType = LapKind.Normal;
            // Hard clear any pending candidate so a stale loss cannot publish after a reset
            _candidateReady = false;
            _totalLossSec = 0.0;
            _totalLossSource = "direct";
            _wasInLane = false;
            _lastLapCached = 0.0;
        }

        /// <summary>
        /// Returns true exactly once when the OUT-LAP finishes (at S/F).
        /// Publishes DTL if > 0, else Direct. Clears its internal latch.
        /// </summary>
        public bool TryGetFinishedOutlap(out double lossSec, out string src)
        {
            if (!_candidateReady)
            {
                lossSec = 0.0;
                src = null;
                return false;
            }

            lossSec = _totalLossSec;
            src = _totalLossSource;
            _candidateReady = false; // one-shot
            return true;
        }

        /// <summary>
        /// Back-compat alias used by LalaLaunch’s DataUpdate block.
        /// </summary>
        public bool ConsumeCandidate(out double lossSec, out string src)
        {
            return TryGetFinishedOutlap(out lossSec, out src);
        }



        /// <summary>
        /// Call once per frame, AFTER:
        ///   1) _pit.Update(...)
        ///   2) you've computed the baseline (AvgLapSec) you display on the dash.
        /// </summary>
        /// <param name="isInPitLane">OnPitRoad boolean this tick.</param>
        /// <param name="completedLaps">Unused (kept for call-site compatibility).</param>
        /// <param name="lastLapSec">SimHub/iRacing LastLapTime.TotalSeconds.</param>
        /// <param name="avgLapSec">Baseline lap time used on the dash (0 if unknown).</param>
        public void Update(bool isInPitLane, int completedLaps, double lastLapSec, double avgLapSec)
        {
            // ---- 1) Edge detection FIRST (for the current, in-progress lap) ----
            if (isInPitLane && !_wasInLane)
            {
                // Pit ENTRY this lap
                _entrySeenThisLap = true;
                SimHub.Logging.Current.Info("[LalaPlugin:Pit Lite] Entry detected. Arming cycle and clearing previous pit figures.");

                _armed = true;

                // New cycle: clear previous latched values so we start fresh
                InLapSec = 0.0;
                OutLapSec = 0.0;
                TimePitLaneSec = 0.0;
                TimePitBoxSec = 0.0;
                DirectSec = 0.0;
                DTLSec = 0.0;

                Status = StatusKind.Armed;
            }
            if (!isInPitLane && _wasInLane)
            {
                // Pit EXIT this lap
                _exitSeenThisLap = true;
                SimHub.Logging.Current.Info("[LalaPlugin:Pit Lite] Exit detected. Latching lane and box timers from PitEngine.");

                // Latch timers immediately from PitEngine
                double tPit = Math.Max(0.0, _pit?.TimeOnPitRoad.TotalSeconds ?? 0.0);
                double tStop = Math.Max(0.0, _pit?.PitStopElapsedSec ?? 0.0);
                TimePitLaneSec = tPit;
                TimePitBoxSec = tStop;
                DirectSec = Math.Max(0.0, tPit - tStop);
                const double stopDetectionThresholdSec = 1.0; // avoid classifying tiny hesitations as stops
                Status = (tStop > stopDetectionThresholdSec) ? StatusKind.StopValid : StatusKind.DriveThrough;
                SimHub.Logging.Current.Info($"[LalaPlugin:Pit Lite] Exit latched. Lane={TimePitLaneSec:F2}s, Box={TimePitBoxSec:F2}s, Direct={DirectSec:F2}s, Status={Status}.");
            }

            // Instantaneous tag for the in-progress lap
            CurrentLapType = _exitSeenThisLap ? LapKind.OutLap
                            : _entrySeenThisLap ? LapKind.InLap
                            : LapKind.Normal;

            // ---- 2) LastLapTime changed => the previous lap just finished: latch now ----
            bool lastLapUpdated = (lastLapSec > 0.0) && (lastLapSec != _lastLapCached);
            if (lastLapUpdated)
            {
                // Prefer OutLap over InLap if both happened in the same lap (drive-through)
                if (_exitSeenThisLap && OutLapSec <= 0.0)
                {
                    OutLapSec = lastLapSec;
                    LastLapType = LapKind.OutLap;
                    // ---- S/F of OUT-LAP: one-shot latch for save ----

                    var chosenSrc = string.IsNullOrEmpty(TotalLossSource) ? "?" : TotalLossSource;
                    SimHub.Logging.Current.Info($"[LalaPlugin:Pit Lite] Out-lap complete. Out={OutLapSec:F2}s, In={InLapSec:F2}s, Lane={TimePitLaneSec:F2}s, Box={TimePitBoxSec:F2}s, Saved={TotalLossSec:F2}s (source={chosenSrc}).");

                }
                else if (_entrySeenThisLap && InLapSec <= 0.0)
                {
                    InLapSec = lastLapSec;
                    LastLapType = LapKind.InLap;
                    SimHub.Logging.Current.Info($"[LalaPlugin:Pit Lite] In-lap latched. In={InLapSec:F2}s.");

                }
                else
                {
                    LastLapType = LapKind.Normal;
                }

                // Simple DTL as soon as both laps + baseline exist
                if (InLapSec > 0.0 && OutLapSec > 0.0 && avgLapSec > 0.0)
                {
                    DTLSec = (InLapSec + OutLapSec) - (2.0 * avgLapSec) - TimePitBoxSec;

                    double chosen = (DTLSec > 0.0) ? DTLSec : DirectSec;
                    _totalLossSec = Math.Max(0.0, chosen);
                    _totalLossSource = (DTLSec > 0.0) ? "dtl" : "direct";
                    _candidateReady = true;   // one-shot for LalaLaunch
                    SimHub.Logging.Current.Info($"[LalaPlugin:Pit Lite] Publishing loss. Source={_totalLossSource}, DTL={DTLSec:F2}s, Direct={DirectSec:F2}s, Avg={avgLapSec:F2}s.");
                }
                else if (InLapSec > 0.0 && OutLapSec > 0.0 && TimePitLaneSec > 0.0)
                {
                    // Baseline pace missing → still publish the direct lane loss so consumers don't stall
                    _totalLossSec = DirectSec;
                    _totalLossSource = "direct";
                    _candidateReady = true;
                    SimHub.Logging.Current.Info($"[LalaPlugin:Pit Lite] Publishing direct loss (avg pace missing). Lane={TimePitLaneSec:F2}s, Box={TimePitBoxSec:F2}s, Direct={DirectSec:F2}s.");

                }

                // New lap begins: clear per-lap flags and reset current-lap type
                _entrySeenThisLap = false;
                _exitSeenThisLap = false;
                CurrentLapType = LapKind.Normal;

                _lastLapCached = lastLapSec;
            }

            // ---- 3) Housekeeping for next frame ----
            _wasInLane = isInPitLane;

            // Recompute deltas vs baseline each frame when data is available
            if (InLapSec > 0.0 && avgLapSec > 0.0)
                DeltaInSec = InLapSec - avgLapSec;
            else
                DeltaInSec = 0.0;

            if (OutLapSec > 0.0 && avgLapSec > 0.0)
                DeltaOutSec = OutLapSec - avgLapSec;
            else
                DeltaOutSec = 0.0;

            // Initialize cache on first valid value
            if (_lastLapCached <= 0.0 && lastLapSec > 0.0)
                _lastLapCached = lastLapSec;
        }
    }
}
