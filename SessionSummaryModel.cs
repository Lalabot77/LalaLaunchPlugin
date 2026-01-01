using System;

namespace LaunchPlugin
{
    /// <summary>
    /// Represents a single session-level summary row. Designed for CSV persistence and UI rendering.
    /// </summary>
    public sealed class SessionSummaryModel
    {
        public const string SchemaVersion = "v1";

        // Identity fields
        public string CarIdentifier { get; init; } = string.Empty;
        public string TrackKey { get; init; } = string.Empty;
        public string PresetName { get; init; } = string.Empty;
        public string SessionType { get; init; } = string.Empty;

        // Planner snapshot (captured once per race)
        public SessionPlannerSnapshot PlannerSnapshot { get; init; } = SessionPlannerSnapshot.Empty;

        // Actuals (populated after checkered)
        public double? ActualFuelUsed { get; set; }
        public int? ActualLapsCompleted { get; set; }
        public int? ActualPitStops { get; set; }
        public double? ActualAfterZeroSeconds { get; set; }

        // Deltas (planner vs actual)
        public double? FuelDelta { get; set; }
        public double? LapDelta { get; set; }
        public double? PitStopDelta { get; set; }
        public double? AfterZeroDelta { get; set; }

        // Flags
        public bool GreenSeen { get; set; }
        public bool CheckeredSeen { get; set; }
        public bool IsReplay { get; set; }

        // Timestamp metadata for downstream file naming
        public DateTime RecordedAtUtc { get; init; } = DateTime.UtcNow;
    }
}
