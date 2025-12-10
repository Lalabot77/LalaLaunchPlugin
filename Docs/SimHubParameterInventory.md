# SimHub Parameter Inventory

This document maps every custom SimHub-exported parameter defined in `LalaLaunch.cs` (core plus verbose/debug). It captures the internal name, type, units/meaning, update cadence, primary source, and a short description to aid dashboard design.

## SimHub Parameter Inventory

### Core parameters (always published)
| Exported name | Type | Units / meaning | Update mechanism | Primary source | Notes / grouping |
| --- | --- | --- | --- | --- | --- |
| Fuel.LiveFuelPerLap | double | L/lap | Recomputed in `UpdateLiveFuelCalcs` (500 ms tick inside `DataUpdate`) | Live telemetry fuel delta per lap with filtering | Fuel strategy |
| Fuel.LiveLapsRemainingInRace | double | Laps | 500 ms fuel update | Live fuel per lap vs. remaining distance | Fuel strategy |
| Fuel.DeltaLaps | double | Laps | 500 ms fuel update | Live fuel vs. projected need | Fuel strategy |
| Fuel.TargetFuelPerLap | double | L/lap | 500 ms fuel update | FuelCalcs target based on session context | Fuel strategy |
| Fuel.IsPitWindowOpen | bool | Flag | 500 ms fuel update | FuelCalcs pit window logic | Fuel strategy |
| Fuel.PitWindowOpeningLap | double | Lap index | 500 ms fuel update | FuelCalcs | Fuel strategy |
| Fuel.LapsRemainingInTank | double | Laps | 500 ms fuel update | FuelCalcs | Fuel strategy |
| Fuel.Confidence | int | Score | 500 ms fuel update | FuelCalcs confidence logic | Fuel strategy |
| Fuel.PushFuelPerLap | double | L/lap | 500 ms fuel update | Live rolling max fuel | Fuel strategy |
| Fuel.DeltaLapsIfPush | double | Laps | 500 ms fuel update | Push fuel vs. target | Fuel strategy |
| Fuel.CanAffordToPush | bool | Flag | 500 ms fuel update | Push analysis | Fuel strategy |
| Fuel.Pit.TotalNeededToEnd | double | Liters | 500 ms fuel update | FuelCalcs total fill needed | Pit/fuel |
| Fuel.Pit.NeedToAdd | double | Liters | 500 ms fuel update | Tank deficit | Pit/fuel |
| Fuel.Pit.TankSpaceAvailable | double | Liters | 500 ms fuel update | Live fuel level vs. max | Pit/fuel |
| Fuel.Pit.WillAdd | double | Liters | 500 ms fuel update | Clamped planned add | Pit/fuel |
| Fuel.Pit.DeltaAfterStop | double | Laps | 500 ms fuel update | Post-stop lap delta | Pit/fuel |
| Fuel.Pit.FuelOnExit | double | Liters | 500 ms fuel update | Predicted post-stop fuel | Pit/fuel |
| Fuel.Pit.StopsRequiredToEnd | int | Count | 500 ms fuel update | FuelCalcs | Pit/fuel |
| Fuel.Live.RefuelRate_Lps | double | Liters/sec | Per-tick | FuelCalcs effective refuel rate (profile or default) | Fuel strategy |
| Fuel.Live.TireChangeTime_S | double | Seconds | Per-tick | FuelCalcs tyre change time currently used | Fuel strategy |
| Fuel.Live.PitLaneLoss_S | double | Seconds | Per-tick | FuelCalcs pit lane loss (DTL) currently used | Fuel strategy |
| Pace.StintAvgLapTimeSec | double | Seconds | 500 ms update | Rolling stint average from live laps | Pace |
| Pace.Last5LapAvgSec | double | Seconds | 500 ms update | Rolling 5-lap average | Pace |
| Pace.PaceConfidence | int | Score | 500 ms update | Internal confidence heuristics | Pace |
| Pace.OverallConfidence | int | Score | 500 ms update | min(fuel, pace confidence) | Pace |
| Pit.LastDirectTravelTime | double | Seconds | On valid pit measurement | PitEngine detection of lane travel | Pit timing |
| Pit.LastTotalPitCycleTimeLoss | double | Seconds | On valid pit measurement | PitEngine | Pit timing |
| Pit.LastPaceDeltaNetLoss | double | Seconds | On valid pit measurement | PitEngine pace delta | Pit timing |
| PitLite.Live.TimeOnPitRoadSec | double | Seconds | Per-tick pit monitor | PitEngine elapsed lane time | Pit-lite |
| PitLite.Live.TimeInBoxSec | double | Seconds | Per-tick pit monitor | PitEngine stationary time | Pit-lite |
| PitLite.TotalLossSec | double | Seconds | On lap-end candidate latch | PitCycleLite total loss | Pit-lite |
| PitLite.TotalLossPlusBoxSec | double | Seconds | On lap-end candidate latch | PitCycleLite total loss plus box time | Pit-lite |
| CurrentDashPage | string | Enum string | Per-tick | ScreenManager.CurrentPage | Dashboard control |
| DashControlMode | string | Enum string | Per-tick | ScreenManager.Mode | Dashboard control |
| FalseStartDetected | bool | Flag | Per-tick | Launch state detection | Launch |
| LastSessionType | string | Session type | Per-tick | Cached from telemetry | Session |
| Race.LeaderHasFinished | bool | Flag | Per-tick | Checkered flag latch for leader | Session |
| MsgCxPressed | bool | Flag | Per-tick | Messaging input state | Messaging |
| PitScreenActive | bool | Flag | Per-tick | Launch/Msg screen selection | Dashboard control |
| RejoinAlertReasonCode | int | Code | Per-tick | RejoinAssistEngine.CurrentLogicCode | Rejoin assist |
| RejoinAlertReasonName | string | Name | Per-tick | RejoinAssistEngine.CurrentLogicCode.ToString() | Rejoin assist |
| RejoinAlertMessage | string | Message | Per-tick | RejoinAssistEngine.CurrentMessage | Rejoin assist |
| RejoinIsExitingPits | bool | Flag | Per-tick | RejoinAssistEngine.IsExitingPits | Rejoin assist |
| RejoinCurrentPitPhaseName | string | Enum string | Per-tick | RejoinAssistEngine.CurrentPitPhase | Rejoin assist |
| RejoinCurrentPitPhase | int | Enum code | Per-tick | RejoinAssistEngine.CurrentPitPhase | Rejoin assist |
| RejoinThreatLevel | int | Enum code | Per-tick | RejoinAssistEngine.CurrentThreatLevel | Rejoin assist |
| RejoinThreatLevelName | string | Name | Per-tick | RejoinAssistEngine.CurrentThreatLevel | Rejoin assist |
| RejoinTimeToThreat | double | Seconds | Per-tick | RejoinAssistEngine.TimeToThreatSeconds | Rejoin assist |
| LalaDashShowLaunchScreen | bool | Flag | Per-tick | Settings | Dash config |
| LalaDashShowPitLimiter | bool | Flag | Per-tick | Settings | Dash config |
| LalaDashShowPitScreen | bool | Flag | Per-tick | Settings | Dash config |
| LalaDashShowRejoinAssist | bool | Flag | Per-tick | Settings | Dash config |
| LalaDashShowVerboseMessaging | bool | Flag | Per-tick | Settings | Dash config |
| LalaDashShowRaceFlags | bool | Flag | Per-tick | Settings | Dash config |
| LalaDashShowRadioMessages | bool | Flag | Per-tick | Settings | Dash config |
| LalaDashShowTraffic | bool | Flag | Per-tick | Settings | Dash config |
| MsgDashShowLaunchScreen | bool | Flag | Per-tick | Settings | Msg dash config |
| MsgDashShowPitLimiter | bool | Flag | Per-tick | Settings | Msg dash config |
| MsgDashShowPitScreen | bool | Flag | Per-tick | Settings | Msg dash config |
| MsgDashShowRejoinAssist | bool | Flag | Per-tick | Settings | Msg dash config |
| MsgDashShowVerboseMessaging | bool | Flag | Per-tick | Settings | Msg dash config |
| MsgDashShowRaceFlags | bool | Flag | Per-tick | Settings | Msg dash config |
| MsgDashShowRadioMessages | bool | Flag | Per-tick | Settings | Msg dash config |
| MsgDashShowTraffic | bool | Flag | Per-tick | Settings | Msg dash config |
| ManualTimeoutRemaining | string | Seconds remaining | Per-tick when launch active | Countdown from manual launch arming | Launch |
| ActualRPMAtClutchRelease | string | RPM | Per launch event | Captured from telemetry at clutch release | Launch |
| ActualThrottleAtClutchRelease | double | % | Per launch event | Telemetry snapshot | Launch |
| AntiStallActive | bool | Flag | Per-tick | Telemetry flag | Launch |
| AntiStallDetectedInLaunch | bool | Flag | Per launch event | Launch detection | Launch |
| AvgSessionLaunchRPM | string | RPM | Per launch aggregation | Average of session launches | Launch |
| BitePointInTargetRange | bool | Flag | Per launch event | Derived from clutch telemetry vs. target | Launch |
| BoggedDown | bool | Flag | Per launch event | Launch performance heuristic | Launch |
| BogDownFactorPercent | double | % | Per-tick | ActiveProfile setting | Launch |
| ClutchReleaseDelta | string | ms | Per launch event | Time delta during release | Launch |
| ClutchReleaseTime | double | Seconds | Per launch event | Telemetry | Launch |
| LastAvgLaunchRPM | double | RPM | Per launch event | Session aggregate | Launch |
| LastLaunchRPM | double | RPM | Per launch event | Telemetry snapshot | Launch |
| LastMinRPM | double | RPM | Per launch event | Telemetry snapshot | Launch |
| LaunchModeActive | bool | Flag | Per-tick | Launch visibility state | Launch |
| LaunchStateLabel | string | Enum string | Per-tick | Launch state | Launch |
| LaunchStateCode | string | Enum code | Per-tick | Launch state | Launch |
| LaunchRPM | double | RPM | Per-tick | Target/actual launch RPM | Launch |
| MaxTractionLoss | double | % | Per launch event | Telemetry | Launch |
| MinRPM | double | RPM | Per launch event | Telemetry | Launch |
| OptimalBitePoint | double | % | Per-tick | ActiveProfile target | Launch |
| OptimalBitePointTolerance | double | % | Per-tick | ActiveProfile tolerance | Launch |
| OptimalRPMTolerance | string | RPM | Per-tick | ActiveProfile tolerance | Launch |
| OptimalThrottleTolerance | string | % | Per-tick | ActiveProfile tolerance | Launch |
| ReactionTime | double | ms | Per launch event | Launch detection | Launch |
| RPMDeviationAtClutchRelease | string | RPM | Per launch event | Telemetry vs. target | Launch |
| RPMInTargetRange | bool | Flag | Per launch event | Telemetry window check | Launch |
| TargetLaunchRPM | string | RPM | Per-tick | ActiveProfile target | Launch |
| TargetLaunchThrottle | string | % | Per-tick | ActiveProfile target | Launch |
| ThrottleDeviationAtClutchRelease | double | % | Per launch event | Telemetry vs. target | Launch |
| ThrottleInTargetRange | bool | Flag | Per launch event | Telemetry window check | Launch |
| ThrottleModulationDelta | double | % | Per launch event | Telemetry modulation | Launch |
| WheelSpinDetected | bool | Flag | Per launch event | Launch detection | Launch |
| ZeroTo100Delta | double | km/h difference | Per launch event | Launch performance | Launch |
| ZeroTo100Time | double | Seconds | Per launch event | Launch performance | Launch |
| MSG.OvertakeApproachLine | double | meters/seconds (relative line) | Per-tick when enabled | MessagingSystem overtake model | Messaging |
| MSG.OvertakeWarnSeconds | double | Seconds | Per-tick when enabled | ActiveProfile traffic warning setting | Messaging |
| MSG.MsgCxTimeMessage | string | Message text | Dash-driven | Placeholder lane for time-silenced alerts (e.g., BOX BOX) | Messaging |
| MSG.MsgCxTimeVisible | bool | Flag | Dash-driven | True when the time-silenced lane is active/visible | Messaging |
| MSG.MsgCxTimeSilenceRemaining | double | Seconds | Dash-driven | Remaining silence on the time lane after MsgCx press | Messaging |
| MSG.MsgCxStateMessage | string | Message text | Dash-driven | Placeholder lane cleared until state/token changes | Messaging |
| MSG.MsgCxStateVisible | bool | Flag | Dash-driven | True when the state-change lane is active/visible | Messaging |
| MSG.MsgCxStateToken | string | Token | Dash-driven | Current state token controlling re-appearance | Messaging |
| MSG.MsgCxActionMessage | string | Message text | Dash-driven | Placeholder lane that fires an action on MsgCx press | Messaging |
| MSG.MsgCxActionPulse | bool | Flag | Dash-driven | One-shot pulse when the action lane is triggered | Messaging |

**MsgCx dash actions (button bindings)**
- `MsgCx` — single-button entry point; automatically targets the active lane in priority order (time → state → action).
- `MsgCxTimeOnly` / `MsgCxStateOnly` / `MsgCxActionOnly` — optional lane-specific bindings if you want separate buttons.
| Fuel.LastPitLaneTravelTime | double | Seconds | On valid measurement | PitEngine direct travel time | Pit timing |

### Verbose / debug parameters (published only when `SimhubPublish.VERBOSE` is true)
| Exported name | Type | Units / meaning | Update mechanism | Primary source | Notes / grouping |
| --- | --- | --- | --- | --- | --- |
| Pit.Debug.TimeOnPitRoad | double | Seconds | Per-tick | PitEngine | Pit debug |
| Pit.Debug.LastPitStopDuration | double | Seconds | Per-tick | PitEngine | Pit debug |
| Lala.Pit.AvgPaceUsedSec | double | Seconds | Per pit calc | PitEngine baseline pace | Pit debug |
| Lala.Pit.AvgPaceSource | string | Label | Per pit calc | PitEngine baseline provenance | Pit debug |
| Lala.Pit.Raw.PitLapSec | double | Seconds | Per pit calc | Raw pit lap delta | Pit debug |
| Lala.Pit.Raw.DTLFormulaSec | double | Seconds | Per pit calc | Formula delta | Pit debug |
| Lala.Pit.InLapSec | double | Seconds | Per pit calc | In-lap delta vs. baseline | Pit debug |
| Lala.Pit.OutLapSec | double | Seconds | Per pit calc | Out-lap delta vs. baseline | Pit debug |
| Lala.Pit.DeltaInSec | double | Seconds | Per pit calc | In-lap loss | Pit debug |
| Lala.Pit.DeltaOutSec | double | Seconds | Per pit calc | Out-lap loss | Pit debug |
| Lala.Pit.DriveThroughLossSec | double | Seconds | Per pit calc | Drive-through loss | Pit debug |
| Lala.Pit.DirectTravelSec | double | Seconds | Per pit calc | Lane direct travel | Pit debug |
| Lala.Pit.StopSeconds | double | Seconds | Per pit calc | Stationary time | Pit debug |
| Lala.Pit.ServiceStopLossSec | double | Seconds | Per pit calc | Total service loss (travel + stop) | Pit debug |
| Lala.Pit.Profile.PitLaneLossSec | double | Seconds | Per pit calc | Profile pit lane loss | Pit debug |
| Lala.Pit.CandidateSavedSec | double | Seconds | On save | PitLite saved loss | Pit debug |
| Lala.Pit.CandidateSource | string | Label | On save | PitLite saved source | Pit debug |
| PitLite.InLapSec | double | Seconds | Per-tick | PitCycleLite in-lap | Pit-lite debug |
| PitLite.OutLapSec | double | Seconds | Per-tick | PitCycleLite out-lap | Pit-lite debug |
| PitLite.DeltaInSec | double | Seconds | Per-tick | PitCycleLite delta | Pit-lite debug |
| PitLite.DeltaOutSec | double | Seconds | Per-tick | PitCycleLite delta | Pit-lite debug |
| PitLite.TimePitLaneSec | double | Seconds | Per-tick | PitCycleLite lane time | Pit-lite debug |
| PitLite.TimePitBoxSec | double | Seconds | Per-tick | PitCycleLite box time | Pit-lite debug |
| PitLite.DirectSec | double | Seconds | Per-tick | PitCycleLite direct time | Pit-lite debug |
| PitLite.DTLSec | double | Seconds | Per-tick | PitCycleLite drive-through loss | Pit-lite debug |
| PitLite.Status | string | Enum string | Per-tick | PitCycleLite status | Pit-lite debug |
| PitLite.CurrentLapType | string | Enum string | Per-tick | PitCycleLite current lap type | Pit-lite debug |
| PitLite.LastLapType | string | Enum string | Per-tick | PitCycleLite last lap type | Pit-lite debug |
| PitLite.LossSource | string | Label | On save | PitCycleLite saved source | Pit-lite debug |
| PitLite.LastSaved.Sec | double | Seconds | On save | PitCycleLite saved loss | Pit-lite debug |
| PitLite.LastSaved.Source | string | Label | On save | PitCycleLite saved source | Pit-lite debug |
| PitLite.Live.SeenEntryThisLap | bool | Flag | Per-tick | PitCycleLite | Pit-lite debug |
| PitLite.Live.SeenExitThisLap | bool | Flag | Per-tick | PitCycleLite | Pit-lite debug |

## Driver-Facing Dashboard Parameters

The most driver-relevant signals (intended for dashboards rather than engineering screens) are:

- **Fuel strategy:** `Fuel.LiveFuelPerLap`, `Fuel.LiveLapsRemainingInRace`, `Fuel.LapsRemainingInTank`, `Fuel.Pit.TotalNeededToEnd`, `Fuel.Pit.NeedToAdd`, `Fuel.PushFuelPerLap`, `Fuel.DeltaLapsIfPush`, `Fuel.IsPitWindowOpen`, `Fuel.PitWindowOpeningLap`, `Fuel.Pit.StopsRequiredToEnd`, `Fuel.LastPitLaneTravelTime`.
- **Pace awareness:** `Pace.StintAvgLapTimeSec`, `Pace.Last5LapAvgSec`, `Pace.PaceConfidence`, `Pace.OverallConfidence`.
- **Pit/penalty context:** `Pit.LastDirectTravelTime`, `Pit.LastTotalPitCycleTimeLoss`, `Pit.LastPaceDeltaNetLoss`, `PitLite.TotalLossSec`.
- **Rejoin assist:** `RejoinAlertReasonCode`, `RejoinAlertReasonName`, `RejoinAlertMessage`, `RejoinIsExitingPits`, `RejoinCurrentPitPhase`, `RejoinThreatLevel`, `RejoinTimeToThreat`.
- **Launch / start aids:** `LaunchModeActive`, `LaunchStateLabel`, `LaunchRPM`, `TargetLaunchRPM`, `TargetLaunchThrottle`, `ActualRPMAtClutchRelease`, `ActualThrottleAtClutchRelease`, `ManualTimeoutRemaining`.

Reliability/caveats:
- Fuel and pace fields are refreshed in the 500 ms loop and rely on continuous live telemetry; confidence scores indicate stability. They are trustworthy only while the session is running and clean laps are being logged.
- Pit loss metrics latch when PitEngine/PitCycleLite detect valid pit cycles; between stops they retain the last saved values.
- Rejoin assist outputs are only meaningful when the RejoinAssistEngine is enabled and actively tracking incidents.
- Launch parameters update during the launch routine; outside of start procedures, many retain the last run values.

## Observations / Potential Issues

- Verbose properties are gated behind `SimhubPublish.VERBOSE`; dashboards relying on them must enable that flag explicitly.
- Several launch-related properties are formatted as strings (e.g., RPM values with `ToString("F0")`) while others are numeric, which may confuse dashboards expecting consistent types.
- `ManualTimeoutRemaining` returns an empty string when not armed instead of `0`, so widgets should handle blank output.
- Pace and fuel confidence are separate but `Pace.OverallConfidence` simply takes the minimum; consumers should pick the most relevant metric rather than assume it is independently computed.
