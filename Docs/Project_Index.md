# Project Index

Validated against commit: 9d77f4a  
Last updated: 2026-02-10  
Branch: work

## What this repo is
LalaLaunchPlugin is a SimHub plugin for iRacing that provides launch control instrumentation, fuel strategy calculations, pit-cycle timing, rejoin assistance, messaging outputs, and dash/screen coordination across the Fuel, Pit, Launch, and Messaging tabs. Runtime behaviour lives in C# (e.g., `LalaLaunch.cs`, `FuelCalcs.cs`, `PitEngine.cs`) with SimHub exports documented below.

## Start here
- [SimHubParameterInventory.md](SimHubParameterInventory.md) — canonical export list.
- [SimHubLogMessages.md](SimHubLogMessages.md) — canonical log catalogue.
- [FuelProperties_Spec.md](FuelProperties_Spec.md) — canonical fuel model behaviour.
- [FuelTab_SourceFlowNotes.md](FuelTab_SourceFlowNotes.md) — canonical Fuel tab source flow.
- [Reset_And_Session_Identity.md](Reset_And_Session_Identity.md) — canonical reset/session rules.
- [Subsystems/Pit_Entry_Assist.md](Subsystems/Pit_Entry_Assist.md) — driver/dash/log spec for pit entry braking cues + manual arming + line debrief outputs.
- [Subsystems/Dash_Integration.md](Subsystems/Dash_Integration.md) — dash consumption and visualisation contracts (Pit Entry Assist + overlay visibility included).
- [Subsystems/Track_Markers.md](Subsystems/Track_Markers.md) — pit entry/exit marker auto-learn, storage, locking, and MSGV1 notifications.
- [Subsystems/Opponents.md](Subsystems/Opponents.md) — nearby pace/fight and pit-exit prediction (Race-only gate, lap ≥1).
- [Subsystems/CarSA.md](Subsystems/CarSA.md) — car-based spatial awareness (SA-Core v2 distance gaps, gate-gap v2 fixes, precision gap outputs, slot info visibility, and debug export).
- [Subsystems/Message_System_V1.md](Subsystems/Message_System_V1.md) — notification layer for pit markers and other signals (definition-driven, no legacy messages).
- [Subsystems/MessageEngineV1_Notes.md](Subsystems/MessageEngineV1_Notes.md) — SimHub export list + migration notes for MSGV1 and legacy MSG lanes.
- [Subsystems/Trace_Logging.md](Subsystems/Trace_Logging.md) — session summary CSV + per-lap trace schema and lifecycle.
- [BranchWorkflow.md](BranchWorkflow.md) — branching policy.
- [ConflictResolution.md](ConflictResolution.md) — merge/conflict process.

## Known Lies / Allowed Compromises
- Replay session identity: Session tokens can look inconsistent in replays — accepted because replay identity data is unreliable; resets are documented to tolerate this. (last reviewed: 2026-01-14)
- Track-length delta alerts: Track-length delta messages are informational only and do not block usage — accepted to avoid false lockouts while still surfacing drift. (last reviewed: 2026-01-14)
- Pit loss (DTL): DTL includes traffic impact, so “loss” can exceed idealized pit time — accepted because it reflects real race loss. (last reviewed: 2026-01-14)
- Fuel planner separation: Planner-only values intentionally remain decoupled from live volatility — accepted to keep strategy settings stable. (last reviewed: 2026-01-14)
- Rejoin alert linger: Dismissal requires both time + speed gates, so warnings can persist if speed stays low — accepted for safety gating. (last reviewed: 2026-01-14)

## Doc Authority & Freshness
- **CANONICAL contracts:** `SimHubParameterInventory.md`, `SimHubLogMessages.md`, `FuelProperties_Spec.md`, `FuelTab_SourceFlowNotes.md`, `Reset_And_Session_Identity.md`, and subsystem specs called out in Start Here are the source of truth.
- **Snapshot/reference docs:** `Code_Snapshot.md`, ad-hoc analysis notes, and legacy spreadsheets are **non-canonical** and may be stale.
- If any snapshot conflicts with canonical docs, treat the snapshot as stale. `Code_Snapshot.md` is not authoritative unless explicitly regenerated for the current commit.

## Doc inventory & canonicalisation
- **Truth docs:** `SimHubParameterInventory.md`, `SimHubLogMessages.md`, `FuelProperties_Spec.md`, `FuelTab_SourceFlowNotes.md`, `Reset_And_Session_Identity.md`, `TimerZeroBehavior.md`, `CarProfiles-Legacy-Map.md` (schema + storage).
- **Subsystem notes:** `Message_Catalog_v5_Signal_Mapping_Report.md`, `FuelTab_LeaderPaceFlow.md`, `FuelTabActionPlanOptions.md`, `FuelTabAnalysis.md`, `LALA-036-extra-time-sanity.md`, `LalaLaunch_Handover_Summary-20251130.docx`, `Subsystems/Pit_Entry_Assist.md`, `Subsystems/Track_Markers.md`, `Subsystems/Dash_Integration.md`, `Subsystems/MessageEngineV1_Notes.md`, `Subsystems/Profiles_And_PB.md`.
- **Workflow/process:** `BranchWorkflow.md`, `ConflictResolution.md`, `RepoStatus.md`.
- **Legacy / reference-only:** `FuelProperties_Spec.xlsx`, `FuelProperties_Spec (version 1).xlsx`, `Message_Catalog_v5.xlsx`, `Message_Catalog_v5_MessageToSignal_Map.csv`, `Message_Catalog_v5_Signals.csv`, `CarInfo_AllCars.xlsx`, `Codex_Task_Backlog-20251215.xlsx`, `Dahl Design → Lala Launch Mapping (lala Dash).docx`, `Dahl Design → Lala Launch Message Properties Mapping.docx`, `Dash Design.pptx`, `Dual Clutch Logic.docx`, `Phase 1 and 2 Test Script-20251202.docx`, `SessionResetIssues.docx`, `SimHub_DualClutch_Paddle_Guide.docx`, `TestingData.djson`, `UI Work.pptx`. Keep for reference; they are superseded by the canonical files above unless explicitly cited.
- **Archived:** leave everything under `/Docs/Archived` untouched.

## Subsystem map
| Subsystem | Purpose | Documentation link |
| --- | --- | --- |
| Fuel model | Live burn capture, stability selection, projection inputs, pit window state machine | [Subsystems/Fuel_Model.md](Subsystems/Fuel_Model.md) |
| Fuel planner tab | UI-facing planner sources, suggestions, and profile integration | [Subsystems/Fuel_Planner_Tab.md](Subsystems/Fuel_Planner_Tab.md) |
| Pit timing & pit-loss | PitEngine DTL/direct capture, pit-lite surface exports | [Subsystems/Pit_Timing_And_PitLoss.md](Subsystems/Pit_Timing_And_PitLoss.md) |
| Pace & projection | Pace windows, projection lap selection, after-zero handling | [Subsystems/Pace_And_Projection.md](Subsystems/Pace_And_Projection.md) |
| Launch mode | Launch state machine, trace logging, anti-stall/bog detection | [Subsystems/Launch_Mode.md](Subsystems/Launch_Mode.md) |
| Message system v1 | Evaluator-driven stack, outputs, and signal registry | [Subsystems/Message_System_V1.md](Subsystems/Message_System_V1.md) / [Subsystems/MessageEngineV1_Notes.md](Subsystems/MessageEngineV1_Notes.md) |
| Profiles & PB | Profile resolution, PB updates, and identity snapshots | [Subsystems/Profiles_And_PB.md](Subsystems/Profiles_And_PB.md) |
| Trace logging | Telemetry trace lifecycle and launch trace handling | [Subsystems/Trace_Logging.md](Subsystems/Trace_Logging.md) |
| Pit Entry Assist | Pit entry braking cues, margin/cue maths, decel capture instrumentation | [Subsystems/Pit_Entry_Assist.md](Subsystems/Pit_Entry_Assist.md) (driver/dash/engine) |
| Track markers | Auto-learned pit entry/exit markers (per track), locking, track-length change detection, MSGV1 notifications | [Subsystems/Track_Markers.md](Subsystems/Track_Markers.md) |
| Opponents | Nearby pace/fight prediction and pit-exit class position forecasting (Race-only, lap gate ≥1, gaps absolute) | [Subsystems/Opponents.md](Subsystems/Opponents.md) |
| CarSA | Car-based spatial awareness using SA-Core v2 distance gaps, gate-gap v2 relative proximity with precision slot-01 gaps, slot info visibility, class-rank StatusE labeling, and debug export | [Subsystems/CarSA.md](Subsystems/CarSA.md) |
| Dash integration | Screen manager modes, pit screen, dash visibility toggles (main/message/overlay), and Pit Entry Assist visual guidance | [Subsystems/Dash_Integration.md](Subsystems/Dash_Integration.md) |

## Canonical docs map
| Topic | Canonical file | Notes |
| --- | --- | --- |
| SimHub exports | [SimHubParameterInventory.md](SimHubParameterInventory.md) | Legacy spreadsheet removed; use this file only. |
| SimHub log catalogue | [SimHubLogMessages.md](SimHubLogMessages.md) | Use this list; no parallel copies. |
| Fuel model & pit rules | [FuelProperties_Spec.md](FuelProperties_Spec.md) | `FuelProperties_Spec.xlsx` and `FuelProperties_Spec (version 1).xlsx` are **LEGACY/REFERENCE ONLY**. |
| Fuel tab data flow | [FuelTab_SourceFlowNotes.md](FuelTab_SourceFlowNotes.md) | Earlier analysis docs remain for context only. |
| Reset & session identity | [Reset_And_Session_Identity.md](Reset_And_Session_Identity.md) | Central reset rules. |
| Timer zero behaviour | [TimerZeroBehavior.md](TimerZeroBehavior.md) | Cross-linked from reset/finish logic. |
| Message catalog signals | [Message_Catalog_v5_Signal_Mapping_Report.md](Message_Catalog_v5_Signal_Mapping_Report.md) | CSV/XLSX variants are **REFERENCE ONLY**. |
| Branching/merges | [BranchWorkflow.md](BranchWorkflow.md) / [ConflictResolution.md](ConflictResolution.md) | Canonical workflow/process guidance. |

## Change-control rules
- Export changes → update `SimHubParameterInventory.md`.
- Log changes → update `SimHubLogMessages.md`.
- Fuel logic changes → update `FuelProperties_Spec.md`.
- Reset / identity changes → update `Reset_And_Session_Identity.md`.
- Fuel tab/UI source or planner changes → update `FuelTab_SourceFlowNotes.md`.

## Freshness
- Validated against commit: 9d77f4a  
- Date: 2026-02-10  
- Branch: work
