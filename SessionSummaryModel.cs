// SessionSummaryModel.cs (C# 7.3 compatible)
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
        public string CarIdentifier { get; set; }
        public string TrackKey { get; set; }
        public string PresetName { get; set; }
        public string SessionType { get; set; }

        // Planner snapshot (captured once per race)
        public SessionPlannerSnapshot PlannerSnapshot { get; set; }

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
        public DateTime RecordedAtUtc { get; set; }

        public SessionSummaryModel()
        {
            CarIdentifier = string.Empty;
            TrackKey = string.Empty;
            PresetName = string.Empty;
            SessionType = string.Empty;

            PlannerSnapshot = SessionPlannerSnapshot.Empty;

            ActualFuelUsed = null;
            ActualLapsCompleted = null;
            ActualPitStops = null;
            ActualAfterZeroSeconds = null;

            FuelDelta = null;
            LapDelta = null;
            PitStopDelta = null;
            AfterZeroDelta = null;

            GreenSeen = false;
            CheckeredSeen = false;
            IsReplay = false;

            RecordedAtUtc = DateTime.UtcNow;
        }
    }
}
