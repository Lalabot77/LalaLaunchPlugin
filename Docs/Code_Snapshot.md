# SNAPSHOT / GENERATED / NON-CANONICAL

# Code Snapshot

- Source commit/PR: 1617166 (post-PR404 workspace head)
- Generated date: 2026-02-16
- Regeneration: manual snapshot; no regen pipeline defined
- Branch: work

If this conflicts with `Project_Index.md` or canonical contract docs, treat this file as stale.

## Architectural notes (high level)
- Core runtime remains centered in `LalaLaunch.cs` with subsystem engines for pit, rejoin, CarSA, opponents, and messaging.
- Shift Assist is now a first-class subsystem: runtime evaluator (`ShiftAssistEngine`), audio resolver/player (`ShiftAssistAudio`), settings plumbing in `LaunchPluginSettings`, per-tick exports, action bindings, and delay telemetry capture for tuning.
- Canonical signal and log contracts are maintained in `SimHubParameterInventory.md` and `SimHubLogMessages.md`; this file is a quick orientation snapshot only.

## Since PR404 (docs refresh delta)
- Refreshed canonical docs metadata and validation hashes to the current workspace head/date.
- Updated Shift Assist subsystem documentation for learning mode, debug telemetry exports, and debug CSV behavior.
- Synced Shift Assist inventory/log docs with current exports and log lines (including debug CSV toggle and audio-delay telemetry).
- Refreshed plugin tooltip inventory, project index, and repository status so the documentation set stays aligned.
