# SNAPSHOT / GENERATED / NON-CANONICAL

# Code Snapshot

- Source commit/PR: 2a38742 (post-PR381 workspace head)
- Generated date: 2026-02-14
- Regeneration: manual snapshot; no regen pipeline defined
- Branch: work

If this conflicts with `Project_Index.md` or canonical contract docs, treat this file as stale.

## Architectural notes (high level)
- Core runtime remains centered in `LalaLaunch.cs` with subsystem engines for pit, rejoin, CarSA, opponents, and messaging.
- Shift Assist is now a first-class subsystem: runtime evaluator (`ShiftAssistEngine`), audio resolver/player (`ShiftAssistAudio`), settings plumbing in `LaunchPluginSettings`, per-tick exports, action bindings, and delay telemetry capture for tuning.
- Canonical signal and log contracts are maintained in `SimHubParameterInventory.md` and `SimHubLogMessages.md`; this file is a quick orientation snapshot only.

## Since PR381 (docs-oriented delta)
- Added Shift Assist subsystem documentation and linked it into the project index.
- Refreshed canonical docs metadata to current head/date.
- Synced Shift Assist log coverage with code (toggle/test beep/beep trigger/delay sample/audio fallback/errors).
- Refreshed repository status and index references so docs align with current branch state.
