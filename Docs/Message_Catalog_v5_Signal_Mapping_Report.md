# Message Catalog v5 — Signal Mapping Report

## Summary
Signals by source: SimHubProperty=6, iRacingExtraProperties=6, PluginCalc=10.

## Per-message mapping
| MsgId | EvaluatorId | RequiredSignals | Signal sources |
| --- | --- | --- | --- |
| fuel.pit_required_Caution | Eval_FuelPitRequired | FuelLapsRemaining | FuelLapsRemaining: PluginCalc → Fuel.LiveLapsRemainingInRace |
| fuel.pit_required_Warning | Eval_FuelPitRequired | FuelLapsRemaining | FuelLapsRemaining: PluginCalc → Fuel.LiveLapsRemainingInRace |
| flag.GreenStart |  | FlagSessionFlags, CompletedLaps | FlagSessionFlags: SimHubProperty → DataCorePlugin.GameRawData.Telemetry.SessionFlags; CompletedLaps: SimHubProperty → DataCorePlugin.GameData.CompletedLaps |
| flag.GreenClear |  | FlagSessionFlags | FlagSessionFlags: SimHubProperty → DataCorePlugin.GameRawData.Telemetry.SessionFlags |
| flag.BlueFlag | Eval_FlagBlue | FlagSessionFlags | FlagSessionFlags: SimHubProperty → DataCorePlugin.GameRawData.Telemetry.SessionFlags |
| flag.green | Eval_FlagGreen | FlagSessionFlags | FlagSessionFlags: SimHubProperty → DataCorePlugin.GameRawData.Telemetry.SessionFlags |
| flag.yellow.local | Eval_FlagYellowLocal | FlagSessionFlags | FlagSessionFlags: SimHubProperty → DataCorePlugin.GameRawData.Telemetry.SessionFlags |
| flag.yellow.fcy | Eval_FlagFCY | FlagSessionFlags | FlagSessionFlags: SimHubProperty → DataCorePlugin.GameRawData.Telemetry.SessionFlags |
| flag.safetycar | Eval_SafetyCar | FlagSessionFlags, PaceMode | FlagSessionFlags: SimHubProperty → DataCorePlugin.GameRawData.Telemetry.SessionFlags; PaceMode: SimHubProperty → DataCorePlugin.GameRawData.Telemetry.PaceMode |
| flag.white | Eval_FlagWhite | FlagSessionFlags | FlagSessionFlags: SimHubProperty → DataCorePlugin.GameRawData.Telemetry.SessionFlags |
| flag.checkered | Eval_FlagCheckered | FlagSessionFlags | FlagSessionFlags: SimHubProperty → DataCorePlugin.GameRawData.Telemetry.SessionFlags |
| flag.black | Eval_FlagBlack | FlagSessionFlags | FlagSessionFlags: SimHubProperty → DataCorePlugin.GameRawData.Telemetry.SessionFlags |
| flag.meatball | Eval_FlagMeatball | FlagSessionFlags | FlagSessionFlags: SimHubProperty → DataCorePlugin.GameRawData.Telemetry.SessionFlags |
| pit.window_open | Eval_PitWindowOpen | PitWindowOpen | PitWindowOpen: PluginCalc → Fuel.IsPitWindowOpen |
| pit.refuel_complete | Eval_RefuelComplete | PitServiceFuelDone | PitServiceFuelDone: SimHubProperty → DataCorePlugin.GameRawData.Telemetry.PlayerCarPitSvFlags (FuelDone bit) |
| fuel.save_required | Eval_FuelSaveRequired | FuelDeltaLaps | FuelDeltaLaps: PluginCalc → Fuel.DeltaLaps |
| fuel.push_ok | Eval_FuelCanPush | FuelCanPush | FuelCanPush: PluginCalc → Fuel.CanAffordToPush |
| strategy.overtake_soon | Eval_CatchClassAhead | DriverAheadGapSeconds, PlayerPaceLast5LapAvg | DriverAheadGapSeconds: iRacingExtraProperties → IRacingExtraProperties.iRacing_DriverAhead_00_RelativeGapToPlayer; PlayerPaceLast5LapAvg: PluginCalc → Pace.Last5LapAvgSec |
| strategy.positionchange | Eval_DriverClassPosition | PlayerClassPosition | PlayerClassPosition: SimHubProperty → DataCorePlugin.GameData.PositionInClass |
| traffic.behind_close | Eval_TrafficBehindClose | TrafficBehindGapSeconds | TrafficBehindGapSeconds: iRacingExtraProperties → IRacingExtraProperties.iRacing_DriverBehind_00_RelativeGapToPlayer |
| traffic.behind_attack | Eval_TrafficBehindFast | TrafficBehindDistanceM | TrafficBehindDistanceM: iRacingExtraProperties → IRacingExtraProperties.iRacing_DriverBehind_00_DistanceToPlayer |
| traffic.fasterclass_behind | Eval_FasterClassBehind | TrafficBehindGapSeconds, TrafficBehindClass, PlayerClassName | TrafficBehindGapSeconds: iRacingExtraProperties → IRacingExtraProperties.iRacing_DriverBehind_00_RelativeGapToPlayer; TrafficBehindClass: iRacingExtraProperties → IRacingExtraProperties.iRacing_DriverBehind_00_ClassName; PlayerClassName: iRacingExtraProperties → IRacingExtraProperties.iRacing_Player_ClassName |
| racecontrol.ahead_slow | Eval_IncidentAhead | IncidentAheadWarning | IncidentAheadWarning: PluginCalc → TBD plugin calc: detect slow/incident car ahead on track |
| rejoin.threat_high | Eval_RejoinThreatHigh | RejoinThreatLevel, RejoinReasonCode | RejoinThreatLevel: PluginCalc → RejoinAssistEngine; RejoinReasonCode: PluginCalc → RejoinAlertReasonCode |
| rejoin.threat_med | Eval_RejoinThreatMed | RejoinThreatLevel, RejoinReasonCode | RejoinThreatLevel: PluginCalc → RejoinAssistEngine; RejoinReasonCode: PluginCalc → RejoinAlertReasonCode |
| racecontrol.slowdown | Eval_SlowDown | SlowDownTimeRemaining | SlowDownTimeRemaining: iRacingExtraProperties → IRacingExtraProperties.iRacing_SlowDownTime |
| racecontrol.inc_points | Eval_IncPoints | IncidentCount | IncidentCount: SimHubProperty → DataCorePlugin.GameRawData.Telemetry.PlayerCarDriverIncidentCount |

## Signal registry
| SignalId | Type | Units | SourceType | Source | Notes |
| --- | --- | --- | --- | --- | --- |
| FuelDeltaL_Current | double | L | PluginCalc | Fuel.Delta_LitresCurrent | Fuel deficit |
| PitWindowOpen | bool | - | PluginCalc | Fuel.IsPitWindowOpen | Pit window open |
| RejoinThreatLevel | int | level | PluginCalc | RejoinAssistEngine | Threat scoring |
| FuelLapsRemaining | double | Laps | PluginCalc | Fuel.LiveLapsRemainingInRace | Used by: fuel.pit_required_* messages |
| RejoinReasonCode | int | code | PluginCalc | RejoinAlertReasonCode | Token for rejoin reason; used by rejoin.threat_* |
| FlagSessionFlags | int | bitmask | SimHubProperty | DataCorePlugin.GameRawData.Telemetry.SessionFlags | iRacing session flags bitmask; decode green/blue/yellow/white/checkered/black/meatball |
| PaceMode | int | mode | SimHubProperty | DataCorePlugin.GameRawData.Telemetry.PaceMode | Detects safety car/pace car phases |
| CompletedLaps | int | laps | SimHubProperty | DataCorePlugin.GameData.CompletedLaps | Gate green start announcements to lap 0/1 |
| PitServiceFuelDone | bool | - | SimHubProperty | DataCorePlugin.GameRawData.Telemetry.PlayerCarPitSvFlags (FuelDone bit) | True once refuel service is complete this stop |
| FuelDeltaLaps | double | laps | PluginCalc | Fuel.DeltaLaps | Negative when short on fuel; used by fuel.save_required |
| FuelCanPush | bool | - | PluginCalc | Fuel.CanAffordToPush | True when push burn still finishes; used by fuel.push_ok |
| TrafficBehindGapSeconds | double | s | iRacingExtraProperties | IRacingExtraProperties.iRacing_DriverBehind_00_RelativeGapToPlayer | Seconds to closest car behind; use to detect close/approaching traffic |
| TrafficBehindDistanceM | double | m | iRacingExtraProperties | IRacingExtraProperties.iRacing_DriverBehind_00_DistanceToPlayer | Distance to car behind; use for attack (<10 m) alerts |
| TrafficBehindClass | string | - | iRacingExtraProperties | IRacingExtraProperties.iRacing_DriverBehind_00_ClassName | Class of trailing car; combine with player class to find faster-class approaches |
| PlayerClassName | string | - | iRacingExtraProperties | IRacingExtraProperties.iRacing_Player_ClassName | Player class label; compare to TrafficBehindClass |
| DriverAheadGapSeconds | double | s | iRacingExtraProperties | IRacingExtraProperties.iRacing_DriverAhead_00_RelativeGapToPlayer | Gap to car ahead for strategy overtake timing |
| PlayerPaceLast5LapAvg | double | s | PluginCalc | Pace.Last5LapAvgSec | Player recent average pace; needed to project catch laps |
| PlayerClassPosition | int | pos | SimHubProperty | DataCorePlugin.GameData.PositionInClass | Use with previous tick to detect position changes |
| IncidentCount | int | pts | SimHubProperty | DataCorePlugin.GameRawData.Telemetry.PlayerCarDriverIncidentCount | Total incident points; for racecontrol.inc_points |
| SlowDownTimeRemaining | double | s | iRacingExtraProperties | IRacingExtraProperties.iRacing_SlowDownTime | Active slowdown penalty countdown |
| IncidentAheadWarning | bool | - | PluginCalc | TBD plugin calc: detect slow/incident car ahead on track | GAP: needs new detection using opponent pace/track distance |
| RejoinTimeToThreat | double | s | PluginCalc | RejoinTimeToThreat | Seconds to nearest threat; supplemental to threat level |

## Gaps and unclear sources
- IncidentAheadWarning requires new plugin calc using opponent pace/track distance to spot slow/incident car ahead.
- Strategy overtake ETA needs opponent-ahead pace; currently only ahead gap and player pace are available.
- FlagSessionFlags assumes session-flag bit decoding; validate bitmasks for meatball vs. black vs. local yellows.
- PitServiceFuelDone uses PitSvFlags FuelDone bit; confirm exposure in SimHub telemetry for all sessions.

## Implementation notes for v1
- Ready now: fuel pit required, pit window open, fuel push/save, rejoin threat, slowdown/incident count — all map to existing LalaLaunch or SimHub/iRacingExtraProperties exports.
- Flags can be implemented via SessionFlags/PaceMode without new calculations; only decoding logic needed.
- Traffic behind alerts can start with iRacingExtraProperties driver-behind gap/distance/class plus player class.
- Strategy overtake and incident-ahead messaging require new calculations (catch prediction, ahead hazard scan).