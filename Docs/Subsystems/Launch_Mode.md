# Launch Mode

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1
Last updated: 2025-12-28
Branch: docs/refresh-index-subsystems

## Purpose
Launch Mode manages automated and manual race starts by coordinating:
- Driver intent (manual arming / disabling),
- Vehicle state (speed, clutch, gear),
- Session context (race start only),
- Safety timeouts and abort conditions.

It exists to reduce start variability while remaining fully defeatable by the driver.

---

## Inputs

- Vehicle speed
- Clutch state / bite point
- Gear selection
- Session state (race, green flag)
- Manual launch button bindings
- Timer-zero and session identity

Inputs are evaluated continuously in the main update loop.

---

## Internal State

- LaunchModeEnabled (user-configurable)
- LaunchArmed / LaunchActive
- ManualPrimed flag
- Timeout timestamps
- User-disabled latch (post-abort)

State transitions are logged.

---

## Logic Blocks

### 1) Eligibility
Launch Mode only evaluates when:
- Session type == Race
- Vehicle speed below threshold
- Driver has not disabled launch this session

Otherwise, the subsystem remains dormant.

---

### 2) Arming
Launch may arm via:
- Automatic detection (grid start)
- Manual priming button

Manual priming starts a timeout window.

---

### 3) Execution
When armed:
- Clutch output ramps according to configured curve
- Gear and throttle assumptions are monitored
- Launch deactivates once speed threshold exceeded

---

### 4) Abort Conditions
Launch aborts immediately if:
- Timeout exceeded
- Driver disables launch
- Unexpected speed/gear state
- Session identity changes

Abort reason is logged and latched.

---

## Outputs

- Launch active flags
- Clutch output percentage
- Debug state exports
- INFO logs explaining transitions

---

## Reset Rules

Launch state resets on:
- Session identity change
- Abort or completion
- Manual disable

---

## Failure Modes

- Missed grid detection → manual prime required
- Timeout expiry → launch disabled for session
- Replay timing anomalies → verify via logs

---

## Test Checklist

- Auto launch on race start
- Manual prime success
- Timeout abort
- Session reset clears all state

---

## TODO / VERIFY

- TODO/VERIFY: Confirm exact speed threshold used to disengage launch.
- TODO/VERIFY: Confirm clutch curve configuration source.
