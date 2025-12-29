# Pit Entry Assist & Deceleration Capture

Validated against commit: 52bd57d7c618f4df094c68c4ea6f1e11cc5e328f  
Last updated: 2026-02-06  
Branch: work

## Purpose
- **Pit Entry Assist** gives a driver-facing cue to hit pit speed at the entry line using distance, speed, and a constant-decel model. It publishes both dash-friendly properties and structured logs for post-run analysis. 
- **Deceleration Capture (DecelCap)** is a developer-only instrumentation tool to empirically measure achievable braking decel per car; it is disabled by default and not part of runtime behaviour.

Canonical exports: `Docs/SimHubParameterInventory.md`  
Canonical logs: `Docs/SimHubLogMessages.md`

---

## Pit Entry Assist — Overview

### Activation & deactivation
- **Arms when:**
  - `PitPhase.EnteringPits`, **OR**
  - Pit limiter **ON** **and** overspeed > **+2 kph** (`Pit.EntrySpeedDelta_kph`).【F:PitEngine.cs†L246-L279】
- **Deactivates when:**
  - Driver enters pit lane (`IsInPitLane`), or
  - Arming conditions drop (limiter off / no overspeed / no entry phase), or
  - Inputs become invalid (missing pit speed, distance >500 m, etc.).【F:PitEngine.cs†L260-L318】【F:PitEngine.cs†L376-L398】

### Flow (high level)
1. Assist arms (conditions above).
2. Braking guidance recomputes every tick (distance, required distance, margin, cue).【F:PitEngine.cs†L260-L363】
3. Driver crosses pit entry line → `LINE` log fires once.【F:PitEngine.cs†L183-L216】
4. Assist ends (pit entry or disarm).

---

## Pit Entry Assist — Core Calculations

- **Distance to pit entry (`Pit.EntryDistanceToLine_m`):**
  - Primary: `IRacingExtraProperties.iRacing_DistanceToPitEntry`.
  - Fallback: `(pitEntryPct − carPct) × trackLength` using `IRacingExtraProperties.iRacing_PitEntryTrkPct` and `SessionData.WeekendInfo.TrackLength`.
  - Clamped to **0–500 m** working window; assist resets if ≥500 m or invalid.【F:PitEngine.cs†L279-L318】

- **Speed delta (`Pit.EntrySpeedDelta_kph`):** Current speed − pit speed limit (session pit speed, fallback to iRacing extra).【F:PitEngine.cs†L251-L276】

- **Required braking distance (`Pit.EntryRequiredDistance_m`):** Constant-decel model: `(v² − vTarget²) / (2 × decel)` when above pit speed. Uses per-profile decel (clamped 5–25 m/s²).【F:PitEngine.cs†L318-L342】

- **Margin (`Pit.EntryMargin_m`):** `distanceToLine − requiredDistance`. Positive = early/room, negative = late.【F:PitEngine.cs†L323-L343】

- **Profile parameters:**
  - `Pit.EntryDecelProfile_mps2` and `Pit.EntryBuffer_m` come from the active car profile (Dash tab sliders). Both are clamped to sane ranges when used.【F:PitEngine.cs†L240-L259】【F:LalaLaunch.cs†L3380-L3387】【F:DashesTabView.xaml†L141-L142】

---

## Pit Entry Cue System

Cue level is derived from **margin vs. buffer** (buffer = profile slider):

| Cue | Text | Meaning |
| --- | --- | --- |
| 0 | OFF | Assist inactive / disarmed |
| 1 | OK | Plenty of margin |
| 2 | BRAKE SOON | Inside buffer window |
| 3 | BRAKE NOW | Immediate braking required (≤0 margin) |
| 4 | LATE | Cannot make pit speed at target decel (margin < −buffer) |

- **Logic:** `margin < -buffer → LATE; margin ≤ 0 → BRAKE NOW; margin ≤ buffer → BRAKE SOON; else OK`.【F:PitEngine.cs†L334-L339】
- **Dash string:** `Pit.EntryCueText` mirrors the cue value with dash-friendly text tokens.【F:PitEngine.cs†L19-L37】
- **Dash visuals are independent:** cue selection does not dictate how the dash renders the marker/indicator.

---

## Dash Integration Guidance

- Use **`Pit.EntryMargin_m`** as the **primary continuous signal**.
- Recommended mapping: **vertical sliding marker** on a fixed scale (e.g., ±150 m) rather than buffer-normalised scaling; centre line = margin ≈ 0 (ideal brake point).
- Marker interpretation:
  - **Up** = early / brake less.
  - **Down** = late / brake more.
- Dash Studio tips:
  - Expressions are simple → avoid heavy clamping/branching that causes stepped motion.
  - Force floating-point math with decimal literals (e.g., `150.0`).
  - Keep cue text (`Pit.EntryCueText`) available as a secondary label if desired.

---

## Pit Entry Assist Logging

Three structured INFO logs (edge-triggered):

- **`ACTIVATE`** — once when assist arms. Fields: distance, required distance, margin, speed delta, decel, buffer, cue. Used to confirm arming context and baseline margin.【F:PitEngine.cs†L340-L363】
- **`LINE`** — once on pit lane entry. Fields: all `ACTIVATE` fields plus `firstOK` (distance where speed first dropped to pit limit) and `okBefore` (metres compliant before the line). Used to evaluate braking timing, compare entries, and tune decel/buffer per track/car.【F:PitEngine.cs†L183-L216】
- **`END`** — once when assist disarms (pit entry or invalidation). Used to confirm clean teardown.【F:PitEngine.cs†L376-L398】

---

## Deceleration Capture (DecelCap)

- Developer-only instrumentation to empirically measure braking decel between **200→50 kph** with straight-line filtering.
- **Master switch:** `MASTER_ENABLED` (constant, default `false`). When false, module is inert at runtime and safe to ship compiled.【F:DecelCapture.cs†L6-L82】
- When explicitly enabled/armed, it logs high-frequency decel samples (dv/dt and lon accel) and distance between 200–50 kph for the current car/track/session token. START/END logs bracket each run; per-tick logs emit at 20 Hz.【F:DecelCapture.cs†L23-L213】【F:DecelCapture.cs†L214-L266】
- **Not part of normal behaviour:** requires explicit enable + toggle; otherwise no logs, no side effects.

---

## Configuration & Profiles

- **Per-car profiles (Dash tab):**
  - **Pit Entry Decel (m/s²):** `Pit.EntryDecelProfile_mps2` used in required-distance calc.
  - **Pit Entry Buffer (m):** `Pit.EntryBuffer_m` used in cue thresholds.
- **Why per-car:** braking capability and tyre/booster effects differ by car; per-class is too coarse for consistent pit entry cues.【F:CarProfiles.cs†L128-L150】【F:ProfilesManagerViewModel.cs†L547-L584】
- **Recommended starting points:**
  - **GT3:** decel ≈ **14 m/s²**, buffer **≈15 m**.
  - **GTP:** similar decel, slightly **higher buffer** for hybrid regen variability.
- Defaults auto-seed on profile creation/copy; adjust per track after reviewing `LINE` logs.【F:ProfilesManagerViewModel.cs†L645-L685】【F:ProfilesManagerViewModel.cs†L503-L505】

