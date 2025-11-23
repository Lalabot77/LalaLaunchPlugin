# Fuel Tab data source flow (current implementation)

## Where values come from
- **Car & track selection**
  - The Fuel tab binds its combo boxes to `AvailableCarProfiles` and `AvailableTrackStats` in `FuelCalcs`. Selecting a car or track updates `SelectedCarProfile` / `SelectedTrackStats`, which in turn calls `LoadProfileData()` to hydrate planner defaults. 【F:FuelCalcs.cs†L510-L623】
  - Live sessions call `SetLiveSession(car, track)` from `LalaLaunch`, which selects the matching profile entry (or the first available track), updates the snapshot labels, and triggers the same `LoadProfileData()` path. 【F:FuelCalcs.cs†L1958-L2100】【F:LalaLaunch.cs†L579-L704】

- **Lap time sources**
  - Manual entry goes through the `EstimatedLapTime` property; typing sets `LapTimeSourceInfo` to `"source: manual"` and recalculates. 【F:FuelCalcs.cs†L676-L716】
  - The **PB** button invokes `LoadPersonalBestAsRacePace()`, which uses the stored PB plus race pace delta, updates `LapTimeSourceInfo` to `"source: PB"`, and recalculates. It also refreshes if the PB changes while PB is already selected. 【F:FuelCalcs.cs†L750-L778】【F:FuelCalcs.cs†L2319-L2332】
  - The **LIVE** button calls `UseLiveLapPace()`, which applies the rolling live average from `_liveAvgLapSeconds`, sets `LapTimeSourceInfo` to `"source: live average"`, and recalculates. Live telemetry populates `_liveAvgLapSeconds` through `SetLiveLapPaceEstimate(...)` when pace samples arrive. 【F:FuelCalcs.cs†L173-L216】【F:FuelCalcs.cs†L1693-L1758】
  - The **PROFILE** button calls `LoadProfileLapTime()`, pulling the saved dry/wet average for the selected track and marking `LapTimeSourceInfo` as `"source: profile"`. `LoadProfileData()` also defaults to the profile dry average (or PB/manual fallback) on load. 【F:FuelCalcs.cs†L108-L150】【F:FuelCalcs.cs†L1085-L1161】【F:FuelCalcs.cs†L2142-L2218】

- **Fuel per lap sources**
  - Manual typing goes through `FuelPerLapText`, which marks `FuelPerLapSourceInfo` as `"source: manual"` and updates `FuelPerLap` when parsable. 【F:FuelCalcs.cs†L500-L546】
  - The **MAX** button calls `UseMaxFuelPerLap()`, pulling the session maximum tracked by the plugin and tagging `FuelPerLapSourceInfo` as `"source: max"`. 【F:FuelCalcs.cs†L770-L783】
  - The **LIVE** button runs `UseLiveFuelPerLap()`, which applies the rolling live average from telemetry and sets `FuelPerLapSourceInfo` to `"source: live average"`. Live telemetry populates the cached averages via `SetLiveFuelPerLap(...)`. 【F:FuelCalcs.cs†L306-L341】【F:FuelCalcs.cs†L785-L806】【F:LalaLaunch.cs†L820-L1093】
  - The **PROFILE** button calls `UseProfileFuelPerLap()`, loading the track’s dry average and marking `FuelPerLapSourceInfo` as `"source: profile"`. Profile loading also sets fuel per lap defaults and availability flags. 【F:FuelCalcs.cs†L1188-L1249】【F:FuelCalcs.cs†L2142-L2226】

## UI indicators and flags
- `LapTimeSourceInfo`, `FuelPerLapSourceInfo`, and the live/PB/profile helper labels are bound in XAML under the lap time and fuel per lap controls so users can see which source is active. 【F:FuelCalculatorView.xaml†L230-L301】【F:FuelCalculatorView.xaml†L371-L423】
- Live availability and suggestion flags come from `FuelCalcs`:
  - `IsLiveLapPaceAvailable` controls the **LIVE** lap time button; `IsLiveFuelPerLapAvailable` controls the fuel **LIVE** button.
  - `ApplyLiveFuelSuggestion` and `ApplyLiveMaxFuelSuggestion` are user-facing toggles; availability is tracked separately via `IsLiveFuelPerLapAvailable` and `HasLiveMaxFuelSuggestion` so telemetry can publish suggestions without overwriting the user’s choice. 【F:FuelCalcs.cs†L280-L343】【F:FuelCalcs.cs†L785-L834】【F:FuelCalcs.cs†L2298-L2316】
- Live session metadata (car, track, best/average laps, fuel tank detection, confidence summaries) is pushed from `LalaLaunch` into `FuelCalcs` through `SetLiveLapPaceEstimate`, `SetLiveFuelPerLap`, `SetMaxFuelPerLap`, and `UpdateLiveDisplay`, which in turn update the snapshot at the top of the Fuel tab. 【F:LalaLaunch.cs†L820-L1093】【F:FuelCalcs.cs†L1729-L1918】【F:FuelCalcs.cs†L2298-L2316】
