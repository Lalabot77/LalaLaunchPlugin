# CarSA (Car System) — Phase 1

## Scope
CarSA provides **session-agnostic**, **class-agnostic** spatial awareness using iRacing CarIdx telemetry arrays as the source of truth. It publishes the 5 nearest cars ahead and 5 behind on track for Practice, Qualifying, and Race sessions.

CarSA is independent of the race-only Opponents subsystem and does not change Opponents or Rejoin Assist behavior.

## Truth source
- **Primary:** `CarIdx*` raw telemetry arrays (CarIdxLapDistPct, CarIdxLap, CarIdxTrackSurface, CarIdxOnPitRoad).
- **Fast-path:** Not enabled in Phase 1 (source remains `CarIdxTruth`).

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
  - `RealGapRawSec = sessionTimeSec - lastCheckpointTimeSec`
  - `RealGapAdjSec` is adjusted for lap delta and wrap behavior.
  - **Sign convention:** Ahead = **positive**, Behind = **negative**.

### Lap delta correction
- `lapDelta = oppLap - myLap`
- If `lapDelta != 0`, `RealGapAdjSec += lapDelta * LapTimeEstimateSec` is applied **only when both the player and the slot are not near S/F** (near edge = within 3% of lapPct 0/1). If either side is near the edge, the correction is skipped to avoid wrap spikes.
- If `lapDelta == 0` and the slot is **behind**, the gap is wrapped by subtracting the lap-time estimate when the raw gap implies the previous lap.
- RealGap is clamped to ±600 s to guard against telemetry spikes.
- LapDelta wrap override: if lap counters differ by ±1 at S/F but the cars are physically close (within 10% lap distance) and straddling the S/F edge (within 15% of lapPct 0/1), LapDelta is treated as `0` to prevent single-tick spikes.

### Gap RealSec grace hold
- If a slot has a valid last gap and a RealGap update is missing (no checkpoint timestamp yet), CarSA **holds the last gap for ~2 seconds** before returning `NaN`.
- During this grace hold, `ClosingRateSecPerSec` is frozen to `0` to avoid spikes.

### ClosingRate definition
- `ClosingRateSecPerSec` uses **absolute gap magnitude**.
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

## Exports (Phase 1)
Prefix: `Car.*`

System:
- `Car.Valid`
- `Car.Source` (`CarIdxTruth`)
- `Car.Checkpoints` (60)
- `Car.SlotsAhead` (5)
- `Car.SlotsBehind` (5)

Slots (Ahead01..Ahead05, Behind01..Behind05):
- Identity: `CarIdx`, `Name`, `CarNumber`, `ClassColor`
- State: `IsOnTrack`, `IsOnPitRoad`, `IsValid`
- Spatial: `LapDelta`, `Gap.RealSec`
- Derived: `ClosingRateSecPerSec`, `Status`

Debug (`Car.Debug.*`):
- Player: `PlayerCarIdx`, `PlayerLapPct`, `PlayerLap`, `PlayerCheckpointIndexNow`, `PlayerCheckpointIndexCrossed`, `PlayerCheckpointCrossed`, `SessionTimeSec`, `SourceFastPathUsed`
- RealGap validation (Ahead01/Behind01 only): `CarIdx`, distance pct, raw/adjusted gap, last checkpoint time
- Sanity: `InvalidLapPctCount`, `OnPitRoadCount`, `OnTrackCount`, `TimestampUpdatesThisTick`
- Timestamp accumulator: `TimestampUpdatesSinceLastPlayerCross` (counts checkpoint timestamp writes since the last player checkpoint crossing).
- Optional (debug-gated): `LapTimeEstimateSec`, `HysteresisReplacementsThisTick`, `SlotCarIdxChangedThisTick`, `RealGapClampsThisTick`
- Candidate filter: `FilteredHalfLapCountAhead`, `FilteredHalfLapCountBehind`

## Debug export (optional)
When `EnableCarSADebugExport` is enabled, CarSA writes a lightweight CSV snapshot on **player checkpoint crossings**:
- Path: `PluginsData/Common/LalaLaunch/Debug/CarSA_DebugExport_<SessionID_SubSessionID>.csv`
- Cadence: **checkpoint crossings only** (same event that updates RealGap).
- Columns: session time, player lap/pct/checkpoint (now + crossed), Ahead01 and Behind01 slot basics (car idx, distance pct, gap, closing rate, lap delta, on-track/pit flags), plus counters (`TimestampUpdatesThisTick`, `RealGapClampsThisTick`, `HysteresisReplacementsThisTick`, `SlotCarIdxChangedThisTick`, `FilteredHalfLapCountAhead/Behind`).

## Performance notes
- Single-pass candidate selection with fixed arrays (no per-tick allocations).
- No LINQ or string formatting in per-tick loops.
- RealGap updates every tick using `PlayerCheckpointIndexNow`.
