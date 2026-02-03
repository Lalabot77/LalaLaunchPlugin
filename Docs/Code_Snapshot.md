# SNAPSHOT / GENERATED / NON-CANONICAL

# Code Snapshot

- Source commit/PR: f4cd1fe (current HEAD; PRs 310–320 CarSA status + CSV/identity updates)
- Generated date: 2026-02-03 (updated)
- Regeneration: manual snapshot; no regen pipeline defined
- Branch: work

If this conflicts with Project_Index.md or contract docs, treat this as stale.

## Snapshot metadata (legacy)
- Commit: f4cd1fe (current HEAD)
- Date: 2026-02-03 (updated)

## Architectural notes (profiles, storage, fuel persistence)
- **Standardized JSON storage** now resolves all plugin JSON data into `PluginsData/Common/LalaPlugin/` via `PluginStorage`, with built-in legacy migration helpers. Car profiles, race presets, message definitions, global settings, and track markers all use this storage helper for consistent locations and one-time migrations.【F:PluginStorage.cs†L1-L76】【F:ProfilesManagerViewModel.cs†L545-L936】【F:RacePresetStore.cs†L11-L140】【F:Messaging/MessageDefinitionStore.cs†L10-L120】【F:LalaLaunch.cs†L3469-L3510】
- **Track markers** now load/save in `.../LalaPlugin/TrackMarkers.json` and migrate from legacy `LalaPlugin.TrackMarkers.json` if needed; the in-memory store only updates from disk once per session load (reload action forces refresh).【F:PitEngine.cs†L948-L1099】【F:ProfilesManagerViewModel.cs†L1008-L1089】
- **Car profiles schema v2** wraps profiles in a `CarProfilesStore` (schema version 2) and opt-in serializes `TrackStats` fields only, while track keys are normalized to lowercase to prevent duplication. Legacy wrapper-less JSON still loads via a fallback path during load.【F:CarProfiles.cs†L152-L239】【F:ProfilesManagerViewModel.cs†L786-L936】
- **Fuel + pace persistence** now writes dry/wet fuel windows and avg lap times into the active track record once sample thresholds are met and condition locks are not set; each condition records its own “last updated” metadata for UI labels and telemetry auditing.【F:LalaLaunch.cs†L1847-L2008】【F:CarProfiles.cs†L622-L889】
- **Base tank capacity** is stored per car profile as an optional `BaseTankLitres` field and can be learned from live `MaxFuel` data through the Profiles UI command (sanitized on load if invalid).【F:CarProfiles.cs†L72-L141】【F:ProfilesManagerViewModel.cs†L658-L912】
- **Live max-fuel tracking** now defaults BoP percent to 1.0 when invalid/missing, carries forward the last valid live max fuel for tank-space calculations, and clears live max-fuel displays when the cap is unavailable to avoid stale UI values.【F:LalaLaunch.cs†L5321-L5368】【F:FuelCalcs.cs†L4159-L4259】
- **Fuel planner max-fuel override** is clamped to the profile base tank in Profile mode; switching into Live Snapshot captures the prior override and uses the live cap (or 0 if missing), while switching back restores the profile value and re-applies any selected preset.【F:FuelCalcs.cs†L300-L405】【F:FuelCalcs.cs†L1821-L1884】
- **Live Snapshot resets** clear live fuel/pace summaries when the car/track changes, ensuring the Live Session panel never shows profile fallback values during a new live session startup.【F:FuelCalcs.cs†L3270-L3703】
- **Messaging signals** include `MSG.OtherClassBehindGap` (seconds behind a faster-class car) alongside `MSG.OvertakeApproachLine`, for use in message catalog evaluators.【F:MessagingSystem.cs†L13-L214】【F:LalaLaunch.cs†L3123-L3129】
- **CarSA SA-Core v2** uses car-centric LapDistPct deltas for distance-based gaps/closing with a 0.5s grace window, car-centric status memory for out-lap/compromised/penalty detection, and replay-safe identity refreshes for slot names/classes.【F:CarSAEngine.cs†L70-L590】【F:LalaLaunch.cs†L5485-L5692】
- **CarSA StatusE + class rank** now distinguishes penalty vs off-track compromises, uses pit-surface classification, and prefers class-rank (CarClassRelSpeed/CarClassEstLapTime) for Faster/Slower class labels with a safe fallback when rank data is missing.【F:CarSAEngine.cs†L930-L1372】【F:LalaLaunch.cs†L4943-L5039】
- **CarSA CSV debug export** adds StatusE reason + class-rank metadata and normalizes cross-check gap columns, with alignment fixes to keep headers and rows consistent.【F:LalaLaunch.cs†L4814-L5440】

## Included .cs Files
- CarProfiles.cs — last modified 2026-02-08T00:00:00+00:00
- CarSAEngine.cs — last modified 2026-02-03 (updated)
- CarSASlot.cs — last modified 2026-02-03 (updated)
- CopyProfileDialog.xaml.cs — last modified 2025-09-14T19:32:49+01:00
- DashesTabView.xaml.cs — last modified 2025-09-14T19:32:49+01:00
- EnumEqualsConverter.cs — last modified 2025-11-04T19:13:41-06:00
- FuelCalcs.cs — last modified 2026-01-13
- FuelCalculatorView.xaml.cs — last modified 2025-11-27T07:30:04-06:00
- FuelProjectionMath.cs — last modified 2025-12-27T12:32:10+00:00
- InvertBooleanConverter.cs — last modified 2025-09-14T19:32:49+01:00
- LalaLaunch.cs — last modified 2026-02-03
- LapTimeValidationRule.cs — last modified 2025-09-14T19:32:49+01:00
- LaunchAnalysisControl.xaml.cs — last modified 2025-11-27T11:49:07-06:00
- LaunchPluginCombinedSettingsControl.xaml.cs — last modified 2025-11-04T19:13:41-06:00
- LaunchPluginSettingsUI.xaml.cs — last modified 2025-09-14T19:32:49+01:00
- LaunchSummaryExpander.xaml.cs — last modified 2025-11-19T09:20:14-06:00
- Messaging/MessageDefinition.cs — last modified 2025-12-27T12:32:10+00:00
- Messaging/MessageDefinitionStore.cs — last modified 2025-12-27T12:32:10+00:00
- Messaging/MessageEngine.cs — last modified 2025-12-27T12:32:10+00:00
- Messaging/MessageEvaluators.cs — last modified 2025-12-27T12:32:10+00:00
- Messaging/MessageInstance.cs — last modified 2025-12-27T12:32:10+00:00
- Messaging/SignalProvider.cs — last modified 2025-12-27T12:32:10+00:00
- MessagingSystem.cs — last modified 2026-01-13
- NotBoolConverter.cs — last modified 2025-11-04T19:13:41-06:00
- ParsedSummary.cs — last modified 2025-09-14T19:32:49+01:00
- PitCycleLite.cs — last modified 2025-12-27T12:32:10+00:00
- PitEngine.cs — last modified 2025-12-27T12:32:10+00:00
- PresetsManagerView.xaml.cs — last modified 2025-12-27T12:32:10+00:00
- ProfilesManagerView.xaml.cs — last modified 2025-09-14T19:32:49+01:00
- ProfilesManagerViewModel.cs — last modified 2025-12-27T12:32:10+00:00
- Properties/AssemblyInfo.cs — last modified 2025-09-14T19:32:49+01:00
- RacePreset.cs — last modified 2025-11-04T19:13:41-06:00
- RacePresetStore.cs — last modified 2025-11-04T19:13:41-06:00
- RejoinAssistEngine.cs — last modified 2025-12-27T12:32:10+00:00
- TelemetryLogger.cs — last modified 2025-09-14T19:32:49+01:00
