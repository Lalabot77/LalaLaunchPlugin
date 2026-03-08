# Repository status

Validated against commit: 5a8c6fd
Last updated: 2026-03-08
>>>>>>> b67acec7d1d25150a0bbc5db340a072e27f8e8fb
Branch: work

## Current repo/link status
- Local branch present: `work`.
- No Git remote is configured in this checkout (`git remote -v` returns empty).
- HEAD includes the existing post-PR426 plugin/runtime changes already documented in the canonical subsystem and inventory docs.

## Documentation sync status (requested set)
- `AGENTS.md` - added at repo root as the always-read agent entry point that directs tools into the canonical docs workflow without duplicating policy.
- `CODEX_CONTRACT.txt` - strengthened as the permanent global Codex policy file and now defines mandatory analysis/start-order workflow rules.
- `Architecture_Guardrails.md` - added as the lightweight architecture and subsystem-ownership guardrail for humans and agents.
- `CODEX_TASK_TEMPLATE.txt` - added as the reusable analysis-first task skeleton for future Codex work.
- `Project_Index.md` - updated with explicit root-`AGENTS.md` cross-reference, read/start order, and links to the workflow docs.
- `SimHubParameterInventory.md` - retained as the canonical SimHub export contract.
- `SimHubLogMessages.md` - retained as the canonical Info/Warn/Error log catalogue.
- `Code_Snapshot.md` - retained as a non-canonical orientation snapshot only.
- `Subsystems/*.md` - retained as the canonical subsystem-local truth for affected areas.

## Delivery status highlights

- Root `AGENTS.md` added as the top-level agent entry point for Codex Desktop and future agent tooling.
- Docs/workflow standardisation pass completed with no plugin/runtime/code changes.
- Codex working method is now explicit: read `AGENTS.md`, start from `Project_Index.md`, follow `CODEX_CONTRACT.txt`, use `Architecture_Guardrails.md`, read affected subsystem docs first, use `Code_Snapshot.md` only as non-canonical orientation, and close by syncing `RepoStatus.md`.
- Architecture guardrails are now documented separately so future tasks can stay subsystem-aware without forcing a rewrite.
- Existing subsystem docs and canonical docs were preserved; no documentation files were removed in this pass.
- Canonical docs and agent entry docs listed above: **SYNCED** to `dce8db8`.
=======
=======
>>>>>>> b67acec7d1d25150a0bbc5db340a072e27f8e8fb
- Dark Mode global dash controls updated with Lovely checkbox always visible (availability-gated by enable state), forced-off persistence when Lovely disappears, and docs alignment across inventory/log/tooltip/subsystem/index docs.
- ShiftAssist debug CSV now logs urgent eligibility/attempt/outcome and timing anchors.
- Shift Assist learning acceptance widened (throttle/brake micro-noise tolerance, movement gate, limiter-hold + timeout grace, artifact-reset cancellation) and learned RPM now uses telemetry crossover (`gear g` vs `g+1`) with stability-gated auto-apply.
- PR454 follow-up hardening: `MinWindowMs=250ms`, limiter-hold continuation capped (`2000ms`), crossover scan band now anchored to sampling-gear redline, and pull acceptance requires minimum valid curve points.
- PR454 follow-up #2: brake hysteresis timing (`>2%` enter / `<1%` exit, `100ms`), limiter-hold cap enforced on total limiter time, and debug semantics clarified with explicit `PullAccepted` (with `SampleAdded` compatibility mirror).
- Shift Assist Learning v2 regression fix: stable adjacent-gear solve now drives auto-apply signaling even when solved RPM equals already-cached `LearnedRpm`, preventing missed apply candidates after target edits/resets while keeping no-fallback solve rules.
- Shift Assist urgent cue now enforces the fixed 1000ms delay inside `ShiftAssistEngine` (preventing early consumption), keeps cue-dependent playback gating in `LalaLaunch`, and keeps urgent volume derived from the primary slider (50%).
- Shift Assist subsystem: **INTEGRATED** (settings, evaluation, audio, exports, logs, delay telemetry).
- Declutter mode + event marker actions: **COMPLETE** (post-PR381 baseline retained).
- Canonical docs listed above: **SYNCED** to `5a8c6fd`.

=======
>>>>>>> b67acec7d1d25150a0bbc5db340a072e27f8e8fb

## Notes
- Canonical agent/doc hierarchy is now: `AGENTS.md` -> `Docs/Project_Index.md` -> `Docs/CODEX_CONTRACT.txt` / `Docs/Architecture_Guardrails.md` / relevant `Docs/Subsystems/*.md` -> `Docs/RepoStatus.md`, with `Docs/CODEX_TASK_TEMPLATE.txt` used for explicit task framing.
- `Docs/Code_Snapshot.md` remains intentionally non-canonical; contract truth lives in the canonical inventories, subsystem docs, and repo workflow docs.
- Potential consolidation candidates were observed but left untouched where the overlap is not unquestionably safe to retire.
