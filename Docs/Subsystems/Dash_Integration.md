# Dash Integration

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1  
Last updated: 2025-12-28  
Branch: docs/refresh-index-subsystems

## Purpose
Coordinate dash pages/modes, pit screen visibility, and dash visibility toggles for Lala and Messaging dashes.

## Inputs (source + cadence)
- User actions: MsgCx, TogglePitScreen, PrimaryDashMode, SecondaryDashMode, LaunchMode (actions registered at init).【F:LalaLaunch.cs†L2585-L2633】
- Telemetry flags: ignition on/off, engine started, pit lane status, session type (per tick in `DataUpdate`).【F:LalaLaunch.cs†L3688-L3735】
- Settings: dash visibility toggles and auto-dash enable flag from `LaunchPluginSettings`.【F:LalaLaunch.cs†L2832-L2849】

## Internal state
- `ScreenManager` with `Mode` (`manual`/`auto`) and `CurrentPage`; `_dashPendingSwitch`, `_dashExecutedForCurrentArm`, `_dashDesiredPage`, `_dashSwitchToken` manage auto-switch debounce.【F:LalaLaunch.cs†L3688-L3735】
- Pit screen flags: `_pitScreenDismissed`, `_pitScreenManualEnabled`, `_pitScreenActive`, `_pittingTimer` for entry debounce.【F:LalaLaunch.cs†L47-L87】【F:LalaLaunch.cs†L3734-L3763】
- Last session type `_dashLastSessionType` to re-arm auto-dash per session change.【F:LalaLaunch.cs†L3680-L3685】

## Calculation blocks (high level)
1. **Action handling:** Primary/Secondary dash actions currently placeholders (log only). PitScreen toggle differentiates in-pits vs on-track behaviour; MsgCx action latched for 500 ms for messaging engines.【F:LalaLaunch.cs†L10-L118】
2. **Auto-dash arming:** Session-type change sets desired page (practice/timing/racing) and arms auto switch; ignition off re-arms for next start.【F:LalaLaunch.cs†L3688-L3735】
3. **Auto-dash execution:** On ignition-on/engine-start, sets mode=auto, page=desired, then reverts to manual after delay if token unchanged; logs both actions.【F:LalaLaunch.cs†L3688-L3730】
4. **Pit screen visibility:** In pits, shows screen after 200 ms unless dismissed; on track, mirrors manual enable; state changes logged.【F:LalaLaunch.cs†L47-L87】【F:LalaLaunch.cs†L3734-L3763】

## Outputs (exports + logs)
- Exports: `CurrentDashPage`, `DashControlMode`, `PitScreenActive`, `MsgCxPressed`, visibility toggles (`LalaDashShow*`, `MsgDashShow*`). See inventory.
- Logs: dash action placeholders, auto-dash arming/execution, pit screen toggle/active changes.【F:LalaLaunch.cs†L10-L118】【F:LalaLaunch.cs†L3688-L3735】

## Dependencies / ordering assumptions
- Auto-dash uses session type from telemetry; requires settings to enable switching.
- Pit screen relies on SimHub pit lane flag `IsOnPitRoad`.

## Reset rules
- Session token change clears pit screen dismissed flag and snapshot labels; auto-dash re-armed on session-type change.【F:LalaLaunch.cs†L3308-L3365】【F:LalaLaunch.cs†L3680-L3730】

## Failure modes
- Placeholder dash actions provide no functional change yet; only log.
- Pit lane flag glitches could flicker pit screen; entry debounce helps mitigate.

## Test checklist
- Toggle pit screen in pits vs on track to confirm log and `PitScreenActive` behaviour.
- Enable auto-dash, change session type, cycle ignition to observe auto page switch and manual reversion.
- Verify visibility toggle exports change when settings flipped in UI.
