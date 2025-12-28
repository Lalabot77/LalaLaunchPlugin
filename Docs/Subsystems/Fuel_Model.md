# Fuel Model

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1  
Last updated: 2025-12-28  
Branch: docs/refresh-index-subsystems

## Purpose
The Fuel Model is the runtime engine that:
- Captures **live fuel burn per lap** (with strict acceptance/rejection rules).
- Maintains **rolling windows** (dry/wet) and computes a **stable fuel-per-lap** used for projections.
- Projects **laps remaining in race**, including **after-zero** behaviour for timed races.
- Computes **deltas, pit needs, and pit window state** outputs consumed by the Fuel Tab and dashboards.

Canonical behaviour and edge-case rules live in:
- `Docs/FuelProperties_Spec.md` (fuel + pit logic contract).  
- `Docs/SimHubParameterInventory.md` (exports list/cadence).  
- `Docs/FuelTab_SourceFlowNotes.md` (how the Fuel Tab consumes “live snapshot” + readiness).

Refs: Fuel spec , exports , Fuel Tab flow :contentReference[oaicite:6]{index=6}.

---

## Scope and boundaries
This doc describes the **fuel model runtime** (burn capture → stable selection → projection → pit math → pit window outputs).
It does **not** re-document:
- Fuel Planner UI source selection logic (see `Subsystems/Fuel_Planner_Tab.md` + `FuelTab_SourceFlowNotes.md`). :contentReference[oaicite:7]{index=7}
- Pace estimator internals (see `Subsystems/Pace_And_Projection.md`). :contentReference[oaicite:8]{index=8}

---

## Inputs (source + cadence)

### Telemetry and SimHub core data (runtime, 500 ms poll)
- Fuel level, laps/completed laps, session time remaining, “timer zero” signals.
- Pit lane status / pitting state.
- Refuel request (MFD fuel to add), tyre selection state.
- SimHub computed fallback fuel-per-lap (`DataCorePlugin.Computed.Fuel_LitersPerLap`) when no accepted laps exist.

These are pulled from the main `DataUpdate` loop (the plugin’s 500 ms cadence is the contract used throughout the exports inventory). 

### Profile baselines (on access)
- Track/car profile fuel baselines (dry/wet averages) used for:
  - Rejection bracketing (0.5x–1.5x baseline).
  - Stable fallback when live confidence is low.
  - Seeding on session transition (Driving → Race).

Behaviour is specified in the fuel spec. 

### Pace inputs (integrated dependency)
- Projection lap time selection (stint avg / last5 / profile / fallback estimator) feeds race-distance projection and pit window. 

---

## Internal state

### Rolling windows and fuel capture
- Dry and wet rolling lists (max 5 accepted laps each).
- Session max burn tracking (bounded by baseline multiplier to avoid spikes).
- “Active seeds” markers (so seeded laps aren’t immediately evicted).
- Wet/dry mode flag influencing which window is considered “live”.

Rules for acceptance, window maintenance, and max tracking are canonical in the fuel spec. 

### Stable burn state (the number dashboards should trust)
- `_stableFuelPerLap` + `Fuel.LiveFuelPerLap_StableSource` + `Fuel.LiveFuelPerLap_StableConfidence`
- Deadband hold: when the candidate is “close enough”, stable value holds while source/confidence can still evolve.

Stable selection rules are canonical in the fuel spec. 

### After-zero state (timed-race support)
- Planner after-zero seconds always available (from strategy/profile).
- Live after-zero estimate becomes valid once timer zero is actually observed and post-zero time advances.
- Source switches between planner and live (and is exported).

After-zero contract is in fuel spec (and cross-linked from pace/projection). 

### Pit window state cache
- Last pit window state enum + label + last log timestamp to avoid spam logging.
- Pit window opening/closing lap calculations are computed from stable burn + tank space logic.

Pit window states and priority rules are canonical in the fuel spec. 

---

## Calculation blocks (high level)

### 1) Lap acceptance (per lap crossing)
On lap completion, compute fuel delta for the lap and apply rejection rules before inserting into windows.

**Rejection gates (canonical):**
- Race warmup (laps ≤ 1) → reject.
- Pit involvement / first lap after pit exit → reject.
- Incident/off-track latched reason (if wired) → reject.
- Fuel delta sanity (>0.05 L, and <= max(10 L, 20% of effective tank)) → reject.
- If profile baseline exists: require 0.5x–1.5x baseline → reject otherwise.

Source of truth: `FuelProperties_Spec.md`. 

### 2) Rolling window maintenance (per accepted lap)
- Insert accepted lap into dry or wet list (depending on wet mode).
- Maintain max length (5).
- Track minima/maxima for guidance.
- Update session max burn only when within a safe baseline multiplier band (avoid spikes).

Source of truth: `FuelProperties_Spec.md`. 

### 3) Live burn and confidence (continuous)
- `Fuel.LiveFuelPerLap` is the burn used for projections when live is valid.
- `Fuel.Confidence` grows with window size/quality (fuel confidence only; pace confidence is separate).
- `Pace.OverallConfidence` reflects combined/derived confidence behaviour and is used by dashes.

Exports and cadence: inventory. 

### 4) Stable fuel selection (continuous)
Stable fuel-per-lap exists so dashboards don’t “thrash” when live data is noisy.

**Candidate sources:**
- Live window average when valid.
- Profile track average when confidence below readiness threshold (or no live yet).
- Fallback when neither exists.

**Deadband hold:**
If the new candidate is within `StableFuelPerLapDeadband`, hold the old stable value while still allowing source/confidence to update.

Stable selection contract: `FuelProperties_Spec.md`. 

### 5) Race-distance projection (continuous)
Compute projected laps remaining using:
- Projection lap time (from Pace subsystem source selection).
- Session time remaining.
- After-zero allowance seconds (planner or live estimate).
If invalid, fallback to SimHub’s laps-remaining telemetry.

Projection precedence and fallbacks are canonical in the fuel spec. 

### 6) Pit math + deltas (continuous)
Using current fuel + projected laps + burns (push/stable/save), compute:
- Laps remaining in tank, target burn, delta laps.
- Liters needed vs end (`Fuel.Pit.TotalNeededToEnd`), liters to add (`Fuel.Pit.NeedToAdd`).
- “Will add” litres based on MFD request clamped to tank space.
- Required stops by capacity vs by plan, and final stops required to end.

These are the “core strategy surfaces” used by Fuel Tab and dashboards. Export list: inventory. 

### 7) Pit window state machine (continuous)
Pit window is **race-only** and only meaningful when stops may be required.

Canonical gates (in priority order):
1. Not Race / session not running / no fuel stops required → **N/A**
2. Stable confidence below readiness threshold → **NO DATA YET**
3. Refuel off or request <= 0 → **SET FUEL!**
4. Unknown tank capacity → **TANK ERROR**
5. Otherwise evaluate whether required add fits within tank space for:
   - PUSH burn, then
   - STD (stable burn), then
   - ECO (save burn)

If any fits → open window with state label:
- **CLEAR PUSH** / **RACE PACE** / **FUEL SAVE**
If none fit → **TANK SPACE**.

Pit window contract: `FuelProperties_Spec.md`. 

### 8) Smoothing (continuous)
Some outputs exist in both raw and smoothed forms for dashboards:
- Laps-remaining and select pit delta strings use smoothing/EMA to avoid “jumping”.
- Smoothed values are typically exposed as `_S` (string) exports.

Inventory is canonical for naming. 

---

## Outputs (exports + logs)

### Core exports (selected)
See `Docs/SimHubParameterInventory.md` for the full list, but the main Fuel Model outputs include:
- `Fuel.LiveFuelPerLap`, `Fuel.LiveFuelPerLap_Stable`, `Fuel.LiveFuelPerLap_StableSource`, `Fuel.LiveFuelPerLap_StableConfidence`
- `Fuel.LiveLapsRemainingInRace(_Stable)` (+ `_S` strings)
- `Fuel.DeltaLaps`, `Fuel.TargetFuelPerLap`, `Fuel.LapsRemainingInTank`
- Pit deltas and need-to-add: `Fuel.Pit.*` and `Fuel.Delta.*`
- Stops required: `Fuel.PitStopsRequiredByFuel`, `Fuel.PitStopsRequiredByPlan`, `Fuel.Pit.StopsRequiredToEnd`
- After-zero model: `Fuel.Live.DriveTimeAfterZero`, `Fuel.Live.ProjectedDriveSecondsRemaining`

Export list/cadence: `SimHubParameterInventory.md`. 

### Logs
Fuel model emits structured INFO logs for:
- Per-lap acceptance/rejection summary (fuel + pace + projection lines).
- Projection source changes (lap-time source, after-zero source).
- Pit window state changes (debounced).

(Logs catalogue is canonical in `SimHubLogMessages.md`—keep that file as the full truth list.) :contentReference[oaicite:25]{index=25}

---

## Dependencies / ordering assumptions
- Pace model must run before projection finalization so `Fuel.ProjectionLapTime_Stable` is meaningful. 
- Fuel Tab auto-apply from live snapshot requires readiness gating (`IsFuelReady` driven by stable confidence). 
- Pit window correctness depends on:
  - Correct tank capacity resolution.
  - Correct clamping of MFD request to tank space.
  - Stable burn not being near-zero.

---

## Reset rules (session identity + transitions)
Fuel model state is reset on:
- Session type change (notably Driving → Race includes seeding behaviour).
- Session token change (`SessionID:SubSessionID`) which performs broader clearing across subsystems.

Reset contract (what gets cleared) is canonical in fuel spec:
- Clears windows, confidence, stable values, lap detector state, pit window state, pace windows, live max fuel tracking.
- Invokes `FuelCalculator.ResetTrackConditionOverrideForSessionChange()` on session change.

Source of truth: `FuelProperties_Spec.md`. 

---

## Failure modes / known edge cases
- **Replay sessions:** timer behaviour and lap validity may differ; projection/after-zero source switching should be validated with logs.
- **Tank capacity unknown:** pit window forced to **TANK ERROR**; pit guidance should not be trusted.
- **No accepted laps:** live burn may be bootstrapped from SimHub computed burn; stable burn may show “Fallback” source until confidence rises. 
- **Chaotic stints:** rejection filters may discard many laps; confidence may stay low and pit window will remain **NO DATA YET**. 

---

## Test checklist (practical)
1. **Clean run, no pits (Race):**
   - Confirm accepted laps accumulate (window size rises) and `Fuel.Confidence` increases.
   - Stable burn should move to live source and stop thrashing.
2. **Pit lap rejection:**
   - Enter pit lane and ensure that lap is rejected as `pit-lap` and the first lap after exit is `pit-warmup`.
3. **Projection source changes:**
   - Force pace confidence low/high (traffic vs clean) and ensure projection lap source changes are logged and exported.
4. **Pit window states:**
   - With stops required > 0, verify transitions:
     - **SET FUEL!** when refuel request is 0/off,
     - then **RACE PACE / FUEL SAVE / CLEAR PUSH** when request is set and tank space allows.
5. **Reset correctness:**
   - Change session type or subsession and confirm windows/confidence/stable values reset (and no stale pit window state remains). 

---

## TODO/VERIFY (hardener list)
- TODO/VERIFY: Confirm incident/off-track wiring is fully connected (spec notes hooks exist but may still be placeholder). :contentReference[oaicite:32]{index=32}
- TODO/VERIFY: Confirm wet/dry mode switching triggers and whether UI exposes wet/dry choices beyond the auto-applied average fuel baselines. 
- TODO/VERIFY: Confirm exact logging throttle conditions for pit window changes (to ensure no log spam on oscillating readiness). (Cross-check `SimHubLogMessages.md` once reviewed as canonical.)
