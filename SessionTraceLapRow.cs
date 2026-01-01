using System;

namespace LaunchPlugin
{
    /// <summary>
    /// Lap-level snapshot used for session tracing (one record per completed lap).
    /// This is intentionally lap-granular (not tick-based) to keep file sizes manageable.
    /// </summary>
    public sealed class SessionTraceLapRow
    {
        public int LapNumber { get; init; }
        public TimeSpan LapTime { get; init; }
        public double FuelRemaining { get; init; }
        public double StableFuelPerLap { get; init; }
        public double FuelConfidence { get; init; }
        public double LapsRemainingEstimate { get; init; }
        public int? PitStopIndex { get; init; }
        public string PitStopPhase { get; init; } = string.Empty;
        public double AfterZeroUsageSeconds { get; init; }
    }
}
