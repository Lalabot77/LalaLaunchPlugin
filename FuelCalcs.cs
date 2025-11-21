using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Tab;

namespace LaunchPlugin
{
public class FuelCalcs : INotifyPropertyChanged
{
    // --- Enums and Structs ---
    public enum RaceType { LapLimited, TimeLimited }
    public enum TrackCondition { Dry, Wet }
    private struct StrategyResult
    {
        public int Stops;
        public double TotalFuel;
        public string Breakdown;
        public double TotalTime;
        public double PlayerLaps;
        public double FirstStintFuel;
        public double FirstStopTimeLoss;
    }
    // --- Private Fields ---
    private readonly LalaLaunch _plugin;
    private CarProfile _selectedCarProfile; // CHANGED to CarProfile object
    private string _selectedTrack;
    private RaceType _raceType;
    private double _raceLaps;
    private double _raceMinutes;
    private string _estimatedLapTime;
    private double _fuelPerLap;
    private TrackCondition _selectedTrackCondition;
    private double _maxFuelOverride;
    private double _tireChangeTime;
    private double _pitLaneTimeLoss;
    private double _fuelSaveTarget;
    private string _timeLossPerLapOfFuelSave;
    private double _formationLapFuelLiters = 1.5;
    private double _totalFuelNeeded;
    private int _requiredPitStops;
    private string _stintBreakdown;
    private int _stopsSaved;
    private string _totalTimeDifference;
    private string _extraTimeAfterLeader;
    private double _firstStintFuel;
    private string _validationMessage;
    private bool _isMissingTrackValidation;
    private double _firstStopTimeLoss;
    private double _refuelRate;
    private double _baseDryFuelPerLap;
    private double _leaderDeltaSeconds;
    private string _lapTimeSourceInfo = "source: manual";
    private bool _isLiveLapPaceAvailable;
    private string _liveLapPaceInfo = "-";
    private double _liveAvgLapSeconds = 0;   // internal cache of live estimate
    private int _liveFuelConfidence;
    private int _livePaceConfidence;
    private int _liveOverallConfidence;
    private bool _isLiveSessionActive;
    private string _liveCarName = "—";
    private string _liveTrackName = "—";
    private string _liveSurfaceModeDisplay = "Dry";
    private string _liveFuelTankSizeDisplay = "-";
    private string _dryLapTimeSummary = "-";
    private string _wetLapTimeSummary = "-";
    private string _dryPaceDeltaSummary = "-";
    private string _wetPaceDeltaSummary = "-";
    private string _dryFuelBurnSummary = "-";
    private string _wetFuelBurnSummary = "-";
    private string _lastPitDriveThroughDisplay = "-";
    private string _lastRefuelRateDisplay = "-";
    private string _liveBestLapDisplay = "-";
    private string _liveLeaderPaceInfo = "-";
    private string _racePaceVsLeaderSummary = "-";
    private double _liveFuelTankLiters;
    private double _liveDryFuelAvg;
    private double _liveDryFuelMin;
    private double _liveDryFuelMax;
    private int _liveDrySamples;
    private double _liveWetFuelAvg;
    private double _liveWetFuelMin;
    private double _liveWetFuelMax;
    private int _liveWetSamples;
    private bool _applyLiveFuelSuggestion = false;
    private bool _applyLiveMaxFuelSuggestion = false;
    private double _conditionRefuelBaseSeconds = 0;
    private double _conditionRefuelSecondsPerLiter = 0;
    private double _conditionRefuelSecondsPerSquare = 0;
    private bool _isRefreshingConditionParameters = false;
    private string _lastTyreChangeDisplay = "-";
                                             
    // --- NEW: Local properties for "what-if" parameters ---
    private double _contingencyValue = 1.5;
    private bool _isContingencyInLaps = true;
    private double _wetFactorPercent = 90.0;

    // --- NEW: Fields for PB Feature ---
    private double _loadedBestLapTimeSeconds;

    // --- Public Properties for UI Binding ---
    public ObservableCollection<CarProfile> AvailableCarProfiles { get; set; } // CHANGED
    public ObservableCollection<string> AvailableTracks { get; set; } = new ObservableCollection<string>();
    public string DetectedMaxFuelDisplay { get; private set; }
    public ICommand LoadLastSessionCommand { get; }
    public ObservableCollection<AnalysisDataRow> AnalysisData { get; set; } = new ObservableCollection<AnalysisDataRow>();
    private string _fuelPerLapText = "";
    private bool _suppressFuelTextSync = false;
    public bool IsPaceVsPbSliderActive => LapTimeSourceInfo == "source: PB";
    public string LapTimeSourceInfo
    {
        get => _lapTimeSourceInfo;
        set
        {
            if (_lapTimeSourceInfo != value)
            {
                _lapTimeSourceInfo = value;
                OnPropertyChanged(nameof(LapTimeSourceInfo));
                OnPropertyChanged(nameof(IsPaceVsPbSliderActive));
            }
        }
    }
    private string _fuelPerLapSourceInfo = "source: manual";
    public string FuelPerLapSourceInfo
    {
        get => _fuelPerLapSourceInfo;
        set { if (_fuelPerLapSourceInfo != value) { _fuelPerLapSourceInfo = value; OnPropertyChanged(); } }
    }
    public bool IsLiveLapPaceAvailable
    {
        get => _isLiveLapPaceAvailable;
        private set { if (_isLiveLapPaceAvailable != value) { _isLiveLapPaceAvailable = value; OnPropertyChanged(nameof(IsLiveLapPaceAvailable)); } }
    }

    public string LiveLapPaceInfo
    {
        get => _liveLapPaceInfo;
        private set { if (_liveLapPaceInfo != value) { _liveLapPaceInfo = value; OnPropertyChanged(nameof(LiveLapPaceInfo)); } }
    }

    public int LiveFuelConfidence { get; private set; }
    public int LivePaceConfidence { get; private set; }
    public int LiveOverallConfidence { get; private set; }
    public string LiveConfidenceSummary { get; private set; } = "Live reliability: n/a";
    public bool IsLiveSessionActive
    {
        get => _isLiveSessionActive;
        private set
        {
            if (_isLiveSessionActive != value)
            {
                _isLiveSessionActive = value;
                OnPropertyChanged();
                UpdateSurfaceModeLabel();
            }
        }
    }
    public string LiveCarName
    {
        get => _liveCarName;
        private set { if (_liveCarName != value) { _liveCarName = value; OnPropertyChanged(); } }
    }
    public string LiveTrackName
    {
        get => _liveTrackName;
        private set { if (_liveTrackName != value) { _liveTrackName = value; OnPropertyChanged(); } }
    }
    public string LiveSurfaceModeDisplay
    {
        get => _liveSurfaceModeDisplay;
        private set { if (_liveSurfaceModeDisplay != value) { _liveSurfaceModeDisplay = value; OnPropertyChanged(); } }
    }
    public string LiveFuelTankSizeDisplay
    {
        get => _liveFuelTankSizeDisplay;
        private set { if (_liveFuelTankSizeDisplay != value) { _liveFuelTankSizeDisplay = value; OnPropertyChanged(); } }
    }
    public string LiveBestLapDisplay
    {
        get => _liveBestLapDisplay;
        private set { if (_liveBestLapDisplay != value) { _liveBestLapDisplay = value; OnPropertyChanged(); } }
    }
    public string LiveLeaderPaceInfo
    {
        get => _liveLeaderPaceInfo;
        private set { if (_liveLeaderPaceInfo != value) { _liveLeaderPaceInfo = value; OnPropertyChanged(); } }
    }

    public void RefreshConditionRefuelParameters(double baseSeconds, double secondsPerLiter, double secondsPerSquare)
    {
        // Prevent UI-triggered loops if bindings update while we set the backing fields
        if (_isRefreshingConditionParameters) return;

        _isRefreshingConditionParameters = true;
        ConditionRefuelBaseSeconds = Math.Max(0, baseSeconds);
        ConditionRefuelSecondsPerLiter = Math.Max(0, secondsPerLiter);
        ConditionRefuelSecondsPerSquare = Math.Max(0, secondsPerSquare);
        _isRefreshingConditionParameters = false;
    }
    public string DryLapTimeSummary
    {
        get => _dryLapTimeSummary;
        private set { if (_dryLapTimeSummary != value) { _dryLapTimeSummary = value; OnPropertyChanged(); } }
    }
    public string WetLapTimeSummary
    {
        get => _wetLapTimeSummary;
        private set { if (_wetLapTimeSummary != value) { _wetLapTimeSummary = value; OnPropertyChanged(); } }
    }
    public string DryPaceDeltaSummary
    {
        get => _dryPaceDeltaSummary;
        private set { if (_dryPaceDeltaSummary != value) { _dryPaceDeltaSummary = value; OnPropertyChanged(); } }
    }
    public string WetPaceDeltaSummary
    {
        get => _wetPaceDeltaSummary;
        private set { if (_wetPaceDeltaSummary != value) { _wetPaceDeltaSummary = value; OnPropertyChanged(); } }
    }
    public string DryFuelBurnSummary
    {
        get => _dryFuelBurnSummary;
        private set { if (_dryFuelBurnSummary != value) { _dryFuelBurnSummary = value; OnPropertyChanged(); } }
    }
    public string WetFuelBurnSummary
    {
        get => _wetFuelBurnSummary;
        private set { if (_wetFuelBurnSummary != value) { _wetFuelBurnSummary = value; OnPropertyChanged(); } }
    }
    public string RacePaceVsLeaderSummary
    {
        get => _racePaceVsLeaderSummary;
        private set { if (_racePaceVsLeaderSummary != value) { _racePaceVsLeaderSummary = value; OnPropertyChanged(); } }
    }
    public string LastPitDriveThroughDisplay
    {
        get => _lastPitDriveThroughDisplay;
        private set { if (_lastPitDriveThroughDisplay != value) { _lastPitDriveThroughDisplay = value; OnPropertyChanged(); } }
    }
    public string LastRefuelRateDisplay
    {
        get => _lastRefuelRateDisplay;
        private set { if (_lastRefuelRateDisplay != value) { _lastRefuelRateDisplay = value; OnPropertyChanged(); } }
    }
    public string LastTyreChangeDisplay
    {
        get => _lastTyreChangeDisplay;
        private set { if (_lastTyreChangeDisplay != value) { _lastTyreChangeDisplay = value; OnPropertyChanged(); } }
    }
    public double ConditionRefuelBaseSeconds
    {
        get => _conditionRefuelBaseSeconds;
        private set { if (Math.Abs(_conditionRefuelBaseSeconds - value) > 1e-9) { _conditionRefuelBaseSeconds = value; OnPropertyChanged(); } }
    }
    public double ConditionRefuelSecondsPerLiter
    {
        get => _conditionRefuelSecondsPerLiter;
        private set { if (Math.Abs(_conditionRefuelSecondsPerLiter - value) > 1e-9) { _conditionRefuelSecondsPerLiter = value; OnPropertyChanged(); } }
    }
    public double ConditionRefuelSecondsPerSquare
    {
        get => _conditionRefuelSecondsPerSquare;
        private set { if (Math.Abs(_conditionRefuelSecondsPerSquare - value) > 1e-9) { _conditionRefuelSecondsPerSquare = value; OnPropertyChanged(); } }
    }

    public string ProfileAvgLapTimeDisplay { get; private set; }
    public string ProfileAvgFuelDisplay { get; private set; }

    public string ProfileAvgDryLapTimeDisplay { get; private set; }
    public string ProfileAvgDryFuelDisplay { get; private set; }
    public string LiveFuelPerLapDisplay { get; private set; } = "-";

    public ObservableCollection<TrackStats> AvailableTrackStats { get; set; } = new ObservableCollection<TrackStats>();

    // --- Properties for PB Feature ---

    private string _historicalBestLapDisplay;
    public string HistoricalBestLapDisplay
    {
        get => _historicalBestLapDisplay;
        private set { if (_historicalBestLapDisplay != value) { _historicalBestLapDisplay = value; OnPropertyChanged(); } }
    }
    public bool IsPersonalBestAvailable { get; private set; }
    private string _livePaceDeltaInfo;
    public string LivePaceDeltaInfo
    {
        get => _livePaceDeltaInfo;
        private set { if (_livePaceDeltaInfo != value) { _livePaceDeltaInfo = value; OnPropertyChanged(); } }
    }

    // ---- Profile/live availability flags for buttons ----
    public bool HasProfileFuelPerLap { get; private set; }
    public bool HasProfilePitLaneLoss { get; private set; }

    // Live availability (fuel per lap comes from LalaLaunch)
    public double LiveFuelPerLap { get; private set; }
    public bool IsLiveFuelPerLapAvailable => LiveFuelPerLap > 0;
    public bool ApplyLiveFuelSuggestion
    {
        get => _applyLiveFuelSuggestion;
        set { if (_applyLiveFuelSuggestion != value) { _applyLiveFuelSuggestion = value; OnPropertyChanged(); } }
    }

    public bool HasLiveMaxFuelSuggestion => _liveMaxFuel > 0;

    public void SetLiveFuelSuggestionFlags(bool applyFuelSuggestion, bool applyMaxFuelSuggestion)
    {
        ApplyLiveFuelSuggestion = applyFuelSuggestion;
        ApplyLiveMaxFuelSuggestion = applyMaxFuelSuggestion;
    }

    private double _liveMaxFuel;
    public bool IsMaxFuelOverrideTooHigh => MaxFuelOverride > _liveMaxFuel && _liveMaxFuel > 0;
    public string MaxFuelPerLapDisplay { get; private set; } = "-";
    public bool IsMaxFuelAvailable => _plugin?.MaxFuelPerLapDisplay > 0;
    public bool ApplyLiveMaxFuelSuggestion
    {
        get => _applyLiveMaxFuelSuggestion;
        set
        {
            if (_applyLiveMaxFuelSuggestion != value)
            {
                _applyLiveMaxFuelSuggestion = value;
                OnPropertyChanged();

                if (value)
                {
                    ApplyLiveMaxFuelSuggestionValue();
                }
            }
        }
    }

    // Update profile if the incoming rate differs (> tiny epsilon), then recalc.
    public void SetRefuelRateLps(double rateLps)
    {
        if (rateLps <= 0) return;

        if (Math.Abs(_refuelRate - rateLps) > 1e-6)
        {
            _refuelRate = rateLps;
            _plugin?.SaveRefuelRateToActiveProfile(rateLps); // call into LalaLaunch
            OnPropertyChanged(nameof(_refuelRate));
            CalculateStrategy();
        }
    }

    // This will hold the "what-if" override for the Race Pace vs PB delta.
    private double _racePaceDeltaOverride;
    public double RacePaceDeltaOverride
    {
        get => _racePaceDeltaOverride;
        set
        {
            if (_racePaceDeltaOverride != value)
            {
                _racePaceDeltaOverride = value;
                OnPropertyChanged(); // Notifies the UI to update the slider's text

                // If the current time source is the PB, recalculate the lap time live
                if (LapTimeSourceInfo == "source: PB")
                {
                    LoadPersonalBestAsRacePace(); // This method already uses the override
                }
                else
                {
                    // Otherwise, just recalculate the final strategy without changing the lap time
                    CalculateStrategy();
                }
            }
        }
    }

    // Presets — list exposed to UI
    private List<RacePreset> _availablePresets = new List<RacePreset>();
    public List<RacePreset> AvailablePresets
    {
        get { return _availablePresets; }
    }

    // Currently selected (in ComboBox). May be null at runtime.
    private RacePreset _selectedPreset;
    public RacePreset SelectedPreset
    {
        get { return _selectedPreset; }
        set
        {
            if (!object.ReferenceEquals(_selectedPreset, value))
            {
                _selectedPreset = value;
                OnPropertyChanged(nameof(SelectedPreset));
                OnPropertyChanged(nameof(HasSelectedPreset));

                // Auto-apply on selection change (removes need for an Apply button)
                ApplySelectedPreset();
            }
        }
    }


    // Has selection (for button enable)
    public bool HasSelectedPreset
    {
        get { return _selectedPreset != null; }
    }

    // Last applied preset (for badge + modified flag)
    private RacePreset _appliedPreset;

    // Badge text shown under the selector
    public string PresetBadge
    {
        get
        {
            if (_appliedPreset == null) return "Preset: (none)";
            return IsPresetModified() ? "Preset: " + _appliedPreset.Name + " (modified)"
                                      : "Preset: " + _appliedPreset.Name;
        }
    }
        public bool IsPresetModifiedFlag
    {
        get { return IsPresetModified(); }
    }

    // Pit-lane live detection not implemented yet; hide the button for now
    public bool IsLivePitLaneLossAvailable => false;

    // ---- Commands for the buttons ----
    //public ICommand ResetFuelPerLapToProfileCommand { get; }
    public ICommand UseLiveFuelPerLapCommand { get; }
    public ICommand ResetPitLaneLossToProfileCommand { get; }
    public ICommand UseLivePitLaneLossCommand { get; }
    // ---- Commands for the lap-time row ----
    public ICommand UseLiveLapPaceCommand { get; }
    public ICommand LoadProfileLapTimeCommand { get; }
    public ICommand SavePlannerDataToProfileCommand { get; }
    public ICommand UseProfileFuelPerLapCommand { get; }
    public ICommand UseMaxFuelPerLapCommand { get; }
    public ICommand ApplyPresetCommand { get; private set; }
    public ICommand ClearPresetCommand { get; private set; }

    private void ApplySelectedPreset()
    {
        if (_selectedPreset == null) return;

        var p = _selectedPreset;

        // Race type + duration
        if (p.Type == RacePresetType.TimeLimited)
        {
            IsTimeLimitedRace = true;   // your existing setters raise OnPropertyChanged
            IsLapLimitedRace = false;
            if (p.RaceMinutes.HasValue) RaceMinutes = p.RaceMinutes.Value;
        }
        else
        {
            IsTimeLimitedRace = false;
            IsLapLimitedRace = true;
            if (p.RaceLaps.HasValue) RaceLaps = p.RaceLaps.Value;
        }

        // Mandatory stop
        MandatoryStopRequired = p.MandatoryStopRequired;

        // Tyre change time: only when specified
        if (p.TireChangeTimeSec.HasValue)
            TireChangeTime = p.TireChangeTimeSec.Value;

        // Max fuel override: only when specified
        if (p.MaxFuelLitres.HasValue)
            MaxFuelOverride = p.MaxFuelLitres.Value;

        // Contingency
        IsContingencyInLaps = p.ContingencyInLaps;
        IsContingencyLitres = !p.ContingencyInLaps;
        ContingencyValue = p.ContingencyValue;

        _appliedPreset = p;
        RaisePresetStateChanged();
    }

    private void ClearAppliedPreset()
    {
        _appliedPreset = null;
        RaisePresetStateChanged();
    }

    private bool IsPresetModified()
    {
        if (_appliedPreset == null) return false;

        bool typeDiff =
            (_appliedPreset.Type == RacePresetType.TimeLimited && !IsTimeLimitedRace) ||
            (_appliedPreset.Type == RacePresetType.LapLimited && !IsLapLimitedRace);

        bool durDiff =
            (_appliedPreset.Type == RacePresetType.TimeLimited && (_appliedPreset.RaceMinutes ?? RaceMinutes) != RaceMinutes) ||
            (_appliedPreset.Type == RacePresetType.LapLimited && (_appliedPreset.RaceLaps ?? RaceLaps) != RaceLaps);

        bool stopDiff = _appliedPreset.MandatoryStopRequired != MandatoryStopRequired;

        bool tyreDiff = _appliedPreset.TireChangeTimeSec.HasValue &&
                        Math.Abs(_appliedPreset.TireChangeTimeSec.Value - TireChangeTime) > 0.05;

        bool fuelDiff = _appliedPreset.MaxFuelLitres.HasValue &&
                        Math.Abs(_appliedPreset.MaxFuelLitres.Value - MaxFuelOverride) > 0.05;

        bool contDiff =
            (_appliedPreset.ContingencyInLaps != IsContingencyInLaps) ||
            Math.Abs(_appliedPreset.ContingencyValue - ContingencyValue) > 0.05;

        return typeDiff || durDiff || stopDiff || tyreDiff || fuelDiff || contDiff;
    }

    private void RaisePresetStateChanged()
    {
        OnPropertyChanged(nameof(PresetBadge));
        OnPropertyChanged(nameof(IsPresetModifiedFlag));
    }

    public string FuelPerLapText
    {
        get => _fuelPerLapText;
        set
        {
            if (_fuelPerLapText == value) return;
            _fuelPerLapText = value ?? "";
            OnPropertyChanged(nameof(FuelPerLapText));
            FuelPerLapSourceInfo = "source: manual";

            // Accept partial inputs like "2.", ".8", "2," while typing.
            var s = _fuelPerLapText.Replace(',', '.').Trim();

            // Empty or just a dot/comma -> don't update the numeric value yet.
            if (string.IsNullOrEmpty(s) || s == ".")
                return;

            // Only update the real numeric when parsable and > 0
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0)
            {
                _suppressFuelTextSync = true;   // prevent feedback loop
                FuelPerLap = v;                 // your existing double property
                _suppressFuelTextSync = false;
            }
            // If not parsable, do nothing; user can keep typing until it becomes valid.
        }
    }

    public CarProfile SelectedCarProfile // CHANGED to CarProfile object
    {
        get => _selectedCarProfile;
        set
        {
            if (_selectedCarProfile != value)
            {
                _selectedCarProfile = value;
                OnPropertyChanged();

                // Rebuild lists
                AvailableTracks.Clear();        // legacy string list – safe to keep for now
                AvailableTrackStats.Clear();    // object list for ComboBox

                if (_selectedCarProfile?.TrackStats != null)
                {
                    foreach (var t in _selectedCarProfile.TrackStats.Values
                                 .OrderBy(t => t.DisplayName ?? t.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                    {
                        AvailableTracks.Add(t.DisplayName);   // legacy
                        AvailableTrackStats.Add(t);           // object list
                    }
                }
                OnPropertyChanged(nameof(AvailableTracks));
                OnPropertyChanged(nameof(AvailableTrackStats));

                // Select first track by instance (triggers LoadProfileData via SelectedTrackStats setter)
                if (AvailableTrackStats.Count > 0)
                {
                    if (!ReferenceEquals(SelectedTrackStats, AvailableTrackStats[0]))
                        SelectedTrackStats = AvailableTrackStats[0];
                }
                else
                {
                    if (SelectedTrackStats != null)
                        SelectedTrackStats = null;
                }
            }
        }
    }

    // Cache of the resolved TrackStats for the current SelectedCarProfile + SelectedTrack
    private TrackStats _selectedTrackStats;
    private bool _suppressSelectedTrackSync;

    public TrackStats SelectedTrackStats
    {
        get => _selectedTrackStats;
        set
        {
            if (!ReferenceEquals(_selectedTrackStats, value))
            {
                _selectedTrackStats = value;
                OnPropertyChanged(nameof(SelectedTrackStats));

                // Keep the legacy string SelectedTrack in sync (avoids touching other code)
                _suppressSelectedTrackSync = true;
                SelectedTrack = value?.DisplayName ?? "";
                _suppressSelectedTrackSync = false;

                // One authoritative reload when selection changes
                LoadProfileData();
            }
        }
    }

    // Resolve the SelectedTrack string to the actual TrackStats object (try key first, then display name)
    private TrackStats ResolveSelectedTrackStats()
    {
        return _selectedCarProfile?.ResolveTrackByNameOrKey(_selectedTrack);
    }

    public string SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            if (_selectedTrack != value)
            {
                _selectedTrack = value;
                OnPropertyChanged();
                if (!_suppressSelectedTrackSync)
                {
                    LoadProfileData();
                }
            }
        }
    }

    public RaceType SelectedRaceType
    {
        get => _raceType;
        set
        {
            if (_raceType != value)
            {
                _raceType = value;
                OnPropertyChanged("SelectedRaceType");
                OnPropertyChanged("IsLapLimitedRace");
                OnPropertyChanged("IsTimeLimitedRace");
                CalculateStrategy();
                RaisePresetStateChanged();
            }
        }
    }

    public bool IsLapLimitedRace
    {
        get => SelectedRaceType == RaceType.LapLimited;
        set { if (value) SelectedRaceType = RaceType.LapLimited; }
    }

    public bool IsTimeLimitedRace
    {
        get => SelectedRaceType == RaceType.TimeLimited;
        set { if (value) SelectedRaceType = RaceType.TimeLimited; }
    }

    public double RaceLaps
    {
        get => _raceLaps;
        set
        {
            if (_raceLaps != value)
            {
                _raceLaps = value;
                OnPropertyChanged("RaceLaps");
                CalculateStrategy();
                RaisePresetStateChanged();
            }
        }
    }

    public double RaceMinutes
    {
        get => _raceMinutes;
        set
        {
            if (_raceMinutes != value)
            {
                _raceMinutes = value;
                OnPropertyChanged("RaceMinutes");
                CalculateStrategy();
                RaisePresetStateChanged();
            }
        }
    }

    public string EstimatedLapTime
    {
        get => _estimatedLapTime;
        set
        {
            if (_estimatedLapTime != value)
            {
                _estimatedLapTime = value;
                OnPropertyChanged("EstimatedLapTime");
                LapTimeSourceInfo = "source: manual"; // Add this line
                CalculateStrategy();
            }
        }
    }

    public double LeaderDeltaSeconds
    {
        get => _leaderDeltaSeconds;
        set
        {
            if (_leaderDeltaSeconds != value)
            {
                _leaderDeltaSeconds = value;
                OnPropertyChanged(nameof(LeaderDeltaSeconds));
                CalculateStrategy();
            }
        }
    }

    public double FuelPerLap
    {
        get => _fuelPerLap;
        set
        {
            if (Math.Abs(_fuelPerLap - value) > 1e-9)
            {
                _fuelPerLap = value;
                OnPropertyChanged(nameof(FuelPerLap));

                if (IsDry) { _baseDryFuelPerLap = _fuelPerLap; }
                CalculateStrategy();

                // Keep the textbox text aligned unless the change originated from the textbox itself
                if (!_suppressFuelTextSync)
                {
                    _fuelPerLapText = _fuelPerLap.ToString("0.###", CultureInfo.InvariantCulture);
                    OnPropertyChanged(nameof(FuelPerLapText));
                }
            }
        }
    }

    public void SetPersonalBestSeconds(double pbSeconds)
    {
        if (pbSeconds <= 0) return;

        _loadedBestLapTimeSeconds = pbSeconds;
        IsPersonalBestAvailable = true;

        var formatted = TimeSpan
            .FromSeconds(_loadedBestLapTimeSeconds)
            .ToString(@"m\:ss\.fff");

        // Update the label shown under the PB button
        HistoricalBestLapDisplay = formatted;
        LiveBestLapDisplay = formatted;

        // If the user has ALREADY selected PB as the source, refresh the estimate and source label.
        if (LapTimeSourceInfo == "source: PB")
        {
            double estSeconds = _loadedBestLapTimeSeconds + RacePaceDeltaOverride;
            EstimatedLapTime = TimeSpan.FromSeconds(estSeconds).ToString(@"m\:ss\.fff");
            OnPropertyChanged(nameof(EstimatedLapTime));
        }

        OnPropertyChanged(nameof(IsPersonalBestAvailable));
        OnPropertyChanged(nameof(HistoricalBestLapDisplay));
        UpdateLapTimeSummaries();
    }

    private void UseMaxFuelPerLap()
    {
        if (_plugin.MaxFuelPerLapDisplay > 0)
        {
            FuelPerLap = _plugin.MaxFuelPerLapDisplay;
            FuelPerLapSourceInfo = "source: max";
        }
    }

    // This pair correctly handles UI thread updates for Live Fuel
    public void SetLiveFuelPerLap(double value)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.CheckAccess())
        {
            ApplySetLiveFuelPerLap(value);
        }
        else
        {
            disp.Invoke(() => ApplySetLiveFuelPerLap(value));
        }
    }
    private void ApplySetLiveFuelPerLap(double value)
    {
        LiveFuelPerLap = value;
        LiveFuelPerLapDisplay = (value > 0) ? $"{value:F2} L" : "-";
        if (value <= 0)
        {
            ApplyLiveFuelSuggestion = false;
        }
        OnPropertyChanged(nameof(LiveFuelPerLap));
        OnPropertyChanged(nameof(LiveFuelPerLapDisplay));
        OnPropertyChanged(nameof(IsLiveFuelPerLapAvailable));
    }

    // This pair correctly handles UI thread updates for Max Fuel
    public void SetMaxFuelPerLap(double value)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.CheckAccess())
        {
            ApplySetMaxFuelPerLap(value);
        }
        else
        {
            disp.Invoke(() => ApplySetMaxFuelPerLap(value));
        }
    }
    private void ApplySetMaxFuelPerLap(double value)
    {
        if (value > 0)
        {
            MaxFuelPerLapDisplay = $"{value:F2} L";
        }
        else
        {
            MaxFuelPerLapDisplay = "-";
        }
        if (value <= 0)
        {
            ApplyLiveMaxFuelSuggestion = false;
        }
        OnPropertyChanged(nameof(MaxFuelPerLapDisplay));
        OnPropertyChanged(nameof(IsMaxFuelAvailable));
    }

    public void SetConditionRefuelParameters(double baseSeconds, double secondsPerLiter, double secondsPerSquare)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.CheckAccess())
        {
            ApplyConditionRefuelParameters(baseSeconds, secondsPerLiter, secondsPerSquare);
        }
        else
        {
            disp.Invoke(() => ApplyConditionRefuelParameters(baseSeconds, secondsPerLiter, secondsPerSquare));
        }
    }

    private void ApplyConditionRefuelParameters(double baseSeconds, double secondsPerLiter, double secondsPerSquare)
    {
        if (_isRefreshingConditionParameters) return;
        _isRefreshingConditionParameters = true;

        ConditionRefuelBaseSeconds = baseSeconds;
        ConditionRefuelSecondsPerLiter = secondsPerLiter;
        ConditionRefuelSecondsPerSquare = secondsPerSquare;

        _isRefreshingConditionParameters = false;
    }

    public void SetLiveFuelWindowStats(double avgDry, double minDry, double maxDry, int drySamples,
        double avgWet, double minWet, double maxWet, int wetSamples)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.CheckAccess())
        {
            ApplyLiveFuelWindowStats(avgDry, minDry, maxDry, drySamples, avgWet, minWet, maxWet, wetSamples);
        }
        else
        {
            disp.Invoke(() => ApplyLiveFuelWindowStats(avgDry, minDry, maxDry, drySamples, avgWet, minWet, maxWet, wetSamples));
        }
    }

    private void ApplyLiveFuelWindowStats(double avgDry, double minDry, double maxDry, int drySamples,
        double avgWet, double minWet, double maxWet, int wetSamples)
    {
        _liveDryFuelAvg = avgDry;
        _liveDryFuelMin = minDry > 0 ? minDry : 0.0;
        _liveDryFuelMax = maxDry > 0 ? maxDry : 0.0;
        _liveDrySamples = Math.Max(0, drySamples);

        _liveWetFuelAvg = avgWet;
        _liveWetFuelMin = minWet > 0 ? minWet : 0.0;
        _liveWetFuelMax = maxWet > 0 ? maxWet : 0.0;
        _liveWetSamples = Math.Max(0, wetSamples);

        UpdateFuelBurnSummaries();
    }

    public void SetLastPitDriveThroughSeconds(double seconds)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.CheckAccess())
        {
            ApplyLastPitDriveThroughSeconds(seconds);
        }
        else
        {
            disp.Invoke(() => ApplyLastPitDriveThroughSeconds(seconds));
        }
    }

    private void ApplyLastPitDriveThroughSeconds(double seconds)
    {
        LastPitDriveThroughDisplay = seconds > 0 ? $"{seconds:F1}s" : "-";
    }

    public void SetLastRefuelRate(double litersPerSecond)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.CheckAccess())
        {
            ApplyLastRefuelRate(litersPerSecond);
        }
        else
        {
            disp.Invoke(() => ApplyLastRefuelRate(litersPerSecond));
        }
    }

    private void ApplyLastRefuelRate(double litersPerSecond)
    {
        LastRefuelRateDisplay = litersPerSecond > 0 ? $"{litersPerSecond:F2} L/s" : "-";
    }

    public void SetLastTyreChangeSeconds(double seconds)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.CheckAccess())
        {
            ApplyLastTyreChangeSeconds(seconds);
        }
        else
        {
            disp.Invoke(() => ApplyLastTyreChangeSeconds(seconds));
        }
    }

    private void ApplyLastTyreChangeSeconds(double seconds)
    {
        LastTyreChangeDisplay = seconds > 0 ? $"{seconds:F1}s" : "-";
    }

    public TrackCondition SelectedTrackCondition
    {
        get => _selectedTrackCondition;
        set
        {
            if (_selectedTrackCondition != value)
            {
                _selectedTrackCondition = value;
                OnPropertyChanged(nameof(SelectedTrackCondition));
                OnPropertyChanged(nameof(IsDry));
                OnPropertyChanged(nameof(IsWet));
                OnPropertyChanged(nameof(ShowDrySnapshotRows));
                OnPropertyChanged(nameof(ShowWetSnapshotRows));

                // Apply fuel factor
                if (IsWet) { ApplyWetFactor(); }
                else { FuelPerLap = _baseDryFuelPerLap; }

                // --- NEW LOGIC: Update Estimated Lap Time based on condition ---
                var ts = SelectedTrackStats ?? ResolveSelectedTrackStats();

                if (ts != null)
                {
                    int? lapTimeMs = IsWet ? ts.AvgLapTimeWet : ts.AvgLapTimeDry;
                    if (lapTimeMs.HasValue && lapTimeMs > 0)
                    {
                        EstimatedLapTime = TimeSpan.FromMilliseconds(lapTimeMs.Value).ToString(@"m\:ss\.fff");
                        LapTimeSourceInfo = $"source: {(IsWet ? "wet avg" : "dry avg")}";
                    }
                }
                UpdateTrackDerivedSummaries();
                UpdateSurfaceModeLabel();
            }
            OnPropertyChanged(nameof(ProfileAvgLapTimeDisplay));
            OnPropertyChanged(nameof(ProfileAvgFuelDisplay));
            RefreshConditionParameters();
        }
    }

    public bool IsDry
    {
        get => SelectedTrackCondition == TrackCondition.Dry;
        set { if (value) SelectedTrackCondition = TrackCondition.Dry; }
    }

    public bool IsWet
    {
        get => SelectedTrackCondition == TrackCondition.Wet;
        set { if (value) SelectedTrackCondition = TrackCondition.Wet; }
    }
    public bool ShowDrySnapshotRows => IsDry;
    public bool ShowWetSnapshotRows => IsWet;

    public double MaxFuelOverride
    {
        get => _maxFuelOverride;
        set
        {
            if (_maxFuelOverride != value)
            {
                _maxFuelOverride = value;
                OnPropertyChanged("MaxFuelOverride");
                OnPropertyChanged(nameof(IsMaxFuelOverrideTooHigh)); // Notify UI to re-check the highlight
                CalculateStrategy();
                RaisePresetStateChanged();
            }
        }
    }

    public double TireChangeTime
    {
        get => _tireChangeTime;
        set
        {
            if (_tireChangeTime != value)
            {
                _tireChangeTime = value;
                OnPropertyChanged("TireChangeTime");
                CalculateStrategy();
                RaisePresetStateChanged();
            }
        }
    }

    public double PitLaneTimeLoss
    {
        get => _pitLaneTimeLoss;
        set
        {
            if (_pitLaneTimeLoss != value)
            {
                _pitLaneTimeLoss = value;
                OnPropertyChanged("PitLaneTimeLoss");
                CalculateStrategy();
            }
        }
    }

    public double FuelSaveTarget
    {
        get => _fuelSaveTarget;
        set
        {
            if (_fuelSaveTarget != value)
            {
                _fuelSaveTarget = value;
                OnPropertyChanged("FuelSaveTarget");
                CalculateStrategy();
            }
        }
    }

    public string TimeLossPerLapOfFuelSave
    {
        get => _timeLossPerLapOfFuelSave;
        set
        {
            if (_timeLossPerLapOfFuelSave != value)
            {
                _timeLossPerLapOfFuelSave = value;
                OnPropertyChanged("TimeLossPerLapOfFuelSave");
                CalculateStrategy();
            }
        }
    }

    public double FormationLapFuelLiters
    {
        get => _formationLapFuelLiters;
        set
        {
            if (_formationLapFuelLiters != value)
            {
                _formationLapFuelLiters = value;
                OnPropertyChanged("FormationLapFuelLiters");
                CalculateStrategy();
            }
        }
    }

    // Keeps stint splits sane and hides a near-zero second stint.
    private static (double firstLaps, double secondLaps, bool showSecond)
    ClampStintSplits(double adjustedLaps, double plannedFirstStintLaps, double minSecondStintToShow = 0.5)
    {
        var first = Math.Max(0.0, Math.Min(adjustedLaps, plannedFirstStintLaps));
        var second = Math.Max(0.0, adjustedLaps - first);
        bool showSecond = second >= minSecondStintToShow;
        if (!showSecond) second = 0.0;
        return (first, second, showSecond);
    }

    // Maps stop-component times to a human-readable suffix for the STOP line.
    private static string BuildStopSuffix(double tyresSeconds, double fuelSeconds)
    {
        bool hasTyres = tyresSeconds > 0.0;
        bool hasFuel = fuelSeconds > 0.0;

        if (hasTyres && hasFuel) return "(Fuel+Tyres)";
        if (hasTyres) return "(Tyres)";
        if (hasFuel) return "(Fuel)";
        return "(Drive-through)";
    }

    // --- Default refuel rate to use when no car/profile rate is available (L/s) ---
    // Default refuel rate (L/s) used when no car/profile rate is available.
    public double DefaultRefuelRateLps { get; set; } = 2.5;

    // Return the effective refuel rate (L/s): profile if present, else fallback default.
    private double GetEffectiveRefuelRateLps()
    {
        // _refuelRate is set from car.RefuelRate when a car/profile is loaded.
        return (_refuelRate > 0.0) ? _refuelRate : DefaultRefuelRateLps;
    }

    private double ComputeRefuelSeconds(double fuelToAdd)
    {
        if (fuelToAdd <= 0.0) return 0.0;

        double baseSeconds = _conditionRefuelBaseSeconds;

        double pourSeconds;
        if (_conditionRefuelSecondsPerLiter > 0.0)
        {
            pourSeconds = _conditionRefuelSecondsPerLiter * fuelToAdd;
        }
        else
        {
            double rate = GetEffectiveRefuelRateLps();
            pourSeconds = (rate > 0.0) ? (fuelToAdd / rate) : 0.0;
        }

        double curveSeconds = 0.0;
        if (_conditionRefuelSecondsPerSquare > 0.0)
        {
            curveSeconds = _conditionRefuelSecondsPerSquare * fuelToAdd * fuelToAdd;
        }

        double total = baseSeconds + pourSeconds + curveSeconds;
        return total < 0.0 ? 0.0 : total;
    }


    // --- REWIRED "What-If" Properties ---
    public void LoadProfileLapTime()
    {
        var ts = SelectedTrackStats ?? ResolveSelectedTrackStats();
        if (ts == null) return;

        int? lapMs = IsDry ? ts.AvgLapTimeDry : ts.AvgLapTimeWet;

        if (lapMs.HasValue && lapMs.Value > 0)
        {
            EstimatedLapTime = TimeSpan.FromMilliseconds(lapMs.Value).ToString(@"m\:ss\.fff");
            LapTimeSourceInfo = "source: profile";
            OnPropertyChanged(nameof(EstimatedLapTime));
            OnPropertyChanged(nameof(LapTimeSourceInfo));
            CalculateStrategy();
        }
        
    }

    public double WetFactorPercent
    {
        get => _wetFactorPercent;
        set
        {
            if (_wetFactorPercent != value)
            {
                _wetFactorPercent = value;
                OnPropertyChanged();
                ApplyWetFactor();
            }
        }
    }

    public double ContingencyValue
    {
        get => _contingencyValue;
        set
        {
            if (_contingencyValue != value)
            {
                _contingencyValue = value;
                OnPropertyChanged();
                CalculateStrategy(); // Recalculate when changed
                RaisePresetStateChanged();
            }
        }
    }

    public bool IsContingencyInLaps
    {
        get => _isContingencyInLaps;
        set
        {
            if (_isContingencyInLaps != value)
            {
                _isContingencyInLaps = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsContingencyLitres));
                CalculateStrategy();
                RaisePresetStateChanged();
            }
        }
    }

    public bool IsContingencyLitres
    {
        get => !_isContingencyInLaps;
        set { IsContingencyInLaps = !value; }
    }

    // --- Mandatory stop (simple) ---
    private bool _mandatoryStopRequired;
    public bool MandatoryStopRequired
    {
        get => _mandatoryStopRequired;
        set
        {
            if (_mandatoryStopRequired != value)
            {
                _mandatoryStopRequired = value;
                OnPropertyChanged();        // nameof(MandatoryStopRequired)
                CalculateStrategy();
                RaisePresetStateChanged();
            }
        }
    }

    private void RebuildAvailableCarProfiles()
    {
        // This now provides the full objects to the ComboBox
        AvailableCarProfiles = _plugin.ProfilesViewModel.CarProfiles;
        OnPropertyChanged(nameof(AvailableCarProfiles));
    }

    private void UseProfileFuelPerLap()
    {
        var ts = SelectedTrackStats ?? ResolveSelectedTrackStats();
        if (ts == null) return;

        // Use the dry value as the primary profile source
        if (ts.AvgFuelPerLapDry.HasValue && ts.AvgFuelPerLapDry > 0)
        {
            FuelPerLap = ts.AvgFuelPerLapDry.Value;
            FuelPerLapSourceInfo = "source: profile";
        }
    }
    public double TotalFuelNeeded { get => _totalFuelNeeded; private set { _totalFuelNeeded = value; OnPropertyChanged("TotalFuelNeeded"); } }
    public int RequiredPitStops { get => _requiredPitStops; private set { _requiredPitStops = value; OnPropertyChanged("RequiredPitStops"); } }
    public string StintBreakdown { get => _stintBreakdown; private set { _stintBreakdown = value; OnPropertyChanged("StintBreakdown"); } }
    public int StopsSaved { get => _stopsSaved; private set { _stopsSaved = value; OnPropertyChanged("StopsSaved"); } }
    public string TotalTimeDifference { get => _totalTimeDifference; private set { _totalTimeDifference = value; OnPropertyChanged("TotalTimeDifference"); } }
    public string ExtraTimeAfterLeader { get => _extraTimeAfterLeader; private set { _extraTimeAfterLeader = value; OnPropertyChanged("ExtraTimeAfterLeader"); } }
    public double FirstStintFuel { get => _firstStintFuel; private set { _firstStintFuel = value; OnPropertyChanged("FirstStintFuel"); } }
    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (_validationMessage != value)
            {
                _validationMessage = value;
                OnPropertyChanged("ValidationMessage");
                OnPropertyChanged("IsValidationMessageVisible");
            }
        }
    }
    public double FirstStopTimeLoss { get => _firstStopTimeLoss; private set { _firstStopTimeLoss = value; OnPropertyChanged(); } }
    public bool IsPitstopRequired => RequiredPitStops > 0;
    public string AvgDeltaToLdrValue { get; private set; }
    public string AvgDeltaToPbValue { get; private set; }
    public bool IsValidationMessageVisible => !string.IsNullOrEmpty(ValidationMessage);
    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    // --- DEBUG: what the plugin *actually* pushed to the UI ---
    private string _seenCarName = "—";
    public string SeenCarName
    {
        get => _seenCarName;
        private set { if (_seenCarName != value) { _seenCarName = value; OnPropertyChanged(nameof(SeenCarName)); } }
    }

    private string _seenTrackName = "—";
    public string SeenTrackName
    {
        get => _seenTrackName;
        private set { if (_seenTrackName != value) { _seenTrackName = value; OnPropertyChanged(nameof(SeenTrackName)); } }
    }

    private string _seenSessionSummary = "No Live Data";
    public string SeenSessionSummary
    {
        get => _seenSessionSummary;
        private set { if (_seenSessionSummary != value) { _seenSessionSummary = value; OnPropertyChanged(nameof(SeenSessionSummary)); } }
    }

    // Call this whenever LalaLaunch updates its LiveFuelPerLap so the UI can enable/disable the Live button.
    public void OnLiveFuelPerLapUpdated()
    {
        OnPropertyChanged(nameof(IsLiveFuelPerLapAvailable));
    }
    private void UseLiveFuelPerLap()
    {
        if (LiveFuelPerLap > 0)
        {
            FuelPerLap = LiveFuelPerLap;
            FuelPerLapSourceInfo = "source: live average";
        }
    }

    private void ApplyLiveFuelSuggestionValue()
    {
        UseLiveFuelPerLap();
    }

    private void ApplyLiveMaxFuelSuggestionValue()
    {
        if (_liveMaxFuel > 0)
        {
            MaxFuelOverride = Math.Round(_liveMaxFuel);
        }
    }

    private void ResetStrategyInputs()
    {
        // Reset race-specific parameters to sensible defaults
        this.SelectedRaceType = RaceType.TimeLimited;
        this.RaceLaps = 20;
        this.RaceMinutes = 40;
        this.MandatoryStopRequired = false;

        // Smartly default Max Fuel: use the live detected value if available, otherwise use 120L
        this.MaxFuelOverride = _liveMaxFuel > 0 ? Math.Round(_liveMaxFuel) : 120.0;

        SimHub.Logging.Current.Debug("FuelCalcs: Race strategy inputs have been reset to defaults.");
    }

    private void SavePlannerDataToProfile()
    {
        // Get live and UI-selected car/track names for logic and auditing
        string liveCarName = _plugin.CurrentCarModel;
        string uiCarName = _selectedCarProfile?.ProfileName;

        // 1) Decide which profile to save to
        CarProfile targetProfile = null;
        bool isLiveSession = !string.IsNullOrEmpty(liveCarName) && liveCarName != "Unknown";

        if (isLiveSession)
        {
            // Live: always save to the live car’s profile (create if missing)
            targetProfile = _plugin.ProfilesViewModel.EnsureCar(liveCarName);
        }
        else
        {
            // Non-live: save to the UI-selected profile
            targetProfile = _selectedCarProfile;
        }

        // 2) Guard: we need a profile and a selected track string
        if (targetProfile == null || string.IsNullOrEmpty(_selectedTrack))
        {
            MessageBox.Show("Please select a car and track profile first.", "No Profile Selected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 3) Resolve the selected TrackStats and decide the key/display to save under
        var selectedTs = ResolveSelectedTrackStats();

        string keyToSave = isLiveSession && !string.IsNullOrWhiteSpace(_plugin.CurrentTrackKey) && _plugin.CurrentTrackKey != "Unknown"
                            ? _plugin.CurrentTrackKey
                            : (selectedTs?.Key ?? _selectedTrack); // fallback to the dropdown string if needed

        string nameToSave = selectedTs?.DisplayName ?? _selectedTrack;

        // Non-live: if we still have no real key, stop the save so we don’t create junk
        if (!isLiveSession && (selectedTs == null || string.IsNullOrWhiteSpace(selectedTs.Key)))
        {
            MessageBox.Show(
                "This track doesn’t exist in the selected profile. Create it on the Profiles tab or start a live session first.",
                "Missing track key", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 4) Ensure the record we’re saving into
        var trackRecord = targetProfile.EnsureTrack(keyToSave, nameToSave);

        // 5) Save car-level settings
        targetProfile.FuelContingencyValue = this.ContingencyValue;
        targetProfile.IsContingencyInLaps = this.IsContingencyInLaps;
        targetProfile.WetFuelMultiplier = this.WetFactorPercent;
        targetProfile.TireChangeTime = this.TireChangeTime;
        targetProfile.RacePaceDeltaSeconds = this.RacePaceDeltaOverride;

        var profileCondition = targetProfile.GetConditionMultipliers(IsWet);
        profileCondition.FormationLapBurnLiters = this.FormationLapFuelLiters;
        if (IsWet)
        {
            profileCondition.WetFactorPercent = this.WetFactorPercent;
        }

        // 6) Save track-specific settings
        var lapTimeMs = trackRecord.LapTimeStringToMilliseconds(EstimatedLapTime);
        double.TryParse(FuelPerLapText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double fuelVal);

        if (IsDry)
        {
            if (lapTimeMs.HasValue) trackRecord.AvgLapTimeDry = lapTimeMs;
            if (fuelVal > 0) trackRecord.AvgFuelPerLapDry = fuelVal;
        }
        else // Wet
        {
            if (lapTimeMs.HasValue) trackRecord.AvgLapTimeWet = lapTimeMs;
            if (fuelVal > 0) trackRecord.AvgFuelPerLapWet = fuelVal;
        }

        trackRecord.PitLaneLossSeconds = this.PitLaneTimeLoss;

        if (IsPersonalBestAvailable && _loadedBestLapTimeSeconds > 0)
            trackRecord.BestLapMs = (int)(_loadedBestLapTimeSeconds * 1000);

        var trackCondition = trackRecord.GetConditionMultipliers(IsWet);
        trackCondition.FormationLapBurnLiters = this.FormationLapFuelLiters;
        if (IsWet)
        {
            trackCondition.WetFactorPercent = this.WetFactorPercent;
        }

        // 7) Persist + refresh dependent UI
        _plugin.ProfilesViewModel.SaveProfiles();
        _plugin.ProfilesViewModel.RefreshTracksForSelectedProfile();
        LoadProfileData(); // refresh ProfileAvg labels/sources

        MessageBox.Show(
            $"All planner settings have been saved to the '{targetProfile.ProfileName}' profile for the track '{trackRecord.DisplayName}'.",
            "Planner Data Saved", MessageBoxButton.OK, MessageBoxImage.Information);

    }

    public void ReloadPresetsFromDisk()
    {
        InitPresets();
    }

    private void InitPresets()
    {
        try
        {
            _availablePresets = LaunchPlugin.RacePresetStore.LoadAll();

            // Do NOT auto-select anything on load.
            // Leave both selection and applied preset null until the user picks one.
            _selectedPreset = null;
            _appliedPreset = null;

            OnPropertyChanged(nameof(AvailablePresets));
            OnPropertyChanged(nameof(SelectedPreset));
            OnPropertyChanged(nameof(HasSelectedPreset));
            RaisePresetStateChanged();
        }
        catch (Exception ex)
        {
            SimHub.Logging.Current.Error("FuelCalcs.InitPresets: " + ex.Message);
            _availablePresets = new List<RacePreset>();
            _selectedPreset = null;
            _appliedPreset = null;
            OnPropertyChanged(nameof(AvailablePresets));
            OnPropertyChanged(nameof(SelectedPreset));
            OnPropertyChanged(nameof(HasSelectedPreset));
            RaisePresetStateChanged();
        }
    }

    public void SavePresetEdits(string originalName, RacePreset updated)
    {
        if (updated == null || string.IsNullOrWhiteSpace(updated.Name)) return;

        var list = _availablePresets ?? new List<RacePreset>();

        // Find by original name first (supports rename-on-save)
        var existing = !string.IsNullOrWhiteSpace(originalName)
            ? list.FirstOrDefault(x => string.Equals(x.Name, originalName, StringComparison.OrdinalIgnoreCase))
            : list.FirstOrDefault(x => string.Equals(x.Name, updated.Name, StringComparison.OrdinalIgnoreCase));

        var wasApplied =
            _appliedPreset != null &&
            existing != null &&
            string.Equals(_appliedPreset.Name, existing.Name, StringComparison.OrdinalIgnoreCase);

        if (existing == null)
        {
            // New entry (not found by original/new name) => add one and then update it in-place
            existing = new RacePreset();
            list.Add(existing);
        }

        // In-place update to keep references (ListBox selection etc.) stable
        existing.Name = updated.Name;
        existing.Type = updated.Type;
        existing.RaceMinutes = updated.RaceMinutes;
        existing.RaceLaps = updated.RaceLaps;
        existing.MandatoryStopRequired = updated.MandatoryStopRequired;
        existing.TireChangeTimeSec = updated.TireChangeTimeSec;
        existing.MaxFuelLitres = updated.MaxFuelLitres;
        existing.ContingencyInLaps = updated.ContingencyInLaps;
        existing.ContingencyValue = updated.ContingencyValue;

        // Persist
        LaunchPlugin.RacePresetStore.SaveAll(list);

        // Force the ItemsSource to refresh without touching _selectedPreset
        _availablePresets = list.ToList();

        // If the applied preset is the one we just edited, apply it live so Fuel values refresh
        if (wasApplied)
        {
            _appliedPreset = existing;
            ApplySelectedPreset();
        }
        else
        {
            RaisePresetStateChanged();
        }

        OnPropertyChanged(nameof(AvailablePresets));
        // We did NOT change SelectedPreset; no need to re-raise unless your UI requires it
        OnPropertyChanged(nameof(HasSelectedPreset));
    }

    public void SaveCurrentAsPreset(string name, bool overwriteIfExists)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Preset name cannot be empty.", nameof(name));

        var list = _availablePresets?.ToList() ?? new List<RacePreset>();

        var p = new RacePreset
        {
            Name = name,
            Type = IsTimeLimitedRace ? RacePresetType.TimeLimited : RacePresetType.LapLimited,
            RaceMinutes = IsTimeLimitedRace ? (int?)RaceMinutes : null,
            RaceLaps = IsLapLimitedRace ? (int?)RaceLaps : null,

            MandatoryStopRequired = MandatoryStopRequired,
            TireChangeTimeSec = TireChangeTime,
            MaxFuelLitres = MaxFuelOverride,

            ContingencyInLaps = IsContingencyInLaps,
            ContingencyValue = ContingencyValue
        };

        var existing = list.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (!overwriteIfExists)
                throw new InvalidOperationException("A preset with that name already exists.");
            var idx = list.IndexOf(existing);
            list[idx] = p;
        }
        else
        {
            list.Add(p);
        }

        LaunchPlugin.RacePresetStore.SaveAll(list);

        // Refresh the collection instance so UI lists redraw, but do NOT change Fuel tab selection
        _availablePresets = list.ToList();

        // DO NOT touch _selectedPreset or _appliedPreset here.
        OnPropertyChanged(nameof(AvailablePresets));
        OnPropertyChanged(nameof(HasSelectedPreset));
        RaisePresetStateChanged();
    }

    public void DeletePreset(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var list = _availablePresets?.ToList() ?? new List<RacePreset>();
        var match = list.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match == null) return;

        list.Remove(match);
        LaunchPlugin.RacePresetStore.SaveAll(list);

        _availablePresets = list;
        if (_selectedPreset == match) _selectedPreset = list.FirstOrDefault();
        if (_appliedPreset == match) _appliedPreset = null;

        OnPropertyChanged(nameof(AvailablePresets));
        OnPropertyChanged(nameof(SelectedPreset));
        OnPropertyChanged(nameof(HasSelectedPreset));
        RaisePresetStateChanged();
    }

    public void RenamePreset(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return;

        var list = _availablePresets?.ToList() ?? new List<RacePreset>();
        var match = list.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
        if (match == null) return;

        if (list.Any(x => string.Equals(x.Name, newName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A preset with that name already exists.");

        match.Name = newName;
        LaunchPlugin.RacePresetStore.SaveAll(list);

        // keep selection/applied pointers stable
        _availablePresets = list;
        if (_selectedPreset != null && string.Equals(_selectedPreset.Name, oldName, StringComparison.OrdinalIgnoreCase))
            _selectedPreset = match;
        if (_appliedPreset != null && string.Equals(_appliedPreset.Name, oldName, StringComparison.OrdinalIgnoreCase))
            _appliedPreset = match;

        OnPropertyChanged(nameof(AvailablePresets));
        OnPropertyChanged(nameof(SelectedPreset));
        OnPropertyChanged(nameof(HasSelectedPreset));
        RaisePresetStateChanged();
    }

    // Unique id to make sure the UI and the engine are the same instance
    public string InstanceTag { get; } = Guid.NewGuid().ToString("N").Substring(0, 6);

    public FuelCalcs(LalaLaunch plugin)
    {
        _plugin = plugin;
        RebuildAvailableCarProfiles();

        ResetLiveSnapshotGuards();

        UseLiveLapPaceCommand = new RelayCommand(_ => UseLiveLapPace(),_ => IsLiveLapPaceAvailable);
        UseLiveFuelPerLapCommand = new RelayCommand(_ => UseLiveFuelPerLap());
        LoadProfileLapTimeCommand = new RelayCommand(_ => LoadProfileLapTime(),_ => SelectedCarProfile != null && !string.IsNullOrEmpty(SelectedTrack));
        UseProfileFuelPerLapCommand = new RelayCommand(_ => UseProfileFuelPerLap());
        UseMaxFuelPerLapCommand = new RelayCommand(_ => UseMaxFuelPerLap(), _ => IsMaxFuelAvailable);

        ApplyPresetCommand = new RelayCommand(o => ApplySelectedPreset(), o => HasSelectedPreset);
        ClearPresetCommand = new RelayCommand(o => ClearAppliedPreset());

        InitPresets();  // populate AvailablePresets + default SelectedPreset

        _plugin.ProfilesViewModel.CarProfiles.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(AvailableCarProfiles));
        };
        SavePlannerDataToProfileCommand = new RelayCommand(
            _ => SavePlannerDataToProfile(),
            _ => _selectedCarProfile != null && !string.IsNullOrEmpty(_selectedTrack)
        );
        SetUIDefaults();
        CalculateStrategy();
    }

    private void ResetLiveSnapshotGuards()
    {
        // Live suggestion toggles and refuel-condition timings are reset early so bindings never see stale values
        ApplyLiveFuelSuggestion = false;
        ApplyLiveMaxFuelSuggestion = false;
        ConditionRefuelBaseSeconds = 0;
        ConditionRefuelSecondsPerLiter = 0;
        ConditionRefuelSecondsPerSquare = 0;
        _isRefreshingConditionParameters = false;
    }

    public void SetLiveSession(string carName, string trackName)
    {
        // Always push these UI-bound mutations to the Dispatcher thread
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp == null || disp.CheckAccess())
        {
            ApplyLiveSession(carName, trackName);
        }
        else
        {
            disp.Invoke(() => ApplyLiveSession(carName, trackName));
        }
    }

    // Called by the Live button
    public void UseLiveLapPace()
    {
        if (_liveAvgLapSeconds <= 0) return;

        // The RacePaceDeltaOverride should not be applied here.
        double estSeconds = _liveAvgLapSeconds;
        EstimatedLapTime = TimeSpan.FromSeconds(estSeconds).ToString(@"m\:ss\.fff");
        LapTimeSourceInfo = "source: live average";

        OnPropertyChanged(nameof(EstimatedLapTime));
        OnPropertyChanged(nameof(LapTimeSourceInfo));
        CalculateStrategy();
    }


    public void SetLiveLapPaceEstimate(double avgSeconds, int sampleCount)
    {
        // Ensure all UI updates happen on the UI thread
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp == null || disp.CheckAccess())
        {
            ApplyLiveLapPaceEstimate(avgSeconds, sampleCount);
        }
        else
        {
            disp.Invoke(() => ApplyLiveLapPaceEstimate(avgSeconds, sampleCount));
        }
    }

    // NEW: Private helper to contain the original logic
    private void ApplyLiveLapPaceEstimate(double avgSeconds, int sampleCount)
    {
        if (avgSeconds > 0 && sampleCount >= 3)
        {
            _liveAvgLapSeconds = avgSeconds;
            IsLiveLapPaceAvailable = true;
            LiveLapPaceInfo = TimeSpan.FromSeconds(avgSeconds).ToString(@"m\:ss\.fff");
            if (_loadedBestLapTimeSeconds > 0)
            {
                double delta = avgSeconds - _loadedBestLapTimeSeconds;
                LivePaceDeltaInfo = $"Live Pace Delta: {delta:+#.0#;-#.0#;0.0}s";
            }
        }
        else
        {
            _liveAvgLapSeconds = 0;
            IsLiveLapPaceAvailable = false;
            LiveLapPaceInfo = "-";
            LivePaceDeltaInfo = ""; // Clear delta info when not available
        }

        // Update Delta to Leader Value + store their rolling average pace
        double leaderAvgPace = _plugin.LiveLeaderAvgPaceSeconds;
        if (leaderAvgPace > 0)
        {
            LiveLeaderPaceInfo = TimeSpan.FromSeconds(leaderAvgPace).ToString(@"m\:ss\.fff");
        }
        else
        {
            LiveLeaderPaceInfo = "-";
        }

        if (avgSeconds > 0 && leaderAvgPace > 0)
        {
            double delta = avgSeconds - leaderAvgPace;
            AvgDeltaToLdrValue = $"{delta:F2}s";
            LeaderDeltaSeconds = Math.Max(0.0, delta);
        }
        else
        {
            AvgDeltaToLdrValue = "-";
            LeaderDeltaSeconds = 0.0; // Clear stale leader delta when pace is unavailable
        }
        OnPropertyChanged(nameof(AvgDeltaToLdrValue));

        // Update Delta to PB Value
        if (avgSeconds > 0 && _loadedBestLapTimeSeconds > 0)
        {
            double delta = avgSeconds - _loadedBestLapTimeSeconds;
            AvgDeltaToPbValue = $"{delta:F2}s";
        }
        else
        {
            AvgDeltaToPbValue = "-";
        }
        OnPropertyChanged(nameof(AvgDeltaToPbValue));
        UpdateTrackDerivedSummaries();

        if (IsLiveSessionActive && IsLiveLapPaceAvailable && LapTimeSourceInfo != "source: manual")
        {
            UseLiveLapPace();
        }
    }

    public void SetLiveConfidenceLevels(int fuelConfidence, int paceConfidence, int overallConfidence)
    {
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp == null || disp.CheckAccess())
        {
            ApplyLiveConfidenceLevels(fuelConfidence, paceConfidence, overallConfidence);
        }
        else
        {
            disp.Invoke(() => ApplyLiveConfidenceLevels(fuelConfidence, paceConfidence, overallConfidence));
        }
    }

    private void ApplyLiveConfidenceLevels(int fuelConfidence, int paceConfidence, int overallConfidence)
    {
        LiveFuelConfidence = ClampConfidence(fuelConfidence);
        LivePaceConfidence = ClampConfidence(paceConfidence);
        LiveOverallConfidence = ClampConfidence(overallConfidence);
        LiveConfidenceSummary = BuildConfidenceSummary();

        OnPropertyChanged(nameof(LiveFuelConfidence));
        OnPropertyChanged(nameof(LivePaceConfidence));
        OnPropertyChanged(nameof(LiveOverallConfidence));
        OnPropertyChanged(nameof(LiveConfidenceSummary));
    }

    private static int ClampConfidence(int value)
    {
        if (value < 0) return 0;
        if (value > 100) return 100;
        return value;
    }

    private string BuildConfidenceSummary()
    {
        if (LiveFuelConfidence <= 0 && LivePaceConfidence <= 0 && LiveOverallConfidence <= 0)
        {
            return "Live reliability: n/a";
        }

        return $"Live reliability: Fuel {LiveFuelConfidence}% | Pace {LivePaceConfidence}% | Overall {LiveOverallConfidence}%";
    }

    private void UpdateSurfaceModeLabel()
    {
        string mode = IsWet ? "Wet" : "Dry";
        LiveSurfaceModeDisplay = IsLiveSessionActive ? $"{mode} • Live" : mode;
    }

    private void ResetSnapshotDisplays()
    {
        IsLiveSessionActive = false;
        LiveCarName = "-";
        LiveTrackName = "-";
        LiveFuelTankSizeDisplay = "-";
        LiveBestLapDisplay = "-";
        LiveLeaderPaceInfo = "-";
        LiveLapPaceInfo = "-";
        AvgDeltaToLdrValue = "-";
        LeaderDeltaSeconds = 0.0;
        AvgDeltaToPbValue = "-";
        _liveMaxFuel = 0;
        _liveFuelTankLiters = 0;
        DryLapTimeSummary = "-";
        WetLapTimeSummary = "-";
        DryPaceDeltaSummary = "-";
        WetPaceDeltaSummary = "-";
        DryFuelBurnSummary = "-";
        WetFuelBurnSummary = "-";
        RacePaceVsLeaderSummary = "-";
        LastPitDriveThroughDisplay = "-";
        LastRefuelRateDisplay = "-";
        LastTyreChangeDisplay = "-";
        LiveSurfaceModeDisplay = "-";
        ApplyLiveFuelSuggestion = false;
        ApplyLiveMaxFuelSuggestion = false;
        ConditionRefuelBaseSeconds = 0;
        ConditionRefuelSecondsPerLiter = 0;
        ConditionRefuelSecondsPerSquare = 0;
        SeenCarName = LiveCarName;
        SeenTrackName = LiveTrackName;
        SeenSessionSummary = "No Live Data";
        OnPropertyChanged(nameof(HasLiveMaxFuelSuggestion));
        OnPropertyChanged(nameof(IsMaxFuelOverrideTooHigh));
    }

    private void UpdateTrackDerivedSummaries()
    {
        UpdateLapTimeSummaries();
        UpdatePaceSummaries();
        UpdateRacePaceVsLeaderSummary();
    }

    private void UpdateLapTimeSummaries()
    {
        DryLapTimeSummary = BuildLiveLapSummary(ShowDrySnapshotRows);
        WetLapTimeSummary = BuildLiveLapSummary(ShowWetSnapshotRows);
    }

    private string BuildLiveLapSummary(bool isVisible)
    {
        if (!isVisible) return "-";

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(LiveBestLapDisplay) && LiveBestLapDisplay != "-")
        {
            parts.Add($"PB {LiveBestLapDisplay}");
        }
        if (!string.IsNullOrWhiteSpace(LiveLapPaceInfo) && LiveLapPaceInfo != "-")
        {
            parts.Add($"Avg {LiveLapPaceInfo}");
        }
        return parts.Count > 0 ? string.Join(" | ", parts) : "-";
    }

    private void UpdatePaceSummaries()
    {
        DryPaceDeltaSummary = BuildLivePaceDeltaSummary(ShowDrySnapshotRows);
        WetPaceDeltaSummary = BuildLivePaceDeltaSummary(ShowWetSnapshotRows);
    }

    private void UpdateRacePaceVsLeaderSummary()
    {
        bool hasDriverAvg = !string.IsNullOrWhiteSpace(LiveLapPaceInfo) && LiveLapPaceInfo != "-";
        bool hasLeaderAvg = !string.IsNullOrWhiteSpace(LiveLeaderPaceInfo) && LiveLeaderPaceInfo != "-";
        if (!hasDriverAvg || !hasLeaderAvg)
        {
            RacePaceVsLeaderSummary = "-";
            return;
        }

        var delta = NormalizeDelta(AvgDeltaToLdrValue);
        RacePaceVsLeaderSummary = delta == null
            ? $"Avg {LiveLapPaceInfo} vs Leader {LiveLeaderPaceInfo}"
            : $"Avg {LiveLapPaceInfo} vs Leader {LiveLeaderPaceInfo} (Δ {delta})";
    }

    private string BuildLivePaceDeltaSummary(bool isVisible)
    {
        if (!isVisible) return "-";

        var parts = new List<string>();
        var pbDelta = NormalizeDelta(AvgDeltaToPbValue);
        var leaderDelta = NormalizeDelta(AvgDeltaToLdrValue);

        if (pbDelta != null)
        {
            parts.Add($"Δ PB: {pbDelta}");
        }
        if (leaderDelta != null)
        {
            parts.Add($"Δ Leader: {leaderDelta}");
        }

        return parts.Count > 0 ? string.Join(" | ", parts) : "-";
    }

    private static string NormalizeDelta(string value)
    {
        return string.IsNullOrWhiteSpace(value) || value == "-" ? null : value;
    }

    private string FormatLabel(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private void UpdateFuelBurnSummaries()
    {
        DryFuelBurnSummary = BuildFuelSummary(_liveDryFuelAvg, _liveDryFuelMin, _liveDryFuelMax, _liveDrySamples);
        WetFuelBurnSummary = BuildFuelSummary(_liveWetFuelAvg, _liveWetFuelMin, _liveWetFuelMax, _liveWetSamples);
    }

    private static string BuildFuelSummary(double avg, double min, double max, int samples)
    {
        var parts = new List<string>();
        if (avg > 0) parts.Add($"Avg {avg:F2} L");
        if (min > 0 && max > 0) parts.Add($"Range {min:F2}–{max:F2} L");
        else if (max > 0) parts.Add($"Max {max:F2} L");
        else if (min > 0) parts.Add($"Min {min:F2} L");
        if (samples > 0) parts.Add(samples == 1 ? "1 lap" : $"{samples} laps");
        if (parts.Count == 0) return "-";
        return string.Join(" | ", parts);
    }

    // Helper does the actual updates (runs on UI thread)
    private void ApplyLiveSession(string carName, string trackName)
    {
        bool hasCar = !string.IsNullOrWhiteSpace(carName) && !carName.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
        bool hasTrack = !string.IsNullOrWhiteSpace(trackName) && !trackName.Equals("Unknown", StringComparison.OrdinalIgnoreCase);

        if (!hasCar && !hasTrack)
        {
            ResetSnapshotDisplays();
            return;
        }

        // 1) Make sure the car profile object is selected (this will also rebuild AvailableTracks once below)
        var carProfile = AvailableCarProfiles.FirstOrDefault(
            p => p.ProfileName.Equals(carName, StringComparison.OrdinalIgnoreCase));

        if (this.SelectedCarProfile != carProfile)
        {
            this.SelectedCarProfile = carProfile;
        }

        // 2) Rebuild the Fuel tab track list strictly from the selected profile
        AvailableTrackStats.Clear();
        if (SelectedCarProfile?.TrackStats != null)
        {
            foreach (var t in SelectedCarProfile.TrackStats.Values
                         .OrderBy(t => t.DisplayName ?? t.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                AvailableTrackStats.Add(t);
            }
        }
        OnPropertyChanged(nameof(AvailableTrackStats));

        // 3) Resolve the actual TrackStats to select:
        //    Prefer the plugin's reliable key; fall back to the live display if needed.
        var ts =
            SelectedCarProfile?.FindTrack(_plugin.CurrentTrackKey) ??
            SelectedCarProfile?.TrackStats?.Values
                .FirstOrDefault(t => t.DisplayName?.Equals(trackName, StringComparison.OrdinalIgnoreCase) == true);

        // 4) Select it by instance (this triggers LoadProfileData via SelectedTrackStats setter)
        if (ts != null && !ReferenceEquals(this.SelectedTrackStats, ts))
        {
            this.SelectedTrackStats = ts;
        }

        LiveCarName = hasCar ? carName : "-";
        LiveTrackName = hasTrack ? trackName : "-";
        var displayCarName = hasCar ? carName : "-";
        var displayTrackName = hasTrack ? trackName : "-";
        SeenCarName = LiveCarName;
        SeenTrackName = LiveTrackName;
        IsLiveSessionActive = hasCar && hasTrack;
        SeenSessionSummary = (hasCar || hasTrack)
            ? $"Live: {FormatLabel(displayCarName, "-")} @ {FormatLabel(displayTrackName, "-")}"
            : "No Live Data";

        UpdateTrackDerivedSummaries();

        if (IsLiveSessionActive)
        {
            UpdateSurfaceModeLabel();
        }
        else
        {
            LiveSurfaceModeDisplay = "-";
        }
    }

    private void SetUIDefaults()
    {
        ResetSnapshotDisplays();
        _raceLaps = 20.0;
        _raceMinutes = 40.0;
        _raceType = RaceType.TimeLimited;
        _estimatedLapTime = "2:45.500";
        FuelPerLap = 2.8; // ensures _baseDryFuelPerLap is set
        _maxFuelOverride = 120.0;
        _tireChangeTime = 30.0;
        _pitLaneTimeLoss = 22.5;
        _fuelSaveTarget = 0.1;
        _timeLossPerLapOfFuelSave = "0:00.250";
        _contingencyValue = 1.5;
        _isContingencyInLaps = true;
        _wetFactorPercent = 90.0;
        HistoricalBestLapDisplay = "-";
        ProfileAvgDryLapTimeDisplay = "-";
        ProfileAvgDryFuelDisplay = "-";
    }

    public void ForceProfileDataReload()
    {
        LoadProfileData();
    }
    public void LoadProfileData()
    {
        if (SelectedCarProfile == null || string.IsNullOrEmpty(SelectedTrack))
        {
            SetUIDefaults();
            CalculateStrategy();
            return;
        }

        var car = SelectedCarProfile;

        // Keep an internal object reference in sync with the dropdown string
        SelectedTrackStats = ResolveSelectedTrackStats();
        var ts = SelectedTrackStats;

        // --- Load Refuel Rate from profile ---
        this._refuelRate = car.RefuelRate;

        // --- Initialize the "What-If" parameters from the profile ---
        this.ContingencyValue = car.FuelContingencyValue;
        this.IsContingencyInLaps = car.IsContingencyInLaps;
        this.WetFactorPercent = car.WetFuelMultiplier;
        this.TireChangeTime = car.TireChangeTime;
        this.RacePaceDeltaOverride = car.RacePaceDeltaSeconds;

        if (ts?.BestLapMs is int ms && ms > 0)
        {
            _loadedBestLapTimeSeconds = ms / 1000.0;
            IsPersonalBestAvailable = true;
            HistoricalBestLapDisplay = TimeSpan.FromMilliseconds(ms).ToString(@"m\:ss\.fff");
        }
        else
        {
            _loadedBestLapTimeSeconds = 0;
            IsPersonalBestAvailable = false;
            HistoricalBestLapDisplay = "-";
        }
        // Manually notify the UI that these properties have changed
        OnPropertyChanged(nameof(RacePaceDeltaOverride));
        OnPropertyChanged(nameof(IsPersonalBestAvailable));
        OnPropertyChanged(nameof(HistoricalBestLapDisplay));


        // --- Set the initial estimated lap time from the profile's dry average ---
        if (ts?.AvgLapTimeDry is int dryMs && dryMs > 0)
        {
            EstimatedLapTime = TimeSpan.FromMilliseconds(dryMs).ToString(@"m\:ss\.fff");
            LapTimeSourceInfo = "source: dry avg";
        }
        else
        {
            // If there's no saved dry average, fall back to the PB if it exists
            if (_loadedBestLapTimeSeconds > 0)
            {
                LoadPersonalBestAsRacePace(); // This will calculate a pace from the PB
            }
            else
            {
                // If there's no data at all, use the UI default
                EstimatedLapTime = "2:45.500";
                LapTimeSourceInfo = "source: manual";
            }
        }

        // --- Load historical/track-specific data ---
        if (ts?.AvgFuelPerLapDry is double avg && avg > 0)
        {
            _baseDryFuelPerLap = avg;
            FuelPerLap = IsDry ? avg : avg * (WetFactorPercent / 100.0);
            FuelPerLapSourceInfo = "source: profile";
        }
        else
        {
            // Handle case where track exists but has no fuel data.
            // Reset to the global default value and update the source text.
            var defaultProfile = _plugin.ProfilesViewModel.GetProfileForCar("Default Settings");
            var defaultFuel = defaultProfile?.TrackStats?["default"]?.AvgFuelPerLapDry ?? 2.8;
            FuelPerLap = defaultFuel;
            FuelPerLapSourceInfo = "source: default";
        }

        if (ts?.PitLaneLossSeconds is double pll && pll > 0)
        {
            PitLaneTimeLoss = pll;
            SetLastPitDriveThroughSeconds(PitLaneTimeLoss);
        }

        // --- CONSOLIDATED: Populate all display properties ---
        var dryLap = ts?.AvgLapTimeDry;
        var wetLap = ts?.AvgLapTimeWet;
        var dryFuel = ts?.AvgFuelPerLapDry;
        var wetFuel = ts?.AvgFuelPerLapWet;

        ProfileAvgLapTimeDisplay = IsDry
            ? (dryLap.HasValue ? TimeSpan.FromMilliseconds(dryLap.Value).ToString(@"m\:ss\.fff") : "-")
            : (wetLap.HasValue ? TimeSpan.FromMilliseconds(wetLap.Value).ToString(@"m\:ss\.fff") : "-");

        ProfileAvgFuelDisplay = IsDry
            ? (dryFuel.HasValue ? dryFuel.Value.ToString("F2") + " L" : "-")
            : (wetFuel.HasValue ? wetFuel.Value.ToString("F2") + " L" : "-");

        ProfileAvgDryLapTimeDisplay = (dryLap.HasValue && dryLap.Value > 0)
            ? TimeSpan.FromMilliseconds(dryLap.Value).ToString(@"m\:ss\.fff")
            : "-";

        ProfileAvgDryFuelDisplay = (dryFuel.HasValue && dryFuel.Value > 0)
            ? dryFuel.Value.ToString("F2") + " L"
            : "-";

        HasProfileFuelPerLap = ts?.AvgFuelPerLapDry > 0 || ts?.AvgFuelPerLapWet > 0;

        RefreshConditionParameters();
        ResetStrategyInputs();

        // Manually notify the UI of all changes
        OnPropertyChanged(nameof(ProfileAvgLapTimeDisplay));
        OnPropertyChanged(nameof(ProfileAvgFuelDisplay));
        OnPropertyChanged(nameof(ProfileAvgDryLapTimeDisplay));
        OnPropertyChanged(nameof(ProfileAvgDryFuelDisplay));
        OnPropertyChanged(nameof(HasProfileFuelPerLap));

        // Recompute with the newly loaded data
        CalculateStrategy();

        UpdateTrackDerivedSummaries();
    }

    private void ApplyWetFactor()
    {
        if (IsWet) { FuelPerLap = _baseDryFuelPerLap * (WetFactorPercent / 100.0); }
    }

    private void RefreshConditionParameters()
    {
        if (_isRefreshingConditionParameters) return;
        _isRefreshingConditionParameters = true;
        try
        {
            var car = SelectedCarProfile;
            var ts = SelectedTrackStats ?? ResolveSelectedTrackStats();
            bool isWet = IsWet;

            var carMultipliers = car?.GetConditionMultipliers(isWet);
            var trackMultipliers = ts?.GetConditionMultipliers(isWet);

            double defaultFormation = carMultipliers?.FormationLapBurnLiters ?? 1.5;
            double targetFormation = trackMultipliers?.FormationLapBurnLiters ?? defaultFormation;

            if (targetFormation > 0 && Math.Abs(FormationLapFuelLiters - targetFormation) > 0.01)
            {
                FormationLapFuelLiters = targetFormation;
            }

            if (isWet)
            {
                double fallbackWet = carMultipliers?.WetFactorPercent ?? car?.WetFuelMultiplier ?? WetFactorPercent;
                double targetWet = trackMultipliers?.WetFactorPercent ?? fallbackWet;
                if (targetWet > 0 && Math.Abs(WetFactorPercent - targetWet) > 0.01)
                {
                    WetFactorPercent = targetWet;
                }
            }

            _conditionRefuelBaseSeconds = trackMultipliers?.RefuelSecondsBase ?? carMultipliers?.RefuelSecondsBase ?? 0.0;
            _conditionRefuelSecondsPerLiter = trackMultipliers?.RefuelSecondsPerLiter ?? carMultipliers?.RefuelSecondsPerLiter ?? 0.0;
            _conditionRefuelSecondsPerSquare = trackMultipliers?.RefuelSecondsPerSquare ?? carMultipliers?.RefuelSecondsPerSquare ?? 0.0;
        }
        finally
        {
            _isRefreshingConditionParameters = false;
        }
    }

    public void UpdateLiveDisplay(double liveMaxFuel)
    {
        _liveMaxFuel = liveMaxFuel; // Store the latest value for the next check
        _liveFuelTankLiters = liveMaxFuel;
        if (liveMaxFuel > 0) { DetectedMaxFuelDisplay = $"(Detected Max: {liveMaxFuel:F1} L)"; }
        else { DetectedMaxFuelDisplay = "(Detected Max: N/A)"; }
        LiveFuelTankSizeDisplay = liveMaxFuel > 0 ? $"{liveMaxFuel:F1} L" : "-";
        if (liveMaxFuel <= 0)
        {
            ApplyLiveMaxFuelSuggestion = false;
        }
        OnPropertyChanged(nameof(DetectedMaxFuelDisplay));
        OnPropertyChanged(nameof(IsMaxFuelOverrideTooHigh)); // Notify UI to re-check the highlight
        OnPropertyChanged(nameof(HasLiveMaxFuelSuggestion));
    }

    public void LoadPersonalBestAsRacePace()
    {
        if (!IsPersonalBestAvailable || _loadedBestLapTimeSeconds <= 0) return;

        double estSeconds = _loadedBestLapTimeSeconds + RacePaceDeltaOverride;
        EstimatedLapTime = TimeSpan.FromSeconds(estSeconds).ToString(@"m\:ss\.fff");
        LapTimeSourceInfo = "source: PB";

        OnPropertyChanged(nameof(EstimatedLapTime));
        OnPropertyChanged(nameof(LapTimeSourceInfo));
        CalculateStrategy();
    }

    private static double ComputeExtraSecondsAfterTimerZero(double leaderLapSec, double yourLapSec, double raceSeconds)
    {
        if (leaderLapSec <= 0.0 || yourLapSec <= 0.0 || raceSeconds <= 0.0) return 0.0;

        // Phase within each lap when the clock hits zero
        double phaseL = raceSeconds % leaderLapSec;
        double tL_rem = leaderLapSec - phaseL;        // leader time-to-line
        if (tL_rem >= leaderLapSec - 1e-6) tL_rem = 0.0;

        double phaseY = raceSeconds % yourLapSec;
        double tY_rem = yourLapSec - phaseY;          // your time-to-line
        if (tY_rem >= yourLapSec - 1e-6) tY_rem = 0.0;

        // Baseline: assume one additional lap after zero
        double extra = tY_rem + yourLapSec;

        // Edge case: leader crosses nearly immediately and you narrowly AVOID being lapped,
        // which can force you into two full laps after your next line crossing.
        // If the leader will complete their next lap before you can reach the line once,
        // you likely owe a second full lap.
        bool leaderNextLapBeatsYouToLine = (tL_rem + leaderLapSec) < tY_rem;

        if (leaderNextLapBeatsYouToLine)
        {
            // Two-lap overrun: you finish current partial + two full laps
            extra = tY_rem + (2.0 * yourLapSec);
        }

        return Math.Max(0.0, extra);
    }


        public void CalculateStrategy()
        {
            var ts = _plugin.ProfilesViewModel.TryGetCarTrack(SelectedCarProfile?.ProfileName, SelectedTrack);
            bool usingDefaultProfile = false;
            if (ts == null)
            {
                // fall back to default profile track (if you have one), or leave current values
                var defaultProfile = _plugin.ProfilesViewModel.GetProfileForCar("Default Settings");
                ts = defaultProfile?.FindTrack("default");
                usingDefaultProfile = (ts != null);
            }

            double fuelPerLap = FuelPerLap;

            double num = PitLaneTimeLoss; // use the current value directly

            double num3 = ParseLapTime(EstimatedLapTime);          // your estimated lap time
            double num2 = num3 - LeaderDeltaSeconds;               // leader pace (your pace - delta)

            double num4 = ParseLapTime(TimeLossPerLapOfFuelSave);  // fuel-save lap time loss
            if (double.IsNaN(num4) || double.IsInfinity(num4) || num4 < 0.0)
            {
                num4 = 0.0;
            }

            // --- Validation guards ---------------------------------------------------
            _isMissingTrackValidation = false;

            bool lapInvalid = double.IsNaN(num3) || double.IsInfinity(num3) ||
                              num3 <= 0.0 || num3 < 20.0 || num3 > 900.0;

            bool leaderInvalid = double.IsNaN(num2) || double.IsInfinity(num2) ||
                                 num2 <= 0.0 || num2 < 20.0 || num2 > 900.0;

            bool fuelInvalid = double.IsNaN(fuelPerLap) || double.IsInfinity(fuelPerLap) ||
                               fuelPerLap <= 0.0 || fuelPerLap > 50.0;

            bool tankInvalid = double.IsNaN(MaxFuelOverride) || double.IsInfinity(MaxFuelOverride) ||
                               MaxFuelOverride <= 0.0 || MaxFuelOverride > 500.0;

            if (lapInvalid)
            {
                ValidationMessage = "Error: Your Estimated Lap Time must be between 20s and 900s.";
            }
            else if (leaderInvalid)
            {
                ValidationMessage = "Error: Leader pace must be between 20s and 900s (check your delta).";
            }
            else if (fuelInvalid)
            {
                ValidationMessage = "Error: Fuel per Lap must be greater than zero and under 50L.";
            }
            else if (tankInvalid)
            {
                ValidationMessage = "Error: Max Fuel Override must be between 0 and 500 litres.";
            }
            else
            {
                ValidationMessage = "";
            }

            if (IsValidationMessageVisible)
            {
                TotalFuelNeeded = 0.0;
                RequiredPitStops = 0;
                StintBreakdown = "";
                StopsSaved = 0;
                TotalTimeDifference = "N/A";
                ExtraTimeAfterLeader = "N/A";
                FirstStintFuel = 0.0;
                return;
            }
            // ------------------------------------------------------------------------

            double num6 = 0.0;
            if (IsTimeLimitedRace)
            {
                int num7 = 0;
                int num8 = -1;
                int num9 = 0;
                double num10 = fuelPerLap; // already includes wet factor when IsWet
                double num11 = RaceMinutes * 60.0;
                while (num7 != num8 && num9 < 10)
                {
                    num9++;
                    num8 = num7;
                    double num12 = (double)num7 * (num + TireChangeTime);
                    double num13 = num11 - num12;
                    if (num13 < 0.0)
                    {
                        num13 = 0.0;
                    }
                    double num14 = num13 / num2;
                    int num15 = 0;
                    if (num3 - num2 > 0.0 && num3 > 0.0)
                    {
                        num15 = (int)Math.Floor(num14 / (num2 / (num3 - num2)));
                    }
                    num6 = Math.Max(0.0, num14 - (double)num15);
                    double num17 = num6 * num10;
                    double num18 = (IsContingencyInLaps ? (ContingencyValue * num10) : ContingencyValue);
                    double num19 = num17 + num18;
                    num7 = ((num19 > MaxFuelOverride)
                        ? (int)Math.Ceiling((num19 - MaxFuelOverride) / MaxFuelOverride)
                        : 0);
                }
            }
            else
            {
                int num20 = 0;
                if (num3 - num2 > 0.0 && num3 > 0.0)
                {
                    num20 = (int)Math.Floor(RaceLaps / (num2 / (num3 - num2)));
                }
                num6 = Math.Max(0.0, RaceLaps - (double)num20);
            }

            StrategyResult strategyResult = CalculateSingleStrategy(
                num6, fuelPerLap, num3, num2, num, RaceMinutes * 60.0);

            TotalFuelNeeded = strategyResult.TotalFuel;
            RequiredPitStops = strategyResult.Stops;
            StintBreakdown = strategyResult.Breakdown;
            FirstStintFuel = strategyResult.FirstStintFuel;
            FirstStopTimeLoss = strategyResult.FirstStopTimeLoss;
            OnPropertyChanged(nameof(IsPitstopRequired));

            if (IsTimeLimitedRace && num3 > 0.0)
            {
                double extra = ComputeExtraSecondsAfterTimerZero(
                    leaderLapSec: num2,   // leader pace (your pace - delta)
                    yourLapSec: num3,     // your estimated pace
                    raceSeconds: RaceMinutes * 60.0
                );
                ExtraTimeAfterLeader = TimeSpan.FromSeconds(extra).ToString("m\\:ss");
            }
            else
            {
                ExtraTimeAfterLeader = "N/A";
            }

            double num24 = fuelPerLap - FuelSaveTarget;
            if (num24 <= 0.0)
            {
                StopsSaved = 0;
                TotalTimeDifference = "N/A";
                return;
            }

            StrategyResult strategyResult2 = CalculateSingleStrategy(
                num6, num24, num3 + num4, num2, num, RaceMinutes * 60.0);

            StopsSaved = strategyResult.Stops - strategyResult2.Stops;
            double num25 = strategyResult2.TotalTime - strategyResult.TotalTime;
            TotalTimeDifference =
                $"{(num25 >= 0.0 ? "+" : "-")}{TimeSpan.FromSeconds(Math.Abs(num25)):m\\:ss\\.fff}";
        }


        private StrategyResult CalculateSingleStrategy(double totalLaps, double fuelPerLap, double playerPaceSeconds, double leaderPaceSeconds, double pitLaneTimeLoss, double raceClockSeconds)
    {
        StrategyResult result = new StrategyResult { PlayerLaps = totalLaps };
        // Can the leader ever get at least +1 lap within the race clock?
        bool anyLappingPossible =
            (leaderPaceSeconds > 0) &&
            ((raceClockSeconds / leaderPaceSeconds) >= (totalLaps + 1));


        // 1. Calculate Total Fuel Needed for the entire race
        double contingencyFuel = IsContingencyInLaps ? (ContingencyValue * fuelPerLap) : ContingencyValue;
        result.TotalFuel = (totalLaps * fuelPerLap) + contingencyFuel + FormationLapFuelLiters;

        // If no stop is needed, we're done (unless user requires a stop).
        if (result.TotalFuel <= MaxFuelOverride && !MandatoryStopRequired)
        {
            result.Stops = 0;
            result.FirstStintFuel = result.TotalFuel;

            var bodyNoStop = new StringBuilder();
            bodyNoStop.Append($"STINT 1:  {totalLaps:F0} Laps   Est {TimeSpan.FromSeconds(totalLaps * playerPaceSeconds):hh\\:mm\\:ss}   Start {result.TotalFuel:F1} litres");

            var lappedEventsNoStop = new List<int>();
            double cumP = 0, cumL = 0;          // player and leader clocks (same wall time)
            int playerLap = 0, nextAhead = 1;   // report +1 first, then +2, ...

            if ((leaderPaceSeconds > 0) && ((raceClockSeconds / leaderPaceSeconds) >= (totalLaps + 1)))
            {
                for (int lap = 0; lap < (int)totalLaps; lap++)
                {
                    // Advance BOTH timelines by the same wall time: one of your laps
                    cumP += playerPaceSeconds;
                    cumL += playerPaceSeconds;
                    playerLap++;

                    int leaderLap = (int)Math.Floor(cumL / leaderPaceSeconds);

                    while (leaderLap >= (playerLap + nextAhead))
                    {
                        var ts = TimeSpan.FromSeconds(cumP);
                        int down = leaderLap - playerLap;
                        bodyNoStop.AppendLine();
                        bodyNoStop.AppendLine($"LAPPED:   {ts:hh\\:mm\\:ss}   Around Lap {playerLap}   (+{down})");
                        lappedEventsNoStop.Add(playerLap);
                        nextAhead++;
                    }
                }
            }

            // Summary: only include segments that have data
            var summaryPartsNoStop = new List<string> { $"{totalLaps:F0} Laps" };
            // No “0 Stops” segment for no-stop races
            if (lappedEventsNoStop.Count > 0)
                summaryPartsNoStop.Add($"Lapped on Lap {string.Join(", ", lappedEventsNoStop)}");

            var headerNoStop = "Summary:  " + string.Join(" | ", summaryPartsNoStop);
            result.Breakdown = headerNoStop + Environment.NewLine + Environment.NewLine + bodyNoStop.ToString();

            result.TotalTime = totalLaps * playerPaceSeconds;
            return result;
        }
        // Mandatory-tyres integration: if baseline would be 0-stop, force exactly one stop
        // If baseline would be 0-stop but a mandatory stop is requested, force exactly one stop
        // If baseline would be 0-stop but a mandatory stop is requested, force exactly one stop
        // If baseline would be 0-stop but a mandatory stop is requested, force exactly one stop
        // If baseline would be 0-stop but a mandatory stop is requested, force exactly one stop
        else if (result.TotalFuel <= MaxFuelOverride && MandatoryStopRequired)
        {
            // Base components
            double lane = pitLaneTimeLoss;
            double tyres = Math.Max(0.0, TireChangeTime);

            // ----- Time-limited reduces laps; lap-limited keeps laps -----
            double adjustedLaps;
            double drivingTimeSeconds;
            double pitAtLap;

            if (SelectedRaceType == RaceType.TimeLimited)
            {
                double raceSecondsLocal = RaceMinutes * 60.0;
                pitAtLap = Math.Max(1.0, Math.Floor((raceSecondsLocal * 0.5) / playerPaceSeconds));

                // Subtract stop time from race clock (we'll compute stop time after we know pourTime)
                // For now, assume stop time = 0 to derive a provisional adjustedLaps,
                // then we’ll recompute accurately after pourTime is known.
                double driveSecondsProvisional = raceSecondsLocal; // provisional
                adjustedLaps = Math.Max(1.0, Math.Floor(driveSecondsProvisional / playerPaceSeconds));
                drivingTimeSeconds = driveSecondsProvisional; // provisional, replaced later
            }
            else
            {
                drivingTimeSeconds = totalLaps * playerPaceSeconds;
                adjustedLaps = totalLaps;
                pitAtLap = Math.Max(1.0, Math.Floor(totalLaps * 0.5));
            }

            // ----- Split using the clamp helper -----
            var (firstStintLaps, secondStintLaps, showSecondStint) = ClampStintSplits(adjustedLaps, pitAtLap);

            // Start grid is FULL, but the stint laps must reflect formation burn
            double effectiveStartFuel2 = Math.Max(0.0, MaxFuelOverride - FormationLapFuelLiters);
            firstStintLaps = (fuelPerLap > 0.0) ? Math.Floor(effectiveStartFuel2 / fuelPerLap) : 0.0;

            // Display always shows a full tank on the grid
            result.FirstStintFuel = Math.Round(MaxFuelOverride, 1);


            // How much fuel would be added for stint 2 (display-only if you keep tyres-only strategy)
            double addLitres = showSecondStint ? Math.Max(0.0, fuelPerLap * secondStintLaps) : 0.0;

            // --- Real pour time using fallback rate when no car/profile data is available ---
            double pourTime = ComputeRefuelSeconds(addLitres);

            // Final stop time respects parallel ops: lane + max(tyres, pour)
            double estStopTime = lane + Math.Max(tyres, pourTime);

            // Recompute time-limited driving seconds (now that estStopTime is known)
            if (SelectedRaceType == RaceType.TimeLimited)
            {
                double raceSecondsLocal = RaceMinutes * 60.0;
                double driveSeconds = Math.Max(0.0, raceSecondsLocal - estStopTime);
                drivingTimeSeconds = driveSeconds;
                adjustedLaps = Math.Max(1.0, Math.Floor(driveSeconds / playerPaceSeconds));

                // If lap count changed due to accurate stop time, resplit cleanly
                (firstStintLaps, secondStintLaps, showSecondStint) =
                    ClampStintSplits(adjustedLaps, Math.Max(1.0, Math.Floor((raceSecondsLocal * 0.5) / playerPaceSeconds)));

                addLitres = showSecondStint ? Math.Max(0.0, fuelPerLap * secondStintLaps) : 0.0;
                pourTime = ComputeRefuelSeconds(addLitres);
                estStopTime = lane + Math.Max(tyres, pourTime);

                result.TotalFuel = Math.Round(fuelPerLap * adjustedLaps, 1);
                result.FirstStintFuel = Math.Round(Math.Min(MaxFuelOverride, (fuelPerLap * firstStintLaps) + FormationLapFuelLiters), 1);
            }

            // Totals & per-stop
            result.TotalTime = drivingTimeSeconds + estStopTime;
            result.Stops = 1;
            result.FirstStopTimeLoss = estStopTime;

            // ----- Breakdown (style aligned) -----
            var header = $"Summary:  {adjustedLaps:F0} Laps  |  1 Mandatory Stop";
            var sb = new StringBuilder();

            var pitApprox = TimeSpan.FromSeconds(firstStintLaps * playerPaceSeconds);
            sb.AppendLine($"Pit at: Lap {firstStintLaps:F0} (≈ {pitApprox:mm\\:ss})");
            sb.AppendLine();

            var stint1Time = TimeSpan.FromSeconds(Math.Floor(firstStintLaps * playerPaceSeconds));
            sb.AppendLine($"STINT 1:  {firstStintLaps:F0} Laps   Est {stint1Time:hh\\:mm\\:ss}   Start {result.FirstStintFuel:F1} litres");

            var stopTs = TimeSpan.FromSeconds(estStopTime);
            string suffix = BuildStopSuffix(tyres, pourTime);
            sb.AppendLine($"STOP 1:   Est {estStopTime:F1}s   Lane {lane:F1}s   Tyres {tyres:F1}s   Fuel {pourTime:F1}s  {suffix}");

            if (showSecondStint)
            {
                var stint2Time = TimeSpan.FromSeconds(Math.Floor(secondStintLaps * playerPaceSeconds));
                sb.AppendLine($"STINT 2:  {secondStintLaps:F0} Laps   Est {stint2Time:hh\\:mm\\:ss}   Add {addLitres:F1} litres");
            }

            result.Breakdown = header + Environment.NewLine + Environment.NewLine + sb.ToString();

            return result;
        }

        // --- Logic for races requiring pit stops ---
        // We build the body first, then prepend a one-line Summary header.
        var body = new StringBuilder();
        double lapsRemaining = totalLaps;
        double fuelNeededFromPits = result.TotalFuel - MaxFuelOverride;
        double totalPitTime = 0.0;

        // --- Lapping bookkeeping (multi-events, leader pits same cadence) ---
        double cumPlayerTime = 0.0;   // your race clock (s)
        double cumLeaderTime = 0.0;   // leader's race clock (s) – advance same wall time
        int playerLapsSoFar = 0;   // completed player laps
        int nextCatchAhead = 1;    // we’ll report +1 lap first, then +2, etc.
        var lappedEvents = new List<int>(); // for Summary: lap numbers the leader catches you

        // Calculate how many stops are required
        result.Stops = (int)Math.Ceiling(fuelNeededFromPits / MaxFuelOverride);

        // Stint 1 (starting stint)
        // Include formation fuel in the starting load, but respect the tank cap.
        // Start grid is FULL, but we already BURN formation fuel before Lap 1 starts.
        double effectiveStartFuel = Math.Max(0.0, MaxFuelOverride - FormationLapFuelLiters);

        // First-stint laps must be based on *effective* fuel at Lap 1 start
        double lapsInFirstStint = (fuelPerLap > 0.0) ? Math.Floor(effectiveStartFuel / fuelPerLap) : 0.0;

        // UI should always show a full tank at the start of the race
        result.FirstStintFuel = Math.Round(MaxFuelOverride, 1);

        lapsRemaining -= lapsInFirstStint;

        // Fuel actually in the tank when you reach pit-in (after formation burn + first-stint laps)
        double fuelAtPitIn = Math.Max(0.0, effectiveStartFuel - (lapsInFirstStint * fuelPerLap));


        body.Append($"STINT 1:  {lapsInFirstStint:F0} Laps   Est {TimeSpan.FromSeconds(lapsInFirstStint * playerPaceSeconds):hh\\:mm\\:ss}   Start {result.FirstStintFuel:F1} litres");

        // Stint 1: walk each lap so we can emit exact catch lap(s)
        if (anyLappingPossible)
        {
            for (int lap = 0; lap < (int)lapsInFirstStint; lap++)
            {
                cumPlayerTime += playerPaceSeconds;
                cumLeaderTime += playerPaceSeconds;
                playerLapsSoFar++;

                int leaderLaps = (int)Math.Floor(cumLeaderTime / leaderPaceSeconds);

                while (leaderLaps >= (playerLapsSoFar + nextCatchAhead))
                {
                    var ts = TimeSpan.FromSeconds(cumPlayerTime);
                    int lapsDown = leaderLaps - playerLapsSoFar;
                    body.AppendLine();
                    body.AppendLine($"LAPPED:   {ts:hh\\:mm\\:ss}   Around Lap {playerLapsSoFar}   (+{lapsDown})");
                    lappedEvents.Add(playerLapsSoFar);
                    nextCatchAhead++;
                }
            }
        }
        else
        {
            // no lapping possible; advance in bulk
            cumPlayerTime += lapsInFirstStint * playerPaceSeconds;
            cumLeaderTime += lapsInFirstStint * playerPaceSeconds;
            playerLapsSoFar += (int)lapsInFirstStint;
        }


        // --- Contingency bookkeeping ---
        // If the UI is "extra laps", carry those laps and convert to fuel only when (and if) we apply them.
        // If the UI is "extra litres", that's a fixed fuel amount we only apply once (final stop).
        double remainingContingencyLaps = _isContingencyInLaps ? _contingencyValue : 0.0;
        double contingencyLitresOnce = _isContingencyInLaps ? 0.0 : _contingencyValue;

        // Loop through each required pit stop
        for (int i = 1; i <= result.Stops; i++)
        {
            body.AppendLine();

            // Calculate fuel needed for the REST of the race
            // Apply contingency ONLY on the final stop
            // - If contingency is "extra laps": convert those laps to litres here, once, on the last stop.
            // - If contingency is "extra litres": add that litre amount here, once, on the last stop.
            bool isFinalStop = (i == result.Stops);

            double contingencyForThisStopFuel = 0.0;
            if (isFinalStop)
            {
                if (_isContingencyInLaps)
                {
                    contingencyForThisStopFuel = remainingContingencyLaps * fuelPerLap;
                    remainingContingencyLaps = 0.0; // consumed
                }
                else
                {
                    contingencyForThisStopFuel = contingencyLitresOnce; // a single-shot fuel amount
                    contingencyLitresOnce = 0.0; // consumed
                }
            }

            // Fuel you need for the rest of the race (this stop and beyond)
            double fuelForRemainingLaps = (lapsRemaining * fuelPerLap) + contingencyForThisStopFuel;


            // The fuel to add is enough for the rest of the race, capped by tank size.
            // Only add what you need beyond what is already in the tank at pit-in, capped by tank size
            double fuelToAdd = Math.Max(0.0, Math.Min(MaxFuelOverride - fuelAtPitIn, fuelForRemainingLaps - fuelAtPitIn));
            double fuelToFillTo = fuelToAdd; // In iRacing, "Fill To" is the amount to add.

            // Calculate pit stop time for this specific stop
            double refuelTime = ComputeRefuelSeconds(fuelToAdd);
            double stationaryTime = Math.Max(this.TireChangeTime, refuelTime);
            double totalStopTime = pitLaneTimeLoss + Math.Max(this.TireChangeTime, refuelTime);
            // ... STOP line (now using BuildStopSuffix(this.TireChangeTime, refuelTime)) ...
            if (i == 1) { result.FirstStopTimeLoss = totalStopTime; }
            totalPitTime += totalStopTime;

            // STOP line (one line, hh:mm:ss total + components) + clear suffix
            var stopTs = TimeSpan.FromSeconds(totalStopTime);
            body.AppendLine();
            string stopSuffix = BuildStopSuffix(this.TireChangeTime, refuelTime);
            body.AppendLine($"STOP {i}:   Est {totalStopTime:F1}s   Lane {pitLaneTimeLoss:F1}s   Tyres {this.TireChangeTime:F1}s   Fuel {refuelTime:F1}s  {stopSuffix}");
            body.AppendLine();

            // Next stint length in laps — robustly clamped to [0, lapsRemaining]
            double fuelAtPitExit = fuelAtPitIn + fuelToAdd;
            double lapsInNextStint = 0.0;
            if (fuelPerLap > 0.0)
            {
                lapsInNextStint = Math.Floor(fuelAtPitExit / fuelPerLap);
                lapsInNextStint = Math.Max(0.0, Math.Min(lapsRemaining, lapsInNextStint));
            }

            // Hide an insignificantly small final stint to avoid “-1 Laps” style artifacts
            bool showNextStint = lapsInNextStint >= 0.5;

            if (showNextStint)
            {
                body.Append($"STINT {i + 1}:  {lapsInNextStint:F0} Laps   Est {TimeSpan.FromSeconds(lapsInNextStint * playerPaceSeconds):hh\\:mm\\:ss}   Add {fuelToFillTo:F1} litres");
            }
            else
            {
                // Nothing meaningful to print; treat as race end after this stop
                lapsInNextStint = 0.0;
            }

            // Advance both timelines by the *same* pit time (assume leader pits when you pit)
            cumPlayerTime += totalStopTime;
            cumLeaderTime += totalStopTime;

            // Now walk the stint lap-by-lap and emit every catch that occurs
            if (anyLappingPossible)
            {
                for (int lap = 0; lap < (int)lapsInNextStint; lap++)
                {
                    cumPlayerTime += playerPaceSeconds;
                    cumLeaderTime += playerPaceSeconds;

                    playerLapsSoFar++;

                    int leaderLaps = (int)Math.Floor(cumLeaderTime / leaderPaceSeconds);

                    while (leaderLaps >= (playerLapsSoFar + nextCatchAhead))
                    {
                        var ts = TimeSpan.FromSeconds(cumPlayerTime);
                        int lapsDown = leaderLaps - playerLapsSoFar;
                        body.AppendLine();
                        body.AppendLine($"LAPPED:   {ts:hh\\:mm\\:ss}   Around Lap {playerLapsSoFar}   (+{lapsDown})");
                        lappedEvents.Add(playerLapsSoFar);
                        nextCatchAhead++;
                    }
                }
            }
            else
            {
                // no lapping expected; advance in bulk
                cumPlayerTime += lapsInNextStint * playerPaceSeconds;
                cumLeaderTime += lapsInNextStint * playerPaceSeconds;
                playerLapsSoFar += (int)lapsInNextStint;
            }


            lapsRemaining -= lapsInNextStint;
            if (lapsInNextStint < lapsRemaining)
            {
                // carry leftover fuel into the next pit-in calculation
                fuelAtPitIn = Math.Max(0.0, fuelAtPitExit - (lapsInNextStint * fuelPerLap));
            }
            else
            {
                fuelAtPitIn = 0.0; // race is done after this stint, no next pit
            }
        }

        // Summary: only include non-empty facts
        var summaryParts = new List<string> { $"{totalLaps:F0} Laps" };
        if (result.Stops > 0) summaryParts.Add($"{result.Stops} Stops");
        if (lappedEvents.Count > 0) summaryParts.Add($"Lapped on Lap {string.Join(", ", lappedEvents)}");

        var summary = "Summary:  " + string.Join(" | ", summaryParts);
        result.Breakdown = summary + Environment.NewLine + Environment.NewLine + body.ToString();

        result.TotalTime = totalLaps * playerPaceSeconds + totalPitTime;
        return result;
    }
    private double ParseLapTime(string timeString)
    {
        if (string.IsNullOrWhiteSpace(timeString)) return 0.0;

        // be tolerant to comma decimals and stray spaces
        timeString = timeString.Trim().Replace(',', '.');

        try
        {
            // sanity: still enforce 0 <= seconds < 60 like before
            var parts = timeString.Split(':');
            if (parts.Length != 2) return 0.0;

            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var secs))
                return 0.0;
            if (secs < 0.0 || secs >= 60.0) return 0.0;

            // accept m:ss, m:ss.f, m:ss.ff, m:ss.fff (and mm: variants)
            string[] formats =
            {
            @"m\:ss\.fff", @"mm\:ss\.fff",
            @"m\:ss\.ff",  @"mm\:ss\.ff",
            @"m\:ss\.f",   @"mm\:ss\.f",
            @"m\:ss",      @"mm\:ss"
        };

            foreach (var fmt in formats)
            {
                if (TimeSpan.TryParseExact(timeString, fmt, CultureInfo.InvariantCulture, TimeSpanStyles.None, out var ts))
                    return ts.TotalSeconds;
            }
        }
        catch
        {
            // ignore and fall through
        }

        return 0.0;
    }


    private void LoadLastSessionData()
    {
        AnalysisData.Clear();
        AnalysisData.Add(new AnalysisDataRow { Metric = "Total Fuel Used", Predicted = "140.5 L", Actual = "142.1 L", Delta = "+1.6 L" });
        AnalysisData.Add(new AnalysisDataRow { Metric = "Avg Fuel/Lap", Predicted = "2.81 L", Actual = "2.84 L", Delta = "+0.03 L" });
        AnalysisData.Add(new AnalysisDataRow { Metric = "Pit Stops", Predicted = "1", Actual = "1", Delta = "0" });
    }

    public class AnalysisDataRow
    {
        public string Metric { get; set; } = string.Empty;
        public string Predicted { get; set; } = string.Empty;
        public string Actual { get; set; } = string.Empty;
        public string Delta { get; set; } = string.Empty;
        public AnalysisDataRow() { }
        public AnalysisDataRow(string metric, string predicted, string actual, string delta)
        {
            Metric = metric ?? string.Empty;
            Predicted = predicted ?? string.Empty;
            Actual = actual ?? string.Empty;
            Delta = delta ?? string.Empty;
        }
    }
}
}
