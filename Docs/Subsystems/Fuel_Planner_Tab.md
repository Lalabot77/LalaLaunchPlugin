# Fuel Planner Tab

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1  
Last updated: 2025-12-28  
Branch: docs/refresh-index-subsystems

## Purpose
Planner UI for lap time and fuel-per-lap selection, mixing live snapshots with profile data and manual overrides. Drives strategy inputs and displays live/session snapshots.

## Inputs (source + cadence)
- Live snapshots from `LalaLaunch` (`SetLiveSession`, `SetLiveLapPaceEstimate`, `SetLiveFuelPerLap`, `SetMaxFuelPerLap`, `UpdateLiveDisplay`) pushed every 500 ms poll.【F:LalaLaunch.cs†L3200-L3233】【F:LalaLaunch.cs†L1895-L2143】【F:FuelCalcs.cs†L1729-L1918】
- Profile data from `ProfilesViewModel` and active profile selection (on load and car/track change).【F:FuelCalcs.cs†L510-L623】
- User interactions: planning source toggle, manual text edits, LIVE/PB/PROFILE/MAX buttons (on demand).【F:FuelCalcs.cs†L298-L365】【F:FuelCalcs.cs†L676-L806】

## Internal state
- `SelectedPlanningSourceMode` (Profile vs LiveSnapshot) with manual flags for lap time and fuel to block auto-apply.【F:FuelCalcs.cs†L298-L339】【F:FuelCalcs.cs†L2635-L2706】
- Live caches: `_liveAvgLapSeconds`, `_liveMaxFuel`, live min/max burn per condition, `IsFuelReady` gating from plugin.【F:FuelCalcs.cs†L108-L216】【F:FuelCalcs.cs†L1188-L1257】
- Snapshot identity (live car/track) for header and suggestion availability.【F:FuelCalcs.cs†L1729-L1918】

## Calculation blocks (high level)
1. **Source selection:** Planning source change clears manual flags and applies profile/live averages to lap time/fuel when allowed.【F:FuelCalcs.cs†L298-L339】【F:FuelCalcs.cs†L2635-L2706】
2. **Manual overrides:** Text edits set manual flags and `SourceInfo` labels (`source: manual`).【F:FuelCalcs.cs†L500-L716】
3. **Button actions:** PB/LIVE/PROFILE/MAX call specific loaders and update `SourceInfo` strings; LIVE/PB guard on availability flags.【F:FuelCalcs.cs†L676-L806】【F:FuelCalcs.cs†L1188-L1249】
4. **Live suggestion handling:** Live fuel auto-applied only when planning source = LiveSnapshot, `IsFuelReady` true, and fuel not manual; max-fuel suggestion requires explicit command (`ApplyLiveMaxFuelSuggestion`).【F:FuelCalcs.cs†L1250-L1257】【F:FuelCalcs.cs†L1222-L1227】
5. **Snapshot updates:** Live session identity changes refresh header labels and live availability flags.【F:FuelCalcs.cs†L1729-L1918】

## Outputs (exports + logs)
- UI bindings in `FuelCalculatorView.xaml` for source info labels, availability states, and helper text.【F:FuelCalculatorView.xaml†L230-L301】【F:FuelCalculatorView.xaml†L371-L423】
- Strategy values consumed inside `FuelCalcs` for pit/lap projections; no direct SimHub exports beyond those set by `LalaLaunch`.
- Logs: PB update acceptance/rejection, track resolution, strategy reset, leader lap calc (see `Docs/SimHubLogMessages.md`).【F:FuelCalcs.cs†L2038-L2057】【F:FuelCalcs.cs†L3839-L3879】【F:ProfilesManagerViewModel.cs†L66-L182】

## Dependencies / ordering assumptions
- Requires active `LalaLaunch` to push live data; planning source LiveSnapshot depends on `IsFuelReady` from plugin.
- Car/track selection uses `FuelCalcs.SetLiveSession` inputs; must align with profile management flow.

## Reset rules
- Planning source change clears manual lap/fuel flags and reapplies source values.【F:FuelCalcs.cs†L298-L339】
- `ResetStrategyInputs` and `ResetSnapshotDisplays` clear planner values when strategy reset is triggered (e.g., via settings reset).【F:FuelCalcs.cs†L2038-L2057】【F:FuelCalcs.cs†L2985-L3023】
- Session identity change from plugin updates live snapshot identity; planner state otherwise persists unless reset invoked.

## Failure modes
- Live snapshot unavailable or fuel not ready → LiveSnapshot planning source auto-apply no-ops, leaving prior values.
- Profile entries missing -> profile loads may fall back to defaults; check `TryGetProfileFuelForCondition` return before applying.
- TODO/VERIFY: Determine UX for wet/dry toggles when live condition flips mid-session; auto-apply currently gated on `IsFuelReady`.

## Test checklist
- Toggle planning source and confirm manual flags clear and sources reapply.
- Enter manual lap/fuel then change planning source to ensure auto-apply resumes.
- Press LIVE/PB/PROFILE/MAX buttons with/without live availability to verify `SourceInfo` labels and blocking.
- With `IsFuelReady` false, ensure LiveSnapshot auto-apply does not overwrite manual values; once ready, confirm it applies.
