# Fuel Tab – Current Wiring (code-level overview)

## Fuel Tab – Control Inventory (UI)
| Control (x:Name/Content) | Type | User label / purpose | Binding / command | Units / meaning | Notes on data source & toggles |
| --- | --- | --- | --- | --- | --- |
| Car Profile combo | ComboBox | "Car Profile:" selector | `SelectedCarProfile` (TwoWay), `AvailableCarProfiles` | profile selection | Drives available tracks; uses profile object instances. 【F:FuelCalculatorView.xaml†L202-L215】 |
| Track combo | ComboBox | "Track:" selector | `SelectedTrackStats` (TwoWay), `AvailableTrackStats` | track/layout choice | Updates selected `TrackStats` and triggers profile load. 【F:FuelCalculatorView.xaml†L209-L215】 |
| Track condition radios | RadioButton | "Track Condition:" Dry / Wet | `IsDry`, `IsWet` | condition mode | Switching recalculates lap/fuel sources and summaries; wet shows wet-factor slider. 【F:FuelCalculatorView.xaml†L219-L225】【F:FuelCalcs.cs†L1034-L1080】 |
| Wet factor slider | ui:TitledSlider | "Wet Factor (%):" | `WetFactorPercent` | percentage multiplier | Visible only when wet; applies multiplier to fuel. 【F:FuelCalculatorView.xaml†L219-L225】【F:FuelCalcs.cs†L116-L119】【F:FuelCalcs.cs†L2221-L2236】 |
| Estimated lap time textbox | TextBox | "Est Avg Lap Time:" | `EstimatedLapTime` (TwoWay, `LapTimeValidationRule`) | m:ss(.fff) | Manual edits mark source `manual`; recalculates strategy. 【F:FuelCalculatorView.xaml†L241-L253】【F:FuelCalcs.cs†L722-L733】 |
| PB button | Button | "USE PB" | `PersonalBestButton_Click` → `LoadPersonalBestAsRacePace()` | lap time basis | Enabled when PB available; uses PB + delta. 【F:FuelCalculatorView.xaml†L254-L258】【F:FuelCalculatorView.xaml.cs†L18-L22】【F:FuelCalcs.cs†L2346-L2356】 |
| Live lap button | Button | "LIVE" | `UseLiveLapPaceCommand` | lap time basis | Uses live average pace if available. 【F:FuelCalculatorView.xaml†L254-L258】【F:FuelCalcs.cs†L1724-L1726】【F:FuelCalcs.cs†L1772-L1784】 |
| Profile lap button | Button | "PROFILE" | `LoadProfileLapTimeCommand` | lap time basis | Loads profile lap time for current track/condition. 【F:FuelCalculatorView.xaml†L254-L259】【F:FuelCalcs.cs†L1725-L1728】【F:FuelCalcs.cs†L1230-L1247】 |
| Leader delta slider | Slider + readout | "Your Pace vs Leader (s):" | `LeaderDeltaSeconds` (TwoWay) | seconds | Manual slider stored separately; effective delta prefers manual over live telemetry. 【F:FuelCalculatorView.xaml†L263-L301】【F:FuelCalcs.cs†L737-L789】 |
| Race pace vs PB slider | Slider + readout | "Race Pace vs PB (s):" | `RacePaceDeltaOverride` (TwoWay) | seconds | Enabled when lap source is PB; adjusts estimated pace. 【F:FuelCalculatorView.xaml†L304-L338】【F:FuelCalcs.cs†L278-L292】【F:FuelCalcs.cs†L2346-L2356】 |
| Max fuel override slider | ui:TitledSlider | "Max Fuel Override (litres):" | `MaxFuelOverride` (TwoWay) | liters total | Highlighted if above detected max; optional apply-live checkbox. 【F:FuelCalculatorView.xaml†L343-L373】【F:FuelCalcs.cs†L1091-L1110】【F:FuelCalcs.cs†L2330-L2343】 |
| Apply live max fuel checkbox | CheckBox | "Apply live suggestion" | `ApplyLiveMaxFuelSuggestion` (TwoWay) | n/a | Enabled when a live tank suggestion exists. 【F:FuelCalculatorView.xaml†L368-L373】【F:FuelCalcs.cs†L329-L353】 |
| Fuel per lap textbox | TextBox | "Fuel per Lap (litres):" | `FuelPerLapText` (TwoWay) | L/lap | Manual edits sync `FuelPerLap`; source info updated. 【F:FuelCalculatorView.xaml†L375-L399】【F:FuelCalcs.cs†L804-L825】 |
| Fuel per lap buttons | Buttons | "MAX" / "LIVE" / "PROFILE" | `UseMaxFuelPerLapCommand`, `UseLiveFuelPerLapCommand`, `UseProfileFuelPerLapCommand` | L/lap | MAX uses session max; LIVE uses live avg; PROFILE uses stored avg. 【F:FuelCalculatorView.xaml†L392-L398】【F:FuelCalcs.cs†L455-L459】【F:FuelCalcs.cs†L1393-L1405】【F:FuelCalcs.cs†L860-L919】【F:FuelCalcs.cs†L2221-L2236】 |
| Fuel per lap apply live checkbox | CheckBox | "Apply live suggestion" | `ApplyLiveFuelSuggestion` (TwoWay) | n/a | Auto-copies live fuel/lap when enabled and available. 【F:FuelCalculatorView.xaml†L399-L404】【F:FuelCalcs.cs†L323-L335】【F:FuelCalcs.cs†L865-L888】 |
| Race preset combo | ComboBox | "Race Preset:" | `SelectedPreset` (TwoWay), `AvailablePresets` | preset selection | Marks modified flag; commands apply/clear in VM. 【F:FuelCalculatorView.xaml†L410-L439】【F:FuelCalcs.cs†L419-L452】【F:FuelCalcs.cs†L1701-L1712】 |
| Race type radios | RadioButton | "Race Type:" Lap-Limited / Time-Limited | `IsLapLimitedRace`, `IsTimeLimitedRace` | mode | Toggles lap vs minute sliders visibility. 【F:FuelCalculatorView.xaml†L444-L452】【F:FuelCalcs.cs†L942-L1021】 |
| Race laps slider | ui:TitledSlider | "Race Laps:" | `RaceLaps` (TwoWay) | laps | Visible for lap-limited races. 【F:FuelCalculatorView.xaml†L450-L451】 |
| Race minutes slider | ui:TitledSlider | "Race Minutes:" | `RaceMinutes` (TwoWay) | minutes | Visible for time-limited races. 【F:FuelCalculatorView.xaml†L450-L452】 |
| Formation lap fuel slider | ui:TitledSlider | "Formation Lap Fuel (liters):" | `FormationLapFuelLiters` (TwoWay) | liters | Baseline fuel burn before start. 【F:FuelCalculatorView.xaml†L453-L454】【F:FuelCalcs.cs†L48-L52】【F:FuelCalcs.cs†L2310-L2318】 |
| Contingency type radios & sliders | RadioButton + ui:TitledSlider | "Contingency Type" with "Extra Laps"/"Extra Litres", sliders for value | `IsContingencyInLaps` / `IsContingencyLitres`, `ContingencyValue` | laps or liters | Mode switches slider visibility; used in fuel calc. 【F:FuelCalculatorView.xaml†L455-L463】【F:FuelCalcs.cs†L125-L133】【F:FuelCalcs.cs†L1120-L1151】 |
| Mandatory stop checkbox | CheckBox | "Mandatory pit stop required" | `MandatoryStopRequired` (TwoWay) | boolean | Forces at least one stop in strategy. 【F:FuelCalculatorView.xaml†L464-L472】【F:FuelCalcs.cs†L1135-L1151】 |
| Pit drive-through loss slider | ui:TitledSlider | "Pit Drive-Through Loss (s):" | `PitLaneTimeLoss` (TwoWay) | seconds | Time delta per stop. 【F:FuelCalculatorView.xaml†L474-L486】【F:FuelCalcs.cs†L1112-L1134】 |
| Tyre change time slider | ui:TitledSlider | "Tyre Change Time (s)" | `TireChangeTime` (TwoWay) | seconds | Additional per-stop time. 【F:FuelCalculatorView.xaml†L488-L498】【F:FuelCalcs.cs†L1091-L1110】 |
| Save all to profile button | styles:SHButtonPrimary | "Save All to Profile" | `SavePlannerDataToProfileCommand` | persist profile | Writes current planner settings to car/track profile. 【F:FuelCalculatorView.xaml†L502-L508】【F:FuelCalcs.cs†L1739-L1744】 |
| Fuel save target slider | Slider | "Fuel Save Target (L/Lap):" | `FuelSaveTarget` (TwoWay) | L/lap | Used in simulator calc. 【F:FuelCalculatorView.xaml†L573-L585】【F:FuelCalcs.cs†L53-L60】 |
| Fuel save time-loss textbox | TextBox | "Est. Time Loss per Lap" | `TimeLossPerLapOfFuelSave` (TwoWay) | time | Manual entry for simulator. 【F:FuelCalculatorView.xaml†L584-L585】【F:FuelCalcs.cs†L54-L60】 |
| Load last session button | Button | "Load Last Session Data for Comparison" | `LoadLastSessionCommand` | post-race | Loads analysis grid. 【F:FuelCalculatorView.xaml†L595-L606】【F:FuelCalcs.cs†L67-L71】 |

## Data Source Wiring – Live vs Profile vs Manual
- **Live telemetry ingestion:** `SetLiveSession` selects the live car/track, rebuilds the track list, and updates snapshot labels; it runs on the UI dispatcher and marks the session active. 【F:FuelCalcs.cs†L1758-L1784】【F:FuelCalcs.cs†L2053-L2087】
- **Live fuel/lap feeds:** `SetLiveFuelPerLap` updates `LiveFuelPerLap`/display, toggles off apply suggestions when unavailable, and exposes `IsLiveFuelPerLapAvailable` for the LIVE button and auto-apply checkbox. 【F:FuelCalcs.cs†L864-L888】
- **Live pace and leader delta:** `SetLiveLapPaceEstimate` caches live average lap pace, enables the LIVE lap button, computes live vs leader delta, and stores live leader delta separately. Effective `LeaderDeltaSeconds` prefers manual slider values over live telemetry. 【F:FuelCalcs.cs†L1772-L1849】【F:FuelCalcs.cs†L737-L789】
- **Profile loading:** `LoadProfileData` pulls car-level settings (contingency, wet factor, tire change time, race pace delta, refuel rate) plus track data (best lap, avg lap times per condition, fuel per lap, pit loss) into bound properties, sets source info labels, and refreshes condition multipliers. 【F:FuelCalcs.cs†L2157-L2282】
- **Manual edits:** Text boxes/sliders for lap time, fuel per lap, and leader delta write directly to backing properties, set source flags to `manual`, and trigger `CalculateStrategy()`. No global mode flag; each field tracks its own source (e.g., `LapTimeSourceInfo`, `FuelPerLapSourceInfo`, manual vs live leader delta flags). 【F:FuelCalcs.cs†L722-L733】【F:FuelCalcs.cs†L804-L825】【F:FuelCalcs.cs†L737-L789】
- **Source-switch controls:** Buttons invoke commands to overwrite the working values from specific sources (PB, Live, Profile, Max) but do not lock the fields; subsequent manual edits or live updates can replace them. 【F:FuelCalculatorView.xaml†L254-L398】【F:FuelCalcs.cs†L860-L919】【F:FuelCalcs.cs†L2346-L2356】

**High-level behaviour summary:**
1. Live session selection auto-selects the matching car/track profile and updates snapshot displays; live pace/fuel values are available through dedicated commands and optional auto-apply checkboxes. 【F:FuelCalcs.cs†L1758-L1784】【F:FuelCalcs.cs†L864-L888】
2. Profile data is loaded when car/track changes and seeds lap time, fuel per lap, pit loss, and car-level defaults; wet/dry choice re-applies profile values for the chosen condition. 【F:FuelCalcs.cs†L1034-L1080】【F:FuelCalcs.cs†L2157-L2282】
3. Manual edits immediately mark their source as manual and drive strategy recalculation; leader delta combines manual slider and live telemetry with manual taking precedence. 【F:FuelCalcs.cs†L722-L733】【F:FuelCalcs.cs†L737-L789】
4. Source buttons (PB/LIVE/PROFILE/MAX) simply overwrite the current working value; the field remains editable afterwards and can be saved back to profiles. 【F:FuelCalculatorView.xaml†L254-L398】【F:FuelCalcs.cs†L860-L919】

## “Save all to profile” Behaviour
- **Trigger:** The "Save All to Profile" button binds to `SavePlannerDataToProfileCommand`, which calls `SavePlannerDataToProfile()`. 【F:FuelCalculatorView.xaml†L502-L508】【F:FuelCalcs.cs†L1739-L1744】【F:FuelCalcs.cs†L1429-L1528】
- **Target resolution:** Determines profile: live session saves to live car via `EnsureCar`, otherwise uses the UI-selected profile; resolves track key using live track key when available or selected `TrackStats`. Guard dialogs prevent saving without a profile/track. 【F:FuelCalcs.cs†L1431-L1477】
- **Values written:**
  - Car-level: contingency value/type, wet multiplier, tire change time, race pace delta, and condition multipliers (formation lap burn, wet factor when wet). 【F:FuelCalcs.cs†L1479-L1492】
  - Track-level: lap time (wet or dry based on current condition), fuel per lap parsed from textbox, pit lane loss, best lap (if available), and condition multipliers (formation lap burn/wet factor). 【F:FuelCalcs.cs†L1493-L1518】
  - Persists via `ProfilesViewModel.SaveProfiles()` and refreshes profile-derived UI afterwards. 【F:FuelCalcs.cs†L1520-L1523】
- **Not updated:** Sample counts or historical min/max stats for fuel/lap times are not modified; leader/pace deltas and live suggestion flags are not persisted. 

## Any Observations / Potential Problem Areas
- Manual slider for leader delta overrides live telemetry whenever non-zero; users may not realize live deltas are ignored until manual slider is reset. 【F:FuelCalcs.cs†L737-L789】
- `LoadProfileData` resets strategy inputs (laps/minutes, max fuel, etc.) to defaults after loading profile-derived values, which can discard user-entered race specifics when switching cars/tracks. 【F:FuelCalcs.cs†L2268-L2279】【F:FuelCalcs.cs†L1415-L1427】
- Wet/dry toggle reloads lap time/fuel from profile when available; if profile lacks wet data, it falls back to dry/defaults without an explicit warning. 【F:FuelCalcs.cs†L1034-L1080】【F:FuelCalcs.cs†L2221-L2236】
- “Apply live suggestion” checkboxes simply copy live values when available; they don’t track the source afterwards, so later live updates are ignored unless the checkbox remains checked and live data is still present. 【F:FuelCalcs.cs†L323-L335】【F:FuelCalcs.cs†L864-L888】
