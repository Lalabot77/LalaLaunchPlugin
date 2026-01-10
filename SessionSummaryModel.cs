// SessionSummaryModel.cs (C# 7.3 compatible)
using System;

namespace LaunchPlugin
{
    /// <summary>
    /// Represents a single session-level summary row. Designed for CSV persistence and UI rendering.
    /// </summary>
    public sealed class SessionSummaryModel
    {
        public const string SchemaVersion = "v2";

        // Identity fields
        public string CarIdentifier { get; set; }
        public string TrackKey { get; set; }
        public string PresetName { get; set; }
        public string SessionType { get; set; }

        // Planner snapshot (captured once per race)
        public SessionPlannerSnapshot PlannerSnapshot { get; set; }

        // Planner snapshot (Phase 2 fields)
        public string PlannerTrackCondition { get; set; }
        public double? PlannerTotalFuelNeededLiters { get; set; }
        public double? PlannerEstDriveTimeAfterTimerZeroSec { get; set; }
        public double? PlannerEstTimePerStopSec { get; set; }
        public int? PlannerRequiredPitStops { get; set; }
        public int? PlannerLapsLappedExpected { get; set; }

        // Profile snapshot (Phase 2 fields)
        public double? ProfileAvgLapTimeSec { get; set; }
        public double? ProfileFuelAvgPerLapLiters { get; set; }
        public double? ProfileBestLapTimeSec { get; set; }

        // Actuals (populated after checkered)
        public double? ActualFuelUsed { get; set; }
        public int? ActualLapsCompleted { get; set; }
        public int? ActualPitStops { get; set; }
        public double? ActualAfterZeroSeconds { get; set; }
        public double? ActualTotalTimeSec { get; set; }
        public double? ActualFuelStartLiters { get; set; }
        public double? ActualFuelAddedLiters { get; set; }
        public double? ActualFuelFinishLiters { get; set; }
        public double? ActualAvgFuelPerLapAllLaps { get; set; }
        public double? ActualAvgLapTimeSecAllLaps { get; set; }
        public double? ActualAvgLapTimeSecValidLaps { get; set; }
        public double? ActualAvgFuelPerLapValidLaps { get; set; }
        public int? ActualLapsLapped { get; set; }

        // Deltas (planner vs actual)
        public double? FuelDelta { get; set; }
        public double? LapDelta { get; set; }
        public double? PitStopDelta { get; set; }
        public double? AfterZeroDelta { get; set; }

        // Data-quality counts
        public int? ActualValidPaceLapCount { get; set; }
        public int? ActualValidFuelLapCount { get; set; }

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
            PlannerTrackCondition = string.Empty;
            PlannerTotalFuelNeededLiters = null;
            PlannerEstDriveTimeAfterTimerZeroSec = null;
            PlannerEstTimePerStopSec = null;
            PlannerRequiredPitStops = null;
            PlannerLapsLappedExpected = null;

            ProfileAvgLapTimeSec = null;
            ProfileFuelAvgPerLapLiters = null;
            ProfileBestLapTimeSec = null;

            ActualFuelUsed = null;
            ActualLapsCompleted = null;
            ActualPitStops = null;
            ActualAfterZeroSeconds = null;
            ActualTotalTimeSec = null;
            ActualFuelStartLiters = null;
            ActualFuelAddedLiters = null;
            ActualFuelFinishLiters = null;
            ActualAvgFuelPerLapAllLaps = null;
            ActualAvgLapTimeSecAllLaps = null;
            ActualAvgLapTimeSecValidLaps = null;
            ActualAvgFuelPerLapValidLaps = null;
            ActualLapsLapped = null;

            FuelDelta = null;
            LapDelta = null;
            PitStopDelta = null;
            AfterZeroDelta = null;

            ActualValidPaceLapCount = null;
            ActualValidFuelLapCount = null;

            GreenSeen = false;
            CheckeredSeen = false;
            IsReplay = false;

            RecordedAtUtc = DateTime.UtcNow;
        }
    }
}
