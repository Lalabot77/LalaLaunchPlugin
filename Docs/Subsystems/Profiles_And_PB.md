# Profiles and Personal Bests

Validated against commit: 298accf  
Last updated: 2026-02-10  
Branch: work

## Purpose
Profiles and PBs provide **persistent baselines** for:
- Fuel per lap (dry/wet)
- Lap time (dry/wet)
- Pit lane loss per track
- Condition lock flags and condition multipliers
- Optional per-car base tank capacity

They seed live models but never override confirmed live data when locks are enabled.

---

## Inputs
- Car ID + track identity
- Session results and valid lap samples
- Fuel windows and pace windows
- User edits and lock/relearn actions

---

## Storage & schema
- Profiles save to `PluginsData/Common/LalaPlugin/CarProfiles.json` using the schema-v2 wrapper `CarProfilesStore` (with `SchemaVersion = 2`).
- Legacy `LalaLaunch_CarProfiles.json` is migrated on load if the new file is missing.
- Track stats are serialized via opt-in fields only, so UI text helpers are not persisted.

---

## Internal State
- Per-car profile settings (launch, fuel, dash, pit entry defaults).
- Per-track stats (dry/wet fuel windows, avg lap times, PBs, pit loss, lock flags).
- Condition-specific “last updated” metadata for PB, avg lap time, and fuel burn.
- Track keys normalized to lowercase to avoid duplicates.

---

## Logic Blocks

### 1) Profile Loading
On session start:
- Matching profile is loaded and applied to live session state.
- Track keys are canonicalized (trimmed + lowercase) to ensure lookups are stable.
- Invalid base tank values (NaN/∞/≤0) are cleared on load.

---

### 2) Live persistence (fuel + pace)
When enough valid samples exist:
- **Fuel burn**: min/avg/max fuel-per-lap and sample counts persist per condition (dry/wet) unless the condition is locked.
- **Avg lap time**: `AvgLapTimeDry/Wet` persists when enough pace samples are present and the condition is not locked.
- Each update stamps condition-specific “last updated” metadata for the UI (separate dry vs wet timestamps/sources).

---

### 3) PB Capture
PB laps are captured when:
- Lap is valid.
- Lap improves the stored PB for the active condition.
- Session context allows PB capture.

PB metadata (source + timestamp) is stored separately for dry and wet laps.

---

### 4) Condition locks
- `DryConditionsLocked` / `WetConditionsLocked` prevent automatic telemetry persistence for that condition.
- Locks persist immediately on toggle.
- Pit loss locking uses a separate `PitLaneLossLocked` flag and stores a blocked candidate record when auto-updates are rejected.

---

### 5) Relearn actions
Profiles UI exposes buttons to reset and relearn track data:
- **Relearn Pit Data**: clears pit loss values/metadata and resets track markers for that track before saving.
- **Relearn Dry/Wet Conditions**: clears PB, avg lap time, and fuel stats for the chosen condition, unlocks the condition, and saves.

---

## Outputs
- Profile lap time (dry/wet)
- Profile fuel per lap (dry/wet min/avg/max)
- PB lap time (dry/wet)
- Track condition locks + pit-loss lock
- Per-condition “last updated” display strings used in the Profiles UI

---

## Reset Rules
- Profiles persist across sessions.
- Live seeds reset on session identity change (but profile baselines remain).

---

## Failure Modes
- Bad samples → rejected by bounds.
- Profile drift → mitigated by averaging.
- PB overwrite errors → logged and rejected.
- Locked condition prevents telemetry updates; ensure the correct track is selected before toggling.

---

## Test Checklist
- Profile loads on session start and auto-selects the active car/track.
- PB captured on valid improvement (dry and wet).
- Fuel and avg lap updates persist after sample thresholds are met.
- Relearn buttons clear the intended fields and reset locks/track markers.
