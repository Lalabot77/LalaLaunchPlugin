# Fuel Tab Leader Pace Data Flow

## Source (LalaLaunch.UpdateLiveFuelCalcs)
- **Helper:** `ReadLeaderLapTimeSeconds(PluginManager, data)` reads the leader's most recent lap time from telemetry when a lap crossing is detected.
- **Trigger:** Executed inside `UpdateLiveFuelCalcs(GameData data)` when `lapCrossed` is true (start/finish line crossed based on track percent).
- **Raw storage:** `_recentLeaderLapTimes` keeps a rolling list of recent leader lap times (seconds) and `_lastLeaderLapTimeSec` tracks the most recent sample.

## Aggregation in LalaLaunch
- **Add sample:** When `leaderLastLapSec` is between 20s and 900s and differs from the last stored value, it is appended to `_recentLeaderLapTimes`; the list is trimmed to `LapTimeSampleCount` entries.
- **Average:** `LiveLeaderAvgPaceSeconds` is set to the average of `_recentLeaderLapTimes`; if no samples exist, it is set to 0.
- **Reset on feed drop:** If `leaderLastLapSec <= 0` while `_recentLeaderLapTimes` has entries, the plugin treats this as a telemetry dropout and clears `_recentLeaderLapTimes`, `_lastLeaderLapTimeSec`, and `LiveLeaderAvgPaceSeconds`.

## Hand-off to FuelCalcs
- **Exposed value:** `LalaLaunch.LiveLeaderAvgPaceSeconds` is read inside `FuelCalcs.ApplyLiveLapPaceEstimate` as `leaderAvgPace`.
- **UI string:** If `leaderAvgPace > 0`, `LiveLeaderPaceInfo` is formatted as a `m:ss.fff` string; otherwise it is set to "-".

## Pace delta computation in FuelCalcs
- **Inputs:** `avgSeconds` (driver's live average lap time) and `leaderAvgPace` (from `LiveLeaderAvgPaceSeconds`).
- **Delta + storage:** When both inputs are positive, delta = `avgSeconds - leaderAvgPace`. `AvgDeltaToLdrValue` displays this as text, and `LeaderDeltaSeconds` stores `max(delta, 0)` for strategy math.
- **No data path:** If either input is missing/invalid, `AvgDeltaToLdrValue` is set to "-" and `LeaderDeltaSeconds` is reset to 0 to avoid using stale gaps.

## UI + strategy consumers
- **Live Session Snapshot:** Uses `RacePaceVsLeaderSummary` and `LiveLeaderPaceInfo` (set via `AvgDeltaToLdrValue` and `LiveLeaderAvgPaceSeconds`) to show "Race Pace vs Leader".
- **Fuel tab planner:** The "Your Pace vs Leader (s)" slider and label bind to `AvgDeltaToLdrValue` / `LeaderDeltaSeconds` to display the same delta.
- **Strategy math:** In `CalculateStrategy()`, leader lap time is reconstructed as `num2 = num3 - LeaderDeltaSeconds` where `num3` is the driver's estimated lap. This influences remaining-lap estimates and refuelling.

## Reset / edge cases
- **Session change / snapshot reset:** `ResetSnapshotDisplays()` clears `LiveLeaderPaceInfo`, `AvgDeltaToLdrValue`, and sets `LeaderDeltaSeconds` to 0.0 along with other live snapshot fields.
- **Feed loss:** In `UpdateLiveFuelCalcs`, if leader timing drops (`leaderLastLapSec <= 0` with stored samples), leader pace state is cleared so downstream calculations do not reuse stale values.
- **No live pace:** When `avgSeconds <= 0` or `leaderAvgPace <= 0` in `ApplyLiveLapPaceEstimate`, leader delta text is set to "-" and `LeaderDeltaSeconds` is reset.

## Known current symptom
- On this branch, SimHub replay shows no leader delta on the Fuel tab even though telemetry contains leader information. For the UI to display a value, `LiveLeaderAvgPaceSeconds` must be > 0 and `LeaderDeltaSeconds` / `AvgDeltaToLdrValue` must be populated in `ApplyLiveLapPaceEstimate`.
