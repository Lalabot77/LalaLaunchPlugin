# Pace and Projection

Validated against commit: b45bc8f  
Last updated: 2026-02-12  
Branch: work

## Purpose
The Pace & Projection subsystem provides a **stable, defensible lap-time reference** used to:
- Project race distance (laps remaining) in timed races.
- Support fuel-to-end and pit window calculations.
- Provide a baseline for **DTL-based pit loss**.
- Drive dashboards with meaningful, low-noise pace figures.

It explicitly separates:
- **Observed pace** (what just happened),
- **Usable pace** (what projections should trust),
- **Fallback pace** (when live data is insufficient).

Canonical references:
- Export definitions: `Docs/SimHubParameterInventory.md`
- Fuel integration rules: `Docs/FuelProperties_Spec.md`
- Pit loss integration: `Subsystems/Pit_Timing_And_PitLoss.md`
- Reset semantics: `Docs/Reset_And_Session_Identity.md`

---

## Scope and boundaries

This doc covers:
- Lap-time capture and rejection.
- Rolling pace windows and confidence.
- Projection lap-time selection and fallbacks.
- Timed-race “after-zero” integration.

Out of scope:
- Fuel burn modelling (see `Fuel_Model.md`).
- Fuel planner UI behaviour (see `Fuel_Planner_Tab.md`).
- Dash rendering logic (see `Subsystems/Dash_Integration.md`).

---

## Inputs (source + cadence)

### Telemetry inputs (runtime, 500 ms cadence)
- Lap completion events (lap number, lap time).
- Session time remaining / timer-zero signals.
- Session state (race / non-race).
- Incident / off-track flags (if wired).

Lap detection and cadence are shared with the Fuel Model.

---

### Profile and planner inputs
- Profile lap-time averages (dry/wet).
- Planner-selected lap time (when planner overrides are active).
- PB-derived lap times (used by planner, not runtime projection unless selected).

The runtime projection prefers **live observed pace** unless explicitly overridden.

---

## Internal state

### Rolling pace windows
- **Stint average**: rolling mean of accepted laps in the current stint.
- **Last-5 average**: rolling window of up to 5 accepted laps.
- **Leader pace**: parsed leader lap times where available.

Each window tracks:
- Sample count.
- Min/max bounds.
- Confidence contribution.

---

### Pace confidence
Pace confidence reflects **how trustworthy live pace is** for projection.

It increases with:
- Number of accepted laps.
- Stability of lap times (low variance).
- Consistency of window membership.

It is reduced or held low when:
- Laps are frequently rejected.
- Large variance exists (traffic, incidents).
- Session has just started.

Pace confidence is used indirectly to decide projection source.

---

## Calculation blocks (high level)

### 1) Lap acceptance (pace)
On each lap completion:
- Reject laps with pit involvement.
- Reject implausibly short or long laps (sanity bounds).
- Reject laps marked invalid by incident logic (if present).

Accepted laps feed pace windows; rejected laps do not affect averages.

Wet/dry mode does **not** change pace validity rules; it only influences how the resulting lap stats are persisted to dry vs wet profile fields.

Acceptance rules mirror fuel-lap acceptance where possible.

---

### 2) Rolling window maintenance
For each accepted lap:
- Update stint-average window.
- Update last-5 window (max 5 samples).
- Track min/max to detect outliers.
- Update confidence metrics.

Window contents are reset on pit entry and session identity change.

---

### 3) Projection lap-time candidates
The subsystem maintains multiple candidate lap times:
- **StintAvgLapTime**
- **Last5LapAvg**
- **LeaderLapAvg** (if available)
- **ProfileLapTime** (fallback)
- **Planner-selected lap time** (if explicitly active)

Each candidate carries:
- Source label.
- Validity flag.
- Confidence contribution.

---

### 4) Projection source selection
The **projection lap time** is selected using priority rules:

1. Planner-selected lap time (explicit override).
2. Live stint/last-5 average when confidence sufficient.
3. Leader-based pace (if configured and valid).
4. Profile lap time.
5. Fallback estimator (SimHub computed or last-known).

Source switches are logged to make behaviour observable.

This selected lap time is exported as the “projection pace”.

---

### 5) Timed-race projection
For timed races:
- Remaining session time is divided by projection lap time to compute laps remaining.
- **After-zero allowance** seconds are added to session time:
  - Planner-defined allowance always exists.
  - Live after-zero estimate becomes valid only after timer zero is observed.

This ensures end-of-race laps are not undercounted.

---

### 6) Smoothing and presentation
To avoid dash thrashing:
- Numeric projection values are smoothed.
- String-formatted projections (`*_S`) are debounced.

Raw values remain available for diagnostics.

---

## Outputs (exports + logs)

### Core exports
(See `SimHubParameterInventory.md` for authoritative list.)

Typical outputs include:
- `Pace.StintAvgLapTimeSec`
- `Pace.Last5LapAvgSec`
- `Pace.LeaderLapAvgSec`
- `Pace.ProjectionLapTimeSec`
- `Fuel.LiveLapsRemainingInRace`
- `Fuel.Live.ProjectedDriveSecondsRemaining`
- Projection source labels

These outputs feed:
- Fuel Model race-distance math.
- Pit loss DTL baseline.
- Dash projections.

---

### Logs
The subsystem emits INFO logs for:
- Lap acceptance/rejection (pace side).
- Projection source changes.
- After-zero source changes.

Logs are designed to explain *why* a projection changed.

See `SimHubLogMessages.md` for canonical definitions.

---

## Dependencies / ordering assumptions
- Lap detector must fire before pace updates.
- Pace updates must complete before fuel projections.
- Fuel Model relies on projection pace being stable within a tick.

---

## Reset rules

Pace state resets on:
- Session identity change.
- Pit entry (stint boundaries).
- Car or track change.

On reset:
- All rolling windows cleared.
- Confidence reset.
- Projection source re-evaluated from defaults.

Reset contract is defined in:
`Docs/Reset_And_Session_Identity.md`.

---

## Failure modes / edge cases

- **Heavy traffic**
  - Variance rises; confidence drops.
  - Projection falls back to profile or leader pace.

- **Replays**
  - Lap timing events may bunch.
  - Validate projection behaviour via logs.

- **Leader data missing**
  - Leader pace candidate invalidated.
  - Projection continues without it.

- **Short sessions**
  - Insufficient samples → fallback pace dominates.

---

## Test checklist

1. **Clean stint**
   - Confidence rises smoothly.
   - Projection source remains stable.

2. **Traffic stint**
   - Confidence drops.
   - Projection source switches are logged.

3. **Planner override**
   - Projection uses planner lap time regardless of live data.

4. **Timed race**
   - After-zero allowance increases laps remaining appropriately.

5. **Reset behaviour**
   - Pit entry resets stint windows.
   - Session change clears all pace state.

---

## TODO / VERIFY

- TODO/VERIFY: Confirm exact variance thresholds used to suppress live pace confidence.
- TODO/VERIFY: Confirm leader pace parsing rules and when leader data is considered valid.
- TODO/VERIFY: Confirm fallback estimator precedence when both profile and SimHub computed pace exist.
