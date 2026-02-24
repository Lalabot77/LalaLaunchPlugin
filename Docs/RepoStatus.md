# Repository status

Validated against commit: e2b71f1
Last updated: 2026-02-24
Branch: work

## Current repo/link status
- Local branch present: `work`.
- No Git remote is configured in this checkout (`git remote -v` returns empty).
- HEAD includes post-PR426 updates, including Shift Assist learning controls, lock/reset actions, per-gear ShiftRPM exports, and debug visibility toggles.

## Documentation sync status (requested set)
- `SimHubParameterInventory.md` — refreshed to current head/date and includes Shift Assist export inventory.
- `SimHubLogMessages.md` — refreshed and expanded Shift Assist log coverage (toggle/test/delay/audio/error paths).
- `Code_Snapshot.md` — regenerated as non-canonical orientation snapshot for current head.
- `Plugin_UI_Tooltips.md` — refreshed in-repo tooltip inventory (line references + Shift Assist control section).
- `Project_Index.md` — updated subsystem map and canonical entry points with Shift Assist subsystem doc link.
- `Subsystems/Shift_Assist.md` — refreshed in the standard subsystem format with latest export/log coverage.

## Delivery status highlights
- ShiftAssist.Beep restored as audio-issued pulse; ShiftLight exports now canonical.
- Shift Assist shift light routing now supports per-profile mode selection (Primary/Urgent/Both), with backward-compatible `ShiftAssist.Beep` plus dedicated `ShiftAssist.BeepPrimary` and `ShiftAssist.BeepUrgent` exports.
- ShiftAssist debug CSV now logs urgent eligibility/attempt/outcome and timing anchors.
- Shift Assist urgent cue now enforces the fixed 1000ms delay inside `ShiftAssistEngine` (preventing early consumption), keeps cue-dependent playback gating in `LalaLaunch`, and keeps urgent volume derived from the primary slider (50%).
- Shift Assist subsystem: **INTEGRATED** (settings, evaluation, audio, exports, logs, delay telemetry).
- Declutter mode + event marker actions: **COMPLETE** (post-PR381 baseline retained).
- Canonical docs listed above: **SYNCED** to `c70b16d`.

## Notes
- `Code_Snapshot.md` remains intentionally non-canonical; contract truth lives in parameter/log inventories and subsystem docs.
