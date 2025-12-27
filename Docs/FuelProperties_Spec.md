# Fuel Properties Technical Specification

This document summarizes how the fuel- and pit-related SimHub properties are computed inside `LalaLaunch.cs`. Names include the implicit `LalaLaunch.` prefix.

## Live consumption and stability
- **Fuel.LiveFuelPerLap** — Rolling average of accepted lap burns. Laps are rejected for pit contact, incidents, tiny/huge fuel deltas, or falling outside a 50–150% profile bracket. Accepted laps update wet/dry windows, mins/maxes, and confidence before sending the value to `FuelCalculator`.【F:LalaLaunch.cs†L1500-L1714】
- **Fuel.LiveFuelPerLap_Stable / StableSource / StableConfidence** — Smoothed burn selected from live, profile, or sim fallback to mitigate early-lap noise; refreshed each tick alongside the live burn.【F:LalaLaunch.cs†L1714-L1767】

## Race length projection
- **Fuel.LiveLapsRemainingInRace / _Stable / _S variants** — Computed every 500 ms using projected lap time and a timed-race overrun model; falls back to the sim’s laps-remaining when projection is invalid. Stable variants mirror the same calculation but prefer the stable burn/pace inputs.【F:LalaLaunch.cs†L1720-L1804】
- **Fuel.Live.ProjectedDriveSecondsRemaining / Fuel.Live.DriveTimeAfterZero** — Wall time remaining including expected post–timer-zero driving, derived from session time, projection lap time, and the extra-overrun allowance.【F:LalaLaunch.cs†L1777-L1788】

## Tank state, deltas, and pacing
- **Fuel.LapsRemainingInTank** — Current fuel divided by the stable burn (or live burn when stable is unavailable).【F:LalaLaunch.cs†L1770-L1778】
- **Fuel.DeltaLaps / Fuel.Delta.* liters** — Surplus or deficit vs. race distance in laps and liters for current, planned, push, and save scenarios; zeros out when no valid burn exists.【F:LalaLaunch.cs†L1931-L2053】
- **Fuel.PushFuelPerLap / Fuel.FuelSavePerLap / Fuel.DeltaLapsIfPush / Fuel.CanAffordToPush** — Push burn uses observed/session max; save burn uses window minima or 97% fallback. Delta/afford flags compare those burns to projected laps remaining.【F:LalaLaunch.cs†L1969-L2053】
- **Pace.StintAvgLapTimeSec / Pace.Last5LapAvgSec / Pace.LeaderDeltaToPlayerSec** — Rolling pace feeds for projection; reset when fuel model is invalid to avoid stale projections.【F:LalaLaunch.cs†L1851-L1866】

## Pit projections and stop timing
- **Fuel.Pit.TotalNeededToEnd / NeedToAdd** — Total liters required to finish at current/stable burn and the shortfall vs. current fuel.【F:LalaLaunch.cs†L1959-L1985】
- **Fuel.Pit.TankSpaceAvailable / WillAdd / FuelOnExit** — Capacity-aware add clamped to BoP/override tank size and whether refuel is selected; projected post-stop fuel uses the clamped add.【F:LalaLaunch.cs†L1960-L1985】
- **Fuel.Pit.DeltaAfterStop / Fuel.Pit.FuelSaveDeltaAfterStop** — Lap surplus after the planned stop using current or save burn; push delta mirrors the same pattern (calculated alongside save delta).【F:LalaLaunch.cs†L1979-L1985】【F:LalaLaunch.cs†L1999-L2027】
- **Fuel.Live.TotalStopLoss / Fuel.Live.RefuelRate_Lps / Fuel.Live.TireChangeTime_S / Fuel.Live.PitLaneLoss_S** — Strategy-facing timing components sourced from `FuelCalculator` each tick; combined to estimate total pit stop loss.【F:LalaLaunch.cs†L2520-L2531】【F:LalaLaunch.cs†L3795-L3809】

- **Fuel.PitWindowState / Fuel.PitWindowLabel** — Race-only pit window status for the requested MFD refuel. Confidence ≤ 60% forces **NO DATA YET** (state 5). Non-race or non-running sessions publish **N/A** (state 6). If refuel is off or the request is ≤ 0, state 4 (**SET FUEL!**). Unknown/invalid tank capacity yields state 8 (**TANK ERROR**). Otherwise the window is open when a stop now (using the clamped add) makes finishing viable under ECO/STD/PUSH based on tank space vs. required add (ECO uses `FuelSaveFuelPerLap`, STD uses stable burn, PUSH uses `PushFuelPerLap`), with precedence PUSH > STD > ECO. If no mode is yet viable, state 7 (**TANK SPACE**) is emitted and `PitWindowOpeningLap` marks the first ECO-viable lap (or 0 if not computable) rather than when the MFD request fits; `PitWindowClosingLap` marks the latest safe lap to pit before fuel runs out, otherwise 0. Open states set `Fuel.IsPitWindowOpen` true and `PitWindowOpeningLap` to the in-progress lap; other states clear the flag and lap. The reserved **RACE CONTROL** state 9 is not emitted without a real signal.【F:LalaLaunch.cs†L2056-L2146】
- **Fuel.PitWindowState enum mapping** — 1 = OPEN ECO, 2 = OPEN STD, 3 = OPEN PUSH, 4 = SET FUEL!, 5 = NO DATA YET, 6 = N/A, 7 = TANK SPACE, 8 = TANK ERROR, 9 = RACE CONTROL (reserved). **Fuel.PitWindowLabel** mirrors these strings.【F:LalaLaunch.cs†L2056-L2146】
- **Fuel.IsPitWindowOpen / Fuel.PitWindowOpeningLap / Fuel.PitWindowClosingLap** — Backward-compatible booleans and lap markers aligned to the state machine above; `PitWindowOpeningLap` is the current lap when open or the first ECO-viable lap when closed, `PitWindowClosingLap` is the latest safe lap to pit based on the stable burn, and both are 0 when not applicable.【F:LalaLaunch.cs†L2056-L2146】
- **Fuel.PitStopsRequiredByFuel / Fuel.PitStopsRequiredByPlan / Fuel.Pit.StopsRequiredToEnd** — Capacity-based and strategy-based stop counts; the published value favors the plan when available, otherwise the capacity calculation.【F:LalaLaunch.cs†L1987-L1997】【F:LalaLaunch.cs†L2524-L2526】

These calculations are exported through the delegates registered in `AttachCore`/`AttachVerbose` near the plugin initialization block.【F:LalaLaunch.cs†L2280-L2330】
