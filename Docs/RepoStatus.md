# Repository status

Validated against commit: ea90c75
Last updated: 2026-03-10
Branch: work

## Current repo/link status
- Local branch present: `work`.
- No Git remote is configured in this checkout (`git remote -v` returns empty).

## Documentation sync status (requested set)
- `Docs/Subsystems/Fuel_Model.md` updated with `FuelDeltaToEnd` (raw) vs `FuelDeltaPlanned` (strategy-aware) semantics.
- `Docs/SimHubParameterInventory.md` updated with `LalaLaunch.Strategy.FuelDeltaPlanned` in the strategy export contract.
- `Docs/Plugin_UI_Tooltips.md` updated for Preset editor Pit Strategy selector tooltip wording.
- `Docs/Project_Index.md` reviewed; no wording changes required for this task.
- `Docs/SimHubLogMessages.md` reviewed; no wording changes required for this task (no new logs).

## Delivery status highlights
- Added runtime export `LalaLaunch.Strategy.FuelDeltaPlanned`.
- `LalaLaunch.Strategy.FuelDeltaToEnd` remains unchanged (`CurrentFuel - TotalFuelNeeded`).
- `FuelDeltaPlanned` behavior is conservative by strategy mode:
  - `Single Stop`: `(CurrentFuel + PlannedSingleStopRefuel) - TotalFuelNeeded` using normalized/clamped `Pit_WillAdd` first.
  - `No Stop`, `Multi Stop`, `Auto`: current-fuel-based delta (`CurrentFuel - TotalFuelNeeded`).
- Preset editor now uses a direct 4-mode `Pit Strategy` selector (Auto/No Stop/Single Stop/Multi Stop) instead of legacy mandatory-stop checkbox semantics.
- Legacy preset/profile JSON compatibility remains preserved through `MandatoryStopRequired` mapping in persisted models.

## Notes
- `Docs/Code_Snapshot.md` remains non-canonical orientation-only documentation.
