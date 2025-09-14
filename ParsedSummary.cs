using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LaunchPlugin
{
    public class ParsedSummary
    {
        // --- All 31 properties remain the same ---
        public string TimestampUtc { get; set; }
        public string Car { get; set; }
        // ... and so on for all your properties ...
        public string Session { get; set; }
        public string Track { get; set; }
        public string Humidity { get; set; }
        public string AirTemp { get; set; }
        public string TrackTemp { get; set; }
        public string Fuel { get; set; }
        public string SurfaceGrip { get; set; }
        public string TargetBitePoint { get; set; }
        public string ClutchReleaseTime { get; set; }
        public string ClutchDelta { get; set; }
        public string AccelTime100Ms { get; set; }
        public string AccelDeltaLast { get; set; }
        public string LaunchOk { get; set; }
        public string Bogged { get; set; }
        public string AntiStallDetected { get; set; }
        public string WheelSpin { get; set; }
        public string LaunchRpm { get; set; }
        public string MinRpm { get; set; }
        public string ReleaseRpm { get; set; }
        public string RpmDeltaToOptimal { get; set; }
        public string RpmUseOk { get; set; }
        public string ThrottleAtClutchRelease { get; set; }
        public string ThrottleAtLaunchZoneStart { get; set; }
        public string ThrottleDeltaToOptimal { get; set; }
        public string ThrottleModulationDelta { get; set; }
        public string ThrottleUseOk { get; set; }
        public string TractionLossRaw { get; set; }
        public string ReactionTimeMs { get; set; }
        public string LaunchTraceFile { get; set; }

        // --- CONSTRUCTORS ---

        // A default constructor for creating a new summary to be written
        public ParsedSummary() { }

        // A new, "smart" constructor for creating a summary from a file line
        public ParsedSummary(string csvLine)
        {
            // --- SAFETY CHECK ---
            // If the line is empty or is a header, do not proceed.
            if (string.IsNullOrWhiteSpace(csvLine) || csvLine.Trim().StartsWith("TimestampUtc,"))
            {
                return;
            }

            // A robust parser that correctly handles commas inside quoted fields
            var parts = new List<string>();
            var reader = new StringReader(csvLine);
            int charCode;
            var currentPart = new System.Text.StringBuilder();
            bool inQuotes = false;
            while ((charCode = reader.Read()) != -1)
            {
                var ch = (char)charCode;
                if (inQuotes)
                {
                    if (ch == '"') { inQuotes = false; } else { currentPart.Append(ch); }
                }
                else
                {
                    if (ch == '"') { inQuotes = true; }
                    else if (ch == ',') { parts.Add(currentPart.ToString()); currentPart.Clear(); }
                    else { currentPart.Append(ch); }
                }
            }
            parts.Add(currentPart.ToString());

            if (parts.Count < 31) return;

            // Assign properties from the parsed parts
            this.TimestampUtc = parts[0]; this.Car = parts[1]; this.Session = parts[2];
            this.Track = parts[3]; this.Humidity = parts[4]; this.AirTemp = parts[5];
            this.TrackTemp = parts[6]; this.Fuel = parts[7]; this.SurfaceGrip = parts[8];
            this.TargetBitePoint = parts[9]; this.ClutchReleaseTime = parts[10]; this.ClutchDelta = parts[11];
            this.AccelTime100Ms = parts[12]; this.AccelDeltaLast = parts[13]; this.LaunchOk = parts[14];
            this.Bogged = parts[15]; this.AntiStallDetected = parts[16]; this.WheelSpin = parts[17];
            this.LaunchRpm = parts[18]; this.MinRpm = parts[19]; this.ReleaseRpm = parts[20];
            this.RpmDeltaToOptimal = parts[21]; this.RpmUseOk = parts[22]; this.ThrottleAtClutchRelease = parts[23];
            this.ThrottleAtLaunchZoneStart = parts[24]; this.ThrottleDeltaToOptimal = parts[25];
            this.ThrottleModulationDelta = parts[26]; this.ThrottleUseOk = parts[27]; this.TractionLossRaw = parts[28];
            this.ReactionTimeMs = parts[29]; this.LaunchTraceFile = parts[30];
        }

        // --- Your existing methods for writing remain the same ---
        public string GetSummaryForCsvLine()
        {
            var fields = new string[] {
                TimestampUtc, Car, Session, Track, Humidity, AirTemp, TrackTemp, Fuel, SurfaceGrip,
                TargetBitePoint, ClutchReleaseTime, ClutchDelta, AccelTime100Ms, AccelDeltaLast, LaunchOk,
                Bogged, AntiStallDetected, WheelSpin, LaunchRpm, MinRpm, ReleaseRpm, RpmDeltaToOptimal,
                RpmUseOk, ThrottleAtClutchRelease, ThrottleAtLaunchZoneStart, ThrottleDeltaToOptimal,
                ThrottleModulationDelta, ThrottleUseOk, TractionLossRaw, ReactionTimeMs, LaunchTraceFile
            };
            return string.Join(",", fields.Select(f => $"\"{f?.Replace("\"", "\"\"")}\""));
        }

        public string GetCsvHeaderLine()
        {
            return string.Join(",",
                nameof(TimestampUtc), nameof(Car), nameof(Session), nameof(Track), nameof(Humidity),
                nameof(AirTemp), nameof(TrackTemp), nameof(Fuel), nameof(SurfaceGrip),
                nameof(TargetBitePoint), nameof(ClutchReleaseTime), nameof(ClutchDelta),
                nameof(AccelTime100Ms), nameof(AccelDeltaLast), nameof(LaunchOk), nameof(Bogged),
                nameof(AntiStallDetected), nameof(WheelSpin), nameof(LaunchRpm), nameof(MinRpm),
                nameof(ReleaseRpm), nameof(RpmDeltaToOptimal), nameof(RpmUseOk),
                nameof(ThrottleAtClutchRelease), nameof(ThrottleAtLaunchZoneStart),
                nameof(ThrottleDeltaToOptimal), nameof(ThrottleModulationDelta),
                nameof(ThrottleUseOk), nameof(TractionLossRaw), nameof(ReactionTimeMs),
                nameof(LaunchTraceFile)
            );
        }
    }
}