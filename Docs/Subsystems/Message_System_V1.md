# Message System V1

Validated against commit: 9f784a9  
Last updated: 2026-01-14  
Branch: work

## Purpose
Evaluate message definitions against telemetry and plugin signals, maintain an active stack, and publish styled SimHub exports for Lala/Msg dashes.

---

## Inputs
- Fuel model outputs, pit state, session type, and other plugin exports.
- Message definitions (JSON) loaded at init.
- MsgCx action pulses (driver cancel/override).
- Pit marker pulses (`TrackMarkers.Pulse.*`) produced by `PitEngine`/`LalaLaunch`.
- Legacy MSG lane signals from `MessagingSystem` (e.g., `MSG.OvertakeApproachLine`, `MSG.OtherClassBehindGap`) feeding catalog evaluators; there is no `MSGOtherClassBehindGap` alias.

---

## Storage (definitions JSON)
- **Current file:** `PluginsData/Common/LalaPlugin/Messages.json` (managed by `MessageDefinitionStore`).
- **Legacy file:** `LalaLaunch.Messages.json` in the common storage root; migrated on load if the new file is missing.
- Definitions are saved as a schema root wrapper (schema version 1) with `Messages[]` and defaulted properties for missing values.

---

## Internal state
- Evaluator dictionary with placeholder evaluators for missing IDs (logged).
- Outputs struct (`MessageEngineOutputs`) containing active text, priorities, colors, font sizes, stack CSV, and missing-evaluator CSV.
- Session reset hook to clear stack when session type/token changes.

---

## Calculation blocks (high level)
1. **Evaluator registration:** Build evaluator map; missing evaluators replaced with placeholders and logged.
2. **Signal ingestion:** Signals pulled from SimHub/plugin exports each evaluation tick (cadence controlled by host).
3. **Message activation:** Evaluators set active messages, priorities, and styles; outputs written to exports.
4. **Cancel handling:** MsgCx triggers `_msgV1Engine.OnMsgCxPressed()` to clear/cancel messages per engine rules.
5. **Pit marker latching:** `Eval_TrackMarkersCaptured/LengthDelta/LockedMismatch` consume marker pulses once per track key, latching tokens to avoid repeat toasts while markers remain unchanged or locked.

---

## Outputs
- Exports: `MSGV1.ActiveText_*`, priority, IDs, colors, font sizes, `ActiveCount`, `LastCancelMsgId`, `ClearAllPulse`, `StackCsv`, `MissingEvaluatorsCsv` (see inventory).
- Logs: `[LalaPlugin:MSGV1] Registered placeholder evaluators: ...` and other engine-level messages.
- Pit marker toasts are definition-driven (`MsgId`: `trackmarkers.captured`, `trackmarkers.length_delta`, `trackmarkers.lock_mismatch`) with no legacy/adhoc path; texts live in `Messages.json` via `MessageDefinitionStore`.

---

## Reset rules
- `ResetSession()` invoked on session type change and session token change; clears outputs/stack and missing-evaluator CSV.

---

## Failure modes
- Missing evaluator IDs -> placeholders; logged with evaluator→message mapping.
- Signals marked TBD in catalog (e.g., incident-ahead) have no evaluator yet; messages will never fire until implemented.

---

## Test checklist
- Load plugin and check log for missing evaluators; verify `MissingEvaluatorsCsv` export matches log.
- Trigger MsgCx action and confirm `ClearAllPulse` or cancel IDs update, stack clears.
- Simulate pit window open, fuel deficit, or flag changes to see message activations per catalog.
