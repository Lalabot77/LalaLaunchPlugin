# Pit Timing & Pit Loss

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1  
Last updated: 2025-12-28  
Branch: docs/refresh-index-subsystems

## Purpose
Capture pit-lane travel, compute DTL/direct losses, and publish pit-lite surfaces for dashboards and strategy. Canonical exports: `Pit.*` and `PitLite.*` in `Docs/SimHubParameterInventory.md`.

## Inputs (source + cadence)
- Telemetry lap times, pit lane flags, stop duration from SimHub telemetry (per tick in `DataUpdate`).【F:LalaLaunch.cs†L1336-L1415】【F:PitEngine.cs†L80-L239】
- Baseline pace from fuel/pace estimator (stint/median) and profile averages for pit delta comparisons.【F:LalaLaunch.cs†L1336-L1415】
- Pit service selections (refuel on/off) influence tank-space and stop-loss calculations in fuel model.【F:LalaLaunch.cs†L1895-L2143】

## Internal state
- `PitEngine` state machine (AwaitingPitLap, AwaitingOutLap) with latched lane/box timers and stop duration.【F:PitEngine.cs†L80-L239】
- Debug fields `_pitDbg_*` latched on out-lap completion (raw pit lap, DTL formula terms).【F:LalaLaunch.cs†L1336-L1415】
- `PitCycleLite` entry/exit latches and candidate loss source (`dtl` vs `direct`).【F:PitCycleLite.cs†L122-L217】
- Freeze flag `_pitFreezeUntilNextCycle` to hold debug values until next pit entry.【F:LalaLaunch.cs†L1336-L1415】

## Calculation blocks (high level)
1. **Pit entry/exit detection:** `PitCycleLite` arms cycle on entry, latches timers on exit from `PitEngine`.【F:PitCycleLite.cs†L122-L163】
2. **Pit lap/out-lap validation:** `PitEngine` validates pit lap and out-lap before computing DTL; invalid laps abort cycle.【F:PitEngine.cs†L175-L218】
3. **DTL/direct selection:** On out-lap completion, chooses DTL if available else direct lane time; publishes totals and debug fields.【F:LalaLaunch.cs†L1336-L1415】【F:PitEngine.cs†L218-L239】
4. **Persistence:** `Pit_OnValidPitStopTimeLossCalculated` saves pit-lane loss to profile (debounced) and updates Fuel tab snapshot.【F:LalaLaunch.cs†L2950-L3004】
5. **Fuel integration:** `UpdateLiveFuelCalcs` consumes last direct time and total loss for stop-loss estimates and pit window tank capacity.【F:LalaLaunch.cs†L1895-L2143】

## Outputs (exports + logs)
- Exports: `Pit.LastDirectTravelTime`, `Pit.LastTotalPitCycleTimeLoss`, `PitLite.*` live timers, debug verbose fields (`Lala.Pit.*`). See inventory for cadence.
- Logs: `[LalaPlugin:Pit Cycle] ...` from `PitEngine`, `[LalaPlugin:Pit Lite] ...` from `PitCycleLite`, profile save logs, pit window logs (fuel model).【F:PitEngine.cs†L90-L239】【F:PitCycleLite.cs†L122-L217】【F:LalaLaunch.cs†L2145-L2335】

## Dependencies / ordering assumptions
- `PitEngine.Update` must run before `PitCycleLite.Update` each tick (`DataUpdate` order) so pit-lite sees fresh timers.【F:LalaLaunch.cs†L3308-L3415】
- Baseline pace provided by fuel/pace estimator; if missing, PitCycleLite can fall back to direct loss publish.
- Profile persistence depends on `ActiveProfile` and track key/name; missing profile aborts save with warning log.【F:LalaLaunch.cs†L2950-L3004】

## Reset rules
- Session token change resets PitEngine/PitLite state and may finalize pending candidate once before clearing.【F:LalaLaunch.cs†L3308-L3365】
- Fuel-model session-type reset indirectly clears pit freeze and debug latches via `ResetLiveFuelModelForNewSession`.【F:LalaLaunch.cs†L830-L1040】

## Failure modes
- Missing baseline pace → PitLite publishes direct loss instead of DTL.
- Repeated identical pit loss within debounce window ignored to prevent thrash.
- TODO/VERIFY: Validate lane/box timers when SimHub pit flags glitch (current code trusts timers if available).

## Test checklist
- Run pit entry/exit to see PitLite entry/exit logs and live timers.
- Complete pit + out-lap with valid baseline to log DTL formula and profile save; verify `Pit.LastTotalPitCycleTimeLoss` updates.
- Trigger invalid pit/out-lap (e.g., off-track) to confirm abort logs and no save.
