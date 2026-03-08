# Architecture Guardrails

Validated against commit: cd7b02a
Last updated: 2026-03-07
Branch: work

## 1. Purpose
Keep future work architecture-aware without forcing a rewrite. This document exists to help Andy and coding agents extend LalaLaunch in a way that preserves current subsystem boundaries, avoids central-file sprawl, and keeps docs aligned with reality.

## 2. Current architectural reality
LalaLaunch is still a pragmatic plugin with significant orchestration in `LalaLaunch.cs`, supported by focused engines and helpers for major areas such as Fuel, Launch Mode, Shift Assist, Pit timing, Pit Entry Assist, Rejoin Assist, Opponents, CarSA, Messaging, Dash integration, and Trace Logging.

The repo already documents those areas as separate subsystems in `Docs/Subsystems/`. That documentation structure is the right local truth even where the runtime is not yet fully separated into small classes.

## 3. Core principle: separate as practical
Future work should keep these concerns distinct wherever practical:

- Input and normalization: raw telemetry reads, session-info parsing, track/profile lookup, and validation of incoming values.
- Domain and service logic: calculations, gating, latches, state machines, learning rules, projections, and alert decisions.
- State and persistence: profile JSON, marker stores, learned values, cached windows, and migration/compatibility handling.
- Output and export publishing: SimHub properties, dashboard-facing text/numbers/flags, logs, trace exports, and message publication.
- UI and settings: settings models, tab controls, actions, labels, and tooltips.

Do not collapse these concerns back together unless there is a concrete reason.

## 4. Responsibility model in LalaLaunch terms
- Fuel Model owns live burn capture, confidence, stable fuel selection, race projection, and pit window logic.
- Fuel Planner Tab owns human-selected planning inputs and deterministic planning outputs; it should not silently become the runtime fuel engine.
- Launch Mode owns launch-specific state and start logic; it should not absorb unrelated telemetry helpers.
- Shift Assist owns RPM cue evaluation, learning, audio routing inputs, and related exports/logs.
- Pit Timing and Pit Loss own pit-lane timing measurements and pit-loss publication.
- Pit Entry Assist owns braking-cue maths and entry-line driver guidance.
- Rejoin Assist owns incident classification, linger/suppression logic, and threat-aware rejoin warnings.
- Opponents owns race-only opponent calculations and pit-exit prediction.
- CarSA owns session-agnostic car-relative spatial awareness.
- Messaging owns message definitions, evaluation, prioritisation, and dash message outputs.
- Dash Integration owns dashboard-consumption guidance and visibility/rendering contracts, not the underlying business logic.
- Trace Logging owns low-frequency forensic capture, not live control decisions.

## 5. Subsystem ownership model
- Canonical subsystem behaviour belongs in `Docs/Subsystems/<name>.md`.
- Canonical export names belong in `Docs/SimHubParameterInventory.md`.
- Canonical log wording and meaning belong in `Docs/SimHubLogMessages.md`.
- Repo-wide working method belongs in `Docs/CODEX_CONTRACT.txt` and `Docs/Project_Index.md`.
- `Docs/RepoStatus.md` records current validated state and documentation sync status.
- `Docs/Code_Snapshot.md` is orientation only and must not override the canonical docs above.

When work touches multiple subsystems, update each owning doc instead of inventing a new catch-all design note unless the cross-cutting contract genuinely needs one.

## 6. Architecture guardrails for future work
- Start from the owning subsystem doc before editing code.
- Extend existing subsystem seams before adding new cross-cutting flags or helper layers.
- Prefer adding a focused helper/engine over adding more mixed responsibility into `LalaLaunch.cs`.
- Keep telemetry acquisition separate from decision logic where practical.
- Keep persistence/migration rules separate from runtime calculations.
- Keep dashboard/export formatting separate from the underlying calculation.
- Avoid creating new global coordinators for behaviour that clearly belongs to one subsystem.
- Do not move logic between subsystems for style reasons alone.
- Do not widen one subsystem into another subsystem's responsibility just because the code path is nearby.

## 7. Recommended gradual evolution path
- Document the owning subsystem first.
- If a file is doing multiple jobs, split only the part required for the task.
- Extract stable helpers around clear seams: telemetry normalization, calculation engines, persistence stores, or output publishers.
- Preserve public contracts while internals are being clarified.
- Keep changes incremental enough that `RepoStatus.md` and subsystem docs can stay accurate after each task.

## 8. Suggested future structure (conceptual only, no mandated rewrite)
Conceptually, the repo should continue trending toward:

- telemetry/input normalization
- subsystem engines/services
- persistence/profile stores
- output/export/log publishers
- UI/settings/actions

This is a direction, not a rewrite order. Existing code may remain more integrated until a scoped task justifies separation.

## 9. Guidance for agent-authored tasks
- Read `Docs/Project_Index.md` first, then the contract, then the affected subsystem docs.
- Do a no-edit analysis pass for non-trivial tasks.
- State affected paths, invariants, and edit boundaries before changing files.
- Prefer subsystem-local changes over adding repo-global prose.
- If a task changes behaviour, update subsystem docs plus export/log docs as needed.
- If canonical docs and `Code_Snapshot.md` disagree, follow the canonical docs and treat the snapshot as stale.
- Do not propose broad architecture rewrites unless the task explicitly asks for one.

## 10. Practical warning signs of architectural drift
- `LalaLaunch.cs` gains new mixed responsibilities that belong to an existing subsystem.
- A subsystem doc no longer matches the place where the real rules live.
- Export naming, logging, persistence, and runtime logic all change together inside one large edit with no clear ownership boundary.
- Dashboard-specific behaviour starts altering core calculations.
- Race-only logic leaks into session-agnostic systems such as CarSA.
- Opponents and CarSA start duplicating the same spatial logic instead of keeping their distinct scopes.
- Fuel runtime logic and planner logic begin overriding each other instead of using the documented live-vs-planner contract.
- Message routing, Pit Entry cues, or Shift Assist audio decisions start depending on undocumented side paths.

When these signs appear, fix the narrow ownership problem first. Do not default to a repo-wide restructure.
