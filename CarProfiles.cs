﻿// In file: CarProfiles.cs
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace LaunchPlugin
{
    public class CarProfile : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _profileName = "New Profile";
        public string ProfileName { get => _profileName; set { if (_profileName != value) { _profileName = value; OnPropertyChanged(); } } }

        // --- Launch Control Properties ---
        private double _targetLaunchRPM = 6000;
        public double TargetLaunchRPM { get => _targetLaunchRPM; set { if (_targetLaunchRPM != value) { _targetLaunchRPM = value; OnPropertyChanged(); } } }
        private double _optimalRPMTolerance = 1000;
        public double OptimalRPMTolerance { get => _optimalRPMTolerance; set { if (_optimalRPMTolerance != value) { _optimalRPMTolerance = value; OnPropertyChanged(); } } }
        private double _targetLaunchThrottle = 80.0;
        public double TargetLaunchThrottle { get => _targetLaunchThrottle; set { if (_targetLaunchThrottle != value) { _targetLaunchThrottle = value; OnPropertyChanged(); } } }
        private double _optimalThrottleTolerance = 5.0;
        public double OptimalThrottleTolerance { get => _optimalThrottleTolerance; set { if (_optimalThrottleTolerance != value) { _optimalThrottleTolerance = value; OnPropertyChanged(); } } }
        private double _targetBitePoint = 45.0;
        public double TargetBitePoint { get => _targetBitePoint; set { if (_targetBitePoint != value) { _targetBitePoint = value; OnPropertyChanged(); } } }
        private double _bitePointTolerance = 3.0;
        public double BitePointTolerance { get => _bitePointTolerance; set { if (_bitePointTolerance != value) { _bitePointTolerance = value; OnPropertyChanged(); } } }
        private double _bogDownFactorPercent = 55.0;
        public double BogDownFactorPercent { get => _bogDownFactorPercent; set { if (_bogDownFactorPercent != value) { _bogDownFactorPercent = value; OnPropertyChanged(); } } }
        private double _antiStallThreshold = 10.0;
        public double AntiStallThreshold { get => _antiStallThreshold; set { if (_antiStallThreshold != value) { _antiStallThreshold = value; OnPropertyChanged(); } } }

        // --- Fuel & Pit Properties ---
        private double _fuelContingencyValue = 1.5;
        public double FuelContingencyValue { get => _fuelContingencyValue; set { if (_fuelContingencyValue != value) { _fuelContingencyValue = value; OnPropertyChanged(); } } }
        private bool _isContingencyInLaps = true;
        public bool IsContingencyInLaps { get => _isContingencyInLaps; set { if (_isContingencyInLaps != value) { _isContingencyInLaps = value; OnPropertyChanged(); } } }
        private double _wetFuelMultiplier = 90;
        public double WetFuelMultiplier { get => _wetFuelMultiplier; set { if (_wetFuelMultiplier != value) { _wetFuelMultiplier = value; OnPropertyChanged(); } } }
        private double _tireChangeTime = 22;
        public double TireChangeTime { get => _tireChangeTime; set { if (_tireChangeTime != value) { _tireChangeTime = value; OnPropertyChanged(); } } }
        private double _racePaceDeltaSeconds = 1.2;
        public double RacePaceDeltaSeconds { get => _racePaceDeltaSeconds; set { if (_racePaceDeltaSeconds != value) { _racePaceDeltaSeconds = value; OnPropertyChanged(); } } }

        // --- NEW Per-Car Property ---
        private double _refuelRate = 2.7;
        public double RefuelRate { get => _refuelRate; set { if (_refuelRate != value) { _refuelRate = value; OnPropertyChanged(); } } }

        [JsonProperty]
        public Dictionary<string, TrackStats> TrackStats { get; set; } = new Dictionary<string, TrackStats>(System.StringComparer.OrdinalIgnoreCase);

        // --- Dash Display Properties ---
        private double _rejoinWarningLingerTime = 10.0;
        public double RejoinWarningLingerTime { get => _rejoinWarningLingerTime; set { if (_rejoinWarningLingerTime != value) { _rejoinWarningLingerTime = value; OnPropertyChanged(); } } }
        private double _rejoinWarningMinSpeed = 50.0;
        public double RejoinWarningMinSpeed { get => _rejoinWarningMinSpeed; set { if (_rejoinWarningMinSpeed != value) { _rejoinWarningMinSpeed = value; OnPropertyChanged(); } } }
        private double _spinYawRateThreshold = 15;
        public double SpinYawRateThreshold { get => _spinYawRateThreshold; set { if (_spinYawRateThreshold != value) { _spinYawRateThreshold = value; OnPropertyChanged(); } } }
        private double _trafficApproachWarnSeconds = 5.0;
        public double TrafficApproachWarnSeconds { get => _trafficApproachWarnSeconds; set { if (_trafficApproachWarnSeconds != value) { _trafficApproachWarnSeconds = value; OnPropertyChanged(); } } }

        // --- Helper methods (unchanged and preserved) ---

        public TrackStats FindTrack(string trackKey)
        {
            if (string.IsNullOrWhiteSpace(trackKey) || TrackStats == null) return null;

            // Simple, direct lookup using the TrackCode as the key.
            TrackStats.TryGetValue(trackKey, out var trackRecord);
            return trackRecord;
        }

        public TrackStats ResolveTrackByNameOrKey(string nameOrKey)
        {
            if (string.IsNullOrWhiteSpace(nameOrKey) || TrackStats == null) return null;

            // 1) Try as dictionary key (your canonical ID)
            var ts = FindTrack(nameOrKey);
            if (ts != null) return ts;

            // 2) Fallback by display name (case-insensitive)
            return TrackStats.Values
                .FirstOrDefault(t => t.DisplayName?.Equals(nameOrKey, StringComparison.OrdinalIgnoreCase) == true);
        }


        public TrackStats EnsureTrack(string trackKey, string trackDisplay)
        {
            if (string.IsNullOrWhiteSpace(trackKey)) return null;

            if (TrackStats == null)
            {
                TrackStats = new Dictionary<string, TrackStats>(StringComparer.OrdinalIgnoreCase);
            }

            // Try to find an existing record using the reliable key.
            if (TrackStats.TryGetValue(trackKey, out var existingRecord))
            {
                // Record found. Just update its DisplayName in case it has changed.
                existingRecord.DisplayName = trackDisplay;
                return existingRecord;
            }
            else
            {
                // No record found. Create a new one.
                var newRecord = new TrackStats
                {
                    Key = trackKey,
                    DisplayName = trackDisplay
                };
                TrackStats[trackKey] = newRecord;
                return newRecord;
            }
        }
    }
    public class TrackStats : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // --- Helper for String-to-Double/Int Conversion ---
        public double? StringToNullableDouble(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (double.TryParse(s.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d))
                return d;
            return null;
        }
        public string MillisecondsToLapTimeString(int? milliseconds)
        {
            if (!milliseconds.HasValue) return string.Empty;
            return TimeSpan.FromMilliseconds(milliseconds.Value).ToString(@"m\:ss\.fff");
        }

        public int? LapTimeStringToMilliseconds(string timeString)
        {
            if (string.IsNullOrWhiteSpace(timeString)) return null;

            // Use the same robust parsing from FuelCalcs, but convert to TotalMilliseconds
            string[] formats = { @"m\:ss\.fff", @"m\:ss\.ff", @"m\:ss\.f", @"m\:ss" };
            if (TimeSpan.TryParseExact(timeString.Trim(), formats, System.Globalization.CultureInfo.InvariantCulture, TimeSpanStyles.None, out var ts))
            {
                return (int)ts.TotalMilliseconds;
            }
            return null; // Return null if parsing fails
        }

        // --- Core Data ---
        private string _displayName;
        [JsonProperty] public string DisplayName { get => _displayName; set { if (_displayName != value) { _displayName = value; OnPropertyChanged(); } } }
        private string _key;
        [JsonProperty] public string Key { get => _key; set { if (_key != value) { _key = value; OnPropertyChanged(); } } }

        private int? _bestLapMs;
        private string _bestLapMsText;
        private bool _suppressBestLapSync = false;

        [JsonProperty]
        public int? BestLapMs
        {
            get => _bestLapMs;
            set
            {
                if (_bestLapMs != value)
                {
                    _bestLapMs = value;
                    OnPropertyChanged();
                    if (!_suppressBestLapSync)
                    {
                        BestLapMsText = MillisecondsToLapTimeString(_bestLapMs);
                    }
                }
            }
        }

        public string BestLapMsText
        {
            get => _bestLapMsText;
            set
            {
                if (_bestLapMsText != value)
                {
                    _bestLapMsText = value;
                    OnPropertyChanged();
                    _suppressBestLapSync = true;
                    BestLapMs = LapTimeStringToMilliseconds(value);
                    _suppressBestLapSync = false;
                }
            }
        }
        private double? _pitLaneLossSeconds;
        [JsonProperty] public double? PitLaneLossSeconds { get => _pitLaneLossSeconds; set { if (_pitLaneLossSeconds != value) { _pitLaneLossSeconds = value; OnPropertyChanged(); OnPropertyChanged(nameof(PitLaneLossSecondsText)); } } }
        public string PitLaneLossSecondsText { get => _pitLaneLossSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture); set => PitLaneLossSeconds = StringToNullableDouble(value); }

        /// --- Dry Conditions Data ---
        private double? _avgFuelPerLapDry;
        private string _avgFuelPerLapDryText;
        private bool _suppressDryFuelSync = false;

        [JsonProperty]
        public double? AvgFuelPerLapDry
        {
            get => _avgFuelPerLapDry;
            set
            {
                if (_avgFuelPerLapDry != value)
                {
                    _avgFuelPerLapDry = value;
                    OnPropertyChanged();
                    if (!_suppressDryFuelSync)
                    {
                        AvgFuelPerLapDryText = _avgFuelPerLapDry?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
            }
        }

        public string AvgFuelPerLapDryText
        {
            get => _avgFuelPerLapDryText;
            set
            {
                if (_avgFuelPerLapDryText != value)
                {
                    _avgFuelPerLapDryText = value;
                    OnPropertyChanged();
                    var parsedValue = StringToNullableDouble(value);
                    if (parsedValue.HasValue)
                    {
                        _suppressDryFuelSync = true;
                        AvgFuelPerLapDry = parsedValue;
                        _suppressDryFuelSync = false;
                    }
                }
            }
        }
        private int? _dryFuelSampleCount;
        [JsonProperty] public int? DryFuelSampleCount { get => _dryFuelSampleCount; set { if (_dryFuelSampleCount != value) { _dryFuelSampleCount = value; OnPropertyChanged(); } } }

        private int? _avgLapTimeDry;
        private string _avgLapTimeDryText;
        private bool _suppressAvgLapDrySync = false;

        [JsonProperty]
        public int? AvgLapTimeDry
        {
            get => _avgLapTimeDry;
            set
            {
                if (_avgLapTimeDry != value)
                {
                    _avgLapTimeDry = value;
                    OnPropertyChanged();
                    if (!_suppressAvgLapDrySync)
                    {
                        AvgLapTimeDryText = MillisecondsToLapTimeString(_avgLapTimeDry);
                    }
                }
            }
        }

        public string AvgLapTimeDryText
        {
            get => _avgLapTimeDryText;
            set
            {
                if (_avgLapTimeDryText != value)
                {
                    _avgLapTimeDryText = value;
                    OnPropertyChanged();
                    _suppressAvgLapDrySync = true;
                    AvgLapTimeDry = LapTimeStringToMilliseconds(value);
                    _suppressAvgLapDrySync = false;
                }
            }
        }
        private int? _dryLapTimeSampleCount;
        [JsonProperty] public int? DryLapTimeSampleCount { get => _dryLapTimeSampleCount; set { if (_dryLapTimeSampleCount != value) { _dryLapTimeSampleCount = value; OnPropertyChanged(); } } }

        private double? _avgDryTrackTemp;
        [JsonProperty] public double? AvgDryTrackTemp { get => _avgDryTrackTemp; set { if (_avgDryTrackTemp != value) { _avgDryTrackTemp = value; OnPropertyChanged(); OnPropertyChanged(nameof(AvgDryTrackTempText)); } } }
        public string AvgDryTrackTempText { get => _avgDryTrackTemp?.ToString(System.Globalization.CultureInfo.InvariantCulture); set => AvgDryTrackTemp = StringToNullableDouble(value); }

        // --- Wet Conditions Data ---
        private double? _avgFuelPerLapWet;
        private string _avgFuelPerLapWetText;
        private bool _suppressWetFuelSync = false;

        [JsonProperty]
        public double? AvgFuelPerLapWet
        {
            get => _avgFuelPerLapWet;
            set
            {
                if (_avgFuelPerLapWet != value)
                {
                    _avgFuelPerLapWet = value;
                    OnPropertyChanged();
                    if (!_suppressWetFuelSync)
                    {
                        AvgFuelPerLapWetText = _avgFuelPerLapWet?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
            }
        }

        public string AvgFuelPerLapWetText
        {
            get => _avgFuelPerLapWetText;
            set
            {
                if (_avgFuelPerLapWetText != value)
                {
                    _avgFuelPerLapWetText = value;
                    OnPropertyChanged();
                    var parsedValue = StringToNullableDouble(value);
                    if (parsedValue.HasValue)
                    {
                        _suppressWetFuelSync = true;
                        AvgFuelPerLapWet = parsedValue;
                        _suppressWetFuelSync = false;
                    }
                }
            }
        }
        private int? _wetFuelSampleCount;
        [JsonProperty] public int? WetFuelSampleCount { get => _wetFuelSampleCount; set { if (_wetFuelSampleCount != value) { _wetFuelSampleCount = value; OnPropertyChanged(); } } }

        private int? _avgLapTimeWet;
        private string _avgLapTimeWetText;
        private bool _suppressAvgLapWetSync = false;

        [JsonProperty]
        public int? AvgLapTimeWet
        {
            get => _avgLapTimeWet;
            set
            {
                if (_avgLapTimeWet != value)
                {
                    _avgLapTimeWet = value;
                    OnPropertyChanged();
                    if (!_suppressAvgLapWetSync)
                    {
                        AvgLapTimeWetText = MillisecondsToLapTimeString(_avgLapTimeWet);
                    }
                }
            }
        }

        public string AvgLapTimeWetText
        {
            get => _avgLapTimeWetText;
            set
            {
                if (_avgLapTimeWetText != value)
                {
                    _avgLapTimeWetText = value;
                    OnPropertyChanged();
                    _suppressAvgLapWetSync = true;
                    AvgLapTimeWet = LapTimeStringToMilliseconds(value);
                    _suppressAvgLapWetSync = false;
                }
            }
        }
        private int? _wetLapTimeSampleCount;
        [JsonProperty] public int? WetLapTimeSampleCount { get => _wetLapTimeSampleCount; set { if (_wetLapTimeSampleCount != value) { _wetLapTimeSampleCount = value; OnPropertyChanged(); } } }

        private double? _avgWetTrackTemp;
        [JsonProperty] public double? AvgWetTrackTemp { get => _avgWetTrackTemp; set { if (_avgWetTrackTemp != value) { _avgWetTrackTemp = value; OnPropertyChanged(); OnPropertyChanged(nameof(AvgWetTrackTempText)); } } }
        public string AvgWetTrackTempText { get => _avgWetTrackTemp?.ToString(System.Globalization.CultureInfo.InvariantCulture); set => AvgWetTrackTemp = StringToNullableDouble(value); }
    }
}