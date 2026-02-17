// --- Using Directives ---
using GameReaderCommon;
using LaunchPlugin.Messaging;
using Newtonsoft.Json;
using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
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
        private const bool HARD_DEBUG_ENABLED = true;

        // iRacing reports Warmup as a session type and we treat it as Practice-like.
        private static string NormalizeSessionTypeName(string raw)
        {
            return string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();
        }

        private static bool IsWarmupSession(string sessionTypeName)
        {
            return string.Equals(NormalizeSessionTypeName(sessionTypeName), "Warmup", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPracticeLikeSession(string sessionTypeName)
        {
            string normalized = NormalizeSessionTypeName(sessionTypeName);
            return string.Equals(normalized, "Practice", StringComparison.OrdinalIgnoreCase) ||
                   IsWarmupSession(normalized);
        }

        private static bool IsQualLikeSession(string sessionTypeName)
        {
            string normalized = NormalizeSessionTypeName(sessionTypeName);
            return string.Equals(normalized, "Open Qualify", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Lone Qualify", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Qualifying", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRaceSession(string sessionTypeName)
        {
            return string.Equals(NormalizeSessionTypeName(sessionTypeName), "Race", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOfflineTestingSession(string sessionTypeName)
        {
            return string.Equals(NormalizeSessionTypeName(sessionTypeName), "Offline Testing", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDrivingSessionForFuelSeed(string sessionTypeName)
        {
            return IsOfflineTestingSession(sessionTypeName) ||
                   IsPracticeLikeSession(sessionTypeName) ||
                   IsQualLikeSession(sessionTypeName) ||
                   IsRaceSession(sessionTypeName);
        }

        private static string GetDashDesiredPageForSession(string sessionTypeName)
        {
            string normalized = NormalizeSessionTypeName(sessionTypeName);
            if (IsOfflineTestingSession(normalized)) return "practice";
            if (IsQualLikeSession(normalized) || IsPracticeLikeSession(normalized)) return "timing";
            if (IsRaceSession(normalized)) return "racing";
            return "practice";
        }

        // --- SimHub Interfaces ---
        public PluginManager PluginManager { get; set; }
        public LaunchPluginSettings Settings { get; private set; }
        public ImageSource PictureIcon => null;
        public string LeftMenuTitle => "Lala Plugin";
        public bool HardDebugEnabledForUi => HardDebugEnabled;

        // --- Dashboard Manager ---
        public ScreenManager Screens = new ScreenManager();
        //private int _declutterMode = 0;

        // --- button dash helpers ---
        // NOTE: This region is intended to expose SimHub Actions and keep cancel semantics stable.
        // MsgCx() is the ONE canonical cancel entry point.
        // Legacy MsgCx variants remain as compatibility stubs (route to MsgCx) so old mappings don't break.

        public void PrimaryDashMode()
        {
            // Placeholder: wired for Controls & Events + UI mapping.
            // TODO: implement real behaviour (switch main dash mode/page).
            SimHub.Logging.Current.Info("[LalaPlugin:Dash] PrimaryDashMode action fired (placeholder).");
        }


        // Exposed dash mode value: 0,1,2
        public int DeclutterMode { get; private set; } = 0;

        // Binding action (button press)
        public void DeclutterMode0()
        {
            ToggleDeclutterMode("DeclutterMode action fired");
        }

        private void ToggleDeclutterMode(string actionLabel)
        {
            DeclutterMode = (DeclutterMode + 1) % 3;
            SimHub.Logging.Current.Info($"[LalaPlugin:Dash] {actionLabel} -> DeclutterMode={DeclutterMode}.");
        }

        // --- Launch button helper ---
        // Manual prime/cancel for testing and for non-standing-start sessions.
        public void LaunchMode()
        {
            // If user has hard-disabled launch mode, let the button re-enable it.
            if (_launchModeUserDisabled)
            {
                _launchModeUserDisabled = false;
                SimHub.Logging.Current.Info("[LalaPlugin:Launch] LaunchMode pressed -> re-enabled launch mode.");
            }

            bool blocked = IsLaunchBlocked(PluginManager, null, out var inPits, out var seriousRejoin);

            // Toggle behaviour:
            // - If idle: enter ManualPrimed
            // - If already active/visible: abort (drops trace + returns to idle via AbortLaunch())
            if (IsIdle)
            {
                if (blocked)
                {
                    SimHub.Logging.Current.Info($"[LalaPlugin:Launch] LaunchMode blocked (inPits={inPits}, seriousRejoin={seriousRejoin}).");
                    return;
                }

                SetLaunchState(LaunchState.ManualPrimed);
                SimHub.Logging.Current.Info("[LalaPlugin:Launch] LaunchMode pressed -> ManualPrimed.");
            }
            else
            {
                SimHub.Logging.Current.Info($"[LalaPlugin:Launch] LaunchMode pressed -> aborting (state={_currentLaunchState}).");
                _launchModeUserDisabled = true;
                CancelLaunchToIdle("User toggle");
            }
        }


        public void TogglePitScreen()
        {
            bool isOnPitRoadFlag = Convert.ToBoolean(
                PluginManager?.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.OnPitRoad") ?? false
            );

            if (isOnPitRoadFlag)
            {
                // In pits: toggle the existing dismiss latch (hide/show the auto pit popup)
                _pitScreenDismissed = !_pitScreenDismissed;

                // Optional: if you dismiss it in pits, also clear any manual force-on
                if (_pitScreenDismissed) _pitScreenManualEnabled = false;

                SimHub.Logging.Current.Info($"[LalaPlugin:PitScreen] Toggle pressed IN PITS -> dismissed={_pitScreenDismissed}, manual={_pitScreenManualEnabled}");
            }
            else
            {
                // On track: toggle the manual force-on
                _pitScreenManualEnabled = !_pitScreenManualEnabled;

                SimHub.Logging.Current.Info($"[LalaPlugin:PitScreen] Toggle pressed ON TRACK -> manual={_pitScreenManualEnabled}");
            }
        }

        public void SetTrackMarkersLocked(bool locked)
        {
            var key = GetCanonicalTrackKeyForMarkers();
            _pit?.SetTrackMarkersLock(key, locked);
        }

        public TrackMarkersSnapshot GetTrackMarkersSnapshot(string trackKey)
        {
            if (string.IsNullOrWhiteSpace(trackKey) || _pit == null)
            {
                return new TrackMarkersSnapshot
                {
                    EntryPct = double.NaN,
                    ExitPct = double.NaN,
                    LastUpdatedUtc = null,
                    Locked = false,
                    HasData = false
                };
            }

            double entryPct;
            double exitPct;
            DateTime lastUpdatedUtc;
            bool locked;
            bool ok = _pit.TryGetStoredTrackMarkers(trackKey, out entryPct, out exitPct, out lastUpdatedUtc, out locked);

            return new TrackMarkersSnapshot
            {
                EntryPct = entryPct,
                ExitPct = exitPct,
                LastUpdatedUtc = lastUpdatedUtc == DateTime.MinValue ? (DateTime?)null : lastUpdatedUtc,
                Locked = locked,
                HasData = ok
            };
        }

        public void SetTrackMarkersLockedForKey(string trackKey, bool locked)
        {
            if (string.IsNullOrWhiteSpace(trackKey)) return;
            _pit?.SetTrackMarkersLock(trackKey, locked);
        }

        public void ReloadTrackMarkersFromDisk()
        {
            _pit?.ReloadTrackMarkerStore();
            ProfilesViewModel?.RefreshTrackMarkersSnapshotForSelectedTrack();
        }

        public void ResetTrackMarkersForKey(string trackKey)
        {
            if (string.IsNullOrWhiteSpace(trackKey)) return;
            _pit?.ResetTrackMarkersForKey(trackKey);
        }

        private bool IsTrackMarkerPulseActive(DateTime utcTimestamp)
        {
            return utcTimestamp != DateTime.MinValue &&
                   (DateTime.UtcNow - utcTimestamp).TotalSeconds < TrackMarkerPulseHoldSeconds;
        }

        public void MsgCx()
        {
            RegisterMsgCxPress();

            // Keep: new system(s) entry point
            _msgSystem?.TriggerMsgCx();
            _rejoinEngine?.TriggerMsgCxOverride();
            _msgV1Engine?.OnMsgCxPressed();

            SimHub.Logging.Current.Info("[LalaPlugin:MsgCx] MsgCx action fired (pressed latched + engines notified).");
        }

        public void EventMarker()
        {
            RegisterEventMarkerPress();
            SimHub.Logging.Current.Info("[LalaPlugin:Dash] Event marker action fired (pressed latched).");
        }

        /*
        // --- Legacy/experimental MsgCx helpers (parked) ---
        // Only keep if you still actively bind to these from somewhere.
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
        */

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
        public string CurrentFasterClassApproachLine => _msgSystem?.OvertakeApproachLine ?? string.Empty;
        public ThreatLevel CurrentRejoinThreat => _rejoinEngine?.CurrentThreatLevel ?? ThreatLevel.CLEAR;
        public RejoinReason CurrentRejoinReason => _rejoinEngine?.CurrentLogicCode ?? RejoinReason.None;
        public double CurrentRejoinTimeToThreat => _rejoinEngine?.TimeToThreatSeconds ?? double.NaN;

        public bool OverallLeaderHasFinished
        {
            get => _overallLeaderHasFinished;
            private set
            {
                if (_overallLeaderHasFinished != value)
                {
                    _overallLeaderHasFinished = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ClassLeaderHasFinished
        {
            get => _classLeaderHasFinished;
            private set
            {
                if (_classLeaderHasFinished != value)
                {
                    _classLeaderHasFinished = value;
                    OnPropertyChanged();
                }
            }
        }

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

        public bool OverallLeaderHasFinishedValid
        {
            get => _overallLeaderHasFinishedValid;
            private set
            {
                if (_overallLeaderHasFinishedValid != value)
                {
                    _overallLeaderHasFinishedValid = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ClassLeaderHasFinishedValid
        {
            get => _classLeaderHasFinishedValid;
            private set
            {
                if (_classLeaderHasFinishedValid != value)
                {
                    _classLeaderHasFinishedValid = value;
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
        private bool _overallLeaderHasFinished;
        private bool _classLeaderHasFinished;
        private bool _overallLeaderHasFinishedValid;
        private bool _classLeaderHasFinishedValid;
        private bool _isMultiClassSession;
        private double _lastClassLeaderLapPct = double.NaN;
        private double _lastOverallLeaderLapPct = double.NaN;
        private int _lastClassLeaderCarIdx = -1;
        private int _lastOverallLeaderCarIdx = -1;
        private readonly Dictionary<int, string> _carIdxToClassShortName = new Dictionary<int, string>();
        private int _lastCompletedLapForFinish = -1;
        private bool _leaderFinishLatchedByFlag;
        private double _afterZeroPlannerSeconds;
        private double _afterZeroLiveEstimateSeconds;
        private double _afterZeroUsedSeconds;
        private string _afterZeroSourceUsed = string.Empty;
        private double _lastProjectedLapsRemaining;
        private double _lastSimLapsRemaining;
        private double _lastProjectionLapSecondsUsed;
        private bool _afterZeroResultLogged;

        // New per-mode rolling windows
        private readonly List<double> _recentDryFuelLaps = new List<double>();
        private readonly List<double> _recentWetFuelLaps = new List<double>();
        private const int FuelWindowSize = 5; // keep last N valid laps per mode
        private const int FuelPersistMinLaps = 2; // guard against early garbage in live persistence

        private double _avgDryFuelPerLap = 0.0;
        private double _avgWetFuelPerLap = 0.0;
        private double _maxDryFuelPerLap = 0.0;
        private double _maxWetFuelPerLap = 0.0;
        private double _minDryFuelPerLap = 0.0;
        private double _minWetFuelPerLap = 0.0;
        private int _validDryLaps = 0;
        private int _validWetLaps = 0;
        private bool _wetFuelPersistLogged = false;
        private bool _dryFuelPersistLogged = false;
        private bool _msgV1InfoLogged = false;
        private int _lastValidLapMs = 0;
        private int _lastValidLapNumber = -1;
        private bool? _lastIsWetTyres = null;


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
        private bool _isWetMode = false;
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
        public int TrackWetness { get; private set; }
        public string TrackWetnessLabel { get; private set; } = "NA";
        // LiveLapsRemainingInRace already uses stable fuel/lap time; _Stable exports mirror the same value for explicit dash use/debugging.
        public double LiveLapsRemainingInRace { get; private set; }
        public double LiveLapsRemainingInRace_Stable { get; private set; }
        public double DeltaLaps { get; private set; }
        public double TargetFuelPerLap { get; private set; }
        public bool IsPitWindowOpen { get; private set; }
        public int PitWindowOpeningLap { get; private set; }
        public int PitWindowClosingLap { get; private set; }
        public int PitWindowState { get; private set; }
        public string PitWindowLabel { get; private set; } = "N/A";
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
        public int PitStopsRequiredByFuel { get; private set; }
        public int PitStopsRequiredByPlan { get; private set; }
        public int Pit_StopsRequiredToEnd { get; private set; }
        public double LiveLapsRemainingInRace_S { get; private set; }
        public double LiveLapsRemainingInRace_Stable_S { get; private set; }
        public double Pit_DeltaAfterStop_S { get; private set; }
        public double Pit_PushDeltaAfterStop_S { get; private set; }
        public double Pit_FuelSaveDeltaAfterStop_S { get; private set; }
        public double Pit_TotalNeededToEnd_S { get; private set; }
        public double Fuel_Delta_LitresCurrent { get; private set; }
        public double Fuel_Delta_LitresPlan { get; private set; }
        public double Fuel_Delta_LitresWillAdd { get; private set; }
        public double Fuel_Delta_LitresCurrentPush { get; private set; }
        public double Fuel_Delta_LitresPlanPush { get; private set; }
        public double Fuel_Delta_LitresWillAddPush { get; private set; }
        public double Fuel_Delta_LitresCurrentSave { get; private set; }
        public double Fuel_Delta_LitresPlanSave { get; private set; }
        public double Fuel_Delta_LitresWillAddSave { get; private set; }
        private bool _isRefuelSelected = true;
        private bool _isTireChangeSelected = true;
        public double LiveCarMaxFuel { get; private set; }
        public double EffectiveLiveMaxTank { get; private set; }
        private double _lastValidLiveMaxFuel = 0.0;

        public double FuelSaveFuelPerLap { get; private set; }
        public double StintBurnTarget { get; private set; }
        public string StintBurnTargetBand { get; private set; } = "current";
        public double FuelBurnPredictor { get; private set; }
        public string FuelBurnPredictorSource { get; private set; } = "SIMHUB";

        public double LiveProjectedDriveTimeAfterZero { get; private set; }
        public double LiveProjectedDriveSecondsRemaining { get; private set; }
        public double AfterZeroPlannerSeconds => _afterZeroPlannerSeconds;
        public double AfterZeroLiveEstimateSeconds => _afterZeroLiveEstimateSeconds;
        public string AfterZeroSource => string.IsNullOrEmpty(_afterZeroSourceUsed) ? "planner" : _afterZeroSourceUsed;

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
        private int _summaryPitStopIndex = 0;
        private DateTime _lastPitLaneSeenUtc = DateTime.MinValue;
        private bool _pitExitEntrySeenLast = false;
        private bool _pitExitExitSeenLast = false;
        private double _lastLoggedProjectedLaps = double.NaN;
        private DateTime _lastProjectionLogUtc = DateTime.MinValue;
        private string _lastProjectionLapSource = string.Empty;
        private double _lastProjectionLapSeconds = 0.0;
        private DateTime _lastProjectionLapLogUtc = DateTime.MinValue;
        private double _lastLoggedProjectionAfterZero = double.NaN;

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
        public double PacePredictor { get; private set; }
        public string PacePredictorSource { get; private set; } = "SIMHUB";
        private bool _lastOnPitRoadForOpponents = false;

        public double ProjectionLapTime_Stable { get; private set; }
        public string ProjectionLapTime_StableSource { get; private set; } = "fallback.none";
        private readonly DecelCapture _decelCapture = new DecelCapture();

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

        public bool IsFuelReady
        {
            get
            {
                return LiveFuelPerLap_StableConfidence >= GetFuelReadyConfidenceThreshold();
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
        private long _lastSubSessionId = -1;
        private string _lastSessionToken = string.Empty;
        private string _currentSessionToken = string.Empty;
        private double _smoothedLiveLapsRemainingState = double.NaN;
        private double _smoothedPitDeltaState = double.NaN;
        private double _smoothedPitPushDeltaState = double.NaN;
        private double _smoothedPitFuelSaveDeltaState = double.NaN;
        private double _smoothedPitTotalNeededState = double.NaN;
        private bool _smoothedProjectionValid = false;
        private bool _smoothedPitValid = false;
        private bool _pendingSmoothingReset = true;
        private const double SmoothedAlpha = 0.35; // ~1–2s response at 500ms tick
        internal const double FuelReadyConfidenceDefault = 60.0;
        internal const int StintFuelMarginPctDefault = 10;
        internal const double CarSANotRelevantGapSecDefault = 10.0;

        private static readonly Dictionary<int, string> DefaultCarSAStatusEBackgroundColors = new Dictionary<int, string>
        {
            { (int)CarSAStatusE.Unknown, "#000000" },
            { (int)CarSAStatusE.OutLap, "#696969" },
            { (int)CarSAStatusE.InPits, "#C0C0C0" },
            { (int)CarSAStatusE.SuspectInvalid, "#FFA500" },
            { (int)CarSAStatusE.CompromisedOffTrack, "#FF0000" },
            { (int)CarSAStatusE.CompromisedPenalty, "#FFA500" },
            { (int)CarSAStatusE.HotlapWarning, "#FF0000" },
            { (int)CarSAStatusE.HotlapCaution, "#FFFF00" },
            { (int)CarSAStatusE.HotlapHot, "#FFFF00" },
            { (int)CarSAStatusE.CoolLapWarning, "#FF0000" },
            { (int)CarSAStatusE.CoolLapCaution, "#FFFF00" },
            { (int)CarSAStatusE.FasterClass, "#000000" },
            { (int)CarSAStatusE.SlowerClass, "#000000" },
            { (int)CarSAStatusE.Racing, "#008000" },
            { (int)CarSAStatusE.LappingYou, "#0000FF" },
            { (int)CarSAStatusE.BeingLapped, "#ADD8E6" }
        };

        private static readonly Dictionary<string, string> DefaultCarSABorderColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { CarSAStyleResolver.BorderModeTeam, "#FF69B4" },
            { CarSAStyleResolver.BorderModeBad, "#FF0000" },
            { CarSAStyleResolver.BorderModeLead, "#FF00FF" },
            { CarSAStyleResolver.BorderModeOtherClass, "#0000FF" },
            { CarSAStyleResolver.BorderModeDefault, "#F5F5F5" }
        };
        private const int LapTimeConfidenceSwitchOn = 50;
        private const double StableFuelPerLapDeadband = 0.03; // 0.03 L/lap chosen to suppress lap-to-lap noise and prevent delta chatter
        private const double StableLapTimeDeadband = 0.3; // 0.3 s chosen to stop projection lap time source flapping on small variance
        private int _lastPitWindowState = -1;
        private string _lastPitWindowLabel = string.Empty;
        private DateTime _lastPitWindowLogUtc = DateTime.MinValue;
        private const double ProfileAllowedConfidenceCeiling = 20.0;

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
            SimHub.Logging.Current.Info($"[LalaPlugin:Profiles] Changes to '{ActiveProfile?.ProfileName}' saved.");
        }

        private static double ClampToRange(double value, double min, double max, double defaultValue)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return defaultValue;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private double GetFuelReadyConfidenceThreshold()
        {
            double value = Settings?.FuelReadyConfidence ?? FuelReadyConfidenceDefault;
            value = ClampToRange(value, 0.0, 100.0, FuelReadyConfidenceDefault);
            return value;
        }

        private double GetStintFuelMarginFraction()
        {
            double value = Settings?.StintFuelMarginPct ?? StintFuelMarginPctDefault;
            value = ClampToRange(value, 0.0, 30.0, StintFuelMarginPctDefault);
            return value / 100.0;
        }

        private int GetShiftAssistBeepDurationMs()
        {
            int value = Settings?.ShiftAssistBeepDurationMs ?? ShiftAssistBeepDurationMsDefault;
            if (value < ShiftAssistBeepDurationMsMin) value = ShiftAssistBeepDurationMsMin;
            if (value > ShiftAssistBeepDurationMsMax) value = ShiftAssistBeepDurationMsMax;
            return value;
        }

        private int GetShiftAssistLeadTimeMs()
        {
            int value = Settings?.ShiftAssistLeadTimeMs ?? ShiftAssistLeadTimeMsDefault;
            if (value < ShiftAssistLeadTimeMsMin) value = ShiftAssistLeadTimeMsMin;
            if (value > ShiftAssistLeadTimeMsMax) value = ShiftAssistLeadTimeMsMax;
            return value;
        }

        private bool IsShiftAssistBeepSoundEnabled()
        {
            return Settings?.ShiftAssistBeepSoundEnabled != false;
        }

        private int GetShiftAssistBeepVolumePct()
        {
            int value = Settings?.ShiftAssistBeepVolumePct ?? 100;
            if (value < 0) value = 0;
            if (value > 100) value = 100;
            return value;
        }

        private static int ClampShiftAssistDelayMs(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0;
            }

            if (value < 50.0 || value > 2000.0)
            {
                return 0;
            }

            return (int)Math.Round(value);
        }

        private static int ClampShiftAssistDelayMs(double value, int maxInclusive)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0;
            }

            if (value < 0.0)
            {
                return 0;
            }

            if (value > maxInclusive)
            {
                value = maxInclusive;
            }

            return (int)Math.Round(value);
        }

        private static int NormalizeShiftAssistGear(int gear)
        {
            return gear >= 1 && gear <= 8 ? gear : 0;
        }

        private void ClearShiftAssistDelayPending()
        {
            _shiftAssistPendingDelayActive = false;
            _shiftAssistPendingDelayGear = 0;
            _shiftAssistPendingDelayStartTs = 0;
            _shiftAssistPendingDelayRpmAtCue = 0;
            _shiftAssistPendingDelayDownshiftSinceTs = 0;
        }

        private static int GetElapsedMsFromTimestamp(long startTs, long endTs)
        {
            if (startTs <= 0 || endTs <= startTs)
            {
                return 0;
            }

            double elapsedMs = (endTs - startTs) * 1000.0 / Stopwatch.Frequency;
            return ClampShiftAssistDelayMs(elapsedMs, int.MaxValue);
        }

        private int GetShiftAssistPendingAgeMs(long nowTs)
        {
            if (!_shiftAssistPendingDelayActive)
            {
                return 0;
            }

            return GetElapsedMsFromTimestamp(_shiftAssistPendingDelayStartTs, nowTs);
        }

        private int GetShiftAssistPendingDownshiftAgeMs(long nowTs)
        {
            if (!_shiftAssistPendingDelayActive || _shiftAssistPendingDelayDownshiftSinceTs <= 0)
            {
                return 0;
            }

            return GetElapsedMsFromTimestamp(_shiftAssistPendingDelayDownshiftSinceTs, nowTs);
        }

        private void LatchShiftAssistDelayDiagnostics(string captureEvent, string beepType, int capturedMs, DateTime nowUtc)
        {
            _shiftAssistDelayDiagLatchedCapturedMs = capturedMs;
            _shiftAssistDelayDiagLatchedEvent = string.IsNullOrWhiteSpace(captureEvent) ? "NONE" : captureEvent;
            _shiftAssistDelayDiagLatchedBeepType = string.IsNullOrWhiteSpace(beepType) ? "NONE" : beepType;
            _shiftAssistDelayDiagLatchedUtc = nowUtc;
        }

        private static int MapShiftAssistDelayCaptureState(string captureEvent)
        {
            switch (captureEvent)
            {
                case "ARM":
                    return 1;
                case "CAPTURE":
                    return 2;
                case "CANCEL_BRAKE":
                    return 3;
                case "CANCEL_DOWN":
                    return 4;
                case "CANCEL_TIMEOUT":
                    return 5;
                default:
                    return 0;
            }
        }

        private void ClearShiftAssistDelayDiagnosticsLatch()
        {
            _shiftAssistDelayDiagLatchedCapturedMs = 0;
            _shiftAssistDelayDiagLatchedEvent = "NONE";
            _shiftAssistDelayDiagLatchedBeepType = "NONE";
            _shiftAssistDelayDiagLatchedUtc = DateTime.MinValue;
            _shiftAssistDelayCaptureState = 0;
        }

        private void ResetShiftAssistDelayStats()
        {
            Array.Clear(_shiftAssistDelaySamples, 0, _shiftAssistDelaySamples.Length);
            Array.Clear(_shiftAssistDelaySampleCounts, 0, _shiftAssistDelaySampleCounts.Length);
            Array.Clear(_shiftAssistDelaySampleNextIndex, 0, _shiftAssistDelaySampleNextIndex.Length);
            Array.Clear(_shiftAssistDelaySampleSums, 0, _shiftAssistDelaySampleSums.Length);
            _shiftAssistAudioDelayMs = 0;
            _shiftAssistAudioDelayLastIssuedUtc = DateTime.MinValue;
            ClearShiftAssistDelayPending();
            ClearShiftAssistDelayDiagnosticsLatch();
        }

        private int GetShiftAssistAudioDelayAgeMs()
        {
            if (_shiftAssistAudioDelayLastIssuedUtc == DateTime.MinValue)
            {
                return 0;
            }

            return ClampShiftAssistDelayMs((DateTime.UtcNow - _shiftAssistAudioDelayLastIssuedUtc).TotalMilliseconds, int.MaxValue);
        }

        private void AddShiftAssistDelaySample(int gear, int delayMs)
        {
            if (gear < 1 || gear > 8 || delayMs <= 0)
            {
                return;
            }

            int idx = gear - 1;
            int count = _shiftAssistDelaySampleCounts[idx];
            int writeIndex = _shiftAssistDelaySampleNextIndex[idx];
            if (count >= ShiftAssistDelayHistorySize)
            {
                _shiftAssistDelaySampleSums[idx] -= _shiftAssistDelaySamples[idx, writeIndex];
            }
            else
            {
                _shiftAssistDelaySampleCounts[idx] = count + 1;
            }

            _shiftAssistDelaySamples[idx, writeIndex] = delayMs;
            _shiftAssistDelaySampleSums[idx] += delayMs;
            _shiftAssistDelaySampleNextIndex[idx] = (writeIndex + 1) % ShiftAssistDelayHistorySize;
        }

        private int GetShiftAssistDelayAverageMs(int gear)
        {
            if (gear < 1 || gear > 8)
            {
                return 0;
            }

            int idx = gear - 1;
            int count = _shiftAssistDelaySampleCounts[idx];
            if (count <= 0)
            {
                return 0;
            }

            return (int)Math.Round((double)_shiftAssistDelaySampleSums[idx] / count);
        }

        private int GetShiftAssistDelayCount(int gear)
        {
            if (gear < 1 || gear > 8)
            {
                return 0;
            }

            return _shiftAssistDelaySampleCounts[gear - 1];
        }

        private int GetShiftAssistLearnSamplesForGear(int gear)
        {
            return _shiftAssistLearningEngine.GetSampleCount(_shiftAssistActiveGearStackId, gear);
        }

        private int GetShiftAssistLearnedRpmForGear(int gear)
        {
            return _shiftAssistLearningEngine.GetLearnedRpm(_shiftAssistActiveGearStackId, gear);
        }

        private int GetShiftAssistTargetRpmForGear(int gear)
        {
            if (gear < 1 || gear > 8 || ActiveProfile == null)
            {
                return 0;
            }

            return ActiveProfile.GetShiftTargetForGear(_shiftAssistActiveGearStackId, gear);
        }

        private bool GetShiftAssistLockedForGear(int gear)
        {
            if (gear < 1 || gear > 8 || ActiveProfile == null)
            {
                return false;
            }

            var stack = ActiveProfile.EnsureShiftStack(_shiftAssistActiveGearStackId);
            return stack.ShiftLocked[gear - 1];
        }

        private bool IsShiftAssistLearnSavedPulseActive()
        {
            return DateTime.UtcNow <= _shiftAssistLearnSavedPulseUntilUtc;
        }

        private static string ToLearningStateText(ShiftAssistLearningState state)
        {
            switch (state)
            {
                case ShiftAssistLearningState.Armed: return "Armed";
                case ShiftAssistLearningState.Sampling: return "Sampling";
                case ShiftAssistLearningState.Complete: return "Complete";
                case ShiftAssistLearningState.Rejected: return "Rejected";
                default: return "Off";
            }
        }

        private static bool SetShiftAssistGearLock(ShiftStackData stack, int gear, bool locked)
        {
            if (stack == null || gear < 1 || gear > 8)
            {
                return false;
            }

            int idx = gear - 1;
            if (stack.ShiftLocked[idx] == locked)
            {
                return false;
            }

            stack.ShiftLocked[idx] = locked;
            return true;
        }

        private void ExecuteShiftAssistLockAction(int gear, Func<bool, bool> resolver, string actionName)
        {
            if (ActiveProfile == null || resolver == null || gear < 1 || gear > 8)
            {
                return;
            }

            var stack = ActiveProfile.EnsureShiftStack(_shiftAssistActiveGearStackId);
            int idx = gear - 1;
            bool desired = resolver(stack.ShiftLocked[idx]);
            if (!SetShiftAssistGearLock(stack, gear, desired))
            {
                return;
            }

            ProfilesViewModel?.SaveProfiles();
            ProfilesViewModel?.RefreshShiftAssistRuntimeStats();
            SimHub.Logging.Current.Info($"[LalaPlugin:ShiftAssist] {actionName} stack='{_shiftAssistActiveGearStackId}' gear=G{gear} locked={desired}");
        }

        private bool TryResolveShiftAssistActiveStackId(out string activeStackId)
        {
            activeStackId = _shiftAssistActiveGearStackId;
            if (string.IsNullOrWhiteSpace(activeStackId))
            {
                return false;
            }

            activeStackId = activeStackId.Trim();
            return activeStackId.Length > 0;
        }

        private void ResetShiftAssistTargetsForActiveStack(string actionName)
        {
            string activeStackId;
            if (ActiveProfile == null || !TryResolveShiftAssistActiveStackId(out activeStackId))
            {
                SimHub.Logging.Current.Info($"[LalaPlugin:ShiftAssist] {actionName} stack='n/a' changed=false");
                return;
            }

            var stack = ActiveProfile.EnsureShiftStack(activeStackId);
            bool changed = false;
            for (int i = 0; i < 8; i++)
            {
                if (stack.ShiftRPM[i] != 0)
                {
                    stack.ShiftRPM[i] = 0;
                    changed = true;
                }

                if (stack.ShiftLocked[i])
                {
                    stack.ShiftLocked[i] = false;
                    changed = true;
                }
            }

            if (changed)
            {
                ProfilesViewModel?.SaveProfiles();
            }

            _shiftAssistTargetCurrentGear = 0;
            ProfilesViewModel?.RefreshShiftAssistRuntimeStats();
            SimHub.Logging.Current.Info($"[LalaPlugin:ShiftAssist] {actionName} stack='{activeStackId}' changed={changed}");
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
                    $"[LalaPlugin:Fuel Burn] Captured seed from session '{fromSessionType}' for car='{_seedCarModel}', track='{_seedTrackKey}': " +
                    $"dry={_seedDryFuelPerLap:F3} (n={_seedDrySampleCount}), wet={_seedWetFuelPerLap:F3} (n={_seedWetSampleCount}).");
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"[LalaPlugin:Fuel Burn] CaptureFuelSeedForNextSession error: {ex.Message}");
            }
        }

        private void ResetLiveMaxFuelTracking()
        {
            LiveCarMaxFuel = 0.0;
            EffectiveLiveMaxTank = 0.0;
            _lastValidLiveMaxFuel = 0.0;
            _lastAnnouncedMaxFuel = -1;
        }

        private void ResetLiveFuelModelForNewSession(string newSessionType, bool applySeeds)
        {
            // Clear per-lap / model state
            ResetLiveMaxFuelTracking();
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
            _lastValidLapMs = 0;
            _lastValidLapNumber = -1;
            _wetFuelPersistLogged = false;
            _dryFuelPersistLogged = false;
            _msgV1InfoLogged = false;
            _lastIsWetTyres = null;
            TrackWetness = 0;
            TrackWetnessLabel = "NA";
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
            _isWetMode = false;
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
            PitWindowState = 6;
            PitWindowLabel = "N/A";
            IsPitWindowOpen = false;
            PitWindowOpeningLap = 0;
            PitWindowClosingLap = 0;
            _lastPitWindowState = -1;
            _lastPitWindowLabel = string.Empty;
            _lastPitWindowLogUtc = DateTime.MinValue;

            FuelCalculator?.ResetTrackConditionOverrideForSessionChange();
            FuelCalculator?.ResetPlannerManualOverrides();

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
                IsRaceSession(newSessionType) &&
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
                    LiveFuelPerLap = _isWetMode
                        ? (_avgWetFuelPerLap > 0 ? _avgWetFuelPerLap : _avgDryFuelPerLap)
                        : (_avgDryFuelPerLap > 0 ? _avgDryFuelPerLap : _avgWetFuelPerLap);

                    Confidence = ComputeFuelModelConfidence(_isWetMode);

                    try
                    {
                        SimHub.Logging.Current.Info(
                            $"[LalaPlugin:Fuel Burn] Seeded race model from previous session (car='{_seedCarModel}', track='{_seedTrackKey}'): " +
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
                SimHub.Logging.Current.Info("[LalaPlugin:Fuel Burn] Car/track change detected – clearing seeds and confidence");
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
                    IsDrivingSessionForFuelSeed(fromSession);

                bool isEnteringRace = IsRaceSession(toSession);

                if (isDrivingFrom && isEnteringRace)
                {
                    // Use fuel learnt in non-race driving sessions as seed for Race.
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
                SimHub.Logging.Current.Error($"[LalaPlugin:Fuel Burn] HandleSessionChangeForFuelModel error: {ex.Message}");
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
            // iRacing SessionState is numeric.
            // If we can't parse it, fail safe and assume NOT running.
            if (string.IsNullOrWhiteSpace(sessionStateToken))
                return false;

            if (int.TryParse(sessionStateToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numericState))
                return numericState == 4; // iRacing "racing"

            // Non-numeric values are unexpected in iRacing; do not guess.
            return false;
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
                        $"[LalaPlugin:Lap Detector] Pending expired lap target={_lapDetectorPendingLapTarget} pct={_lapDetectorPendingLastPct:F3} session state={_lapDetectorPendingSessionState}");

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
                                $"[LalaPlugin:Lap Detector] Pending rejected lap={lapCount} prev lap={_lapDetectorLastCompleted} " +
                                $"last track point={lastPctNormalized:F3} current track point={curPctNormalized:F3} speed kmh={speedKmh:F1} session state={sessionStateToken}"
                                );
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
                        SimHub.Logging.Current.Info($"[LalaPlugin:Lap Detector] Ignored reason=low speed lap={lapCount} speed kmh={speedKmh:F1} session state={sessionStateToken}"
);
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
                            $"[LalaPlugin:Lap Detector] Pending armed lap={lapCount} prev lap={_lapDetectorLastCompleted} " +
                            $"last track point={lastPctNormalized:F3} current track point={curPctNormalized:F3} speed kmh={speedKmh:F1} session state={sessionStateToken} " +
                            $"near S/F={nearStartFinish} far from S/F={pctFarFromSf} pct impossible={pctImpossible}"
                            );
                    }

                    return false;
                }

                if (!nearStartFinish && lastPctNormalized >= 0 && curPctNormalized >= 0 && ShouldLogLapDetector("atypical"))
                {
                    SimHub.Logging.Current.Info(
                        $"[LalaPlugin:Lap Detector] lap_crossed source=CompletedLaps " +
                        $"lap={lapCount} prev_lap={_lapDetectorLastCompleted} " +
                        $"trackpct.last={lastPctNormalized:F3} trackpct.cur={curPctNormalized:F3} " +
                        $"near_sf={nearStartFinish} far_from_sf={pctFarFromSf} pct_impossible={pctImpossible} " +
                        $"speed_kmh={speedKmh:F1} session_state={sessionStateToken}");
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
            bool pitTripActive,
            double deltaLitres,
            double requiredLitres,
            double stableFuelPerLap,
            double stableLapsRemaining,
            double currentFuel,
            double afterZeroUsedSeconds,
            string afterZeroSource,
            double timerZeroSessionTime,
            double sessionTimeRemain,
            double projectedLapsRemaining,
            double projectionLapSeconds,
            double projectedDriveSecondsRemaining)
        {
            int posClass = 0;
            int posOverall = 0;
            double gapToLeaderSec = double.NaN;
            bool hasRaceState = _opponentsEngine?.TryGetPlayerRaceState(out posClass, out posOverall, out gapToLeaderSec) == true;
            string posClassText = (hasRaceState && posClass > 0) ? $"P{posClass}" : "na";
            string posOverallText = (hasRaceState && posOverall > 0) ? $"P{posOverall}" : "na";
            string gapText = (!double.IsNaN(gapToLeaderSec) && !double.IsInfinity(gapToLeaderSec))
                ? gapToLeaderSec.ToString("F1", CultureInfo.InvariantCulture)
                : "na";

            SimHub.Logging.Current.Info(
                $"[LalaPlugin:PACE] Lap {lapNumber}: " +
                $"ok={paceAccepted} reason={paceReason} " +
                $"lap_s={lastLapSeconds:F3} baseline_s={paceBaseline} delta_s={paceDelta} " +
                $"stint_avg_s={stintAvg:F3} last5_avg_s={last5Avg:F3} conf_pct={paceConfidence} " +
                $"leader_lap_s={leaderLapSeconds:F3} leader_avg_s={leaderAvgSeconds:F3} leader_samples={leaderSampleCount} " +
                $"posClass={posClassText} posOverall={posOverallText} gapLdr={gapText}"
            );
            SimHub.Logging.Current.Info(
                $"[LalaPlugin:FUEL PER LAP] Lap {lapNumber}: " +
                $"ok={fuelAccepted} reason={fuelReason} mode={(isWetMode ? "wet" : "dry")} " +
                $"live_fpl={liveFuelPerLap:F3} " +
                $"window_dry={validDryLaps} window_wet={validWetLaps} " +
                $"max_session_fpl={maxFuelPerLapSession:F3} " +
                $"fuel_conf_pct={fuelConfidence} overall_conf_pct={overallConfidence} " +
                $"pit_trip_active={pitTripActive}"
            );
            SimHub.Logging.Current.Info(
                $"[LalaPlugin:FUEL DELTA] Lap {lapNumber}: " +
                $"current_l={currentFuel:F1} required_l={requiredLitres:F1} delta_l={deltaLitres:F1} " +
                $"stable_fpl={stableFuelPerLap:F3} stable_laps={stableLapsRemaining:F2}"
            );
            SimHub.Logging.Current.Info(
                $"[LalaPlugin:RACE PROJECTION] Lap {lapNumber}: " +
                $"after_zero_used_s={afterZeroUsedSeconds:F1} source={afterZeroSource} " +
                $"timer0_s={FormatSecondsOrNA(timerZeroSessionTime)} " +
                $"session_remain_s={FormatSecondsOrNA(sessionTimeRemain)} " +
                $"projected_laps={projectedLapsRemaining:F2} " +
                $"projection_lap_s={projectionLapSeconds:F3} " +
                $"projected_remain_s={projectedDriveSecondsRemaining:F1}"
            );
        }

        private void UpdateLiveFuelCalcs(GameData data, PluginManager pluginManager)
        {
            // --- 1) Gather required data ---
            UpdateLiveMaxFuel(pluginManager);
            double currentFuel = data.NewData?.Fuel ?? 0.0;
            double rawLapPct = data.NewData?.TrackPositionPercent ?? 0.0;
            double fallbackFuelPerLap = Convert.ToDouble(
                PluginManager.GetPropertyValue("DataCorePlugin.Computed.Fuel_LitersPerLap") ?? 0.0
            );

            double effectiveMaxTank = EffectiveLiveMaxTank;

            double sessionTime = SafeReadDouble(pluginManager, "DataCorePlugin.GameRawData.Telemetry.SessionTime", 0.0);
            double sessionTimeRemain = SafeReadDouble(pluginManager, "DataCorePlugin.GameRawData.Telemetry.SessionTimeRemain", double.NaN);

            int trackWetness = ReadTrackWetness(pluginManager);
            TrackWetness = trackWetness;
            TrackWetnessLabel = MapWetnessLabel(trackWetness);

            int playerTireCompoundRaw;
            string extraPropRaw;
            string tyreSource;
            bool isWetTyres = TryReadIsWetTyres(pluginManager, out playerTireCompoundRaw, out extraPropRaw, out tyreSource);
            bool hasTyreSignal = !string.Equals(tyreSource, "unknown", StringComparison.Ordinal);

            if (hasTyreSignal)
            {
                if (_lastIsWetTyres.HasValue && _lastIsWetTyres.Value != isWetTyres)
                {
                    string from = _lastIsWetTyres.Value ? "Wet" : "Dry";
                    string to = isWetTyres ? "Wet" : "Dry";
                    SimHub.Logging.Current.Info(
                        $"[LalaPlugin:Surface] Mode flip {from}->{to} (tyres={(isWetTyres ? "Wet" : "Dry")}, " +
                        $"PlayerTireCompound={playerTireCompoundRaw}, ExtraProp={extraPropRaw ?? "null"}, trackWetness={trackWetness})");
                }
                _lastIsWetTyres = isWetTyres;
                _isWetMode = isWetTyres;
            }

            // Pit detection: use both signals (some installs expose only one reliably)
            bool isInPitLaneFlag = (data.NewData?.IsInPitLane ?? 0) != 0;
            bool isOnPitRoadFlag = Convert.ToBoolean(
                PluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.OnPitRoad") ?? false
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
                var leaderLap = ReadLeaderLapTimeSeconds(PluginManager, data, Pace_Last5LapAvgSec, LiveLeaderAvgPaceSeconds, IsVerboseDebugLoggingOn);
                leaderLastLapSec = leaderLap.seconds;
                leaderLapWasFallback = leaderLap.isFallback;

                if (leaderLastLapSec <= 0.0 && _recentLeaderLapTimes.Count > 0)
                {
                    // Feed dropped: clear leader pace so downstream calcs don't reuse stale values
                    if (!_leaderPaceClearedLogged)
                    {
                        SimHub.Logging.Current.Info(string.Format(
                            "[LalaPlugin:Leader Lap] clearing leader pace (feed dropped), lastAvg={0:F3}",
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

                    // Decide baseline once (priority: live_median -> profile_avg -> session_pb)
                    double liveMedianPace = ComputeStableMedian(_recentLapTimes);
                    double stableAvgPace = liveMedianPace;
                    string paceSource = "live_median";

                    double profileAvgPace = 0.0;
                    try
                    {
                        if (ActiveProfile != null)
                        {
                            var tr =
                                ActiveProfile.FindTrack(CurrentTrackKey) ??
                                ActiveProfile.ResolveTrackByNameOrKey(CurrentTrackName);

                            if (tr?.AvgLapTimeDry > 0)
                                profileAvgPace = tr.AvgLapTimeDry.Value / 1000.0; // ms -> sec
                        }
                    }
                    catch { /* ignore */ }

                    double sessionPbPace = (_lastSeenBestLap > TimeSpan.Zero) ? _lastSeenBestLap.TotalSeconds : 0.0;

                    if (stableAvgPace <= 0.0 && profileAvgPace > 0.0)
                    {
                        stableAvgPace = profileAvgPace;
                        paceSource = "profile_avg";
                    }
                    if (stableAvgPace <= 0.0 && sessionPbPace > 0.0)
                    {
                        stableAvgPace = sessionPbPace;
                        paceSource = "session_pb";
                    }

                    _usingFallbackPaceProfile = (paceSource == "profile_avg");

                    if (IsVerboseDebugLoggingOn)
                    {
                        SimHub.Logging.Current.Debug(
                            $"[LalaPlugin:Pace] baseline_used chosen={paceSource} baseline_s={stableAvgPace:F3} " +
                            $"live_median_s={liveMedianPace:F3} profile_avg_s={profileAvgPace:F3} session_pb_s={sessionPbPace:F3}");
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

                    bool paceRejected = false;
                    string paceRejectReason = "";
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

                    double fuelUsed = (_lapStartFuel > 0 && currentFuel >= 0)
                        ? (_lapStartFuel - currentFuel)
                        : 0.0;

                    // 1) Global race warm-up: ignore very early race laps (same as fuel)
                    if (completedLapsNow <= 1)
                    {
                        paceRejected = true;
                        paceRejectReason = "race-warmup";
                    }

                    // 2) Any pit involvement this lap? Ignore for pace.
                    if (!paceRejected && pitTripActive)
                    {
                        paceRejected = true;
                        paceRejectReason = "pit-lap";
                    }

                    // 3) First lap after pit exit – tyres cold
                    if (!paceRejected && _lapsSincePitExit == 0)
                    {
                        paceRejected = true;
                        paceRejectReason = "pit-warmup";
                    }

                    // 4) Serious off / incident laps
                    if (!paceRejected && _hadOffTrackThisLap)
                    {
                        paceRejected = true;
                        paceRejectReason = _latchedIncidentReason.HasValue
                            ? $"incident:{_latchedIncidentReason.Value}"
                            : "incident";
                    }

                    // 5) Obvious junk lap times
                    if (!paceRejected)
                    {
                        if (lastLapSec <= 20.0 || lastLapSec >= 900.0)
                        {
                            paceRejected = true;
                            paceRejectReason = "bad-lap-time";
                        }
                    }

                    // 6) Timing bracket: moderate + gross outliers
                    if (!paceRejected && _recentLapTimes.Count >= 3 && paceBaselineForLog > 0)
                    {
                        double delta = paceDeltaForLog; // +ve = slower than recent average

                        // 6a) Gross outliers: >20s away from our current clean pace, either direction.
                        //     This catches things like huge course cuts or tow / timing glitches.
                        if (Math.Abs(delta) > 20.0)
                        {
                            if (IsVerboseDebugLoggingOn)
                            {
                                SimHub.Logging.Current.Debug($"[LalaPlugin:Pace] Gross outlier lap {lastLapSec:F2}s (avg={paceBaselineForLog:F2}s, Δ={delta:F1}s)");
                            }
                            paceRejected = true;
                            paceRejectReason = "gross-outlier";
                        }
                        // 6b) Normal too-slow laps: more than ~6s slower than our recent clean pace.
                        //     Keeps spins / heavy traffic / yellows out of the model, but allows faster laps.
                        else if (delta > 6.0)
                        {
                            if (IsVerboseDebugLoggingOn)
                            {
                                SimHub.Logging.Current.Debug($"[LalaPlugin:Pace] Rejected too-slow lap {lastLapSec:F2}s (avg={paceBaselineForLog:F2}s, Δ={delta:F1}s)");
                            }
                            paceRejected = true;
                            paceRejectReason = "slow-outlier";
                        }
                    }

                    bool paceAccepted = !paceRejected;
                    string paceReason = paceAccepted
                        ? "accepted"
                        : (string.IsNullOrEmpty(paceRejectReason) ? "rejected" : paceRejectReason);

                    bool fuelAccepted = false;
                    string fuelReason = "pace-rejected";
                    if (paceAccepted)
                    {
                        bool fuelRejected = false;
                        string fuelRejectReason = "";

                        // 7) Obvious fuel telemetry junk
                        if (!fuelRejected)
                        {
                            // coarse cap: 20% of tank or 10 L, whichever is larger
                            double maxPlausibleHard = Math.Max(10.0, 0.20 * Math.Max(effectiveMaxTank, 50.0));
                            if (fuelUsed <= 0.05)
                            {
                                fuelRejected = true;
                                fuelRejectReason = "fuel<=0";
                            }
                            else if (fuelUsed > maxPlausibleHard)
                            {
                                fuelRejected = true;
                                fuelRejectReason = "fuelTooHigh";
                            }
                        }

                        // 8) Profile-based sanity bracket [0.5, 1.5] × baseline
                        if (!fuelRejected)
                        {
                            var (baselineDry, baselineWet) = GetProfileFuelBaselines();
                            double baseline = _isWetMode ? baselineWet : baselineDry;

                            if (baseline > 0.0)
                            {
                                double ratio = fuelUsed / baseline;
                                if (ratio < 0.5 || ratio > 1.5)
                                {
                                    fuelRejected = true;
                                    fuelRejectReason = string.Format("profileBracket (r={0:F2})", ratio);
                                }
                            }
                        }

                        fuelAccepted = !fuelRejected;
                        fuelReason = fuelAccepted
                            ? "accepted"
                            : (string.IsNullOrEmpty(fuelRejectReason) ? "rejected" : fuelRejectReason);
                    }

                    bool recordWetFuel = fuelAccepted && _isWetMode;
                    bool recordPaceForStats = paceAccepted;
                    bool recordFuelForStats = fuelAccepted;

                    if (recordPaceForStats)
                    {
                        _lastValidLapMs = (int)Math.Round(lastLapSec * 1000.0);
                        _lastValidLapNumber = completedLapsNow;
                    }

                    if (recordPaceForStats)
                    {
                        _recentLapTimes.Add(lastLapSec);
                        // Trim to window
                        while (_recentLapTimes.Count > LapTimeSampleCount)
                        {
                            _recentLapTimes.RemoveAt(0);
                        }

                        SessionSummaryRuntime.OnValidPaceLap(_currentSessionToken, lastLapSec);

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
                    }

                    string paceBaselineLog = (paceBaselineForLog > 0)
                        ? paceBaselineForLog.ToString("F3")
                        : "-";
                    string paceDeltaLog = (paceBaselineForLog > 0)
                        ? paceDeltaForLog.ToString("+0.000;-0.000;0.000")
                        : "-";

                    // --- Fuel per lap calculation & rolling averages ---
                    if (recordFuelForStats)
                    {
                        var window = recordWetFuel ? _recentWetFuelLaps : _recentDryFuelLaps;

                        if (recordWetFuel)
                            _freshWetSamplesInWindow++;
                        else
                            _freshDrySamplesInWindow++;

                        window.Add(fuelUsed);
                        SessionSummaryRuntime.OnValidFuelLap(_currentSessionToken, fuelUsed);
                        while (window.Count > FuelWindowSize)
                        {
                            if (recordWetFuel && _hasActiveWetSeed)
                            {
                                window.RemoveAt(0);
                                _hasActiveWetSeed = false;
                            }
                            else if (!recordWetFuel && _hasActiveDrySeed)
                            {
                                window.RemoveAt(0);
                                _hasActiveDrySeed = false;
                            }
                            else
                            {
                                window.RemoveAt(0);
                                if (recordWetFuel && _freshWetSamplesInWindow > 0)
                                    _freshWetSamplesInWindow--;
                                else if (!recordWetFuel && _freshDrySamplesInWindow > 0)
                                    _freshDrySamplesInWindow--;
                            }
                        }

                        if (recordWetFuel)
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
                        LiveFuelPerLap = _isWetMode
                            ? (_avgWetFuelPerLap > 0 ? _avgWetFuelPerLap : _avgDryFuelPerLap)
                            : (_avgDryFuelPerLap > 0 ? _avgDryFuelPerLap : _avgWetFuelPerLap);

                        _usingFallbackFuelProfile = false;
                        Confidence = ComputeFuelModelConfidence(_isWetMode);

                        // Overall confidence is computed in its getter from Confidence + PaceConfidence

                        FuelCalculator?.SetLiveFuelPerLap(LiveFuelPerLap);
                        FuelCalculator?.SetLiveConfidenceLevels(Confidence, PaceConfidence, OverallConfidence);

                        // Update session max for current mode if available
                        double maxForMode = _isWetMode ? _maxWetFuelPerLap : _maxDryFuelPerLap;
                        if (maxForMode > 0)
                        {
                            _maxFuelPerLapSession = maxForMode;
                            FuelCalculator?.SetMaxFuelPerLap(_maxFuelPerLapSession);
                        }

                        FuelCalculator?.SetLiveFuelWindowStats(
                            _avgDryFuelPerLap, _minDryFuelPerLap, _maxDryFuelPerLap, _validDryLaps,
                            _avgWetFuelPerLap, _minWetFuelPerLap, _maxWetFuelPerLap, _validWetLaps);

                        if (ActiveProfile != null)
                        {
                            var trackRecord = ActiveProfile.FindTrack(CurrentTrackKey)
                                ?? ActiveProfile.ResolveTrackByNameOrKey(CurrentTrackName);
                            if (trackRecord != null)
                            {
                                if (_isWetMode)
                                {
                                    trackRecord.WetFuelSampleCount = _validWetLaps;

                                    if (!trackRecord.WetConditionsLocked && _validWetLaps >= FuelPersistMinLaps)
                                    {
                                        if (_minWetFuelPerLap > 0) trackRecord.MinFuelPerLapWet = _minWetFuelPerLap;
                                        if (_avgWetFuelPerLap > 0) trackRecord.AvgFuelPerLapWet = _avgWetFuelPerLap;
                                        if (_maxWetFuelPerLap > 0) trackRecord.MaxFuelPerLapWet = _maxWetFuelPerLap;
                                        trackRecord.MarkFuelUpdatedWet("Telemetry");
                                        if (!_wetFuelPersistLogged)
                                        {
                                            SimHub.Logging.Current.Info(
                                                $"[LalaPlugin:Profile/Fuel] Persisted Wet fuel stats: " +
                                                $"samples={_validWetLaps} avg={_avgWetFuelPerLap:F3} min={_minWetFuelPerLap:F3} max={_maxWetFuelPerLap:F3} " +
                                                $"locked={trackRecord.WetConditionsLocked}");
                                            _wetFuelPersistLogged = true;
                                        }
                                    }
                                }
                                else
                                {
                                    trackRecord.DryFuelSampleCount = _validDryLaps;

                                    if (!trackRecord.DryConditionsLocked && _validDryLaps >= FuelPersistMinLaps)
                                    {
                                        if (_minDryFuelPerLap > 0) trackRecord.MinFuelPerLapDry = _minDryFuelPerLap;
                                        if (_avgDryFuelPerLap > 0) trackRecord.AvgFuelPerLapDry = _avgDryFuelPerLap;
                                        if (_maxDryFuelPerLap > 0) trackRecord.MaxFuelPerLapDry = _maxDryFuelPerLap;
                                        trackRecord.MarkFuelUpdatedDry("Telemetry");
                                        if (!_dryFuelPersistLogged)
                                        {
                                            SimHub.Logging.Current.Info(
                                                $"[LalaPlugin:Profile/Fuel] Persisted Dry fuel stats: " +
                                                $"samples={_validDryLaps} avg={_avgDryFuelPerLap:F3} min={_minDryFuelPerLap:F3} max={_maxDryFuelPerLap:F3} " +
                                                $"locked={trackRecord.DryConditionsLocked}");
                                            _dryFuelPersistLogged = true;
                                        }
                                    }
                                }

                                int paceSamples = _recentLapTimes.Count;
                                if (_isWetMode)
                                {
                                    trackRecord.WetLapTimeSampleCount = paceSamples;
                                }
                                else
                                {
                                    trackRecord.DryLapTimeSampleCount = paceSamples;
                                }

                                bool persistedAvgLap = false;
                                int persistedMs = 0;
                                if (paceSamples >= FuelPersistMinLaps && Pace_StintAvgLapTimeSec > 0)
                                {
                                    int ms = (int)Math.Round(Pace_StintAvgLapTimeSec * 1000.0);
                                    if (ms > 0)
                                    {
                                        if (_isWetMode)
                                        {
                                            if (!trackRecord.WetConditionsLocked)
                                            {
                                                trackRecord.AvgLapTimeWet = ms;
                                                trackRecord.MarkAvgLapUpdatedWet("Telemetry");
                                                persistedAvgLap = true;
                                                persistedMs = ms;
                                            }
                                        }
                                        else
                                        {
                                            if (!trackRecord.DryConditionsLocked)
                                            {
                                                trackRecord.AvgLapTimeDry = ms;
                                                trackRecord.MarkAvgLapUpdatedDry("Telemetry");
                                                persistedAvgLap = true;
                                                persistedMs = ms;
                                            }
                                        }
                                    }
                                }

                                if (persistedAvgLap)
                                {
                                    ProfilesViewModel?.SaveProfiles();
                                    string trackLabel = !string.IsNullOrWhiteSpace(trackRecord.DisplayName)
                                        ? trackRecord.DisplayName
                                        : (!string.IsNullOrWhiteSpace(CurrentTrackName) ? CurrentTrackName : trackRecord.Key ?? "(unknown track)");
                                    string carLabel = ActiveProfile?.ProfileName ?? "(unknown car)";
                                    string modeLabel = _isWetMode ? "Wet" : "Dry";
                                    bool locked = _isWetMode ? trackRecord.WetConditionsLocked : trackRecord.DryConditionsLocked;
                                    string lapText = trackRecord.MillisecondsToLapTimeString(persistedMs);
                                    SimHub.Logging.Current.Info(
                                        $"[LalaPlugin:Profile/Pace] Persisted AvgLapTime{modeLabel} for {carLabel} @ {trackLabel}: " +
                                        $"{lapText} ({persistedMs} ms), samples={paceSamples}, locked={locked}");
                                }
                            }
                        }
                    }

                    double stableFuelPerLap = LiveFuelPerLap_Stable;
                    double stableLapsRemaining = LiveLapsRemainingInRace_Stable;
                    double litresRequiredToFinish =
                        (stableLapsRemaining > 0.0 && stableFuelPerLap > 0.0)
                            ? stableLapsRemaining * stableFuelPerLap
                            : 0.0;

                    LogLapCrossingSummary(
                        completedLapsNow,
                        lastLapSec,
                        recordPaceForStats,
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
                        recordFuelForStats,
                        fuelReason,
                        _isWetMode,
                        LiveFuelPerLap,
                        _validDryLaps,
                        _validWetLaps,
                        _maxFuelPerLapSession,
                        Confidence,
                        OverallConfidence,
                        pitTripActive,
                        Fuel_Delta_LitresCurrent,
                        litresRequiredToFinish,
                        stableFuelPerLap,
                        stableLapsRemaining,
                        currentFuel,
                        _afterZeroUsedSeconds,
                        AfterZeroSource,
                        _timerZeroSessionTime,
                        sessionTimeRemain,
                        _lastProjectedLapsRemaining,
                        _lastProjectionLapSecondsUsed,
                        LiveProjectedDriveSecondsRemaining);



                    SessionSummaryRuntime.OnLapCrossed(
                        _currentSessionToken,
                        completedLapsNow,
                        lastLapTs,
                        currentFuel,
                        stableFuelPerLap,
                        Confidence,
                        stableLapsRemaining,
                        _summaryPitStopIndex,
                        (_pit?.CurrentPitPhase ?? PitPhase.None).ToString(),
                        _afterZeroUsedSeconds,
                        data.NewData?.CarModel ?? string.Empty,
                        data.NewData?.TrackName ?? string.Empty,
                        FuelCalculator?.AppliedPreset?.Name ?? string.Empty
                    );

                    // Per-lap resets for next lap (must be inside completedLapsNow scope)
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

            UpdateStableFuelPerLap(_isWetMode, fallbackFuelPerLap);

            // --- 3) Core dashboard properties (guarded by a valid consumption rate) ---
            double requestedAddLitresForSmooth = 0.0;
            double fuelPerLapForCalc = LiveFuelPerLap_Stable > 0.0
                ? LiveFuelPerLap_Stable
                : LiveFuelPerLap;
            double fuelToRequest = Convert.ToDouble(
                PluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PitSvFuel") ?? 0.0);
            if (!_isRefuelSelected)
            {
                fuelToRequest = 0.0;
            }
            double pitWindowRequestedAdd = Math.Max(0, fuelToRequest);
            double maxTankCapacity = ResolveMaxTankCapacity();

            // --- Always-on pit menu reality (NOT dependent on fuel-per-lap validity) ---
            requestedAddLitresForSmooth = pitWindowRequestedAdd;

            Pit_TankSpaceAvailable = Math.Max(0, maxTankCapacity - currentFuel);
            Pit_WillAdd = Math.Min(pitWindowRequestedAdd, Pit_TankSpaceAvailable);
            Pit_FuelOnExit = currentFuel + Pit_WillAdd;

            int strategyRequiredStops = FuelCalculator?.RequiredPitStops ?? 0;

            if (fuelPerLapForCalc <= 0)
            {
                LiveLapsRemainingInRace = 0;
                LiveLapsRemainingInRace_Stable = 0;
                DeltaLaps = 0;
                TargetFuelPerLap = 0;
                IsPitWindowOpen = false;
                PitWindowOpeningLap = 0;
                PitWindowClosingLap = 0;
                LapsRemainingInTank = 0;

                Pit_TotalNeededToEnd = 0;
                Pit_NeedToAdd = 0;
                Pit_DeltaAfterStop = 0;
                Pit_FuelSaveDeltaAfterStop = 0;
                Pit_PushDeltaAfterStop = 0;
                PitStopsRequiredByFuel = 0;
                PitStopsRequiredByPlan = 0;
                Pit_StopsRequiredToEnd = 0;

                Fuel_Delta_LitresCurrent = 0;
                Fuel_Delta_LitresPlan = 0;
                Fuel_Delta_LitresWillAdd = 0;
                Fuel_Delta_LitresCurrentPush = 0;
                Fuel_Delta_LitresPlanPush = 0;
                Fuel_Delta_LitresWillAddPush = 0;
                Fuel_Delta_LitresCurrentSave = 0;
                Fuel_Delta_LitresPlanSave = 0;
                Fuel_Delta_LitresWillAddSave = 0;

                PushFuelPerLap = 0;
                DeltaLapsIfPush = 0;
                CanAffordToPush = false;

                FuelSaveFuelPerLap = 0;
                StintBurnTarget = 0;
                StintBurnTargetBand = "current";
                FuelBurnPredictor = 0;
                FuelBurnPredictorSource = "SIMHUB";
                LiveProjectedDriveTimeAfterZero = 0;
                LiveProjectedDriveSecondsRemaining = 0;

                _afterZeroPlannerSeconds = 0.0;
                _afterZeroLiveEstimateSeconds = 0.0;
                _afterZeroUsedSeconds = 0.0;
                _afterZeroSourceUsed = string.Empty;
                _lastProjectedLapsRemaining = 0.0;
                _lastSimLapsRemaining = 0.0;
                _lastProjectionLapSecondsUsed = 0.0;

                Pace_StintAvgLapTimeSec = 0.0;
                Pace_Last5LapAvgSec = 0.0;
                Pace_LeaderDeltaToPlayerSec = 0.0;
                PaceConfidence = 0;
                PacePredictor = 0.0;
                PacePredictorSource = "SIMHUB";
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

                _afterZeroPlannerSeconds = FuelCalculator?.StrategyDriverExtraSecondsAfterZero ?? 0.0;
                _afterZeroLiveEstimateSeconds = FuelProjectionMath.EstimateDriveTimeAfterZero(
                    sessionTime,
                    sessionTimeRemain,
                    projectionLapSeconds,
                    _afterZeroPlannerSeconds,
                    _timerZeroSeen,
                    _timerZeroSessionTime);

                if (!_timerZeroSeen)
                {
                    _afterZeroLiveEstimateSeconds = 0.0;
                }

                bool liveAfterZeroValid =
                    _timerZeroSeen &&
                    !double.IsNaN(_timerZeroSessionTime) &&
                    sessionTime > _timerZeroSessionTime &&
                    _afterZeroLiveEstimateSeconds > 0.0;
                string afterZeroSourceNow = liveAfterZeroValid ? "live" : "planner";

                if (!string.Equals(afterZeroSourceNow, _afterZeroSourceUsed, StringComparison.Ordinal))
                {
                    SimHub.Logging.Current.Info(
                        $"[LalaPlugin:Drive Time Projection] After 0 Source Change from={_afterZeroSourceUsed} to={afterZeroSourceNow} " +
                        $"live valid={liveAfterZeroValid} timer0 seen={_timerZeroSeen}");

                    _afterZeroSourceUsed = afterZeroSourceNow; // <-- stops the spam
                }


                _afterZeroUsedSeconds = liveAfterZeroValid ? _afterZeroLiveEstimateSeconds : _afterZeroPlannerSeconds;

                LiveProjectedDriveTimeAfterZero = _afterZeroUsedSeconds;
                double projectedLapsRemaining = ComputeProjectedLapsRemaining(simLapsRemaining, projectionLapSeconds, sessionTimeRemain, _afterZeroUsedSeconds);

                if (projectedLapsRemaining > 0.0)
                {
                    LiveLapsRemainingInRace = projectedLapsRemaining;
                    LiveLapsRemainingInRace_Stable = LiveLapsRemainingInRace;

                    if (ShouldLogProjection(simLapsRemaining, projectedLapsRemaining))
                    {
                        LogProjectionDifference(
                            simLapsRemaining,
                            projectedLapsRemaining,
                            projectionLapSeconds,
                            LiveProjectedDriveSecondsRemaining,
                            _afterZeroSourceUsed,
                            sessionTimeRemain);
                    }
                }
                else
                {
                    LiveLapsRemainingInRace = simLapsRemaining;
                    LiveLapsRemainingInRace_Stable = LiveLapsRemainingInRace;
                }

                _lastProjectedLapsRemaining = LiveLapsRemainingInRace_Stable;
                _lastSimLapsRemaining = simLapsRemaining;
                _lastProjectionLapSecondsUsed = projectionLapSeconds;

                double fuelNeededToEnd = LiveLapsRemainingInRace_Stable * fuelPerLapForCalc;
                DeltaLaps = LapsRemainingInTank - LiveLapsRemainingInRace_Stable;

                double stableLapsRemaining = LiveLapsRemainingInRace_Stable;
                double stableFuelPerLap = LiveFuelPerLap_Stable;
                double litresRequiredToFinish =
                    (stableLapsRemaining > 0.0 && stableFuelPerLap > 0.0)
                        ? stableLapsRemaining * stableFuelPerLap
                        : fuelNeededToEnd;

                // Raw target fuel per lap if we're short on fuel
                double rawTargetFuelPerLap = (DeltaLaps < 0 && LiveLapsRemainingInRace_Stable > 0)
                    ? currentFuel / LiveLapsRemainingInRace_Stable
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
                Pit_TotalNeededToEnd = litresRequiredToFinish;
                Pit_NeedToAdd = Math.Max(0, litresRequiredToFinish - currentFuel);
                double requestedAddLitres = pitWindowRequestedAdd;
                requestedAddLitresForSmooth = requestedAddLitres;
                Pit_TankSpaceAvailable = Math.Max(0, maxTankCapacity - currentFuel);

                double safeFuelRequest = requestedAddLitres;
                Pit_WillAdd = Math.Min(safeFuelRequest, Pit_TankSpaceAvailable);

                Pit_FuelOnExit = currentFuel + Pit_WillAdd;
                double fuelSaveRate = _isWetMode ? _minWetFuelPerLap : _minDryFuelPerLap;
                if (fuelSaveRate <= 0.0 && fuelPerLapForCalc > 0.0)
                {
                    fuelSaveRate = fuelPerLapForCalc * 0.97; // light saving fallback
                }

                FuelSaveFuelPerLap = fuelSaveRate;

                Pit_DeltaAfterStop = (fuelPerLapForCalc > 0)
                    ? (Pit_FuelOnExit / fuelPerLapForCalc) - LiveLapsRemainingInRace_Stable
                    : 0;

                Pit_FuelSaveDeltaAfterStop = (fuelSaveRate > 0)
                    ? (Pit_FuelOnExit / fuelSaveRate) - LiveLapsRemainingInRace_Stable
                    : 0;

                // Pit stop counts based on requested MFD refuel amount and
                // the effective tank capacity shared with the Fuel tab's detected max.
                double litresShort = Math.Max(0, litresRequiredToFinish - currentFuel);
                int stopsRequiredByFuel = (effectiveMaxTank > 0)
                    ? (int)Math.Ceiling(litresShort / effectiveMaxTank)
                    : 0;
                int stopsRequiredByPlan = strategyRequiredStops > 0 ? strategyRequiredStops : stopsRequiredByFuel;

                PitStopsRequiredByFuel = Math.Max(0, stopsRequiredByFuel);
                PitStopsRequiredByPlan = Math.Max(0, stopsRequiredByPlan);
                Pit_StopsRequiredToEnd = PitStopsRequiredByPlan;

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
                    DeltaLapsIfPush = lapsRemainingIfPush - LiveLapsRemainingInRace_Stable;
                    CanAffordToPush = DeltaLapsIfPush >= 0.0;

                    Pit_PushDeltaAfterStop = (Pit_FuelOnExit > 0.0)
                        ? (Pit_FuelOnExit / pushFuel) - LiveLapsRemainingInRace_Stable
                        : 0.0;
                }
                else
                {
                    DeltaLapsIfPush = 0.0;
                    CanAffordToPush = false;
                    Pit_PushDeltaAfterStop = 0.0;
                }

                // --- Stint burn target: live guidance for the current tank only (no strategy intent) ---
                double stableBurn = fuelPerLapForCalc;
                double ecoBurn = FuelSaveFuelPerLap;
                double pushBurn = PushFuelPerLap;
                double marginLitres = stableBurn * GetStintFuelMarginFraction();
                double usableFuel = Math.Max(0.0, currentFuel - marginLitres);

                if (usableFuel <= 0.0 || stableBurn <= 0.0 || ecoBurn <= 0.0 || pushBurn <= 0.0)
                {
                    StintBurnTarget = 0.0;
                    StintBurnTargetBand = "HOLD";
                }
                else
                {
                    const double lapEpsilon = 1e-6;
                    double lapsPossibleStable = usableFuel / stableBurn;
                    double lapsPossibleEco = usableFuel / ecoBurn;
                    double lapsPossiblePush = usableFuel / pushBurn;

                    double lapPos = SafeReadDouble(PluginManager, "DataCorePlugin.GameRawData.Telemetry.LapDistPct", double.NaN);
                    double pitPos = SafeReadDouble(PluginManager, "DataCorePlugin.GameRawData.SessionData.DriverInfo.DriverPitTrkPct", double.NaN);
                    bool posValid = !double.IsNaN(lapPos) && lapPos >= 0.0 && lapPos <= 1.0;
                    bool pitValid = !double.IsNaN(pitPos) && pitPos >= 0.0 && pitPos <= 1.0;

                    double fracToPit = 0.0;
                    if (posValid)
                    {
                        if (pitValid)
                        {
                            fracToPit = pitPos >= lapPos
                                ? pitPos - lapPos
                                : (1.0 - lapPos) + pitPos;
                        }
                        else
                        {
                            fracToPit = 1.0 - lapPos;
                        }
                    }
                    else
                    {
                        // LapDistPct invalid: disable position correction and fall back to legacy behavior.
                        fracToPit = 0.0;
                    }

                    fracToPit = Math.Max(0.0, Math.Min(0.999999, fracToPit));

                    double stableWhole = Math.Floor((lapsPossibleStable - fracToPit) + lapEpsilon);
                    double ecoWhole = Math.Floor((lapsPossibleEco - fracToPit) + lapEpsilon);
                    double pushWhole = Math.Floor((lapsPossiblePush - fracToPit) + lapEpsilon);

                    if (stableWhole < 1.0)
                    {
                        StintBurnTarget = stableBurn;
                        StintBurnTargetBand = "HOLD";
                    }
                    else if (ecoWhole > stableWhole)
                    {
                        double desiredWhole = stableWhole + 1.0;
                        StintBurnTarget = usableFuel / Math.Max(1.0, desiredWhole + fracToPit);
                        StintBurnTargetBand = "SAVE";
                    }
                    else if (pushWhole == stableWhole)
                    {
                        double desiredWhole = stableWhole;
                        StintBurnTarget = usableFuel / Math.Max(1.0, desiredWhole + fracToPit);
                        StintBurnTargetBand = "PUSH";
                    }
                    else
                    {
                        StintBurnTarget = stableBurn;
                        StintBurnTargetBand = "HOLD";
                    }

                    double deadband = stableBurn * 0.01; // change to adapt driver behavior and accuracy of advice. Example 3.10 burn with 3.04 target would not trigger if at 2% deadband
                    if (Math.Abs(StintBurnTarget - stableBurn) <= deadband)
                    {
                        StintBurnTargetBand = "OKAY";
                    }
                }

                double fuelPlanExit = currentFuel + requestedAddLitres;
                double fuelWillAddExit = currentFuel + Pit_WillAdd;

                double ComputeDeltaLitres(double fuelAmount, double requiredLitres, bool hasRequirement)
                {
                    return hasRequirement ? fuelAmount - requiredLitres : 0.0;
                }

                bool hasNormalRequirement = stableFuelPerLap > 0.0 && stableLapsRemaining > 0.0;
                double requiredLitresNormal = hasNormalRequirement ? stableLapsRemaining * stableFuelPerLap : 0.0;
                Fuel_Delta_LitresCurrent = ComputeDeltaLitres(currentFuel, requiredLitresNormal, hasNormalRequirement);
                Fuel_Delta_LitresPlan = ComputeDeltaLitres(fuelPlanExit, requiredLitresNormal, hasNormalRequirement);
                Fuel_Delta_LitresWillAdd = ComputeDeltaLitres(fuelWillAddExit, requiredLitresNormal, hasNormalRequirement);

                bool hasPushRequirement = PushFuelPerLap > 0.0 && stableLapsRemaining > 0.0;
                double requiredLitresPush = hasPushRequirement ? stableLapsRemaining * PushFuelPerLap : 0.0;
                Fuel_Delta_LitresCurrentPush = ComputeDeltaLitres(currentFuel, requiredLitresPush, hasPushRequirement);
                Fuel_Delta_LitresPlanPush = ComputeDeltaLitres(fuelPlanExit, requiredLitresPush, hasPushRequirement);
                Fuel_Delta_LitresWillAddPush = ComputeDeltaLitres(fuelWillAddExit, requiredLitresPush, hasPushRequirement);

                bool hasSaveRequirement = FuelSaveFuelPerLap > 0.0 && stableLapsRemaining > 0.0;
                double requiredLitresSave = hasSaveRequirement ? stableLapsRemaining * FuelSaveFuelPerLap : 0.0;
                Fuel_Delta_LitresCurrentSave = ComputeDeltaLitres(currentFuel, requiredLitresSave, hasSaveRequirement);
                Fuel_Delta_LitresPlanSave = ComputeDeltaLitres(fuelPlanExit, requiredLitresSave, hasSaveRequirement);
                Fuel_Delta_LitresWillAddSave = ComputeDeltaLitres(fuelWillAddExit, requiredLitresSave, hasSaveRequirement);
            }

            // --- Pit window state exports ---
            int pitWindowState;
            string pitWindowLabel;
            int pitWindowOpeningLap = 0;
            double tankSpace = Math.Max(0, maxTankCapacity - currentFuel);
            double completedLaps = Convert.ToDouble(data.NewData?.CompletedLaps ?? 0m);
            int currentLapNumber = (int)Math.Max(1, Math.Floor(completedLaps) + 1);
            string sessionStateToken = ReadLapDetectorSessionState();
            bool sessionRunning = IsSessionRunningForLapDetector(sessionStateToken);
            bool isRaceSession = IsRaceSession(data.NewData?.SessionTypeName);
            double fuelPerLapForPitWindow = LiveFuelPerLap_Stable > 0.0 ? LiveFuelPerLap_Stable : fuelPerLapForCalc;
            int pitWindowClosingLap = 0;
            double fuelReadyConfidence = GetFuelReadyConfidenceThreshold();

            // Step 1 — Race-only gate FIRST (so Qualifying always shows N/A)
            if (!isRaceSession || !sessionRunning)
            {
                pitWindowState = 6;
                pitWindowLabel = "N/A";
                IsPitWindowOpen = false;
                pitWindowOpeningLap = 0;
                pitWindowClosingLap = 0;
            }
            // Step 1b — Inhibit when no more fuel stops required
            else if (PitStopsRequiredByFuel <= 0)
            {
                pitWindowState = 6;
                pitWindowLabel = "N/A";
                IsPitWindowOpen = false;
                pitWindowOpeningLap = 0;
                pitWindowClosingLap = 0;
            }
            // Step 0/2 — Confidence gate (now only applies in-race)
            else if (LiveFuelPerLap_StableConfidence < fuelReadyConfidence)
            {
                pitWindowState = 5;
                pitWindowLabel = "NO DATA YET";
                IsPitWindowOpen = false;
                pitWindowOpeningLap = 0;
                pitWindowClosingLap = 0;
            }
            else if (!_isRefuelSelected || pitWindowRequestedAdd <= 0.0)
            {
                pitWindowState = 4;
                pitWindowLabel = "SET FUEL!";
                IsPitWindowOpen = false;
                pitWindowOpeningLap = 0;
                pitWindowClosingLap = 0;
            }
            else if (maxTankCapacity <= 0.0)
            {
                pitWindowState = 8;
                pitWindowLabel = "TANK ERROR";
                IsPitWindowOpen = false;
                pitWindowOpeningLap = 0;
                pitWindowClosingLap = 0;
            }
            else
            {

                double stableLapsRemaining = LiveLapsRemainingInRace_Stable;
                double stableFuelPerLap = LiveFuelPerLap_Stable;

                bool pushValid = stableLapsRemaining > 0.0 && PushFuelPerLap > 0.0;
                bool stdValid = stableLapsRemaining > 0.0 && stableFuelPerLap > 0.0;
                bool ecoValid = stableLapsRemaining > 0.0 && FuelSaveFuelPerLap > 0.0;

                double needAddPush = pushValid ? Math.Max(0.0, (stableLapsRemaining * PushFuelPerLap) - currentFuel) : 0.0;
                double needAddStd = stdValid ? Math.Max(0.0, (stableLapsRemaining * stableFuelPerLap) - currentFuel) : 0.0;
                double needAddEco = ecoValid ? Math.Max(0.0, (stableLapsRemaining * FuelSaveFuelPerLap) - currentFuel) : 0.0;

                bool openPush = pushValid && tankSpace >= needAddPush;
                bool openStd = stdValid && tankSpace >= needAddStd;
                bool openEco = ecoValid && tankSpace >= needAddEco;

                if (openPush || openStd || openEco)
                {
                    IsPitWindowOpen = true;
                    pitWindowOpeningLap = currentLapNumber;

                    if (openPush)
                    {
                        pitWindowState = 3;
                        pitWindowLabel = "CLEAR PUSH";
                    }
                    else if (openStd)
                    {
                        pitWindowState = 2;
                        pitWindowLabel = "RACE PACE";
                    }
                    else
                    {
                        pitWindowState = 1;
                        pitWindowLabel = "FUEL SAVE";
                    }
                }
                else
                {
                    pitWindowState = 7;
                    pitWindowLabel = "TANK SPACE";
                    IsPitWindowOpen = false;

                    if (ecoValid && fuelPerLapForPitWindow > 0.0)
                    {
                        double fuelToBurnEco = Math.Max(0.0, needAddEco - tankSpace);
                        int lapsToOpen = (int)Math.Ceiling(fuelToBurnEco / fuelPerLapForPitWindow);
                        if (lapsToOpen < 0) lapsToOpen = 0;

                        pitWindowOpeningLap = currentLapNumber + lapsToOpen;
                    }
                    else
                    {
                        pitWindowOpeningLap = 0;
                    }
                }

                if (fuelPerLapForPitWindow > 0.0)
                {
                    double lapsRemainingInTankNow = currentFuel / fuelPerLapForPitWindow;
                    int closingLap = (int)Math.Floor(lapsRemainingInTankNow);
                    int latestLap = currentLapNumber + closingLap;
                    if (latestLap < currentLapNumber) latestLap = currentLapNumber;
                    pitWindowClosingLap = latestLap;
                }
                else
                {
                    pitWindowClosingLap = 0;
                }
            }

            PitWindowState = pitWindowState;
            PitWindowLabel = pitWindowLabel;
            PitWindowOpeningLap = pitWindowOpeningLap;
            PitWindowClosingLap = pitWindowClosingLap;

            if ((pitWindowState != _lastPitWindowState ||
                !string.Equals(pitWindowLabel, _lastPitWindowLabel, StringComparison.Ordinal)) &&
                (DateTime.UtcNow - _lastPitWindowLogUtc).TotalSeconds > 0.5)
            {
                SimHub.Logging.Current.Info(
                    $"[LalaPlugin:Pit Window] state={pitWindowState} label={pitWindowLabel} reqAdd={pitWindowRequestedAdd:F1} " +
                    $"tankSpace={tankSpace:F1} lap={currentLapNumber} confStable={LiveFuelPerLap_StableConfidence:F0}% confOverall={OverallConfidence:F0}% reqStops={strategyRequiredStops} closeLap={pitWindowClosingLap}");

                _lastPitWindowState = pitWindowState;
                _lastPitWindowLabel = pitWindowLabel;
                _lastPitWindowLogUtc = DateTime.UtcNow;
            }
            else if (pitWindowState != _lastPitWindowState ||
                !string.Equals(pitWindowLabel, _lastPitWindowLabel, StringComparison.Ordinal))
            {
                _lastPitWindowState = pitWindowState;
                _lastPitWindowLabel = pitWindowLabel;
                _lastPitWindowLogUtc = DateTime.UtcNow;
            }

            LiveLapsRemainingInRace_Stable = LiveLapsRemainingInRace;

            if (fuelPerLapForCalc > 0.0)
            {
                UpdatePredictorOutputs();
            }

            UpdateSmoothedFuelOutputs(requestedAddLitresForSmooth);

            if (lapCrossed && IsRaceSession(data.NewData?.SessionTypeName))
            {
                double observedAfterZero = (_timerZeroSeen && sessionTime > _timerZeroSessionTime)
                    ? Math.Max(0.0, sessionTime - _timerZeroSessionTime)
                    : 0.0;

                SimHub.Logging.Current.Info(
                    $"[LalaPlugin:Drive Time Projection] " +
                    $"tRemain={FormatSecondsOrNA(sessionTimeRemain)} " +
                    $"after0Used={_afterZeroUsedSeconds:F1}s src={AfterZeroSource} " +
                    $"lapsProj={_lastProjectedLapsRemaining:F2} simLaps={_lastSimLapsRemaining:F2} " +
                    $"lapRef={_lastProjectionLapSecondsUsed:F3}s lapRefSrc={ProjectionLapTime_StableSource} " +
                    $"after0Observed={observedAfterZero:F1}s");
            }

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
                    if (IsVerboseDebugLoggingOn)
                    {
                        SimHub.Logging.Current.Debug($"[LalaPlugin:Profiles] Refuel rate saved for '{ActiveProfile.ProfileName}': {rateLps:F3} L/s");
                    }
                }
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn($"[LalaPlugin:Profiles] Refuel rate save failed: {ex.Message}");
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
        private bool _hasValidClutchReleaseData = false;
        private bool _hasValidLaunchData = false;
        private bool _isAntiStallActive = false;
        private bool _isTimingZeroTo100 = false;
        private bool _launchSuccessful = false;
        private bool _eventMarkerPressed = false;
        private bool _msgCxPressed = false;
        private bool _pitScreenActive = false;
        private bool _pitScreenDismissed = false;
        private bool _pitScreenManualEnabled = false;
        private string _pitScreenMode = "auto";
        private bool _rpmInTargetRange = false;
        private bool _throttleInTargetRange = false;
        private bool _waitingForClutchRelease = false;
        private bool _wheelSpinDetected = false;
        private bool _zeroTo100CompletedThisRun = false;
        private bool _wasClutchDown = false;

        // --- Rejoin Assist Module State ---
        private readonly Stopwatch _offTrackHighSpeedTimer = new Stopwatch();
        private readonly Stopwatch _msgCxCooldownTimer = new Stopwatch();
        private readonly Stopwatch _eventMarkerCooldownTimer = new Stopwatch();

        // --- State: Timers ---
        private readonly Stopwatch _pittingTimer = new Stopwatch();
        private readonly Stopwatch _zeroTo100Stopwatch = new Stopwatch();
        private readonly Stopwatch _clutchTimer = new Stopwatch();
        private readonly Stopwatch _reactionTimer = new Stopwatch();
        private DateTime _launchEndTime = DateTime.MinValue;
        private bool _launchModeUserDisabled = false;
        private DateTime _manualPrimedStartedAt = DateTime.MinValue;
        private string _dashDesiredPage = "practice";
        private bool _dashPendingSwitch = false;
        private bool _dashExecutedForCurrentArm = false;
        private int _dashSwitchToken = 0;
        private string _dashLastSessionType = string.Empty;
        private bool _dashLastIgnitionOn = false;
        private bool _launchAbortLatched = false;
        private const int DashPulseMs = 500;
        private const int EventMarkerPulseMs = 5000;

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

        private void RegisterEventMarkerPress()
        {
            _eventMarkerPressed = true;
            _eventMarkerCooldownTimer.Restart();
        }

        private struct OffTrackDebugSnapshot
        {
            public int EventFired;
            public int SessionState;
            public int SessionFlagsRaw;
            public int ProbeCarIdx;
            public int TrackSurface;
            public int TrackSurfaceMaterial;
            public int CarSessionFlags;
            public bool? CarOnPitRoad;
            public int CarLap;
            public double CarLapDistPct;
            public bool? OffTrackNow;
            public bool? SurfaceOffTrackNow;
            public bool? DefinitiveOffTrackNow;
            public bool? BoundaryEvidenceNow;
            public int OffTrackStreak;
            public double OffTrackFirstSeenTimeSec;
            public bool? SuspectOffTrackNow;
            public int SuspectOffTrackStreak;
            public double SuspectOffTrackFirstSeenTimeSec;
            public bool? SuspectOffTrackActive;
            public int SuspectEventId;
            public double SuspectPulseUntilTimeSec;
            public bool? SuspectPulseActive;
            public int CompromisedUntilLap;
            public bool? CompromisedOffTrackActive;
            public bool? CompromisedPenaltyActive;
            public bool? AllowLatches;
            public int PlayerCarIdx;
            public int PlayerIncidentCount;
            public int PlayerIncidentDelta;
        }

        private static bool OffTrackDebugSnapshotEquals(
            OffTrackDebugSnapshot left,
            OffTrackDebugSnapshot right,
            bool ignoreContextFields)
        {
            return left.EventFired == right.EventFired
                && left.SessionState == right.SessionState
                && left.SessionFlagsRaw == right.SessionFlagsRaw
                && left.ProbeCarIdx == right.ProbeCarIdx
                && left.TrackSurface == right.TrackSurface
                && left.TrackSurfaceMaterial == right.TrackSurfaceMaterial
                && left.CarSessionFlags == right.CarSessionFlags
                && left.CarOnPitRoad == right.CarOnPitRoad
                && left.CarLap == right.CarLap
                && (ignoreContextFields || OffTrackDebugDoubleEquals(left.CarLapDistPct, right.CarLapDistPct))
                && left.OffTrackNow == right.OffTrackNow
                && left.SurfaceOffTrackNow == right.SurfaceOffTrackNow
                && left.DefinitiveOffTrackNow == right.DefinitiveOffTrackNow
                && left.BoundaryEvidenceNow == right.BoundaryEvidenceNow
                && (ignoreContextFields || left.OffTrackStreak == right.OffTrackStreak)
                && (ignoreContextFields || OffTrackDebugDoubleEquals(left.OffTrackFirstSeenTimeSec, right.OffTrackFirstSeenTimeSec))
                && left.SuspectOffTrackNow == right.SuspectOffTrackNow
                && (ignoreContextFields || left.SuspectOffTrackStreak == right.SuspectOffTrackStreak)
                && (ignoreContextFields || OffTrackDebugDoubleEquals(left.SuspectOffTrackFirstSeenTimeSec, right.SuspectOffTrackFirstSeenTimeSec))
                && left.SuspectOffTrackActive == right.SuspectOffTrackActive
                && left.SuspectEventId == right.SuspectEventId
                && (ignoreContextFields || OffTrackDebugDoubleEquals(left.SuspectPulseUntilTimeSec, right.SuspectPulseUntilTimeSec))
                && left.SuspectPulseActive == right.SuspectPulseActive
                && (ignoreContextFields || left.CompromisedUntilLap == right.CompromisedUntilLap)
                && left.CompromisedOffTrackActive == right.CompromisedOffTrackActive
                && left.CompromisedPenaltyActive == right.CompromisedPenaltyActive
                && left.AllowLatches == right.AllowLatches
                && left.PlayerCarIdx == right.PlayerCarIdx
                && left.PlayerIncidentCount == right.PlayerIncidentCount
                && left.PlayerIncidentDelta == right.PlayerIncidentDelta;
        }

        private static bool OffTrackDebugDoubleEquals(double left, double right)
        {
            if (double.IsNaN(left))
            {
                return double.IsNaN(right);
            }

            return left.Equals(right);
        }

        // Centralized state machine for launch phases
        private void SetLaunchState(LaunchState newState)
        {
            if (_currentLaunchState == newState) return;
            SimHub.Logging.Current.Info($"[LalaPlugin:Launch] State change: {_currentLaunchState} -> {newState}");

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
        private MessageEngine _msgV1Engine;
        private PitEngine _pit;
        private OpponentsEngine _opponentsEngine;
        private CarSAEngine _carSaEngine;
        private readonly RadioFrequencyNameCache _radioFrequencyNameCache = new RadioFrequencyNameCache();
        private int _lastTransmitCarIdx = -1;
        private int _lastTransmitRadioIdx = -1;
        private int _lastTransmitFrequencyIdx = -1;
        private string _radioTransmitShortName = string.Empty;
        private string _radioTransmitFullName = string.Empty;
        private bool _radioTransmitFrequencyMuted;
        private object _radioTransmitFrequencyEntry;
        private Func<object, bool> _radioTransmitFrequencyMutedAccessor;
        private int _localTxRadioIdx = -1;
        private int _localTxFrequencyNum = -1;
        private string _localTxFrequencyName = string.Empty;
        private bool _localTxFrequencyMuted;
        private object _localTxFrequencyEntry;
        private Func<object, bool> _localTxFrequencyMutedAccessor;
        private bool _radioIsPlayerTransmitting;
        private int _lastTransmitTalkingSlotKey = -1;
        private string _radioTransmitClassPosLabel = string.Empty;
        private int _lastTransmitClassPosCarIdx = -1;
        private int _lastTransmitClassPosition = 0;
        private string _lastTransmitClassShort = string.Empty;
        private StringBuilder _carSaDebugExportBuffer;
        private string _carSaDebugExportPath;
        private string _carSaDebugExportToken;
        private int _carSaDebugExportPendingLines;
        private StringBuilder _offTrackDebugExportBuffer;
        private string _offTrackDebugExportPath;
        private string _offTrackDebugExportToken;
        private int _offTrackDebugExportPendingLines;
        private double _offTrackDebugLastSessionTimeSec = double.NaN;
        private double _offTrackDebugEventWindowUntilSessionTimeSec = double.NaN;
        private OffTrackDebugSnapshot _offTrackDebugLastSnapshot;
        private bool _offTrackDebugSnapshotInitialized;
        private bool _offTrackDebugLastChangeOnlyEnabled;
        private bool _playerLapInvalid;
        private int _playerLapInvalidLap = int.MinValue;
        private int _playerLastIncidentCount = int.MinValue;
        private int _playerIncidentDelta = int.MinValue;
        private int _playerIncidentCount = -1;
        private double _playerLapInvalidLastSessionTimeSec = double.NaN;
        private const int CarSaDebugExportSlotCount = 5;
        private const int CarSaDebugCadenceTick = 0;
        private const int CarSaDebugCadenceMiniSector = 1;
        private const int CarSaDebugCadenceEventOnly = 2;
        private const int TransmitSlotKeyBehindOffset = 100;
        private int _carSaDebugCheckpointIndexNow = -1;
        private int _carSaDebugCheckpointIndexCrossed = -1;
        private int _carSaDebugMiniSectorTickId = 0;
        private double _carSaDebugLastWriteSessionTimeSec = double.NaN;
        private string _carSaEventsExportPath;
        private string _carSaEventsExportToken;
        private readonly StringBuilder _carSaEventsExportBuffer = new StringBuilder(1024);
        private int _carSaEventsExportPendingLines;
        private readonly int[] _carSaEventLastAheadCarIdx = new int[CarSaDebugExportSlotCount];
        private readonly int[] _carSaEventLastBehindCarIdx = new int[CarSaDebugExportSlotCount];
        private readonly int[] _carSaEventLastAheadStatusE = new int[CarSaDebugExportSlotCount];
        private readonly int[] _carSaEventLastBehindStatusE = new int[CarSaDebugExportSlotCount];
        private readonly int[] _carSaEventLastAheadHotCoolIntent = new int[CarSaDebugExportSlotCount];
        private readonly int[] _carSaEventLastBehindHotCoolIntent = new int[CarSaDebugExportSlotCount];
        private readonly bool[] _carSaEventLastAheadConflict = new bool[CarSaDebugExportSlotCount];
        private readonly bool[] _carSaEventLastBehindConflict = new bool[CarSaDebugExportSlotCount];
        private bool _carSaEventLastStateInitialized;
        private static readonly string[] CarSaDebugAheadDahlProperties =
        {
            "DahlDesign.CarAhead01Relative",
            "DahlDesign.CarAhead02Relative",
            "DahlDesign.CarAhead03Relative",
            "DahlDesign.CarAhead04Relative",
            "DahlDesign.CarAhead05Relative"
        };
        private static readonly string[] CarSaDebugBehindDahlProperties =
        {
            "DahlDesign.CarBehind01Relative",
            "DahlDesign.CarBehind02Relative",
            "DahlDesign.CarBehind03Relative",
            "DahlDesign.CarBehind04Relative",
            "DahlDesign.CarBehind05Relative"
        };
        private static readonly string[] CarSaDebugAheadIRacingProperties =
        {
            "IRacingExtraProperties.iRacing_DriverAhead_00_RelativeGapToPlayer",
            "IRacingExtraProperties.iRacing_DriverAhead_01_RelativeGapToPlayer",
            "IRacingExtraProperties.iRacing_DriverAhead_02_RelativeGapToPlayer",
            "IRacingExtraProperties.iRacing_DriverAhead_03_RelativeGapToPlayer",
            "IRacingExtraProperties.iRacing_DriverAhead_04_RelativeGapToPlayer"
        };
        private static readonly string[] CarSaDebugBehindIRacingProperties =
        {
            "IRacingExtraProperties.iRacing_DriverBehind_00_RelativeGapToPlayer",
            "IRacingExtraProperties.iRacing_DriverBehind_01_RelativeGapToPlayer",
            "IRacingExtraProperties.iRacing_DriverBehind_02_RelativeGapToPlayer",
            "IRacingExtraProperties.iRacing_DriverBehind_03_RelativeGapToPlayer",
            "IRacingExtraProperties.iRacing_DriverBehind_04_RelativeGapToPlayer"
        };
        private readonly double[] _carSaDebugAheadDahlRelativeGapSec = new double[CarSaDebugExportSlotCount];
        private readonly double[] _carSaDebugBehindDahlRelativeGapSec = new double[CarSaDebugExportSlotCount];
        private readonly double[] _carSaDebugAheadIRacingRelativeGapSec = new double[CarSaDebugExportSlotCount];
        private readonly double[] _carSaDebugBehindIRacingRelativeGapSec = new double[CarSaDebugExportSlotCount];
        private Dictionary<string, int> _carSaClassRankByColor;
        private string _carSaClassRankToken;
        private string _carSaClassRankSource;
        private readonly int[] _carSaLastAheadIdx = new int[CarSAEngine.SlotsAhead];
        private readonly int[] _carSaLastBehindIdx = new int[CarSAEngine.SlotsBehind];
        private readonly Dictionary<int, int> _carSaLastPaceFlags = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _carSaLastSessionFlags = new Dictionary<int, int>();
        private readonly Dictionary<int, DateTime> _carSaLastPaceFlagsLog = new Dictionary<int, DateTime>();
        private readonly Dictionary<int, DateTime> _carSaLastSessionFlagsLog = new Dictionary<int, DateTime>();
        private readonly int[] _carSaTrackedCarIdxs = new int[CarSAEngine.SlotsAhead + CarSAEngine.SlotsBehind + 1];
        private bool _carSaIdentityRefreshRequested;
        private double _carSaIdentityLastRetrySessionTimeSec = -1.0;
        private readonly double[] _carSaBestLapTimeSecByIdx = new double[CarSAEngine.MaxCars];
        private readonly double[] _carSaLastLapTimeSecByIdx = new double[CarSAEngine.MaxCars];
        private readonly double[] _carSaPrevBestLapTimeSecByIdx = new double[CarSAEngine.MaxCars];
        private readonly double[] _carSaPrevLastLapTimeSecByIdx = new double[CarSAEngine.MaxCars];
        private readonly int[] _carSaPrevLapCountByIdx = new int[CarSAEngine.MaxCars];
        private readonly string[] _carSaLapTimeUpdateByIdx = new string[CarSAEngine.MaxCars];
        private readonly double[] _carSaLapTimeUpdateExpireAtSecByIdx = new double[CarSAEngine.MaxCars];
        private readonly double[] _carSaEstTimeSecByIdx = new double[CarSAEngine.MaxCars];
        private readonly double[] _carSaCarClassEstLapTimeSecByIdx = new double[CarSAEngine.MaxCars];
        private readonly int[] _carSaClassPositionByIdx = new int[CarSAEngine.MaxCars];
        private readonly int[] _carSaIRatingByIdx = new int[CarSAEngine.MaxCars];
        private readonly HashSet<int> _friendUserIds = new HashSet<int>();
        private readonly HashSet<int> _teammateUserIds = new HashSet<int>();
        private readonly HashSet<int> _badUserIds = new HashSet<int>();
        private int _friendsCount;
        private readonly HashSet<LaunchPluginFriendEntry> _friendEntrySubscriptions = new HashSet<LaunchPluginFriendEntry>();
        private ObservableCollection<LaunchPluginFriendEntry> _friendsCollection;
        private bool _friendsDirty = true;
        private bool _carSaBestLapFallbackInfoLogged;
        private const double CarSaLapTimeUpdateVisibilitySeconds = 3.0;
        private const double CarSaLapTimeEpsilonSec = 0.001;

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
        private const double LiveMaxFuelJitterThreshold = 0.1;

        // --- Track marker trigger pulses (for messaging module) ---
        private DateTime _trackMarkerFirstCapturePulseUtc = DateTime.MinValue;
        private DateTime _trackMarkerTrackLengthChangedPulseUtc = DateTime.MinValue;
        private DateTime _trackMarkerLinesRefreshedPulseUtc = DateTime.MinValue;
        private const double TrackMarkerPulseHoldSeconds = 3.0;
        private readonly TrackMarkerPulse<TrackMarkerCapturedMessage> _trackMarkerCapturedPulse = new TrackMarkerPulse<TrackMarkerCapturedMessage>();
        private readonly TrackMarkerPulse<TrackMarkerLengthDeltaMessage> _trackMarkerLengthDeltaPulse = new TrackMarkerPulse<TrackMarkerLengthDeltaMessage>();
        private readonly TrackMarkerPulse<TrackMarkerLockedMismatchMessage> _trackMarkerLockedMismatchPulse = new TrackMarkerPulse<TrackMarkerLockedMismatchMessage>();
        private int _pitExitDistanceM = 0;
        private int _pitExitTimeS = 0;
        private const double PitExitSpeedEpsilonMps = 0.1;

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

        private const int ShiftAssistCooldownMsDefault = 500;
        private const int ShiftAssistResetHysteresisRpmDefault = 200;
        internal const int ShiftAssistBeepDurationMsDefault = 250;
        private const int ShiftAssistBeepDurationMsMin = 100;
        private const int ShiftAssistBeepDurationMsMax = 1000;
        private const int ShiftAssistBeepVolumePctMin = 0;
        private const int ShiftAssistBeepVolumePctMax = 100;
        internal const int ShiftAssistLeadTimeMsDefault = 200;
        private const int ShiftAssistLeadTimeMsMin = 0;
        private const int ShiftAssistLeadTimeMsMax = 500;
        internal const int ShiftAssistDebugCsvMaxHzDefault = 10;
        private const int ShiftAssistDebugCsvMaxHzMin = 1;
        private const int ShiftAssistDebugCsvMaxHzMax = 60;
        private const int ShiftAssistDelayHistorySize = 5;
        private const int ShiftAssistDelayPendingTimeoutMs = 2000;
        private const int ShiftAssistDelayDownshiftGraceMs = 150;
        private const int ShiftAssistGearZeroHoldMs = 250;
        private const int ShiftAssistLearnSavePulseMs = 250;
        private readonly ShiftAssistEngine _shiftAssistEngine = new ShiftAssistEngine();
        private readonly ShiftAssistLearningEngine _shiftAssistLearningEngine = new ShiftAssistLearningEngine();
        private ShiftAssistAudio _shiftAssistAudio;
        private string _shiftAssistActiveGearStackId = "Default";
        private int _shiftAssistTargetCurrentGear;
        private double _shiftAssistLearnPeakAccelLatched;
        private int _shiftAssistLearnPeakRpmLatched;
        private bool _shiftAssistLastEnabled;
        private DateTime _shiftAssistBeepUntilUtc = DateTime.MinValue;
        private bool _shiftAssistBeepLatched;
        private int _shiftAssistAudioDelayMs;
        private DateTime _shiftAssistAudioDelayLastIssuedUtc = DateTime.MinValue;
        private bool _shiftAssistAudioIssuedPulse;
        private int _shiftAssistLastGear;
        private int _shiftAssistLastValidGear;
        private DateTime _shiftAssistLastValidGearUtc = DateTime.MinValue;
        private double _shiftAssistLastSpeedMps = double.NaN;
        private DateTime _shiftAssistLastSpeedSampleUtc = DateTime.MinValue;
        private int _shiftAssistPendingDelayGear;
        private long _shiftAssistPendingDelayStartTs;
        private int _shiftAssistPendingDelayRpmAtCue;
        private bool _shiftAssistPendingDelayActive;
        private long _shiftAssistPendingDelayDownshiftSinceTs;
        private int _shiftAssistLastBeepRpmLatched;
        private int _shiftAssistLastCapturedDelayMs;
        private string _shiftAssistDelayCaptureEvent = "NONE";
        private int _shiftAssistDelayCaptureState;
        private string _shiftAssistDelayBeepType = "NONE";
        private int _shiftAssistDelayDiagLatchedCapturedMs;
        private string _shiftAssistDelayDiagLatchedEvent = "NONE";
        private string _shiftAssistDelayDiagLatchedBeepType = "NONE";
        private DateTime _shiftAssistDelayDiagLatchedUtc = DateTime.MinValue;
        private readonly int[,] _shiftAssistDelaySamples = new int[8, ShiftAssistDelayHistorySize];
        private readonly int[] _shiftAssistDelaySampleCounts = new int[8];
        private readonly int[] _shiftAssistDelaySampleNextIndex = new int[8];
        private readonly int[] _shiftAssistDelaySampleSums = new int[8];
        private double _shiftAssistDebugCsvLastWriteSessionSec = double.NaN;
        private DateTime _shiftAssistDebugCsvLastWriteUtc = DateTime.MinValue;
        private string _shiftAssistDebugCsvPath;
        private string _shiftAssistDebugCsvFileTimestamp;
        private bool _shiftAssistDebugCsvFailed;
        private DateTime _shiftAssistLearnSavedPulseUntilUtc = DateTime.MinValue;
        private ShiftAssistLearningTick _shiftAssistLastLearningTick = new ShiftAssistLearningTick { State = ShiftAssistLearningState.Off };

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

        private const string GlobalSettingsFileName = "GlobalSettings.json";
        private const string GlobalSettingsLegacyFileName = "LalaLaunch.GlobalSettings_V2.json";

        private void AttachCore(string name, Func<object> getter) => this.AttachDelegate(name, getter);
        private void AttachVerbose(string name, Func<object> getter)
        {
            if (SimhubPublish.VERBOSE) this.AttachDelegate(name, getter);
        }

        private bool HardDebugEnabled => HARD_DEBUG_ENABLED;
        private bool SoftDebugEnabled => HardDebugEnabled && (Settings?.EnableSoftDebug == true);
        private bool IsDebugOnForLogic => SoftDebugEnabled;
        private bool IsVerboseDebugLoggingOn => SoftDebugEnabled && (Settings?.EnableDebugLogging == true);

        internal bool IsVerboseDebugLoggingEnabledForExternal => IsVerboseDebugLoggingOn;

        public void Init(PluginManager pluginManager)
        {
            // --- INITIALIZATION ---
            this.PluginManager = pluginManager;
            PluginStorage.Initialize(pluginManager);
            Settings = LoadSettings();
            EnforceHardDebugSettings(Settings);
            HookFriendSettings(Settings);
            MarkFriendsDirty();
            _shiftAssistAudio = new ShiftAssistAudio(() => Settings);

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
                    SimHub.Logging.Current.Info("[LalaPlugin:Profiles] Applied profile to live and refreshed Fuel.");
                },
                () => this.CurrentCarModel,
                () => this.CurrentTrackKey,
                (trackKey) => GetTrackMarkersSnapshot(trackKey),
                (trackKey, locked) => SetTrackMarkersLockedForKey(trackKey, locked),
                () => ReloadTrackMarkersFromDisk(),
                (trackKey) => ResetTrackMarkersForKey(trackKey),
                () => _shiftAssistActiveGearStackId,
                () => Settings?.ShiftAssistEnabled == true,
                (enabled) =>
                {
                    Settings.ShiftAssistEnabled = enabled;
                    SaveSettings();
                },
                () => Settings?.ShiftAssistLearningModeEnabled == true,
                (enabled) =>
                {
                    Settings.ShiftAssistLearningModeEnabled = enabled;
                    SaveSettings();
                },
                () => GetShiftAssistBeepDurationMs(),
                (durationMs) =>
                {
                    if (Settings == null)
                    {
                        return;
                    }

                    Settings.ShiftAssistBeepDurationMs = durationMs;
                    SaveSettings();
                },
                () => GetShiftAssistLeadTimeMs(),
                (leadTimeMs) =>
                {
                    if (Settings == null)
                    {
                        return;
                    }

                    Settings.ShiftAssistLeadTimeMs = leadTimeMs;
                    SaveSettings();
                },
                () => Settings?.ShiftAssistUseCustomWav == true,
                (enabled) =>
                {
                    Settings.ShiftAssistUseCustomWav = enabled;
                    _shiftAssistAudio.ResetInvalidCustomWarning();
                    SaveSettings();
                },
                () => Settings?.ShiftAssistCustomWavPath ?? string.Empty,
                (path) =>
                {
                    Settings.ShiftAssistCustomWavPath = path ?? string.Empty;
                    _shiftAssistAudio.ResetInvalidCustomWarning();
                    SaveSettings();
                },
                () => IsShiftAssistBeepSoundEnabled(),
                (enabled) =>
                {
                    if (Settings == null)
                    {
                        return;
                    }

                    Settings.ShiftAssistBeepSoundEnabled = enabled;
                    SaveSettings();
                },
                () => GetShiftAssistBeepVolumePct(),
                (volumePct) =>
                {
                    if (Settings == null)
                    {
                        return;
                    }

                    if (volumePct < ShiftAssistBeepVolumePctMin) volumePct = ShiftAssistBeepVolumePctMin;
                    if (volumePct > ShiftAssistBeepVolumePctMax) volumePct = ShiftAssistBeepVolumePctMax;
                    Settings.ShiftAssistBeepVolumePct = volumePct;
                    SaveSettings();
                },
                () => TriggerShiftAssistTestBeep()
            );


            ProfilesViewModel.LoadProfiles();
            Screens.Mode = "manual";

            // --- Set the initial ActiveProfile on startup ---
            // It will be "Default Settings" or the first profile if that doesn't exist.
            ActiveProfile = ProfilesViewModel.GetProfileForCar("Default Settings") ?? ProfilesViewModel.CarProfiles.FirstOrDefault();

            // --- NEW: Instantiate the Fuel Calculator ---
            FuelCalculator = new FuelCalcs(this);

            SaveActiveProfileCommand = new RelayCommand(p => SaveActiveProfile());
            ReturnToDefaultsCommand = new RelayCommand(p => ReturnToDefaults());
            _telemetryTraceLogger = new TelemetryTraceLogger(this);
            _opponentsEngine = new OpponentsEngine();
            _carSaEngine = new CarSAEngine();
            ResetCarSaLapTimeUpdateState();
            ResetCarSaIdentityState();

            _poll250ms.Start();
            _poll500ms.Start();

            ResetLiveMaxFuelTracking();
            ResetAllValues();
            ResetFinishTimingState();
            _pit?.ResetPitPhaseState();

            SimHub.Logging.Current.Info($"[LalaPlugin:ShiftAssist] Enabled={Settings?.ShiftAssistEnabled == true}");

            // --- ACTIONS (exposed to Controls & Events) ---
            this.AddAction("MsgCx", (a, b) => MsgCx());
            this.AddAction("TogglePitScreen", (a, b) => TogglePitScreen());
            this.AddAction("PrimaryDashMode", (a, b) => PrimaryDashMode());
            this.AddAction("DeclutterMode", (a, b) => DeclutterMode0());
            this.AddAction("EventMarker", (a, b) => EventMarker());
            this.AddAction("LaunchMode", (a, b) => LaunchMode());
            this.AddAction("TrackMarkersLock", (a, b) => SetTrackMarkersLocked(true));
            this.AddAction("TrackMarkersUnlock", (a, b) => SetTrackMarkersLocked(false));
            this.AddAction("Debug_Hide_1_Toggle", (a, b) =>
            {
                if (Settings == null)
                {
                    return;
                }

                Settings.DebugHide1 = !Settings.DebugHide1;
                SaveSettings();
                SimHub.Logging.Current.Info($"[LalaPlugin:Debug] Debug_Hide_1_Toggle -> Hide={Settings.DebugHide1}");
            });
            this.AddAction("Debug_Hide_2_Toggle", (a, b) =>
            {
                if (Settings == null)
                {
                    return;
                }

                Settings.DebugHide2 = !Settings.DebugHide2;
                SaveSettings();
                SimHub.Logging.Current.Info($"[LalaPlugin:Debug] Debug_Hide_2_Toggle -> Hide={Settings.DebugHide2}");
            });
            this.AddAction("Debug_Hide_3_Toggle", (a, b) =>
            {
                if (Settings == null)
                {
                    return;
                }

                Settings.DebugHide3 = !Settings.DebugHide3;
                SaveSettings();
                SimHub.Logging.Current.Info($"[LalaPlugin:Debug] Debug_Hide_3_Toggle -> Hide={Settings.DebugHide3}");
            });
            this.AddAction("ShiftAssist_ResetDelayStats", (a, b) =>
            {
                ResetShiftAssistDelayStats();
                ProfilesViewModel?.RefreshShiftAssistRuntimeStats();
                SimHub.Logging.Current.Info("[LalaPlugin:ShiftAssist] Delay stats reset via action.");
            });
            this.AddAction("ShiftAssist_ToggleShiftAssist", (a, b) =>
            {
                if (Settings == null)
                {
                    return;
                }

                Settings.ShiftAssistEnabled = !Settings.ShiftAssistEnabled;
                SaveSettings();
                SimHub.Logging.Current.Info($"[LalaPlugin:ShiftAssist] Toggle action -> Enabled={Settings.ShiftAssistEnabled}");
            });
            this.AddAction("ShiftAssist_ToggleDebugCsv", (a, b) =>
            {
                if (Settings == null)
                {
                    return;
                }

                Settings.EnableShiftAssistDebugCsv = !Settings.EnableShiftAssistDebugCsv;
                if (!Settings.EnableShiftAssistDebugCsv)
                {
                    ResetShiftAssistDebugCsvState();
                }

                SaveSettings();
                ProfilesViewModel?.RefreshShiftAssistRuntimeStats();
                SimHub.Logging.Current.Info($"[LalaPlugin:ShiftAssist] Debug CSV toggle action -> Enabled={Settings.EnableShiftAssistDebugCsv}");
            });
            this.AddAction("ShiftAssist_TestBeep", (a, b) => TriggerShiftAssistTestBeep());
            this.AddAction("ShiftAssist_Learn_ResetSamples", (a, b) =>
            {
                _shiftAssistLearningEngine.ResetSamplesForStack(_shiftAssistActiveGearStackId);
                _shiftAssistLearnPeakAccelLatched = 0.0;
                _shiftAssistLearnPeakRpmLatched = 0;
                _shiftAssistLastLearningTick = new ShiftAssistLearningTick
                {
                    State = Settings?.ShiftAssistLearningModeEnabled == true ? ShiftAssistLearningState.Armed : ShiftAssistLearningState.Off
                };
                ProfilesViewModel?.RefreshShiftAssistRuntimeStats();
                SimHub.Logging.Current.Info($"[LalaPlugin:ShiftAssist] Learning samples reset for stack '{_shiftAssistActiveGearStackId}'.");
            });
            this.AddAction("ShiftAssist_ResetTargets_ActiveStack", (a, b) => ResetShiftAssistTargetsForActiveStack("ShiftAssist_ResetTargets_ActiveStack"));
            this.AddAction("ShiftAssist_ResetTargets_ActiveStack_AndSamples", (a, b) =>
            {
                ResetShiftAssistTargetsForActiveStack("ShiftAssist_ResetTargets_ActiveStack_AndSamples");
                _shiftAssistLearningEngine.ResetSamplesForStack(_shiftAssistActiveGearStackId);
                _shiftAssistLearnPeakAccelLatched = 0.0;
                _shiftAssistLearnPeakRpmLatched = 0;
                _shiftAssistLastLearningTick = new ShiftAssistLearningTick
                {
                    State = Settings?.ShiftAssistLearningModeEnabled == true ? ShiftAssistLearningState.Armed : ShiftAssistLearningState.Off
                };
                ProfilesViewModel?.RefreshShiftAssistRuntimeStats();
            });
            this.AddAction("ShiftAssist_Lock_G1", (a, b) => ExecuteShiftAssistLockAction(1, current => true, "ShiftAssist_Lock_G1"));
            this.AddAction("ShiftAssist_Lock_G2", (a, b) => ExecuteShiftAssistLockAction(2, current => true, "ShiftAssist_Lock_G2"));
            this.AddAction("ShiftAssist_Lock_G3", (a, b) => ExecuteShiftAssistLockAction(3, current => true, "ShiftAssist_Lock_G3"));
            this.AddAction("ShiftAssist_Lock_G4", (a, b) => ExecuteShiftAssistLockAction(4, current => true, "ShiftAssist_Lock_G4"));
            this.AddAction("ShiftAssist_Lock_G5", (a, b) => ExecuteShiftAssistLockAction(5, current => true, "ShiftAssist_Lock_G5"));
            this.AddAction("ShiftAssist_Lock_G6", (a, b) => ExecuteShiftAssistLockAction(6, current => true, "ShiftAssist_Lock_G6"));
            this.AddAction("ShiftAssist_Lock_G7", (a, b) => ExecuteShiftAssistLockAction(7, current => true, "ShiftAssist_Lock_G7"));
            this.AddAction("ShiftAssist_Lock_G8", (a, b) => ExecuteShiftAssistLockAction(8, current => true, "ShiftAssist_Lock_G8"));
            this.AddAction("ShiftAssist_Unlock_G1", (a, b) => ExecuteShiftAssistLockAction(1, current => false, "ShiftAssist_Unlock_G1"));
            this.AddAction("ShiftAssist_Unlock_G2", (a, b) => ExecuteShiftAssistLockAction(2, current => false, "ShiftAssist_Unlock_G2"));
            this.AddAction("ShiftAssist_Unlock_G3", (a, b) => ExecuteShiftAssistLockAction(3, current => false, "ShiftAssist_Unlock_G3"));
            this.AddAction("ShiftAssist_Unlock_G4", (a, b) => ExecuteShiftAssistLockAction(4, current => false, "ShiftAssist_Unlock_G4"));
            this.AddAction("ShiftAssist_Unlock_G5", (a, b) => ExecuteShiftAssistLockAction(5, current => false, "ShiftAssist_Unlock_G5"));
            this.AddAction("ShiftAssist_Unlock_G6", (a, b) => ExecuteShiftAssistLockAction(6, current => false, "ShiftAssist_Unlock_G6"));
            this.AddAction("ShiftAssist_Unlock_G7", (a, b) => ExecuteShiftAssistLockAction(7, current => false, "ShiftAssist_Unlock_G7"));
            this.AddAction("ShiftAssist_Unlock_G8", (a, b) => ExecuteShiftAssistLockAction(8, current => false, "ShiftAssist_Unlock_G8"));
            this.AddAction("ShiftAssist_ToggleLock_G1", (a, b) => ExecuteShiftAssistLockAction(1, current => !current, "ShiftAssist_ToggleLock_G1"));
            this.AddAction("ShiftAssist_ToggleLock_G2", (a, b) => ExecuteShiftAssistLockAction(2, current => !current, "ShiftAssist_ToggleLock_G2"));
            this.AddAction("ShiftAssist_ToggleLock_G3", (a, b) => ExecuteShiftAssistLockAction(3, current => !current, "ShiftAssist_ToggleLock_G3"));
            this.AddAction("ShiftAssist_ToggleLock_G4", (a, b) => ExecuteShiftAssistLockAction(4, current => !current, "ShiftAssist_ToggleLock_G4"));
            this.AddAction("ShiftAssist_ToggleLock_G5", (a, b) => ExecuteShiftAssistLockAction(5, current => !current, "ShiftAssist_ToggleLock_G5"));
            this.AddAction("ShiftAssist_ToggleLock_G6", (a, b) => ExecuteShiftAssistLockAction(6, current => !current, "ShiftAssist_ToggleLock_G6"));
            this.AddAction("ShiftAssist_ToggleLock_G7", (a, b) => ExecuteShiftAssistLockAction(7, current => !current, "ShiftAssist_ToggleLock_G7"));
            this.AddAction("ShiftAssist_ToggleLock_G8", (a, b) => ExecuteShiftAssistLockAction(8, current => !current, "ShiftAssist_ToggleLock_G8"));
            SimHub.Logging.Current.Info("[LalaPlugin:Init] Actions registered: MsgCx, TogglePitScreen, PrimaryDashMode, DeclutterMode, SecondaryDashMode (legacy), EventMarker, LaunchMode, TrackMarkersLock, TrackMarkersUnlock, Debug_Hide_1_Toggle, Debug_Hide_2_Toggle, Debug_Hide_3_Toggle, ShiftAssist_ResetDelayStats, ShiftAssist_ToggleShiftAssist, ShiftAssist_ToggleDebugCsv, ShiftAssist_TestBeep, ShiftAssist_Learn_ResetSamples, ShiftAssist_ResetTargets_ActiveStack, ShiftAssist_ResetTargets_ActiveStack_AndSamples, ShiftAssist_Lock_G1..G8, ShiftAssist_Unlock_G1..G8, ShiftAssist_ToggleLock_G1..G8");

            AttachCore("LalaLaunch.Friends.Count", () => _friendsCount);

            // --- DELEGATES FOR LIVE FUEL CALCULATOR (CORE) ---
            AttachCore("Fuel.LiveFuelPerLap", () => LiveFuelPerLap);
            AttachCore("Fuel.LiveFuelPerLap_Stable", () => LiveFuelPerLap_Stable);
            AttachCore("Fuel.LiveFuelPerLap_StableSource", () => LiveFuelPerLap_StableSource);
            AttachCore("Fuel.LiveFuelPerLap_StableConfidence", () => LiveFuelPerLap_StableConfidence);
            AttachCore("Surface.TrackWetness", () => TrackWetness);
            AttachCore("Surface.TrackWetnessLabel", () => TrackWetnessLabel);
            AttachCore("Fuel.FuelReadyConfidenceThreshold", () => GetFuelReadyConfidenceThreshold());
            AttachCore("Fuel.LiveLapsRemainingInRace", () => LiveLapsRemainingInRace);
            AttachCore("Fuel.LiveLapsRemainingInRace_S", () => LiveLapsRemainingInRace_S);
            AttachCore("Fuel.LiveLapsRemainingInRace_Stable", () => LiveLapsRemainingInRace_Stable);
            AttachCore("Fuel.LiveLapsRemainingInRace_Stable_S", () => LiveLapsRemainingInRace_Stable_S);
            AttachCore("Fuel.DeltaLaps", () => DeltaLaps);
            AttachCore("Fuel.TargetFuelPerLap", () => TargetFuelPerLap);
            AttachCore("Fuel.IsPitWindowOpen", () => IsPitWindowOpen);
            AttachCore("Fuel.PitWindowOpeningLap", () => PitWindowOpeningLap);
            AttachCore("Fuel.PitWindowClosingLap", () => PitWindowClosingLap);
            AttachCore("Fuel.PitWindowState", () => PitWindowState);
            AttachCore("Fuel.PitWindowLabel", () => PitWindowLabel);
            AttachCore("Fuel.LapsRemainingInTank", () => LapsRemainingInTank);
            AttachCore("Fuel.Confidence", () => Confidence);
            AttachCore("Fuel.PushFuelPerLap", () => PushFuelPerLap);
            AttachCore("Fuel.FuelSavePerLap", () => FuelSaveFuelPerLap);
            AttachCore("Fuel.StintBurnTarget", () => StintBurnTarget);
            AttachCore("Fuel.StintBurnTargetBand", () => StintBurnTargetBand);
            AttachCore("Fuel.FuelBurnPredictor", () => FuelBurnPredictor);
            AttachCore("Fuel.FuelBurnPredictorSource", () => FuelBurnPredictorSource);
            AttachCore("Fuel.DeltaLapsIfPush", () => DeltaLapsIfPush);
            AttachCore("Fuel.CanAffordToPush", () => CanAffordToPush);
            AttachCore("Fuel.Delta.LitresCurrent", () => Math.Round(Fuel_Delta_LitresCurrent, 1));
            AttachCore("Fuel.Delta.LitresPlan", () => Math.Round(Fuel_Delta_LitresPlan, 1));
            AttachCore("Fuel.Delta.LitresWillAdd", () => Math.Round(Fuel_Delta_LitresWillAdd, 1));
            AttachCore("Fuel.Delta.LitresCurrentPush", () => Math.Round(Fuel_Delta_LitresCurrentPush, 1));
            AttachCore("Fuel.Delta.LitresPlanPush", () => Math.Round(Fuel_Delta_LitresPlanPush, 1));
            AttachCore("Fuel.Delta.LitresWillAddPush", () => Math.Round(Fuel_Delta_LitresWillAddPush, 1));
            AttachCore("Fuel.Delta.LitresCurrentSave", () => Math.Round(Fuel_Delta_LitresCurrentSave, 1));
            AttachCore("Fuel.Delta.LitresPlanSave", () => Math.Round(Fuel_Delta_LitresPlanSave, 1));
            AttachCore("Fuel.Delta.LitresWillAddSave", () => Math.Round(Fuel_Delta_LitresWillAddSave, 1));
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
            AttachCore("Fuel.PitStopsRequiredByFuel", () => PitStopsRequiredByFuel);
            AttachCore("Fuel.PitStopsRequiredByPlan", () => PitStopsRequiredByPlan);
            AttachCore("Fuel.Pit.StopsRequiredToEnd", () => Pit_StopsRequiredToEnd);
            AttachCore("Fuel.Live.RefuelRate_Lps", () => FuelCalculator?.EffectiveRefuelRateLps ?? 0.0);
            AttachCore("Fuel.Live.TireChangeTime_S", () => GetEffectiveTireChangeTimeSeconds());
            AttachCore("Fuel.Live.PitLaneLoss_S", () => FuelCalculator?.PitLaneTimeLoss ?? 0.0);
            AttachCore("Fuel.Live.TotalStopLoss", () => CalculateTotalStopLossSeconds());
            AttachCore("Fuel.Live.DriveTimeAfterZero", () => LiveProjectedDriveTimeAfterZero);
            AttachCore("Fuel.After0.PlannerSeconds", () => AfterZeroPlannerSeconds);
            AttachCore("Fuel.After0.LiveEstimateSeconds", () => AfterZeroLiveEstimateSeconds);
            AttachCore("Fuel.After0.Source", () => AfterZeroSource);
            AttachCore("Fuel.ProjectionLapTime_Stable", () => ProjectionLapTime_Stable);
            AttachCore("Fuel.ProjectionLapTime_StableSource", () => ProjectionLapTime_StableSource);
            AttachCore("Fuel.Live.ProjectedDriveSecondsRemaining", () => LiveProjectedDriveSecondsRemaining);
            AttachCore("Fuel.Live.IsFuelReady", () => IsFuelReady);

            // --- Pace metrics (CORE) ---
            AttachCore("Pace.StintAvgLapTimeSec", () => Pace_StintAvgLapTimeSec);
            AttachCore("Pace.Last5LapAvgSec", () => Pace_Last5LapAvgSec);
            AttachCore("Pace.LeaderAvgLapTimeSec", () => LiveLeaderAvgPaceSeconds);
            AttachCore("Pace.LeaderDeltaToPlayerSec", () => Pace_LeaderDeltaToPlayerSec);
            AttachCore("Pace.PaceConfidence", () => PaceConfidence);
            AttachCore("Pace.OverallConfidence", () => OverallConfidence);
            AttachCore("Pace.PacePredictor", () => PacePredictor);
            AttachCore("Pace.PacePredictorSource", () => PacePredictorSource);
            AttachCore("Reset.LastSession", () => _lastSessionToken);
            AttachCore("Reset.ThisSession", () => _currentSessionToken);
            AttachCore("Reset.ThisSessionType", () => _finishTimingSessionType);
            AttachCore("car.player.LapInvalid", () => _playerLapInvalid);

            // --- Pit time-loss (finals kept CORE; raw & debug VERBOSE) ---
            AttachCore("Pit.LastDirectTravelTime", () => _pit.LastDirectTravelTime);
            AttachCore("Pit.LastTotalPitCycleTimeLoss", () => _pit.LastTotalPitCycleTimeLoss);
            AttachCore("Pit.LastPaceDeltaNetLoss", () => _pit.LastPaceDeltaNetLoss);
            AttachVerbose("Pit.Debug.TimeOnPitRoad", () => _pit.TimeOnPitRoad.TotalSeconds);

            // --- Pit Entry Assist (CORE + optional driver/debug) ---
            AttachCore("Pit.EntryAssistActive", () => _pit.PitEntryAssistActive);
            AttachCore("Pit.EntryDistanceToLine_m", () => _pit.PitEntryDistanceToLine_m);
            AttachCore("Pit.EntryRequiredDistance_m", () => _pit.PitEntryRequiredDistance_m);
            AttachCore("Pit.EntryMargin_m", () => _pit.PitEntryMargin_m);
            AttachCore("Pit.EntryCue", () => _pit.PitEntryCue);
            AttachCore("Pit.EntryCueText", () => _pit.PitEntryCueText);
            AttachCore("Pit.EntrySpeedDelta_kph", () => _pit.PitEntrySpeedDelta_kph);
            AttachCore("Pit.EntryDecelProfile_mps2", () => _pit.PitEntryDecelProfile_mps2);
            AttachCore("Pit.EntryBuffer_m", () => _pit.PitEntryBuffer_m);


            // AttachVerbose("Pit.Debug.LastTimeOnPitRoad",  () => _pit.TimeOnPitRoad.TotalSeconds);
            AttachVerbose("Pit.Debug.LastPitStopDuration", () => _pit?.PitStopElapsedSec ?? 0.0);

            // --- PIT TEST / RAW (all VERBOSE) ---
            AttachCore("Lala.Pit.AvgPaceUsedSec", () => _pitDbg_AvgPaceUsedSec);
            AttachCore("Lala.Pit.AvgPaceSource", () => _pitDbg_AvgPaceSource);
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
            AttachCore("PitLite.LastSaved.Sec", () => _pitDbg_CandidateSavedSec);
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
            AttachCore("DeclutterMode", () => DeclutterMode);
            AttachCore("Race.OverallLeaderHasFinished", () => OverallLeaderHasFinished);
            AttachCore("Race.OverallLeaderHasFinishedValid", () => OverallLeaderHasFinishedValid);
            AttachCore("Race.ClassLeaderHasFinished", () => ClassLeaderHasFinished);
            AttachCore("Race.ClassLeaderHasFinishedValid", () => ClassLeaderHasFinishedValid);
            AttachCore("Race.LeaderHasFinished", () => LeaderHasFinished);
            AttachCore("MsgCxPressed", () => _msgCxPressed);
            AttachCore("Debug.EventMarkerPressed", () => _eventMarkerPressed);
            AttachCore("Debug.Hide_1", () => Settings?.DebugHide1 == true ? 1 : 0);
            AttachCore("Debug.Hide_2", () => Settings?.DebugHide2 == true ? 1 : 0);
            AttachCore("Debug.Hide_3", () => Settings?.DebugHide3 == true ? 1 : 0);
            AttachCore("PitScreenActive", () => _pitScreenActive);
            AttachCore("PitScreenMode", () => _pitScreenMode);
            AttachCore("Pit.EntryLineDebrief", () => _pit.PitEntryLineDebrief);
            AttachCore("Pit.EntryLineDebriefText", () => _pit.PitEntryLineDebriefText);
            AttachCore("Pit.EntryLineTimeLoss_s", () => _pit.PitEntryLineTimeLoss_s);

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

            // --- Overlay Options (CORE) ---
            AttachCore("OverlayDashShowLaunchScreen", () => Settings.OverlayDashShowLaunchScreen);
            AttachCore("OverlayDashShowPitLimiter", () => Settings.OverlayDashShowPitLimiter);
            AttachCore("OverlayDashShowPitScreen", () => Settings.OverlayDashShowPitScreen);
            AttachCore("OverlayDashShowRejoinAssist", () => Settings.OverlayDashShowRejoinAssist);
            AttachCore("OverlayDashShowVerboseMessaging", () => Settings.OverlayDashShowVerboseMessaging);
            AttachCore("OverlayDashShowRaceFlags", () => Settings.OverlayDashShowRaceFlags);
            AttachCore("OverlayDashShowRadioMessages", () => Settings.OverlayDashShowRadioMessages);
            AttachCore("OverlayDashShowTraffic", () => Settings.OverlayDashShowTraffic);

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

            AttachCore("ShiftAssist.ActiveGearStackId", () => _shiftAssistActiveGearStackId ?? "Default");
            AttachCore("ShiftAssist.TargetRPM_CurrentGear", () => _shiftAssistTargetCurrentGear);
            AttachCore("ShiftAssist.ShiftRPM_G1", () => GetShiftAssistTargetRpmForGear(1));
            AttachCore("ShiftAssist.ShiftRPM_G2", () => GetShiftAssistTargetRpmForGear(2));
            AttachCore("ShiftAssist.ShiftRPM_G3", () => GetShiftAssistTargetRpmForGear(3));
            AttachCore("ShiftAssist.ShiftRPM_G4", () => GetShiftAssistTargetRpmForGear(4));
            AttachCore("ShiftAssist.ShiftRPM_G5", () => GetShiftAssistTargetRpmForGear(5));
            AttachCore("ShiftAssist.ShiftRPM_G6", () => GetShiftAssistTargetRpmForGear(6));
            AttachCore("ShiftAssist.ShiftRPM_G7", () => GetShiftAssistTargetRpmForGear(7));
            AttachCore("ShiftAssist.ShiftRPM_G8", () => GetShiftAssistTargetRpmForGear(8));
            AttachCore("ShiftAssist.EffectiveTargetRPM_CurrentGear", () => _shiftAssistEngine.LastEffectiveTargetRpm);
            AttachCore("ShiftAssist.RpmRate", () => _shiftAssistEngine.LastRpmRate);
            AttachCore("ShiftAssist.Beep", () => _shiftAssistBeepLatched);
            AttachCore("ShiftAssist.Learn.Enabled", () => Settings?.ShiftAssistLearningModeEnabled == true ? 1 : 0);
            AttachCore("ShiftAssist.Learn.State", () => ToLearningStateText(_shiftAssistLastLearningTick?.State ?? ShiftAssistLearningState.Off));
            AttachCore("ShiftAssist.Learn.ActiveGear", () => _shiftAssistLastLearningTick?.ActiveGear ?? 0);
            AttachCore("ShiftAssist.Learn.WindowMs", () => _shiftAssistLastLearningTick?.WindowMs ?? 0);
            AttachCore("ShiftAssist.Learn.PeakAccelMps2", () => _shiftAssistLearnPeakAccelLatched);
            AttachCore("ShiftAssist.Learn.PeakRpm", () => _shiftAssistLearnPeakRpmLatched);
            AttachCore("ShiftAssist.Learn.LastSampleRpm", () => _shiftAssistLastLearningTick?.LastSampleRpm ?? 0);
            AttachCore("ShiftAssist.Learn.SavedPulse", () => IsShiftAssistLearnSavedPulseActive());
            AttachCore("ShiftAssist.Learn.Samples_G1", () => GetShiftAssistLearnSamplesForGear(1));
            AttachCore("ShiftAssist.Learn.Samples_G2", () => GetShiftAssistLearnSamplesForGear(2));
            AttachCore("ShiftAssist.Learn.Samples_G3", () => GetShiftAssistLearnSamplesForGear(3));
            AttachCore("ShiftAssist.Learn.Samples_G4", () => GetShiftAssistLearnSamplesForGear(4));
            AttachCore("ShiftAssist.Learn.Samples_G5", () => GetShiftAssistLearnSamplesForGear(5));
            AttachCore("ShiftAssist.Learn.Samples_G6", () => GetShiftAssistLearnSamplesForGear(6));
            AttachCore("ShiftAssist.Learn.Samples_G7", () => GetShiftAssistLearnSamplesForGear(7));
            AttachCore("ShiftAssist.Learn.Samples_G8", () => GetShiftAssistLearnSamplesForGear(8));
            AttachCore("ShiftAssist.Learn.LearnedRpm_G1", () => GetShiftAssistLearnedRpmForGear(1));
            AttachCore("ShiftAssist.Learn.LearnedRpm_G2", () => GetShiftAssistLearnedRpmForGear(2));
            AttachCore("ShiftAssist.Learn.LearnedRpm_G3", () => GetShiftAssistLearnedRpmForGear(3));
            AttachCore("ShiftAssist.Learn.LearnedRpm_G4", () => GetShiftAssistLearnedRpmForGear(4));
            AttachCore("ShiftAssist.Learn.LearnedRpm_G5", () => GetShiftAssistLearnedRpmForGear(5));
            AttachCore("ShiftAssist.Learn.LearnedRpm_G6", () => GetShiftAssistLearnedRpmForGear(6));
            AttachCore("ShiftAssist.Learn.LearnedRpm_G7", () => GetShiftAssistLearnedRpmForGear(7));
            AttachCore("ShiftAssist.Learn.LearnedRpm_G8", () => GetShiftAssistLearnedRpmForGear(8));
            AttachCore("ShiftAssist.Learn.Locked_G1", () => GetShiftAssistLockedForGear(1));
            AttachCore("ShiftAssist.Learn.Locked_G2", () => GetShiftAssistLockedForGear(2));
            AttachCore("ShiftAssist.Learn.Locked_G3", () => GetShiftAssistLockedForGear(3));
            AttachCore("ShiftAssist.Learn.Locked_G4", () => GetShiftAssistLockedForGear(4));
            AttachCore("ShiftAssist.Learn.Locked_G5", () => GetShiftAssistLockedForGear(5));
            AttachCore("ShiftAssist.Learn.Locked_G6", () => GetShiftAssistLockedForGear(6));
            AttachCore("ShiftAssist.Learn.Locked_G7", () => GetShiftAssistLockedForGear(7));
            AttachCore("ShiftAssist.Learn.Locked_G8", () => GetShiftAssistLockedForGear(8));
            AttachCore("ShiftAssist.State", () => _shiftAssistEngine.LastState.ToString());
            AttachCore("ShiftAssist.Debug.AudioDelayMs", () => _shiftAssistAudioDelayMs);
            AttachCore("ShiftAssist.Debug.AudioDelayAgeMs", () => GetShiftAssistAudioDelayAgeMs());
            AttachCore("ShiftAssist.Debug.AudioIssued", () => _shiftAssistAudioIssuedPulse);
            AttachCore("ShiftAssist.Debug.AudioBackend", () => "SoundPlayer");
            AttachCore("ShiftAssist.Debug.CsvEnabled", () => Settings?.EnableShiftAssistDebugCsv == true ? 1 : 0);
            AttachCore("ShiftAssist.DelayAvg_G1", () => GetShiftAssistDelayAverageMs(1));
            AttachCore("ShiftAssist.DelayAvg_G2", () => GetShiftAssistDelayAverageMs(2));
            AttachCore("ShiftAssist.DelayAvg_G3", () => GetShiftAssistDelayAverageMs(3));
            AttachCore("ShiftAssist.DelayAvg_G4", () => GetShiftAssistDelayAverageMs(4));
            AttachCore("ShiftAssist.DelayAvg_G5", () => GetShiftAssistDelayAverageMs(5));
            AttachCore("ShiftAssist.DelayAvg_G6", () => GetShiftAssistDelayAverageMs(6));
            AttachCore("ShiftAssist.DelayAvg_G7", () => GetShiftAssistDelayAverageMs(7));
            AttachCore("ShiftAssist.DelayAvg_G8", () => GetShiftAssistDelayAverageMs(8));
            AttachCore("ShiftAssist.DelayN_G1", () => GetShiftAssistDelayCount(1));
            AttachCore("ShiftAssist.DelayN_G2", () => GetShiftAssistDelayCount(2));
            AttachCore("ShiftAssist.DelayN_G3", () => GetShiftAssistDelayCount(3));
            AttachCore("ShiftAssist.DelayN_G4", () => GetShiftAssistDelayCount(4));
            AttachCore("ShiftAssist.DelayN_G5", () => GetShiftAssistDelayCount(5));
            AttachCore("ShiftAssist.DelayN_G6", () => GetShiftAssistDelayCount(6));
            AttachCore("ShiftAssist.DelayN_G7", () => GetShiftAssistDelayCount(7));
            AttachCore("ShiftAssist.DelayN_G8", () => GetShiftAssistDelayCount(8));
            AttachCore("ShiftAssist.Delay.Pending", () => _shiftAssistPendingDelayActive ? 1 : 0);
            AttachCore("ShiftAssist.Delay.PendingGear", () => _shiftAssistPendingDelayGear);
            AttachCore("ShiftAssist.Delay.PendingAgeMs", () => GetShiftAssistPendingAgeMs(Stopwatch.GetTimestamp()));
            AttachCore("ShiftAssist.Delay.PendingRpmAtCue", () => _shiftAssistPendingDelayRpmAtCue);
            AttachCore("ShiftAssist.Delay.RpmAtBeep", () => _shiftAssistLastBeepRpmLatched);
            AttachCore("ShiftAssist.Delay.CaptureState", () => _shiftAssistDelayCaptureState);

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
            AttachCore("MSG.OtherClassBehindGap", () => _msgSystem.OtherClassBehindGap);
            AttachCore("MSG.OvertakeWarnSeconds", () => ActiveProfile.TrafficApproachWarnSeconds);
            AttachCore("MSG.MsgCxTimeMessage", () => _msgSystem.MsgCxTimeMessage);
            AttachCore("MSG.MsgCxTimeVisible", () => _msgSystem.IsMsgCxTimeActive);
            AttachCore("MSG.MsgCxTimeSilenceRemaining", () => _msgSystem.MsgCxTimeSilenceRemainingSeconds);
            AttachCore("MSG.MsgCxStateMessage", () => _msgSystem.MsgCxStateMessage);
            AttachCore("MSG.MsgCxStateVisible", () => _msgSystem.IsMsgCxStateActive);
            AttachCore("MSG.MsgCxStateToken", () => _msgSystem.MsgCxStateToken);
            AttachCore("MSG.MsgCxActionMessage", () => _msgSystem.MsgCxActionMessage);
            AttachCore("MSG.MsgCxActionPulse", () => _msgSystem.MsgCxActionPulse);

            _msgV1Engine = new MessageEngine(pluginManager, this);
            AttachCore("MSGV1.ActiveText_Lala", () => _msgV1Engine?.Outputs.ActiveTextLala ?? string.Empty);
            AttachCore("MSGV1.ActivePriority_Lala", () => _msgV1Engine?.Outputs.ActivePriorityLala ?? string.Empty);
            AttachCore("MSGV1.ActiveMsgId_Lala", () => _msgV1Engine?.Outputs.ActiveMsgIdLala ?? string.Empty);
            AttachCore("MSGV1.ActiveText_Msg", () => _msgV1Engine?.Outputs.ActiveTextMsg ?? string.Empty);
            AttachCore("MSGV1.ActivePriority_Msg", () => _msgV1Engine?.Outputs.ActivePriorityMsg ?? string.Empty);
            AttachCore("MSGV1.ActiveMsgId_Msg", () => _msgV1Engine?.Outputs.ActiveMsgIdMsg ?? string.Empty);
            AttachCore("MSGV1.ActiveCount", () => _msgV1Engine?.Outputs.ActiveCount ?? 0);
            AttachCore("MSGV1.LastCancelMsgId", () => _msgV1Engine?.Outputs.LastCancelMsgId ?? string.Empty);
            AttachCore("MSGV1.ClearAllPulse", () => _msgV1Engine?.Outputs.ClearAllPulse ?? false);
            AttachCore("MSGV1.StackCsv", () => _msgV1Engine?.Outputs.StackCsv ?? string.Empty);
            AttachCore("MSGV1.ActiveTextColor_Lala", () => _msgV1Engine?.Outputs.ActiveTextColorLala ?? string.Empty);
            AttachCore("MSGV1.ActiveBgColor_Lala", () => _msgV1Engine?.Outputs.ActiveBgColorLala ?? string.Empty);
            AttachCore("MSGV1.ActiveOutlineColor_Lala", () => _msgV1Engine?.Outputs.ActiveOutlineColorLala ?? string.Empty);
            AttachCore("MSGV1.ActiveFontSize_Lala", () => _msgV1Engine?.Outputs.ActiveFontSizeLala ?? 24);
            AttachCore("MSGV1.ActiveTextColor_Msg", () => _msgV1Engine?.Outputs.ActiveTextColorMsg ?? string.Empty);
            AttachCore("MSGV1.ActiveBgColor_Msg", () => _msgV1Engine?.Outputs.ActiveBgColorMsg ?? string.Empty);
            AttachCore("MSGV1.ActiveOutlineColor_Msg", () => _msgV1Engine?.Outputs.ActiveOutlineColorMsg ?? string.Empty);
            AttachCore("MSGV1.ActiveFontSize_Msg", () => _msgV1Engine?.Outputs.ActiveFontSizeMsg ?? 24);
            AttachCore("MSGV1.MissingEvaluatorsCsv", () => _msgV1Engine?.Outputs.MissingEvaluatorsCsv ?? string.Empty);

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
            AttachCore("TrackMarkers.TrackKey", () => _pit?.TrackMarkersTrackKey ?? GetCanonicalTrackKeyForMarkers());
            AttachCore("TrackMarkers.Stored.EntryPct", () => _pit?.TrackMarkersStoredEntryPct ?? double.NaN);
            AttachCore("TrackMarkers.Stored.ExitPct", () => _pit?.TrackMarkersStoredExitPct ?? double.NaN);
            AttachCore("TrackMarkers.Stored.Locked", () => _pit?.TrackMarkersStoredLocked ?? true);
            AttachCore("TrackMarkers.Stored.LastUpdatedUtc", () => _pit?.TrackMarkersStoredLastUpdatedUtc ?? string.Empty);
            AttachCore("TrackMarkers.Session.TrackLengthM", () => _pit?.TrackMarkersSessionTrackLengthM ?? double.NaN);
            AttachCore("TrackMarkers.Session.TrackLengthChanged", () => _pit?.TrackMarkersSessionTrackLengthChanged ?? false);
            AttachCore("TrackMarkers.Session.NeedsEntryRefresh", () => _pit?.TrackMarkersSessionNeedsEntryRefresh ?? false);
            AttachCore("TrackMarkers.Session.NeedsExitRefresh", () => _pit?.TrackMarkersSessionNeedsExitRefresh ?? false);
            AttachCore("PitExit.DistanceM", () => _pitExitDistanceM);
            AttachCore("PitExit.TimeS", () => _pitExitTimeS);
            AttachCore("TrackMarkers.Trigger.FirstCapture", () => IsTrackMarkerPulseActive(_trackMarkerFirstCapturePulseUtc));
            AttachCore("TrackMarkers.Trigger.TrackLengthChanged", () => IsTrackMarkerPulseActive(_trackMarkerTrackLengthChangedPulseUtc));
            AttachCore("TrackMarkers.Trigger.LinesRefreshed", () => IsTrackMarkerPulseActive(_trackMarkerLinesRefreshedPulseUtc));
            AttachCore("MSG.Trigger.trackmarkers.first_capture", () => IsTrackMarkerPulseActive(_trackMarkerFirstCapturePulseUtc));
            AttachCore("MSG.Trigger.trackmarkers.track_length_changed", () => IsTrackMarkerPulseActive(_trackMarkerTrackLengthChangedPulseUtc));
            AttachCore("MSG.Trigger.trackmarkers.lines_refreshed", () => IsTrackMarkerPulseActive(_trackMarkerLinesRefreshedPulseUtc));

            double SafeOppValue(double v) => (double.IsNaN(v) || double.IsInfinity(v)) ? 0.0 : v;

            AttachCore("Opp.Ahead1.Name", () => _opponentsEngine?.Outputs.Ahead1.Name ?? string.Empty);
            AttachCore("Opp.Ahead1.CarNumber", () => _opponentsEngine?.Outputs.Ahead1.CarNumber ?? string.Empty);
            AttachCore("Opp.Ahead1.ClassColor", () => _opponentsEngine?.Outputs.Ahead1.ClassColor ?? string.Empty);
            AttachCore("Opp.Ahead1.GapToPlayerSec", () => SafeOppValue(_opponentsEngine?.Outputs.Ahead1.GapToPlayerSec ?? 0.0));
            AttachCore("Opp.Ahead1.BlendedPaceSec", () => SafeOppValue(_opponentsEngine?.Outputs.Ahead1.BlendedPaceSec ?? 0.0));
            AttachCore("Opp.Ahead1.PaceDeltaSecPerLap", () => SafeOppValue(_opponentsEngine?.Outputs.Ahead1.PaceDeltaSecPerLap ?? double.NaN));
            AttachCore("Opp.Ahead1.LapsToFight", () => SafeOppValue(_opponentsEngine?.Outputs.Ahead1.LapsToFight ?? double.NaN));

            AttachCore("Opp.Ahead2.Name", () => _opponentsEngine?.Outputs.Ahead2.Name ?? string.Empty);
            AttachCore("Opp.Ahead2.CarNumber", () => _opponentsEngine?.Outputs.Ahead2.CarNumber ?? string.Empty);
            AttachCore("Opp.Ahead2.ClassColor", () => _opponentsEngine?.Outputs.Ahead2.ClassColor ?? string.Empty);
            AttachCore("Opp.Ahead2.GapToPlayerSec", () => SafeOppValue(_opponentsEngine?.Outputs.Ahead2.GapToPlayerSec ?? 0.0));
            AttachCore("Opp.Ahead2.BlendedPaceSec", () => SafeOppValue(_opponentsEngine?.Outputs.Ahead2.BlendedPaceSec ?? 0.0));
            AttachCore("Opp.Ahead2.PaceDeltaSecPerLap", () => SafeOppValue(_opponentsEngine?.Outputs.Ahead2.PaceDeltaSecPerLap ?? double.NaN));
            AttachCore("Opp.Ahead2.LapsToFight", () => SafeOppValue(_opponentsEngine?.Outputs.Ahead2.LapsToFight ?? double.NaN));

            AttachCore("Opp.Behind1.Name", () => _opponentsEngine?.Outputs.Behind1.Name ?? string.Empty);
            AttachCore("Opp.Behind1.CarNumber", () => _opponentsEngine?.Outputs.Behind1.CarNumber ?? string.Empty);
            AttachCore("Opp.Behind1.ClassColor", () => _opponentsEngine?.Outputs.Behind1.ClassColor ?? string.Empty);
            AttachCore("Opp.Behind1.GapToPlayerSec", () => SafeOppValue(_opponentsEngine?.Outputs.Behind1.GapToPlayerSec ?? 0.0));
            AttachCore("Opp.Behind1.BlendedPaceSec", () => SafeOppValue(_opponentsEngine?.Outputs.Behind1.BlendedPaceSec ?? 0.0));
            AttachCore("Opp.Behind1.PaceDeltaSecPerLap", () => SafeOppValue(_opponentsEngine?.Outputs.Behind1.PaceDeltaSecPerLap ?? double.NaN));
            AttachCore("Opp.Behind1.LapsToFight", () => SafeOppValue(_opponentsEngine?.Outputs.Behind1.LapsToFight ?? double.NaN));

            AttachCore("Opp.Behind2.Name", () => _opponentsEngine?.Outputs.Behind2.Name ?? string.Empty);
            AttachCore("Opp.Behind2.CarNumber", () => _opponentsEngine?.Outputs.Behind2.CarNumber ?? string.Empty);
            AttachCore("Opp.Behind2.ClassColor", () => _opponentsEngine?.Outputs.Behind2.ClassColor ?? string.Empty);
            AttachCore("Opp.Behind2.GapToPlayerSec", () => SafeOppValue(_opponentsEngine?.Outputs.Behind2.GapToPlayerSec ?? 0.0));
            AttachCore("Opp.Behind2.BlendedPaceSec", () => SafeOppValue(_opponentsEngine?.Outputs.Behind2.BlendedPaceSec ?? 0.0));
            AttachCore("Opp.Behind2.PaceDeltaSecPerLap", () => SafeOppValue(_opponentsEngine?.Outputs.Behind2.PaceDeltaSecPerLap ?? double.NaN));
            AttachCore("Opp.Behind2.LapsToFight", () => SafeOppValue(_opponentsEngine?.Outputs.Behind2.LapsToFight ?? double.NaN));

            AttachCore("Opp.Leader.BlendedPaceSec", () => SafeOppValue(_opponentsEngine != null ? _opponentsEngine.Outputs.LeaderBlendedPaceSec : double.NaN));
            AttachCore("Opp.P2.BlendedPaceSec", () => SafeOppValue(_opponentsEngine != null ? _opponentsEngine.Outputs.P2BlendedPaceSec : double.NaN));
            AttachCore("Opponents_SummaryAhead", () => _opponentsEngine?.Outputs.SummaryAhead ?? string.Empty);
            AttachCore("Opponents_SummaryBehind", () => _opponentsEngine?.Outputs.SummaryBehind ?? string.Empty);
            AttachCore("Opponents_SummaryAhead1", () => _opponentsEngine?.Outputs.SummaryAhead1 ?? string.Empty);
            AttachCore("Opponents_SummaryAhead2", () => _opponentsEngine?.Outputs.SummaryAhead2 ?? string.Empty);
            AttachCore("Opponents_SummaryBehind1", () => _opponentsEngine?.Outputs.SummaryBehind1 ?? string.Empty);
            AttachCore("Opponents_SummaryBehind2", () => _opponentsEngine?.Outputs.SummaryBehind2 ?? string.Empty);

            AttachCore("PitExit.Valid", () => _opponentsEngine?.Outputs.PitExit.Valid ?? false);
            AttachCore("PitExit.PredictedPositionInClass", () => _opponentsEngine?.Outputs.PitExit.PredictedPositionInClass ?? 0);
            AttachCore("PitExit.CarsAheadAfterPitCount", () => _opponentsEngine?.Outputs.PitExit.CarsAheadAfterPitCount ?? 0);
            AttachCore("PitExit.Summary", () => _opponentsEngine?.Outputs.PitExit.Summary ?? string.Empty);
            AttachCore("PitExit.Ahead.Name", () => _opponentsEngine?.Outputs.PitExit.AheadName ?? string.Empty);
            AttachCore("PitExit.Ahead.CarNumber", () => _opponentsEngine?.Outputs.PitExit.AheadCarNumber ?? string.Empty);
            AttachCore("PitExit.Ahead.ClassColor", () => _opponentsEngine?.Outputs.PitExit.AheadClassColor ?? string.Empty);
            AttachCore("PitExit.Ahead.GapSec", () => _opponentsEngine?.Outputs.PitExit.AheadGapSec ?? 0.0);
            AttachCore("PitExit.Behind.Name", () => _opponentsEngine?.Outputs.PitExit.BehindName ?? string.Empty);
            AttachCore("PitExit.Behind.CarNumber", () => _opponentsEngine?.Outputs.PitExit.BehindCarNumber ?? string.Empty);
            AttachCore("PitExit.Behind.ClassColor", () => _opponentsEngine?.Outputs.PitExit.BehindClassColor ?? string.Empty);
            AttachCore("PitExit.Behind.GapSec", () => _opponentsEngine?.Outputs.PitExit.BehindGapSec ?? 0.0);

            AttachCore("Radio.TransmitShortName", () => _radioTransmitShortName ?? string.Empty);
            AttachCore("Radio.TransmitFullName", () => _radioTransmitFullName ?? string.Empty);
            AttachCore("Radio.TransmitFrequencyName", () => string.Empty);
            AttachCore("Radio.TransmitFrequencyMuted", () => _radioTransmitFrequencyMuted);
            AttachCore("Radio.TransmitClassPosLabel", () => _radioTransmitClassPosLabel ?? string.Empty);
            AttachCore("Radio.LocalTxFrequencyNum", () => _localTxFrequencyNum);
            AttachCore("Radio.LocalTxFrequencyName", () => _localTxFrequencyName ?? string.Empty);
            AttachCore("Radio.LocalTxFrequencyMuted", () => _localTxFrequencyMuted);
            AttachCore("Radio.IsPlayerTransmitting", () => _radioIsPlayerTransmitting);
            AttachCore("Car.Valid", () => _carSaEngine?.Outputs.Valid ?? false);
            AttachCore("Car.Source", () => _carSaEngine?.Outputs.Source ?? string.Empty);
            AttachCore("Car.SlotsAhead", () => _carSaEngine?.Outputs.SlotsAhead ?? 0);
            AttachCore("Car.SlotsBehind", () => _carSaEngine?.Outputs.SlotsBehind ?? 0);
            AttachCore("Car.iRatingSOF", () => _carSaEngine?.Outputs.IRatingSOF ?? 0.0);
            AttachCore("Car.Ahead01P.Gap.Sec", () => _carSaEngine?.Outputs.Ahead01PrecisionGapSec ?? double.NaN);
            AttachCore("Car.Behind01P.Gap.Sec", () => _carSaEngine?.Outputs.Behind01PrecisionGapSec ?? double.NaN);
            AttachCore("Car.Player.PaceFlagsRaw", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.PlayerPaceFlagsRaw ?? -1) : -1);
            AttachCore("Car.Player.SessionFlagsRaw", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.PlayerSessionFlagsRaw ?? -1) : -1);
            AttachCore("Car.Player.TrackSurfaceMaterialRaw", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.PlayerTrackSurfaceMaterialRaw ?? -1) : -1);
            AttachCore("Car.Player.CarIdx", () => _carSaEngine?.Outputs.PlayerSlot.CarIdx ?? -1);
            AttachCore("Car.Player.ClassName", () => _carSaEngine?.Outputs.PlayerSlot.ClassName ?? string.Empty);
            AttachCore("Car.Player.ClassColor", () => _carSaEngine?.Outputs.PlayerSlot.ClassColor ?? string.Empty);
            AttachCore("Car.Player.ClassColorHex", () => _carSaEngine?.Outputs.PlayerSlot.ClassColorHex ?? string.Empty);
            AttachCore("Car.Player.IRating", () => _carSaEngine?.Outputs.PlayerSlot.IRating ?? 0);
            AttachCore("Car.Player.Licence", () => _carSaEngine?.Outputs.PlayerSlot.Licence ?? string.Empty);
            AttachCore("Car.Player.SafetyRating", () => _carSaEngine?.Outputs.PlayerSlot.SafetyRating ?? double.NaN);
            AttachCore("Car.Player.LicLevel", () => _carSaEngine?.Outputs.PlayerSlot.LicLevel ?? 0);
            AttachCore("Car.Player.TeamID", () => _carSaEngine?.Outputs.PlayerSlot.TeamID ?? 0);
            AttachCore("Car.Player.IsFriend", () => _carSaEngine?.Outputs.PlayerSlot.IsFriend ?? false);
            AttachCore("Car.Player.IsTeammate", () => _carSaEngine?.Outputs.PlayerSlot.IsTeammate ?? false);
            AttachCore("Car.Player.IsBad", () => _carSaEngine?.Outputs.PlayerSlot.IsBad ?? false);
            AttachCore("Car.Player.LapsSincePit", () => _carSaEngine?.Outputs.PlayerSlot.LapsSincePit ?? -1);
            AttachCore("Car.Player.Status", () => _carSaEngine?.Outputs.PlayerSlot.Status ?? 0);
            AttachCore("Car.Player.StatusE", () => _carSaEngine?.Outputs.PlayerSlot.StatusE ?? 0);
            AttachCore("Car.Player.StatusShort", () => _carSaEngine?.Outputs.PlayerSlot.StatusShort ?? string.Empty);
            AttachCore("Car.Player.StatusLong", () => _carSaEngine?.Outputs.PlayerSlot.StatusLong ?? string.Empty);
            AttachCore("Car.Player.StatusEReason", () => _carSaEngine?.Outputs.PlayerSlot.StatusEReason ?? string.Empty);
            AttachCore("Car.Player.SuspectPulseActive", () => _carSaEngine?.Outputs.PlayerSlot.SuspectPulseActive ?? false);
            AttachCore("Car.Player.SuspectEventId", () => _carSaEngine?.Outputs.PlayerSlot.SuspectEventId ?? 0);
            AttachCore("Car.Player.LapTimeUpdate", () => _carSaEngine?.Outputs.PlayerSlot.LapTimeUpdate ?? string.Empty);
            AttachCore("Car.Player.LapTimeUpdateVisibilitySec", () => _carSaEngine?.Outputs.PlayerSlot.LapTimeUpdateVisibilitySec ?? 0.0);
            AttachCore("Player.LapTimeUpdate", () => _carSaEngine?.Outputs.PlayerSlot.LapTimeUpdate ?? string.Empty);
            AttachCore("Player.LapTimeUpdateVisibilitySec", () => _carSaEngine?.Outputs.PlayerSlot.LapTimeUpdateVisibilitySec ?? 0.0);
            for (int i = 0; i < CarSAEngine.SlotsAhead; i++)
            {
                int slotIndex = i;
                string label = (i + 1).ToString("00");
                AttachCore($"Car.Ahead{label}.CarIdx", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].CarIdx ?? -1);
                AttachCore($"Car.Ahead{label}.Name", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].Name ?? string.Empty);
                AttachCore($"Car.Ahead{label}.CarNumber", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].CarNumber ?? string.Empty);
                AttachCore($"Car.Ahead{label}.ClassColor", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].ClassColor ?? string.Empty);
                AttachCore($"Car.Ahead{label}.IsOnTrack", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].IsOnTrack ?? false);
                AttachCore($"Car.Ahead{label}.IsOnPitRoad", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].IsOnPitRoad ?? false);
                AttachCore($"Car.Ahead{label}.IsValid", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].IsValid ?? false);
                AttachCore($"Car.Ahead{label}.IsTalking", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].IsTalking ?? false);
                AttachCore($"Car.Ahead{label}.TalkRadioIdx", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].TalkRadioIdx ?? -1);
                AttachCore($"Car.Ahead{label}.TalkFrequencyIdx", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].TalkFrequencyIdx ?? -1);
                AttachCore($"Car.Ahead{label}.TalkFrequencyName", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].TalkFrequencyName ?? string.Empty);
                AttachCore($"Car.Ahead{label}.LapDelta", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].LapDelta ?? 0);
                AttachCore($"Car.Ahead{label}.Gap.TrackSec", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].GapTrackSec ?? double.NaN);
                AttachCore($"Car.Ahead{label}.Gap.RelativeSec", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].GapRelativeSec ?? double.NaN);
                AttachCore($"Car.Ahead{label}.Gap.RelativeSource", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].GapRelativeSource ?? 0);
                AttachCore($"Car.Ahead{label}.InfoVisibility", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].InfoVisibility ?? 0);
                AttachCore($"Car.Ahead{label}.Info", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].Info ?? string.Empty);
                AttachCore($"Car.Ahead{label}.ClosingRateSecPerSec", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].ClosingRateSecPerSec ?? double.NaN);
                AttachCore($"Car.Ahead{label}.Status", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].Status ?? 0);
                AttachCore($"Car.Ahead{label}.StatusE", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].StatusE ?? 0);
                AttachCore($"Car.Ahead{label}.StatusShort", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].StatusShort ?? string.Empty);
                AttachCore($"Car.Ahead{label}.StatusLong", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].StatusLong ?? string.Empty);
                AttachCore($"Car.Ahead{label}.StatusEReason", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].StatusEReason ?? string.Empty);
                AttachCore($"Car.Ahead{label}.SuspectPulseActive", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].SuspectPulseActive ?? false);
                AttachCore($"Car.Ahead{label}.SuspectEventId", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].SuspectEventId ?? 0);
                AttachCore($"Car.Ahead{label}.StatusBgHex", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].StatusBgHex ?? "#000000");
                AttachCore($"Car.Ahead{label}.BorderMode", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].BorderMode ?? CarSAStyleResolver.BorderModeDefault);
                AttachCore($"Car.Ahead{label}.BorderHex", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].BorderHex ?? "#A9A9A9");
                AttachCore($"Car.Ahead{label}.SessionFlagsRaw", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].SessionFlagsRaw ?? -1);
                AttachCore($"Car.Ahead{label}.TrackSurfaceMaterialRaw", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].TrackSurfaceMaterialRaw ?? -1);
                AttachCore($"Car.Ahead{label}.PositionInClass", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].PositionInClass ?? 0);
                AttachCore($"Car.Ahead{label}.ClassName", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].ClassName ?? string.Empty);
                AttachCore($"Car.Ahead{label}.ClassColorHex", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].ClassColorHex ?? string.Empty);
                AttachCore($"Car.Ahead{label}.CarClassShortName", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].CarClassShortName ?? string.Empty);
                AttachCore($"Car.Ahead{label}.Initials", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].Initials ?? string.Empty);
                AttachCore($"Car.Ahead{label}.AbbrevName", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].AbbrevName ?? string.Empty);
                AttachCore($"Car.Ahead{label}.IRating", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].IRating ?? 0);
                AttachCore($"Car.Ahead{label}.Licence", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].Licence ?? string.Empty);
                AttachCore($"Car.Ahead{label}.SafetyRating", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].SafetyRating ?? double.NaN);
                AttachCore($"Car.Ahead{label}.LicLevel", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].LicLevel ?? 0);
                AttachCore($"Car.Ahead{label}.UserID", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].UserID ?? 0);
                AttachCore($"Car.Ahead{label}.TeamID", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].TeamID ?? 0);
                AttachCore($"Car.Ahead{label}.IsFriend", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].IsFriend ?? false);
                AttachCore($"Car.Ahead{label}.IsTeammate", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].IsTeammate ?? false);
                AttachCore($"Car.Ahead{label}.IsBad", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].IsBad ?? false);
                AttachCore($"Car.Ahead{label}.LapsSincePit", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].LapsSincePit ?? -1);
                AttachCore($"Car.Ahead{label}.BestLapTimeSec", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].BestLapTimeSec ?? double.NaN);
                AttachCore($"Car.Ahead{label}.LastLapTimeSec", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].LastLapTimeSec ?? double.NaN);
                AttachCore($"Car.Ahead{label}.BestLap", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].BestLap ?? string.Empty);
                AttachCore($"Car.Ahead{label}.BestLapIsEstimated", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].BestLapIsEstimated ?? false);
                AttachCore($"Car.Ahead{label}.LastLap", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].LastLap ?? string.Empty);
                AttachCore($"Car.Ahead{label}.DeltaBestSec", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].DeltaBestSec ?? double.NaN);
                AttachCore($"Car.Ahead{label}.DeltaBest", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].DeltaBest ?? string.Empty);
                AttachCore($"Car.Ahead{label}.EstLapTimeSec", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].EstLapTimeSec ?? double.NaN);
                AttachCore($"Car.Ahead{label}.EstLapTime", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].EstLapTime ?? string.Empty);
                AttachCore($"Car.Ahead{label}.LapTimeUpdate", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].LapTimeUpdate ?? string.Empty);
                AttachCore($"Car.Ahead{label}.LapTimeUpdateVisibilitySec", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].LapTimeUpdateVisibilitySec ?? 0.0);
                AttachCore($"Car.Ahead{label}.HotScore", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].HotScore ?? 0.0);
                AttachCore($"Car.Ahead{label}.HotVia", () => _carSaEngine?.Outputs.AheadSlots[slotIndex].HotVia ?? string.Empty);
            }
            for (int i = 0; i < CarSAEngine.SlotsBehind; i++)
            {
                int slotIndex = i;
                string label = (i + 1).ToString("00");
                AttachCore($"Car.Behind{label}.CarIdx", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].CarIdx ?? -1);
                AttachCore($"Car.Behind{label}.Name", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].Name ?? string.Empty);
                AttachCore($"Car.Behind{label}.CarNumber", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].CarNumber ?? string.Empty);
                AttachCore($"Car.Behind{label}.ClassColor", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].ClassColor ?? string.Empty);
                AttachCore($"Car.Behind{label}.IsOnTrack", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].IsOnTrack ?? false);
                AttachCore($"Car.Behind{label}.IsOnPitRoad", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].IsOnPitRoad ?? false);
                AttachCore($"Car.Behind{label}.IsValid", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].IsValid ?? false);
                AttachCore($"Car.Behind{label}.IsTalking", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].IsTalking ?? false);
                AttachCore($"Car.Behind{label}.TalkRadioIdx", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].TalkRadioIdx ?? -1);
                AttachCore($"Car.Behind{label}.TalkFrequencyIdx", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].TalkFrequencyIdx ?? -1);
                AttachCore($"Car.Behind{label}.TalkFrequencyName", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].TalkFrequencyName ?? string.Empty);
                AttachCore($"Car.Behind{label}.LapDelta", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].LapDelta ?? 0);
                AttachCore($"Car.Behind{label}.Gap.TrackSec", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].GapTrackSec ?? double.NaN);
                AttachCore($"Car.Behind{label}.Gap.RelativeSec", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].GapRelativeSec ?? double.NaN);
                AttachCore($"Car.Behind{label}.Gap.RelativeSource", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].GapRelativeSource ?? 0);
                AttachCore($"Car.Behind{label}.InfoVisibility", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].InfoVisibility ?? 0);
                AttachCore($"Car.Behind{label}.Info", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].Info ?? string.Empty);
                AttachCore($"Car.Behind{label}.ClosingRateSecPerSec", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].ClosingRateSecPerSec ?? double.NaN);
                AttachCore($"Car.Behind{label}.Status", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].Status ?? 0);
                AttachCore($"Car.Behind{label}.StatusE", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].StatusE ?? 0);
                AttachCore($"Car.Behind{label}.StatusShort", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].StatusShort ?? string.Empty);
                AttachCore($"Car.Behind{label}.StatusLong", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].StatusLong ?? string.Empty);
                AttachCore($"Car.Behind{label}.StatusEReason", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].StatusEReason ?? string.Empty);
                AttachCore($"Car.Behind{label}.SuspectPulseActive", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].SuspectPulseActive ?? false);
                AttachCore($"Car.Behind{label}.SuspectEventId", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].SuspectEventId ?? 0);
                AttachCore($"Car.Behind{label}.StatusBgHex", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].StatusBgHex ?? "#000000");
                AttachCore($"Car.Behind{label}.BorderMode", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].BorderMode ?? CarSAStyleResolver.BorderModeDefault);
                AttachCore($"Car.Behind{label}.BorderHex", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].BorderHex ?? "#A9A9A9");
                AttachCore($"Car.Behind{label}.SessionFlagsRaw", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].SessionFlagsRaw ?? -1);
                AttachCore($"Car.Behind{label}.TrackSurfaceMaterialRaw", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].TrackSurfaceMaterialRaw ?? -1);
                AttachCore($"Car.Behind{label}.PositionInClass", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].PositionInClass ?? 0);
                AttachCore($"Car.Behind{label}.ClassName", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].ClassName ?? string.Empty);
                AttachCore($"Car.Behind{label}.ClassColorHex", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].ClassColorHex ?? string.Empty);
                AttachCore($"Car.Behind{label}.CarClassShortName", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].CarClassShortName ?? string.Empty);
                AttachCore($"Car.Behind{label}.Initials", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].Initials ?? string.Empty);
                AttachCore($"Car.Behind{label}.AbbrevName", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].AbbrevName ?? string.Empty);
                AttachCore($"Car.Behind{label}.IRating", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].IRating ?? 0);
                AttachCore($"Car.Behind{label}.Licence", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].Licence ?? string.Empty);
                AttachCore($"Car.Behind{label}.SafetyRating", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].SafetyRating ?? double.NaN);
                AttachCore($"Car.Behind{label}.LicLevel", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].LicLevel ?? 0);
                AttachCore($"Car.Behind{label}.UserID", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].UserID ?? 0);
                AttachCore($"Car.Behind{label}.TeamID", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].TeamID ?? 0);
                AttachCore($"Car.Behind{label}.IsFriend", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].IsFriend ?? false);
                AttachCore($"Car.Behind{label}.IsTeammate", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].IsTeammate ?? false);
                AttachCore($"Car.Behind{label}.IsBad", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].IsBad ?? false);
                AttachCore($"Car.Behind{label}.LapsSincePit", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].LapsSincePit ?? -1);
                AttachCore($"Car.Behind{label}.BestLapTimeSec", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].BestLapTimeSec ?? double.NaN);
                AttachCore($"Car.Behind{label}.LastLapTimeSec", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].LastLapTimeSec ?? double.NaN);
                AttachCore($"Car.Behind{label}.BestLap", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].BestLap ?? string.Empty);
                AttachCore($"Car.Behind{label}.BestLapIsEstimated", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].BestLapIsEstimated ?? false);
                AttachCore($"Car.Behind{label}.LastLap", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].LastLap ?? string.Empty);
                AttachCore($"Car.Behind{label}.DeltaBestSec", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].DeltaBestSec ?? double.NaN);
                AttachCore($"Car.Behind{label}.DeltaBest", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].DeltaBest ?? string.Empty);
                AttachCore($"Car.Behind{label}.EstLapTimeSec", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].EstLapTimeSec ?? double.NaN);
                AttachCore($"Car.Behind{label}.EstLapTime", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].EstLapTime ?? string.Empty);
                AttachCore($"Car.Behind{label}.LapTimeUpdate", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].LapTimeUpdate ?? string.Empty);
                AttachCore($"Car.Behind{label}.LapTimeUpdateVisibilitySec", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].LapTimeUpdateVisibilitySec ?? 0.0);
                AttachCore($"Car.Behind{label}.HotScore", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].HotScore ?? 0.0);
                AttachCore($"Car.Behind{label}.HotVia", () => _carSaEngine?.Outputs.BehindSlots[slotIndex].HotVia ?? string.Empty);
            }

            AttachCore("Car.Debug.PlayerCarIdx", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.PlayerCarIdx ?? -1) : -1);
            AttachCore("Car.Debug.PlayerLapPct", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.PlayerLapPct ?? double.NaN) : double.NaN);
            AttachCore("Car.Debug.PlayerLap", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.PlayerLap ?? 0) : 0);
            AttachCore("Car.Debug.SessionTimeSec", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.SessionTimeSec ?? 0.0) : 0.0);
            AttachCore("Car.Debug.OffTrack.ProbeCarIdx", () => SoftDebugEnabled ? (Settings?.OffTrackDebugProbeCarIdx ?? -1) : -1);
            AttachCore("Car.Debug.SourceFastPathUsed", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.SourceFastPathUsed ?? false) : false);
            AttachCore("Car.Debug.HasCarIdxPaceFlags", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.HasCarIdxPaceFlags ?? false) : false);
            AttachCore("Car.Debug.HasCarIdxSessionFlags", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.HasCarIdxSessionFlags ?? false) : false);
            AttachCore("Car.Debug.HasCarIdxTrackSurfaceMaterial", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.HasCarIdxTrackSurfaceMaterial ?? false) : false);
            AttachCore("Car.Debug.RawTelemetryReadMode", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.RawTelemetryReadMode ?? string.Empty) : string.Empty);
            AttachCore("Car.Debug.RawTelemetryFailReason", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.RawTelemetryFailReason ?? string.Empty) : string.Empty);
            AttachCore("Car.Debug.Ahead01.CarIdx", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.Ahead01CarIdx ?? -1) : -1);
            AttachCore("Car.Debug.Ahead01.ForwardDistPct", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.Ahead01ForwardDistPct ?? double.NaN) : double.NaN);
            AttachCore("Car.Debug.Behind01.CarIdx", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.Behind01CarIdx ?? -1) : -1);
            AttachCore("Car.Debug.Behind01.BackwardDistPct", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.Behind01BackwardDistPct ?? double.NaN) : double.NaN);
            AttachCore("Car.Debug.InvalidLapPctCount", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.InvalidLapPctCount ?? 0) : 0);
            AttachCore("Car.Debug.OnPitRoadCount", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.OnPitRoadCount ?? 0) : 0);
            AttachCore("Car.Debug.OnTrackCount", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.OnTrackCount ?? 0) : 0);
            AttachCore("Car.Debug.TimestampUpdatesThisTick", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.TimestampUpdatesThisTick ?? 0) : 0);
            AttachCore("Car.Debug.FilteredHalfLapCountAhead", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.FilteredHalfLapCountAhead ?? 0) : 0);
            AttachCore("Car.Debug.FilteredHalfLapCountBehind", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.FilteredHalfLapCountBehind ?? 0) : 0);
            AttachCore("Car.Debug.LapTimeEstimateSec", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.LapTimeEstimateSec ?? 0.0) : 0.0);
            AttachCore("Car.Debug.LapTimeUsedSec", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.LapTimeUsedSec ?? 0.0) : 0.0);
            AttachCore("Car.Debug.HysteresisReplacementsThisTick", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.HysteresisReplacementsThisTick ?? 0) : 0);
            AttachCore("Car.Debug.SlotCarIdxChangedThisTick", () => SoftDebugEnabled ? (_carSaEngine?.Outputs.Debug.SlotCarIdxChangedThisTick ?? 0) : 0);

        }

        private void Pit_OnValidPitStopTimeLossCalculated(double timeLossSeconds, string sourceFromPublisher)
        {
            // Guards
            if (ActiveProfile == null || string.IsNullOrEmpty(CurrentTrackKey))
            {
                SimHub.Logging.Current.Warn("[LalaPlugin:Pit Cycle] Cannot save pit time loss – no active profile or track.");
                return;
            }

            var trackStatsForLog = ActiveProfile.ResolveTrackByNameOrKey(CurrentTrackKey);
            string existingValue = trackStatsForLog?.PitLaneLossSeconds.HasValue == true
                ? trackStatsForLog.PitLaneLossSeconds.Value.ToString("0.00")
                : "null";
            bool existingLocked = trackStatsForLog?.PitLaneLossLocked ?? false;
            double lastDirect = _pit?.LastDirectTravelTime ?? 0.0;
            SimHub.Logging.Current.Info(
                $"[LalaPlugin:Pit Cycle] Persist request: timeLoss={timeLossSeconds:F2} " +
                $"src={sourceFromPublisher ?? "none"} lastDirect={lastDirect:F2} " +
                $"existingLocked={existingLocked} existingValue={existingValue}");

            // If we've already saved this exact DTL value, ignore repeat callers.
            if (sourceFromPublisher != null
                && sourceFromPublisher.Equals("dtl", StringComparison.OrdinalIgnoreCase)
                && Math.Abs(timeLossSeconds - _lastPitLossSaved) < 0.01)
            {
                return;
            }

            // 1) Prefer the number passed in (PitLite’s one-shot). If zero/invalid, skip persist.
            double loss = Math.Max(0.0, timeLossSeconds);
            string src = (sourceFromPublisher ?? "").Trim().ToLowerInvariant();
            if (double.IsNaN(timeLossSeconds) || double.IsNaN(loss))
            {
                SimHub.Logging.Current.Info(
                    $"[LalaPlugin:Pit Cycle] Persist decision: action=SKIP reason=nan_candidate " +
                    $"timeLoss={timeLossSeconds:F2} src={src}");
                return;
            }
            if (loss <= 0.0)
            {
                SimHub.Logging.Current.Info(
                    $"[LalaPlugin:Pit Cycle] Persist decision: action=SKIP reason=invalid_candidate " +
                    $"timeLoss={timeLossSeconds:F2} src={src}");
                return;
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

            bool existingValid = trackRecord.PitLaneLossSeconds.HasValue
                && trackRecord.PitLaneLossSeconds.Value > 0.0
                && !double.IsNaN(trackRecord.PitLaneLossSeconds.Value);
            bool candidateValid = rounded > 0.0 && !double.IsNaN(rounded);
            if (!candidateValid)
            {
                SimHub.Logging.Current.Info(
                    $"[LalaPlugin:Pit Cycle] Persist decision: action=SKIP reason=candidate_invalid " +
                    $"seconds={rounded:0.00} src={src}");
                return;
            }

            if (!existingValid)
            {
                trackRecord.PitLaneLossSeconds = rounded;
                trackRecord.PitLaneLossSource = src;                  // "dtl" or "direct"
                trackRecord.PitLaneLossUpdatedUtc = now;              // DateTime.UtcNow above
                ProfilesViewModel?.SaveProfiles();
                SimHub.Logging.Current.Info(
                    $"[LalaPlugin:Pit Cycle] Persist decision: action=WRITE " +
                    $"seconds={rounded:0.00} src={src} locked={trackRecord.PitLaneLossLocked}");
            }
            else if (existingValid && trackRecord.PitLaneLossLocked)
            {

                trackRecord.PitLaneLossBlockedCandidateSeconds = rounded;
                trackRecord.PitLaneLossBlockedCandidateSource = src;
                trackRecord.PitLaneLossBlockedCandidateUpdatedUtc = now;

                ProfilesViewModel?.SaveProfiles();
                SimHub.Logging.Current.Info(
                    $"[LalaPlugin:Pit Cycle] Persist decision: action=BLOCKED_CANDIDATE " +
                    $"seconds={rounded:0.00} src={src} locked={trackRecord.PitLaneLossLocked}");
                _lastPitLossSaved = rounded;
                _lastPitLossSavedAtUtc = now;
                _lastPitLossSource = src;

                return;
            }
            else
            {
                trackRecord.PitLaneLossSeconds = rounded;
                trackRecord.PitLaneLossSource = src;                  // "dtl" or "direct"
                trackRecord.PitLaneLossUpdatedUtc = now;              // DateTime.UtcNow above
                ProfilesViewModel?.SaveProfiles();
                SimHub.Logging.Current.Info(
                    $"[LalaPlugin:Pit Cycle] Persist decision: action=WRITE " +
                    $"seconds={rounded:0.00} src={src} locked={trackRecord.PitLaneLossLocked}");
            }

            // Publish to the live snapshot + Fuel tab immediately
            FuelCalculator?.SetLastPitDriveThroughSeconds(rounded);
            FuelCalculator?.ForceProfileDataReload();

            // Remember last save
            _lastPitLossSaved = rounded;
            _lastPitLossSavedAtUtc = now;
            _lastPitLossSource = src;

            SimHub.Logging.Current.Info($"[LalaPlugin:Pit Cycle] Saved PitLaneLoss = {rounded:0.00}s ({src}).");
        }

        private void ProcessTrackMarkerTriggers()
        {
            if (_pit == null) return;

            while (_pit.TryDequeueTrackMarkerTrigger(out var trig))
            {
                switch (trig.Trigger)
                {
                    case PitEngine.TrackMarkerTriggerType.FirstCapture:
                        _trackMarkerFirstCapturePulseUtc = DateTime.UtcNow;
                        _trackMarkerCapturedPulse.Set(new TrackMarkerCapturedMessage
                        {
                            TrackKey = trig.TrackKey,
                            EntryPct = trig.EntryPct,
                            ExitPct = trig.ExitPct,
                            Locked = trig.Locked
                        });
                        break;
                    case PitEngine.TrackMarkerTriggerType.TrackLengthChanged:
                        _trackMarkerTrackLengthChangedPulseUtc = DateTime.UtcNow;
                        _trackMarkerLengthDeltaPulse.Set(new TrackMarkerLengthDeltaMessage
                        {
                            TrackKey = trig.TrackKey,
                            StartM = trig.StartTrackLengthM,
                            NowM = trig.CurrentTrackLengthM,
                            DeltaM = trig.TrackLengthDeltaM
                        });
                        break;
                    case PitEngine.TrackMarkerTriggerType.LinesRefreshed:
                        _trackMarkerLinesRefreshedPulseUtc = DateTime.UtcNow;
                        break;
                    case PitEngine.TrackMarkerTriggerType.LockedMismatch:
                        _trackMarkerLockedMismatchPulse.Set(new TrackMarkerLockedMismatchMessage
                        {
                            TrackKey = trig.TrackKey,
                            StoredEntryPct = trig.EntryPct,
                            StoredExitPct = trig.ExitPct,
                            CandidateEntryPct = trig.CandidateEntryPct,
                            CandidateExitPct = trig.CandidateExitPct,
                            TolerancePct = trig.TolerancePct
                        });
                        break;
                }
            }
        }

        internal TrackMarkerCapturedMessage ConsumeTrackMarkerCapturedPulse()
        {
            return _trackMarkerCapturedPulse.TryConsume(TrackMarkerPulseHoldSeconds, out var data) ? data : null;
        }

        internal TrackMarkerLengthDeltaMessage ConsumeTrackMarkerLengthDeltaPulse()
        {
            return _trackMarkerLengthDeltaPulse.TryConsume(TrackMarkerPulseHoldSeconds, out var data) ? data : null;
        }

        internal TrackMarkerLockedMismatchMessage ConsumeTrackMarkerLockedMismatchPulse()
        {
            return _trackMarkerLockedMismatchPulse.TryConsume(TrackMarkerPulseHoldSeconds, out var data) ? data : null;
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
                SimHub.Logging.Current.Info($"[LalaPlugin:Pit Cycle] Pit Lite Data used for DTL.");
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

            ResetOffTrackDebugExportState();
            ResetCarSaDebugExportState();

            _shiftAssistAudio?.Dispose();

            // Persist settings
            SaveSettings();
            ProfilesViewModel.SaveProfiles();

        }

        private LaunchPluginSettings LoadSettings()
        {
            var newPath = PluginStorage.GetPluginFilePath(GlobalSettingsFileName);
            var legacyPath = PluginStorage.GetCommonFilePath(GlobalSettingsLegacyFileName);

            try
            {
                if (File.Exists(newPath))
                {
                    var settings = ReadSettingsFromPath(newPath);
                    EnforceHardDebugSettings(settings);
                    return settings;
                }

                if (File.Exists(legacyPath))
                {
                    var settings = ReadSettingsFromPath(legacyPath);
                    EnforceHardDebugSettings(settings);
                    SaveSettingsToPath(newPath, settings);
                    SimHub.Logging.Current.Info($"[LalaPlugin:Storage] migrated {legacyPath} -> {newPath}");
                    return settings;
                }

                var defaults = new LaunchPluginSettings();
                EnforceHardDebugSettings(defaults);
                SaveSettingsToPath(newPath, defaults);
                return defaults;
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn($"[LalaPlugin:Storage] settings load failed; using defaults. {ex.Message}");
                var defaults = new LaunchPluginSettings();
                EnforceHardDebugSettings(defaults);
                SafeTry(() => SaveSettingsToPath(newPath, defaults));
                return defaults;
            }
        }

        private LaunchPluginSettings ReadSettingsFromPath(string path)
        {
            var json = File.ReadAllText(path);
            var settings = JsonConvert.DeserializeObject<LaunchPluginSettings>(json) ?? new LaunchPluginSettings();
            NormalizeCarSaStyleSettings(settings);
            NormalizeShiftAssistSettings(settings);
            return settings;
        }

        private void SaveSettings()
        {
            var path = PluginStorage.GetPluginFilePath(GlobalSettingsFileName);
            SaveSettingsToPath(path, Settings);
        }

        private void SaveSettingsToPath(string path, LaunchPluginSettings settings)
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            var effectiveSettings = settings ?? new LaunchPluginSettings();
            EnforceHardDebugSettings(effectiveSettings);
            NormalizeCarSaStyleSettings(effectiveSettings);
            NormalizeShiftAssistSettings(effectiveSettings);
            var json = JsonConvert.SerializeObject(effectiveSettings, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        private void EnforceHardDebugSettings(LaunchPluginSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            if (!HardDebugEnabled)
            {
                settings.EnableSoftDebug = false;
            }
        }

        private static void NormalizeShiftAssistSettings(LaunchPluginSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            if (settings.ShiftAssistBeepDurationMs < ShiftAssistBeepDurationMsMin)
            {
                settings.ShiftAssistBeepDurationMs = ShiftAssistBeepDurationMsMin;
            }
            else if (settings.ShiftAssistBeepDurationMs > ShiftAssistBeepDurationMsMax)
            {
                settings.ShiftAssistBeepDurationMs = ShiftAssistBeepDurationMsMax;
            }

            if (settings.ShiftAssistLeadTimeMs < ShiftAssistLeadTimeMsMin)
            {
                settings.ShiftAssistLeadTimeMs = ShiftAssistLeadTimeMsMin;
            }
            else if (settings.ShiftAssistLeadTimeMs > ShiftAssistLeadTimeMsMax)
            {
                settings.ShiftAssistLeadTimeMs = ShiftAssistLeadTimeMsMax;
            }

            if (settings.ShiftAssistBeepVolumePct < ShiftAssistBeepVolumePctMin)
            {
                settings.ShiftAssistBeepVolumePct = ShiftAssistBeepVolumePctMin;
            }
            else if (settings.ShiftAssistBeepVolumePct > ShiftAssistBeepVolumePctMax)
            {
                settings.ShiftAssistBeepVolumePct = ShiftAssistBeepVolumePctMax;
            }

            if (settings.ShiftAssistDebugCsvMaxHz <= 0)
            {
                settings.ShiftAssistDebugCsvMaxHz = ShiftAssistDebugCsvMaxHzDefault;
            }

            if (settings.ShiftAssistDebugCsvMaxHz < ShiftAssistDebugCsvMaxHzMin)
            {
                settings.ShiftAssistDebugCsvMaxHz = ShiftAssistDebugCsvMaxHzMin;
            }
            else if (settings.ShiftAssistDebugCsvMaxHz > ShiftAssistDebugCsvMaxHzMax)
            {
                settings.ShiftAssistDebugCsvMaxHz = ShiftAssistDebugCsvMaxHzMax;
            }
        }

        private static void NormalizeCarSaStyleSettings(LaunchPluginSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.CarSAStatusEBackgroundColors = NormalizeStatusColorMap(settings.CarSAStatusEBackgroundColors);
            settings.CarSABorderColors = NormalizeBorderColorMap(settings.CarSABorderColors);
            NormalizeFriendSettings(settings);
        }

        private static void NormalizeFriendSettings(LaunchPluginSettings settings)
        {
            if (settings.Friends == null)
            {
                settings.Friends = new ObservableCollection<LaunchPluginFriendEntry>();
                return;
            }

            for (int i = settings.Friends.Count - 1; i >= 0; i--)
            {
                var entry = settings.Friends[i];
                if (entry == null)
                {
                    settings.Friends.RemoveAt(i);
                    continue;
                }

                string name = entry.Name?.Trim() ?? string.Empty;
                entry.Name = string.IsNullOrWhiteSpace(name) ? "Friend" : name;
                if (entry.UserId < 0)
                {
                    entry.UserId = 0;
                }
            }
        }

        private static Dictionary<int, string> NormalizeStatusColorMap(Dictionary<int, string> source)
        {
            var normalized = source != null
                ? new Dictionary<int, string>(source)
                : new Dictionary<int, string>();

            foreach (var pair in DefaultCarSAStatusEBackgroundColors)
            {
                if (!normalized.TryGetValue(pair.Key, out var color) || !CarSAStyleResolver.IsValidHexColor(color))
                {
                    normalized[pair.Key] = pair.Value;
                }
            }

            return normalized;
        }

        private static Dictionary<string, string> NormalizeBorderColorMap(Dictionary<string, string> source)
        {
            var normalized = source != null
                ? new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in DefaultCarSABorderColors)
            {
                if (!normalized.TryGetValue(pair.Key, out var color) || !CarSAStyleResolver.IsValidHexColor(color))
                {
                    normalized[pair.Key] = pair.Value;
                }
            }

            return normalized;
        }

        private static void SafeTry(Action action)
        {
            try { action(); } catch { /* ignore */ }
        }

        private void AbortLaunch()
        {
            SetLaunchState(LaunchState.Cancelled);
            ResetCoreLaunchMetrics(); // Call the shared method

            // Abort-specific actions
            _telemetryTraceLogger?.StopLaunchTrace();
            _telemetryTraceLogger?.DiscardCurrentTrace();
        }

        private void CancelLaunchToIdle(string reason)
        {
            AbortLaunch();
            SetLaunchState(LaunchState.Idle);

            if (!_launchAbortLatched)
            {
                SimHub.Logging.Current.Info($"[LalaPlugin:Launch Trace] {reason} – cancelling to Idle.");
                _launchAbortLatched = true;
            }
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
            _launchAbortLatched = false;

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
            PitWindowState = 6;
            PitWindowLabel = "N/A";
            IsPitWindowOpen = false;
            PitWindowOpeningLap = 0;
            PitWindowClosingLap = 0;
            _lastPitWindowState = -1;
            _lastPitWindowLabel = string.Empty;
            _lastPitWindowLogUtc = DateTime.MinValue;
            _opponentsEngine?.Reset();
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
            //_launchAbortLatched = false;
        }

        private bool IsLaunchBlocked(PluginManager pluginManager, GameData data, out bool inPits, out bool seriousRejoin)
        {
            inPits = false;
            seriousRejoin = false;

            if (pluginManager != null)
            {
                var pitRoad = TryReadNullableBool(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.OnPitRoad"));
                var inPitLane = data?.NewData != null ? (data.NewData.IsInPitLane != 0) : (bool?)null;
                inPits = pitRoad ?? inPitLane ?? false;
            }

            var rejoin = _rejoinEngine;
            if (rejoin != null)
            {
                var logic = rejoin.CurrentLogicCode;
                var det = rejoin.DetectedReason;

                bool spin = (logic == RejoinReason.Spin || det == RejoinReason.Spin);
                bool wrongWay = (logic == RejoinReason.WrongWay || det == RejoinReason.WrongWay);

                seriousRejoin = spin || wrongWay;
            }

            return inPits || seriousRejoin;
        }

        private void ResetFinishTimingState()
        {
            _timerZeroSeen = false;
            _timerZeroSessionTime = double.NaN;
            _prevSessionTimeRemain = double.NaN;
            _leaderCheckeredSessionTime = double.NaN;
            _driverCheckeredSessionTime = double.NaN;
            _leaderFinishedSeen = false;
            _overallLeaderHasFinished = false;
            _classLeaderHasFinished = false;
            _overallLeaderHasFinishedValid = false;
            _classLeaderHasFinishedValid = false;
            _isMultiClassSession = false;
            _lastClassLeaderLapPct = double.NaN;
            _lastOverallLeaderLapPct = double.NaN;
            _lastClassLeaderCarIdx = -1;
            _lastOverallLeaderCarIdx = -1;
            _carIdxToClassShortName.Clear();
            _lastCompletedLapForFinish = -1;
            LeaderHasFinished = false;
            _leaderFinishLatchedByFlag = false;
        }

        private void ResetPitScreenToAuto(string reason)
        {
            bool wasManualEnabled = _pitScreenManualEnabled;
            bool wasDismissed = _pitScreenDismissed;
            string previousMode = _pitScreenMode;

            _pitScreenManualEnabled = false;
            _pitScreenDismissed = false;
            _pitScreenMode = "auto";

            if (wasManualEnabled || wasDismissed || !string.Equals(previousMode, _pitScreenMode, StringComparison.Ordinal))
            {
                SimHub.Logging.Current.Info(
                    $"[LalaPlugin:PitScreen] Reset to auto ({reason}) -> mode={_pitScreenMode}, manual={_pitScreenManualEnabled}, dismissed={_pitScreenDismissed}");
            }
        }

        private void UpdatePitScreenState(PluginManager pluginManager)
        {
            bool isOnPitRoad = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.OnPitRoad") ?? false);

            bool newPitScreenActive = _pitScreenActive; // default
            string newPitScreenMode = _pitScreenMode;

            if (isOnPitRoad)
            {
                newPitScreenMode = "auto";
                _pitScreenManualEnabled = false;
                if (!_pittingTimer.IsRunning)
                    _pittingTimer.Restart();

                if (_pittingTimer.Elapsed.TotalMilliseconds > 200)
                    newPitScreenActive = !_pitScreenDismissed;
                else
                    newPitScreenActive = false;
            }
            else
            {
                newPitScreenMode = _pitScreenManualEnabled ? "manual" : "auto";
                newPitScreenActive = _pitScreenManualEnabled;
                _pitScreenDismissed = false;

                if (_pittingTimer.IsRunning)
                {
                    _pittingTimer.Stop();
                    _pittingTimer.Reset();
                }
            }

            if (newPitScreenActive != _pitScreenActive)
            {
                _pitScreenActive = newPitScreenActive;
                SimHub.Logging.Current.Info($"[LalaPlugin:PitScreen] Active -> {_pitScreenActive} (onPitRoad={isOnPitRoad}, dismissed={_pitScreenDismissed}, manual={_pitScreenManualEnabled})");
            }

            if (!string.Equals(newPitScreenMode, _pitScreenMode, StringComparison.Ordinal))
            {
                _pitScreenMode = newPitScreenMode;
                SimHub.Logging.Current.Info($"[LalaPlugin:PitScreen] Mode -> {_pitScreenMode} (onPitRoad={isOnPitRoad}, dismissed={_pitScreenDismissed}, manual={_pitScreenManualEnabled})");
            }
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

        private string GetCanonicalTrackKeyForMarkers()
        {
            if (!string.IsNullOrWhiteSpace(CurrentTrackKey) && !CurrentTrackKey.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                return CurrentTrackKey;
            if (!string.IsNullOrWhiteSpace(CurrentTrackName) && !CurrentTrackName.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                return CurrentTrackName;
            return "unknown";
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
            catch (Exception ex) { SimHub.Logging.Current.Warn($"[LalaPlugin:Profile] Simplified Car/Track probe failed: {ex.Message}"); }

            if (_msgCxCooldownTimer.IsRunning && _msgCxCooldownTimer.ElapsedMilliseconds > DashPulseMs)
            {
                _msgCxCooldownTimer.Reset();
                _msgCxPressed = false;
            }

            if (_eventMarkerCooldownTimer.IsRunning && _eventMarkerCooldownTimer.ElapsedMilliseconds > EventMarkerPulseMs)
            {
                _eventMarkerCooldownTimer.Reset();
                _eventMarkerPressed = false;
            }

            // --- MASTER GUARD CLAUSES ---
            if (Settings == null) return;
            if (!data.GameRunning || data.NewData == null) return;
            EnforceHardDebugSettings(Settings);

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
            long currentSubSessionId = Convert.ToInt64(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.WeekendInfo.SubSessionID") ?? -1);
            string currentSessionToken = $"{currentSessionId}:{currentSubSessionId}";
            if (!string.Equals(currentSessionToken, _lastSessionToken, StringComparison.Ordinal))
            {
                string oldToken = string.IsNullOrWhiteSpace(_lastSessionToken) ? "none" : _lastSessionToken;
                string sessionTypeForLog = string.IsNullOrWhiteSpace(currentSessionTypeForConfidence) ? "unknown" : currentSessionTypeForConfidence;
                _currentSessionToken = currentSessionToken;
                SimHub.Logging.Current.Info($"[LalaPlugin:Session] token change old={oldToken} new={currentSessionToken} type={sessionTypeForLog}");

                SimHub.Logging.Current.Info(
                    $"[LalaPlugin:Pit Cycle] SessionChange: skipPitLossSave=true " +
                    $"pitLiteStatus={_pitLite?.Status} " +
                    $"candidateReady={_pitLite?.CandidateReady ?? false} " +
                    $"lastDirect={_pit?.LastDirectTravelTime:F2} " +
                    $"oldToken={oldToken} newToken={currentSessionToken}");

                // If we exited lane and the session ended before S/F, finalize once with PitLite’s one-shot.
                if (_pitLite != null && _pitLite.ConsumeCandidate(out var scLoss, out var scSrc))
                {
                    Pit_OnValidPitStopTimeLossCalculated(scLoss, scSrc);
                    // nothing else: ConsumeCandidate cleared the latch, sink de-dupe will ignore repeats
                }

                _rejoinEngine.Reset();
                _pit.Reset();
                _pitLite?.ResetCycle();
                _pit?.ResetPitPhaseState();
                _opponentsEngine?.Reset();
                _carSaEngine?.Reset();
                _radioFrequencyNameCache.Reset();
                ResetTransmitState();
                ResetCarSaIdentityState();
                ResetCarSaLapTimeUpdateState();
                ResetCarSaDebugExportState();
                ResetOffTrackDebugExportState();
                ResetPlayerLapInvalidState();
                _currentCarModel = string.Empty;
                CurrentTrackName = string.Empty;
                CurrentTrackKey = string.Empty;
                _lastSeenCar = string.Empty;
                _lastSeenTrack = string.Empty;
                _lastSnapshotCar = string.Empty;
                _lastSnapshotTrack = string.Empty;
                _lastAnnouncedMaxFuel = -1;
                _lastSessionId = currentSessionId;
                _lastSubSessionId = currentSubSessionId;
                _lastSessionToken = currentSessionToken;
                _summaryPitStopIndex = 0;
                _lastValidLapMs = 0;
                _lastValidLapNumber = -1;
                _wetFuelPersistLogged = false;
                _dryFuelPersistLogged = false;
                _msgV1InfoLogged = false;
                _lastIsWetTyres = null;
                _isWetMode = false;
                _carSaBestLapFallbackInfoLogged = false;
                FuelCalculator.ForceProfileDataReload();
                ResetLiveFuelModelForNewSession(currentSessionTypeForConfidence, false);
                ClearFuelInstructionOutputs();
                ResetFinishTimingState();
                ResetSmoothedOutputs();
                _pendingSmoothingReset = true;
                _msgV1Engine?.ResetSession();
                _trackMarkerCapturedPulse.Reset();
                _trackMarkerLengthDeltaPulse.Reset();
                _trackMarkerLockedMismatchPulse.Reset();
                ResetPitScreenToAuto("session-change");

                SimHub.Logging.Current.Info($"[LalaPlugin:Profile] Session start snapshot: Car='{CurrentCarModel}'  Track='{CurrentTrackName}'");
            }

            UpdateLiveSurfaceSummary(pluginManager);
            if (!_msgV1InfoLogged && _msgV1Engine != null)
            {
                SimHub.Logging.Current.Info("[LalaPlugin:MSGV1] Session active (logs suppressed; set DEBUG to view details)");
                _msgV1InfoLogged = true;
            }

            // --- Pit Entry Assist config from profile (per-car) ---
            if (_pit != null && ActiveProfile != null)
            {
                _pit.ConfigPitEntryDecelMps2 = ActiveProfile.PitEntryDecelMps2;
                _pit.ConfigPitEntryBufferM = ActiveProfile.PitEntryBufferM;
            }

            // --- Pit System Monitoring (needs tick granularity for phase detection) ---
            UpdatePitScreenState(pluginManager);
            _pit.Update(data, pluginManager, _pitScreenActive);
            ProcessTrackMarkerTriggers();
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

            bool isInPitStall = Convert.ToBoolean(
                pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PlayerCarInPitStall") ?? false);
            double speedKph = data.NewData?.SpeedKmh ?? 0.0;
            _pitLite?.Update(inLane, completedLaps, lastLapSec, avgUsed, isInPitStall, speedKph);

            bool pitEntryEdge = false;
            bool pitExitEdge = false;
            if (_pitLite != null)
            {
                pitEntryEdge = _pitLite.EntrySeenThisLap && !_pitExitEntrySeenLast;
                pitExitEdge = _pitLite.ExitSeenThisLap && !_pitExitExitSeenLast;
                _pitExitEntrySeenLast = _pitLite.EntrySeenThisLap;
                _pitExitExitSeenLast = _pitLite.ExitSeenThisLap;
            }

            // Per-tick pit-exit display values (only while in pit lane)
            if (inLane)
            {
                UpdatePitExitDisplayValues(data, true);
            }
            else
            {
                // Clear once when not in pit lane
                UpdatePitExitDisplayValues(data, false);
            }

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

            double myPaceSec = Pace_StintAvgLapTimeSec;
            if (myPaceSec <= 0.0) myPaceSec = Pace_Last5LapAvgSec;
            if (myPaceSec <= 0.0 && _lastSeenBestLap > TimeSpan.Zero) myPaceSec = _lastSeenBestLap.TotalSeconds;

            double pitLossSec = CalculateTotalStopLossSeconds();
            if (double.IsNaN(pitLossSec) || double.IsInfinity(pitLossSec) || pitLossSec < 0.0)
            {
                try
                {
                    double fromExport = Convert.ToDouble(pluginManager.GetPropertyValue("LalaLaunch.Fuel.Live.TotalStopLoss") ?? double.NaN);
                    if (!double.IsNaN(fromExport) && !double.IsInfinity(fromExport))
                    {
                        pitLossSec = fromExport;
                    }
                }
                catch
                {
                    pitLossSec = double.NaN;
                }
            }

            if (double.IsNaN(pitLossSec) || double.IsInfinity(pitLossSec) || pitLossSec < 0.0)
            {
                pitLossSec = FuelCalculator?.PitLaneTimeLoss ?? 0.0;
            }

            if (double.IsNaN(pitLossSec) || double.IsInfinity(pitLossSec) || pitLossSec < 0.0) pitLossSec = 0.0;

            string sessionTypeForOpponents = !string.IsNullOrWhiteSpace(currentSessionTypeForConfidence)
                ? currentSessionTypeForConfidence
                : (data.NewData?.SessionTypeName ?? string.Empty);
            bool isRaceSessionNow = IsRaceSession(sessionTypeForOpponents);
            bool pitExitRecently = (DateTime.UtcNow - _lastPitLaneSeenUtc).TotalSeconds < 1.0;
            bool pitTripActive = _wasInPitThisLap || inLane || pitExitRecently;
            double trackPct = SafeReadDouble(pluginManager, "IRacingExtraProperties.iRacing_Player_LapDistPct", double.NaN);
            double sessionTimeSec = ResolveShiftAssistSessionTimeSec(pluginManager);
            double sessionTimeRemainingSec = SafeReadDouble(pluginManager, "DataCorePlugin.GameRawData.Telemetry.SessionTimeRemain", double.NaN);
            int sessionState = SafeReadInt(pluginManager, "DataCorePlugin.GameRawData.Telemetry.SessionState", 0);
            string sessionTypeName = !string.IsNullOrWhiteSpace(currentSessionTypeForConfidence)
                ? currentSessionTypeForConfidence
                : (data.NewData?.SessionTypeName ?? string.Empty);
            bool debugMaster = IsDebugOnForLogic;
            bool verboseLogs = IsVerboseDebugLoggingOn;
            _opponentsEngine?.Update(data, pluginManager, isRaceSessionNow, completedLaps, myPaceSec, pitLossSec, pitTripActive, inLane, trackPct, sessionTimeSec, sessionTimeRemainingSec, verboseLogs);

            int playerCarIdx = SafeReadInt(pluginManager, "DataCorePlugin.GameRawData.Telemetry.PlayerCarIdx", -1);
            float[] carIdxLapDistPct = SafeReadFloatArray(pluginManager, "DataCorePlugin.GameRawData.Telemetry.CarIdxLapDistPct");
            int[] carIdxLap = SafeReadIntArray(pluginManager, "DataCorePlugin.GameRawData.Telemetry.CarIdxLap");
            int[] carIdxTrackSurface = SafeReadIntArray(pluginManager, "DataCorePlugin.GameRawData.Telemetry.CarIdxTrackSurface");
            int[] carIdxTrackSurfaceMaterial = SafeReadIntArray(pluginManager, "DataCorePlugin.GameRawData.Telemetry.CarIdxTrackSurfaceMaterial");
            bool[] carIdxOnPitRoad = SafeReadBoolArray(pluginManager, "DataCorePlugin.GameRawData.Telemetry.CarIdxOnPitRoad");
            UpdatePlayerLapInvalidState(pluginManager, sessionTimeSec, playerCarIdx, carIdxLap);
            int[] carIdxSessionFlags = null;
            _ = TryReadTelemetryIntArray(pluginManager, "CarIdxSessionFlags", out carIdxSessionFlags, out _, out _);
            double lapTimeEstimateSec = myPaceSec;
            if (!IsValidCarSaLapTimeSec(lapTimeEstimateSec))
            {
                lapTimeEstimateSec = lastLapSec;
            }
            if (!IsValidCarSaLapTimeSec(lapTimeEstimateSec))
            {
                lapTimeEstimateSec = double.NaN;
            }
            double playerBestLapTimeSec = _lastSeenBestLap > TimeSpan.Zero
                ? _lastSeenBestLap.TotalSeconds
                : double.NaN;
            if (!IsValidCarSaLapTimeSec(playerBestLapTimeSec) && playerCarIdx >= 0 && playerCarIdx < _carSaBestLapTimeSecByIdx.Length)
            {
                playerBestLapTimeSec = _carSaBestLapTimeSecByIdx[playerCarIdx];
            }
            playerBestLapTimeSec = SanitizeCarSaLapTimeSec(playerBestLapTimeSec);
            double playerLastLapTimeSec = SanitizeCarSaLapTimeSec(lastLapSec);
            double classEstLapTimeSec = (playerCarIdx >= 0 && playerCarIdx < _carSaCarClassEstLapTimeSecByIdx.Length)
                ? _carSaCarClassEstLapTimeSecByIdx[playerCarIdx]
                : double.NaN;
            double notRelevantGapSec = Settings?.NotRelevantGapSec ?? CarSANotRelevantGapSecDefault;
            if (double.IsNaN(notRelevantGapSec) || double.IsInfinity(notRelevantGapSec) || notRelevantGapSec < 0.0)
            {
                notRelevantGapSec = CarSANotRelevantGapSecDefault;
            }
            bool hasMultipleClassOpponents = false;
            try
            {
                hasMultipleClassOpponents = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameData.HasMultipleClassOpponents") ?? false);
            }
            catch
            {
                hasMultipleClassOpponents = false;
            }
            _carSaEngine?.Update(sessionTimeSec, sessionState, sessionTypeName, playerCarIdx, hasMultipleClassOpponents, carIdxLapDistPct, carIdxLap, carIdxTrackSurface, carIdxTrackSurfaceMaterial, carIdxOnPitRoad, carIdxSessionFlags, null, playerBestLapTimeSec, playerLastLapTimeSec, lapTimeEstimateSec, classEstLapTimeSec, notRelevantGapSec, debugMaster);
            if (_carSaEngine != null)
            {
                UpdateCarSaTelemetryCaches(pluginManager, sessionTimeSec);
                UpdateCarSaTransmitState(pluginManager, _carSaEngine.Outputs);
                bool carSaDebugExportEnabled = debugMaster && Settings?.EnableCarSADebugExport == true;
                if (carSaDebugExportEnabled)
                {
                    for (int i = 0; i < CarSaDebugExportSlotCount; i++)
                    {
                        _carSaDebugAheadDahlRelativeGapSec[i] = SafeReadDouble(pluginManager, CarSaDebugAheadDahlProperties[i], double.NaN);
                        _carSaDebugBehindDahlRelativeGapSec[i] = SafeReadDouble(pluginManager, CarSaDebugBehindDahlProperties[i], double.NaN);
                        _carSaDebugAheadIRacingRelativeGapSec[i] = SafeReadDouble(pluginManager, CarSaDebugAheadIRacingProperties[i], double.NaN);
                        _carSaDebugBehindIRacingRelativeGapSec[i] = SafeReadDouble(pluginManager, CarSaDebugBehindIRacingProperties[i], double.NaN);
                    }

                    UpdateCarSaDebugCadenceState(trackPct, _carSaEngine.Outputs.Debug);
                }

                if (debugMaster)
                {
                    UpdateCarSaRawTelemetryDebug(pluginManager, _carSaEngine.Outputs, playerCarIdx, verboseLogs);
                }

                int probeCarIdx = Settings?.OffTrackDebugProbeCarIdx ?? -1;
                bool offTrackDebugEnabled = Settings?.EnableOffTrackDebugCsv == true && probeCarIdx >= 0;
                int sessionFlagsRaw = -1;
                if (offTrackDebugEnabled)
                {
                    sessionFlagsRaw = SafeReadInt(pluginManager, "DataCorePlugin.GameRawData.Telemetry.SessionFlags", -1);
                }
                WriteOffTrackDebugExport(
                    pluginManager,
                    sessionTimeSec,
                    sessionState,
                    sessionFlagsRaw,
                    probeCarIdx,
                    playerCarIdx,
                    _playerIncidentCount,
                    _playerIncidentDelta,
                    carIdxTrackSurface,
                    carIdxTrackSurfaceMaterial,
                    carIdxSessionFlags,
                    carIdxOnPitRoad,
                    carIdxLap,
                    carIdxLapDistPct);

                WriteCarSaDebugExport(pluginManager, _carSaEngine.Outputs, sessionState, sessionTypeName, debugMaster);
                RefreshCarSaSlotIdentities(pluginManager, sessionTimeSec);
                UpdateCarSaSlotTelemetry(pluginManager, _carSaEngine.Outputs.AheadSlots, carIdxLapDistPct, sessionTimeSec);
                UpdateCarSaSlotTelemetry(pluginManager, _carSaEngine.Outputs.BehindSlots, carIdxLapDistPct, sessionTimeSec);
                UpdateCarSaPlayerTelemetry(pluginManager, playerCarIdx, sessionTimeSec);
                if (_friendsDirty)
                {
                    RefreshFriendUserIds();
                    _friendsDirty = false;
                }
                UpdateCarSaFriendFlags(_carSaEngine.Outputs.AheadSlots);
                UpdateCarSaFriendFlags(_carSaEngine.Outputs.BehindSlots);
                UpdateCarSaPlayerFriendFlag();
                string playerClassColor = GetCarClassColorHex(pluginManager, "IRacingExtraProperties.iRacing_Player_ClassColor");
                if (string.IsNullOrWhiteSpace(playerClassColor) && playerCarIdx >= 0)
                {
                    if (TryGetCarIdentityFromSessionInfo(pluginManager, playerCarIdx, out _, out _, out var fallbackClassColor))
                    {
                        if (!string.IsNullOrWhiteSpace(fallbackClassColor))
                        {
                            playerClassColor = fallbackClassColor;
                        }
                    }
                }
                if (string.IsNullOrWhiteSpace(playerClassColor))
                {
                    playerClassColor = _carSaEngine.Outputs.PlayerSlot.ClassColor;
                }
                UpdateCarSaClassRankMap(pluginManager);
                _carSaEngine.SetClassRankMap(_carSaClassRankByColor);
                _carSaEngine.RefreshStatusE(notRelevantGapSec, _opponentsEngine?.Outputs, playerClassColor);
                UpdateCarSaSlotStyles(_carSaEngine.Outputs.AheadSlots, playerClassColor ?? string.Empty);
                UpdateCarSaSlotStyles(_carSaEngine.Outputs.BehindSlots, playerClassColor ?? string.Empty);
            }

            if (pitEntryEdge)
            {
                LogPitExitPitInSnapshot(sessionTime, completedLaps + 1, pitLossSec);
            }

            if (pitExitEdge)
            {
                _opponentsEngine?.NotifyPitExitLine(completedLaps, sessionTime, trackPct);
                // LogPitExitPitOutSnapshot(sessionTime, completedLaps + 1, pitTripActive);
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

                    if (IsVerboseDebugLoggingOn)
                    {
                        SimHub.Logging.Current.Debug($"[LalaPlugin:Refuel] Refuel started at {_refuelStartTime:F1}s (fuel {_refuelStartFuel:F1}L).");
                    }
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

                    if (fuelAdded > 0.0)
                    {
                        SessionSummaryRuntime.OnFuelAdded(_currentSessionToken, fuelAdded);
                    }

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
                                $"[LalaPlugin:Refuel Rate] Learned refuel rate {savedRate:F2} L/s (raw {rate:F2} L/s, added {fuelAdded:F1} L over {duration:F1} s). " +
                                $"Cooldown until {_refuelLearnCooldownEnd:F1} s.");
                        }

                    }

                    if (IsVerboseDebugLoggingOn)
                    {
                        SimHub.Logging.Current.Debug($"[LalaPlugin:Refuel] Refuel ended at {stopTime:F1} s.");
                    }

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

                    // PitStopIndex is 1-based: increment once per completed pit cycle (first stop => 1).
                    _summaryPitStopIndex++;

                    Pit_OnValidPitStopTimeLossCalculated(lossSec, src);
                    _lastSavedLap = completedLaps;
                }
            }

            int laps = Convert.ToInt32(data.NewData?.CompletedLaps ?? 0);

            // --- 250ms group: things safe to refresh at ~4 Hz ---
            if (_poll250ms.ElapsedMilliseconds >= 250)
            {
                _poll250ms.Restart();
                UpdateLiveMaxFuel(pluginManager);
                _msgSystem.Enabled = Settings.MsgDashShowTraffic || Settings.LalaDashShowTraffic || Settings.OverlayDashShowTraffic;
                double warn = ActiveProfile.TrafficApproachWarnSeconds;
                if (!(warn > 0)) warn = 5.0;
                _msgSystem.WarnSeconds = warn;
                if (_msgSystem.Enabled)
                    _msgSystem.Update(data, pluginManager);
                else
                    _msgSystem.MaintainMsgCxTimers();

                _msgV1Engine?.Tick(data);
            }

            // --- Launch State helpers (need tick-level responsiveness) ---
            bool launchBlocked = IsLaunchBlocked(pluginManager, data, out var inPitsBlocked, out var seriousRejoinBlocked);
            if (launchBlocked && !IsIdle && !_launchAbortLatched)
            {
                CancelLaunchToIdle("Blocked (pits/serious)");
            }
            else if (!launchBlocked)
            {
                _launchAbortLatched = false;
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

                    int lapMs = (int)Math.Round(currentBestLap.TotalMilliseconds);
                    int completedLapsNow = Convert.ToInt32(data.NewData?.CompletedLaps ?? 0);
                    bool lapValidForPb = _lastValidLapNumber == completedLapsNow && Math.Abs(_lastValidLapMs - lapMs) <= 2;

                    bool accepted = false;
                    if (lapValidForPb)
                    {
                        accepted = ProfilesViewModel.TryUpdatePBByCondition(CurrentCarModel, CurrentTrackKey, lapMs, _isWetMode);
                        string pbLog = $"[LalaPlugin:Pace] candidate={lapMs}ms car='{CurrentCarModel}' trackKey='{CurrentTrackKey}' -> {(accepted ? "accepted" : "rejected")}";
                        if (accepted)
                            SimHub.Logging.Current.Info(pbLog);
                        else
                            if (IsVerboseDebugLoggingOn)
                            {
                                SimHub.Logging.Current.Debug(pbLog);
                            }
                    }

                    var activeTrackStats = ActiveProfile?.ResolveTrackByNameOrKey(CurrentTrackKey)
                        ?? ActiveProfile?.ResolveTrackByNameOrKey(CurrentTrackName);
                    int? selectedPbMs = activeTrackStats?.GetBestLapMsForCondition(_isWetMode);
                    double selectedPbSeconds = selectedPbMs.HasValue ? selectedPbMs.Value / 1000.0 : 0.0;
                    FuelCalculator?.SetPersonalBestSeconds(selectedPbSeconds);
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
                    SimHub.Logging.Current.Info($"[LalaPlugin:Profile] New live combo detected. Auto-selecting profile for Car='{CurrentCarModel}', Track='{trackIdentity}'.");

                    // Store this combo's KEY so we don't trigger again for the same session.
                    _lastSeenCar = CurrentCarModel;
                    _lastSeenTrack = trackIdentity; // track key preferred, fall back to display name
                    ResetSmoothedOutputs();
                    _pendingSmoothingReset = true;
                    ResetPitScreenToAuto("combo-change");

                    // Dispatch UI updates to the main thread.
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var profileToLoad = ProfilesViewModel.GetProfileForCar(CurrentCarModel) ?? ProfilesViewModel.EnsureCar(CurrentCarModel);
                        this.ActiveProfile = profileToLoad;

                        // Ensure the track exists via the Profiles VM (this triggers UI refresh + selection)
                        if (!string.IsNullOrWhiteSpace(CurrentTrackKey))
                        {
                            if (IsVerboseDebugLoggingOn)
                            {
                                SimHub.Logging.Current.Debug($"[LalaPlugin:Profiles] Ensure car and track: car='{CurrentCarModel}', trackKey='{CurrentTrackKey}'");
                            }

                            ProfilesViewModel.EnsureCarTrack(CurrentCarModel, CurrentTrackKey);
                        }
                        else
                        {
                            if (IsVerboseDebugLoggingOn)
                            {
                                SimHub.Logging.Current.Debug($"[LalaPlugin:Profile] EnsureCarTrack fallback -> car='{CurrentCarModel}', trackName='{trackIdentity}'");
                            }
                            ProfilesViewModel.EnsureCarTrack(CurrentCarModel, trackIdentity);
                        }

                        string trackNameForSnapshot = !string.IsNullOrWhiteSpace(CurrentTrackName)
                            ? CurrentTrackName
                            : trackIdentity;
                        FuelCalculator?.SetLiveSession(CurrentCarModel, trackNameForSnapshot);
                    });

                }

                UpdateOpponentsAndPitExit(data, pluginManager, completedLaps, currentSessionTypeForConfidence);
            }

            UpdateLiveProperties(pluginManager, ref data);
            HandleLaunchState(pluginManager, ref data);
            EvaluateShiftAssist(pluginManager, data);

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

            string currentSession = NormalizeSessionTypeName(Convert.ToString(
                pluginManager.GetPropertyValue("DataCorePlugin.GameData.SessionTypeName") ?? ""));

            // Session summary: handle startup already-in-race case.
            // The session-change block below won't fire on first tick because _lastFuelSessionType is empty.
            if (string.IsNullOrEmpty(_lastFuelSessionType) &&
                IsRaceSession(currentSession))
            {
                SessionSummaryRuntime.OnRaceSessionStart(
                    _currentSessionToken,
                    currentSession,
                    CurrentCarModel,
                    CurrentTrackKey,
                    CurrentTrackName,
                    FuelCalculator?.SelectedPreset?.Name ?? string.Empty,
                    FuelCalculator,
                    Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameData.IsReplay") ?? false),
                    data.NewData?.Fuel ?? 0.0,
                    sessionTime);
            }


            // Fuel model session-change handling (independent of auto-dash setting)
            if (!string.IsNullOrEmpty(_lastFuelSessionType) && !string.Equals(currentSession, _lastFuelSessionType, StringComparison.OrdinalIgnoreCase))
            {
                // First: let the fuel model handle phase transitions (including seed carry-over rules)
                HandleSessionChangeForFuelModel(_lastFuelSessionType, currentSession);

                // Phase boundary resets (SessionToken stays constant across P/Q/R for same event)
                ResetFinishTimingState();
                ResetSmoothedOutputs();
                ClearFuelInstructionOutputs();
                ResetCarSaLapTimeUpdateState();

                // Message session state should not bleed across phase transitions
                _msgV1Engine?.ResetSession();

                if (IsRaceSession(currentSession))
                {
                    // NOTE: snapshot currently latched at Race session entry; will move to true green latch later
                    SessionSummaryRuntime.OnRaceSessionStart(
                        _currentSessionToken,
                        currentSession,
                        CurrentCarModel,
                        CurrentTrackKey,
                        CurrentTrackName,
                        FuelCalculator?.SelectedPreset?.Name ?? string.Empty,
                        FuelCalculator,
                        Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameData.IsReplay") ?? false),
                        data.NewData?.Fuel ?? 0.0,
                        sessionTime);
                }
            }

            _lastFuelSessionType = NormalizeSessionTypeName(currentSession);

            // --- AUTO DASH SWITCHING (READINESS-GATED, NO GLOBAL RESET) ---
            bool ignitionOn = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameData.EngineIgnitionOn") ?? false);
            bool engineStarted = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameData.EngineStarted") ?? false);

            if (Settings.EnableAutoDashSwitch && !string.IsNullOrWhiteSpace(currentSession) && !string.Equals(currentSession, _dashLastSessionType, StringComparison.OrdinalIgnoreCase))
            {
                _dashLastSessionType = NormalizeSessionTypeName(currentSession);
                _dashPendingSwitch = true;
                _dashExecutedForCurrentArm = false;
                _dashSwitchToken++;
                _lastSessionType = NormalizeSessionTypeName(currentSession);

                _dashDesiredPage = GetDashDesiredPageForSession(currentSession);
            }

            if (!ignitionOn && _dashLastIgnitionOn)
            {
                _dashPendingSwitch = true;
                _dashExecutedForCurrentArm = false;
                _dashSwitchToken++;
                SimHub.Logging.Current.Info("[LalaPlugin:Dash] Ignition off detected – auto dash re-armed.");
            }
            _dashLastIgnitionOn = ignitionOn;

            if (Settings.EnableAutoDashSwitch && _dashPendingSwitch && !_dashExecutedForCurrentArm && (ignitionOn || engineStarted))
            {
                _dashExecutedForCurrentArm = true;
                int token = _dashSwitchToken;
                string pageToShow = _dashDesiredPage;
                string sessionForLog = _dashLastSessionType;

                Task.Run(async () =>
                {
                    Screens.Mode = "auto";
                    Screens.CurrentPage = pageToShow;
                    _dashPendingSwitch = false;
                    SimHub.Logging.Current.Info($"[LalaPlugin:Dash] Auto dash executed for session '{sessionForLog}' – mode=auto, page='{Screens.CurrentPage}'.");
                    await Task.Delay(750);
                    if (token == _dashSwitchToken && Settings.EnableAutoDashSwitch)
                    {
                        Screens.Mode = "manual";
                        SimHub.Logging.Current.Info("[LalaPlugin:Dash] Auto dash timer expired – mode set to 'manual'.");
                    }
                });
            }
            bool isOnPitRoad = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.OnPitRoad") ?? false);
            bool pitRoadChanged = isOnPitRoad != _lastOnPitRoadForOpponents;
            _lastOnPitRoadForOpponents = isOnPitRoad;

            if (pitRoadChanged)
            {
                UpdateOpponentsAndPitExit(data, pluginManager, completedLaps, currentSessionTypeForConfidence);
            }

            // --- Decel capture instrumentation (toggle = pit screen active) ---
            {
                bool captureOn = _pitScreenActive;

                // Throttle: your codebase treats it like 0..100, so normalise to 0..1
                double throttleRaw = data.NewData?.Throttle ?? 0.0;
                double throttle01 = throttleRaw > 1.5 ? (throttleRaw / 100.0) : throttleRaw;

                // Brake: confirmed in SimHub property tab
                double brakeRaw = SafeReadDouble(pluginManager, "DataCorePlugin.GameData.Brake", 0.0);
                double brake01 = brakeRaw > 1.5 ? (brakeRaw / 100.0) : brakeRaw;

                // LongAccel: confirmed in SimHub property tab (m/s^2 in most setups)
                double longAccel = SafeReadDouble(pluginManager, "DataCorePlugin.GameRawData.Telemetry.LongAccel", 0.0);

                // Lateral G: optional; if you have a known property for it, put it here.
                // If not, pass 0 and the straight-line filter becomes inactive.
                double latG = 0.0;

                _decelCapture.Update(
                    captureToggleOn: captureOn,
                    speedKph: speedKph,
                    brakePct01: brake01,
                    throttlePct01: throttle01,
                    lonAccel_mps2: Math.Abs(longAccel), // make it positive magnitude
                    latG: latG,
                    carNameOrClass: string.IsNullOrWhiteSpace(CurrentCarModel) ? "na" : CurrentCarModel,
                    trackName: string.IsNullOrWhiteSpace(CurrentTrackName) ? "na" : CurrentTrackName,
                    sessionToken: string.IsNullOrWhiteSpace(_currentSessionToken) ? "na" : _currentSessionToken
                );
            }


        }

        private void TriggerShiftAssistTestBeep()
        {
            var settings = Settings;
            bool soundEnabled = settings?.ShiftAssistBeepSoundEnabled != false;
            bool volumeEnabled = (settings?.ShiftAssistBeepVolumePct ?? 100) > 0;
            if (!soundEnabled || !volumeEnabled)
            {
                _shiftAssistAudio?.HardStop();
                return;
            }

            int durationMs = GetShiftAssistBeepDurationMs();
            DateTime nowUtc = DateTime.UtcNow;
            _shiftAssistBeepUntilUtc = nowUtc.AddMilliseconds(durationMs);
            _shiftAssistBeepLatched = true;
            DateTime issuedUtc = DateTime.MinValue;
            if (_shiftAssistAudio != null)
            {
                _shiftAssistAudio.TryPlayBeep(out issuedUtc);
            }

            if (IsVerboseDebugLoggingOn)
            {
                SimHub.Logging.Current.Info($"[LalaPlugin:ShiftAssist] Test beep triggered (duration={durationMs}ms)");
            }
        }

        private void EvaluateShiftAssist(PluginManager pluginManager, GameData data)
        {
            var settings = Settings;
            bool beepSoundEnabled = settings?.ShiftAssistBeepSoundEnabled != false;
            bool beepVolumeEnabled = (settings?.ShiftAssistBeepVolumePct ?? 100) > 0;
            if (!beepSoundEnabled || !beepVolumeEnabled)
            {
                _shiftAssistAudio?.HardStop();
            }

            bool enabled = settings?.ShiftAssistEnabled == true;
            if (!enabled)
            {
                _shiftAssistAudio?.HardStop();
                _shiftAssistTargetCurrentGear = 0;
                _shiftAssistActiveGearStackId = "Default";
                _shiftAssistBeepUntilUtc = DateTime.MinValue;
                _shiftAssistBeepLatched = false;
                _shiftAssistAudioDelayMs = 0;
                _shiftAssistAudioDelayLastIssuedUtc = DateTime.MinValue;
                _shiftAssistAudioIssuedPulse = false;
                _shiftAssistLastGear = 0;
                _shiftAssistLastValidGear = 0;
                _shiftAssistLastValidGearUtc = DateTime.MinValue;
                _shiftAssistLastSpeedMps = double.NaN;
                _shiftAssistLastSpeedSampleUtc = DateTime.MinValue;
                _shiftAssistEngine.Reset();
                _shiftAssistLastLearningTick = _shiftAssistLearningEngine.Update(false, _shiftAssistActiveGearStackId, 0, 0, 0.0, 0.0, double.NaN, 0.0);
                _shiftAssistLearnSavedPulseUntilUtc = DateTime.MinValue;
                ClearShiftAssistDelayPending();
                ResetShiftAssistDebugCsvState();
                if (_shiftAssistLastEnabled)
                {
                    SimHub.Logging.Current.Info("[LalaPlugin:ShiftAssist] Enabled=false");
                    _shiftAssistLastEnabled = false;
                }
                return;
            }

            if (!_shiftAssistLastEnabled)
            {
                SimHub.Logging.Current.Info("[LalaPlugin:ShiftAssist] Enabled=true");
                _shiftAssistLastEnabled = true;
            }

            DateTime nowUtc = DateTime.UtcNow;
            _shiftAssistBeepLatched = nowUtc <= _shiftAssistBeepUntilUtc;
            _shiftAssistAudioIssuedPulse = false;
            _shiftAssistLastCapturedDelayMs = 0;
            _shiftAssistDelayCaptureEvent = "NONE";
            _shiftAssistDelayBeepType = "NONE";

            int gear;
            if (!TryReadNullableInt(pluginManager, "DataCorePlugin.GameRawData.Telemetry.Gear", out gear))
            {
                gear = 0;
                string gearRaw = data.NewData?.Gear;
                if (!string.IsNullOrWhiteSpace(gearRaw))
                {
                    gearRaw = gearRaw.Trim();

                    int parsedGear;
                    if (int.TryParse(gearRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedGear))
                    {
                        gear = parsedGear;
                    }
                    else if (string.Equals(gearRaw, "N", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(gearRaw, "R", StringComparison.OrdinalIgnoreCase))
                    {
                        gear = 0;
                    }
                }
            }

            gear = NormalizeShiftAssistGear(gear);
            if (gear >= 1)
            {
                _shiftAssistLastValidGear = gear;
                _shiftAssistLastValidGearUtc = nowUtc;
            }

            int effectiveGear = gear;
            if (gear == 0 && _shiftAssistLastValidGear >= 1 && _shiftAssistLastValidGearUtc != DateTime.MinValue)
            {
                if ((nowUtc - _shiftAssistLastValidGearUtc).TotalMilliseconds <= ShiftAssistGearZeroHoldMs)
                {
                    effectiveGear = _shiftAssistLastValidGear;
                }
            }

            _shiftAssistLastGear = effectiveGear;

            int rpm = (int)Math.Round(data.NewData?.Rpms ?? 0.0);
            double throttleRaw = data.NewData?.Throttle ?? 0.0;
            double throttle01 = throttleRaw > 1.5 ? (throttleRaw / 100.0) : throttleRaw;
            double brakeRaw = SafeReadDouble(pluginManager, "DataCorePlugin.GameData.Brake", 0.0);
            double brake01 = brakeRaw > 1.5 ? (brakeRaw / 100.0) : brakeRaw;

            if (_shiftAssistPendingDelayActive)
            {
                long nowTs = Stopwatch.GetTimestamp();
                if (brake01 > 0.10)
                {
                    _shiftAssistDelayCaptureEvent = "CANCEL_BRAKE";
                    _shiftAssistDelayCaptureState = 3;
                    LatchShiftAssistDelayDiagnostics("CANCEL_BRAKE", _shiftAssistDelayBeepType, 0, nowUtc);
                    ClearShiftAssistDelayPending();
                }
                else if (effectiveGear == (_shiftAssistPendingDelayGear + 1))
                {
                    int delayMs = GetElapsedMsFromTimestamp(_shiftAssistPendingDelayStartTs, nowTs);
                    if (delayMs > 0)
                    {
                        _shiftAssistLastCapturedDelayMs = delayMs;
                        _shiftAssistDelayCaptureEvent = "CAPTURE";
                        _shiftAssistDelayCaptureState = 2;
                        LatchShiftAssistDelayDiagnostics("CAPTURE", _shiftAssistDelayBeepType, delayMs, nowUtc);
                        AddShiftAssistDelaySample(_shiftAssistPendingDelayGear, delayMs);
                        ProfilesViewModel?.RefreshShiftAssistRuntimeStats();
                        if (IsVerboseDebugLoggingOn)
                        {
                            int avgDelay = GetShiftAssistDelayAverageMs(_shiftAssistPendingDelayGear);
                            SimHub.Logging.Current.Info($"[LalaPlugin:ShiftAssist] Delay sample captured gear={_shiftAssistPendingDelayGear} delayMs={delayMs} avgMs={avgDelay}");
                        }
                    }

                    ClearShiftAssistDelayPending();
                }
                else
                {
                    if (effectiveGear != 0 && effectiveGear < _shiftAssistPendingDelayGear)
                    {
                        if (_shiftAssistPendingDelayDownshiftSinceTs == 0)
                        {
                            _shiftAssistPendingDelayDownshiftSinceTs = nowTs;
                        }
                        else if (GetShiftAssistPendingDownshiftAgeMs(nowTs) >= ShiftAssistDelayDownshiftGraceMs)
                        {
                            _shiftAssistDelayCaptureEvent = "CANCEL_DOWN";
                            _shiftAssistDelayCaptureState = 4;
                            LatchShiftAssistDelayDiagnostics("CANCEL_DOWN", _shiftAssistDelayBeepType, 0, nowUtc);
                            ClearShiftAssistDelayPending();
                        }
                    }
                    else
                    {
                        _shiftAssistPendingDelayDownshiftSinceTs = 0;
                    }

                    if (_shiftAssistPendingDelayActive && GetShiftAssistPendingAgeMs(nowTs) > ShiftAssistDelayPendingTimeoutMs)
                    {
                        _shiftAssistDelayCaptureEvent = "CANCEL_TIMEOUT";
                        _shiftAssistDelayCaptureState = 5;
                        LatchShiftAssistDelayDiagnostics("CANCEL_TIMEOUT", _shiftAssistDelayBeepType, 0, nowUtc);
                        ClearShiftAssistDelayPending();
                    }
                }
            }
            double sessionTimeSec = ResolveShiftAssistSessionTimeSec(pluginManager);

            double speedMps = ResolveShiftAssistSpeedMps(pluginManager, data);
            double accelDerivedMps2 = 0.0;
            if (_shiftAssistLastSpeedSampleUtc != DateTime.MinValue && !double.IsNaN(_shiftAssistLastSpeedMps))
            {
                double dtSec = (nowUtc - _shiftAssistLastSpeedSampleUtc).TotalSeconds;
                if (dtSec >= 0.02 && dtSec <= 0.20)
                {
                    accelDerivedMps2 = (speedMps - _shiftAssistLastSpeedMps) / dtSec;
                }
            }

            _shiftAssistLastSpeedSampleUtc = nowUtc;
            _shiftAssistLastSpeedMps = speedMps;

            double lonAccelTelemetryMps2 = ResolveShiftAssistLongitudinalAccelMps2(pluginManager);

            int maxForwardGears = ResolveShiftAssistMaxForwardGears(pluginManager);
            LearnShiftAssistMaxForwardGearsHint(maxForwardGears);

            string gearStackId = Convert.ToString(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.CarSetup.Chassis.GearsDifferential.GearStack") ?? "");
            if (string.IsNullOrWhiteSpace(gearStackId))
            {
                gearStackId = "Default";
            }
            else
            {
                gearStackId = gearStackId.Trim();
            }

            _shiftAssistActiveGearStackId = gearStackId;
            double learningLonAccelMps2 = (!double.IsNaN(lonAccelTelemetryMps2) && !double.IsInfinity(lonAccelTelemetryMps2) && Math.Abs(lonAccelTelemetryMps2) >= 0.05)
                ? lonAccelTelemetryMps2
                : accelDerivedMps2;

            _shiftAssistLastLearningTick = _shiftAssistLearningEngine.Update(
                settings?.ShiftAssistLearningModeEnabled == true,
                gearStackId,
                effectiveGear,
                rpm,
                throttle01,
                brake01,
                sessionTimeSec,
                learningLonAccelMps2);

            if (_shiftAssistLastLearningTick != null &&
                (_shiftAssistLastLearningTick.State == ShiftAssistLearningState.Sampling || _shiftAssistLastLearningTick.State == ShiftAssistLearningState.Complete))
            {
                double peakAccel = _shiftAssistLastLearningTick.PeakAccelMps2;
                int peakRpm = _shiftAssistLastLearningTick.PeakRpm;
                if (!double.IsNaN(peakAccel) && !double.IsInfinity(peakAccel) && peakAccel > 0.0 && peakRpm > 0)
                {
                    _shiftAssistLearnPeakAccelLatched = peakAccel;
                    _shiftAssistLearnPeakRpmLatched = peakRpm;
                }
            }

            if (_shiftAssistLastLearningTick != null && _shiftAssistLastLearningTick.ShouldApplyLearnedRpm && ActiveProfile != null)
            {
                int learnedGear = _shiftAssistLastLearningTick.ApplyGear;
                int learnedRpm = _shiftAssistLastLearningTick.ApplyRpm;
                if (learnedGear >= 1 && learnedGear <= 8 && learnedRpm > 0)
                {
                    var stack = ActiveProfile.EnsureShiftStack(gearStackId);
                    int idx = learnedGear - 1;
                    if (!stack.ShiftLocked[idx] && stack.ShiftRPM[idx] != learnedRpm)
                    {
                        stack.ShiftRPM[idx] = learnedRpm;
                        _shiftAssistLearnSavedPulseUntilUtc = nowUtc.AddMilliseconds(ShiftAssistLearnSavePulseMs);
                        ProfilesViewModel?.SaveProfiles();
                        ProfilesViewModel?.RefreshShiftAssistRuntimeStats();
                    }
                }
            }

            int targetRpm = ActiveProfile?.GetShiftTargetForGear(gearStackId, effectiveGear) ?? 0;

            int redlineRpm = 0;
            if (!TryReadNullableInt(pluginManager, "DataCorePlugin.GameData.CarSettings_CurrentGearRedLineRPM", out redlineRpm) || redlineRpm <= 0)
            {
                TryReadNullableInt(pluginManager, "DataCorePlugin.GameData.CarSettings_RedLineRPM", out redlineRpm);
            }

            if (targetRpm <= 0)
            {
                if (redlineRpm > 0)
                {
                    targetRpm = redlineRpm;
                }
            }
            else if (redlineRpm <= 0)
            {
                redlineRpm = targetRpm;
            }

            if (redlineRpm <= 0)
            {
                redlineRpm = targetRpm;
            }

            bool topGearKnown = maxForwardGears >= 1;
            bool canShiftUp = !topGearKnown || (effectiveGear >= 1 && effectiveGear < maxForwardGears);
            if (!canShiftUp)
            {
                targetRpm = 0;
            }

            _shiftAssistTargetCurrentGear = targetRpm;

            int leadTimeMs = GetShiftAssistLeadTimeMs();
            bool suppressDownBeforeTrigger = _shiftAssistEngine.IsSuppressingDownshift;
            bool suppressUpBeforeTrigger = _shiftAssistEngine.IsSuppressingUpshift;
            bool beep = _shiftAssistEngine.Evaluate(
                effectiveGear,
                rpm,
                throttle01,
                targetRpm,
                ShiftAssistCooldownMsDefault,
                ShiftAssistResetHysteresisRpmDefault,
                leadTimeMs,
                redlineRpm);

            if (beep)
            {
                int durationMs = GetShiftAssistBeepDurationMs();
                _shiftAssistBeepUntilUtc = nowUtc.AddMilliseconds(durationMs);
                _shiftAssistBeepLatched = true;

                DateTime triggerUtc = nowUtc;
                DateTime issuedUtc = DateTime.MinValue;
                bool audioIssued = _shiftAssistAudio != null && _shiftAssistAudio.TryPlayBeep(out issuedUtc);
                if (audioIssued)
                {
                    _shiftAssistAudioIssuedPulse = true;
                    int delayMs = ClampShiftAssistDelayMs((issuedUtc - triggerUtc).TotalMilliseconds, 2000);
                    _shiftAssistAudioDelayMs = delayMs;
                    _shiftAssistAudioDelayLastIssuedUtc = issuedUtc;

                    if (Settings?.EnableShiftAssistDebugCsv == true)
                    {
                        SimHub.Logging.Current.Info($"[LalaPlugin:ShiftAssist] AudioDelayMs={delayMs} backend=SoundPlayer");
                    }
                }

                if (effectiveGear >= 1 && effectiveGear <= 8 && !_shiftAssistEngine.LastBeepWasUrgent)
                {
                    ClearShiftAssistDelayPending();
                    _shiftAssistPendingDelayGear = effectiveGear;
                    _shiftAssistPendingDelayStartTs = Stopwatch.GetTimestamp();
                    _shiftAssistPendingDelayRpmAtCue = rpm;
                    _shiftAssistLastBeepRpmLatched = rpm;
                    _shiftAssistPendingDelayActive = true;
                    _shiftAssistDelayCaptureEvent = "ARM";
                    _shiftAssistDelayCaptureState = 1;
                    _shiftAssistDelayBeepType = "PRIMARY";
                    LatchShiftAssistDelayDiagnostics("ARM", "PRIMARY", 0, nowUtc);
                }

                if (IsVerboseDebugLoggingOn)
                {
                    SimHub.Logging.Current.Info(
                        $"[LalaPlugin:ShiftAssist] Beep type={(_shiftAssistEngine.LastBeepWasUrgent ? "urgent" : "primary")} gear={effectiveGear} rawGear={gear} maxForwardGears={maxForwardGears} target={targetRpm} redline={redlineRpm} effectiveTarget={_shiftAssistEngine.LastEffectiveTargetRpm} rpm={rpm} rpmRate={_shiftAssistEngine.LastRpmRate} leadMs={leadTimeMs} throttle={throttle01:F2} suppressDown={suppressDownBeforeTrigger} suppressUp={suppressUpBeforeTrigger}");
                }
            }

            bool exportedBeepLatched = _shiftAssistBeepLatched;
            WriteShiftAssistDebugCsv(nowUtc, sessionTimeSec, gear, effectiveGear, maxForwardGears, rpm, throttle01, targetRpm, leadTimeMs, beep, exportedBeepLatched, speedMps, accelDerivedMps2, lonAccelTelemetryMps2, _shiftAssistLastLearningTick);
        }

        private int ResolveShiftAssistMaxForwardGears(PluginManager pluginManager)
        {
            int maxForwardGears;
            if (!TryReadNullableInt(pluginManager, "DataCorePlugin.GameData.CarSettings_MaxGears", out maxForwardGears) || maxForwardGears <= 0)
            {
                if (!TryReadNullableInt(pluginManager, "DataCorePlugin.GameRawData.SessionData.DriverInfo.DriverCarGearNumForward", out maxForwardGears) || maxForwardGears <= 0)
                {
                    maxForwardGears = ActiveProfile?.MaxForwardGearsHint ?? 0;
                    if (maxForwardGears <= 0)
                    {
                        return 0;
                    }
                }
            }

            if (maxForwardGears > 8)
            {
                maxForwardGears = 8;
            }

            return maxForwardGears;
        }

        private void LearnShiftAssistMaxForwardGearsHint(int maxForwardGears)
        {
            if (maxForwardGears <= 0 || ActiveProfile == null)
            {
                return;
            }

            if (ActiveProfile.MaxForwardGearsHint == maxForwardGears)
            {
                return;
            }

            ActiveProfile.MaxForwardGearsHint = maxForwardGears;
            ProfilesViewModel?.SaveProfiles();
            ProfilesViewModel?.RefreshShiftAssistRuntimeStats();
        }

        private int GetShiftAssistDebugCsvMaxHz()
        {
            int value = Settings?.ShiftAssistDebugCsvMaxHz ?? ShiftAssistDebugCsvMaxHzDefault;
            if (value <= 0)
            {
                value = ShiftAssistDebugCsvMaxHzDefault;
            }

            if (value < ShiftAssistDebugCsvMaxHzMin)
            {
                value = ShiftAssistDebugCsvMaxHzMin;
            }
            else if (value > ShiftAssistDebugCsvMaxHzMax)
            {
                value = ShiftAssistDebugCsvMaxHzMax;
            }

            return value;
        }

        private double ResolveShiftAssistSessionTimeSec(PluginManager pluginManager)
        {
            // Keep session-time resolution aligned with OffTrack debug CSV path:
            // DataCorePlugin.GameRawData.Telemetry.SessionTime with 0.0 fallback.
            return SafeReadDouble(pluginManager, "DataCorePlugin.GameRawData.Telemetry.SessionTime", 0.0);
        }

        private double ResolveShiftAssistSpeedMps(PluginManager pluginManager, GameData data)
        {
            double speedMps = SafeReadDouble(pluginManager, "DataCorePlugin.GameRawData.Telemetry.Speed", double.NaN);
            if (!double.IsNaN(speedMps) && !double.IsInfinity(speedMps) && speedMps >= 0.0)
            {
                return speedMps;
            }

            double speedKph = data.NewData?.SpeedKmh ?? double.NaN;
            if (!double.IsNaN(speedKph) && !double.IsInfinity(speedKph) && speedKph >= 0.0)
            {
                return speedKph / 3.6;
            }

            double speedMph = SafeReadDouble(pluginManager, "DataCorePlugin.GameData.SpeedMph", double.NaN);
            if (!double.IsNaN(speedMph) && !double.IsInfinity(speedMph) && speedMph >= 0.0)
            {
                return speedMph * 0.44704;
            }

            return 0.0;
        }

        private double ResolveShiftAssistLongitudinalAccelMps2(PluginManager pluginManager)
        {
            double lonAccelMps2 = SafeReadDouble(pluginManager, "DataCorePlugin.GameRawData.Telemetry.LongAccel", double.NaN);
            if (!double.IsNaN(lonAccelMps2) && !double.IsInfinity(lonAccelMps2))
            {
                return lonAccelMps2;
            }

            lonAccelMps2 = SafeReadDouble(pluginManager, "DataCorePlugin.GameRawData.Telemetry.LonAccel", double.NaN);
            if (!double.IsNaN(lonAccelMps2) && !double.IsInfinity(lonAccelMps2))
            {
                return lonAccelMps2;
            }

            return 0.0;
        }

        private void ResetShiftAssistDebugCsvState()
        {
            _shiftAssistDebugCsvFailed = false;
            _shiftAssistDebugCsvLastWriteSessionSec = double.NaN;
            _shiftAssistDebugCsvLastWriteUtc = DateTime.MinValue;
            _shiftAssistDebugCsvPath = null;
            _shiftAssistDebugCsvFileTimestamp = null;
            ClearShiftAssistDelayDiagnosticsLatch();
        }

        private void WriteShiftAssistDebugCsv(DateTime nowUtc, double sessionTimeSec, int gear, int effectiveGear, int maxForwardGears, int rpm, double throttle01, int targetRpm, int leadTimeMs, bool beepTriggered, bool exportedBeepLatched, double speedMps, double accelDerivedMps2, double lonAccelTelemetryMps2, ShiftAssistLearningTick learningTick)
        {
            if (Settings?.EnableShiftAssistDebugCsv != true)
            {
                ResetShiftAssistDebugCsvState();
                return;
            }

            if (_shiftAssistDebugCsvFailed)
            {
                return;
            }

            int maxHz = GetShiftAssistDebugCsvMaxHz();
            double minIntervalSec = 1.0 / maxHz;
            if (!double.IsNaN(sessionTimeSec) && !double.IsInfinity(sessionTimeSec) && sessionTimeSec > 0.0)
            {
                if (!double.IsNaN(_shiftAssistDebugCsvLastWriteSessionSec) && sessionTimeSec < (_shiftAssistDebugCsvLastWriteSessionSec + minIntervalSec))
                {
                    return;
                }

                _shiftAssistDebugCsvLastWriteSessionSec = sessionTimeSec;
            }
            else if (_shiftAssistDebugCsvLastWriteUtc != DateTime.MinValue && (nowUtc - _shiftAssistDebugCsvLastWriteUtc).TotalSeconds < minIntervalSec)
            {
                return;
            }

            _shiftAssistDebugCsvLastWriteUtc = nowUtc;

            try
            {
                if (string.IsNullOrWhiteSpace(_shiftAssistDebugCsvPath))
                {
                    string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "LalapluginData");
                    Directory.CreateDirectory(folder);

                    if (string.IsNullOrWhiteSpace(_shiftAssistDebugCsvFileTimestamp))
                    {
                        _shiftAssistDebugCsvFileTimestamp = nowUtc.ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture);
                    }

                    _shiftAssistDebugCsvPath = Path.Combine(folder, "ShiftAssist_Debug_" + _shiftAssistDebugCsvFileTimestamp + ".csv");
                    if (!File.Exists(_shiftAssistDebugCsvPath))
                    {
                        File.WriteAllText(_shiftAssistDebugCsvPath,
                            "UtcTime,SessionTimeSec,Gear,MaxForwardGears,Rpm,Throttle01,TargetRpm,EffectiveTargetRpm,RpmRate,LeadTimeMs,BeepTriggered,BeepLatched,EngineState,SuppressDownshift,SuppressUpshift,SpeedMps,AccelDerivedMps2,LonAccelTelemetryMps2,EffectiveGear,LearnEnabled,LearnState,LearnPeakRpm,LearnPeakAccelMps2,LearnSampleAdded,LearnSamplesForGear,LearnLearnedRpmForGear,DelayPending,DelayPendingGear,DelayPendingAgeMs,DelayPendingRpmAtCue,DelayPendingTargetGear,DelayPendingDownshiftAgeMs,DelayCapturedMs,DelayCaptureEvent,DelayBeepType,DelayRpmAtBeep,DelayCaptureState" + Environment.NewLine);
                    }
                }

                string learnState = ToLearningStateText(learningTick?.State ?? ShiftAssistLearningState.Off);
                int learnPeakRpm = learningTick?.PeakRpm ?? 0;
                double learnPeakAccelMps2 = learningTick?.PeakAccelMps2 ?? 0.0;
                bool learnSampleAdded = learningTick?.SampleAdded == true;
                int learnSamplesForGear = learningTick?.SamplesForGear ?? 0;
                int learnLearnedRpmForGear = learningTick?.LearnedRpmForGear ?? 0;
                long delayNowTs = Stopwatch.GetTimestamp();
                int delayPending = _shiftAssistPendingDelayActive ? 1 : 0;
                int delayPendingGear = _shiftAssistPendingDelayGear;
                int delayPendingAgeMs = GetShiftAssistPendingAgeMs(delayNowTs);
                int delayPendingRpmAtCue = _shiftAssistPendingDelayRpmAtCue;
                int delayPendingTargetGear = _shiftAssistPendingDelayActive ? (_shiftAssistPendingDelayGear + 1) : 0;
                int delayPendingDownshiftAgeMs = GetShiftAssistPendingDownshiftAgeMs(delayNowTs);
                int delayCapturedMs = _shiftAssistDelayDiagLatchedCapturedMs;
                string delayCaptureEvent = _shiftAssistDelayDiagLatchedEvent ?? "NONE";
                string delayBeepType = _shiftAssistDelayDiagLatchedBeepType ?? "NONE";
                int delayCaptureState = _shiftAssistDelayCaptureState;
                if (delayCaptureState == 0)
                {
                    delayCaptureState = MapShiftAssistDelayCaptureState(delayCaptureEvent);
                }
                int delayRpmAtBeep = _shiftAssistLastBeepRpmLatched;

                var line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:o},{1},{2},{3},{4},{5:F4},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15:F4},{16:F4},{17:F4},{18},{19},{20},{21},{22:F4},{23},{24},{25},{26},{27},{28},{29},{30},{31},{32},{33},{34},{35},{36}",
                    nowUtc,
                    sessionTimeSec.ToString("F3", CultureInfo.InvariantCulture),
                    gear,
                    maxForwardGears,
                    rpm,
                    throttle01,
                    targetRpm,
                    _shiftAssistEngine.LastEffectiveTargetRpm,
                    _shiftAssistEngine.LastRpmRate,
                    leadTimeMs,
                    beepTriggered ? "1" : "0",
                    exportedBeepLatched ? "1" : "0",
                    _shiftAssistEngine.LastState.ToString(),
                    _shiftAssistEngine.IsSuppressingDownshift ? "1" : "0",
                    _shiftAssistEngine.IsSuppressingUpshift ? "1" : "0",
                    speedMps,
                    accelDerivedMps2,
                    lonAccelTelemetryMps2,
                    effectiveGear,
                    Settings?.ShiftAssistLearningModeEnabled == true ? "1" : "0",
                    learnState,
                    learnPeakRpm,
                    learnPeakAccelMps2,
                    learnSampleAdded ? "1" : "0",
                    learnSamplesForGear,
                    learnLearnedRpmForGear,
                    delayPending,
                    delayPendingGear,
                    delayPendingAgeMs,
                    delayPendingRpmAtCue,
                    delayPendingTargetGear,
                    delayPendingDownshiftAgeMs,
                    delayCapturedMs,
                    delayCaptureEvent,
                    delayBeepType,
                    delayRpmAtBeep,
                    delayCaptureState);

                File.AppendAllText(_shiftAssistDebugCsvPath, line + Environment.NewLine);
                ClearShiftAssistDelayDiagnosticsLatch();
            }
            catch (Exception ex)
            {
                _shiftAssistDebugCsvFailed = true;
                SimHub.Logging.Current.Warn($"[LalaPlugin:ShiftAssist] Debug CSV disabled for session after write failure path='{_shiftAssistDebugCsvPath ?? "(unset)"}' error='{ex.Message}'");
            }
        }

        #endregion

        #region Private Helper Methods for DataUpdate

        private void WriteCarSaDebugExport(PluginManager pluginManager, CarSAOutputs outputs, int sessionState, string sessionTypeName, bool debugMaster)
        {
            if (outputs == null || !debugMaster || Settings?.EnableCarSADebugExport != true)
            {
                return;
            }

            try
            {
                bool hasEvents = CaptureCarSaDebugEvents(outputs, outputs.Debug.SessionTimeSec);
                int cadenceMode = Settings?.CarSADebugExportCadence ?? CarSaDebugCadenceMiniSector;
                bool shouldWriteDebugRow;
                if (cadenceMode == CarSaDebugCadenceTick)
                {
                    int maxHz = Settings?.CarSADebugExportTickMaxHz ?? 20;
                    if (maxHz < 1)
                    {
                        maxHz = 1;
                    }

                    double minInterval = 1.0 / maxHz;
                    shouldWriteDebugRow = double.IsNaN(_carSaDebugLastWriteSessionTimeSec)
                        || outputs.Debug.SessionTimeSec >= (_carSaDebugLastWriteSessionTimeSec + minInterval);
                }
                else if (cadenceMode == CarSaDebugCadenceEventOnly)
                {
                    shouldWriteDebugRow = hasEvents;
                }
                else
                {
                    shouldWriteDebugRow = _carSaDebugCheckpointIndexCrossed >= 0;
                }

                if (shouldWriteDebugRow)
                {
                    EnsureCarSaDebugExportFile(pluginManager);

                    StringBuilder buffer = _carSaDebugExportBuffer ?? (_carSaDebugExportBuffer = new StringBuilder(1024));
                    buffer.Append(outputs.Debug.SessionTimeSec.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
                    buffer.Append(_eventMarkerPressed ? '1' : '0').Append(',');
                    buffer.Append(sessionState).Append(',');
                    AppendCsvSafeValue(buffer, sessionTypeName, "unknown");
                    buffer.Append(',');
                    buffer.Append(outputs.Debug.PlayerLapPct.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                    buffer.Append(_carSaDebugCheckpointIndexNow).Append(',');
                    buffer.Append(_carSaDebugCheckpointIndexCrossed).Append(',');

                    for (int i = 0; i < CarSaDebugExportSlotCount; i++)
                    {
                        CarSASlot ahead = outputs.AheadSlots.Length > i ? outputs.AheadSlots[i] : null;
                        AppendSlotDebugRow(buffer, ahead, isAhead: true);
                    }

                    for (int i = 0; i < CarSaDebugExportSlotCount; i++)
                    {
                        CarSASlot behind = outputs.BehindSlots.Length > i ? outputs.BehindSlots[i] : null;
                        AppendSlotDebugRow(buffer, behind, isAhead: false);
                    }

                    AppendPlayerRawEvidence(buffer, outputs);
                    buffer.AppendLine();

                    _carSaDebugLastWriteSessionTimeSec = outputs.Debug.SessionTimeSec;
                    _carSaDebugExportPendingLines++;
                    if (_carSaDebugExportPendingLines >= 20 || buffer.Length >= 4096)
                    {
                        FlushCarSaDebugExportBuffer();
                    }
                }

                if (hasEvents && cadenceMode == CarSaDebugCadenceEventOnly && Settings?.CarSADebugExportWriteEventsCsv == true)
                {
                    FlushCarSaEventsExportBuffer();
                }
            }
            catch (Exception)
            {
                _carSaDebugExportPath = null;
                _carSaDebugExportPendingLines = 0;
                _carSaDebugLastWriteSessionTimeSec = double.NaN;
                if (_carSaDebugExportBuffer != null)
                {
                    _carSaDebugExportBuffer.Clear();
                }
            }
        }

        private void WriteOffTrackDebugExport(
            PluginManager pluginManager,
            double sessionTimeSec,
            int sessionState,
            int sessionFlagsRaw,
            int probeCarIdx,
            int playerCarIdx,
            int playerIncidentCount,
            int playerIncidentDelta,
            int[] carIdxTrackSurface,
            int[] carIdxTrackSurfaceMaterial,
            int[] carIdxSessionFlags,
            bool[] carIdxOnPitRoad,
            int[] carIdxLap,
            float[] carIdxLapDistPct)
        {
            if (Settings?.EnableOffTrackDebugCsv != true || probeCarIdx < 0)
            {
                ResetOffTrackDebugExportState();
                return;
            }

            bool changeOnlyEnabled = Settings?.OffTrackDebugLogChangesOnly == true;
            if (_offTrackDebugLastChangeOnlyEnabled != changeOnlyEnabled)
            {
                _offTrackDebugLastChangeOnlyEnabled = changeOnlyEnabled;
                _offTrackDebugSnapshotInitialized = false;
            }

            if (!double.IsNaN(_offTrackDebugLastSessionTimeSec)
                && sessionTimeSec < _offTrackDebugLastSessionTimeSec - 0.5)
            {
                ResetOffTrackDebugExportState();
            }

            EnsureOffTrackDebugExportFile(pluginManager);

            if (_eventMarkerPressed)
            {
                _offTrackDebugEventWindowUntilSessionTimeSec = sessionTimeSec + 5.0;
            }

            bool eventActive = !double.IsNaN(_offTrackDebugEventWindowUntilSessionTimeSec)
                && sessionTimeSec <= _offTrackDebugEventWindowUntilSessionTimeSec;
            if (!eventActive)
            {
                _offTrackDebugEventWindowUntilSessionTimeSec = double.NaN;
            }

            int eventFired = eventActive ? 1 : 0;

            int trackSurface = ReadCarIdxInt(carIdxTrackSurface, probeCarIdx, int.MinValue);
            int trackSurfaceMaterial = ReadCarIdxInt(carIdxTrackSurfaceMaterial, probeCarIdx, int.MinValue);
            int carSessionFlags = ReadCarIdxInt(carIdxSessionFlags, probeCarIdx, int.MinValue);
            bool? carOnPitRoad = ReadCarIdxBool(carIdxOnPitRoad, probeCarIdx);
            int carLap = ReadCarIdxInt(carIdxLap, probeCarIdx, int.MinValue);
            double carLapDistPct = ReadCarIdxFloat(carIdxLapDistPct, probeCarIdx);

            CarSAEngine.OffTrackDebugState offTrackState = default;
            bool hasState = false;
            if (_carSaEngine != null)
            {
                hasState = _carSaEngine.TryGetOffTrackDebugState(probeCarIdx, out offTrackState);
            }
            bool? offTrackNow = hasState ? (bool?)offTrackState.OffTrackNow : null;
            bool? surfaceOffTrackNow = hasState ? (bool?)offTrackState.SurfaceOffTrackNow : null;
            bool? definitiveOffTrackNow = hasState ? (bool?)offTrackState.DefinitiveOffTrackNow : null;
            bool? boundaryEvidenceNow = hasState ? (bool?)offTrackState.BoundaryEvidenceNow : null;
            int offTrackStreak = hasState ? offTrackState.OffTrackStreak : int.MinValue;
            double offTrackFirstSeenTimeSec = hasState ? offTrackState.OffTrackFirstSeenTimeSec : double.NaN;
            bool? suspectOffTrackNow = hasState ? (bool?)offTrackState.SuspectOffTrackNow : null;
            int suspectOffTrackStreak = hasState ? offTrackState.SuspectOffTrackStreak : int.MinValue;
            double suspectOffTrackFirstSeenTimeSec = hasState ? offTrackState.SuspectOffTrackFirstSeenTimeSec : double.NaN;
            bool? suspectOffTrackActive = hasState ? (bool?)offTrackState.SuspectOffTrackActive : null;
            int suspectEventId = hasState ? offTrackState.SuspectEventId : 0;
            double suspectPulseUntilTimeSec = hasState ? offTrackState.SuspectPulseUntilTimeSec : double.NaN;
            bool? suspectPulseActive = hasState ? (bool?)offTrackState.SuspectPulseActive : null;
            int compromisedUntilLap = hasState ? offTrackState.CompromisedUntilLap : int.MinValue;
            bool? compromisedOffTrackActive = hasState ? (bool?)offTrackState.CompromisedOffTrackActive : null;
            bool? compromisedPenaltyActive = hasState ? (bool?)offTrackState.CompromisedPenaltyActive : null;
            bool? allowLatches = hasState ? (bool?)offTrackState.AllowLatches : null;

            OffTrackDebugSnapshot snapshot = new OffTrackDebugSnapshot
            {
                EventFired = eventFired,
                SessionState = sessionState,
                SessionFlagsRaw = sessionFlagsRaw,
                ProbeCarIdx = probeCarIdx,
                TrackSurface = trackSurface,
                TrackSurfaceMaterial = trackSurfaceMaterial,
                CarSessionFlags = carSessionFlags,
                CarOnPitRoad = carOnPitRoad,
                CarLap = carLap,
                CarLapDistPct = carLapDistPct,
                OffTrackNow = offTrackNow,
                SurfaceOffTrackNow = surfaceOffTrackNow,
                DefinitiveOffTrackNow = definitiveOffTrackNow,
                BoundaryEvidenceNow = boundaryEvidenceNow,
                OffTrackStreak = offTrackStreak,
                OffTrackFirstSeenTimeSec = offTrackFirstSeenTimeSec,
                SuspectOffTrackNow = suspectOffTrackNow,
                SuspectOffTrackStreak = suspectOffTrackStreak,
                SuspectOffTrackFirstSeenTimeSec = suspectOffTrackFirstSeenTimeSec,
                SuspectOffTrackActive = suspectOffTrackActive,
                SuspectEventId = suspectEventId,
                SuspectPulseUntilTimeSec = suspectPulseUntilTimeSec,
                SuspectPulseActive = suspectPulseActive,
                CompromisedUntilLap = compromisedUntilLap,
                CompromisedOffTrackActive = compromisedOffTrackActive,
                CompromisedPenaltyActive = compromisedPenaltyActive,
                AllowLatches = allowLatches,
                PlayerCarIdx = playerCarIdx,
                PlayerIncidentCount = playerIncidentCount,
                PlayerIncidentDelta = playerIncidentDelta
            };

            bool shouldWrite = true;
            if (changeOnlyEnabled)
            {
                shouldWrite = !_offTrackDebugSnapshotInitialized
                    || eventActive
                    || !OffTrackDebugSnapshotEquals(snapshot, _offTrackDebugLastSnapshot, ignoreContextFields: true);
                if (!shouldWrite)
                {
                    return;
                }

                _offTrackDebugLastSnapshot = snapshot;
                _offTrackDebugSnapshotInitialized = true;
            }

            StringBuilder buffer = _offTrackDebugExportBuffer ?? (_offTrackDebugExportBuffer = new StringBuilder(512));
            buffer.Append(sessionTimeSec.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
            buffer.Append(eventFired).Append(',');
            buffer.Append(sessionState).Append(',');
            AppendCsvHexValue(buffer, sessionFlagsRaw);
            buffer.Append(',');
            AppendCsvOptionalInt(buffer, sessionFlagsRaw, -1);
            buffer.Append(',');
            buffer.Append(probeCarIdx).Append(',');
            AppendCsvOptionalInt(buffer, trackSurface, int.MinValue);
            buffer.Append(',');
            AppendCsvOptionalInt(buffer, trackSurfaceMaterial, int.MinValue);
            buffer.Append(',');
            AppendCsvHexValue(buffer, carSessionFlags);
            buffer.Append(',');
            AppendCsvOptionalInt(buffer, carSessionFlags, int.MinValue);
            buffer.Append(',');
            AppendCsvOptionalBool(buffer, carOnPitRoad);
            buffer.Append(',');
            AppendCsvOptionalInt(buffer, carLap, int.MinValue);
            buffer.Append(',');
            AppendCsvOptionalDouble(buffer, carLapDistPct, "F6");
            buffer.Append(',');
            AppendCsvOptionalBool(buffer, surfaceOffTrackNow);
            buffer.Append(',');
            AppendCsvOptionalBool(buffer, definitiveOffTrackNow);
            buffer.Append(',');
            AppendCsvOptionalBool(buffer, boundaryEvidenceNow);
            buffer.Append(',');
            AppendCsvOptionalInt(buffer, offTrackStreak, int.MinValue);
            buffer.Append(',');
            AppendCsvOptionalDouble(buffer, offTrackFirstSeenTimeSec, "F3");
            buffer.Append(',');
            AppendCsvOptionalBool(buffer, suspectOffTrackNow);
            buffer.Append(',');
            AppendCsvOptionalInt(buffer, suspectOffTrackStreak, int.MinValue);
            buffer.Append(',');
            AppendCsvOptionalDouble(buffer, suspectOffTrackFirstSeenTimeSec, "F3");
            buffer.Append(',');
            AppendCsvOptionalBool(buffer, suspectOffTrackActive);
            buffer.Append(',');
            AppendCsvOptionalInt(buffer, suspectEventId, 0);
            buffer.Append(',');
            AppendCsvOptionalDouble(buffer, suspectPulseUntilTimeSec, "F3");
            buffer.Append(',');
            AppendCsvOptionalBool(buffer, suspectPulseActive);
            buffer.Append(',');
            AppendCsvOptionalInt(buffer, compromisedUntilLap, int.MinValue);
            buffer.Append(',');
            AppendCsvOptionalBool(buffer, compromisedOffTrackActive);
            buffer.Append(',');
            AppendCsvOptionalBool(buffer, compromisedPenaltyActive);
            buffer.Append(',');
            AppendCsvOptionalBool(buffer, allowLatches);
            buffer.Append(',');
            AppendCsvOptionalInt(buffer, playerCarIdx, -1);
            buffer.Append(',');
            AppendCsvOptionalInt(buffer, playerIncidentCount, -1);
            buffer.Append(',');
            AppendCsvOptionalInt(buffer, playerIncidentDelta, int.MinValue);
            buffer.AppendLine();

            _offTrackDebugLastSessionTimeSec = sessionTimeSec;
            _offTrackDebugExportPendingLines++;
            if (_offTrackDebugExportPendingLines >= 20 || buffer.Length >= 4096)
            {
                FlushOffTrackDebugExportBuffer();
            }
        }

        private void UpdatePlayerLapInvalidState(
            PluginManager pluginManager,
            double sessionTimeSec,
            int playerCarIdx,
            int[] carIdxLap)
        {
            if (!double.IsNaN(_playerLapInvalidLastSessionTimeSec)
                && sessionTimeSec < _playerLapInvalidLastSessionTimeSec - 0.5)
            {
                ResetPlayerLapInvalidState();
            }

            _playerLapInvalidLastSessionTimeSec = sessionTimeSec;

            int playerLap = ReadCarIdxInt(carIdxLap, playerCarIdx, int.MinValue);
            if (playerLap != int.MinValue && playerLap != _playerLapInvalidLap)
            {
                _playerLapInvalid = false;
                _playerLapInvalidLap = playerLap;
            }

            int incidentCount = SafeReadInt(pluginManager, "DataCorePlugin.GameRawData.Telemetry.PlayerCarMyIncidentCount", -1);
            _playerIncidentCount = incidentCount;

            int incidentDelta = int.MinValue;
            if (incidentCount >= 0)
            {
                if (_playerLastIncidentCount != int.MinValue)
                {
                    incidentDelta = incidentCount - _playerLastIncidentCount;
                }

                _playerLastIncidentCount = incidentCount;
            }
            else
            {
                _playerLastIncidentCount = int.MinValue;
            }

            _playerIncidentDelta = incidentDelta;

            if (incidentDelta > 0)
            {
                _playerLapInvalid = true;
            }
        }

        private bool CaptureCarSaDebugEvents(CarSAOutputs outputs, double sessionTimeSec)
        {
            if (outputs == null)
            {
                return false;
            }

            bool any = false;
            any |= CaptureCarSaDebugEventsForSide(outputs.AheadSlots, true, sessionTimeSec);
            any |= CaptureCarSaDebugEventsForSide(outputs.BehindSlots, false, sessionTimeSec);
            _carSaEventLastStateInitialized = true;
            return any;
        }

        private bool CaptureCarSaDebugEventsForSide(CarSASlot[] slots, bool isAhead, double sessionTimeSec)
        {
            if (slots == null)
            {
                return false;
            }

            bool any = false;
            for (int i = 0; i < CarSaDebugExportSlotCount; i++)
            {
                CarSASlot slot = slots.Length > i ? slots[i] : null;
                int carIdx = slot?.CarIdx ?? -1;
                int statusE = slot?.StatusE ?? (int)CarSAStatusE.Unknown;
                int hotCoolIntent = slot?.HotCoolIntent ?? 0;
                bool conflict = slot?.HotCoolConflictCached == true;
                any |= CaptureCarSaDebugEventValueChange(sessionTimeSec, isAhead, i, carIdx, "CarIdx", _carSaEventLastStateInitialized ? GetEventStateInt(isAhead, i, 0) : int.MinValue, carIdx, slot?.StatusEReason ?? string.Empty);
                any |= CaptureCarSaDebugEventValueChange(sessionTimeSec, isAhead, i, carIdx, "StatusE", _carSaEventLastStateInitialized ? GetEventStateInt(isAhead, i, 1) : int.MinValue, statusE, slot?.StatusEReason ?? string.Empty);
                any |= CaptureCarSaDebugEventValueChange(sessionTimeSec, isAhead, i, carIdx, "HotCoolIntent", _carSaEventLastStateInitialized ? GetEventStateInt(isAhead, i, 2) : int.MinValue, hotCoolIntent, string.Empty);
                any |= CaptureCarSaDebugEventValueChange(sessionTimeSec, isAhead, i, carIdx, "Conflict", _carSaEventLastStateInitialized ? (GetEventStateBool(isAhead, i) ? 1 : 0) : int.MinValue, conflict ? 1 : 0, string.Empty);
                SetEventState(isAhead, i, carIdx, statusE, hotCoolIntent, conflict);
            }

            return any;
        }

        private int GetEventStateInt(bool isAhead, int index, int kind)
        {
            if (kind == 0) return isAhead ? _carSaEventLastAheadCarIdx[index] : _carSaEventLastBehindCarIdx[index];
            if (kind == 1) return isAhead ? _carSaEventLastAheadStatusE[index] : _carSaEventLastBehindStatusE[index];
            return isAhead ? _carSaEventLastAheadHotCoolIntent[index] : _carSaEventLastBehindHotCoolIntent[index];
        }

        private bool GetEventStateBool(bool isAhead, int index)
        {
            return isAhead ? _carSaEventLastAheadConflict[index] : _carSaEventLastBehindConflict[index];
        }

        private void SetEventState(bool isAhead, int index, int carIdx, int statusE, int hotCoolIntent, bool conflict)
        {
            if (isAhead)
            {
                _carSaEventLastAheadCarIdx[index] = carIdx;
                _carSaEventLastAheadStatusE[index] = statusE;
                _carSaEventLastAheadHotCoolIntent[index] = hotCoolIntent;
                _carSaEventLastAheadConflict[index] = conflict;
            }
            else
            {
                _carSaEventLastBehindCarIdx[index] = carIdx;
                _carSaEventLastBehindStatusE[index] = statusE;
                _carSaEventLastBehindHotCoolIntent[index] = hotCoolIntent;
                _carSaEventLastBehindConflict[index] = conflict;
            }
        }

        private bool CaptureCarSaDebugEventValueChange(double sessionTimeSec, bool isAhead, int slotIndex, int carIdx, string eventType, int oldValue, int newValue, string reason)
        {
            if (oldValue == newValue)
            {
                return false;
            }

            if (!_carSaEventLastStateInitialized)
            {
                return false;
            }

            if ((Settings?.CarSADebugExportCadence ?? CarSaDebugCadenceMiniSector) != CarSaDebugCadenceEventOnly
                || Settings?.CarSADebugExportWriteEventsCsv != true)
            {
                return true;
            }

            EnsureCarSaEventsExportFile();
            string slotLabel = (isAhead ? "Ahead" : "Behind") + (slotIndex + 1).ToString("00", CultureInfo.InvariantCulture);
            _carSaEventsExportBuffer.Append(sessionTimeSec.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
            AppendCsvSafeValue(_carSaEventsExportBuffer, eventType, string.Empty);
            _carSaEventsExportBuffer.Append(',');
            AppendCsvSafeValue(_carSaEventsExportBuffer, slotLabel, string.Empty);
            _carSaEventsExportBuffer.Append(',');
            _carSaEventsExportBuffer.Append(carIdx).Append(',');
            _carSaEventsExportBuffer.Append(oldValue).Append(',');
            _carSaEventsExportBuffer.Append(newValue).Append(',');
            AppendCsvSafeValue(_carSaEventsExportBuffer, string.IsNullOrWhiteSpace(reason) ? "" : reason, string.Empty);
            _carSaEventsExportBuffer.AppendLine();
            _carSaEventsExportPendingLines++;
            if (_carSaEventsExportPendingLines >= 20 || _carSaEventsExportBuffer.Length >= 4096)
            {
                FlushCarSaEventsExportBuffer();
            }

            return true;
        }

        private void EnsureCarSaEventsExportFile()
        {
            string token = string.IsNullOrWhiteSpace(_currentSessionToken) ? "na" : _currentSessionToken.Replace(":", "_");
            if (string.Equals(token, _carSaEventsExportToken, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(_carSaEventsExportPath))
            {
                return;
            }

            _carSaEventsExportToken = token;
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "LalapluginData");
            Directory.CreateDirectory(folder);
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            _carSaEventsExportPath = Path.Combine(folder, $"CarSA_Events_{timestamp}.csv");
            if (!File.Exists(_carSaEventsExportPath))
            {
                File.WriteAllText(_carSaEventsExportPath, "SessionTimeSec,EventType,Slot,CarIdx,OldValue,NewValue,Reason" + Environment.NewLine);
            }
        }

        private void FlushCarSaEventsExportBuffer()
        {
            if (string.IsNullOrWhiteSpace(_carSaEventsExportPath) || _carSaEventsExportBuffer.Length == 0)
            {
                return;
            }

            File.AppendAllText(_carSaEventsExportPath, _carSaEventsExportBuffer.ToString());
            _carSaEventsExportBuffer.Clear();
            _carSaEventsExportPendingLines = 0;
        }

        private void UpdateCarSaDebugCadenceState(double lapPctRaw, CarSADebug debug)
        {
            if (debug != null)
            {
                _carSaDebugCheckpointIndexNow = debug.PlayerCheckpointIndexNow;
                _carSaDebugCheckpointIndexCrossed = debug.PlayerCheckpointIndexCrossed;
                _carSaDebugMiniSectorTickId = debug.MiniSectorTickId;
                return;
            }

            int checkpointNow = -1;
            if (!double.IsNaN(lapPctRaw) && !double.IsInfinity(lapPctRaw))
            {
                double lapPct = lapPctRaw > 1.5 ? lapPctRaw * 0.01 : lapPctRaw;
                if (lapPct >= 0.0 && lapPct < 1.0)
                {
                    checkpointNow = (int)Math.Floor(lapPct * 60.0);
                    if (checkpointNow >= 60)
                    {
                        checkpointNow = 59;
                    }
                }
            }

            int checkpointCrossed = -1;
            if (checkpointNow >= 0 && _carSaDebugCheckpointIndexNow >= 0 && checkpointNow != _carSaDebugCheckpointIndexNow)
            {
                checkpointCrossed = checkpointNow;
                _carSaDebugMiniSectorTickId++;
            }

            _carSaDebugCheckpointIndexNow = checkpointNow;
            _carSaDebugCheckpointIndexCrossed = checkpointCrossed;
        }

        private void UpdateCarSaRawTelemetryDebug(PluginManager pluginManager, CarSAOutputs outputs, int playerCarIdx, bool verboseLoggingEnabled)
        {
            if (outputs == null)
            {
                return;
            }

            int rawTelemetryMode = Settings?.CarSARawTelemetryMode ?? 1;
            if (rawTelemetryMode <= 0)
            {
                outputs.Debug.HasCarIdxPaceFlags = false;
                outputs.Debug.HasCarIdxSessionFlags = false;
                outputs.Debug.HasCarIdxTrackSurfaceMaterial = false;
                outputs.Debug.PlayerPaceFlagsRaw = -1;
                outputs.Debug.PlayerSessionFlagsRaw = -1;
                outputs.Debug.PlayerTrackSurfaceMaterialRaw = -1;
                outputs.Debug.RawTelemetryReadMode = "disabled";
                outputs.Debug.RawTelemetryFailReason = string.Empty;
                ClearCarSaRawSlots(outputs.AheadSlots);
                ClearCarSaRawSlots(outputs.BehindSlots);
                return;
            }

            bool hasPaceFlags = TryReadTelemetryIntArray(pluginManager, "CarIdxPaceFlags", out int[] paceFlags, out string paceReadMode, out string paceFailReason);
            bool hasSessionFlags = TryReadTelemetryIntArray(pluginManager, "CarIdxSessionFlags", out int[] sessionFlags, out string sessionReadMode, out string sessionFailReason);
            bool hasTrackSurfaceMaterial = TryReadTelemetryIntArray(pluginManager, "CarIdxTrackSurfaceMaterial", out int[] trackSurfaceMaterial, out string trackReadMode, out string trackFailReason);

            outputs.Debug.HasCarIdxPaceFlags = hasPaceFlags;
            outputs.Debug.HasCarIdxSessionFlags = hasSessionFlags;
            outputs.Debug.HasCarIdxTrackSurfaceMaterial = hasTrackSurfaceMaterial;
            outputs.Debug.RawTelemetryReadMode = ResolveRawTelemetryReadMode(hasPaceFlags, paceReadMode, hasSessionFlags, sessionReadMode, hasTrackSurfaceMaterial, trackReadMode);
            outputs.Debug.RawTelemetryFailReason = ResolveRawTelemetryFailReason(
                outputs.Debug.RawTelemetryReadMode,
                paceFailReason,
                sessionFailReason,
                trackFailReason);

            outputs.Debug.PlayerPaceFlagsRaw = ReadCarIdxRawValue(paceFlags, hasPaceFlags, playerCarIdx);
            outputs.Debug.PlayerSessionFlagsRaw = ReadCarIdxRawValue(sessionFlags, hasSessionFlags, playerCarIdx);
            outputs.Debug.PlayerTrackSurfaceMaterialRaw = ReadCarIdxRawValue(trackSurfaceMaterial, hasTrackSurfaceMaterial, playerCarIdx);

            // Always populate slot raw telemetry when raw telemetry is enabled.
            // Mode >= 2 is reserved for verbose change logging only.
            bool includeSlots = rawTelemetryMode >= 1;
            if (includeSlots)
            {
                UpdateCarSaRawSlots(outputs.AheadSlots, paceFlags, sessionFlags, trackSurfaceMaterial, hasPaceFlags, hasSessionFlags, hasTrackSurfaceMaterial);
                UpdateCarSaRawSlots(outputs.BehindSlots, paceFlags, sessionFlags, trackSurfaceMaterial, hasPaceFlags, hasSessionFlags, hasTrackSurfaceMaterial);
            }
            else
            {
                ClearCarSaRawSlots(outputs.AheadSlots);
                ClearCarSaRawSlots(outputs.BehindSlots);
            }

            bool enableRawLogging = rawTelemetryMode >= 2 && verboseLoggingEnabled;
            if (enableRawLogging)
            {
                int trackedCount = BuildTrackedCarIdxs(playerCarIdx, outputs, _carSaTrackedCarIdxs);
                if (hasPaceFlags)
                {
                    LogFlagChanges("PaceFlags", paceFlags, _carSaTrackedCarIdxs, trackedCount);
                }
                if (hasSessionFlags)
                {
                    LogFlagChanges("SessionFlags", sessionFlags, _carSaTrackedCarIdxs, trackedCount);
                }
            }
        }

        private void UpdateCarSaClassRankMap(PluginManager pluginManager)
        {
            string token = string.IsNullOrWhiteSpace(_currentSessionToken) ? "na" : _currentSessionToken;
            if (_carSaClassRankByColor != null && string.Equals(_carSaClassRankToken, token, StringComparison.Ordinal))
            {
                return;
            }

            _carSaClassRankToken = token;
            _carSaClassRankByColor = BuildCarSaClassRankMap(pluginManager, out _carSaClassRankSource);
            if (_carSaClassRankByColor != null && _carSaClassRankByColor.Count > 0)
            {
                string sourceLabel = string.IsNullOrWhiteSpace(_carSaClassRankSource) ? "unknown" : _carSaClassRankSource;
                SimHub.Logging.Current.Info($"[LalaPlugin:CarSA] Class rank map built using {sourceLabel} ({_carSaClassRankByColor.Count} classes)");
            }
        }

        private Dictionary<string, int> BuildCarSaClassRankMap(PluginManager pluginManager, out string source)
        {
            source = string.Empty;
            if (pluginManager == null)
            {
                return null;
            }

            var relSpeedByColor = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 64; i++)
            {
                string basePath = $"DataCorePlugin.GameRawData.SessionData.DriverInfo.CompetingDrivers[{i}]";
                int carIdx = GetInt(pluginManager, $"{basePath}.CarIdx", int.MinValue);
                if (carIdx == int.MinValue)
                {
                    break;
                }

                string classColor = GetCarClassColorHex(pluginManager, $"{basePath}.CarClassColor");
                if (string.IsNullOrWhiteSpace(classColor))
                {
                    continue;
                }

                double relSpeed = SafeReadDouble(pluginManager, $"{basePath}.CarClassRelSpeed", double.NaN);
                if (double.IsNaN(relSpeed) || double.IsInfinity(relSpeed))
                {
                    continue;
                }

                if (!relSpeedByColor.ContainsKey(classColor))
                {
                    relSpeedByColor[classColor] = relSpeed;
                }
            }

            if (relSpeedByColor.Count > 0)
            {
                source = "DriverInfo.CompetingDrivers.CarClassRelSpeed";
                return BuildCarSaClassRankMap(relSpeedByColor, descending: true);
            }

            var estLapByColor = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 64; i++)
            {
                string basePath = $"DataCorePlugin.GameRawData.SessionData.DriverInfo.CompetingDrivers[{i}]";
                int carIdx = GetInt(pluginManager, $"{basePath}.CarIdx", int.MinValue);
                if (carIdx == int.MinValue)
                {
                    break;
                }

                string classColor = GetCarClassColorHex(pluginManager, $"{basePath}.CarClassColor");
                if (string.IsNullOrWhiteSpace(classColor))
                {
                    continue;
                }

                double estLap = SafeReadDouble(pluginManager, $"{basePath}.CarClassEstLapTime", double.NaN);
                if (double.IsNaN(estLap) || double.IsInfinity(estLap) || estLap <= 0.0)
                {
                    continue;
                }

                if (!estLapByColor.ContainsKey(classColor))
                {
                    estLapByColor[classColor] = estLap;
                }
            }

            if (estLapByColor.Count > 0)
            {
                source = "DriverInfo.CompetingDrivers.CarClassEstLapTime";
                return BuildCarSaClassRankMap(estLapByColor, descending: false);
            }

            return null;
        }

        private static Dictionary<string, int> BuildCarSaClassRankMap(Dictionary<string, double> values, bool descending)
        {
            if (values == null || values.Count == 0)
            {
                return null;
            }

            var ordered = descending
                ? values.OrderByDescending(kvp => kvp.Value)
                : values.OrderBy(kvp => kvp.Value);

            var ranks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int rank = 1;
            foreach (var kvp in ordered)
            {
                ranks[kvp.Key] = rank;
                rank++;
            }

            return ranks;
        }

        private static void UpdateCarSaRawSlots(
            CarSASlot[] slots,
            int[] paceFlags,
            int[] sessionFlags,
            int[] trackSurfaceMaterial,
            bool hasPaceFlags,
            bool hasSessionFlags,
            bool hasTrackSurfaceMaterial)
        {
            if (slots == null)
            {
                return;
            }

            foreach (var slot in slots)
            {
                if (slot == null)
                {
                    continue;
                }
                int carIdx = slot.CarIdx;
                slot.SessionFlagsRaw = ReadCarIdxRawValue(sessionFlags, hasSessionFlags, carIdx);
                slot.TrackSurfaceMaterialRaw = ReadCarIdxRawValue(trackSurfaceMaterial, hasTrackSurfaceMaterial, carIdx);
            }
        }

        private static void ClearCarSaRawSlots(CarSASlot[] slots)
        {
            if (slots == null)
            {
                return;
            }

            foreach (var slot in slots)
            {
                if (slot == null)
                {
                    continue;
                }
                slot.SessionFlagsRaw = -1;
                slot.TrackSurfaceMaterialRaw = -1;
                slot.TrackSurfaceRaw = int.MinValue;
            }
        }

        private static int ReadCarIdxRawValue(int[] values, bool hasValues, int carIdx)
        {
            if (!hasValues || values == null || carIdx < 0 || carIdx >= values.Length)
            {
                return -1;
            }

            return values[carIdx];
        }

        private int BuildTrackedCarIdxs(int playerCarIdx, CarSAOutputs outputs, int[] buffer)
        {
            if (buffer == null)
            {
                return 0;
            }

            int count = 0;
            count = AddTrackedCarIdx(buffer, count, playerCarIdx);
            if (outputs != null)
            {
                count = AddTrackedSlots(buffer, count, outputs.AheadSlots);
                count = AddTrackedSlots(buffer, count, outputs.BehindSlots);
            }

            return count;
        }

        private static int AddTrackedCarIdx(int[] buffer, int count, int carIdx)
        {
            if (carIdx < 0)
            {
                return count;
            }

            for (int i = 0; i < count; i++)
            {
                if (buffer[i] == carIdx)
                {
                    return count;
                }
            }

            if (count < buffer.Length)
            {
                buffer[count++] = carIdx;
            }

            return count;
        }

        private static int AddTrackedSlots(int[] buffer, int count, CarSASlot[] slots)
        {
            if (slots == null)
            {
                return count;
            }

            foreach (var slot in slots)
            {
                count = AddTrackedCarIdx(buffer, count, slot?.CarIdx ?? -1);
            }

            return count;
        }

        private void LogFlagChanges(string label, int[] values, int[] tracked, int trackedCount)
        {
            if (values == null || tracked == null)
            {
                return;
            }

            for (int i = 0; i < trackedCount; i++)
            {
                int carIdx = tracked[i];
                if (carIdx < 0 || carIdx >= values.Length)
                {
                    continue;
                }
                int current = values[carIdx];
                if (label == "PaceFlags")
                {
                    LogCarIdxFlagChange(label, carIdx, current, _carSaLastPaceFlags, _carSaLastPaceFlagsLog);
                }
                else
                {
                    LogCarIdxFlagChange(label, carIdx, current, _carSaLastSessionFlags, _carSaLastSessionFlagsLog);
                }
            }
        }

        private static void LogCarIdxFlagChange(
            string label,
            int carIdx,
            int current,
            Dictionary<int, int> lastValues,
            Dictionary<int, DateTime> lastLogs)
        {
            if (!lastValues.TryGetValue(carIdx, out int previous))
            {
                lastValues[carIdx] = current;
                return;
            }

            if (previous == current)
            {
                return;
            }

            lastValues[carIdx] = current;

            DateTime now = DateTime.UtcNow;
            if (lastLogs.TryGetValue(carIdx, out DateTime lastLog) && (now - lastLog).TotalSeconds < 1.0)
            {
                return;
            }

            lastLogs[carIdx] = now;
            int xor = previous ^ current;
            SimHub.Logging.Current.Debug($"[LalaPlugin:CarSA] {label} changed carIdx={carIdx} {previous} -> {current} (xor=0x{xor:X})");
        }

        private void AppendSlotDebugRow(StringBuilder buffer, CarSASlot slot, bool isAhead)
        {
            if (slot == null)
            {
                buffer.Append("-1,");
                buffer.Append("NaN,");
                buffer.Append("0,");
                buffer.Append("NaN,");
                buffer.Append("NaN,");
                buffer.Append("NaN,");
                buffer.Append("0,");
                buffer.Append("0,");
                buffer.Append("0,");
                buffer.Append("0,");
                buffer.Append("0,");
                AppendSlotRawEvidence(buffer, null);
                return;
            }

            buffer.Append(slot.CarIdx).Append(',');
            buffer.Append((isAhead ? slot.ForwardDistPct : slot.BackwardDistPct).ToString("F6", CultureInfo.InvariantCulture)).Append(',');
            buffer.Append(slot.LapDelta).Append(',');
            buffer.Append(slot.GapTrackSec.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
            buffer.Append(slot.GapRelativeSec.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
            buffer.Append(slot.GapRelativeSource).Append(',');
            buffer.Append(slot.ClosingRateSecPerSec.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
            buffer.Append(slot.DeltaBestSec.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
            buffer.Append(slot.HotCoolIntent).Append(',');
            buffer.Append(slot.HotCoolConflictCached ? 1 : 0).Append(',');
            buffer.Append(slot.StatusE).Append(',');
            AppendSlotRawEvidence(buffer, slot);
        }

        private static void AppendSlotRawEvidence(StringBuilder buffer, CarSASlot slot)
        {
            if (slot == null)
            {
                buffer.Append("-1,");
                buffer.Append("0,");
                buffer.Append("-1,");
                return;
            }

            int trackSurfaceRaw = NormalizeTrackSurfaceRaw(slot.TrackSurfaceRawDebug);
            int sessionFlagsRaw = slot.SessionFlagsRaw;
            buffer.Append(trackSurfaceRaw).Append(',');
            buffer.Append(slot.IsOnPitRoad ? 1 : 0).Append(',');
            buffer.Append(sessionFlagsRaw).Append(',');
        }

        private static void AppendPlayerRawEvidence(StringBuilder buffer, CarSAOutputs outputs)
        {
            if (outputs?.Debug == null)
            {
                buffer.Append("-1,");
                buffer.Append("-1");
                return;
            }

            int playerTrackSurfaceRaw = NormalizeTrackSurfaceRaw(outputs.Debug.PlayerTrackSurfaceRaw);
            buffer.Append(playerTrackSurfaceRaw).Append(',');
            buffer.Append(outputs.Debug.PlayerSessionFlagsRaw);
        }

        private void EnsureCarSaDebugExportFile(PluginManager pluginManager)
        {
            string token = string.IsNullOrWhiteSpace(_currentSessionToken) ? "na" : _currentSessionToken.Replace(":", "_");
            string trackNameSource = !string.IsNullOrWhiteSpace(CurrentTrackName)
                ? CurrentTrackName
                : CurrentTrackKey;
            if (string.IsNullOrWhiteSpace(trackNameSource))
            {
                trackNameSource = GetSessionInfoTrackName(pluginManager);
            }
            if (string.IsNullOrWhiteSpace(trackNameSource))
            {
                trackNameSource = "Unknown";
            }
            if (!string.Equals(token, _carSaDebugExportToken, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(_carSaDebugExportPath))
            {
                FlushCarSaDebugExportBuffer();
                _carSaDebugExportToken = token;

                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "LalapluginData");
                Directory.CreateDirectory(folder);
                string trackName = SanitizeCarSaDebugExportName(trackNameSource);
                string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
                _carSaDebugExportPath = Path.Combine(folder, $"CarSA_Debug_{timestamp}_{trackName}.csv");

                if (!File.Exists(_carSaDebugExportPath))
                {
                    File.WriteAllText(_carSaDebugExportPath, GetCarSaDebugExportHeader() + Environment.NewLine);
                }

                FlushCarSaDebugExportBuffer();
            }

            if (_carSaDebugExportBuffer == null)
            {
                _carSaDebugExportBuffer = new StringBuilder(1024);
            }
        }

        private static string GetSessionInfoTrackName(PluginManager pluginManager)
        {
            string trackDisplay = GetString(pluginManager, "DataCorePlugin.GameRawData.SessionData.WeekendInfo.TrackDisplayName");
            if (!string.IsNullOrWhiteSpace(trackDisplay))
            {
                return trackDisplay;
            }

            string trackName = GetString(pluginManager, "DataCorePlugin.GameRawData.SessionData.WeekendInfo.TrackName");
            if (!string.IsNullOrWhiteSpace(trackName))
            {
                return trackName;
            }

            string trackConfig = GetString(pluginManager, "DataCorePlugin.GameRawData.SessionData.WeekendInfo.TrackConfigName");
            if (!string.IsNullOrWhiteSpace(trackConfig))
            {
                return trackConfig;
            }

            return null;
        }

        private static string SanitizeCarSaDebugExportName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return "Unknown";
            }

            string invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            char[] buffer = input.ToCharArray();
            char[] cleaned = new char[buffer.Length];
            int cleanedLength = 0;
            bool lastUnderscore = false;
            for (int i = 0; i < buffer.Length; i++)
            {
                char c = buffer[i];
                bool makeUnderscore = char.IsWhiteSpace(c) || invalidChars.IndexOf(c) >= 0;
                char nextChar = makeUnderscore ? '_' : c;
                if (nextChar == '_')
                {
                    if (lastUnderscore)
                    {
                        continue;
                    }
                    lastUnderscore = true;
                }
                else
                {
                    lastUnderscore = false;
                }

                cleaned[cleanedLength] = nextChar;
                cleanedLength++;
            }

            int start = 0;
            int end = cleanedLength - 1;
            while (start <= end && cleaned[start] == '_')
            {
                start++;
            }
            while (end >= start && cleaned[end] == '_')
            {
                end--;
            }

            if (end < start)
            {
                return "Unknown";
            }

            int maxLength = 60;
            int length = end - start + 1;
            if (length > maxLength)
            {
                length = maxLength;
            }

            return new string(cleaned, start, length);
        }

        private static string SanitizeOffTrackDebugExportName(string input)
        {
            string sanitized = SanitizeCarSaDebugExportName(input);
            if (string.Equals(sanitized, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return "UnknownTrack";
            }

            return sanitized;
        }

        private void FlushCarSaDebugExportBuffer()
        {
            if (string.IsNullOrWhiteSpace(_carSaDebugExportPath) || _carSaDebugExportBuffer == null || _carSaDebugExportBuffer.Length == 0)
            {
                return;
            }

            File.AppendAllText(_carSaDebugExportPath, _carSaDebugExportBuffer.ToString());
            _carSaDebugExportBuffer.Clear();
            _carSaDebugExportPendingLines = 0;
        }

        private void EnsureOffTrackDebugExportFile(PluginManager pluginManager)
        {
            string token = string.IsNullOrWhiteSpace(_currentSessionToken) ? "na" : _currentSessionToken.Replace(":", "_");
            if (string.Equals(token, _offTrackDebugExportToken, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(_offTrackDebugExportPath))
            {
                return;
            }

            FlushOffTrackDebugExportBuffer();
            _offTrackDebugExportToken = token;
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "LalapluginData");
            Directory.CreateDirectory(folder);
            string trackNameSource = !string.IsNullOrWhiteSpace(CurrentTrackName)
                ? CurrentTrackName
                : CurrentTrackKey;
            if (string.IsNullOrWhiteSpace(trackNameSource))
            {
                trackNameSource = GetSessionInfoTrackName(pluginManager);
            }
            if (string.IsNullOrWhiteSpace(trackNameSource))
            {
                trackNameSource = "UnknownTrack";
            }

            string trackName = SanitizeOffTrackDebugExportName(trackNameSource);
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            _offTrackDebugExportPath = Path.Combine(folder, $"OffTrackDebug_{trackName}_{timestamp}.csv");
            if (!File.Exists(_offTrackDebugExportPath))
            {
                File.WriteAllText(_offTrackDebugExportPath, GetOffTrackDebugExportHeader() + Environment.NewLine);
            }
        }

        private void FlushOffTrackDebugExportBuffer()
        {
            if (string.IsNullOrWhiteSpace(_offTrackDebugExportPath) || _offTrackDebugExportBuffer == null || _offTrackDebugExportBuffer.Length == 0)
            {
                return;
            }

            File.AppendAllText(_offTrackDebugExportPath, _offTrackDebugExportBuffer.ToString());
            _offTrackDebugExportBuffer.Clear();
            _offTrackDebugExportPendingLines = 0;
        }

        private void ResetOffTrackDebugExportState()
        {
            if (string.IsNullOrWhiteSpace(_offTrackDebugExportPath) && (_offTrackDebugExportBuffer == null || _offTrackDebugExportBuffer.Length == 0))
            {
                return;
            }

            FlushOffTrackDebugExportBuffer();
            _offTrackDebugExportPath = null;
            _offTrackDebugExportToken = null;
            _offTrackDebugExportPendingLines = 0;
            _offTrackDebugLastSessionTimeSec = double.NaN;
            _offTrackDebugEventWindowUntilSessionTimeSec = double.NaN;
            _offTrackDebugLastSnapshot = default;
            _offTrackDebugSnapshotInitialized = false;
            _offTrackDebugLastChangeOnlyEnabled = false;
        }

        private void ResetPlayerLapInvalidState()
        {
            _playerLapInvalid = false;
            _playerLapInvalidLap = int.MinValue;
            _playerLastIncidentCount = int.MinValue;
            _playerIncidentDelta = int.MinValue;
            _playerIncidentCount = -1;
            _playerLapInvalidLastSessionTimeSec = double.NaN;
        }

        private static string GetOffTrackDebugExportHeader()
        {
            StringBuilder buffer = new StringBuilder(512);
            buffer.Append("SessionTimeSec,EventFired,SessionState,SessionFlagsHex,SessionFlagsDec,ProbeCarIdx,");
            buffer.Append("CarIdxTrackSurface,CarIdxTrackSurfaceMaterial,CarIdxSessionFlagsHex,CarIdxSessionFlagsDec,");
            buffer.Append("CarIdxOnPitRoad,CarIdxLap,CarIdxLapDistPct,");
            buffer.Append("SurfaceOffTrackNow,DefinitiveOffTrackNow,BoundaryEvidenceNow,OffTrackStreak,OffTrackFirstSeenTimeSec,");
            buffer.Append("SuspectOffTrackNow,SuspectOffTrackStreak,SuspectOffTrackFirstSeenTimeSec,SuspectOffTrackActive,");
            buffer.Append("SuspectEventId,SuspectPulseUntilTimeSec,SuspectPulseActive,");
            buffer.Append("CompromisedUntilLap,CompromisedOffTrackActive,CompromisedPenaltyActive,AllowLatches,");
            buffer.Append("PlayerCarIdx,PlayerIncidentCount,PlayerIncidentDelta");
            return buffer.ToString();
        }

        private static string GetCarSaDebugExportHeader()
        {
            StringBuilder buffer = new StringBuilder(2048);
            AppendCarSaDebugHeaderColumn(buffer, "SessionTimeSec");
            AppendCarSaDebugHeaderColumn(buffer, "EventFired");
            AppendCarSaDebugHeaderColumn(buffer, "SessionState");
            AppendCarSaDebugHeaderColumn(buffer, "SessionTypeName");
            AppendCarSaDebugHeaderColumn(buffer, "PlayerLapPct");
            AppendCarSaDebugHeaderColumn(buffer, "PlayerCheckpointIndexNow");
            AppendCarSaDebugHeaderColumn(buffer, "PlayerCheckpointIndexCrossed");

            for (int i = 1; i <= CarSaDebugExportSlotCount; i++)
            {
                AppendCarSaDebugSlotHeader(buffer, isAhead: true, slotIndex: i);
            }

            for (int i = 1; i <= CarSaDebugExportSlotCount; i++)
            {
                AppendCarSaDebugSlotHeader(buffer, isAhead: false, slotIndex: i);
            }

            AppendCarSaDebugHeaderColumn(buffer, "PlayerTrackSurfaceRaw");
            AppendCarSaDebugHeaderColumn(buffer, "PlayerSessionFlagsRaw");

            if (buffer.Length > 0)
            {
                buffer.Length -= 1;
            }

            return buffer.ToString();
        }

        private static void AppendCarSaDebugHeaderColumn(StringBuilder buffer, string columnName)
        {
            buffer.Append(columnName).Append(',');
        }

        private static void AppendCarSaDebugSlotHeader(StringBuilder buffer, bool isAhead, int slotIndex)
        {
            string slotLabel = (isAhead ? "Ahead" : "Behind") + slotIndex.ToString("00", CultureInfo.InvariantCulture);

            AppendCarSaDebugHeaderColumn(buffer, slotLabel + ".CarIdx");
            AppendCarSaDebugHeaderColumn(buffer, slotLabel + ".DistPct");
            AppendCarSaDebugHeaderColumn(buffer, slotLabel + ".LapDelta");
            AppendCarSaDebugHeaderColumn(buffer, slotLabel + ".GapTrackSec");
            AppendCarSaDebugHeaderColumn(buffer, slotLabel + ".GapRelativeSec");
            AppendCarSaDebugHeaderColumn(buffer, slotLabel + ".GapRelativeSource");
            AppendCarSaDebugHeaderColumn(buffer, slotLabel + ".ClosingRateSecPerSec");
            AppendCarSaDebugHeaderColumn(buffer, slotLabel + ".DeltaBestSec");
            AppendCarSaDebugHeaderColumn(buffer, slotLabel + ".HotCoolIntent");
            AppendCarSaDebugHeaderColumn(buffer, slotLabel + ".HotCoolConflict");
            AppendCarSaDebugHeaderColumn(buffer, slotLabel + ".StatusE");
            AppendCarSaDebugHeaderColumn(buffer, slotLabel + ".TrackSurfaceRaw");
            AppendCarSaDebugHeaderColumn(buffer, slotLabel + ".IsOnPitRoad");
            AppendCarSaDebugHeaderColumn(buffer, slotLabel + ".SessionFlagsRaw");
        }

        private static int NormalizeTrackSurfaceRaw(int raw)
        {
            return raw == int.MinValue ? -1 : raw;
        }

        private static void AppendCsvSafeValue(StringBuilder buffer, string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                buffer.Append(fallback);
                return;
            }

            int startLength = buffer.Length;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == ',' || c == '\n' || c == '\r')
                {
                    continue;
                }
                buffer.Append(c);
            }

            if (buffer.Length == startLength)
            {
                buffer.Append(fallback);
            }
        }

        private static void AppendCsvHexValue(StringBuilder buffer, int value)
        {
            if (value < 0)
            {
                return;
            }

            buffer.Append("0x").Append(value.ToString("X", CultureInfo.InvariantCulture));
        }

        private static void AppendCsvOptionalInt(StringBuilder buffer, int value, int unsetValue)
        {
            if (value == unsetValue)
            {
                return;
            }

            buffer.Append(value);
        }

        private static void AppendCsvOptionalDouble(StringBuilder buffer, double value, string format)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return;
            }

            buffer.Append(value.ToString(format, CultureInfo.InvariantCulture));
        }

        private static void AppendCsvOptionalBool(StringBuilder buffer, bool? value)
        {
            if (!value.HasValue)
            {
                return;
            }

            buffer.Append(value.Value ? "1" : "0");
        }

        private static int ReadCarIdxInt(int[] values, int carIdx, int fallback)
        {
            if (values == null || carIdx < 0 || carIdx >= values.Length)
            {
                return fallback;
            }

            return values[carIdx];
        }

        private static bool? ReadCarIdxBool(bool[] values, int carIdx)
        {
            if (values == null || carIdx < 0 || carIdx >= values.Length)
            {
                return null;
            }

            return values[carIdx];
        }

        private static double ReadCarIdxFloat(float[] values, int carIdx)
        {
            if (values == null || carIdx < 0 || carIdx >= values.Length)
            {
                return double.NaN;
            }

            return values[carIdx];
        }

        private void ResetCarSaDebugExportState()
        {
            FlushCarSaDebugExportBuffer();
            FlushCarSaEventsExportBuffer();
            _carSaDebugExportPath = null;
            _carSaDebugExportToken = null;
            _carSaDebugExportPendingLines = 0;
            _carSaDebugCheckpointIndexNow = -1;
            _carSaDebugCheckpointIndexCrossed = -1;
            _carSaDebugMiniSectorTickId = 0;
            _carSaDebugLastWriteSessionTimeSec = double.NaN;
            _carSaEventsExportPath = null;
            _carSaEventsExportToken = null;
            _carSaEventsExportBuffer.Clear();
            _carSaEventsExportPendingLines = 0;
            _carSaEventLastStateInitialized = false;
            ResetCarSaDebugGapArray(_carSaDebugAheadDahlRelativeGapSec);
            ResetCarSaDebugGapArray(_carSaDebugBehindDahlRelativeGapSec);
            ResetCarSaDebugGapArray(_carSaDebugAheadIRacingRelativeGapSec);
            ResetCarSaDebugGapArray(_carSaDebugBehindIRacingRelativeGapSec);
        }

        private static void ResetCarSaDebugGapArray(double[] values)
        {
            if (values == null)
            {
                return;
            }

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = double.NaN;
            }
        }

        private void ResetCarSaIdentityState()
        {
            _carSaIdentityRefreshRequested = true;
            _carSaIdentityLastRetrySessionTimeSec = -1.0;
            for (int i = 0; i < _carSaLastAheadIdx.Length; i++)
            {
                _carSaLastAheadIdx[i] = -1;
            }
            for (int i = 0; i < _carSaLastBehindIdx.Length; i++)
            {
                _carSaLastBehindIdx[i] = -1;
            }
        }

        private void ResetCarSaLapTimeUpdateState()
        {
            for (int i = 0; i < CarSAEngine.MaxCars; i++)
            {
                _carSaPrevBestLapTimeSecByIdx[i] = double.NaN;
                _carSaPrevLastLapTimeSecByIdx[i] = double.NaN;
                _carSaPrevLapCountByIdx[i] = -1;
                _carSaLapTimeUpdateByIdx[i] = string.Empty;
                _carSaLapTimeUpdateExpireAtSecByIdx[i] = double.NaN;
            }
        }

        private void UpdateCarSaTelemetryCaches(PluginManager pluginManager, double sessionTimeSec)
        {
            if (pluginManager == null)
            {
                return;
            }

            float[] bestLapTimes = SafeReadFloatArray(pluginManager, "DataCorePlugin.GameRawData.Telemetry.CarIdxBestLapTime");
            float[] lastLapTimes = SafeReadFloatArray(pluginManager, "DataCorePlugin.GameRawData.Telemetry.CarIdxLastLapTime");
            int[] lapCounts = SafeReadIntArray(pluginManager, "DataCorePlugin.GameRawData.Telemetry.CarIdxLap");
            float[] estTimes = SafeReadFloatArray(pluginManager, "DataCorePlugin.GameRawData.Telemetry.CarIdxEstTime");
            int[] classPositions = SafeReadIntArray(pluginManager, "DataCorePlugin.GameRawData.Telemetry.CarIdxClassPosition");
            var previousBestLapTimes = new double[CarSAEngine.MaxCars];
            var previousLastLapTimes = new double[CarSAEngine.MaxCars];
            var previousLapCounts = new int[CarSAEngine.MaxCars];
            Array.Copy(_carSaPrevBestLapTimeSecByIdx, previousBestLapTimes, CarSAEngine.MaxCars);
            Array.Copy(_carSaPrevLastLapTimeSecByIdx, previousLastLapTimes, CarSAEngine.MaxCars);
            Array.Copy(_carSaPrevLapCountByIdx, previousLapCounts, CarSAEngine.MaxCars);

            for (int i = 0; i < CarSAEngine.MaxCars; i++)
            {
                double currentBestLap = ReadCarIdxTime(bestLapTimes, i);
                double currentLastLap = ReadCarIdxTime(lastLapTimes, i);
                double previousBestLap = previousBestLapTimes[i];
                double previousLastLap = previousLastLapTimes[i];
                int currentLapCount = (lapCounts != null && i < lapCounts.Length) ? lapCounts[i] : -1;
                int previousLapCount = previousLapCounts[i];
                bool baselineUnknown = previousLapCount < 0;
                bool hasCompletedLap = currentLapCount >= 1;
                bool crossedIntoCompletedLapState = previousLapCount < 1 && hasCompletedLap;

                if (!hasCompletedLap || crossedIntoCompletedLapState)
                {
                    _carSaLapTimeUpdateByIdx[i] = string.Empty;
                    _carSaLapTimeUpdateExpireAtSecByIdx[i] = double.NaN;
                }

                if (crossedIntoCompletedLapState)
                {
                    previousBestLap = double.NaN;
                    previousLastLap = double.NaN;
                }

                bool hasLapUpdate = IsValidCarSaLapTimeSec(currentLastLap)
                    && (!IsValidCarSaLapTimeSec(previousLastLap) || Math.Abs(currentLastLap - previousLastLap) > CarSaLapTimeEpsilonSec);
                bool hasNewPersonalBest = hasLapUpdate
                    && IsValidCarSaLapTimeSec(previousBestLap)
                    && IsValidCarSaLapTimeSec(currentBestLap)
                    && (currentBestLap + CarSaLapTimeEpsilonSec) < previousBestLap;

                if (hasCompletedLap && hasLapUpdate && !baselineUnknown && !crossedIntoCompletedLapState)
                {
                    if (hasNewPersonalBest)
                    {
                        bool hasNewSessionBestInClass = IsNewSessionBestInClass(i, currentBestLap, bestLapTimes, previousBestLapTimes);
                        _carSaLapTimeUpdateByIdx[i] = hasNewSessionBestInClass ? "SB" : "PB";
                    }
                    else
                    {
                        _carSaLapTimeUpdateByIdx[i] = "LL";
                    }

                    _carSaLapTimeUpdateExpireAtSecByIdx[i] = sessionTimeSec + CarSaLapTimeUpdateVisibilitySeconds;
                }

                _carSaBestLapTimeSecByIdx[i] = currentBestLap;
                _carSaLastLapTimeSecByIdx[i] = currentLastLap;
                _carSaEstTimeSecByIdx[i] = ReadCarIdxTime(estTimes, i);
                _carSaClassPositionByIdx[i] = (classPositions != null && i < classPositions.Length) ? classPositions[i] : 0;
            }

            Array.Copy(_carSaBestLapTimeSecByIdx, _carSaPrevBestLapTimeSecByIdx, CarSAEngine.MaxCars);
            Array.Copy(_carSaLastLapTimeSecByIdx, _carSaPrevLastLapTimeSecByIdx, CarSAEngine.MaxCars);
            int lapCountsLength = lapCounts?.Length ?? 0;
            int copiedLapCountLength = Math.Min(lapCountsLength, CarSAEngine.MaxCars);
            if (copiedLapCountLength > 0)
            {
                Array.Copy(lapCounts, _carSaPrevLapCountByIdx, copiedLapCountLength);
            }
            for (int i = copiedLapCountLength; i < CarSAEngine.MaxCars; i++)
            {
                _carSaPrevLapCountByIdx[i] = -1;
            }

            UpdateCarSaDriverInfoCache(pluginManager);
            _carSaEngine?.UpdateIRatingSof(_carSaIRatingByIdx);
        }

        private void UpdateCarSaDriverInfoCache(PluginManager pluginManager)
        {
            for (int i = 0; i < _carSaIRatingByIdx.Length; i++)
            {
                _carSaIRatingByIdx[i] = 0;
                _carSaCarClassEstLapTimeSecByIdx[i] = double.NaN;
            }

            bool populated = false;
            for (int i = 1; i <= 64; i++)
            {
                string basePath = $"DataCorePlugin.GameRawData.SessionData.DriverInfo.Drivers{i:00}";
                int idx = GetInt(pluginManager, $"{basePath}.CarIdx", int.MinValue);
                if (idx == int.MinValue)
                {
                    continue;
                }

                populated = true;
                if (idx < 0 || idx >= CarSAEngine.MaxCars)
                {
                    continue;
                }

                _carSaIRatingByIdx[idx] = GetInt(pluginManager, $"{basePath}.IRating", 0);
                _carSaCarClassEstLapTimeSecByIdx[idx] = SanitizeCarSaLapTimeSec(
                    SafeReadDouble(pluginManager, $"{basePath}.CarClassEstLapTime", double.NaN));
            }

            if (populated)
            {
                return;
            }

            for (int i = 0; i < 64; i++)
            {
                string basePath = $"DataCorePlugin.GameRawData.SessionData.DriverInfo.CompetingDrivers[{i}]";
                int idx = GetInt(pluginManager, $"{basePath}.CarIdx", int.MinValue);
                if (idx == int.MinValue)
                {
                    break;
                }

                if (idx < 0 || idx >= CarSAEngine.MaxCars)
                {
                    continue;
                }

                _carSaIRatingByIdx[idx] = GetInt(pluginManager, $"{basePath}.IRating", 0);
                _carSaCarClassEstLapTimeSecByIdx[idx] = SanitizeCarSaLapTimeSec(
                    SafeReadDouble(pluginManager, $"{basePath}.CarClassEstLapTime", double.NaN));
            }
        }

        private void RefreshCarSaSlotIdentities(PluginManager pluginManager, double sessionTimeSec)
        {
            if (_carSaEngine == null || pluginManager == null)
            {
                return;
            }

            bool forceRefresh = _carSaIdentityRefreshRequested;
            if (forceRefresh && !IsCarSaIdentitySourceReady(pluginManager))
            {
                if (_carSaIdentityLastRetrySessionTimeSec < 0.0 || sessionTimeSec - _carSaIdentityLastRetrySessionTimeSec >= 0.5)
                {
                    _carSaIdentityLastRetrySessionTimeSec = sessionTimeSec;
                }
                return;
            }

            if (forceRefresh)
            {
                _carSaIdentityRefreshRequested = false;
            }

            ApplyCarSaIdentityRefresh(pluginManager, _carSaEngine.Outputs.AheadSlots, _carSaLastAheadIdx, forceRefresh, sessionTimeSec);
            ApplyCarSaIdentityRefresh(pluginManager, _carSaEngine.Outputs.BehindSlots, _carSaLastBehindIdx, forceRefresh, sessionTimeSec);
        }

        private const double LiveDeltaClampSec = 30.0;

        private void MarkFriendsDirty()
        {
            _friendsDirty = true;
        }

        public void NotifyFriendsChanged()
        {
            MarkFriendsDirty();
        }

        private void HookFriendSettings(LaunchPluginSettings settings)
        {
            var friends = settings?.Friends;
            if (friends == null)
            {
                if (_friendsCollection != null)
                {
                    _friendsCollection.CollectionChanged -= OnFriendsCollectionChanged;
                    _friendsCollection = null;
                }

                UnsubscribeAllFriendEntries();
                return;
            }

            if (ReferenceEquals(_friendsCollection, friends))
            {
                return;
            }

            if (_friendsCollection != null)
            {
                _friendsCollection.CollectionChanged -= OnFriendsCollectionChanged;
            }

            UnsubscribeAllFriendEntries();
            _friendsCollection = friends;
            _friendsCollection.CollectionChanged += OnFriendsCollectionChanged;
            SubscribeFriendEntries(_friendsCollection);
            MarkFriendsDirty();
        }

        private void SubscribeFriendEntries(IList<LaunchPluginFriendEntry> entries)
        {
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                SubscribeFriendEntry(entries[i]);
            }
        }

        private void SubscribeFriendEntry(LaunchPluginFriendEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (_friendEntrySubscriptions.Add(entry))
            {
                entry.PropertyChanged += OnFriendEntryPropertyChanged;
            }
        }

        private void UnsubscribeFriendEntry(LaunchPluginFriendEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (_friendEntrySubscriptions.Remove(entry))
            {
                entry.PropertyChanged -= OnFriendEntryPropertyChanged;
            }
        }

        private void UnsubscribeAllFriendEntries()
        {
            if (_friendEntrySubscriptions.Count == 0)
            {
                return;
            }

            foreach (var entry in _friendEntrySubscriptions)
            {
                if (entry != null)
                {
                    entry.PropertyChanged -= OnFriendEntryPropertyChanged;
                }
            }

            _friendEntrySubscriptions.Clear();
        }

        private void OnFriendsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            MarkFriendsDirty();
            if (e == null)
            {
                return;
            }

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                UnsubscribeAllFriendEntries();
                var entries = sender as IList<LaunchPluginFriendEntry>;
                if (entries != null)
                {
                    SubscribeFriendEntries(entries);
                }
                return;
            }

            if (e.OldItems != null)
            {
                for (int i = 0; i < e.OldItems.Count; i++)
                {
                    UnsubscribeFriendEntry(e.OldItems[i] as LaunchPluginFriendEntry);
                }
            }

            if (e.NewItems != null)
            {
                for (int i = 0; i < e.NewItems.Count; i++)
                {
                    SubscribeFriendEntry(e.NewItems[i] as LaunchPluginFriendEntry);
                }
            }
        }

        private void OnFriendEntryPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.PropertyName))
            {
                MarkFriendsDirty();
                return;
            }

            if (e.PropertyName == nameof(LaunchPluginFriendEntry.Name)
                || e.PropertyName == nameof(LaunchPluginFriendEntry.UserId)
                || e.PropertyName == nameof(LaunchPluginFriendEntry.Tag)
                || e.PropertyName == nameof(LaunchPluginFriendEntry.IsTeammate)
                || e.PropertyName == nameof(LaunchPluginFriendEntry.IsBad))
            {
                MarkFriendsDirty();
            }
        }

        private void RefreshFriendUserIds()
        {
            _friendUserIds.Clear();
            _teammateUserIds.Clear();
            _badUserIds.Clear();
            _friendsCount = 0;
            var friends = Settings?.Friends;
            if (friends == null)
            {
                return;
            }

            for (int i = 0; i < friends.Count; i++)
            {
                var entry = friends[i];
                if (entry == null)
                {
                    continue;
                }

                int userId = entry.UserId;
                if (userId > 0)
                {
                    _friendsCount++;
                    _friendUserIds.Add(userId);
                    if (entry.IsTeammate)
                    {
                        _teammateUserIds.Add(userId);
                    }

                    if (entry.IsBad)
                    {
                        _badUserIds.Add(userId);
                    }
                }
            }
        }

        private void UpdateCarSaFriendFlags(CarSASlot[] slots)
        {
            if (slots == null)
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null)
                {
                    continue;
                }

                int userId = slot.UserID;
                slot.IsFriend = userId > 0 && _friendUserIds.Contains(userId);
                slot.IsTeammate = userId > 0 && _teammateUserIds.Contains(userId);
                slot.IsBad = userId > 0 && _badUserIds.Contains(userId);
            }
        }

        private void UpdateCarSaPlayerFriendFlag()
        {
            var playerSlot = _carSaEngine?.Outputs?.PlayerSlot;
            if (playerSlot == null)
            {
                return;
            }

            int userId = playerSlot.UserID;
            playerSlot.IsFriend = userId > 0 && _friendUserIds.Contains(userId);
            playerSlot.IsTeammate = userId > 0 && _teammateUserIds.Contains(userId);
            playerSlot.IsBad = userId > 0 && _badUserIds.Contains(userId);
        }

        private void UpdateCarSaSlotStyles(CarSASlot[] slots, string playerClassColorHex)
        {
            if (slots == null)
            {
                return;
            }

            var statusMap = Settings?.CarSAStatusEBackgroundColors ?? DefaultCarSAStatusEBackgroundColors;
            var borderMap = Settings?.CarSABorderColors ?? DefaultCarSABorderColors;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null)
                {
                    continue;
                }

                bool isManualTeammate = slot.IsTeammate;
                bool isTelemetryTeammate = CanMarkTeammate(slot);
                if (!slot.StyleInputsChanged(slot.StatusE, slot.ClassColorHex, slot.PositionInClass, playerClassColorHex, slot.CarIdx, slot.IsValid, slot.IsFriend, isManualTeammate, isTelemetryTeammate, slot.IsBad))
                {
                    continue;
                }

                bool isOtherClass = IsOtherClassSlot(slot, playerClassColorHex);
                bool isClassLeader = slot.IsValid && slot.IsOnTrack && slot.PositionInClass == 1;
                CarSAStyleResolver.Resolve(
                    slot.StatusE,
                    slot.ClassColorHex,
                    slot.IsFriend,
                    isManualTeammate,
                    isTelemetryTeammate,
                    slot.IsBad,
                    isClassLeader,
                    isOtherClass,
                    statusMap,
                    borderMap,
                    out var statusBgHex,
                    out var borderMode,
                    out var borderHex);

                slot.StatusBgHex = statusBgHex;
                slot.BorderMode = borderMode;
                slot.BorderHex = borderHex;
            }
        }

        private static bool IsOtherClassSlot(CarSASlot slot, string playerClassColorHex)
        {
            if (slot == null)
            {
                return false;
            }

            if (!CarSAStyleResolver.IsValidHexColor(slot.ClassColorHex) || !CarSAStyleResolver.IsValidHexColor(playerClassColorHex))
            {
                return false;
            }

            return !string.Equals(slot.ClassColorHex, playerClassColorHex, StringComparison.OrdinalIgnoreCase);
        }

        private static bool CanMarkTeammate(CarSASlot slot)
        {
            _ = slot;
            // TODO: Wire teammate detection when shared teammate list settings are available.
            return false;
        }

        private void UpdateCarSaTransmitState(PluginManager pluginManager, CarSAOutputs outputs)
        {
            if (outputs == null)
            {
                return;
            }

            int transmitCarIdx = SafeReadInt(pluginManager, "DataCorePlugin.GameRawData.Telemetry.RadioTransmitCarIdx", -1);
            int transmitRadioIdx = SafeReadInt(pluginManager, "DataCorePlugin.GameRawData.Telemetry.RadioTransmitRadioIdx", -1);
            int transmitFrequencyIdx = SafeReadInt(pluginManager, "DataCorePlugin.GameRawData.Telemetry.RadioTransmitFrequencyIdx", -1);
            int playerCarIdx = SafeReadInt(pluginManager, "DataCorePlugin.GameRawData.Telemetry.PlayerCarIdx", -1);

            object radioInfo = null;
            try
            {
                radioInfo = pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.RadioInfo");
            }
            catch
            {
                radioInfo = null;
            }

            _radioFrequencyNameCache.EnsureBuilt(_currentSessionToken, radioInfo);
            UpdateLocalTxState(transmitRadioIdx, transmitFrequencyIdx);
            _radioIsPlayerTransmitting = transmitCarIdx >= 0 && playerCarIdx >= 0 && transmitCarIdx == playerCarIdx;

            if (transmitCarIdx < 0)
            {
                if (_lastTransmitCarIdx < 0)
                {
                    return;
                }

                ClearAllTransmitSlots(outputs);
                ResetTransmitState();
                return;
            }

            if (_carIdxToClassShortName.Count == 0)
            {
                RefreshClassMetadata(pluginManager);
            }

            UpdateTransmitClassPosLabel(transmitCarIdx);

            bool carChanged = transmitCarIdx != _lastTransmitCarIdx;
            bool radioChanged = transmitRadioIdx != _lastTransmitRadioIdx;
            bool frequencyChanged = transmitFrequencyIdx != _lastTransmitFrequencyIdx;
            bool changed = carChanged || radioChanged || frequencyChanged;

            if (carChanged || string.IsNullOrEmpty(_radioTransmitShortName) || string.IsNullOrEmpty(_radioTransmitFullName))
            {
                _radioTransmitShortName = ResolveTransmitShortName(pluginManager, transmitCarIdx);
                _radioTransmitFullName = ResolveTransmitFullName(pluginManager, transmitCarIdx);
            }

            if (changed || !_radioFrequencyNameCache.HasBuilt || _radioTransmitFrequencyEntry == null)
            {
                bool hasInfo = _radioFrequencyNameCache.TryGetInfo(transmitRadioIdx, transmitFrequencyIdx, out _, out _radioTransmitFrequencyEntry, out _radioTransmitFrequencyMutedAccessor);
                if (!hasInfo)
                {
                    if (_radioFrequencyNameCache.TryGetInfoByTransmittingCar(transmitRadioIdx, transmitCarIdx, out _, out var fallbackEntry, out var fallbackMutedAccessor))
                    {
                        _radioTransmitFrequencyEntry = fallbackEntry;
                        _radioTransmitFrequencyMutedAccessor = fallbackMutedAccessor;
                    }
                    else if (!hasInfo)
                    {
                        _radioTransmitFrequencyEntry = null;
                        _radioTransmitFrequencyMutedAccessor = null;
                        _radioTransmitFrequencyMuted = false;
                    }
                }
            }

            _radioTransmitFrequencyMuted = ReadTransmitFrequencyMuted();

            if (changed)
            {
                ClearAllTransmitSlots(outputs);
            }
            else if (_lastTransmitTalkingSlotKey >= 0)
            {
                ClearTransmitSlot(outputs, _lastTransmitTalkingSlotKey);
            }

            if (TryFindTransmitSlot(outputs, transmitCarIdx, out var slot, out var slotKey))
            {
                slot.SetTransmitState(true, transmitRadioIdx, transmitFrequencyIdx, string.Empty);
                _lastTransmitTalkingSlotKey = slotKey;
            }
            else
            {
                _lastTransmitTalkingSlotKey = -1;
            }

            _lastTransmitCarIdx = transmitCarIdx;
            _lastTransmitRadioIdx = transmitRadioIdx;
            _lastTransmitFrequencyIdx = transmitFrequencyIdx;
        }

        private void ResetTransmitState()
        {
            _lastTransmitCarIdx = -1;
            _lastTransmitRadioIdx = -1;
            _lastTransmitFrequencyIdx = -1;
            _radioTransmitShortName = string.Empty;
            _radioTransmitFullName = string.Empty;
            _radioTransmitFrequencyMuted = false;
            _radioTransmitFrequencyEntry = null;
            _radioTransmitFrequencyMutedAccessor = null;
            _lastTransmitTalkingSlotKey = -1;
            _radioTransmitClassPosLabel = string.Empty;
            _lastTransmitClassPosCarIdx = -1;
            _lastTransmitClassPosition = 0;
            _lastTransmitClassShort = string.Empty;
            _radioIsPlayerTransmitting = false;
        }

        private void ClearAllTransmitSlots(CarSAOutputs outputs)
        {
            if (outputs?.AheadSlots != null)
            {
                for (int i = 0; i < outputs.AheadSlots.Length; i++)
                {
                    outputs.AheadSlots[i]?.SetTransmitState(false, -1, -1, string.Empty);
                }
            }

            if (outputs?.BehindSlots != null)
            {
                for (int i = 0; i < outputs.BehindSlots.Length; i++)
                {
                    outputs.BehindSlots[i]?.SetTransmitState(false, -1, -1, string.Empty);
                }
            }
        }

        private void ClearTransmitSlot(CarSAOutputs outputs, int slotKey)
        {
            if (!TryGetSlotByKey(outputs, slotKey, out var slot))
            {
                return;
            }

            slot.SetTransmitState(false, -1, -1, string.Empty);
        }

        private bool TryGetSlotByKey(CarSAOutputs outputs, int slotKey, out CarSASlot slot)
        {
            slot = null;
            if (outputs == null || slotKey < 0)
            {
                return false;
            }

            if (slotKey >= TransmitSlotKeyBehindOffset)
            {
                int index = slotKey - TransmitSlotKeyBehindOffset;
                if (outputs.BehindSlots != null && index >= 0 && index < outputs.BehindSlots.Length)
                {
                    slot = outputs.BehindSlots[index];
                }
            }
            else
            {
                int index = slotKey;
                if (outputs.AheadSlots != null && index >= 0 && index < outputs.AheadSlots.Length)
                {
                    slot = outputs.AheadSlots[index];
                }
            }

            return slot != null;
        }

        private bool TryFindTransmitSlot(CarSAOutputs outputs, int transmitCarIdx, out CarSASlot slot, out int slotKey)
        {
            slot = null;
            slotKey = -1;
            if (outputs == null || transmitCarIdx < 0)
            {
                return false;
            }

            if (outputs.AheadSlots != null)
            {
                for (int i = 0; i < outputs.AheadSlots.Length; i++)
                {
                    var candidate = outputs.AheadSlots[i];
                    if (candidate != null && candidate.IsValid && candidate.CarIdx == transmitCarIdx)
                    {
                        slot = candidate;
                        slotKey = i;
                        return true;
                    }
                }
            }

            if (outputs.BehindSlots != null)
            {
                for (int i = 0; i < outputs.BehindSlots.Length; i++)
                {
                    var candidate = outputs.BehindSlots[i];
                    if (candidate != null && candidate.IsValid && candidate.CarIdx == transmitCarIdx)
                    {
                        slot = candidate;
                        slotKey = TransmitSlotKeyBehindOffset + i;
                        return true;
                    }
                }
            }

            return false;
        }

        private void UpdateTransmitClassPosLabel(int transmitCarIdx)
        {
            if (transmitCarIdx < 0)
            {
                _radioTransmitClassPosLabel = string.Empty;
                _lastTransmitClassPosCarIdx = -1;
                _lastTransmitClassPosition = 0;
                _lastTransmitClassShort = string.Empty;
                return;
            }

            int positionInClass = 0;
            string classShort = string.Empty;
            TryGetCarIdxIdentity(transmitCarIdx, out positionInClass, out classShort);

            bool changed = transmitCarIdx != _lastTransmitClassPosCarIdx
                || positionInClass != _lastTransmitClassPosition
                || !string.Equals(classShort ?? string.Empty, _lastTransmitClassShort ?? string.Empty, StringComparison.Ordinal);

            if (!changed)
            {
                return;
            }

            _lastTransmitClassPosCarIdx = transmitCarIdx;
            _lastTransmitClassPosition = positionInClass;
            _lastTransmitClassShort = classShort ?? string.Empty;

            if (positionInClass > 0)
            {
                _radioTransmitClassPosLabel = !string.IsNullOrWhiteSpace(classShort)
                    ? $"{classShort} P{positionInClass}"
                    : $"P{positionInClass}";
            }
            else
            {
                _radioTransmitClassPosLabel = string.Empty;
            }
        }

        private bool ReadTransmitFrequencyMuted()
        {
            if (_radioTransmitFrequencyEntry == null || _radioTransmitFrequencyMutedAccessor == null)
            {
                return false;
            }

            try
            {
                return _radioTransmitFrequencyMutedAccessor(_radioTransmitFrequencyEntry);
            }
            catch
            {
                return false;
            }
        }

        private void UpdateLocalTxState(int transmitRadioIdx, int transmitFrequencyIdx)
        {
            int localRadioIdx = transmitRadioIdx;
            int localFrequencyNum = -1;

            if (localRadioIdx >= 0 && _radioFrequencyNameCache.TryGetTunedFrequencyNum(localRadioIdx, out var tunedFrequencyNum))
            {
                localFrequencyNum = tunedFrequencyNum;
            }
            else
            {
                localFrequencyNum = transmitFrequencyIdx;
            }

            bool localChanged = localRadioIdx != _localTxRadioIdx
                                || localFrequencyNum != _localTxFrequencyNum
                                || !_radioFrequencyNameCache.HasBuilt
                                || _localTxFrequencyEntry == null;

            _localTxRadioIdx = localRadioIdx;
            _localTxFrequencyNum = localFrequencyNum;

            if (localChanged)
            {
                if (localRadioIdx >= 0 && localFrequencyNum >= 0
                    && _radioFrequencyNameCache.TryGetInfo(localRadioIdx, localFrequencyNum, out var localName, out var localEntry, out var localMutedAccessor))
                {
                    _localTxFrequencyName = localName ?? string.Empty;
                    _localTxFrequencyEntry = localEntry;
                    _localTxFrequencyMutedAccessor = localMutedAccessor;
                }
                else
                {
                    _localTxFrequencyName = string.Empty;
                    _localTxFrequencyEntry = null;
                    _localTxFrequencyMutedAccessor = null;
                    _localTxFrequencyMuted = false;
                }
            }

            _localTxFrequencyMuted = ReadLocalTxFrequencyMuted();
        }

        private bool ReadLocalTxFrequencyMuted()
        {
            if (_localTxFrequencyEntry == null || _localTxFrequencyMutedAccessor == null)
            {
                return false;
            }

            try
            {
                return _localTxFrequencyMutedAccessor(_localTxFrequencyEntry);
            }
            catch
            {
                return false;
            }
        }

        private string ResolveTransmitShortName(PluginManager pluginManager, int carIdx)
        {
            if (pluginManager == null || carIdx < 0)
            {
                return string.Empty;
            }

            for (int i = 1; i <= 64; i++)
            {
                string basePath = $"DataCorePlugin.GameRawData.SessionData.DriverInfo.Drivers{i:00}";
                int idx = GetInt(pluginManager, $"{basePath}.CarIdx", int.MinValue);
                if (idx == int.MinValue || idx != carIdx)
                {
                    continue;
                }

                return GetString(pluginManager, $"{basePath}.AbbrevName") ?? string.Empty;
            }

            return string.Empty;
        }

        private string ResolveTransmitFullName(PluginManager pluginManager, int carIdx)
        {
            if (pluginManager == null || carIdx < 0)
            {
                return string.Empty;
            }

            for (int i = 1; i <= 64; i++)
            {
                string basePath = $"DataCorePlugin.GameRawData.SessionData.DriverInfo.Drivers{i:00}";
                int idx = GetInt(pluginManager, $"{basePath}.CarIdx", int.MinValue);
                if (idx == int.MinValue || idx != carIdx)
                {
                    continue;
                }

                string fullName = GetString(pluginManager, $"{basePath}.UserName");
                if (string.IsNullOrWhiteSpace(fullName))
                {
                    fullName = GetString(pluginManager, $"{basePath}.DisplayName");
                }

                if (string.IsNullOrWhiteSpace(fullName))
                {
                    fullName = GetString(pluginManager, $"{basePath}.Name");
                }

                if (!string.IsNullOrWhiteSpace(fullName))
                {
                    return fullName.Trim();
                }

                string firstName = GetString(pluginManager, $"{basePath}.FirstName");
                string lastName = GetString(pluginManager, $"{basePath}.LastName");
                if (!string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(lastName))
                {
                    return $"{firstName.Trim()} {lastName.Trim()}";
                }

                if (!string.IsNullOrWhiteSpace(firstName))
                {
                    return firstName.Trim();
                }

                if (!string.IsNullOrWhiteSpace(lastName))
                {
                    return lastName.Trim();
                }

                return string.Empty;
            }

            return string.Empty;
        }

        private bool TryGetCarIdxIdentity(int carIdx, out int positionInClass, out string classShort)
        {
            positionInClass = 0;
            classShort = string.Empty;

            if (carIdx < 0 || carIdx >= _carSaClassPositionByIdx.Length)
            {
                return false;
            }

            positionInClass = _carSaClassPositionByIdx[carIdx];
            classShort = GetCachedClassShortName(carIdx) ?? string.Empty;
            return positionInClass > 0 || !string.IsNullOrWhiteSpace(classShort);
        }

        private void UpdateCarSaSlotTelemetry(PluginManager pluginManager, CarSASlot[] slots, float[] carIdxLapDistPct, double sessionTimeSec)
        {
            if (slots == null)
            {
                return;
            }

            foreach (var slot in slots)
            {
                if (slot == null)
                {
                    continue;
                }

                int carIdx = slot.CarIdx;
                if (carIdx < 0 || carIdx >= CarSAEngine.MaxCars)
                {
                    slot.PositionInClass = 0;
                    slot.ClassName = string.Empty;
                    slot.ClassColorHex = string.Empty;
                    slot.CarClassShortName = string.Empty;
                    slot.Initials = string.Empty;
                    slot.AbbrevName = string.Empty;
                    slot.IRating = 0;
                    slot.Licence = string.Empty;
                    slot.SafetyRating = double.NaN;
                    slot.LicLevel = 0;
                    slot.UserID = 0;
                    slot.TeamID = 0;
                    slot.IsFriend = false;
                    slot.IsTeammate = false;
                    slot.IsBad = false;
                    slot.LapsSincePit = -1;
                    slot.BestLapTimeSec = double.NaN;
                    slot.LastLapTimeSec = double.NaN;
                    slot.BestLap = "-";
                    slot.BestLapIsEstimated = false;
                    slot.LastLap = "-";
                    slot.DeltaBestSec = double.NaN;
                    slot.DeltaBest = "-";
                    slot.EstLapTimeSec = double.NaN;
                    slot.EstLapTime = "-";
                    slot.LapTimeUpdate = string.Empty;
                    slot.LapTimeUpdateVisibilitySec = 0.0;
                    slot.GapRelativeSec = double.NaN;
                    slot.GapRelativeSource = 0;
                    slot.HotScore = 0.0;
                    slot.HotVia = string.Empty;
                    continue;
                }

                slot.PositionInClass = _carSaClassPositionByIdx[carIdx] > 0 ? _carSaClassPositionByIdx[carIdx] : 0;
                double bestLapUsedSec = _carSaBestLapTimeSecByIdx[carIdx];
                bool bestLapIsEstimated = false;
                if (!IsValidCarSaLapTimeSec(bestLapUsedSec))
                {
                    double classEstimateSec = _carSaCarClassEstLapTimeSecByIdx[carIdx];
                    if (IsValidCarSaLapTimeSec(classEstimateSec))
                    {
                        bestLapUsedSec = classEstimateSec;
                        bestLapIsEstimated = true;
                        if (!_carSaBestLapFallbackInfoLogged)
                        {
                            _carSaBestLapFallbackInfoLogged = true;
                            SimHub.Logging.Current.Info("[CarSA] BestLap fallback now using DriverInfo CarClassEstLapTime until best lap available.");
                        }
                    }
                    else
                    {
                        bestLapUsedSec = double.NaN;
                    }
                }

                slot.BestLapTimeSec = bestLapUsedSec;
                slot.LastLapTimeSec = _carSaLastLapTimeSecByIdx[carIdx];
                slot.EstLapTimeSec = _carSaEstTimeSecByIdx[carIdx];
                slot.BestLap = FormatLapTime(slot.BestLapTimeSec);
                slot.BestLapIsEstimated = bestLapIsEstimated;
                slot.LastLap = FormatLapTime(slot.LastLapTimeSec);
                slot.EstLapTime = FormatEstLapTime(slot.EstLapTimeSec);
                double lapTimeUpdateExpirySec = _carSaLapTimeUpdateExpireAtSecByIdx[carIdx];
                double lapTimeUpdateRemainingSec = (!double.IsNaN(lapTimeUpdateExpirySec) && lapTimeUpdateExpirySec > sessionTimeSec)
                    ? (lapTimeUpdateExpirySec - sessionTimeSec)
                    : 0.0;
                slot.LapTimeUpdate = _carSaLapTimeUpdateByIdx[carIdx] ?? string.Empty;
                slot.LapTimeUpdateVisibilitySec = lapTimeUpdateRemainingSec > 0.0 ? lapTimeUpdateRemainingSec : 0.0;
                if (_carSaEngine != null && _carSaEngine.ShouldUpdateMiniSectorForCar(carIdx))
                {
                    slot.DeltaBestSec = ComputeLiveDeltaBestSec(carIdx, carIdxLapDistPct, sessionTimeSec, bestLapUsedSec);
                    slot.DeltaBest = FormatLiveDelta(slot.DeltaBestSec);
                }
                slot.HotScore = 0.0;
                slot.HotVia = string.Empty;

                if (TryGetCarDriverInfo(pluginManager, carIdx, out string className, out string classColorHex, out int iRating, out string licString,
                    out string classShortName, out string initials, out string abbrevName, out int licLevel, out int userId, out int teamId))
                {
                    slot.ClassName = className ?? string.Empty;
                    slot.ClassColorHex = classColorHex ?? string.Empty;
                    slot.CarClassShortName = classShortName ?? string.Empty;
                    slot.Initials = initials ?? string.Empty;
                    slot.AbbrevName = abbrevName ?? string.Empty;
                    slot.IRating = iRating;
                    if (TryParseLicenseString(licString, out string licence, out double safetyRating))
                    {
                        slot.Licence = licence;
                        slot.SafetyRating = safetyRating;
                    }
                    else
                    {
                        slot.Licence = string.Empty;
                        slot.SafetyRating = double.NaN;
                    }
                    slot.LicLevel = licLevel;
                    slot.UserID = userId;
                    slot.TeamID = teamId;
                }
                if (string.IsNullOrWhiteSpace(slot.ClassColorHex) && !string.IsNullOrWhiteSpace(slot.ClassColor))
                {
                    slot.ClassColorHex = NormalizeClassColorHex(slot.ClassColor);
                }
            }
        }

        private void UpdateCarSaPlayerTelemetry(PluginManager pluginManager, int playerCarIdx, double sessionTimeSec)
        {
            if (_carSaEngine?.Outputs?.PlayerSlot == null)
            {
                return;
            }

            CarSASlot playerSlot = _carSaEngine.Outputs.PlayerSlot;
            if (playerCarIdx < 0 || playerCarIdx >= CarSAEngine.MaxCars)
            {
                playerSlot.ClassName = string.Empty;
                playerSlot.ClassColor = string.Empty;
                playerSlot.ClassColorHex = string.Empty;
                playerSlot.CarClassShortName = string.Empty;
                playerSlot.Initials = string.Empty;
                playerSlot.AbbrevName = string.Empty;
                playerSlot.IRating = 0;
                playerSlot.Licence = string.Empty;
                playerSlot.SafetyRating = double.NaN;
                playerSlot.LicLevel = 0;
                playerSlot.UserID = 0;
                playerSlot.TeamID = 0;
                playerSlot.IsFriend = false;
                playerSlot.IsTeammate = false;
                playerSlot.IsBad = false;
                playerSlot.LapTimeUpdate = string.Empty;
                playerSlot.LapTimeUpdateVisibilitySec = 0.0;
                return;
            }

            double lapTimeUpdateExpirySec = _carSaLapTimeUpdateExpireAtSecByIdx[playerCarIdx];
            double lapTimeUpdateRemainingSec = (!double.IsNaN(lapTimeUpdateExpirySec) && lapTimeUpdateExpirySec > sessionTimeSec)
                ? (lapTimeUpdateExpirySec - sessionTimeSec)
                : 0.0;
            playerSlot.LapTimeUpdate = _carSaLapTimeUpdateByIdx[playerCarIdx] ?? string.Empty;
            playerSlot.LapTimeUpdateVisibilitySec = lapTimeUpdateRemainingSec > 0.0 ? lapTimeUpdateRemainingSec : 0.0;

            if (TryGetCarIdentityFromSessionInfo(pluginManager, playerCarIdx, out _, out _, out string classColor))
            {
                if (!string.IsNullOrWhiteSpace(classColor))
                {
                    playerSlot.ClassColor = classColor;
                }
            }

            if (TryGetCarDriverInfo(pluginManager, playerCarIdx, out string className, out string classColorHex, out int iRating, out string licString,
                out string classShortName, out string initials, out string abbrevName, out int licLevel, out int userId, out int teamId))
            {
                playerSlot.ClassName = className ?? string.Empty;
                playerSlot.ClassColorHex = classColorHex ?? string.Empty;
                playerSlot.CarClassShortName = classShortName ?? string.Empty;
                playerSlot.Initials = initials ?? string.Empty;
                playerSlot.AbbrevName = abbrevName ?? string.Empty;
                playerSlot.IRating = iRating;
                if (TryParseLicenseString(licString, out string licence, out double safetyRating))
                {
                    playerSlot.Licence = licence;
                    playerSlot.SafetyRating = safetyRating;
                }
                else
                {
                    playerSlot.Licence = string.Empty;
                    playerSlot.SafetyRating = double.NaN;
                }
                playerSlot.LicLevel = licLevel;
                playerSlot.UserID = userId;
                playerSlot.TeamID = teamId;
            }

            if (string.IsNullOrWhiteSpace(playerSlot.ClassColorHex) && !string.IsNullOrWhiteSpace(playerSlot.ClassColor))
            {
                playerSlot.ClassColorHex = NormalizeClassColorHex(playerSlot.ClassColor);
            }
        }

        private double ComputeLiveDeltaBestSec(int carIdx, float[] carIdxLapDistPct, double sessionTimeSec, double baselineSec)
        {
            if (carIdx < 0 || carIdx >= CarSAEngine.MaxCars)
            {
                return double.NaN;
            }

            if (!IsValidCarSaLapTimeSec(baselineSec))
            {
                return double.NaN;
            }

            double lapPct = double.NaN;
            if (carIdxLapDistPct != null && carIdx < carIdxLapDistPct.Length)
            {
                lapPct = carIdxLapDistPct[carIdx];
            }

            if (double.IsNaN(lapPct) || lapPct < 0.0 || lapPct > 1.0)
            {
                return double.NaN;
            }

            if (_carSaEngine == null || !_carSaEngine.TryGetLapStartTimeSec(carIdx, out double lapStartTimeSec))
            {
                return double.NaN;
            }

            if (double.IsNaN(sessionTimeSec) || sessionTimeSec < lapStartTimeSec)
            {
                return double.NaN;
            }

            double elapsed = sessionTimeSec - lapStartTimeSec;
            double expected = baselineSec * lapPct;
            double delta = elapsed - expected;
            if (delta < -LiveDeltaClampSec)
            {
                delta = -LiveDeltaClampSec;
            }
            else if (delta > LiveDeltaClampSec)
            {
                delta = LiveDeltaClampSec;
            }
            return delta;
        }

        private static bool IsValidCarSaLapTimeSec(double value)
        {
            return value > 0.0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double SanitizeCarSaLapTimeSec(double value)
        {
            return IsValidCarSaLapTimeSec(value) ? value : double.NaN;
        }

        private static double ReadCarIdxTime(float[] values, int index)
        {
            if (values == null || index < 0 || index >= values.Length)
            {
                return double.NaN;
            }

            double value = values[index];
            if (!(value > 0.0) || double.IsNaN(value) || double.IsInfinity(value))
            {
                return double.NaN;
            }

            return value;
        }

        private static string FormatLapTime(double seconds)
        {
            if (!(seconds > 0.0) || double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                return "-";
            }

            int totalMs = (int)Math.Round(seconds * 1000.0, MidpointRounding.AwayFromZero);
            int minutes = totalMs / 60000;
            int secondsPart = (totalMs / 1000) % 60;
            int hundredths = (totalMs % 1000) / 10;
            return $"{minutes}:{secondsPart:00}.{hundredths:00}";
        }

        private static string FormatEstLapTime(double seconds)
        {
            if (!(seconds > 0.0) || double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                return "-";
            }

            return TimeSpan.FromSeconds(seconds).ToString(@"m\:ss\.ff", CultureInfo.InvariantCulture);
        }

        private static string FormatLiveDelta(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                return "-";
            }

            return seconds.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
        }

        private static bool TryParseLicenseString(string licString, out string licence, out double safetyRating)
        {
            licence = string.Empty;
            safetyRating = double.NaN;

            if (string.IsNullOrWhiteSpace(licString))
            {
                return false;
            }

            string trimmed = licString.Trim();
            string[] parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                licence = parts[0];
            }

            if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                safetyRating = parsed;
                return true;
            }

            int numericStart = -1;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if ((c >= '0' && c <= '9') || c == '.')
                {
                    numericStart = i;
                    break;
                }
            }

            if (numericStart >= 0)
            {
                string numeric = trimmed.Substring(numericStart);
                if (double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                {
                    safetyRating = parsed;
                    if (string.IsNullOrWhiteSpace(licence))
                    {
                        licence = trimmed.Substring(0, numericStart).Trim();
                    }
                    return true;
                }
            }

            return !string.IsNullOrWhiteSpace(licence);
        }

        private void ApplyCarSaIdentityRefresh(PluginManager pluginManager, CarSASlot[] slots, int[] lastCarIdx, bool forceRefresh, double sessionTimeSec)
        {
            if (slots == null || lastCarIdx == null) return;

            int count = Math.Min(slots.Length, lastCarIdx.Length);
            for (int i = 0; i < count; i++)
            {
                var slot = slots[i];
                int carIdx = slot?.CarIdx ?? -1;
                bool carIdxChanged = carIdx != lastCarIdx[i];

                if (carIdxChanged)
                {
                    lastCarIdx[i] = carIdx;
                    if (slot != null)
                    {
                        slot.IdentityResolved = false;
                        slot.LastIdentityAttemptSessionTimeSec = -1.0;
                    }
                }

                if (slot == null || carIdx < 0)
                {
                    if (slot != null)
                    {
                        slot.Name = string.Empty;
                        slot.CarNumber = string.Empty;
                        slot.ClassColor = string.Empty;
                        slot.IdentityResolved = false;
                        slot.LastIdentityAttemptSessionTimeSec = -1.0;
                        slot.StatusETextDirty = true;
                    }
                    continue;
                }

                bool needsIdentity = string.IsNullOrWhiteSpace(slot.ClassColor)
                    || string.IsNullOrWhiteSpace(slot.CarNumber)
                    || string.IsNullOrWhiteSpace(slot.Name);
                bool allowRetry = !slot.IdentityResolved && needsIdentity;
                bool shouldAttempt = forceRefresh || carIdxChanged || allowRetry;
                if (!shouldAttempt)
                {
                    continue;
                }

                if (!forceRefresh && !carIdxChanged && allowRetry)
                {
                    if (slot.LastIdentityAttemptSessionTimeSec >= 0.0
                        && sessionTimeSec - slot.LastIdentityAttemptSessionTimeSec < 0.5)
                    {
                        continue;
                    }
                }

                slot.LastIdentityAttemptSessionTimeSec = sessionTimeSec;

                if (forceRefresh || carIdxChanged)
                {
                    slot.Name = string.Empty;
                    slot.CarNumber = string.Empty;
                    slot.ClassColor = string.Empty;
                    slot.StatusETextDirty = true;
                }

                bool identityUpdated = false;
                string previousName = slot.Name;
                string previousCarNumber = slot.CarNumber;
                string previousClassColor = slot.ClassColor;

                if (TryGetCarIdentityFromSessionInfo(pluginManager, carIdx, out var name, out var carNumber, out var classColor))
                {
                    if (!string.IsNullOrWhiteSpace(name)) slot.Name = name;
                    if (!string.IsNullOrWhiteSpace(carNumber)) slot.CarNumber = carNumber;
                    if (!string.IsNullOrWhiteSpace(classColor)) slot.ClassColor = classColor;
                }
                else if (carIdxChanged)
                {
                    if (IsVerboseDebugLoggingOn)
                    {
                        SimHub.Logging.Current.Debug($"[LalaPlugin:CarSA] Identity unresolved for carIdx={carIdx} after slot change.");
                    }
                }

                if (!string.Equals(previousName, slot.Name, StringComparison.Ordinal))
                {
                    identityUpdated = true;
                }
                if (!string.Equals(previousCarNumber, slot.CarNumber, StringComparison.Ordinal))
                {
                    identityUpdated = true;
                }
                if (!string.Equals(previousClassColor, slot.ClassColor, StringComparison.Ordinal))
                {
                    identityUpdated = true;
                }

                if (identityUpdated)
                {
                    slot.StatusETextDirty = true;
                }

                if (!string.IsNullOrWhiteSpace(slot.ClassColor))
                {
                    slot.IdentityResolved = true;
                }
            }
        }

        private bool TryGetCarIdentityFromSessionInfo(PluginManager pluginManager, int carIdx, out string name, out string carNumber, out string classColor)
        {
            name = string.Empty;
            carNumber = string.Empty;
            classColor = string.Empty;

            if (pluginManager == null || carIdx < 0)
            {
                return false;
            }

            if (TryGetCarIdentityFromDriversTable(pluginManager, carIdx, out name, out carNumber, out classColor))
            {
                return true;
            }

            return TryGetCarIdentityFromCompetingDrivers(pluginManager, carIdx, out name, out carNumber, out classColor);
        }

        private bool TryGetCarIdentityFromDriversTable(PluginManager pluginManager, int carIdx, out string name, out string carNumber, out string classColor)
        {
            name = string.Empty;
            carNumber = string.Empty;
            classColor = string.Empty;

            for (int i = 1; i <= 64; i++)
            {
                string basePath = $"DataCorePlugin.GameRawData.SessionData.DriverInfo.Drivers{i:00}";
                int idx = GetInt(pluginManager, $"{basePath}.CarIdx", int.MinValue);
                if (idx == int.MinValue || idx != carIdx)
                {
                    continue;
                }

                name = GetString(pluginManager, $"{basePath}.UserName") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = GetString(pluginManager, $"{basePath}.AbbrevName") ?? string.Empty;
                }

                carNumber = GetString(pluginManager, $"{basePath}.CarNumber") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(carNumber))
                {
                    int numberRaw = GetInt(pluginManager, $"{basePath}.CarNumberRaw", int.MinValue);
                    if (numberRaw != int.MinValue)
                    {
                        carNumber = numberRaw.ToString(CultureInfo.InvariantCulture);
                    }
                }

                classColor = GetCarClassColorHex(pluginManager, $"{basePath}.CarClassColor");

                return true;
            }

            return false;
        }

        private bool TryGetCarIdentityFromCompetingDrivers(PluginManager pluginManager, int carIdx, out string name, out string carNumber, out string classColor)
        {
            name = string.Empty;
            carNumber = string.Empty;
            classColor = string.Empty;

            for (int i = 0; i < 64; i++)
            {
                int idx = GetInt(pluginManager, $"DataCorePlugin.GameRawData.SessionData.DriverInfo.CompetingDrivers[{i}].CarIdx", int.MinValue);
                if (idx == int.MinValue)
                {
                    break;
                }

                if (idx != carIdx)
                {
                    continue;
                }

                name = GetString(pluginManager, $"DataCorePlugin.GameRawData.SessionData.DriverInfo.CompetingDrivers[{i}].UserName") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = GetString(pluginManager, $"DataCorePlugin.GameRawData.SessionData.DriverInfo.CompetingDrivers[{i}].TeamName") ?? string.Empty;
                }
                carNumber = GetString(pluginManager, $"DataCorePlugin.GameRawData.SessionData.DriverInfo.CompetingDrivers[{i}].CarNumber") ?? string.Empty;
                classColor = GetCarClassColorHex(pluginManager, $"DataCorePlugin.GameRawData.SessionData.DriverInfo.CompetingDrivers[{i}].CarClassColor");
                return true;
            }

            return false;
        }

        private bool TryGetCarDriverInfo(PluginManager pluginManager, int carIdx, out string className, out string classColorHex, out int iRating, out string licString,
            out string classShortName, out string initials, out string abbrevName, out int licLevel, out int userId, out int teamId)
        {
            className = string.Empty;
            classColorHex = string.Empty;
            iRating = 0;
            licString = string.Empty;
            classShortName = string.Empty;
            initials = string.Empty;
            abbrevName = string.Empty;
            licLevel = 0;
            userId = 0;
            teamId = 0;

            if (pluginManager == null || carIdx < 0)
            {
                return false;
            }

            if (TryGetCarDriverInfoFromDriversTable(pluginManager, carIdx, out className, out classColorHex, out iRating, out licString,
                out classShortName, out initials, out abbrevName, out licLevel, out userId, out teamId))
            {
                return true;
            }

            return TryGetCarDriverInfoFromCompetingDrivers(pluginManager, carIdx, out className, out classColorHex, out iRating, out licString,
                out classShortName, out initials, out abbrevName, out licLevel, out userId, out teamId);
        }

        private bool TryGetCarDriverInfoFromDriversTable(PluginManager pluginManager, int carIdx, out string className, out string classColorHex, out int iRating,
            out string licString, out string classShortName, out string initials, out string abbrevName, out int licLevel, out int userId, out int teamId)
        {
            className = string.Empty;
            classColorHex = string.Empty;
            iRating = 0;
            licString = string.Empty;
            classShortName = string.Empty;
            initials = string.Empty;
            abbrevName = string.Empty;
            licLevel = 0;
            userId = 0;
            teamId = 0;

            for (int i = 1; i <= 64; i++)
            {
                string basePath = $"DataCorePlugin.GameRawData.SessionData.DriverInfo.Drivers{i:00}";
                int idx = GetInt(pluginManager, $"{basePath}.CarIdx", int.MinValue);
                if (idx == int.MinValue || idx != carIdx)
                {
                    continue;
                }

                classShortName = GetString(pluginManager, $"{basePath}.CarClassShortName") ?? string.Empty;
                className = classShortName;
                if (string.IsNullOrWhiteSpace(className))
                {
                    className = GetString(pluginManager, $"{basePath}.CarClassName") ?? string.Empty;
                }

                classColorHex = GetCarClassColorHexHash(pluginManager, $"{basePath}.CarClassColor");
                iRating = GetInt(pluginManager, $"{basePath}.IRating", 0);
                licString = GetString(pluginManager, $"{basePath}.LicString") ?? string.Empty;
                initials = GetString(pluginManager, $"{basePath}.Initials") ?? string.Empty;
                abbrevName = GetString(pluginManager, $"{basePath}.AbbrevName") ?? string.Empty;
                licLevel = GetInt(pluginManager, $"{basePath}.LicLevel", 0);
                userId = GetInt(pluginManager, $"{basePath}.UserID", 0);
                teamId = GetInt(pluginManager, $"{basePath}.TeamID", 0);
                return true;
            }

            return false;
        }

        private bool TryGetCarDriverInfoFromCompetingDrivers(PluginManager pluginManager, int carIdx, out string className, out string classColorHex, out int iRating,
            out string licString, out string classShortName, out string initials, out string abbrevName, out int licLevel, out int userId, out int teamId)
        {
            className = string.Empty;
            classColorHex = string.Empty;
            iRating = 0;
            licString = string.Empty;
            classShortName = string.Empty;
            initials = string.Empty;
            abbrevName = string.Empty;
            licLevel = 0;
            userId = 0;
            teamId = 0;

            for (int i = 0; i < 64; i++)
            {
                string basePath = $"DataCorePlugin.GameRawData.SessionData.DriverInfo.CompetingDrivers[{i}]";
                int idx = GetInt(pluginManager, $"{basePath}.CarIdx", int.MinValue);
                if (idx == int.MinValue)
                {
                    break;
                }

                if (idx != carIdx)
                {
                    continue;
                }

                classShortName = GetString(pluginManager, $"{basePath}.CarClassShortName") ?? string.Empty;
                className = classShortName;
                if (string.IsNullOrWhiteSpace(className))
                {
                    className = GetString(pluginManager, $"{basePath}.CarClassName") ?? string.Empty;
                }

                classColorHex = GetCarClassColorHexHash(pluginManager, $"{basePath}.CarClassColor");
                iRating = GetInt(pluginManager, $"{basePath}.IRating", 0);
                licString = GetString(pluginManager, $"{basePath}.LicString") ?? string.Empty;
                initials = GetString(pluginManager, $"{basePath}.Initials") ?? string.Empty;
                abbrevName = GetString(pluginManager, $"{basePath}.AbbrevName") ?? string.Empty;
                licLevel = GetInt(pluginManager, $"{basePath}.LicLevel", 0);
                userId = GetInt(pluginManager, $"{basePath}.UserID", 0);
                teamId = GetInt(pluginManager, $"{basePath}.TeamID", 0);
                return true;
            }

            return false;
        }

        private static string GetCarClassColorHex(PluginManager pluginManager, string propertyName)
        {
            if (pluginManager == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return string.Empty;
            }

            try
            {
                var raw = pluginManager.GetPropertyValue(propertyName);
                if (raw == null) return string.Empty;

                if (raw is int intValue)
                {
                    return FormatCarClassColor(intValue);
                }

                if (raw is long longValue)
                {
                    return FormatCarClassColor((int)longValue);
                }

                if (raw is uint uintValue)
                {
                    return FormatCarClassColor((int)uintValue);
                }

                string rawText = Convert.ToString(raw, CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;

                rawText = rawText.Trim();
                if (rawText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    rawText = rawText.Substring(2);
                }

                if (int.TryParse(rawText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int parsedHex))
                {
                    return FormatCarClassColor(parsedHex);
                }

                if (int.TryParse(rawText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt))
                {
                    return FormatCarClassColor(parsedInt);
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetCarClassColorHexHash(PluginManager pluginManager, string propertyName)
        {
            if (pluginManager == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return string.Empty;
            }

            try
            {
                var raw = pluginManager.GetPropertyValue(propertyName);
                if (raw == null) return string.Empty;

                if (raw is int intValue)
                {
                    return FormatCarClassColorHex(intValue);
                }

                if (raw is long longValue)
                {
                    return FormatCarClassColorHex((int)longValue);
                }

                if (raw is uint uintValue)
                {
                    return FormatCarClassColorHex((int)uintValue);
                }

                string rawText = Convert.ToString(raw, CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;

                rawText = rawText.Trim();
                if (rawText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    rawText = rawText.Substring(2);
                }
                if (rawText.StartsWith("#", StringComparison.OrdinalIgnoreCase))
                {
                    rawText = rawText.Substring(1);
                }

                if (int.TryParse(rawText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int parsedHex))
                {
                    return FormatCarClassColorHex(parsedHex);
                }

                if (int.TryParse(rawText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt))
                {
                    return FormatCarClassColorHex(parsedInt);
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string FormatCarClassColor(int colorRaw)
        {
            int rgb = colorRaw & 0xFFFFFF;
            return $"0x{rgb:X6}";
        }

        private static string FormatCarClassColorHex(int colorRaw)
        {
            int rgb = colorRaw & 0xFFFFFF;
            return $"#{rgb:X6}";
        }

        private static string NormalizeClassColorHex(string classColor)
        {
            if (string.IsNullOrWhiteSpace(classColor))
            {
                return string.Empty;
            }

            string trimmed = classColor.Trim();
            if (trimmed.StartsWith("#", StringComparison.OrdinalIgnoreCase) && trimmed.Length == 7)
            {
                return trimmed.ToUpperInvariant();
            }

            string rawText = trimmed;
            if (rawText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                rawText = rawText.Substring(2);
            }
            if (rawText.StartsWith("#", StringComparison.OrdinalIgnoreCase))
            {
                rawText = rawText.Substring(1);
            }

            if (int.TryParse(rawText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int parsedHex))
            {
                return FormatCarClassColorHex(parsedHex);
            }
            if (int.TryParse(rawText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt))
            {
                return FormatCarClassColorHex(parsedInt);
            }

            return string.Empty;
        }

        private bool IsCarSaIdentitySourceReady(PluginManager pluginManager)
        {
            if (pluginManager == null) return false;
            int driversIdx = GetInt(pluginManager, "DataCorePlugin.GameRawData.SessionData.DriverInfo.Drivers01.CarIdx", int.MinValue);
            if (driversIdx != int.MinValue) return true;

            int competingIdx = GetInt(pluginManager, "DataCorePlugin.GameRawData.SessionData.DriverInfo.CompetingDrivers[0].CarIdx", int.MinValue);
            return competingIdx != int.MinValue;
        }

        private void UpdateOpponentsAndPitExit(GameData data, PluginManager pluginManager, int completedLaps, string sessionTypeToken)
        {
            if (_opponentsEngine == null) return;

            double myPaceSec = Pace_StintAvgLapTimeSec;
            if (myPaceSec <= 0.0) myPaceSec = Pace_Last5LapAvgSec;
            if (myPaceSec <= 0.0 && _lastSeenBestLap > TimeSpan.Zero) myPaceSec = _lastSeenBestLap.TotalSeconds;

            double pitLossSec = CalculateTotalStopLossSeconds();
            if (double.IsNaN(pitLossSec) || double.IsInfinity(pitLossSec) || pitLossSec < 0.0)
            {
                try
                {
                    double fromExport = Convert.ToDouble(pluginManager.GetPropertyValue("LalaLaunch.Fuel.Live.TotalStopLoss") ?? double.NaN);
                    if (!double.IsNaN(fromExport) && !double.IsInfinity(fromExport))
                    {
                        pitLossSec = fromExport;
                    }
                }
                catch
                {
                    // ignore and keep evaluating fallbacks
                }
            }

            if (double.IsNaN(pitLossSec) || double.IsInfinity(pitLossSec) || pitLossSec < 0.0)
            {
                pitLossSec = FuelCalculator?.PitLaneTimeLoss ?? 0.0;
            }

            if (double.IsNaN(pitLossSec) || double.IsInfinity(pitLossSec) || pitLossSec < 0.0) pitLossSec = 0.0;

            string sessionTypeForOpponents = !string.IsNullOrWhiteSpace(sessionTypeToken)
                ? sessionTypeToken
                : (data.NewData?.SessionTypeName ?? string.Empty);
            bool isRaceSessionNow = IsRaceSession(sessionTypeForOpponents);

            bool isOnPitRoadFlag = Convert.ToBoolean(
                pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.OnPitRoad") ?? false
            );
            bool isInPitLaneFlag = (data.NewData?.IsInPitLane ?? 0) != 0;
            bool onPitRoad = isOnPitRoadFlag || isInPitLaneFlag;
            bool pitExitRecently = (DateTime.UtcNow - _lastPitLaneSeenUtc).TotalSeconds < 1.0;
            bool pitTripActive = _wasInPitThisLap || onPitRoad || pitExitRecently;

            double trackPct = SafeReadDouble(pluginManager, "IRacingExtraProperties.iRacing_Player_LapDistPct", double.NaN);
            double sessionTimeSec = SafeReadDouble(pluginManager, "DataCorePlugin.GameRawData.Telemetry.SessionTime", 0.0);
            double sessionTimeRemainingSec = SafeReadDouble(pluginManager, "DataCorePlugin.GameRawData.Telemetry.SessionTimeRemain", double.NaN);
            bool verboseLogs = IsVerboseDebugLoggingOn;
            _opponentsEngine.Update(data, pluginManager, isRaceSessionNow, completedLaps, myPaceSec, pitLossSec, pitTripActive, onPitRoad, trackPct, sessionTimeSec, sessionTimeRemainingSec, verboseLogs);
        }

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

        private static int SafeReadInt(PluginManager pluginManager, string propertyName, int fallback)
        {
            try
            {
                return Convert.ToInt32(pluginManager.GetPropertyValue(propertyName) ?? fallback);
            }
            catch
            {
                return fallback;
            }
        }

        private static bool TryReadNullableInt(PluginManager pluginManager, string propertyName, out int value)
        {
            value = 0;
            if (pluginManager == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            try
            {
                object raw = pluginManager.GetPropertyValue(propertyName);
                if (raw == null)
                {
                    return false;
                }

                if (raw is int intValue)
                {
                    value = intValue;
                    return true;
                }

                if (raw is short shortValue)
                {
                    value = shortValue;
                    return true;
                }

                if (raw is long longValue)
                {
                    if (longValue < int.MinValue || longValue > int.MaxValue)
                    {
                        return false;
                    }

                    value = (int)longValue;
                    return true;
                }

                if (raw is byte byteValue)
                {
                    value = byteValue;
                    return true;
                }

                if (raw is string text)
                {
                    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static float[] SafeReadFloatArray(PluginManager pluginManager, string propertyName)
        {
            try
            {
                return pluginManager.GetPropertyValue(propertyName) as float[];
            }
            catch
            {
                return null;
            }
        }

        private static int[] SafeReadIntArray(PluginManager pluginManager, string propertyName)
        {
            try
            {
                return pluginManager.GetPropertyValue(propertyName) as int[];
            }
            catch
            {
                return null;
            }
        }

        private static bool[] SafeReadBoolArray(PluginManager pluginManager, string propertyName)
        {
            try
            {
                return pluginManager.GetPropertyValue(propertyName) as bool[];
            }
            catch
            {
                return null;
            }
        }

        private static bool TryReadTelemetryIntArray(
            PluginManager pluginManager,
            string propertyName,
            out int[] values,
            out string readMode,
            out string failReason)
        {
            values = null;
            readMode = "none";
            failReason = string.Empty;
            if (pluginManager == null)
            {
                failReason = "plugin_null";
                return false;
            }

            object raw;
            try
            {
                raw = pluginManager.GetPropertyValue($"DataCorePlugin.GameRawData.Telemetry.{propertyName}");
            }
            catch
            {
                raw = null;
            }

            if (raw != null)
            {
                if (TryConvertToIntArray(raw, out values))
                {
                    readMode = "direct";
                    return true;
                }
                failReason = "type_unsupported_direct";
            }

            object telemetry;
            try
            {
                telemetry = pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry");
            }
            catch
            {
                failReason = string.IsNullOrEmpty(failReason) ? "telemetry_exception" : failReason;
                return false;
            }

            if (telemetry == null)
            {
                failReason = string.IsNullOrEmpty(failReason) ? "telemetry_null" : failReason;
                return false;
            }

            var prop = telemetry.GetType().GetProperty(propertyName);
            if (prop != null)
            {
                try
                {
                    raw = prop.GetValue(telemetry);
                }
                catch
                {
                    failReason = "property_exception";
                    raw = null;
                }

                if (raw != null)
                {
                    if (TryConvertToIntArray(raw, out values))
                    {
                        readMode = "property";
                        return true;
                    }

                    failReason = "type_unsupported_property";
                }
            }
            else if (string.IsNullOrEmpty(failReason))
            {
                failReason = "prop_missing";
            }

            var field = telemetry.GetType().GetField(propertyName);
            if (field == null)
            {
                if (string.IsNullOrEmpty(failReason))
                {
                    failReason = "field_missing";
                }
                return false;
            }

            try
            {
                raw = field.GetValue(telemetry);
            }
            catch
            {
                failReason = "field_exception";
                return false;
            }

            if (raw == null)
            {
                failReason = "field_null";
                return false;
            }

            if (TryConvertToIntArray(raw, out values))
            {
                readMode = "field";
                return true;
            }

            failReason = "type_unsupported_field";
            return false;
        }

        private static string ResolveRawTelemetryReadMode(
            bool hasPaceFlags,
            string paceReadMode,
            bool hasSessionFlags,
            string sessionReadMode,
            bool hasTrackSurfaceMaterial,
            string trackReadMode)
        {
            if (hasPaceFlags)
            {
                return paceReadMode;
            }
            if (hasSessionFlags)
            {
                return sessionReadMode;
            }
            if (hasTrackSurfaceMaterial)
            {
                return trackReadMode;
            }

            return "none";
        }

        private static string ResolveRawTelemetryFailReason(string readMode, params string[] reasons)
        {
            if (!string.Equals(readMode, "none", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (reasons != null)
            {
                foreach (var reason in reasons)
                {
                    if (!string.IsNullOrEmpty(reason))
                    {
                        return reason;
                    }
                }
            }

            return "unknown";
        }

        private static bool TryConvertToIntArray(object raw, out int[] values)
        {
            values = null;
            switch (raw)
            {
                case int[] ints:
                    values = ints;
                    return true;
                case uint[] uints:
                    values = Array.ConvertAll(uints, v => unchecked((int)v));
                    return true;
                case short[] shorts:
                    values = Array.ConvertAll(shorts, v => (int)v);
                    return true;
                case ushort[] ushorts:
                    values = Array.ConvertAll(ushorts, v => (int)v);
                    return true;
                case byte[] bytes:
                    values = Array.ConvertAll(bytes, v => (int)v);
                    return true;
                case sbyte[] sbytes:
                    values = Array.ConvertAll(sbytes, v => (int)v);
                    return true;
                case long[] longs:
                    // CarIdx bitfield arrays only define the lower 32 bits; mask intentionally.
                    values = Array.ConvertAll(longs, v => unchecked((int)(v & 0xFFFFFFFF)));
                    return true;
                case ulong[] ulongs:
                    values = Array.ConvertAll(ulongs, v => unchecked((int)(v & 0xFFFFFFFF)));
                    return true;
                default:
                    return false;
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

        private void UpdatePitExitDisplayValues(GameData data, bool inPitLane)
        {
            // Only clear when NOT in pit lane.
            if (!inPitLane)
            {
                _pitExitDistanceM = 0;
                _pitExitTimeS = 0;
                return;
            }

            if (data?.NewData == null || _pit == null) return;

            double exitPct = _pit.TrackMarkersStoredExitPct;
            double trackLenM = _pit.TrackMarkersSessionTrackLengthM;

            // If we can't compute, HOLD last-good values.
            if (double.IsNaN(exitPct) || double.IsNaN(trackLenM) || trackLenM <= 0.0)
                return;

            double carPct = data.NewData.TrackPositionPercent;
            if (carPct > 1.5) carPct *= 0.01;
            if (carPct < 0.0 || carPct > 1.0 || double.IsNaN(carPct) || double.IsInfinity(carPct))
                return;

            double speedMps = data.NewData.SpeedKmh / 3.6;

            double deltaPct = exitPct - carPct;
            if (deltaPct < 0.0) deltaPct += 1.0;

            double distanceM = deltaPct * trackLenM;
            if (double.IsNaN(distanceM) || distanceM < 0.0) distanceM = 0.0;

            double timeS = (speedMps > PitExitSpeedEpsilonMps) ? distanceM / speedMps : 0.0;

            _pitExitDistanceM = Math.Max(0, (int)Math.Round(distanceM, MidpointRounding.AwayFromZero));
            _pitExitTimeS = Math.Max(0, (int)Math.Round(timeS, MidpointRounding.AwayFromZero));
        }

        private void RefreshClassMetadata(PluginManager pluginManager)
        {
            _carIdxToClassShortName.Clear();
            var classNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < 64; i++)
            {
                int carIdx = GetInt(pluginManager, $"DataCorePlugin.GameRawData.SessionData.DriverInfo.CompetingDrivers[{i}].CarIdx", int.MinValue);
                if (carIdx == int.MinValue) break;

                string cls = GetString(pluginManager, $"DataCorePlugin.GameRawData.SessionData.DriverInfo.CompetingDrivers[{i}].CarClassShortName");
                if (string.IsNullOrWhiteSpace(cls))
                {
                    cls = GetString(pluginManager, $"DataCorePlugin.GameRawData.SessionData.DriverInfo.CompetingDrivers[{i}].CarClassName");
                }

                if (!string.IsNullOrWhiteSpace(cls))
                {
                    _carIdxToClassShortName[carIdx] = cls;
                    classNames.Add(cls);
                }
            }

            _isMultiClassSession = classNames.Count > 1;
        }

        private string GetCachedClassShortName(int carIdx)
        {
            if (carIdx < 0) return null;
            return _carIdxToClassShortName.TryGetValue(carIdx, out var cls) ? cls : null;
        }

        private bool IsNewSessionBestInClass(int carIdx, double candidateBestLapSec, float[] currentBestLapTimes, double[] previousBestLapTimes)
        {
            if (carIdx < 0 || carIdx >= CarSAEngine.MaxCars || !IsValidCarSaLapTimeSec(candidateBestLapSec))
            {
                return false;
            }

            string classShort = GetCachedClassShortName(carIdx);
            if (string.IsNullOrWhiteSpace(classShort))
            {
                return false;
            }

            double previousClassBestSec = double.NaN;
            double currentClassBestSec = double.NaN;
            for (int i = 0; i < CarSAEngine.MaxCars; i++)
            {
                string classShortForCar = GetCachedClassShortName(i);
                if (!string.Equals(classShortForCar, classShort, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double prevBest = (previousBestLapTimes != null && i < previousBestLapTimes.Length)
                    ? previousBestLapTimes[i]
                    : _carSaPrevBestLapTimeSecByIdx[i];
                if (IsValidCarSaLapTimeSec(prevBest) && (!IsValidCarSaLapTimeSec(previousClassBestSec) || prevBest < previousClassBestSec))
                {
                    previousClassBestSec = prevBest;
                }

                double currBest = ReadCarIdxTime(currentBestLapTimes, i);
                if (IsValidCarSaLapTimeSec(currBest) && (!IsValidCarSaLapTimeSec(currentClassBestSec) || currBest < currentClassBestSec))
                {
                    currentClassBestSec = currBest;
                }
            }

            if (!IsValidCarSaLapTimeSec(previousClassBestSec) || !IsValidCarSaLapTimeSec(currentClassBestSec))
            {
                return false;
            }

            return (currentClassBestSec + CarSaLapTimeEpsilonSec) < previousClassBestSec
                && Math.Abs(candidateBestLapSec - currentClassBestSec) <= CarSaLapTimeEpsilonSec;
        }

        private int FindClassLeaderCarIdx(string playerClassShort, int[] classPositions, int[] trackSurfaces)
        {
            if (string.IsNullOrWhiteSpace(playerClassShort) || classPositions == null) return -1;

            for (int i = 0; i < classPositions.Length; i++)
            {
                if (classPositions[i] != 1) continue;
                if (!IsCarInWorld(trackSurfaces, i)) continue;

                var classShort = GetCachedClassShortName(i);
                if (string.IsNullOrWhiteSpace(classShort)) continue;

                if (string.Equals(classShort, playerClassShort, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsCarInWorld(int[] trackSurfaces, int index)
        {
            if (index < 0) return false;
            if (trackSurfaces == null) return true;
            if (index >= trackSurfaces.Length) return true;
            return trackSurfaces[index] >= 0;
        }

        private void MaybeLatchLeaderFinished(bool isClassLeader, int leaderIdx, double[] lapPct, int[] trackSurfaces, double sessionTime, int sessionStateNumeric)
        {
            if (leaderIdx < 0 || lapPct == null || leaderIdx >= lapPct.Length) return;
            if (!IsCarInWorld(trackSurfaces, leaderIdx)) return;

            double lastPct = isClassLeader ? _lastClassLeaderLapPct : _lastOverallLeaderLapPct;
            int lastIdx = isClassLeader ? _lastClassLeaderCarIdx : _lastOverallLeaderCarIdx;
            double currentPct = lapPct[leaderIdx];

            if (lastIdx != leaderIdx)
            {
                if (isClassLeader)
                {
                    _lastClassLeaderCarIdx = leaderIdx;
                    _lastClassLeaderLapPct = currentPct;
                }
                else
                {
                    _lastOverallLeaderCarIdx = leaderIdx;
                    _lastOverallLeaderLapPct = currentPct;
                }

                return;
            }

            if (!double.IsNaN(lastPct) && lastPct > 0.90 && currentPct < 0.10)
            {
                if (isClassLeader && !ClassLeaderHasFinished)
                {
                    ClassLeaderHasFinished = true;
                    SimHub.Logging.Current.Info(
                        $"[LalaPlugin:Drive Time Projection] ClassLeader finished (heuristic): carIdx={leaderIdx} sessionTime={sessionTime:F1}s sessionState={sessionStateNumeric} timerZero={_timerZeroSeen}");
                }

                if (!isClassLeader && !OverallLeaderHasFinished)
                {
                    OverallLeaderHasFinished = true;
                    SimHub.Logging.Current.Info(
                        $"[LalaPlugin:Drive Time Projection] OverallLeader finished (heuristic): carIdx={leaderIdx} sessionTime={sessionTime:F1}s sessionState={sessionStateNumeric} timerZero={_timerZeroSeen}");
                }
            }

            if (isClassLeader)
            {
                _lastClassLeaderLapPct = currentPct;
                _lastClassLeaderCarIdx = leaderIdx;
            }
            else
            {
                _lastOverallLeaderLapPct = currentPct;
                _lastOverallLeaderCarIdx = leaderIdx;
            }
        }

        private int ReadSessionStateInt(PluginManager pluginManager)
        {
            try
            {
                var raw = pluginManager?.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.SessionState");
                if (raw == null) return 0;

                if (raw is int i) return i;
                if (raw is long l) return (int)l;
                if (raw is double d) return (int)d;
                if (raw is float f) return (int)f;

                var s = Convert.ToString(raw, CultureInfo.InvariantCulture);
                return int.TryParse(s, out var parsed) ? parsed : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static int GetInt(PluginManager pluginManager, string propertyName, int fallback)
        {
            try
            {
                var raw = pluginManager?.GetPropertyValue(propertyName);
                if (raw == null) return fallback;
                return Convert.ToInt32(raw);
            }
            catch
            {
                return fallback;
            }
        }

        private static string GetString(PluginManager pluginManager, string propertyName)
        {
            try
            {
                var raw = pluginManager?.GetPropertyValue(propertyName);
                return Convert.ToString(raw, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static int[] GetIntArray(PluginManager pluginManager, string propertyName)
        {
            try
            {
                var raw = pluginManager?.GetPropertyValue(propertyName);
                if (raw == null) return null;

                if (raw is int[] ints) return ints;
                if (raw is long[] longs) return longs.Select(l => (int)l).ToArray();
                if (raw is float[] floats) return floats.Select(f => (int)f).ToArray();
                if (raw is double[] doubles) return doubles.Select(d => (int)d).ToArray();

                if (raw is System.Collections.IEnumerable enumerable)
                {
                    var list = new List<int>(64);
                    foreach (var item in enumerable)
                    {
                        try
                        {
                            list.Add(Convert.ToInt32(item));
                        }
                        catch
                        {
                            list.Add(0);
                        }
                    }
                    return list.Count > 0 ? list.ToArray() : null;
                }
            }
            catch { }

            return null;
        }

        private static double[] GetDoubleArray(PluginManager pluginManager, string propertyName)
        {
            try
            {
                var raw = pluginManager?.GetPropertyValue(propertyName);
                if (raw == null) return null;

                if (raw is double[] doubles) return doubles;
                if (raw is float[] floats) return floats.Select(f => (double)f).ToArray();
                if (raw is int[] ints) return ints.Select(i => (double)i).ToArray();
                if (raw is long[] longs) return longs.Select(l => (double)l).ToArray();

                if (raw is System.Collections.IEnumerable enumerable)
                {
                    var list = new List<double>(64);
                    foreach (var item in enumerable)
                    {
                        try
                        {
                            list.Add(Convert.ToDouble(item));
                        }
                        catch
                        {
                            list.Add(double.NaN);
                        }
                    }
                    return list.Count > 0 ? list.ToArray() : null;
                }
            }
            catch { }

            return null;
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

        private static string FormatSecondsWithSuffix(double seconds)
        {
            return (double.IsNaN(seconds) || double.IsInfinity(seconds))
                ? "n/a"
                : seconds.ToString("F1", CultureInfo.InvariantCulture) + "s";
        }

        private string ResolvePitExitPitLossSource()
        {
            if (ActiveProfile == null) return "default";

            try
            {
                var trackStats = ActiveProfile.ResolveTrackByNameOrKey(CurrentTrackKey)
                                 ?? ActiveProfile.ResolveTrackByNameOrKey(CurrentTrackName);
                if (trackStats?.PitLaneLossSeconds is double pll && pll > 0.0)
                {
                    string src = (trackStats.PitLaneLossSource ?? string.Empty).Trim().ToLowerInvariant();
                    if (src == "dtl" || src == "direct" || src == "total")
                    {
                        return "learned_cached";
                    }

                    return "profile_dtl";
                }
            }
            catch
            {
                return "default_fallback";
            }

            return "default_fallback";
        }

        private void LogPitExitPitInSnapshot(double sessionTime, int lapNumber, double pitLossSec)
        {
            if (_opponentsEngine == null) return;

            var hasSnapshot = _opponentsEngine.TryGetPitExitSnapshot(out var snapshot);
            int posClass = hasSnapshot ? snapshot.PlayerPositionInClass : 0;
            int posOverall = hasSnapshot ? snapshot.PlayerPositionOverall : 0;
            double gapLdr = hasSnapshot ? snapshot.PlayerGapToLeader : double.NaN;
            int predPosClass = hasSnapshot ? snapshot.PredictedPositionInClass : 0;
            int carsAhead = hasSnapshot ? snapshot.CarsAheadAfterPit : 0;
            double pitLoss = hasSnapshot ? snapshot.PitLossSec : pitLossSec;
            double entryGapLdr = hasSnapshot ? snapshot.PitEntryGapToLeaderSec : double.NaN;
            double gapLdrLive = hasSnapshot ? snapshot.GapToLeaderLiveSec : gapLdr;
            double gapLdrUsed = hasSnapshot ? snapshot.GapToLeaderUsedSec : gapLdr;
            double pitLossLive = hasSnapshot ? snapshot.PitLossLiveSec : pitLossSec;
            double pitLossUsed = hasSnapshot ? snapshot.PitLossUsedSec : pitLoss;
            double predGapAfterPit = hasSnapshot ? snapshot.PredGapAfterPitSec : double.NaN;
            bool pitTripLockActive = hasSnapshot && snapshot.PitTripLockActive;

            double laneRef = _pitLite?.TimePitLaneSec ?? 0.0;
            double boxRef = _pitLite?.TimePitBoxSec ?? 0.0;
            double directRef = _pitLite?.DirectSec ?? 0.0;
            string srcPitLoss = ResolvePitExitPitLossSource();

            SimHub.Logging.Current.Info(
                $"[LalaPlugin:PitExit] Pit-in snapshot: lap={lapNumber} t={sessionTime:F1} " +
                $"posClass=P{posClass} posOverall=P{posOverall} gapLdr={FormatSecondsWithSuffix(gapLdr)} " +
                $"pitLoss={FormatSecondsWithSuffix(pitLoss)} predPosClass=P{predPosClass} carsAhead={carsAhead} " +
                $"srcPitLoss={srcPitLoss} laneRef={FormatSecondsWithSuffix(laneRef)} " +
                $"boxRef={FormatSecondsWithSuffix(boxRef)} directRef={FormatSecondsWithSuffix(directRef)} " +
                $"entryGapLdr={FormatSecondsWithSuffix(entryGapLdr)} gapLdrLive={FormatSecondsWithSuffix(gapLdrLive)} " +
                $"gapLdrUsed={FormatSecondsWithSuffix(gapLdrUsed)} pitLossLive={FormatSecondsWithSuffix(pitLossLive)} " +
                $"pitLossUsed={FormatSecondsWithSuffix(pitLossUsed)} predGapAfterPit={FormatSecondsWithSuffix(predGapAfterPit)} " +
                $"lock={pitTripLockActive}"
            );

            bool pitExitVerbose = SoftDebugEnabled && Settings?.PitExitVerboseLogging == true;
            if (pitExitVerbose && _opponentsEngine.TryGetPitExitMathAudit(out var auditLine))
            {
                SimHub.Logging.Current.Info($"[LalaPlugin:PitExit] {auditLine}");
            }
        }
        /*
                private void LogPitExitPitOutSnapshot(double sessionTime, int lapNumber, bool pitTripActive)
                {
                    if (_opponentsEngine == null) return;

                    var hasSnapshot = _opponentsEngine.TryGetPitExitSnapshot(out var snapshot);
                    int posClass = hasSnapshot ? snapshot.PlayerPositionInClass : 0;
                    int posOverall = hasSnapshot ? snapshot.PlayerPositionOverall : 0;
                    int predPosClass = hasSnapshot ? snapshot.PredictedPositionInClass : 0;
                    int carsAhead = hasSnapshot ? snapshot.CarsAheadAfterPit : 0;
                    double entryGapLdr = hasSnapshot ? snapshot.PitEntryGapToLeaderSec : double.NaN;
                    double gapLdrLive = hasSnapshot ? snapshot.GapToLeaderLiveSec : double.NaN;
                    double gapLdrUsed = hasSnapshot ? snapshot.GapToLeaderUsedSec : double.NaN;
                    double predGapAfterPit = hasSnapshot ? snapshot.PredGapAfterPitSec : double.NaN;
                    bool pitTripLockActive = hasSnapshot && snapshot.PitTripLockActive;

                    double laneRef = _pitLite?.TimePitLaneSec ?? 0.0;
                    double boxRef = _pitLite?.TimePitBoxSec ?? 0.0;
                    double directRef = _pitLite?.DirectSec ?? 0.0;

                    SimHub.Logging.Current.Info(
                        $"[LalaPlugin:PitExit] Pit-out snapshot: lap={lapNumber} t={sessionTime:F1} " +
                        $"posClass=P{posClass} posOverall=P{posOverall} predPosClassNow=P{predPosClass} " +
                        $"carsAheadNow={carsAhead} lane={FormatSecondsWithSuffix(laneRef)} box={FormatSecondsWithSuffix(boxRef)} " +
                        $"direct={FormatSecondsWithSuffix(directRef)} pitTripActive={pitTripActive} " +
                        $"entryGapLdr={FormatSecondsWithSuffix(entryGapLdr)} gapLdrLiveNow={FormatSecondsWithSuffix(gapLdrLive)} " +
                        $"gapLdrUsed={FormatSecondsWithSuffix(gapLdrUsed)} predGapAfterPit={FormatSecondsWithSuffix(predGapAfterPit)} " +
                        $"lock={pitTripLockActive}"
                    );
                }
        */
        private void ResetSmoothedOutputs()
        {
            // Reset internal EMA state
            _smoothedLiveLapsRemainingState = double.NaN;
            _smoothedPitDeltaState = double.NaN;
            _smoothedPitPushDeltaState = double.NaN;
            _smoothedPitFuelSaveDeltaState = double.NaN;
            _smoothedPitTotalNeededState = double.NaN;

            // Reset smoothing gates
            _smoothedProjectionValid = false;
            _smoothedPitValid = false;
            _pendingSmoothingReset = false;

            // Also clear the published smoothed outputs so dashboards can't "freeze" old values
            // when smoothing doesn't run immediately after a reset.
            LiveLapsRemainingInRace_S = 0;

            Pit_DeltaAfterStop_S = 0;
            Pit_PushDeltaAfterStop_S = 0;
            Pit_FuelSaveDeltaAfterStop_S = 0;
            Pit_TotalNeededToEnd_S = 0;
        }

        private void ClearFuelInstructionOutputs()
        {
            // --- Pit / instructions (already present) ---
            Pit_TotalNeededToEnd = 0;
            Pit_NeedToAdd = 0;
            Pit_TankSpaceAvailable = 0;
            Pit_WillAdd = 0;
            Pit_FuelOnExit = 0;
            Pit_DeltaAfterStop = 0;
            Pit_FuelSaveDeltaAfterStop = 0;
            Pit_PushDeltaAfterStop = 0;
            PitStopsRequiredByFuel = 0;
            PitStopsRequiredByPlan = 0;
            Pit_StopsRequiredToEnd = 0;

            Fuel_Delta_LitresCurrent = 0;
            Fuel_Delta_LitresPlan = 0;
            Fuel_Delta_LitresWillAdd = 0;
            Fuel_Delta_LitresCurrentPush = 0;
            Fuel_Delta_LitresPlanPush = 0;
            Fuel_Delta_LitresWillAddPush = 0;
            Fuel_Delta_LitresCurrentSave = 0;
            Fuel_Delta_LitresPlanSave = 0;
            Fuel_Delta_LitresWillAddSave = 0;

            // --- Additional dashboard-facing fuel/projection outputs that must not latch across resets ---
            // (These were listed in SessionResetIssues.docx)
            DeltaLaps = 0;
            DeltaLapsIfPush = 0;
            FuelSaveFuelPerLap = 0;
            PushFuelPerLap = 0;

            LapsRemainingInTank = 0;

            LiveProjectedDriveTimeAfterZero = 0;
            LiveProjectedDriveSecondsRemaining = 0;

            LiveLapsRemainingInRace = 0;
            LiveLapsRemainingInRace_Stable = 0;

            // Smoothed versions are cleared here too for determinism
            LiveLapsRemainingInRace_S = 0;
            Pit_DeltaAfterStop_S = 0;
            Pit_PushDeltaAfterStop_S = 0;
            Pit_TotalNeededToEnd_S = 0;
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
            double fuelReadyConfidence = GetFuelReadyConfidenceThreshold();

            double candidate = fallbackFuelPerLap;
            string source = "Fallback";

            if (Confidence >= fuelReadyConfidence && LiveFuelPerLap > 0.0)
            {
                candidate = LiveFuelPerLap;
                source = "Live";
            }
            else if (profileFuel > 0.0 && fuelReadyConfidence <= ProfileAllowedConfidenceCeiling)
            {
                candidate = profileFuel;
                source = "Profile";
            }

            // --- Stable confidence reflects the chosen stable source, not always live Confidence ---
            // Align Profile stable confidence with the same threshold you use for switching Live on.
            double ProfileStableConfidenceFloor = ClampToRange(fuelReadyConfidence, 0.0, 100.0, FuelReadyConfidenceDefault);

            double GetConfidenceForStableSource(string src)
            {
                if (string.Equals(src, "Live", StringComparison.OrdinalIgnoreCase)) return Confidence;
                if (string.Equals(src, "Profile", StringComparison.OrdinalIgnoreCase)) return ProfileStableConfidenceFloor;
                return 0.0; // Fallback / unknown
            }
            // -------------------------------------------------------------------------------

            double stable = _stableFuelPerLap;
            string selectedSource = source;
            double selectedConfidence = GetConfidenceForStableSource(selectedSource);

            if (candidate <= 0.0)
            {
                if (stable > 0.0)
                {
                    // Hold previous stable triple if candidate is invalid.
                    candidate = stable;
                    selectedSource = _stableFuelPerLapSource;
                    selectedConfidence = _stableFuelPerLapConfidence;
                }
                else
                {
                    stable = 0.0;
                    selectedSource = "Fallback";
                    selectedConfidence = 0.0;
                }
            }
            else
            {
                if (stable <= 0.0 || Math.Abs(candidate - stable) >= StableFuelPerLapDeadband)
                {
                    // Accept new stable candidate: update source + confidence together.
                    stable = candidate;
                    selectedSource = source;
                    selectedConfidence = GetConfidenceForStableSource(selectedSource);
                }
                else
                {
                    // Deadband hold: keep value, but allow source/confidence to advance
                    selectedSource = source;
                    selectedConfidence = GetConfidenceForStableSource(selectedSource);
                }
            }

            // Clamp values defensively
            stable = Math.Max(0.1, stable); // avoid pathological near-zero persistence

            _stableFuelPerLap = stable;
            _stableFuelPerLapSource = selectedSource;
            _stableFuelPerLapConfidence = ClampToRange(selectedConfidence, 0.0, 100.0, 0.0);

            LiveFuelPerLap_Stable = _stableFuelPerLap;
            LiveFuelPerLap_StableSource = _stableFuelPerLapSource;
            LiveFuelPerLap_StableConfidence = _stableFuelPerLapConfidence;
        }

        private static double? GetRollingAverage(List<double> samples, int sampleCount)
        {
            if (samples == null || samples.Count < sampleCount)
            {
                return null;
            }

            double sum = 0.0;
            for (int i = samples.Count - sampleCount; i < samples.Count; i++)
            {
                sum += samples[i];
            }

            return sum / sampleCount;
        }

        private static string MapFuelPredictorSource(string stableSource)
        {
            if (string.Equals(stableSource, "Live", StringComparison.OrdinalIgnoreCase)) return "STINT";
            if (string.Equals(stableSource, "Profile", StringComparison.OrdinalIgnoreCase)) return "PLUGIN";
            return "SIMHUB";
        }

        private static string MapPacePredictorSource(string projectionSource)
        {
            if (string.Equals(projectionSource, "pace.stint", StringComparison.OrdinalIgnoreCase)) return "STINT";
            if (string.Equals(projectionSource, "pace.last5", StringComparison.OrdinalIgnoreCase)) return "AVG5";
            if (string.Equals(projectionSource, "profile.avg", StringComparison.OrdinalIgnoreCase)) return "PLUGIN";
            if (string.Equals(projectionSource, "fuelcalc.estimated", StringComparison.OrdinalIgnoreCase)) return "SIMHUB";
            if (string.Equals(projectionSource, "telemetry.lastlap", StringComparison.OrdinalIgnoreCase)) return "SIMHUB";
            return "SIMHUB";
        }

        private void UpdatePredictorOutputs()
        {
            var fuelWindow = _isWetMode ? _recentWetFuelLaps : _recentDryFuelLaps;
            double? fuelAvg3 = GetRollingAverage(fuelWindow, 3);
            if (fuelWindow.Count >= 5 && fuelAvg3.HasValue)
            {
                FuelBurnPredictor = fuelAvg3.Value;
                FuelBurnPredictorSource = "AVG3";
            }
            else
            {
                FuelBurnPredictor = LiveFuelPerLap_Stable;
                FuelBurnPredictorSource = LiveFuelPerLap_Stable > 0.0
                    ? MapFuelPredictorSource(LiveFuelPerLap_StableSource)
                    : "SIMHUB";
            }

            double? paceAvg3 = GetRollingAverage(_recentLapTimes, 3);
            if (_recentLapTimes.Count >= 5 && paceAvg3.HasValue)
            {
                PacePredictor = paceAvg3.Value;
                PacePredictorSource = "AVG3";
            }
            else
            {
                PacePredictor = ProjectionLapTime_Stable;
                PacePredictorSource = PacePredictor > 0.0
                    ? MapPacePredictorSource(ProjectionLapTime_StableSource)
                    : "SIMHUB";
            }
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
                    $"[LalaPlugin:Pace] source={selectedSource} lap={_stableProjectionLapTime:F3}s " +
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

        private double ComputeLiveMaxFuelFromSimhub(PluginManager pluginManager)
        {
            double baseMaxFuel = SafeReadDouble(pluginManager, "DataCorePlugin.GameData.MaxFuel", 0.0);
            if (double.IsNaN(baseMaxFuel) || baseMaxFuel <= 0.0)
                return 0.0;

            double bopPercent = SafeReadDouble(pluginManager, "DataCorePlugin.GameRawData.SessionData.DriverInfo.DriverCarMaxFuelPct", 1.0);
            if (double.IsNaN(bopPercent) || bopPercent <= 0.0)
                bopPercent = 1.0;

            bopPercent = Math.Min(1.0, Math.Max(0.01, bopPercent));

            double detected = baseMaxFuel * bopPercent;
            return detected < 0.0 ? 0.0 : detected;
        }

        private void UpdateLiveMaxFuel(PluginManager pluginManager)
        {
            double computedMaxFuel = ComputeLiveMaxFuelFromSimhub(pluginManager);

            if (computedMaxFuel > 0.0)
            {
                _lastValidLiveMaxFuel = computedMaxFuel;
                EffectiveLiveMaxTank = computedMaxFuel;

                bool meaningfulChange =
                    (LiveCarMaxFuel <= 0.0) ||
                    (Math.Abs(LiveCarMaxFuel - computedMaxFuel) > LiveMaxFuelJitterThreshold);

                if (!meaningfulChange)
                    return;

                LiveCarMaxFuel = computedMaxFuel;

                if (Math.Abs(LiveCarMaxFuel - _lastAnnouncedMaxFuel) > 0.01 && FuelCalculator != null)
                {
                    _lastAnnouncedMaxFuel = LiveCarMaxFuel;
                    FuelCalculator.UpdateLiveDisplay(LiveCarMaxFuel);
                }

                return;
            }

            // computedMaxFuel <= 0 : keep a stable non-zero effective value if we have one
            EffectiveLiveMaxTank = (LiveCarMaxFuel > 0.0) ? LiveCarMaxFuel : _lastValidLiveMaxFuel;
        }

        private double ResolveMaxTankCapacity()
        {
            double maxTankCapacity = FuelCalculator?.MaxFuelOverride ?? 0.0;

            double sessionMaxFuel = EffectiveLiveMaxTank;

            if (maxTankCapacity <= 0.0)
            {
                maxTankCapacity = sessionMaxFuel;
            }
            else if (sessionMaxFuel > 0.0)
            {
                maxTankCapacity = Math.Min(maxTankCapacity, sessionMaxFuel);
            }

            return maxTankCapacity;
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

            double afterZeroChange = double.IsNaN(_lastLoggedProjectionAfterZero)
                ? double.PositiveInfinity
                : Math.Abs(LiveProjectedDriveTimeAfterZero - _lastLoggedProjectionAfterZero);
            if (afterZeroChange < 1.0)
                return false;

            return true;
        }

        private void LogProjectionDifference(
            double simLapsRemaining,
            double projectedLapsRemaining,
            double lapSeconds,
            double projectedSeconds,
            string afterZeroSource,
            double sessionTimeRemain)
        {
            _lastProjectionLogUtc = DateTime.UtcNow;
            _lastLoggedProjectedLaps = projectedLapsRemaining;
            _lastLoggedProjectionAfterZero = LiveProjectedDriveTimeAfterZero;

            SimHub.Logging.Current.Info(
                $"[LalaPlugin:Drive Time Projection] " +
                $"projection=drive_time " +
                $"lapsProj={projectedLapsRemaining:F2} simLaps={simLapsRemaining:F2} deltaLaps={(projectedLapsRemaining - simLapsRemaining):+0.00;-0.00;0.00} " +
                $"lapRef={lapSeconds:F2}s " +
                $"after0Used={LiveProjectedDriveTimeAfterZero:F1}s src={afterZeroSource} " +
                $"tRemain={FormatSecondsOrNA(sessionTimeRemain)} " +
                $"driveRemain={projectedSeconds:F1}s"
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

        private void MaybeLogAfterZeroResult(double sessionTime, bool sessionEnded)
        {
            if (_afterZeroResultLogged || !sessionEnded)
            {
                return;
            }

            double leaderExtra = double.IsNaN(_leaderCheckeredSessionTime)
                ? double.NaN
                : ComputeObservedExtraSeconds(_leaderCheckeredSessionTime);
            double driverExtra = ComputeObservedExtraSeconds(_driverCheckeredSessionTime);

            if (driverExtra <= 0.0 && sessionTime > 0.0)
            {
                driverExtra = ComputeObservedExtraSeconds(sessionTime);
            }

            string leaderText = double.IsNaN(leaderExtra)
                ? "n/a"
                : $"{leaderExtra:F1}s";
            string driverText = $"{driverExtra:F1}s";

            SimHub.Logging.Current.Info(
                $"[LalaPlugin:After0Result] driver={driverText} leader={leaderText} " +
                $"pred={_afterZeroUsedSeconds:F1}s lapsPred={_lastProjectedLapsRemaining:F2}");

            _afterZeroResultLogged = true;
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
            bool isRace = IsRaceSession(sessionType);

            // Reset cleanly on session change
            if (sessionId != _finishTimingSessionId || sessionType != _finishTimingSessionType)
            {
                _finishTimingSessionId = sessionId;
                _finishTimingSessionType = sessionType;
                _afterZeroResultLogged = false;
                ResetFinishTimingState();
                RefreshClassMetadata(pluginManager);
            }

            if (!isRace)
            {
                _prevSessionTimeRemain = !double.IsNaN(sessionTimeRemain) ? sessionTimeRemain : double.NaN;
                return;
            }

            bool hasRemain = !double.IsNaN(sessionTimeRemain);

            // Detect first genuine crossing to zero
            bool crossedToZero =
                hasRemain &&
                !double.IsNaN(_prevSessionTimeRemain) &&
                _prevSessionTimeRemain > 0.5 &&
                sessionTimeRemain <= 0.5 &&
                completedLaps > 0;

            if (!_timerZeroSeen && crossedToZero)
            {
                _timerZeroSeen = true;
                _timerZeroSessionTime = sessionTime;
            }

            if (_carIdxToClassShortName.Count == 0 && isRace)
            {
                RefreshClassMetadata(pluginManager);
            }

            int sessionStateNumeric = ReadSessionStateInt(pluginManager);
            bool isTimedRace = hasRemain;
            bool sessionStateRaceOrLater = sessionStateNumeric >= 4;

            int playerCarIdx = GetInt(pluginManager, "DataCorePlugin.GameRawData.Telemetry.PlayerCarIdx", -1);
            string playerClassShort = GetCachedClassShortName(playerCarIdx);
            if (string.IsNullOrWhiteSpace(playerClassShort) && pluginManager != null)
            {
                RefreshClassMetadata(pluginManager);
                playerClassShort = GetCachedClassShortName(playerCarIdx);
            }

            var classPositions = GetIntArray(pluginManager, "DataCorePlugin.GameRawData.Telemetry.CarIdxClassPosition");
            var trackSurfaces = GetIntArray(pluginManager, "DataCorePlugin.GameRawData.Telemetry.CarIdxTrackSurface");
            var lapDistPct = GetDoubleArray(pluginManager, "DataCorePlugin.GameRawData.Telemetry.CarIdxLapDistPct");

            int classLeaderIdx = FindClassLeaderCarIdx(playerClassShort, classPositions, trackSurfaces);
            ClassLeaderHasFinishedValid = classLeaderIdx >= 0;

            int overallLeaderIdx = -1;

            // Single-class: overall leader == class leader
            if (!_isMultiClassSession && classLeaderIdx >= 0)
            {
                overallLeaderIdx = classLeaderIdx;
            }

            // Validity means “we know who the leader is”, not “we are using it”
            OverallLeaderHasFinishedValid = overallLeaderIdx >= 0;

            bool checkeredFlagData = isRace && ReadFlagBool(
                pluginManager,
                "DataCorePlugin.GameRawData.Telemetry.SessionFlagsDetails.IsCheckeredFlag",
                "DataCorePlugin.GameRawData.Telemetry.SessionFlagsDetails.IsCheckered"
            );
            if (checkeredFlagData && !_leaderFinishLatchedByFlag)
            {
                _leaderFinishLatchedByFlag = true;
                OverallLeaderHasFinished = true;
                OverallLeaderHasFinishedValid = true;

                if (_isMultiClassSession)
                {
                    ClassLeaderHasFinished = true;
                    ClassLeaderHasFinishedValid = classLeaderIdx >= 0;
                }

                _leaderFinishedSeen = true;
                if (double.IsNaN(_leaderCheckeredSessionTime))
                {
                    _leaderCheckeredSessionTime = sessionTime;
                }

                SimHub.Logging.Current.Info(
                    $"[LalaPlugin:Finish] checkered_flag trigger=flag leader_finished={LeaderHasFinished} " +
                    $"class_finished={ClassLeaderHasFinished} class_valid={ClassLeaderHasFinishedValid} " +
                    $"overall_finished={OverallLeaderHasFinished} overall_valid={OverallLeaderHasFinishedValid} " +
                    $"multiclass={_isMultiClassSession}"
                );
            }


            if (!isTimedRace)
            {
                // Lap-limited: no derivation path without per-car lap counts or finish flags.
                if (classLeaderIdx >= 0 && lapDistPct != null && classLeaderIdx < lapDistPct.Length)
                {
                    _lastClassLeaderLapPct = lapDistPct[classLeaderIdx];
                    _lastClassLeaderCarIdx = classLeaderIdx;
                }

                if (overallLeaderIdx >= 0 && lapDistPct != null && overallLeaderIdx < lapDistPct.Length)
                {
                    _lastOverallLeaderLapPct = lapDistPct[overallLeaderIdx];
                    _lastOverallLeaderCarIdx = overallLeaderIdx;
                }
            }
            else if (_timerZeroSeen && sessionStateRaceOrLater)
            {
                MaybeLatchLeaderFinished(
                    isClassLeader: true,
                    leaderIdx: classLeaderIdx,
                    lapDistPct,
                    trackSurfaces,
                    sessionTime,
                    sessionStateNumeric);

                if (overallLeaderIdx >= 0)
                {
                    MaybeLatchLeaderFinished(
                        isClassLeader: false,
                        leaderIdx: overallLeaderIdx,
                        lapDistPct,
                        trackSurfaces,
                        sessionTime,
                        sessionStateNumeric);
                }
            }
            else
            {
                if (classLeaderIdx >= 0 && lapDistPct != null && classLeaderIdx < lapDistPct.Length)
                {
                    _lastClassLeaderLapPct = lapDistPct[classLeaderIdx];
                    _lastClassLeaderCarIdx = classLeaderIdx;
                }

                if (overallLeaderIdx >= 0 && lapDistPct != null && overallLeaderIdx < lapDistPct.Length)
                {
                    _lastOverallLeaderLapPct = lapDistPct[overallLeaderIdx];
                    _lastOverallLeaderCarIdx = overallLeaderIdx;
                }
            }

            bool derivedLeaderBefore = LeaderHasFinished;
            bool derivedLeaderAfter = _isMultiClassSession
                ? (ClassLeaderHasFinishedValid && ClassLeaderHasFinished)
                : (OverallLeaderHasFinishedValid && OverallLeaderHasFinished);
            LeaderHasFinished = derivedLeaderAfter;

            if (!derivedLeaderBefore && derivedLeaderAfter)
            {
                _leaderFinishedSeen = true;
                _leaderCheckeredSessionTime = sessionTime;
                SimHub.Logging.Current.Info(
                    $"[LalaPlugin:Finish] leader_finish trigger=derived source={(_isMultiClassSession ? "class" : "overall")} " +
                    $"session_state={sessionStateNumeric} timer0_seen={_timerZeroSeen}");
            }

            // ----- DRIVER FINISH -----
            bool lapCompleted =
                (_lastCompletedLapForFinish >= 0) &&
                (completedLaps > _lastCompletedLapForFinish);

            _lastCompletedLapForFinish = completedLaps;
            _prevSessionTimeRemain = hasRemain ? sessionTimeRemain : double.NaN;

            bool checkeredFlag = ReadFlagBool(
                pluginManager,
                "DataCorePlugin.GameRawData.Telemetry.SessionFlagsDetails.IsCheckeredFlag",
                "DataCorePlugin.GameRawData.Telemetry.SessionFlagsDetails.IsCheckered"
            );

            bool sessionEnded = checkeredFlag || checkeredFlagData;

            if (lapCompleted && checkeredFlag)
            {
                _driverCheckeredSessionTime = sessionTime;

                bool leaderCheckeredKnown = !double.IsNaN(_leaderCheckeredSessionTime);
                double leaderExtra = leaderCheckeredKnown
                    ? ComputeObservedExtraSeconds(_leaderCheckeredSessionTime)
                    : double.NaN;
                double driverExtra = ComputeObservedExtraSeconds(_driverCheckeredSessionTime);

                string leaderAfterZeroText = leaderCheckeredKnown
                    ? $"{leaderExtra:F1}s"
                    : "n/a";

                SimHub.Logging.Current.Info(
                    $"[LalaPlugin:Finish] finish_latch trigger=driver_checkered timer0_s={FormatSecondsOrNA(_timerZeroSessionTime)} " +
                    $"leader_chk_s={FormatSecondsOrNA(_leaderCheckeredSessionTime)} driver_chk_s={FormatSecondsOrNA(_driverCheckeredSessionTime)} " +
                    $"leader_after0_s={leaderAfterZeroText} driver_after0_s={driverExtra:F1} " +
                    $"leader_finished={LeaderHasFinished} class_finished={ClassLeaderHasFinished} overall_finished={OverallLeaderHasFinished} " +
                    $"session_remain_s={FormatSecondsOrNA(sessionTimeRemain)}"
                );

                SessionSummaryRuntime.OnDriverCheckered(
                    _currentSessionToken,
                    completedLaps,
                    data.NewData?.Fuel ?? 0.0,
                    driverExtra,
                    null,
                    Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameData.IsReplay") ?? false),
                    sessionTime);

                MaybeLogAfterZeroResult(sessionTime, sessionEnded);
                ResetFinishTimingState();
            }
            else if (sessionEnded)
            {
                MaybeLogAfterZeroResult(sessionTime, sessionEnded);
            }
        }

        private static string CompactPercent(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            // "92 %", "92%" -> "92%"
            return value.Replace(" ", "").Trim();
        }

        private static string CompactTemp1dp(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;

            var trimmed = value.Trim();
            var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"([-+]?\d+(\.\d+)?)");
            if (!match.Success) return trimmed;

            if (!double.TryParse(match.Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var v))
                return trimmed;

            var rounded = v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

            // Replace ONLY the first numeric token
            return trimmed.Substring(0, match.Index)
                   + rounded
                   + trimmed.Substring(match.Index + match.Length);
        }

        private static (double seconds, bool isFallback) ReadLeaderLapTimeSeconds(
            PluginManager pluginManager,
            GameData data,
            double playerRecentAvg,
            double leaderAvgFallback,
            bool verboseLoggingEnabled)
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
                    SimHub.Logging.Current.Warn($"[LalaPlugin:Leader Lap] TryReadSeconds error for value '{raw}': {ex.Message}");
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
                if (verboseLoggingEnabled)
                {
                    SimHub.Logging.Current.Debug($"[LalaPlugin:Leader Lap] candidate source={candidate.Name} raw='{candidate.Raw}' parsed_s={seconds:F3}");
                }


                if (seconds > 0.0)
                {
                    double rejectionFloor = (playerRecentAvg > 0.0) ? playerRecentAvg * 0.5 : 0.0;
                    if (seconds < 30.0 || (rejectionFloor > 0.0 && seconds < rejectionFloor))
                    {
                        double fallback = leaderAvgFallback > 0.0 ? leaderAvgFallback : 0.0;
                        string rejectReason =
                            seconds < 30.0 ? "too_small" :
                            (rejectionFloor > 0.0 && seconds < rejectionFloor) ? "below_player_half" :
                            "unknown";

                        SimHub.Logging.Current.Info(
                            $"[LalaPlugin:Leader Lap] reject source={candidate.Name} sec={seconds:F3} " +
                            $"reason={rejectReason} player_last5_sec={playerRecentAvg:F3} min_sec={rejectionFloor:F3} " +
                            $"fallback_sec={fallback:F3}");

                        if (fallback > 0.0)
                        {
                            return (fallback, true);
                        }

                        continue;
                    }

                    SimHub.Logging.Current.Info(
                        $"[LalaPlugin:Leader Lap] using leader lap from {candidate.Name} = {seconds:F3}s");
                    return (seconds, false);
                }
            }

            SimHub.Logging.Current.Info("[LalaPlugin:Leader Lap] no valid leader lap time from any candidate – returning 0");
            return (0.0, false);
        }


        private void UpdateLiveSurfaceSummary(PluginManager pluginManager)
        {
            if (FuelCalculator == null) return;

            string airTemp = GetSurfaceText(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.WeekendInfo.TrackAirTemp"));
            string trackTemp = GetSurfaceText(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.WeekendInfo.TrackSurfaceTemp"));
            string humidity = GetSurfaceText(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.WeekendInfo.TrackRelativeHumidity"));
            string rubberState = GetSurfaceText(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.WeekendInfo.TrackRubberState"));
            string precipitation = GetSurfaceText(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.SessionData.WeekendInfo.TrackPrecipitation"));

            var parts = new List<string>();
            int trackWetness = ReadTrackWetness(pluginManager);
            TrackWetness = trackWetness;
            TrackWetnessLabel = MapWetnessLabel(trackWetness);
            bool isWet = _isWetMode;
            parts.Add(isWet ? "Wet" : "Dry");

            string tempSegment = ComposeTemperatureSegment(airTemp, trackTemp);
            if (!string.IsNullOrWhiteSpace(tempSegment)) parts.Add(tempSegment);

            if (!string.IsNullOrWhiteSpace(humidity)) parts.Add($"{CompactPercent(humidity)} humid");
            if (!string.IsNullOrWhiteSpace(rubberState)) parts.Add($"Rubber {rubberState}");
            if (!string.IsNullOrWhiteSpace(precipitation)) parts.Add($"{CompactPercent(precipitation)} Rain");

            string summary = parts.Count > 0 ? string.Join(" | ", parts) : "-";

            FuelCalculator.SetLiveSurfaceSummary(isWet, summary);
        }

        private static string GetSurfaceText(object value)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static string ComposeTemperatureSegment(string airTemp, string trackTemp)
        {
            airTemp = CompactTemp1dp(airTemp);
            trackTemp = CompactTemp1dp(trackTemp);

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

        private int ReadTrackWetness(PluginManager pluginManager)
        {
            object raw = pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.TrackWetness");
            if (raw == null) return 0;

            try
            {
                int value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                if (value < 0 || value > 7)
                {
                    return 0;
                }
                return value;
            }
            catch
            {
                return 0;
            }
        }

        private static string MapWetnessLabel(int trackWetness)
        {
            switch (trackWetness)
            {
                case 0:
                    return "NA";
                case 1:
                    return "Dry";
                case 2:
                    return "Moist";
                case 3:
                    return "Damp";
                case 4:
                    return "Light Wet";
                case 5:
                    return "Mod Wet";
                case 6:
                    return "Very Wet";
                case 7:
                    return "Monsoon";
                default:
                    return "NA";
            }
        }

        private bool TryReadIsWetTyres(PluginManager pluginManager, out int playerTireCompoundRaw, out string extraPropRaw, out string source)
        {
            source = "unknown";
            playerTireCompoundRaw = -1;
            extraPropRaw = null;

            // 1) iRacing primary: PlayerTireCompound (0=dry, 1=wet)
            object rawPlayer = pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.PlayerTireCompound");
            int? playerCompound = TryReadNullableInt(rawPlayer);
            if (playerCompound.HasValue)
            {
                playerTireCompoundRaw = playerCompound.Value;
                source = "PlayerTireCompound";
                return playerCompound.Value == 1;
            }

            // 2) ExtraProperties: iRacing_Player_TireCompound ("S"=dry, "M"=wet)
            object rawExtra = pluginManager.GetPropertyValue("iRacingExtraProperties.iRacing_Player_TireCompound");
            extraPropRaw = rawExtra == null ? "null" : Convert.ToString(rawExtra, CultureInfo.InvariantCulture) ?? "null";
            string extra = extraPropRaw;
            if (!string.IsNullOrWhiteSpace(extra))
            {
                string trimmed = extra.Trim();
                if (string.Equals(trimmed, "M", StringComparison.OrdinalIgnoreCase))
                {
                    source = "ExtraProp";
                    return true;
                }

                if (string.Equals(trimmed, "S", StringComparison.OrdinalIgnoreCase))
                {
                    source = "ExtraProp";
                    return false;
                }
            }

            // Nothing usable found
            return false;
        }

        private static int? TryReadNullableInt(object value)
        {
            if (value == null) return null;

            try
            {
                switch (value)
                {
                    case int i: return i;
                    case long l: return (int)l;
                    case double d: return (int)Math.Round(d);
                    case float f: return (int)Math.Round(f);
                    case string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                        return parsed;
                    default:
                        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
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

            // --- START PHASE FLAGS ---
            bool isStartReady = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.SessionFlagsDetails.IsStartReady") ?? false);
            bool isStartGo = Convert.ToBoolean(pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.Telemetry.SessionFlagsDetails.IsStartGo") ?? false);
            double speed = data.NewData?.SpeedKmh ?? 0;

            // --- Manual Launch Timeout Logic ---
            // ManualPrimed only: once launch starts (state changes), timeout no longer applies.
            if (_manualPrimedStartedAt != DateTime.MinValue && IsManualPrimed)
            {
                if ((DateTime.Now - _manualPrimedStartedAt).TotalSeconds > 30)
                {
                    _launchModeUserDisabled = true; // behave like user cancel; prevents immediate auto re-prime
                    CancelLaunchToIdle("Manual launch timed out after 30 seconds");

                    // Optional debug (only if speed/start flags are already in scope here)
                    SimHub.Logging.Current.Info($"[LalaPlugin:Launch] ManualPrimed timeout fired at speed={speed:0.0} startReady={isStartReady} startGo={isStartGo} userDisabled={_launchModeUserDisabled}");
                }
            }

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
            bool isRaceSession = IsRaceSession(data.NewData?.SessionTypeName);

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
                    SimHub.Logging.Current.Error($"[LalaPlugin:Launch Trace] CSV Logging Error: {ex.Message}");
                }
            }
        }

        #endregion

        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new LaunchPluginCombinedSettingsControl(this, _telemetryTraceLogger);
        }
    }

    public class LaunchPluginFriendEntry : INotifyPropertyChanged
    {
        public const string TagFriend = "Friend";
        public const string TagTeammate = "Teammate";
        public const string TagBad = "Bad";

        private string _name = "Friend";
        private int _userId;
        private string _tag = TagFriend;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Name
        {
            get => _name;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? "Friend" : value.Trim();
                if (normalized == _name)
                {
                    return;
                }

                _name = normalized;
                OnPropertyChanged();
            }
        }

        public int UserId
        {
            get => _userId;
            set
            {
                int normalized = value < 0 ? 0 : value;
                if (normalized == _userId)
                {
                    return;
                }

                _userId = normalized;
                OnPropertyChanged();
            }
        }

        public string Tag
        {
            get => _tag;
            set
            {
                var normalized = NormalizeTag(value);
                if (string.Equals(normalized, _tag, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _tag = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTeammate));
                OnPropertyChanged(nameof(IsBad));
            }
        }

        [JsonIgnore]
        public bool IsTeammate
        {
            get => string.Equals(Tag, TagTeammate, StringComparison.OrdinalIgnoreCase);
            set
            {
                if (value)
                {
                    Tag = TagTeammate;
                }
                else if (IsTeammate)
                {
                    Tag = TagFriend;
                }
            }
        }

        [JsonIgnore]
        public bool IsBad
        {
            get => string.Equals(Tag, TagBad, StringComparison.OrdinalIgnoreCase);
            set
            {
                if (value)
                {
                    Tag = TagBad;
                }
                else if (IsBad)
                {
                    Tag = TagFriend;
                }
            }
        }


        [JsonProperty("IsTeammate")]
        private bool LegacyIsTeammate
        {
            set
            {
                if (value && !string.Equals(_tag, TagBad, StringComparison.OrdinalIgnoreCase))
                {
                    _tag = TagTeammate;
                }
            }
        }

        [JsonProperty("IsBad")]
        private bool LegacyIsBad
        {
            set
            {
                if (value)
                {
                    _tag = TagBad;
                }
            }
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            _tag = NormalizeTag(_tag);
        }

        public static string NormalizeTag(string rawTag)
        {
            if (string.Equals(rawTag, TagTeammate, StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawTag, "Team", StringComparison.OrdinalIgnoreCase))
            {
                return TagTeammate;
            }

            if (string.Equals(rawTag, TagBad, StringComparison.OrdinalIgnoreCase))
            {
                return TagBad;
            }

            return TagFriend;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class LaunchPluginSettings : INotifyPropertyChanged
    {
        [JsonProperty]
        public int SchemaVersion { get; set; } = 2;

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }

        // --- Global Settings with Corrected Defaults ---
        public bool EnableSoftDebug { get; set; } = false;
        public bool DebugHide1 { get; set; } = false;
        public bool DebugHide2 { get; set; } = false;
        public bool DebugHide3 { get; set; } = false;
        public bool EnableDebugLogging { get; set; } = false;
        public bool EnableCarSADebugExport { get; set; } = false;
        public bool EnableOffTrackDebugCsv { get; set; } = false;
        public bool OffTrackDebugLogChangesOnly { get; set; } = false;
        public int CarSADebugExportCadence { get; set; } = 1;
        public int CarSADebugExportTickMaxHz { get; set; } = 20;
        public bool CarSADebugExportWriteEventsCsv { get; set; } = true;
        public int CarSARawTelemetryMode { get; set; } = 1;
        public int OffTrackDebugProbeCarIdx { get; set; } = -1;
        public bool PitExitVerboseLogging { get; set; } = false;
        public double ResultsDisplayTime { get; set; } = 5.0; // Corrected to 5 seconds
        public double FuelReadyConfidence { get; set; } = LalaLaunch.FuelReadyConfidenceDefault;
        public int StintFuelMarginPct { get; set; } = LalaLaunch.StintFuelMarginPctDefault;
        public bool EnableAutoDashSwitch { get; set; } = true;
        public bool EnableCsvLogging { get; set; } = true;
        public string CsvLogPath { get; set; } = "";
        public string TraceLogPath { get; set; } = "";
        public bool EnableTelemetryTracing { get; set; } = true;
        public bool ShiftAssistEnabled { get; set; } = false;
        public bool ShiftAssistLearningModeEnabled { get; set; } = false;
        public int ShiftAssistBeepDurationMs { get; set; } = LalaLaunch.ShiftAssistBeepDurationMsDefault;
        public int ShiftAssistLeadTimeMs { get; set; } = LalaLaunch.ShiftAssistLeadTimeMsDefault;
        public bool ShiftAssistBeepSoundEnabled { get; set; } = true;
        public int ShiftAssistBeepVolumePct { get; set; } = 100;
        public bool ShiftAssistUseCustomWav { get; set; } = false;
        public string ShiftAssistCustomWavPath { get; set; } = "";
        public bool EnableShiftAssistDebugCsv { get; set; } = false;
        public int ShiftAssistDebugCsvMaxHz { get; set; } = LalaLaunch.ShiftAssistDebugCsvMaxHzDefault;
        public double NotRelevantGapSec { get; set; } = LalaLaunch.CarSANotRelevantGapSecDefault;
        public Dictionary<int, string> CarSAStatusEBackgroundColors { get; set; } = new Dictionary<int, string>
        {
            { (int)CarSAStatusE.Unknown, "#000000" },
            { (int)CarSAStatusE.OutLap, "#696969" },
            { (int)CarSAStatusE.InPits, "#C0C0C0" },
            { (int)CarSAStatusE.SuspectInvalid, "#FFA500" },
            { (int)CarSAStatusE.CompromisedOffTrack, "#FF0000" },
            { (int)CarSAStatusE.CompromisedPenalty, "#FFA500" },
            { (int)CarSAStatusE.HotlapWarning, "#FF0000" },
            { (int)CarSAStatusE.HotlapCaution, "#FFFF00" },
            { (int)CarSAStatusE.HotlapHot, "#FFFF00" },
            { (int)CarSAStatusE.CoolLapWarning, "#FF0000" },
            { (int)CarSAStatusE.CoolLapCaution, "#FFFF00" },
            { (int)CarSAStatusE.FasterClass, "#000000" },
            { (int)CarSAStatusE.SlowerClass, "#000000" },
            { (int)CarSAStatusE.Racing, "#008000" },
            { (int)CarSAStatusE.LappingYou, "#0000FF" },
            { (int)CarSAStatusE.BeingLapped, "#ADD8E6" }
        };
        public Dictionary<string, string> CarSABorderColors { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { CarSAStyleResolver.BorderModeTeam, "#FF69B4" },
            { CarSAStyleResolver.BorderModeBad, "#FF0000" },
            { CarSAStyleResolver.BorderModeLead, "#FF00FF" },
            { CarSAStyleResolver.BorderModeOtherClass, "#0000FF" },
            { CarSAStyleResolver.BorderModeDefault, "#F5F5F5" }
        };
        public ObservableCollection<LaunchPluginFriendEntry> Friends { get; set; } = new ObservableCollection<LaunchPluginFriendEntry>();

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

        // --- Overlay Toggles (Default ON) ---
        public bool OverlayDashShowLaunchScreen { get; set; } = true;
        public bool OverlayDashShowPitLimiter { get; set; } = true;
        public bool OverlayDashShowPitScreen { get; set; } = true;
        public bool OverlayDashShowRejoinAssist { get; set; } = true;
        public bool OverlayDashShowVerboseMessaging { get; set; } = true;
        public bool OverlayDashShowRaceFlags { get; set; } = true;
        public bool OverlayDashShowRadioMessages { get; set; } = true;
        public bool OverlayDashShowTraffic { get; set; } = true;
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
                    if (_plugin?.IsVerboseDebugLoggingEnabledForExternal == true)
                    {
                        SimHub.Logging.Current.Debug($"[LalaPlugin:Launch Trace] Discarded trace file: {_currentFilePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"[LalaPlugin:Launch Trace] Failed to discard trace file: {ex.Message}");
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

            if (_plugin?.IsVerboseDebugLoggingEnabledForExternal == true)
            {
                SimHub.Logging.Current.Debug($"[LalaPlugin:Launch Trace] New launch trace file opened: {_currentFilePath}");
            }
                return _currentFilePath;
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"[LalaPlugin:Launch Trace] Failed to start new launch trace: {ex.Message}");
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
                    SimHub.Logging.Current.Error($"[LalaPlugin:Launch Trace] Failed to write telemetry data: {ex.Message}");
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
                SimHub.Logging.Current.Warn("[LalaPlugin:Launch Trace] Cannot append summary. Trace file path is invalid or file does not exist.");
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
            if (_plugin?.IsVerboseDebugLoggingEnabledForExternal == true)
            {
                SimHub.Logging.Current.Debug($"[LalaPlugin:Launch Trace] Successfully appended launch summary to {_currentFilePath}");
            }
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"[LalaPlugin:Launch Trace] Failed to append launch summary: {ex.Message}");
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
                if (_plugin?.IsVerboseDebugLoggingEnabledForExternal == true)
                {
                    SimHub.Logging.Current.Debug($"[LalaPlugin:Launch Trace] Closed launch trace file: {_currentFilePath}");
                }
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Error($"[LalaPlugin:Launch Trace] Error closing launch trace: {ex.Message}");

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
                SimHub.Logging.Current.Warn($"[LalaPlugin:Launch Trace] Trace directory not found: {tracePath}");
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
                SimHub.Logging.Current.Error($"[LalaPlugin:Launch Trace] Error getting launch trace files: {ex.Message}");
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
                SimHub.Logging.Current.Warn($"[LalaPlugin:Launch Trace] Trace file not found: {filePath}");
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
                SimHub.Logging.Current.Error($"[LalaPlugin:Launch Trace] Error reading launch trace file '{filePath}': {ex.Message}");
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
                SimHub.Logging.Current.Error($"[LalaPlugin:Launch Trace] Error parsing telemetry data row: {ex.Message}. Line: '{line}'");
                return null;
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"[LalaPlugin:Launch Trace] Failed parsing row: {ex.Message} | Line: {line}");
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
