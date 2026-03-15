# Repository status

Validated against commit: HEAD
Last updated: 2026-03-14
Branch: work

## Current repo/link status
- Local branch present: `work`.
- No Git remote is configured in this checkout (`git remote -v` returns empty).

## Documentation sync status (requested set)
- `Docs/SimHubParameterInventory.md` updated for the new `LalaLaunch.PreRace.*` public contract (`Selected`, `SelectedText`, `Stints`, `TotalFuelNeeded`, `FuelDelta`, `FuelSource`, `LapTimeSource`).
- `Docs/Plugin_UI_Tooltips.md` updated to describe pit mode selection as a PreRace on-grid info layer control.
- `Docs/Subsystems/Fuel_Model.md` updated to replace Strategy export guidance with the PreRace adapter/export contract and source-label behavior.
- `Docs/Subsystems/Fuel_Planner_Tab.md` updated to clarify planner authority vs PreRace display intent.

## Delivery status highlights
- Refactored race-start dash-facing outputs from `LalaLaunch.Strategy.*` to `LalaLaunch.PreRace.*` in `LalaLaunch.cs`.
- Implemented unified PreRace outputs with one stints value, one total-fuel value, and one delta value; fixed Auto to mirror planner outputs.
- Preserved selected mode persistence/label behavior (0 No Stop, 1 Single Stop, 2 Multi Stop, 3 Auto).
- PreRace race distance basis remains `DataCorePlugin.GameRawData.CurrentSessionInfo._SessionTime` (+ after-zero allowance).
- Implemented explicit source ordering for pre-race manual-mode inputs:
  - Fuel burn: planner/profile value -> SimHub computed burn -> hard fallback (3.0 L/lap).
  - Lap time: planner/profile value -> SimHub/iRacing predicted value chain -> hard fallback (120.0 s).
- `Single Stop` PreRace delta uses current fuel + pit-menu refuel intent (`PitSvFuel`) for live on-grid response; Auto PreRace delta now mirrors planner first-stint assumption.
- Planner and continuous live `Fuel.*` model behavior remain unchanged beyond read-only reuse of existing inputs.

## Notes
- `Docs/Code_Snapshot.md` remains non-canonical orientation-only documentation.
