using System;
using System.Collections.Generic;
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

        public SessionSummaryLogger(SessionFileManager fileManager)
        {
            _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        }

        public string ResolveSummaryDirectory(string configuredPath)
        {
            return string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "LaunchData", "SessionSummary")
                : configuredPath.Trim();
        }

        public string ResolveTraceDirectory(string configuredPath)
        {
            return string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "LaunchData", "SessionSummary", "Traces")
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
            string directory = ResolveSummaryDirectory(configuredPath);
            string filename = BuildSummaryFilename(directory);

            // Placeholder: scaffolding only — no file IO occurs here. Writing will be added during the wiring task.
            _ = directory;
            _ = filename;
            _ = summary;
        }

        public void AppendLapTraceRows(IEnumerable<SessionTraceLapRow> laps, string configuredPath, string activeTraceFile)
        {
            string directory = ResolveTraceDirectory(configuredPath);
            string filename = string.IsNullOrWhiteSpace(activeTraceFile)
                ? BuildTraceFilename(directory, "car", "track")
                : activeTraceFile;

            // Placeholder: scaffolding only — no file IO occurs here. Append logic will be added during the wiring task.
            _ = directory;
            _ = filename;
            _ = laps;
        }

        public void AppendSummaryToTrace(string summaryLine, string traceFilePath)
        {
            if (string.IsNullOrWhiteSpace(traceFilePath))
            {
                return;
            }

            // Placeholder: scaffolding only — no file IO occurs here. Marker-based appends will be added during the wiring task.
            _ = summaryLine;
        }
    }
}
