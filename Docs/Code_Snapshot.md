# Code Snapshot

- Branch: work
- Commit: (current HEAD) (Docs: document PitExit ahead/behind exports)
- Date: 2026-02-08

## Architectural notes (pit markers, MSGV1, and host wiring)
- `PitEngine` now owns pit entry/exit marker auto-learning, storage, and track-length change detection (threshold 50 m). It emits trigger events for first capture, length delta, line refresh, and locked mismatches, and persists markers immediately into `PluginsData/Common/LalaLaunch/LalaLaunch.TrackMarkers.json`. Lock/unlock semantics are enforced at capture time to keep stored markers authoritative unless refreshed or manually unlocked.【F:PitEngine.cs†L525-L769】【F:PitEngine.cs†L669-L714】【F:PitEngine.cs†L968-L1034】
- `LalaLaunch` manages marker pulses, attaches marker exports (`TrackMarkers.*` stored/session/trigger fields), and exposes actions to lock/unlock markers plus a reload hook for manual JSON edits. Track marker trigger pulses are routed to MSGV1 via pulsed objects held for ~3 s to allow evaluators to consume them once.【F:LalaLaunch.cs†L134-L182】【F:LalaLaunch.cs†L3047-L3179】
- `SignalProvider` extends MSGV1 signals with `TrackMarkers.Pulse.*` entries that return the most recent pulse payloads for evaluators; these are polled per evaluation tick and cleared on consumption.【F:Messaging/SignalProvider.cs†L86-L102】
- `MessageDefinitionStore` registers new definition-driven MSGV1 messages for pit marker capture, track-length delta, and locked mismatch, each paired with dedicated evaluators that latch per-track tokens to avoid repeats. Messages live in `Messages.json` (definition store) and replace any legacy/adhoc messaging for this area.【F:Messaging/MessageDefinitionStore.cs†L393-L452】【F:Messaging/MessageEvaluators.cs†L401-L476】
- `LalaLaunch` now publishes dash-facing pit exit distance and time outputs derived from stored pit exit track percentage, cached track length, live car track position, and speed; values wrap forward around S/F, clamp to integers, and fall back to zero when data is missing or speed is negligible. Updates are throttled to the 250 ms poll cadence and only run while the car is in pit lane, keeping the exports at zero on track.【F:LalaLaunch.cs†L3743-L3759】【F:LalaLaunch.cs†L4077-L4104】

## Included .cs Files
- CarProfiles.cs — last modified 2025-12-27T12:32:10+00:00
- CopyProfileDialog.xaml.cs — last modified 2025-09-14T19:32:49+01:00
- DashesTabView.xaml.cs — last modified 2025-09-14T19:32:49+01:00
- EnumEqualsConverter.cs — last modified 2025-11-04T19:13:41-06:00
- FuelCalcs.cs — last modified 2025-12-27T12:37:19+00:00
- FuelCalculatorView.xaml.cs — last modified 2025-11-27T07:30:04-06:00
- FuelProjectionMath.cs — last modified 2025-12-27T12:32:10+00:00
- InvertBooleanConverter.cs — last modified 2025-09-14T19:32:49+01:00
- LalaLaunch.cs — last modified 2026-02-08T00:00:00+00:00
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
- MessagingSystem.cs — last modified 2025-11-30T10:47:37+00:00
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
