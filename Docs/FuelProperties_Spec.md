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
- **Fuel.DeltaLaps / Fuel.Delta.* liters** — Surplus or deficit vs. race distance in laps and liters for current, planned, push, and save scenarios; zeros out when no valid burn exists.【F:LalaLaunch.cs†L1722-L1756】
- **Fuel.PushFuelPerLap / Fuel.FuelSavePerLap / Fuel.DeltaLapsIfPush / Fuel.CanAffordToPush** — Push burn uses observed/session max; save burn uses window minima or 97% fallback. Delta/afford flags compare those burns to projected laps remaining.【F:LalaLaunch.cs†L1754-L1870】
- **Pace.StintAvgLapTimeSec / Pace.Last5LapAvgSec / Pace.LeaderDeltaToPlayerSec** — Rolling pace feeds for projection; reset when fuel model is invalid to avoid stale projections.【F:LalaLaunch.cs†L1758-L1766】

## Pit projections and stop timing
- **Fuel.Pit.TotalNeededToEnd / NeedToAdd** — Total liters required to finish at current/stable burn and the shortfall vs. current fuel.【F:LalaLaunch.cs†L1806-L1854】
- **Fuel.Pit.TankSpaceAvailable / WillAdd / FuelOnExit** — Capacity-aware add clamped to BoP/override tank size and whether refuel is selected; projected post-stop fuel uses the clamped add.【F:LalaLaunch.cs†L1837-L1855】
- **Fuel.Pit.DeltaAfterStop / Fuel.Pit.FuelSaveDeltaAfterStop** — Lap surplus after the planned stop using current or save burn; push delta mirrors the same pattern (calculated alongside save delta).【F:LalaLaunch.cs†L1862-L1870】
- **Fuel.Live.TotalStopLoss / Fuel.Live.RefuelRate_Lps / Fuel.Live.TireChangeTime_S / Fuel.Live.PitLaneLoss_S** — Strategy-facing timing components sourced from `FuelCalculator` each tick; combined to estimate total pit stop loss.【F:LalaLaunch.cs†L2319-L2326】

## Pit window and stop counts
- **Fuel.IsPitWindowOpen / Fuel.PitWindowOpeningLap** — Single-stop helper comparing requested add vs. tank space and current burn to determine whether the stop fits this lap or when it will.【F:LalaLaunch.cs†L1872-L1879】【F:LalaLaunch.cs†L2289-L2299】
- **Fuel.PitStopsRequiredByFuel / Fuel.PitStopsRequiredByPlan / Fuel.Pit.StopsRequiredToEnd** — Capacity-based and strategy-based stop counts; the published value favors the plan when available, otherwise the capacity calculation.【F:LalaLaunch.cs†L1874-L1880】【F:LalaLaunch.cs†L2320-L2322】

These calculations are exported through the delegates registered in `AttachCore`/`AttachVerbose` near the plugin initialization block.【F:LalaLaunch.cs†L2280-L2330】
