# Profiles and Personal Bests

Validated against commit: 52bd57d7c618f4df094c68c4ea6f1e11cc5e328f  
Last updated: 2026-02-06  
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
