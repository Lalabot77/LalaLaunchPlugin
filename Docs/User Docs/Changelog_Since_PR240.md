# LalaLaunch Plugin Changelog (since PR240)

- Auto-learned **pit entry/exit markers** per track with lock/reload controls, plus **manual pit entry assist arming** and on-track cue/debrief outputs to help nail the entry line.
- **Pit timing & loss accuracy** improvements (direct vs DTL calculations), including pit-entry time-loss capture and more reliable pit lane loss reporting in strategy screens.
- **Session summary & trace logging**: CSV/trace outputs for post-session review and trend analysis.
- **Opponents race tools**: nearby car summaries, pace deltas, “laps to fight,” and **pit-exit position prediction** for class battles.
- **Dry vs wet tracking**: separate dry/wet fuel + pace stats, condition locks, wet mode detection from tyre compound, and track wetness labels for dashboards.
- **Fuel planner/live snapshot upgrades**: live snapshot resets on car/track changes, clearer readiness gates, safer max-tank handling, and preset max fuel defined as a **percentage of base tank** with visible preset values even in Live Snapshot mode.
- **Fuel strategy UI polish**: standardized tooltips, clearer toggle labels, overlay visibility column, and compact “last updated” labels.
- **Fuel math updates**: stint fuel margin expressed as a percentage and refined stops-required calculations for cleaner strategy guidance.
- **Profile storage & schema updates**: standardized JSON storage, normalized track keys, and base-tank litres stored per car profile.
- **New predictor exports** for fuel burn and pace so dashboards can show consistent “next lap” guidance even before long stints are built up.
