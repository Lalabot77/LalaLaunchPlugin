# Dash Integration

Validated against commit: 52bd57d7c618f4df094c68c4ea6f1e11cc5e328f  
Last updated: 2026-02-06  
Branch: work

## Purpose
Define how SimHub exports from LalaLaunch should be consumed by dashboards with emphasis on the **Pit Entry Assist** surface. This document complements the subsystem specs and focuses on binding, gating, and rendering guidance.

## Core principles
- **Defensive consumption:** All properties may be `null`/zero on session start; gate on readiness flags when present.
- **Prefer stable variants:** Use `_Stable` when available for UI text; use numeric forms for gauges.
- **Visibility gating:** Use explicit visibility flags (e.g., `Pit.EntryAssistActive`) to avoid SimHub suppression.
- **No renormalisation:** Preserve plugin-provided units; avoid cue-driven remapping that hides continuous metrics.

## Pit Entry Assist binding
- **Properties:** `Pit.EntryAssistActive`, `Pit.EntryDistanceToLine_m`, `Pit.EntryRequiredDistance_m`, `Pit.EntryMargin_m`, `Pit.EntryCue`, `Pit.EntryCueText`, `Pit.EntrySpeedDelta_kph`, `Pit.EntryDecelProfile_mps2`, `Pit.EntryBuffer_m`.
- **Validity window:** Only render while `Pit.EntryAssistActive == true`; the assist clears itself when distance >500 m or inputs are invalid.
- **Primary signal:** `Pit.EntryMargin_m` (metres). Use cues only as secondary state indicators.
- **Cue mapping (0–4):** OFF / OK / BRAKE SOON / BRAKE NOW / LATE; derived from margin vs. buffer.

### Recommended visualisation
- **Layout:** Vertical slider or marker with fixed ±150 m scale; centre = 0 m (ideal brake point).
- **Direction:** Marker up = early; marker down = late.
- **Secondary labels:** Show `Pit.EntryCueText` beside the marker; keep colours neutral to avoid masking small movements.
- **Expression hygiene:** Force floating-point math in SimHub expressions (e.g., `150.0`) to prevent integer truncation; avoid nested `if` blocks that cause stepped movement.

## Reset behaviour
- Hide or clear Pit Entry Assist visuals when:
  - Session identity changes,
  - `Pit.EntryAssistActive` is false,
  - SimHub reports `IsInPitLane` true and the line transition already fired (assist logs `END`).

## Logging alignment
- Dash developers can cross-check live visuals with logs:
  - `ACTIVATE` confirms input resolution (distance, pit speed, decel/buffer).
  - `LINE` provides `firstOK`/`okBefore` for post-run tuning.
  - `END` confirms teardown; visuals should already be hidden when this appears.

## Non-Pit Entry guidance (summary)
- Use `_Stable` pace/fuel properties for any text labels.
- Gate launch UI on `LaunchModeActive`; gate rejoin displays on `RejoinIsExitingPits`.
- Keep visibility toggles (`LalaDashShow...`) respected to avoid fighting user preferences.
