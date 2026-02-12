# CarSA (Car System) — SA-Core v2

## Scope
CarSA provides **session-agnostic**, **class-aware** spatial awareness using iRacing CarIdx telemetry arrays as the source of truth. It publishes the 5 nearest cars ahead and 5 behind on track for Practice, Qualifying, and Race sessions using distance-based gaps derived from car-centric LapDistPct deltas, plus gate-gap v2 relative proximity.

CarSA is independent of the race-only Opponents subsystem and does not change Opponents or Rejoin Assist behavior.

## Truth source
- **Primary:** `CarIdx*` telemetry arrays (CarIdxLapDistPct, CarIdxLap, CarIdxTrackSurface, CarIdxOnPitRoad).
- **Raw flags (optional):** `CarIdxSessionFlags`, `CarIdxPaceFlags`, and `CarIdxTrackSurfaceMaterial` when raw telemetry read mode is enabled (drives compromised evidence and debug exports).
- **Identity:** Slot Name/CarNumber/ClassColor are pulled from session info (`DriverInfo.DriversXX` preferred, fallback to `DriverInfo.CompetingDrivers`) with retry logic so replays can resolve identities once the session data arrives.
- **Class rank map:** CarSA builds a per-session class rank map from `CarClassRelSpeed` (preferred) or `CarClassEstLapTime` to label Faster/Slower class statuses.
- **Strength-of-field:** An iRating SOF average is computed across active CarSA slots for quick session context.

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
- **Gap.RelativeSec (GateGap v2):** mini-sector gate timing produces a gate truth gap that is filtered forward in time with a rate EMA, corrected toward fresh truth, and held briefly (sticky publish) if inputs drop; values are mapped to ahead/behind sign so wraps stay direction-safe. Falls back to `Gap.TrackSec` when no gate data exists. Normalization guards handle lapped cars and mismatch fallbacks when gate gaps diverge from track gaps.
- **Gap.RelativeSource:** tracks which input fed the relative gap (filtered, truth, track fallback, sticky hold, invalid).
- **Slot 01 precision gaps:** `Car.Ahead01P.Gap.Sec` / `Car.Behind01P.Gap.Sec` surface the best available gate-gap truth/filtered proximity (falling back to track gaps) for the closest ahead/behind slot.
- **ClosingRateSecPerSec:** derived from change in absolute delta pct over time; **positive values mean closing**; clamped to ±5 s/s.
- **Lap time estimate:** player average pace, else last lap, else 120 s fallback.
- **LapDelta:** computed from CarIdx lap counters with S/F straddle guards to avoid one-tick spikes when cars are physically close around the line.

## Info banner behaviour
- **Burst model:** `InfoVisibility`/`Info` are driven by short bursts instead of continuous rotation.
  - **S/F burst:** when a same-class slot crosses the start/finish window (0.00–0.05 lap pct), a 9 s burst cycles through **laps-since-pit**, **LL vs BL**, and **LL vs Me** messages (3 s each).
  - **Half-lap burst:** when a same-class slot enters the 0.55–1.00 lap pct window, a 9 s burst cycles **Live Δ** and **laps-since-pit** (3 s phases with fallback to available messages).
  - **Baseline gating:** outside bursts, baseline info only shows when `StatusE == Unknown` with a 1 s debounce to prevent flicker; info is cleared for invalid slots, cross-class opponents, or slot swaps.
- **Burst latching:** bursts are latched per CarIdx and cancelled if the slot changes identity mid-burst.

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
| MID | 130 | HotlapWarning | FAST! | Hot lap interference warning |
| MID | 131 | HotlapCaution | PUSH | Push lap |
| MID | 132 | HotlapHot | HOT | Hot lap |
| MID | 140 | CoolLapWarning | SLOW! | Cool lap interference warning |
| MID | 141 | CoolLapCaution | COOL | Cool lap |
| HIGH | 200 | FasterClass | FCL | Faster class |
| HIGH | 210 | SlowerClass | SCL | Slower class |
| HIGH | 220 | Racing | RCE | Racing |
| HIGH | 230 | LappingYou | +nL | Up +n Laps |
| HIGH | 240 | BeingLapped | -nL | Down -n Laps |

## StatusE + colour decode (plugin defaults)

The dash uses **two colour channels** per CarSA slot:
- `StatusBgHex`: background colour selected from the StatusE map.
- `BorderHex`: border colour selected from the tag/priority border-mode resolver.

### A) StatusE background colour map (`StatusBgHex`)

Default map from `LaunchPluginSettings.CarSAStatusEColorMap`:

| StatusE | Value | Hex | Colour name | Notes |
| --- | ---: | --- | --- | --- |
| Unknown | 0 | `#000000` | Black | Fallback/default state. |
| OutLap | 100 | `#696969` | Dim Gray | Out-lap latch active. |
| InPits | 110 | `#C0C0C0` | Silver | Pit road / pit-area state. |
| SuspectInvalid | 120 | `#FFA500` | Orange | Enum exists; reserved/compat path. |
| CompromisedOffTrack | 121 | `#FF0000` | Red | Off-track compromise latch. |
| CompromisedPenalty | 122 | `#FFA500` | Orange | Penalty compromise latch. |
| HotlapWarning | 130 | `#FF0000` | Red | FAST interference warning. |
| HotlapCaution | 131 | `#FFFF00` | Yellow | PUSH intent band. |
| HotlapHot | 132 | `#FFFF00` | Yellow | HOT intent band. |
| CoolLapWarning | 140 | `#FF0000` | Red | SLOW interference warning. |
| CoolLapCaution | 141 | `#FFFF00` | Yellow | COOL intent band. |
| FasterClass | 200 | `#000000`* | Black* | *When `ClassColorHex` is valid, CarSA uses class colour instead of map value.* |
| SlowerClass | 210 | `#000000`* | Black* | *When `ClassColorHex` is valid, CarSA uses class colour instead of map value.* |
| Racing | 220 | `#008000` | Green | Same-class racing context. |
| LappingYou | 230 | `#0000FF` | Blue | Opponent up laps on player. |
| BeingLapped | 240 | `#ADD8E6` | Light Blue | Opponent down laps to player. |

### B) Tagged-driver crossover (border decode via `BorderHex`)

Tagging does **not** change StatusE value; it changes the border via a priority resolver.

Border-mode priority (highest first):
1. Manual **Teammate** tag → `TEAM`
2. Manual **Bad** tag → `BAD`
3. Manual **Friend** tag → `FRIEND`
4. Telemetry teammate match → `TEAM`
5. Class leader → `LEAD`
6. Other class → `OCLS`
7. Fallback → `DEF`

Default border map from `LaunchPluginSettings.CarSABorderColorMap` + resolver override:

| BorderMode | Trigger source | Hex | Colour name | Notes |
| --- | --- | --- | --- | --- |
| FRIEND | Manual Friend tag | `#00FF00` | Lime | Hardcoded resolver override (ignores map for FRIEND). |
| TEAM | Manual Teammate tag OR telemetry teammate | `#FF69B4` | Hot Pink | Comes from border map defaults. |
| BAD | Manual Bad tag | `#FF0000` | Red | Comes from border map defaults. |
| LEAD | Position in class = 1 (same class) | `#FF00FF` | Magenta | Comes from border map defaults. |
| OCLS | Other-class car | `#0000FF` | Blue | Comes from border map defaults. |
| DEF | Default/no special flag | `#F5F5F5` | White Smoke | Comes from border map defaults. |
| (final fallback) | Invalid/missing map values | `#A9A9A9` | Dark Gray | Resolver fallback if no valid default border colour. |

### C) Tag names used by the driver-tag system

Driver tags are normalized to one of:
- `Friend`
- `Teammate` (also accepts legacy `Team`)
- `Bad`

These tags feed the border-mode resolver above and therefore define the visual crossover between the tagged-driver system and CarSA slot styling.

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

## Hot/Cool intent bands
Hot/Cool intent uses `DeltaBestSec` with seconds-based bands (full system in Practice/Qual/Warmup):
- `deltaBest < 0.00` ⇒ **HOT** (intent `Hot`, status `HotlapHot` unless FAST conflict warning applies).
- `0.00 <= deltaBest <= 0.50` ⇒ **PUSH** (intent `Push`, status `HotlapCaution` unless FAST conflict warning applies).
- `0.50 < deltaBest <= 1.00` ⇒ **NONE** (no message, no Hot/Cool StatusE override).
- `deltaBest > 1.00` ⇒ **COOL** (intent `Cool`, status `CoolLapCaution` unless SLOW conflict warning applies).

`FAST!` and `SLOW!` remain interference warnings driven by the existing conflict test (`behind + conflict` for FAST, `ahead + conflict` for SLOW). They are not DeltaBest intent bands.

## SessionType policy
- **Practice/Qual/Warmup:** full Hot/Cool system applies per the existing intent and conflict rules. Existing non-race suppression remains `Racing`/`LappingYou`/`BeingLapped` (220/230/240), and StatusE stays gated by `SessionState == 4` with the current settle delay.
- **Race:** FAST/SLOW interference warnings (`HotlapWarning`/`CoolLapWarning`, 130/140) remain enabled, while base intent messages HOT/PUSH/COOL (`HotlapHot`/`HotlapCaution`/`CoolLapCaution`, 132/131/141) are suppressed.
- **Lone Qualify + Offline Testing:** StatusE is forced to `Unknown` for all slots.

## Raw telemetry flags
- Player-level raw flags are published as `Car.Player.PaceFlagsRaw`, `Car.Player.SessionFlagsRaw`, `Car.Player.TrackSurfaceMaterialRaw` when soft debug + raw telemetry mode are enabled.
- Slot-level raw flags (`SessionFlagsRaw`, `TrackSurfaceMaterialRaw`) are populated when raw telemetry mode includes slots (and soft debug is on).
- Debug outputs report whether each raw array was readable and the read mode/failure reason.

## Exports (SA-Core v2)
Prefix: `Car.*`

System:
- `Car.Valid`
- `Car.Source` (`CarIdxTruth`)
- `Car.SlotsAhead` (5)
- `Car.SlotsBehind` (5)
- `Car.iRatingSOF`
- `Car.Ahead01P.Gap.Sec`
- `Car.Behind01P.Gap.Sec`
- `Car.Player.PaceFlagsRaw`
- `Car.Player.SessionFlagsRaw`
- `Car.Player.TrackSurfaceMaterialRaw`

Slots (Ahead01..Ahead05, Behind01..Behind05):
- Identity: `CarIdx`, `Name`, `CarNumber`, `ClassColor`, `ClassColorHex`, `ClassName`, `PositionInClass`, `IRating`, `Licence`, `SafetyRating`
- State: `IsOnTrack`, `IsOnPitRoad`, `IsValid`
- Spatial: `LapDelta`, `Gap.TrackSec`, `Gap.RelativeSec`, `Gap.RelativeSource`, `LapsSincePit`, `BestLapTimeSec`, `LastLapTimeSec`, `BestLap`, `BestLapIsEstimated`, `LastLap`, `DeltaBestSec`, `DeltaBest`, `EstLapTimeSec`, `EstLapTime`
- Derived: `ClosingRateSecPerSec`, `Status`, `StatusE`, `StatusShort`, `StatusLong`, `StatusEReason`, `HotScore`, `HotVia`
- Info banners: `InfoVisibility`, `Info` (bursted: laps-since-pit, last-lap vs best, last-lap vs me, live Δ; baseline gated to `StatusE == Unknown`)
- Raw telemetry (mode permitting): `SessionFlagsRaw`, `TrackSurfaceMaterialRaw`

Debug (`Car.Debug.*`):
- `PlayerCarIdx`, `PlayerLapPct`, `PlayerLap`, `SessionTimeSec`, `SourceFastPathUsed`
- Raw telemetry availability: `HasCarIdxPaceFlags`, `HasCarIdxSessionFlags`, `HasCarIdxTrackSurfaceMaterial`, `RawTelemetryReadMode`, `RawTelemetryFailReason`
- Slot debug: `Ahead01.CarIdx`, `Ahead01.ForwardDistPct`, `Behind01.CarIdx`, `Behind01.BackwardDistPct`
- Sanity/counters: `InvalidLapPctCount`, `OnPitRoadCount`, `OnTrackCount`, `TimestampUpdatesThisTick`, `FilteredHalfLapCountAhead`, `FilteredHalfLapCountBehind`
- Optional (debug-gated): `LapTimeEstimateSec`, `HysteresisReplacementsThisTick`, `SlotCarIdxChangedThisTick`
- Optional (debug-gated): `LapTimeUsedSec`

## Debug export (optional)
When `EnableCarSADebugExport` is enabled (and soft debug is on), CarSA writes a lightweight CSV snapshot on **every DataUpdate tick** (buffered, flushed every 20 lines or 4 KB):
- Path: `SimHub/Logs/LalapluginData/CarSA_Debug_YYYY-MM-DD_HH-mm-ss_<TrackName>.csv` (UTC timestamp, sanitized track name; repeated `_` collapsed, trimmed, clamped to 60 chars)
- Columns (grouped, validation export; expect HotLap/CoolLap extensions later):
  - **Top-level context:** `SessionTimeSec`, `SessionState`, `SessionTypeName`, `PlayerCarIdx`, `PlayerLap`, `PlayerLapPct`, `PlayerCheckpointIndexNow`, `PlayerCheckpointIndexCrossed`, `NotRelevantGapSec`.
- **Per-slot (Ahead01..Ahead05, Behind01..Behind05):** `CarIdx`, `CarNumber`, `Name`, `ClassColor`, `DistPct`, `GapTrackSec`, `GapRelativeSec`, `GapRelativeSource`, `ClosingRateSecPerSec`, `LapDelta`, `IsOnPitRoad`, `StatusE`, `StatusEReason`, `TrackSurfaceRaw`, `SessionFlagsRaw`.
  - **Player tail:** `PlayerTrackSurfaceRaw`, `PlayerSessionFlagsRaw`.

## Off-track probe export (optional)
When `EnableOffTrackDebugCsv` is enabled (and a probe `OffTrackDebugProbeCarIdx` is configured), the plugin writes `OffTrackDebug_<Track>_<Timestamp>.csv` under `SimHub/Logs/LalapluginData/` with raw telemetry and latch state for the probe car. If `OffTrackDebugLogChangesOnly` is enabled, rows are written only when the snapshot changes:
- **Context:** session time/state, session flags (hex + dec), probe CarIdx, and per-car telemetry (`CarIdxTrackSurface`, `CarIdxTrackSurfaceMaterial`, `CarIdxSessionFlags`, `CarIdxOnPitRoad`, `CarIdxLap`, `CarIdxLapDistPct`).
- **Latch state:** off-track now, off-track streak, first-seen time, compromised-until lap, compromised-off-track/penalty active flags, and latch enable.
- **Player incidents:** player CarIdx, incident count, and incident delta for the tick.

## Performance notes
- Single-pass candidate selection with fixed arrays (no per-tick allocations).
- No LINQ or string formatting in per-tick loops.
- Car-centric cache avoids per-slot computations for closing and status latches.
