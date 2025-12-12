# Pit-related SimHub Properties

The plugin exposes a mix of pit-lane timing, loss calculations, PitLite telemetry, rejoin phase state, and fuel/pit window helpers. Properties are grouped by their SimHub channel (Core vs. Verbose) and marked for expected use.

## Time-loss & pit timers
| Property | Channel | Source/Calculation | Intended use |
| --- | --- | --- | --- |
| `Pit.LastDirectTravelTime` | Core | PitEngine direct lane travel computed at pit exit: `TimeOnPitRoad - PitStopDuration`, clamped to 0–300s and latched on exit. | Driver-facing: raw limiter-to-limiter travel time (drive-through style). |
| `Pit.LastTotalPitCycleTimeLoss` | Core | PitEngine DTL formula on lap completion: `(pitLap - stop + outLap) - 2*avgPace`, floored at 0. | Driver-facing: overall pit cycle time-loss vs. baseline pace. |
| `Pit.LastPaceDeltaNetLoss` | Core | PitEngine net loss excluding stop: `LastTotalPitCycleTimeLoss - stop`, floored at 0. | Mostly diagnostic; secondary driver metric. |
| `PitLite.TotalLossSec` | Core | PitLite publishes either DTL (when baseline available) or direct lane loss on out-lap completion. | Driver-facing: single preferred pit loss output. |
| `PitLite.TotalLossPlusBoxSec` | Core | `PitLite.TotalLossSec + TimePitBoxSec`; combines the chosen loss with stopped time. | Driver-facing: total pit delta including stationary time. |
| `PitLite.Live.TimeOnPitRoadSec` | Core | Passthrough of PitEngine `TimeOnPitRoad.TotalSeconds` (running). | Driver-facing: live pit-lane timer during stop. |
| `PitLite.Live.TimeInBoxSec` | Core | Passthrough of PitEngine `PitStopElapsedSec` (running box timer). | Driver-facing: live stationary timer. |
| `Fuel.LastPitLaneTravelTime` | Core | Alias to `LastDirectTravelTime` used by fuel calculator. | Driver-facing/fuel integration. |

### Verbose pit/loss diagnostics
| Property | Channel | Source/Calculation | Intended use |
| --- | --- | --- | --- |
| `Pit.Debug.TimeOnPitRoad` | Verbose | PitEngine `TimeOnPitRoad.TotalSeconds`. | Debug. |
| `Pit.Debug.LastPitStopDuration` | Verbose | PitEngine `PitStopElapsedSec`. | Debug. |
| `Lala.Pit.AvgPaceUsedSec` | Verbose | Baseline pace fed into PitEngine DTL. | Debug. |
| `Lala.Pit.AvgPaceSource` | Verbose | Text label for the baseline source. | Debug. |
| `Lala.Pit.Raw.PitLapSec` | Verbose | Pit lap (includes stop) captured by PitEngine. | Debug. |
| `Lala.Pit.Raw.DTLFormulaSec` | Verbose | Raw DTL formula result before flooring. | Debug. |
| `Lala.Pit.InLapSec` | Verbose | PitEngine stored pit-lap seconds. | Debug. |
| `Lala.Pit.OutLapSec` | Verbose | PitEngine stored out-lap seconds. | Debug. |
| `Lala.Pit.DeltaInSec` | Verbose | Pit lap delta vs. baseline. | Debug. |
| `Lala.Pit.DeltaOutSec` | Verbose | Out-lap delta vs. baseline. | Debug. |
| `Lala.Pit.DriveThroughLossSec` | Verbose | Mirrors `LastTotalPitCycleTimeLoss`. | Debug. |
| `Lala.Pit.DirectTravelSec` | Verbose | Mirrors `LastDirectTravelTime`. | Debug. |
| `Lala.Pit.StopSeconds` | Verbose | PitEngine stop duration. | Debug. |
| `Lala.Pit.ServiceStopLossSec` | Verbose | Derived: `LastTotalPitCycleTimeLoss + stop`, floored at 0. | Debug/service analysis. |
| `Lala.Pit.Profile.PitLaneLossSec` | Verbose | Active profile’s saved pit lane loss for current track. | Debug/validation. |
| `Lala.Pit.CandidateSavedSec` | Verbose | Last saved pit loss candidate. | Debug. |
| `Lala.Pit.CandidateSource` | Verbose | Source label for saved candidate. | Debug. |

### PitLite verbose telemetry
| Property | Channel | Source/Calculation | Intended use |
| --- | --- | --- | --- |
| `PitLite.InLapSec` | Verbose | PitLite latched in-lap seconds (pit lap). | Debug/analysis. |
| `PitLite.OutLapSec` | Verbose | PitLite latched out-lap seconds. | Debug/analysis. |
| `PitLite.DeltaInSec` | Verbose | PitLite in-lap delta vs. average pace. | Debug/analysis. |
| `PitLite.DeltaOutSec` | Verbose | PitLite out-lap delta vs. average pace. | Debug/analysis. |
| `PitLite.TimePitLaneSec` | Verbose | Latched limiter-to-limiter time from PitEngine at exit. | Debug/analysis. |
| `PitLite.TimePitBoxSec` | Verbose | Latched stationary time from PitEngine at exit. | Debug/analysis. |
| `PitLite.DirectSec` | Verbose | `TimePitLaneSec - TimePitBoxSec`, floored at 0. | Debug/analysis. |
| `PitLite.DTLSec` | Verbose | PitLite computed DTL: `(In + Out) - 2*avg - TimePitBoxSec`, floored at 0. | Debug/analysis. |
| `PitLite.Status` | Verbose | PitLite status enum (`DriveThrough`, `StopValid`, etc.). | Debug. |
| `PitLite.CurrentLapType` | Verbose | Current lap classification. | Debug. |
| `PitLite.LastLapType` | Verbose | Prior lap classification. | Debug. |
| `PitLite.LossSource` | Verbose | Which loss was published (`dtl`/`direct`). | Debug. |
| `PitLite.LastSaved.Sec` | Verbose | Last saved candidate seconds. | Debug. |
| `PitLite.LastSaved.Source` | Verbose | Source label for last saved candidate. | Debug. |
| `PitLite.Live.SeenEntryThisLap` | Verbose | Entry edge flag for current lap. | Debug. |
| `PitLite.Live.SeenExitThisLap` | Verbose | Exit edge flag for current lap. | Debug. |

## Rejoin/pit phase properties
| Property | Channel | Source/Calculation | Intended use |
| --- | --- | --- | --- |
| `RejoinIsExitingPits` | Core | True when `PitEngine.CurrentPitPhase == ExitingPits`. | Driver dash/rejoin overlays. |
| `RejoinCurrentPitPhaseName` | Core | Current `PitPhase` enum name from PitEngine. | Driver dash/rejoin overlays. |
| `RejoinCurrentPitPhase` | Core | Numeric `PitPhase` value. | Driver dash/rejoin overlays. |

## Fuel & pit window helpers
| Property | Channel | Source/Calculation | Intended use |
| --- | --- | --- | --- |
| `Fuel.Pit.TotalNeededToEnd` | Core | Fuel calculator total fuel required to finish. | Driver strategy. |
| `Fuel.Pit.NeedToAdd` | Core | Fuel required at next stop. | Driver strategy. |
| `Fuel.Pit.TankSpaceAvailable` | Core | Max fuel that fits given tank size. | Driver strategy. |
| `Fuel.Pit.WillAdd` | Core | Planned fuel to add this stop. | Driver strategy. |
| `Fuel.Pit.DeltaAfterStop` | Core | Laps delta after refuel (`FuelOnExit/LapsPerLap - lapsRemaining`). | Driver strategy. |
| `Fuel.Pit.FuelSaveDeltaAfterStop` | Core | Laps delta after refuel using low-burn profile. | Driver strategy. |
| `Fuel.Pit.PushDeltaAfterStop` | Core | Laps delta after refuel using push/max-burn profile. | Driver strategy. |
| `Fuel.Pit.FuelOnExit` | Core | Estimated fuel after stop. | Driver strategy. |
| `Fuel.Pit.StopsRequiredToEnd` | Core | Integer stops needed to finish. | Driver strategy. |
| `Fuel.FuelSavePerLap` | Core | Current low-burn per-lap estimate. | Driver strategy. |
| `Fuel.Live.TotalStopLoss` | Core | Pit lane loss plus concurrent box time from fuel/tyre selections. | Driver strategy. |
| `Fuel.Live.DriveTimeAfterZero` | Core | Projected driving time once the race clock reaches 0. | Driver strategy. |
| `Fuel.Live.ProjectedDriveSecondsRemaining` | Core | Projected wall time remaining including after-zero driving. | Driver strategy. |
| `Fuel.IsPitWindowOpen` | Core | Boolean pit window flag. | Driver strategy. |
| `Fuel.PitWindowOpeningLap` | Core | Lap when pit window opens. | Driver strategy. |
| `Fuel.LastPitLaneTravelTime` | Core | Alias noted above; also fuels consumption model. | Driver strategy. |

## Debug vs. driver-facing
- **Driver-facing (dash-worthy):** Core pit loss/timer outputs (`Pit.LastDirectTravelTime`, `Pit.LastTotalPitCycleTimeLoss`, `PitLite.TotalLossSec`, live lane/box timers), pit phase core properties, and fuel/pit window helpers.
- **Primarily debug:** All `Pit.Debug.*`, `Lala.Pit.*`, and `PitLite.*` verbose properties are intended for diagnostics/validation and can be hidden from dashboards unless troubleshooting.

