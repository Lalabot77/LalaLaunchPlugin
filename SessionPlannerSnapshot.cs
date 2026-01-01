using System;

namespace LaunchPlugin
{
    /// <summary>
    /// Immutable snapshot of the planned race configuration captured at the green flag.
    /// Holds only planner outputs; does not read live telemetry or mutate state.
    /// </summary>
    public sealed record SessionPlannerSnapshot
    {
        public string PresetName { get; init; } = string.Empty;
        public string CarIdentifier { get; init; } = string.Empty;
        public string TrackKey { get; init; } = string.Empty;
        public string SessionType { get; init; } = string.Empty;
        public double PlannerFuelPerLap { get; init; }
        public TimeSpan PlannerLapTime { get; init; }
        public double TotalFuelRequired { get; init; }
        public int PlannedPitStops { get; init; }
        public double PlannedAfterZeroAllowance { get; init; }
        public string CondensedStintPlanSummary { get; init; } = string.Empty;

        public static SessionPlannerSnapshot Empty => new SessionPlannerSnapshot
        {
            PlannerLapTime = TimeSpan.Zero,
            CondensedStintPlanSummary = string.Empty
        };
    }
}
