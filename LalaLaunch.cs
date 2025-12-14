// --- Using Directives ---
using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;


namespace LaunchPlugin
{

    [PluginDescription("Launch Analysis and Dashes")]
    [PluginAuthor("Lalabot")]
    [PluginName("LalaLaunch")]

    public class BooleanToTextConverter : IValueConverter
    {
        public string TrueText { get; set; } = "Laps";
        public string FalseText { get; set; } = "Litres";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var b = value is bool bv && bv;
            return b ? TrueText : FalseText;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Not needed
            return Binding.DoNothing;
        }
    }
    public class LalaLaunch : IPlugin, IDataPlugin, IWPFSettingsV2, INotifyPropertyChanged
    {
        // --- SimHub Interfaces ---
        public PluginManager PluginManager { get; set; }
        public LaunchPluginSettings Settings { get; private set; }
        public ImageSource PictureIcon => null;
        public string LeftMenuTitle => "Lala Plugin";

        // --- Dashboard Manager ---
        public ScreenManager Screens = new ScreenManager();

        // --- MsgCx dash helpers ---
        public void MsgCx()
        {
            RegisterMsgCxPress();
            // Single-button binding entry point: follows timed → state → action priority
            _msgSystem?.TriggerMsgCx();
            _rejoinEngine?.TriggerMsgCxOverride();
        }

        public void MsgCxTimeOnly()
        {
            RegisterMsgCxPress();
            _msgSystem?.TriggerTimedSilence();
        }

        public void MsgCxStateOnly()
        {
            RegisterMsgCxPress();
            _msgSystem?.TriggerStateClear();
        }

        public void MsgCxActionOnly()
        {
            RegisterMsgCxPress();
            _msgSystem?.TriggerAction();
        }

        public void SetMsgCxTimeMessage(string message, TimeSpan? silence = null)
            => _msgSystem?.PublishTimedMessage(message, silence);

        public void SetMsgCxStateMessage(string message, string stateToken)
            => _msgSystem?.PublishStateMessage(message, stateToken);

        public void SetMsgCxActionMessage(string message)
            => _msgSystem?.PublishActionMessage(message);

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

        public bool LeaderHasFinished
        {
            get => _leaderHasFinished;
            private set
            {
                if (_leaderHasFinished != value)
                {
                    _leaderHasFinished = value;
                    OnPropertyChanged();
                }
            }
        }

        // --- Live Fuel Calculation State ---
        private double _lastFuelLevel = -1;
        private double _lapStartFuel = -1;
        private double _lastLapDistPct = -1;
        private int _lapDetectorLastCompleted = -1;
        private string _lapDetectorLastSessionState = string.Empty;
        private bool _lapDetectorPending;
        private int _lapDetectorPendingLapTarget = -1;
        private string _lapDetectorPendingSessionState = string.Empty;
        private DateTime _lapDetectorPendingExpiresUtc = DateTime.MinValue;
        private double _lapDetectorPendingLastPct = -1.0;
        private DateTime _lapDetectorLastLogUtc = DateTime.MinValue;
        private string _lapDetectorLastLogKey = string.Empty;

        // --- Finish timing + flag detection ---
        private bool _timerZeroSeen;
        private double _timerZeroSessionTime = double.NaN;
        private double _prevSessionTimeRemain = double.NaN;
        private double _leaderCheckeredSessionTime = double.NaN;
        private double _driverCheckeredSessionTime = double.NaN;
        private bool _leaderFinishedSeen;
        private bool _leaderHasFinished;
        private int _lastCompletedLapForFinish = -1;

        // New per-mode rolling windows
        private readonly List<double> _recentDryFuelLaps = new List<double>();
        private readonly List<double> _recentWetFuelLaps = new List<double>();
        private const int FuelWindowSize = 5; // keep last N valid laps per mode

        private double _avgDryFuelPerLap = 0.0;
        private double _avgWetFuelPerLap = 0.0;
        private double _maxDryFuelPerLap = 0.0;
        private double _maxWetFuelPerLap = 0.0;
        private double _minDryFuelPerLap = 0.0;
        private double _minWetFuelPerLap = 0.0;
        private int _validDryLaps = 0;
        private int _validWetLaps = 0;

        // Lap-context state for rejection logic
        private int _lastCompletedFuelLap = -1;
        private int _lapsSincePitExit = int.MaxValue; // big value so early race laps are not treated as pit warmup
        private bool _wasInPitThisLap = false;
        private bool _hadOffTrackThisLap = false; // placeholder; can be wired to incidents later
        private RejoinReason? _latchedIncidentReason = null;

        // --- Cross-session fuel seeds (for Race start) ---
        private double _seedDryFuelPerLap = 0.0;
        private int _seedDrySampleCount = 0;
        private double _seedWetFuelPerLap = 0.0;
        private int _seedWetSampleCount = 0;
        private string _seedCarModel = "";
        private string _seedTrackKey = "";
        private bool _hasActiveDrySeed = false;
        private bool _hasActiveWetSeed = false;
        private int _freshDrySamplesInWindow = 0;
        private int _freshWetSamplesInWindow = 0;
        private string _confidenceCarModel = string.Empty;
        private string _confidenceTrackIdentity = string.Empty;
        private bool _usingFallbackFuelProfile = false;
        private bool _usingFallbackPaceProfile = false;

        // --- Live Fuel Calculation Outputs ---
        public double LiveFuelPerLap { get; private set; }
        public double LiveFuelPerLap_Stable { get; private set; }
        public string LiveFuelPerLap_StableSource { get; private set; } = "None";
        public double LiveFuelPerLap_StableConfidence { get; private set; }
        // LiveLapsRemainingInRace already uses stable fuel/lap time; _Stable exports mirror the same value for explicit dash use/debugging.
        public double LiveLapsRemainingInRace { get; private set; }
        public double LiveLapsRemainingInRace_Stable { get; private set; }
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
        public double Pit_FuelSaveDeltaAfterStop { get; private set; }
        public double Pit_PushDeltaAfterStop { get; private set; }
        public int Pit_StopsRequiredToEnd { get; private set; }
        public double LiveLapsRemainingInRace_S { get; private set; }
        public double LiveLapsRemainingInRace_Stable_S { get; private set; }
        public double Pit_DeltaAfterStop_S { get; private set; }
        public double Pit_PushDeltaAfterStop_S { get; private set; }
        public double Pit_FuelSaveDeltaAfterStop_S { get; private set; }
        public double Pit_TotalNeededToEnd_S { get; private set; }
        private bool _isRefuelSelected = true;
        private bool _isTireChangeSelected = true;
        public double LiveCarMaxFuel { get; private set; }

        public double FuelSaveFuelPerLap { get; private set; }

        public double LiveProjectedDriveTimeAfterZero { get; private set; }
        public double LiveProjectedDriveSecondsRemaining { get; private set; }

        // Push / max-burn guidance
        public double PushFuelPerLap { get; private set; }
        public double DeltaLapsIfPush { get; private set; }
        public bool CanAffordToPush { get; private set; }

        private double _maxFuelPerLapSession = 0.0;
        public double MaxFuelPerLapDisplay => _maxFuelPerLapSession;

        // --- Live Lap Pace (for Fuel tab, once-per-lap update) ---
        private readonly List<double> _recentLapTimes = new List<double>(); // seconds
        private const int LapTimeSampleCount = 6;   // keep last N clean laps
        private TimeSpan _lastSeenBestLap = TimeSpan.Zero;
        private readonly List<double> _recentLeaderLapTimes = new List<double>(); // seconds
        private double _lastLeaderLapTimeSec = 0.0;
        private bool _leaderPaceClearedLogged = false;
        public double LiveLeaderAvgPaceSeconds { get; private set; }
        public double Pace_LeaderDeltaToPlayerSec { get; private set; }
        private double _lastPitLossSaved = 0.0;
        private DateTime _lastPitLossSavedAtUtc = DateTime.MinValue;
        private string _lastPitLossSource = "";
        private DateTime _lastPitLaneSeenUtc = DateTime.MinValue;
        private double _lastLoggedProjectedLaps = double.NaN;
        private DateTime _lastProjectionLogUtc = DateTime.MinValue;
        private string _lastProjectionLapSource = string.Empty;
        private double _lastProjectionLapSeconds = 0.0;
        private DateTime _lastProjectionLapLogUtc = DateTime.MinValue;

        // Stable model inputs
        private double _stableFuelPerLap = 0.0;
        private string _stableFuelPerLapSource = "None";
        private double _stableFuelPerLapConfidence = 0.0;
        private double _stableProjectionLapTime = 0.0;
        private string _stableProjectionLapTimeSource = "fallback.none";

        // --- Stint / Pace tracking ---
        public double Pace_StintAvgLapTimeSec { get; private set; }
        public double Pace_Last5LapAvgSec { get; private set; }
        public int PaceConfidence { get; private set; }

        public double ProjectionLapTime_Stable { get; private set; }
        public string ProjectionLapTime_StableSource { get; private set; } = "fallback.none";

        // Combined view of fuel & pace reliability (for dash use)
        public int OverallConfidence
        {
            get
            {
                // If either metric is missing, fall back to the other
                if (Confidence <= 0) return PaceConfidence;
                if (PaceConfidence <= 0) return Confidence;

                // Combine as fractional probabilities, then rescale to 0–100
                return (int)Math.Round((Confidence / 100.0) * (PaceConfidence / 100.0) * 100.0);
            }
        }

        private PitCycleLite _pitLite; // simple, deterministic pit-cycle surface for the test dash

        // Freeze latched pit debug values after we finalize at the end of OUT LAP.
        // Cleared when a new pit cycle starts (first time we see AwaitingPitLap again).
        private bool _pitFreezeUntilNextCycle = false;

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
        private double _smoothedLiveLapsRemainingState = double.NaN;
        private double _smoothedPitDeltaState = double.NaN;
        private double _smoothedPitPushDeltaState = double.NaN;
        private double _smoothedPitFuelSaveDeltaState = double.NaN;
        private double _smoothedPitTotalNeededState = double.NaN;
        private bool _smoothedProjectionValid = false;
        private bool _smoothedPitValid = false;
        private bool _pendingSmoothingReset = true;
        private const double SmoothedAlpha = 0.35; // ~1–2s response at 500ms tick
        private const int FuelModelConfidenceSwitchOn = 60;
        private const int LapTimeConfidenceSwitchOn = 60;
        private const double StableFuelPerLapDeadband = 0.03; // 0.03 L/lap chosen to suppress lap-to-lap noise and prevent delta chatter
        private const double StableLapTimeDeadband = 0.3; // 0.3 s chosen to stop projection lap time source flapping on small variance

        public RelayCommand SaveActiveProfileCommand { get; private set; }
        public RelayCommand ReturnToDefaultsCommand { get; private set; }
        private void ReturnToDefaults()
        {
            ActiveProfile = ProfilesViewModel.GetProfileForCar("Default Settings") ?? ProfilesViewModel.CarProfiles.FirstOrDefault();
            _currentCarModel = string.Empty; // Reset car model state to match
        }
        private void SaveActiveProfile()
        {
            ProfilesViewModel.SaveProfiles();
            IsActiveProfileDirty = false; // Reset the dirty flag after saving
            SimHub.Logging.Current.Info($"[Profiles] Changes to '{ActiveProfile?.ProfileName}' saved.");
        }

        private static double ComputeStableMedian(List<double> samples)
        {
            if (samples == null || samples.Count == 0) return 0;
            var arr = samples.ToArray();
            Array.Sort(arr);
            int mid = arr.Length / 2;
            return (arr.Length % 2 == 1) ? arr[mid] : (arr[mid - 1] + arr[mid]) / 2.0;
        }

        private static double ComputeCoefficientOfVariation(List<double> samples, double average)
        {
            if (samples == null || samples.Count == 0 || average <= 0.0) return 0.0;
            if (samples.Count == 1) return 0.0;

            double sumSquared = 0.0;
            foreach (var s in samples)
            {
                double delta = s - average;
                sumSquared += delta * delta;
            }

            double variance = sumSquared / samples.Count;
            return Math.Sqrt(variance) / average;
        }

        // Returns profile average lap in *seconds* for the current car+track, 0 if none.
        // Prefers Dry avg, falls back to Wet if Dry is unset.
        private double GetProfileAvgLapSeconds()
        {
            try
            {
                var car = ActiveProfile; // your existing pointer
                if (car == null) return 0.0;

                // Try by key first, then by display name (your CarProfile has this utility)
                var ts =
                    car.ResolveTrackByNameOrKey(CurrentTrackKey) ??
                    car.ResolveTrackByNameOrKey(CurrentTrackName);

                if (ts == null) return 0.0;

                // TrackStats stores lap averages in milliseconds (nullable)
                var ms =
                    (ts.AvgLapTimeDry ?? 0) > 0 ? (ts.AvgLapTimeDry ?? 0) :
                    (ts.AvgLapTimeWet ?? 0);

                if (ms <= 0) return 0.0;
                return ms / 1000.0; // convert ms -> sec
            }
            catch { return 0.0; }
        }

        // Returns dry/wet profile fuel baselines for the current car+track (0 if unknown)
        private (double dry, double wet) GetProfileFuelBaselines()
        {
            try
            {
                var car = ActiveProfile;
                if (car == null) return (0.0, 0.0);

                var ts =
                    car.ResolveTrackByNameOrKey(CurrentTrackKey) ??
                    car.ResolveTrackByNameOrKey(CurrentTrackName);

                if (ts == null) return (0.0, 0.0);

                double dry = ts.AvgFuelPerLapDry ?? 0.0;
                double wet = ts.AvgFuelPerLapWet ?? 0.0;
                return (dry, wet);
            }
            catch
            {
                return (0.0, 0.0);
            }
        }

        // 0–100 confidence for the current mode using weighted samples, variance, wet/dry match, and fallback usage
        private int ComputeFuelModelConfidence(bool isWetMode)
        {
            var window = isWetMode ? _recentWetFuelLaps : _recentDryFuelLaps;
            var avg = isWetMode ? _avgWetFuelPerLap : _avgDryFuelPerLap;
            var min = isWetMode ? _minWetFuelPerLap : _minDryFuelPerLap;
            var max = isWetMode ? _maxWetFuelPerLap : _maxDryFuelPerLap;

            int freshSamples = isWetMode ? _freshWetSamplesInWindow : _freshDrySamplesInWindow;
            bool hasSeed = isWetMode ? _hasActiveWetSeed : _hasActiveDrySeed;

            if (window.Count <= 0 || avg <= 0.0)
                return 0;

            double inheritedFloor = hasSeed ? 0.30 : 0.0; // seeded laps should give a small/moderate start
            double weightedSampleCount = freshSamples + (hasSeed ? 0.35 : 0.0);
            double sampleFactor = Math.Min(1.0, inheritedFloor + (weightedSampleCount / 5.0));

            // Variance factor (uses both coefficient of variation and spread)
            double varianceFactor;
            double cv = ComputeCoefficientOfVariation(window, avg);
            if (window.Count == 1)
                varianceFactor = 0.85; // single sample: allow moderate confidence only
            else if (cv <= 0.03)
                varianceFactor = 1.0;
            else if (cv <= 0.08)
                varianceFactor = 0.9;
            else if (cv <= 0.15)
                varianceFactor = 0.7;
            else
                varianceFactor = 0.5;

            double spreadFactor = 1.0;
            if (avg > 0.0 && max > 0.0 && min > 0.0)
            {
                double spreadRatio = (max - min) / avg;
                if (spreadRatio > 0.25)
                    spreadFactor = 0.75;
                if (spreadRatio > 0.40)
                    spreadFactor = 0.55;
            }

            // Wet/dry match penalty when we are borrowing opposite-condition data
            bool hasModeData = isWetMode ? _validWetLaps > 0 : _validDryLaps > 0;
            bool usingCrossModeData = !hasModeData && ((isWetMode && _validDryLaps > 0) || (!isWetMode && _validWetLaps > 0));
            double wetMatchFactor = usingCrossModeData ? 0.6 : 1.0;

            double fallbackFactor = _usingFallbackFuelProfile ? 0.65 : 1.0;

            double final = sampleFactor * varianceFactor * spreadFactor * wetMatchFactor * fallbackFactor;
            if (final < 0.0) final = 0.0;
            if (final > 1.0) final = 1.0;

            return (int)Math.Round(final * 100.0);
        }

        // 0–100 confidence for the lap-time model using sample strength, pace variance, and fallback weighting
        private int ComputePaceConfidence()
        {
            int count = _recentLapTimes.Count;
            if (count <= 0) return 0;

            double avg = _recentLapTimes.Average();
            if (avg <= 0.0) return 0;

            double sampleFactor = Math.Min(1.0, 0.2 + (count / 6.0));

            double varianceFactor;
            double cv = ComputeCoefficientOfVariation(_recentLapTimes, avg);
            if (count == 1)
                varianceFactor = 0.8;
            else if (cv <= 0.015)
                varianceFactor = 1.0;
            else if (cv <= 0.04)
                varianceFactor = 0.9;
            else if (cv <= 0.08)
                varianceFactor = 0.7;
            else
                varianceFactor = 0.5;

            double spreadFactor = 1.0;
            double min = _recentLapTimes.Min();
            double max = _recentLapTimes.Max();
            double spreadRatio = avg > 0 ? (max - min) / avg : 0.0;
            if (spreadRatio > 0.06)
                spreadFactor = 0.8;
            if (spreadRatio > 0.12)
                spreadFactor = 0.6;

            bool fallbackUsed = _usingFallbackPaceProfile || count < 2;
            double fallbackFactor = fallbackUsed ? 0.7 : 1.0;

            double final = sampleFactor * varianceFactor * spreadFactor * fallbackFactor;
            if (final < 0.0) final = 0.0;
            if (final > 1.0) final = 1.0;

            return (int)Math.Round(final * 100.0);
        }

        private void UpdateLeaderDelta()
        {
            if (Pace_Last5LapAvgSec > 0.0 && LiveLeaderAvgPaceSeconds > 0.0)
            {
                Pace_LeaderDeltaToPlayerSec = Pace_Last5LapAvgSec - LiveLeaderAvgPaceSeconds;
            }
            else
            {
                Pace_LeaderDeltaToPlayerSec = 0.0;
            }
        }

        private void CaptureFuelSeedForNextSession(string fromSessionType)
        {
            try
            {
                int totalValid = _validDryLaps + _validWetLaps;
                if (totalValid <= 0)
                    return;

                if (string.IsNullOrEmpty(CurrentCarModel) || CurrentCarModel == "Unknown")
                    return;
                if (string.IsNullOrEmpty(CurrentTrackKey) || CurrentTrackKey == "unknown")
                    return;

                _seedCarModel = CurrentCarModel;
                _seedTrackKey = CurrentTrackKey;

                if (_validDryLaps > 0 && _avgDryFuelPerLap > 0.0)
                {
                    _seedDryFuelPerLap = _avgDryFuelPerLap;
                    _seedDrySampleCount = Math.Min(_validDryLaps, FuelWindowSize);
                }
                else
                {
                    _seedDryFuelPerLap = 0.0;
                    _seedDrySampleCount = 0;
                }

                if (_validWetLaps > 0 && _avgWetFuelPerLap > 0.0)
                {
                    _seedWetFuelPerLap = _avgWetFuelPerLap;
                    _seedWetSampleCount = Math.Min(_validWetLaps, FuelWindowSize);
                }
                else
                {
                    _seedWetFuelPerLap = 0.0;
                    _seedWetSampleCount = 0;
                }

                        SimHub.Logging.Current.Info(
                            $"[LiveFuel] Captured seed from session '{fromSessionType}' for car='{_seedCarModel}', track='{_seedTrackKey}': " +
                            $"dry={_seedDryFuelPerLap:F3} (n={_seedDrySampleCount}), wet={_seedWetFuelPerLap:F3} (n={_seedWetSampleCount}).");
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Info($"[LiveFuel] CaptureFuelSeedForNextSession error: {ex.Message}");
            }
        }

        private void ResetLiveFuelModelForNewSession(string newSessionType, bool applySeeds)
        {
            // Clear per-lap / model state
            _recentDryFuelLaps.Clear();
            _recentWetFuelLaps.Clear();
            _validDryLaps = 0;
            _validWetLaps = 0;
            _avgDryFuelPerLap = 0.0;
            _avgWetFuelPerLap = 0.0;
            _maxDryFuelPerLap = 0.0;
            _maxWetFuelPerLap = 0.0;
            _minDryFuelPerLap = 0.0;
            _minWetFuelPerLap = 0.0;
            _lastFuelLevel = -1.0;
            _lapStartFuel = -1.0;
            _lastLapDistPct = -1.0;
            _lapDetectorLastCompleted = -1;
            _lapDetectorLastSessionState = string.Empty;
            _lapDetectorPending = false;
            _lapDetectorPendingLapTarget = -1;
            _lapDetectorPendingSessionState = string.Empty;
            _lapDetectorPendingExpiresUtc = DateTime.MinValue;
            _lapDetectorPendingLastPct = -1.0;
            _lapDetectorLastLogUtc = DateTime.MinValue;
            _lapDetectorLastLogKey = string.Empty;
            _lastCompletedFuelLap = -1;
            _lapsSincePitExit = int.MaxValue;
            _wasInPitThisLap = false;
            _hadOffTrackThisLap = false;
            _latchedIncidentReason = null;
            _lastPitLaneSeenUtc = DateTime.MinValue;
            _freshDrySamplesInWindow = 0;
            _freshWetSamplesInWindow = 0;
            _hasActiveDrySeed = false;
            _hasActiveWetSeed = false;
            _usingFallbackFuelProfile = false;
            _stableFuelPerLap = 0.0;
            _stableFuelPerLapSource = "None";
            _stableFuelPerLapConfidence = 0.0;
            LiveFuelPerLap_Stable = 0.0;
            LiveFuelPerLap_StableSource = "None";
            LiveFuelPerLap_StableConfidence = 0.0;
            _stableProjectionLapTime = 0.0;
            _stableProjectionLapTimeSource = "fallback.none";
            ProjectionLapTime_Stable = 0.0;
            ProjectionLapTime_StableSource = "fallback.none";
            LiveLapsRemainingInRace_Stable = 0.0;
            LiveLapsRemainingInRace_Stable_S = 0.0;

            FuelCalculator?.ResetTrackConditionOverrideForSessionChange();

            // Clear pace tracking alongside fuel model resets so session transitions don't carry stale data
            _recentLapTimes.Clear();
            _recentLeaderLapTimes.Clear();
            _lastLeaderLapTimeSec = 0.0;
            LiveLeaderAvgPaceSeconds = 0.0;
            _leaderPaceClearedLogged = false;
            Pace_StintAvgLapTimeSec = 0.0;
            Pace_Last5LapAvgSec = 0.0;
            Pace_LeaderDeltaToPlayerSec = 0.0;
            PaceConfidence = 0;
            _usingFallbackPaceProfile = false;

            LiveFuelPerLap = 0.0;
            Confidence = 0;
            _maxFuelPerLapSession = 0.0;
            FuelCalculator?.SetLiveConfidenceLevels(0, 0, 0);
            FuelCalculator?.SetLiveLapPaceEstimate(0, 0);
            FuelCalculator?.SetLiveFuelWindowStats(0, 0, 0, 0, 0, 0, 0, 0);

            // Only seed when entering Race with matching car/track
            if (applySeeds &&
                newSessionType == "Race" &&
                _seedCarModel == CurrentCarModel &&
                _seedTrackKey == CurrentTrackKey)
            {
                bool seededAny = false;

                if (_seedDryFuelPerLap > 0.0)
                {
                    _recentDryFuelLaps.Add(_seedDryFuelPerLap);
                    _validDryLaps = 1;
                    _avgDryFuelPerLap = _seedDryFuelPerLap;
                    _maxDryFuelPerLap = _seedDryFuelPerLap;
                    _minDryFuelPerLap = _seedDryFuelPerLap;
                    _hasActiveDrySeed = true;
                    seededAny = true;
                }

                if (_seedWetFuelPerLap > 0.0)
                {
                    _recentWetFuelLaps.Add(_seedWetFuelPerLap);
                    _validWetLaps = 1;
                    _avgWetFuelPerLap = _seedWetFuelPerLap;
                    _maxWetFuelPerLap = _seedWetFuelPerLap;
                    _minWetFuelPerLap = _seedWetFuelPerLap;
                    _hasActiveWetSeed = true;
                    seededAny = true;
                }

                if (seededAny)
                {
                    bool isWetNow = FuelCalculator != null && FuelCalculator.IsWet;
                    LiveFuelPerLap = isWetNow
                        ? (_avgWetFuelPerLap > 0 ? _avgWetFuelPerLap : _avgDryFuelPerLap)
                        : (_avgDryFuelPerLap > 0 ? _avgDryFuelPerLap : _avgWetFuelPerLap);

                    Confidence = ComputeFuelModelConfidence(isWetNow);

                    try
                    {
                        SimHub.Logging.Current.Info(
                            $"[LiveFuel] Seeded race model from previous session (car='{_seedCarModel}', track='{_seedTrackKey}'): " +
                            $"dry={_seedDryFuelPerLap:F3}, wet={_seedWetFuelPerLap:F3}, conf={Confidence}%.");
                    }
                    catch { /* logging must not throw */ }
                }
                FuelCalculator?.SetLiveFuelWindowStats(_avgDryFuelPerLap, _minDryFuelPerLap, _maxDryFuelPerLap, _validDryLaps,
                    _avgWetFuelPerLap, _minWetFuelPerLap, _maxWetFuelPerLap, _validWetLaps);
            }

            _confidenceCarModel = CurrentCarModel ?? string.Empty;
            _confidenceTrackIdentity =
                !string.IsNullOrWhiteSpace(CurrentTrackKey) && !CurrentTrackKey.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                    ? CurrentTrackKey
                    : (CurrentTrackName ?? string.Empty);
        }

        private void ResetConfidenceForNewCombo(string sessionType)
        {
            _seedDryFuelPerLap = 0.0;
            _seedDrySampleCount = 0;
            _seedWetFuelPerLap = 0.0;
            _seedWetSampleCount = 0;
            _seedCarModel = string.Empty;
            _seedTrackKey = string.Empty;
            ResetLiveFuelModelForNewSession(sessionType, false);

            try
            {
                SimHub.Logging.Current.Info("[LiveFuel] Car/track change detected – clearing seeds and confidence");
            }
            catch { /* logging must not throw */ }
        }

        private void HandleSessionChangeForFuelModel(string fromSession, string toSession)
        {
            try
            {
                // If we don't know car/track yet, just reset without seeding.
                if (string.IsNullOrEmpty(CurrentCarModel) || CurrentCarModel == "Unknown" ||
                    string.IsNullOrEmpty(CurrentTrackKey) || CurrentTrackKey == "unknown")
                {
                    ResetLiveFuelModelForNewSession(toSession, false);
                    return;
                }

                bool isDrivingFrom =
                    fromSession == "Offline Testing" ||
                    fromSession == "Practice" ||
                    fromSession == "Open Qualify" ||
                    fromSession == "Lone Qualify" ||
                    fromSession == "Qualifying" ||
                    fromSession == "Warmup" ||
                    fromSession == "Race";

                bool isEnteringRace = toSession == "Race";

                if (isDrivingFrom && isEnteringRace)
                {
                    // Use fuel learnt in Practice/Qual/Warmup/etc as seed for Race.
                    CaptureFuelSeedForNextSession(fromSession);
                    ResetLiveFuelModelForNewSession(toSession, true);
                }
                else
                {
                    // Non-race transitions: just clear the model (no seeding).
                    ResetLiveFuelModelForNewSession(toSession, false);
                }
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Info($"[LiveFuel] HandleSessionChangeForFuelModel error: {ex.Message}");
            }
        }

        private string ReadLapDetectorSessionState()
        {
            try
            {
                if (PluginManager == null) return string.Empty;
                object raw = PluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.SessionState");
                return raw != null ? raw.ToString() ?? string.Empty : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool IsSessionRunningForLapDetector(string sessionStateToken)
        {
            if (string.IsNullOrWhiteSpace(sessionStateToken))
                return true; // assume running if we can't read state

            if (int.TryParse(sessionStateToken, out int numericState))
                return numericState == 4; // iRacing "racing"

            string normalized = sessionStateToken.Trim().ToLowerInvariant();
            return normalized.Contains("race") || normalized.Contains("running") || normalized.Contains("green");
        }

        private bool DetectLapCrossing(GameData data, double curPctNormalized, double lastPctNormalized)
        {
            int lapCount = Convert.ToInt32(data.NewData?.CompletedLaps ?? 0);
            string sessionStateToken = ReadLapDetectorSessionState();
            bool sessionRunning = IsSessionRunningForLapDetector(sessionStateToken);

            if (_lapDetectorPending && DateTime.UtcNow > _lapDetectorPendingExpiresUtc)
            {
                if (ShouldLogLapDetector("pending-expired"))
                {
                    SimHub.Logging.Current.Info(
                        $"[LapDetector] Pending lap confirmation expired (lap={_lapDetectorPendingLapTarget}, pct={_lapDetectorPendingLastPct:F3}).");
                }
                _lapDetectorPending = false;
                _lapDetectorPendingLapTarget = -1;
                _lapDetectorPendingSessionState = string.Empty;
            }

            if (_lapDetectorLastCompleted < 0)
            {
                _lapDetectorLastSessionState = sessionStateToken;
                _lapDetectorLastCompleted = lapCount;
                return false;
            }

            if (_lapDetectorLastSessionState != sessionStateToken || lapCount < _lapDetectorLastCompleted)
            {
                _lapDetectorLastSessionState = sessionStateToken;
                _lapDetectorLastCompleted = lapCount;
                _lapDetectorPending = false;
                _lapDetectorPendingLapTarget = -1;
                _lapDetectorPendingSessionState = string.Empty;
                return false;
            }

            int lapDelta = lapCount - _lapDetectorLastCompleted;
            if (lapDelta > 1)
            {
                _lapDetectorLastSessionState = sessionStateToken;
                _lapDetectorLastCompleted = lapCount;
                _lapDetectorPending = false;
                _lapDetectorPendingLapTarget = -1;
                _lapDetectorPendingSessionState = string.Empty;
                return false; // ignore jumps
            }

            if (lapDelta == 1 && sessionRunning)
            {
                double speedKmh = data.NewData?.SpeedKmh ?? 0.0;
                bool speedTooLow = speedKmh < 8.0;

                bool pctFarFromSf = lastPctNormalized > 0.15 && curPctNormalized > 0.15 &&
                    lastPctNormalized < 0.85 && curPctNormalized < 0.85;
                bool pctImpossible = lastPctNormalized >= 0 && curPctNormalized >= 0 &&
                    (lastPctNormalized < 0.25 && curPctNormalized > 0.25);
                bool nearStartFinish = lastPctNormalized > 0.95 && curPctNormalized < 0.05;

                bool pendingActive = _lapDetectorPending &&
                    _lapDetectorPendingLapTarget == lapCount &&
                    _lapDetectorPendingSessionState == sessionStateToken;

                if (pendingActive)
                {
                    bool pendingExpired = DateTime.UtcNow >= _lapDetectorPendingExpiresUtc;

                    if (!speedTooLow && (!pctFarFromSf || pendingExpired || nearStartFinish))
                    {
                        _lapDetectorLastSessionState = sessionStateToken;
                        _lapDetectorLastCompleted = lapCount;
                        _lapDetectorPending = false;
                        _lapDetectorPendingLapTarget = -1;
                        _lapDetectorPendingSessionState = string.Empty;
                        return true;
                    }

                    if (pendingExpired)
                    {
                        if (ShouldLogLapDetector("pending-rejected"))
                        {
                            SimHub.Logging.Current.Info(
                                $"[LapDetector] Pending lap increment rejected after debounce (lap={lapCount}, lastPct={lastPctNormalized:F3}, curPct={curPctNormalized:F3}, speed={speedKmh:F1}).");
                        }
                        _lapDetectorLastSessionState = sessionStateToken;
                        _lapDetectorLastCompleted = lapCount;
                        _lapDetectorPending = false;
                        _lapDetectorPendingLapTarget = -1;
                        _lapDetectorPendingSessionState = string.Empty;
                    }

                    return false;
                }

                if (speedTooLow)
                {
                    if (ShouldLogLapDetector("low-speed"))
                    {
                        SimHub.Logging.Current.Info($"[LapDetector] Ignoring lap increment at low speed ({speedKmh:F1} km/h).");
                    }
                    _lapDetectorLastSessionState = sessionStateToken;
                    _lapDetectorLastCompleted = lapCount;
                    return false;
                }

                if (pctFarFromSf || pctImpossible)
                {
                    _lapDetectorPending = true;
                    _lapDetectorPendingLapTarget = lapCount;
                    _lapDetectorPendingSessionState = sessionStateToken;
                    _lapDetectorPendingExpiresUtc = DateTime.UtcNow.AddMilliseconds(500);
                    _lapDetectorPendingLastPct = curPctNormalized;

                    if (ShouldLogLapDetector("pending-armed"))
                    {
                        SimHub.Logging.Current.Info(
                            $"[LapDetector] Lap increment queued for confirmation (lap={lapCount}, lastPct={lastPctNormalized:F3}, curPct={curPctNormalized:F3}, speed={speedKmh:F1}).");
                    }

                    return false;
                }

                if (!nearStartFinish && lastPctNormalized >= 0 && curPctNormalized >= 0 && ShouldLogLapDetector("atypical"))
                {
                    SimHub.Logging.Current.Info($"[LapDetector] Lap increment detected via CompletedLaps with atypical track% values (last={lastPctNormalized:F3}, cur={curPctNormalized:F3}).");
                }

                _lapDetectorLastSessionState = sessionStateToken;
                _lapDetectorLastCompleted = lapCount;
                _lapDetectorPending = false;
                _lapDetectorPendingLapTarget = -1;
                _lapDetectorPendingSessionState = string.Empty;
                return true;
            }

            _lapDetectorLastSessionState = sessionStateToken;
            _lapDetectorLastCompleted = lapCount;
            return false;
        }

        private bool ShouldLogLapDetector(string key)
        {
            var now = DateTime.UtcNow;
            if (key != _lapDetectorLastLogKey || (now - _lapDetectorLastLogUtc).TotalSeconds > 1.0)
            {
                _lapDetectorLastLogKey = key;
                _lapDetectorLastLogUtc = now;
                return true;
            }

            return false;
        }

        private void LogLapCrossingSummary(
            int lapNumber,
            double lastLapSeconds,
            bool paceAccepted,
            string paceReason,
            string paceBaseline,
            string paceDelta,
            double stintAvg,
            double last5Avg,
            int paceConfidence,
            double leaderLapSeconds,
            double leaderAvgSeconds,
            int leaderSampleCount,
            double fuelUsed,
            bool fuelAccepted,
            string fuelReason,
            bool isWetMode,
            double liveFuelPerLap,
            int validDryLaps,
            int validWetLaps,
            double maxFuelPerLapSession,
            int fuelConfidence,
            int overallConfidence,
            bool pitTripActive)
        {
            string prefix = "[SimHubLogInfo][LapCrossing]";
            string pacePart =
                $"pace lap={lapNumber} time={lastLapSeconds:F3}s accepted={paceAccepted} reason={paceReason} " +
                $"baseline={paceBaseline} delta={paceDelta} stintAvg={stintAvg:F3}s last5={last5Avg:F3}s " +
                $"conf={paceConfidence}% leaderLap={leaderLapSeconds:F3}s leaderAvg={leaderAvgSeconds:F3}s " +
                $"leaderCount={leaderSampleCount}";

            string fuelPart =
                $"fuel used={fuelUsed:F3}L accepted={fuelAccepted} reason={fuelReason} " +
                $"mode={(isWetMode ? "wet" : "dry")} live={liveFuelPerLap:F3}L/lap windowDry={validDryLaps} " +
                $"windowWet={validWetLaps} maxSession={maxFuelPerLapSession:F3}L conf={fuelConfidence}% " +
                $"overall={overallConfidence}% pitTrip={pitTripActive}";

            SimHub.Logging.Current.Info($"{prefix} | {pacePart} | {fuelPart}");
        }

        private void UpdateLiveFuelCalcs(GameData data, PluginManager pluginManager)
        {
            // --- 1) Gather required data ---
            double currentFuel = data.NewData?.Fuel ?? 0.0;
            double rawLapPct = data.NewData?.TrackPositionPercent ?? 0.0;
            double maxFuel = data.NewData?.MaxFuel ?? 0.0;
            double fallbackFuelPerLap = Convert.ToDouble(
                PluginManager.GetPropertyValue("DataCorePlugin.Computed.Fuel_LitersPerLap") ?? 0.0
            );

            double sessionTime = SafeReadDouble(pluginManager, "DataCorePlugin.GameRawData.Telemetry.SessionTime", 0.0);
            double sessionTimeRemain = SafeReadDouble(pluginManager, "DataCorePlugin.GameRawData.Telemetry.SessionTimeRemain", double.NaN);

            // Pit detection: use both signals (some installs expose only one reliably)
            bool isInPitLaneFlag = (data.NewData?.IsInPitLane ?? 0) != 0;
            bool isOnPitRoadFlag = Convert.ToBoolean(
                PluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.IsOnPitRoad") ?? false
            );
            bool inPitArea = isInPitLaneFlag || isOnPitRoadFlag;

            // Track per-lap pit involvement so we can reject any lap that touched pit lane
            if (inPitArea)
            {
                _lastPitLaneSeenUtc = DateTime.UtcNow;
                _wasInPitThisLap = true;
            }

            bool pitExitRecently = (DateTime.UtcNow - _lastPitLaneSeenUtc).TotalSeconds < 1.0;
            bool pitTripActive = _wasInPitThisLap || inPitArea || pitExitRecently;

            // Normalize lap % to 0..1 in case source is 0..100
            double curPct = rawLapPct;
            if (curPct > 1.5) curPct *= 0.01;
            double lastPct = _lastLapDistPct;
            if (lastPct > 1.5) lastPct *= 0.01;

            // --- 2) Detect S/F crossing via lap counter (track% used only for sanity) ---
            bool lapCrossed = DetectLapCrossing(data, curPct, lastPct);

            // Unfreeze once we're primed for a new pit cycle (next entry detected)
            if (_pitFreezeUntilNextCycle && _pit?.CurrentState == PitEngine.PaceDeltaState.AwaitingPitLap)
            {
                _pitFreezeUntilNextCycle = false;
            }

            double leaderLastLapSec = 0.0;
            bool leaderLapWasFallback = false;

            if (lapCrossed)
            {
                var leaderLap = ReadLeaderLapTimeSeconds(PluginManager, data, Pace_Last5LapAvgSec, LiveLeaderAvgPaceSeconds);
                leaderLastLapSec = leaderLap.seconds;
                leaderLapWasFallback = leaderLap.isFallback;

                if (leaderLastLapSec <= 0.0 && _recentLeaderLapTimes.Count > 0)
                {
                    // Feed dropped: clear leader pace so downstream calcs don't reuse stale values
                    if (!_leaderPaceClearedLogged)
                    {
                        SimHub.Logging.Current.Info(string.Format(
                            "[Leader] clearing leader pace (feed dropped), lastAvg={0:F3}",
                            LiveLeaderAvgPaceSeconds));
                        _leaderPaceClearedLogged = true;
                    }
                    _recentLeaderLapTimes.Clear();
                    _lastLeaderLapTimeSec = 0.0;
                    LiveLeaderAvgPaceSeconds = 0.0;
                    Pace_LeaderDeltaToPlayerSec = 0.0;
                }

                // This logic checks if the PitEngine is waiting for an out-lap and, if so,
                // provides it with the necessary data to finalize the calculation.
                if (_pit != null && (_pit.CurrentPitPhase == PitPhase.None || _pit.CurrentPitPhase == PitPhase.ExitingPits)) // Ensure we are on track
                {
                    var lastLapTsPit = data.NewData?.LastLapTime ?? TimeSpan.Zero;
                    double lastLapSecPit = lastLapTsPit.TotalSeconds;

                    // Basic validity check for the lap itself
                    bool lastLapLooksClean = !pitTripActive && lastLapSecPit > 20 && lastLapSecPit < 900;

                    // Get a stable average pace to compare against
                    double stableAvgPace = ComputeStableMedian(_recentLapTimes);
                    bool usedProfileFallback = false;

                    // --- Add fallback logic if live pace is unavailable ---
                    if (stableAvgPace <= 0 && ActiveProfile != null)
                    {
                        var trackRecord = ActiveProfile.FindTrack(CurrentTrackKey);
                        if (trackRecord?.AvgLapTimeDry > 0)
                        {
                            stableAvgPace = trackRecord.AvgLapTimeDry.Value / 1000.0;
                            SimHub.Logging.Current.Info($"[Pace] No live pace available. Using profile avg lap time as fallback: {stableAvgPace:F2}s");
                            usedProfileFallback = true;
                        }
                    }
                    // --- PB tertiary fallback (for replays / no live median and no profile avg) ---
                    if (stableAvgPace <= 0 && _lastSeenBestLap > TimeSpan.Zero)
                    {
                        stableAvgPace = _lastSeenBestLap.TotalSeconds;
                    }
                    _usingFallbackPaceProfile = usedProfileFallback;
                    SimHub.Logging.Current.Info($"[Pit/Pace] Baseline used = {stableAvgPace:F3}s (live median → profile avg → PB).");

                    // Decide and publish baseline (profile-avg → live-median → session-pb)
                    string paceSource = "live-median"; // default to whatever stableAvgPace currently holds

                    // 1) Prefer profile average (Dry). Fall back to name if key resolve fails.
                    try
                    {
                        if (ActiveProfile != null)
                        {
                            var tr =
                                ActiveProfile.FindTrack(CurrentTrackKey) ??
                                ActiveProfile.ResolveTrackByNameOrKey(CurrentTrackName);

                            if (tr?.AvgLapTimeDry > 0)
                            {
                                stableAvgPace = tr.AvgLapTimeDry.Value / 1000.0; // ms -> sec
                                paceSource = "profile-avg";
                            }
                        }
                    }
                    catch { /* keep fallback behavior */ }

                    // 2) If still not set (>0), leave live-median as-is (stableAvgPace already holds it).
                    //    3) If live-median is also missing, fall back to the session PB.
                    if (stableAvgPace <= 0 && _lastSeenBestLap > TimeSpan.Zero)
                    {
                        stableAvgPace = _lastSeenBestLap.TotalSeconds;
                        paceSource = "session-pb";
                    }

                    // Publish to dash
                    _pitDbg_AvgPaceUsedSec = stableAvgPace;
                    _pitDbg_AvgPaceSource = paceSource;

                    // Publish lap numbers to dash as soon as we cross S/F, based on pit state
                    var pitPhaseBefore = _pit?.CurrentState ?? PitEngine.PaceDeltaState.Idle;

                    if (pitPhaseBefore == PitEngine.PaceDeltaState.AwaitingPitLap)
                    {
                        // This crossing just completed the PIT LAP (includes the stop)
                        _pitDbg_InLapSec = lastLapSecPit;
                        _pitDbg_DeltaInSec = _pitDbg_InLapSec - _pitDbg_AvgPaceUsedSec;
                    }
                    else if (pitPhaseBefore == PitEngine.PaceDeltaState.AwaitingOutLap)
                    {
                        // This crossing just completed the OUT LAP
                        _pitDbg_OutLapSec = lastLapSecPit;
                        _pitDbg_DeltaOutSec = _pitDbg_OutLapSec - _pitDbg_AvgPaceUsedSec;
                    }

                    // Call PitEngine to advance the state / compute totals when appropriate
                    _pit.FinalizePaceDeltaCalculation(lastLapSecPit, stableAvgPace, lastLapLooksClean);

                    // --- IMMEDIATE PUBLISH: only when we just completed the OUT LAP ---
                    if (pitPhaseBefore == PitEngine.PaceDeltaState.AwaitingOutLap)
                    {
                        // Prefer the DTL (Total) if available; else fall back to Direct
                        var dtlNow = _pit?.LastTotalPitCycleTimeLoss ?? 0.0;
                        var directNow = _pit?.LastDirectTravelTime ?? 0.0;
                        FuelCalculator?.SetLastPitDriveThroughSeconds(directNow);

                        _pitDbg_CandidateSavedSec = (dtlNow > 0.0) ? dtlNow : directNow;
                        _pitDbg_CandidateSource = (dtlNow > 0.0) ? "total" : "direct";

                        // Lock the debug panel numbers to this event until the next cycle
                        _pitDbg_InLapSec = _lastLapTimeSec; // the lap we primed as IN-lap
                        _pitDbg_OutLapSec = lastLapSecPit;   // the OUT-lap that just finished

                        _pitDbg_DeltaInSec = _pitDbg_InLapSec - _pitDbg_AvgPaceUsedSec;
                        _pitDbg_DeltaOutSec = _pitDbg_OutLapSec - _pitDbg_AvgPaceUsedSec;

                        // Raw “formula” view for the dash:
                        // DTL = (Lpit - Stop + Lout) - 2*Avg,
                        // and Lpit (with stop included) can be reconstructed as:
                        // Lpit = DTL + (2*Avg) - Lout + Stop
                        double stopNow = _pit?.PitStopDuration.TotalSeconds ?? 0.0;
                        FuelCalculator?.SetLastTyreChangeSeconds(stopNow);
                        _pitDbg_RawPitLapSec = dtlNow + (2.0 * _pitDbg_AvgPaceUsedSec) - _pitDbg_OutLapSec + stopNow;
                        _pitDbg_RawDTLFormulaSec = (_pitDbg_RawPitLapSec - stopNow + _pitDbg_OutLapSec) - (2.0 * _pitDbg_AvgPaceUsedSec);

                        // Freeze everything until the next pit entry
                        _pitFreezeUntilNextCycle = true;
                    }

                    // Roll the "previous lap" pointer AFTER we used it as in-lap
                    _lastLapTimeSec = lastLapSecPit;
                }
            }

            // First-time init: capture a starting fuel level and bail until we’ve completed one lap
            if (_lapStartFuel < 0)
            {
                _lapStartFuel = currentFuel;
            }

            if (lapCrossed)
            {
                // Guard with CompletedLaps so we only process fully completed race laps
                int completedLapsNow = Convert.ToInt32(data.NewData?.CompletedLaps ?? 0);
                if (completedLapsNow > _lastCompletedFuelLap)
                {
                    // --- Lap-time / pace tracking (clean laps only) ---
                    var lastLapTs = data.NewData?.LastLapTime ?? TimeSpan.Zero;
                    double lastLapSec = lastLapTs.TotalSeconds;

                    // Refresh the leader rolling average whenever we see a new lap time
                    if (!leaderLapWasFallback && leaderLastLapSec > 20.0 && leaderLastLapSec < 900.0 &&
                        Math.Abs(leaderLastLapSec - _lastLeaderLapTimeSec) > 1e-6)
                    {
                        _leaderPaceClearedLogged = false;
                        _recentLeaderLapTimes.Add(leaderLastLapSec);
                        while (_recentLeaderLapTimes.Count > LapTimeSampleCount)
                        {
                            _recentLeaderLapTimes.RemoveAt(0);
                        }

                        _lastLeaderLapTimeSec = leaderLastLapSec;
                        LiveLeaderAvgPaceSeconds = _recentLeaderLapTimes.Average();
                        UpdateLeaderDelta();
                    }
                    else if (_recentLeaderLapTimes.Count == 0)
                    {
                        LiveLeaderAvgPaceSeconds = 0.0;
                        Pace_LeaderDeltaToPlayerSec = 0.0;
                    }

                    double currentAvgLeader = LiveLeaderAvgPaceSeconds;
                    int currentLeaderCount = _recentLeaderLapTimes.Count;

                    bool paceReject = false;
                    string paceReason = "";
                    double paceBaselineForLog = 0.0;
                    double paceDeltaForLog = 0.0;

                    if (_recentLapTimes.Count > 0)
                    {
                        int baselineSamples = Math.Min(5, _recentLapTimes.Count);
                        double sum = 0.0;
                        for (int i = _recentLapTimes.Count - baselineSamples; i < _recentLapTimes.Count; i++)
                        {
                            sum += _recentLapTimes[i];
                        }
                        paceBaselineForLog = sum / baselineSamples;
                        paceDeltaForLog = lastLapSec - paceBaselineForLog;
                    }

                    // 1) Global race warm-up: ignore very early race laps (same as fuel)
                    if (completedLapsNow <= 2)
                    {
                        paceReject = true;
                        paceReason = "race-warmup";
                    }

                    // 2) Any pit involvement this lap? Ignore for pace.
                    if (!paceReject && pitTripActive)
                    {
                        paceReject = true;
                        paceReason = "pit-lap";
                    }

                    // 3) Serious off / incident laps
                    if (!paceReject && _hadOffTrackThisLap)
                    {
                        paceReject = true;
                        paceReason = _latchedIncidentReason.HasValue
                            ? $"incident:{_latchedIncidentReason.Value}"
                            : "incident";
                    }

                    // 4) Obvious junk lap times
                    if (!paceReject)
                    {
                        if (lastLapSec <= 20.0 || lastLapSec >= 900.0)
                        {
                            paceReject = true;
                            paceReason = "bad-lap-time";
                        }
                    }

                    // 5) Timing bracket: moderate + gross outliers
                    if (!paceReject && _recentLapTimes.Count >= 3 && paceBaselineForLog > 0)
                    {
                        double delta = paceDeltaForLog; // +ve = slower than recent average

                        // 5a) Gross outliers: >20s away from our current clean pace, either direction.
                        //     This catches things like huge course cuts or tow / timing glitches.
                        if (Math.Abs(delta) > 20.0)
                        {
                            // You can log this if you like:
                            SimHub.Logging.Current.Info($"[Pace] Gross outlier lap {lastLapSec:F2}s (avg={paceBaselineForLog:F2}s, Δ={delta:F1}s)");
                            paceReject = true;
                            paceReason = "gross-outlier";
                        }
                        // 5b) Normal too-slow laps: more than ~6s slower than our recent clean pace.
                        //     Keeps spins / heavy traffic / yellows out of the model, but allows faster laps.
                        else if (delta > 6.0)
                        {
                            SimHub.Logging.Current.Info($"[Pace] Rejected too-slow lap {lastLapSec:F2}s (avg={paceBaselineForLog:F2}s, Δ={delta:F1}s)");
                            paceReject = true;
                            paceReason = "slow-outlier";
                        }
                    }


                    if (!paceReject)
                    {
                        _recentLapTimes.Add(lastLapSec);
                        // Trim to window
                        while (_recentLapTimes.Count > LapTimeSampleCount)
                        {
                            _recentLapTimes.RemoveAt(0);
                        }

                        // Stint average: across all recent clean laps
                        Pace_StintAvgLapTimeSec = _recentLapTimes.Average();

                        // Last-5-laps average (or fewer if we haven't got 5 yet)
                        int count = _recentLapTimes.Count;
                        int take = (count >= 5) ? 5 : count;
                        if (take > 0)
                        {
                            double sum = 0.0;
                            for (int i = count - take; i < count; i++)
                            {
                                sum += _recentLapTimes[i];
                            }
                            Pace_Last5LapAvgSec = sum / take;
                        }
                        else
                        {
                            Pace_Last5LapAvgSec = 0.0;
                        }

                        UpdateLeaderDelta();

                        // Update pace confidence
                        PaceConfidence = ComputePaceConfidence();

                        if (Pace_StintAvgLapTimeSec > 0)
                        {
                            FuelCalculator?.SetLiveLapPaceEstimate(Pace_StintAvgLapTimeSec, _recentLapTimes.Count);
                        }

                        if (string.IsNullOrEmpty(paceReason))
                        {
                            paceReason = "accepted";
                        }
                    }
                    else
                    {
                        // If we rejected the lap, keep existing averages, but make sure reason is not empty
                        if (string.IsNullOrEmpty(paceReason))
                        {
                            paceReason = "rejected";
                        }
                    }

                    string paceBaselineLog = (paceBaselineForLog > 0)
                        ? paceBaselineForLog.ToString("F3")
                        : "-";
                    string paceDeltaLog = (paceBaselineForLog > 0)
                        ? paceDeltaForLog.ToString("+0.000;-0.000;0.000")
                        : "-";

                    // --- Fuel per lap calculation & rolling averages ---

                    double fuelUsed = (_lapStartFuel > 0 && currentFuel >= 0)
                        ? (_lapStartFuel - currentFuel)
                        : 0.0;

                    bool isWetMode = FuelCalculator?.IsWet ?? false;

                    bool reject = false;
                    string reason = "";

                    // Global race warm-up: ignore very early race laps
                    // CompletedLaps 0/1 = formation + first race lap (depending on series)
                    if (!reject && completedLapsNow <= 1)
                    {
                        reject = true;
                        reason = "race-warmup";
                    }

                    // 1) Pit involvement – any lap that touched pit lane is rejected
                    if (pitTripActive)
                    {
                        reject = true;
                        reason = "pit-lap";
                    }

                    // 2) First lap after pit exit – tyres cold
                    if (!reject && _lapsSincePitExit == 0)
                    {
                        reject = true;
                        reason = "pit-warmup";
                    }

                    // 3) Incident/off-track laps (hook this when you wire incidents)
                    if (!reject && _hadOffTrackThisLap)
                    {
                        reject = true;
                        reason = _latchedIncidentReason.HasValue
                            ? $"incident:{_latchedIncidentReason.Value}"
                            : "incident";
                    }

                    // 4) Obvious telemetry junk
                    if (!reject)
                    {
                        // coarse cap: 20% of tank or 10 L, whichever is larger
                        double maxPlausibleHard = Math.Max(10.0, 0.20 * Math.Max(maxFuel, 50.0));
                        if (fuelUsed <= 0.05)
                        {
                            reject = true;
                            reason = "fuel<=0";
                        }
                        else if (fuelUsed > maxPlausibleHard)
                        {
                            reject = true;
                            reason = "fuelTooHigh";
                        }
                    }

                    // 5) Profile-based sanity bracket [0.5, 1.5] × baseline
                    if (!reject)
                    {
                        var (baselineDry, baselineWet) = GetProfileFuelBaselines();
                        double baseline = isWetMode ? baselineWet : baselineDry;

                        if (baseline > 0.0)
                        {
                            double ratio = fuelUsed / baseline;
                            if (ratio < 0.5 || ratio > 1.5)
                            {
                                reject = true;
                                reason = string.Format("profileBracket (r={0:F2})", ratio);
                            }
                        }
                    }

                    if (!reject)
                    {
                        var window = isWetMode ? _recentWetFuelLaps : _recentDryFuelLaps;

                        if (isWetMode)
                            _freshWetSamplesInWindow++;
                        else
                            _freshDrySamplesInWindow++;

                        window.Add(fuelUsed);
                        while (window.Count > FuelWindowSize)
                        {
                            if (isWetMode && _hasActiveWetSeed)
                            {
                                window.RemoveAt(0);
                                _hasActiveWetSeed = false;
                            }
                            else if (!isWetMode && _hasActiveDrySeed)
                            {
                                window.RemoveAt(0);
                                _hasActiveDrySeed = false;
                            }
                            else
                            {
                                window.RemoveAt(0);
                                if (isWetMode && _freshWetSamplesInWindow > 0)
                                    _freshWetSamplesInWindow--;
                                else if (!isWetMode && _freshDrySamplesInWindow > 0)
                                    _freshDrySamplesInWindow--;
                            }
                        }

                        if (isWetMode)
                        {
                            _avgWetFuelPerLap = window.Average();
                            _validWetLaps = window.Count;
                            if (window.Count > 0)
                                _minWetFuelPerLap = window.Min();

                            // Max tracking with looser bounds [0.7, 1.8] × baseline
                            var (_, baselineWet) = GetProfileFuelBaselines();
                            double baseline = baselineWet > 0 ? baselineWet : _avgWetFuelPerLap;
                            if (baseline > 0 && fuelUsed > _maxWetFuelPerLap)
                            {
                                double r = fuelUsed / baseline;
                                if (r >= 0.7 && r <= 1.8)
                                    _maxWetFuelPerLap = fuelUsed;
                            }
                        }
                        else
                        {
                            _avgDryFuelPerLap = window.Average();
                            _validDryLaps = window.Count;
                            if (window.Count > 0)
                                _minDryFuelPerLap = window.Min();

                            var (baselineDry, _) = GetProfileFuelBaselines();
                            double baseline = baselineDry > 0 ? baselineDry : _avgDryFuelPerLap;
                            if (baseline > 0 && fuelUsed > _maxDryFuelPerLap)
                            {
                                double r = fuelUsed / baseline;
                                if (r >= 0.7 && r <= 1.8)
                                    _maxDryFuelPerLap = fuelUsed;
                            }
                        }

                        // Choose mode-aware LiveFuelPerLap, but allow cross-mode fallback if only one side has data
                        LiveFuelPerLap = isWetMode
                            ? (_avgWetFuelPerLap > 0 ? _avgWetFuelPerLap : _avgDryFuelPerLap)
                            : (_avgDryFuelPerLap > 0 ? _avgDryFuelPerLap : _avgWetFuelPerLap);

                        _usingFallbackFuelProfile = false;
                        Confidence = ComputeFuelModelConfidence(isWetMode);

                        // Overall confidence is computed in its getter from Confidence + PaceConfidence

                        FuelCalculator?.SetLiveFuelPerLap(LiveFuelPerLap);
                        FuelCalculator?.SetLiveConfidenceLevels(Confidence, PaceConfidence, OverallConfidence);

                        // Update session max for current mode if available
                        double maxForMode = isWetMode ? _maxWetFuelPerLap : _maxDryFuelPerLap;
                        if (maxForMode > 0)
                        {
                            _maxFuelPerLapSession = maxForMode;
                            FuelCalculator?.SetMaxFuelPerLap(_maxFuelPerLapSession);
                        }

                        FuelCalculator?.SetLiveFuelWindowStats(
                            _avgDryFuelPerLap, _minDryFuelPerLap, _maxDryFuelPerLap, _validDryLaps,
                            _avgWetFuelPerLap, _minWetFuelPerLap, _maxWetFuelPerLap, _validWetLaps);

                        // Keep profile’s dry fuel updated from stable dry data only
                        if (!isWetMode && _validDryLaps >= 3 && ActiveProfile != null)
                        {
                            var trackRecord = ActiveProfile.FindTrack(CurrentTrackKey);
                            if (trackRecord != null)
                            {
                                trackRecord.AvgFuelPerLapDry = _avgDryFuelPerLap;
                                trackRecord.MarkFuelUpdated("Telemetry fuel");
                            }
                        }

                    }
                    else
                    {
                        if (string.IsNullOrEmpty(reason))
                        {
                            reason = "rejected";
                        }
                    }

                    string fuelReasonForLog = reject
                        ? reason
                        : (string.IsNullOrEmpty(reason) ? "accepted" : reason);

                    LogLapCrossingSummary(
                        completedLapsNow,
                        lastLapSec,
                        !paceReject,
                        paceReason,
                        paceBaselineLog,
                        paceDeltaLog,
                        Pace_StintAvgLapTimeSec,
                        Pace_Last5LapAvgSec,
                        PaceConfidence,
                        leaderLastLapSec,
                        currentAvgLeader,
                        currentLeaderCount,
                        fuelUsed,
                        !reject,
                        fuelReasonForLog,
                        isWetMode,
                        LiveFuelPerLap,
                        _validDryLaps,
                        _validWetLaps,
                        _maxFuelPerLapSession,
                        Confidence,
                        OverallConfidence,
                        pitTripActive);

                    // Per-lap resets for next lap
                    if (pitTripActive)
                    {
                        _lapsSincePitExit = 0;
                    }
                    else if (_lapsSincePitExit < int.MaxValue)
                    {
                        _lapsSincePitExit++;
                    }

                    _wasInPitThisLap = false;
                    _hadOffTrackThisLap = false;
                    _latchedIncidentReason = null;
                    _lastCompletedFuelLap = completedLapsNow;
                }

                // Start the next lap’s measurement window
                _lapStartFuel = currentFuel;
            }

            // If we haven’t accumulated any accepted laps yet, fall back to SimHub’s estimator
            if ((_validDryLaps + _validWetLaps) == 0)
            {
                LiveFuelPerLap = fallbackFuelPerLap;
                _usingFallbackFuelProfile = true;
                Confidence = 0;
                FuelCalculator?.SetLiveConfidenceLevels(Confidence, PaceConfidence, OverallConfidence);

                if (LiveFuelPerLap > 0)
                    FuelCalculator?.OnLiveFuelPerLapUpdated();
            }

            UpdateStableFuelPerLap(isWetMode, fallbackFuelPerLap);

            // --- 3) Core dashboard properties (guarded by a valid consumption rate) ---
            double requestedAddLitresForSmooth = 0.0;
            double fuelPerLapForCalc = (_stableFuelPerLap > 0.0) ? _stableFuelPerLap : LiveFuelPerLap;

            if (fuelPerLapForCalc <= 0)
            {
                LiveLapsRemainingInRace = 0;
                LiveLapsRemainingInRace_Stable = 0;
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
                Pit_FuelSaveDeltaAfterStop = 0;
                Pit_PushDeltaAfterStop = 0;
                Pit_StopsRequiredToEnd = 0;

                PushFuelPerLap = 0;
                DeltaLapsIfPush = 0;
                CanAffordToPush = false;

                FuelSaveFuelPerLap = 0;
                LiveProjectedDriveTimeAfterZero = 0;
                LiveProjectedDriveSecondsRemaining = 0;

                Pace_StintAvgLapTimeSec = 0.0;
                Pace_Last5LapAvgSec = 0.0;
                Pace_LeaderDeltaToPlayerSec = 0.0;
                PaceConfidence = 0;
                FuelCalculator?.SetLiveLapPaceEstimate(0, 0);
                FuelCalculator?.SetLiveConfidenceLevels(Confidence, PaceConfidence, OverallConfidence);
            }
            else
            {
                LapsRemainingInTank = currentFuel / fuelPerLapForCalc;

                double simLapsRemaining = Convert.ToDouble(
                    PluginManager.GetPropertyValue("IRacingExtraProperties.iRacing_LapsRemainingFloat") ?? 0.0
                );

                bool isTimedRace = !double.IsNaN(sessionTimeRemain);
                double projectionLapSeconds = GetProjectionLapSeconds(data);
                double projectedDriveAfterZero = FuelProjectionMath.EstimateDriveTimeAfterZero(
                    sessionTime,
                    sessionTimeRemain,
                    projectionLapSeconds,
                    FuelCalculator?.StrategyDriverExtraSecondsAfterZero ?? 0.0,
                    _timerZeroSeen,
                    _timerZeroSessionTime);

                LiveProjectedDriveTimeAfterZero = projectedDriveAfterZero;
                double projectedLapsRemaining = ComputeProjectedLapsRemaining(simLapsRemaining, projectionLapSeconds, sessionTimeRemain, projectedDriveAfterZero);

                if (projectedLapsRemaining > 0.0)
                {
                    LiveLapsRemainingInRace = projectedLapsRemaining;
                    LiveLapsRemainingInRace_Stable = LiveLapsRemainingInRace;

                    if (ShouldLogProjection(simLapsRemaining, projectedLapsRemaining))
                    {
                        LogProjectionDifference(simLapsRemaining, projectedLapsRemaining, projectionLapSeconds);
                    }
                }
                else
                {
                    LiveLapsRemainingInRace = simLapsRemaining;
                    LiveLapsRemainingInRace_Stable = LiveLapsRemainingInRace;
                }

                double fuelNeededToEnd = LiveLapsRemainingInRace * fuelPerLapForCalc;
                DeltaLaps = LapsRemainingInTank - LiveLapsRemainingInRace;

                // Raw target fuel per lap if we're short on fuel
                double rawTargetFuelPerLap = (DeltaLaps < 0 && LiveLapsRemainingInRace > 0)
                    ? currentFuel / LiveLapsRemainingInRace
                    : 0.0;

                // Apply 10% saving guard: don't assume better than 10% below live average
                if (rawTargetFuelPerLap > 0.0 && fuelPerLapForCalc > 0.0)
                {
                    double minAllowed = fuelPerLapForCalc * 0.90; // max 10% fuel saving
                    TargetFuelPerLap = (rawTargetFuelPerLap < minAllowed)
                        ? minAllowed
                        : rawTargetFuelPerLap;
                }
                else
                {
                    TargetFuelPerLap = 0.0;
                }

                // Pit math
                Pit_TotalNeededToEnd = fuelNeededToEnd;
                Pit_NeedToAdd = Math.Max(0, fuelNeededToEnd - currentFuel);
                double fuelToRequest = Convert.ToDouble(
                    PluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PitSvFuel") ?? 0.0);

                if (!_isRefuelSelected)
                {
                    fuelToRequest = 0.0;
                }

                // Use the MaxTankCapacity already surfaced by the Fuel Calculator so we respect
                // iRacing's percentage-based tank limits. Fall back to the live/session max if
                // the calculator has not established a value yet.
                double maxTankCapacity = FuelCalculator?.MaxFuelOverride ?? 0.0;
                if (maxTankCapacity <= 0)
                {
                    double sessionMaxFuel = LiveCarMaxFuel > 0 ? LiveCarMaxFuel : maxFuel;
                    if (LiveCarMaxFuel > 0 && maxFuel > 0)
                        sessionMaxFuel = Math.Min(LiveCarMaxFuel, maxFuel);

                    maxTankCapacity = sessionMaxFuel;
                }

                double requestedAddLitres = Math.Max(0, fuelToRequest);
                requestedAddLitresForSmooth = requestedAddLitres;
                Pit_TankSpaceAvailable = Math.Max(0, maxTankCapacity - currentFuel);

                double safeFuelRequest = requestedAddLitres;
                Pit_WillAdd = Math.Min(safeFuelRequest, Pit_TankSpaceAvailable);

                Pit_FuelOnExit = currentFuel + Pit_WillAdd;
                bool isWetModeNow = FuelCalculator?.IsWet ?? false;
                double fuelSaveRate = isWetModeNow ? _minWetFuelPerLap : _minDryFuelPerLap;
                if (fuelSaveRate <= 0.0 && fuelPerLapForCalc > 0.0)
                {
                    fuelSaveRate = fuelPerLapForCalc * 0.97; // light saving fallback
                }

                FuelSaveFuelPerLap = fuelSaveRate;

                Pit_DeltaAfterStop = (fuelPerLapForCalc > 0)
                    ? (Pit_FuelOnExit / fuelPerLapForCalc) - LiveLapsRemainingInRace
                    : 0;

                Pit_FuelSaveDeltaAfterStop = (fuelSaveRate > 0)
                    ? (Pit_FuelOnExit / fuelSaveRate) - LiveLapsRemainingInRace
                    : 0;

                // Pit window logic – single-stop only, based on requested MFD refuel amount and
                // the effective tank capacity shared with the Fuel tab's detected max.
                int strategyRequiredStops = FuelCalculator?.RequiredPitStops ?? 0;
                int stopsRequired = strategyRequiredStops;
                if (stopsRequired <= 0 && maxTankCapacity > 0)
                {
                    stopsRequired = (int)Math.Ceiling((fuelNeededToEnd - currentFuel) / maxTankCapacity);
                }

                Pit_StopsRequiredToEnd = Math.Max(0, stopsRequired);

                bool isSingleStopStrategy = strategyRequiredStops == 1;
                bool hasValidFuelPerLap = fuelPerLapForCalc > 0.0;
                bool hasRequestedAdd = requestedAddLitres > 0.0;
                bool hasTankCapacity = maxTankCapacity > 0.0;

                // CompletedLaps is decimal? -> normalize to double and translate to human lap number
                double completedLaps = Convert.ToDouble(data.NewData?.CompletedLaps ?? 0m);
                int currentLapNumber = (int)Math.Max(0, Math.Floor(completedLaps) + 1);

                if (!isSingleStopStrategy || !_isRefuelSelected || !hasValidFuelPerLap || !hasRequestedAdd || !hasTankCapacity)
                {
                    IsPitWindowOpen = false;
                    PitWindowOpeningLap = 0;
                }
                else
                {
                    double tankSpace = Pit_TankSpaceAvailable;

                    if (tankSpace >= requestedAddLitres)
                    {
                        IsPitWindowOpen = true;
                        // Use the in-progress lap number (completed laps + 1) for the opening marker.
                        PitWindowOpeningLap = currentLapNumber;
                    }
                    else
                    {
                        double fuelToBurn = requestedAddLitres - tankSpace;
                        int lapsToOpen = (int)Math.Ceiling(fuelToBurn / fuelPerLapForCalc);
                        if (lapsToOpen < 0) lapsToOpen = 0;

                        PitWindowOpeningLap = currentLapNumber + lapsToOpen;
                        IsPitWindowOpen = false;
                    }
                }

                // --- Push / max-burn guidance ---
                double pushFuel = 0.0;
                if (_maxFuelPerLapSession > 0.0 && _maxFuelPerLapSession >= fuelPerLapForCalc)
                {
                    pushFuel = _maxFuelPerLapSession;
                }
                else
                {
                    pushFuel = fuelPerLapForCalc * 1.02; // fallback: +2% if we don't have a proper max yet
                }

                PushFuelPerLap = pushFuel;

                if (pushFuel > 0.0)
                {
                    double lapsRemainingIfPush = currentFuel / pushFuel;
                    DeltaLapsIfPush = lapsRemainingIfPush - LiveLapsRemainingInRace;
                    CanAffordToPush = DeltaLapsIfPush >= 0.0;

                    Pit_PushDeltaAfterStop = (Pit_FuelOnExit > 0.0)
                        ? (Pit_FuelOnExit / pushFuel) - LiveLapsRemainingInRace
                        : 0.0;
                }
                else
                {
                    DeltaLapsIfPush = 0.0;
                    CanAffordToPush = false;
                    Pit_PushDeltaAfterStop = 0.0;
                }
            }

            LiveLapsRemainingInRace_Stable = LiveLapsRemainingInRace;

            UpdateSmoothedFuelOutputs(requestedAddLitresForSmooth);

            // --- 4) Update "last" values for next tick ---
            _lastFuelLevel = currentFuel;
            _lastLapDistPct = rawLapPct; // keep original scale; we normalize on read
            if (_lapStartFuel < 0) _lapStartFuel = currentFuel;
        }

        // --- Settings / Car Profiles ---

        private string _currentCarModel = string.Empty;
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

        public string CurrentTrackName { get; private set; } = string.Empty;
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

        // Save the refuel rate into the active car profile and persist profiles.json
        public void SaveRefuelRateToActiveProfile(double rateLps)
        {
            try
            {
                if (rateLps > 0 && ActiveProfile != null)
                {
                    ActiveProfile.RefuelRate = rateLps;   // property already exists on CarProfile
                    ProfilesViewModel?.SaveProfiles();    // persist immediately
                    SimHub.Logging.Current.Info($"[Profiles] RefuelRate saved for '{ActiveProfile.ProfileName}': {rateLps:F3} L/s");
                }
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Info($"[Profiles] SaveRefuelRateToActiveProfile failed: {ex.Message}");
            }
        }

        public string CurrentTrackKey { get; private set; } = string.Empty;

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

        private void RegisterMsgCxPress()
        {
            _msgCxPressed = true;
            _msgCxCooldownTimer.Restart();
        }

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
        private string _lastSessionType = "";          // used by auto-dash & UI
        private string _lastFuelSessionType = "";      // used only by fuel model seeding

        private string _lastSeenCar = "";
        private string _lastSeenTrack = "";
        private string _lastSnapshotCar = string.Empty;
        private string _lastSnapshotTrack = string.Empty;
        private double _lastLapTimeSec = 0.0;   // last completed lap time
        private int _lastSavedLap = -1;   // last completedLaps value we saved against


        // --- Session Launch RPM Tracker ---
        private readonly List<double> _sessionLaunchRPMs = new List<double>();

        // --- Light schedulers to throttle non-critical work ---
        private readonly System.Diagnostics.Stopwatch _poll250ms = new System.Diagnostics.Stopwatch();  // ~4 Hz
        private readonly System.Diagnostics.Stopwatch _poll500ms = new System.Diagnostics.Stopwatch();  // ~2 Hz

        // --- Already added earlier for MaxFuel throttling ---
        private double _lastAnnouncedMaxFuel = -1;

        // ==== Refuel learning state (hardened) ====
        private bool _isRefuelling = false;
        private double _refuelStartFuel = 0.0;
        private double _refuelStartTime = 0.0;

        // Hysteresis / debounce
        private double _refuelLastFuel = 0.0;         // last sample
        private double _refuelWindowStart = 0.0;      // window used to decide "start"
        private double _refuelWindowRise = 0.0;       // liters accumulated in start window
        private double _refuelLastRiseTime = 0.0;     // sessionTime when we last saw a positive rise

        // Tunables (conservative defaults)
        private const double FuelNoiseEps = 0.02;  // ignore < 0.02 L ticks
        private const double StartWindowSec = 0.50;  // require rise inside this window
        private const double StartRiseLiters = 0.40;  // need ≥ 0.4 L to start
        private const double EndIdleSec = 0.50;  // stop if no rise for this long
        private const double MaxDeltaPerTickLit = 2.00;  // clamp corrupted spikes
        private const double MinValidAddLiters = 1.00;  // discard tiny fills
        private const double MinValidDurSec = 2.00;  // discard ultra-short
        private const double MinRateLps = 0.05;  // plausible range
        private const double MaxRateLps = 10.0;
        // --- Refuel learning smoothing + cooldown ---
        private double _refuelRateEmaLps = 0.0;           // smoothed learned rate (EMA)
        private double _refuelLearnCooldownEnd = 0.0;     // sessionTime when we can learn again

        private const double EmaAlpha = 0.35;             // 0..1; higher = follow raw rate more
        private const double LearnCooldownSec = 20.0;     // block new learn saves for N seconds

        private double _lastFuel = 0.0;

        // ---Temporary for Testing Purposes ---

        // --- Dual Clutch Placeholder (commented out) ---
        // private double _clutchLeft = 0.0;
        // private double _clutchRight = 0.0;
        // private double _virtualClutch = 0.0;

        // ---- SimHub publish controls ----------------------------------------------
        internal static class SimhubPublish
        {
            public static bool VERBOSE = false;
        }

        private void AttachCore(string name, Func<object> getter) => this.AttachDelegate(name, getter);
        private void AttachVerbose(string name, Func<object> getter)
        {
            if (SimhubPublish.VERBOSE) this.AttachDelegate(name, getter);
        }

        public void Init(PluginManager pluginManager)
        {
            // --- INITIALIZATION ---
            this.PluginManager = pluginManager;
            Settings = this.ReadCommonSettings<LaunchPluginSettings>("GlobalSettings_V2", () => new LaunchPluginSettings());
#if DEBUG
            FuelProjectionMath.RunSelfTests();
#endif
            // The Action for "Apply to Live" in the Profiles tab is now simplified: just update the ActiveProfile
            ProfilesViewModel = new ProfilesManagerViewModel(
                this.PluginManager,
                (profile) =>
                {
                    // 1) Switch active profile
                    this.ActiveProfile = profile;

                    // 2) Refresh anything that reads profile fuel/pace
                    this.FuelCalculator?.ForceProfileDataReload();

                    // Log so we can confirm it ran
                    SimHub.Logging.Current.Info("[Profiles] Applied profile to live and refreshed Fuel.");
                },
                () => this.CurrentCarModel,
                () => this.CurrentTrackKey
            );


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
            ResetFinishTimingState();
            _pit?.ResetPitPhaseState();

            // --- DELEGATES FOR LIVE FUEL CALCULATOR (CORE) ---
            AttachCore("Fuel.LiveFuelPerLap", () => LiveFuelPerLap);
            AttachCore("Fuel.LiveFuelPerLap_Stable", () => LiveFuelPerLap_Stable);
            AttachCore("Fuel.LiveFuelPerLap_StableSource", () => LiveFuelPerLap_StableSource);
            AttachCore("Fuel.LiveFuelPerLap_StableConfidence", () => LiveFuelPerLap_StableConfidence);
            AttachCore("Fuel.LiveLapsRemainingInRace", () => LiveLapsRemainingInRace);
            AttachCore("Fuel.LiveLapsRemainingInRace_S", () => LiveLapsRemainingInRace_S);
            AttachCore("Fuel.LiveLapsRemainingInRace_Stable", () => LiveLapsRemainingInRace_Stable);
            AttachCore("Fuel.LiveLapsRemainingInRace_Stable_S", () => LiveLapsRemainingInRace_Stable_S);
            AttachCore("Fuel.DeltaLaps", () => DeltaLaps);
            AttachCore("Fuel.TargetFuelPerLap", () => TargetFuelPerLap);
            AttachCore("Fuel.IsPitWindowOpen", () => IsPitWindowOpen);
            AttachCore("Fuel.PitWindowOpeningLap", () => PitWindowOpeningLap);
            AttachCore("Fuel.LapsRemainingInTank", () => LapsRemainingInTank);
            AttachCore("Fuel.Confidence", () => Confidence);
            AttachCore("Fuel.PushFuelPerLap", () => PushFuelPerLap);
            AttachCore("Fuel.FuelSavePerLap", () => FuelSaveFuelPerLap);
            AttachCore("Fuel.DeltaLapsIfPush", () => DeltaLapsIfPush);
            AttachCore("Fuel.CanAffordToPush", () => CanAffordToPush);
            AttachCore("Fuel.Pit.TotalNeededToEnd", () => Pit_TotalNeededToEnd);
            AttachCore("Fuel.Pit.TotalNeededToEnd_S", () => Pit_TotalNeededToEnd_S);
            AttachCore("Fuel.Pit.NeedToAdd", () => Pit_NeedToAdd);
            AttachCore("Fuel.Pit.TankSpaceAvailable", () => Pit_TankSpaceAvailable);
            AttachCore("Fuel.Pit.WillAdd", () => Pit_WillAdd);
            AttachCore("Fuel.Pit.DeltaAfterStop", () => Pit_DeltaAfterStop);
            AttachCore("Fuel.Pit.DeltaAfterStop_S", () => Pit_DeltaAfterStop_S);
            AttachCore("Fuel.Pit.FuelSaveDeltaAfterStop", () => Pit_FuelSaveDeltaAfterStop);
            AttachCore("Fuel.Pit.FuelSaveDeltaAfterStop_S", () => Pit_FuelSaveDeltaAfterStop_S);
            AttachCore("Fuel.Pit.PushDeltaAfterStop", () => Pit_PushDeltaAfterStop);
            AttachCore("Fuel.Pit.PushDeltaAfterStop_S", () => Pit_PushDeltaAfterStop_S);
            AttachCore("Fuel.Pit.FuelOnExit", () => Pit_FuelOnExit);
            AttachCore("Fuel.Pit.StopsRequiredToEnd", () => Pit_StopsRequiredToEnd);
            AttachCore("Fuel.Live.RefuelRate_Lps", () => FuelCalculator?.EffectiveRefuelRateLps ?? 0.0);
            AttachCore("Fuel.Live.TireChangeTime_S", () => GetEffectiveTireChangeTimeSeconds());
            AttachCore("Fuel.Live.PitLaneLoss_S", () => FuelCalculator?.PitLaneTimeLoss ?? 0.0);
            AttachCore("Fuel.Live.TotalStopLoss", () => CalculateTotalStopLossSeconds());
            AttachCore("Fuel.Live.DriveTimeAfterZero", () => LiveProjectedDriveTimeAfterZero);
            AttachCore("Fuel.ProjectionLapTime_Stable", () => ProjectionLapTime_Stable);
            AttachCore("Fuel.ProjectionLapTime_StableSource", () => ProjectionLapTime_StableSource);
            AttachCore("Fuel.Live.ProjectedDriveSecondsRemaining", () => LiveProjectedDriveSecondsRemaining);

            // --- Pace metrics (CORE) ---
            AttachCore("Pace.StintAvgLapTimeSec", () => Pace_StintAvgLapTimeSec);
            AttachCore("Pace.Last5LapAvgSec", () => Pace_Last5LapAvgSec);
            AttachCore("Pace.LeaderAvgLapTimeSec", () => LiveLeaderAvgPaceSeconds);
            AttachCore("Pace.LeaderDeltaToPlayerSec", () => Pace_LeaderDeltaToPlayerSec);
            AttachCore("Pace.PaceConfidence", () => PaceConfidence);
            AttachCore("Pace.OverallConfidence", () => OverallConfidence);

            // --- Pit time-loss (finals kept CORE; raw & debug VERBOSE) ---
            AttachCore("Pit.LastDirectTravelTime", () => _pit.LastDirectTravelTime);
            AttachCore("Pit.LastTotalPitCycleTimeLoss", () => _pit.LastTotalPitCycleTimeLoss);
            AttachCore("Pit.LastPaceDeltaNetLoss", () => _pit.LastPaceDeltaNetLoss);
            AttachVerbose("Pit.Debug.TimeOnPitRoad", () => _pit.TimeOnPitRoad.TotalSeconds);
            
            // AttachVerbose("Pit.Debug.LastTimeOnPitRoad",  () => _pit.TimeOnPitRoad.TotalSeconds);
            AttachVerbose("Pit.Debug.LastPitStopDuration", () => _pit?.PitStopElapsedSec ?? 0.0);

            // --- PIT TEST / RAW (all VERBOSE) ---
            AttachVerbose("Lala.Pit.AvgPaceUsedSec", () => _pitDbg_AvgPaceUsedSec);
            AttachVerbose("Lala.Pit.AvgPaceSource", () => _pitDbg_AvgPaceSource);
            AttachVerbose("Lala.Pit.Raw.PitLapSec", () => _pitDbg_RawPitLapSec);
            AttachVerbose("Lala.Pit.Raw.DTLFormulaSec", () => _pitDbg_RawDTLFormulaSec);
            AttachVerbose("Lala.Pit.InLapSec", () => _pitDbg_InLapSec);
            AttachVerbose("Lala.Pit.OutLapSec", () => _pitDbg_OutLapSec);
            AttachVerbose("Lala.Pit.DeltaInSec", () => _pitDbg_DeltaInSec);
            AttachVerbose("Lala.Pit.DeltaOutSec", () => _pitDbg_DeltaOutSec);
            AttachVerbose("Lala.Pit.DriveThroughLossSec", () => _pit?.LastTotalPitCycleTimeLoss ?? 0.0);
            AttachVerbose("Lala.Pit.DirectTravelSec", () => _pit?.LastDirectTravelTime ?? 0.0);
            AttachVerbose("Lala.Pit.StopSeconds", () => _pit?.PitStopDuration.TotalSeconds ?? 0.0);

            // Service stop loss = DTL + stationary stop (VERBOSE)
            AttachVerbose("Lala.Pit.ServiceStopLossSec", () =>
            {
                var dtl = _pit?.LastTotalPitCycleTimeLoss ?? 0.0;
                var stop = _pit?.PitStopDuration.TotalSeconds ?? 0.0;
                var val = dtl + stop;
                return val < 0 ? 0.0 : val;
            });

            // Profile lane loss + “last saved” provenance (VERBOSE)
            AttachVerbose("Lala.Pit.Profile.PitLaneLossSec", () =>
            {
                var ts = ActiveProfile?.FindTrack(CurrentTrackKey);
                return ts?.PitLaneLossSeconds ?? 0.0;
            });
            AttachVerbose("Lala.Pit.CandidateSavedSec", () => _pitDbg_CandidateSavedSec);
            AttachVerbose("Lala.Pit.CandidateSource", () => _pitDbg_CandidateSource);

            // --- PitLite (test dash; VERBOSE) ---
            AttachVerbose("PitLite.InLapSec", () => _pitLite?.InLapSec ?? 0.0);
            AttachVerbose("PitLite.OutLapSec", () => _pitLite?.OutLapSec ?? 0.0);
            AttachVerbose("PitLite.DeltaInSec", () => _pitLite?.DeltaInSec ?? 0.0);
            AttachVerbose("PitLite.DeltaOutSec", () => _pitLite?.DeltaOutSec ?? 0.0);
            AttachVerbose("PitLite.TimePitLaneSec", () => _pitLite?.TimePitLaneSec ?? 0.0);
            AttachVerbose("PitLite.TimePitBoxSec", () => _pitLite?.TimePitBoxSec ?? 0.0);
            AttachVerbose("PitLite.DirectSec", () => _pitLite?.DirectSec ?? 0.0);
            AttachVerbose("PitLite.DTLSec", () => _pitLite?.DTLSec ?? 0.0);
            AttachVerbose("PitLite.Status", () => _pitLite?.Status.ToString() ?? "None");
            AttachCore("PitLite.Live.TimeOnPitRoadSec", () => _pit?.TimeOnPitRoad.TotalSeconds ?? 0.0);
            AttachCore("PitLite.Live.TimeInBoxSec", () => _pit?.PitStopElapsedSec ?? 0.0);
            AttachVerbose("PitLite.CurrentLapType", () => _pitLite?.CurrentLapType.ToString() ?? "Normal");
            AttachVerbose("PitLite.LastLapType", () => _pitLite?.LastLapType.ToString() ?? "None");
            AttachCore("PitLite.TotalLossSec", () => _pitLite?.TotalLossSec ?? 0.0);
            AttachVerbose("PitLite.LossSource", () => _pitLite?.TotalLossSource ?? "None");
            AttachVerbose("PitLite.LastSaved.Sec", () => _pitDbg_CandidateSavedSec);
            AttachVerbose("PitLite.LastSaved.Source", () => _pitDbg_CandidateSource ?? "none");
            AttachCore("PitLite.TotalLossPlusBoxSec", () => _pitLite?.TotalLossPlusBoxSec ?? 0.0);

            // Live edge flags (VERBOSE)
            AttachVerbose("PitLite.Live.SeenEntryThisLap", () => _pitLite?.EntrySeenThisLap ?? false);
            AttachVerbose("PitLite.Live.SeenExitThisLap", () => _pitLite?.ExitSeenThisLap ?? false);

            // --- DELEGATES FOR DASHBOARD STATE & OVERLAYS (CORE) ---
            AttachCore("CurrentDashPage", () => Screens.CurrentPage);
            AttachCore("DashControlMode", () => Screens.Mode);
            AttachCore("FalseStartDetected", () => _falseStartDetected);
            AttachCore("LastSessionType", () => _lastSessionType);
            AttachCore("Race.LeaderHasFinished", () => LeaderHasFinished);
            AttachCore("MsgCxPressed", () => _msgCxPressed);
            AttachCore("PitScreenActive", () => _pitScreenActive);

            AttachCore("RejoinAlertReasonCode", () => (int)_rejoinEngine.CurrentLogicCode);
            AttachCore("RejoinAlertReasonName", () => _rejoinEngine.CurrentLogicCode.ToString());
            AttachCore("RejoinAlertMessage", () => _rejoinEngine.CurrentMessage);
            AttachCore("RejoinIsExitingPits", () => _rejoinEngine.IsExitingPits);
            AttachCore("RejoinCurrentPitPhaseName", () => _rejoinEngine.CurrentPitPhase.ToString());
            AttachCore("RejoinCurrentPitPhase", () => (int)_rejoinEngine.CurrentPitPhase);
            // REMOVED: obsolete RejoinAssist_PitExitTime (always 0.0)
            // AttachCore("RejoinAssist_PitExitTime",         () => _rejoinEngine.PitExitTimerSeconds);

            AttachCore("RejoinThreatLevel", () => (int)_rejoinEngine.CurrentThreatLevel);
            AttachCore("RejoinThreatLevelName", () => _rejoinEngine.CurrentThreatLevel.ToString());
            AttachCore("RejoinTimeToThreat", () => _rejoinEngine.TimeToThreatSeconds);

            // --- LalaDash Options (CORE) ---
            AttachCore("LalaDashShowLaunchScreen", () => Settings.LalaDashShowLaunchScreen);
            AttachCore("LalaDashShowPitLimiter", () => Settings.LalaDashShowPitLimiter);
            AttachCore("LalaDashShowPitScreen", () => Settings.LalaDashShowPitScreen);
            AttachCore("LalaDashShowRejoinAssist", () => Settings.LalaDashShowRejoinAssist);
            AttachCore("LalaDashShowVerboseMessaging", () => Settings.LalaDashShowVerboseMessaging);
            AttachCore("LalaDashShowRaceFlags", () => Settings.LalaDashShowRaceFlags);
            AttachCore("LalaDashShowRadioMessages", () => Settings.LalaDashShowRadioMessages);
            AttachCore("LalaDashShowTraffic", () => Settings.LalaDashShowTraffic);

            // --- MsgDash Options (CORE) ---
            AttachCore("MsgDashShowLaunchScreen", () => Settings.MsgDashShowLaunchScreen);
            AttachCore("MsgDashShowPitLimiter", () => Settings.MsgDashShowPitLimiter);
            AttachCore("MsgDashShowPitScreen", () => Settings.MsgDashShowPitScreen);
            AttachCore("MsgDashShowRejoinAssist", () => Settings.MsgDashShowRejoinAssist);
            AttachCore("MsgDashShowVerboseMessaging", () => Settings.MsgDashShowVerboseMessaging);
            AttachCore("MsgDashShowRaceFlags", () => Settings.MsgDashShowRaceFlags);
            AttachCore("MsgDashShowRadioMessages", () => Settings.MsgDashShowRadioMessages);
            AttachCore("MsgDashShowTraffic", () => Settings.MsgDashShowTraffic);

            // --- Manual Timeout (CORE) ---
            AttachCore("ManualTimeoutRemaining", () =>
            {
                if (_manualPrimedStartedAt == DateTime.MinValue) return "";
                if (!IsLaunchActive) return "";
                var remaining = TimeSpan.FromSeconds(30) - (DateTime.Now - _manualPrimedStartedAt);
                return remaining.TotalSeconds > 0 ? remaining.TotalSeconds.ToString("F0") : "0";
            });

            // --- LAUNCH CONTROL (CORE) ---
            AttachCore("ActualRPMAtClutchRelease", () => _actualRpmAtClutchRelease.ToString("F0"));
            AttachCore("ActualThrottleAtClutchRelease", () => _actualThrottleAtClutchRelease);
            AttachCore("AntiStallActive", () => _isAntiStallActive);
            AttachCore("AntiStallDetectedInLaunch", () => _antiStallDetectedThisRun);
            AttachCore("AvgSessionLaunchRPM", () => _avgSessionLaunchRPM.ToString("F0"));
            AttachCore("BitePointInTargetRange", () => _bitePointInTargetRange);
            AttachCore("BoggedDown", () => _boggedDown);
            AttachCore("BogDownFactorPercent", () => ActiveProfile.BogDownFactorPercent);
            AttachCore("ClutchReleaseDelta", () => _clutchReleaseDelta.ToString("F0"));
            AttachCore("ClutchReleaseTime", () => _hasValidClutchReleaseData ? _clutchReleaseLastTime : 0);
            AttachCore("LastAvgLaunchRPM", () => _lastAvgSessionLaunchRPM);
            AttachCore("LastLaunchRPM", () => _lastLaunchRPM);
            AttachCore("LastMinRPM", () => _lastMinRPMDuringLaunch);
            AttachCore("LaunchModeActive", () => IsLaunchVisible);
            AttachCore("LaunchStateLabel", () => _currentLaunchState.ToString());
            AttachCore("LaunchStateCode", () => ((int)_currentLaunchState).ToString());
            AttachCore("LaunchRPM", () => _currentLaunchRPM);
            AttachCore("MaxTractionLoss", () => _maxTractionLossDuringLaunch);
            AttachCore("MinRPM", () => _minRPMDuringLaunch);
            AttachCore("OptimalBitePoint", () => ActiveProfile.TargetBitePoint);
            AttachCore("OptimalBitePointTolerance", () => ActiveProfile.BitePointTolerance);
            AttachCore("OptimalRPMTolerance", () => ActiveProfile.OptimalRPMTolerance.ToString("F0"));
            AttachCore("OptimalThrottleTolerance", () => ActiveProfile.OptimalThrottleTolerance.ToString("F0"));
            AttachCore("ReactionTime", () => _reactionTimeMs);
            AttachCore("RPMDeviationAtClutchRelease", () => _rpmDeviationAtClutchRelease.ToString("F0"));
            AttachCore("RPMInTargetRange", () => _rpmInTargetRange);
            AttachCore("TargetLaunchRPM", () => ActiveProfile.TargetLaunchRPM.ToString("F0"));
            AttachCore("TargetLaunchThrottle", () => ActiveProfile.TargetLaunchThrottle.ToString("F0"));
            AttachCore("ThrottleDeviationAtClutchRelease", () => _throttleDeviationAtClutchRelease);
            AttachCore("ThrottleInTargetRange", () => _throttleInTargetRange);
            AttachCore("ThrottleModulationDelta", () => _throttleModulationDelta);
            AttachCore("WheelSpinDetected", () => _wheelSpinDetected);
            AttachCore("ZeroTo100Delta", () => _zeroTo100Delta);
            AttachCore("ZeroTo100Time", () => _hasValidLaunchData ? _zeroTo100LastTime : 0);

            // --- TESTING / DEBUGGING (VERBOSE) ---
            // REMOVED: MSG.PitPhaseDebug (old vs new) — PitEngine is single source of truth now.
            // AttachVerbose("MSG.PitPhaseDebug", ...);

            // --- Link engines (unchanged) ---
            _rejoinEngine = new RejoinAssistEngine(
                () => ActiveProfile.RejoinWarningMinSpeed,
                () => ActiveProfile.RejoinWarningLingerTime,
                () => ActiveProfile.SpinYawRateThreshold / 10.0
            );

            _msgSystem = new MessagingSystem();
            AttachCore("MSG.OvertakeApproachLine", () => _msgSystem.OvertakeApproachLine);
            AttachCore("MSG.OvertakeWarnSeconds", () => ActiveProfile.TrafficApproachWarnSeconds);
            AttachCore("MSG.MsgCxTimeMessage", () => _msgSystem.MsgCxTimeMessage);
            AttachCore("MSG.MsgCxTimeVisible", () => _msgSystem.IsMsgCxTimeActive);
            AttachCore("MSG.MsgCxTimeSilenceRemaining", () => _msgSystem.MsgCxTimeSilenceRemainingSeconds);
            AttachCore("MSG.MsgCxStateMessage", () => _msgSystem.MsgCxStateMessage);
            AttachCore("MSG.MsgCxStateVisible", () => _msgSystem.IsMsgCxStateActive);
            AttachCore("MSG.MsgCxStateToken", () => _msgSystem.MsgCxStateToken);
            AttachCore("MSG.MsgCxActionMessage", () => _msgSystem.MsgCxActionMessage);
            AttachCore("MSG.MsgCxActionPulse", () => _msgSystem.MsgCxActionPulse);

            _pit = new PitEngine(() =>
            {
                var s = ActiveProfile.RejoinWarningLingerTime;
                if (double.IsNaN(s) || s < 0.5) s = 0.5;
                if (s > 10.0) s = 10.0;
                return s;
            });
            _pitLite = new PitCycleLite(_pit);
            _rejoinEngine.SetPitEngine(_pit);

            // --- New direct travel time property (CORE) ---
            AttachCore("Fuel.LastPitLaneTravelTime", () => LastDirectTravelTime);

        }

        private void Pit_OnValidPitStopTimeLossCalculated(double timeLossSeconds, string sourceFromPublisher)
        {
            // Guards
            if (ActiveProfile == null || string.IsNullOrEmpty(CurrentTrackKey))
            {
                SimHub.Logging.Current.Warn("LalaLaunch: Cannot save pit time loss – no active profile or track.");
                return;
            }

            // If we've already saved this exact DTL value, ignore repeat callers.
            if (sourceFromPublisher != null
                && sourceFromPublisher.Equals("dtl", StringComparison.OrdinalIgnoreCase)
                && Math.Abs(timeLossSeconds - _lastPitLossSaved) < 0.01)
            {
                return;
            }

            // 1) Prefer the number passed in (PitLite’s one-shot). If zero/invalid, fall back to Direct.
            double loss = Math.Max(0.0, timeLossSeconds);
            string src = (sourceFromPublisher ?? "").Trim().ToLowerInvariant();
            if (loss <= 0.0)
            {
                loss = Math.Max(0.0, _pit?.LastDirectTravelTime ?? 0.0);
                src = "direct";
            }

            // Debounce / override rules (keep your current behavior)
            var now = DateTime.UtcNow;
            bool justSaved = (now - _lastPitLossSavedAtUtc).TotalSeconds < 10.0;
            bool allowOverride = (src == "dtl" || src == "total") && _lastPitLossSource == "direct";

            if (!allowOverride)
            {
                if (justSaved && Math.Abs(loss - _lastPitLossSaved) < 0.01)
                    return;
            }

            // Round & persist
            double rounded = Math.Round(loss, 2);
            var trackRecord = ActiveProfile.EnsureTrack(CurrentTrackKey, CurrentTrackName);
            trackRecord.PitLaneLossSeconds = rounded;

            // NEW: persist source + timestamp for Profiles page
            trackRecord.PitLaneLossSource = src;                  // "dtl" or "direct"
            trackRecord.PitLaneLossUpdatedUtc = now;              // DateTime.UtcNow above

            // Publish to the live snapshot + Fuel tab immediately
            FuelCalculator?.SetLastPitDriveThroughSeconds(rounded);
            FuelCalculator?.ForceProfileDataReload();

            // Remember last save
            _lastPitLossSaved = rounded;
            _lastPitLossSavedAtUtc = DateTime.UtcNow;
            _lastPitLossSource = src;

            SimHub.Logging.Current.Info($"[Pit/Pace] Saved PitLaneLoss = {rounded:0.00}s ({src}).");
        }

        public bool SavePendingPitLaneLossIfAny(out string source, out double seconds)
        {
            source = "none";
            seconds = 0;

            // Defensive: only act if PitLite exists and can yield a candidate
            if (_pitLite == null) return false;

            if (_pitLite.TryGetFinishedOutlap(out var loss, out var src))
            {
                // Mirrors your existing per-tick consume+save path:
                Pit_OnValidPitStopTimeLossCalculated(loss, src);
                source = src;
                seconds = loss;
                SimHub.Logging.Current.Info($"[PitLite] Pit Lite Data used for DTL.");
                return true;
            }

            return false;
        }

        public void End(PluginManager pluginManager)
        {
            // --- Cleanup trace logger for current run ---
            _telemetryTraceLogger?.DiscardCurrentTrace();

            // Stop trace first
            _telemetryTraceLogger?.EndService();

            // Optionally discard only if you really want to delete last file on exit
            // _telemetryTraceLogger?.DiscardCurrentTrace();

            FuelCalculator?.PromptToSaveLiveFuelOnExit();

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

        private void ResetFinishTimingState()
        {
            _timerZeroSeen = false;
            _timerZeroSessionTime = double.NaN;
            _prevSessionTimeRemain = double.NaN;
            _leaderCheckeredSessionTime = double.NaN;
            _driverCheckeredSessionTime = double.NaN;
            _leaderFinishedSeen = false;
            _lastCompletedLapForFinish = -1;
            LeaderHasFinished = false;
        }


        #region Core Update Method

        private void PushLiveSnapshotIdentity()
        {
            string carName = (!string.IsNullOrWhiteSpace(CurrentCarModel) && !CurrentCarModel.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                ? CurrentCarModel
                : string.Empty;

            string trackLabel = (!string.IsNullOrWhiteSpace(CurrentTrackName) && !CurrentTrackName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                ? CurrentTrackName
                : (!string.IsNullOrWhiteSpace(CurrentTrackKey) && !CurrentTrackKey.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                    ? CurrentTrackKey
                    : string.Empty);

            if (FuelCalculator == null)
            {
                return;
            }

            if (carName == _lastSnapshotCar && trackLabel == _lastSnapshotTrack)
            {
                return;
            }

            FuelCalculator.SetLiveSession(carName, trackLabel);
            _lastSnapshotCar = carName;
            _lastSnapshotTrack = trackLabel;

            // Reset max fuel announcement throttle so the live display refreshes immediately for the new snapshot
            _lastAnnouncedMaxFuel = -1;
            if (LiveCarMaxFuel > 0)
            {
                FuelCalculator.UpdateLiveDisplay(LiveCarMaxFuel);
            }
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            // ==== New, Simplified Car & Track Detection ====
            // This is the function that needs to exist for the car model detection below
            string FirstNonEmpty(params object[] vals) => vals.Select(v => Convert.ToString(v)).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";
            try
            {
                string trackKey = Convert.ToString(pluginManager.GetPropertyValue("DataCorePlugin.GameData.TrackCode"));
                string trackDisplay = FirstNonEmpty(
                    pluginManager.GetPropertyValue("IRacingExtraProperties.iRacing_TrackDisplayName"),
                    pluginManager.GetPropertyValue("DataCorePlugin.GameData.TrackNameWithConfig"),
                    pluginManager.GetPropertyValue("DataCorePlugin.GameData.TrackName")
                );

                string carModel = FirstNonEmpty(
                    pluginManager.GetPropertyValue("DataCorePlugin.GameData.CarModel"),
                    pluginManager.GetPropertyValue("IRacingExtraProperties.iRacing_CarModel")
                );

                if (string.Equals(trackKey, "unknown", StringComparison.OrdinalIgnoreCase)) trackKey = string.Empty;
                if (string.Equals(trackDisplay, "unknown", StringComparison.OrdinalIgnoreCase)) trackDisplay = string.Empty;
                if (string.Equals(carModel, "unknown", StringComparison.OrdinalIgnoreCase)) carModel = string.Empty;

                if (!string.IsNullOrWhiteSpace(carModel))
                {
                    CurrentCarModel = carModel;
                }

                if (!string.IsNullOrWhiteSpace(trackKey))
                {
                    CurrentTrackKey = trackKey;
                }

                if (!string.IsNullOrWhiteSpace(trackDisplay))
                {
                    CurrentTrackName = trackDisplay;
                }

                PushLiveSnapshotIdentity();
            }
            catch (Exception ex) { SimHub.Logging.Current.Warn($"[LalaLaunch] Simplified Car/Track probe failed: {ex.Message}"); }

            if (_msgCxCooldownTimer.IsRunning && _msgCxCooldownTimer.ElapsedMilliseconds > 500)
            {
                _msgCxCooldownTimer.Reset();
                _msgCxPressed = false;
            }

            // --- MASTER GUARD CLAUSES ---
            if (Settings == null) return;
            if (!data.GameRunning || data.NewData == null) return;

            _isRefuelSelected = IsRefuelSelected(pluginManager);
            _isTireChangeSelected = IsAnyTireChangeSelected(pluginManager);

            // Pull raw session time from SimHub property engine so projections and refuel learning share the same values.
            double sessionTime = 0.0;
            try
            {
                sessionTime = Convert.ToDouble(
                    pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.SessionTime") ?? 0.0
                );
            }
            catch { sessionTime = 0.0; }

            double sessionTimeRemain = SafeReadDouble(pluginManager, "DataCorePlugin.GameRawData.Telemetry.SessionTimeRemain", double.NaN);

            string currentSessionTypeForConfidence = data.NewData?.SessionTypeName ?? string.Empty;
            string trackIdentityForConfidence =
                (!string.IsNullOrWhiteSpace(CurrentTrackKey) && !CurrentTrackKey.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                    ? CurrentTrackKey
                    : CurrentTrackName;

            if (!string.IsNullOrWhiteSpace(CurrentCarModel) && !string.IsNullOrWhiteSpace(trackIdentityForConfidence))
            {
                if (!string.Equals(CurrentCarModel, _confidenceCarModel, StringComparison.Ordinal) ||
                    !string.Equals(trackIdentityForConfidence, _confidenceTrackIdentity, StringComparison.Ordinal))
                {
                    ResetConfidenceForNewCombo(currentSessionTypeForConfidence);
                }
            }

            long currentSessionId = Convert.ToInt64(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.WeekendInfo.SessionID") ?? -1);
            if (currentSessionId != _lastSessionId)
            {
                // If we exited lane and the session ended before S/F, finalize once with PitLite’s one-shot.
                if (_pitLite != null && _pitLite.ConsumeCandidate(out var scLoss, out var scSrc))
                {
                    Pit_OnValidPitStopTimeLossCalculated(scLoss, scSrc);
                    // nothing else: ConsumeCandidate cleared the latch, sink de-dupe will ignore repeats
                }
                // Optional: if nothing latched, fall back to direct once.
                else if ((_pit?.LastDirectTravelTime ?? 0.0) > 0.0)
                {
                    Pit_OnValidPitStopTimeLossCalculated(_pit.LastDirectTravelTime, "direct");
                }

                _rejoinEngine.Reset();
                _pit.Reset();
                _pitLite?.ResetCycle();
                _pit?.ResetPitPhaseState();
                _currentCarModel = string.Empty;
                CurrentTrackName = string.Empty;
                CurrentTrackKey = string.Empty;
                _lastSeenCar = string.Empty;
                _lastSeenTrack = string.Empty;
                _lastSnapshotCar = string.Empty;
                _lastSnapshotTrack = string.Empty;
                _lastAnnouncedMaxFuel = -1;
                _lastSessionId = currentSessionId;
                FuelCalculator.ForceProfileDataReload();
                ResetFinishTimingState();
                ResetSmoothedOutputs();
                _pendingSmoothingReset = true;

                SimHub.Logging.Current.Info($"[LalaLaunch] Session start snapshot: Car='{CurrentCarModel}'  Track='{CurrentTrackName}'");
            }

            UpdateLiveSurfaceSummary(pluginManager);

            // --- Pit System Monitoring (needs tick granularity for phase detection) ---
            _pit.Update(data, pluginManager);
            // --- PitLite tick: after PitEngine update and baseline selection ---
            bool inLane = _pit?.IsOnPitRoad ?? (data.NewData.IsInPitLane != 0);
            int completedLaps = Convert.ToInt32(data.NewData?.CompletedLaps ?? 0);
            UpdateFinishTiming(
                pluginManager,
                data,
                sessionTime,
                sessionTimeRemain,
                completedLaps,
                currentSessionId,
                currentSessionTypeForConfidence);
            double lastLapSec = (data.NewData?.LastLapTime ?? TimeSpan.Zero).TotalSeconds;
            // IMPORTANT: give PitLite a *real* baseline pace.
            // Order: stable avg (from your fuel/baseline logic) → pit debug avg → profile avg → 0
            // --- Choose a stable baseline lap pace for PitLite ---
            // 1) Prefer the live, already-computed average we show on the dash
            double avgUsed = _pitDbg_AvgPaceUsedSec;

            // 2) If that’s not available yet (startup), fall back to profile average for this track
            if (avgUsed <= 0 && ActiveProfile != null)
            {
                try
                {
                    var tr =
                        ActiveProfile.ResolveTrackByNameOrKey(CurrentTrackKey) ??
                        ActiveProfile.ResolveTrackByNameOrKey(CurrentTrackName);

                    if (tr?.AvgLapTimeDry > 0)
                        avgUsed = tr.AvgLapTimeDry.Value / 1000.0; // ms -> s
                }
                catch { /* keep avgUsed as 0.0 if anything goes wrong */ }
            }

            _pitLite?.Update(inLane, completedLaps, lastLapSec, avgUsed);

            // --- Rejoin assist update & lap incident tracking ---
            _rejoinEngine?.Update(data, pluginManager, IsLaunchActive);
            if (_rejoinEngine != null && !_hadOffTrackThisLap)
            {
                var latchedReason = _rejoinEngine.CurrentLogicCode;
                if (!RejoinAssistEngine.IsSeriousIncidentReason(latchedReason))
                {
                    latchedReason = _rejoinEngine.DetectedReason;
                }

                if (RejoinAssistEngine.IsSeriousIncidentReason(latchedReason))
                {
                    _hadOffTrackThisLap = true;
                    _latchedIncidentReason = latchedReason;
                }
            }

            // === AUTO-LEARN REFUEL RATE FROM PIT BOX (hardened) ===
            double currentFuel = data.NewData?.Fuel ?? 0.0;

            bool inPitLaneFlag = (data.NewData?.IsInPitLane ?? 0) != 0;

            // Cooldown: avoid re-learning immediately after a save
            bool onCooldown = sessionTime < _refuelLearnCooldownEnd;
            if (onCooldown)
            {
                _lastFuel = currentFuel;   // keep last fuel fresh
                return;
            }

            // Clamp per-tick delta, ignore noise
            double delta = currentFuel - _refuelLastFuel;
            if (delta > MaxDeltaPerTickLit) delta = MaxDeltaPerTickLit;
            if (delta < -MaxDeltaPerTickLit) delta = -MaxDeltaPerTickLit;

            bool rising = delta > FuelNoiseEps;

            // Guard: ignore session start / garage / not in pits
            bool learningEnabled = sessionTime > 5.0 && inPitLaneFlag;

            // ---- Not refuelling yet: look for a “start” window
            if (learningEnabled && !_isRefuelling)
            {
                // Start or advance window
                if (_refuelWindowStart <= 0.0) _refuelWindowStart = sessionTime;

                if (rising) _refuelWindowRise += delta;

                // If window aged out, evaluate & maybe start
                double winAge = sessionTime - _refuelWindowStart;
                if (winAge >= StartWindowSec)
                {
                    if (_refuelWindowRise >= StartRiseLiters)
                    {
                        // Start refuel
                        _isRefuelling = true;
                        _refuelStartFuel = _refuelLastFuel;   // fuel level before rise
                        _refuelStartTime = _refuelWindowStart;
                        _refuelLastRiseTime = sessionTime;

                        SimHub.Logging.Current.Info($"[LalaLaunch] Refuel started at {_refuelStartTime:F1}s (Fuel={_refuelStartFuel:F1})");
                    }

                    // Reset window (whether we started or not)
                    _refuelWindowStart = sessionTime;
                    _refuelWindowRise = 0.0;
                }
            }
            // ---- Already refuelling: track and look for “end”
            else if (_isRefuelling)
            {
                if (rising) _refuelLastRiseTime = sessionTime;

                bool idleTooLong = (sessionTime - _refuelLastRiseTime) >= EndIdleSec;
                bool leftPit = !inPitLaneFlag;

                if (idleTooLong || leftPit)
                {
                    // Finalize using last positive-rise time for duration
                    double stopTime = _refuelLastRiseTime;
                    if (stopTime <= _refuelStartTime) stopTime = sessionTime; // fallback

                    double fuelAdded = currentFuel - _refuelStartFuel;
                    double duration = Math.Max(0.0, stopTime - _refuelStartTime);

                    if (fuelAdded >= MinValidAddLiters && duration >= MinValidDurSec)
                    {
                        double rate = fuelAdded / duration;
                        if (rate >= MinRateLps && rate <= MaxRateLps)
                        {
                            // Exponential moving average for stability
                            if (_refuelRateEmaLps <= 0.0) _refuelRateEmaLps = rate;
                            else _refuelRateEmaLps = (EmaAlpha * rate) + ((1.0 - EmaAlpha) * _refuelRateEmaLps);

                            var savedRate = _refuelRateEmaLps;

                            SaveRefuelRateToActiveProfile(savedRate);
                            FuelCalculator?.SetLastRefuelRate(savedRate);
                            _refuelLearnCooldownEnd = sessionTime + LearnCooldownSec;

                            SimHub.Logging.Current.Info(
                                $"[LaLaLaunch] Learned refuel rate (smoothed): {savedRate:F2} L/s  [raw {rate:F2}] (Δfuel={fuelAdded:F1}, t={duration:F1}s). " +
                                $"Cooldown until {_refuelLearnCooldownEnd:F1}s");
                        }

                    }

                    SimHub.Logging.Current.Info($"[LalaLaunch] Refuel ended at {stopTime:F1}s");

                    // Reset state
                    _isRefuelling = false;
                    _refuelStartFuel = 0.0;
                    _refuelStartTime = 0.0;
                    _refuelWindowStart = 0.0;
                    _refuelWindowRise = 0.0;
                    _refuelLastRiseTime = 0.0;
                }
            }

            // Track last fuel for next tick (always)
            _refuelLastFuel = currentFuel;


            // Save exactly once at the S/F that ended the OUT-LAP
            if (_pitLite != null && _pitLite.ConsumeCandidate(out var lossSec, out var src))
            {
                if (completedLaps != _lastSavedLap)            // don't double-save this lap
                {
                    _pitDbg_CandidateSavedSec = lossSec;
                    _pitDbg_CandidateSource = (src ?? "direct").ToLowerInvariant();

                    Pit_OnValidPitStopTimeLossCalculated(lossSec, src);
                    _lastSavedLap = completedLaps;
                }
            }

            int laps = Convert.ToInt32(data.NewData?.CompletedLaps ?? 0);

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
                else
                    _msgSystem.MaintainMsgCxTimers();
            }

            // --- Launch State helpers (need tick-level responsiveness) ---
            bool isOnTrack = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.IsOnTrack") ?? false);
            if (!isOnTrack && !IsIdle)
            {
                SimHub.Logging.Current.Info("[LalaLaunch] Off track or in pits – aborting launch state to Idle.");
                AbortLaunch();
            }
            double clutchRaw = Convert.ToDouble(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.ClutchRaw") ?? 0.0);
            _paddleClutch = 100.0 - (clutchRaw * 100.0); // Convert to the same scale as the settings

            // --- 500ms group: identity polling & session-change handling ---
            if (_poll500ms.ElapsedMilliseconds >= 500)
            {
                _poll500ms.Restart();
                UpdateLiveFuelCalcs(data, pluginManager);

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
                string trackIdentity =
                    (!string.IsNullOrWhiteSpace(CurrentTrackKey) && !CurrentTrackKey.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                        ? CurrentTrackKey
                        : CurrentTrackName;
                bool hasCar = !string.IsNullOrEmpty(CurrentCarModel) && CurrentCarModel != "Unknown";
                bool hasTrack = !string.IsNullOrWhiteSpace(trackIdentity);

                if (hasCar && hasTrack && (CurrentCarModel != _lastSeenCar || trackIdentity != _lastSeenTrack))
                {
                    // It's a new combo, so we'll perform the auto-selection.
                    SimHub.Logging.Current.Info($"[LalaLaunch] New live combo detected. Auto-selecting profile for Car='{CurrentCarModel}', Track='{trackIdentity}'.");

                    // Store this combo's KEY so we don't trigger again for the same session.
                    _lastSeenCar = CurrentCarModel;
                    _lastSeenTrack = trackIdentity; // track key preferred, fall back to display name
                    ResetSmoothedOutputs();
                    _pendingSmoothingReset = true;

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
                            SimHub.Logging.Current.Info($"[LalaLaunch] EnsureCarTrack fallback -> car='{CurrentCarModel}', trackName='{trackIdentity}'");
                            ProfilesViewModel.EnsureCarTrack(CurrentCarModel, trackIdentity);
                        }

                        string trackNameForSnapshot = !string.IsNullOrWhiteSpace(CurrentTrackName)
                            ? CurrentTrackName
                            : trackIdentity;
                        FuelCalculator?.SetLiveSession(CurrentCarModel, trackNameForSnapshot);
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
            // Fuel model session-change handling (independent of auto-dash setting)
            if (!string.IsNullOrEmpty(_lastFuelSessionType) && currentSession != _lastFuelSessionType)
            {
                HandleSessionChangeForFuelModel(_lastFuelSessionType, currentSession);
            }
            _lastFuelSessionType = currentSession;

            // --- AUTO DASH SWITCHING BASED ON ON-TRACK STATUS ---
            if (Settings.EnableAutoDashSwitch && isOnTrack && !_hasProcessedOnTrack)
            {
                _hasProcessedOnTrack = true;
                _lastSessionType = currentSession;
                SimHub.Logging.Current.Info("[LalaLaunch] New session on-track activity detected. Resetting all values.");
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
                    SimHub.Logging.Current.Info($"[LalaLaunch] OnTrack detected – mode=auto, page='{Screens.CurrentPage}'.");
                    await Task.Delay(2000);
                    Screens.Mode = "manual";
                    SimHub.Logging.Current.Info("[LalaLaunch] Auto mode timer expired – mode set to 'manual'.");
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

        private static double SafeReadDouble(PluginManager pluginManager, string propertyName, double fallback)
        {
            try
            {
                return Convert.ToDouble(pluginManager.GetPropertyValue(propertyName) ?? fallback);
            }
            catch
            {
                return fallback;
            }
        }

        private static bool ReadFlagBool(PluginManager pluginManager, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                try
                {
                    var raw = pluginManager.GetPropertyValue(name);
                    if (raw != null)
                    {
                        return Convert.ToBoolean(raw);
                    }
                }
                catch
                {
                    // ignore and try the next candidate
                }
            }

            return false;
        }

        private bool IsRefuelSelected(PluginManager pluginManager)
        {
            try
            {
                var raw = pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.dpFuelFill");
                if (raw != null)
                {
                    return Convert.ToBoolean(raw);
                }
            }
            catch
            {
                // Treat read failures as "selected" to preserve prior behavior.
            }

            return true;
        }

        private bool IsAnyTireChangeSelected(PluginManager pluginManager)
        {
            bool sawFlag = false;
            string[] selectors = new[]
            {
                "DataCorePlugin.GameRawData.Telemetry.dpLFTireChange",
                "DataCorePlugin.GameRawData.Telemetry.dpRFTireChange",
                "DataCorePlugin.GameRawData.Telemetry.dpLRTireChange",
                "DataCorePlugin.GameRawData.Telemetry.dpRRTireChange"
            };

            foreach (var name in selectors)
            {
                try
                {
                    var raw = pluginManager.GetPropertyValue(name);
                    if (raw != null)
                    {
                        sawFlag = true;
                        if (Convert.ToBoolean(raw))
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    // ignore and keep looking
                }
            }

            return !sawFlag;
        }

        private double GetEffectiveTireChangeTimeSeconds()
        {
            double baseTime = FuelCalculator?.TireChangeTime ?? 0.0;
            if (!_isTireChangeSelected)
            {
                return 0.0;
            }

            return baseTime < 0.0 ? 0.0 : baseTime;
        }

        private double CalculateTotalStopLossSeconds()
        {
            double pitLaneLoss = FuelCalculator?.PitLaneTimeLoss ?? 0.0;
            if (pitLaneLoss < 0.0) pitLaneLoss = 0.0;

            double willAdd = Pit_WillAdd;
            double refuelRate = FuelCalculator?.EffectiveRefuelRateLps ?? 0.0;
            double fuelTime = (willAdd > 0.0 && refuelRate > 0.0) ? (willAdd / refuelRate) : 0.0;
            if (fuelTime < 0.0 || double.IsNaN(fuelTime) || double.IsInfinity(fuelTime)) fuelTime = 0.0;

            double tireTime = GetEffectiveTireChangeTimeSeconds();
            double boxTime = Math.Max(fuelTime, tireTime);

            double total = pitLaneLoss + boxTime;
            return (total < 0.0 || double.IsNaN(total) || double.IsInfinity(total)) ? 0.0 : total;
        }

        private static string FormatSecondsOrNA(double seconds)
        {
            return (double.IsNaN(seconds) || double.IsInfinity(seconds))
                ? "n/a"
                : seconds.ToString("F1", CultureInfo.InvariantCulture);
        }

        private void ResetSmoothedOutputs()
        {
            _smoothedLiveLapsRemainingState = double.NaN;
            _smoothedPitDeltaState = double.NaN;
            _smoothedPitPushDeltaState = double.NaN;
            _smoothedPitFuelSaveDeltaState = double.NaN;
            _smoothedPitTotalNeededState = double.NaN;
            _smoothedProjectionValid = false;
            _smoothedPitValid = false;
            _pendingSmoothingReset = false;
        }

        private static double ApplyEma(double alpha, double raw, double previous)
        {
            if (double.IsNaN(previous))
            {
                return raw;
            }

            return (alpha * raw) + ((1.0 - alpha) * previous);
        }

        private void UpdateStableFuelPerLap(bool isWetMode, double fallbackFuelPerLap)
        {
            var (profileDry, profileWet) = GetProfileFuelBaselines();
            double profileFuel = isWetMode ? profileWet : profileDry;

            double candidate = fallbackFuelPerLap;
            string source = "Fallback";

            if (Confidence >= FuelModelConfidenceSwitchOn && LiveFuelPerLap > 0.0)
            {
                candidate = LiveFuelPerLap;
                source = "Live";
            }
            else if (profileFuel > 0.0)
            {
                candidate = profileFuel;
                source = "Profile";
            }

            double stable = _stableFuelPerLap;
            string selectedSource = source;
            double selectedConfidence = Confidence;

            if (candidate <= 0.0)
            {
                if (stable > 0.0)
                {
                    candidate = stable;
                    selectedSource = _stableFuelPerLapSource;
                    selectedConfidence = _stableFuelPerLapConfidence;
                }
                else
                {
                    stable = 0.0;
                }
            }
            else
            {
                if (stable <= 0.0 || Math.Abs(candidate - stable) >= StableFuelPerLapDeadband)
                {
                    stable = candidate;
                    selectedSource = source;
                    selectedConfidence = Confidence;
                }
                else
                {
                    selectedSource = _stableFuelPerLapSource;
                    selectedConfidence = _stableFuelPerLapConfidence;
                }
            }

            stable = Math.Max(0.1, stable); // Clamp to avoid pathological near-zero persistence
            _stableFuelPerLap = stable;
            _stableFuelPerLapSource = selectedSource;
            _stableFuelPerLapConfidence = selectedConfidence;

            LiveFuelPerLap_Stable = _stableFuelPerLap;
            LiveFuelPerLap_StableSource = _stableFuelPerLapSource;
            LiveFuelPerLap_StableConfidence = _stableFuelPerLapConfidence;
        }

        private void UpdateSmoothedFuelOutputs(double requestedAddLitres)
        {
            bool projectionValid = LiveFuelPerLap_Stable > 0.0 && LiveLapsRemainingInRace_Stable > 0.0;
            bool pitValid = LiveFuelPerLap_Stable > 0.0;

            bool validityReset = (projectionValid && !_smoothedProjectionValid) || (pitValid && !_smoothedPitValid);

            if (_pendingSmoothingReset || validityReset)
            {
                ResetSmoothedOutputs();
            }

            if (!projectionValid)
            {
                _smoothedProjectionValid = false;
                _smoothedLiveLapsRemainingState = double.NaN;
                LiveLapsRemainingInRace_S = LiveLapsRemainingInRace;
                LiveLapsRemainingInRace_Stable_S = LiveLapsRemainingInRace_Stable;
            }
            else
            {
                _smoothedLiveLapsRemainingState = ApplyEma(SmoothedAlpha, LiveLapsRemainingInRace_Stable, _smoothedLiveLapsRemainingState);
                LiveLapsRemainingInRace_S = _smoothedLiveLapsRemainingState;
                LiveLapsRemainingInRace_Stable_S = _smoothedLiveLapsRemainingState;
                _smoothedProjectionValid = true;
            }

            if (!pitValid)
            {
                _smoothedPitValid = false;
                _smoothedPitDeltaState = double.NaN;
                _smoothedPitPushDeltaState = double.NaN;
                _smoothedPitFuelSaveDeltaState = double.NaN;
                _smoothedPitTotalNeededState = double.NaN;

                Pit_DeltaAfterStop_S = Pit_DeltaAfterStop;
                Pit_PushDeltaAfterStop_S = Pit_PushDeltaAfterStop;
                Pit_FuelSaveDeltaAfterStop_S = Pit_FuelSaveDeltaAfterStop;
                Pit_TotalNeededToEnd_S = Pit_TotalNeededToEnd;
            }
            else
            {
                _smoothedPitDeltaState = ApplyEma(SmoothedAlpha, Pit_DeltaAfterStop, _smoothedPitDeltaState);
                _smoothedPitPushDeltaState = ApplyEma(SmoothedAlpha, Pit_PushDeltaAfterStop, _smoothedPitPushDeltaState);
                _smoothedPitFuelSaveDeltaState = ApplyEma(SmoothedAlpha, Pit_FuelSaveDeltaAfterStop, _smoothedPitFuelSaveDeltaState);
                _smoothedPitTotalNeededState = ApplyEma(SmoothedAlpha, Pit_TotalNeededToEnd, _smoothedPitTotalNeededState);

                Pit_DeltaAfterStop_S = _smoothedPitDeltaState;
                Pit_PushDeltaAfterStop_S = _smoothedPitPushDeltaState;
                Pit_FuelSaveDeltaAfterStop_S = _smoothedPitFuelSaveDeltaState;
                Pit_TotalNeededToEnd_S = _smoothedPitTotalNeededState;
                _smoothedPitValid = true;
            }
        }

        private double GetProjectionLapSeconds(GameData data)
        {
            double profileAvgSeconds = GetProfileAvgLapSeconds();

            double lapSeconds = 0.0;
            string source = "fallback.none";

            double liveAvg = Pace_StintAvgLapTimeSec > 0.0 ? Pace_StintAvgLapTimeSec : Pace_Last5LapAvgSec;
            if (PaceConfidence >= LapTimeConfidenceSwitchOn && liveAvg > 0.0)
            {
                lapSeconds = liveAvg;
                source = (Math.Abs(liveAvg - Pace_StintAvgLapTimeSec) < 1e-6) ? "pace.stint" : "pace.last5";
            }
            else if (profileAvgSeconds > 0.0)
            {
                lapSeconds = profileAvgSeconds;
                source = "profile.avg";
            }
            else
            {
                double estimator = 0.0;
                string estimatorSource = string.Empty;

                string estimatedLap = FuelCalculator?.EstimatedLapTime ?? string.Empty;
                if (TimeSpan.TryParse(estimatedLap, out var ts) && ts.TotalSeconds > 0.0)
                {
                    estimator = ts.TotalSeconds;
                    estimatorSource = "fuelcalc.estimated";
                }

                double lastLapSeconds = (data.NewData?.LastLapTime ?? TimeSpan.Zero).TotalSeconds;
                if (estimator <= 0.0 && lastLapSeconds > 0.0)
                {
                    estimator = lastLapSeconds;
                    estimatorSource = "telemetry.lastlap";
                }

                lapSeconds = estimator;
                source = string.IsNullOrEmpty(estimatorSource) ? "fallback.none" : estimatorSource;
            }

            double stable = _stableProjectionLapTime;
            string selectedSource = source;

            if (lapSeconds <= 0.0)
            {
                if (stable > 0.0)
                {
                    lapSeconds = stable;
                    selectedSource = _stableProjectionLapTimeSource;
                }
            }
            else
            {
                double roundedCandidate = Math.Round(lapSeconds, 1);
                if (stable <= 0.0 || Math.Abs(roundedCandidate - stable) >= StableLapTimeDeadband)
                {
                    stable = roundedCandidate;
                    selectedSource = source;
                }
                else
                {
                    selectedSource = _stableProjectionLapTimeSource;
                }
            }

            if (lapSeconds <= 0.0)
            {
                stable = lapSeconds;
            }

            _stableProjectionLapTime = stable;
            _stableProjectionLapTimeSource = selectedSource;
            ProjectionLapTime_Stable = _stableProjectionLapTime;
            ProjectionLapTime_StableSource = _stableProjectionLapTimeSource;

            bool shouldLog = (!string.Equals(selectedSource, _lastProjectionLapSource, StringComparison.Ordinal)) ||
                             Math.Abs(_stableProjectionLapTime - _lastProjectionLapSeconds) > 0.05;

            if (shouldLog && (DateTime.UtcNow - _lastProjectionLapLogUtc) > TimeSpan.FromSeconds(5))
            {
                _lastProjectionLapSource = selectedSource;
                _lastProjectionLapSeconds = _stableProjectionLapTime;
                _lastProjectionLapLogUtc = DateTime.UtcNow;

                SimHub.Logging.Current.Info(
                    $"[ProjectionLap] source={selectedSource} lap={_stableProjectionLapTime:F3}s " +
                    $"stint={Pace_StintAvgLapTimeSec:F3}s last5={Pace_Last5LapAvgSec:F3}s profile={profileAvgSeconds:F3}s");
            }

            return _stableProjectionLapTime;
        }

        private double ComputeProjectedLapsRemaining(double simLapsRemaining, double lapSeconds, double sessionTimeRemain, double driveTimeAfterZero)
        {
            double projectedSeconds;
            double projectedLaps = FuelProjectionMath.ProjectLapsRemaining(
                lapSeconds,
                sessionTimeRemain,
                driveTimeAfterZero,
                simLapsRemaining,
                out projectedSeconds);

            LiveProjectedDriveSecondsRemaining = projectedSeconds;
            return projectedLaps;
        }

        private bool ShouldLogProjection(double simLapsRemaining, double projectedLapsRemaining)
        {
            double diff = Math.Abs(projectedLapsRemaining - simLapsRemaining);
            if (diff < 0.25)
                return false;

            if ((DateTime.UtcNow - _lastProjectionLogUtc) < TimeSpan.FromSeconds(20))
                return false;

            if (!double.IsNaN(_lastLoggedProjectedLaps) && Math.Abs(projectedLapsRemaining - _lastLoggedProjectedLaps) < 0.25)
                return false;

            return true;
        }

        private void LogProjectionDifference(double simLapsRemaining, double projectedLapsRemaining, double lapSeconds)
        {
            _lastProjectionLogUtc = DateTime.UtcNow;
            _lastLoggedProjectedLaps = projectedLapsRemaining;

            SimHub.Logging.Current.Info(
                $"[FuelProjection] using drive-time projection laps={projectedLapsRemaining:F2} (sim={simLapsRemaining:F2}, lap={lapSeconds:F2}s, after0={LiveProjectedDriveTimeAfterZero:F1}s)"
            );
        }

        private double ComputeObservedExtraSeconds(double finishSessionTime)
        {
            if (double.IsNaN(finishSessionTime) || finishSessionTime <= 0.0)
            {
                return 0.0;
            }

            if (_timerZeroSeen && !double.IsNaN(_timerZeroSessionTime))
            {
                return Math.Max(0.0, finishSessionTime - _timerZeroSessionTime);
            }

            return 0.0;
        }

        private long _finishTimingSessionId = -1;
        private string _finishTimingSessionType = string.Empty;

        private void UpdateFinishTiming(
            PluginManager pluginManager,
            GameData data,
            double sessionTime,
            double sessionTimeRemain,
            int completedLaps,
            long sessionId,
            string sessionType)
        {
            bool isRace = string.Equals(sessionType, "Race", StringComparison.OrdinalIgnoreCase);
            bool sessionChanged = sessionId != _finishTimingSessionId ||
                                  !string.Equals(sessionType, _finishTimingSessionType, StringComparison.OrdinalIgnoreCase);

            if (sessionChanged)
            {
                _finishTimingSessionId = sessionId;
                _finishTimingSessionType = sessionType;
                ResetFinishTimingState();
            }

            if (!isRace)
            {
                // Guardrail: ignore timer / finish latching in non-race sessions so "time zero"
                // events (e.g., practice/qual transitions) cannot pollute race finish timing.
                ResetFinishTimingState();
                _prevSessionTimeRemain = !double.IsNaN(sessionTimeRemain) ? sessionTimeRemain : double.NaN;
                return;
            }

            bool hasRemain = !double.IsNaN(sessionTimeRemain);

            bool checkeredFlag = ReadFlagBool(
                pluginManager,
                "DataCorePlugin.GameRawData.Telemetry.SessionFlagsDetails.IsCheckeredFlag",
                "DataCorePlugin.GameRawData.Telemetry.SessionFlagsDetails.IsCheckered"
            );

            bool whiteFlag = ReadFlagBool(
                pluginManager,
                "DataCorePlugin.GameRawData.Telemetry.SessionFlagsDetails.IsWhiteFlag",
                "DataCorePlugin.GameRawData.Telemetry.SessionFlagsDetails.IsWhite"
            );

            bool leaderFinishSignal = ReadFlagBool(
                pluginManager,
                "IRacingExtraProperties.iRacing_ClassLeaderboard_Driver_00_IsCheckered",
                "IRacingExtraProperties.iRacing_ClassLeaderboard_Driver_00_Checkered"
            );

            bool endOfRaceIndicator = whiteFlag || checkeredFlag || leaderFinishSignal;

            bool crossedToZero = hasRemain && !double.IsNaN(_prevSessionTimeRemain) &&
                                 _prevSessionTimeRemain > 0.5 && sessionTimeRemain <= 0.5;

            if (!_timerZeroSeen && crossedToZero && completedLaps > 0 && endOfRaceIndicator)
            {
                _timerZeroSeen = true;
                _timerZeroSessionTime = sessionTime;
            }

            bool leaderWasFinished = LeaderHasFinished;
            LeaderHasFinished = leaderFinishSignal;
            bool leaderFinishTransition = LeaderHasFinished && !leaderWasFinished;

            if (leaderFinishTransition && !_leaderFinishedSeen)
            {
                _leaderFinishedSeen = true;
                _leaderCheckeredSessionTime = sessionTime;
            }

            bool lapCompleted = (_lastCompletedLapForFinish >= 0) && (completedLaps > _lastCompletedLapForFinish);
            _lastCompletedLapForFinish = completedLaps;

            _prevSessionTimeRemain = hasRemain ? sessionTimeRemain : double.NaN;

            if (lapCompleted && checkeredFlag)
            {
                _driverCheckeredSessionTime = sessionTime;

                double leaderExtra = ComputeObservedExtraSeconds(_leaderCheckeredSessionTime);
                double driverExtra = ComputeObservedExtraSeconds(_driverCheckeredSessionTime);

                SimHub.Logging.Current.Info(
                    $"[LalaLaunch] Finish timing: timer0={FormatSecondsOrNA(_timerZeroSessionTime)}s " +
                    $"leaderChk={FormatSecondsOrNA(_leaderCheckeredSessionTime)}s driverChk={FormatSecondsOrNA(_driverCheckeredSessionTime)}s " +
                    $"leader after 00:00={leaderExtra:F1}s driver after 00:00={driverExtra:F1}s remain={FormatSecondsOrNA(sessionTimeRemain)}s"
                );

                ResetFinishTimingState();
            }
        }

        private static (double seconds, bool isFallback) ReadLeaderLapTimeSeconds(
            PluginManager pluginManager,
            GameData data,
            double playerRecentAvg,
            double leaderAvgFallback)
        {
            // Local helper to normalise any raw value to seconds
            double TryReadSeconds(object raw)
            {
                if (raw == null) return 0.0;

                try
                {
                    if (raw is TimeSpan ts) return ts.TotalSeconds;
                    if (raw is double d) return d;
                    if (raw is float f) return (double)f;
                    if (raw is IConvertible c) return Convert.ToDouble(c, CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Info($"[FuelLeader] TryReadSeconds error for value '{raw}': {ex.Message}");
                }

                return 0.0;
            }

            // Candidate sources – ordered by preference
            var candidates = new (string Name, object Raw)[]
            {
        // Verified working class-leader property:
        ("IRacingExtraProperties.iRacing_ClassLeaderboard_Driver_00_LastLapTime",
            pluginManager.GetPropertyValue("IRacingExtraProperties.iRacing_ClassLeaderboard_Driver_00_LastLapTime")),

        // Legacy / fallback properties (may or may not exist in your SimHub version):
        ("IRacingExtraProperties.iRacing_LeaderLastLapTime",
            pluginManager.GetPropertyValue("IRacingExtraProperties.iRacing_LeaderLastLapTime")),
        ("DataCorePlugin.GameData.LeaderLastLapTime",
            pluginManager.GetPropertyValue("DataCorePlugin.GameData.LeaderLastLapTime")),
        ("DataCorePlugin.GameData.LeaderAverageLapTime",
            pluginManager.GetPropertyValue("DataCorePlugin.GameData.LeaderAverageLapTime")),
            };

            foreach (var candidate in candidates)
            {
                double seconds = TryReadSeconds(candidate.Raw);

                // Debug trace for inspection in SimHub log
                SimHub.Logging.Current.Info(
                    $"[FuelLeader] candidate {candidate.Name} raw='{candidate.Raw}' seconds={seconds:F3}");

                if (seconds > 0.0)
                {
                    double rejectionFloor = (playerRecentAvg > 0.0) ? playerRecentAvg * 0.5 : 0.0;
                    if (seconds < 30.0 || (rejectionFloor > 0.0 && seconds < rejectionFloor))
                    {
                        double fallback = leaderAvgFallback > 0.0 ? leaderAvgFallback : 0.0;
                        SimHub.Logging.Current.Info(
                            $"[FuelLeader] rejected outlier {seconds:F3}s from {candidate.Name} " +
                            $"(playerLast5={playerRecentAvg:F3}s floor={rejectionFloor:F3}s) fallback={fallback:F3}s");

                        if (fallback > 0.0)
                        {
                            return (fallback, true);
                        }

                        continue;
                    }

                    SimHub.Logging.Current.Info(
                        $"[FuelLeader] using leader lap from {candidate.Name} = {seconds:F3}s");
                    return (seconds, false);
                }
            }

            SimHub.Logging.Current.Info("[FuelLeader] no valid leader lap time from any candidate – returning 0");
            return (0.0, false);
        }


        private void UpdateLiveSurfaceSummary(PluginManager pluginManager)
        {
            if (FuelCalculator == null) return;

            bool? declaredWet = TryReadNullableBool(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.WeatherDeclaredWet"));

            string airTemp = GetSurfaceText(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.WeekendInfo.TrackAirTemp"));
            string trackTemp = GetSurfaceText(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.WeekendInfo.TrackSurfaceTemp"));
            string humidity = GetSurfaceText(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.WeekendInfo.TrackRelativeHumidity"));
            string rubberState = GetSurfaceText(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.WeekendInfo.TrackRubberState"));
            string precipitation = GetSurfaceText(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.WeekendInfo.TrackPrecipitation"));

            var parts = new List<string>();
            bool isWet = declaredWet ?? FuelCalculator.IsWet;
            parts.Add(isWet ? "Wet" : "Dry");

            string tempSegment = ComposeTemperatureSegment(airTemp, trackTemp);
            if (!string.IsNullOrWhiteSpace(tempSegment)) parts.Add(tempSegment);

            if (!string.IsNullOrWhiteSpace(humidity)) parts.Add($"{humidity} humidity");
            if (!string.IsNullOrWhiteSpace(rubberState)) parts.Add($"Rubber {rubberState}");
            if (!string.IsNullOrWhiteSpace(precipitation)) parts.Add($"Precip: {precipitation}");

            string summary = parts.Count > 0 ? string.Join(" | ", parts) : "-";

            FuelCalculator.SetLiveSurfaceSummary(declaredWet, summary);
        }

        private static string GetSurfaceText(object value)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static string ComposeTemperatureSegment(string airTemp, string trackTemp)
        {
            if (!string.IsNullOrWhiteSpace(airTemp) && !string.IsNullOrWhiteSpace(trackTemp))
            {
                return $"{airTemp} / {trackTemp}";
            }

            if (!string.IsNullOrWhiteSpace(airTemp))
            {
                return $"{airTemp} air";
            }

            if (!string.IsNullOrWhiteSpace(trackTemp))
            {
                return $"{trackTemp} track";
            }

            return null;
        }

        private static bool? TryReadNullableBool(object value)
        {
            if (value == null) return null;

            try
            {
                switch (value)
                {
                    case bool b:
                        return b;
                    case string s when bool.TryParse(s, out var parsedBool):
                        return parsedBool;
                    case string s when int.TryParse(s, out var parsedInt):
                        return parsedInt != 0;
                    default:
                        return Convert.ToBoolean(value);
                }
            }
            catch
            {
                return null;
            }
        }

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
                    SimHub.Logging.Current.Info("[LalaLaunch] Manual launch timed out after 30 seconds.");
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
                    SimHub.Logging.Current.Info($"[LaunchTrace] Discarded trace file: {_currentFilePath}");
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

                SimHub.Logging.Current.Info($"[LaunchTrace] New launch trace file opened: {_currentFilePath}");
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
                //SimHub.Logging.Current.Debug("TelemetryTraceLogger: Skipping trace logging — disabled in plugin settings.");
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
                SimHub.Logging.Current.Info($"[LaunchTrace] Successfully appended launch summary to {_currentFilePath}");
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
                    SimHub.Logging.Current.Info($"[LaunchTrace] Launch trace file closed: {_currentFilePath}");
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
                SimHub.Logging.Current.Info($"[LaunchTrace] Trace directory not found: {tracePath}");
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
