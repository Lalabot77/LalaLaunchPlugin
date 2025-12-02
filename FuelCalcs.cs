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
        public enum PlanningSourceMode { Profile, LiveSnapshot }
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
    private double _firstStopTimeLoss;
    private double _refuelRate;
    private double _baseDryFuelPerLap;
    private bool _isTrackConditionManualOverride;
    private bool _isApplyingAutomaticTrackCondition;
    private string _trackConditionModeLabel = "Automatic (dry)";
    
    private double _lastLoggedLeaderDeltaSeconds = 0.0;
    private double _lastLoggedStrategyLeaderLap = 0.0;
    private double _lastLoggedStrategyEstLap = 0.0;

        // Leader delta handling
        private double _leaderDeltaSeconds;          // effective delta used by strategy & UI

        // Separate storage for live vs manual deltas
        private double _liveLeaderDeltaSeconds;      // from telemetry
        private double _manualLeaderDeltaSeconds;    // from the slider

        private bool _hasLiveLeaderDelta;
        private bool _isLeaderDeltaManual;

    private string _lapTimeSourceInfo = "Manual (user entry)";
    private bool _isLiveLapPaceAvailable;

    private bool _isEstimatedLapTimeManual;

    private DateTime _lastStrategyResetLogUtc = DateTime.MinValue;
    private DateTime _lastSnapshotResetLogUtc = DateTime.MinValue;
    private bool _isFuelPerLapManual;

    private bool _isApplyingPlanningSourceUpdates;


    private string _liveLapPaceInfo = "-";
    private double _liveAvgLapSeconds = 0;   // internal cache of live estimate
    private bool? _liveWeatherIsWet;
    private string _liveSurfaceSummary;
    private bool _isLiveSessionActive;
    private bool _isLiveSessionSnapshotExpanded;
    private string _liveCarName = "—";
    private string _liveTrackName = "—";
    private string _activeLiveCarKey;
    private string _activeLiveTrackKey;
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

    private double _profileDryFuelAvg;
    private double _profileDryFuelMin;
    private double _profileDryFuelMax;
    private int _profileDrySamples;
    private double _profileWetFuelAvg;
    private double _profileWetFuelMin;
    private double _profileWetFuelMax;
    private int _profileWetSamples;
    private double _conditionRefuelBaseSeconds = 0;
    private double _conditionRefuelSecondsPerLiter = 0;
    private double _conditionRefuelSecondsPerSquare = 0;
    private bool _isRefreshingConditionParameters = false;
    private string _lastTyreChangeDisplay = "-";

    // --- Planner state tracking ---
    private bool _isPlannerDirty = false;
    private bool _suppressPlannerDirtyUpdates = false;
                                             
    // --- NEW: Local properties for "what-if" parameters ---
    private double _contingencyValue = 1.5;
    private bool _isContingencyInLaps = true;
    private double _wetFactorPercent = 90.0;

    // --- NEW: Fields for PB Feature ---
    private double _loadedBestLapTimeSeconds;

    // --- Tracking last loaded profile state to avoid resetting unrelated fields on track changes ---
    private CarProfile _lastLoadedCarProfile;
    private string _lastLoadedTrackKey;

    // --- Public Properties for UI Binding ---
    public ObservableCollection<CarProfile> AvailableCarProfiles { get; set; } // CHANGED
    public ObservableCollection<string> AvailableTracks { get; set; } = new ObservableCollection<string>();
    public string DetectedMaxFuelDisplay { get; private set; }
    public ICommand ResetLeaderDeltaToLiveCommand { get; }
    public ICommand SetLiveMaxFuelOverrideCommand { get; }
    private string _fuelPerLapText = "";
    private bool _suppressFuelTextSync = false;
    public string LapTimeSourceInfo
    {
        get => _lapTimeSourceInfo;
        set
        {
            if (_lapTimeSourceInfo != value)
            {
                _lapTimeSourceInfo = value;
                OnPropertyChanged(nameof(LapTimeSourceInfo));
            }
        }
    }

    public bool IsPlannerDirty
    {
        get => _isPlannerDirty;
        private set
        {
            if (_isPlannerDirty != value)
            {
                _isPlannerDirty = value;
                OnPropertyChanged(nameof(IsPlannerDirty));
            }
        }
    }

    private void MarkPlannerDirty()
    {
        if (_suppressPlannerDirtyUpdates || _isApplyingPlanningSourceUpdates) return;
        IsPlannerDirty = true;
    }

    private void ResetPlannerDirty()
    {
        IsPlannerDirty = false;
    }

    public bool IsEstimatedLapTimeManual
    {
        get => _isEstimatedLapTimeManual;
        set { if (_isEstimatedLapTimeManual != value) { _isEstimatedLapTimeManual = value; OnPropertyChanged(); } }
    }
    private string _fuelPerLapSourceInfo = "Manual";
    public string FuelPerLapSourceInfo
    {
        get => _fuelPerLapSourceInfo;
        set
        {
            if (_fuelPerLapSourceInfo != value)
            {
                _fuelPerLapSourceInfo = value;
                OnPropertyChanged();
                RaiseFuelChoiceIndicators();
            }
        }
    }

    public bool IsProfileAverageFuelChoiceActive => SelectedPlanningSourceMode == PlanningSourceMode.Profile
        && FuelPerLapSourceInfo?.Contains("Profile avg") == true;

    public bool IsProfileEcoFuelChoiceActive => SelectedPlanningSourceMode == PlanningSourceMode.Profile
        && FuelPerLapSourceInfo?.Contains("Profile eco") == true;

    public bool IsProfileMaxFuelChoiceActive => SelectedPlanningSourceMode == PlanningSourceMode.Profile
        && FuelPerLapSourceInfo?.Contains("Profile max") == true;

    public bool IsLiveAverageFuelChoiceActive => SelectedPlanningSourceMode == PlanningSourceMode.LiveSnapshot
        && FuelPerLapSourceInfo?.Contains("Live avg") == true;

    public bool IsLiveSaveFuelChoiceActive => SelectedPlanningSourceMode == PlanningSourceMode.LiveSnapshot
        && FuelPerLapSourceInfo?.Contains("Live save") == true;

    public bool IsLiveMaxFuelChoiceActive => SelectedPlanningSourceMode == PlanningSourceMode.LiveSnapshot
        && FuelPerLapSourceInfo?.Contains("Max") == true;

    private void RaiseFuelChoiceIndicators()
    {
        OnPropertyChanged(nameof(IsProfileAverageFuelChoiceActive));
        OnPropertyChanged(nameof(IsProfileEcoFuelChoiceActive));
        OnPropertyChanged(nameof(IsProfileMaxFuelChoiceActive));
        OnPropertyChanged(nameof(IsLiveAverageFuelChoiceActive));
        OnPropertyChanged(nameof(IsLiveSaveFuelChoiceActive));
        OnPropertyChanged(nameof(IsLiveMaxFuelChoiceActive));
    }

    private void RaiseSourceWetFactorIndicators()
    {
        OnPropertyChanged(nameof(SourceWetFactorPercent));
        OnPropertyChanged(nameof(HasSourceWetFactor));
        OnPropertyChanged(nameof(SourceWetFactorDisplay));
        CommandManager.InvalidateRequerySuggested();
    }

    public bool IsFuelPerLapManual
    {
        get => _isFuelPerLapManual;
        set { if (_isFuelPerLapManual != value) { _isFuelPerLapManual = value; OnPropertyChanged(); } }
    }

    public bool IsLeaderDeltaManual
    {
        get => _isLeaderDeltaManual;
        set { if (_isLeaderDeltaManual != value) { _isLeaderDeltaManual = value; OnPropertyChanged(); } }
    }

    public double LiveLeaderDeltaSeconds
    {
        get => _liveLeaderDeltaSeconds;
        set
        {
            if (Math.Abs(_liveLeaderDeltaSeconds - value) < 0.0001) return;
            _liveLeaderDeltaSeconds = value;
            OnPropertyChanged();

            if (!IsLeaderDeltaManual)
            {
                UpdateEffectiveLeaderDelta();
            }
        }
    }

    private PlanningSourceMode _planningSourceMode = PlanningSourceMode.Profile;
    public PlanningSourceMode SelectedPlanningSourceMode
    {
        get => _planningSourceMode;
        set
        {
            if (_planningSourceMode == value) return;

            _planningSourceMode = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPlanningSourceProfile));
            OnPropertyChanged(nameof(IsPlanningSourceLiveSnapshot));
            OnPropertyChanged(nameof(ShowLiveLapHelper));
            OnPropertyChanged(nameof(ShowProfileLapHelper));
            RaiseFuelChoiceIndicators();
            RaiseSourceWetFactorIndicators();

            // When the user changes the global planning source,
            // drop any manual overrides so the new source fully takes over.
            IsFuelPerLapManual = false;

            // Auto-expand/collapse the Live Session telemetry panel based on planning source.
            if (value == PlanningSourceMode.LiveSnapshot)
            {
                IsLiveSessionSnapshotExpanded = true;
            }
            else if (value == PlanningSourceMode.Profile)
            {
                IsLiveSessionSnapshotExpanded = false;
            }

            ApplyPlanningSourceToAutoFields(applyLapTime: false, applyFuel: true);

            if (value == PlanningSourceMode.Profile)
            {
                UpdateProfileAverageDisplaysForCondition();
            }

        }
    }

    public bool IsPlanningSourceProfile
    {
        get => SelectedPlanningSourceMode == PlanningSourceMode.Profile;
        set { if (value) SelectedPlanningSourceMode = PlanningSourceMode.Profile; }
    }

    public bool IsPlanningSourceLiveSnapshot
    {
        get => SelectedPlanningSourceMode == PlanningSourceMode.LiveSnapshot;
        set { if (value) SelectedPlanningSourceMode = PlanningSourceMode.LiveSnapshot; }
    }

    public bool ShowLiveLapHelper => IsPlanningSourceProfile && IsLiveLapPaceAvailable;

    public bool ShowProfileLapHelper => IsPlanningSourceLiveSnapshot && !string.IsNullOrWhiteSpace(ProfileAvgLapTimeDisplay) && ProfileAvgLapTimeDisplay != "-";

    public bool IsLiveLapPaceAvailable
    {
        get => _isLiveLapPaceAvailable;
        private set
        {
            if (_isLiveLapPaceAvailable != value)
            {
                _isLiveLapPaceAvailable = value;
                OnPropertyChanged(nameof(IsLiveLapPaceAvailable));
                OnPropertyChanged(nameof(ShowLiveLapHelper));
            }
        }
    }

    public string LiveLapPaceInfo
    {
        get => _liveLapPaceInfo;
        set
        {
            if (_liveLapPaceInfo != value)
            {
                _liveLapPaceInfo = value;
                OnPropertyChanged(nameof(LiveLapPaceInfo));
            }
        }
    }

        public int LiveFuelConfidence { get; private set; }
    public int LivePaceConfidence { get; private set; }
    public int LiveOverallConfidence { get; private set; }
    public string LiveConfidenceSummary { get; private set; } = "n/a";
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
    public bool IsLiveSessionSnapshotExpanded
    {
        get => _isLiveSessionSnapshotExpanded;
        set
        {
            if (_isLiveSessionSnapshotExpanded == value) return;
            _isLiveSessionSnapshotExpanded = value;
            OnPropertyChanged();
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
        private set { _liveFuelTankSizeDisplay = value; OnPropertyChanged(); }
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

    public double LiveLeaderAvgPaceSeconds => _plugin?.LiveLeaderAvgPaceSeconds ?? 0.0;

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

    public string ProfileFuelSaveDisplay { get; private set; } = "-";
    public string ProfileFuelMaxDisplay { get; private set; } = "-";

    public string ProfileAvgDryLapTimeDisplay { get; private set; }
    public string ProfileAvgDryFuelDisplay { get; private set; }
    public string LiveFuelPerLapDisplay { get; private set; } = "-";
    public string LiveFuelSaveDisplay { get; private set; } = "-";
    public string LiveFuelMaxDisplay { get; private set; } = "-";

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
    private bool _hasProfileFuelPerLap;
    public bool HasProfileFuelPerLap
    {
        get => _hasProfileFuelPerLap;
        private set
        {
            if (_hasProfileFuelPerLap != value)
            {
                _hasProfileFuelPerLap = value;
                OnPropertyChanged();
            }
        }
    }
    public bool IsProfileFuelSaveAvailable { get; private set; }
    public bool IsProfileFuelMaxAvailable { get; private set; }
    public bool HasProfilePitLaneLoss { get; private set; }

    // Live availability (fuel per lap comes from LalaLaunch)
    public double LiveFuelPerLap { get; private set; }
    public bool IsLiveFuelPerLapAvailable => GetActiveAverageFuel().value.HasValue;
    public bool IsLiveFuelSaveAvailable { get; private set; }

    public bool HasLiveMaxFuelSuggestion => _liveMaxFuel > 0;

    private double _liveMaxFuel;
    public bool IsMaxFuelOverrideTooHigh => MaxFuelOverride > _liveMaxFuel && _liveMaxFuel > 0;
    public string MaxFuelPerLapDisplay { get; private set; } = "-";
    public bool IsMaxFuelAvailable => GetActiveLiveFuelMax().HasValue || (_plugin?.MaxFuelPerLapDisplay > 0);

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

    public RacePreset AppliedPreset => _appliedPreset;

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
    public ICommand UseLiveFuelSaveCommand { get; }
    public ICommand ResetPitLaneLossToProfileCommand { get; }
    public ICommand UseLivePitLaneLossCommand { get; }
    // ---- Commands for the lap-time row ----
    public ICommand UseLiveLapPaceCommand { get; }
    public ICommand LoadProfileLapTimeCommand { get; }
    public ICommand SavePlannerDataToProfileCommand { get; }
    public ICommand UseProfileFuelPerLapCommand { get; }
        public ICommand UseProfileFuelSaveCommand { get; }
        public ICommand UseProfileFuelMaxCommand { get; }
        public ICommand UseMaxFuelPerLapCommand { get; }
        public ICommand RefreshLiveSnapshotCommand { get; }
        public ICommand RefreshPlannerViewCommand { get; }
        public ICommand ResetEstimatedLapTimeToSourceCommand { get; }
        public ICommand ResetFuelPerLapToSourceCommand { get; }
        public ICommand ApplyPresetCommand { get; private set; }
        public ICommand ClearPresetCommand { get; private set; }
        public ICommand ApplySourceWetFactorCommand { get; }

    private void ApplyPresetValues(RacePreset preset)
    {
        if (preset == null) return;

        var p = preset;

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

    private void ApplySelectedPreset()
    {
        ApplyPresetValues(_selectedPreset);
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
        OnPropertyChanged(nameof(AppliedPreset));
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

            if (!_isApplyingPlanningSourceUpdates)
            {
                IsFuelPerLapManual = true;
                FuelPerLapSourceInfo = "Manual";
            }

            MarkPlannerDirty();

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
                ResetTrackConditionOverrideForSessionChange();
                OnPropertyChanged();

                // Rebuild lists
                AvailableTracks.Clear();        // legacy string list – safe to keep for now
                AvailableTrackStats.Clear();    // object list for ComboBox

                // Preserve the user's current track selection when possible
                // so a car swap keeps the planner focused on the same circuit.
                var preferredTrack = _selectedCarProfile?.ResolveTrackByNameOrKey(_selectedTrack);

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

                // Select the preferred track if it exists on the new profile; otherwise fall back to first
                if (preferredTrack == null)
                {
                    preferredTrack = AvailableTrackStats.FirstOrDefault();
                }

                // Temporarily suppress reloads while syncing the selected track; we'll trigger
                // one explicit LoadProfileData after the selection is settled.
                _suppressProfileDataReload = true;
                if (!ReferenceEquals(SelectedTrackStats, preferredTrack))
                {
                    SelectedTrackStats = preferredTrack;
                }
                else
                {
                    // Keep the legacy string SelectedTrack in sync even when the object didn't change
                    _suppressSelectedTrackSync = true;
                    SelectedTrack = preferredTrack?.DisplayName ?? string.Empty;
                    _suppressSelectedTrackSync = false;
                }
                _suppressProfileDataReload = false;

                LoadProfileData();
            }
        }
    }

    // Cache of the resolved TrackStats for the current SelectedCarProfile + SelectedTrack
    private TrackStats _selectedTrackStats;
    private bool _suppressSelectedTrackSync;
    private bool _suppressProfileDataReload;

    public TrackStats SelectedTrackStats
    {
        get => _selectedTrackStats;
        set
        {
            if (!ReferenceEquals(_selectedTrackStats, value))
            {
                _selectedTrackStats = value;
                ResetTrackConditionOverrideForSessionChange();
                OnPropertyChanged(nameof(SelectedTrackStats));

                // Keep the legacy string SelectedTrack in sync (avoids touching other code)
                _suppressSelectedTrackSync = true;
                SelectedTrack = value?.DisplayName ?? "";
                _suppressSelectedTrackSync = false;

                // One authoritative reload when selection changes
                if (!_suppressProfileDataReload)
                {
                    LoadProfileData();
                }
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

                if (!_isApplyingPlanningSourceUpdates)
                {
                    IsEstimatedLapTimeManual = true;
                    LapTimeSourceInfo = "Manual (user entry)";
                }
                CalculateStrategy();
                MarkPlannerDirty();
            }
        }
    }

        // Effective leader delta (seconds), exposed to UI.
        // Setter is treated as **manual** input from the slider.
        public double LeaderDeltaSeconds
        {
            get => _leaderDeltaSeconds;
            set
            {
                if (IsLeaderDeltaManual && Math.Abs(_manualLeaderDeltaSeconds - value) < 0.001)
                {
                    return;
                }

                _manualLeaderDeltaSeconds = value;
                IsLeaderDeltaManual = true;

                UpdateEffectiveLeaderDelta();
            }
        }

        /// <summary>
        /// Recomputes the effective leader delta based on live and manual sources.
        /// Manual input wins when set; otherwise live is used when available.
        /// </summary>
        private void UpdateEffectiveLeaderDelta()
        {
            double newDelta;

            if (IsLeaderDeltaManual)
            {
                newDelta = _manualLeaderDeltaSeconds;
            }
            else if (_hasLiveLeaderDelta)
            {
                newDelta = LiveLeaderDeltaSeconds;
            }
            else
            {
                newDelta = 0.0;
            }

            if (Math.Abs(_leaderDeltaSeconds - newDelta) < 0.001)
            {
                return;
            }

            _leaderDeltaSeconds = newDelta;

            // Same behaviour as before: changing the effective delta recalculates the strategy.
            CalculateStrategy();
            OnPropertyChanged(nameof(LeaderDeltaSeconds));
        }

        /// <summary>
        /// Fully clears all leader-delta state (live + manual) without calling the public setter.
        /// </summary>
        private void ClearLeaderDeltaState()
        {
            LiveLeaderDeltaSeconds = 0.0;
            _manualLeaderDeltaSeconds = 0.0;
            _hasLiveLeaderDelta = false;
            IsLeaderDeltaManual = false;
            _leaderDeltaSeconds = 0.0;
            OnPropertyChanged(nameof(LeaderDeltaSeconds));
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

                if (!_isApplyingPlanningSourceUpdates)
                {
                    IsFuelPerLapManual = true;
                    FuelPerLapSourceInfo = "Manual";
                }

                MarkPlannerDirty();

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

        // Update the PB displays
        HistoricalBestLapDisplay = formatted;
        LiveBestLapDisplay = formatted;

        OnPropertyChanged(nameof(IsPersonalBestAvailable));
        OnPropertyChanged(nameof(HistoricalBestLapDisplay));
        UpdateLapTimeSummaries();
    }

    private static string FormatFuelPerLapDisplay(double? value)
    {
        return value.HasValue ? $"{value.Value:F2} L" : "-";
    }

    private bool IsWetSurface() => _liveWeatherIsWet ?? IsWet;

    private string FormatConditionSourceLabel(string baseLabel)
    {
        return IsWetSurface() ? $"{baseLabel} (wet)" : $"{baseLabel} (dry)";
    }

    private double? GetLiveAverageFuelPerLapForCurrentCondition()
    {
        bool isWetSurface = IsWetSurface();

        if (isWetSurface && _liveWetFuelAvg > 0)
        {
            return _liveWetFuelAvg;
        }

        if (!isWetSurface && _liveDryFuelAvg > 0)
        {
            return _liveDryFuelAvg;
        }

        return LiveFuelPerLap > 0 ? LiveFuelPerLap : (double?)null;
    }
    private (double? value, string source) GetActiveAverageFuel()
    {
        if (IsWet)
        {
            if (_liveWetFuelAvg > 0) return (_liveWetFuelAvg, "Live avg (wet)");
            if (_profileWetFuelAvg > 0) return (_profileWetFuelAvg, "Profile avg (wet)");
        }
        else
        {
            if (LiveFuelPerLap > 0) return (LiveFuelPerLap, "Live avg");
            if (_liveDryFuelAvg > 0) return (_liveDryFuelAvg, "Live avg");
            if (_profileDryFuelAvg > 0) return (_profileDryFuelAvg, "Profile avg");
        }

        return (null, null);
    }

    private double? GetActiveLiveFuelMin()
    {
        double value = IsWetSurface() ? _liveWetFuelMin : _liveDryFuelMin;
        return value > 0 ? value : (double?)null;
    }

    private double? GetActiveLiveFuelMax()
    {
        double value = IsWetSurface() ? _liveWetFuelMax : _liveDryFuelMax;
        return value > 0 ? value : (double?)null;
    }

    private void UseLiveFuelSave()
    {
        var min = GetActiveLiveFuelMin();
        if (min.HasValue)
        {
            FuelPerLap = min.Value;
            FuelPerLapSourceInfo = FormatConditionSourceLabel("Live save");
        }
    }

    private void UseMaxFuelPerLap()
    {
        var liveMax = GetActiveLiveFuelMax();
        if (liveMax.HasValue)
        {
            FuelPerLap = liveMax.Value;
            FuelPerLapSourceInfo = FormatConditionSourceLabel("Live max");
            return;
        }

        if (_plugin.MaxFuelPerLapDisplay > 0)
        {
            FuelPerLap = _plugin.MaxFuelPerLapDisplay;
            FuelPerLapSourceInfo = "Max";
        }
    }

    private void ApplyLiveMaxFuelSuggestion()
    {
        if (_liveMaxFuel <= 0) return;

        MaxFuelOverride = _liveMaxFuel;
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
        UpdateLiveFuelChoiceDisplays();

        OnPropertyChanged(nameof(LiveFuelPerLap));
        OnPropertyChanged(nameof(IsLiveFuelPerLapAvailable));

        if (SelectedPlanningSourceMode == PlanningSourceMode.LiveSnapshot
            && !IsFuelPerLapManual
            && value > 0)
        {
            ApplyPlanningSourceToAutoFields(applyLapTime: false, applyFuel: true);
        }
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
        OnPropertyChanged(nameof(MaxFuelPerLapDisplay));
        OnPropertyChanged(nameof(IsMaxFuelAvailable));
        UpdateLiveFuelChoiceDisplays();
    }

    public void SetLiveSurfaceSummary(bool? isDeclaredWet, string summary)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.CheckAccess())
        {
            ApplyLiveSurfaceSummary(isDeclaredWet, summary);
        }
        else
        {
            disp.Invoke(() => ApplyLiveSurfaceSummary(isDeclaredWet, summary));
        }
    }

    public void ResetTrackConditionOverrideForSessionChange()
    {
        _isTrackConditionManualOverride = false;
        MaybeAutoApplyTrackConditionFromTelemetry(_liveWeatherIsWet);
    }

    private void ApplyLiveSurfaceSummary(bool? isDeclaredWet, string summary)
    {
        bool wasWetVisible = ShowWetSnapshotRows;
        bool wasWetCondition = IsWet;

        _liveWeatherIsWet = isDeclaredWet;
        _liveSurfaceSummary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();

        MaybeAutoApplyTrackConditionFromTelemetry(isDeclaredWet);

        bool isWetVisible = ShowWetSnapshotRows;
        if (isWetVisible != wasWetVisible)
        {
            OnPropertyChanged(nameof(ShowWetSnapshotRows));
            UpdateLapTimeSummaries();
            UpdatePaceSummaries();
        }

        if (IsWet != wasWetCondition && !_isTrackConditionManualOverride && SelectedPlanningSourceMode == PlanningSourceMode.LiveSnapshot)
        {
            ApplyPlanningSourceToAutoFields(applyLapTime: true, applyFuel: true);
        }

        UpdateSurfaceModeLabel();
        UpdateTrackConditionModeLabel();
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
        UpdateLiveFuelChoiceDisplays();
        RaiseSourceWetFactorIndicators();
    }

    private void UpdateLiveFuelChoiceDisplays()
    {
        var avg = GetActiveAverageFuel();
        var min = GetActiveLiveFuelMin();
        var max = GetActiveLiveFuelMax();

        // If we don't have a live max yet, fall back to the plugin's detected max for availability + display
        if (!max.HasValue && _plugin?.MaxFuelPerLapDisplay > 0)
        {
            max = _plugin.MaxFuelPerLapDisplay;
        }

        LiveFuelPerLapDisplay = FormatFuelPerLapDisplay(avg.value);
        LiveFuelSaveDisplay = FormatFuelPerLapDisplay(min);
        LiveFuelMaxDisplay = FormatFuelPerLapDisplay(max);

        IsLiveFuelSaveAvailable = min.HasValue;

        OnPropertyChanged(nameof(LiveFuelPerLapDisplay));
        OnPropertyChanged(nameof(LiveFuelSaveDisplay));
        OnPropertyChanged(nameof(LiveFuelMaxDisplay));
        OnPropertyChanged(nameof(IsLiveFuelSaveAvailable));
        OnPropertyChanged(nameof(IsLiveFuelPerLapAvailable));
        OnPropertyChanged(nameof(IsMaxFuelAvailable));
    }

    private double? GetProfileFuelSaveForCurrentCondition()
    {
        var ts = SelectedTrackStats;
        if (ts == null) return null;

        var dry = ts.MinFuelPerLapDry;
        var wet = ts.MinFuelPerLapWet;

        if (IsDry)
        {
            if (dry.HasValue && dry.Value > 0) return dry.Value;
        }
        else
        {
            if (wet.HasValue && wet.Value > 0) return wet.Value;
            if (dry.HasValue && dry.Value > 0) return dry.Value * (WetFactorPercent / 100.0);
        }

        return null;
    }

    private double? GetProfileFuelMaxForCurrentCondition()
    {
        var ts = SelectedTrackStats;
        if (ts == null) return null;

        var dry = ts.MaxFuelPerLapDry;
        var wet = ts.MaxFuelPerLapWet;

        if (IsDry)
        {
            if (dry.HasValue && dry.Value > 0) return dry.Value;
        }
        else
        {
            if (wet.HasValue && wet.Value > 0) return wet.Value;
            if (dry.HasValue && dry.Value > 0) return dry.Value * (WetFactorPercent / 100.0);
        }

        return null;
    }

    private void UpdateProfileFuelChoiceDisplays()
    {
        var min = GetProfileFuelSaveForCurrentCondition();
        var max = GetProfileFuelMaxForCurrentCondition();

        ProfileFuelSaveDisplay = FormatFuelPerLapDisplay(min);
        ProfileFuelMaxDisplay = FormatFuelPerLapDisplay(max);

        IsProfileFuelSaveAvailable = min.HasValue;
        IsProfileFuelMaxAvailable = max.HasValue;

        OnPropertyChanged(nameof(ProfileFuelSaveDisplay));
        OnPropertyChanged(nameof(ProfileFuelMaxDisplay));
        OnPropertyChanged(nameof(IsProfileFuelSaveAvailable));
        OnPropertyChanged(nameof(IsProfileFuelMaxAvailable));
    }

    private void UpdateProfileAverageDisplays()
    {
        var ts = SelectedTrackStats;
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

        OnPropertyChanged(nameof(ProfileAvgLapTimeDisplay));
        OnPropertyChanged(nameof(ProfileAvgFuelDisplay));
        OnPropertyChanged(nameof(ShowProfileLapHelper));
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

                if (!_isApplyingAutomaticTrackCondition)
                {
                    _isTrackConditionManualOverride = true;
                }

                OnPropertyChanged(nameof(SelectedTrackCondition));
                OnPropertyChanged(nameof(IsDry));
                OnPropertyChanged(nameof(IsWet));
                OnPropertyChanged(nameof(ShowDrySnapshotRows));
                OnPropertyChanged(nameof(ShowWetSnapshotRows));
                UpdateTrackConditionModeLabel();
                UpdateLiveFuelChoiceDisplays();
                UpdateProfileFuelChoiceDisplays();

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
                        LapTimeSourceInfo = FormatConditionSourceLabel("Profile avg");
                    }
                }
                UpdateProfileAverageDisplaysForCondition();
                RefreshProfilePlanningData();
                UpdateTrackDerivedSummaries();
                UpdateSurfaceModeLabel();
            }
                OnPropertyChanged(nameof(ProfileAvgLapTimeDisplay));
                OnPropertyChanged(nameof(ShowProfileLapHelper));
                OnPropertyChanged(nameof(ProfileAvgFuelDisplay));
                RefreshConditionParameters();
                RaiseSourceWetFactorIndicators();
        }
    }

    public string TrackConditionModeLabel
    {
        get => _trackConditionModeLabel;
        private set
        {
            if (_trackConditionModeLabel != value)
            {
                _trackConditionModeLabel = value;
                OnPropertyChanged(nameof(TrackConditionModeLabel));
            }
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
    public bool ShowWetSnapshotRows => (_liveWeatherIsWet == true) || IsWet;

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
                MarkPlannerDirty();
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
                MarkPlannerDirty();
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
                MarkPlannerDirty();
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

            var lap = GetProfileLapTimeForCondition(IsWet, out var lapSource);

            if (lap.HasValue)
            {
                EstimatedLapTime = lap.Value.ToString(@"m\:ss\.fff");
                LapTimeSourceInfo = FormatConditionSourceLabel("Profile avg");
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
                MarkPlannerDirty();
            }
        }
    }

    public double? SourceWetFactorPercent
    {
        get
        {
            if (!IsWet) return null;

            if (SelectedPlanningSourceMode == PlanningSourceMode.Profile)
            {
                var trackMultipliers = (SelectedTrackStats ?? ResolveSelectedTrackStats())?.GetConditionMultipliers(true);
                var carMultipliers = SelectedCarProfile?.GetConditionMultipliers(true);
                return trackMultipliers?.WetFactorPercent ?? carMultipliers?.WetFactorPercent ?? SelectedCarProfile?.WetFuelMultiplier;
            }

            if (SelectedPlanningSourceMode == PlanningSourceMode.LiveSnapshot)
            {
                if (_liveDryFuelAvg > 0 && _liveWetFuelAvg > 0)
                {
                    return (_liveWetFuelAvg / _liveDryFuelAvg) * 100.0;
                }
            }

            return null;
        }
    }

    public bool HasSourceWetFactor => SourceWetFactorPercent.HasValue;

    public string SourceWetFactorDisplay
    {
        get
        {
            var prefix = SelectedPlanningSourceMode == PlanningSourceMode.LiveSnapshot ? "Live" : "Profile";
            return SourceWetFactorPercent.HasValue ? $"{prefix} wet factor: {SourceWetFactorPercent.Value:0.#}%" : $"{prefix} wet factor unavailable";
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
                MarkPlannerDirty();
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
                MarkPlannerDirty();
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
        var avg = GetProfileAverageFuelPerLapForCurrentCondition();
        if (avg.HasValue)
        {
            FuelPerLap = avg.Value;
            FuelPerLapSourceInfo = FormatConditionSourceLabel("Profile avg");
        }
    }

    private void UseProfileFuelSave()
    {
        var min = GetProfileFuelSaveForCurrentCondition();
        if (min.HasValue)
        {
            FuelPerLap = min.Value;
            FuelPerLapSourceInfo = FormatConditionSourceLabel("Profile eco");
        }
    }

    private void UseProfileFuelMax()
    {
        var max = GetProfileFuelMaxForCurrentCondition();
        if (max.HasValue)
        {
            FuelPerLap = max.Value;
            FuelPerLapSourceInfo = FormatConditionSourceLabel("Profile max");
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

    private string _liveSessionHeader = "LIVE SESSION (no live data)";
    public string LiveSessionHeader
    {
        get => _liveSessionHeader;
        private set
        {
            if (_liveSessionHeader != value)
            {
                _liveSessionHeader = value;
                OnPropertyChanged(nameof(LiveSessionHeader));
            }
        }
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
        UpdateLiveFuelChoiceDisplays();
    }
    private void UseLiveFuelPerLap()
    {
        var liveFuel = GetLiveAverageFuelPerLapForCurrentCondition();
        if (liveFuel.HasValue)
        {
            FuelPerLap = liveFuel.Value;
            FuelPerLapSourceInfo = FormatConditionSourceLabel("Live avg");
        }
    }

    private void ResetStrategyInputs(bool preserveMaxFuel = false, bool preserveRaceDuration = false)
    {
        // Reset race-specific parameters to sensible defaults
        if (!preserveRaceDuration)
        {
            this.SelectedRaceType = RaceType.TimeLimited;
            this.RaceLaps = 20;
            this.RaceMinutes = 40;
        }
        this.MandatoryStopRequired = false;

        // Smartly default Max Fuel: use the live detected value if available, otherwise use 120L
        if (!preserveMaxFuel)
            this.MaxFuelOverride = _liveMaxFuel > 0 ? Math.Round(_liveMaxFuel) : 120.0;

        var nowUtc = DateTime.UtcNow;
        if ((nowUtc - _lastStrategyResetLogUtc) > TimeSpan.FromSeconds(1))
        {
            SimHub.Logging.Current.Info("[FuelCalcs] Strategy reset – defaults applied.");
            _lastStrategyResetLogUtc = nowUtc;
        }
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

        bool saveWet = IsWet || (IsPlanningSourceLiveSnapshot && _liveWeatherIsWet == true);
        bool saveDry = !saveWet;

        var profileCondition = targetProfile.GetConditionMultipliers(saveWet);
        profileCondition.FormationLapBurnLiters = this.FormationLapFuelLiters;
        if (saveWet)
        {
            profileCondition.WetFactorPercent = this.WetFactorPercent;
        }

        // 6) Save track-specific settings
        var lapTimeMs = trackRecord.LapTimeStringToMilliseconds(EstimatedLapTime);
        double.TryParse(FuelPerLapText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double fuelVal);
        bool fuelStamped = false;

        if (saveDry)
        {
            if (lapTimeMs.HasValue) trackRecord.AvgLapTimeDry = lapTimeMs;
            if (fuelVal > 0)
            {
                trackRecord.AvgFuelPerLapDry = fuelVal;
                fuelStamped = true;
            }
        }
        else // Wet
        {
            if (lapTimeMs.HasValue) trackRecord.AvgLapTimeWet = lapTimeMs;
            if (fuelVal > 0)
            {
                trackRecord.AvgFuelPerLapWet = fuelVal;
                fuelStamped = true;
            }
        }

        if (saveWet)
        {
            if (_liveWetFuelAvg > 0)
            {
                trackRecord.AvgFuelPerLapWet = _liveWetFuelAvg;
                fuelStamped = true;
            }

            if (_liveWetFuelMin > 0) trackRecord.MinFuelPerLapWet = _liveWetFuelMin;
            if (_liveWetFuelMax > 0) trackRecord.MaxFuelPerLapWet = _liveWetFuelMax;
            if (_liveWetSamples > 0) trackRecord.WetFuelSampleCount = _liveWetSamples;
        }
        else
        {
            if (_liveDryFuelAvg > 0)
            {
                trackRecord.AvgFuelPerLapDry = _liveDryFuelAvg;
                fuelStamped = true;
            }

            if (_liveDryFuelMin > 0) trackRecord.MinFuelPerLapDry = _liveDryFuelMin;
            if (_liveDryFuelMax > 0) trackRecord.MaxFuelPerLapDry = _liveDryFuelMax;
            if (_liveDrySamples > 0) trackRecord.DryFuelSampleCount = _liveDrySamples;
        }

        if (fuelStamped)
        {
            var source = isLiveSession ? "Telemetry fuel" : "Planner save";
            trackRecord.MarkFuelUpdated(source);
        }

        trackRecord.PitLaneLossSeconds = this.PitLaneTimeLoss;

        if (IsPersonalBestAvailable && _loadedBestLapTimeSeconds > 0)
            trackRecord.BestLapMs = (int)(_loadedBestLapTimeSeconds * 1000);

        var trackCondition = trackRecord.GetConditionMultipliers(saveWet);
        trackCondition.FormationLapBurnLiters = this.FormationLapFuelLiters;
        if (saveWet)
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

        ResetPlannerDirty();

    }

    public void PromptToSaveLiveFuelOnExit()
    {
        if (!HasHigherQualityLiveFuelData(out var trackStats))
        {
            return;
        }

        var carName = _plugin.CurrentCarModel;
        var trackLabel = trackStats?.DisplayName ?? _plugin.CurrentTrackName ?? _plugin.CurrentTrackKey;

        var result = MessageBox.Show(
            $"New live fuel data was collected for {carName} @ {trackLabel}. Save planner data to the profile before exiting?",
            "Save Live Fuel Data?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _selectedCarProfile = _plugin.ActiveProfile;
            _selectedTrack = _plugin.CurrentTrackKey ?? _plugin.CurrentTrackName;
            SavePlannerDataToProfile();
        }
    }

    private bool HasHigherQualityLiveFuelData(out TrackStats activeTrack)
    {
        activeTrack = null;

        if (_plugin?.ActiveProfile == null)
        {
            return false;
        }

        var activeCarName = _plugin.CurrentCarModel;
        var activeTrackKey = _plugin.CurrentTrackKey;

        if (string.IsNullOrWhiteSpace(activeCarName) || string.Equals(activeCarName, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(activeTrackKey) || string.Equals(activeTrackKey, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(_plugin.ActiveProfile.ProfileName, activeCarName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        activeTrack = _plugin.ActiveProfile.ResolveTrackByNameOrKey(activeTrackKey)
            ?? _plugin.ActiveProfile.ResolveTrackByNameOrKey(_plugin.CurrentTrackName);

        if (activeTrack == null)
        {
            return false;
        }

        bool hasBetterDry = _liveDrySamples > 0 && _liveDryFuelAvg > 0 && _liveDrySamples > (activeTrack.DryFuelSampleCount ?? 0);
        bool hasBetterWet = _liveWetSamples > 0 && _liveWetFuelAvg > 0 && _liveWetSamples > (activeTrack.WetFuelSampleCount ?? 0);

        return hasBetterDry || hasBetterWet;
    }

    public void ReloadPresetsFromDisk()
    {
        InitPresets();
    }

    private void InitPresets()
    {
        try
        {
            _availablePresets = LaunchPlugin.RacePresetStore.LoadAll() ?? new List<RacePreset>();

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

    private List<RacePreset> PresetList => _availablePresets ?? (_availablePresets = new List<RacePreset>());

    private static void CopyPresetValues(RacePreset source, RacePreset target)
    {
        if (source == null || target == null) return;

        target.Type = source.Type;
        target.RaceMinutes = source.RaceMinutes;
        target.RaceLaps = source.RaceLaps;
        target.MandatoryStopRequired = source.MandatoryStopRequired;
        target.TireChangeTimeSec = source.TireChangeTimeSec;
        target.MaxFuelLitres = source.MaxFuelLitres;
        target.ContingencyInLaps = source.ContingencyInLaps;
        target.ContingencyValue = source.ContingencyValue;
    }

    private static RacePreset ClonePreset(RacePreset source)
    {
        if (source == null) return null;

        return new RacePreset
        {
            Name = source.Name,
            Type = source.Type,
            RaceMinutes = source.RaceMinutes,
            RaceLaps = source.RaceLaps,
            MandatoryStopRequired = source.MandatoryStopRequired,
            TireChangeTimeSec = source.TireChangeTimeSec,
            MaxFuelLitres = source.MaxFuelLitres,
            ContingencyInLaps = source.ContingencyInLaps,
            ContingencyValue = source.ContingencyValue
        };
    }

    private RacePreset FindPresetByName(string name, RacePreset ignore = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        return PresetList.FirstOrDefault(x => !ReferenceEquals(x, ignore)
            && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private string GetUniquePresetName(string baseName, RacePreset ignore = null)
    {
        var seed = string.IsNullOrWhiteSpace(baseName) ? "Preset" : baseName.Trim();
        var candidate = seed;
        var i = 1;

        while (FindPresetByName(candidate, ignore) != null)
        {
            i++;
            candidate = $"{seed} {i}";
        }

        return candidate;
    }

    private RacePreset CreateOrUpdatePreset(RacePreset template, string originalName, bool allowOverwrite, bool forceUniqueName)
    {
        if (template == null || string.IsNullOrWhiteSpace(template.Name))
            throw new ArgumentException("Preset must include a name.", nameof(template));

        var requestedName = template.Name.Trim();
        var list = PresetList;

        var target = FindPresetByName(originalName);
        if (target == null)
        {
            target = FindPresetByName(requestedName);
        }

        var finalName = forceUniqueName ? GetUniquePresetName(requestedName, target) : requestedName;
        var conflict = FindPresetByName(finalName, target);

        if (conflict != null && !allowOverwrite)
        {
            throw new InvalidOperationException("A preset with that name already exists.");
        }

        if (conflict != null)
        {
            target = conflict;
        }

        if (target == null)
        {
            target = new RacePreset();
            list.Add(target);
        }

        CopyPresetValues(template, target);
        target.Name = finalName;

        return target;
    }

    private RacePreset ResolveSelection(RacePreset desired)
    {
        if (desired != null && PresetList.Contains(desired))
            return desired;

        return desired == null ? null : PresetList.FirstOrDefault();
    }

    private RacePreset ResolveAppliedPreset(RacePreset desired)
    {
        if (desired != null && PresetList.Contains(desired))
            return desired;

        return null;
    }

    private void CommitPresetChanges(RacePreset preferredSelection, bool reapplyAppliedPreset)
    {
        LaunchPlugin.RacePresetStore.SaveAll(PresetList);

        _selectedPreset = ResolveSelection(preferredSelection ?? _selectedPreset);
        _appliedPreset = ResolveAppliedPreset(_appliedPreset);

        OnPropertyChanged(nameof(AvailablePresets));
        OnPropertyChanged(nameof(SelectedPreset));
        OnPropertyChanged(nameof(HasSelectedPreset));

        if (reapplyAppliedPreset && _appliedPreset != null)
        {
            ApplyPresetValues(_appliedPreset);
        }
        else
        {
            RaisePresetStateChanged();
        }
    }

    private RacePreset BuildPresetFromCurrentState(string name)
    {
        return new RacePreset
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
    }

    private RacePreset BuildDefaultPresetTemplate()
    {
        return new RacePreset
        {
            Name = "New Preset",
            Type = RacePresetType.TimeLimited,
            RaceLaps = null,
            RaceMinutes = 40,
            MandatoryStopRequired = false,
            TireChangeTimeSec = 23,
            MaxFuelLitres = 110,
            ContingencyInLaps = true,
            ContingencyValue = 1
        };
    }

    public RacePreset CreatePresetFromDefaults()
    {
        var template = BuildDefaultPresetTemplate();
        var preset = CreateOrUpdatePreset(template, originalName: null, allowOverwrite: false, forceUniqueName: true);

        CommitPresetChanges(preferredSelection: preset, reapplyAppliedPreset: false);
        return preset;
    }

    public RacePreset SavePresetEdits(string originalName, RacePreset updated)
    {
        if (updated == null || string.IsNullOrWhiteSpace(updated.Name)) return null;

        var template = ClonePreset(updated);
        var preset = CreateOrUpdatePreset(template, originalName, allowOverwrite: false, forceUniqueName: string.IsNullOrWhiteSpace(originalName));

        var keepSelection = ReferenceEquals(_selectedPreset, preset)
            || (!string.IsNullOrWhiteSpace(originalName)
                && _selectedPreset != null
                && string.Equals(_selectedPreset.Name, originalName, StringComparison.OrdinalIgnoreCase));
        var wasApplied = ReferenceEquals(_appliedPreset, preset);

        CommitPresetChanges(preferredSelection: keepSelection ? preset : null, reapplyAppliedPreset: wasApplied);
        return preset;
    }

    public RacePreset SaveCurrentAsPreset(string name, bool overwriteIfExists)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Preset name cannot be empty.", nameof(name));

        var template = BuildPresetFromCurrentState(name);
        var preset = CreateOrUpdatePreset(template, originalName: null, allowOverwrite: overwriteIfExists, forceUniqueName: !overwriteIfExists);

        CommitPresetChanges(preferredSelection: preset, reapplyAppliedPreset: false);
        return preset;
    }

    public void DeletePreset(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var match = FindPresetByName(name);
        if (match == null) return;

        PresetList.Remove(match);
        var preferSelection = ReferenceEquals(_selectedPreset, match) ? PresetList.FirstOrDefault() : null;

        CommitPresetChanges(preferredSelection: preferSelection, reapplyAppliedPreset: false);
    }

    public void RenamePreset(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return;

        var match = FindPresetByName(oldName);
        if (match == null) return;

        if (FindPresetByName(newName, match) != null)
            throw new InvalidOperationException("A preset with that name already exists.");

        match.Name = newName.Trim();

        CommitPresetChanges(preferredSelection: match, reapplyAppliedPreset: ReferenceEquals(_appliedPreset, match));
    }

    // Unique id to make sure the UI and the engine are the same instance
    public string InstanceTag { get; } = Guid.NewGuid().ToString("N").Substring(0, 6);

    public FuelCalcs(LalaLaunch plugin)
    {
        _plugin = plugin;
        RebuildAvailableCarProfiles();

        ResetLiveSnapshotGuards();
        UpdateLiveFuelChoiceDisplays();

        UseLiveLapPaceCommand = new RelayCommand(_ => UseLiveLapPace(),_ => IsLiveLapPaceAvailable);
        UseLiveFuelPerLapCommand = new RelayCommand(_ => UseLiveFuelPerLap());
        UseLiveFuelSaveCommand = new RelayCommand(_ => UseLiveFuelSave(), _ => IsLiveFuelSaveAvailable);
        LoadProfileLapTimeCommand = new RelayCommand(_ => LoadProfileLapTime(),_ => SelectedCarProfile != null && !string.IsNullOrEmpty(SelectedTrack));
        UseProfileFuelPerLapCommand = new RelayCommand(_ => UseProfileFuelPerLap());
        UseProfileFuelSaveCommand = new RelayCommand(_ => UseProfileFuelSave(), _ => IsProfileFuelSaveAvailable);
        UseProfileFuelMaxCommand = new RelayCommand(_ => UseProfileFuelMax(), _ => IsProfileFuelMaxAvailable);
        UseMaxFuelPerLapCommand = new RelayCommand(_ => UseMaxFuelPerLap(), _ => IsMaxFuelAvailable);
        SetLiveMaxFuelOverrideCommand = new RelayCommand(_ => ApplyLiveMaxFuelSuggestion(), _ => HasLiveMaxFuelSuggestion);
        RefreshLiveSnapshotCommand = new RelayCommand(_ => RefreshLiveSnapshot());
        RefreshPlannerViewCommand = new RelayCommand(_ => RefreshPlannerView());
        ResetEstimatedLapTimeToSourceCommand = new RelayCommand(_ => ResetEstimatedLapTimeToSource());
        ResetFuelPerLapToSourceCommand = new RelayCommand(_ => ResetFuelPerLapToSource());
        ResetLeaderDeltaToLiveCommand = new RelayCommand(_ => ResetLeaderDeltaToLive());
        ApplySourceWetFactorCommand = new RelayCommand(_ => ApplySourceWetFactorFromSource(), _ => HasSourceWetFactor);

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
        UpdateTrackConditionModeLabel();
        CalculateStrategy();
    }

    private void ResetLiveSnapshotGuards()
    {
        // Refuel-condition timings are reset early so bindings never see stale values
        ConditionRefuelBaseSeconds = 0;
        ConditionRefuelSecondsPerLiter = 0;
        ConditionRefuelSecondsPerSquare = 0;
        _isRefreshingConditionParameters = false;
    }

    private void RefreshLiveSnapshot()
    {
        // Behaviour will be implemented in a later task.
        ApplyPlanningSourceToAutoFields(applyLapTime: false, applyFuel: true);
    }

    private void ResetEstimatedLapTimeToSource()
    {
        IsEstimatedLapTimeManual = false;
        ApplyPlanningSourceToAutoFields(applyLapTime: true, applyFuel: false);
    }

    private void ResetFuelPerLapToSource()
    {
        IsFuelPerLapManual = false;
        ApplyPlanningSourceToAutoFields(applyLapTime: false, applyFuel: true);
    }

    private void ResetLeaderDeltaToLive()
    {
        IsLeaderDeltaManual = false;
        _manualLeaderDeltaSeconds = LiveLeaderDeltaSeconds;
        UpdateEffectiveLeaderDelta();
    }

    private void ApplyPlanningSourceToAutoFields(bool applyLapTime = true, bool applyFuel = true)
    {
        if (_isApplyingPlanningSourceUpdates)
        {
            return;
        }

        _isApplyingPlanningSourceUpdates = true;

        try
        {
            if (applyLapTime && !IsEstimatedLapTimeManual)
            {
                TimeSpan? lap = null;

                string lapSource = null;

                if (SelectedPlanningSourceMode == PlanningSourceMode.Profile)
                {
                    lap = GetProfileLapTimeForCondition(IsWet, out lapSource);
                }
                else if (SelectedPlanningSourceMode == PlanningSourceMode.LiveSnapshot)
                {
                    lap = GetLiveAverageLapTimeSnapshot();
                    lapSource = "Live avg";
                }

                if (lap.HasValue)
                {
                    EstimatedLapTime = lap.Value.ToString("m\\:ss\\.fff");
                    IsEstimatedLapTimeManual = false;
                    LapTimeSourceInfo = SelectedPlanningSourceMode == PlanningSourceMode.Profile
                        ? FormatConditionSourceLabel("Profile avg")
                        : FormatConditionSourceLabel("Live avg");
                }
            }

            if (applyFuel && !IsFuelPerLapManual)
            {
                double? fuel = null;
                string fuelSource = null;

                if (SelectedPlanningSourceMode == PlanningSourceMode.Profile)
                {
                    if (TryGetProfileFuelForCondition(IsWet, out var profileFuel, out var profileSource))
                    {
                        fuel = profileFuel;
                        fuelSource = profileSource;
                    }
                }
                else if (SelectedPlanningSourceMode == PlanningSourceMode.LiveSnapshot)
                {
                    fuel = GetLiveAverageFuelPerLapForCurrentCondition();
                }

                if (fuel.HasValue)
                {
                    FuelPerLap = fuel.Value;
                    FuelPerLapText = fuel.Value.ToString("0.00", CultureInfo.InvariantCulture);
                    IsFuelPerLapManual = false;
                    FuelPerLapSourceInfo = SelectedPlanningSourceMode == PlanningSourceMode.Profile
                        ? FormatConditionSourceLabel("Profile avg")
                        : FormatConditionSourceLabel("Live avg");
                }
            }
        }
        finally
        {
            _isApplyingPlanningSourceUpdates = false;
        }
    }

    private TimeSpan? GetProfileAverageLapTimeForCurrentCondition()
    {
        return GetProfileLapTimeForCondition(IsWet, out _);
    }

    private TimeSpan? GetLiveAverageLapTimeSnapshot()
    {
        if (_liveAvgLapSeconds > 0 && IsLiveLapPaceAvailable)
        {
            return TimeSpan.FromSeconds(_liveAvgLapSeconds);
        }

        return null;
    }

    private double? GetProfileAverageFuelPerLapForCurrentCondition()
    {
        return TryGetProfileFuelForCondition(IsWet, out var fuel, out _) ? fuel : (double?)null;
    }

    private bool TryGetProfileFuelForCondition(bool isWet, out double fuelPerLap, out string sourceLabel)
    {
        fuelPerLap = 0.0;
        sourceLabel = null;

        var ts = SelectedTrackStats;
        if (ts == null)
        {
            return false;
        }

        var dryFuel = ts.AvgFuelPerLapDry;
        var wetFuel = ts.AvgFuelPerLapWet;

        if (!isWet && dryFuel.HasValue && dryFuel.Value > 0)
        {
            fuelPerLap = dryFuel.Value;
            sourceLabel = "Profile avg (dry)";
            return true;
        }

        if (isWet && wetFuel.HasValue && wetFuel.Value > 0)
        {
            fuelPerLap = wetFuel.Value;
            sourceLabel = "Profile avg (wet)";
            return true;
        }

        if (isWet && dryFuel.HasValue && dryFuel.Value > 0)
        {
            fuelPerLap = dryFuel.Value * (WetFactorPercent / 100.0);
            sourceLabel = "Profile dry avg × wet factor";
            return true;
        }

        return false;
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
    // Called by the Live button AND by live auto-updates
    public void UseLiveLapPace()
    {
        if (_liveAvgLapSeconds <= 0) return;

        // Treat this as a controlled source update, not a manual edit
        _isApplyingPlanningSourceUpdates = true;
        try
        {
            double estSeconds = _liveAvgLapSeconds;
            EstimatedLapTime = TimeSpan.FromSeconds(estSeconds).ToString(@"m\:ss\.fff");

            // This is explicitly “live average”, not manual
            LapTimeSourceInfo = FormatConditionSourceLabel("Live avg");
            IsEstimatedLapTimeManual = false;
        }
        finally
        {
            _isApplyingPlanningSourceUpdates = false;
        }

        // These are arguably redundant because the setter already raises
        // change notifications and calls CalculateStrategy, but we can
        // keep them for now to avoid side-effects.
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

            LiveLeaderDeltaSeconds = Math.Max(0.0, delta);
            _hasLiveLeaderDelta = LiveLeaderDeltaSeconds > 0.0;
        }
        else
        {
            // No usable live leader pace – clear live delta only,
            // but leave any manual slider value alone.
            AvgDeltaToLdrValue = "-";
            LiveLeaderDeltaSeconds = 0.0;
            _hasLiveLeaderDelta = false;
        }

        // Recompute effective delta (live if available, otherwise manual)
        UpdateEffectiveLeaderDelta();

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

        if (IsLiveSessionActive
        && IsLiveLapPaceAvailable
        && !IsEstimatedLapTimeManual
        && SelectedPlanningSourceMode == PlanningSourceMode.LiveSnapshot)
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
            return "n/a";
        }

        return $"Fuel {LiveFuelConfidence}% | Pace {LivePaceConfidence}% | Overall {LiveOverallConfidence}%";
    }

    private void MaybeAutoApplyTrackConditionFromTelemetry(bool? isDeclaredWet)
    {
        if (_isTrackConditionManualOverride || !isDeclaredWet.HasValue)
        {
            UpdateTrackConditionModeLabel();
            return;
        }

        var liveCondition = isDeclaredWet.Value ? TrackCondition.Wet : TrackCondition.Dry;
        if (SelectedTrackCondition != liveCondition)
        {
            _isApplyingAutomaticTrackCondition = true;
            SelectedTrackCondition = liveCondition;
            _isApplyingAutomaticTrackCondition = false;
        }

        UpdateTrackConditionModeLabel();
    }

    private void UpdateTrackConditionModeLabel()
    {
        string modeText;
        if (_isTrackConditionManualOverride)
        {
            modeText = "Manual override";
        }
        else
        {
            string condition = IsWet ? "wet" : "dry";
            modeText = $"Automatic ({condition})";
        }

        TrackConditionModeLabel = modeText;
    }

    private void UpdateSurfaceModeLabel()
    {
        if (!IsLiveSessionActive)
        {
            LiveSurfaceModeDisplay = "-";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_liveSurfaceSummary))
        {
            LiveSurfaceModeDisplay = _liveSurfaceSummary;
            return;
        }

        bool isWet = _liveWeatherIsWet ?? IsWet;
        LiveSurfaceModeDisplay = isWet ? "Wet" : "Dry";
    }

    private void ResetSnapshotDisplays()
    {
        IsLiveSessionActive = false;
        LiveCarName = "-";
        LiveTrackName = "-";
        _activeLiveCarKey = null;
        _activeLiveTrackKey = null;
        RefreshLiveMaxFuelDisplays(0);
        LiveBestLapDisplay = "-";
        LiveLeaderPaceInfo = "-";
        LiveLapPaceInfo = "-";
        AvgDeltaToLdrValue = "-";
        ClearLeaderDeltaState();
        _hasLiveLeaderDelta = false;
        var nowUtc = DateTime.UtcNow;
        if ((nowUtc - _lastSnapshotResetLogUtc) > TimeSpan.FromSeconds(1))
        {
            SimHub.Logging.Current.Info("[Leader] ResetSnapshotDisplays: cleared live snapshot including leader delta.");
            _lastSnapshotResetLogUtc = nowUtc;
        }
        AvgDeltaToPbValue = "-";
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
        bool wasWetVisible = ShowWetSnapshotRows;
        _liveWeatherIsWet = null;
        _liveSurfaceSummary = null;
        LiveSurfaceModeDisplay = "-";
        if (ShowWetSnapshotRows != wasWetVisible)
        {
            OnPropertyChanged(nameof(ShowWetSnapshotRows));
            UpdateLapTimeSummaries();
            UpdatePaceSummaries();
        }
        ConditionRefuelBaseSeconds = 0;
        ConditionRefuelSecondsPerLiter = 0;
        ConditionRefuelSecondsPerSquare = 0;
        SeenCarName = LiveCarName;
        SeenTrackName = LiveTrackName;
        SeenSessionSummary = "No Live Data";
        LiveSessionHeader = "LIVE SESSION (no live data)";
    }

    private void ClearLiveFuelSnapshot()
    {
        RefreshLiveMaxFuelDisplays(0);
        _liveDryFuelAvg = 0;
        _liveDryFuelMin = 0;
        _liveDryFuelMax = 0;
        _liveDrySamples = 0;
        _liveWetFuelAvg = 0;
        _liveWetFuelMin = 0;
        _liveWetFuelMax = 0;
        _liveWetSamples = 0;

        _profileDryFuelAvg = 0;
        _profileDryFuelMin = 0;
        _profileDryFuelMax = 0;
        _profileDrySamples = 0;
        _profileWetFuelAvg = 0;
        _profileWetFuelMin = 0;
        _profileWetFuelMax = 0;
        _profileWetSamples = 0;

        UpdateFuelBurnSummaries();
        UpdateLiveFuelChoiceDisplays();
        RaiseSourceWetFactorIndicators();
    }

    private void ClearLiveSnapshotForNewCombination()
    {
        ClearLiveFuelSnapshot();

        _liveAvgLapSeconds = 0;
        IsLiveLapPaceAvailable = false;
        LiveLapPaceInfo = "-";
        LivePaceDeltaInfo = string.Empty;
        AvgDeltaToPbValue = "-";
        LiveBestLapDisplay = "-";
        LiveLeaderPaceInfo = "-";
        AvgDeltaToLdrValue = "-";
        RacePaceVsLeaderSummary = "-";
        ClearLeaderDeltaState();

        ApplyLiveConfidenceLevels(0, 0, 0);

        UpdateTrackDerivedSummaries();
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

        var lap = GetConditionAverageLapTime(isWet: isVisible && ShowWetSnapshotRows, out var sourceLabel);
        if (lap.HasValue)
        {
            var formatted = lap.Value.ToString(@"m\:ss\.fff");
            parts.Add(string.IsNullOrWhiteSpace(sourceLabel)
                ? $"Avg {formatted}"
                : $"Avg {formatted} ({sourceLabel})");
        }

        return parts.Count > 0 ? string.Join(" | ", parts) : "-";
    }

    private void UpdatePaceSummaries()
    {
        DryPaceDeltaSummary = BuildLivePaceDeltaSummary(ShowDrySnapshotRows, false);
        WetPaceDeltaSummary = BuildLivePaceDeltaSummary(ShowWetSnapshotRows, true);
    }

    private void UpdateRacePaceVsLeaderSummary()
    {
        var lap = GetConditionAverageLapTime(IsWet, out var sourceLabel);
        bool hasLeaderAvg = !string.IsNullOrWhiteSpace(LiveLeaderPaceInfo) && LiveLeaderPaceInfo != "-";
        if (!lap.HasValue || !hasLeaderAvg)
        {
            RacePaceVsLeaderSummary = "-";
            return;
        }

        string lapDisplay = lap.Value.ToString(@"m\:ss\.fff");
        var delta = LiveLeaderAvgPaceSeconds > 0
            ? NormalizeDelta((lap.Value.TotalSeconds - LiveLeaderAvgPaceSeconds).ToString("+0.00;-0.00;0.00"))
            : null;

        var labelSuffix = string.IsNullOrWhiteSpace(sourceLabel) ? string.Empty : $" ({sourceLabel})";
        RacePaceVsLeaderSummary = delta == null
            ? $"Avg {lapDisplay}{labelSuffix} vs Leader {LiveLeaderPaceInfo}"
            : $"Avg {lapDisplay}{labelSuffix} vs Leader {LiveLeaderPaceInfo} (Δ {delta})";
    }

    private string BuildLivePaceDeltaSummary(bool isVisible, bool isWet)
    {
        if (!isVisible) return "-";

        var parts = new List<string>();
        var lap = GetConditionAverageLapTime(isWet, out var sourceLabel);
        double? lapSeconds = lap?.TotalSeconds;

        string pbDelta = null;
        if (lapSeconds.HasValue && _loadedBestLapTimeSeconds > 0)
        {
            pbDelta = NormalizeDelta((lapSeconds.Value - _loadedBestLapTimeSeconds).ToString("+0.00;-0.00;0.00"));
        }

        string leaderDelta = null;
        if (lapSeconds.HasValue && LiveLeaderAvgPaceSeconds > 0)
        {
            leaderDelta = NormalizeDelta((lapSeconds.Value - LiveLeaderAvgPaceSeconds).ToString("+0.00;-0.00;0.00"));
        }

        if (pbDelta != null)
        {
            parts.Add($"Δ PB: {pbDelta}");
        }
        if (leaderDelta != null)
        {
            parts.Add($"Δ Leader: {leaderDelta}");
        }

        if (lap.HasValue)
        {
            var labelSuffix = string.IsNullOrWhiteSpace(sourceLabel) ? string.Empty : $" ({sourceLabel})";
            parts.Insert(0, $"Avg {lap.Value.ToString(@"m\:ss\.fff")}{labelSuffix}");
        }

        return parts.Count > 0 ? string.Join(" | ", parts) : "-";
    }

    private static string NormalizeDelta(string value)
    {
        return string.IsNullOrWhiteSpace(value) || value == "-" ? null : value;
    }

    private TimeSpan? GetConditionAverageLapTime(bool isWet, out string sourceLabel)
    {
        sourceLabel = null;

        if (IsLiveLapPaceAvailable && _liveAvgLapSeconds > 0)
        {
            sourceLabel = "Live avg";
            return TimeSpan.FromSeconds(_liveAvgLapSeconds);
        }

        return GetProfileLapTimeForCondition(isWet, out sourceLabel);
    }

    private TimeSpan? GetProfileLapTimeForCondition(bool isWet, out string sourceLabel)
    {
        sourceLabel = null;
        var ts = SelectedTrackStats ?? ResolveSelectedTrackStats();
        if (ts == null)
        {
            return null;
        }

        int? lapMs = isWet ? ts.AvgLapTimeWet : ts.AvgLapTimeDry;
        if (lapMs.HasValue && lapMs.Value > 0)
        {
            sourceLabel = isWet ? "Profile avg (wet)" : "Profile avg (dry)";
            return TimeSpan.FromMilliseconds(lapMs.Value);
        }

        if (isWet && ts.AvgLapTimeDry.HasValue && ts.AvgLapTimeDry.Value > 0)
        {
            sourceLabel = "Profile avg (dry × wet factor)";
            double factor = WetFactorPercent / 100.0;
            double scaledMs = ts.AvgLapTimeDry.Value * factor;
            return TimeSpan.FromMilliseconds(scaledMs);
        }

        return null;
    }

    private string FormatLabel(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private void UpdateFuelBurnSummaries()
    {
        var dry = GetFuelSummaryInputs(isWet: false);
        DryFuelBurnSummary = BuildFuelSummary(dry.avg, dry.min, dry.max, dry.samples, dry.sourceLabel);

        var wet = GetFuelSummaryInputs(isWet: true);
        WetFuelBurnSummary = BuildFuelSummary(wet.avg, wet.min, wet.max, wet.samples, wet.sourceLabel);
    }

    private (double avg, double min, double max, int samples, string sourceLabel) GetFuelSummaryInputs(bool isWet)
    {
        double liveAvg = isWet ? _liveWetFuelAvg : _liveDryFuelAvg;
        double liveMin = isWet ? _liveWetFuelMin : _liveDryFuelMin;
        double liveMax = isWet ? _liveWetFuelMax : _liveDryFuelMax;
        int liveSamples = isWet ? _liveWetSamples : _liveDrySamples;

        double profileAvg = isWet ? _profileWetFuelAvg : _profileDryFuelAvg;
        double profileMin = isWet ? _profileWetFuelMin : _profileDryFuelMin;
        double profileMax = isWet ? _profileWetFuelMax : _profileDryFuelMax;
        int profileSamples = isWet ? _profileWetSamples : _profileDrySamples;

        if (liveAvg > 0 || liveMin > 0 || liveMax > 0 || liveSamples > 0)
        {
            return (liveAvg, liveMin, liveMax, liveSamples, "Live");
        }

        if (profileAvg > 0 || profileMin > 0 || profileMax > 0 || profileSamples > 0)
        {
            var sourceLabel = isWet ? "Profile (wet)" : "Profile (dry)";
            return (profileAvg, profileMin, profileMax, profileSamples, sourceLabel);
        }

        if (isWet && _profileDryFuelAvg > 0)
        {
            double factor = WetFactorPercent / 100.0;
            double scaledMin = _profileDryFuelMin > 0 ? _profileDryFuelMin * factor : 0.0;
            double scaledMax = _profileDryFuelMax > 0 ? _profileDryFuelMax * factor : 0.0;
            return (_profileDryFuelAvg * factor, scaledMin, scaledMax, _profileDrySamples, "Profile (dry × wet factor)");
        }

        return (0, 0, 0, 0, null);
    }

    private static string BuildFuelSummary(double avg, double min, double max, int samples, string sourceLabel)
    {
        var parts = new List<string>();
        if (avg > 0) parts.Add($"Avg {avg:F2} L");
        if (min > 0 && max > 0) parts.Add($"Range {min:F2}–{max:F2} L");
        else if (max > 0) parts.Add($"Max {max:F2} L");
        else if (min > 0) parts.Add($"Min {min:F2} L");
        if (samples > 0) parts.Add(samples == 1 ? "1 lap" : $"{samples} laps");
        if (parts.Count == 0) return "-";

        var summary = string.Join(" | ", parts);
        return string.IsNullOrWhiteSpace(sourceLabel) ? summary : $"{sourceLabel} · {summary}";
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

        string liveTrackKey = (!string.IsNullOrWhiteSpace(_plugin.CurrentTrackKey)
                               && !_plugin.CurrentTrackKey.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            ? _plugin.CurrentTrackKey
            : trackName;

        string normalizedCar = hasCar ? carName?.Trim() : null;
        string normalizedTrack = hasTrack ? liveTrackKey?.Trim() : null;

        bool comboChanged = IsLiveSessionActive
            && (!string.Equals(normalizedCar, _activeLiveCarKey, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(normalizedTrack, _activeLiveTrackKey, StringComparison.OrdinalIgnoreCase));

        bool startingNewLiveSession = hasCar && hasTrack && (!IsLiveSessionActive || comboChanged);

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
        LiveSessionHeader = (IsLiveSessionActive && (hasCar || hasTrack))
            ? $"LIVE SESSION: {FormatLabel(displayCarName, "-")} @ {FormatLabel(displayTrackName, "-")}"
            : "LIVE SESSION TELEMETRY (no live data)";

        if (startingNewLiveSession)
        {
            ClearLiveSnapshotForNewCombination();
        }

        _activeLiveCarKey = IsLiveSessionActive ? normalizedCar : null;
        _activeLiveTrackKey = IsLiveSessionActive ? normalizedTrack : null;

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
        ProfileFuelSaveDisplay = "-";
        ProfileFuelMaxDisplay = "-";
        IsProfileFuelSaveAvailable = false;
        IsProfileFuelMaxAvailable = false;
    }

    public void ForceProfileDataReload()
    {
        LoadProfileData();
    }

    public void RefreshPlannerView()
    {
        _suppressPlannerDirtyUpdates = true;
        try
        {
            // Keep the car selection aligned with the active profile if available
            var activeProfile = _plugin?.ActiveProfile;
            if (activeProfile != null && !ReferenceEquals(SelectedCarProfile, activeProfile))
            {
                SelectedCarProfile = activeProfile;
            }

            // Re-resolve the track against the active profile and live telemetry identifiers
            TrackStats resolvedTrack = SelectedTrackStats;
            if (SelectedCarProfile != null)
            {
                var liveTrackKey = _plugin?.CurrentTrackKey;
                var liveTrackName = _plugin?.CurrentTrackName;

                resolvedTrack = SelectedCarProfile.ResolveTrackByNameOrKey(
                                    resolvedTrack?.Key
                                    ?? (!string.IsNullOrWhiteSpace(liveTrackKey) ? liveTrackKey : liveTrackName)
                                    ?? SelectedTrack)
                               ?? resolvedTrack;
            }

            if (!ReferenceEquals(resolvedTrack, SelectedTrackStats) && resolvedTrack != null)
            {
                _suppressProfileDataReload = true;
                SelectedTrackStats = resolvedTrack;
                _suppressProfileDataReload = false;
            }

            LoadProfileData();
            RefreshProfilePlanningData();
            RefreshConditionParameters();
            UpdateTrackDerivedSummaries();
            UpdateFuelBurnSummaries();
            UpdateLiveFuelChoiceDisplays();
            CalculateStrategy();
        }
        finally
        {
            _suppressPlannerDirtyUpdates = false;
            ResetPlannerDirty();
        }
    }
    public void LoadProfileData()
    {
        _suppressPlannerDirtyUpdates = true;
        try
        {
            if (SelectedCarProfile == null || string.IsNullOrEmpty(SelectedTrack))
            {
                _lastLoadedCarProfile = null;
                _lastLoadedTrackKey = null;
                SetUIDefaults();
                CalculateStrategy();
                return;
            }

            var car = SelectedCarProfile;

            // Keep an internal object reference in sync with the dropdown string
            SelectedTrackStats = ResolveSelectedTrackStats();
            var ts = SelectedTrackStats;

            var trackKey = ts?.Key ?? SelectedTrack;
            bool carChanged = !ReferenceEquals(car, _lastLoadedCarProfile);

            // Always clear stale track-scoped state before applying new data so
            // a car/track swap cannot leak lap times or fuel numbers from the
            // previous selection (e.g., switching from McLaren 720S to Ferrari 296).
            ResetTrackScopedProfileData();

            if (ts == null)
            {
                UpdateProfileAverageDisplaysForCondition(null);
                UpdateTrackDerivedSummaries();
                CalculateStrategy();
                _lastLoadedCarProfile = car;
                _lastLoadedTrackKey = trackKey;
                return;
            }

            // --- Load Refuel Rate and car-level settings only when the car changes ---
            if (carChanged || _lastLoadedCarProfile == null)
            {
                this._refuelRate = car.RefuelRate;
                this.ContingencyValue = car.FuelContingencyValue;
                this.IsContingencyInLaps = car.IsContingencyInLaps;
                this.WetFactorPercent = car.WetFuelMultiplier;
            }

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
            OnPropertyChanged(nameof(IsPersonalBestAvailable));
            OnPropertyChanged(nameof(HistoricalBestLapDisplay));


            // --- Set the initial estimated lap time from the profile's condition average ---
            var initialLap = GetProfileAverageLapTimeForCurrentCondition();
            if (initialLap.HasValue)
            {
                EstimatedLapTime = initialLap.Value.ToString(@"m\:ss\.fff");
                LapTimeSourceInfo = FormatConditionSourceLabel("Profile avg");
            }
            else
            {
                // If there's no data at all, use the UI default
                EstimatedLapTime = "2:45.500";
                LapTimeSourceInfo = "Manual (user entry)";
            }

            // --- Load historical/track-specific data ---
            if (ts?.AvgFuelPerLapDry is double avg && avg > 0)
            {
                _baseDryFuelPerLap = avg;

                var initialFuel = GetProfileAverageFuelPerLapForCurrentCondition();
                if (initialFuel.HasValue)
                {
                    FuelPerLap = initialFuel.Value;
                    FuelPerLapSourceInfo = FormatConditionSourceLabel("Profile avg");
                    double factor = WetFactorPercent / 100.0;
                    if (IsWet && ts.AvgFuelPerLapWet.HasValue && ts.AvgFuelPerLapWet.Value > 0)
                    {
                        FuelPerLap = ts.AvgFuelPerLapWet.Value;
                        FuelPerLapSourceInfo = "Profile avg (wet)";
                    }
                    else if (IsWet)
                    {
                        FuelPerLap = avg * factor;
                        FuelPerLapSourceInfo = "Profile dry avg × wet factor";
                    }
                    else
                    {
                        FuelPerLap = avg;
                        FuelPerLapSourceInfo = "Profile avg (dry)";
                    }
                }
                else
                {
                    // Handle case where track exists but has no fuel data.
                    // Reset to the global default value and update the source text.
                    var defaultProfile = _plugin.ProfilesViewModel.GetProfileForCar("Default Settings");
                    var defaultFuel = defaultProfile?.TrackStats?["default"]?.AvgFuelPerLapDry ?? 2.8;
                    FuelPerLap = defaultFuel;
                    FuelPerLapSourceInfo = "Default";
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

                UpdateProfileAverageDisplays();

                ProfileAvgDryLapTimeDisplay = (dryLap.HasValue && dryLap.Value > 0)
                    ? TimeSpan.FromMilliseconds(dryLap.Value).ToString(@"m\:ss\.fff")
                    : "-";

                ProfileAvgDryFuelDisplay = (dryFuel.HasValue && dryFuel.Value > 0)
                    ? dryFuel.Value.ToString("F2") + " L"
                    : "-";

                HasProfileFuelPerLap = ts?.AvgFuelPerLapDry > 0 || ts?.AvgFuelPerLapWet > 0;

                _profileDryFuelAvg = ts?.AvgFuelPerLapDry ?? 0;
                _profileDryFuelMin = ts?.MinFuelPerLapDry ?? 0;
                _profileDryFuelMax = ts?.MaxFuelPerLapDry ?? 0;
                _profileDrySamples = ts?.DryFuelSampleCount ?? 0;
                _profileWetFuelAvg = ts?.AvgFuelPerLapWet ?? 0;
                _profileWetFuelMin = ts?.MinFuelPerLapWet ?? 0;
                _profileWetFuelMax = ts?.MaxFuelPerLapWet ?? 0;
                _profileWetSamples = ts?.WetFuelSampleCount ?? 0;

                UpdateProfileFuelChoiceDisplays();
                UpdateFuelBurnSummaries();

                RefreshConditionParameters();
                // Only reset race-strategy defaults when the car changes; track changes should not touch these values.
                if (carChanged)
                {
                    // When switching cars, reinitialize the max-fuel override from the new car's tank size
                    // (or default) but keep the user's race duration/type selections intact.
                    ResetStrategyInputs(preserveMaxFuel: false, preserveRaceDuration: true);
                }

                // Recompute with the newly loaded data
                CalculateStrategy();

                UpdateTrackDerivedSummaries();

                _lastLoadedCarProfile = car;
                _lastLoadedTrackKey = trackKey;
            }
        }
        finally
        {
            _suppressPlannerDirtyUpdates = false;
            ResetPlannerDirty();
        }
    }

    // Clears lap/fuel caches and display helpers that are scoped to the
    // current track selection. This avoids showing stale values when the
    // next track lacks saved data.
    private void ResetTrackScopedProfileData()
    {
        _loadedBestLapTimeSeconds = 0;
        IsPersonalBestAvailable = false;
        HistoricalBestLapDisplay = "-";
        OnPropertyChanged(nameof(IsPersonalBestAvailable));
        OnPropertyChanged(nameof(HistoricalBestLapDisplay));

        ProfileAvgLapTimeDisplay = "-";
        ProfileAvgFuelDisplay = "-";
        ProfileAvgDryLapTimeDisplay = "-";
        ProfileAvgDryFuelDisplay = "-";
        ProfileFuelSaveDisplay = "-";
        ProfileFuelMaxDisplay = "-";
        HasProfileFuelPerLap = false;
        HasProfilePitLaneLoss = false;

        _profileDryFuelAvg = 0;
        _profileDryFuelMin = 0;
        _profileDryFuelMax = 0;
        _profileWetFuelAvg = 0;
        _profileWetFuelMin = 0;
        _profileWetFuelMax = 0;
        _profileDrySamples = 0;
        _profileWetSamples = 0;
        _baseDryFuelPerLap = 0;

        UpdateProfileFuelChoiceDisplays();
        UpdateFuelBurnSummaries();
    }

    private void ApplySourceWetFactorFromSource()
    {
        var source = SourceWetFactorPercent;
        if (source.HasValue)
        {
            WetFactorPercent = source.Value;
        }
    }

    private void ApplyWetFactor()
    {
        if (IsWet) { FuelPerLap = _baseDryFuelPerLap * (WetFactorPercent / 100.0); }
        UpdateProfileFuelChoiceDisplays();
    }

    private void UpdateProfileAverageDisplaysForCondition(TrackStats ts = null)
    {
        if (ts == null)
        {
            ts = SelectedTrackStats ?? ResolveSelectedTrackStats();
        }

        var dryLap = ts?.AvgLapTimeDry;
        var wetLap = ts?.AvgLapTimeWet;
        var dryFuel = ts?.AvgFuelPerLapDry;
        var wetFuel = ts?.AvgFuelPerLapWet;

        ProfileAvgLapTimeDisplay = IsDry
            ? (dryLap.HasValue && dryLap.Value > 0
                ? TimeSpan.FromMilliseconds(dryLap.Value).ToString(@"m\:ss\.fff")
                : "-")
            : (wetLap.HasValue && wetLap.Value > 0
                ? TimeSpan.FromMilliseconds(wetLap.Value).ToString(@"m\:ss\.fff")
                : "-");

        ProfileAvgFuelDisplay = IsDry
            ? (dryFuel.HasValue && dryFuel.Value > 0 ? $"{dryFuel.Value:F2} L" : "-")
            : (wetFuel.HasValue && wetFuel.Value > 0 ? $"{wetFuel.Value:F2} L" : "-");

        ProfileAvgDryLapTimeDisplay = (dryLap.HasValue && dryLap.Value > 0)
            ? TimeSpan.FromMilliseconds(dryLap.Value).ToString(@"m\:ss\.fff")
            : "-";

        ProfileAvgDryFuelDisplay = (dryFuel.HasValue && dryFuel.Value > 0)
            ? dryFuel.Value.ToString("F2") + " L"
            : "-";

        HasProfileFuelPerLap = (dryFuel.HasValue && dryFuel.Value > 0) || (wetFuel.HasValue && wetFuel.Value > 0);

        OnPropertyChanged(nameof(ProfileAvgLapTimeDisplay));
        OnPropertyChanged(nameof(ProfileAvgFuelDisplay));
        OnPropertyChanged(nameof(ProfileAvgDryLapTimeDisplay));
        OnPropertyChanged(nameof(ProfileAvgDryFuelDisplay));
        OnPropertyChanged(nameof(ShowProfileLapHelper));
    }

    private void RefreshProfilePlanningData()
    {
        if (SelectedPlanningSourceMode == PlanningSourceMode.Profile)
        {
            ApplyPlanningSourceToAutoFields(applyLapTime: true, applyFuel: true);
            return;
        }

        // Maintain live/manual values unless the user left the auto fields untouched.
        if (!IsFuelPerLapManual)
        {
            if (IsWet)
            {
                ApplyWetFactor();
            }
            else
            {
                FuelPerLap = _baseDryFuelPerLap;
                FuelPerLapText = _baseDryFuelPerLap.ToString("0.00", CultureInfo.InvariantCulture);
            }
        }

        if (!IsEstimatedLapTimeManual)
        {
            var ts = SelectedTrackStats ?? ResolveSelectedTrackStats();
            int? lapTimeMs = IsWet ? ts?.AvgLapTimeWet : ts?.AvgLapTimeDry;
            if (lapTimeMs.HasValue && lapTimeMs > 0)
            {
                EstimatedLapTime = TimeSpan.FromMilliseconds(lapTimeMs.Value).ToString(@"m\:ss\.fff");
                LapTimeSourceInfo = $"Profile avg ({(IsWet ? "wet" : "dry")})";
            }
        }
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

        RaiseSourceWetFactorIndicators();
    }

    private void RefreshLiveMaxFuelDisplays(double liveMaxFuel)
    {
        _liveMaxFuel = liveMaxFuel;
        _liveFuelTankLiters = liveMaxFuel;

        LiveFuelTankSizeDisplay = liveMaxFuel > 0 ? $"{liveMaxFuel:F1} L" : "-";
        DetectedMaxFuelDisplay = liveMaxFuel > 0 ? $"(Detected Max: {liveMaxFuel:F1} L)" : "(Detected Max: N/A)";

        OnPropertyChanged(nameof(DetectedMaxFuelDisplay));
        OnPropertyChanged(nameof(HasLiveMaxFuelSuggestion));
        OnPropertyChanged(nameof(IsMaxFuelOverrideTooHigh));
        CommandManager.InvalidateRequerySuggested();
    }

    public void UpdateLiveDisplay(double liveMaxFuel)
    {
        RefreshLiveMaxFuelDisplays(liveMaxFuel);
    }

    private static double ComputeExtraSecondsAfterTimerZero(double leaderLapSec, double yourLapSec, double raceSeconds)
    {
        // Continuous, decimal-lap model: compute when the leader finishes (time + one lap),
        // then find the first time you cross the line after that moment without flooring laps.
        if (leaderLapSec <= 0.0 || yourLapSec <= 0.0 || raceSeconds <= 0.0) return 0.0;

        // How many leader laps elapse by the time the race clock hits zero (fractional allowed)
        double leaderLapsAtZero = raceSeconds / leaderLapSec;

        // Leader's actual finish timestamp = smallest whole-lap crossing after the timer expires
        double leaderFinishClock = Math.Ceiling(leaderLapsAtZero - 1e-9) * leaderLapSec;

        // Using the leader's finish timestamp, compute your fractional lap count at that instant
        double yourLapsWhenLeaderFinishes = leaderFinishClock / yourLapSec;

        // You take the checkered on your first crossing AFTER the leader finishes
        double yourFinishClock = Math.Ceiling(yourLapsWhenLeaderFinishes - 1e-9) * yourLapSec;

        // Extra seconds you keep driving after the timer hits zero
        double extraSecondsAfterZero = yourFinishClock - raceSeconds;

        return Math.Max(0.0, extraSecondsAfterZero);
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
            bool leaderPaceAvailable = IsLeaderDeltaManual || _hasLiveLeaderDelta;
            double appliedDelta = IsLeaderDeltaManual ? LeaderDeltaSeconds : LiveLeaderDeltaSeconds;
            double num2 = leaderPaceAvailable
                ? num3 - appliedDelta                       // leader pace (your pace - delta)
                : num3;                                           // fall back to your pace when no leader data

            if (LeaderDeltaSeconds > 0.0 && num3 > 0.0)
            {
                double leaderLap = num2;
                bool shouldLog = Math.Abs(leaderLap - _lastLoggedStrategyLeaderLap) > 0.01 ||
                                 Math.Abs(num3 - _lastLoggedStrategyEstLap) > 0.01 ||
                                 Math.Abs(LeaderDeltaSeconds - _lastLoggedLeaderDeltaSeconds) > 0.01;
                if (shouldLog)
                {
                    SimHub.Logging.Current.Info(string.Format(
                        "[FuelLeader] CalculateStrategy: estLap={0:F3}, leaderDelta={1:F3}, leaderLap={2:F3}",
                        num3,
                        LeaderDeltaSeconds,
                        leaderLap));

                    _lastLoggedStrategyLeaderLap = leaderLap;
                    _lastLoggedStrategyEstLap = num3;
                    _lastLoggedLeaderDeltaSeconds = LeaderDeltaSeconds;
                }
            }

            double num4 = ParseLapTime(TimeLossPerLapOfFuelSave);  // fuel-save lap time loss
            if (double.IsNaN(num4) || double.IsInfinity(num4) || num4 < 0.0)
            {
                num4 = 0.0;
            }

            // --- Validation guards ---------------------------------------------------
            
            bool lapInvalid = double.IsNaN(num3) || double.IsInfinity(num3) ||
                              num3 <= 0.0 || num3 < 20.0 || num3 > 900.0;

            bool leaderInvalid = leaderPaceAvailable && (double.IsNaN(num2) || double.IsInfinity(num2) ||
                                 num2 <= 0.0 || num2 < 20.0 || num2 > 900.0);

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
        double cumPlayerTime = 0.0;        // your race clock (s)
        double cumLeaderDriveTime = 0.0;   // leader's DRIVING time (s) – excludes pit time
        int playerLapsSoFar = 0;           // completed player laps
        int nextCatchAhead = 1;            // we’ll report +1 lap first, then +2, etc.
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
                cumLeaderDriveTime += playerPaceSeconds;
                playerLapsSoFar++;

                int leaderLaps = (int)Math.Floor(cumLeaderDriveTime / leaderPaceSeconds);

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
            cumLeaderDriveTime += lapsInFirstStint * playerPaceSeconds;
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

            // Advance your race clock by the pit time. The leader loses the same wall time,
            // but we do **not** convert that into laps for the leader’s driving time.
            cumPlayerTime += totalStopTime;

            // Now walk the stint lap-by-lap and emit every catch that occurs
            if (anyLappingPossible)
            {
                for (int lap = 0; lap < (int)lapsInNextStint; lap++)
                {
                    cumPlayerTime += playerPaceSeconds;
                    cumLeaderDriveTime += playerPaceSeconds;

                    playerLapsSoFar++;

                    int leaderLaps = (int)Math.Floor(cumLeaderDriveTime / leaderPaceSeconds);

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
                cumLeaderDriveTime += lapsInNextStint * playerPaceSeconds;
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


}
}
