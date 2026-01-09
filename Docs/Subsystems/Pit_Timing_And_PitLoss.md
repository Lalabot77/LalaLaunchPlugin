# Pit Timing and Pit Loss

Validated against commit: f542ae2  
Last updated: 2026-02-08  
Branch: work

## Purpose
The Pit Timing & Pit Loss subsystem measures **real pit lane time loss** during live sessions and exposes a **stable pit-loss figure** used by:
- Fuel Model pit projections.
- Fuel Planner pit loss calculations.
- Dashboards showing expected vs actual pit impact.

It converts raw pit entry/exit signals into **lane travel**, **box time**, and **net pit loss** figures, with safeguards to avoid publishing invalid or misleading data.

Canonical references:
- Log semantics: `Docs/SimHubLogMessages.md`
- Fuel consumption & pit integration: `Docs/FuelProperties_Spec.md`
- Export list & cadence: `Docs/SimHubParameterInventory.md`

---

## Scope and boundaries

This doc covers:
- Detection of pit entry/exit.
- Measurement of pit lane travel and stationary time.
- Computation of pit loss (DTL vs direct).
- Publication rules and fallbacks.

Out of scope:
- Fuel-to-add logic (see `Fuel_Model.md`).
- Planner usage of pit loss (see `Fuel_Planner_Tab.md`).
- Dash presentation logic (see `Dash_Integration.md`).

---

## Inputs (source + cadence)

### Telemetry inputs (runtime, 500 ms cadence)
- Pit lane entry/exit flags.
- Vehicle speed (used to detect stop vs drive-through).
- Lap timing (in-lap, pit-lap, out-lap).
- Session state (race vs non-race).

These are consumed inside the plugin’s main `DataUpdate` loop.

---

## Internal state

### Pit cycle state
A pit cycle is tracked as a **state machine**:

1. **Idle**
2. **Entry armed**
3. **In pit lane**
4. **Stopped / box time**
5. **Exit detected**
6. **Out-lap completion**
7. **Evaluation / publish**
8. **Reset to idle**

Each cycle is isolated; partial or invalid cycles are discarded.

---

### Latched timing values
For a valid pit cycle, the subsystem latches:
- **Lane time** (entry → exit).
- **Box time** (stationary portion inside pit).
- **Direct lane travel** (lane minus box).
- **In-lap time**
- **Out-lap time**
- **Baseline lap time** (average pace reference).

These values are frozen once latched to avoid post-hoc drift.

---

## Calculation blocks (high level)

### 1) Pit entry detection
Pit entry is detected using:
- Pit lane flag transitions.
- Edge detection to avoid re-arming mid-lane.

On entry:
- Previous pit metrics are cleared.
- Entry timestamp is latched.
- State transitions to “in pit”.

Logs are emitted to confirm arming.

---

### 2) Box time detection
While in pit lane:
- Vehicle speed is monitored.
- Sustained near-zero speed indicates **box stop**.
- Box start and end timestamps are latched.

Drive-throughs are detected when:
- No valid stationary period occurs.
- Box time remains zero.

---

### 3) Pit exit detection
On pit lane exit:
- Exit timestamp is latched.
- Lane time is computed.
- Direct lane travel = lane time − box time.
- Cycle moves to “awaiting out-lap completion”.

If exit occurs without valid entry, the cycle is invalidated.

---

### 4) In-lap / out-lap capture
- **In-lap**: lap ending at pit entry.
- **Out-lap**: lap starting at pit exit.

Both laps must complete cleanly to compute DTL (delta time loss).
If either lap is invalid (incident, missing data), DTL path is aborted.

---

### 5) Pit loss computation

#### A) DTL-based loss (preferred)
If valid baseline pace exists and both laps are valid:

DTL = (InLap + OutLap) − (2 × BaselineLap)

This reflects **net race time lost** due to pitting.

#### B) Direct lane loss (fallback)
If baseline or laps are unavailable:

DirectLoss = LaneTime − BoxTime


This represents raw pit lane traversal time.

Selection rules:
- Prefer DTL when available.
- Fallback to direct lane loss otherwise.
- Never publish negative or zero losses.

Canonical behaviour is documented in logs and fuel spec.

---

### 6) Publication rules
Pit loss is published only when:
- A full pit cycle completes.
- Loss value passes sanity checks.
- Session context is valid (typically Race).

Published values update:
- `Fuel.Live.PitLaneLoss_S`
- `Fuel.Live.TotalStopLoss`
- Planner-accessible pit loss fields.

Once published, the value remains until superseded by a newer valid cycle.

#### Locking and blocked candidates
- Per-track pit loss values can be **locked** in Profiles. When locked, new candidates are **not** applied to the stored pit loss value.
- Blocked candidates are captured with timestamp and source so the UI can show what was rejected while locked.
- When unlocked, the next valid cycle can overwrite the stored pit loss normally.

---

## Outputs (exports + logs)

### Core exports
(See `SimHubParameterInventory.md` for authoritative list.)

Typical outputs include:
- `Fuel.Live.PitLaneLoss_S`
- `Fuel.Live.TotalStopLoss`
- `Fuel.Live.RefuelRate_Lps`
- `Fuel.Live.TireChangeTime_S`
- `PitExit.DistanceM` / `PitExit.TimeS` (pit-exit waypoint distance/time using stored exit marker + live speed). These remain zero outside the pit lane and refresh on the 250 ms poll cadence, matching pit-lane state to avoid noisy updates when circulating on track.

These values feed directly into:
- Fuel Model pit projections.
- Fuel Planner pit loss math.

---

### Logs
The subsystem emits structured INFO logs for:
- Pit entry arming.
- Exit detection and latched times.
- In-lap / out-lap capture.
- DTL computation and fallback usage.
- Publication decision (DTL vs direct).
- Locking blocks (candidate blocked + logged) when the profile pit loss is locked.

Log semantics are canonical in `SimHubLogMessages.md`.

---

## Dependencies / ordering assumptions
- Pace subsystem must provide a stable baseline for DTL to be valid.
- Lap detector must be reliable to correctly identify in-lap and out-lap.
- Fuel Model consumes pit loss only after publication (not mid-cycle).

---

## Reset rules

Pit Timing state resets on:
- Session identity change.
- Car or track change.
- Manual invalidation (e.g. aborted cycle).

On reset:
- All latched pit timings are cleared.
- No stale pit loss is reused.
- Next valid cycle starts fresh.

Reset semantics are centralised in:
`Docs/Reset_And_Session_Identity.md`.

---

## Failure modes / edge cases

- **Drive-through penalties**
  - No box time → DTL may still compute if laps valid.
  - Direct lane loss may be misleading; logs indicate drive-through.

- **Invalid out-lap**
  - DTL path aborted.
  - Direct lane loss used if sane.

- **Replay sessions**
  - Pit signals may be delayed or reordered.
  - Validate behaviour using logs.

- **Traffic on out-lap**
  - DTL includes traffic impact by design.
  - This is intentional and represents real race loss.

---

## Test checklist

1. **Standard pit stop**
   - Box stop detected.
   - Lane, box, and direct times latched.
   - DTL published after out-lap.

2. **Drive-through**
   - No box time detected.
   - Direct loss published or DTL if laps valid.

3. **Aborted cycle**
   - Enter pit, exit immediately, invalidate cycle.
   - No pit loss published.

4. **Session reset**
   - Change subsession.
   - Confirm all pit timing state cleared.

5. **Planner integration**
   - Planner pit loss updates only after valid publication.

---

## TODO / VERIFY

- TODO/VERIFY: Confirm exact speed threshold and dwell time used to classify “box stop” vs drive-through.
- TODO/VERIFY: Confirm whether DTL uses session-average or stint-average pace as baseline.
- TODO/VERIFY: Confirm pit loss publication gating for non-race sessions (practice/qualifying).


