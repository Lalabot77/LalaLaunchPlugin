using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace LaunchPlugin
{
    /// <summary>
    /// Owns session summary CSV output and lap-based trace writing.
    /// NOTE: scaffolding only — no live telemetry reads or automatic hooks yet.
    /// </summary>
    public sealed class SessionSummaryLogger
    {
        private readonly SessionFileManager _fileManager;
        private readonly object _ioLock = new object();

        public SessionSummaryLogger(SessionFileManager fileManager)
        {
            _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        }

        public string ResolveSummaryDirectory(string configuredPath)
        {
            return string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "LalaPluginData", "LalaSessionSummaries")
                : configuredPath.Trim();
        }

        public string ResolveTraceDirectory(string configuredPath)
        {
            return string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "LalaPluginData", "LalaSessionSummaries", "Traces")
                : configuredPath.Trim();
        }


        public string BuildSummaryFilename(string directory)
        {
            return Path.Combine(directory, "SessionSummary.csv");
        }

        public string BuildTraceFilename(string directory, string carIdentifier, string trackKey, DateTime? timestamp = null)
        {
            var safeCar = _fileManager.SanitizeName(carIdentifier);
            var safeTrack = _fileManager.SanitizeName(trackKey);
            var stamp = (timestamp ?? DateTime.UtcNow).ToString("yyyyMMdd_HHmmss");
            return Path.Combine(directory, $"SessionTrace_{safeCar}_{safeTrack}_{stamp}.csv");
        }

        public void AppendSummaryRow(SessionSummaryModel summary, string configuredPath)
        {
            SimHub.Logging.Current.Info($"[LalaPlugin:SessionSummary] AppendSummaryRow called green={summary?.GreenSeen} checkered={summary?.CheckeredSeen}");

            if (summary == null || !summary.GreenSeen || !summary.CheckeredSeen)
            {
                return;
            }

            string directory = ResolveSummaryDirectory(configuredPath);
            string filename = BuildSummaryFilename(directory);

            Directory.CreateDirectory(directory);

            string headerLine = BuildSummaryHeaderLine();
            string rowLine = BuildSummaryRowLine(summary);

            lock (_ioLock)
            {
                bool exists = File.Exists(filename);
                using (var writer = new StreamWriter(filename, true))
                {
                    if (!exists)
                    {
                        writer.WriteLine(headerLine);
                    }

                    writer.WriteLine(rowLine);
                }
            }
        }

        public void AppendLapTraceRows(IEnumerable<SessionTraceLapRow> laps, string configuredPath, string activeTraceFile)
        {
            if (laps == null)
            {
                return;
            }

            // Runtime must provide a stable trace file path.
            // If it's blank, we do NOT auto-create a new timestamped file per call.
            if (string.IsNullOrWhiteSpace(activeTraceFile))
            {
                return;
            }

            string directory = ResolveTraceDirectory(configuredPath);
            string filename = activeTraceFile;

            Directory.CreateDirectory(directory);

            string headerLine = BuildTraceHeaderLine();

            lock (_ioLock)
            {
                bool exists = File.Exists(filename);
                using (var writer = new StreamWriter(filename, true))
                {
                    if (!exists)
                    {
                        writer.WriteLine(headerLine);
                    }

                    foreach (var lap in laps)
                    {
                        if (lap == null)
                        {
                            continue;
                        }

                        writer.WriteLine(BuildTraceRowLine(lap));
                    }
                }
            }
        }

        public void AppendSummaryToTrace(string summaryLine, string traceFilePath)
        {
            if (string.IsNullOrWhiteSpace(traceFilePath))
            {
                return;
            }

            string directory = Path.GetDirectoryName(traceFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string markerStart = "#[SessionSummary]";
            string markerEnd = "#[EndSessionSummary]";

            lock (_ioLock)
            {
                var block = string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        string.Empty,
                        markerStart,
                        summaryLine ?? string.Empty,
                        markerEnd
                    });

                File.AppendAllText(traceFilePath, block + Environment.NewLine);
            }
        }

        private string BuildSummaryHeaderLine()
        {
            return string.Join(",",
                "SchemaVersion",
                "RecordedAtUtc",
                "SessionType",
                "PresetName",
                "CarIdentifier",
                "TrackKey",
                "ActualLapsCompleted",
                "ActualPitStops",
                "ActualAfterZeroSeconds",
                "ActualFuelUsed",
                "PlannerFuelPerLap",
                "TotalFuelRequired",
                "PlannedPitStops",
                "PlannedAfterZeroAllowance",
                "PlannerLapTimeSeconds");
        }

        private string BuildSummaryRowLine(SessionSummaryModel summary)
        {
            var snapshot = summary.PlannerSnapshot ?? SessionPlannerSnapshot.Empty;

            return string.Join(",",
                ToCsvValue(SessionSummaryModel.SchemaVersion),
                ToCsvValue(summary.RecordedAtUtc.ToString("o", CultureInfo.InvariantCulture)),
                ToCsvValue(summary.SessionType),
                ToCsvValue(summary.PresetName),
                ToCsvValue(summary.CarIdentifier),
                ToCsvValue(summary.TrackKey),
                ToCsvValue(summary.ActualLapsCompleted),
                ToCsvValue(summary.ActualPitStops),
                ToCsvValue(summary.ActualAfterZeroSeconds),
                ToCsvValue(summary.ActualFuelUsed),
                ToCsvValue(snapshot.PlannerFuelPerLap),
                ToCsvValue(snapshot.TotalFuelRequired),
                ToCsvValue(snapshot.PlannedPitStops),
                ToCsvValue(snapshot.PlannedAfterZeroAllowance),
                ToCsvValue(snapshot.PlannerLapTime.TotalSeconds));
        }

        private string BuildTraceHeaderLine()
        {
            return string.Join(",",
                "LapNumber",
                "LapTimeSeconds",
                "FuelRemaining",
                "StableFuelPerLap",
                "FuelConfidence",
                "LapsRemainingEstimate",
                "PitStopIndex",
                "PitStopPhase",
                "AfterZeroUsageSeconds");
        }

        private string BuildTraceRowLine(SessionTraceLapRow lap)
        {
            return string.Join(",",
                ToCsvValue(lap.LapNumber),
                ToCsvValue(lap.LapTime.TotalSeconds),
                ToCsvValue(lap.FuelRemaining),
                ToCsvValue(lap.StableFuelPerLap),
                ToCsvValue(lap.FuelConfidence),
                ToCsvValue(lap.LapsRemainingEstimate),
                ToCsvValue(lap.PitStopIndex),
                ToCsvValue(lap.PitStopPhase),
                ToCsvValue(lap.AfterZeroUsageSeconds));
        }

        private string ToCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "n/a";
            }

            bool needsEscaping = value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r");
            if (needsEscaping)
            {
                string escaped = value.Replace("\"", "\"\"");
                return "\"" + escaped + "\"";
            }

            return value;
        }

        private string ToCsvValue(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private string ToCsvValue(double value)
        {
            return value.ToString("G", CultureInfo.InvariantCulture);
        }

        private string ToCsvValue(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "n/a";
        }

        private string ToCsvValue(double? value)
        {
            return value.HasValue ? value.Value.ToString("G", CultureInfo.InvariantCulture) : "n/a";
        }
    }
}
