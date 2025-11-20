# Fuel Tab recovery options

## What the current implementation already does
- UI splits a live session snapshot and a "Pre-Race Planner" stack so users can view live telemetry alongside manual planning inputs. The live snapshot already binds to live car, track, fuel tank, lap pace, and surface metadata through `FuelCalcs` properties. 【F:FuelCalculatorView.xaml†L46-L130】
- The planner paths call into `FuelCalcs`, which pulls car/track defaults from the Profiles view model when available and falls back to a default profile/track if none match. Strategy calculations also respect lap-time inputs, leader deltas, fuel per lap, and max fuel validation guards. 【F:FuelCalcs.cs†L2364-L2420】
- Personal best pacing and live max-fuel suggestions are wired: loading a PB updates the estimated lap time source flag and re-runs strategy math, while live fuel tank detections drive display strings and UI hints. 【F:FuelCalcs.cs†L2306-L2329】

## Option 1 — Stabilize and validate existing flow (low risk)
1. Trace the current data-source toggles (manual vs. live vs. PB vs. profile) and ensure UI indicators track the active source; fix any mismatched bindings or stale state in `FuelCalcs` setters before touching calculations.
2. Harden validation for all inputs used by `CalculateStrategy` (lap times, leader delta, fuel/lap, max fuel) with user-friendly messages and disable calculations when data is clearly invalid instead of allowing fallbacks.
3. Add targeted unit-style regression checks around `CalculateStrategy` and `CalculateSingleStrategy` to lock in current math while we clean bindings.
4. Deliverable: repaired bindings and validation with test coverage; no structural changes to data models.

## Option 2 — Make data source selection explicit (medium risk)
1. Introduce an explicit "data source" selector limited to **live capture** or **stored profile**. Manual entry remains always available as inline overrides (text boxes/sliders) so a user can tweak whichever base source is loaded.
2. Split hydration paths so live and profile loads populate both value and source metadata (lap time, fuel burn, pit lane loss, max fuel). Gate recalculations until the selected source is fully loaded, then allow manual overrides to immediately re-run strategy math.
3. Keep the existing save-to-profile action, but ensure it writes whatever mixed state the user has after combining the selected base source with manual tweaks (including contingency/wet-factor) into the Profiles tab store.
4. Deliverable: predictable, user-controlled source flow with save/load symmetry between Fuel and Profiles tabs while preserving free-form manual tuning.

## Option 3 — Reframe the tab around the "pre-race plan" story (higher effort)
1. Restructure the Pre-Race Planner section into a wizard-like flow: choose car/track → choose data source (manual/live/profile) → review loaded defaults → adjust strategy knobs (wet factor, contingencies) → commit plan.
2. Add a "plan snapshot" summary card that mirrors the live snapshot but for the planned race (fuel budget, expected stints/stops, pit timings), and allow one-click export to profiles or application presets.
3. Refine `CalculateStrategy` inputs to clearly separate planned values from live overrides (e.g., independent structs) to avoid cross-contamination when a live session is running while planning.
4. Deliverable: cohesive planner UX that enforces the philosophy that the Fuel tab is the pre-race planning surface with optional live/replay assistance.

## Recommendation
Start with Option 1 to stabilize bindings/validation and add safety tests, then layer Option 2 so users can intentionally pick and persist their data source. Option 3 can follow once stability is regained and usage feedback is collected.
