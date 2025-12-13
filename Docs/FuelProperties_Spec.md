# Fuel Properties Technical Specification

This document is the single source of truth for all fuel-related SimHub properties exposed by LalaLaunch. Property names are shown with the implicit `LalaLaunch.` plugin prefix that SimHub adds to every exported delegate.

> CSV export: the same content is available in tabular form at `Docs/FuelProperties_Spec.csv` for spreadsheet use.

## Dependency overview

Tank capacity → `Fuel.Pit.TankSpaceAvailable` → `Fuel.Pit.WillAdd` → `Fuel.Pit.FuelOnExit` → `Fuel.Pit.DeltaAfterStop`

Timed race projection → `Fuel.Live.DriveTimeAfterZero` → `Fuel.Live.ProjectedDriveSecondsRemaining` → `Fuel.LiveLapsRemainingInRace` → fuel needed / deltas

## Live consumption and pacing

### LalaLaunch.Fuel.LiveFuelPerLap
- **Unit:** L/lap
- **Meaning:** Current best estimate of per-lap fuel burn for the active track condition (dry/wet), using rolling accepted laps.
- **Formula:** Rolling average of accepted lap burns. On lap crossing, `fuelUsed = lapStartFuel - currentFuel`. If wet mode: average of `_recentWetFuelLaps`; else `_recentDryFuelLaps`. Falls back to SimHub `DataCorePlugin.Computed.Fuel_LitersPerLap` when no accepted laps exist.【F:LalaLaunch.cs†L1323-L1477】
- **Inputs:**
  - Telemetry: `data.NewData.Fuel`, `data.NewData.MaxFuel`, `data.NewData.LastLapTime`, pit-lane flags, completed laps.
  - Internal state: `_recentDryFuelLaps` / `_recentWetFuelLaps`, seeds, `_lapStartFuel`, `FuelCalculator.IsWet`, profile baselines from `GetProfileFuelBaselines()`.
- **Gating / validity:** Lap rejected when in pit, warm-up, first lap after pit exit, incident flagged, fuel delta ≤0.05 L, fuel delta >20% tank or >baseline×[0.5,1.5].【F:LalaLaunch.cs†L1334-L1397】
- **Clamping:** Max window size 5 samples; max fuel-per-lap tracked only if within [0.7,1.8]×baseline.【F:LalaLaunch.cs†L1408-L1463】
- **Update cadence:** Computed at every completed lap; fallback read on 500 ms `UpdateLiveFuelCalcs` tick until first accepted lap.【F:LalaLaunch.cs†L963-L977】【F:LalaLaunch.cs†L1323-L1477】
- **Code location:** `UpdateLiveFuelCalcs` in `LalaLaunch.cs` lines ~963–1477.
- **Edge cases:** Clears to 0 when no valid fuel per lap; seeds from previous session can pre-populate window when available.

### LalaLaunch.Fuel.LapsRemainingInTank
- **Unit:** Laps
- **Meaning:** How many laps the current fuel load can cover using `Fuel.LiveFuelPerLap`.
- **Formula:** `currentFuel / LiveFuelPerLap` when `LiveFuelPerLap > 0`, else 0.【F:LalaLaunch.cs†L1598-L1667】
- **Inputs:** Telemetry `data.NewData.Fuel`; `LiveFuelPerLap`.
- **Gating / validity:** Only non-zero when `LiveFuelPerLap > 0`.
- **Clamping:** None beyond zero guard.
- **Update cadence:** 500 ms `UpdateLiveFuelCalcs` tick.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1598–1667.
- **Edge cases:** Resets to 0 when live fuel per lap unavailable.

### LalaLaunch.Fuel.LiveLapsRemainingInRace
- **Unit:** Laps
- **Meaning:** Projected race distance remaining (laps) using timed-race overrun logic.
- **Formula:** `ComputeProjectedLapsRemaining(simLapsRemaining, projectionLapSeconds, sessionTimeRemain, projectedDriveAfterZero)`; defaults to SimHub laps if projection invalid.【F:LalaLaunch.cs†L1608-L1637】【F:LalaLaunch.cs†L3185-L3201】
- **Inputs:** SimHub `IRacingExtraProperties.iRacing_LapsRemainingFloat`; telemetry lap times via `GetProjectionLapSeconds`; session time remaining; `FuelCalculator.StrategyDriverExtraSecondsAfterZero`.
- **Gating / validity:** Requires `LiveFuelPerLap > 0` to publish; falls back to sim estimate if projection returns 0.
- **Clamping:** None beyond fallback.
- **Update cadence:** 500 ms tick.
- **Code location:** `UpdateLiveFuelCalcs` and helper `ComputeProjectedLapsRemaining` lines ~1608–1637 and 3185–3201.
- **Edge cases:** Logging throttled when projection deviates >0.25 laps from sim; handles negative session time (after zero) by including observed overrun.

### LalaLaunch.Fuel.DeltaLaps
- **Unit:** Laps (positive = surplus, negative = deficit)
- **Meaning:** Surplus/deficit to finish at current burn.
- **Formula:** `DeltaLaps = LapsRemainingInTank - LiveLapsRemainingInRace`.【F:LalaLaunch.cs†L1639-L1644】
- **Inputs:** `LapsRemainingInTank`, `LiveLapsRemainingInRace`.
- **Gating / validity:** Only computed when `LiveFuelPerLap > 0`.
- **Clamping:** None.
- **Update cadence:** 500 ms tick.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1639–1644.

### LalaLaunch.Fuel.TargetFuelPerLap
- **Unit:** L/lap
- **Meaning:** Max-allowed burn to reach finish when fuel short.
- **Formula:** If `DeltaLaps < 0`: `raw = currentFuel / LiveLapsRemainingInRace`; clamp to ≥90% of `LiveFuelPerLap` (max 10% saving). Otherwise 0.【F:LalaLaunch.cs†L1646-L1664】
- **Inputs:** `currentFuel`, `LiveLapsRemainingInRace`, `LiveFuelPerLap`.
- **Gating / validity:** Only when deficit and valid fuel per lap.
- **Clamping:** Lower bound 0.9×`LiveFuelPerLap`.
- **Update cadence:** 500 ms tick.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1646–1664.

### LalaLaunch.Fuel.PushFuelPerLap / Fuel.FuelSavePerLap
- **Unit:** L/lap
- **Meaning:** Push = aggressive burn guidance; FuelSave = conservative burn using minima.
- **Formula (Push):** Use `_maxFuelPerLapSession` if ≥ `LiveFuelPerLap`; else `LiveFuelPerLap * 1.02`.【F:LalaLaunch.cs†L1752-L1762】
- **Formula (FuelSave):** Minimum of wet/dry rolling windows; if none, 97% of `LiveFuelPerLap`.【F:LalaLaunch.cs†L1688-L1703】
- **Inputs:** `_maxFuelPerLapSession`, `LiveFuelPerLap`, `_minDryFuelPerLap`, `_minWetFuelPerLap`, `FuelCalculator.IsWet`.
- **Gating / validity:** Require `LiveFuelPerLap > 0` for meaningful values.
- **Clamping:** Implicit via window min/max and non-negative guard.
- **Update cadence:** 500 ms tick.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1688–1703 and 1752–1762.

### LalaLaunch.Fuel.DeltaLapsIfPush
- **Unit:** Laps
- **Meaning:** Surplus/deficit if driving at push burn.
- **Formula:** `lapsRemainingIfPush = currentFuel / PushFuelPerLap`; `DeltaLapsIfPush = lapsRemainingIfPush - LiveLapsRemainingInRace`.【F:LalaLaunch.cs†L1764-L1773】
- **Inputs:** `currentFuel`, `PushFuelPerLap`, `LiveLapsRemainingInRace`.
- **Gating / validity:** Only when push fuel >0.
- **Clamping:** None beyond zero guards.
- **Update cadence:** 500 ms tick.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1752–1773.

### LalaLaunch.Fuel.CanAffordToPush
- **Unit:** Bool
- **Meaning:** True if `DeltaLapsIfPush >= 0` (no deficit when pushing).【F:LalaLaunch.cs†L1764-L1773】
- **Inputs:** `DeltaLapsIfPush`.
- **Gating / validity:** Requires push fuel >0.
- **Update cadence:** 500 ms tick.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1752–1773.

### LalaLaunch.Fuel.Confidence
- **Unit:** Percent (int)
- **Meaning:** Stability of the live fuel model.
- **Formula:** `ComputeFuelModelConfidence` uses window size, min/max spread and baseline sanity to assign score 0–100.【F:LalaLaunch.cs†L413-L439】【F:LalaLaunch.cs†L1470-L1477】
- **Inputs:** Rolling windows, baseline fuel, wet/dry mode.
- **Gating / validity:** 0 when using fallback SimHub estimator.
- **Update cadence:** Once per accepted lap; pushed to UI each tick.
- **Code location:** `ComputeFuelModelConfidence` and `UpdateLiveFuelCalcs` lines ~413–439 and 1470–1477.

## Pace inputs used by fuel projection

### LalaLaunch.Pace.StintAvgLapTimeSec / Pace.Last5LapAvgSec
- **Unit:** Seconds
- **Meaning:** Rolling pace metrics used for projection when available.
- **Formula:** `Pace.StintAvgLapTimeSec` = median-filtered average of clean laps; `Pace.Last5LapAvgSec` = mean of last up to 5 clean laps.【F:LalaLaunch.cs†L1220-L1305】
- **Inputs:** Clean lap times excluding pit/incident laps; leader deltas for logging.
- **Gating / validity:** Ignored early laps, pit laps, incident laps.【F:LalaLaunch.cs†L1208-L1260】【F:LalaLaunch.cs†L1334-L1363】
- **Update cadence:** On lap crossing.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1208–1305.

### LalaLaunch.Pace.LeaderAvgLapTimeSec / Pace.LeaderDeltaToPlayerSec
- **Unit:** Seconds
- **Meaning:** Leader pace used only for debug/logging of projection differences.
- **Formula:** Rolling average of recent leader laps read at each lap crossing; delta = leader avg − player avg pace.【F:LalaLaunch.cs†L1115-L1189】
- **Inputs:** Telemetry leader lap times via `ReadLeaderLapTimeSeconds` (multiple candidates).【F:LalaLaunch.cs†L3257-L3334】
- **Gating / validity:** Leader feed cleared if stale or zero; only computed on new laps.【F:LalaLaunch.cs†L1001-L1041】
- **Update cadence:** Lap crossing.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1001–1189; `ReadLeaderLapTimeSeconds` lines ~3257–3334.

## Pit and strategy properties

### LalaLaunch.Fuel.Pit.TotalNeededToEnd
- **Unit:** Litres
- **Meaning:** Total fuel required from now to finish at current burn rate.
- **Formula:** `fuelNeededToEnd = LiveLapsRemainingInRace * LiveFuelPerLap`.【F:LalaLaunch.cs†L1633-L1644】
- **Inputs:** `LiveLapsRemainingInRace`, `LiveFuelPerLap`.
- **Gating / validity:** Zero when `LiveFuelPerLap <= 0`.
- **Update cadence:** 500 ms tick.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1633–1644.

### LalaLaunch.Fuel.Pit.NeedToAdd
- **Unit:** Litres
- **Meaning:** Additional fuel required to reach finish.
- **Formula:** `max(0, Fuel.Pit.TotalNeededToEnd - currentFuel)`.【F:LalaLaunch.cs†L1666-L1677】
- **Inputs:** `currentFuel`, `Fuel.Pit.TotalNeededToEnd`.
- **Gating / validity:** Requires valid fuel per lap.
- **Update cadence:** 500 ms tick.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1666–1677.

### LalaLaunch.Fuel.Pit.TankSpaceAvailable
- **Unit:** Litres
- **Meaning:** Free capacity in tank respecting BoP and overrides.
- **Formula:** `max(0, maxTankCapacity - currentFuel)` where `maxTankCapacity = FuelCalculator.MaxFuelOverride` or telemetry `MaxFuel * DriverCarMaxFuelPct`.【F:LalaLaunch.cs†L1679-L1701】【F:LalaLaunch.cs†L2823-L2833】
- **Inputs:** `currentFuel`, `FuelCalculator.MaxFuelOverride`, telemetry `DataCorePlugin.GameData.MaxFuel`, `DriverInfo.DriverCarMaxFuelPct`.
- **Gating / validity:** Zero if no capacity info.
- **Update cadence:** 500 ms tick.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1679–1701; max detection lines ~2823–2833.

### LalaLaunch.Fuel.Pit.WillAdd
- **Unit:** Litres
- **Meaning:** Actual fuel volume expected to be added given MFD request and capacity.
- **Formula:** `requestedAddLitres` (converted from `PitSvFuel` gallons) clamped to `TankSpaceAvailable`. If refuel not selected, request is forced to 0.【F:LalaLaunch.cs†L1669-L1698】
- **Inputs:** Telemetry `DataCorePlugin.GameRawData.Telemetry.PitSvFuel`, `dpFuelFill` (refuel selected), unit conversion `DataCorePlugin.GameData.Units`. Tank space as above.
- **Gating / validity:** Zero when refuel not selected or no tank space.
- **Update cadence:** 500 ms tick.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1669–1698; `IsRefuelSelected` lines ~3044–3058.

### LalaLaunch.Fuel.Pit.FuelOnExit
- **Unit:** Litres
- **Meaning:** Expected fuel in tank after pit stop completes.
- **Formula:** `currentFuel + Fuel.Pit.WillAdd`.【F:LalaLaunch.cs†L1685-L1703】
- **Inputs:** `currentFuel`, `Fuel.Pit.WillAdd`.
- **Gating / validity:** Zero when invalid fuel per lap.
- **Update cadence:** 500 ms tick.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1685–1703.

### LalaLaunch.Fuel.Pit.DeltaAfterStop / Fuel.Pit.FuelSaveDeltaAfterStop / Fuel.Pit.PushDeltaAfterStop
- **Unit:** Laps (post-stop surplus/deficit)
- **Meaning:** Projected lap surplus at current, save, or push burn after executing the planned stop.
- **Formula:** `(Fuel.Pit.FuelOnExit / burnRate) - LiveLapsRemainingInRace`, where burnRate is `LiveFuelPerLap`, `FuelSaveFuelPerLap`, or `PushFuelPerLap` respectively.【F:LalaLaunch.cs†L1695-L1704】【F:LalaLaunch.cs†L1764-L1773】
- **Inputs:** `Fuel.Pit.FuelOnExit`, burn rates, `LiveLapsRemainingInRace`.
- **Gating / validity:** Zero when burn rate <=0 or no valid projection.
- **Update cadence:** 500 ms tick.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1695–1704 and 1764–1773.

### LalaLaunch.Fuel.Pit.StopsRequiredToEnd
- **Unit:** Count (int)
- **Meaning:** Planned number of remaining stops from FuelCalculator strategy or inferred from capacity.
- **Formula:** Use `FuelCalculator.RequiredPitStops`; if <=0 and `maxTankCapacity > 0`, compute `ceil((fuelNeededToEnd - currentFuel) / maxTankCapacity)` then clamp ≥0.【F:LalaLaunch.cs†L1706-L1721】
- **Inputs:** `FuelCalculator.RequiredPitStops`, `fuelNeededToEnd`, `currentFuel`, `maxTankCapacity`.
- **Gating / validity:** Zero when no valid inputs.
- **Update cadence:** 500 ms tick.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1706–1721.

### LalaLaunch.Fuel.IsPitWindowOpen / Fuel.PitWindowOpeningLap
- **Unit:** Bool / Lap number (int)
- **Meaning:** Whether a single-stop plan can pit now for the requested add; opening lap when not yet available.
- **Formula:** For single-stop strategy with refuel selected and valid burn: if `TankSpaceAvailable >= requestedAddLitres` then open and `PitWindowOpeningLap = currentLapNumber`; else compute `lapsToOpen = ceil((requestedAddLitres - tankSpace)/LiveFuelPerLap)` and opening lap = current lap + lapsToOpen.【F:LalaLaunch.cs†L1723-L1751】
- **Inputs:** `FuelCalculator.RequiredPitStops`, refuel selection, `requestedAddLitres`, `TankSpaceAvailable`, `LiveFuelPerLap`, completed lap count.
- **Gating / validity:** Only for strategies requiring exactly one stop and refuel selected; otherwise false/0.
- **Update cadence:** 500 ms tick.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1723–1751.

### LalaLaunch.Fuel.Live.RefuelRate_Lps
- **Unit:** L/s
- **Meaning:** Effective refuel rate considering profile/telemetry.
- **Formula:** Delegates to `FuelCalcs.EffectiveRefuelRateLps` (profile-configured or measured). No further math in `LalaLaunch.cs`.【F:LalaLaunch.cs†L2101-L2101】【F:FuelCalcs.cs†L2740-L2795】
- **Inputs:** Internal FuelCalcs state (measured refuel events via `_refuelStartFuel`/`_refuelLastFuel`).
- **Gating / validity:** 0 when unknown.
- **Update cadence:** Published each tick from latest FuelCalcs value.
- **Code location:** Attach in `LalaLaunch.cs` line ~2101; refuel detection lines ~2699–2801.

### LalaLaunch.Fuel.Live.TireChangeTime_S
- **Unit:** Seconds
- **Meaning:** Box time for tyre change if selected in MFD.
- **Formula:** `FuelCalculator.TireChangeTime` if any tyre change selected; else 0. Negative values clamped to 0.【F:LalaLaunch.cs†L3058-L3085】
- **Inputs:** Telemetry tire change flags (`dpLFTireChange`, `dpRFTireChange`, `dpLRTireChange`, `dpRRTireChange`), FuelCalcs tyre time.
- **Gating / validity:** Assumes tyre change selected when telemetry missing (returns true if no flags present).【F:LalaLaunch.cs†L3028-L3056】
- **Update cadence:** 500 ms tick.
- **Code location:** `GetEffectiveTireChangeTimeSeconds` lines ~3058–3085.

### LalaLaunch.Fuel.Live.PitLaneLoss_S
- **Unit:** Seconds
- **Meaning:** Latest pit-lane travel time loss (DTL) used for planning.
- **Formula:** Directly exposes `FuelCalculator.PitLaneTimeLoss` (computed by `PitEngine`).【F:LalaLaunch.cs†L2103-L2103】【F:PitEngine.cs†L1-L200】
- **Inputs:** PitEngine measurements (`LastTotalPitCycleTimeLoss` etc.).
- **Gating / validity:** 0 when not measured.
- **Update cadence:** 500 ms tick.
- **Code location:** Attach in `LalaLaunch.cs` line ~2103.

### LalaLaunch.Fuel.Live.TotalStopLoss
- **Unit:** Seconds
- **Meaning:** Expected total time loss for an upcoming stop including lane travel and box work (fuel vs tyres).
- **Formula:** `pitLaneLoss + max(fuelTime, tireTime)` where `fuelTime = WillAdd / refuelRate` when both >0; `tireTime` from `GetEffectiveTireChangeTimeSeconds`. Negative/NaN guarded to 0.【F:LalaLaunch.cs†L3094-L3130】
- **Inputs:** `FuelCalculator.PitLaneTimeLoss`, `Fuel.Pit.WillAdd`, `Fuel.Live.RefuelRate_Lps`, tyre selection.
- **Gating / validity:** Requires pit lane loss and refuel/tire selections; zeros otherwise.
- **Update cadence:** 500 ms tick.
- **Code location:** `CalculateTotalStopLossSeconds` lines ~3094–3130.

### LalaLaunch.Fuel.Live.DriveTimeAfterZero
- **Unit:** Seconds
- **Meaning:** Projected additional drive time once race clock hits zero, combining observed overrun, strategy projection, and lap-based fallback.
- **Formula:** `EstimateDriveTimeAfterZero(sessionTime, sessionTimeRemain, lapSeconds, strategyProjection, timerZeroSeen, timerZeroSessionTime)`; max of observed after-zero, strategy projection, and lap time as floor.【F:LalaLaunch.cs†L1604-L1620】【F:FuelProjectionMath.cs†L8-L41】
- **Inputs:** Session time/remaining telemetry, `GetProjectionLapSeconds`, `FuelCalculator.StrategyDriverExtraSecondsAfterZero`, internal timer-zero tracking.
- **Gating / validity:** Uses 0 when telemetry missing.
- **Update cadence:** 500 ms tick.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1604–1620; helper in `FuelProjectionMath.cs` lines ~8–41.

### LalaLaunch.Fuel.Live.ProjectedDriveSecondsRemaining
- **Unit:** Seconds
- **Meaning:** Wall-clock drive time expected to finish, including overrun beyond zero.
- **Formula:** From `FuelProjectionMath.ProjectLapsRemaining`, returned `projectedSecondsRemaining = max(sessionTimeRemain,0) + max(driveTimeAfterZero,0)`.【F:LalaLaunch.cs†L1618-L1637】【F:FuelProjectionMath.cs†L23-L49】
- **Inputs:** Session time remaining telemetry, `Fuel.Live.DriveTimeAfterZero`, lap pace.
- **Gating / validity:** 0 when projection invalid.
- **Update cadence:** 500 ms tick.
- **Code location:** `ComputeProjectedLapsRemaining` lines ~3185–3201; `FuelProjectionMath` lines ~23–49.

### LalaLaunch.Fuel.Pit.Tank capacity (LiveCarMaxFuel / MaxFuelOverride)
- **Unit:** Litres
- **Meaning:** Effective tank size used for all pit math and window logic.
- **Formula:** `LiveCarMaxFuel = GameData.MaxFuel * DriverCarMaxFuelPct`; overrides applied via `FuelCalculator.MaxFuelOverride`/suggestions. Updates pushed to FuelCalculator display when changed.【F:LalaLaunch.cs†L2823-L2833】【F:LalaLaunch.cs†L2521-L2531】
- **Inputs:** Telemetry `DataCorePlugin.GameData.MaxFuel`, `DriverInfo.DriverCarMaxFuelPct`; FuelCalcs overrides.
- **Update cadence:** 500 ms tick.
- **Code location:** `DataUpdate` loop lines ~2819–2833; UI update lines ~2521–2531.

### LalaLaunch.Fuel.LastPitLaneTravelTime
- **Unit:** Seconds
- **Meaning:** Last measured direct pit-lane travel (no stop) time from PitEngine; exposed for dashboards.
- **Formula:** Delegated from `_pit.LastDirectTravelTime` captured after out-lap completes in PitEngine state machine.【F:LalaLaunch.cs†L1149-L1189】【F:LalaLaunch.cs†L2301-L2301】
- **Inputs:** PitEngine lap crossing timings (`FinalizePaceDeltaCalculation`).
- **Update cadence:** On pit cycle completion.
- **Code location:** `UpdateLiveFuelCalcs` lines ~1043–1189; attached at line ~2301.

## Findings / Suspected Issues

- Refuel selection defaults to true when telemetry fields are missing, which may overestimate `Fuel.Pit.WillAdd` on sims that omit `dpFuelFill`. Follow-up: confirm desired default per sim and possibly guard with session type check.【F:LalaLaunch.cs†L3044-L3058】
- Leader pace is cleared when feed drops, but delta-based projection logging might still reflect older `Pace_StintAvgLapTimeSec` values. Consider marking projection confidence accordingly.【F:LalaLaunch.cs†L1001-L1041】【F:LalaLaunch.cs†L1608-L1637】
