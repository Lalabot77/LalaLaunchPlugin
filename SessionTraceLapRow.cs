// SessionTraceLapRow.cs (C# 7.3 compatible)
using System;

namespace LaunchPlugin
{
    /// <summary>
    /// Lap-level snapshot used for session tracing (one record per completed lap).
    /// </summary>
    public sealed class SessionTraceLapRow
    {
        public int LapNumber { get; set; }
        public TimeSpan LapTime { get; set; }
        public double FuelRemaining { get; set; }
        public double StableFuelPerLap { get; set; }
        public double FuelConfidence { get; set; }
        public double LapsRemainingEstimate { get; set; }
        public int? PitStopIndex { get; set; }
        public string PitStopPhase { get; set; }
        public double AfterZeroUsageSeconds { get; set; }

        public SessionTraceLapRow()
        {
            LapNumber = 0;
            LapTime = TimeSpan.Zero;
            FuelRemaining = 0.0;
            StableFuelPerLap = 0.0;
            FuelConfidence = 0.0;
            LapsRemainingEstimate = 0.0;
            PitStopIndex = null;
            PitStopPhase = string.Empty;
            AfterZeroUsageSeconds = 0.0;
        }
    }
}
