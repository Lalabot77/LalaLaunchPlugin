# Fuel Tab Data Source Flow (CANONICAL)

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1  
Last updated: 2025-12-28  
Branch: docs/refresh-index-subsystems

Scope: technical flow of lap time and fuel-per-lap sources for the Fuel planner tab. See `Docs/SimHubParameterInventory.md` for exported properties and `Docs/FuelProperties_Spec.md` for fuel-model rules.

## Source precedence and automatic application
Evidence: `FuelCalcs.cs` — planning source handling and auto-apply paths.【F:FuelCalcs.cs†L298-L339】【F:FuelCalcs.cs†L2635-L2706】

- **Planning source selector:** `SelectedPlanningSourceMode` toggles between **Profile** and **LiveSnapshot**. Switching resets manual lap time/fuel flags and re-applies sources to auto fields.
- **Auto-apply rules:** When planning source = Profile, profile averages (lap and fuel) are pushed automatically unless the corresponding manual flag is set. When planning source = LiveSnapshot and `IsFuelReady` is true, live averages are auto-applied (lap and fuel) only if the user has not marked them manual.
- **Manual override:** Typing lap time or fuel sets `IsEstimatedLapTimeManual` / `IsFuelPerLapManual`, blocking further auto-applies until the planning source is changed (which clears the manual flags).
- **Buttons override source info:** PB/LIVE/PROFILE/MAX buttons set both the value and `SourceInfo` labels; the latest button press wins even if the planning source differs.

## Lap time sources
- **Manual entry:** `EstimatedLapTime` textbox; sets `LapTimeSourceInfo` = `"source: manual"` and flips the manual flag.【F:FuelCalcs.cs†L676-L716】
- **PB button:** `LoadPersonalBestAsRacePace()` loads stored PB + delta, sets `LapTimeSourceInfo` = `"source: PB"`, and refreshes when PB changes while PB mode is active.【F:FuelCalcs.cs†L750-L778】【F:FuelCalcs.cs†L2319-L2332】
- **LIVE button:** `UseLiveLapPace()` applies `_liveAvgLapSeconds`, sets `LapTimeSourceInfo` = `"source: live average"`. Live average maintained via `SetLiveLapPaceEstimate(...)` from `LalaLaunch`.【F:FuelCalcs.cs†L173-L216】【F:FuelCalcs.cs†L1693-L1758】【F:LalaLaunch.cs†L1895-L2143】
- **PROFILE button:** `LoadProfileLapTime()` uses profile dry/wet average and marks `LapTimeSourceInfo` = `"source: profile"`. Profile loading defaults to dry average (PB/manual fallback) on load.【F:FuelCalcs.cs†L108-L150】【F:FuelCalcs.cs†L1085-L1161】【F:FuelCalcs.cs†L2142-L2226】
- **Auto-apply:** When planning source LiveSnapshot and fuel is ready, live avg is auto-applied unless manually overridden; Profile mode auto-applies profile average similarly.【F:FuelCalcs.cs†L2635-L2706】

## Fuel per lap sources
- **Manual entry:** `FuelPerLapText` setter parses text, sets `FuelPerLapSourceInfo` = `"source: manual"`, and marks fuel manual.【F:FuelCalcs.cs†L500-L546】
- **LIVE button:** `UseLiveFuelPerLap()` applies rolling live average and sets `FuelPerLapSourceInfo` = `"source: live average"`. Live values arrive via `SetLiveFuelPerLap(...)` from `LalaLaunch`.【F:FuelCalcs.cs†L306-L341】【F:FuelCalcs.cs†L785-L806】【F:LalaLaunch.cs†L1895-L2143】
- **MAX button:** `UseMaxFuelPerLap()` uses session max (`_liveMaxFuel` per condition) or plugin max display; sets `FuelPerLapSourceInfo` accordingly.【F:FuelCalcs.cs†L1188-L1220】
- **PROFILE button:** `UseProfileFuelPerLap()` loads profile dry average and marks `FuelPerLapSourceInfo` = `"source: profile"`. Profile data also seeds availability flags on load.【F:FuelCalcs.cs†L1188-L1249】【F:FuelCalcs.cs†L2142-L2226】
- **Save/eco helpers:** `UseLiveFuelSave()` pulls live min burn when available; `FuelPerLapSourceInfo` notes `"Live save"`.【F:FuelCalcs.cs†L1195-L1205】
- **Auto-apply:** When planning source LiveSnapshot and fuel is ready, `ApplySetLiveFuelPerLap` auto-applies live fuel to planner if fuel is not manual; Profile mode auto-applies profile fuel similarly.【F:FuelCalcs.cs†L1250-L1257】【F:FuelCalcs.cs†L2635-L2706】

## Live snapshot integration
- `LalaLaunch` pushes identity and live stats via `SetLiveSession`, `SetLiveLapPaceEstimate`, `SetLiveFuelPerLap`, `SetMaxFuelPerLap`, and `UpdateLiveDisplay`. These update the snapshot header, availability flags, and strategy displays in `FuelCalcs`.【F:LalaLaunch.cs†L3200-L3233】【F:LalaLaunch.cs†L1895-L2143】【F:FuelCalcs.cs†L1729-L1918】【F:FuelCalcs.cs†L2298-L2316】
- Live lap pace availability gate: `IsLiveLapPaceAvailable` drives the LIVE button enablement and helper hints.【F:FuelCalcs.cs†L353-L365】【F:FuelCalcs.cs†L1693-L1758】
- Live fuel readiness gate: auto-apply from live snapshot requires `IsFuelReady` (stable confidence) from the plugin.【F:FuelCalcs.cs†L2635-L2706】【F:LalaLaunch.cs†L468-L506】

## Max fuel suggestion vs. override
- `HasLiveMaxFuelSuggestion` surfaces when live max fuel is known; `SetLiveMaxFuelOverrideCommand` calls `ApplyLiveMaxFuelSuggestion()` to copy it into `MaxFuelOverride`. No auto-apply occurs without user command.【F:FuelCalcs.cs†L588-L605】【F:FuelCalcs.cs†L1222-L1227】【F:FuelCalcs.cs†L2575-L2595】

## UI indicators and bindings
- `LapTimeSourceInfo`, `FuelPerLapSourceInfo`, and helper labels are bound in XAML to show active source selections under each control.【F:FuelCalculatorView.xaml†L230-L301】【F:FuelCalculatorView.xaml†L371-L423】
- Live availability flags (`IsLiveLapPaceAvailable`, `IsLiveFuelPerLapAvailable`, `HasLiveMaxFuelSuggestion`) control button enablement; manual flags block automatic reapplication until the planning source changes.【F:FuelCalcs.cs†L298-L365】【F:FuelCalcs.cs†L2142-L2226】

## TODO/VERIFY
- TODO/VERIFY: Confirm whether any UI flow surfaces wet/dry profile eco choices beyond min fuel; current code auto-applies only average values (`TryGetProfileFuelForCondition`).【F:FuelCalcs.cs†L2635-L2706】
