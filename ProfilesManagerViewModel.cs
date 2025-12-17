// In file: ProfilesManagerViewModel.cs

using SimHub.Plugins; // Required for this.GetPluginManager()
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace LaunchPlugin
{
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
        private readonly string _profilesFilePath;
        // --- PB constants ---
        private const int PB_MIN_MS = 30000;     // >= 30s
        private const int PB_MAX_MS = 1200000;   // <= 20min
        private const int PB_IMPROVE_MS = 50;    // >= 0.05s faster

        /// <summary>
        /// Try to update personal best for the given car+track. Returns true if updated.
        /// </summary>
        public bool TryUpdatePB(string carName, string trackKey, int lapMs)
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

            bool improved = !ts.BestLapMs.HasValue || lapMs <= ts.BestLapMs.Value - PB_IMPROVE_MS;
            if (!improved)
            {
                SimHub.Logging.Current.Debug($"[LalaPlugin:Pace] Reject PB: not improved enough. old={ts.BestLapMs} new={lapMs} (≥{PB_IMPROVE_MS} ms required).");
                return false;
            }

            ts.BestLapMs = lapMs;
            ts.BestLapMsText = ts.MillisecondsToLapTimeString(ts.BestLapMs);
            SaveProfiles();

            // If Profiles tab is on this car, refresh so the new PB shows immediately
            var disp = System.Windows.Application.Current?.Dispatcher;
            void DoUi() { if (SelectedProfile == car) RefreshTracksForSelectedProfile(); }
            if (disp == null || disp.CheckAccess()) DoUi(); else disp.BeginInvoke((Action)DoUi);

            SimHub.Logging.Current.Info($"[LalaPlugin:Pace] PB Updated: {carName} @ '{ts.DisplayName}' -> {ts.BestLapMsText}");
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
                }
            }
        }

        public CarProfile EnsureCar(string carProfileName)
        {
            var car = GetProfileForCar(carProfileName);
            if (car != null)
            {
                SimHub.Logging.Current.Debug($"[LalaPlugin:Profile] EnsureCar('{carProfileName}') -> FOUND existing profile.");
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
                    car.DryConditionMultipliers = defaultProfile.DryConditionMultipliers?.Clone() ?? ConditionMultipliers.CreateDefaultDry();
                    car.WetConditionMultipliers = defaultProfile.WetConditionMultipliers?.Clone() ?? ConditionMultipliers.CreateDefaultWet();
                    car.RejoinWarningLingerTime = defaultProfile.RejoinWarningLingerTime;
                    car.RejoinWarningMinSpeed = defaultProfile.RejoinWarningMinSpeed;
                    car.SpinYawRateThreshold = defaultProfile.SpinYawRateThreshold;
                    car.TrafficApproachWarnSeconds = defaultProfile.TrafficApproachWarnSeconds;
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
            ts.BestLapMsText = ts.MillisecondsToLapTimeString(ts.BestLapMs);
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
                if (_selectedTrack != value)
                {
                    _selectedTrack = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsTrackSelected));
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
            }

            if (disp == null || disp.CheckAccess()) DoRefresh();
            else disp.Invoke(DoRefresh);
        }


        public bool IsProfileSelected => SelectedProfile != null;
        public bool IsTrackSelected => SelectedTrack != null;

        // --- Commands for UI Buttons ---
        public RelayCommand NewProfileCommand { get; }
        public RelayCommand CopySettingsCommand { get; }
        public RelayCommand DeleteProfileCommand { get; }
        public RelayCommand SaveChangesCommand { get; }
        public RelayCommand ApplyToLiveCommand { get; }
        public RelayCommand DeleteTrackCommand { get; }


        public ProfilesManagerViewModel(PluginManager pluginManager, Action<CarProfile> applyProfileToLiveAction, Func<string> getCurrentCarModel, Func<string> getCurrentTrackName)
        {
            _pluginManager = pluginManager;
            _applyProfileToLiveAction = applyProfileToLiveAction;
            _getCurrentCarModel = getCurrentCarModel;
            _getCurrentTrackName = getCurrentTrackName;
            CarProfiles = new ObservableCollection<CarProfile>();

            // Define the path for the JSON file in SimHub's common storage folder
            string commonDataFolder = _pluginManager.GetCommonStoragePath();
            Directory.CreateDirectory(commonDataFolder); // Ensure it exists
            _profilesFilePath = Path.Combine(commonDataFolder, "LalaLaunch_CarProfiles.json");

            // Initialize Commands
            NewProfileCommand = new RelayCommand(p => NewProfile());
            CopySettingsCommand = new RelayCommand(p => CopySettings(), p => IsProfileSelected);
            DeleteProfileCommand = new RelayCommand(p => DeleteProfile(), p => IsProfileSelected && SelectedProfile.ProfileName != "Default Settings");
            SaveChangesCommand = new RelayCommand(p => SaveProfiles());
            ApplyToLiveCommand = new RelayCommand(p => ApplySelectedProfileToLive(), p => IsProfileSelected);
            DeleteTrackCommand = new RelayCommand(p => DeleteTrack(), p => SelectedTrack != null);
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
                newProfile.RejoinWarningLingerTime = defaultProfile.RejoinWarningLingerTime;
                newProfile.RejoinWarningMinSpeed = defaultProfile.RejoinWarningMinSpeed;
                newProfile.SpinYawRateThreshold = defaultProfile.SpinYawRateThreshold;
                newProfile.TrafficApproachWarnSeconds = defaultProfile.TrafficApproachWarnSeconds;
                newProfile.RacePaceDeltaSeconds = defaultProfile.RacePaceDeltaSeconds;
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

        public void LoadProfiles()
        {
            try
            {
                if (File.Exists(_profilesFilePath))
                {
                    string json = File.ReadAllText(_profilesFilePath);
                    var loadedProfiles = Newtonsoft.Json.JsonConvert.DeserializeObject<ObservableCollection<CarProfile>>(json);
                    if (loadedProfiles != null)
                    {
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
                    BestLapMs = null,
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

                // Add the newly created profile to our collection and save it to disk.
                CarProfiles.Add(defaultProfile);
                SaveProfiles();
            }
        }

        public void SaveProfiles()
        {
            try
            {
                // First, save all profiles to the file as before.
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(CarProfiles, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(_profilesFilePath, json);
                SimHub.Logging.Current.Debug("[LalaPlugin:Profiles] Profiles saved to JSON.");
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error($"[LalaPlugin:Profiles] Save failed: {ex.Message}");
            }
        }
    }
}