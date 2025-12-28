# Pace & Projection

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1  
Last updated: 2025-12-28  
Branch: docs/refresh-index-subsystems

## Purpose
Maintain clean-lap pace metrics, choose projection lap times, and integrate leader pace for strategy and fuel projection.

## Inputs (source + cadence)
- Telemetry lap times, leader lap time candidates, session type/state (per lap via `DetectLapCrossing`).【F:LalaLaunch.cs†L1080-L1415】
- Profile pace averages for fallback; live leader lap times from SimHub telemetry (`SessionBestLapTime`, etc.).【F:LalaLaunch.cs†L1336-L1375】【F:LalaLaunch.cs†L4306-L4391】

## Internal state
- Recent player lap list (size 6) for stint/last5, median pace for pit baseline, leader lap history list with last value and cleared flag.【F:LalaLaunch.cs†L1080-L1415】【F:LalaLaunch.cs†L1336-L1375】
- Projection lap stable value/source cache with last logged source and debounce timestamp.【F:LalaLaunch.cs†L4306-L4391】
- Pace confidence based on window size/outlier rejection; overall confidence combines with fuel confidence.【F:LalaLaunch.cs†L1080-L1415】【F:LalaLaunch.cs†L468-L491】

## Calculation blocks (high level)
1. **Lap acceptance:** Reject laps for warm-up, pit involvement, incidents, bad lap times, and pace outliers (gross >20 s, slow >6 s above baseline).【F:LalaLaunch.cs†L1080-L1415】
2. **Pace windows:** Update stint avg, last5 avg, leader avg, leader delta; push live lap pace to FuelCalcs when valid.【F:LalaLaunch.cs†L1080-L1415】【F:LalaLaunch.cs†L1336-L1375】
3. **Projection lap selection:** Prefer stint average when pace confidence high; else last5; else profile avg; fallback estimator. Logs source changes every ≥5 s when value/source changes.【F:LalaLaunch.cs†L4306-L4391】
4. **Integration with fuel:** Projection lap and pace confidence feed race-distance projection and pit window calculations in fuel model.【F:LalaLaunch.cs†L1895-L2143】

## Outputs (exports + logs)
- Exports: `Pace.StintAvgLapTimeSec`, `Pace.Last5LapAvgSec`, `Pace.LeaderAvgLapTimeSec`, `Pace.LeaderDeltaToPlayerSec`, `Pace.PaceConfidence`, `Pace.OverallConfidence`, `Fuel.ProjectionLapTime_Stable`, `Fuel.ProjectionLapTime_StableSource`. See inventory.
- Logs: per-lap `[LalaPlugin:PACE] ...`, leader pace clear, projection source change, leader-lap acceptance/rejection logs.【F:LalaLaunch.cs†L1238-L1281】【F:LalaLaunch.cs†L1336-L1375】【F:LalaLaunch.cs†L4378-L4391】【F:LalaLaunch.cs†L4845-L4872】

## Dependencies / ordering assumptions
- Leader pace depends on telemetry candidates; uses player pace as fallback for rejection floor.
- Fuel projection relies on stable burn readiness; if fuel not ready, projection laps may fall back to SimHub laps remaining.

## Reset rules
- Fuel-model reset and session-token change clear pace windows and leader pace history; confidence reset to 0.【F:LalaLaunch.cs†L830-L1040】【F:LalaLaunch.cs†L3308-L3365】

## Failure modes
- Missing leader data → leader avg cleared and logged; projection still runs using player pace.
- Outlier filtering may reject valid laps in chaotic sessions; monitor lap reason logs.

## Test checklist
- Drive clean laps to see pace/leader logs and exports rise.
- Simulate leader feed drop to confirm clear log and zeroed leader delta.
- Change session type to verify pace reset and projection source log refresh.
