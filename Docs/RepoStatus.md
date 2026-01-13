# Repository status and how to sync with GitHub

## What exists in this checkout right now
- Only one local branch is present: `work`.
- There is no Git remote configured, so nothing in this checkout is currently linked to GitHub.
- The latest commit on `work` is the current HEAD with PRs #241–#261 (session summary v2 mapping, race summary refinements, profile schema upgrades, JSON storage standardization, and pit/fuel persistence fixes). Use `git log --oneline -n 22` to see the recent merges if you want to double-check.

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
- **Track Markers:** **COMPLETE** — pit entry/exit markers auto-learned per track with lock/unlock semantics, track-length change detection, and MSGV1 notifications. Stored in `PluginsData/Common/LalaPlugin/TrackMarkers.json` (migrates from legacy filenames on load).
- **MSGV1 for pit markers:** **INTEGRATED** — pit marker capture/length-delta/lock-mismatch messages defined in `PluginsData/Common/LalaPlugin/Messages.json` via `MessageDefinitionStore`; MSGV1 core continues elsewhere but this repo ships pit marker hooks.
- **Legacy messaging:** **Not used** — only MSGV1 definition-driven messages fire; no legacy/adhoc messaging paths remain for pit markers.
- **Pit loss locking:** **COMPLETE** — per-track pit loss values can be locked to block auto-updates; blocked candidates are captured and surfaced in the Profiles UI for review before manual unlock.
- **Pit-exit prediction audit + settled logging:** **COMPLETE** — pit-exit predictor now locks pit-loss and gap inputs at pit entry to avoid drift, logs richer pit-in/out snapshots plus math audit, and emits a one-lap-delayed “pit-out settled” confirmation.
- **Opponents subsystem:** **COMPLETE** — race-only opponent pace/fight and pit-exit prediction exports with lap gate ≥1, summary strings, and log support.
- **Dry/Wet condition lock UI:** **COMPLETE** — per-track dry/wet condition lock toggles persist immediately in profiles (no save prompt).
- **Session summary + trace v2:** **COMPLETE** — session summary CSV v2, lap trace rows with pit-stop index/phase, corrected pit-stop counting semantics, and explicit CSV column mapping for summary exports.
- **Profile storage & schema:** **COMPLETE** — car profiles now save in a schema-v2 wrapper with opt-in track stats serialization, normalized track keys, and legacy JSON migration from older filenames/locations.
- **Fuel + pace live persistence:** **COMPLETE** — dry/wet fuel burn windows and average lap times persist into track profiles once sample thresholds are met, with condition-specific “last updated” metadata.
- **Profiles UI controls:** **COMPLETE** — base tank litres field with “learn from live” action, plus “relearn” buttons for pit data and dry/wet condition resets.
- **Pit cycle guards:** **COMPLETE** — pit loss persistence now skips NaN/invalid candidates; PitLite entry arming is gated to avoid false triggers in pit stalls.
- **Known/accepted limitations:** Replay session identity quirks remain (see `Reset_And_Session_Identity.md`) and track-length deltas are informational only; both are understood/accepted for current shipping state.
