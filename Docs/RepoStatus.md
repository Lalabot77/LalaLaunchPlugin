# Repository status

Validated against commit: dd294c0
Last updated: 2026-03-10
Branch: work

## Current repo/link status
- Local branch present: `work`.
- No Git remote is configured in this checkout (`git remote -v` returns empty).

## Documentation sync status (requested set)
- `Docs/Subsystems/Fuel_Model.md` updated to clarify Strategy.* race-time basis (`CurrentSessionInfo._SessionTime`) and current-fuel delta/stop semantics.
- `Docs/SimHubParameterInventory.md` updated to clarify `LalaLaunch.Strategy.TotalFuelNeeded`, `FuelDeltaToEnd`, and `FuelDeltaPlanned` contracts and race-time basis.
- `Docs/Project_Index.md` reviewed; no wording changes required for this task.
- `Docs/Plugin_UI_Tooltips.md` reviewed; no wording changes required for this task.
- `Docs/SimHubLogMessages.md` reviewed; no wording changes required for this task (no new logs).

## Delivery status highlights
- Fixed Strategy.* projection basis to use race session definition time (`DataCorePlugin.GameRawData.CurrentSessionInfo._SessionTime`) for strategy forecast laps/fuel instead of telemetry elapsed/remaining timing.
- `LalaLaunch.Strategy.TotalFuelNeeded`, `FuelDeltaToEnd`, `FuelDeltaPlanned`, and `CalculatedStops` now derive from the corrected strategy projection basis while keeping current fuel as the live stop/delta basis.
- `LalaLaunch.Strategy.FuelDeltaPlanned` retained conservative mode behavior:
  - `Single Stop`: uses current fuel + planned/clamped refuel (`Pit_WillAdd`, fallback request value).
  - `Auto`, `No Stop`, `Multi Stop`: use current-fuel delta to end.
- Selected-vs-calculated strategy separation and existing Fuel.* stop-window behavior remain unchanged.

## Notes
- `Docs/Code_Snapshot.md` remains non-canonical orientation-only documentation.
