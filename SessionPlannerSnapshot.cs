// SessionPlannerSnapshot.cs  (C# 7.3 compatible)
using System;

namespace LaunchPlugin
{
    /// <summary>
    /// Immutable-ish snapshot of the planned race configuration captured at the green flag.
    /// C# 7.3 friendly: no record / init-only setters.
    /// </summary>
    public sealed class SessionPlannerSnapshot
    {
        public string PresetName { get; set; }
        public string CarIdentifier { get; set; }
        public string TrackKey { get; set; }
        public string SessionType { get; set; }

        public double PlannerFuelPerLap { get; set; }
        public TimeSpan PlannerLapTime { get; set; }
        public double TotalFuelRequired { get; set; }
        public int PlannedPitStops { get; set; }
        public double PlannedAfterZeroAllowance { get; set; }
        public string CondensedStintPlanSummary { get; set; }

        public SessionPlannerSnapshot()
        {
            PresetName = string.Empty;
            CarIdentifier = string.Empty;
            TrackKey = string.Empty;
            SessionType = string.Empty;
            PlannerFuelPerLap = 0.0;
            PlannerLapTime = TimeSpan.Zero;
            TotalFuelRequired = 0.0;
            PlannedPitStops = 0;
            PlannedAfterZeroAllowance = 0.0;
            CondensedStintPlanSummary = string.Empty;
        }

        public static SessionPlannerSnapshot Empty
        {
            get { return new SessionPlannerSnapshot(); }
        }
    }
}
