# Profiles & PB Management

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1  
Last updated: 2025-12-28  
Branch: docs/refresh-index-subsystems

## Purpose
Manage car/track profiles, personal bests (PB), and track identity resolution for planner defaults and live snapshot labeling.

## Inputs (source + cadence)
- Live car/track detection from `LalaLaunch` (`SetLiveSession`) every 500 ms poll; session token changes clear snapshots.【F:LalaLaunch.cs†L3200-L3233】【F:LalaLaunch.cs†L3308-L3365】
- User actions: save profile, apply profile to live, PB manual edits, profile selection in UI.【F:LalaLaunch.cs†L2585-L2633】【F:ProfilesManagerViewModel.cs†L50-L200】
- Telemetry PB updates from SimHub best lap (`DataUpdate` per tick).【F:LalaLaunch.cs†L3578-L3591】

## Internal state
- `ProfilesManagerViewModel` list of `CarProfile` objects with nested track stats (avg lap, fuel averages, pit lane loss, PB).【F:ProfilesManagerViewModel.cs†L28-L200】
- Snapshot caches `_lastSnapshotCar/_lastSnapshotTrack` to avoid redundant pushes to FuelCalcs.【F:LalaLaunch.cs†L3200-L3233】
- Auto-select trackers `_lastSeenCar/_lastSeenTrack` to avoid repeated auto profile selection within session.【F:LalaLaunch.cs†L3596-L3625】

## Calculation blocks (high level)
1. **Identity probe:** Read car model/track key/display; normalize "unknown"; push to FuelCalcs if changed.【F:LalaLaunch.cs†L3200-L3233】
2. **Auto profile application:** On new car/track combo, auto-select profile and refresh smoothing/strategy; logged once per combo.【F:LalaLaunch.cs†L3596-L3625】
3. **PB handling:** PB updates from telemetry forwarded to FuelCalcs and Profiles VM; acceptance logged; CarProfile PB property logs changes (manual edits included).【F:LalaLaunch.cs†L3578-L3591】【F:CarProfiles.cs†L230-L251】
4. **Profile save/apply:** UI commands save active profile and apply profile to live on init callback; profile persistence handled in VM.【F:LalaLaunch.cs†L2585-L2633】【F:ProfilesManagerViewModel.cs†L551-L570】
5. **Track resolution:** VM resolves track keys/display names and logs resolution; creates default profile if missing.【F:ProfilesManagerViewModel.cs†L160-L182】【F:ProfilesManagerViewModel.cs†L551-L570】

## Outputs (exports + logs)
- Exports: `Reset.*` session tokens, `CurrentTrackName/Key` (internal), live snapshot labels in Fuel tab; PB/lap/fuel averages feed planner defaults.
- Logs: profile save/apply, auto-select, PB updates, track resolution, default-profile creation (see `Docs/SimHubLogMessages.md`).【F:LalaLaunch.cs†L560-L580】【F:LalaLaunch.cs†L3596-L3625】【F:ProfilesManagerViewModel.cs†L66-L182】【F:CarProfiles.cs†L230-L251】

## Dependencies / ordering assumptions
- `ProfilesManagerViewModel` instantiated during plugin `Init`; `FuelCalcs` constructed afterward but receives profile handle.
- Active profile must exist for pit-lane loss persistence and fuel baselines; auto-created default used otherwise.

## Reset rules
- Session token change clears snapshot identity and resets profile-related smoothing; auto-select may re-run when new live combo detected.【F:LalaLaunch.cs†L3308-L3365】【F:LalaLaunch.cs†L3596-L3625】
- Fuel-model reset does not alter stored profiles but may update track stats when new stable dry fuel is available.

## Failure modes
- Unknown car/track prevents seed application and profile lookup; logged as errors/warnings.
- TODO/VERIFY: Confirm concurrency when profile save occurs during live auto-select (UI thread vs DataUpdate dispatcher).

## Test checklist
- Start session with known car/track; verify auto-select log and Fuel tab snapshot labels.
- Save profile changes and ensure save log fires.
- Update PB via telemetry and via manual edit; confirm logs and planner displays update.
