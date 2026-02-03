# CarSA (Car System) — SA-Core v2

## Scope
CarSA provides **session-agnostic**, **class-aware** spatial awareness using iRacing CarIdx telemetry arrays as the source of truth. It publishes the 5 nearest cars ahead and 5 behind on track for Practice, Qualifying, and Race sessions using distance-based gaps derived from car-centric LapDistPct deltas.

CarSA is independent of the race-only Opponents subsystem and does not change Opponents or Rejoin Assist behavior.

## Truth source
- **Primary:** `CarIdx*` telemetry arrays (CarIdxLapDistPct, CarIdxLap, CarIdxTrackSurface, CarIdxOnPitRoad).
- **Raw flags (optional):** `CarIdxSessionFlags`, `CarIdxPaceFlags`, and `CarIdxTrackSurfaceMaterial` when raw telemetry read mode is enabled (drives compromised evidence and debug exports).
- **Identity:** Slot Name/CarNumber/ClassColor are pulled from session info (`DriverInfo.DriversXX` preferred, fallback to `DriverInfo.CompetingDrivers`) with retry logic so replays can resolve identities once the session data arrives.
- **Class rank map:** CarSA builds a per-session class rank map from `CarClassRelSpeed` (preferred) or `CarClassEstLapTime` to label Faster/Slower class statuses.

## Car-centric state cache
CarSA keeps a car-centric shadow state per `CarIdx` that is authoritative for StatusE decisions and gap/closing-rate stability:
- **Spatial deltas:** Forward/backward distance pct, signed delta pct, and closing rate based on per-tick LapDistPct deltas.
- **Track state:** Track surface raw, pit area detection (pit lane or pit stall), on-track flag, and session flags.
- **Latches:**
  - **Out-lap** latches when a car exits pit area onto track; stays active until the next lap completes.
  - **Compromised (off-track)** latches when track surface == OffTrack; stays active until the next lap completes.
  - **Compromised (penalty)** latches when session flags include black/furled/repair/disqualify; stays active until the next lap completes.
- **Grace windows:**
  - **LapPct grace:** 0.5 s grace before clearing delta/closing data on invalid LapDistPct.
  - **Not-in-world grace:** 3.0 s before clearing latches if a car remains NotInWorld.

## Slot selection (Ahead/Behind)
- Ordering uses car-centric forward/backward distances computed from LapDistPct.
- Forward distance: `(oppPct - myPct + 1.0) % 1.0`
- Backward distance: `(myPct - oppPct + 1.0) % 1.0`
- **TrackSurface semantics:** `IsOnTrack` is true only when TrackSurface == OnTrack. Pit lane/stall is treated as pit area. NotInWorld makes a slot invalid.
- **Pit-road exclusion:** pit-road cars are excluded from candidate slots by default to reduce practice/quali noise.
- **Half-lap filter:** candidates with `distPct` in **[0.40, 0.60]** are skipped per direction to avoid S/F wrap ambiguity (tracked by debug counters).
- **Hysteresis:** a new candidate replaces a slot only if it is **at least 10% closer** (or the current slot is invalid).
- **Slot reset:** when a slot swaps to a new `CarIdx`, gap and status text caches are cleared. Identity is refreshed with retry to avoid replay timing gaps.

## Gap & closing semantics
- **Gap.TrackSec:** `distPct * lapTimeEstimateSec` (distance-based proximity gap).
- **Gap.RealSec:** legacy alias of `Gap.TrackSec` for backward compatibility.
- **ClosingRateSecPerSec:** derived from change in absolute delta pct over time; **positive values mean closing**; clamped to ±5 s/s.
- **Lap time estimate:** player average pace, else last lap, else 120 s fallback.
- **LapDelta:** computed from CarIdx lap counters with S/F straddle guards to avoid one-tick spikes when cars are physically close around the line.

## Status enums
CarSA publishes a minimal base status enum:
- `Unknown = 0`
- `Normal = 1`
- `InPits = 2` (set whenever `CarIdxOnPitRoad` is true).

## StatusE ladder (SA-Core v2)
CarSA publishes a Traffic SA “E-number” ladder per slot for dash filtering. StatusE uses car-centric latches, pit-area detection, and class rank data.

**Numeric mapping (locked):**

| Range | Value | Status | Short | Long |
| --- | --- | --- | --- | --- |
| LOW | 0 | Unknown | UNK |  |
| MID | 100 | OutLap | OUT | Out lap |
| MID | 110 | InPits | PIT | In pits |
| MID | 121 | CompromisedOffTrack | OFF | Lap Invalid |
| MID | 122 | CompromisedPenalty | PEN | Penalty |
| HIGH | 200 | FasterClass | FCL | Faster class |
| HIGH | 210 | SlowerClass | SCL | Slower class |
| HIGH | 220 | Racing | RCE | Racing |
| HIGH | 230 | LappingYou | +nL | Up +n Laps |
| HIGH | 240 | BeingLapped | -nL | Down -n Laps |

**StatusE logic (per slot, priority order):**
1. If the slot is on pit road or in a pit-area surface ⇒ `InPits` (reason `pits`).
2. If car-centric compromised penalty latch is active ⇒ `CompromisedPenalty` (reason `cmp_pen`).
3. Else if car-centric compromised off-track latch is active ⇒ `CompromisedOffTrack` (reason `cmp_off`).
4. If slot is invalid / NotInWorld / not on track ⇒ `Unknown` (reason `unknown`).
5. If car-centric out-lap latch is active ⇒ `OutLap` (reason `outlap`).
6. If `LapDelta > 0` ⇒ `LappingYou` (reason `lap_ahead`).
7. If `LapDelta < 0` ⇒ `BeingLapped` (reason `lap_behind`).
8. If same class as player ⇒ `Racing` (reason `racing`).
9. If other class and class ranks exist ⇒ `FasterClass` or `SlowerClass` (reason `otherclass`).
10. If other class but rank is unavailable ⇒ fallback based on ahead/behind direction (`FasterClass` when behind, `SlowerClass` when ahead; reason `otherclass_unknownrank`).

Gap-based relevance gating is disabled in SA-Core v2.

## Raw telemetry flags
- Player-level raw flags are published as `Car.Player.PaceFlagsRaw`, `Car.Player.SessionFlagsRaw`, `Car.Player.TrackSurfaceMaterialRaw`.
- Slot-level raw flags (`PaceFlagsRaw`, `SessionFlagsRaw`, `TrackSurfaceMaterialRaw`) are populated when raw telemetry mode includes slots.
- Debug outputs report whether each raw array was readable and the read mode/failure reason.

## Exports (SA-Core v2)
Prefix: `Car.*`

System:
- `Car.Valid`
- `Car.Source` (`CarIdxTruth`)
- `Car.SlotsAhead` (5)
- `Car.SlotsBehind` (5)
- `Car.Player.PaceFlagsRaw`
- `Car.Player.SessionFlagsRaw`
- `Car.Player.TrackSurfaceMaterialRaw`

Slots (Ahead01..Ahead05, Behind01..Behind05):
- Identity: `CarIdx`, `Name`, `CarNumber`, `ClassColor`
- State: `IsOnTrack`, `IsOnPitRoad`, `IsValid`
- Spatial: `LapDelta`, `Gap.TrackSec`, `Gap.RealSec`
- Derived: `ClosingRateSecPerSec`, `Status`, `StatusE`, `StatusShort`, `StatusLong`, `StatusEReason`
- Raw telemetry (mode permitting): `PaceFlagsRaw`, `SessionFlagsRaw`, `TrackSurfaceMaterialRaw`

Debug (`Car.Debug.*`):
- `PlayerCarIdx`, `PlayerLapPct`, `PlayerLap`, `SessionTimeSec`, `SourceFastPathUsed`
- Raw telemetry availability: `HasCarIdxPaceFlags`, `HasCarIdxSessionFlags`, `HasCarIdxTrackSurfaceMaterial`, `RawTelemetryReadMode`, `RawTelemetryFailReason`
- Slot debug: `Ahead01.CarIdx`, `Ahead01.ForwardDistPct`, `Behind01.CarIdx`, `Behind01.BackwardDistPct`
- Sanity/counters: `InvalidLapPctCount`, `OnPitRoadCount`, `OnTrackCount`, `TimestampUpdatesThisTick`, `FilteredHalfLapCountAhead`, `FilteredHalfLapCountBehind`
- Optional (debug-gated): `LapTimeEstimateSec`, `HysteresisReplacementsThisTick`, `SlotCarIdxChangedThisTick`

## Debug export (optional)
When `EnableCarSADebugExport` is enabled, CarSA writes a lightweight CSV snapshot on **every DataUpdate tick** (buffered, flushed every 20 lines or 4 KB):
- Path: `SimHub/Logs/LalapluginData/CarSA_Debug_YYYY-MM-DD_HH-mm-ss_<TrackName>.csv` (UTC timestamp, sanitized track name; repeated `_` collapsed, trimmed, clamped to 60 chars)
- Columns (grouped, validation export; expect HotLap/CoolLap extensions later):
  - **Top-level context:** `SessionTimeSec`, `SessionState`, `SessionTypeName`, `PlayerCarIdx`, `PlayerLap`, `PlayerLapPct`, `PlayerCheckpointIndexNow`, `PlayerCheckpointIndexCrossed`, `NotRelevantGapSec`.
  - **Per-slot (Ahead01..Ahead05, Behind01..Behind05):** `CarIdx`, `CarNumber`, `Name`, `ClassColor`, `DistPct`, `GapTrackSec`, `ClosingRateSecPerSec`, `LapDelta`, `IsOnPitRoad`, `StatusE`, `StatusEReason`, `TrackSurfaceRaw`, `SessionFlagsRaw`.
  - **Player tail:** `PlayerTrackSurfaceRaw`, `PlayerSessionFlagsRaw`.

## Performance notes
- Single-pass candidate selection with fixed arrays (no per-tick allocations).
- No LINQ or string formatting in per-tick loops.
- Car-centric cache avoids per-slot computations for closing and status latches.
