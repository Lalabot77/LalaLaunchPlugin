# Trace Logging

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1
Last updated: 2025-12-28
Branch: docs/refresh-index-subsystems

## Purpose
Trace Logging captures **post-race forensic data** for:
- Fuel usage
- Pace evolution
- Pit loss
- Strategy validation

It is intentionally low-frequency and human-readable.

---

## Inputs

- Lap crossings
- Fuel deltas
- Pace snapshots
- Pit cycle completion
- Session identity

---

## Internal State

- Active trace file handle
- Session metadata
- Trace validity flags

---

## Logic Blocks

### 1) File Lifecycle
- Open on race start
- Append per-lap summaries
- Close on session end
- Discard invalid traces

---

### 2) Data Selection
One row per lap crossing:
- Lap number
- Fuel used
- Pace reference
- Projection snapshot
- Pit state

---

## Outputs

- CSV trace files
- INFO logs for open/close/discard

---

## Reset Rules

- New session identity → new trace
- Invalid trace → discard

---

## Failure Modes

- Replay sessions → may discard trace
- Missing lap events → partial trace
- File IO errors → logged

---

## Test Checklist

- Trace opens on race
- One row per lap
- Trace closes cleanly

---

## TODO / VERIFY

- TODO/VERIFY: Confirm exact discard conditions for replay sessions.
