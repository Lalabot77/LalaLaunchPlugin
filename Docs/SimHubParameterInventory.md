# SimHub Parameter Inventory

The tables below list every SimHub-exposed property published from `LalaLaunch.cs`. Names omit the implicit `LalaLaunch.` prefix that SimHub adds. Core parameters are always exported; verbose parameters require `SimhubPublish.VERBOSE`.

## Core parameters

### Fuel model and projection
| Exported name | Type | Units / meaning | Update cadence | Primary source |
| --- | --- | --- | --- | --- |
| Fuel.LiveFuelPerLap | double | L/lap burn used for all projections | 500 ms tick + lap crossing | Rolling accepted laps / fallback SimHub estimate |
| Fuel.LiveFuelPerLap_Stable | double | Smoothed burn used when the live figure is noisy | 500 ms tick | `LiveFuelPerLap` stability window |
| Fuel.LiveFuelPerLap_StableSource | string | Provenance for the stable burn (live / profile / sim) | 500 ms tick | Stability selector |
| Fuel.LiveFuelPerLap_StableConfidence | int | Confidence for the stable burn | 500 ms tick | Stability selector |
| Fuel.LiveLapsRemainingInRace | double | Laps remaining using current projection | 500 ms tick | `ComputeProjectedLapsRemaining` |
| Fuel.LiveLapsRemainingInRace_S | string | Formatted laps-remaining string | 500 ms tick | Above calculation |
| Fuel.LiveLapsRemainingInRace_Stable | double | Laps remaining using stable burn | 500 ms tick | Stable burn projection |
| Fuel.LiveLapsRemainingInRace_Stable_S | string | Formatted stable projection | 500 ms tick | Stable burn projection |
| Fuel.DeltaLaps | double | Surplus/deficit to finish at current burn | 500 ms tick | Tank laps − projected race laps |
| Fuel.TargetFuelPerLap | double | Required burn to finish when short | 500 ms tick | Current fuel ÷ projected laps |
| Fuel.LapsRemainingInTank | double | Laps possible with current fuel | 500 ms tick | Tank fuel ÷ `LiveFuelPerLap` |
| Fuel.ProjectionLapTime_Stable | double | Lap time used for race-length projection | 500 ms tick | Pace estimator |
| Fuel.ProjectionLapTime_StableSource | string | Source label for projection lap time | 500 ms tick | Pace estimator |
| Fuel.Confidence | int | Fuel-model confidence score | Lap crossing; surfaced each tick | Window quality heuristics |
| Fuel.PushFuelPerLap | double | Aggressive burn guidance | 500 ms tick | Max observed/session heuristic |
| Fuel.FuelSavePerLap | double | Conservative burn guidance | 500 ms tick | Window minima |
| Fuel.DeltaLapsIfPush | double | Surplus/deficit if driving at push burn | 500 ms tick | Push guidance |
| Fuel.CanAffordToPush | bool | True when push burn still finishes the race | 500 ms tick | Push guidance |
| Fuel.Live.DriveTimeAfterZero | double | Expected driving after race timer hits 0 | 500 ms tick | Strategy overrun model |
| Fuel.Live.ProjectedDriveSecondsRemaining | double | Wall-time remaining including overrun | 500 ms tick | Strategy overrun model |

### Fuel deltas and pit needs
| Exported name | Type | Units / meaning | Update cadence | Primary source |
| --- | --- | --- | --- | --- |
| Fuel.Delta.LitresCurrent / LitresPlan / LitresWillAdd | double | Liters needed vs. finish for current fuel, current MFD plan, and planned add | 500 ms tick | FuelCalculator deltas |
| Fuel.Delta.LitresCurrentPush / LitresPlanPush / LitresWillAddPush | double | Same as above assuming push burn | 500 ms tick | FuelCalculator deltas |
| Fuel.Delta.LitresCurrentSave / LitresPlanSave / LitresWillAddSave | double | Same as above assuming fuel-save burn | 500 ms tick | FuelCalculator deltas |
| Fuel.Pit.TotalNeededToEnd / TotalNeededToEnd_S | double/string | Total liters to finish at current burn | 500 ms tick | Live burn × projected laps |
| Fuel.Pit.NeedToAdd | double | Additional liters required to finish | 500 ms tick | Total needed − tank fuel |
| Fuel.Pit.TankSpaceAvailable | double | Liters that fit given BoP/override tank | 500 ms tick | Telemetry max fuel / override |
| Fuel.Pit.WillAdd | double | Liters expected to be added (after clamping to tank space) | 500 ms tick | Telemetry MFD refuel request |
| Fuel.Pit.FuelOnExit | double | Estimated fuel after completing the stop | 500 ms tick | Current fuel + `WillAdd` |
| Fuel.Pit.DeltaAfterStop / DeltaAfterStop_S | double/string | Lap surplus after stop at current burn | 500 ms tick | `FuelOnExit` ÷ burn − race laps |
| Fuel.Pit.FuelSaveDeltaAfterStop / _S | double/string | Lap surplus after stop at save burn | 500 ms tick | Save burn projection |
| Fuel.Pit.PushDeltaAfterStop / _S | double/string | Lap surplus after stop at push burn | 500 ms tick | Push burn projection |
| Fuel.PitStopsRequiredByFuel | int | Stops implied by capacity vs. deficit | 500 ms tick | Capacity-based ceiling |
| Fuel.PitStopsRequiredByPlan | int | Stops from strategy plan | 500 ms tick | FuelCalculator strategy |
| Fuel.Pit.StopsRequiredToEnd | int | Final stops required (plan or capacity) | 500 ms tick | Strategy + capacity |
| Fuel.Live.RefuelRate_Lps | double | Effective refuel rate | 500 ms tick | FuelCalcs profile/measured rate |
| Fuel.Live.TireChangeTime_S | double | Seconds to change tyres if selected | 500 ms tick | FuelCalcs tyre-time estimator |
| Fuel.Live.PitLaneLoss_S | double | Pit lane loss used by strategy | 500 ms tick | FuelCalcs lane loss |
| Fuel.Live.TotalStopLoss | double | Pit lane loss + service time | 500 ms tick | FuelCalcs + tyre/refuel time |

### Pace and race state
| Exported name | Type | Units / meaning | Update cadence | Primary source |
| --- | --- | --- | --- | --- |
| Pace.StintAvgLapTimeSec | double | Rolling stint-average lap | Lap crossing surfaced each tick | Pace estimator |
| Pace.Last5LapAvgSec | double | Average of last up-to-five clean laps | Lap crossing surfaced each tick | Pace estimator |
| Pace.LeaderAvgLapTimeSec | double | Rolling leader pace | Lap crossing surfaced each tick | Leader telemetry candidate read |
| Pace.LeaderDeltaToPlayerSec | double | Leader pace − player pace | Lap crossing surfaced each tick | Derived from above |
| Pace.PaceConfidence | int | Pace model confidence | Lap crossing surfaced each tick | Heuristics on lap quality |
| Pace.OverallConfidence | int | Min of pace/fuel confidence | Lap crossing surfaced each tick | Derived |
| Race.OverallLeaderHasFinished / Race.ClassLeaderHasFinished / Race.LeaderHasFinished | bool | Checkered flag latches | Per-tick | Session flags |
| Race.OverallLeaderHasFinishedValid / Race.ClassLeaderHasFinishedValid | bool | Validity flags for leader-finished values | Per-tick | Session flags |

### Pit timing and PitLite
| Exported name | Type | Units / meaning | Update cadence | Primary source |
| --- | --- | --- | --- | --- |
| Pit.LastDirectTravelTime | double | Limiter-to-limiter lane time | On valid pit measurement | PitEngine direct travel |
| Pit.LastTotalPitCycleTimeLoss | double | Pit delta vs. baseline pace | On valid pit measurement | PitEngine DTL | 
| Pit.LastPaceDeltaNetLoss | double | Pit loss minus stopped time | On valid pit measurement | PitEngine |
| PitLite.Live.TimeOnPitRoadSec | double | Running lane timer | Per-tick during pit | PitEngine |
| PitLite.Live.TimeInBoxSec | double | Running stationary timer | Per-tick during pit | PitEngine |
| PitLite.TotalLossSec | double | Preferred pit loss output (DTL/direct) | Latched on out-lap | PitCycleLite |
| PitLite.TotalLossPlusBoxSec | double | Pit loss plus stationary time | Latched on out-lap | PitCycleLite |

### Dashboard overlays and rejoin assist
| Exported name | Type | Meaning |
| --- | --- | --- |
| CurrentDashPage | string | Active dash page |
| DashControlMode | string | Dash auto/manual mode |
| PitScreenActive | bool | Whether pit screen is being shown |
| FalseStartDetected | bool | Launch false-start latch |
| LastSessionType | string | Most recent session type token |
| MsgCxPressed | bool | Whether MsgCx button was pressed this tick |
| RejoinAlertReasonCode / Name | int/string | Active rejoin logic code |
| RejoinAlertMessage | string | Human-readable rejoin instruction |
| RejoinIsExitingPits | bool | Pit phase flag |
| RejoinCurrentPitPhase / Name | int/string | Current pit phase enum |
| RejoinThreatLevel / Name | int/string | Current traffic threat level |
| RejoinTimeToThreat | double | Seconds to nearest threat |

### Dash visibility settings
| Exported name | Type | Dash |
| --- | --- | --- |
| LalaDashShowLaunchScreen, LalaDashShowPitLimiter, LalaDashShowPitScreen, LalaDashShowRejoinAssist, LalaDashShowVerboseMessaging, LalaDashShowRaceFlags, LalaDashShowRadioMessages, LalaDashShowTraffic | bool | Lala dash toggles |
| MsgDashShowLaunchScreen, MsgDashShowPitLimiter, MsgDashShowPitScreen, MsgDashShowRejoinAssist, MsgDashShowVerboseMessaging, MsgDashShowRaceFlags, MsgDashShowRadioMessages, MsgDashShowTraffic | bool | Messaging dash toggles |

### Launch control & manual timer
| Exported name | Type | Units / meaning |
| --- | --- | --- |
| ManualTimeoutRemaining | string | Seconds remaining on manual launch arm |
| ActualRPMAtClutchRelease | string | RPM snapshot at clutch release |
| ActualThrottleAtClutchRelease | double | % throttle at clutch release |
| AntiStallActive / AntiStallDetectedInLaunch | bool | Anti-stall flags |
| AvgSessionLaunchRPM / LastAvgLaunchRPM | string/double | Session average launch RPM |
| BitePointInTargetRange | bool | Whether clutch bite was within tolerance |
| BoggedDown / BogDownFactorPercent | bool/double | Bog-down detection and profile factor |
| ClutchReleaseDelta | string | ms delta during release |
| ClutchReleaseTime | double | Seconds for clutch release |
| LastLaunchRPM / LastMinRPM / MinRPM | double | Telemetry snapshots |
| LaunchModeActive | bool | Whether launch UI is visible |
| LaunchStateLabel / LaunchStateCode | string | Current launch state |
| LaunchRPM / TargetLaunchRPM | double | Live and target launch RPM |
| MaxTractionLoss | double | % slip observed |
| OptimalBitePoint / OptimalBitePointTolerance | double | Target bite point and tolerance |
| OptimalRPMTolerance / OptimalThrottleTolerance | string | Tolerances for RPM and throttle |
| ReactionTime | double | ms to release |
| RPMDeviationAtClutchRelease | string | Delta vs. target RPM |
| RPMInTargetRange | bool | RPM window flag |
| TargetLaunchThrottle | string | Target throttle % |
| ThrottleDeviationAtClutchRelease | double | Delta vs. target throttle |
| ThrottleInTargetRange | bool | Throttle window flag |
| ThrottleModulationDelta | double | % modulation measurement |
| WheelSpinDetected | bool | Wheel spin flag |
| ZeroTo100Delta | double | km/h delta vs. baseline |
| ZeroTo100Time | double | 0–100 km/h time for last launch |

### Messaging system
| Exported name | Type | Meaning |
| --- | --- | --- |
| MSG.OvertakeApproachLine | double | Relative line metric for approaching traffic |
| MSG.OvertakeWarnSeconds | double | Seconds of approach buffer to warn |
| MSG.MsgCxTimeMessage / MsgCxStateMessage / MsgCxActionMessage | string | Message text per lane |
| MSG.MsgCxTimeVisible / MsgCxStateVisible | bool | Visibility for time/state lanes |
| MSG.MsgCxTimeSilenceRemaining | double | Remaining silence window for time lane |
| MSG.MsgCxStateToken | string | Token controlling state lane reappearance |
| MSG.MsgCxActionPulse | bool | One-shot trigger when action lane fires |

## Verbose / debug parameters
| Exported name | Type | Units / meaning | Update cadence | Source |
| --- | --- | --- | --- | --- |
| Pit.Debug.TimeOnPitRoad | double | Seconds on pit road (live timer) | Per-tick | PitEngine |
| Pit.Debug.LastPitStopDuration | double | Stationary box timer | Per-tick | PitEngine |
| Lala.Pit.AvgPaceUsedSec | double | Baseline pace fed into DTL | Per calc | PitEngine |
| Lala.Pit.AvgPaceSource | string | Source label for baseline pace | Per calc | PitEngine |
| Lala.Pit.Raw.PitLapSec | double | Pit lap including stop | Per calc | PitEngine |
| Lala.Pit.Raw.DTLFormulaSec | double | Raw DTL computation | Per calc | PitEngine |
| Lala.Pit.InLapSec / OutLapSec | double | In/out lap deltas vs. baseline | Per calc | PitEngine |
| Lala.Pit.DeltaInSec / DeltaOutSec | double | In/out loss contributions | Per calc | PitEngine |
| Lala.Pit.DriveThroughLossSec | double | Drive-through-style loss | Per calc | PitEngine |
| Lala.Pit.DirectTravelSec | double | Direct lane travel time | Per calc | PitEngine |
| Lala.Pit.StopSeconds | double | Stationary stop duration | Per calc | PitEngine |
| Lala.Pit.ServiceStopLossSec | double | DTL + stop time (floored) | Per calc | PitEngine |
| Lala.Pit.Profile.PitLaneLossSec | double | Saved profile pit-lane loss | Per calc | Active profile track |
| Lala.Pit.CandidateSavedSec / CandidateSource | double/string | Last saved pit-loss candidate and provenance | On save | PitEngine |
| PitLite.InLapSec / OutLapSec | double | Latched pit-lite lap deltas | Per-tick | PitCycleLite |
| PitLite.DeltaInSec / DeltaOutSec | double | Pit-lite deltas vs. pace | Per-tick | PitCycleLite |
| PitLite.TimePitLaneSec / TimePitBoxSec | double | Latched timers from PitEngine | Per-tick | PitCycleLite |
| PitLite.DirectSec | double | Lane time minus box | Per-tick | PitCycleLite |
| PitLite.DTLSec | double | Pit-lite DTL (floored) | Per-tick | PitCycleLite |
| PitLite.Status | string | Pit-lite status enum | Per-tick | PitCycleLite |
| PitLite.CurrentLapType / LastLapType | string | Current/previous lap classification | Per-tick | PitCycleLite |
| PitLite.LossSource | string | Whether DTL or direct loss was published | Per-tick | PitCycleLite |
| PitLite.LastSaved.Sec / LastSaved.Source | double/string | Last saved pit-lite candidate and source | On save | PitCycleLite |
| PitLite.Live.SeenEntryThisLap / SeenExitThisLap | bool | Pit-edge flags for current lap | Per-tick | PitCycleLite |
