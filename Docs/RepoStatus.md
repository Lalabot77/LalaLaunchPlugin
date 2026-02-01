# Repository status and how to sync with GitHub

## What exists in this checkout right now
- Only one local branch is present: `work`.
- There is no Git remote configured, so nothing in this checkout is currently linked to GitHub.
- The latest commit on `work` is the current HEAD with PRs #282–#312 (Fuel planner max-fuel percent/preset handling, live snapshot max-tank sync, predictor outputs, Opponents/CarSA documentation, extensive CarSA Phase 2.2 updates, and SA-Core v2 distance-based car-centric CarSA gaps with closing-rate sign/grace fixes plus CSV cross-check logging cleanup). Use `git log --oneline -n 22` to see the recent merges if you want to double-check.

## How to connect this checkout to your GitHub repo
1. Add your GitHub remote (replace the URL with your actual repository clone URL):
   ```bash
   git remote add origin https://github.com/<your-account>/LalaLaunchPlugin.git
   ```
2. Fetch all remote branches so you can see what already exists on GitHub:
   ```bash
   git fetch --all
   ```
3. List branches to confirm you can now see the remote ones:
   ```bash
   git branch -a
   ```

## How to push the current work to GitHub
- If you want the `work` branch to become your GitHub `main` (replacing or updating it):
  ```bash
  git push -u origin work:main
  ```
- If you prefer to keep `work` separate and open a pull request later, push it as its own branch:
  ```bash
  git push -u origin work
  ```

## If GitHub already has different commits on `main`
- First, fetch as above, then inspect remote history (example):
  ```bash
  git log --oneline origin/main | head
  ```
- To keep your current work safe, create a backup branch locally before attempting any merges or rebases:
  ```bash
  git branch backup/work-local
  ```
- To combine remote `main` with your local changes, check out `main` tracking the remote and merge/rebase `work` onto it:
  ```bash
  git checkout -b main origin/main
  git merge work
  ```
  Resolve any conflicts that appear, commit the resolution, then push:
  ```bash
  git push -u origin main
  ```

## Where to ask SimHub for car/track live data
- SimHub exposes the live car and track via data references such as `DataCorePlugin.GameData.CarModel` and `DataCorePlugin.GameData.TrackNameWithConfig`. The UI dropdowns use these live values, so once your GitHub branches are synced you can verify that both the pre-race and snapshot fields bind to the same live strings.

## Feature delivery status (docs canonical)
- **Pit Entry Assist:** **COMPLETE** — stable, shipped, and represented in driver/dash docs. Uses pit speed + distance sources with decel/buffer tuning; no pending work in this repo.
- **Pit entry assist (manual mode):** **COMPLETE** — pit entry assist can be manually armed via pit screen toggle when within 500 m; pit screen mode resets to auto on session/combo changes to avoid stale manual state.
- **Pit entry line debrief + time loss:** **COMPLETE** — ENTRY LINE logs and exports capture safe/normal/bad results plus time loss vs pit limiter once below-limit distance is known.
- **Track Markers:** **COMPLETE** — pit entry/exit markers auto-learned per track with lock/unlock semantics, track-length change detection, and MSGV1 notifications. Stored in `PluginsData/Common/LalaPlugin/TrackMarkers.json` (migrates from legacy filenames on load).
- **MSGV1 for pit markers:** **INTEGRATED** — pit marker capture/length-delta/lock-mismatch messages defined in `PluginsData/Common/LalaPlugin/Messages.json` via `MessageDefinitionStore`; MSGV1 core continues elsewhere but this repo ships pit marker hooks.
- **Legacy messaging:** **Not used** — only MSGV1 definition-driven messages fire; no legacy/adhoc messaging paths remain for pit markers.
- **Pit loss locking:** **COMPLETE** — per-track pit loss values can be locked to block auto-updates; blocked candidates are captured and surfaced in the Profiles UI for review before manual unlock.
- **Pit-exit prediction audit + settled logging:** **COMPLETE** — pit-exit predictor now locks pit-loss and gap inputs at pit entry to avoid drift, logs richer pit-in/out snapshots plus math audit, and emits a one-lap-delayed “pit-out settled” confirmation.
- **Opponents subsystem:** **COMPLETE** — race-only opponent pace/fight and pit-exit prediction exports with lap gate ≥1, summary strings, and log support.
- **CarSA SA-Core v2:** **INTEGRATED** — distance-based, car-centric gap/closing model with player-centric closing sign, grace window for telemetry blips, and trimmed CSV debug columns plus raw external gap cross-checks.
- **Dry/Wet condition lock UI:** **COMPLETE** — per-track dry/wet condition lock toggles persist immediately in profiles (no save prompt).
- **Session summary + trace v2:** **COMPLETE** — session summary CSV v2, lap trace rows with pit-stop index/phase, corrected pit-stop counting semantics, and explicit CSV column mapping for summary exports.
- **Profile storage & schema:** **COMPLETE** — car profiles now save in a schema-v2 wrapper with opt-in track stats serialization, normalized track keys, and legacy JSON migration from older filenames/locations.
- **Fuel + pace live persistence:** **COMPLETE** — dry/wet fuel burn windows and average lap times persist into track profiles once sample thresholds are met, with condition-specific “last updated” metadata.
- **Profiles UI controls:** **COMPLETE** — base tank litres field with “learn from live” action, plus “relearn” buttons for pit data and dry/wet condition resets.
- **Pit cycle guards:** **COMPLETE** — pit loss persistence now skips NaN/invalid candidates; PitLite entry arming is gated to avoid false triggers in pit stalls.
- **Fuel planner max-fuel handling:** **COMPLETE** — profile-mode max fuel override is clamped to per-car base tank; preset max fuel values are stored as % of base tank; Live Snapshot mode uses live session cap (MaxFuel × BoP, defaulting BoP to 1.0) and raises a clear error when live cap is unavailable. The Live Session panel clears max-fuel displays when the cap is missing.
- **Live Snapshot + presets:** **COMPLETE** — changing car/track clears the Live Snapshot UI to avoid stale data; switching back to Profile mode restores the previous profile max-fuel override and re-applies the selected preset.
- **Messaging signals:** **COMPLETE** — `MSG.OtherClassBehindGap` exported for multi-class approach messaging; no `MSGOtherClassBehindGap` alias remains.
- **Stint burn targets:** **COMPLETE** — live “current tank” burn guidance exported with banding (SAVE/HOLD/PUSH/OKAY) and a configurable pit-in reserve expressed as % of one lap’s stable burn.
- **Dash overlay visibility:** **COMPLETE** — overlay dash receives the same show/hide toggles as main/message dashes, including pit/launch/rejoin/race flags and traffic alerts.
- **Wet/dry stat gating:** **COMPLETE** — wet mode is detected via tire compound signals, wet stats are captured separately, and wet/dry confidence applies a cross-mode penalty when using opposite-condition data.
- **Wet surface telemetry exports:** **COMPLETE** — track wetness and label are exported alongside live wet/dry mode updates for dashboards and live snapshot UI.

## Known/accepted limitations (intentional)
- Replay session identity quirks can surface inconsistent session tokens in replays — accepted because replay identity data is unreliable (see `Reset_And_Session_Identity.md`).
- Track-length delta messages are informational only and do not block use — accepted to avoid false lockouts while still surfacing drift.

Items in this section should be mirrored (top 5–10 only) into Project_Index.md → Known Lies / Allowed Compromises.
