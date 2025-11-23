# Fuel Tab Leader Pace Data Flow

## Source (LalaLaunch.UpdateLiveFuelCalcs)
- **Helper:** `ReadLeaderLapTimeSeconds(PluginManager, data)` reads the leader's most recent lap time strictly from the RSC class-leader integration (`iRacing_ClassLeaderLastLapTime` / `iRacing_ClassLeaderAverageLapTime`) when a lap crossing is detected.
- **Trigger:** Executed inside `UpdateLiveFuelCalcs(GameData data)` when `lapCrossed` is true (start/finish line crossed based on track percent).
- **Raw storage:** `_recentLeaderLapTimes` keeps a rolling list of recent leader lap times (seconds) and `_lastLeaderLapTimeSec` tracks the most recent sample.

## Aggregation in LalaLaunch
- **Add sample:** When `leaderLastLapSec` is between 20s and 900s and differs from the last stored value, it is appended to `_recentLeaderLapTimes`; the list is trimmed to `LapTimeSampleCount` entries.
- **Average:** `LiveLeaderAvgPaceSeconds` is set to the average of `_recentLeaderLapTimes`; if no samples exist, it is set to 0.
- **Reset on feed drop:** If `leaderLastLapSec <= 0` while `_recentLeaderLapTimes` has entries, the plugin treats this as a telemetry dropout and clears `_recentLeaderLapTimes`, `_lastLeaderLapTimeSec`, and `LiveLeaderAvgPaceSeconds`.
- **Unavailable source:** If the RSC/class-leader feed is missing or cannot be read, the helper returns `0.0` (no leader pace) and logs a single `[FuelLeader]` info line for the session (reset on session changes).

## Hand-off to FuelCalcs
- **Exposed value:** `LalaLaunch.LiveLeaderAvgPaceSeconds` is read inside `FuelCalcs.ApplyLiveLapPaceEstimate` as `leaderAvgPace`.
- **UI string:** If `leaderAvgPace > 0`, `LiveLeaderPaceInfo` is formatted as a `m:ss.fff` string; otherwise it is set to "-".

## Pace delta computation in FuelCalcs
- **Inputs:** `avgSeconds` (driver's live average lap time) and `leaderAvgPace` (from `LiveLeaderAvgPaceSeconds`).
- **Delta + storage:** When both inputs are positive, delta = `avgSeconds - leaderAvgPace`. `AvgDeltaToLdrValue` displays this as text, and `LeaderDeltaSeconds` stores `max(delta, 0)` for strategy math.
- **No data path:** If either input is missing/invalid, `AvgDeltaToLdrValue` is set to "-" while preserving any manual slider value so it can continue driving the strategy. In the absence of live telemetry, the "Your Pace vs Leader (s)" slider drives a manual delta that strategy math uses immediately for planning.

## UI + strategy consumers
- **Live Session Snapshot:** Uses `RacePaceVsLeaderSummary` and `LiveLeaderPaceInfo` (set via `AvgDeltaToLdrValue` and `LiveLeaderAvgPaceSeconds`) to show "Race Pace vs Leader".
- **Fuel tab planner:** The "Your Pace vs Leader (s)" slider and label bind to `AvgDeltaToLdrValue` / `LeaderDeltaSeconds` to display the same delta.
- **Strategy math:** In `CalculateStrategy()`, leader lap time is reconstructed as `num2 = num3 - leaderDelta` where `leaderDelta` comes from live telemetry when available or from the manual slider when no live leader data exists. This influences remaining-lap estimates and refuelling.

## Reset / edge cases
- **Session change / snapshot reset:** `ResetSnapshotDisplays()` clears `LiveLeaderPaceInfo`, `AvgDeltaToLdrValue`, and sets `LeaderDeltaSeconds` to 0.0 along with other live snapshot fields.
- **Feed loss:** In `UpdateLiveFuelCalcs`, if leader timing drops (`leaderLastLapSec <= 0` with stored samples), leader pace state is cleared so downstream calculations do not reuse stale values.
- **No live pace:** When `avgSeconds <= 0` or `leaderAvgPace <= 0` in `ApplyLiveLapPaceEstimate`, leader delta text is set to "-" while preserving any manual slider value so manual planning can continue without being wiped each lap.
- **No RSC / no leader:** When no class-leader sample is available, UI fields stay at "-", `LeaderDeltaSeconds` remains 0, and strategy validation treats leader pace as unavailable rather than invalid.

## Known current symptom
- On this branch, SimHub replay shows no leader delta on the Fuel tab even though telemetry contains leader information. For the UI to display a value, `LiveLeaderAvgPaceSeconds` must be > 0 and `LeaderDeltaSeconds` / `AvgDeltaToLdrValue` must be populated in `ApplyLiveLapPaceEstimate`.

## Unsupported sources (documented only)
- SimHub exposes `GameRawData.SessionData.DriverInfo.DriversXX.CarClassEstLapTime` and generic leader fields, but these are **not** used because the plugin cannot reliably identify the correct class leader in multi-class sessions. Only the RSC class-leader integration is supported for leader pace.
