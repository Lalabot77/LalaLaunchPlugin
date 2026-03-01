// In file: ProfilesManagerViewModel.cs

using SimHub.Plugins; // Required for this.GetPluginManager()
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using Microsoft.Win32;

namespace LaunchPlugin
{
    public struct TrackMarkersSnapshot
    {
        public double EntryPct { get; set; }
        public double ExitPct { get; set; }
        public DateTime? LastUpdatedUtc { get; set; }
        public bool Locked { get; set; }
        public bool HasData { get; set; }
    }

    public class ProfilesManagerViewModel : INotifyPropertyChanged
    {
        // Boilerplate for UI updates
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        // --- Private Fields ---
        private readonly PluginManager _pluginManager;
        private readonly Action<CarProfile> _applyProfileToLiveAction;
        private readonly Func<string> _getCurrentCarModel;
        private readonly Func<string> _getCurrentTrackName;
        private readonly Func<string, TrackMarkersSnapshot> _getTrackMarkersSnapshotForKey;
        private readonly Action<string, bool> _setTrackMarkersLockForKey;
        private readonly Action _reloadTrackMarkersFromDisk;
        private readonly Action<string> _resetTrackMarkersForKey;
        private readonly Func<string> _getCurrentGearStackId;
        private readonly Action<string> _setCurrentGearStackId;
        private readonly Func<bool> _getShiftAssistEnabled;
        private readonly Action<bool> _setShiftAssistEnabled;
        private readonly Func<bool> _getShiftAssistLearningModeEnabled;
        private readonly Action<bool> _setShiftAssistLearningModeEnabled;
        private readonly Func<int> _getShiftAssistBeepDurationMs;
        private readonly Action<int> _setShiftAssistBeepDurationMs;
        private readonly Func<bool> _getShiftAssistLightEnabled;
        private readonly Action<bool> _setShiftAssistLightEnabled;
        private readonly Func<int> _getShiftAssistLeadTimeMs;
        private readonly Action<int> _setShiftAssistLeadTimeMs;
        private readonly Func<bool> _getShiftAssistUseCustomWav;
        private readonly Action<bool> _setShiftAssistUseCustomWav;
        private readonly Func<string> _getShiftAssistCustomWavPath;
        private readonly Action<string> _setShiftAssistCustomWavPath;
        private readonly Func<bool> _getShiftAssistBeepSoundEnabled;
        private readonly Action<bool> _setShiftAssistBeepSoundEnabled;
        private readonly Func<int> _getShiftAssistBeepVolumePct;
        private readonly Action<int> _setShiftAssistBeepVolumePct;
        private readonly Func<bool> _getShiftAssistUrgentEnabled;
        private readonly Action<bool> _setShiftAssistUrgentEnabled;
        private readonly Action _playShiftAssistTestBeep;
        private readonly Action _shiftAssistResetLearningAction;
        private readonly Action _shiftAssistResetTargetsAction;
        private readonly Action _shiftAssistResetTargetsAndLearningAction;
        private readonly Action _shiftAssistResetDelayStatsAction;
        private readonly Action _shiftAssistApplyLearnedOverrideAction;
        private readonly DispatcherTimer _shiftAssistRuntimeTimer;
        private List<ShiftGearRow> _shiftGearRows;
        private readonly int[] _shiftStackEditRpm = new int[8];
        private readonly bool[] _shiftStackEditLocked = new bool[8];
        private string _previousShiftStackId;
        private bool _isInternalShiftStackSelectionChange;
        private const int ShiftAssistMaxStoredGears = 8;
        private const int ShiftAssistDefaultForwardGears = 6;
        private readonly string _profilesFilePath;
        private readonly string _legacyProfilesFilePath;
        private bool _suppressTrackMarkersLockAction;
        private int _lastRenderedShiftTargetRows;
        // --- PB constants ---
        private const int PB_MIN_MS = 30000;     // >= 30s
        private const int PB_MAX_MS = 1200000;   // <= 20min
        private const int PB_IMPROVE_MS = 50;    // >= 0.05s faster

        /// <summary>
        /// Try to update personal best for the given car+track. Returns true if updated.
        /// </summary>
        public bool TryUpdatePB(string carName, string trackKey, int lapMs)
        {
            return TryUpdatePBByCondition(carName, trackKey, lapMs, isWetEffective: false);
        }

        /// <summary>
        /// Try to update personal best for the given car+track based on track condition. Returns true if updated.
        /// </summary>
        public bool TryUpdatePBByCondition(string carName, string trackKey, int lapMs, bool isWetEffective)
        {
            if (string.IsNullOrWhiteSpace(carName) || string.IsNullOrWhiteSpace(trackKey))
            {
                SimHub.Logging.Current.Debug("[LalaPlugin:Pace] Reject PB: missing car/track.");
                return false;
            }
            if (lapMs < PB_MIN_MS || lapMs > PB_MAX_MS)
            {
                SimHub.Logging.Current.Debug($"[LalaPlugin:Pace] Reject PB: out of range ({lapMs} ms).");
                return false;
            }

            var car = EnsureCar(carName);

            // Resolve by key first, fall back to display name via the centralized resolver
            var ts = car.FindTrack(trackKey) ?? car.ResolveTrackByNameOrKey(trackKey);
            if (ts == null)
            {
                // Safety: ensure the track exists using the key as both key+display
                ts = car.EnsureTrack(trackKey, trackKey);
            }

            int? baselineMs = isWetEffective
                ? ts.BestLapMsWet
                : ts.BestLapMsDry;

            bool improved = !baselineMs.HasValue || lapMs <= baselineMs.Value - PB_IMPROVE_MS;
            if (!improved)
            {
                SimHub.Logging.Current.Debug(
                    $"[LalaPlugin:Pace] Reject PB ({(isWetEffective ? "wet" : "dry")}): not improved enough. " +
                    $"old={baselineMs} new={lapMs} (≥{PB_IMPROVE_MS} ms required).");
                return false;
            }

            if (isWetEffective)
            {
                ts.BestLapMsWet = lapMs;
                ts.BestLapTimeWetText = ts.MillisecondsToLapTimeString(ts.BestLapMsWet);
                ts.MarkBestLapUpdatedWet("Telemetry");
            }
            else
            {
                ts.BestLapMsDry = lapMs;
                ts.BestLapTimeDryText = ts.MillisecondsToLapTimeString(ts.BestLapMsDry);
                ts.MarkBestLapUpdatedDry("Telemetry");
            }
            SaveProfiles();

            // If Profiles tab is on this car, refresh so the new PB shows immediately
            var disp = System.Windows.Application.Current?.Dispatcher;
            void DoUi() { if (SelectedProfile == car) RefreshTracksForSelectedProfile(); }
            if (disp == null || disp.CheckAccess()) DoUi(); else disp.BeginInvoke((Action)DoUi);

            var pbText = isWetEffective ? ts.BestLapTimeWetText : ts.BestLapTimeDryText;
            SimHub.Logging.Current.Info($"[LalaPlugin:Pace] PB Updated ({(isWetEffective ? "wet" : "dry")}): {carName} @ '{ts.DisplayName}' -> {pbText}");
            return true;
        }

        // --- Public Properties for UI Binding ---
        public ICollectionView SortedCarProfiles { get; } // This will be a sorted view of the CarProfiles collection
        public ObservableCollection<CarProfile> CarProfiles { get; set; }

        private CarProfile _selectedProfile;
        public CarProfile SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (_selectedProfile != value)
                {
                    _selectedProfile = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsProfileSelected));

                    // Now we just call our new refresh method
                    RefreshTracksForSelectedProfile();

                    // Auto-select the live track or the first in the list
                    string liveTrackName = _getCurrentTrackName();
                    var liveTrack = TracksForSelectedProfile.FirstOrDefault(t => t.DisplayName.Equals(liveTrackName, StringComparison.OrdinalIgnoreCase));
                    SelectedTrack = liveTrack ?? TracksForSelectedProfile.FirstOrDefault();

                    RefreshTrackMarkersSnapshotForSelectedTrack();
                    SetSelectedShiftStackFromLiveOrDefault();
                    RebuildShiftGearRows();
                    OnPropertyChanged(nameof(ShiftStackIds));
                    OnPropertyChanged(nameof(ShiftGearRows));
                    OnPropertyChanged(nameof(ShiftAssistMaxTargetGears));
                    OnPropertyChanged(nameof(ShiftAssistCurrentGearRedlineHint));
                    OnPropertyChanged(nameof(ActiveShiftStackLabel));
                    OnPropertyChanged(nameof(IsSelectedStackActiveLiveStack));
                    OnPropertyChanged(nameof(ShiftAssistStackLearningStatsNotice));
                    OnPropertyChanged(nameof(ShiftAssistShiftLightMode));
                }
            }
        }

        public CarProfile EnsureCar(string carProfileName)
        {
            var car = GetProfileForCar(carProfileName);
            if (car != null)
            {
                SimHub.Logging.Current.Debug($"[LalaPlugin:Profile] EnsureCar('{carProfileName}') -> FOUND existing profile.");
                if (EnsurePitEntryDefaults(car, carProfileName))
                {
                    SaveProfiles();
                }
                return car;
            }
            if (car == null)
            {
                const string defaultProfileName = "Default Settings";
                var defaultProfile = CarProfiles.FirstOrDefault(p => p.ProfileName.Equals(defaultProfileName, StringComparison.OrdinalIgnoreCase));

                car = new CarProfile { ProfileName = carProfileName };

                if (defaultProfile != null)
                {
                    // Manually copy all properties from the default profile to the new one
                    car.TargetLaunchRPM = defaultProfile.TargetLaunchRPM;
                    car.OptimalRPMTolerance = defaultProfile.OptimalRPMTolerance;
                    car.TargetLaunchThrottle = defaultProfile.TargetLaunchThrottle;
                    car.OptimalThrottleTolerance = defaultProfile.OptimalThrottleTolerance;
                    car.TargetBitePoint = defaultProfile.TargetBitePoint;
                    car.BitePointTolerance = defaultProfile.BitePointTolerance;
                    car.BogDownFactorPercent = defaultProfile.BogDownFactorPercent;
                    car.AntiStallThreshold = defaultProfile.AntiStallThreshold;
                    car.FuelContingencyValue = defaultProfile.FuelContingencyValue;
                    car.IsContingencyInLaps = defaultProfile.IsContingencyInLaps;
                    car.WetFuelMultiplier = defaultProfile.WetFuelMultiplier;
                    car.TireChangeTime = defaultProfile.TireChangeTime;
                    car.RacePaceDeltaSeconds = defaultProfile.RacePaceDeltaSeconds;
                    car.RefuelRate = defaultProfile.RefuelRate;
                    car.BaseTankLitres = defaultProfile.BaseTankLitres;
                    car.DryConditionMultipliers = defaultProfile.DryConditionMultipliers?.Clone() ?? ConditionMultipliers.CreateDefaultDry();
                    car.WetConditionMultipliers = defaultProfile.WetConditionMultipliers?.Clone() ?? ConditionMultipliers.CreateDefaultWet();
                    car.RejoinWarningLingerTime = defaultProfile.RejoinWarningLingerTime;
                    car.RejoinWarningMinSpeed = defaultProfile.RejoinWarningMinSpeed;
                    car.SpinYawRateThreshold = defaultProfile.SpinYawRateThreshold;
                    car.TrafficApproachWarnSeconds = defaultProfile.TrafficApproachWarnSeconds;
                    car.PitEntryDecelMps2 = defaultProfile.PitEntryDecelMps2;
                    car.PitEntryBufferM = defaultProfile.PitEntryBufferM;
                    car.ShiftAssistShiftLightMode = defaultProfile.ShiftAssistShiftLightMode;
                    CloneShiftStacks(defaultProfile, car);
                }

                // Ensure the newly created car profile has a default track record
                car.EnsureTrack("Default", "Default");
                SimHub.Logging.Current.Debug($"[LalaPlugin:Profile] EnsureCar('{carProfileName}') -> CREATED new profile.");
                var disp = System.Windows.Application.Current?.Dispatcher;
                if (disp == null || disp.CheckAccess())
                {
                    CarProfiles.Add(car);
                }
                else
                {
                    disp.Invoke(() => CarProfiles.Add(car));
                }
                EnsurePitEntryDefaults(car, carProfileName);
                SaveProfiles();
            }
            return car;
        }



        public TrackStats EnsureCarTrack(string carProfileName, string trackName, string trackDisplay = null)
        {
            SimHub.Logging.Current.Debug($"[LalaPlugin:Profile] EnsureCarTrack('{carProfileName}', '{trackName}')");

            var car = EnsureCar(carProfileName);
            var display = string.IsNullOrWhiteSpace(trackDisplay) ? trackName : trackDisplay;
            var ts = car.EnsureTrack(trackName, display);
            SimHub.Logging.Current.Info($"[LalaPlugin:Profiles] Track resolved: key='{ts?.Key}', display='{ts?.DisplayName}'");

            // --- FIX: Manually initialize the text properties after creation ---
            // This is crucial because the UI now relies on them.
            ts.BestLapTimeDryText = ts.MillisecondsToLapTimeString(ts.BestLapMsDry);
            ts.BestLapTimeWetText = ts.MillisecondsToLapTimeString(ts.BestLapMsWet);
            ts.AvgLapTimeDryText = ts.MillisecondsToLapTimeString(ts.AvgLapTimeDry);
            ts.AvgLapTimeWetText = ts.MillisecondsToLapTimeString(ts.AvgLapTimeWet);
            ts.PitLaneLossSecondsText = ts.PitLaneLossSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ts.AvgFuelPerLapDryText = ts.AvgFuelPerLapDry?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ts.MinFuelPerLapDryText = ts.MinFuelPerLapDry?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ts.MaxFuelPerLapDryText = ts.MaxFuelPerLapDry?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ts.AvgFuelPerLapWetText = ts.AvgFuelPerLapWet?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ts.MinFuelPerLapWetText = ts.MinFuelPerLapWet?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ts.MaxFuelPerLapWetText = ts.MaxFuelPerLapWet?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ts.AvgDryTrackTempText = ts.AvgDryTrackTemp?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ts.AvgWetTrackTempText = ts.AvgWetTrackTemp?.ToString(System.Globalization.CultureInfo.InvariantCulture);

            SaveProfiles();
            var disp = System.Windows.Application.Current?.Dispatcher;

            void DoUiWork()
            {
                // 1) ensure the visible car is the instance in CarProfiles
                var carInList = CarProfiles.FirstOrDefault(p =>
                    p.ProfileName.Equals(car.ProfileName, StringComparison.OrdinalIgnoreCase));

                if (carInList != null && !ReferenceEquals(SelectedProfile, carInList))
                    SelectedProfile = carInList; // this may call RefreshTracksForSelectedProfile() internally

                // --- LOG A: state just before we mutate the tracks list
                SimHub.Logging.Current.Debug(
                    $"[LalaPlugin:Profile] UI Before refresh: SelectedProfile='{SelectedProfile?.ProfileName}', targetCar='{car.ProfileName}'");

                // 2) force refresh (mutating the existing collection)
                RefreshTracksForSelectedProfile();

                // --- LOG B: what does the bound list look like after refresh?
                var keysAfter = TracksForSelectedProfile?
                    .Select(t => t?.Key)
                    .Where(k => !string.IsNullOrWhiteSpace(k)) ?? Enumerable.Empty<string>();
                    SimHub.Logging.Current.Debug($"[LalaPlugin:Profile] UI After refresh: Track count={TracksForSelectedProfile?.Count ?? 0}, keys=[{string.Join(",", keysAfter)}]");

                // --- LOG C: what are we trying to select?
                SimHub.Logging.Current.Debug(
                    $"[LalaPlugin:Profile] UI Looking for track: key='{ts?.Key}', name='{ts?.DisplayName}'");

                // 3) select the track instance (no fallback add — track should already exist after EnsureCarTrack)
                TrackStats match = null;
                if (TracksForSelectedProfile != null)
                {
                    match = TracksForSelectedProfile.FirstOrDefault(x => x?.Key == ts?.Key)
                         ?? TracksForSelectedProfile.FirstOrDefault(x => string.Equals(x?.DisplayName, ts?.DisplayName, StringComparison.OrdinalIgnoreCase));
                }

                if (match != null)
                {
                    SelectedTrack = match;
                }
                else
                {
                    // Optional: keep a breadcrumb if something ever goes wrong again
                    SimHub.Logging.Current.Debug("[LalaPlugin:Profile] Track instance not found in TracksForSelectedProfile after refresh.");
                }

            }


            if (disp == null || disp.CheckAccess()) DoUiWork();
            else disp.Invoke(DoUiWork);

            return ts;
        }

        public void FocusTrack(string carProfileName, string trackKeyOrName, string trackDisplay = null)
        {
            if (string.IsNullOrWhiteSpace(carProfileName)) return;

            var car = EnsureCar(carProfileName);
            TrackStats track = null;

            if (!string.IsNullOrWhiteSpace(trackKeyOrName))
            {
                track = car.ResolveTrackByNameOrKey(trackKeyOrName);
            }

            if (track == null && !string.IsNullOrWhiteSpace(trackDisplay))
            {
                track = car.ResolveTrackByNameOrKey(trackDisplay);
            }

            if (track == null && !string.IsNullOrWhiteSpace(trackKeyOrName))
            {
                track = car.EnsureTrack(trackKeyOrName, trackDisplay ?? trackKeyOrName);
            }

            var disp = System.Windows.Application.Current?.Dispatcher;
            void DoFocus()
            {
                SelectedProfile = car;
                RefreshTracksForSelectedProfile();

                if (track == null) return;

                var match = TracksForSelectedProfile.FirstOrDefault(x => x?.Key == track.Key)
                           ?? TracksForSelectedProfile.FirstOrDefault(x => string.Equals(x?.DisplayName, track.DisplayName, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    SelectedTrack = match;
                }
                else
                {
                    SelectedTrack = track;
                }
            }

            if (disp == null || disp.CheckAccess()) DoFocus();
            else disp.Invoke((Action)DoFocus);
        }

        public TrackStats TryGetCarTrack(string carProfileName, string trackName)
        {
            var car = GetProfileForCar(carProfileName);
            return car?.ResolveTrackByNameOrKey(trackName);
        }

        public ObservableCollection<TrackStats> TracksForSelectedProfile { get; } = new ObservableCollection<TrackStats>();

        private TrackStats _selectedTrack;
        public TrackStats SelectedTrack
        {
            get => _selectedTrack;
            set
            {
                if (!ReferenceEquals(_selectedTrack, value))
                {
                    // Detach from old selection (important)
                    if (_selectedTrack != null)
                        _selectedTrack.RequestSaveProfiles = null;

                    _selectedTrack = value;

                    // Attach to new selection
                    if (_selectedTrack != null)
                        _selectedTrack.RequestSaveProfiles = SaveProfiles;

                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsTrackSelected));
                    RefreshTrackMarkersSnapshotForSelectedTrack();
                }
            }
        }


        public void RefreshTracksForSelectedProfile()
        {
            var disp = System.Windows.Application.Current?.Dispatcher;
            void DoRefresh()
            {
                // keep the same ObservableCollection instance so WPF bindings stay wired
                if (TracksForSelectedProfile == null) return;

                TracksForSelectedProfile.Clear();

                if (SelectedProfile?.TrackStats != null)
                {
                    // add in a stable order (name or key)
                    foreach (var t in SelectedProfile.TrackStats.Values
                                 .OrderBy(t => t.DisplayName ?? t.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                    {
                        TracksForSelectedProfile.Add(t);
                    }
                }

                // ensure selection points to the instance that’s in the list
                if (SelectedTrack != null && TracksForSelectedProfile.Count > 0)
                {
                    var same = TracksForSelectedProfile.FirstOrDefault(x => x?.Key == SelectedTrack.Key)
                           ?? TracksForSelectedProfile.FirstOrDefault(x => string.Equals(x?.DisplayName, SelectedTrack.DisplayName, StringComparison.OrdinalIgnoreCase));
                    if (same != null) SelectedTrack = same;
                }

                // nudge the view in case the control uses a CollectionView
                System.Windows.Data.CollectionViewSource.GetDefaultView(TracksForSelectedProfile)?.Refresh();

                RefreshTrackMarkersSnapshotForSelectedTrack();
            }

            if (disp == null || disp.CheckAccess()) DoRefresh();
            else disp.Invoke(DoRefresh);
        }


        public bool IsProfileSelected => SelectedProfile != null;
        public bool IsTrackSelected => SelectedTrack != null;

        public string PitLaneLossSecondsText
        {
            get => SelectedTrack?.PitLaneLossSecondsText;
            set
            {
                if (SelectedTrack == null) return;

                var parsed = SelectedTrack.StringToNullableDouble(value);
                double? rounded = parsed.HasValue ? (double?)Math.Round(parsed.Value, 2) : null;

                if (SelectedTrack.PitLaneLossSeconds != rounded)
                {
                    SelectedTrack.PitLaneLossSeconds = rounded;
                    SelectedTrack.PitLaneLossSource = "manual";
                    SelectedTrack.PitLaneLossUpdatedUtc = DateTime.UtcNow;
                    SaveProfiles();
                }
                OnPropertyChanged();
            }
        }

        public bool PitLaneLossLocked
        {
            get => SelectedTrack?.PitLaneLossLocked ?? false;
            set
            {
                if (SelectedTrack == null) return;
                if (SelectedTrack.PitLaneLossLocked != value)
                {
                    SelectedTrack.PitLaneLossLocked = value;
                    SaveProfiles();
                    SimHub.Logging.Current.Debug($"[LalaPlugin:Profile] PitLoss lock set to {value} for '{SelectedTrack.DisplayName}'.");
                    OnPropertyChanged();
                }
            }
        }

        private string _storedPitEntryPctText = "n/a";
        public string StoredPitEntryPctText
        {
            get => _storedPitEntryPctText;
            private set
            {
                if (_storedPitEntryPctText != value)
                {
                    _storedPitEntryPctText = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _storedPitExitPctText = "n/a";
        public string StoredPitExitPctText
        {
            get => _storedPitExitPctText;
            private set
            {
                if (_storedPitExitPctText != value)
                {
                    _storedPitExitPctText = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _trackMarkersLastUpdatedText = "n/a";
        public string TrackMarkersLastUpdatedText
        {
            get => _trackMarkersLastUpdatedText;
            private set
            {
                if (_trackMarkersLastUpdatedText != value)
                {
                    _trackMarkersLastUpdatedText = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _trackMarkersLocked;
        public bool TrackMarkersLocked
        {
            get => _trackMarkersLocked;
            set
            {
                if (_trackMarkersLocked != value)
                {
                    _trackMarkersLocked = value;
                    OnPropertyChanged();
                    if (!_suppressTrackMarkersLockAction && _setTrackMarkersLockForKey != null)
                    {
                        string key = SelectedTrack?.Key ?? SelectedTrack?.DisplayName;
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            _setTrackMarkersLockForKey(key, value);
                        }
                    }
                }
            }
        }

        public bool ShiftAssistEnabled
        {
            get => _getShiftAssistEnabled?.Invoke() == true;
            set
            {
                _setShiftAssistEnabled?.Invoke(value);
                OnPropertyChanged();
            }
        }

        public bool ShiftAssistLearningModeEnabled
        {
            get => _getShiftAssistLearningModeEnabled?.Invoke() == true;
            set
            {
                _setShiftAssistLearningModeEnabled?.Invoke(value);
                OnPropertyChanged();
            }
        }

        public int ShiftAssistBeepDurationMs
        {
            get => _getShiftAssistBeepDurationMs?.Invoke() ?? 250;
            set
            {
                int clamped = value;
                if (clamped < 100) clamped = 100;
                if (clamped > 1000) clamped = 1000;
                _setShiftAssistBeepDurationMs?.Invoke(clamped);
                OnPropertyChanged();
            }
        }

        public bool ShiftAssistLightEnabled
        {
            get => _getShiftAssistLightEnabled?.Invoke() != false;
            set
            {
                _setShiftAssistLightEnabled?.Invoke(value);
                OnPropertyChanged();
            }
        }

        public int ShiftAssistLeadTimeMs
        {
            get => _getShiftAssistLeadTimeMs?.Invoke() ?? 200;
            set
            {
                int clamped = value;
                if (clamped < 0) clamped = 0;
                if (clamped > 200) clamped = 200;
                _setShiftAssistLeadTimeMs?.Invoke(clamped);
                OnPropertyChanged();
            }
        }


        public int ShiftAssistShiftLightMode
        {
            get => SelectedProfile?.ShiftAssistShiftLightMode ?? 2;
            set
            {
                if (SelectedProfile == null)
                {
                    return;
                }

                int normalized = value;
                if (normalized < 0) normalized = 0;
                if (normalized > 2) normalized = 2;

                if (SelectedProfile.ShiftAssistShiftLightMode != normalized)
                {
                    SelectedProfile.ShiftAssistShiftLightMode = normalized;
                    OnPropertyChanged();
                    SaveProfiles();
                }
            }
        }


        public bool ShiftAssistUseCustomWav
        {
            get => _getShiftAssistUseCustomWav?.Invoke() == true;
            set
            {
                _setShiftAssistUseCustomWav?.Invoke(value);
                OnPropertyChanged();
            }
        }

        public string ShiftAssistCustomWavPath
        {
            get => _getShiftAssistCustomWavPath?.Invoke() ?? string.Empty;
            set
            {
                _setShiftAssistCustomWavPath?.Invoke(value ?? string.Empty);
                OnPropertyChanged();
            }
        }

        public bool ShiftAssistBeepSoundEnabled
        {
            get => _getShiftAssistBeepSoundEnabled?.Invoke() != false;
            set
            {
                _setShiftAssistBeepSoundEnabled?.Invoke(value);
                OnPropertyChanged();
            }
        }

        public int ShiftAssistBeepVolumePct
        {
            get => _getShiftAssistBeepVolumePct?.Invoke() ?? 100;
            set
            {
                int clamped = value;
                if (clamped < 0) clamped = 0;
                if (clamped > 100) clamped = 100;
                _setShiftAssistBeepVolumePct?.Invoke(clamped);
                OnPropertyChanged();
            }
        }

        public bool ShiftAssistUrgentEnabled
        {
            get => _getShiftAssistUrgentEnabled?.Invoke() != false;
            set
            {
                _setShiftAssistUrgentEnabled?.Invoke(value);
                OnPropertyChanged();
            }
        }

        private string _selectedShiftStackId = "Default";
        public bool ShiftStackIsDirty { get; private set; }
        public string SelectedShiftStackId
        {
            get => string.IsNullOrWhiteSpace(_selectedShiftStackId) ? "Default" : _selectedShiftStackId;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? "Default" : value.Trim();

                if (_isInternalShiftStackSelectionChange)
                {
                    if (string.Equals(_selectedShiftStackId, normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        OnPropertyChanged(nameof(SelectedShiftStackId));
                        return;
                    }

                    ApplySelectedShiftStackChange(normalized);
                    return;
                }

                if (string.Equals(_selectedShiftStackId, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (ShiftStackIsDirty)
                {
                    string previous = string.IsNullOrWhiteSpace(_previousShiftStackId) ? _selectedShiftStackId : _previousShiftStackId;
                    var choice = MessageBox.Show(
                        "Save changes to the current shift stack before switching?",
                        "Unsaved Shift Stack Changes",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning,
                        MessageBoxResult.Yes);

                    if (choice == MessageBoxResult.Cancel)
                    {
                        _isInternalShiftStackSelectionChange = true;
                        try
                        {
                            SelectedShiftStackId = previous;
                        }
                        finally
                        {
                            _isInternalShiftStackSelectionChange = false;
                        }

                        return;
                    }

                    if (choice == MessageBoxResult.Yes)
                    {
                        CommitShiftStackBufferToStored(previous);
                    }
                }

                ApplySelectedShiftStackChange(normalized);
            }
        }

        private void ApplySelectedShiftStackChange(string normalized)
        {
            _selectedShiftStackId = normalized;
            EnsureShiftStackForSelectedProfile(normalized);
            _setCurrentGearStackId?.Invoke(normalized);
            _previousShiftStackId = normalized;
            LoadShiftStackBuffer(normalized);

            OnPropertyChanged(nameof(SelectedShiftStackId));
            OnPropertyChanged(nameof(ActiveShiftStackLabel));
            OnPropertyChanged(nameof(IsSelectedStackActiveLiveStack));
            OnPropertyChanged(nameof(ShiftAssistStackLearningStatsNotice));
            OnPropertyChanged(nameof(ShiftStackIds));
            OnPropertyChanged(nameof(ShiftGearRows));
            OnPropertyChanged(nameof(ShiftAssistMaxTargetGears));
            OnPropertyChanged(nameof(ShiftAssistCurrentGearRedlineHint));
            CommandManager.InvalidateRequerySuggested();
        }

        public string ActiveLiveShiftStackId
        {
            get
            {
                string current = _getCurrentGearStackId?.Invoke();
                return string.IsNullOrWhiteSpace(current) ? "Default" : current.Trim();
            }
        }

        public bool IsSelectedStackActiveLiveStack => string.Equals(SelectedShiftStackId, ActiveLiveShiftStackId, StringComparison.OrdinalIgnoreCase);

        public string ShiftAssistStackLearningStatsNotice => string.Empty;

        public string ActiveShiftStackLabel => $"Active stack: {ActiveLiveShiftStackId}";

        public IEnumerable<string> ShiftStackIds
        {
            get
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add("Default");

                string current = _getCurrentGearStackId?.Invoke();
                if (!string.IsNullOrWhiteSpace(current)) set.Add(current.Trim());

                if (SelectedProfile?.ShiftAssistStacks != null)
                {
                    foreach (var key in SelectedProfile.ShiftAssistStacks.Keys)
                    {
                        if (!string.IsNullOrWhiteSpace(key)) set.Add(key.Trim());
                    }
                }

                return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }

        public int ShiftAssistMaxTargetGears
        {
            get
            {
                return ResolveShiftAssistTargetRowCount();
            }
        }

        private int ResolveShiftAssistTargetRowCount()
        {
            int maxGears = TryReadPluginInt("DataCorePlugin.GameData.CarSettings_MaxGears");
            if (maxGears <= 0)
            {
                maxGears = TryReadPluginInt("DataCorePlugin.GameRawData.SessionData.DriverInfo.DriverCarGearNumForward");
            }

            if (maxGears <= 0)
            {
                maxGears = SelectedProfile?.MaxForwardGearsHint ?? 0;
            }

            if (maxGears <= 0)
            {
                maxGears = ShiftAssistDefaultForwardGears;
            }

            int targets = maxGears - 1;
            if (targets < 1)
            {
                return 1;
            }

            if (targets > ShiftAssistMaxStoredGears)
            {
                return ShiftAssistMaxStoredGears;
            }

            return targets;
        }

        public string ShiftAssistLearningState
        {
            get
            {
                string state = TryReadPluginString("ShiftAssist.Learn.State");
                return string.IsNullOrWhiteSpace(state) ? "Waiting for data" : state;
            }
        }

        public int ShiftAssistLearningActiveGear => TryReadPluginInt("ShiftAssist.Learn.ActiveGear");

        public string ShiftAssistLearningActiveGearText
        {
            get
            {
                int gear = ShiftAssistLearningActiveGear;
                return gear > 0 ? gear.ToString(CultureInfo.InvariantCulture) : "Waiting for data";
            }
        }

        public string ShiftAssistLearningPeakAccelText
        {
            get
            {
                double peakAccel = TryReadPluginDouble("ShiftAssist.Learn.PeakAccelMps2");
                return peakAccel > 0.0
                    ? peakAccel.ToString("0.00", CultureInfo.InvariantCulture)
                    : "Waiting for data";
            }
        }

        public string ShiftAssistLearningPeakRpmText
        {
            get
            {
                int peakRpm = TryReadPluginInt("ShiftAssist.Learn.PeakRpm");
                return peakRpm > 0 ? peakRpm.ToString(CultureInfo.InvariantCulture) : "Waiting for data";
            }
        }

        public string ShiftAssistDelayPendingSummary
        {
            get
            {
                bool pending = TryReadPluginInt("ShiftAssist.Delay.Pending") > 0;
                if (!pending)
                {
                    return "None";
                }

                int pendingGear = TryReadPluginInt("ShiftAssist.Delay.PendingGear");
                int pendingAgeMs = TryReadPluginInt("ShiftAssist.Delay.PendingAgeMs");
                int targetGear = pendingGear > 0 ? pendingGear + 1 : 0;
                if (targetGear > 0)
                {
                    return string.Format(CultureInfo.InvariantCulture, "G{0} {1}ms", pendingGear, Math.Max(0, pendingAgeMs));
                }

                return string.Format(CultureInfo.InvariantCulture, "Pending {0}ms", Math.Max(0, pendingAgeMs));
            }
        }

        public string ShiftAssistCurrentGearRedlineHint
        {
            get
            {
                int redlineRpm = TryReadPluginInt("DataCorePlugin.GameData.CarSettings_CurrentGearRedLineRPM");
                if (redlineRpm <= 0)
                {
                    return string.Empty;
                }

                return $"Current gear redline (SimHub): {redlineRpm.ToString(CultureInfo.InvariantCulture)} RPM";
            }
        }

        public IEnumerable<ShiftGearRow> ShiftGearRows
        {
            get
            {
                if (_shiftGearRows == null)
                {
                    RebuildShiftGearRows();
                }

                return _shiftGearRows;
            }
        }

        private void RebuildShiftGearRows()
        {
            int targetRows = ResolveShiftAssistTargetRowCount();
            var rows = new List<ShiftGearRow>(targetRows);
            for (int i = 0; i < targetRows; i++)
            {
                int gearIdx = i;
                int rowGear = gearIdx + 1;
                var row = new ShiftGearRow
                {
                    GearLabel = $"Shift from Gear {rowGear}",
                    RpmText = _shiftStackEditRpm[gearIdx] > 0 ? _shiftStackEditRpm[gearIdx].ToString(CultureInfo.InvariantCulture) : string.Empty,
                    IsLocked = _shiftStackEditLocked[gearIdx],
                };

                row.SaveAction = txt =>
                {
                    int value;
                    if (!int.TryParse((txt ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 0)
                        value = 0;

                    bool changed = _shiftStackEditRpm[gearIdx] != value;
                    _shiftStackEditRpm[gearIdx] = value;
                    row.RpmText = value > 0 ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;

                    if (changed)
                    {
                        SetShiftStackDirty(true);
                    }

                    OnPropertyChanged(nameof(ShiftAssistCurrentGearRedlineHint));
                };

                row.SetLockAction = locked =>
                {
                    if (_shiftStackEditLocked[gearIdx] == locked)
                        return;

                    _shiftStackEditLocked[gearIdx] = locked;
                    row.IsLocked = locked;
                    SetShiftStackDirty(true);
                };

                rows.Add(row);
            }

            _shiftGearRows = rows;
            _lastRenderedShiftTargetRows = targetRows;
            RefreshShiftAssistRuntimeLiveStatsOnly();
        }

        public void RefreshShiftAssistRuntimeStats()
        {
            RefreshShiftAssistRuntimeLiveStatsOnly();
            OnPropertyChanged(nameof(ShiftGearRows));
        }

        public void RefreshShiftAssistRuntimeLiveStatsOnly()
        {
            int targetRows = ResolveShiftAssistTargetRowCount();
            bool shouldRebuildRows = _shiftGearRows == null || _lastRenderedShiftTargetRows != targetRows;
            if (shouldRebuildRows)
            {
                RebuildShiftGearRows();
                OnPropertyChanged(nameof(ShiftGearRows));
                OnPropertyChanged(nameof(ShiftAssistMaxTargetGears));
                return;
            }

            OnPropertyChanged(nameof(ShiftAssistLearningState));
            OnPropertyChanged(nameof(ShiftAssistLearningActiveGear));
            OnPropertyChanged(nameof(ShiftAssistLearningActiveGearText));
            OnPropertyChanged(nameof(ShiftAssistLearningPeakAccelText));
            OnPropertyChanged(nameof(ShiftAssistLearningPeakRpmText));
            OnPropertyChanged(nameof(ShiftAssistDelayPendingSummary));
            OnPropertyChanged(nameof(ActiveLiveShiftStackId));
            OnPropertyChanged(nameof(ActiveShiftStackLabel));
            OnPropertyChanged(nameof(IsSelectedStackActiveLiveStack));
            OnPropertyChanged(nameof(ShiftAssistStackLearningStatsNotice));

            if (_shiftGearRows == null)
            {
                return;
            }

            for (int i = 0; i < _shiftGearRows.Count; i++)
            {
                int rowGear = i + 1;
                int avgDelayMs = TryReadPluginInt($"ShiftAssist.DelayAvg_G{rowGear}");
                int delaySamples = TryReadPluginInt($"ShiftAssist.DelayN_G{rowGear}");
                int learnedRpm = TryReadPluginInt($"ShiftAssist.Learn.LearnedRpm_G{rowGear}");
                int learnedSamples = TryReadPluginInt($"ShiftAssist.Learn.Samples_G{rowGear}");

                _shiftGearRows[i].UpdateRuntimeStats(
                    learnedRpm > 0 ? learnedRpm.ToString(CultureInfo.InvariantCulture) : "—",
                    learnedSamples > 0 ? $"x{learnedSamples.ToString(CultureInfo.InvariantCulture)}" : "x0",
                    delaySamples > 0 && avgDelayMs > 0 ? avgDelayMs.ToString(CultureInfo.InvariantCulture) : "—",
                    delaySamples > 0 ? $"x{delaySamples.ToString(CultureInfo.InvariantCulture)}" : "x0");

                bool stackLock = i < _shiftStackEditLocked.Length && _shiftStackEditLocked[i];
                if (_shiftGearRows[i].IsLocked != stackLock)
                {
                    _shiftGearRows[i].IsLocked = stackLock;
                }
            }
        }

        private int TryReadPluginInt(string propertyName)
        {
            if (_pluginManager == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return 0;
            }

            try
            {
                object raw = TryGetPluginPropertyValue(propertyName);
                if (raw == null)
                {
                    return 0;
                }

                if (raw is int intValue)
                {
                    return intValue;
                }

                if (raw is short shortValue)
                {
                    return shortValue;
                }

                if (raw is byte byteValue)
                {
                    return byteValue;
                }

                if (raw is long longValue)
                {
                    if (longValue < int.MinValue || longValue > int.MaxValue)
                    {
                        return 0;
                    }

                    return (int)longValue;
                }

                if (raw is string text)
                {
                    int parsed;
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                    {
                        return parsed;
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        private double TryReadPluginDouble(string propertyName)
        {
            if (_pluginManager == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return 0.0;
            }

            try
            {
                object raw = TryGetPluginPropertyValue(propertyName);
                if (raw == null)
                {
                    return 0.0;
                }

                if (raw is double d)
                {
                    return d;
                }

                if (raw is float f)
                {
                    return f;
                }

                if (raw is decimal m)
                {
                    return (double)m;
                }

                if (raw is string text)
                {
                    double parsed;
                    if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed))
                    {
                        return parsed;
                    }
                }
            }
            catch
            {
            }

            return 0.0;
        }

        private string TryReadPluginString(string propertyName)
        {
            if (_pluginManager == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return string.Empty;
            }

            try
            {
                object raw = TryGetPluginPropertyValue(propertyName);
                return raw?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private object TryGetPluginPropertyValue(string propertyName)
        {
            object raw = _pluginManager.GetPropertyValue(propertyName);
            if (raw != null)
            {
                return raw;
            }

            if (string.IsNullOrWhiteSpace(propertyName)
                || propertyName.StartsWith("LalaLaunch.", StringComparison.OrdinalIgnoreCase)
                || propertyName.StartsWith("DataCorePlugin.", StringComparison.OrdinalIgnoreCase)
                || propertyName.StartsWith("IRacingExtraProperties.", StringComparison.OrdinalIgnoreCase)
                || propertyName.StartsWith("ShakeIT", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return _pluginManager.GetPropertyValue($"LalaLaunch.{propertyName}");
        }


        // --- Commands for UI Buttons ---
        public RelayCommand NewProfileCommand { get; }
        public RelayCommand CopySettingsCommand { get; }
        public RelayCommand DeleteProfileCommand { get; }
        public RelayCommand SaveChangesCommand { get; }
        public RelayCommand ApplyToLiveCommand { get; }
        public RelayCommand DeleteTrackCommand { get; }
        public RelayCommand ReloadTrackMarkersCommand { get; }
        public RelayCommand RelearnPitDataCommand { get; }
        public RelayCommand RelearnDryCommand { get; }
        public RelayCommand RelearnWetCommand { get; }
        public RelayCommand LearnBaseTankCommand { get; }
        public RelayCommand ShiftAddCurrentStackCommand { get; }
        public RelayCommand ShiftSaveStackAsCommand { get; }
        public RelayCommand ShiftDeleteStackCommand { get; }
        public ICommand SaveShiftStackCommand { get; }
        public ICommand RevertShiftStackCommand { get; }
        public RelayCommand ShiftBrowseCustomWavCommand { get; }
        public RelayCommand ShiftUseEmbeddedDefaultCommand { get; }
        public RelayCommand ShiftTestBeepCommand { get; }
        public RelayCommand ShiftResetLearningCommand { get; }
        public RelayCommand ShiftResetTargetsCommand { get; }
        public RelayCommand ShiftResetTargetsAndLearningCommand { get; }
        public RelayCommand ShiftResetDelaysCommand { get; }
        public RelayCommand ShiftApplyLearnedOverrideCommand { get; }


        public ProfilesManagerViewModel(PluginManager pluginManager, Action<CarProfile> applyProfileToLiveAction, Func<string> getCurrentCarModel, Func<string> getCurrentTrackName, Func<string, TrackMarkersSnapshot> getTrackMarkersSnapshotForKey, Action<string, bool> setTrackMarkersLockForKey, Action reloadTrackMarkersFromDisk, Action<string> resetTrackMarkersForKey, Func<string> getCurrentGearStackId, Action<string> setCurrentGearStackId, Func<bool> getShiftAssistEnabled, Action<bool> setShiftAssistEnabled, Func<bool> getShiftAssistLearningModeEnabled, Action<bool> setShiftAssistLearningModeEnabled, Func<int> getShiftAssistBeepDurationMs, Action<int> setShiftAssistBeepDurationMs, Func<bool> getShiftAssistLightEnabled, Action<bool> setShiftAssistLightEnabled, Func<int> getShiftAssistLeadTimeMs, Action<int> setShiftAssistLeadTimeMs, Func<bool> getShiftAssistUseCustomWav, Action<bool> setShiftAssistUseCustomWav, Func<string> getShiftAssistCustomWavPath, Action<string> setShiftAssistCustomWavPath, Func<bool> getShiftAssistBeepSoundEnabled, Action<bool> setShiftAssistBeepSoundEnabled, Func<int> getShiftAssistBeepVolumePct, Action<int> setShiftAssistBeepVolumePct, Func<bool> getShiftAssistUrgentEnabled, Action<bool> setShiftAssistUrgentEnabled, Action playShiftAssistTestBeep, Action shiftAssistResetLearningAction, Action shiftAssistResetTargetsAction, Action shiftAssistResetTargetsAndLearningAction, Action shiftAssistResetDelayStatsAction, Action shiftAssistApplyLearnedOverrideAction)
        {
            _pluginManager = pluginManager;
            _applyProfileToLiveAction = applyProfileToLiveAction;
            _getCurrentCarModel = getCurrentCarModel;
            _getCurrentTrackName = getCurrentTrackName;
            _getTrackMarkersSnapshotForKey = getTrackMarkersSnapshotForKey;
            _setTrackMarkersLockForKey = setTrackMarkersLockForKey;
            _reloadTrackMarkersFromDisk = reloadTrackMarkersFromDisk;
            _resetTrackMarkersForKey = resetTrackMarkersForKey;
            _getCurrentGearStackId = getCurrentGearStackId;
            _setCurrentGearStackId = setCurrentGearStackId;
            _getShiftAssistEnabled = getShiftAssistEnabled;
            _setShiftAssistEnabled = setShiftAssistEnabled;
            _getShiftAssistLearningModeEnabled = getShiftAssistLearningModeEnabled;
            _setShiftAssistLearningModeEnabled = setShiftAssistLearningModeEnabled;
            _getShiftAssistBeepDurationMs = getShiftAssistBeepDurationMs;
            _setShiftAssistBeepDurationMs = setShiftAssistBeepDurationMs;
            _getShiftAssistLightEnabled = getShiftAssistLightEnabled;
            _setShiftAssistLightEnabled = setShiftAssistLightEnabled;
            _getShiftAssistLeadTimeMs = getShiftAssistLeadTimeMs;
            _setShiftAssistLeadTimeMs = setShiftAssistLeadTimeMs;
            _getShiftAssistUseCustomWav = getShiftAssistUseCustomWav;
            _setShiftAssistUseCustomWav = setShiftAssistUseCustomWav;
            _getShiftAssistCustomWavPath = getShiftAssistCustomWavPath;
            _setShiftAssistCustomWavPath = setShiftAssistCustomWavPath;
            _getShiftAssistBeepSoundEnabled = getShiftAssistBeepSoundEnabled;
            _setShiftAssistBeepSoundEnabled = setShiftAssistBeepSoundEnabled;
            _getShiftAssistBeepVolumePct = getShiftAssistBeepVolumePct;
            _setShiftAssistBeepVolumePct = setShiftAssistBeepVolumePct;
            _getShiftAssistUrgentEnabled = getShiftAssistUrgentEnabled;
            _setShiftAssistUrgentEnabled = setShiftAssistUrgentEnabled;
            _playShiftAssistTestBeep = playShiftAssistTestBeep;
            _shiftAssistResetLearningAction = shiftAssistResetLearningAction;
            _shiftAssistResetTargetsAction = shiftAssistResetTargetsAction;
            _shiftAssistResetTargetsAndLearningAction = shiftAssistResetTargetsAndLearningAction;
            _shiftAssistResetDelayStatsAction = shiftAssistResetDelayStatsAction;
            _shiftAssistApplyLearnedOverrideAction = shiftAssistApplyLearnedOverrideAction;
            CarProfiles = new ObservableCollection<CarProfile>();

            // Define the path for the JSON file in SimHub's common storage folder
            _profilesFilePath = PluginStorage.GetPluginFilePath("CarProfiles.json");
            _legacyProfilesFilePath = PluginStorage.GetCommonFilePath("LalaLaunch_CarProfiles.json");

            // Initialize Commands
            NewProfileCommand = new RelayCommand(p => NewProfile());
            CopySettingsCommand = new RelayCommand(p => CopySettings(), p => IsProfileSelected);
            DeleteProfileCommand = new RelayCommand(p => DeleteProfile(), p => IsProfileSelected && SelectedProfile.ProfileName != "Default Settings");
            SaveChangesCommand = new RelayCommand(p => SaveProfiles());
            ApplyToLiveCommand = new RelayCommand(p => ApplySelectedProfileToLive(), p => IsProfileSelected);
            DeleteTrackCommand = new RelayCommand(p => DeleteTrack(), p => SelectedTrack != null);
            ReloadTrackMarkersCommand = new RelayCommand(p => ReloadTrackMarkers());
            RelearnPitDataCommand = new RelayCommand(p => RelearnPitData(), p => SelectedTrack != null);
            RelearnDryCommand = new RelayCommand(p => RelearnDryConditions(), p => SelectedTrack != null);
            RelearnWetCommand = new RelayCommand(p => RelearnWetConditions(), p => SelectedTrack != null);
            LearnBaseTankCommand = new RelayCommand(p => LearnBaseTankFromLive(), p => IsProfileSelected);
            ShiftAddCurrentStackCommand = new RelayCommand(p => AddCurrentShiftStack(), p => IsProfileSelected);
            ShiftSaveStackAsCommand = new RelayCommand(p => SaveShiftStackAs(), p => IsProfileSelected);
            ShiftDeleteStackCommand = new RelayCommand(p => DeleteSelectedShiftStack(), p => CanDeleteSelectedShiftStack());
            SaveShiftStackCommand = new RelayCommand(p => SaveSelectedShiftStack(), p => IsProfileSelected);
            RevertShiftStackCommand = new RelayCommand(p => RevertSelectedShiftStack(), p => IsProfileSelected);
            ShiftBrowseCustomWavCommand = new RelayCommand(p => BrowseShiftCustomWav());
            ShiftUseEmbeddedDefaultCommand = new RelayCommand(p => UseEmbeddedDefaultSound());
            ShiftTestBeepCommand = new RelayCommand(p => _playShiftAssistTestBeep?.Invoke());
            ShiftResetLearningCommand = new RelayCommand(p => ExecuteShiftAssistActionOnSelectedStack(_shiftAssistResetLearningAction), p => IsProfileSelected);
            ShiftResetTargetsCommand = new RelayCommand(p => ExecuteShiftAssistActionOnSelectedStack(_shiftAssistResetTargetsAction), p => IsProfileSelected);
            ShiftResetTargetsAndLearningCommand = new RelayCommand(p => ExecuteShiftAssistActionOnSelectedStack(_shiftAssistResetTargetsAndLearningAction), p => IsProfileSelected);
            ShiftResetDelaysCommand = new RelayCommand(p => ExecuteShiftAssistActionOnSelectedStack(_shiftAssistResetDelayStatsAction), p => IsProfileSelected);
            ShiftApplyLearnedOverrideCommand = new RelayCommand(p => ExecuteShiftAssistActionOnSelectedStack(_shiftAssistApplyLearnedOverrideAction), p => IsProfileSelected);
            SortedCarProfiles = CollectionViewSource.GetDefaultView(CarProfiles);
            SortedCarProfiles.SortDescriptions.Add(new SortDescription(nameof(CarProfile.ProfileName), ListSortDirection.Ascending));

            _shiftAssistRuntimeTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _shiftAssistRuntimeTimer.Tick += ShiftAssistRuntimeTimer_Tick;
        }

        private void ShiftAssistRuntimeTimer_Tick(object sender, EventArgs e)
        {
            RefreshShiftAssistRuntimeLiveStatsOnly();
        }

        public void StartShiftAssistRuntimeTimer()
        {
            if (_shiftAssistRuntimeTimer != null && !_shiftAssistRuntimeTimer.IsEnabled)
                _shiftAssistRuntimeTimer.Start();
        }

        public void StopShiftAssistRuntimeTimer()
        {
            if (_shiftAssistRuntimeTimer != null && _shiftAssistRuntimeTimer.IsEnabled)
                _shiftAssistRuntimeTimer.Stop();
        }

        public CarProfile GetProfileForCar(string carName)
        {
            // Find a profile that matches the car name, case-insensitively
            return CarProfiles.FirstOrDefault(p => p.ProfileName.Equals(carName, StringComparison.OrdinalIgnoreCase));
        }

        private void NewProfile()
        {
            const string defaultProfileName = "Default Settings";
            var defaultProfile = CarProfiles.FirstOrDefault(p => p.ProfileName.Equals(defaultProfileName, StringComparison.OrdinalIgnoreCase));

            // Create a new profile object
            var newProfile = new CarProfile();

            // If we found a master default profile, copy its values to the new one
            if (defaultProfile != null)
            {
                // Manually copy properties from the default profile
                newProfile.TargetLaunchRPM = defaultProfile.TargetLaunchRPM;
                newProfile.OptimalRPMTolerance = defaultProfile.OptimalRPMTolerance;
                newProfile.TargetLaunchThrottle = defaultProfile.TargetLaunchThrottle;
                newProfile.OptimalThrottleTolerance = defaultProfile.OptimalThrottleTolerance;
                newProfile.TargetBitePoint = defaultProfile.TargetBitePoint;
                newProfile.BitePointTolerance = defaultProfile.BitePointTolerance;
                newProfile.BogDownFactorPercent = defaultProfile.BogDownFactorPercent;
                newProfile.AntiStallThreshold = defaultProfile.AntiStallThreshold;
                newProfile.FuelContingencyValue = defaultProfile.FuelContingencyValue;
                newProfile.IsContingencyInLaps = defaultProfile.IsContingencyInLaps;
                newProfile.WetFuelMultiplier = defaultProfile.WetFuelMultiplier;
                newProfile.RefuelRate = defaultProfile.RefuelRate;
                newProfile.BaseTankLitres = defaultProfile.BaseTankLitres;
                newProfile.RejoinWarningLingerTime = defaultProfile.RejoinWarningLingerTime;
                newProfile.RejoinWarningMinSpeed = defaultProfile.RejoinWarningMinSpeed;
                newProfile.SpinYawRateThreshold = defaultProfile.SpinYawRateThreshold;
                newProfile.TrafficApproachWarnSeconds = defaultProfile.TrafficApproachWarnSeconds;
                newProfile.RacePaceDeltaSeconds = defaultProfile.RacePaceDeltaSeconds;
                newProfile.PitEntryDecelMps2 = defaultProfile.PitEntryDecelMps2;
                newProfile.PitEntryBufferM = defaultProfile.PitEntryBufferM;
                newProfile.ShiftAssistShiftLightMode = defaultProfile.ShiftAssistShiftLightMode;
                CloneShiftStacks(defaultProfile, newProfile);
            }

            // Ensure the new profile has a unique name
            int count = 2;
            string baseName = "New Profile";
            newProfile.ProfileName = baseName;
            while (CarProfiles.Any(p => p.ProfileName.Equals(newProfile.ProfileName, StringComparison.OrdinalIgnoreCase)))
            {
                newProfile.ProfileName = $"{baseName} ({count++})";
            }
            newProfile.EnsureTrack("Default", "Default");
            CarProfiles.Add(newProfile);
            SelectedProfile = newProfile;
            SaveProfiles();
        }

        private void CopySettings()
        {
            if (SelectedProfile == null) return;

            // Create and show the dialog
            var dialog = new CopyProfileDialog(SelectedProfile, CarProfiles.Where(p => p != SelectedProfile));
            if (dialog.ShowDialog() == true)
            {
                if (dialog.IsCreatingNew)
                {
                    // Logic for CLONING to a new profile
                    var newProfile = new CarProfile();
                    // Copy properties from source (SelectedProfile) to newProfile
                    CopyProfileProperties(SelectedProfile, newProfile);
                    newProfile.ProfileName = dialog.NewProfileName;

                    CarProfiles.Add(newProfile);
                    SelectedProfile = newProfile;
                }
                else
                {
                    // Logic for COPYING to an existing profile
                    var destination = dialog.DestinationProfile;
                    if (MessageBox.Show($"This will overwrite all settings in '{destination.ProfileName}' with the settings from '{SelectedProfile.ProfileName}'.\n\nAre you sure you want to continue?",
                        "Confirm Overwrite", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    {
                        CopyProfileProperties(SelectedProfile, destination);
                    }
                }
                SaveProfiles();
            }
        }

        // Helper method to avoid duplicating code
        private static void CloneShiftStacks(CarProfile source, CarProfile destination)
        {
            destination.ShiftAssistStacks = new Dictionary<string, ShiftStackData>(StringComparer.OrdinalIgnoreCase);
            if (source?.ShiftAssistStacks == null)
            {
                return;
            }

            foreach (var kvp in source.ShiftAssistStacks)
            {
                var src = kvp.Value ?? new ShiftStackData();
                destination.ShiftAssistStacks[kvp.Key] = src.Clone();
            }
        }

        private void CopyProfileProperties(CarProfile source, CarProfile destination)
        {
            // This copies every setting except the name
            destination.TargetLaunchRPM = source.TargetLaunchRPM;
            destination.OptimalRPMTolerance = source.OptimalRPMTolerance;
            destination.TargetLaunchThrottle = source.TargetLaunchThrottle;
            destination.OptimalThrottleTolerance = source.OptimalThrottleTolerance;
            destination.TargetBitePoint = source.TargetBitePoint;
            destination.BitePointTolerance = source.BitePointTolerance;
            destination.BogDownFactorPercent = source.BogDownFactorPercent;
            destination.AntiStallThreshold = source.AntiStallThreshold;
            destination.FuelContingencyValue = source.FuelContingencyValue;
            destination.IsContingencyInLaps = source.IsContingencyInLaps;
            destination.WetFuelMultiplier = source.WetFuelMultiplier;
            destination.RejoinWarningLingerTime = source.RejoinWarningLingerTime;
            destination.RejoinWarningMinSpeed = source.RejoinWarningMinSpeed;
            destination.SpinYawRateThreshold = source.SpinYawRateThreshold;
            destination.TrafficApproachWarnSeconds = source.TrafficApproachWarnSeconds;
            destination.RacePaceDeltaSeconds = source.RacePaceDeltaSeconds;
            destination.RefuelRate = source.RefuelRate;
            destination.BaseTankLitres = source.BaseTankLitres;
            destination.PitEntryDecelMps2 = source.PitEntryDecelMps2;
            destination.PitEntryBufferM = source.PitEntryBufferM;
            destination.ShiftAssistShiftLightMode = source.ShiftAssistShiftLightMode;

            CloneShiftStacks(source, destination);
            destination.MaxForwardGearsHint = source.MaxForwardGearsHint;
        }

        private ShiftStackData EnsureShiftStackForSelectedProfile(string stackId)
        {
            if (SelectedProfile == null)
            {
                return new ShiftStackData();
            }

            return SelectedProfile.EnsureShiftStack(stackId);
        }

        private void SetSelectedShiftStackFromLiveOrDefault()
        {
            string live = _getCurrentGearStackId?.Invoke();
            string preferred = !string.IsNullOrWhiteSpace(live) ? live.Trim() : "Default";
            EnsureShiftStackForSelectedProfile(preferred);
            _selectedShiftStackId = preferred;
            _previousShiftStackId = preferred;
            _setCurrentGearStackId?.Invoke(preferred);
            LoadShiftStackBuffer(preferred);
        }

        private void LoadShiftStackBuffer(string stackId)
        {
            for (int i = 0; i < ShiftAssistMaxStoredGears; i++)
            {
                _shiftStackEditRpm[i] = 0;
                _shiftStackEditLocked[i] = false;
            }

            if (SelectedProfile != null)
            {
                var stack = SelectedProfile.EnsureShiftStack(stackId);
                int count = Math.Min(ShiftAssistMaxStoredGears, Math.Min(stack.ShiftRPM.Length, stack.ShiftLocked.Length));
                for (int i = 0; i < count; i++)
                {
                    _shiftStackEditRpm[i] = stack.ShiftRPM[i];
                    _shiftStackEditLocked[i] = stack.ShiftLocked[i];
                }
            }

            SetShiftStackDirty(false);
            RebuildShiftGearRows();
            OnPropertyChanged(nameof(ShiftGearRows));
        }

        private void CommitShiftStackBufferToStored(string stackId)
        {
            if (SelectedProfile == null)
            {
                SetShiftStackDirty(false);
                return;
            }

            var stack = SelectedProfile.EnsureShiftStack(stackId);
            int count = Math.Min(ShiftAssistMaxStoredGears, Math.Min(stack.ShiftRPM.Length, stack.ShiftLocked.Length));
            for (int i = 0; i < count; i++)
            {
                stack.ShiftRPM[i] = _shiftStackEditRpm[i];
                stack.ShiftLocked[i] = _shiftStackEditLocked[i];
            }

            SetShiftStackDirty(false);
            SaveProfiles();
        }

        private void SetShiftStackDirty(bool value)
        {
            if (ShiftStackIsDirty == value)
            {
                return;
            }

            ShiftStackIsDirty = value;
            OnPropertyChanged(nameof(ShiftStackIsDirty));
            CommandManager.InvalidateRequerySuggested();
        }

        private void ExecuteShiftAssistActionOnSelectedStack(Action action)
        {
            if (!IsProfileSelected)
            {
                return;
            }

            string selectedStack = SelectedShiftStackId;
            EnsureShiftStackForSelectedProfile(selectedStack);
            _setCurrentGearStackId?.Invoke(selectedStack);
            action?.Invoke();
            RefreshShiftAssistRuntimeStats();
        }

        private void AddCurrentShiftStack()
        {
            if (SelectedProfile == null) return;
            string live = _getCurrentGearStackId?.Invoke();
            string stack = string.IsNullOrWhiteSpace(live) ? "Default" : live.Trim();
            EnsureShiftStackForSelectedProfile(stack);
            TryChangeSelectedShiftStack(stack);
            SaveProfiles();
        }

        private bool TryChangeSelectedShiftStack(string targetId)
        {
            string normalized = string.IsNullOrWhiteSpace(targetId) ? "Default" : targetId.Trim();
            SelectedShiftStackId = normalized;
            return string.Equals(SelectedShiftStackId, normalized, StringComparison.OrdinalIgnoreCase);
        }

        private bool CanDeleteSelectedShiftStack()
        {
            return IsProfileSelected && !string.Equals(SelectedShiftStackId, "Default", StringComparison.OrdinalIgnoreCase);
        }

        private void SaveShiftStackAs()
        {
            if (SelectedProfile == null) return;

            string requested = PromptForShiftStackName(SelectedShiftStackId + " Copy");
            if (string.IsNullOrWhiteSpace(requested))
            {
                return;
            }

            string uniqueName = BuildUniqueShiftStackName(requested.Trim());
            var destination = SelectedProfile.EnsureShiftStack(uniqueName);
            int count = Math.Min(ShiftAssistMaxStoredGears, Math.Min(destination.ShiftRPM.Length, destination.ShiftLocked.Length));
            for (int i = 0; i < count; i++)
            {
                destination.ShiftRPM[i] = _shiftStackEditRpm[i];
                destination.ShiftLocked[i] = _shiftStackEditLocked[i];
            }

            _isInternalShiftStackSelectionChange = true;
            try
            {
                SelectedShiftStackId = uniqueName;
            }
            finally
            {
                _isInternalShiftStackSelectionChange = false;
            }

            SetShiftStackDirty(false);
            _previousShiftStackId = uniqueName;
            SaveProfiles();
            OnPropertyChanged(nameof(ShiftStackIds));
            CommandManager.InvalidateRequerySuggested();
        }

        private void DeleteSelectedShiftStack()
        {
            if (SelectedProfile == null || !CanDeleteSelectedShiftStack())
            {
                return;
            }

            string deleteId = SelectedShiftStackId;
            if (!TryChangeSelectedShiftStack("Default"))
            {
                return;
            }

            if (string.Equals(SelectedShiftStackId, deleteId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SelectedProfile.ShiftAssistStacks?.Remove(deleteId);
            SaveProfiles();
            OnPropertyChanged(nameof(ShiftStackIds));
            CommandManager.InvalidateRequerySuggested();
        }

        private string BuildUniqueShiftStackName(string baseName)
        {
            string normalized = string.IsNullOrWhiteSpace(baseName) ? "Stack" : baseName.Trim();
            var existing = new HashSet<string>(ShiftStackIds, StringComparer.OrdinalIgnoreCase);
            if (!existing.Contains(normalized))
            {
                return normalized;
            }

            for (int i = 2; i < 1000; i++)
            {
                string candidate = normalized + " (" + i.ToString(CultureInfo.InvariantCulture) + ")";
                if (!existing.Contains(candidate))
                {
                    return candidate;
                }
            }

            return normalized + " " + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        private string PromptForShiftStackName(string defaultValue)
        {
            var input = new TextBox
            {
                Text = defaultValue ?? string.Empty,
                MinWidth = 260,
                Margin = new Thickness(0, 8, 0, 8)
            };

            var okButton = new Button { Content = "OK", MinWidth = 70, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancelButton = new Button { Content = "Cancel", MinWidth = 70, IsCancel = true };

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = "New gear stack name:" });
            panel.Children.Add(input);
            panel.Children.Add(buttonPanel);

            var dialog = new Window
            {
                Title = "Save Gear Stack As",
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Content = panel,
                Owner = Application.Current?.MainWindow
            };

            okButton.Click += (s, e) => dialog.DialogResult = true;

            bool? result = dialog.ShowDialog();
            if (result != true)
            {
                return null;
            }

            return input.Text;
        }

        public void RefreshShiftAssistTargetTextsFromStack(string stackId)
        {
            if (_shiftGearRows == null || !string.Equals(stackId, SelectedShiftStackId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (ShiftStackIsDirty)
            {
                OnPropertyChanged(nameof(ShiftGearRows));
                return;
            }

            LoadShiftStackBuffer(stackId);
        }

        private void SaveSelectedShiftStack()
        {
            if (SelectedProfile == null || string.IsNullOrWhiteSpace(SelectedShiftStackId))
            {
                return;
            }

            CommitShiftStackBufferToStored(SelectedShiftStackId);
        }

        private void RevertSelectedShiftStack()
        {
            if (SelectedProfile == null)
            {
                return;
            }

            LoadShiftStackBuffer(SelectedShiftStackId);
        }

        public void NotifyShiftStackOptionsChanged()
        {
            OnPropertyChanged(nameof(ShiftStackIds));
        }

        private void BrowseShiftCustomWav()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "WAV files (*.wav)|*.wav|All files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                ShiftAssistCustomWavPath = dialog.FileName;
            }
        }

        private void UseEmbeddedDefaultSound()
        {
            ShiftAssistUseCustomWav = false;
            ShiftAssistCustomWavPath = string.Empty;
        }

        private void LearnBaseTankFromLive()
        {
            if (SelectedProfile == null) return;

            double rawMaxFuel = Convert.ToDouble(_pluginManager?.GetPropertyValue("DataCorePlugin.GameData.MaxFuel") ?? 0.0);
            if (double.IsNaN(rawMaxFuel) || double.IsInfinity(rawMaxFuel) || rawMaxFuel <= 0.0)
            {
                SimHub.Logging.Current.Debug("[LalaPlugin:Profiles] Learn Base Tank skipped: invalid MaxFuel from live data.");
                return;
            }

            double learnedValue = Math.Round(rawMaxFuel, 1);
            SelectedProfile.BaseTankLitres = learnedValue;
            SimHub.Logging.Current.Debug($"[LalaPlugin:Profiles] Learned Base Tank for '{SelectedProfile.ProfileName}': {learnedValue:F1} L");
        }

        private void DeleteProfile()
        {
            if (SelectedProfile?.ProfileName == "Default Settings") return; // ADD THIS LINE

            if (SelectedProfile == null) return;

            if (MessageBox.Show($"Are you sure you want to delete '{SelectedProfile.ProfileName}'?", "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                CarProfiles.Remove(SelectedProfile);
                SelectedProfile = null;
                SaveProfiles();
            }
        }
        private void DeleteTrack()
        {
            if (SelectedProfile == null || SelectedTrack == null) return;

            if (MessageBox.Show($"Are you sure you want to delete the data for '{SelectedTrack.DisplayName}' from the '{SelectedProfile.ProfileName}' profile?",
                "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                // Find the key in the source dictionary to remove it
                var keyToRemove = SelectedProfile.TrackStats.FirstOrDefault(kvp => kvp.Value == SelectedTrack).Key;

                if (!string.IsNullOrEmpty(keyToRemove))
                {
                    SelectedProfile.TrackStats.Remove(keyToRemove);
                    TracksForSelectedProfile.Remove(SelectedTrack); // Remove from the UI list
                    SelectedTrack = null;
                    SaveProfiles(); // Persist the change to the JSON file
                }
            }
        }
        private void ApplySelectedProfileToLive()
        {
            if (SelectedProfile != null)
            {
                _applyProfileToLiveAction(SelectedProfile);
            }
        }

        private (double decel, double buffer) GetPitEntryDefaultsForCar(string carName)
        {
            if (!string.IsNullOrWhiteSpace(carName))
            {
                var name = carName;
                if (name.IndexOf("GTP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("963", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("LMDh", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Hypercar", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return (15.0, 20.0);
                }

                if (name.IndexOf("GT3", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return (14.0, 10.0);
                }
            }

            return (13.5, 15.0);
        }

        private bool EnsurePitEntryDefaults(CarProfile profile, string carName)
        {
            if (profile == null) return false;

            bool changed = false;
            var defaults = GetPitEntryDefaultsForCar(carName);

            if (profile.PitEntryDecelMps2 <= 0)
            {
                profile.PitEntryDecelMps2 = defaults.decel;
                changed = true;
            }

            if (profile.PitEntryBufferM <= 0)
            {
                profile.PitEntryBufferM = defaults.buffer;
                changed = true;
            }

            return changed;
        }

        public void LoadProfiles()
        {
            try
            {
                PluginStorage.TryMigrate(_legacyProfilesFilePath, _profilesFilePath);
                if (File.Exists(_profilesFilePath))
                {
                    string json = File.ReadAllText(_profilesFilePath);
                    ObservableCollection<CarProfile> loadedProfiles = null;
                    int? schemaVersion = null;
                    try
                    {
                        var store = Newtonsoft.Json.JsonConvert.DeserializeObject<CarProfilesStore>(json);
                        loadedProfiles = store?.Profiles;
                        schemaVersion = store?.SchemaVersion;
                    }
                    catch
                    {
                        loadedProfiles = null;
                    }

                    if (loadedProfiles == null)
                    {
                        loadedProfiles = Newtonsoft.Json.JsonConvert.DeserializeObject<ObservableCollection<CarProfile>>(json);
                    }
                    if (loadedProfiles != null)
                    {
                        foreach (var profile in loadedProfiles)
                        {
                            if (profile.ShiftAssistStacks == null)
                            {
                                profile.ShiftAssistStacks = new Dictionary<string, ShiftStackData>(StringComparer.OrdinalIgnoreCase);
                            }
                            foreach (var stack in profile.ShiftAssistStacks.Values)
                            {
                                stack?.EnsureValidShape();
                            }

                            if (profile.BaseTankLitres.HasValue)
                            {
                                var value = profile.BaseTankLitres.Value;
                                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                                {
                                    profile.BaseTankLitres = null;
                                }
                            }
                        }
                        // Clear the existing collection and add the loaded items
                        // This ensures the SortedCarProfiles view is updated correctly.
                        CarProfiles.Clear();
                        foreach (var profile in loadedProfiles)
                        {
                            CarProfiles.Add(profile);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"[LalaPlugin:Profile] Failed to load car profiles: {ex.Message}");
            }
            
            // After attempting to load, check if a "Default Settings" profile exists.
            // If not, create one from scratch. This makes the plugin self-healing.
            bool createdDefault = false;
            if (!CarProfiles.Any(p => p.ProfileName.Equals("Default Settings", StringComparison.OrdinalIgnoreCase)))
            {
                SimHub.Logging.Current.Info("[LalaPlugin:Profiles] Default Settings profile not found, creating baseline profile.");

                // Create the foundational default profile with all properties explicitly set.
                var defaultProfile = new CarProfile
                {
                    ProfileName = "Default Settings",

                    // Launch Control Properties
                    TargetLaunchRPM = 6000,
                    OptimalRPMTolerance = 1000,
                    TargetLaunchThrottle = 80.0,
                    OptimalThrottleTolerance = 5.0,
                    TargetBitePoint = 45.0,
                    BitePointTolerance = 3.0,
                    BogDownFactorPercent = 55.0,
                    AntiStallThreshold = 10.0,

                    // Fuel & Pit Properties
                    FuelContingencyValue = 1.5,
                    IsContingencyInLaps = true,
                    WetFuelMultiplier = 90.0,
                    TireChangeTime = 22,
                    RacePaceDeltaSeconds = 1.2,
                    RefuelRate = 3.7,
                    BaseTankLitres = null,
                    PitEntryDecelMps2 = 13.5,
                    PitEntryBufferM = 15.0,

                    // Dash Display Properties
                    RejoinWarningLingerTime = 10.0,
                    RejoinWarningMinSpeed = 50.0,
                    SpinYawRateThreshold = 15.0,
                    TrafficApproachWarnSeconds = 5.0
                };

                // Create a default track entry with all properties explicitly set.
                var defaultTrack = new TrackStats
                {
                    DisplayName = "Default",
                    Key = "default",
                    BestLapMsDry = null,
                    BestLapMsWet = null,
                    PitLaneLossSeconds = 25.0,
                    AvgFuelPerLapDry = 2.8,
                    DryFuelSampleCount = 0,
                    AvgLapTimeDry = 120000, // 2 minutes
                    DryLapTimeSampleCount = 0,
                    AvgDryTrackTemp = null,
                    AvgFuelPerLapWet = 3.1, // A slightly higher default for wet
                    WetFuelSampleCount = 0,
                    AvgLapTimeWet = 135000, // A slightly higher default for wet
                    WetLapTimeSampleCount = 0,
                    AvgWetTrackTemp = null
                };
                defaultProfile.TrackStats.Add(defaultTrack.Key, defaultTrack);

                // Add the newly created profile to our collection.
                CarProfiles.Add(defaultProfile);
                createdDefault = true;
            }

            // Seed any missing pit entry defaults on load for backward compatibility.
            bool seededAny = false;
            foreach (var profile in CarProfiles)
            {
                seededAny |= EnsurePitEntryDefaults(profile, profile.ProfileName);
            }

            if (seededAny || createdDefault)
            {
                SaveProfiles();
            }
        }

        public void SaveProfiles()
        {
            try
            {
                // First, save all profiles to the file as before.
                var store = new CarProfilesStore
                {
                    SchemaVersion = 2,
                    Profiles = CarProfiles
                };
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(store, Newtonsoft.Json.Formatting.Indented);
                var folder = Path.GetDirectoryName(_profilesFilePath);
                if (!string.IsNullOrWhiteSpace(folder))
                    Directory.CreateDirectory(folder);
                File.WriteAllText(_profilesFilePath, json);
                SimHub.Logging.Current.Debug("[LalaPlugin:Profiles] Profiles saved to JSON.");
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"[LalaPlugin:Profiles] Save failed: {ex.Message}");
            }
        }

        private static string FormatPercentText(object raw)
        {
            try
            {
                double value;
                if (raw is double d) value = d;
                else if (raw is float f) value = f;
                else if (raw is decimal m) value = (double)m;
                else if (raw is int || raw is long || raw is short || raw is byte || raw is sbyte || raw is uint || raw is ulong || raw is ushort)
                    value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                else if (raw is string s)
                {
                    if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                        !double.TryParse(s, out value))
                    {
                        return "n/a";
                    }
                }
                else
                {
                    return "n/a";
                }

                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    return "n/a";
                }

                return value.ToString("0.0000", CultureInfo.InvariantCulture);
            }
            catch
            {
                return "n/a";
            }
        }

        private static string FormatTimestampText(object raw)
        {
            try
            {
                DateTime timestamp;
                if (raw is DateTime dt)
                {
                    timestamp = dt;
                }
                else if (raw is string s)
                {
                    if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out timestamp) &&
                        !DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out timestamp))
                    {
                        return "n/a";
                    }
                }
                else
                {
                    timestamp = Convert.ToDateTime(raw, CultureInfo.InvariantCulture);
                }

                if (timestamp == DateTime.MinValue)
                {
                    return "n/a";
                }

                return timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
            }
            catch
            {
                return "n/a";
            }
        }

        private static bool SafeToBoolean(object raw)
        {
            try
            {
                if (raw is bool b) return b;
                if (raw is string s)
                {
                    if (bool.TryParse(s, out var parsed)) return parsed;
                    if (string.Equals(s, "1", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(s, "0", StringComparison.OrdinalIgnoreCase)) return false;
                }

                return Convert.ToBoolean(raw, CultureInfo.InvariantCulture);
            }
            catch
            {
                return false;
            }
        }

        public void RefreshTrackMarkersSnapshotForSelectedTrack()
        {
            try
            {
                string key = SelectedTrack?.Key ?? SelectedTrack?.DisplayName;
                TrackMarkersSnapshot snapshot = default;
                if (_getTrackMarkersSnapshotForKey != null && !string.IsNullOrWhiteSpace(key))
                {
                    snapshot = _getTrackMarkersSnapshotForKey(key);
                }

                void Apply()
                {
                    _suppressTrackMarkersLockAction = true;
                    try
                    {
                        object entry = snapshot.HasData ? (object)snapshot.EntryPct : null;
                        object exitPct = snapshot.HasData ? (object)snapshot.ExitPct : null;
                        object updatedUtc = snapshot.HasData ? (object)(snapshot.LastUpdatedUtc ?? DateTime.MinValue) : null;

                        StoredPitEntryPctText = FormatPercentText(entry);
                        StoredPitExitPctText = FormatPercentText(exitPct);
                        TrackMarkersLastUpdatedText = FormatTimestampText(updatedUtc);
                        TrackMarkersLocked = snapshot.HasData ? snapshot.Locked : false;
                    }
                    finally
                    {
                        _suppressTrackMarkersLockAction = false;
                    }
                }

                var disp = System.Windows.Application.Current?.Dispatcher;
                if (disp == null || disp.CheckAccess()) Apply(); else disp.Invoke((Action)Apply);
            }
            catch
            {
                // Never allow snapshot refresh to throw
            }
        }

        private void ReloadTrackMarkers()
        {
            try
            {
                _reloadTrackMarkersFromDisk?.Invoke();
                RefreshTrackMarkersSnapshotForSelectedTrack();
            }
            catch
            {
                // Suppress any UI-facing errors during reload
            }
        }

        private void RelearnPitData()
        {
            var track = SelectedTrack;
            if (track == null) return;

            string car = SelectedProfile?.ProfileName ?? "unknown";
            string trackName = track.DisplayName ?? track.Key ?? "unknown";
            SimHub.Logging.Current.Info($"[LalaPlugin:Profiles] Relearn Pit Data for {car} @ {trackName}");

            track.RelearnPitLoss();
            SaveProfiles();

            string key = track.Key ?? track.DisplayName;
            if (!string.IsNullOrWhiteSpace(key))
            {
                _resetTrackMarkersForKey?.Invoke(key);
            }

            RefreshTrackMarkersSnapshotForSelectedTrack();
        }

        private void RelearnDryConditions()
        {
            var track = SelectedTrack;
            if (track == null) return;

            string car = SelectedProfile?.ProfileName ?? "unknown";
            string trackName = track.DisplayName ?? track.Key ?? "unknown";
            SimHub.Logging.Current.Info($"[LalaPlugin:Profiles] Relearn Dry Conditions for {car} @ {trackName}");

            track.RelearnDryConditions();
            track.DryConditionsLocked = false;
            SaveProfiles();
        }

        private void RelearnWetConditions()
        {
            var track = SelectedTrack;
            if (track == null) return;

            string car = SelectedProfile?.ProfileName ?? "unknown";
            string trackName = track.DisplayName ?? track.Key ?? "unknown";
            SimHub.Logging.Current.Info($"[LalaPlugin:Profiles] Relearn Wet Conditions for {car} @ {trackName}");

            track.RelearnWetConditions();
            track.WetConditionsLocked = false;
            SaveProfiles();
        }
    }

    public class ShiftGearRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private string _gearLabel;
        public string GearLabel
        {
            get => _gearLabel;
            set
            {
                if (_gearLabel == value) return;
                _gearLabel = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GearLabel)));
            }
        }

        private string _rpmText;
        public string RpmText
        {
            get => _rpmText;
            set
            {
                if (_rpmText == value) return;
                _rpmText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RpmText)));
            }
        }

        public Action<string> SaveAction { get; set; }

        private string _learnedRpmText;
        public string LearnedRpmText
        {
            get => _learnedRpmText;
            set
            {
                if (_learnedRpmText == value) return;
                _learnedRpmText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LearnedRpmText)));
            }
        }

        private string _sampleCountText;
        public string SampleCountText
        {
            get => _sampleCountText;
            set
            {
                if (_sampleCountText == value) return;
                _sampleCountText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SampleCountText)));
            }
        }

        private string _delayAvgMsText;
        public string DelayAvgMsText
        {
            get => _delayAvgMsText;
            set
            {
                if (_delayAvgMsText == value) return;
                _delayAvgMsText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DelayAvgMsText)));
            }
        }

        private string _delayCountText;
        public string DelayCountText
        {
            get => _delayCountText;
            set
            {
                if (_delayCountText == value) return;
                _delayCountText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DelayCountText)));
            }
        }

        private bool _isLocked;
        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                if (_isLocked == value) return;
                _isLocked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLocked)));
            }
        }

        public Action<bool> SetLockAction { get; set; }

        public void UpdateRuntimeStats(string learnedRpmText, string sampleCountText, string delayAvgMsText, string delayCountText)
        {
            LearnedRpmText = learnedRpmText;
            SampleCountText = sampleCountText;
            DelayAvgMsText = delayAvgMsText;
            DelayCountText = delayCountText;
        }
    }
}
