// --- Using Directives ---
using GameReaderCommon;
using LaunchPlugin;
using log4net.Repository.Hierarchy;
using Newtonsoft.Json;
using SimHub.Plugins;
using SimHub.Plugins.OutputPlugins.GraphicalDash;
using SimHubWPF;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using static SimHub.Plugins.UI.SupportedGamePicker;



namespace LaunchPlugin
{

    [PluginDescription("Launch Analysis and Dashes")]
    [PluginAuthor("Lalabot")]
    [PluginName("LalaLaunch")]

    
    public class LalaLaunch : IPlugin, IDataPlugin, IWPFSettingsV2, INotifyPropertyChanged
    {
        // --- SimHub Interfaces ---
        public PluginManager PluginManager { get; set; }
        public LaunchPluginSettings Settings { get; private set; }
        public ImageSource PictureIcon => null;
        public string LeftMenuTitle => "Lala Plugin";

        // --- Dashboard Manager ---
        public ScreenManager Screens = new ScreenManager();

        // --- Fuel Calculator Engine ---
        public FuelCalcs FuelCalculator { get; private set; }
        public TrackStats EnsureTrackRecord(string carProfileName, string trackName)
            => ProfilesViewModel.EnsureCarTrack(carProfileName, trackName);
        public TrackStats GetTrackRecord(string carProfileName, string trackName)
            => ProfilesViewModel.TryGetCarTrack(carProfileName, trackName);

        // --- Profiles Manager ---
        public ProfilesManagerViewModel ProfilesViewModel { get; private set; }

        // --- NEW: Active Profile Hub ---
        private CarProfile _activeProfile;
        public CarProfile ActiveProfile
        {
            get => _activeProfile;
            set
            {
                if (_activeProfile != value)
                {
                    // Unsubscribe from the old profile's PropertyChanged event
                    if (_activeProfile != null)
                    {
                        _activeProfile.PropertyChanged -= ActiveProfile_PropertyChanged;
                    }

                    _activeProfile = value;

                    // Subscribe to the new profile's PropertyChanged event
                    if (_activeProfile != null)
                    {
                        _activeProfile.PropertyChanged += ActiveProfile_PropertyChanged;
                    }
                    OnPropertyChanged(); // Notify UI that ActiveProfile itself has changed
                    OnPropertyChanged(nameof(CanReturnToDefaults));
                    if (ProfilesViewModel != null && ProfilesViewModel.SelectedProfile != _activeProfile)
                    {
                        ProfilesViewModel.SelectedProfile = _activeProfile;
                    }
                    
                    IsActiveProfileDirty = false; // Reset dirty flag on profile switch
                }
            }
        }


        private bool _isActiveProfileDirty;
        public bool IsActiveProfileDirty
        {
            get => _isActiveProfileDirty;
            set { if (_isActiveProfileDirty != value) { _isActiveProfileDirty = value; OnPropertyChanged(); } }
        }

        private void ActiveProfile_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // When any property on the ActiveProfile changes, mark it as dirty.
            IsActiveProfileDirty = true;
        }
        public bool CanReturnToDefaults => ActiveProfile?.ProfileName != "Default Settings";
        
        // --- Expose the direct travel time calculated by PitEngine ---
        public double LastDirectTravelTime => _pit?.LastDirectTravelTime ?? 0.0;

        // --- Live Fuel Calculation State ---
        private double _lastFuelLevel = -1;
        private double _lapStartFuel = -1;
        private double _lastLapDistPct = -1;
        private readonly List<double> _recentConsumptions = new List<double>();
        private const int ConsumptionSampleCount = 3; // Number of clean laps to average

        // --- Live Fuel Calculation Outputs ---
        public double LiveFuelPerLap { get; private set; }
        public double LiveLapsRemainingInRace { get; private set; }
        public double DeltaLaps { get; private set; }
        public double TargetFuelPerLap { get; private set; }
        public bool IsPitWindowOpen { get; private set; }
        public int PitWindowOpeningLap { get; private set; }
        public double LapsRemainingInTank { get; private set; }
        public int Confidence { get; private set; }
        public double Pit_TotalNeededToEnd { get; private set; }
        public double Pit_NeedToAdd { get; private set; }
        public double Pit_TankSpaceAvailable { get; private set; }
        public double Pit_WillAdd { get; private set; }
        public double Pit_DeltaAfterStop { get; private set; }
        public double Pit_FuelOnExit { get; private set; }
        public int Pit_StopsRequiredToEnd { get; private set; }
        public double LiveCarMaxFuel { get; private set; }

        private double _maxFuelPerLapSession = 0.0;
        public double MaxFuelPerLapDisplay => _maxFuelPerLapSession;

        // --- Live Lap Pace (for Fuel tab, once-per-lap update) ---
        private readonly List<double> _recentLapTimes = new List<double>(); // seconds
        private const int LapTimeSampleCount = 6;   // keep last N clean laps
        private TimeSpan _lastSeenBestLap = TimeSpan.Zero;
        private readonly List<double> _recentLeaderLapTimes = new List<double>(); // seconds
        public double LiveLeaderAvgPaceSeconds { get; private set; }
        private DateTime _lastPitLossSavedAtUtc = DateTime.MinValue;
        private double _lastPitLossSaved = double.NaN;

        // --- PIT TEST: dash-facing fields (for replay validation) ---
        double _pitDbg_AvgPaceUsedSec = 0.0;
        string _pitDbg_AvgPaceSource = "";

        double _pitDbg_InLapSec = 0.0;
        double _pitDbg_OutLapSec = 0.0;
        double _pitDbg_DeltaInSec = 0.0;
        double _pitDbg_DeltaOutSec = 0.0;

        double _pitDbg_CandidateSavedSec = 0.0;
        string _pitDbg_CandidateSource = "";

        // --- PIT TEST: raw formula diagnostics ---
        double _pitDbg_RawPitLapSec = 0.0;       // derived: lap that included the stop (see formula)
        double _pitDbg_RawDTLFormulaSec = 0.0;   // (Lpit - Stop + Lout) - 2*Avg

        // --- Property Changed Interface ---
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private long _lastSessionId = -1;

        public RelayCommand SaveActiveProfileCommand { get; private set; }
        public RelayCommand ReturnToDefaultsCommand { get; private set; }
        private void ReturnToDefaults()
        {
            ActiveProfile = ProfilesViewModel.GetProfileForCar("Default Settings") ?? ProfilesViewModel.CarProfiles.FirstOrDefault();
            _currentCarModel = "Unknown"; // Reset car model state to match
        }
        private void SaveActiveProfile()
        {
            ProfilesViewModel.SaveProfiles();
            IsActiveProfileDirty = false; // Reset the dirty flag after saving
            SimHub.Logging.Current.Info($"LalaLaunch: Changes to '{ActiveProfile?.ProfileName}' saved.");
        }

        private static double ComputeStableMedian(List<double> samples)
        {
            if (samples == null || samples.Count == 0) return 0;
            var arr = samples.ToArray();
            Array.Sort(arr);
            int mid = arr.Length / 2;
            return (arr.Length % 2 == 1) ? arr[mid] : (arr[mid - 1] + arr[mid]) / 2.0;
        }

        private void UpdateLiveFuelCalcs(GameData data)
        {
            // --- 1) Gather required data ---
            double currentFuel = data.NewData?.Fuel ?? 0.0;
            double rawLapPct = data.NewData?.TrackPositionPercent ?? 0.0;
            double maxFuel = data.NewData?.MaxFuel ?? 0.0;

            // Pit detection: use both signals (some installs expose only one reliably)
            bool isInPitLaneFlag = (data.NewData?.IsInPitLane ?? 0) != 0;
            bool isOnPitRoadFlag = Convert.ToBoolean(
                PluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.IsOnPitRoad") ?? false
            );
            bool inPitArea = isInPitLaneFlag || isOnPitRoadFlag;

            // Normalize lap % to 0..1 in case source is 0..100
            double curPct = rawLapPct;
            if (curPct > 1.5) curPct *= 0.01;
            double lastPct = _lastLapDistPct;
            if (lastPct > 1.5) lastPct *= 0.01;

            // --- 2) Detect S/F crossing & update rolling averages ---
            bool lapCrossed = lastPct > 0.95 && curPct < 0.05;

            // --- Add this block right after 'lapCrossed' is calculated ---
            if (lapCrossed)
            {
                // This logic checks if the PitEngine is waiting for an out-lap and, if so,
                // provides it with the necessary data to finalize the calculation.
                if (_pit != null && (_pit.CurrentPitPhase == PitPhase.None || _pit.CurrentPitPhase == PitPhase.ExitingPits)) // Ensure we are on track
                {
                    var lastLapTs = data.NewData?.LastLapTime ?? TimeSpan.Zero;
                    double lastLapSec = lastLapTs.TotalSeconds;

                    // Basic validity check for the lap itself
                    bool lastLapLooksClean = !inPitArea && lastLapSec > 20 && lastLapSec < 900;

                    // Get a stable average pace to compare against
                    double stableAvgPace = ComputeStableMedian(_recentLapTimes);

                    // --- Add fallback logic if live pace is unavailable ---
                    if (stableAvgPace <= 0 && ActiveProfile != null)
                    {
                        var trackRecord = ActiveProfile.FindTrack(CurrentTrackKey);
                        if (trackRecord?.AvgLapTimeDry > 0)
                        {
                            stableAvgPace = trackRecord.AvgLapTimeDry.Value / 1000.0;
                            SimHub.Logging.Current.Debug($"Pace Delta: No live pace available. Using profile avg lap time as fallback: {stableAvgPace:F2}s");
                        }
                    }
                    // --- PB tertiary fallback (for replays / no live median and no profile avg) ---
                    if (stableAvgPace <= 0 && _lastSeenBestLap > TimeSpan.Zero)
                    {
                        stableAvgPace = _lastSeenBestLap.TotalSeconds;
                    }
                    SimHub.Logging.Current.Debug($"[Pit/Pace] Baseline used = {stableAvgPace:F3}s (live median → profile avg → PB).");
                    // Pass the data to the PitEngine to handle
                    _pit.PrimeInLapTime(_lastLapTimeSec);

                    // Decide and publish baseline (live-median → profile-avg → session-pb)
                    string paceSource = "live-median";

                    if (stableAvgPace <= 0 && ActiveProfile != null)
                    {
                        var trackRecord = ActiveProfile.FindTrack(CurrentTrackKey);
                        if (trackRecord?.AvgLapTimeDry > 0)
                        {
                            stableAvgPace = trackRecord.AvgLapTimeDry.Value / 1000.0;
                            paceSource = "profile-avg";
                        }
                    }

                    if (stableAvgPace <= 0 && _lastSeenBestLap > TimeSpan.Zero)
                    {
                        stableAvgPace = _lastSeenBestLap.TotalSeconds;
                        paceSource = "session-pb";
                    }

                    _pitDbg_AvgPaceUsedSec = stableAvgPace;
                    _pitDbg_AvgPaceSource = paceSource;

                    _pit.FinalizePaceDeltaCalculation(lastLapSec, stableAvgPace, lastLapLooksClean);

                    // Publish in/out and deltas for the dash
                    _pitDbg_InLapSec = _lastLapTimeSec;   // the lap we primed as the in-lap
                    _pitDbg_OutLapSec = lastLapSec;        // the lap that just finished (out-lap)

                    _pitDbg_DeltaInSec = _pitDbg_InLapSec - _pitDbg_AvgPaceUsedSec; // can be negative at shortcut tracks
                    _pitDbg_DeltaOutSec = _pitDbg_OutLapSec - _pitDbg_AvgPaceUsedSec;

                    // Roll the "previous lap" pointer AFTER we used it as in-lap
                    _lastLapTimeSec = lastLapSec;
                    // --- PIT TEST: derive the raw pit lap and the formula DTL for visibility ---
                    // Inputs at this point:
                    //   DTL  = _pit.LastTotalPitCycleTimeLoss
                    //   Avg  = _pitDbg_AvgPaceUsedSec
                    //   Lout = lastLapSec
                    //   Stop = _pit.PitStopDuration.TotalSeconds
                    double dtl = _pit?.LastTotalPitCycleTimeLoss ?? 0.0;
                    double avg = _pitDbg_AvgPaceUsedSec;
                    double lout = lastLapSec;
                    double stop = _pit?.PitStopDuration.TotalSeconds ?? 0.0;

                    // Reconstruct the raw pit-lap time that would include the stop:
                    _pitDbg_RawPitLapSec = dtl + (2.0 * avg) - lout + stop;

                    // Show the formula directly: (Lpit - Stop + Lout) - 2*Avg
                    _pitDbg_RawDTLFormulaSec = (_pitDbg_RawPitLapSec - stop + lout) - (2.0 * avg);

                }
            }

            if (lapCrossed) { SimHub.Logging.Current.Debug("LalaLaunch.LiveFuel: Lap crossed."); }
            // First-time init: capture a starting fuel level and bail until we’ve completed one lap
            if (_lapStartFuel < 0)
            {
                _lapStartFuel = currentFuel;
            }

            if (lapCrossed)
            {
                // Per-lap fuel sample (ignore pit-lane laps and obvious refuel spikes)
                if (!inPitArea && _lapStartFuel > 0)
                {
                    double consumed = _lapStartFuel - currentFuel;

                    // Sanity window: ignore negative, tiny, or absurdly high values
                    // (0.1 L minimum; 0.1*maxFuel per lap maximum as a coarse upper bound)
                    double maxPlausible = Math.Max(5.0, 0.10 * Math.Max(maxFuel, 50.0)); // at least 5 L, or 10% of tank
                    if (consumed > 0.10 && consumed < maxPlausible)
                    {
                        SimHub.Logging.Current.Debug($"LalaLaunch.LiveFuel: Clean fuel sample taken: {consumed:F3} L."); // Log good samples

                        // --- Update max fuel per lap for the session, rejecting outliers ---
                        if (consumed > _maxFuelPerLapSession)
                        {
                            // A simple outlier check: only accept a new max if it's within 150% of the current
                            // rolling average, provided we have a stable average to compare against.
                            bool isOutlier = (_recentConsumptions.Count >= ConsumptionSampleCount) && (consumed > _recentConsumptions.Average() * 1.5);
                            if (!isOutlier)
                            {
                                _maxFuelPerLapSession = consumed;
                                FuelCalculator?.SetMaxFuelPerLap(_maxFuelPerLapSession);
                            }
                        }
                        _recentConsumptions.Add(consumed);
                        while (_recentConsumptions.Count > ConsumptionSampleCount)
                            _recentConsumptions.RemoveAt(0);

                        // We recompute the live average once per clean lap
                        LiveFuelPerLap = _recentConsumptions.Average();
                        Confidence = _recentConsumptions.Count;
                        FuelCalculator?.SetLiveFuelPerLap(LiveFuelPerLap);

                        // If we have a full sample set, update the active profile's track data
                        if (_recentConsumptions.Count >= ConsumptionSampleCount && ActiveProfile != null)
                        {
                            var trackRecord = ActiveProfile.FindTrack(CurrentTrackKey);
                            if (trackRecord != null)
                            {
                                // Update the dry average fuel per lap
                                trackRecord.AvgFuelPerLapDry = LiveFuelPerLap;
                            }
                        }

                    }
                    else
                    {
                        // else: discarded sample
                        SimHub.Logging.Current.Debug($"LalaLaunch.LiveFuel: Discarded invalid fuel sample: {consumed:F3} L. (inPit={inPitArea})");
                    }
                    
                }

                // Start the next lap’s measurement window
                _lapStartFuel = currentFuel;

                // --- Live lap pace bookkeeping (only if your backing members exist) ---
                // Prefer SimHub/iRacing last-lap if available; basic guard against out/in laps.
                var lastLapTs = data.NewData?.LastLapTime ?? TimeSpan.Zero;
                double lastLapSec = lastLapTs.TotalSeconds;
                bool lastLapLooksClean = !inPitArea && lastLapSec > 20 && lastLapSec < 900; // 20s..15min


                if (lastLapLooksClean)
                {
                    try
                    {
                        double sessionPBSeconds = _lastSeenBestLap.TotalSeconds;
                        // Only count the lap if we have no PB yet, or if the lap is within 5s of the PB.
                        bool isPaceLap = (sessionPBSeconds <= 0) || (lastLapSec < sessionPBSeconds + 10.0);

                        if (isPaceLap)
                        {
                            _recentLapTimes.Add(lastLapSec);
                            while (_recentLapTimes.Count > LapTimeSampleCount)
                                _recentLapTimes.RemoveAt(0);

                            var stableAvg = ComputeStableMedian(_recentLapTimes);
                            FuelCalculator?.SetLiveLapPaceEstimate(stableAvg, _recentLapTimes.Count);

                            FuelCalculator?.SetPersonalBestSeconds(sessionPBSeconds);
                        }
                        else
                        {
                            SimHub.Logging.Current.Debug($"LalaLaunch.LivePace: Discarded slow lap time sample: {lastLapSec:F3}s (delta to PB > 10s).");
                        }
                    }
                    catch { /* ... */ }
                }
                else
                {
                    SimHub.Logging.Current.Debug($"LalaLaunch.LivePace: Discarded invalid lap time sample: {lastLapSec:F3} s. (inPit={inPitArea})");
                }
                // --- NEW: Live LEADER pace bookkeeping ---
                try
                {
                    var leaderLastLapTs = (TimeSpan?)PluginManager.GetPropertyValue("IRacingExtraProperties.iRacing_ClassLeaderboard_Driver_00_LastLapTime") ?? TimeSpan.Zero;
                    double leaderLastLapSec = leaderLastLapTs.TotalSeconds;

                    if (!inPitArea && leaderLastLapSec > 20 && leaderLastLapSec < 900)
                    {
                        var leaderBestLapTs = (TimeSpan?)PluginManager.GetPropertyValue("IRacingExtraProperties.iRacing_ClassLeaderboard_Driver_00_BestLapTime") ?? TimeSpan.Zero;
                        double leaderBestLapSec = leaderBestLapTs.TotalSeconds;

                        // Only count the lap if the leader has no PB, or if their lap is within 5s of their PB.
                        bool isLeaderPaceLap = (leaderBestLapSec <= 0) || (leaderLastLapSec < leaderBestLapSec + 10.0);

                        if (isLeaderPaceLap)
                        {
                            _recentLeaderLapTimes.Add(leaderLastLapSec);
                            while (_recentLeaderLapTimes.Count > 3)
                                _recentLeaderLapTimes.RemoveAt(0);

                            if (_recentLeaderLapTimes.Count > 0)
                            {
                                LiveLeaderAvgPaceSeconds = _recentLeaderLapTimes.Average();
                            }
                        }
                    }
                }
                catch { /* if property is not available, do nothing */ }
            }

            // If we haven’t accumulated any clean-lap samples yet, fall back to SimHub’s computed estimator
            if (!_recentConsumptions.Any())
            {
                LiveFuelPerLap = Convert.ToDouble(
                    PluginManager.GetPropertyValue("DataCorePlugin.Computed.Fuel_LitersPerLap") ?? 0.0
                );
                Confidence = 0;

                // Ensure the UI can enable the Live button as soon as a non-zero estimate appears
                if (LiveFuelPerLap > 0)
                    FuelCalculator?.OnLiveFuelPerLapUpdated();
            }

            // --- 3) Core dashboard properties (guarded by a valid consumption rate) ---
            if (LiveFuelPerLap <= 0)
            {
                LiveLapsRemainingInRace = 0;
                DeltaLaps = 0;
                TargetFuelPerLap = 0;
                IsPitWindowOpen = false;
                PitWindowOpeningLap = 0;
                LapsRemainingInTank = 0;

                Pit_TotalNeededToEnd = 0;
                Pit_NeedToAdd = 0;
                Pit_TankSpaceAvailable = 0;
                Pit_WillAdd = 0;
                Pit_FuelOnExit = 0;
                Pit_DeltaAfterStop = 0;
                Pit_StopsRequiredToEnd = 0;
            }
            else
            {
                LapsRemainingInTank = currentFuel / LiveFuelPerLap;

                LiveLapsRemainingInRace = Convert.ToDouble(
                    PluginManager.GetPropertyValue("IRacingExtraProperties.iRacing_LapsRemainingFloat") ?? 0.0
                );

                double fuelNeededToEnd = LiveLapsRemainingInRace * LiveFuelPerLap;
                DeltaLaps = LapsRemainingInTank - LiveLapsRemainingInRace;

                TargetFuelPerLap = (DeltaLaps < 0 && LiveLapsRemainingInRace > 0)
                    ? currentFuel / LiveLapsRemainingInRace
                    : 0;

                // Pit math
                Pit_TotalNeededToEnd = fuelNeededToEnd;
                Pit_NeedToAdd = Math.Max(0, fuelNeededToEnd - currentFuel);
                Pit_TankSpaceAvailable = Math.Max(0, maxFuel - currentFuel);

                double fuelToRequest = Convert.ToDouble(
                    PluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PitSvFuel") ?? 0.0
                );
                Pit_WillAdd = Math.Min(fuelToRequest, Pit_TankSpaceAvailable);

                Pit_FuelOnExit = currentFuel + Pit_WillAdd;
                Pit_DeltaAfterStop = (LiveFuelPerLap > 0)
                    ? (Pit_FuelOnExit / LiveFuelPerLap) - LiveLapsRemainingInRace
                    : 0;

                // Pit window logic
                double lapsPerTank = (LiveFuelPerLap > 0) ? (maxFuel / LiveFuelPerLap) : 0;
                int stopsRequired = (maxFuel > 0)
                    ? (int)Math.Ceiling((fuelNeededToEnd - currentFuel) / maxFuel)
                    : 0;
                Pit_StopsRequiredToEnd = Math.Max(0, stopsRequired);

                if (stopsRequired <= 0)
                {
                    IsPitWindowOpen = false;
                    PitWindowOpeningLap = 0;
                }
                else
                {
                    double lapsUntilEmpty = LapsRemainingInTank;
                    double pitWindowLapThreshold = lapsPerTank;
                    IsPitWindowOpen = lapsUntilEmpty <= pitWindowLapThreshold;

                    if (IsPitWindowOpen)
                    {
                        PitWindowOpeningLap = 0;
                    }
                    else
                    {
                        double lapsUntilWindowOpens = lapsUntilEmpty - pitWindowLapThreshold;

                        // CompletedLaps is decimal? -> normalize to double
                        double completedLaps = Convert.ToDouble(data.NewData?.CompletedLaps ?? 0m);

                        PitWindowOpeningLap = (int)Math.Floor(completedLaps + lapsUntilWindowOpens); ;
                    }
                }
            }

            // --- 4) Update "last" values for next tick ---
            _lastFuelLevel = currentFuel;
            _lastLapDistPct = rawLapPct; // keep original scale; we normalize on read
            if (_lapStartFuel < 0) _lapStartFuel = currentFuel;
        }


        // --- Settings / Car Profiles ---

        private string _currentCarModel = "Unknown";
        private string _currentSettingsProfileName = "Default Settings";
        public string CurrentCarModel
        {
            get => _currentCarModel;
            set
            {
                if (_currentCarModel != value)
                {
                    _currentCarModel = value;
                    OnPropertyChanged(nameof(CurrentCarModel));
                }
            }
        }

        public string CurrentTrackName { get; private set; } = "Unknown";
        public string CurrentSettingsProfileName
        {
            get => _currentSettingsProfileName;
            set
            {
                if (_currentSettingsProfileName != value)
                {
                    _currentSettingsProfileName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CurrentTrackKey { get; private set; } = "unknown";

        public enum ProfileEditMode { ActiveCar, CarProfile, Template }

        
        // --- Logging ---
        private string _currentLaunchTraceFilenameForSummary = "N/A";
        private TelemetryTraceLogger _telemetryTraceLogger;

        // --- Flags & State (Boolean) ---
        private bool _antiStallDetectedThisRun = false;
        private bool _bitePointInTargetRange = false;
        private bool _boggedDown = false;
        private bool _falseStartDetected = false;
        private bool _hasCapturedClutchDropThrottle = false;
        private bool _hasCapturedLaunchRPMForRun = false;
        private bool _hasCapturedReactionTime = false;
        private bool _hasLoggedCurrentRun = false;
        private bool _hasProcessedOnTrack = false;
        private bool _hasValidClutchReleaseData = false;
        private bool _hasValidLaunchData = false;
        private bool _isAntiStallActive = false;
        private bool _isTimingZeroTo100 = false;
        private bool _launchSuccessful = false;
        private bool _msgCxPressed = false;
        private bool _pitScreenActive = false;
        private bool _pitScreenDismissed = false;
        private bool _rpmInTargetRange = false;
        private bool _throttleInTargetRange = false;
        private bool _waitingForClutchRelease = false;
        private bool _wheelSpinDetected = false;
        private bool _zeroTo100CompletedThisRun = false;
        private bool _wasClutchDown = false;

        // --- Rejoin Assist Module State ---
        private readonly Stopwatch _offTrackHighSpeedTimer = new Stopwatch();
        private readonly Stopwatch _msgCxCooldownTimer = new Stopwatch();
        
        // --- State: Timers ---
        private readonly Stopwatch _pittingTimer = new Stopwatch();
        private readonly Stopwatch _zeroTo100Stopwatch = new Stopwatch();
        private readonly Stopwatch _clutchTimer = new Stopwatch();
        private readonly Stopwatch _reactionTimer = new Stopwatch();
        private DateTime _launchEndTime = DateTime.MinValue;
        private bool _launchModeUserDisabled = false;
        private DateTime _manualPrimedStartedAt = DateTime.MinValue;

        // --- FSM Helper Flags ---
        private bool IsIdle => _currentLaunchState == LaunchState.Idle;
        private bool IsManualPrimed => _currentLaunchState == LaunchState.ManualPrimed;
        private bool IsAutoPrimed => _currentLaunchState == LaunchState.AutoPrimed;
        private bool IsInProgress => _currentLaunchState == LaunchState.InProgress;
        private bool IsLogging => _currentLaunchState == LaunchState.Logging;
        private bool IsCompleted => _currentLaunchState == LaunchState.Completed;
        private bool IsCancelled => _currentLaunchState == LaunchState.Cancelled;

        // --- Convenience Flags ---
        private bool IsPrimed => IsManualPrimed || IsAutoPrimed;
        private bool IsLaunchActive => IsPrimed || IsInProgress || IsLogging;
        private bool IsLaunchVisible => IsLaunchActive || IsCompleted;

        // Centralized state machine for launch phases
        private void SetLaunchState(LaunchState newState)
        {
            if (_currentLaunchState == newState) return;

            _currentLaunchState = newState;

            // Start timeout timer if entering ManualPrimed
            if (newState == LaunchState.ManualPrimed)
            {
                _manualPrimedStartedAt = DateTime.Now;
            }
        }

        // Code Engines
        private RejoinAssistEngine _rejoinEngine;
        private MessagingSystem _msgSystem;
        private PitEngine _pit;



        private enum LaunchState
        {
            Idle,           // Resting state
            ManualPrimed,   // User toggled ON manually (non-race)
            AutoPrimed,     // Auto start triggered (race mode)
            InProgress,     // Launch just beginning
            Logging,        // Trace logging has started
            Completed,      // 0–100 complete, post-launch hold state
            Cancelled       // User override or expired timeout
        }

        private LaunchState _currentLaunchState = LaunchState.Idle;


        // --- Launch Metrics / Values ---
        private double _actualRpmAtClutchRelease = 0.0;
        private double _actualThrottleAtClutchRelease = 0.0;
        private double _avgSessionLaunchRPM = 0.0;
        private double _clutchReleaseCurrentRunMs = 0.0;
        private double _clutchReleaseDelta = 0.0;
        private double _clutchReleaseLastTime = 0.0;
        private double _currentLaunchRPM = 0.0;
        private double _lastAvgSessionLaunchRPM = 0.0;
        private double _lastLaunchRPM = 0.0;
        private double _lastMinRPMDuringLaunch = 0.0;
        private double _maxThrottlePostLaunch = -1.0;
        private double _maxTractionLossDuringLaunch = 0.0;
        private double _minRPMDuringLaunch = 99999.0;
        private double _minThrottlePostLaunch = 101.0;
        private double _paddleClutch = 0.0;
        private double _reactionTimeMs = 0.0;
        private double _rpmDeviationAtClutchRelease = 0.0;
        private double _throttleAtLaunchZoneStart = 0.0;
        private double _throttleDeviationAtClutchRelease = 0.0;
        private double _throttleModulationDelta = 0.0;
        private double _zeroTo100Delta = 0.0;
        private double _zeroTo100LastTime = 0.0;

        // --- Session State ---
        private string _lastSessionType = "";

        private string _lastSeenCar = "";
        private string _lastSeenTrack = "";
        private double _lastLapTimeSec = 0.0;   // last completed lap time

        // --- Session Launch RPM Tracker ---
        private readonly List<double> _sessionLaunchRPMs = new List<double>();

        // --- Light schedulers to throttle non-critical work ---
        private readonly System.Diagnostics.Stopwatch _poll250ms = new System.Diagnostics.Stopwatch();  // ~4 Hz
        private readonly System.Diagnostics.Stopwatch _poll500ms = new System.Diagnostics.Stopwatch();  // ~2 Hz

        // --- Already added earlier for MaxFuel throttling ---
        private double _lastAnnouncedMaxFuel = -1;


        // ---Temporary for Testing Purposes ---

        // --- Dual Clutch Placeholder (commented out) ---
        // private double _clutchLeft = 0.0;
        // private double _clutchRight = 0.0;
        // private double _virtualClutch = 0.0;


        public void Init(PluginManager pluginManager)
        {
            // --- INITIALIZATION ---
            this.PluginManager = pluginManager;
            Settings = this.ReadCommonSettings<LaunchPluginSettings>("GlobalSettings_V2", () => new LaunchPluginSettings());
            // The Action for "Apply to Live" in the Profiles tab is now simplified: just update the ActiveProfile
            ProfilesViewModel = new ProfilesManagerViewModel(this.PluginManager, (profile) => { this.ActiveProfile = profile; }, () => this.CurrentCarModel, () => this.CurrentTrackKey);
            ProfilesViewModel.LoadProfiles();

            // --- Set the initial ActiveProfile on startup ---
            // It will be "Default Settings" or the first profile if that doesn't exist.
            ActiveProfile = ProfilesViewModel.GetProfileForCar("Default Settings") ?? ProfilesViewModel.CarProfiles.FirstOrDefault();

            // --- NEW: Instantiate the Fuel Calculator ---
            FuelCalculator = new FuelCalcs(this);

            SaveActiveProfileCommand = new RelayCommand(p => SaveActiveProfile());
            ReturnToDefaultsCommand = new RelayCommand(p => ReturnToDefaults());
            _telemetryTraceLogger = new TelemetryTraceLogger(this);

            _poll250ms.Start();
            _poll500ms.Start();

            ResetAllValues();

            // --- DELEGATES FOR LIVE FUEL CALCULATOR ---
            this.AttachDelegate("Fuel.LiveFuelPerLap", () => LiveFuelPerLap);
            this.AttachDelegate("Fuel.LiveLapsRemainingInRace", () => LiveLapsRemainingInRace);
            this.AttachDelegate("Fuel.DeltaLaps", () => DeltaLaps);
            this.AttachDelegate("Fuel.TargetFuelPerLap", () => TargetFuelPerLap);
            this.AttachDelegate("Fuel.IsPitWindowOpen", () => IsPitWindowOpen);
            this.AttachDelegate("Fuel.PitWindowOpeningLap", () => PitWindowOpeningLap);
            this.AttachDelegate("Fuel.LapsRemainingInTank", () => LapsRemainingInTank);
            this.AttachDelegate("Fuel.Confidence", () => Confidence);
            this.AttachDelegate("Fuel.Pit.TotalNeededToEnd", () => Pit_TotalNeededToEnd);
            this.AttachDelegate("Fuel.Pit.NeedToAdd", () => Pit_NeedToAdd);
            this.AttachDelegate("Fuel.Pit.TankSpaceAvailable", () => Pit_TankSpaceAvailable);
            this.AttachDelegate("Fuel.Pit.WillAdd", () => Pit_WillAdd);
            this.AttachDelegate("Fuel.Pit.DeltaAfterStop", () => Pit_DeltaAfterStop);
            this.AttachDelegate("Fuel.Pit.FuelOnExit", () => Pit_FuelOnExit);
            this.AttachDelegate("Fuel.Pit.StopsRequiredToEnd", () => Pit_StopsRequiredToEnd);

            // --- Expose all Pit Time Loss properties ---
            this.AttachDelegate("Pit.LastDirectTravelTime", () => _pit.LastDirectTravelTime); // The final calculated value from the "Direct Stopwatch" method.
            this.AttachDelegate("Pit.LastTotalPitCycleTimeLoss", () => _pit.LastTotalPitCycleTimeLoss); // The final calculated value from the "Race Pace Delta" method.
            this.AttachDelegate("Pit.Debug.TimeOnPitRoad", () => _pit.TimeOnPitRoad.TotalSeconds); // The raw timer for total time spent on pit road (tPit).
            this.AttachDelegate("Pit.Debug.LastPitStopDuration", () => _pit.PitStopDuration.TotalSeconds); // The raw timer for the last stationary pit stop duration (tStop).
            this.AttachDelegate("Pit.LastPaceDeltaNetLoss", () => _pit.LastPaceDeltaNetLoss); // --- The net loss from the Pace Delta method ---
            // --- PIT TEST: inputs & outputs for replay validation ---
            this.AttachDelegate("Lala.Pit.AvgPaceUsedSec", () => _pitDbg_AvgPaceUsedSec);
            this.AttachDelegate("Lala.Pit.AvgPaceSource", () => _pitDbg_AvgPaceSource);
            // Raw components / formula view
            this.AttachDelegate("Lala.Pit.Raw.PitLapSec", () => _pitDbg_RawPitLapSec);
            this.AttachDelegate("Lala.Pit.Raw.DTLFormulaSec", () => _pitDbg_RawDTLFormulaSec);

            this.AttachDelegate("Lala.Pit.InLapSec", () => _pitDbg_InLapSec);
            this.AttachDelegate("Lala.Pit.OutLapSec", () => _pitDbg_OutLapSec);
            this.AttachDelegate("Lala.Pit.DeltaInSec", () => _pitDbg_DeltaInSec);
            this.AttachDelegate("Lala.Pit.DeltaOutSec", () => _pitDbg_DeltaOutSec);

            this.AttachDelegate("Lala.Pit.DriveThroughLossSec", () => _pit?.LastTotalPitCycleTimeLoss ?? 0.0);
            this.AttachDelegate("Lala.Pit.DirectTravelSec", () => _pit?.LastDirectTravelTime ?? 0.0);
            this.AttachDelegate("Lala.Pit.StopSeconds", () => _pit?.PitStopDuration.TotalSeconds ?? 0.0);
            this.AttachDelegate("Lala.Pit.NetMinusStopSec", () => _pit?.LastPaceDeltaNetLoss ?? 0.0);

            // From profile: what’s currently stored as “Pit Lane Loss”
            this.AttachDelegate("Lala.Pit.Profile.PitLaneLossSec", () =>
            {
                var ts = ActiveProfile?.FindTrack(CurrentTrackKey);
                return ts?.PitLaneLossSeconds ?? 0.0;
            });

            // After a save, show what we saved and why
            this.AttachDelegate("Lala.Pit.CandidateSavedSec", () => _pitDbg_CandidateSavedSec);
            this.AttachDelegate("Lala.Pit.CandidateSource", () => _pitDbg_CandidateSource); // "total" or "direct"

            // --- DELEGATES FOR DASHBOARD STATE & OVERLAYS ---
            this.AttachDelegate("CurrentDashPage", () => Screens.CurrentPage);
            this.AttachDelegate("DashControlMode", () => Screens.Mode);
            this.AttachDelegate("FalseStartDetected", () => _falseStartDetected);
            this.AttachDelegate("LastSessionType", () => _lastSessionType);
            this.AttachDelegate("MsgCxPressed", () => _msgCxPressed);
            this.AttachDelegate("PitScreenActive", () => _pitScreenActive);
            this.AttachDelegate("RejoinAlertReasonCode", () => (int)_rejoinEngine.CurrentLogicCode);
            this.AttachDelegate("RejoinAlertReasonName", () => _rejoinEngine.CurrentLogicCode.ToString());
            this.AttachDelegate("RejoinAlertMessage", () => _rejoinEngine.CurrentMessage);
            this.AttachDelegate("RejoinIsExitingPits", () => _rejoinEngine.IsExitingPits);
            this.AttachDelegate("RejoinCurrentPitPhaseName", () => _rejoinEngine.CurrentPitPhase.ToString());
            this.AttachDelegate("RejoinCurrentPitPhase", () => (int)_rejoinEngine.CurrentPitPhase);
            this.AttachDelegate("RejoinAssist_PitExitTime", () => _rejoinEngine.PitExitTimerSeconds);
            this.AttachDelegate("RejoinThreatLevel", () => (int)_rejoinEngine.CurrentThreatLevel);
            this.AttachDelegate("RejoinThreatLevelName", () => _rejoinEngine.CurrentThreatLevel.ToString());
            this.AttachDelegate("RejoinTimeToThreat", () => _rejoinEngine.TimeToThreatSeconds);

            // --- LalaDash Options Delegates ---
            this.AttachDelegate("LalaDashShowLaunchScreen", () => Settings.LalaDashShowLaunchScreen);
            this.AttachDelegate("LalaDashShowPitLimiter", () => Settings.LalaDashShowPitLimiter);
            this.AttachDelegate("LalaDashShowPitScreen", () => Settings.LalaDashShowPitScreen);
            this.AttachDelegate("LalaDashShowRejoinAssist", () => Settings.LalaDashShowRejoinAssist);
            this.AttachDelegate("LalaDashShowVerboseMessaging", () => Settings.LalaDashShowVerboseMessaging);
            this.AttachDelegate("LalaDashShowRaceFlags", () => Settings.LalaDashShowRaceFlags);
            this.AttachDelegate("LalaDashShowRadioMessages", () => Settings.LalaDashShowRadioMessages);
            this.AttachDelegate("LalaDashShowTraffic", () => Settings.LalaDashShowTraffic);

            // --- MsgDash Options Delegates ---
            this.AttachDelegate("MsgDashShowLaunchScreen", () => Settings.MsgDashShowLaunchScreen);
            this.AttachDelegate("MsgDashShowPitLimiter", () => Settings.MsgDashShowPitLimiter);
            this.AttachDelegate("MsgDashShowPitScreen", () => Settings.MsgDashShowPitScreen);
            this.AttachDelegate("MsgDashShowRejoinAssist", () => Settings.MsgDashShowRejoinAssist);
            this.AttachDelegate("MsgDashShowVerboseMessaging", () => Settings.MsgDashShowVerboseMessaging);
            this.AttachDelegate("MsgDashShowRaceFlags", () => Settings.MsgDashShowRaceFlags);
            this.AttachDelegate("MsgDashShowRadioMessages", () => Settings.MsgDashShowRadioMessages);
            this.AttachDelegate("MsgDashShowTraffic", () => Settings.MsgDashShowTraffic);

            // --- Manual Timeout ---
            this.AttachDelegate("ManualTimeoutRemaining", () =>
            {
                // If the manual start time hasn't been set, there's no countdown.
                if (_manualPrimedStartedAt == DateTime.MinValue) return "";

                // The countdown should only be visible while the launch is active.
                if (!IsLaunchActive) return "";

                var remaining = TimeSpan.FromSeconds(30) - (DateTime.Now - _manualPrimedStartedAt);

                // If time has run out, return "0" but don't clear the property
                // until the state changes (handled by the IsLaunchActive check).
                return remaining.TotalSeconds > 0
                    ? remaining.TotalSeconds.ToString("F0")
                    : "0";
            });

           
            // --- DELEGATES FOR LAUNCH CONTROL ---
            this.AttachDelegate("ActualRPMAtClutchRelease", () => _actualRpmAtClutchRelease.ToString("F0"));
            this.AttachDelegate("ActualThrottleAtClutchRelease", () => _actualThrottleAtClutchRelease);
            this.AttachDelegate("AntiStallActive", () => _isAntiStallActive);
            this.AttachDelegate("AntiStallDetectedInLaunch", () => _antiStallDetectedThisRun);
            this.AttachDelegate("AvgSessionLaunchRPM", () => _avgSessionLaunchRPM.ToString("F0"));
            this.AttachDelegate("BitePointInTargetRange", () => _bitePointInTargetRange);
            this.AttachDelegate("BoggedDown", () => _boggedDown);
            this.AttachDelegate("BogDownFactorPercent", () => ActiveProfile.BogDownFactorPercent);
            this.AttachDelegate("ClutchReleaseDelta", () => _clutchReleaseDelta.ToString("F0"));
            this.AttachDelegate("ClutchReleaseTime", () => _hasValidClutchReleaseData ? _clutchReleaseLastTime : 0);
            this.AttachDelegate("LastAvgLaunchRPM", () => _lastAvgSessionLaunchRPM);
            this.AttachDelegate("LastLaunchRPM", () => _lastLaunchRPM);
            this.AttachDelegate("LastMinRPM", () => _lastMinRPMDuringLaunch);
            this.AttachDelegate("LaunchModeActive", () => IsLaunchVisible);
            this.AttachDelegate("LaunchStateLabel", () => _currentLaunchState.ToString());
            this.AttachDelegate("LaunchStateCode", () => ((int)_currentLaunchState).ToString());
            this.AttachDelegate("LaunchRPM", () => _currentLaunchRPM);
            this.AttachDelegate("MaxTractionLoss", () => _maxTractionLossDuringLaunch);
            this.AttachDelegate("MinRPM", () => _minRPMDuringLaunch);
            this.AttachDelegate("OptimalBitePoint", () => ActiveProfile.TargetBitePoint);
            this.AttachDelegate("OptimalBitePointTolerance", () => ActiveProfile.BitePointTolerance);
            this.AttachDelegate("OptimalRPMTolerance", () => ActiveProfile.OptimalRPMTolerance.ToString("F0"));
            this.AttachDelegate("OptimalThrottleTolerance", () => ActiveProfile.OptimalThrottleTolerance.ToString("F0"));
            this.AttachDelegate("ReactionTime", () => _reactionTimeMs);
            this.AttachDelegate("RPMDeviationAtClutchRelease", () => _rpmDeviationAtClutchRelease.ToString("F0"));
            this.AttachDelegate("RPMInTargetRange", () => _rpmInTargetRange);
            this.AttachDelegate("TargetLaunchRPM", () => ActiveProfile.TargetLaunchRPM.ToString("F0"));
            this.AttachDelegate("TargetLaunchThrottle", () => ActiveProfile.TargetLaunchThrottle.ToString("F0"));
            this.AttachDelegate("ThrottleDeviationAtClutchRelease", () => _throttleDeviationAtClutchRelease);
            this.AttachDelegate("ThrottleInTargetRange", () => _throttleInTargetRange);
            this.AttachDelegate("ThrottleModulationDelta", () => _throttleModulationDelta);
            this.AttachDelegate("WheelSpinDetected", () => _wheelSpinDetected);
            this.AttachDelegate("ZeroTo100Delta", () => _zeroTo100Delta);
            this.AttachDelegate("ZeroTo100Time", () => _hasValidLaunchData ? _zeroTo100LastTime : 0);

            // --- ACTIONS FOR USER INPUT ---

            this.AddAction("ClearResults", (a, b) =>
            {
                ResetAllValues();
            });

            this.AddAction("ToggleLaunchMode", (a, b) =>
            {
                if (IsLaunchActive)
                {
                    // If a launch is active (Primed, InProgress, Logging), this is a CANCEL request.
                    SimHub.Logging.Current.Info("LaunchPlugin: Launch mode toggled OFF by user");
                    _launchModeUserDisabled = true; // Keep the 'master off' functionality
                    AbortLaunch();
                }
                else
                {
                    // If a launch is NOT active (Idle, Completed, Cancelled), this is a START request.
                    SimHub.Logging.Current.Info("LaunchPlugin: Launch mode toggled ON by user");
                    _launchModeUserDisabled = false; // Ensure the 'master off' is disabled
                    SetLaunchState(LaunchState.ManualPrimed);
                }
            });

            this.AddAction("MsgCx", (a, b) =>
            {
                // This part handles your other future dash module controls
                _msgCxPressed = true;
                SimHub.Logging.Current.Info("LaunchPlugin: MsgCx was triggered.");
                Task.Delay(2000).ContinueWith(_ => _msgCxPressed = false);

                // --- NEW: Conditional Rejoin Assist Override ---
                // Only start the 30-second cooldown if a rejoin alert is currently active.
                if ((int)_rejoinEngine.CurrentLogicCode >= 100)
                {
                    SimHub.Logging.Current.Info("MsgCx pressed during active rejoin alert, triggering override.");
                    _rejoinEngine.TriggerMsgCxOverride();
                }
            });

            this.AddAction("TogglePitScreen", (plugin, context) =>
            {
                _pitScreenDismissed = !_pitScreenDismissed;
            });

            // --- ACTIONS & DELEGATES FOR FUTURE USE ---
            this.AddAction("PrimaryDashMode", (plugin, context) => { SimHub.Logging.Current.Info($"LalaLaunch: Primary Dash Mode action triggered."); });
            this.AddAction("SecondaryDashMode", (plugin, context) => { SimHub.Logging.Current.Info("LalaLaunch: Secondary Dash Mode action triggered."); });
            //this.AttachDelegate("VirtualClutch", () => _virtualClutch);

            // --- ACTIONS & DELEGATES FOR TESTING AND DEBUGGING ---
            this.AttachDelegate("RejoinAssist_LingerTime", () => _rejoinEngine.LingerTimeSeconds);
            this.AttachDelegate("RejoinAssist_OverrideTime", () => _rejoinEngine.OverrideTimeSeconds);
            this.AttachDelegate("RejoinAssist_DelayTime", () => _rejoinEngine.DelayTimerSeconds);
            this.AttachDelegate("RejoinAlertDetectedCode", () => (int)_rejoinEngine.DetectedReason);
            this.AttachDelegate("RejoinAlertDetectedName", () => _rejoinEngine.DetectedReason.ToString());
            this.AttachDelegate("RejoinThreatDebug", () => _rejoinEngine.ThreatDebug);
            // Debug: compare Rejoin's pit phase vs PitEngine's phase (remove after testing)
            this.AttachDelegate("MSG.PitPhaseDebug", () =>
            {
                if (_rejoinEngine == null || _pit == null) return "";

                var oldPhase = _rejoinEngine.CurrentPitPhase; // Rejoin’s phase (existing)
                var newPhase = _pit.CurrentPitPhase;          // PitEngine’s phase

                // Keep the dash clean when nothing is happening
                if (oldPhase == PitPhase.None && newPhase == PitPhase.None) return "";

                var tPit = _pit.TimeOnPitRoad.TotalSeconds;
                var tStop = _pit.PitStopDuration.TotalSeconds;
                var onPit = _pit.IsOnPitRoad ? "Y" : "N";

                return $"Old:{oldPhase} New:{newPhase} Pit:{onPit} tPit:{tPit:0.0}s tStop:{tStop:0.0}s";
            });

            // --- Link in The Rejoin Assist Engine Code ---
            _rejoinEngine = new RejoinAssistEngine(
                () => ActiveProfile.RejoinWarningMinSpeed,
                () => ActiveProfile.RejoinWarningLingerTime,
                () => ActiveProfile.SpinYawRateThreshold / 10.0 // Divide the setting value by 10 here
            );

            _msgSystem = new MessagingSystem();
            this.AttachDelegate("MSG.OvertakeApproachLine", () => _msgSystem.OvertakeApproachLine);
            this.AttachDelegate("MSG.OvertakeWarnSeconds", () => ActiveProfile.TrafficApproachWarnSeconds);

            _pit = new PitEngine(() =>
            {
                var s = ActiveProfile.RejoinWarningLingerTime;
                if (double.IsNaN(s) || s < 0.5) s = 0.5;
                if (s > 10.0) s = 10.0; // clamp to something sensible
                return s;
            });

            // Hand the PitEngine to Rejoin so it reads pit phases from the single source of truth
            _rejoinEngine.SetPitEngine(_pit);

            // --- Subscribe to the PitEngine's event for saving valid time loss data ---
            _pit.OnValidPitStopTimeLossCalculated += Pit_OnValidPitStopTimeLossCalculated;

            // --- Attach a delegate for the new direct travel time property ---
            this.AttachDelegate("Fuel.LastPitLaneTravelTime", () => LastDirectTravelTime);

        }

        private void Pit_OnValidPitStopTimeLossCalculated(double timeLossSeconds)
        {
            // Basic guards
            if (ActiveProfile == null || string.IsNullOrEmpty(CurrentTrackKey))
            {
                SimHub.Logging.Current.Warn("LalaLaunch: Cannot save pit time loss – no active profile or track.");
                return;
            }

            // Debounce: engine fires twice (Total & Net); only handle once
            var now = DateTime.UtcNow;
            if ((now - _lastPitLossSavedAtUtc).TotalSeconds < 1.0)
                return;

            // Choose a conservative candidate
            // Prefer DTL (lap-delta drive-through loss). If it's null/NaN/≤0, fallback to Direct.
            double direct = _pit?.LastDirectTravelTime ?? 0.0;          // entry→exit while moving
            double total = _pit?.LastTotalPitCycleTimeLoss ?? 0.0;     // Δin + Δout (can include shortcut credit)

            double candidate;
            if (total > 0.0 && total < 180.0)        // accept any positive DTL in a sane window
            {
                candidate = total;                   // primary: race-pace delta
                _pitDbg_CandidateSource = "total";
            }
            else if (direct > 0.0 && direct < 180.0) // fallback
            {
                candidate = direct;
                _pitDbg_CandidateSource = "direct";
            }
            else
            {
                SimHub.Logging.Current.Warn(
                    $"LalaLaunch: Skipping pit-lane loss candidate (total {total:F2}, direct {direct:F2})");
                return;
            }


            // Sanity window (ignore teleport / noise)
            if (candidate < 5.0 || candidate > 180.0)
            {
                SimHub.Logging.Current.Warn($"LalaLaunch: Skipping pit-lane loss candidate {candidate:F2}s (direct {direct:F2}, total {total:F2})");
                return;
            }

            // Skip if we just saved the exact same value recently
            if (!double.IsNaN(_lastPitLossSaved) &&
                Math.Abs(candidate - _lastPitLossSaved) < 0.01 &&
                (now - _lastPitLossSavedAtUtc).TotalSeconds < 10.0)
            {
                return;
            }

            // Round (keep 2 dp so it matches UI/params nicely)
            double rounded = Math.Round(candidate, 2);

            // Persist to the current car/track profile
            var trackRecord = ActiveProfile.EnsureTrack(CurrentTrackKey, CurrentTrackName);
            if (trackRecord != null)
            {
                trackRecord.PitLaneLossSeconds = rounded;
                trackRecord.PitLaneLossSecondsText = rounded.ToString(CultureInfo.InvariantCulture);

                // Live UI update
                if (FuelCalculator != null)
                    FuelCalculator.PitLaneTimeLoss = rounded;

                ProfilesViewModel?.SaveProfiles();
                ProfilesViewModel?.RefreshTracksForSelectedProfile();
                _pitDbg_CandidateSavedSec = rounded;
                _pitDbg_CandidateSource = (Math.Abs(candidate - total) < 0.001) ? "total" : "direct";

                SimHub.Logging.Current.Info(
                    $"LalaLaunch: Saved pit lane loss: {rounded:F2}s (direct {direct:F2}, total {total:F2})");
            }

            // Update debounce memory
            _lastPitLossSaved = rounded;
            _lastPitLossSavedAtUtc = now;
        }


        public void End(PluginManager pluginManager)
        {
            // --- Cleanup trace logger for current run ---
            _telemetryTraceLogger?.DiscardCurrentTrace();

            // Stop trace first
            _telemetryTraceLogger?.EndService();

            // Optionally discard only if you really want to delete last file on exit
            // _telemetryTraceLogger?.DiscardCurrentTrace();

            // Persist settings
            this.SaveCommonSettings("GlobalSettings_V2", Settings);
            ProfilesViewModel.SaveProfiles();

        }

        private void AbortLaunch()
        {
            SetLaunchState(LaunchState.Cancelled);
            ResetCoreLaunchMetrics(); // Call the shared method

            // Abort-specific actions
            _telemetryTraceLogger?.StopLaunchTrace();
            _telemetryTraceLogger?.DiscardCurrentTrace();
        }

        private void ResetAllValues()
        {
            _telemetryTraceLogger?.StopLaunchTrace();
            ResetCoreLaunchMetrics(); // Call the shared method

            // --- Keep the session-specific and last-run resets here ---

            // Last Run Data
            _clutchReleaseLastTime = 0.0;
            _clutchReleaseDelta = 0.0;
            _hasValidClutchReleaseData = false;
            _hasValidLaunchData = false;
            _zeroTo100LastTime = 0.0;
            _zeroTo100Delta = 0.0;
            _lastLaunchRPM = 0.0;
            _lastMinRPMDuringLaunch = 0.0;
            _lastAvgSessionLaunchRPM = 0.0;

            // Session Data
            _sessionLaunchRPMs.Clear();
            _avgSessionLaunchRPM = 0.0;

            // --- Throttle and RPM values not in the core reset ---
            _actualRpmAtClutchRelease = 0.0;
            _rpmDeviationAtClutchRelease = 0.0;
            _rpmInTargetRange = false;
            _actualThrottleAtClutchRelease = 0.0;
            _throttleDeviationAtClutchRelease = 0.0;
            _throttleInTargetRange = false;
            _throttleModulationDelta = 0.0;
            _throttleAtLaunchZoneStart = 0.0;

            // Set the default state
            SetLaunchState(LaunchState.Idle);
            _maxFuelPerLapSession = 0.0;
        }

        private void ResetCoreLaunchMetrics()
        {
            // --- Flags ---
            _isTimingZeroTo100 = false;
            _zeroTo100CompletedThisRun = false;
            _waitingForClutchRelease = false;
            _hasCapturedClutchDropThrottle = false;
            _hasCapturedReactionTime = false;
            _hasLoggedCurrentRun = false;
            _launchSuccessful = false;
            _falseStartDetected = false;
            _boggedDown = false;
            _antiStallDetectedThisRun = false;
            _wasClutchDown = false;

            // --- Timers ---
            _clutchTimer.Stop();
            _clutchTimer.Reset();
            _zeroTo100Stopwatch.Stop();
            _zeroTo100Stopwatch.Reset();
            _reactionTimer.Stop(); // It's good practice to stop all timers
            _reactionTimer.Reset();

            // --- Launch Metrics ---
            _clutchReleaseCurrentRunMs = 0.0;
            _reactionTimeMs = 0.0;
            _manualPrimedStartedAt = DateTime.MinValue;
            _currentLaunchRPM = 0.0;
            _minRPMDuringLaunch = 99999.0;
            _maxTractionLossDuringLaunch = 0.0;
            _wheelSpinDetected = false;
            _minThrottlePostLaunch = 101.0;
            _maxThrottlePostLaunch = -1.0;
        }


        #region Core Update Method

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            // ==== New, Simplified Car & Track Detection ====
            // This is the function that needs to exist for the car model detection below
            string FirstNonEmpty(params object[] vals) => vals.Select(v => Convert.ToString(v)).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";
            try
            {
                string trackKey = Convert.ToString(pluginManager.GetPropertyValue("DataCorePlugin.GameData.TrackCode"));
                string trackDisplay = Convert.ToString(pluginManager.GetPropertyValue("IRacingExtraProperties.iRacing_TrackDisplayName"));

                string carModel = "Unknown";
                var carModelProbe = FirstNonEmpty(
                    pluginManager.GetPropertyValue("DataCorePlugin.GameData.CarModel"),
                    pluginManager.GetPropertyValue("IRacingExtraProperties.iRacing_CarModel")
                );
                if (!string.IsNullOrWhiteSpace(carModelProbe)) { carModel = carModelProbe; }

                if (!string.IsNullOrWhiteSpace(trackKey))
                {
                    if (string.IsNullOrWhiteSpace(trackDisplay)) { trackDisplay = Convert.ToString(pluginManager.GetPropertyValue("DataCorePlugin.GameData.TrackName")); }
                    CurrentCarModel = carModel;
                    CurrentTrackKey = trackKey;
                    CurrentTrackName = trackDisplay;
                }
            }
            catch (Exception ex) { SimHub.Logging.Current.Warn($"[LalaLaunch] Simplified Car/Track probe failed: {ex.Message}"); }

            // --- MASTER GUARD CLAUSES ---
            if (Settings == null) return;
            if (!data.GameRunning || data.NewData == null) return;

            long currentSessionId = Convert.ToInt64(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.WeekendInfo.SessionID") ?? -1);
            if (currentSessionId != _lastSessionId)
            {
                _rejoinEngine.Reset();
                _pit.Reset();
                _currentCarModel = "Unknown";
                _lastSessionId = currentSessionId;
                FuelCalculator.ForceProfileDataReload();
                SimHub.Logging.Current.Info($"[LalaLaunch] Session start snapshot: Car='{CurrentCarModel}'  Track='{CurrentTrackName}'");
            }

            // --- Pit System Monitoring (needs tick granularity for phase detection) ---
            _pit.Update(data, pluginManager);

            // --- 250ms group: things safe to refresh at ~4 Hz ---
            if (_poll250ms.ElapsedMilliseconds >= 250)
            {
                _poll250ms.Restart();
                double baseMaxFuel = Convert.ToDouble(pluginManager.GetPropertyValue("DataCorePlugin.GameData.MaxFuel") ?? 0.0);
                double bopPercent = Convert.ToDouble(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.DriverInfo.DriverCarMaxFuelPct") ?? 1.0);
                if (bopPercent <= 0) { bopPercent = 1.0; }
                double maxFuel = baseMaxFuel * bopPercent;
                LiveCarMaxFuel = maxFuel;
                if (Math.Abs(LiveCarMaxFuel - _lastAnnouncedMaxFuel) > 0.01)
                {
                    _lastAnnouncedMaxFuel = LiveCarMaxFuel;
                    FuelCalculator.UpdateLiveDisplay(LiveCarMaxFuel);
                }
                _msgSystem.Enabled = Settings.MsgDashShowTraffic || Settings.LalaDashShowTraffic;
                double warn = ActiveProfile.TrafficApproachWarnSeconds;
                if (!(warn > 0)) warn = 5.0;
                _msgSystem.WarnSeconds = warn;
                if (_msgSystem.Enabled)
                    _msgSystem.Update(data, pluginManager);
            }

            // --- Launch State helpers (need tick-level responsiveness) ---
            bool isOnTrack = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.IsOnTrack") ?? false);
            if (!isOnTrack && !IsIdle)
            {
                SimHub.Logging.Current.Info("LaunchPlugin: Off track or in pits, aborting launch state to Idle.");
                AbortLaunch();
            }
            double clutchRaw = Convert.ToDouble(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.ClutchRaw") ?? 0.0);
            _paddleClutch = 100.0 - (clutchRaw * 100.0);

            // --- 500ms group: identity polling & session-change handling ---
            if (_poll500ms.ElapsedMilliseconds >= 500)
            {
                _poll500ms.Restart();
                UpdateLiveFuelCalcs(data);

                var currentBestLap = data.NewData?.BestLapTime ?? TimeSpan.Zero;
                if (currentBestLap > TimeSpan.Zero && currentBestLap != _lastSeenBestLap)
                {
                    _lastSeenBestLap = currentBestLap;
                    FuelCalculator?.SetPersonalBestSeconds(currentBestLap.TotalSeconds);

                    int lapMs = (int)currentBestLap.TotalMilliseconds;
                    bool accepted = ProfilesViewModel.TryUpdatePB(CurrentCarModel, CurrentTrackKey, lapMs);
                    SimHub.Logging.Current.Info($"[PB] candidate={lapMs}ms car='{CurrentCarModel}' trackKey='{CurrentTrackKey}' -> {(accepted ? "accepted" : "rejected")}");
                }

                // =========================================================================
                // ======================= MODIFIED BLOCK START ============================
                // This new logic performs the auto-selection ONLY ONCE per session change.
                // =========================================================================

                // Check if the currently detected car/track is different from the one we last auto-selected.
                // ---- THIS IS THE FINAL, CORRECTED LOGIC ----
                if (!string.IsNullOrEmpty(CurrentCarModel) && CurrentCarModel != "Unknown" && !string.IsNullOrEmpty(CurrentTrackKey) &&
                    (CurrentCarModel != _lastSeenCar || CurrentTrackKey != _lastSeenTrack))
                {
                    // It's a new combo, so we'll perform the auto-selection.
                    SimHub.Logging.Current.Info($"[LalaLaunch] New live combo detected. Auto-selecting profile for Car='{CurrentCarModel}', Track='{CurrentTrackName}'.");

                    // Store this combo's KEY so we don't trigger again for the same session.
                    _lastSeenCar = CurrentCarModel;
                    _lastSeenTrack = CurrentTrackKey; // <-- This now correctly stores the key

                    // Dispatch UI updates to the main thread.
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var profileToLoad = ProfilesViewModel.GetProfileForCar(CurrentCarModel) ?? ProfilesViewModel.EnsureCar(CurrentCarModel);
                        this.ActiveProfile = profileToLoad;

                        // Ensure the track exists via the Profiles VM (this triggers UI refresh + selection)
                        if (!string.IsNullOrWhiteSpace(CurrentTrackKey))
                        {
                            SimHub.Logging.Current.Info($"[LalaLaunch] EnsureCarTrack hook -> car='{CurrentCarModel}', trackKey='{CurrentTrackKey}'");
                            ProfilesViewModel.EnsureCarTrack(CurrentCarModel, CurrentTrackKey);
                        }
                        else
                        {
                            SimHub.Logging.Current.Info("[LalaLaunch] Skipped EnsureCarTrack: live track key is empty/unknown.");
                        }

                        FuelCalculator.SetLiveSession(CurrentCarModel, CurrentTrackName);
                    });

                }
                // =======================================================================
                // ======================= MODIFIED BLOCK END ============================
                // =======================================================================
            }

            UpdateLiveProperties(pluginManager, ref data);
            HandleLaunchState(pluginManager, ref data);

            if (IsInProgress || IsLogging)
            {
                double clutch = data.NewData?.Clutch ?? 0;
                double throttle = data.NewData?.Throttle ?? 0;
                if (!IsLogging && clutch < 98 && throttle >= 10)
                {
                    SetLaunchState(LaunchState.Logging);
                }
                if (IsLogging)
                {
                    _telemetryTraceLogger.Update(data);
                }
                ExecuteLaunchTimers(pluginManager, ref data);
            }

            string currentSession = data.NewData?.SessionTypeName ?? "";
            if (Settings.EnableAutoDashSwitch && isOnTrack && !_hasProcessedOnTrack)
            {
                _hasProcessedOnTrack = true;
                _lastSessionType = currentSession;
                SimHub.Logging.Current.Info("LaunchPlugin: New session on-track activity detected. Resetting all values.");
                ResetAllValues();
                Task.Run(async () =>
                {
                    string pageToShow = "practice";
                    switch (_lastSessionType)
                    {
                        case "Offline Testing": pageToShow = "practice"; break;
                        case "Open Qualify":
                        case "Lone Qualify":
                        case "Qualifying":
                        case "Warmup": pageToShow = "timing"; break;
                        case "Race": pageToShow = "racing"; break;
                    }
                    Screens.Mode = "auto";
                    Screens.CurrentPage = pageToShow;
                    SimHub.Logging.Current.Info($"OnTrack detected. Mode set to 'auto', page set to '{Screens.CurrentPage}'.");
                    await Task.Delay(2000);
                    Screens.Mode = "manual";
                    SimHub.Logging.Current.Info("Auto mode timer expired. Mode set to 'manual'.");
                });
            }
            else if (!isOnTrack && _hasProcessedOnTrack)
            {
                _hasProcessedOnTrack = false;
            }
            bool isOnPitRoad = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.IsOnPitRoad") ?? false);
            if (isOnPitRoad)
            {
                if (!_pittingTimer.IsRunning)
                {
                    _pittingTimer.Restart();
                }
                if (_pittingTimer.Elapsed.TotalMilliseconds > 200)
                {
                    _pitScreenActive = !_pitScreenDismissed;
                }
            }
            else
            {
                _pitScreenActive = false;
                _pitScreenDismissed = false;
                if (_pittingTimer.IsRunning)
                {
                    _pittingTimer.Stop();
                    _pittingTimer.Reset();
                }
            }
        }
        #endregion

        #region Private Helper Methods for DataUpdate

        private static string GetString(object o) => Convert.ToString(o, CultureInfo.InvariantCulture) ?? "";

        private string DetectCarModel(GameData data, PluginManager pm)
        {
            // 1) SimHub’s high-level string (when available)
            var s = data?.NewData?.CarModel;
            if (!string.IsNullOrWhiteSpace(s) && !string.Equals(s, "Unknown", StringComparison.OrdinalIgnoreCase))
                return s;

            // 2) iRacing DriverInfo fallbacks exposed by SimHub’s raw telemetry
            //    (different installs expose slightly different names; try a few)
            var c = GetString(pm.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.DriverInfo.DriverCarScreenName"));

            if (!string.IsNullOrWhiteSpace(c)) return c;

            c = GetString(pm.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.DriverInfo.DriverCarShortName"));
            if (!string.IsNullOrWhiteSpace(c)) return c;

            c = GetString(pm.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.DriverInfo.DriverCarSLShortName"));
            if (!string.IsNullOrWhiteSpace(c)) return c;

            // 3) As a last resort, keep it stable but explicit
            return "Unknown";
        }
                
        /// Updates properties that need to be checked on every tick, like dash switching and anti-stall.
        private void UpdateLiveProperties(PluginManager pluginManager, ref GameData data)
        {

            if (IsCompleted && (DateTime.Now - _launchEndTime).TotalSeconds > Settings.ResultsDisplayTime)
            {
                SetLaunchState(LaunchState.Idle);
            }

            // --- Manual Launch Timeout Logic ---
            // Check if a manual launch was started AND we are still in the pre-launch waiting phase.
            if (_manualPrimedStartedAt != DateTime.MinValue && (IsManualPrimed || IsInProgress))
            {
                // Check if more than 30 seconds have passed.
                if ((DateTime.Now - _manualPrimedStartedAt).TotalSeconds > 30)
                {
                    SimHub.Logging.Current.Info("LaunchPlugin: Manual launch timed out after 30 seconds.");
                    AbortLaunch();
                }
            }


            // --- START PHASE FLAGS ---
            bool isStartReady = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.SessionFlagsDetails.IsStartReady") ?? false);
            bool isStartGo = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.SessionFlagsDetails.IsStartGo") ?? false);
            double speed = data.NewData?.SpeedKmh ?? 0;

            // --- FALSE START DETECTION ---
            // Lights are on (ready), but not "Go" yet — and the car moves with clutch released
            if (isStartReady && !isStartGo)
            {
                if (speed > 1.0 && _paddleClutch < 90.0)
                {
                    _falseStartDetected = true;
                }
            }

            // --- ANTI-STALL LIVE CHECK ---
            double gameClutch = data.NewData?.Clutch ?? 0;
            _isAntiStallActive = (gameClutch > _paddleClutch + ActiveProfile.AntiStallThreshold);

            // --- BITE POINT INDICATOR ---
            if (IsLaunchVisible)
            {
                _bitePointInTargetRange = Math.Abs(_paddleClutch - ActiveProfile.TargetBitePoint) <= ActiveProfile.BitePointTolerance;
            }
            else
            {
                _bitePointInTargetRange = false;
            }
        }

        /// Manages the activation and deactivation of a launch sequence.
        private void HandleLaunchState(PluginManager pluginManager, ref GameData data)
        {
            double speed = data.NewData?.SpeedKmh ?? 0;
            bool isStartReady = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.SessionFlagsDetails.IsStartReady") ?? false);

            // --- ACTIVATION CONDITIONS ---
            bool isStandingStart = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.WeekendInfo.WeekendOptions.StandingStart") ?? false);
            bool isRaceSession = (data.NewData?.SessionTypeName ?? "") == "Race";

            bool isAutoStartCondition = isStartReady && isStandingStart && isRaceSession && speed < 1;
            bool isManualPrimed = IsManualPrimed && speed < 2;

            // --- USER OVERRIDE ---
            if (_launchModeUserDisabled)
            {
                // User has toggled Launch Mode OFF — block any auto or manual activation
                return;
            }


            // Assign LaunchState based on trigger condition
            if (isManualPrimed)
            {
                SetLaunchState(LaunchState.ManualPrimed);
            }
            else if (isAutoStartCondition && !IsLaunchActive)
            {
                SetLaunchState(LaunchState.AutoPrimed);
            }


            bool isLaunchConditionMet = (isAutoStartCondition || isManualPrimed);

            if (isLaunchConditionMet && !IsInProgress)
            {
                SetLaunchState(LaunchState.InProgress);
                ResetForNewLaunch(data);
            }

            // --- DEACTIVATION CONDITIONS ---
            bool isDeactivationConditionMet =
                speed >= 150 ||
                (!isStartReady && speed < 5 && _zeroTo100Stopwatch.ElapsedMilliseconds > 2000);

            if ((IsInProgress || IsLogging) && isDeactivationConditionMet)
            {
                AbortLaunch();
            }
        }


        /// This is the private helper method you created in the previous step.
        /// It resets all variables to their default states for a new launch attempt.
        /// </summary>
        private void ResetForNewLaunch(GameData data)
        {
            // --- Activation flags ---
            _waitingForClutchRelease = true;
            _falseStartDetected = false;
            _launchSuccessful = false;
            _hasLoggedCurrentRun = false;
            _hasCapturedLaunchRPMForRun = false;
            _hasCapturedClutchDropThrottle = false;
            _hasCapturedReactionTime = false;
            _antiStallDetectedThisRun = false;

            // --- Logging / trace setup ---
            _currentLaunchTraceFilenameForSummary = "Telemetry Disabled";
            if (Settings.EnableTelemetryTracing)
            {
                _currentLaunchTraceFilenameForSummary = _telemetryTraceLogger.StartLaunchTrace(
                    data.NewData?.CarModel ?? "N/A",
                    data.NewData?.TrackName ?? "N/A"
                );
            }

            // --- Clutch state ---
            _clutchTimer.Reset();
            _wasClutchDown = false;
            _clutchReleaseCurrentRunMs = 0.0;

            // --- Launch timers ---
            _isTimingZeroTo100 = false;
            _zeroTo100CompletedThisRun = false;
            _zeroTo100Stopwatch.Reset();
            _reactionTimer.Reset();
            _reactionTimeMs = 0.0;

            // --- RPM analysis ---
            _currentLaunchRPM = 0.0;
            _minRPMDuringLaunch = 99999.0;
            _actualRpmAtClutchRelease = 0.0;
            _rpmDeviationAtClutchRelease = 0.0;
            _rpmInTargetRange = false;

            // --- Throttle tracking ---
            _actualThrottleAtClutchRelease = 0.0;
            _throttleDeviationAtClutchRelease = 0.0;
            _throttleInTargetRange = false;
            _throttleModulationDelta = 0.0;
            _minThrottlePostLaunch = 101.0;
            _maxThrottlePostLaunch = -1.0;

            // --- Traction / bogging ---
            _wheelSpinDetected = false;
            _maxTractionLossDuringLaunch = 0.0;
            _boggedDown = false;
        }

        /// Contains the core logic for timing the clutch release, 0-100 acceleration,
        /// and logging the final summary data.
        private void ExecuteLaunchTimers(PluginManager pluginManager, ref GameData data)
        {
            double clutch = data.NewData?.Clutch ?? 0;
            double speed = data.NewData?.SpeedKmh ?? 0;
            double engineRpm = data.NewData?.Rpms ?? 0;
            double throttle = data.NewData?.Throttle ?? 0;

            // --- REACTION TIME CAPTURE ---
            bool isStartGo = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.SessionFlagsDetails.IsStartGo") ?? false);

            // Only start the timer if the "Go" signal is on AND we haven't already captured the time for this run.
            if (isStartGo && !_reactionTimer.IsRunning && !_hasCapturedReactionTime)
            {
                _reactionTimer.Restart();
            }
            else if (!_hasCapturedReactionTime && _reactionTimer.IsRunning && (speed > 0.2 && _paddleClutch < 95.0))
            {
                _reactionTimer.Stop();
                _reactionTimeMs = _reactionTimer.Elapsed.TotalMilliseconds;
                _hasCapturedReactionTime = true; // Set the flag to true AFTER capturing
            }

            // --- CLUTCH RELEASE TIMING ---
            if (_waitingForClutchRelease)
            {
                if (clutch >= 98.0 && !_wasClutchDown)
                {
                    _wasClutchDown = true;
                    _clutchTimer.Reset();
                }
                else if (_wasClutchDown && clutch < 99.0 && clutch > 5.0)
                {
                    if (!_clutchTimer.IsRunning)
                    {
                        _clutchTimer.Start();

                        if (!_hasCapturedClutchDropThrottle)
                        {
                            _throttleAtLaunchZoneStart = throttle;
                            _hasCapturedClutchDropThrottle = true;
                        }
                    }

                }
                else if (_clutchTimer.IsRunning && clutch <= 5.0)
                {
                    _clutchTimer.Stop();
                    _clutchReleaseCurrentRunMs = _clutchTimer.Elapsed.TotalMilliseconds;

                    if (_clutchReleaseCurrentRunMs >= 10)
                    {
                        _clutchReleaseDelta = (_clutchReleaseLastTime > 0)
                            ? _clutchReleaseCurrentRunMs - _clutchReleaseLastTime
                            : 0;

                        _clutchReleaseLastTime = _clutchReleaseCurrentRunMs;
                        _hasValidClutchReleaseData = true;

                        _actualThrottleAtClutchRelease = throttle;
                        _throttleDeviationAtClutchRelease = Math.Abs(_actualThrottleAtClutchRelease - ActiveProfile.TargetLaunchThrottle);
                        _throttleInTargetRange = _throttleDeviationAtClutchRelease <= ActiveProfile.OptimalThrottleTolerance;
                    }

                    _actualRpmAtClutchRelease = engineRpm;
                    _rpmDeviationAtClutchRelease = _actualRpmAtClutchRelease - ActiveProfile.TargetLaunchRPM;
                    _rpmInTargetRange = Math.Abs(_rpmDeviationAtClutchRelease) <= ActiveProfile.OptimalRPMTolerance;

                    _waitingForClutchRelease = false;
                    _wasClutchDown = false;
                }
            }

            // --- 0-100 KM/H TIMING START ---
            if (speed > 0.2 && !_isTimingZeroTo100 && !_zeroTo100CompletedThisRun)
            {
                _zeroTo100Stopwatch.Restart();
                _isTimingZeroTo100 = true;

                if (!_hasCapturedLaunchRPMForRun)
                {
                    _currentLaunchRPM = engineRpm;
                    _hasCapturedLaunchRPMForRun = true;
                }

                _minThrottlePostLaunch = throttle;
                _maxThrottlePostLaunch = throttle;
            }

            // --- 0-100 KM/H TIMING IN PROGRESS ---
            if (_isTimingZeroTo100)
            {
                // --- Update throttle and RPM tracking ---
                if (throttle < _minThrottlePostLaunch) _minThrottlePostLaunch = throttle;
                if (throttle > _maxThrottlePostLaunch) _maxThrottlePostLaunch = throttle;
                if (engineRpm < _minRPMDuringLaunch) _minRPMDuringLaunch = engineRpm;

                // --- Check traction loss ---
                double tractionLoss = Convert.ToDouble(pluginManager.GetPropertyValue("ShakeITMotorsV3Plugin.Export.TractionLoss.All") ?? 0.0);
                if (tractionLoss > _maxTractionLossDuringLaunch)
                {
                    _maxTractionLossDuringLaunch = tractionLoss;
                }

                // --- Detect Anti-Stall ---
                if (!_antiStallDetectedThisRun)
                {
                    if (clutch > _paddleClutch + ActiveProfile.AntiStallThreshold)
                    {
                        _antiStallDetectedThisRun = true;
                    }
                }

                // --- 0-100 KM/H TIMING COMPLETE ---
                if (speed >= 100 && !_zeroTo100CompletedThisRun)
                {
                    _zeroTo100Stopwatch.Stop();
                    double ms = _zeroTo100Stopwatch.Elapsed.TotalMilliseconds;

                    _zeroTo100Delta = (_zeroTo100LastTime > 0) ? ms - _zeroTo100LastTime : 0;
                    _zeroTo100LastTime = ms;
                    _hasValidLaunchData = true;
                    _zeroTo100CompletedThisRun = true;

                    _launchSuccessful = _hasValidClutchReleaseData && _zeroTo100CompletedThisRun;
                    _sessionLaunchRPMs.Add(_currentLaunchRPM);
                    _avgSessionLaunchRPM = _sessionLaunchRPMs.Average();

                    _lastLaunchRPM = _currentLaunchRPM;
                    _lastMinRPMDuringLaunch = _minRPMDuringLaunch;
                    _lastAvgSessionLaunchRPM = _avgSessionLaunchRPM;

                    _boggedDown = _minRPMDuringLaunch < (_currentLaunchRPM * (ActiveProfile.BogDownFactorPercent / 100.0));
                    _throttleModulationDelta = _maxThrottlePostLaunch - _minThrottlePostLaunch;
                    _wheelSpinDetected = _maxTractionLossDuringLaunch > 0.3;

                    SetLaunchState(LaunchState.Completed);
                    _launchEndTime = DateTime.Now;
                    LogLaunchSummary(pluginManager, ref data);
                }

                // --- ABORT: CAR STOPPED AFTER START ---
                else if (speed < 1 && _zeroTo100Stopwatch.Elapsed.Milliseconds > 1000)
                {
                    _isTimingZeroTo100 = false;
                    _zeroTo100Stopwatch.Stop();
                    _zeroTo100Stopwatch.Reset();

                    _hasValidLaunchData = false;
                    _zeroTo100CompletedThisRun = false;
                    SetLaunchState(LaunchState.Cancelled);

                    if (_telemetryTraceLogger != null && Settings.EnableTelemetryTracing)
                    {
                        _telemetryTraceLogger.StopLaunchTrace();
                        _telemetryTraceLogger.DiscardCurrentTrace();
                    }
                }
            }
        }

        /// Creates the summary object and writes the launch data to the CSV log file.
        private void LogLaunchSummary(PluginManager pluginManager, ref GameData data)
        {
            if (!_launchSuccessful)
            {
                if (_telemetryTraceLogger != null && Settings.EnableTelemetryTracing)
                {
                    _telemetryTraceLogger.StopLaunchTrace();
                    _telemetryTraceLogger.DiscardCurrentTrace();
                }
                return;
            }

            if (Settings.EnableCsvLogging && !_hasLoggedCurrentRun)
            {
                try
                {
                    var summary = new ParsedSummary
                    {
                        TimestampUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                        Car = data.NewData?.CarModel ?? "N/A",
                        Session = data.NewData?.SessionTypeName ?? "N/A",
                        Track = data.NewData?.TrackName ?? "N/A",
                        Humidity = ((double)(pluginManager.GetPropertyValue("DataCorePlugin.GameData.Humidity") ?? 0.0)).ToString("F1"),
                        AirTemp = (data.NewData?.AirTemperature ?? 0.0).ToString("F1"),
                        TrackTemp = (data.NewData?.RoadTemperature ?? 0.0).ToString("F1"),
                        Fuel = (data.NewData?.FuelPercent ?? 0.0).ToString("F1"),
                        SurfaceGrip = pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.CurrentSessionInfo.SessionTrackRubberState")?.ToString() ?? "Unknown",
                        TargetBitePoint = ((float)ActiveProfile.TargetBitePoint).ToString("F0"),
                        ClutchReleaseTime = _clutchReleaseLastTime.ToString("F0"),
                        ClutchDelta = _clutchReleaseDelta.ToString("F0"),
                        AccelTime100Ms = _zeroTo100LastTime.ToString("F0"),
                        AccelDeltaLast = _zeroTo100Delta.ToString("F3"),
                        LaunchOk = _hasValidLaunchData.ToString(),
                        Bogged = _boggedDown.ToString(),
                        AntiStallDetected = _antiStallDetectedThisRun.ToString(),
                        WheelSpin = _wheelSpinDetected.ToString(),
                        LaunchRpm = _currentLaunchRPM.ToString("F0"),
                        MinRpm = _minRPMDuringLaunch.ToString("F0"),
                        ReleaseRpm = _actualRpmAtClutchRelease.ToString("F0"),
                        RpmDeltaToOptimal = _rpmDeviationAtClutchRelease.ToString("F0"),
                        RpmUseOk = _rpmInTargetRange.ToString(),
                        ThrottleAtClutchRelease = _actualThrottleAtClutchRelease.ToString("F0"),
                        ThrottleAtLaunchZoneStart = _throttleAtLaunchZoneStart.ToString("F0"),
                        ThrottleDeltaToOptimal = _throttleDeviationAtClutchRelease.ToString("F0"),
                        ThrottleModulationDelta = _throttleModulationDelta.ToString("F0"),
                        ThrottleUseOk = _throttleInTargetRange.ToString(),
                        TractionLossRaw = _maxTractionLossDuringLaunch.ToString("F2"),
                        ReactionTimeMs = _reactionTimeMs.ToString("F0"),
                        LaunchTraceFile = _currentLaunchTraceFilenameForSummary
                    };

                    string summaryLine = summary.GetSummaryForCsvLine();

                    // --- Log to trace (if enabled) ---
                    _telemetryTraceLogger?.StopLaunchTrace();
                    _telemetryTraceLogger?.AppendLaunchSummaryToTrace(summaryLine);

                    // --- Write to CSV file ---
                    string folder = string.IsNullOrWhiteSpace(Settings.CsvLogPath)
                        ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "LaunchData")
                        : Settings.CsvLogPath.Trim();

                    Directory.CreateDirectory(folder);
                    string filename = Path.Combine(folder, $"launch_{DateTime.Now:yyyy-MM-dd}.csv");

                    if (!File.Exists(filename))
                    {
                        File.WriteAllText(filename, summary.GetCsvHeaderLine() + Environment.NewLine);
                    }

                    File.AppendAllText(filename, summaryLine + Environment.NewLine);
                    _hasLoggedCurrentRun = true;
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Error($"LaunchPlugin: CSV Logging Error: {ex.Message}");
                }
            }
        }

        #endregion

        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new LaunchPluginCombinedSettingsControl(this, _telemetryTraceLogger);
        }
    }

    public class LaunchPluginSettings : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }

        // --- Global Settings with Corrected Defaults ---
        public bool EnableDebugLogging { get; set; } = false;
        public double ResultsDisplayTime { get; set; } = 5.0; // Corrected to 5 seconds
        public bool EnableAutoDashSwitch { get; set; } = true;
        public bool EnableCsvLogging { get; set; } = true;
        public string CsvLogPath { get; set; } = "";
        public string TraceLogPath { get; set; } = "";
        public bool EnableTelemetryTracing { get; set; } = true;

        // --- LalaDash Toggles (Default ON) ---
        public bool LalaDashShowLaunchScreen { get; set; } = true;
        public bool LalaDashShowPitLimiter { get; set; } = true;
        public bool LalaDashShowPitScreen { get; set; } = true;
        public bool LalaDashShowRejoinAssist { get; set; } = true;
        public bool LalaDashShowVerboseMessaging { get; set; } = true;
        public bool LalaDashShowRaceFlags { get; set; } = true;
        public bool LalaDashShowRadioMessages { get; set; } = true;
        public bool LalaDashShowTraffic { get; set; } = true;

        // --- Message System Toggles (Default ON) ---
        public bool MsgDashShowLaunchScreen { get; set; } = true;
        public bool MsgDashShowPitLimiter { get; set; } = true;
        public bool MsgDashShowPitScreen { get; set; } = true;
        public bool MsgDashShowRejoinAssist { get; set; } = true;
        public bool MsgDashShowVerboseMessaging { get; set; } = true;
        public bool MsgDashShowRaceFlags { get; set; } = true;
        public bool MsgDashShowRadioMessages { get; set; } = true;
        public bool MsgDashShowTraffic { get; set; } = true;
    }
    /// <summary>
    /// Helper class for continuous telemetry data logging, now specifically focused on per-launch traces.
    /// This class is instantiated and managed by the main LaunchPlugin.
    /// </summary>
    public class TelemetryTraceLogger
    {
        private StreamWriter _traceWriter;
        private string _currentFilePath;
        private DateTime _traceStartTime;
        private readonly LaunchPlugin.LalaLaunch _plugin;

        public void DiscardCurrentTrace()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_currentFilePath) && File.Exists(_currentFilePath))
                {
                    File.Delete(_currentFilePath);
                    SimHub.Logging.Current.Info($"LaunchPlugin: Discarded trace file: {_currentFilePath}");
                }
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"LaunchPlugin: Failed to discard trace file: {ex.Message}");
            }
            finally
            {
                _currentFilePath = null;
            }
        }

        public TelemetryTraceLogger(LaunchPlugin.LalaLaunch plugin)
        {
            _plugin = plugin;
        }

        /// <summary>
        /// Starts a new telemetry trace file.
        /// </summary>
        /// <param name="carModel">The current car model.</param>
        /// <param name="trackName">The current track name.</param>
        /// <returns>The full path to the created trace file.</returns>
        public string StartLaunchTrace(string carModel, string trackName)
        {
            StopLaunchTrace(); // Ensure any previous trace is stopped and file is closed

            string folder = GetCurrentTracePath();
            System.IO.Directory.CreateDirectory(folder); // Ensure the directory exists

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string safeCarModel = SanitizeFileName(carModel);
            string safeTrackName = SanitizeFileName(trackName);

            // Construct the path but don't assign it to the class field yet
            string newFilePath = System.IO.Path.Combine(folder, $"LaunchTrace_{safeCarModel}_{safeTrackName}_{timestamp}.csv");

            try
            {
                // Open the file for writing. Using FileMode.Create ensures a new file is created.
                _traceWriter = new StreamWriter(newFilePath, false); // 'false' for overwrite/create new
                _traceWriter.WriteLine("Timestamp (UTC),Speed (Kmh),GameClutch (%),PaddleClutch (%),Throttle (%),RPMs,AccelerationSurge (G),TractionLoss (ShakeIT)");
                _traceWriter.Flush(); // Ensure header is written immediately

                // --- CRITICAL: Only assign the file path and start time AFTER the file is successfully opened ---
                _currentFilePath = newFilePath;
                _traceStartTime = DateTime.UtcNow;

                SimHub.Logging.Current.Info($"TelemetryTraceLogger: New launch trace file opened: {_currentFilePath}");
                return _currentFilePath;
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"TelemetryTraceLogger: Failed to start new launch trace: {ex.Message}");
                // Ensure state is clean on failure
                _traceWriter = null;
                _currentFilePath = null;
                return "Error_TraceFile";
            }
        }

        /// <summary>
        /// Gets the default path for launch trace files.
        /// </summary>
        /// <returns>The default path.</returns>

        // Renamed from GetDefaultLaunchTracePath
        public string GetCurrentTracePath()
        {
            // Check if a custom path is set in the settings
            if (!string.IsNullOrWhiteSpace(_plugin.Settings.TraceLogPath))
            {
                // Use the custom path
                return _plugin.Settings.TraceLogPath.Trim();
            }
            else
            {
                // Fall back to the default path
                string pluginInstallPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                return Path.Combine(pluginInstallPath, "Logs", "LaunchData", "LaunchTraces");
            }
        }


        /// <summary>
        /// Appends telemetry data to the current trace file if active.
        /// </summary>
        /// <param name="data">The game data.</param>
        public void Update(GameData data)
        {
            if (_plugin.Settings.EnableTelemetryTracing && _traceWriter != null && _traceWriter.BaseStream.CanWrite)
            {
                try
                {
                    // Calculate time elapsed from the start of the trace
                    double timeElapsed = (DateTime.UtcNow - _traceStartTime).TotalSeconds;

                    double speedKmh = data.NewData?.SpeedKmh ?? 0;
                    double gameClutch = data.NewData?.Clutch ?? 0; // This is the value affected by anti-stall
                    double clutchRaw = Convert.ToDouble(_plugin.PluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.ClutchRaw") ?? 0.0);
                    double paddleClutch = 100.0 - (clutchRaw * 100.0); // This is the pure paddle input, converted to the same scale
                    double throttle = data.NewData?.Throttle ?? 0;
                    double rpms = data.NewData?.Rpms ?? 0;
                    double accelSurge = data.NewData?.AccelerationSurge ?? 0;
                    double tractionLoss = Convert.ToDouble(_plugin.PluginManager.GetPropertyValue("ShakeITMotorsV3Plugin.Export.TractionLoss.All") ?? 0.0);

                    // Format line using InvariantCulture to ensure consistent decimal separators (dots)
                    string line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},{speedKmh.ToString("F2", CultureInfo.InvariantCulture)},{gameClutch.ToString("F1", CultureInfo.InvariantCulture)},{paddleClutch.ToString("F1", CultureInfo.InvariantCulture)},{throttle.ToString("F1", CultureInfo.InvariantCulture)},{rpms.ToString("F0", CultureInfo.InvariantCulture)},{accelSurge.ToString("F3", CultureInfo.InvariantCulture)},{tractionLoss.ToString("F2", CultureInfo.InvariantCulture)}";
                    _traceWriter.WriteLine(line);
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Error($"TelemetryTraceLogger: Failed to write telemetry data: {ex.Message}");
                }
            }
            else if (!_plugin.Settings.EnableTelemetryTracing)
            {
                SimHub.Logging.Current.Debug("TelemetryTraceLogger: Skipping trace logging — disabled in plugin settings.");
            }
        }

        /// <summary>
        /// Appends the launch summary to the trace file.
        /// This method *must* be called after the main telemetry logging has been stopped (i.e., after StopLaunchTrace() has closed _traceWriter).
        /// It will use File.AppendAllText directly to avoid file locking issues.
        /// </summary>
        /// <param name="summaryLine">The formatted summary CSV line.</param>
        public void AppendLaunchSummaryToTrace(string summaryLine)
        {
            if (string.IsNullOrEmpty(_currentFilePath) || !System.IO.File.Exists(_currentFilePath))
            {
                SimHub.Logging.Current.Warn("TelemetryTraceLogger: Cannot append summary. Trace file path is invalid or file does not exist.");
                return;
            }

            try
            {
                // Define the summary section markers and content
                List<string> summaryContent = new List<string>
                {
                    Environment.NewLine, // Add a blank line for separation
                    "[LaunchSummaryHeader]",
                    new ParsedSummary().GetCsvHeaderLine(), // Get the header from ParsedSummary
                    "[LaunchSummary]",
                    summaryLine
                };

                // Append all lines at once using File.AppendAllLines to ensure atomicity
                System.IO.File.AppendAllLines(_currentFilePath, summaryContent);
                SimHub.Logging.Current.Info($"TelemetryTraceLogger: Successfully appended launch summary to {_currentFilePath}");
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"TelemetryTraceLogger: Failed to append launch summary using File.AppendAllLines: {ex.Message}");
            }
        }


        /// <summary>
        /// Stops the current telemetry trace, flushes and closes the file.
        /// </summary>
        public void StopLaunchTrace()
        {
            if (_traceWriter != null)
            {
                try
                {
                    _traceWriter.Flush();
                    _traceWriter.Dispose(); // This closes the underlying file stream and disposes the writer
                    SimHub.Logging.Current.Info($"TelemetryTraceLogger: Launch trace file closed: {_currentFilePath}");
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Error($"TelemetryTraceLogger: Error stopping launch trace: {ex.Message}");
                }
                finally
                {
                    _traceWriter = null; // Set to null to indicate it's closed
                }
            }
        }

        /// <summary>
        /// Called when the plugin is ending. Ensures the trace writer is closed.
        /// </summary>
        public void EndService()
        {

            StopLaunchTrace(); // Ensure the file is closed on plugin shutdown
        }

        /// <summary>
        /// Sanitize a string for use in a file name by replacing invalid characters.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <returns>A sanitized string suitable for a file name.</returns>
        private string SanitizeFileName(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return "Unknown";
            }
            string invalidChars = new string(System.IO.Path.GetInvalidFileNameChars()) + new string(System.IO.Path.GetInvalidPathChars());
            foreach (char c in invalidChars)
            {
                input = input.Replace(c, '_');
            }
            return input;
        }

        /// <summary>
        /// Gets a list of all launch trace files in the default trace directory.
        /// </summary>
        /// <returns>A list of full file paths to trace files.</returns>
        public List<string> GetLaunchTraceFiles(string tracePath) // We receive the path as a parameter
        {
            // NO LONGER NEEDED: string tracePath = GetCurrentTracePath(); <-- DELETE THIS LINE if it exists

            if (!System.IO.Directory.Exists(tracePath))
            {
                SimHub.Logging.Current.Info($"TelemetryTraceLogger: Trace directory not found: {tracePath}");
                return new List<string>();
            }

            try
            {
                // Return files from the provided tracePath
                return System.IO.Directory.GetFiles(tracePath, "LaunchTrace_*.csv")
                                        .OrderByDescending(f => System.IO.File.GetCreationTime(f))
                                        .ToList();
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"TelemetryTraceLogger: Error getting launch trace files: {ex.Message}");
                return new List<string>();
            }
        }


        /// <summary>
        /// Reads and parses a single launch trace file, extracting telemetry data and summary.
        /// </summary>
        /// <param name="filePath">The full path to the trace file.</param>
        /// <returns>A tuple containing a list of TelemetryDataRow and the ParsedSummary (or null if not found).</returns>
        public (List<TelemetryDataRow> data, ParsedSummary summary) ReadLaunchTraceFile(string filePath)
        {
            List<TelemetryDataRow> dataRows = new List<TelemetryDataRow>();
            ParsedSummary summary = null;

            if (!System.IO.File.Exists(filePath))
            {
                SimHub.Logging.Current.Warn($"TelemetryTraceLogger: Trace file not found: {filePath}");
                return (dataRows, null);
            }

            try
            {
                var lines = System.IO.File.ReadAllLines(filePath);
                bool readingSummary = false;

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("Timestamp"))
                        continue;

                    if (line.Trim() == "[LaunchSummary]")
                    {
                        readingSummary = true;
                        continue;
                    }

                    if (readingSummary)
                    {
                        summary = ParseSummaryLine(line);
                        readingSummary = false;
                        continue;
                    }


                    var dataRow = TelemetryTraceLogger.ParseTelemetryDataRow(line);
                    if (dataRow != null)
                        dataRows.Add(dataRow);
                }
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"TelemetryTraceLogger: Error reading launch trace file '{filePath}': {ex.Message}");
                return (new List<TelemetryDataRow>(), null);
            }

            if (dataRows.Any())
            {
                var startTime = dataRows.First().Timestamp;
                foreach (var row in dataRows)
                {
                    row.TimeElapsed = (row.Timestamp - startTime).TotalSeconds;
                }
            }

            return (dataRows, summary);
        }


        // Helper to parse a TelemetryDataRow from a CSV line (for reading, not writing)
        public static TelemetryDataRow ParseTelemetryDataRow(string line)
        {
            var parts = line.Split(',');
            if (parts.Length < 8) // Ensure there are now enough parts for all 8 fields
            {
                return null;
            }

            try
            {
                return new TelemetryDataRow
                {
                    Timestamp = DateTime.ParseExact(parts[0], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    SpeedKmh = double.Parse(parts[1], CultureInfo.InvariantCulture),
                    GameClutch = double.Parse(parts[2], CultureInfo.InvariantCulture),
                    PaddleClutch = double.Parse(parts[3], CultureInfo.InvariantCulture),
                    Throttle = double.Parse(parts[4], CultureInfo.InvariantCulture),
                    RPMs = double.Parse(parts[5], CultureInfo.InvariantCulture),
                    AccelerationSurge = double.Parse(parts[6], CultureInfo.InvariantCulture),
                    TractionLoss = double.Parse(parts[7], CultureInfo.InvariantCulture)
                };
            }
            catch (FormatException ex)
            {
                SimHub.Logging.Current.Error($"TelemetryTraceLogger: Error parsing telemetry data row: {ex.Message}. Line: '{line}'");
                return null;
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"TelemetryTraceLogger: Failed parsing row: {ex.Message} | Line: {line}");
                return null;
            }

        }

        // Helper to parse a ParsedSummary from a CSV line (for reading, not writing)
        public static ParsedSummary ParseSummaryLine(string line)
        {
            // The logic is now inside the ParsedSummary class itself.
            return new ParsedSummary(line);
        }

    }

    public class ScreenManager
    {
        private readonly List<string> _pages = new List<string> { "practice", "timing", "racing", "track", "testing" };
        public string CurrentPage { get; set; } = "practice";
        public string Mode { get; set; } = "auto"; // Start in "auto" mode
    }
    public class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly System.Action<object> _execute;
        private readonly System.Predicate<object> _canExecute;
        public RelayCommand(System.Action<object> execute, System.Predicate<object> canExecute = null) { _execute = execute; _canExecute = canExecute; }
        public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object parameter) => _execute(parameter);
        public event System.EventHandler CanExecuteChanged { add => System.Windows.Input.CommandManager.RequerySuggested += value; remove => System.Windows.Input.CommandManager.RequerySuggested -= value; }
    }

}
