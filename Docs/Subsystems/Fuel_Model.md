# Fuel Model

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1  
Last updated: 2025-12-28  
Branch: docs/refresh-index-subsystems

## Purpose
Captures live fuel burn, selects a stable burn, projects race distance (including after-zero), and drives pit window exports consumed by the Fuel tab and dashes. Canonical exports: see `Docs/SimHubParameterInventory.md` (Fuel section).

## Inputs (source + cadence)
- Telemetry fuel level, lap count, session time/remain, pit refuel request (`DataUpdate` 500 ms poll).【F:LalaLaunch.cs†L1895-L2143】【F:LalaLaunch.cs†L3572-L3578】
- Profile fuel baselines (dry/wet) from active profile (per access).【F:LalaLaunch.cs†L810-L1040】
- SimHub fallback `DataCorePlugin.Computed.Fuel_LitersPerLap` when no laps accepted.【F:LalaLaunch.cs†L1895-L1967】
- Session type and identity for reset/seeding; car/track identity for seeds.【F:LalaLaunch.cs†L830-L1003】【F:LalaLaunch.cs†L3308-L3365】

## Internal state
- Rolling windows for dry/wet burn (size 5), min/max, sample freshness counters, active seeds, mode flag (`_isWetMode`).【F:LalaLaunch.cs†L340-L420】【F:LalaLaunch.cs†L810-L1040】
- Stable burn triple (`_stableFuelPerLap`, source, confidence) with deadband hold.【F:LalaLaunch.cs†L4180-L4254】
- After-zero planner/live estimates and last projection values for logging thresholds.【F:LalaLaunch.cs†L1895-L2143】【F:LalaLaunch.cs†L4498-L4516】
- Pit window state cache to throttle logging (`_lastPitWindowState`, label, timestamp).【F:LalaLaunch.cs†L2145-L2335】

## Calculation blocks (high level)
1. **Lap acceptance** — Apply warm-up/pit/incident/telemetry/profile-bracket rejection before window insert.【F:LalaLaunch.cs†L1080-L1415】
2. **Window update** — Maintain dry/wet lists, mins/maxes, session max, and send live stats to FuelCalcs.【F:LalaLaunch.cs†L1714-L1868】
3. **Stable selection** — Choose live vs. profile candidate with deadband; assign confidence.【F:LalaLaunch.cs†L4180-L4254】
4. **Projection** — Compute projection lap time (pace-driven) and projected laps; apply after-zero source (planner vs live).【F:LalaLaunch.cs†L1895-L2143】【F:LalaLaunch.cs†L4306-L4391】
5. **Pit math** — Tank laps, deltas, push/save guidance, required stops, and pit window state machine.【F:LalaLaunch.cs†L1895-L2335】
6. **Smoothing** — EMA smoothing for laps-remaining and pit deltas for `_S` exports.【F:LalaLaunch.cs†L4243-L4306】

## Outputs (exports + logs)
- Exports under `Fuel.*`, `Pace.*`, pit window fields; see inventory. Logs: lap summaries (`PACE/FUEL/RACE PROJECTION`), pit window state changes, projection source changes, after-zero results.【F:LalaLaunch.cs†L1238-L1281】【F:LalaLaunch.cs†L2145-L2347】【F:LalaLaunch.cs†L4378-L4516】

## Dependencies / ordering assumptions
- Requires `FuelCalculator` instance for live display updates and strategy integration; `UpdateLiveFuelCalcs` called after leader delta refresh and pit updates in the 500 ms loop.【F:LalaLaunch.cs†L3572-L3578】【F:LalaLaunch.cs†L1336-L1415】
- Pit window uses stable burn and tank capacity from pit systems (`EffectiveLiveMaxTank`).【F:LalaLaunch.cs†L1895-L2143】

## Reset rules
- `HandleSessionChangeForFuelModel` (session type change) and session-token change both call `ResetLiveFuelModelForNewSession` (clears windows, stable, confidence, pit window, pace windows) with optional seeding on Race entry.【F:LalaLaunch.cs†L830-L1040】【F:LalaLaunch.cs†L3308-L3365】【F:LalaLaunch.cs†L3649-L3676】
- Car/track change clears seeds/confidence.【F:LalaLaunch.cs†L968-L1003】

## Failure modes
- Telemetry zeros or implausible fuel deltas → rejection; may leave model unready (confidence 0, pit window **NO DATA YET**).
- Missing profile baselines → profile fallback skipped; stable holds prior or resets to fallback.
- TODO/VERIFY: Incident latch (`_latchedIncidentReason`) is a placeholder; confirm wiring to actual incident telemetry.

## Test checklist
- Start new session, observe `[LalaPlugin:Fuel Burn] ... captured seed` and seeding on Race entry with matching combo.
- Drive clean laps to see lap summary logs and stable burn rise above readiness; pit window transitions out of **NO DATA YET**.
- Trigger pit lap and pit-warmup laps to confirm rejection reasons and stable burn hold.
- Switch session type (e.g., Practice→Race) and verify resets + optional seed application.
