# Profiles and Personal Bests

Validated against commit: da0639e  
Last updated: 2026-02-09  
Branch: work

## Purpose
Profiles provide **per-car (and per-track) persisted baselines** for:
- Fuel and pace seeds,
- Launch parameters,
- **Pit Entry Assist parameters** (`Pit Entry Decel`, `Pit Entry Buffer`).

Personal Bests (PBs) store best-observed lap times for reference and seeding.

## Profile contents (Pit Entry Assist focused)
- **Pit Entry Decel (m/s²):** Target deceleration used by the constant-decel model; clamped **5–25 m/s²** at runtime.  
- **Pit Entry Buffer (m):** Cushion that shapes cue thresholds; clamped **0–50 m** at runtime.  
- **Defaults:** New profiles seed sensible defaults (≈14 m/s² decel, ≈15 m buffer) that can be tuned per car/track.  
- **Storage:** Values are saved with the active car profile and copied when profiles are duplicated so braking cues remain consistent across tyres/ABS/regen settings.【F:CarProfiles.cs†L128-L150】【F:ProfilesManagerViewModel.cs†L503-L584】  
- **UI location:** Dash tab sliders labeled “Pit Entry Decel” and “Pit Entry Buffer”; changes update the active profile immediately.【F:DashesTabView.xaml†L141-L142】【F:LalaLaunch.cs†L3380-L3387】

## Profile contents (Pit loss controls)
- **Pit Lane Loss (s):** Track-level pit loss value used for fuel planning and pit-exit prediction. Editable directly in the Profiles UI track table.【F:ProfilesManagerView.xaml†L286-L333】
- **Lock:** When enabled, the stored pit loss is protected from automatic updates. New pit loss candidates are captured as “blocked candidate” info for review, and auto-save resumes once unlocked.【F:ProfilesManagerViewModel.cs†L405-L421】【F:LalaLaunch.cs†L3199-L3212】
- **Blocked candidate display:** Shows the last blocked candidate time, timestamp, and source while the lock is active, making it easy to compare before unlocking.【F:CarProfiles.cs†L349-L359】【F:ProfilesManagerView.xaml†L315-L327】

## Profile contents (Dry/Wet condition locks)
- **Dry/Wet lock toggles:** Each track has a Dry and Wet “Locked” checkbox in the Profiles UI that persists immediately (no separate save prompt). These flags are stored on the track record for the dry/wet condition blocks.【F:ProfilesManagerView.xaml†L360-L512】【F:CarProfiles.cs†L320-L347】
- **Immediate persistence:** The lock toggles save through the track-level `RequestSaveProfiles` hook, which is wired to `SaveProfiles` when a track is selected.【F:CarProfiles.cs†L327-L347】【F:ProfilesManagerViewModel.cs†L330-L356】

## PB capture overview
- PBs update when a **valid lap** beats the stored PB for the current car/track.
- Session context and sanity bounds prevent regressions or invalid overwrites.

## Tuning guidance for Pit Entry Assist
- Start with the seeded defaults; review `ACTIVATE`/`LINE` logs:
  - **Too early (firstOK > ~80 m):** Decrease decel or increase buffer.
  - **Late (negative margin at LINE):** Increase decel before shrinking buffer.
- Re-tune when track grip or tyre compound changes; store as profile values to keep consistency.

## Design notes
- Profiles are **per car**, not per class, to account for braking hardware and regen differences.
- Pit Entry Assist parameters are published to SimHub every tick while armed (`Pit.EntryDecelProfile_mps2`, `Pit.EntryBuffer_m`) so dashes can show the live settings for traceability.
- PB data is separate from Pit Entry Assist; braking cues are unaffected by PB resets.
