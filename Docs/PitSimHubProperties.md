# Pit-related SimHub Properties

The plugin exposes lane timers, pit-cycle losses, pit-lite telemetry, rejoin phase state, and fuel/pit-window helpers. Core parameters are always published; verbose parameters require `SimhubPublish.VERBOSE`.

## Time-loss & pit timers (core)
| Property | Source / calculation | Intended use |
| --- | --- | --- |
| `Pit.LastDirectTravelTime` | PitEngine limiter-to-limiter travel captured at pit exit (`TimeOnPitRoad - PitStopDuration`, clamped 0–300 s). | Driver-facing raw lane time; also fuels consumption model. |
| `Pit.LastTotalPitCycleTimeLoss` | PitEngine DTL on lap completion: `(pitLap - stop + outLap) - 2*avgPace`, floored at 0. | Driver-facing overall pit cycle delta. |
| `Pit.LastPaceDeltaNetLoss` | PitEngine DTL minus stopped time. | Secondary driver metric / validation. |
| `PitLite.TotalLossSec` | PitCycleLite publishes preferred loss (DTL when valid; otherwise direct lane loss). | Single pit-loss output for dashes. |
| `PitLite.TotalLossPlusBoxSec` | `TotalLossSec + TimePitBoxSec`. | Total pit delta including stopped time. |
| `PitLite.Live.TimeOnPitRoadSec` | Passthrough of PitEngine running lane timer. | Live pit-lane timer during stop. |
| `PitLite.Live.TimeInBoxSec` | Passthrough of PitEngine running box timer. | Live stationary timer. |
| `RejoinIsExitingPits` / `RejoinCurrentPitPhase(Name)` | Current pit phase from PitEngine. | Dash overlays / rejoin warnings. |

## Fuel & pit-window helpers (core)
| Property | Source / calculation | Intended use |
| --- | --- | --- |
| `Fuel.Pit.TotalNeededToEnd` / `_S` | `LiveLapsRemainingInRace × LiveFuelPerLap` (string version is formatted). | Driver strategy: total liters required. |
| `Fuel.Pit.NeedToAdd` | `max(0, TotalNeededToEnd - currentFuel)`. | Strategy add needed. |
| `Fuel.Pit.TankSpaceAvailable` | `max(0, capacity - currentFuel)` using BoP/override. | Capacity check. |
| `Fuel.Pit.WillAdd` | Telemetry MFD refuel request clamped to `TankSpaceAvailable` (0 if refuel not selected). | Strategy validation. |
| `Fuel.Pit.FuelOnExit` | `currentFuel + WillAdd`. | Post-stop projection. |
| `Fuel.Pit.DeltaAfterStop` / `_S` | Lap surplus after stop at current burn. | Finish viability check. |
| `Fuel.Pit.FuelSaveDeltaAfterStop` / `_S` | Lap surplus using fuel-save burn. | Conservative projection. |
| `Fuel.Pit.PushDeltaAfterStop` / `_S` | Lap surplus using push burn. | Aggressive projection. |
| `Fuel.PitStopsRequiredByFuel` / `Fuel.PitStopsRequiredByPlan` | Capacity-inferred vs. strategy-requested stop counts. | Strategy debugging. |
| `Fuel.Pit.StopsRequiredToEnd` | Final required stops (plan or capacity). | Driver strategy. |
| `Fuel.Live.TotalStopLoss` | Pit lane loss plus concurrent service time. | Strategy timing. |
| `Fuel.Live.RefuelRate_Lps` | Effective refuel rate used in stop timing. | Strategy timing / validation. |
| `Fuel.Live.TireChangeTime_S` | Time to change tyres if selected. | Strategy timing / validation. |
| `Fuel.Live.PitLaneLoss_S` | Lane loss used by fuel/strategy calculators. | Strategy timing / validation. |

## Verbose pit/loss diagnostics
| Property | Source / calculation | Intended use |
| --- | --- | --- |
| `Pit.Debug.TimeOnPitRoad` | PitEngine `TimeOnPitRoad.TotalSeconds`. | Debug timer. |
| `Pit.Debug.LastPitStopDuration` | PitEngine `PitStopElapsedSec`. | Debug timer. |
| `Lala.Pit.AvgPaceUsedSec` / `AvgPaceSource` | Baseline pace used for DTL + source label. | Debug. |
| `Lala.Pit.Raw.PitLapSec` / `Raw.DTLFormulaSec` | Captured pit lap and raw DTL before flooring. | Debug. |
| `Lala.Pit.InLapSec` / `OutLapSec` / `DeltaInSec` / `DeltaOutSec` | Stored in/out laps and deltas vs. baseline. | Debug. |
| `Lala.Pit.DriveThroughLossSec` | Mirrors final DTL. | Debug. |
| `Lala.Pit.DirectTravelSec` | Mirrors direct lane time. | Debug. |
| `Lala.Pit.StopSeconds` | Stationary time. | Debug. |
| `Lala.Pit.ServiceStopLossSec` | `LastTotalPitCycleTimeLoss + stop`, floored. | Debug/service analysis. |
| `Lala.Pit.Profile.PitLaneLossSec` | Active profile’s saved pit lane loss. | Validation. |
| `Lala.Pit.CandidateSavedSec` / `CandidateSource` | Last saved pit-loss candidate + provenance. | Debug. |

## PitLite verbose telemetry
| Property | Source / calculation | Intended use |
| --- | --- | --- |
| `PitLite.InLapSec` / `OutLapSec` | Latched pit-lite lap times. | Debug/analysis. |
| `PitLite.DeltaInSec` / `DeltaOutSec` | Pit-lite deltas vs. average pace. | Debug/analysis. |
| `PitLite.TimePitLaneSec` / `TimePitBoxSec` | Latched timers from PitEngine. | Debug/analysis. |
| `PitLite.DirectSec` | `TimePitLaneSec - TimePitBoxSec`, floored at 0. | Debug/analysis. |
| `PitLite.DTLSec` | Pit-lite computed DTL. | Debug/analysis. |
| `PitLite.Status` / `CurrentLapType` / `LastLapType` | Pit-lite status and lap classifications. | Debug/analysis. |
| `PitLite.LossSource` | Which loss was published (`dtl`/`direct`). | Debug/analysis. |
| `PitLite.LastSaved.Sec` / `LastSaved.Source` | Last saved candidate seconds and source. | Debug/analysis. |
| `PitLite.Live.SeenEntryThisLap` / `SeenExitThisLap` | Entry/exit edge flags for current lap. | Debug/analysis. |

## Driver vs. debug guidance
- **Driver-facing:** Core pit loss/timer outputs, pit phase flags, and fuel/pit-window helpers listed in the core tables above.
- **Debug/validation:** All `Pit.Debug.*`, `Lala.Pit.*`, and `PitLite.*` verbose properties are intended for diagnostics and can be hidden from dashboards unless troubleshooting.
