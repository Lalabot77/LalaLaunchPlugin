# Project Index

Validated against commit: b31a0be584c2941a7d8d4d4c5dde2e852d7b32a2  
Last updated: 2026-02-08  
Branch: Opponents-Module

## What this repo is
LalaLaunchPlugin is a SimHub plugin for iRacing that provides launch control instrumentation, fuel strategy calculations, pit-cycle timing, rejoin assistance, messaging outputs, and dash/screen coordination across the Fuel, Pit, Launch, and Messaging tabs. Runtime behaviour lives in C# (e.g., `LalaLaunch.cs`, `FuelCalcs.cs`, `PitEngine.cs`) with SimHub exports documented below.

## Start here
- [SimHubParameterInventory.md](SimHubParameterInventory.md) — canonical export list.
- [SimHubLogMessages.md](SimHubLogMessages.md) — canonical log catalogue.
- [FuelProperties_Spec.md](FuelProperties_Spec.md) — canonical fuel model behaviour.
- [FuelTab_SourceFlowNotes.md](FuelTab_SourceFlowNotes.md) — canonical Fuel tab source flow.
- [Reset_And_Session_Identity.md](Reset_And_Session_Identity.md) — canonical reset/session rules.
- [Pit_Entry_Assist.md](Pit_Entry_Assist.md) — driver/dash/log spec for pit entry braking cues.
- [Dash_Integration.md](Dash_Integration.md) — dash consumption and visualisation contracts (Pit Entry Assist included).
- [Subsystems/Track_Markers.md](Subsystems/Track_Markers.md) — pit entry/exit marker auto-learn, storage, locking, and MSGV1 notifications.
- [Subsystems/Opponents.md](Subsystems/Opponents.md) — nearby pace/fight and pit-exit prediction (Race-only gate, lap ≥2).
- [Subsystems/Message_System_V1.md](Subsystems/Message_System_V1.md) — notification layer for pit markers and other signals (definition-driven, no legacy messages).
- [BranchWorkflow.md](BranchWorkflow.md) — branching policy.
- [ConflictResolution.md](ConflictResolution.md) — merge/conflict process.

## Doc inventory & canonicalisation
- **Truth docs:** `SimHubParameterInventory.md`, `SimHubLogMessages.md`, `FuelProperties_Spec.md`, `FuelTab_SourceFlowNotes.md`, `Reset_And_Session_Identity.md`, `TimerZeroBehavior.md`.
- **Subsystem notes:** `Message_Catalog_v5_Signal_Mapping_Report.md`, `FuelTab_LeaderPaceFlow.md`, `FuelTabActionPlanOptions.md`, `FuelTabAnalysis.md`, `LALA-036-extra-time-sanity.md`, `LalaLaunch_Handover_Summary-20251130.docx`, `Pit_Entry_Assist.md`, `Subsystems/Track_Markers.md`, `Dash_Integration.md`, `Profiles_And_PB.md`.
- **Workflow/process:** `BranchWorkflow.md`, `ConflictResolution.md`, `RepoStatus.md`.
- **Legacy / reference-only:** `SimHub_Parameter_Inventory.xlsx`, `FuelProperties_Spec.xlsx`, `FuelProperties_Spec (version 1).xlsx`, `Message_Catalog_v5.xlsx`, `Message_Catalog_v5_MessageToSignal_Map.csv`, `Message_Catalog_v5_Signals.csv`, `CarInfo_AllCars.xlsx`, `Codex_Task_Backlog-20251215.xlsx`, `Dahl Design → Lala Launch Mapping (lala Dash).docx`, `Dahl Design → Lala Launch Message Properties Mapping.docx`, `Dash Design.pptx`, `Dual Clutch Logic.docx`, `Phase 1 and 2 Test Script-20251202.docx`, `SessionResetIssues.docx`, `SimHub_DualClutch_Paddle_Guide.docx`, `TestingData.djson`, `UI Work.pptx`. Keep for reference; they are superseded by the canonical files above unless explicitly cited.
- **Archived:** leave everything under `/Docs/Archived` untouched.

## Subsystem map
| Subsystem | Purpose | Documentation link |
| --- | --- | --- |
| Fuel model | Live burn capture, stability selection, projection inputs, pit window state machine | [Subsystems/Fuel_Model.md](Subsystems/Fuel_Model.md) |
| Fuel planner tab | UI-facing planner sources, suggestions, and profile integration | [Subsystems/Fuel_Planner_Tab.md](Subsystems/Fuel_Planner_Tab.md) |
| Pit timing & pit-loss | PitEngine DTL/direct capture, pit-lite surface exports | [Subsystems/Pit_Timing_And_PitLoss.md](Subsystems/Pit_Timing_And_PitLoss.md) |
| Pace & projection | Pace windows, projection lap selection, after-zero handling | [Subsystems/Pace_And_Projection.md](Subsystems/Pace_And_Projection.md) |
| Launch mode | Launch state machine, trace logging, anti-stall/bog detection | [Subsystems/Launch_Mode.md](Subsystems/Launch_Mode.md) |
| Message system v1 | Evaluator-driven stack, outputs, and signal registry | [Subsystems/Message_System_V1.md](Subsystems/Message_System_V1.md) |
| Profiles & PB | Profile resolution, PB updates, and identity snapshots | [Subsystems/Profiles_And_PB.md](Subsystems/Profiles_And_PB.md) |
| Trace logging | Telemetry trace lifecycle and launch trace handling | [Subsystems/Trace_Logging.md](Subsystems/Trace_Logging.md) |
| Pit Entry Assist | Pit entry braking cues, margin/cue maths, decel capture instrumentation | [Pit_Entry_Assist.md](Pit_Entry_Assist.md) (driver/dash) / [Subsystems/Pit_Entry_Assist.md](Subsystems/Pit_Entry_Assist.md) (engine) |
| Track markers | Auto-learned pit entry/exit markers (per track), locking, track-length change detection, MSGV1 notifications | [Subsystems/Track_Markers.md](Subsystems/Track_Markers.md) |
| Opponents | Nearby pace/fight prediction and pit-exit class position forecasting (Race-only, lap gate ≥2, gaps absolute) | [Subsystems/Opponents.md](Subsystems/Opponents.md) |
| Dash integration | Screen manager modes, pit screen, dash visibility toggles, and Pit Entry Assist visual guidance | [Dash_Integration.md](Dash_Integration.md) / [Subsystems/Dash_Integration.md](Subsystems/Dash_Integration.md) |

## Canonical docs map
| Topic | Canonical file | Notes |
| --- | --- | --- |
| SimHub exports | [SimHubParameterInventory.md](SimHubParameterInventory.md) | `SimHub_Parameter_Inventory.xlsx` is **LEGACY/REFERENCE ONLY**. |
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
- Validated against commit: b31a0be584c2941a7d8d4d4c5dde2e852d7b32a2  
- Date: 2026-02-08  
- Branch: Opponents-Module
