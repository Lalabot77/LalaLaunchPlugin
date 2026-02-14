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
        private readonly Func<bool> _getShiftAssistEnabled;
        private readonly Action<bool> _setShiftAssistEnabled;
        private readonly Func<int> _getShiftAssistBeepDurationMs;
        private readonly Action<int> _setShiftAssistBeepDurationMs;
        private readonly Func<int> _getShiftAssistLeadTimeMs;
        private readonly Action<int> _setShiftAssistLeadTimeMs;
        private readonly Func<bool> _getShiftAssistUseCustomWav;
        private readonly Action<bool> _setShiftAssistUseCustomWav;
        private readonly Func<string> _getShiftAssistCustomWavPath;
        private readonly Action<string> _setShiftAssistCustomWavPath;
        private readonly Action _playShiftAssistTestBeep;
        private const int ShiftAssistMaxStoredGears = 8;
        private readonly string _profilesFilePath;
        private readonly string _legacyProfilesFilePath;
        private bool _suppressTrackMarkersLockAction;
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
                    OnPropertyChanged(nameof(ShiftStackIds));
                    OnPropertyChanged(nameof(ShiftGearRows));
                    OnPropertyChanged(nameof(ShiftAssistMaxTargetGears));
                    OnPropertyChanged(nameof(ShiftAssistCurrentGearRedlineHint));
                    OnPropertyChanged(nameof(ActiveShiftStackLabel));
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

        public int ShiftAssistLeadTimeMs
        {
            get => _getShiftAssistLeadTimeMs?.Invoke() ?? 200;
            set
            {
                int clamped = value;
                if (clamped < 0) clamped = 0;
                if (clamped > 500) clamped = 500;
                _setShiftAssistLeadTimeMs?.Invoke(clamped);
                OnPropertyChanged();
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

        private string _selectedShiftStackId = "Default";
        public string SelectedShiftStackId
        {
            get => string.IsNullOrWhiteSpace(_selectedShiftStackId) ? "Default" : _selectedShiftStackId;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? "Default" : value.Trim();
                if (string.Equals(_selectedShiftStackId, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _selectedShiftStackId = normalized;
                EnsureShiftStackForSelectedProfile(normalized);
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveShiftStackLabel));
                OnPropertyChanged(nameof(ShiftStackIds));
                OnPropertyChanged(nameof(ShiftGearRows));
                OnPropertyChanged(nameof(ShiftAssistMaxTargetGears));
                OnPropertyChanged(nameof(ShiftAssistCurrentGearRedlineHint));
            }
        }

        public string ActiveShiftStackLabel => $"Active stack: {SelectedShiftStackId}";

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
                int maxGears = TryReadPluginInt("DataCorePlugin.GameData.CarSettings_MaxGears");
                if (maxGears <= 0)
                {
                    maxGears = TryReadPluginInt("DataCorePlugin.GameRawData.SessionData.DriverInfo.DriverCarGearNumForward");
                }

                if (maxGears <= 0)
                {
                    maxGears = ShiftAssistMaxStoredGears;
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
                var stack = EnsureShiftStackForSelectedProfile(SelectedShiftStackId);
                int targetRows = ShiftAssistMaxTargetGears;
                for (int i = 0; i < targetRows; i++)
                {
                    int gearIdx = i;
                    int rowGear = gearIdx + 1;
                    int avgDelayMs = TryReadPluginInt($"ShiftAssist.DelayAvg_G{rowGear}");
                    int delaySamples = TryReadPluginInt($"ShiftAssist.DelayN_G{rowGear}");
                    yield return new ShiftGearRow
                    {
                        GearLabel = $"Shift from Gear {rowGear}",
                        RpmText = stack.ShiftRPM[gearIdx] > 0 ? stack.ShiftRPM[gearIdx].ToString(CultureInfo.InvariantCulture) : string.Empty,
                        DelaySummary = delaySamples > 0 && avgDelayMs > 0
                            ? string.Format(CultureInfo.InvariantCulture, "Avg delay: {0} ms (n={1})", avgDelayMs, delaySamples)
                            : "—",
                        SaveAction = txt =>
                        {
                            int value;
                            if (!int.TryParse((txt ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 0)
                            {
                                value = 0;
                            }

                            stack.ShiftRPM[gearIdx] = value;
                            SaveProfiles();
                            OnPropertyChanged(nameof(ShiftGearRows));
                            OnPropertyChanged(nameof(ShiftAssistCurrentGearRedlineHint));
                        }
                    };
                }
            }
        }

        public void RefreshShiftAssistRuntimeStats()
        {
            OnPropertyChanged(nameof(ShiftGearRows));
        }

        private int TryReadPluginInt(string propertyName)
        {
            if (_pluginManager == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return 0;
            }

            try
            {
                object raw = _pluginManager.GetPropertyValue(propertyName);
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
        public RelayCommand ShiftCopyFromStackCommand { get; }
        public RelayCommand ShiftBrowseCustomWavCommand { get; }
        public RelayCommand ShiftUseEmbeddedDefaultCommand { get; }
        public RelayCommand ShiftTestBeepCommand { get; }

        private string _shiftCopySourceStackId = "Default";
        public string ShiftCopySourceStackId
        {
            get => _shiftCopySourceStackId;
            set
            {
                _shiftCopySourceStackId = string.IsNullOrWhiteSpace(value) ? "Default" : value.Trim();
                OnPropertyChanged();
            }
        }


        public ProfilesManagerViewModel(PluginManager pluginManager, Action<CarProfile> applyProfileToLiveAction, Func<string> getCurrentCarModel, Func<string> getCurrentTrackName, Func<string, TrackMarkersSnapshot> getTrackMarkersSnapshotForKey, Action<string, bool> setTrackMarkersLockForKey, Action reloadTrackMarkersFromDisk, Action<string> resetTrackMarkersForKey, Func<string> getCurrentGearStackId, Func<bool> getShiftAssistEnabled, Action<bool> setShiftAssistEnabled, Func<int> getShiftAssistBeepDurationMs, Action<int> setShiftAssistBeepDurationMs, Func<int> getShiftAssistLeadTimeMs, Action<int> setShiftAssistLeadTimeMs, Func<bool> getShiftAssistUseCustomWav, Action<bool> setShiftAssistUseCustomWav, Func<string> getShiftAssistCustomWavPath, Action<string> setShiftAssistCustomWavPath, Action playShiftAssistTestBeep)
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
            _getShiftAssistEnabled = getShiftAssistEnabled;
            _setShiftAssistEnabled = setShiftAssistEnabled;
            _getShiftAssistBeepDurationMs = getShiftAssistBeepDurationMs;
            _setShiftAssistBeepDurationMs = setShiftAssistBeepDurationMs;
            _getShiftAssistLeadTimeMs = getShiftAssistLeadTimeMs;
            _setShiftAssistLeadTimeMs = setShiftAssistLeadTimeMs;
            _getShiftAssistUseCustomWav = getShiftAssistUseCustomWav;
            _setShiftAssistUseCustomWav = setShiftAssistUseCustomWav;
            _getShiftAssistCustomWavPath = getShiftAssistCustomWavPath;
            _setShiftAssistCustomWavPath = setShiftAssistCustomWavPath;
            _playShiftAssistTestBeep = playShiftAssistTestBeep;
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
            ShiftCopyFromStackCommand = new RelayCommand(p => CopyShiftStackFromSource(), p => IsProfileSelected);
            ShiftBrowseCustomWavCommand = new RelayCommand(p => BrowseShiftCustomWav());
            ShiftUseEmbeddedDefaultCommand = new RelayCommand(p => UseEmbeddedDefaultSound());
            ShiftTestBeepCommand = new RelayCommand(p => _playShiftAssistTestBeep?.Invoke());
            SortedCarProfiles = CollectionViewSource.GetDefaultView(CarProfiles);
            SortedCarProfiles.SortDescriptions.Add(new SortDescription(nameof(CarProfile.ProfileName), ListSortDirection.Ascending));
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
                var copied = new ShiftStackData();
                var src = kvp.Value ?? new ShiftStackData();
                src.EnsureValidShape();
                for (int i = 0; i < 8; i++) copied.ShiftRPM[i] = src.ShiftRPM[i];
                destination.ShiftAssistStacks[kvp.Key] = copied;
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

            CloneShiftStacks(source, destination);
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
        }

        private void AddCurrentShiftStack()
        {
            if (SelectedProfile == null) return;
            string live = _getCurrentGearStackId?.Invoke();
            string stack = string.IsNullOrWhiteSpace(live) ? "Default" : live.Trim();
            EnsureShiftStackForSelectedProfile(stack);
            SelectedShiftStackId = stack;
            SaveProfiles();
        }

        private void CopyShiftStackFromSource()
        {
            if (SelectedProfile == null) return;
            var source = EnsureShiftStackForSelectedProfile(ShiftCopySourceStackId);
            var destination = EnsureShiftStackForSelectedProfile(SelectedShiftStackId);
            for (int i = 0; i < 8; i++)
            {
                destination.ShiftRPM[i] = source.ShiftRPM[i];
            }

            SaveProfiles();
            OnPropertyChanged(nameof(ShiftGearRows));
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

    public class ShiftGearRow
    {
        public string GearLabel { get; set; }
        public string RpmText { get; set; }
        public Action<string> SaveAction { get; set; }
        public string DelaySummary { get; set; }
    }
}
