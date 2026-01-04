# Pit Entry Assist — System Overview

Validated against commit: 52bd57d7c618f4df094c68c4ea6f1e11cc5e328f  
Last updated: 2026-02-06  
Branch: work

## Purpose & driver benefit
- Provide a **distance-to-speed** cue that helps the driver cross the pit entry line **at or below pit speed** without guesswork.
- Normalize braking expectations across cars by using a **profiled deceleration target** and a **buffer** window that can be tuned per car/track.
- Emit structured SimHub properties for dashes and structured logs for post-run tuning.

## High-level flow
1) Assist arms when entering the pit phase **or** when the pit limiter is on with an overspeed > **+2 kph**.  
2) On every tick, the engine resolves inputs (pit speed, distance, speed), computes the constant-decel requirement, margin, and cue.  
3) The system clamps to a **0–500 m working window**; outside this range it disarms and clears outputs.  
4) Crossing the pit entry line emits a `LINE` log with compliance markers; any disarm emits `END`.

## Physics model & definitions
- **Constant deceleration formula:** `d_required = (v² − v_target²) / (2 × a)`, where `v` is current speed (m/s), `v_target` is pit limit (m/s), and `a` is the profiled decel.  
- **Margin:** `margin = distance_to_line − d_required`. Positive = early (room to spare); negative = late.  
- **Buffer:** Profiled distance cushion applied to cue thresholds; it does **not** alter the physics calculation.

## Inputs & sources
- **Pit speed:** Prefer `WeekendInfo.TrackPitSpeedLimit`; fallback `IRacingExtraProperties.iRacing_PitSpeedLimitKph`. Missing/invalid → assist disarms.
- **Distance to line:** Prefer `IRacingExtraProperties.iRacing_DistanceToPitEntry`; fallback `(PitEntryPct − CarPct) × TrackLength`. Missing/invalid → assist disarms.
- **Speed:** Live car speed (`SpeedKmh`).
- **Profile parameters:** `Pit Entry Decel (m/s²)` and `Pit Entry Buffer (m)` from the active car profile (Dash tab sliders); clamped to **5–25 m/s²** and **0–50 m** when used.

## Activation, pit phase, and limiter logic
- Arms when **PitPhase = EnteringPits** **or** **pit limiter is ON** with overspeed > **+2 kph**.
- Uses the pit-lane crossing edge (`IsInPitLane` transition) to emit `LINE`, even if arming conditions later drop.
- Disarms (and clears outputs) when:
  - Pit phase/limiter arming is false **and** no line was crossed that tick,
  - Distance source is invalid or ≥ **500 m**,
  - Pit speed source is unavailable,
  - Car is already in the pit lane.

## Working window & clamping
- Distance is clamped to **0–500 m**; values above 500 m force a reset to avoid stale far-distance inputs.
- Decel and buffer are clamped as noted above; outputs keep the **last used** decel/buffer for diagnosis even after disarm.

## Cue logic (levels 0–4)
| Cue | Meaning | Condition |
| --- | --- | --- |
| 0 | OFF | Assist inactive/disarmed |
| 1 | OK | Margin greater than buffer |
| 2 | BRAKE SOON | Margin within `(0, buffer]` |
| 3 | BRAKE NOW | Margin `≤ 0` |
| 4 | LATE | Margin `< -buffer` |

**Buffer usage:** The buffer widens the “OK” region and separates “BRAKE SOON” vs. “LATE”; it does **not** change required distance or margin.

## Pit speed & distance fallback logic
- **Pit speed:** session pit limit → iRacing extra pit limit → disarm if still invalid.
- **Distance:** iRacing distance to pit entry → track-percent delta × track length → disarm if still invalid.
- **Window clamp:** after resolving distance, clamp to 0–500 m; ≥500 m (while armed) triggers reset to avoid misleading early cues.

## Windowed braking guidance
- Constant-decel requirement is recomputed each tick using the current speed and profiled decel.
- **Margin** is the single continuous metric; cues are derived from it. Dash/driver should treat margin as the primary indicator.
- **Limiter behaviour:** Limiter ON with overspeed can arm the assist even outside the explicit pit phase, preventing missed arming on short entries.

## Dash integration
- **Exposed properties:** `Pit.EntryAssistActive`, `Pit.EntryDistanceToLine_m`, `Pit.EntryRequiredDistance_m`, `Pit.EntryMargin_m`, `Pit.EntryCue`, `Pit.EntryCueText`, `Pit.EntrySpeedDelta_kph`, `Pit.EntryDecelProfile_mps2`, `Pit.EntryBuffer_m`.
- **Recommended visualisation:** Use a **vertical marker** driven by `Pit.EntryMargin_m` on a fixed ±150 m scale; centre = 0 m (ideal brake point). Keep `Pit.EntryCueText` visible as a secondary label but avoid cue-driven color logic that masks continuous motion.
- **Null/zero handling:** Gate visibility on `Pit.EntryAssistActive` to avoid SimHub suppression; do not renormalise margin by buffer (maintain raw metres).

## Logging & diagnostics
- **`ACTIVATE`** — fires once when the assist arms. Fields: `dToLine`, `dReq`, `margin`, `spdΔ`, `decel`, `buffer`, `cue`. Use to validate inputs and starting margin.  
- **`LINE`** — fires once on pit-lane entry. Adds `firstOK` (distance where speed first dropped to pit limit within this activation) and `okBefore` (metres compliant before the line; currently mirrors `firstOK` because compliance is recorded against distance to line).  
- **`END`** — fires once when the assist disarms (pit entry handled, invalid inputs, or arming removed).  
- **Example logs:**
  - `[LalaPlugin:PitEntryAssist] ACTIVATE dToLine=185.3m dReq=142.7m margin=42.6m spdΔ=35.2kph decel=14.0 buffer=15.0 cue=2`
  - `[LalaPlugin:PitEntryAssist] LINE dToLine=3.2m dReq=0.0m margin=3.2m spdΔ=-2.1kph firstOK=58.4m okBefore=58.4m decel=14.0 buffer=15.0 cue=1`
  - `[LalaPlugin:PitEntryAssist] END`

## Profile parameters & tuning
- **Pit Entry Decel (m/s²):** Target constant decel used in the braking-distance calculation.  
- **Pit Entry Buffer (m):** Safety window that shapes cue thresholds.  
- **Per-car profiles:** Stored per car (and copied per profile actions) to reflect brake hardware/tyre differences; class-level defaults are too coarse.  
- **Tuning guidance:** Start around **14 m/s²** with a **15 m** buffer for GT3/GTP. Review `LINE` logs:
  - If `firstOK` is far (>80 m), reduce decel or buffer.
  - If `margin` at LINE is negative or late, increase decel slightly before reducing buffer.
  - Re-verify after tyre/temp changes; log fields remain latched for the activation.

## Design notes
- **Edge-triggered logs only:** No per-tick log spam; diagnostics rely on ACTIVATE/LINE/END snapshots.
- **Limiter-friendly arming:** Limiter ON + overspeed prevents missing the assist on ultra-short entries where phase changes late.
- **Latched decel/buffer:** Outputs retain the last-used decel and buffer after reset to aid post-run debugging.
- **Per-car profiles:** Values are stored with car profiles to keep braking cues stable across tyre compounds and ABS/regen behaviours.
- **Pit entry markers & storage:** Pit entry/exit markers auto-learn per track (locked by default) into `PluginsData/Common/LalaLaunch/LalaLaunch.TrackMarkers.json`; track-length deltas >50 m force unlock and MSGV1 notifications. Lock/refresh controls live in Profiles → Tracks. See `Subsystems/Track_Markers.md` for the full capture/lock/notify flow.

## Known limitations
- Relies on sim-provided pit distance; if both primary and fallback sources are absent, the assist cannot arm.  
- Constant-decel model ignores gradient/surface changes; drivers may need per-track decel tuning.  
- `okBefore` mirrors `firstOK` because compliance is measured against distance-to-line rather than a separate time window; a separate “metres before line while legal” metric is deferred to later work.

## Intended Part 2 extensions
- Ingest **DecelCapture** samples to auto-seed per-car/per-track decel targets.  
- Add **per-track defaults** and **entry-segment presets** (e.g., short vs. long pit entries).  
- Expand logging to separate `okBefore` into distinct “legal distance before line” and “time-at-limit” metrics for richer tuning.
