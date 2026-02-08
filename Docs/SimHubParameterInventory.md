# SimHub Parameter Inventory

**CANONICAL CONTRACT**

Validated against: 9d77f4a  
Last reviewed: 2026-02-10  
Last updated: 2026-02-10  
Branch: work

- All exports are attached in `LalaLaunch.cs` during `Init()` via `AttachCore`/`AttachVerbose`. Core values are refreshed in `DataUpdate` (500 ms poll for fuel/pace/pit via `_poll500ms`; per-tick for launch/dash/messaging). Verbose rows require `SimhubPublish.VERBOSE`.【F:LalaLaunch.cs†L2644-L3120】【F:LalaLaunch.cs†L3411-L3775】
- Legacy spreadsheet removed; this file is canonical.
- “Defined in” lists the class/method that computes the value before `AttachCore/AttachVerbose` publishes it.

## Fuel
| Exported name | Type | Units / meaning | Update cadence | Defined in |
| --- | --- | --- | --- | --- |
| Fuel.LiveFuelPerLap | double | Rolling average burn per accepted lap (wet/dry windows, rejects pit/warmup/off-track/outliers). | 500 ms poll (`UpdateLiveFuelCalcs`). | `LalaLaunch.cs` — `UpdateLiveFuelCalcs` + `AttachCore`【F:LalaLaunch.cs†L1895-L2143】【F:LalaLaunch.cs†L2672-L2955】 |
| Fuel.LiveFuelPerLap_Stable / StableSource / StableConfidence | double/string/double | Smoothed burn chosen from live/profile (deadband hold; confidence aligned to source). | 500 ms poll. | `LalaLaunch.cs` — `UpdateStableFuelPerLap` + `AttachCore`【F:LalaLaunch.cs†L4180-L4254】【F:LalaLaunch.cs†L2672-L2955】 |
| Surface.TrackWetness | int | Raw iRacing track wetness (0–3/4 depending on telemetry); informational only. | 500 ms poll. | `LalaLaunch.cs` — `ReadTrackWetness` + `AttachCore`【F:LalaLaunch.cs†L1402-L1426】【F:LalaLaunch.cs†L6095-L6134】【F:LalaLaunch.cs†L2996-L3003】 |
| Surface.TrackWetnessLabel | string | Human label for track wetness (“Dry/Damp/Light Wet/Mod Wet/Very Wet/Unknown”). | 500 ms poll. | `LalaLaunch.cs` — `MapWetnessLabel` + `AttachCore`【F:LalaLaunch.cs†L1402-L1426】【F:LalaLaunch.cs†L6115-L6134】【F:LalaLaunch.cs†L2996-L3003】 |
| Fuel.FuelReadyConfidenceThreshold | double | Confidence threshold gating fuel readiness and pit window. | 500 ms poll. | `LalaLaunch.cs` — `GetFuelReadyConfidenceThreshold` + `AttachCore`【F:LalaLaunch.cs†L2672-L2955】 |
| Fuel.LiveLapsRemainingInRace / _S | double | Projected laps remaining using stable burn & lap time with EMA-smoothed string variant. | 500 ms poll. | `LalaLaunch.cs` — `ComputeProjectedLapsRemaining`, `UpdateSmoothedFuelOutputs`, `AttachCore`【F:LalaLaunch.cs†L1895-L2143】【F:LalaLaunch.cs†L4243-L4306】【F:LalaLaunch.cs†L2676-L2690】 |
| Fuel.LiveLapsRemainingInRace_Stable / _Stable_S | double | Explicit stable mirror of the projection (for dash/debug). | 500 ms poll. | `LalaLaunch.cs` — `UpdateSmoothedFuelOutputs` + `AttachCore`【F:LalaLaunch.cs†L4243-L4306】【F:LalaLaunch.cs†L2676-L2690】 |
| Fuel.DeltaLaps | double | Lap surplus/deficit vs. projected distance at stable burn. | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs` + `AttachCore`【F:LalaLaunch.cs†L1895-L2143】【F:LalaLaunch.cs†L2676-L2690】 |
| Fuel.TargetFuelPerLap | double | Required burn to finish when short (floored to 10% saving). | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs` + `AttachCore`【F:LalaLaunch.cs†L1995-L2054】【F:LalaLaunch.cs†L2681-L2684】 |
| Fuel.LapsRemainingInTank | double | Tank fuel ÷ stable (or live) burn. | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs` + `AttachCore`【F:LalaLaunch.cs†L1895-L1967】【F:LalaLaunch.cs†L2687-L2690】 |
| Fuel.Confidence | int | Fuel-model confidence from accepted window size/quality. | 500 ms poll. | `LalaLaunch.cs` — `ComputeFuelModelConfidence` + `AttachCore`【F:LalaLaunch.cs†L1830-L1890】【F:LalaLaunch.cs†L2688-L2691】 |
| Fuel.PushFuelPerLap / Fuel.FuelSavePerLap | double | Push = max session burn or +2%; Save = min window burn or 97% fallback. | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs` + `AttachCore`【F:LalaLaunch.cs†L2005-L2143】【F:LalaLaunch.cs†L2690-L2694】 |
| Fuel.StintBurnTarget / Fuel.StintBurnTargetBand | double/string | Current-tank per-lap burn target plus band label (SAVE/PUSH/HOLD/OKAY) based on pit-in reserve percentage. | 500 ms poll. | `LalaLaunch.cs` — stint target block + `AttachCore`【F:LalaLaunch.cs†L2371-L2449】【F:LalaLaunch.cs†L3017-L3021】 |
| Fuel.FuelBurnPredictor / Fuel.FuelBurnPredictorSource | double/string | Predictor burn: stable burn until 5 valid laps, then rolling 3-lap average. Source labels: SIMHUB/PLUGIN/STINT/AVG3. | 500 ms poll. | `LalaLaunch.cs` — predictor outputs + `AttachCore`【F:LalaLaunch.cs†L5364-L5427】【F:LalaLaunch.cs†L3030-L3033】 |
| Fuel.DeltaLapsIfPush / Fuel.CanAffordToPush | double/bool | Surplus/deficit if driving at push burn and affordability flag. | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs` + `AttachCore`【F:LalaLaunch.cs†L2005-L2143】【F:LalaLaunch.cs†L2690-L2694】 |
| Fuel.Delta.LitresCurrent / Plan / WillAdd | double | Liter delta to finish for current fuel, MFD request, and clamped add at stable burn. | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs` + `AttachCore`【F:LalaLaunch.cs†L2145-L2195】【F:LalaLaunch.cs†L2694-L2702】 |
| Fuel.Delta.LitresCurrentPush / PlanPush / WillAddPush | double | Same deltas assuming push burn. | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs` + `AttachCore`【F:LalaLaunch.cs†L2145-L2210】【F:LalaLaunch.cs†L2697-L2702】 |
| Fuel.Delta.LitresCurrentSave / PlanSave / WillAddSave | double | Same deltas assuming fuel-save burn. | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs` + `AttachCore`【F:LalaLaunch.cs†L2145-L2210】【F:LalaLaunch.cs†L2699-L2702】 |
| Fuel.Pit.TotalNeededToEnd / _S | double | Liters required to finish at stable burn (smoothed `_S` via EMA). | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs`, `UpdateSmoothedFuelOutputs`, `AttachCore`【F:LalaLaunch.cs†L1895-L2143】【F:LalaLaunch.cs†L4243-L4306】【F:LalaLaunch.cs†L2703-L2710】 |
| Fuel.Pit.NeedToAdd | double | Shortfall vs. required liters. | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs` + `AttachCore`【F:LalaLaunch.cs†L1895-L2143】【F:LalaLaunch.cs†L2704-L2706】 |
| Fuel.Pit.TankSpaceAvailable | double | Remaining capacity after BoP/override clamp. | 500 ms poll. | `LalaLaunch.cs` — `ResolveMaxTankCapacity`, `UpdateLiveFuelCalcs`, `AttachCore`【F:LalaLaunch.cs†L1895-L2143】【F:LalaLaunch.cs†L2705-L2708】 |
| Fuel.Pit.WillAdd | double | Requested add clamped to tank space. | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs` + `AttachCore`【F:LalaLaunch.cs†L1895-L2143】【F:LalaLaunch.cs†L2706-L2708】 |
| Fuel.Pit.FuelOnExit | double | Current fuel + clamped add. | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs` + `AttachCore`【F:LalaLaunch.cs†L1895-L2143】【F:LalaLaunch.cs†L2712-L2716】 |
| Fuel.Pit.DeltaAfterStop / _S | double | Lap surplus after the planned stop at stable burn (`_S` smoothed). | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs`, `UpdateSmoothedFuelOutputs`, `AttachCore`【F:LalaLaunch.cs†L1895-L2143】【F:LalaLaunch.cs†L2711-L2714】【F:LalaLaunch.cs†L4243-L4306】 |
| Fuel.Pit.FuelSaveDeltaAfterStop / _S | double | Surplus after stop using fuel-save burn (`_S` smoothed). | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs`, `UpdateSmoothedFuelOutputs`, `AttachCore`【F:LalaLaunch.cs†L1895-L2143】【F:LalaLaunch.cs†L2710-L2712】【F:LalaLaunch.cs†L4243-L4306】 |
| Fuel.Pit.PushDeltaAfterStop / _S | double | Surplus after stop assuming push burn (`_S` smoothed). | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs`, `UpdateSmoothedFuelOutputs`, `AttachCore`【F:LalaLaunch.cs†L1895-L2143】【F:LalaLaunch.cs†L2711-L2713】【F:LalaLaunch.cs†L4243-L4306】 |
| Fuel.PitStopsRequiredByFuel / Fuel.PitStopsRequiredByPlan / Fuel.Pit.StopsRequiredToEnd | int | Capacity-based stop count, strategy-required stops, and published stop requirement (plan-first). | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs` + `AttachCore`【F:LalaLaunch.cs†L1895-L2143】【F:LalaLaunch.cs†L2714-L2718】 |
| Fuel.Live.RefuelRate_Lps | double | Learned refuel rate EMA for current profile. | 500 ms poll. | `LalaLaunch.cs` — refuel learning in `UpdateLiveFuelCalcs` + `AttachCore`【F:LalaLaunch.cs†L3489-L3510】【F:LalaLaunch.cs†L2717-L2720】 |
| Fuel.Live.TireChangeTime_S | double | Estimated tyre change seconds for current selection. | 500 ms poll. | `LalaLaunch.cs` — `GetEffectiveTireChangeTimeSeconds` + `AttachCore`【F:LalaLaunch.cs†L1911-L1950】【F:LalaLaunch.cs†L2717-L2720】 |
| Fuel.Live.PitLaneLoss_S | double | Pit lane loss from profile/learned value used by strategy. | 500 ms poll. | `LalaLaunch.cs` — `_pit.LastTotalPitCycleTimeLoss` persistence + `AttachCore`【F:LalaLaunch.cs†L1408-L1462】【F:LalaLaunch.cs†L2717-L2721】 |
| Fuel.Live.TotalStopLoss | double | Pit lane loss + service time composite for strategy displays. | 500 ms poll. | `LalaLaunch.cs` — `CalculateTotalStopLossSeconds` + `AttachCore`【F:LalaLaunch.cs†L1911-L1950】【F:LalaLaunch.cs†L2719-L2722】 |
| Fuel.Live.DriveTimeAfterZero | double | Extra drive seconds after timer zero (planner/live observed). | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs` after-zero model + `AttachCore`【F:LalaLaunch.cs†L1911-L1993】【F:LalaLaunch.cs†L2721-L2724】 |
| Fuel.After0.PlannerSeconds / Fuel.After0.LiveEstimateSeconds / Fuel.After0.Source | double/double/string | Planner-configured after-zero allowance, live observed estimate, and source label (“planner”/“live”). | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs` + `AttachCore`【F:LalaLaunch.cs†L1911-L1993】【F:LalaLaunch.cs†L2722-L2726】 |
| Fuel.Live.ProjectedDriveSecondsRemaining | double | Projected wall-time remaining (includes after-zero). | 500 ms poll. | `LalaLaunch.cs` — `ComputeProjectedLapsRemaining` + `AttachCore`【F:LalaLaunch.cs†L4396-L4404】【F:LalaLaunch.cs†L2726-L2729】 |
| Fuel.ProjectionLapTime_Stable / _StableSource | double/string | Selected projection lap time and provenance (stint/last5/profile/fallback). | 500 ms poll. | `LalaLaunch.cs` — `GetProjectionLapSeconds` + `AttachCore`【F:LalaLaunch.cs†L4306-L4394】【F:LalaLaunch.cs†L2725-L2727】 |
| Fuel.Live.IsFuelReady | bool | True when stable confidence meets the readiness threshold. | 500 ms poll. | `LalaLaunch.cs` — `IsFuelReady` property + `AttachCore`【F:LalaLaunch.cs†L500-L506】【F:LalaLaunch.cs†L2727-L2729】 |
| Fuel.PitWindowState / Fuel.PitWindowLabel / Fuel.IsPitWindowOpen / Fuel.PitWindowOpeningLap / Fuel.PitWindowClosingLap | int/string/bool/int/int | Pit window state machine (open ECO/STD/PUSH, SET FUEL, NO DATA YET, N/A, TANK SPACE/ERROR). Opening/closing lap markers mirror the state machine. | 500 ms poll. | `LalaLaunch.cs` — `UpdateLiveFuelCalcs` pit window block + `AttachCore`【F:LalaLaunch.cs†L2145-L2335】【F:LalaLaunch.cs†L2682-L2687】 |
| Fuel.LastPitLaneTravelTime | double | Last direct pit-lane travel time saved from pit cycle. | On save; surfaced each poll. | `LalaLaunch.cs` — `Pit_OnValidPitStopTimeLossCalculated` + `AttachCore`【F:LalaLaunch.cs†L2950-L3004】【F:LalaLaunch.cs†L2672-L2955】 |

## Pace / Projection
| Exported name | Type | Units / meaning | Update cadence | Defined in |
| --- | --- | --- | --- | --- |
| Pace.StintAvgLapTimeSec | double | Rolling average of accepted clean laps. | Per lap (on crossing). | `LalaLaunch.cs` — `DetectLapCrossing` & pace acceptance + `AttachCore`【F:LalaLaunch.cs†L1080-L1405】【F:LalaLaunch.cs†L2732-L2737】 |
| Pace.Last5LapAvgSec | double | Average of last up-to-five clean laps. | Per lap. | `LalaLaunch.cs` — pace window update + `AttachCore`【F:LalaLaunch.cs†L1080-L1405】【F:LalaLaunch.cs†L2732-L2737】 |
| Pace.LeaderAvgLapTimeSec | double | Rolling leader pace (cleared when feed drops). | Per lap. | `LalaLaunch.cs` — leader lap processing + `AttachCore`【F:LalaLaunch.cs†L1336-L1375】【F:LalaLaunch.cs†L2733-L2736】 |
| Pace.LeaderDeltaToPlayerSec | double | Leader pace minus player pace. | Per lap. | `LalaLaunch.cs` — `UpdateLeaderDelta` + `AttachCore`【F:LalaLaunch.cs†L1080-L1405】【F:LalaLaunch.cs†L2734-L2737】 |
| Pace.PaceConfidence | int | Confidence derived from clean pace window size/outlier rejection. | Per lap. | `LalaLaunch.cs` — `ComputePaceConfidence` + `AttachCore`【F:LalaLaunch.cs†L1080-L1405】【F:LalaLaunch.cs†L2735-L2737】 |
| Pace.OverallConfidence | int | Combined fuel/pace confidence (probabilistic product). | Per lap / poll. | `LalaLaunch.cs` — `OverallConfidence` getter + `AttachCore`【F:LalaLaunch.cs†L468-L491】【F:LalaLaunch.cs†L2736-L2737】 |
| Pace.PacePredictor / Pace.PacePredictorSource | double/string | Predictor lap time: projection lap time until 5 clean laps, then rolling 3-lap average. Source labels: SIMHUB/PLUGIN/STINT/AVG5/AVG3. | Per lap / poll. | `LalaLaunch.cs` — predictor outputs + `AttachCore`【F:LalaLaunch.cs†L5364-L5427】【F:LalaLaunch.cs†L3073-L3081】 |

## Opponents & Pit Exit
| Exported name | Type | Units / meaning | Update cadence | Defined in |
| --- | --- | --- | --- | --- |
| Opp.Ahead1.Name / CarNumber / ClassColor | string | Identity of the closest class car ahead (slot bound by iRacing extras; identity = ClassColor:CarNumber). Empty before Race lap ≥2 gate. | Per tick (Race only, lap gate ≥2). | `Opponents.cs` — nearby slot ingestion + `AttachCore`【F:Opponents.cs†L42-L344】【F:LalaLaunch.cs†L3079-L3093】 |
| Opp.Ahead1.GapToPlayerSec | double | Gap seconds to player from IRacing relative (stored absolute). | Per tick (Race only, lap gate ≥2). | `Opponents.cs` — slot read + `AttachCore`【F:Opponents.cs†L268-L317】【F:LalaLaunch.cs†L3079-L3093】 |
| Opp.Ahead1.BlendedPaceSec | double | Blended pace (0.70×recent avg + 0.30×best×1.01) for ahead car; NaN when invalid. | Per tick (Race only, lap gate ≥2). | `Opponents.cs` — entity cache + `AttachCore`【F:Opponents.cs†L627-L717】【F:LalaLaunch.cs†L3079-L3093】 |
| Opp.Ahead1.PaceDeltaSecPerLap | double | Closing rate vs player pace (opponent − mine; positive means you are faster by that margin). NaN when no pace. | Per tick (Race only, lap gate ≥2). | `Opponents.cs` — fight computation + `AttachCore`【F:Opponents.cs†L268-L317】【F:LalaLaunch.cs†L3079-L3093】 |
| Opp.Ahead1.LapsToFight | double | Laps to reach/fight the ahead car; requires gap >0 and closingRate >0.05 s/lap, else NaN. | Per tick (Race only, lap gate ≥2). | `Opponents.cs` — fight computation + `AttachCore`【F:Opponents.cs†L268-L317】【F:LalaLaunch.cs†L3079-L3093】 |
| Opp.Ahead2.* | string/double | Same fields as Ahead1 for the second car ahead in class. | Per tick (Race only, lap gate ≥2). | `Opponents.cs` — slot 2 ahead + `AttachCore`【F:Opponents.cs†L252-L344】【F:LalaLaunch.cs†L3079-L3094】 |
| Opp.Behind1.* | string/double | Same fields for closest car behind in class. PaceDeltaSecPerLap = (mine − opponent). LapsToFight published only when closingRate >0.05 s/lap. | Per tick (Race only, lap gate ≥2). | `Opponents.cs` — slot behind + `AttachCore`【F:Opponents.cs†L252-L344】【F:LalaLaunch.cs†L3095-L3102】 |
| Opp.Behind2.* | string/double | Same fields for second car behind in class. | Per tick (Race only, lap gate ≥2). | `Opponents.cs` — slot behind + `AttachCore`【F:Opponents.cs†L252-L344】【F:LalaLaunch.cs†L3103-L3109】 |
| Opp.Leader.BlendedPaceSec / Opp.P2.BlendedPaceSec | double | Blended pace for class leader / P2 from leaderboard rows (NaN if unavailable or gated). | Per tick (Race only, lap gate ≥2). | `Opponents.cs` — leaderboard pace + `AttachCore`【F:Opponents.cs†L395-L444】【F:LalaLaunch.cs†L3111-L3113】 |
| Opponents_SummaryAhead / Opponents_SummaryBehind | string | Concise summaries for cars ahead/behind (e.g., `Ahead:  A1 #25 +0.6s Δ-0.12s/L LTF=5 | A2 #11 +1.4s Δ-0.05s/L LTF=—`). Uses `—` for missing data; single-line. | Per tick (Race only, lap gate ≥2). | `Opponents.cs` — summary builder + `AttachCore`【F:Opponents.cs†L78-L155】【F:LalaLaunch.cs†L3111-L3116】 |
| Opponents_SummaryAhead1/2 / Opponents_SummaryBehind1/2 | string | Per-slot summaries for A1/A2/B1/B2 (e.g., `A1 #25 +0.6s Δ-0.12s/L LTF=5` or `A2 —` when empty). | Per tick (Race only, lap gate ≥2). | `Opponents.cs` — summary builder + `AttachCore`【F:Opponents.cs†L78-L155】【F:LalaLaunch.cs†L3112-L3116】 |
| PitExit.Valid | bool | True when pit-exit predictor has a player row + leaderboard data while gated (Race, lap ≥2). | Per tick on pit road; per lap-quarter off pit road; skipped when session time remaining ≤120 s (if available). | `Opponents.cs` — pit-exit predictor + `AttachCore`【F:Opponents.cs†L507-L742】【F:LalaLaunch.cs†L3115-L3118】 |
| PitExit.PredictedPositionInClass | int | Predicted class position after a pit stop using validated pitLossSec (1 + cars predicted ahead). | Per tick on pit road; per lap-quarter off pit road; skipped when session time remaining ≤120 s (if available). | `Opponents.cs` — pit-exit predictor + `AttachCore`【F:Opponents.cs†L507-L742】【F:LalaLaunch.cs†L3115-L3118】 |
| PitExit.CarsAheadAfterPitCount | int | Count of same-class connected cars expected to be ahead after serving pit loss. | Per tick on pit road; per lap-quarter off pit road; skipped when session time remaining ≤120 s (if available). | `Opponents.cs` — pit-exit predictor + `AttachCore`【F:Opponents.cs†L532-L742】【F:LalaLaunch.cs†L3115-L3118】 |
| PitExit.Summary | string | Human summary of predicted post-stop position (`PitExit: P# after stop (ahead=X, loss=Ys)`). | Per tick on pit road; per lap-quarter off pit road; skipped when session time remaining ≤120 s (if available). | `Opponents.cs` — pit-exit predictor + `AttachCore`【F:Opponents.cs†L550-L742】【F:LalaLaunch.cs†L3115-L3118】 |
| PitExit.Ahead.Name / CarNumber / ClassColor | string | Identity of the nearest same-class connected car predicted ahead after the stop (closest negative delta). Empty when none. | Per tick on pit road; per lap-quarter off pit road; skipped when session time remaining ≤120 s (if available). | `Opponents.cs` — pit-exit predictor scan + `AttachCore`【F:Opponents.cs†L532-L742】【F:LalaLaunch.cs†L3126-L3133】 |
| PitExit.Ahead.GapSec | double | Absolute seconds to the nearest predicted-ahead car after pit loss (from delta vs. player predicted gap). 0 when none. | Per tick on pit road; per lap-quarter off pit road; skipped when session time remaining ≤120 s (if available). | `Opponents.cs` — pit-exit predictor scan + `AttachCore`【F:Opponents.cs†L532-L742】【F:LalaLaunch.cs†L3126-L3133】 |
| PitExit.Behind.Name / CarNumber / ClassColor | string | Identity of the nearest same-class connected car predicted behind after the stop (closest positive delta). Empty when none. | Per tick on pit road; per lap-quarter off pit road; skipped when session time remaining ≤120 s (if available). | `Opponents.cs` — pit-exit predictor scan + `AttachCore`【F:Opponents.cs†L532-L742】【F:LalaLaunch.cs†L3130-L3137】 |
| PitExit.Behind.GapSec | double | Absolute seconds to the nearest predicted-behind car after pit loss (from delta vs. player predicted gap). 0 when none. | Per tick on pit road; per lap-quarter off pit road; skipped when session time remaining ≤120 s (if available). | `Opponents.cs` — pit-exit predictor scan + `AttachCore`【F:Opponents.cs†L532-L742】【F:LalaLaunch.cs†L3130-L3137】 |

## CarSA (Car System)
| Exported name | Type | Units / meaning | Update cadence | Defined in |
| --- | --- | --- | --- | --- |
| Car.Valid | bool | True when CarSA has a valid player index and CarIdx lap pct truth for the tick. | Per tick. | `CarSAEngine.cs` + `AttachCore`【F:CarSAEngine.cs†L74-L233】【F:LalaLaunch.cs†L3414-L3418】 |
| Car.Source | string | Source label (`CarIdxTruth`). | Per tick. | `CarSAEngine.cs` + `AttachCore`【F:CarSAEngine.cs†L74-L87】【F:LalaLaunch.cs†L3414-L3418】 |
| Car.SlotsAhead / Car.SlotsBehind | int | Slot counts (5/5). | Per tick. | `CarSAEngine.cs` + `AttachCore`【F:CarSAEngine.cs†L7-L11】【F:LalaLaunch.cs†L3414-L3418】 |
| Car.iRatingSOF | double | Strength-of-field iRating average across active CarSA slots. | Per tick. | `CarSAEngine.cs` + `AttachCore`【F:CarSAEngine.cs†L270-L304】【F:LalaLaunch.cs†L3551-L3556】 |
| Car.Ahead01P.Gap.Sec / Car.Behind01P.Gap.Sec | double | Precision gap for slot 1 derived from gate-gap truth/filtered (fallback to track gap), mapped ahead/behind. | Per tick. | `CarSAEngine.cs` + `AttachCore`【F:CarSAEngine.cs†L653-L1490】【F:LalaLaunch.cs†L3553-L3556】 |
| Car.Player.* | mixed | Player slot outputs: CarIdx, ClassName, ClassColor, ClassColorHex, IRating, Licence, SafetyRating, LicLevel, TeamID, LapsSincePit, Status, StatusE, StatusShort, StatusLong, StatusEReason. | Per tick. | `CarSAEngine.cs` + `AttachCore`【F:CarSAEngine.cs†L248-L709】【F:LalaLaunch.cs†L3475-L3512】 |
| Car.Player.PaceFlagsRaw / SessionFlagsRaw / TrackSurfaceMaterialRaw | int | Raw telemetry flags for the player CarIdx row (or -1 if missing). Populated only when soft debug + raw-telemetry mode are enabled. | Per tick. | `LalaLaunch.cs` raw telemetry debug + `AttachCore`【F:LalaLaunch.cs†L3557-L3560】【F:LalaLaunch.cs†L4874-L4917】 |
| Car.Ahead01..Ahead05.* | mixed | Slot outputs for nearest ahead cars: CarIdx, Name, CarNumber, ClassColor, ClassColorHex, ClassName, CarClassShortName, Initials, AbbrevName, LicLevel, UserID, TeamID, PositionInClass, IRating, Licence, SafetyRating, IsOnTrack, IsOnPitRoad, IsValid, IsTalking (false when not transmitting), TalkRadioIdx (-1 default), TalkFrequencyIdx (-1 default), TalkFrequencyName (empty when unknown), LapDelta, LapsSincePit, BestLapTimeSec, LastLapTimeSec, BestLap, BestLapIsEstimated, LastLap, DeltaBestSec, DeltaBest, EstLapTimeSec, EstLapTime, HotScore, HotVia, Gap.TrackSec (distance-based proximity), Gap.RelativeSec (gate-gap v2 filtered proximity; may be NaN), Gap.RelativeSource (0 invalid, 1 filtered, 2 truth, 3 track fallback, 4 sticky hold), InfoVisibility, Info, ClosingRateSecPerSec (player-centric; positive = closing), Status, StatusE, StatusShort, StatusLong, StatusEReason, StatusBgHex, BorderMode, BorderHex, SessionFlagsRaw, TrackSurfaceMaterialRaw. | Per tick; distance gaps derived from car-centric LapDistPct deltas. | `CarSAEngine.cs` slot update + `AttachCore`【F:CarSAEngine.cs†L248-L709】【F:LalaLaunch.cs†L3419-L3508】 |
| Car.Behind01..Behind05.* | mixed | Slot outputs for nearest behind cars: CarIdx, Name, CarNumber, ClassColor, ClassColorHex, ClassName, CarClassShortName, Initials, AbbrevName, LicLevel, UserID, TeamID, PositionInClass, IRating, Licence, SafetyRating, IsOnTrack, IsOnPitRoad, IsValid, IsTalking (false when not transmitting), TalkRadioIdx (-1 default), TalkFrequencyIdx (-1 default), TalkFrequencyName (empty when unknown), LapDelta, LapsSincePit, BestLapTimeSec, LastLapTimeSec, BestLap, BestLapIsEstimated, LastLap, DeltaBestSec, DeltaBest, EstLapTimeSec, EstLapTime, HotScore, HotVia, Gap.TrackSec (distance-based proximity), Gap.RelativeSec (gate-gap v2 filtered proximity; may be NaN), Gap.RelativeSource (0 invalid, 1 filtered, 2 truth, 3 track fallback, 4 sticky hold), InfoVisibility, Info, ClosingRateSecPerSec (player-centric; positive = closing), Status, StatusE, StatusShort, StatusLong, StatusEReason, StatusBgHex, BorderMode, BorderHex, SessionFlagsRaw, TrackSurfaceMaterialRaw. | Per tick; distance gaps derived from car-centric LapDistPct deltas. | `CarSAEngine.cs` slot update + `AttachCore`【F:CarSAEngine.cs†L248-L709】【F:LalaLaunch.cs†L3526-L3587】 |
| Car.Debug.* | mixed | Debug telemetry for player/slot identity, raw telemetry availability, and slot filtering: `PlayerCarIdx`, `PlayerLapPct`, `PlayerLap`, `SessionTimeSec`, `SourceFastPathUsed`, `HasCarIdxPaceFlags`, `HasCarIdxSessionFlags`, `HasCarIdxTrackSurfaceMaterial`, `RawTelemetryReadMode`, `RawTelemetryFailReason`, `Ahead01.CarIdx`, `Ahead01.ForwardDistPct`, `Behind01.CarIdx`, `Behind01.BackwardDistPct`, `InvalidLapPctCount`, `OnPitRoadCount`, `OnTrackCount`, `TimestampUpdatesThisTick`, `FilteredHalfLapCountAhead`, `FilteredHalfLapCountBehind`, `LapTimeEstimateSec`, `LapTimeUsedSec`, `HysteresisReplacementsThisTick`, `SlotCarIdxChangedThisTick`. | Per tick. | `CarSAEngine.cs` debug fields + `AttachCore`【F:CarSAEngine.cs†L85-L283】【F:LalaLaunch.cs†L3482-L3510】 |


## Radio
| Exported name | Type | Units / meaning | Update cadence | Defined in |
| --- | --- | --- | --- | --- |
| Radio.TransmitClassPosLabel | string | Class position label for the currently transmitting car, formatted as `P{pos} {class}` when available (empty when no transmitter or identity missing). | Per tick. | `LalaLaunch.cs` transmit caching + `AttachCore`【F:LalaLaunch.cs†L3551-L3557】【F:LalaLaunch.cs†L6612-L6756】 |

**CarSA style helper exports (30 total)**
- `Car.Ahead01.StatusBgHex`, `Car.Ahead01.BorderMode`, `Car.Ahead01.BorderHex`
- `Car.Ahead02.StatusBgHex`, `Car.Ahead02.BorderMode`, `Car.Ahead02.BorderHex`
- `Car.Ahead03.StatusBgHex`, `Car.Ahead03.BorderMode`, `Car.Ahead03.BorderHex`
- `Car.Ahead04.StatusBgHex`, `Car.Ahead04.BorderMode`, `Car.Ahead04.BorderHex`
- `Car.Ahead05.StatusBgHex`, `Car.Ahead05.BorderMode`, `Car.Ahead05.BorderHex`
- `Car.Behind01.StatusBgHex`, `Car.Behind01.BorderMode`, `Car.Behind01.BorderHex`
- `Car.Behind02.StatusBgHex`, `Car.Behind02.BorderMode`, `Car.Behind02.BorderHex`
- `Car.Behind03.StatusBgHex`, `Car.Behind03.BorderMode`, `Car.Behind03.BorderHex`
- `Car.Behind04.StatusBgHex`, `Car.Behind04.BorderMode`, `Car.Behind04.BorderHex`
- `Car.Behind05.StatusBgHex`, `Car.Behind05.BorderMode`, `Car.Behind05.BorderHex`

**Notes**
- Hot/Cool intent updates on a coarse 6-sector cadence (10 mini-sectors each), while FAST!/SLOW! conflict detection still evaluates on the 60 mini-sector updates for responsiveness.【F:CarSAEngine.cs†L1502-L1787】
- CarSA style helpers now publish `StatusBgHex`, `BorderMode`, and `BorderHex` for Ahead/Behind slots. `StatusBgHex` is keyed by `StatusE` from settings (except `FasterClass`/`SlowerClass`, which use the slot `ClassColorHex`), and border resolution uses priority TEAM > LEAD > OCLS > DEF with colors from settings.
- Gate-gap v2 publishes `Gap.RelativeSec` using mini-sector gate timing with prediction and sticky hold, keeping directionally correct proximity even during wraps; it falls back to `Gap.TrackSec` when no gate data exists.
- `Gap.RelativeSource` indicates which input fed the relative gap: filtered (1), truth (2), track fallback (3), sticky hold (4), or invalid (0). Slot01 precision gaps mirror the same source priority but bypass sticky-hold unless a valid gate/track source exists.【F:CarSAEngine.cs†L1336-L1490】
- `InfoVisibility`/`Info` expose rotating slot info (last-lap delta, delta-best, laps-since-pit) with a 5 s cadence and gate delays; visibility is suppressed outside Unknown/Racing states and when the slot is invalid.【F:CarSAEngine.cs†L1488-L1637】

## Pit timing and PitLite
| Exported name | Type | Units / meaning | Update cadence | Defined in |
| --- | --- | --- | --- | --- |
| Pit.LastDirectTravelTime / Pit.LastTotalPitCycleTimeLoss / Pit.LastPaceDeltaNetLoss | double | Latest pit lane direct time, total DTL, and pace-delta net loss from PitEngine. | On pit cycle completion (latched) and surfaced each poll. | `LalaLaunch.cs` — pit finalization + `AttachCore`【F:LalaLaunch.cs†L1408-L1469】【F:LalaLaunch.cs†L2743-L2746】 |
| PitLite.Live.TimeOnPitRoadSec / TimeInBoxSec | double | Live timers during a pit stop (lane and box). | Per tick while pitting. | `LalaLaunch.cs` — PitEngine timers + `AttachCore`【F:LalaLaunch.cs†L1408-L1469】【F:LalaLaunch.cs†L2791-L2795】 |
| PitLite.TotalLossSec / TotalLossPlusBoxSec | double | PitLite preferred loss (DTL/direct) and loss+box composite latched on out-lap. | On pit-lite publication, surfaced each poll. | `PitCycleLite.cs` publish + `AttachCore`【F:PitCycleLite.cs†L170-L208】【F:LalaLaunch.cs†L2796-L2800】 |
| PitLite.Status | string | PitLite status enum (AwaitingPitLap/OutLapComplete/etc.). | Per tick; **verbose**. | `PitCycleLite.cs` state + `AttachVerbose`【F:PitCycleLite.cs†L122-L217】【F:LalaLaunch.cs†L2790-L2793】 |
| PitLite.CurrentLapType / LastLapType | string | PitLite lap classification (Normal/Pit/Out/etc.). | Per tick; **verbose**. | `PitCycleLite.cs` + `AttachVerbose`【F:PitCycleLite.cs†L122-L217】【F:LalaLaunch.cs†L2793-L2797】 |
| PitLite.LossSource | string | Whether DTL or direct loss was published. | Per publication; **verbose**. | `PitCycleLite.cs` + `AttachVerbose`【F:PitCycleLite.cs†L170-L217】【F:LalaLaunch.cs†L2796-L2799】 |
| PitLite.LastSaved.Sec / LastSaved.Source | double/string | Most recent saved pit-lite candidate and provenance. | On save; **verbose**. | `PitCycleLite.cs` + `AttachVerbose`【F:PitCycleLite.cs†L170-L217】【F:LalaLaunch.cs†L2797-L2799】 |
| PitLite.Live.SeenEntryThisLap / SeenExitThisLap | bool | Entry/exit edges observed this lap. | Per tick; **verbose**. | `PitCycleLite.cs` + `AttachVerbose`【F:PitCycleLite.cs†L122-L163】【F:LalaLaunch.cs†L2801-L2805】 |
| Pit.EntryAssistActive | bool | True while pit entry assist is armed (EnteringPits or limiter ON with overspeed >2 kph) and distance is within the 0–500 m window. | Per tick. | `PitEngine.UpdatePitEntryAssist` + `AttachCore`【F:PitEngine.cs†L240-L343】【F:LalaLaunch.cs†L2751-L2760】 |
| Pit.EntryDistanceToLine_m | double | Distance to pit entry guidance line (raw distance minus profile buffer). Clamped 0–500 m; assist resets when raw distance ≥500 m. | Per tick when armed. | `PitEngine.UpdatePitEntryAssist` + `AttachCore`【F:PitEngine.cs†L353-L417】【F:LalaLaunch.cs†L2751-L2754】 |
| Pit.EntryRequiredDistance_m | double | Constant-decel distance needed to reach pit speed at the guidance line using the profiled decel. | Per tick when armed. | `PitEngine.UpdatePitEntryAssist` + `AttachCore`【F:PitEngine.cs†L397-L418】【F:LalaLaunch.cs†L2752-L2754】 |
| Pit.EntryMargin_m | double | Guidance distance remaining minus required distance; positive = early, negative = late. Primary dash signal. | Per tick when armed. | `PitEngine.UpdatePitEntryAssist` + `AttachCore`【F:PitEngine.cs†L405-L417】【F:LalaLaunch.cs†L2752-L2755】 |
| Pit.EntryCue | int | Pit entry cue level (0 OFF, 1 OK, 2 BRAKE SOON, 3 BRAKE NOW, 4 LATE) derived from margin vs. buffer. | Per tick when armed. | `PitEngine.UpdatePitEntryAssist` + `AttachCore`【F:PitEngine.cs†L334-L339】【F:LalaLaunch.cs†L2754-L2756】 |
| Pit.EntryCueText | string | Dash-friendly cue text (“OFF/OK/BRAKE SOON/BRAKE NOW/LATE”). | Per tick when armed. | `PitEngine.PitEntryCueText` + `AttachCore`【F:PitEngine.cs†L19-L38】【F:LalaLaunch.cs†L2755-L2757】 |
| Pit.EntrySpeedDelta_kph | double | Current speed minus pit speed limit (session pit limit, fallback to iRacing extra). | Per tick when armed. | `PitEngine.UpdatePitEntryAssist` + `AttachCore`【F:PitEngine.cs†L251-L276】【F:LalaLaunch.cs†L2756-L2758】 |
| Pit.EntryDecelProfile_mps2 | double | Profile-configured pit entry decel used for the constant-decel model (clamped to 5–25 m/s²). | Per tick when armed. | `PitEngine.UpdatePitEntryAssist` + `AttachCore`【F:PitEngine.cs†L240-L259】【F:LalaLaunch.cs†L2757-L2759】 |
| Pit.EntryBuffer_m | double | Profile-configured buffer distance used for cue thresholds (clamped to 0–50 m). | Per tick when armed. | `PitEngine.UpdatePitEntryAssist` + `AttachCore`【F:PitEngine.cs†L240-L259】【F:LalaLaunch.cs†L2758-L2760】 |
| Pit.EntryLineDebrief | string | Debrief status at the pit entry line (`safe`, `normal`, `bad`) based on line speed delta and buffer window. | Latched on pit entry line. | `PitEngine.UpdatePitEntryAssist` + `AttachCore`【F:PitEngine.cs†L447-L497】【F:LalaLaunch.cs†L3045-L3048】 |
| Pit.EntryLineDebriefText | string | Plain-English debrief message for the pit entry line outcome (includes time loss vs. pit limit when available). | Latched on pit entry line. | `PitEngine.UpdatePitEntryAssist` + `AttachCore`【F:PitEngine.cs†L452-L507】【F:LalaLaunch.cs†L3045-L3049】 |
| Pit.EntryLineTimeLoss_s | double | Time loss in seconds versus pit limit from the first compliant point to the pit entry line (0 if not computed). | Latched on pit entry line. | `PitEngine.UpdatePitEntryAssist` + `AttachCore`【F:PitEngine.cs†L452-L510】【F:LalaLaunch.cs†L3045-L3049】 |
| PitExit.DistanceM | int | Forward distance in metres to the stored pit exit marker (wraps at S/F). Returns 0 if data missing. | Per tick. | `LalaLaunch.cs` — `UpdatePitExitDisplayValues` + `AttachCore`【F:LalaLaunch.cs†L4216-L4248】【F:LalaLaunch.cs†L3068-L3069】 |
| PitExit.TimeS | int | Estimated time in seconds to pit exit using current speed (0 if speed too small or data missing). | Per tick. | `LalaLaunch.cs` — `UpdatePitExitDisplayValues` + `AttachCore`【F:LalaLaunch.cs†L4216-L4248】【F:LalaLaunch.cs†L3068-L3069】 |

## Launch
| Exported name | Type | Units / meaning | Update cadence | Defined in |
| --- | --- | --- | --- | --- |
| ManualTimeoutRemaining | string | Seconds remaining on 30 s manual prime timeout (empty when inactive). | Per tick during launch states. | `LalaLaunch.cs` — `AttachCore` timeout block【F:LalaLaunch.cs†L2845-L2860】 |
| LaunchModeActive | bool | Whether launch UI is visible (primed/in-progress/logging/completed). | Per tick. | `LalaLaunch.cs` — launch state machine + `AttachCore`【F:LalaLaunch.cs†L2470-L2510】【F:LalaLaunch.cs†L2869-L2878】 |
| LaunchStateLabel / LaunchStateCode | string/string | Human-readable and numeric launch state tokens. | Per tick. | `LalaLaunch.cs` — launch state machine + `AttachCore`【F:LalaLaunch.cs†L2470-L2510】【F:LalaLaunch.cs†L2872-L2878】 |
| LaunchRPM / TargetLaunchRPM | double | Live and target RPMs for launch. | Per tick. | `LalaLaunch.cs` — launch metrics + `AttachCore`【F:LalaLaunch.cs†L2845-L2895】 |
| ActualRPMAtClutchRelease / RPMDeviationAtClutchRelease | string/string | RPM at clutch release and deviation vs. target. | Per launch event; surfaced per tick. | `LalaLaunch.cs` — launch metrics + `AttachCore`【F:LalaLaunch.cs†L2845-L2895】 |
| ActualThrottleAtClutchRelease / ThrottleDeviationAtClutchRelease | double/double | Throttle at release and deviation vs. target. | Per launch event; surfaced per tick. | `LalaLaunch.cs` — launch metrics + `AttachCore`【F:LalaLaunch.cs†L2845-L2895】 |
| TargetLaunchThrottle / OptimalThrottleTolerance | string/string | Target throttle % and tolerance from profile. | Per tick. | `LalaLaunch.cs` — profile-backed metrics + `AttachCore`【F:LalaLaunch.cs†L2845-L2895】 |
| OptimalBitePoint / OptimalBitePointTolerance | double/double | Profile bite point target/tolerance. | Per tick. | `LalaLaunch.cs` — profile metrics + `AttachCore`【F:LalaLaunch.cs†L2845-L2895】 |
| AvgSessionLaunchRPM / LastAvgLaunchRPM / LastLaunchRPM / LastMinRPM / MinRPM | string/double/double/double/double | Session launch RPM aggregates and last-run snapshots. | Per tick. | `LalaLaunch.cs` — launch metrics + `AttachCore`【F:LalaLaunch.cs†L2845-L2895】 |
| MaxTractionLoss | double | Max % traction loss during launch. | Per launch run; surfaced per tick. | `LalaLaunch.cs` — launch metrics + `AttachCore`【F:LalaLaunch.cs†L2845-L2895】 |
| ReactionTime / ClutchReleaseTime / ClutchReleaseDelta | double/string/string | Launch reaction/phase timing measurements. | Per launch run; surfaced per tick. | `LalaLaunch.cs` — launch metrics + `AttachCore`【F:LalaLaunch.cs†L2845-L2895】 |
| BitePointInTargetRange / RPMInTargetRange / ThrottleInTargetRange | bool | True when the respective control stayed within tolerance during launch. | Per launch run; surfaced per tick. | `LalaLaunch.cs` — launch metrics + `AttachCore`【F:LalaLaunch.cs†L2845-L2895】 |
| WheelSpinDetected / BoggedDown / BogDownFactorPercent | bool/bool/double | Spin/bog detection flags and profile bog factor. | Per tick. | `LalaLaunch.cs` — launch metrics + `AttachCore`【F:LalaLaunch.cs†L2845-L2895】 |
| AntiStallActive / AntiStallDetectedInLaunch | bool | Anti-stall detection live flag and launch-run latch. | Per tick. | `LalaLaunch.cs` — anti-stall logic + `AttachCore`【F:LalaLaunch.cs†L2845-L2895】 |
| TargetLaunchThrottle | string | Target throttle % from profile. | Per tick. | `LalaLaunch.cs` — profile metrics + `AttachCore`【F:LalaLaunch.cs†L2845-L2895】 |
| ThrottleModulationDelta | double | % modulation measurement during launch. | Per launch run; surfaced per tick. | `LalaLaunch.cs` — launch metrics + `AttachCore`【F:LalaLaunch.cs†L2845-L2895】 |
| ZeroTo100Time / ZeroTo100Delta | double | 0–100 km/h time and delta vs. baseline. | Per launch run. | `LalaLaunch.cs` — launch metrics + `AttachCore`【F:LalaLaunch.cs†L2845-L2895】 |
| FalseStartDetected | bool | Flag set when car moves before green while clutch released. | Per tick. | `LalaLaunch.cs` — false start detection + `AttachCore`【F:LalaLaunch.cs†L4989-L5015】【F:LalaLaunch.cs†L2807-L2811】 |

## Messages & Rejoin
| Exported name | Type | Units / meaning | Update cadence | Defined in |
| --- | --- | --- | --- | --- |
| MSG.OvertakeApproachLine | double | Relative line metric for approaching traffic. | Per tick. | `LalaLaunch.cs` — `_msgSystem` outputs + `AttachCore`【F:LalaLaunch.cs†L2899-L2940】 |
| MSG.OtherClassBehindGap | double | Seconds behind for the selected different-class car (or -1 when none); no `MSGOtherClassBehindGap` alias. | Per tick. | `LalaLaunch.cs` — `_msgSystem` outputs + `AttachCore`【F:LalaLaunch.cs†L2899-L2940】 |
| MSG.OvertakeWarnSeconds | double | Approach buffer seconds to warn (from profile). | Per tick. | `LalaLaunch.cs` — profile read + `AttachCore`【F:LalaLaunch.cs†L2899-L2940】 |
| MSG.MsgCxTimeMessage / MsgCxStateMessage / MsgCxActionMessage | string | Message text for time/state/action lanes. | Per tick. | `LalaLaunch.cs` — `_msgSystem` outputs + `AttachCore`【F:LalaLaunch.cs†L2899-L2940】 |
| MSG.MsgCxTimeVisible / MsgCxStateVisible | bool | Visibility flags for respective lanes. | Per tick. | `LalaLaunch.cs` — `_msgSystem` outputs + `AttachCore`【F:LalaLaunch.cs†L2899-L2940】 |
| MSG.MsgCxTimeSilenceRemaining | double | Remaining silence window for time lane. | Per tick. | `LalaLaunch.cs` — `_msgSystem` outputs + `AttachCore`【F:LalaLaunch.cs†L2899-L2940】 |
| MSG.MsgCxStateToken | string | Token controlling state lane reappearance. | Per tick. | `LalaLaunch.cs` — `_msgSystem` outputs + `AttachCore`【F:LalaLaunch.cs†L2899-L2940】 |
| MSG.MsgCxActionPulse | bool | One-shot trigger when action lane fires. | Per tick. | `LalaLaunch.cs` — `_msgSystem` outputs + `AttachCore`【F:LalaLaunch.cs†L2899-L2940】 |
| MSGV1.ActiveText_Lala / ActivePriority_Lala / ActiveMsgId_Lala / ActiveText_Msg / ActivePriority_Msg / ActiveMsgId_Msg | string | Active message payloads for Lala and Msg lanes. | Per tick while MSGV1 engine active. | `LalaLaunch.cs` — `_msgV1Engine.Outputs` + `AttachCore`【F:LalaLaunch.cs†L2919-L2940】【F:Messaging/MessageEngine.cs†L478-L560】 |
| MSGV1.ActiveTextColor_* / ActiveBgColor_* / ActiveOutlineColor_* / ActiveFontSize_* | string/int | Visual styling for active messages. | Per tick. | `Messaging/MessageEngine.cs` outputs + `AttachCore`【F:Messaging/MessageEngine.cs†L478-L560】【F:LalaLaunch.cs†L2919-L2940】 |
| MSGV1.ActiveCount | int | Count of active queued messages. | Per tick. | `Messaging/MessageEngine.cs` outputs + `AttachCore`【F:Messaging/MessageEngine.cs†L478-L560】【F:LalaLaunch.cs†L2919-L2940】 |
| MSGV1.LastCancelMsgId | string | ID of last cancelled message. | Per tick. | `Messaging/MessageEngine.cs` outputs + `AttachCore`【F:Messaging/MessageEngine.cs†L478-L560】【F:LalaLaunch.cs†L2919-L2940】 |
| MSGV1.ClearAllPulse | bool | One-shot clear-all pulse. | Per tick. | `Messaging/MessageEngine.cs` outputs + `AttachCore`【F:Messaging/MessageEngine.cs†L478-L560】【F:LalaLaunch.cs†L2919-L2940】 |
| MSGV1.StackCsv | string | CSV of active message IDs/priorities for debugging. | Per tick. | `Messaging/MessageEngine.cs` outputs + `AttachCore`【F:Messaging/MessageEngine.cs†L478-L560】【F:LalaLaunch.cs†L2919-L2940】 |
| MSGV1.MissingEvaluatorsCsv | string | CSV of evaluator IDs missing definitions. | On detection; surfaced per tick. | `Messaging/MessageEngine.cs` — `RegisterMissingEvaluators` + `AttachCore`【F:Messaging/MessageEngine.cs†L478-L560】 |
| RejoinAlertReasonCode / RejoinAlertReasonName / RejoinAlertMessage | int/string/string | Active rejoin logic code/name/message. | Per tick. | `LalaLaunch.cs` — `_rejoinEngine` outputs + `AttachCore`【F:LalaLaunch.cs†L2818-L2824】 |
| RejoinIsExitingPits | bool | Whether rejoin assist thinks you are exiting pits. | Per tick. | `RejoinAssistEngine` state + `AttachCore`【F:LalaLaunch.cs†L2819-L2823】【F:RejoinAssistEngine.cs†L90-L160】 |
| RejoinCurrentPitPhase / RejoinCurrentPitPhaseName | int/string | Current pit phase enum and label. | Per tick. | `RejoinAssistEngine` state + `AttachCore`【F:LalaLaunch.cs†L2819-L2825】【F:RejoinAssistEngine.cs†L90-L160】 |
| RejoinThreatLevel / RejoinThreatLevelName / RejoinTimeToThreat | int/string/double | Threat scoring and time-to-threat for rejoin assist. | Per tick. | `RejoinAssistEngine` outputs + `AttachCore`【F:LalaLaunch.cs†L2826-L2831】【F:RejoinAssistEngine.cs†L540-L640】 |
| MsgCxPressed | bool | Latched true for 500 ms after MsgCx action. | Per tick. | `LalaLaunch.cs` — `RegisterMsgCxPress` + `AttachCore`【F:LalaLaunch.cs†L2475-L2479】【F:LalaLaunch.cs†L2815-L2820】 |
| Debug.EventMarkerPressed | bool | Latched true for 500 ms after the Event Marker action. | Per tick. | `LalaLaunch.cs` — `RegisterEventMarkerPress` + `AttachCore`【F:LalaLaunch.cs†L2481-L2492】【F:LalaLaunch.cs†L2816-L2822】 |

## Session / Identity
| Exported name | Type | Units / meaning | Update cadence | Defined in |
| --- | --- | --- | --- | --- |
| Reset.LastSession / Reset.ThisSession | string | Last and current session tokens (`SessionID:SubSessionID`). | On session change; surfaced each poll. | `LalaLaunch.cs` — session token handling + `AttachCore`【F:LalaLaunch.cs†L3308-L3365】【F:LalaLaunch.cs†L2737-L2740】 |
| Reset.ThisSessionType | string | Cached session type used for finish timing. | On session change. | `LalaLaunch.cs` — finish timing reset + `AttachCore`【F:LalaLaunch.cs†L4566-L4715】【F:LalaLaunch.cs†L2737-L2740】 |
| LastSessionType | string | Last observed session type token (raw). | Per tick. | `LalaLaunch.cs` — DataUpdate session probe + `AttachCore`【F:LalaLaunch.cs†L3201-L3274】【F:LalaLaunch.cs†L2808-L2811】 |
| Race.OverallLeaderHasFinished / Race.ClassLeaderHasFinished / Race.LeaderHasFinished | bool | Leader-finished latches (overall/class plus derived). | Per tick; updated when flags/heuristics trip. | `LalaLaunch.cs` — `UpdateFinishTiming` + `AttachCore`【F:LalaLaunch.cs†L4566-L4790】【F:LalaLaunch.cs†L2809-L2816】 |
| Race.OverallLeaderHasFinishedValid / Race.ClassLeaderHasFinishedValid | bool | Validity flags for leader-finished values. | Per tick. | `LalaLaunch.cs` — `UpdateFinishTiming` + `AttachCore`【F:LalaLaunch.cs†L4566-L4790】【F:LalaLaunch.cs†L2809-L2816】 |

## Dash control & visibility
| Exported name | Type | Units / meaning | Update cadence | Defined in |
| --- | --- | --- | --- | --- |
| CurrentDashPage | string | Current dash page set by `ScreenManager`. | Per tick. | `LalaLaunch.cs` — `Screens` state + `AttachCore`【F:LalaLaunch.cs†L2805-L2810】【F:LalaLaunch.cs†L3688-L3730】 |
| DashControlMode | string | Dash control mode (“manual”/“auto”). | Per tick. | `LalaLaunch.cs` — `Screens.Mode` + `AttachCore`【F:LalaLaunch.cs†L2805-L2810】【F:LalaLaunch.cs†L3688-L3730】 |
| PitScreenActive | bool | Whether pit screen is currently shown. | Per tick. | `LalaLaunch.cs` — pit screen state + `AttachCore`【F:LalaLaunch.cs†L3732-L3878】【F:LalaLaunch.cs†L3158-L3162】 |
| PitScreenMode | string | Pit screen mode (`auto` or `manual`). | Per tick. | `LalaLaunch.cs` — pit screen state + `AttachCore`【F:LalaLaunch.cs†L3837-L3878】【F:LalaLaunch.cs†L3158-L3162】 |
| LalaDashShowLaunchScreen / LalaDashShowPitLimiter / LalaDashShowPitScreen / LalaDashShowRejoinAssist / LalaDashShowVerboseMessaging / LalaDashShowRaceFlags / LalaDashShowRadioMessages / LalaDashShowTraffic | bool | User visibility toggles for Lala dash. | Per tick. | `LaunchPluginSettings` persisted values + `AttachCore`【F:LalaLaunch.cs†L3177-L3185】 |
| MsgDashShowLaunchScreen / MsgDashShowPitLimiter / MsgDashShowPitScreen / MsgDashShowRejoinAssist / MsgDashShowVerboseMessaging / MsgDashShowRaceFlags / MsgDashShowRadioMessages / MsgDashShowTraffic | bool | User visibility toggles for messaging dash. | Per tick. | `LaunchPluginSettings` persisted values + `AttachCore`【F:LalaLaunch.cs†L3187-L3195】 |
| OverlayDashShowLaunchScreen / OverlayDashShowPitLimiter / OverlayDashShowPitScreen / OverlayDashShowRejoinAssist / OverlayDashShowVerboseMessaging / OverlayDashShowRaceFlags / OverlayDashShowRadioMessages / OverlayDashShowTraffic | bool | User visibility toggles for overlay dash. | Per tick. | `LaunchPluginSettings` persisted values + `AttachCore`【F:LalaLaunch.cs†L3197-L3205】 |

## Debug (verbose-only)
| Exported name | Type | Units / meaning | Update cadence | Defined in |
| --- | --- | --- | --- | --- |
| Pit.Debug.TimeOnPitRoad / Pit.Debug.LastPitStopDuration | double | Live lane timer and last stationary duration. | Per tick (pits); **verbose**. | `PitEngine` timers + `AttachVerbose`【F:PitEngine.cs†L80-L239】【F:LalaLaunch.cs†L2745-L2749】 |
| Lala.Pit.AvgPaceUsedSec / AvgPaceSource | double/string | Baseline pace fed into pit delta and its source. | Per pit delta calc; **verbose**. | `PitEngine` pace selection + `AttachVerbose`【F:LalaLaunch.cs†L1336-L1415】【F:LalaLaunch.cs†L2751-L2755】 |
| Lala.Pit.Raw.PitLapSec / Raw.DTLFormulaSec | double | Reconstructed pit lap and formula DTL components. | On pit delta publish; **verbose**. | `LalaLaunch.cs` pit finalize + `AttachVerbose`【F:LalaLaunch.cs†L1336-L1415】【F:LalaLaunch.cs†L2753-L2756】 |
| Lala.Pit.InLapSec / OutLapSec / DeltaInSec / DeltaOutSec | double | Latched in/out lap times and deltas vs. baseline. | On pit lap/out-lap; **verbose**. | `PitEngine` finalize + `AttachVerbose`【F:LalaLaunch.cs†L1336-L1415】【F:LalaLaunch.cs†L2755-L2759】 |
| Lala.Pit.DriveThroughLossSec / DirectTravelSec / StopSeconds / ServiceStopLossSec | double | Drive-through loss, direct lane time, stop duration, and floored service loss. | On pit delta calc; **verbose**. | `PitEngine` + `AttachVerbose`【F:PitEngine.cs†L80-L239】【F:LalaLaunch.cs†L2759-L2766】 |
| Lala.Pit.Profile.PitLaneLossSec | double | Saved profile pit-lane loss. | On save; **verbose**. | `LalaLaunch.cs` pit save + `AttachVerbose`【F:LalaLaunch.cs†L2950-L3028】【F:LalaLaunch.cs†L2765-L2775】 |
| Lala.Pit.CandidateSavedSec / CandidateSource | double/string | Last candidate pit loss and its provenance. | On save; **verbose**. | `LalaLaunch.cs` pit save + `AttachVerbose`【F:LalaLaunch.cs†L1336-L1415】【F:LalaLaunch.cs†L2777-L2780】 |
| PitLite.InLapSec / OutLapSec / DeltaInSec / DeltaOutSec / TimePitLaneSec / TimePitBoxSec / DirectSec / DTLSec | double | PitLite lap/timer breakdowns. | Per tick/out-lap; **verbose**. | `PitCycleLite.cs` + `AttachVerbose`【F:PitCycleLite.cs†L122-L217】【F:LalaLaunch.cs†L2781-L2790】 |
| PitLite.LossSource | string | Whether DTL or direct was published. | Per publish; **verbose**. | `PitCycleLite.cs` + `AttachVerbose`【F:PitCycleLite.cs†L170-L217】【F:LalaLaunch.cs†L2796-L2799】 |
| PitLite.LastSaved.Sec / LastSaved.Source | double/string | Last saved pit-lite candidate/time source. | On save; **verbose**. | `PitCycleLite.cs` + `AttachVerbose`【F:PitCycleLite.cs†L170-L217】【F:LalaLaunch.cs†L2797-L2799】 |

## CSV exports (debug)
* `OffTrackDebug_<Track>_<Timestamp>.csv` includes an `EventFired` column immediately after `SessionTimeSec`, populated with `1`/`0` based on the event marker pulse state.【F:LalaLaunch.cs†L5278-L5338】【F:LalaLaunch.cs†L6212-L6218】
* `CarSA_Debug_YYYY-MM-DD_HH-mm-ss_<TrackName>.csv` includes an `EventFired` column immediately after `SessionTimeSec`, populated with `1`/`0` based on the event marker pulse state.【F:LalaLaunch.cs†L5189-L5222】【F:LalaLaunch.cs†L6220-L6242】
