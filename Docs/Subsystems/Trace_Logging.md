# Trace Logging

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1  
Last updated: 2025-12-28  
Branch: docs/refresh-index-subsystems

## Purpose
Capture and manage telemetry trace files for launch analysis, including lifecycle hooks during launch mode and manual deletion from UI.

## Inputs (source + cadence)
- Launch state transitions from `LalaLaunch` (primed/logging/completed/cancelled).【F:LalaLaunch.cs†L2470-L2510】【F:LalaLaunch.cs†L3045-L3074】
- Telemetry samples during launch (`DataUpdate` per tick) streamed into `TelemetryTraceLogger.Update(...)` while state is Logging.【F:LalaLaunch.cs†L3572-L3664】
- UI command to delete a trace file via `LaunchAnalysisControl` button click.【F:LaunchAnalysisControl.xaml.cs†L55-L70】

## Internal state
- `TelemetryTraceLogger` service (see `TelemetryLogger.cs`) holding current trace, target file path, and pending discard flag.
- `_telemetryTraceLogger` owned by `LalaLaunch`; lifecycle managed on init/end and launch abort.
- `_launchAbortLatched` prevents duplicate cancel logs when discarding a trace.【F:LalaLaunch.cs†L3045-L3074】

## Calculation blocks (high level)
1. **Start trace:** When launch transitions to Logging, trace logger begins collecting telemetry rows (speed, clutch, throttle, RPM, traction).【F:TelemetryLogger.cs†L1-L120】【F:LalaLaunch.cs†L3572-L3664】
2. **Stop/commit:** On launch completion or plugin end, logger stops service; optional discard invoked during cleanup.
3. **Abort/cancel:** `CancelLaunchToIdle` stops trace and discards current file, logging reason once.【F:LalaLaunch.cs†L3045-L3074】
4. **Manual delete:** UI delete command removes a saved trace file and logs `[LaunchTrace] Deleted trace file: <path>`.【F:LaunchAnalysisControl.xaml.cs†L55-L70】

## Outputs (exports + logs)
- Files: launch trace CSVs (location configured in `TelemetryTraceLogger` settings — TODO/VERIFY exact path in current build).
- Logs: launch trace cancel reasons, manual delete log; launch state logs indicate trace start/stop conditions.【F:LalaLaunch.cs†L3045-L3074】【F:LaunchAnalysisControl.xaml.cs†L55-L70】

## Dependencies / ordering assumptions
- `TelemetryTraceLogger` instantiated during plugin init; disposed in `End()` before plugin unload.【F:LalaLaunch.cs†L2585-L2633】【F:LalaLaunch.cs†L3006-L3035】
- Launch state machine drives trace lifecycle; no independent scheduler.

## Reset rules
- Session token/type changes do not automatically discard traces unless launch abort occurs; plugin end discards/ends current trace.

## Failure modes
- Aborted launch without valid state could leave partial trace unless discard executed; `_launchAbortLatched` mitigates log spam.
- TODO/VERIFY: Confirm behaviour when multiple launches occur in one session (file naming/rotation).

## Test checklist
- Trigger a full launch and verify trace file appears; open in analysis UI.
- Abort a primed launch to confirm trace discard log and no leftover file.
- Use UI delete button to remove a trace and check log entry.
