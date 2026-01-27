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
- Default: pit-road cars are **excluded** from candidate slots to reduce practice/quali noise.
- Slot stability: a candidate replaces the current slot only if it is **at least 10% closer** (or the current slot is invalid).
- Slot stability: if a slot swaps to a new `CarIdx`, the gap/closing-rate history is cleared to avoid carrying stale gap data across cars.

## RealGap (checkpoint stopwatch)
- **60 checkpoints** evenly spaced around lapPct [0..1).
- For each (checkpoint, carIdx), CarSA stores the last timestamp when that car crossed the checkpoint.
- When the player crosses a checkpoint, CarSA computes RealGap for the current slots:
  - `RealGapRawSec = sessionTimeSec - lastCheckpointTimeSec`
  - `RealGapAdjSec` is adjusted for lap delta and wrap behavior.
  - **Sign convention:** Ahead = **positive**, Behind = **negative**.

### Lap delta correction
- `lapDelta = oppLap - myLap`
- If `lapDelta != 0`, `RealGapAdjSec += lapDelta * LapTimeEstimateSec`.
- If `lapDelta == 0` and the slot is **behind**, the gap is wrapped by subtracting the lap-time estimate when the raw gap implies the previous lap.
- RealGap is clamped to ±600 s to guard against telemetry spikes.

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
- Player: `PlayerCarIdx`, `PlayerLapPct`, `PlayerLap`, `PlayerCheckpointIndex`, `PlayerCheckpointCrossed`, `SessionTimeSec`, `SourceFastPathUsed`
- RealGap validation (Ahead01/Behind01 only): `CarIdx`, distance pct, raw/adjusted gap, last checkpoint time
- Sanity: `InvalidLapPctCount`, `OnPitRoadCount`, `OnTrackCount`, `TimestampUpdatesThisTick`
- Optional (debug-gated): `LapTimeEstimateSec`, `HysteresisReplacementsThisTick`, `RealGapClampsThisTick`

## Performance notes
- Single-pass candidate selection with fixed arrays (no per-tick allocations).
- No LINQ or string formatting in per-tick loops.
- RealGap updates only on player checkpoint crossings.
