# Profiles and Personal Bests

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1
Last updated: 2025-12-28
Branch: docs/refresh-index-subsystems

## Purpose
Profiles and PBs provide **persistent baselines** for:
- Fuel per lap
- Lap time
- Wet vs dry conditions

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

---

## Reset Rules

Profiles persist across sessions.
Live seeds reset on session identity change.

---

## Failure Modes

- Bad samples → rejected by bounds
- Profile drift → mitigated by averaging
- PB overwrite errors → logged and rejected

---

## Test Checklist

- Profile loads on session start
- PB captured on valid improvement
- Profiles do not update from rejected laps

---

## TODO / VERIFY

- TODO/VERIFY: Confirm wet/dry auto-detection trigger.
- TODO/VERIFY: Confirm minimum sample count for profile update.
