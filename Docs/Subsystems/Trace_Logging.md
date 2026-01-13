# Trace Logging

Validated against commit: 298accf
Last updated: 2026-02-10
Branch: work

## Purpose
Trace Logging captures **post-race forensic data** for:
- Fuel usage
- Pace evolution
- Pit loss
- Strategy validation
- Session summary and per-lap trace exports

It is intentionally low-frequency and human-readable.

---

## Inputs

- Lap crossings
- Fuel deltas
- Pace snapshots
- Pit cycle completion
- Session identity

---

## Internal State

- Active trace file handle
- Session summary model (v2 schema)
- Session metadata
- Trace validity flags

---

## Logic Blocks

### 1) File Lifecycle
- Open on race start and create a per-session trace filename once car/track identity is available.
- Append one row per completed lap for the trace output.
- Append a summary row only once green + checkered are seen.
- Optionally embed the summary line into the trace file (wrapped by `#[SessionSummary]` markers) for full context.
- Discard invalid traces (e.g., missing identity, replay gating).

---

### 2) Data Selection
One row per lap crossing:
- Lap number + lap time
- Fuel remaining + stable burn + confidence + laps-remaining estimate
- Pit stop index + pit stop phase
- After-zero usage seconds

---

## Outputs

- Summary file: `SessionSummary.csv` under `Logs/LalaPluginData/LalaSessionSummaries` (or configured path). Rows are only written once green + checkered are seen and include planner, profile, and actual race stats in schema version `v2`.【F:SessionSummaryLogger.cs†L31-L176】【F:SessionSummaryModel.cs†L9-L137】
- Per-lap trace files: `SessionTrace_<car>_<track>_<timestamp>.csv` under `.../LalaSessionSummaries/Traces`. Trace rows include `PitStopIndex` and `PitStopPhase` alongside lap/fuel snapshots.【F:SessionSummaryLogger.cs†L41-L131】【F:SessionTraceLapRow.cs†L7-L33】
- Summary embedded in trace: the summary row can be appended inside the trace file between `#[SessionSummary]` markers for later parsing.【F:SessionSummaryLogger.cs†L133-L178】

---

## Session summary CSV v2 column map
Columns are emitted in the exact order below (see `SessionSummaryLogger.BuildSummaryHeaderLine`).

1. `SchemaVersion`
2. `RecordedAtUtc`
3. `SessionType`
4. `PresetName`
5. `CarIdentifier`
6. `TrackKey`
7. `Planner_TrackCondition`
8. `Planner_TotalFuelNeeded_L`
9. `Planner_EstDriveTimeAfterTimerZero_Sec`
10. `Planner_EstTimePerStop_Sec`
11. `Planner_RequiredPitStops`
12. `Planner_LapsLappedExpected`
13. `Profile_AvgLapTimeSec`
14. `Profile_FuelAvgPerLap_L`
15. `Profile_BestLapTimeSec`
16. `Actual_LapsCompleted`
17. `Actual_PitStops`
18. `Actual_AfterZeroSeconds`
19. `Actual_TotalTimeSec`
20. `Actual_FuelStart_L`
21. `Actual_FuelAdded_L`
22. `Actual_FuelFinish_L`
23. `Actual_FuelUsed_L`
24. `Actual_AvgFuelPerLap_L_AllLaps`
25. `Actual_AvgLapTimeSec_AllLaps`
26. `Actual_AvgLapTimeSec_ValidLaps`
27. `Actual_AvgFuelPerLap_L_ValidLaps`
28. `Actual_LapsLapped`
29. `Actual_ValidPaceLapCount`
30. `Actual_ValidFuelLapCount`

The values above map directly to `SessionSummaryModel` fields, and the `SchemaVersion` value is hard-coded to `v2`.

---

## Reset Rules

- New session identity → new trace + summary context reset.
- Invalid trace → discard.

---

## Failure Modes

- Replay sessions → summary and trace rows are suppressed when flagged as replay.
- Missing lap events → partial trace.
- File IO errors → logged.

---

## Test Checklist

- Trace opens on race.
- One row per lap (with pit stop index/phase fields populated as expected).
- Summary row appended once green + checkered are seen.

---

## Pit stop index semantics
- `PitStopIndex` is **1-based** and increments once per completed pit cycle (first stop => 1). The maximum index seen is used as the session’s `Actual_PitStops` value in the summary output.【F:LalaLaunch.cs†L3927-L3942】【F:SessionSummaryRuntime.cs†L152-L245】

## TODO / VERIFY

- TODO/VERIFY: Confirm exact discard conditions for replay sessions.
