# Launch Mode

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1  
Last updated: 2025-12-28  
Branch: docs/refresh-index-subsystems

## Purpose
State machine and telemetry capture for launch control: priming, logging, metrics (reaction, RPM/throttle windows), anti-stall/bog detection, and manual timeout.

## Inputs (source + cadence)
- User actions: `LaunchMode` toggle (SimHub action), MsgCx, dashboard primary/secondary actions (placeholders).【F:LalaLaunch.cs†L10-L45】【F:LalaLaunch.cs†L87-L118】
- Telemetry per tick: clutch, throttle, speed, start flags, anti-stall threshold from profile, time since prime (manual timeout).【F:LalaLaunch.cs†L3572-L3664】【F:LalaLaunch.cs†L4989-L5015】
- Profile targets: target RPM/throttle, bite point/tolerances, bog factor, anti-stall threshold.【F:LalaLaunch.cs†L2845-L2895】

## Internal state
- Launch state enum (Idle, ManualPrimed, AutoPrimed, InProgress, Logging, Completed, Cancelled) and user-disabled latch for toggle behaviour.【F:LalaLaunch.cs†L2470-L2510】
- Timers: manual prime 30 s timeout, reaction/clutch timing, trace logger lifecycle flags.【F:LalaLaunch.cs†L3045-L3074】【F:LalaLaunch.cs†L4989-L5015】
- Metrics: RPM/throttle at clutch release, deviations, zero-to-100 time/delta, traction loss, anti-stall detection, wheel spin, bog detection flags.【F:LalaLaunch.cs†L2845-L2895】

## Calculation blocks (high level)
1. **Action handling:** LaunchMode toggles between primed and aborted states with pit/rejoin guards; state changes logged.【F:LalaLaunch.cs†L10-L45】【F:LalaLaunch.cs†L2470-L2494】
2. **Launch start detection:** When primed and clutch/throttle move past thresholds, enter Logging; trace logger updates each tick during launch.【F:LalaLaunch.cs†L3572-L3664】
3. **Metric capture:** Record RPM/throttle snapshots, timing deltas, anti-stall, wheel spin, bog factor during launch run; surface via SimHub exports.【F:LalaLaunch.cs†L2845-L2895】
4. **Timeout/abort:** Manual prime timeout cancels launch and logs; CancelLaunchToIdle logs reason once.【F:LalaLaunch.cs†L3045-L3074】【F:LalaLaunch.cs†L4989-L5015】

## Outputs (exports + logs)
- Exports: launch metrics, manual timeout remaining, state labels/codes, zero-to-100 metrics (see inventory). Logs: launch mode button events, state changes, trace cancel reasons, timeout log.【F:LalaLaunch.cs†L10-L45】【F:LalaLaunch.cs†L2470-L2510】【F:LalaLaunch.cs†L3045-L3074】【F:LalaLaunch.cs†L4989-L5015】

## Dependencies / ordering assumptions
- Trace logger (`TelemetryTraceLogger`) must exist; start/stop handled inside launch code (see `Trace_Logging` stub). Launch metrics depend on active profile thresholds.

## Reset rules
- `ResetAllValues` clears last-run metrics and abort latch; called during init and when leaving sessions via broader resets.【F:LalaLaunch.cs†L3045-L3094】
- Session token/type changes do not directly alter launch state but pits/rejoin guards can block priming.

## Failure modes
- Placeholder dash actions currently log only; no functional switch.
- TODO/VERIFY: Confirm integration point for auto-primed state (currently manual toggle dominates).【F:LalaLaunch.cs†L2470-L2510】

## Test checklist
- Trigger LaunchMode in pits and on track to see block vs. primed logs.
- Perform a launch to capture metrics and verify exports update; ensure manual timeout cancels after 30 s.
- Abort launch (LaunchMode again) and confirm trace cancel log fires once.
