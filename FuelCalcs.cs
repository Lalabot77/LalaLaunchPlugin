using LaunchPlugin;
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
    private double _firstStopTimeLoss;
    private double _refuelRate;
    private double _baseDryFuelPerLap;
    private double _leaderDeltaSeconds;
    private string _lapTimeSourceInfo = "source: manual";
    private bool _isLiveLapPaceAvailable;
    private string _liveLapPaceInfo = "-";
    private double _liveAvgLapSeconds = 0;   // internal cache of live estimate
                                             
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

    public string ProfileAvgLapTimeDisplay { get; private set; }
    public string ProfileAvgFuelDisplay { get; private set; }

    public string ProfileAvgDryLapTimeDisplay { get; private set; }
    public string ProfileAvgDryFuelDisplay { get; private set; }
    public string LiveFuelPerLapDisplay { get; private set; } = "-";

    // --- NEW: Properties for PB Feature ---
    
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

    private double _liveMaxFuel;
    public bool IsMaxFuelOverrideTooHigh => MaxFuelOverride > _liveMaxFuel && _liveMaxFuel > 0;
    public string MaxFuelPerLapDisplay { get; private set; } = "-";
    public bool IsMaxFuelAvailable => _plugin?.MaxFuelPerLapDisplay > 0;

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

                AvailableTracks.Clear();
                if (_selectedCarProfile?.TrackStats != null)
                {
                    foreach (var t in _selectedCarProfile.TrackStats.Values.OrderBy(t => t.DisplayName))
                        AvailableTracks.Add(t.DisplayName);
                }
                OnPropertyChanged(nameof(AvailableTracks));

                // When car changes, if a track is already selected, reload data
                if (!string.IsNullOrEmpty(SelectedTrack))
                {
                    LoadProfileData();
                }
                SelectedTrack = AvailableTracks.FirstOrDefault();
            }
        }
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
                // FIX: Must reload data when track changes to update PB hint
                LoadProfileData();
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

        // Update the label shown under the PB button
        HistoricalBestLapDisplay = TimeSpan
            .FromSeconds(_loadedBestLapTimeSeconds)
            .ToString(@"m\:ss\.fff");

        // If the user has ALREADY selected PB as the source, refresh the estimate and source label.
        if (LapTimeSourceInfo == "source: PB")
        {
            double estSeconds = _loadedBestLapTimeSeconds + RacePaceDeltaOverride;
            EstimatedLapTime = TimeSpan.FromSeconds(estSeconds).ToString(@"m\:ss\.fff");
            OnPropertyChanged(nameof(EstimatedLapTime));
        }

        OnPropertyChanged(nameof(IsPersonalBestAvailable));
        OnPropertyChanged(nameof(HistoricalBestLapDisplay));
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
        OnPropertyChanged(nameof(MaxFuelPerLapDisplay));
        OnPropertyChanged(nameof(IsMaxFuelAvailable));
    }

    // --- REPLACE the old property with this new version ---
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

                // Apply fuel factor
                if (IsWet) { ApplyWetFactor(); }
                else { FuelPerLap = _baseDryFuelPerLap; }

                // --- NEW LOGIC: Update Estimated Lap Time based on condition ---
                var ts = _selectedCarProfile?.FindTrack(_selectedTrack);
                if (ts != null)
                {
                    int? lapTimeMs = IsWet ? ts.AvgLapTimeWet : ts.AvgLapTimeDry;
                    if (lapTimeMs.HasValue && lapTimeMs > 0)
                    {
                        EstimatedLapTime = TimeSpan.FromMilliseconds(lapTimeMs.Value).ToString(@"m\:ss\.fff");
                        LapTimeSourceInfo = $"source: {(IsWet ? "wet avg" : "dry avg")}";
                    }
                }
            }
            OnPropertyChanged(nameof(ProfileAvgLapTimeDisplay));
            OnPropertyChanged(nameof(ProfileAvgFuelDisplay));
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

    // --- REWIRED "What-If" Properties ---
    public void LoadProfileLapTime()
    {
        var ts = _selectedCarProfile?.FindTrack(_selectedTrack);
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
            }
        }
    }

    public bool IsContingencyLitres
    {
        get => !_isContingencyInLaps;
        set { IsContingencyInLaps = !value; }
    }

    private void RebuildAvailableCarProfiles()
    {
        // This now provides the full objects to the ComboBox
        AvailableCarProfiles = _plugin.ProfilesViewModel.CarProfiles;
        OnPropertyChanged(nameof(AvailableCarProfiles));
    }

    private void UseProfileFuelPerLap()
    {
        var ts = _selectedCarProfile?.FindTrack(_selectedTrack);
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
        if (_plugin.LiveFuelPerLap > 0)
        {
            FuelPerLap = _plugin.LiveFuelPerLap;
            FuelPerLapSourceInfo = "source: live average";
        }
    }

    private void ResetStrategyInputs()
    {
        // Reset race-specific parameters to sensible defaults
        this.SelectedRaceType = RaceType.LapLimited;
        this.RaceLaps = 20;
        this.RaceMinutes = 40;

        // Smartly default Max Fuel: use the live detected value if available, otherwise use 120L
        this.MaxFuelOverride = _liveMaxFuel > 0 ? Math.Round(_liveMaxFuel) : 120.0;

        SimHub.Logging.Current.Debug("FuelCalcs: Race strategy inputs have been reset to defaults.");
    }

    private void SavePlannerDataToProfile()
    {
        // Get live and UI-selected car/track names for logic and auditing
        string liveCarName = _plugin.CurrentCarModel;
        string uiCarName = _selectedCarProfile?.ProfileName;

        // --- NEW LOGIC: Determine the correct profile to save to ---
        CarProfile targetProfile = null;
        bool isLiveSession = !string.IsNullOrEmpty(liveCarName) && liveCarName != "Unknown";

        if (isLiveSession)
        {
            // In a live session, ALWAYS save to the live car's profile.
            // EnsureCar will find the existing profile or create a new one based on the live car name.
            targetProfile = _plugin.ProfilesViewModel.EnsureCar(liveCarName);
        }
        else
        {
            // If not in a live session, it's safe to save to the profile selected in the UI.
            targetProfile = _selectedCarProfile;
        }

        // --- NEW: Audit log line as requested ---
        SimHub.Logging.Current.Info($"[FuelCalcs.Save] Saving data. Target: '{targetProfile?.ProfileName}' (Live Car: '{liveCarName}', UI Car: '{uiCarName}')");

        // Guard clause: If no target profile could be determined, or no track is selected, exit.
        if (targetProfile == null || string.IsNullOrEmpty(_selectedTrack))
        {
            MessageBox.Show("Please select a car and track profile first.", "No Profile Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string trackKeyToSave;

        // Use the live track key only when actually in a live session and the key is valid
        if (isLiveSession && !string.IsNullOrWhiteSpace(_plugin.CurrentTrackKey) && _plugin.CurrentTrackKey != "Unknown")
        {
            trackKeyToSave = _plugin.CurrentTrackKey;
        }
        else
        {
            // Non-live save: find the existing track in the selected profile by its display name
            var tsExisting = targetProfile.FindTrack(_selectedTrack);

            if (tsExisting == null || string.IsNullOrWhiteSpace(tsExisting.Key))
            {
                MessageBox.Show(
                    "This track doesn’t exist in the selected profile. Create it on the Profiles tab or start a live session first.",
                    "Missing track key",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            trackKeyToSave = tsExisting.Key;
        }

        var trackRecord = targetProfile.EnsureTrack(trackKeyToSave, _selectedTrack);


        // --- Save Car-Level Settings ---
        targetProfile.FuelContingencyValue = this.ContingencyValue;
        targetProfile.IsContingencyInLaps = this.IsContingencyInLaps;
        targetProfile.WetFuelMultiplier = this.WetFactorPercent;
        targetProfile.TireChangeTime = this.TireChangeTime;
        targetProfile.RacePaceDeltaSeconds = this.RacePaceDeltaOverride;

        // --- Save Track-Specific Settings ---
        var lapTimeMs = trackRecord.LapTimeStringToMilliseconds(EstimatedLapTime); // Correctly calls the method on the TrackStats object
        double.TryParse(FuelPerLapText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double fuelVal);

        if (IsDry)
        {
            if (lapTimeMs.HasValue) trackRecord.AvgLapTimeDry = lapTimeMs;
            if (fuelVal > 0) trackRecord.AvgFuelPerLapDry = fuelVal;
        }
        else // IsWet
        {
            if (lapTimeMs.HasValue) trackRecord.AvgLapTimeWet = lapTimeMs;
            if (fuelVal > 0) trackRecord.AvgFuelPerLapWet = fuelVal;
        }

        trackRecord.PitLaneLossSeconds = this.PitLaneTimeLoss;

        if (IsPersonalBestAvailable && _loadedBestLapTimeSeconds > 0)
        {
            trackRecord.BestLapMs = (int)(_loadedBestLapTimeSeconds * 1000);
        }
        // --- Save to File and Notify User ---
        _plugin.ProfilesViewModel.SaveProfiles();

        // --- NEW: Force the Profiles tab's track list to refresh ---
        _plugin.ProfilesViewModel.RefreshTracksForSelectedProfile();

        LoadProfileData(); // refresh ProfileAvg... labels and sources after save

        MessageBox.Show($"All planner settings have been saved to the '{targetProfile.ProfileName}' profile for the track '{trackRecord.DisplayName}'.", "Planner Data Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // Unique id to make sure the UI and the engine are the same instance
    public string InstanceTag { get; } = Guid.NewGuid().ToString("N").Substring(0, 6);

    public FuelCalcs(LalaLaunch plugin)
    {
        _plugin = plugin;
        RebuildAvailableCarProfiles();

        UseLiveLapPaceCommand = new RelayCommand(_ => UseLiveLapPace(),_ => IsLiveLapPaceAvailable);
        UseLiveFuelPerLapCommand = new RelayCommand(_ => UseLiveFuelPerLap());
        LoadProfileLapTimeCommand = new RelayCommand(_ => LoadProfileLapTime(),_ => SelectedCarProfile != null && !string.IsNullOrEmpty(SelectedTrack));
        UseProfileFuelPerLapCommand = new RelayCommand(_ => UseProfileFuelPerLap());
        UseMaxFuelPerLapCommand = new RelayCommand(_ => UseMaxFuelPerLap(), _ => IsMaxFuelAvailable);

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

        // Update Delta to Leader Value
        double leaderAvgPace = _plugin.LiveLeaderAvgPaceSeconds;
        if (avgSeconds > 0 && leaderAvgPace > 0)
        {
            double delta = avgSeconds - leaderAvgPace;
            AvgDeltaToLdrValue = $"{delta:F2}s";
        }
        else
        {
            AvgDeltaToLdrValue = "-";
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
    }

    // Helper does the actual updates (runs on UI thread)
    private void ApplyLiveSession(string carName, string trackName)
    {
        SeenSessionSummary = $"Live: {carName} @ {trackName}";

        // 1) Find and set the car profile object
        var carProfile = AvailableCarProfiles.FirstOrDefault(p => p.ProfileName.Equals(carName, StringComparison.OrdinalIgnoreCase));
        if (this.SelectedCarProfile != carProfile)
        {
            this.SelectedCarProfile = carProfile;
        }

        // 2) Select track (triggers LoadProfileData via setter)
        if (!string.IsNullOrWhiteSpace(trackName) && this.SelectedTrack != trackName)
        {
            if (this.AvailableTracks.Contains(trackName))
            {
                this.SelectedTrack = trackName;
            }
            else
            {
                // If track doesn't exist for profile, add it to the list and select it
                this.AvailableTracks.Add(trackName);
                this.SelectedTrack = trackName;
            }
        }
        //else if (this.SelectedCarProfile == carProfile && this.SelectedTrack == trackName)
        //{
            // Force refresh if car/track haven't changed string-wise (e.g., on session restart)
            //LoadProfileData(); removed due conflict with live data selection
        //}
    }

    private void SetUIDefaults()
    {
        _raceLaps = 20.0;
        _raceMinutes = 40.0;
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
            SetUIDefaults(); // If no valid selection, reset to sandbox defaults
            CalculateStrategy();
            return;
        }

        var car = SelectedCarProfile;
        // Robust lookup: accept either key or display name
        var ts = car.FindTrack(SelectedTrack);
        if (ts == null && car?.TrackStats != null)
        {
            // Try by key
            if (!car.TrackStats.TryGetValue(SelectedTrack, out ts))
            {
                // Try by display name
                ts = car.TrackStats.Values
                    .FirstOrDefault(t => t.DisplayName?.Equals(SelectedTrack, StringComparison.OrdinalIgnoreCase) == true);
            }
        }


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

        ResetStrategyInputs();

        // Manually notify the UI of all changes
        OnPropertyChanged(nameof(ProfileAvgLapTimeDisplay));
        OnPropertyChanged(nameof(ProfileAvgFuelDisplay));
        OnPropertyChanged(nameof(ProfileAvgDryLapTimeDisplay));
        OnPropertyChanged(nameof(ProfileAvgDryFuelDisplay));
        OnPropertyChanged(nameof(HasProfileFuelPerLap));

        // Recompute with the newly loaded data
        CalculateStrategy();
    }

    private void ApplyWetFactor()
    {
        if (IsWet) { FuelPerLap = _baseDryFuelPerLap * (WetFactorPercent / 100.0); }
    }

    public void UpdateLiveDisplay(double liveMaxFuel)
    {
        // --- NEW LOGIC: Auto-set the override slider on new discovery ---
        // Check if this is a new, significantly different detected max fuel value.
        if (liveMaxFuel > 0 && Math.Abs(liveMaxFuel - _liveMaxFuel) > 0.1)
        {
            // It's a new discovery, so set the override slider to this value (rounded).
            MaxFuelOverride = Math.Round(liveMaxFuel);
        }

        _liveMaxFuel = liveMaxFuel; // Store the latest value for the next check
        if (liveMaxFuel > 0) { DetectedMaxFuelDisplay = $"(Detected Max: {liveMaxFuel:F1} L)"; }
        else { DetectedMaxFuelDisplay = "(Detected Max: N/A)"; }
        OnPropertyChanged(nameof(DetectedMaxFuelDisplay));
        OnPropertyChanged(nameof(IsMaxFuelOverrideTooHigh)); // Notify UI to re-check the highlight
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


        double num3 = ParseLapTime(EstimatedLapTime);
        double num2 = num3 - LeaderDeltaSeconds;
        double num4 = ParseLapTime(TimeLossPerLapOfFuelSave);
        ValidationMessage = "";
        if (num3 <= 0.0) { ValidationMessage = "Error: Your Estimated Lap Time cannot be zero or invalid."; }
        else if (num2 <= 0.0) { ValidationMessage = "Error: Leader's pace cannot be zero or negative (check your delta)."; }
        else if (fuelPerLap <= 0.0) { ValidationMessage = "Error: Fuel per Lap must be greater than zero."; }
        else if (MaxFuelOverride <= 0.0) { ValidationMessage = "Error: Max Fuel Override must be greater than zero."; }
        if (IsValidationMessageVisible)
        {
            TotalFuelNeeded = 0.0; RequiredPitStops = 0; StintBreakdown = ""; StopsSaved = 0;
            TotalTimeDifference = "N/A"; ExtraTimeAfterLeader = "N/A"; FirstStintFuel = 0.0;
            return;
        }
        double num6 = 0.0;
        if (IsTimeLimitedRace)
        {
            int num7 = 0; int num8 = -1; int num9 = 0;
            double num10 = fuelPerLap; // already includes wet factor when IsWet
            double num11 = RaceMinutes * 60.0;
            while (num7 != num8 && num9 < 10)
            {
                num9++; num8 = num7;
                double num12 = (double)num7 * (num + TireChangeTime);
                double num13 = num11 - num12;
                if (num13 < 0.0) { num13 = 0.0; }
                double num14 = num13 / num2;
                int num15 = 0;
                if (num3 - num2 > 0.0 && num3 > 0.0) { num15 = (int)Math.Floor(num14 / (num2 / (num3 - num2))); }
                num6 = Math.Max(0.0, num14 - (double)num15);
                double num17 = num6 * num10;
                double num18 = (IsContingencyInLaps ? (ContingencyValue * num10) : ContingencyValue);
                double num19 = num17 + num18;
                num7 = ((num19 > MaxFuelOverride) ? ((int)Math.Ceiling((num19 - MaxFuelOverride) / MaxFuelOverride)) : 0);
            }
        }
        else
        {
            int num20 = 0;
            if (num3 - num2 > 0.0 && num3 > 0.0) { num20 = (int)Math.Floor(RaceLaps / (num2 / (num3 - num2))); }
            num6 = Math.Max(0.0, RaceLaps - (double)num20);
        }
        StrategyResult strategyResult = CalculateSingleStrategy(num6, fuelPerLap, num3, num2, num, RaceMinutes * 60.0);

        TotalFuelNeeded = strategyResult.TotalFuel; RequiredPitStops = strategyResult.Stops;
        StintBreakdown = strategyResult.Breakdown; FirstStintFuel = strategyResult.FirstStintFuel;
        FirstStopTimeLoss = strategyResult.FirstStopTimeLoss;
        OnPropertyChanged(nameof(IsPitstopRequired));
        if (IsTimeLimitedRace && num3 > 0.0)
        {
            double value = Math.Max(0.0, num6 * num3 - RaceMinutes * 60.0);
            ExtraTimeAfterLeader = TimeSpan.FromSeconds(value).ToString("m\\:ss");
        }
        else { ExtraTimeAfterLeader = "N/A"; }
        double num24 = fuelPerLap - FuelSaveTarget;
        if (num24 <= 0.0) { StopsSaved = 0; TotalTimeDifference = "N/A"; return; }
        StrategyResult strategyResult2 = CalculateSingleStrategy(num6, num24, num3 + num4, num2, num, RaceMinutes * 60.0);

        StopsSaved = strategyResult.Stops - strategyResult2.Stops;
        double num25 = strategyResult2.TotalTime - strategyResult.TotalTime;
        TotalTimeDifference = $"{(num25 >= 0.0 ? "+" : "-")}{TimeSpan.FromSeconds(Math.Abs(num25)):m\\:ss\\.fff}";
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

        // If no stop is needed, we're done.
        if (result.TotalFuel <= MaxFuelOverride)
        {
            result.Stops = 0;
            result.FirstStintFuel = result.TotalFuel;

            var bodyNoStop = new StringBuilder();
            bodyNoStop.Append($"STINT 1:  {totalLaps:F0} L   Est {TimeSpan.FromSeconds(totalLaps * playerPaceSeconds):hh\\:mm\\:ss}   Start {result.TotalFuel:F1} L");

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
        result.FirstStintFuel = MaxFuelOverride;
        double lapsInFirstStint = Math.Floor(MaxFuelOverride / fuelPerLap);
        lapsRemaining -= lapsInFirstStint;
        // Fuel left when you arrive for the first stop (could be >0 if Stint 1 didn’t end exactly at empty)
        double fuelAtPitIn = Math.Max(0.0, MaxFuelOverride - (lapsInFirstStint * fuelPerLap));

        body.Append($"STINT 1:  {lapsInFirstStint:F0} L   Est {TimeSpan.FromSeconds(lapsInFirstStint * playerPaceSeconds):hh\\:mm\\:ss}   Start {result.FirstStintFuel:F1} L");

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
            double refuelTime = (_refuelRate > 0) ? (fuelToAdd / _refuelRate) : 0;
            double stationaryTime = Math.Max(this.TireChangeTime, refuelTime);
            double totalStopTime = pitLaneTimeLoss + stationaryTime;
            if (i == 1) { result.FirstStopTimeLoss = totalStopTime; }
            totalPitTime += totalStopTime;


            // STOP line (one line, hh:mm:ss.f total + components). Then a blank line after it.
            var stopTs = TimeSpan.FromSeconds(totalStopTime);
            body.AppendLine();
            body.AppendLine($"STOP {i}:   Est {stopTs:mm\\:ss}   Lane {pitLaneTimeLoss:F1}   Tires {this.TireChangeTime:F1}   Fuel {refuelTime:F1}");
            body.AppendLine();

            double fuelAtPitExit = fuelAtPitIn + fuelToAdd;
            double lapsInNextStint = Math.Floor(fuelAtPitExit / fuelPerLap);
            // Ensure the final stint isn't longer than the remaining race laps
            if (lapsInNextStint > lapsRemaining)
            {
                lapsInNextStint = lapsRemaining;
            }

            body.Append($"STINT {i + 1}:  {lapsInNextStint:F0} L   Est {TimeSpan.FromSeconds(lapsInNextStint * playerPaceSeconds):hh\\:mm\\:ss}   Add   {fuelToFillTo:F1} L");

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