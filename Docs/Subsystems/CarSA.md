# CarSA (Car System) — Phase 2.2

## Scope
CarSA provides **session-agnostic**, **class-agnostic** spatial awareness using iRacing CarIdx telemetry arrays as the source of truth. It publishes the 5 nearest cars ahead and 5 behind on track for Practice, Qualifying, and Race sessions.

CarSA is independent of the race-only Opponents subsystem and does not change Opponents or Rejoin Assist behavior.

## Truth source
- **Primary:** `CarIdx*` raw telemetry arrays (CarIdxLapDistPct, CarIdxLap, CarIdxTrackSurface, CarIdxOnPitRoad).
- **Raw flags (optional):** `CarIdxPaceFlags`, `CarIdxSessionFlags`, `CarIdxTrackSurfaceMaterial` when raw telemetry read mode is enabled (drives compromised-lap inference and debug visibility).
- **Fast-path:** Not enabled in Phase 1 (source remains `CarIdxTruth`).
- **Identity:** Slot Name/CarNumber/ClassColor are populated on slot rebinds/session reset from `SessionData.DriverInfo.CompetingDrivers` (UserName/CarNumber/CarClassColor).

## Slot selection (Ahead/Behind)
- Ordering uses CarIdx lap distance percentages (`LapDistPct`) for all cars.
- Forward distance: `(oppPct - myPct + 1.0) % 1.0`
- Backward distance: `(myPct - oppPct + 1.0) % 1.0`
- TrackSurface semantics: `IsOnTrack` is **true only when TrackSurface == OnTrack**; OffTrack/OnPitRoad/NotInWorld are treated as not on track, and NotInWorld marks the slot invalid.
- Default: pit-road cars are **excluded** from candidate slots to reduce practice/quali noise.
- Slot stability: a candidate replaces the current slot only if it is **at least 10% closer** (or the current slot is invalid).
- Slot stability: if a slot swaps to a new `CarIdx`, the gap/closing-rate history is cleared to avoid carrying stale gap data across cars.
- Half-lap filter: candidates with `distPct` in **[0.40, 0.60]** are skipped per direction to avoid S/F wrap ambiguity (tracked by debug counters).

## RealGap (checkpoint stopwatch)
- **60 checkpoints** evenly spaced around lapPct [0..1).
- For each (checkpoint, carIdx), CarSA stores the last timestamp when that car crossed the checkpoint.
- CarSA computes the player’s **current checkpoint index** every tick:
  - `PlayerCheckpointIndexNow = floor(PlayerLapPct * CheckpointCount)` (clamped 0..59). It only returns `-1` when `PlayerLapPct` is invalid.
  - `PlayerCheckpointIndexCrossed` updates **only** on the crossing tick and is `-1` otherwise.
- RealGap is updated **every tick** using `PlayerCheckpointIndexNow`:
  - `RealGapRawSec = playerCheckpointTimeSec - lastCheckpointTimeSec`
  - `RealGapAdjSec` is adjusted for lap delta and wrap behavior.
  - **Sign convention:** Ahead = **positive**, Behind = **negative**.

### Lap delta correction
- `lapDelta = oppLap - myLap`
- If `lapDelta != 0`, `RealGapAdjSec += lapDelta * LapTimeEstimateSec` is applied **unless both the player and the slot are near S/F and physically close** (near edge = within 3% of lapPct 0/1, close = within 10% lap distance). This suppresses only the true S/F straddle spike.
- If `lapDelta == 0` and the slot is **behind**, the gap is wrapped by subtracting the lap-time estimate when the raw gap implies the previous lap; when both cars are near S/F and physically close, the wrap uses a stricter threshold (0.90 * lap time) to avoid false wraps.
- On the **first RealGap update after a slot rebind**, lap-delta and behind-wrap corrections are suppressed (one-tick settle guard) and cleared after the first successful RealGap publish.
- RealGap is clamped to ±600 s to guard against telemetry spikes.
- LapDelta wrap override: if lap counters differ by ±1 at S/F but the cars are physically close (within 10% lap distance) and straddling the S/F edge (within 15% of lapPct 0/1), LapDelta is treated as `0` to prevent single-tick spikes.

### Gap RealSec grace hold
- If a slot has a valid last gap and a RealGap update is missing (no checkpoint timestamp yet), CarSA **holds the last gap for ~2 seconds** before returning `NaN`.
- During this grace hold, `ClosingRateSecPerSec` is frozen to `0` to avoid spikes.

### ClosingRate definition
- `ClosingRateSecPerSec` uses **absolute gap magnitude** derived from `GapTrackSec`.
- Positive values mean the gap is **shrinking** (closing), negative values mean the gap is growing (dropping back), regardless of ahead/behind sign.

### Lap time estimate (used for RealGap)
Priority order:
1. Player’s running average pace (from existing pace model).
2. Player’s last lap time.
3. Fallback (120 s).

## Status enum (Phase 1)
CarSA defines a minimal, stable status enum:
- `Unknown = 0`
- `Normal = 1`
- `InPits = 2` (set whenever `CarIdxOnPitRoad` is true).

## StatusE ladder (Phase 2.2)
CarSA publishes a Traffic SA “E-number” ladder per slot for dash filtering. Values are grouped into LOW/MID/HIGH numeric ranges, and Phase 2.2 enables real status selection beyond `Unknown`.

**Numeric mapping (locked):**

| Range | Value | Status | Short | Long |
| --- | --- | --- | --- | --- |
| LOW | 0 | Unknown | UNK | Unknown |
| MID | 100 | OutLap | OUT | Out lap |
| MID | 110 | InPits | PIT | In pits |
| MID | 120 | CompromisedThisLap | CMP | Compromised |
| MID | 190 | NotRelevant | NR | Not relevant |
| HIGH | 200 | FasterClass | F+ | Faster class |
| HIGH | 210 | SlowerClass | S- | Slower class |
| HIGH | 220 | Racing | RCE | Racing |
| HIGH | 230 | LappingYou | LY | Lapping you |
| HIGH | 240 | BeingLapped | BL | Being lapped |

**Phase 2.2 logic (per slot, priority order):**
1. If the slot is invalid or `TrackSurface == NotInWorld` ⇒ `NotRelevant` (190, reason `invalid`).
2. If `IsOnPitRoad` ⇒ `InPits` (110, reason `pits`).
3. If not on track ⇒ `NotRelevant` (190, reason `invalid`).
4. If compromised evidence exists (off-track surface, track material >= 15, or session flags indicate compromised) ⇒ `CompromisedThisLap` (120, reason `cmp`).
5. If `abs(GapTrackSec) > NotRelevantGapSec` ⇒ `NotRelevant` (190, reason `nr_gap`).
6. If slot just exited pits and is on the out-lap ⇒ `OutLap` (100, reason `outlap`).
7. If `LapDelta > 0` ⇒ `LappingYou` (230, reason `lapping_you`).
8. If `LapDelta < 0` ⇒ `BeingLapped` (240, reason `you_are_lapping`).
9. If Opponents identifies this slot as a live fight (lap ≥1, `LapsToFight > 0`, identity match) ⇒ `Racing` (220, reason `racing`).
10. If class color mismatches the player’s class ⇒ `FasterClass` (200, reason `otherclass`). Phase 2.2 uses `FasterClass` as the placeholder for “other class”; `SlowerClass` remains reserved.
11. Else ⇒ `Unknown` (0, reason `unknown`).

**Configuration:**
- `NotRelevantGapSec` (double, default **10.0s**) is stored in global plugin settings and persists across sessions.

## Gap semantics (Phase 2.2)
- **Gap.TrackSec:** proximity gap derived from checkpoint stopwatch without lap-delta adjustment (used for closing-rate).
- **Gap.RaceSec:** race-style gap with lap-delta adjustment applied.
- **Gap.RealSec:** current published gap mirror of `Gap.RaceSec` (signed + ahead / - behind).

## Raw telemetry flags (Phase 2.2)
- Player-level raw flags are published as `Car.Player.PaceFlagsRaw`, `Car.Player.SessionFlagsRaw`, `Car.Player.TrackSurfaceMaterialRaw`.
- Slot-level raw flags (`PaceFlagsRaw`, `SessionFlagsRaw`, `TrackSurfaceMaterialRaw`) are populated when raw telemetry mode includes slots.
- Debug outputs report whether each raw array was readable and the read mode/failure reason (see Debug exports below).

## Exports (Phase 2.2)
Prefix: `Car.*`

System:
- `Car.Valid`
- `Car.Source` (`CarIdxTruth`)
- `Car.Checkpoints` (60)
- `Car.SlotsAhead` (5)
- `Car.SlotsBehind` (5)
- `Car.Player.PaceFlagsRaw`
- `Car.Player.SessionFlagsRaw`
- `Car.Player.TrackSurfaceMaterialRaw`

Slots (Ahead01..Ahead05, Behind01..Behind05):
- Identity: `CarIdx`, `Name`, `CarNumber`, `ClassColor`
- State: `IsOnTrack`, `IsOnPitRoad`, `IsValid`
- Spatial: `LapDelta`, `Gap.RealSec`, `Gap.TrackSec`, `Gap.RaceSec`
- Derived: `ClosingRateSecPerSec`, `Status`, `StatusE`, `StatusShort`, `StatusLong`
- Raw telemetry (mode permitting): `PaceFlagsRaw`, `SessionFlagsRaw`, `TrackSurfaceMaterialRaw`

Debug (`Car.Debug.*`):
- Player: `PlayerCarIdx`, `PlayerLapPct`, `PlayerLap`, `PlayerCheckpointIndexNow`, `PlayerCheckpointIndexCrossed`, `PlayerCheckpointCrossed`, `SessionTimeSec`, `SourceFastPathUsed`
- Raw telemetry: `HasCarIdxPaceFlags`, `HasCarIdxSessionFlags`, `HasCarIdxTrackSurfaceMaterial`, `RawTelemetryReadMode`, `RawTelemetryFailReason`
- RealGap validation (Ahead01/Behind01 only): `CarIdx`, distance pct, raw/adjusted gap, last checkpoint time
- Sanity: `InvalidLapPctCount`, `OnPitRoadCount`, `OnTrackCount`, `TimestampUpdatesThisTick`
- Timestamp accumulator: `TimestampUpdatesSinceLastPlayerCross` (counts checkpoint timestamp writes since the last player checkpoint crossing).
- Optional (debug-gated): `LapTimeEstimateSec`, `HysteresisReplacementsThisTick`, `SlotCarIdxChangedThisTick`, `RealGapClampsThisTick`
- Candidate filter: `FilteredHalfLapCountAhead`, `FilteredHalfLapCountBehind`

## Debug export (optional)
When `EnableCarSADebugExport` is enabled, CarSA writes a lightweight CSV snapshot on **player checkpoint crossings**:
- Path: `SimHub/Logs/LalapluginData/CarSA_Debug_YYYY-MM-DD_HH-mm-ss_<TrackName>.csv` (UTC timestamp, sanitized track name; repeated `_` collapsed, trimmed, and clamped to 60 chars)
- Cadence: **checkpoint crossings only** (same event that updates RealGap).
- Columns (grouped):
  - **Player:** `SessionTimeSec`, `PlayerLap`, `PlayerLapPct`, `CheckpointIndexNow`, `CheckpointIndexCrossed`, `NotRelevantGapSec`.
  - **Ahead01 / Behind01:** `CarIdx`, distance pct, `GapRealSec`, `GapTrackSec`, `GapRaceSec`, `ClosingRateSecPerSec`, `LapDelta`, `IsOnTrack`, `IsOnPitRoad`, `StatusE`, `StatusShort`, `StatusLong`.
  - **Status latches:** `OutLapLatched`, `CompromisedThisLapLatched`, `CurrentLap`, `LastLap`, `OutLapActive`, `OutLapLap`, `WasOnPitRoad`, `CompromisedLap`.
  - **Compromised flags:** `CmpFlag_Black`, `CmpFlag_Furled`, `CmpFlag_Repair`, `CmpFlag_Disqualify`.
  - **Raw flags:** `TrackSurfaceRaw`, `TrackSurfaceMaterialRaw`, `SessionFlagsRaw`.
  - **Compromised evidence:** `CmpEvidence_OffTrack`, `CmpEvidence_Material`, `CmpEvidence_SessionFlags`.
  - **Status metadata:** `StatusEReason`, `StatusEChanged`, `CarIdxChanged`.
  - **Counters:** `TimestampUpdatesThisTick`, `RealGapClampsThisTick`, `HysteresisReplacementsThisTick`, `SlotCarIdxChangedThisTick`, `FilteredHalfLapCountAhead`, `FilteredHalfLapCountBehind`.

## Performance notes
- Single-pass candidate selection with fixed arrays (no per-tick allocations).
- No LINQ or string formatting in per-tick loops.
- RealGap updates every tick using `PlayerCheckpointIndexNow`.
