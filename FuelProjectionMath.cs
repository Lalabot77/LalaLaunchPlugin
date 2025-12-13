using System;
using System.Diagnostics;

namespace LaunchPlugin
{
    internal static class FuelProjectionMath
    {
        public static double EstimateDriveTimeAfterZero(
            double sessionTime,
            double sessionTimeRemain,
            double lapSeconds,
            double strategyProjection,
            bool timerZeroSeen,
            double timerZeroSessionTime)
        {
            double projected = Math.Max(0.0, strategyProjection);

            if (projected <= 0.0 && lapSeconds > 0.0)
            {
                // Fall back to at least a lap's worth of buffer when the strategy projection
                // is unavailable; keep it bounded to avoid runaway values.
                projected = lapSeconds;
            }

            return projected;
        }

        public static double ProjectLapsRemaining(
            double lapSeconds,
            double sessionTimeRemain,
            double driveTimeAfterZero,
            double simLapsRemaining,
            out double projectedSecondsRemaining)
        {
            double timePortion = (!double.IsNaN(sessionTimeRemain) && sessionTimeRemain > 0.0)
                ? sessionTimeRemain
                : 0.0;

            projectedSecondsRemaining = timePortion + Math.Max(0.0, driveTimeAfterZero);

            if (lapSeconds > 0.0 && projectedSecondsRemaining > 0.0)
            {
                double projectedLaps = projectedSecondsRemaining / lapSeconds;
                if (projectedLaps > 0.0)
                {
                    return projectedLaps;
                }
            }

            return simLapsRemaining;
        }

#if DEBUG
        public static void RunSelfTests()
        {
            double projectedSeconds;

            double laps = ProjectLapsRemaining(60.0, 120.0, 60.0, 0.0, out projectedSeconds);
            Debug.Assert(Math.Abs(laps - 3.0) < 0.001, "Timed race projection should include after-zero lap");
            Debug.Assert(Math.Abs(projectedSeconds - 180.0) < 0.001, "Projected seconds should include extra drive time");

            double lapsFallback = ProjectLapsRemaining(0.0, double.NaN, 0.0, 5.0, out projectedSeconds);
            Debug.Assert(Math.Abs(lapsFallback - 5.0) < 0.001, "Projection should fall back to sim laps when pace missing");

            double projectedAfterZero = EstimateDriveTimeAfterZero(1810.0, -12.0, 90.0, 15.0, true, 1800.0);
            Debug.Assert(Math.Abs(projectedAfterZero - 15.0) < 0.001, "Strategy projection should drive after-zero");
            double lapBasedAfterZero = EstimateDriveTimeAfterZero(0.0, 50.0, 100.0, 0.0, false, double.NaN);
            Debug.Assert(Math.Abs(lapBasedAfterZero - 0.0) < 0.001, "Zero strategy projection should return zero");
        }
#endif
    }
}
