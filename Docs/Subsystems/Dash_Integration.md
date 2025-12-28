# Dash Integration

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1
Last updated: 2025-12-28
Branch: docs/refresh-index-subsystems

## Purpose
Dash Integration defines the **contract between LalaLaunch and dashboards**:
- What properties exist
- When they are valid
- How they should be interpreted

Dash logic must be defensive and null-safe.

---

## Inputs (from plugin)
All exported SimHub properties defined in:
- SimHubParameterInventory.md

---

## Contracts

- Properties may be null or zero early in session
- `_Stable` variants are preferred for display
- `_S` string variants are preferred for UI text
- Readiness flags must gate behaviour

---

## Recommended Dash Patterns

- Never assume non-null numeric values
- Use `IsFuelReady` and confidence flags
- Avoid arithmetic on raw properties without null guards
- Use visibility gating to avoid SimHub suppression

---

## Common Errors

- Null-to-value conversions
- Comparing uninitialised values
- Ignoring session context

---

## Reset Behaviour

Dash state should reset or hide on:
- Session identity change
- Session end
- Replay transitions

---

## Test Checklist

- Dash loads without errors at session start
- No expression suppression in logs
- Values transition smoothly

---

## TODO / VERIFY

- TODO/VERIFY: Confirm which dash-facing flags are guaranteed boolean vs nullable.
