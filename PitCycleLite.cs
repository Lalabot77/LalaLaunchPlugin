using System;

namespace LaunchPlugin
{
    /// <summary>
    /// Deterministic, tiny pit-cycle tracker:
    /// - Arms on lane entry
    /// - Captures PIT lap at first S/F after entry
    /// - Latches tPit and tStop on lane exit (using PitEngine's trusted timers)
    /// - Captures OUT lap at first S/F after exit, computes Direct + DTL, then holds until next entry
    /// </summary>
    public sealed class PitCycleLite
    {
        public enum StatusKind { None, Armed, StopValid, DriveThrough, Incomplete }

        private readonly PitEngine _pit;   // for lane state + timers
        private bool _wasInLane = false;
        private int _prevCompletedLaps = -1;

        // Cycle latches
        private bool _armed = false;
        private int _entryLap = -1;
        private int _exitLap = -1;

        // Public surface (latched until next arm)
        public double InLapSec { get; private set; } = 0.0;
        public double OutLapSec { get; private set; } = 0.0;
        public double TimePitLaneSec { get; private set; } = 0.0;  // limiter line to limiter line
        public double TimePitBoxSec { get; private set; } = 0.0;   // stationary in stall
        public double DirectSec { get; private set; } = 0.0;       // tPit - tStop
        public double DTLSec { get; private set; } = 0.0;          // (Lpit - Stop + Lout) - 2*Avg
        public StatusKind Status { get; private set; } = StatusKind.None;

        public PitCycleLite(PitEngine pit) => _pit = pit;

        public void ResetCycle()
        {
            _armed = false;
            _entryLap = -1;
            _exitLap = -1;

            // Keep last latched values visible for the dash until next entry
            Status = StatusKind.None;
        }

        /// <summary>
        /// Call every tick from LalaLaunch.DataUpdate.
        /// Pass:
        /// - isInPitLane: lane state this tick
        /// - completedLaps: integer lap count (increments at S/F)
        /// - lastLapSec: SimHub's last completed lap time (seconds)
        /// - avgLapSec: baseline pace to use for DTL (0 if unknown)
        /// </summary>
        public void Update(bool isInPitLane, int completedLaps, double lastLapSec, double avgLapSec)
        {
            // Lane edges
            bool justEnteredLane = (isInPitLane && !_wasInLane);
            bool justExitedLane = (!isInPitLane && _wasInLane);

            if (justEnteredLane)
            {
                _armed = true;
                _entryLap = completedLaps;
                _exitLap = -1;

                // Clear lap latches for the new cycle (previous values remain until re-arm)
                InLapSec = 0.0;
                OutLapSec = 0.0;
                TimePitLaneSec = 0.0;
                TimePitBoxSec = 0.0;
                DirectSec = 0.0;
                DTLSec = 0.0;
                Status = StatusKind.Armed;
            }

            if (justExitedLane)
            {
                _exitLap = completedLaps;

                // Latch timers from PitEngine (trusted stopwatches)
                double tPit = Math.Max(0.0, _pit?.TimeOnPitRoad.TotalSeconds ?? 0.0);
                double tStop = Math.Max(0.0, _pit?.PitStopDuration.TotalSeconds ?? 0.0);

                TimePitLaneSec = tPit;
                TimePitBoxSec = tStop;
                DirectSec = Math.Max(0.0, tPit - tStop);
                Status = (tStop > 0.5) ? StatusKind.StopValid : StatusKind.DriveThrough;
            }

            // S/F detection (CompletedLaps increments)
            bool sfCrossed = (_prevCompletedLaps >= 0) && (completedLaps != _prevCompletedLaps);
            if (sfCrossed)
            {
                // First S/F after entry -> PIT lap
                if (_armed && InLapSec <= 0.0 && completedLaps == _entryLap + 1)
                {
                    if (lastLapSec > 0.0) InLapSec = lastLapSec;
                }

                // First S/F after exit -> OUT lap -> finalize
                if (_armed && _exitLap >= 0 && OutLapSec <= 0.0 && completedLaps == _exitLap + 1)
                {
                    if (lastLapSec > 0.0) OutLapSec = lastLapSec;

                    // Compute DTL if baseline is known
                    if (InLapSec > 0.0 && OutLapSec > 0.0 && avgLapSec > 0.0)
                    {
                        // DTL = (Lpit - Stop + Lout) - 2*Avg
                        DTLSec = Math.Max(0.0, (InLapSec - TimePitBoxSec + OutLapSec) - (2.0 * avgLapSec));
                    }

                    // Stay latched until the next arm; ResetCycle() happens on next lane entry
                    // (so the dash continues to show this result)
                }
            }

            _wasInLane = isInPitLane;
            _prevCompletedLaps = completedLaps;
        }
    }
}
