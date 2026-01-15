# Fuel Model

Validated against commit: 9f784a9  
Last updated: 2026-01-14  
Branch: work

## Purpose
The Fuel Model is the runtime engine that:
- Captures **live fuel burn per lap** (with strict acceptance/rejection rules).
- Maintains **rolling windows** (dry/wet) and computes a **stable fuel-per-lap** used for projections.
- Seeds fuel burn across session transitions to avoid cold starts in Race.
- Persists stable fuel and pace stats into profiles when confidence thresholds are met.
- Projects **laps remaining in race**, including **after-zero** behaviour for timed races.
- Computes **deltas, pit needs, and pit window state** outputs consumed by the Fuel Tab and dashboards.

Canonical behaviour and edge-case rules live in:
- `Docs/FuelProperties_Spec.md` (fuel + pit logic contract).
- `Docs/SimHubParameterInventory.md` (exports list/cadence).
- `Docs/FuelTab_SourceFlowNotes.md` (how the Fuel Tab consumes “live snapshot” + readiness).

---

## Scope and boundaries
This doc describes the **fuel model runtime** (burn capture → stable selection → projection → pit math → pit window outputs).
It does **not** re-document:
- Fuel Planner UI source selection logic (see `Subsystems/Fuel_Planner_Tab.md` + `FuelTab_SourceFlowNotes.md`).
- Pace estimator internals (see `Subsystems/Pace_And_Projection.md`).

---

## Inputs (source + cadence)

### Telemetry and SimHub core data (runtime, 500 ms poll)
- Fuel level, laps/completed laps, session time remaining, “timer zero” signals.
- Pit lane status / pitting state.
- Refuel request (MFD fuel to add), tyre selection state.
- SimHub computed fallback fuel-per-lap (`DataCorePlugin.Computed.Fuel_LitersPerLap`) when no accepted laps exist.
- Live max fuel inputs: `DataCorePlugin.GameData.MaxFuel` and `DriverCarMaxFuelPct` (BoP) for effective tank capacity.

### Profile baselines (on access)
- Track/car profile fuel baselines (dry/wet averages) used for:
  - Rejection bracketing (0.5x–1.5x baseline).
  - Stable fallback when live confidence is low.
  - Seeding on session transition (Driving → Race).

### Pace inputs (integrated dependency)
- Projection lap time selection (stint avg / last5 / profile / fallback estimator) feeds race-distance projection and pit window.

---

## Internal state

### Rolling windows and fuel capture
- Dry and wet rolling lists (max 5 accepted laps each).
- Session max burn tracking (bounded by baseline multiplier to avoid spikes).
- “Active seeds” markers (so seeded laps aren’t immediately evicted).
- Wet/dry mode flag influencing which window is considered “live”.

### Seed handling across sessions
- On session transitions, the model captures dry/wet seeds (burn averages + sample counts) and re-applies them when entering a Race session for the same car/track.
- Seeded laps are protected from immediate eviction in the rolling window until fresh samples arrive.

### Stable burn state (the number dashboards should trust)
- `_stableFuelPerLap` + `Fuel.LiveFuelPerLap_StableSource` + `Fuel.LiveFuelPerLap_StableConfidence`.
- Deadband hold: when the candidate is “close enough”, stable value holds while source/confidence can still evolve.

### Live max tank tracking
- Live max tank is computed as `MaxFuel × BoP` with BoP clamped to [0.01, 1.0] and defaulted to 1.0 when missing.
- The last valid live max fuel is retained so tank-space calculations remain stable if telemetry temporarily drops.
- Live Session UI displays are cleared to `—` when no valid cap exists, avoiding stale values during session transitions.

### Profile persistence (dry vs wet)
- Once enough samples exist (≥2 valid laps), the model persists min/avg/max fuel burn, sample counts, and avg lap time into the active track profile.
- Condition locks (`DryConditionsLocked`/`WetConditionsLocked`) prevent telemetry-driven writes.
- Per-condition “last updated” metadata is recorded separately for dry and wet stats.

### After-zero state (timed-race support)
- Planner after-zero seconds always available (from strategy/profile).
- Live after-zero estimate becomes valid once timer zero is observed and post-zero time advances.
- Source switches between planner and live (and is exported).

### Pit window state cache
- Last pit window state enum + label + last log timestamp to avoid spam logging.
- Pit window opening/closing lap calculations are computed from stable burn + tank space logic.

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

### 3) Live burn and confidence (continuous)
- `Fuel.LiveFuelPerLap` is the burn used for projections when live is valid.
- `Fuel.Confidence` grows with window size/quality (fuel confidence only; pace confidence is separate).
- `Pace.OverallConfidence` reflects combined/derived confidence behaviour and is used by dashes.

### 4) Stable fuel selection (continuous)
Stable fuel-per-lap exists so dashboards don’t “thrash” when live data is noisy.

**Candidate sources:**
- Live window average when valid.
- Profile track average when confidence below readiness threshold (or no live yet).
- Fallback when neither exists.

**Deadband hold:**
If the new candidate is within `StableFuelPerLapDeadband`, hold the old stable value while still allowing source/confidence to update.

### 5) Race-distance projection (continuous)
Compute projected laps remaining using:
- Projection lap time (from Pace subsystem source selection).
- Session time remaining.
- After-zero allowance seconds (planner or live estimate).
If invalid, fallback to SimHub’s laps-remaining telemetry.

### 6) Pit math + deltas (continuous)
Using current fuel + projected laps + burns (push/stable/save), compute:
- Laps remaining in tank, target burn, delta laps.
- Liters needed vs end (`Fuel.Pit.TotalNeededToEnd`), liters to add (`Fuel.Pit.NeedToAdd`).
- “Will add” litres based on MFD request clamped to tank space.
- Required stops by capacity vs by plan, and final stops required to end.

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

### Fuel tab UI refresh
- When a new live car/track combination appears, the Fuel Tab’s live snapshot is cleared and fuel-burn summaries are recomputed so the UI renders “-” instead of stale profile values during startup.

### Logs
Fuel model emits structured INFO logs for:
- Per-lap acceptance/rejection summary (fuel + pace + projection lines).
- Projection source changes (lap-time source, after-zero source).
- Pit window state changes (debounced).

---

## Reset rules (session identity + transitions)
Fuel model state is reset on:
- Session type change (notably Driving → Race includes seeding behaviour).
- Session token change (`SessionID:SubSessionID`) which performs broader clearing across subsystems.

Reset contract (what gets cleared) is canonical in `FuelProperties_Spec.md`.

---

## Failure modes / known edge cases
- **Replay sessions:** timer behaviour and lap validity may differ; projection/after-zero source switching should be validated with logs.
