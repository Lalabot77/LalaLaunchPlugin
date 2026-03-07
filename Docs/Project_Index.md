# Project Index

Validated against commit: cd7b02a
Last updated: 2026-03-07
Branch: work

## What this repo is
LalaLaunchPlugin is a SimHub plugin for iRacing that provides launch instrumentation, fuel strategy and planning, pit-cycle analytics, rejoin support, Shift Assist cueing, messaging, and multi-dash visibility coordination.

## Codex read/start order
1. Start with `Project_Index.md`.
2. Read `CODEX_CONTRACT.txt` for mandatory engineering policy.
3. Read the relevant `Subsystems/*.md` files before editing affected areas.
4. Check `RepoStatus.md` for the current validated repo/doc state.
5. Use `Code_Snapshot.md` only for orientation when needed; it is not canonical if it conflicts with the docs above.
6. Follow the analysis-first workflow and reusable task framing in `CODEX_TASK_TEMPLATE.txt`.

## Start here (canonical docs)
- [CODEX_CONTRACT.txt](CODEX_CONTRACT.txt) - mandatory Codex/global engineering policy.
- [Architecture_Guardrails.md](Architecture_Guardrails.md) - practical architecture boundaries and subsystem ownership guidance.
- [CODEX_TASK_TEMPLATE.txt](CODEX_TASK_TEMPLATE.txt) - reusable task skeleton for analysis-first Codex work.
- [SimHubParameterInventory.md](SimHubParameterInventory.md) - canonical SimHub export contract.
- [SimHubLogMessages.md](SimHubLogMessages.md) - canonical Info/Warn/Error log catalogue.
- [Subsystems/Shift_Assist.md](Subsystems/Shift_Assist.md) - Shift Assist purpose, inputs/state, outputs, and validation checklist.
- [Subsystems/Pit_Entry_Assist.md](Subsystems/Pit_Entry_Assist.md)
- [Subsystems/Track_Markers.md](Subsystems/Track_Markers.md)
- [Subsystems/Opponents.md](Subsystems/Opponents.md)
- [Subsystems/CarSA.md](Subsystems/CarSA.md)
- [Subsystems/Message_System_V1.md](Subsystems/Message_System_V1.md)
- [Subsystems/Dash_Integration.md](Subsystems/Dash_Integration.md)
- [Subsystems/Trace_Logging.md](Subsystems/Trace_Logging.md)
- [RepoStatus.md](RepoStatus.md) - current branch/repo health and delivery status.

## Subsystem map
| Subsystem | Purpose | Documentation link |
| --- | --- | --- |
| Fuel model | Live burn capture, confidence, race projection, pit-window logic | [Subsystems/Fuel_Model.md](Subsystems/Fuel_Model.md) |
| Fuel planner tab | Strategy calculator and profile/live source selection | [Subsystems/Fuel_Planner_Tab.md](Subsystems/Fuel_Planner_Tab.md) |
| Launch mode | Launch state machine, anti-stall/bog detection, launch metrics | [Subsystems/Launch_Mode.md](Subsystems/Launch_Mode.md) |
| Shift Assist | RPM target cueing with predictive lead-time, primary/urgent beep routing (urgent at derived 50% volume), and per-gear delay telemetry | [Subsystems/Shift_Assist.md](Subsystems/Shift_Assist.md) |
| Pit timing & pit-loss | PitEngine DTL/direct timing, pit-cycle exports | [Subsystems/Pit_Timing_And_PitLoss.md](Subsystems/Pit_Timing_And_PitLoss.md) |
| Pit Entry Assist | Braking cues, margin/cue maths, entry-line debrief outputs | [Subsystems/Pit_Entry_Assist.md](Subsystems/Pit_Entry_Assist.md) |
| Rejoin assist | Incident/threat detection, linger logic, and suppression guardrails | [Subsystems/Rejoin_Assist.md](Subsystems/Rejoin_Assist.md) |
| Opponents | Nearby pace/fight and pit-exit class prediction (race-gated) | [Subsystems/Opponents.md](Subsystems/Opponents.md) |
| CarSA | Car-centric gap/closing/status engine and debug exports | [Subsystems/CarSA.md](Subsystems/CarSA.md) |
| Messaging | MSG lanes + definition-driven MessageEngine v1 outputs | [Subsystems/Message_System_V1.md](Subsystems/Message_System_V1.md) |
| Dash integration | Main/message/overlay visibility, screen state exports, and global dark-mode controls (`LalaLaunch.Dash.DarkMode.*`) | [Subsystems/Dash_Integration.md](Subsystems/Dash_Integration.md) |

## Freshness
- Validated against commit: cd7b02a
- Date: 2026-03-07
- Branch: work
