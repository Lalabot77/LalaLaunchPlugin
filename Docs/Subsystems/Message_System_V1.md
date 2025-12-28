# Message System V1

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1
Last updated: 2025-12-28
Branch: docs/refresh-index-subsystems

## Purpose
The Message System V1 delivers **context-aware, non-spammy driver messages** based on:
- Fuel state
- Pit state
- Session context
- User acknowledgement

It is intentionally conservative and contract-driven.

---

## Inputs

- Fuel Model outputs
- Pit state and stop requirements
- Session type and phase
- Message catalog definitions
- Driver cancel / acknowledge actions

---

## Internal State

- Message eligibility flags
- Per-session latch states
- Cooldown timers
- Cancelled / suppressed messages

---

## Logic Blocks

### 1) Message Eligibility
A message may trigger only if:
- Session context matches (e.g. Race only)
- Required inputs are valid
- Message has not already latched this session

---

### 2) Display Rules
Messages specify:
- Minimum visible duration
- Cancel behaviour (manual / session end)
- One-shot vs repeatable

---

### 3) Suppression
Messages are suppressed if:
- Driver cancels them
- A higher-priority message is active
- Cooldown timer is active

---

## Outputs

- Active message ID
- Message text / severity
- Cancel visibility flag
- Logs explaining trigger/suppression

---

## Reset Rules

- All message latches reset on session identity change
- Cancel state may persist per-session only

---

## Failure Modes

- Over-eager triggers → prevented by latching
- Missing evaluators → treated as silent placeholders
- Replay sessions → verify via logs

---

## Test Checklist

- One-shot fuel messages fire once per race
- Cancel suppresses repeat
- Session reset clears latches

---

## TODO / VERIFY

- TODO/VERIFY: Confirm priority ordering between fuel vs pit messages.
- TODO/VERIFY: Confirm exact cooldown defaults per message type.
# Message System V1

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1  
Last updated: 2025-12-28  
Branch: docs/refresh-index-subsystems

## Purpose
Evaluate message definitions against telemetry and plugin signals, maintain active stack, and publish styled SimHub exports for Lala/Msg dashes.

## Inputs (source + cadence)
- Message definitions from `MessageEngine` (evaluators + registry) loaded at init. Evaluators consume SimHub properties (flags, pace mode, gaps) and plugin exports (pit window, fuel deltas, rejoin threat, pace averages).【F:Messaging/MessageEngine.cs†L430-L560】【F:Docs/Message_Catalog_v5_Signal_Mapping_Report.md†L7-L80】
- MsgCx action pulses for cancel/override (per action press).【F:LalaLaunch.cs†L87-L118】
- Planning source and profile changes indirectly affect signals (e.g., fuel deltas).

## Internal state
- Evaluator dictionary with placeholder evaluators for missing IDs (logged).
- Outputs struct (`MessageEngineOutputs`) containing active text, priorities, colors, font sizes, stack CSV, and missing-evaluator CSV.【F:Messaging/MessageEngine.cs†L478-L560】
- Session reset hook to clear stack when session type/token changes.【F:LalaLaunch.cs†L3308-L3365】【F:LalaLaunch.cs†L3649-L3676】

## Calculation blocks (high level)
1. **Evaluator registration:** Build evaluator map; missing evaluators replaced with placeholders and logged.【F:Messaging/MessageEngine.cs†L430-L520】
2. **Signal ingestion:** Signals pulled from SimHub/plugin exports each evaluation tick (cadence controlled by host—TODO/VERIFY cadence in runtime loop).【F:Messaging/MessageEngine.cs†L430-L520】
3. **Message activation:** Evaluators set active messages, priorities, styles; outputs written to exports.
4. **Cancel handling:** MsgCx triggers `_msgV1Engine.OnMsgCxPressed()` to clear/cancel messages per engine rules.【F:LalaLaunch.cs†L87-L118】

## Outputs (exports + logs)
- Exports: `MSGV1.ActiveText_*`, priority, IDs, colors, font sizes, `ActiveCount`, `LastCancelMsgId`, `ClearAllPulse`, `StackCsv`, `MissingEvaluatorsCsv` (see inventory).【F:LalaLaunch.cs†L2919-L2940】
- Logs: `[LalaPlugin:MSGV1] Registered placeholder evaluators: ...` and other engine-level messages.【F:Messaging/MessageEngine.cs†L499-L560】
- Signal mappings: see `Docs/Message_Catalog_v5_Signal_Mapping_Report.md`.

## Dependencies / ordering assumptions
- Requires plugin exports (fuel deltas, pit window, pace) to be up-to-date; relies on `LalaLaunch` 500 ms loop feeding those exports.
- Session reset must call `ResetSession` to avoid stale stack; triggered on session type/token change in `LalaLaunch`.

## Reset rules
- `ResetSession()` invoked on session type change and session token change; clears outputs/stack and missing-evaluator CSV.【F:LalaLaunch.cs†L3308-L3365】【F:LalaLaunch.cs†L3649-L3676】

## Failure modes
- Missing evaluator IDs -> placeholders; logged with evaluator→message mapping.
- Signals marked TBD in catalog (e.g., incident-ahead) have no evaluator yet; messages will never fire until implemented.

## Test checklist
- Load plugin and check log for missing evaluators; verify `MissingEvaluatorsCsv` export matches log.
- Trigger MsgCx action and confirm `ClearAllPulse` or cancel IDs update, stack clears.
- Simulate pit window open, fuel deficit, or flag changes to see message activations per catalog.
