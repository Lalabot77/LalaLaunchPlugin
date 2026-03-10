# Repository status

Validated against commit: f1d051b
Last updated: 2026-03-10
Branch: work

## Current repo/link status
- Local branch present: `work`.
- No Git remote is configured in this checkout (`git remote -v` returns empty).

## Documentation sync status (requested set)
- `Docs/Subsystems/Fuel_Model.md` updated for strategy-selected vs calculated stop separation and new strategy exports.
- `Docs/Subsystems/Fuel_Planner_Tab.md` updated for Pit Strategy planner behavior (selected override vs raw calculation).
- `Docs/SimHubParameterInventory.md` updated with `LalaLaunch.Strategy.*` export contract entries.
- `Docs/Plugin_UI_Tooltips.md` updated for Fuel tab Pit Strategy selector tooltip text.
- `Docs/Project_Index.md` reviewed; no wording changes required for this task.
- `Docs/SimHubLogMessages.md` reviewed; no wording changes required for this task (no new logs).

## Delivery status highlights
- Fuel tab mandatory-stop checkbox was replaced by a `Pit Strategy` selector with modes `Auto`, `No Stop`, `Single Stop`, and `Multi Stop`.
- Planner/preset/profile persistence now stores pit strategy as numeric mode (`0/1/2/3`) and defaults safely to `Auto` when missing.
- Legacy boolean `MandatoryStopRequired` profile/preset data remains load-compatible and maps to strategy (`true -> Single Stop`, `false -> Auto`).
- Planner stop output now applies selected strategy override while keeping calculated-stop math independent.
- Runtime now exports start-of-race strategy fields under `LalaLaunch.Strategy.*` for selected mode, planned stops, calculated stops, total fuel needed, and fuel delta to end.

## Notes
- `Docs/Code_Snapshot.md` remains non-canonical orientation-only documentation.
