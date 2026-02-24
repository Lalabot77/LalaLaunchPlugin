# Repository status

Validated against commit: b9250e1
Last updated: 2026-02-24
Branch: work

## Current repo/link status
- Local branch present: `work`.
- No Git remote is configured in this checkout (`git remote -v` returns empty).
- HEAD includes post-PR426 updates, including Shift Assist learning controls, lock/reset actions, per-gear ShiftRPM exports, and debug visibility toggles.

## Documentation sync status (requested set)
- `SimHubParameterInventory.md` — refreshed to current head/date and includes Shift Assist export inventory.
- `SimHubLogMessages.md` — refreshed and expanded Shift Assist + Dark Mode log coverage (actions, Lovely availability, auto transitions).
- `Code_Snapshot.md` — regenerated as non-canonical orientation snapshot for current head.
- `Plugin_UI_Tooltips.md` — refreshed in-repo tooltip inventory (line references + Shift Assist control section).
- `Project_Index.md` — updated subsystem map including Dash integration dark-mode scope.
- `Subsystems/Shift_Assist.md` — refreshed in the standard subsystem format with latest export/log coverage.
- `Subsystems/Dash_Integration.md` — updated with Dark Mode export contract and dashboard-consumption guidance.

## Delivery status highlights
- Dark Mode global dash controls updated with Lovely checkbox always visible (availability-gated by enable state), forced-off persistence when Lovely disappears, and docs alignment across inventory/log/tooltip/subsystem/index docs.
- ShiftAssist debug CSV now logs urgent eligibility/attempt/outcome and timing anchors.
- Shift Assist urgent cue now enforces the fixed 1000ms delay inside `ShiftAssistEngine` (preventing early consumption), keeps cue-dependent playback gating in `LalaLaunch`, and keeps urgent volume derived from the primary slider (50%).
- Shift Assist subsystem: **INTEGRATED** (settings, evaluation, audio, exports, logs, delay telemetry).
- Declutter mode + event marker actions: **COMPLETE** (post-PR381 baseline retained).
- Canonical docs listed above: **SYNCED** to `b9250e1`.

## Notes
- `Code_Snapshot.md` remains intentionally non-canonical; contract truth lives in parameter/log inventories and subsystem docs.
