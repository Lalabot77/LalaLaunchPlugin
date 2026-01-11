// In file: CarProfiles.cs
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
        public double WetFuelMultiplier
        {
            get => _wetFuelMultiplier;
            set
            {
                if (_wetFuelMultiplier != value)
                {
                    _wetFuelMultiplier = value;
                    OnPropertyChanged();

                    // Keep the legacy wet multiplier in sync with the condition overrides
                    if (WetConditionMultipliers == null)
                    {
                        WetConditionMultipliers = ConditionMultipliers.CreateDefaultWet();
                    }
                    WetConditionMultipliers.WetFactorPercent = value;
                }
            }
        }
        private double _tireChangeTime = 22;
        public double TireChangeTime { get => _tireChangeTime; set { if (_tireChangeTime != value) { _tireChangeTime = value; OnPropertyChanged(); } } }
        private double _racePaceDeltaSeconds = 1.2;
        public double RacePaceDeltaSeconds { get => _racePaceDeltaSeconds; set { if (_racePaceDeltaSeconds != value) { _racePaceDeltaSeconds = value; OnPropertyChanged(); } } }

        // --- NEW Per-Car Property ---
        private double _refuelRate = 2.7;
        public double RefuelRate { get => _refuelRate; set { if (_refuelRate != value) { _refuelRate = value; OnPropertyChanged(); } } }

        private ConditionMultipliers _dryConditionMultipliers = ConditionMultipliers.CreateDefaultDry();
        private ConditionMultipliers _wetConditionMultipliers = ConditionMultipliers.CreateDefaultWet();

        [JsonProperty]
        public ConditionMultipliers DryConditionMultipliers
        {
            get => _dryConditionMultipliers;
            set
            {
                var next = value ?? ConditionMultipliers.CreateDefaultDry();
                if (!ReferenceEquals(_dryConditionMultipliers, next))
                {
                    _dryConditionMultipliers = next;
                    OnPropertyChanged();
                }
            }
        }

        [JsonProperty]
        public ConditionMultipliers WetConditionMultipliers
        {
            get => _wetConditionMultipliers;
            set
            {
                var next = value ?? ConditionMultipliers.CreateDefaultWet();
                if (!ReferenceEquals(_wetConditionMultipliers, next))
                {
                    _wetConditionMultipliers = next;
                    OnPropertyChanged();
                }
            }
        }

        public ConditionMultipliers GetConditionMultipliers(bool isWet)
        {
            return isWet
                ? (WetConditionMultipliers ?? ConditionMultipliers.CreateDefaultWet())
                : (DryConditionMultipliers ?? ConditionMultipliers.CreateDefaultDry());
        }

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
        private double _pitEntryDecelMps2 = 13.5;
        public double PitEntryDecelMps2 { get => _pitEntryDecelMps2; set { if (_pitEntryDecelMps2 != value) { _pitEntryDecelMps2 = value; OnPropertyChanged(); } } }
        private double _pitEntryBufferM = 15.0;
        public double PitEntryBufferM { get => _pitEntryBufferM; set { if (_pitEntryBufferM != value) { _pitEntryBufferM = value; OnPropertyChanged(); } } }

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


            // 1) Try as key (direct)
            var ts = FindTrack(nameOrKey);
            if (ts != null) return ts;

            // 2) Fallback: match by DisplayName (case-insensitive)

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
                    DisplayName = trackDisplay,
                    DryConditionMultipliers = ConditionMultipliers.CreateDefaultDry(),
                    WetConditionMultipliers = ConditionMultipliers.CreateDefaultWet()
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

        [JsonIgnore]
        public Action RequestSaveProfiles { get; set; }


        // --- Core Data ---
        private string _displayName;
        [JsonProperty] public string DisplayName { get => _displayName; set { if (_displayName != value) { _displayName = value; OnPropertyChanged(); } } }
        private string _key;
        [JsonProperty] public string Key { get => _key; set { if (_key != value) { _key = value; OnPropertyChanged(); } } }

        private int? _bestLapMs;
        private string _bestLapMsText;
        private bool _suppressBestLapSync = false;
        private int? _bestLapMsDry;
        private string _bestLapMsDryText;
        private bool _suppressBestLapDrySync = false;
        private int? _bestLapMsWet;
        private string _bestLapMsWetText;
        private bool _suppressBestLapWetSync = false;

        [JsonProperty]
        public int? BestLapMs
        {
            get => _bestLapMs;
            set
            {
                if (_bestLapMs != value)
                {
                    var old = _bestLapMs;
                    _bestLapMs = value;
                    OnPropertyChanged();

                    // LOG: PB changed (covers live PB and manual text edits)
                    try
                    {
                        SimHub.Logging.Current.Info(
                            $"[LalaPlugin:Profile/Pace] PB updated for track '{DisplayName ?? "(null)"}' ({Key ?? "(null)"}): " +
                            $"'{MillisecondsToLapTimeString(old)}' -> '{MillisecondsToLapTimeString(_bestLapMs)}'"
                        );
                    }
                    catch { /* logging must never throw */ }

                    if (!_suppressBestLapSync)
                    {
                        BestLapMsText = MillisecondsToLapTimeString(_bestLapMs);
                    }
                    OnPropertyChanged(nameof(BestLapTimeDryText));
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

        [JsonProperty]
        public int? BestLapMsDry
        {
            get => _bestLapMsDry;
            set
            {
                if (_bestLapMsDry != value)
                {
                    var old = _bestLapMsDry;
                    _bestLapMsDry = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BestLapTimeDryText));

                    try
                    {
                        SimHub.Logging.Current.Info(
                            $"[LalaPlugin:Profile/Pace] PB Dry updated for track '{DisplayName ?? "(null)"}' ({Key ?? "(null)"}): " +
                            $"'{MillisecondsToLapTimeString(old)}' -> '{MillisecondsToLapTimeString(_bestLapMsDry)}'"
                        );
                    }
                    catch { }

                    if (!_suppressBestLapDrySync)
                    {
                        BestLapTimeDryText = MillisecondsToLapTimeString(_bestLapMsDry);
                    }
                }
            }
        }

        public string BestLapTimeDryText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_bestLapMsDryText)
                    && (!BestLapMsDry.HasValue || BestLapMsDry.Value <= 0)
                    && BestLapMs.HasValue && BestLapMs.Value > 0)
                {
                    return MillisecondsToLapTimeString(BestLapMs);
                }
                return _bestLapMsDryText;
            }
            set
            {
                if (_bestLapMsDryText != value)
                {
                    _bestLapMsDryText = value;
                    OnPropertyChanged();
                    _suppressBestLapDrySync = true;
                    BestLapMsDry = LapTimeStringToMilliseconds(value);
                    _suppressBestLapDrySync = false;
                }
            }
        }

        [JsonProperty]
        public int? BestLapMsWet
        {
            get => _bestLapMsWet;
            set
            {
                if (_bestLapMsWet != value)
                {
                    var old = _bestLapMsWet;
                    _bestLapMsWet = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BestLapTimeWetText));

                    try
                    {
                        SimHub.Logging.Current.Info(
                            $"[LalaPlugin:Profile/Pace] PB Wet updated for track '{DisplayName ?? "(null)"}' ({Key ?? "(null)"}): " +
                            $"'{MillisecondsToLapTimeString(old)}' -> '{MillisecondsToLapTimeString(_bestLapMsWet)}'"
                        );
                    }
                    catch { }

                    if (!_suppressBestLapWetSync)
                    {
                        BestLapTimeWetText = MillisecondsToLapTimeString(_bestLapMsWet);
                    }
                }
            }
        }

        public string BestLapTimeWetText
        {
            get => _bestLapMsWetText;
            set
            {
                if (_bestLapMsWetText != value)
                {
                    _bestLapMsWetText = value;
                    OnPropertyChanged();
                    _suppressBestLapWetSync = true;
                    BestLapMsWet = LapTimeStringToMilliseconds(value);
                    _suppressBestLapWetSync = false;
                }
            }
        }

        public int? GetBestLapMsForCondition(bool isWetEffective)
        {
            if (isWetEffective)
            {
                if (BestLapMsWet.HasValue && BestLapMsWet.Value > 0) return BestLapMsWet;
                if (BestLapMsDry.HasValue && BestLapMsDry.Value > 0) return BestLapMsDry;
                if (BestLapMs.HasValue && BestLapMs.Value > 0) return BestLapMs;
                return null;
            }

            if (BestLapMsDry.HasValue && BestLapMsDry.Value > 0) return BestLapMsDry;
            if (BestLapMs.HasValue && BestLapMs.Value > 0) return BestLapMs;
            return null;
        }
        private double? _pitLaneLossSeconds;
        [JsonProperty] public double? PitLaneLossSeconds { get => _pitLaneLossSeconds; set { if (_pitLaneLossSeconds != value) { _pitLaneLossSeconds = value; OnPropertyChanged(); OnPropertyChanged(nameof(PitLaneLossSecondsText)); } } }
        public string PitLaneLossSecondsText
        {
            get => PitLaneLossSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            set
            {
                var parsed = StringToNullableDouble(value);
                double? rounded = parsed.HasValue ? (double?)Math.Round(parsed.Value, 2) : null;

                if (PitLaneLossSeconds != rounded)
                {
                    PitLaneLossSeconds = rounded;
                    PitLaneLossSource = "manual";
                    PitLaneLossUpdatedUtc = DateTime.UtcNow;
                    RequestSaveProfiles?.Invoke();
                }
            }
        }

        private bool _pitLaneLossLocked;
        [JsonProperty]
        public bool PitLaneLossLocked
        {
            get => _pitLaneLossLocked;
            set
            {
                if (_pitLaneLossLocked != value)
                {
                    _pitLaneLossLocked = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PitLaneLossBlockedCandidateDisplay));
                }
            }
        }

        private bool _dryConditionsLocked;
        [JsonProperty]
        public bool DryConditionsLocked
        {
            get => _dryConditionsLocked;
            set
            {
                if (_dryConditionsLocked != value)
                {
                    _dryConditionsLocked = value;
                    OnPropertyChanged();
                    RequestSaveProfiles?.Invoke();
                }
            }
        }

        private bool _wetConditionsLocked;
        [JsonProperty]
        public bool WetConditionsLocked
        {
            get => _wetConditionsLocked;
            set
            {
                if (_wetConditionsLocked != value)
                {
                    _wetConditionsLocked = value;
                    OnPropertyChanged();
                    RequestSaveProfiles?.Invoke();
                }
            }
        }

        private double _pitLaneLossBlockedCandidateSeconds;
        [JsonProperty]
        public double PitLaneLossBlockedCandidateSeconds
        {
            get => _pitLaneLossBlockedCandidateSeconds;
            set
            {
                if (Math.Abs(_pitLaneLossBlockedCandidateSeconds - value) > 0.0001)
                {
                    _pitLaneLossBlockedCandidateSeconds = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PitLaneLossBlockedCandidateDisplay));
                }
            }
        }

        private DateTime? _pitLaneLossBlockedCandidateUpdatedUtc;
        [JsonProperty]
        public DateTime? PitLaneLossBlockedCandidateUpdatedUtc
        {
            get => _pitLaneLossBlockedCandidateUpdatedUtc;
            set
            {
                if (_pitLaneLossBlockedCandidateUpdatedUtc != value)
                {
                    _pitLaneLossBlockedCandidateUpdatedUtc = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PitLaneLossBlockedCandidateDisplay));
                }
            }
        }

        private string _pitLaneLossBlockedCandidateSource;
        [JsonProperty]
        public string PitLaneLossBlockedCandidateSource
        {
            get => _pitLaneLossBlockedCandidateSource;
            set
            {
                if (_pitLaneLossBlockedCandidateSource != value)
                {
                    _pitLaneLossBlockedCandidateSource = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PitLaneLossBlockedCandidateDisplay));
                }
            }
        }

        [JsonIgnore]
        public string PitLaneLossBlockedCandidateDisplay
        {
            get
            {
                if (!PitLaneLossLocked) return string.Empty;
                if (PitLaneLossBlockedCandidateSeconds <= 0) return string.Empty;
                if (!PitLaneLossBlockedCandidateUpdatedUtc.HasValue) return string.Empty;
                var source = string.IsNullOrWhiteSpace(PitLaneLossBlockedCandidateSource)
                    ? "unknown"
                    : PitLaneLossBlockedCandidateSource;
                return $"Blocked candidate: {PitLaneLossBlockedCandidateSeconds:0.0}s @ {PitLaneLossBlockedCandidateUpdatedUtc.Value:yyyy-MM-dd HH:mm} ({source})";
            }
        }

        private ConditionMultipliers _dryConditionMultipliers = ConditionMultipliers.CreateDefaultDry();
        private ConditionMultipliers _wetConditionMultipliers = ConditionMultipliers.CreateDefaultWet();

        [JsonProperty]
        public ConditionMultipliers DryConditionMultipliers
        {
            get => _dryConditionMultipliers;
            set
            {
                var next = value ?? ConditionMultipliers.CreateDefaultDry();
                if (!ReferenceEquals(_dryConditionMultipliers, next))
                {
                    _dryConditionMultipliers = next;
                    OnPropertyChanged();
                }
            }
        }

        [JsonProperty]
        public ConditionMultipliers WetConditionMultipliers
        {
            get => _wetConditionMultipliers;
            set
            {
                var next = value ?? ConditionMultipliers.CreateDefaultWet();
                if (!ReferenceEquals(_wetConditionMultipliers, next))
                {
                    _wetConditionMultipliers = next;
                    OnPropertyChanged();
                }
            }
        }

        public ConditionMultipliers GetConditionMultipliers(bool isWet)
        {
            return isWet
                ? (WetConditionMultipliers ?? ConditionMultipliers.CreateDefaultWet())
                : (DryConditionMultipliers ?? ConditionMultipliers.CreateDefaultDry());
        }

        private string _pitLaneLossSource;
        [JsonProperty]
        public string PitLaneLossSource
        {
            get => _pitLaneLossSource;
            set { if (_pitLaneLossSource != value) { _pitLaneLossSource = value; OnPropertyChanged(); } }
        }

        private DateTime? _pitLaneLossUpdatedUtc;
        [JsonProperty]
        public DateTime? PitLaneLossUpdatedUtc
        {
            get => _pitLaneLossUpdatedUtc;
            set { if (_pitLaneLossUpdatedUtc != value) { _pitLaneLossUpdatedUtc = value; OnPropertyChanged(); } }
        }

        private string _fuelUpdatedSource;
        [JsonProperty]
        public string FuelUpdatedSource
        {
            get => _fuelUpdatedSource;
            set
            {
                if (_fuelUpdatedSource != value)
                {
                    _fuelUpdatedSource = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FuelLastUpdatedText));
                    OnPropertyChanged(nameof(DryFuelLastUpdatedText));
                    OnPropertyChanged(nameof(WetFuelLastUpdatedText));
                }
            }
        }

        private DateTime? _fuelUpdatedUtc;
        [JsonProperty]
        public DateTime? FuelUpdatedUtc
        {
            get => _fuelUpdatedUtc;
            set
            {
                if (_fuelUpdatedUtc != value)
                {
                    _fuelUpdatedUtc = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FuelLastUpdatedText));
                    OnPropertyChanged(nameof(DryFuelLastUpdatedText));
                    OnPropertyChanged(nameof(WetFuelLastUpdatedText));
                }
            }
        }

        private string _dryFuelUpdatedSource;
        [JsonProperty]
        public string DryFuelUpdatedSource
        {
            get => _dryFuelUpdatedSource;
            set
            {
                if (_dryFuelUpdatedSource != value)
                {
                    _dryFuelUpdatedSource = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DryFuelLastUpdatedText));
                }
            }
        }

        private DateTime? _dryFuelUpdatedUtc;
        [JsonProperty]
        public DateTime? DryFuelUpdatedUtc
        {
            get => _dryFuelUpdatedUtc;
            set
            {
                if (_dryFuelUpdatedUtc != value)
                {
                    _dryFuelUpdatedUtc = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DryFuelLastUpdatedText));
                }
            }
        }

        private string _wetFuelUpdatedSource;
        [JsonProperty]
        public string WetFuelUpdatedSource
        {
            get => _wetFuelUpdatedSource;
            set
            {
                if (_wetFuelUpdatedSource != value)
                {
                    _wetFuelUpdatedSource = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(WetFuelLastUpdatedText));
                }
            }
        }

        private DateTime? _wetFuelUpdatedUtc;
        [JsonProperty]
        public DateTime? WetFuelUpdatedUtc
        {
            get => _wetFuelUpdatedUtc;
            set
            {
                if (_wetFuelUpdatedUtc != value)
                {
                    _wetFuelUpdatedUtc = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(WetFuelLastUpdatedText));
                }
            }
        }

        [JsonIgnore]
        public string FuelLastUpdatedText
        {
            get
            {
                if (!_fuelUpdatedUtc.HasValue) return string.Empty;
                var sourceLabel = string.IsNullOrWhiteSpace(_fuelUpdatedSource)
                    ? "Last updated"
                    : _fuelUpdatedSource;
                return $"{sourceLabel}: {_fuelUpdatedUtc.Value:yyyy-MM-dd HH:mm}";
            }
        }

        [JsonIgnore]
        public string DryFuelLastUpdatedText
        {
            get
            {
                if (_dryFuelUpdatedUtc.HasValue)
                {
                    var sourceLabel = string.IsNullOrWhiteSpace(_dryFuelUpdatedSource)
                        ? "Last updated"
                        : _dryFuelUpdatedSource;
                    return $"{sourceLabel}: {_dryFuelUpdatedUtc.Value:yyyy-MM-dd HH:mm}";
                }

                if (!_fuelUpdatedUtc.HasValue) return string.Empty;
                var legacySourceLabel = string.IsNullOrWhiteSpace(_fuelUpdatedSource)
                    ? "Last updated"
                    : _fuelUpdatedSource;
                return $"{legacySourceLabel}: {_fuelUpdatedUtc.Value:yyyy-MM-dd HH:mm}";
            }
        }

        [JsonIgnore]
        public string WetFuelLastUpdatedText
        {
            get
            {
                if (_wetFuelUpdatedUtc.HasValue)
                {
                    var sourceLabel = string.IsNullOrWhiteSpace(_wetFuelUpdatedSource)
                        ? "Last updated"
                        : _wetFuelUpdatedSource;
                    return $"{sourceLabel}: {_wetFuelUpdatedUtc.Value:yyyy-MM-dd HH:mm}";
                }

                if (!_fuelUpdatedUtc.HasValue) return string.Empty;
                var legacySourceLabel = string.IsNullOrWhiteSpace(_fuelUpdatedSource)
                    ? "Last updated"
                    : _fuelUpdatedSource;
                return $"{legacySourceLabel}: {_fuelUpdatedUtc.Value:yyyy-MM-dd HH:mm}";
            }
        }

        public void MarkFuelUpdated(string source, DateTime? whenUtc = null)
        {
            FuelUpdatedSource = source;
            FuelUpdatedUtc = whenUtc ?? DateTime.UtcNow;
        }

        public void MarkFuelUpdatedDry(string source, DateTime? whenUtc = null)
        {
            DryFuelUpdatedSource = source;
            DryFuelUpdatedUtc = whenUtc ?? DateTime.UtcNow;
        }

        public void MarkFuelUpdatedWet(string source, DateTime? whenUtc = null)
        {
            WetFuelUpdatedSource = source;
            WetFuelUpdatedUtc = whenUtc ?? DateTime.UtcNow;
        }

        public void RelearnPitLoss()
        {
            PitLaneLossSeconds = null;
            PitLaneLossSource = null;
            PitLaneLossUpdatedUtc = null;
            PitLaneLossBlockedCandidateSeconds = 0;
            PitLaneLossBlockedCandidateSource = null;
            PitLaneLossBlockedCandidateUpdatedUtc = null;
        }

        public void RelearnDryConditions()
        {
            _suppressBestLapDrySync = true;
            BestLapMsDry = 0;
            _suppressBestLapDrySync = false;
            _bestLapMsDryText = string.Empty;
            OnPropertyChanged(nameof(BestLapTimeDryText));

            _suppressAvgLapDrySync = true;
            AvgLapTimeDry = 0;
            _suppressAvgLapDrySync = false;
            _avgLapTimeDryText = string.Empty;
            OnPropertyChanged(nameof(AvgLapTimeDryText));

            _suppressDryMinFuelSync = true;
            MinFuelPerLapDry = null;
            _suppressDryMinFuelSync = false;
            _minFuelPerLapDryText = string.Empty;
            OnPropertyChanged(nameof(MinFuelPerLapDryText));

            _suppressDryFuelSync = true;
            AvgFuelPerLapDry = null;
            _suppressDryFuelSync = false;
            _avgFuelPerLapDryText = string.Empty;
            OnPropertyChanged(nameof(AvgFuelPerLapDryText));

            _suppressDryMaxFuelSync = true;
            MaxFuelPerLapDry = null;
            _suppressDryMaxFuelSync = false;
            _maxFuelPerLapDryText = string.Empty;
            OnPropertyChanged(nameof(MaxFuelPerLapDryText));

            DryLapTimeSampleCount = 0;
            DryFuelSampleCount = 0;
            DryFuelUpdatedSource = null;
            DryFuelUpdatedUtc = null;
        }

        public void RelearnWetConditions()
        {
            _suppressBestLapWetSync = true;
            BestLapMsWet = 0;
            _suppressBestLapWetSync = false;
            _bestLapMsWetText = string.Empty;
            OnPropertyChanged(nameof(BestLapTimeWetText));

            _suppressAvgLapWetSync = true;
            AvgLapTimeWet = 0;
            _suppressAvgLapWetSync = false;
            _avgLapTimeWetText = string.Empty;
            OnPropertyChanged(nameof(AvgLapTimeWetText));

            _suppressWetMinFuelSync = true;
            MinFuelPerLapWet = null;
            _suppressWetMinFuelSync = false;
            _minFuelPerLapWetText = string.Empty;
            OnPropertyChanged(nameof(MinFuelPerLapWetText));

            _suppressWetFuelSync = true;
            AvgFuelPerLapWet = null;
            _suppressWetFuelSync = false;
            _avgFuelPerLapWetText = string.Empty;
            OnPropertyChanged(nameof(AvgFuelPerLapWetText));

            _suppressWetMaxFuelSync = true;
            MaxFuelPerLapWet = null;
            _suppressWetMaxFuelSync = false;
            _maxFuelPerLapWetText = string.Empty;
            OnPropertyChanged(nameof(MaxFuelPerLapWetText));

            WetLapTimeSampleCount = 0;
            WetFuelSampleCount = 0;
            WetFuelUpdatedSource = null;
            WetFuelUpdatedUtc = null;
        }


        /// --- Dry Conditions Data ---
        private double? _avgFuelPerLapDry;
        private string _avgFuelPerLapDryText;
        private bool _suppressDryFuelSync = false;

        private double? _minFuelPerLapDry;
        private string _minFuelPerLapDryText;
        private bool _suppressDryMinFuelSync = false;

        private double? _maxFuelPerLapDry;
        private string _maxFuelPerLapDryText;
        private bool _suppressDryMaxFuelSync = false;

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
                    NotifyWetVsDryDeltasChanged();
                    if (!_suppressDryFuelSync)
                    {
                        AvgFuelPerLapDryText = _avgFuelPerLapDry?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
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
                        if (!_suppressDryFuelSync)
                        {
                            MarkFuelUpdatedDry("Manual fuel edit");
                        }
                        _suppressDryFuelSync = true;
                        AvgFuelPerLapDry = parsedValue;
                        _suppressDryFuelSync = false;
                    }
                }
            }
        }

        [JsonProperty]
        public double? MinFuelPerLapDry
        {
            get => _minFuelPerLapDry;
            set
            {
                if (_minFuelPerLapDry != value)
                {
                    _minFuelPerLapDry = value;
                    OnPropertyChanged();
                    if (!_suppressDryMinFuelSync)
                    {
                        MinFuelPerLapDryText = _minFuelPerLapDry?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
            }
        }

        public string MinFuelPerLapDryText
        {
            get => _minFuelPerLapDryText;
            set
            {
                if (_minFuelPerLapDryText != value)
                {
                    _minFuelPerLapDryText = value;
                    OnPropertyChanged();
                    var parsedValue = StringToNullableDouble(value);
                    if (parsedValue.HasValue)
                    {
                        _suppressDryMinFuelSync = true;
                        MinFuelPerLapDry = parsedValue;
                        _suppressDryMinFuelSync = false;
                    }
                }
            }
        }

        [JsonProperty]
        public double? MaxFuelPerLapDry
        {
            get => _maxFuelPerLapDry;
            set
            {
                if (_maxFuelPerLapDry != value)
                {
                    _maxFuelPerLapDry = value;
                    OnPropertyChanged();
                    if (!_suppressDryMaxFuelSync)
                    {
                        MaxFuelPerLapDryText = _maxFuelPerLapDry?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
            }
        }

        public string MaxFuelPerLapDryText
        {
            get => _maxFuelPerLapDryText;
            set
            {
                if (_maxFuelPerLapDryText != value)
                {
                    _maxFuelPerLapDryText = value;
                    OnPropertyChanged();
                    var parsedValue = StringToNullableDouble(value);
                    if (parsedValue.HasValue)
                    {
                        _suppressDryMaxFuelSync = true;
                        MaxFuelPerLapDry = parsedValue;
                        _suppressDryMaxFuelSync = false;
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
                    var old = _avgLapTimeDry;
                    _avgLapTimeDry = value;
                    OnPropertyChanged();
                    NotifyWetVsDryDeltasChanged();

                    // LOG: Avg dry lap changed
                    try
                    {
                        SimHub.Logging.Current.Info(
                            $"[LalaPlugin:Profile/Pace] AvgDry updated for track '{DisplayName ?? "(null)"}' ({Key ?? "(null)"}): " +
                            $"'{MillisecondsToLapTimeString(old)}' -> '{MillisecondsToLapTimeString(_avgLapTimeDry)}'"
                        );
                    }
                    catch { }

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

        private double? _minFuelPerLapWet;
        private string _minFuelPerLapWetText;
        private bool _suppressWetMinFuelSync = false;

        private double? _maxFuelPerLapWet;
        private string _maxFuelPerLapWetText;
        private bool _suppressWetMaxFuelSync = false;

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
                    NotifyWetVsDryDeltasChanged();
                    if (!_suppressWetFuelSync)
                    {
                        AvgFuelPerLapWetText = _avgFuelPerLapWet?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
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
                        if (!_suppressWetFuelSync)
                        {
                            MarkFuelUpdatedWet("Manual fuel edit");
                        }
                        _suppressWetFuelSync = true;
                        AvgFuelPerLapWet = parsedValue;
                        _suppressWetFuelSync = false;
                    }
                }
            }
        }

        [JsonProperty]
        public double? MinFuelPerLapWet
        {
            get => _minFuelPerLapWet;
            set
            {
                if (_minFuelPerLapWet != value)
                {
                    _minFuelPerLapWet = value;
                    OnPropertyChanged();
                    if (!_suppressWetMinFuelSync)
                    {
                        MinFuelPerLapWetText = _minFuelPerLapWet?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
            }
        }

        public string MinFuelPerLapWetText
        {
            get => _minFuelPerLapWetText;
            set
            {
                if (_minFuelPerLapWetText != value)
                {
                    _minFuelPerLapWetText = value;
                    OnPropertyChanged();
                    var parsedValue = StringToNullableDouble(value);
                    if (parsedValue.HasValue)
                    {
                        _suppressWetMinFuelSync = true;
                        MinFuelPerLapWet = parsedValue;
                        _suppressWetMinFuelSync = false;
                    }
                }
            }
        }

        [JsonProperty]
        public double? MaxFuelPerLapWet
        {
            get => _maxFuelPerLapWet;
            set
            {
                if (_maxFuelPerLapWet != value)
                {
                    _maxFuelPerLapWet = value;
                    OnPropertyChanged();
                    if (!_suppressWetMaxFuelSync)
                    {
                        MaxFuelPerLapWetText = _maxFuelPerLapWet?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
            }
        }

        public string MaxFuelPerLapWetText
        {
            get => _maxFuelPerLapWetText;
            set
            {
                if (_maxFuelPerLapWetText != value)
                {
                    _maxFuelPerLapWetText = value;
                    OnPropertyChanged();
                    var parsedValue = StringToNullableDouble(value);
                    if (parsedValue.HasValue)
                    {
                        _suppressWetMaxFuelSync = true;
                        MaxFuelPerLapWet = parsedValue;
                        _suppressWetMaxFuelSync = false;
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
                    var old = _avgLapTimeWet;
                    _avgLapTimeWet = value;
                    OnPropertyChanged();
                    NotifyWetVsDryDeltasChanged();

                    // LOG: Avg wet lap changed
                    try
                    {
                        SimHub.Logging.Current.Info(
                            $"[LalaPlugin:Profile / Pace] AvgWet updated for track '{DisplayName ?? "(null)"}' ({Key ?? "(null)"}): " +
                            $"'{MillisecondsToLapTimeString(old)}' -> '{MillisecondsToLapTimeString(_avgLapTimeWet)}'"
                        );
                    }
                    catch { }

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

        private void NotifyWetVsDryDeltasChanged()
        {
            OnPropertyChanged(nameof(WetVsDryAvgLapDeltaSeconds));
            OnPropertyChanged(nameof(WetVsDryAvgLapDeltaPercent));
            OnPropertyChanged(nameof(WetVsDryAvgLapDeltaText));
            OnPropertyChanged(nameof(WetVsDryAvgFuelDelta));
            OnPropertyChanged(nameof(WetVsDryAvgFuelPercent));
            OnPropertyChanged(nameof(WetVsDryAvgFuelDeltaText));
        }

        [JsonIgnore]
        public double? WetVsDryAvgLapDeltaSeconds
        {
            get
            {
                if (AvgLapTimeWet.HasValue && AvgLapTimeDry.HasValue)
                {
                    return (AvgLapTimeWet.Value - AvgLapTimeDry.Value) / 1000.0;
                }
                return null;
            }
        }

        [JsonIgnore]
        public double? WetVsDryAvgLapDeltaPercent
        {
            get
            {
                if (AvgLapTimeWet.HasValue && AvgLapTimeDry.HasValue && AvgLapTimeDry.Value != 0)
                {
                    return (AvgLapTimeWet.Value / (double)AvgLapTimeDry.Value) * 100.0;
                }
                return null;
            }
        }

        [JsonIgnore]
        public string WetVsDryAvgLapDeltaText
        {
            get
            {
                if (!WetVsDryAvgLapDeltaSeconds.HasValue) return "—";
                var delta = WetVsDryAvgLapDeltaSeconds.Value;
                var sign = delta >= 0 ? "+" : string.Empty;
                var percent = WetVsDryAvgLapDeltaPercent;
                var deltaText = $"{sign}{delta:0.00}s";
                return percent.HasValue ? $"{deltaText} ({percent.Value:0.#}%)" : deltaText;
            }
        }

        [JsonIgnore]
        public double? WetVsDryAvgFuelDelta
        {
            get
            {
                if (AvgFuelPerLapWet.HasValue && AvgFuelPerLapDry.HasValue)
                {
                    return AvgFuelPerLapWet.Value - AvgFuelPerLapDry.Value;
                }
                return null;
            }
        }

        [JsonIgnore]
        public double? WetVsDryAvgFuelPercent
        {
            get
            {
                if (AvgFuelPerLapWet.HasValue && AvgFuelPerLapDry.HasValue && AvgFuelPerLapDry.Value != 0)
                {
                    return (AvgFuelPerLapWet.Value / AvgFuelPerLapDry.Value) * 100.0;
                }
                return null;
            }
        }

        [JsonIgnore]
        public string WetVsDryAvgFuelDeltaText
        {
            get
            {
                if (!WetVsDryAvgFuelDelta.HasValue) return "—";
                var delta = WetVsDryAvgFuelDelta.Value;
                var sign = delta >= 0 ? "+" : string.Empty;
                var percent = WetVsDryAvgFuelPercent;
                var deltaText = $"{sign}{delta:0.00}";
                return percent.HasValue ? $"{deltaText} ({percent.Value:0.#}%)" : deltaText;
            }
        }
    }

    public class ConditionMultipliers : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private double? _wetFactorPercent;
        [JsonProperty]
        public double? WetFactorPercent
        {
            get => _wetFactorPercent;
            set
            {
                if (_wetFactorPercent != value)
                {
                    _wetFactorPercent = value;
                    OnPropertyChanged();
                }
            }
        }

        private double? _formationLapBurnLiters;
        [JsonProperty]
        public double? FormationLapBurnLiters
        {
            get => _formationLapBurnLiters;
            set
            {
                if (_formationLapBurnLiters != value)
                {
                    _formationLapBurnLiters = value;
                    OnPropertyChanged();
                }
            }
        }

        private double? _refuelSecondsBase;
        [JsonProperty]
        public double? RefuelSecondsBase
        {
            get => _refuelSecondsBase;
            set
            {
                if (_refuelSecondsBase != value)
                {
                    _refuelSecondsBase = value;
                    OnPropertyChanged();
                }
            }
        }

        private double? _refuelSecondsPerLiter;
        [JsonProperty]
        public double? RefuelSecondsPerLiter
        {
            get => _refuelSecondsPerLiter;
            set
            {
                if (_refuelSecondsPerLiter != value)
                {
                    _refuelSecondsPerLiter = value;
                    OnPropertyChanged();
                }
            }
        }

        private double? _refuelSecondsPerSquare;
        [JsonProperty]
        public double? RefuelSecondsPerSquare
        {
            get => _refuelSecondsPerSquare;
            set
            {
                if (_refuelSecondsPerSquare != value)
                {
                    _refuelSecondsPerSquare = value;
                    OnPropertyChanged();
                }
            }
        }

        public ConditionMultipliers Clone()
        {
            return new ConditionMultipliers
            {
                WetFactorPercent = this.WetFactorPercent,
                FormationLapBurnLiters = this.FormationLapBurnLiters,
                RefuelSecondsBase = this.RefuelSecondsBase,
                RefuelSecondsPerLiter = this.RefuelSecondsPerLiter,
                RefuelSecondsPerSquare = this.RefuelSecondsPerSquare
            };
        }

        public static ConditionMultipliers CreateDefaultDry()
        {
            return new ConditionMultipliers
            {
                FormationLapBurnLiters = 1.5
            };
        }

        public static ConditionMultipliers CreateDefaultWet()
        {
            var cm = CreateDefaultDry();
            cm.WetFactorPercent = 90.0;
            return cm;
        }
    }
}
