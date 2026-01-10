# Profiles and Personal Bests

Validated against commit: da0639e
Last updated: 2026-02-09
Branch: work

## Purpose
Profiles and PBs provide **persistent baselines** for:
- Fuel per lap
- Lap time
- Wet vs dry conditions
- Dry/Wet condition lock flags per track

They seed live models but never override confirmed live data.

---

## Inputs

- Car ID
- Track ID
- Session results
- Accepted lap samples
- Fuel windows

---

## Internal State

- Per-car/per-track profiles
- Dry and wet averages
- Sample counts
- PB lap times
- Dry/Wet lock flags on each track record

---

## Logic Blocks

### 1) Profile Loading
On session start:
- Matching profile is loaded
- Values seed planner and fuel model

---

### 2) Profile Updating
Profiles update only when:
- Sufficient accepted samples exist
- New values pass sanity bounds
- Condition lock toggles (Dry/Wet) persist immediately when toggled in the Profiles UI.

---

### 3) PB Capture
PB laps are captured when:
- Lap is valid
- Lap improves stored PB
- Session context allows PB capture

---

## Outputs

- Profile lap time
- Profile fuel per lap
- PB lap time
- Logs for capture/rejection
- Track condition lock flags (DryConditionsLocked/WetConditionsLocked)

---

## Reset Rules

Profiles persist across sessions.
Live seeds reset on session identity change.

---

## Failure Modes

- Bad samples → rejected by bounds
- Profile drift → mitigated by averaging
- PB overwrite errors → logged and rejected
- Lock toggles persist immediately; ensure the intended track is selected before toggling.

---

## Test Checklist

- Profile loads on session start
- PB captured on valid improvement
- Profiles do not update from rejected laps

---

## TODO / VERIFY

- TODO/VERIFY: Confirm wet/dry auto-detection trigger.
- TODO/VERIFY: Confirm minimum sample count for profile update.
