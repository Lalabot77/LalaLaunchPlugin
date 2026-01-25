// File: RacePreset.cs
// Namespace must match the rest of your plugin.
using Newtonsoft.Json;

namespace LaunchPlugin
{
    public enum RacePresetType
    {
        TimeLimited = 0,
        LapLimited = 1
    }

    /// <summary>
    /// Minimal model for storing/loading race presets as JSON.
    /// Keep this class dumb: no SimHub or UI dependencies.
    /// </summary>
    public class RacePreset
    {
        // Display name in the UI (e.g., "IMSA (40 min)").
        public string Name { get; set; } = "Untitled";

        // Preset kind: time- or lap-limited.
        public RacePresetType Type { get; set; } = RacePresetType.TimeLimited;

        // Duration (use one based on Type).
        public int? RaceMinutes { get; set; }   // when Type == TimeLimited
        public int? RaceLaps { get; set; }   // when Type == LapLimited

        // Strategy bits
        public bool MandatoryStopRequired { get; set; }

        // Tyre change time (seconds). null => leave current UI value unchanged.
        public double? TireChangeTimeSec { get; set; }

        // Max fuel override (% of base tank). null => leave current UI value unchanged.
        public double? MaxFuelPercent { get; set; }

        // Legacy max fuel override in litres (kept for backward compatibility with old JSON).
        [JsonProperty("MaxFuelLitres", NullValueHandling = NullValueHandling.Ignore)]
        public double? LegacyMaxFuelLitres { get; set; }

        // Contingency buffer
        public bool ContingencyInLaps { get; set; } = true; // true = laps; false = litres
        public double ContingencyValue { get; set; } = 1.5;

        public override string ToString() => Name ?? "Preset";
    }
}
