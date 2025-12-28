# Fuel Properties Technical Specification (CANONICAL)

Validated against commit: 8618f167efb6ed4f89b7fe60b69a25dd4da53fd1  
Last updated: 2025-12-28  
Branch: docs/refresh-index-subsystems

Scope: describes how fuel- and pit-related SimHub properties are computed inside `LalaLaunch.cs`. Export names include the implicit `LalaLaunch.` prefix and are attached via `AttachCore` (see `Docs/SimHubParameterInventory.md` for the full export list).

## Acceptance and rejection rules (per-lap fuel capture)
Evidence: `LalaLaunch.cs` — `DetectLapCrossing` and fuel model updates.【F:LalaLaunch.cs†L1080-L1415】

- **Lap gating:** only completed laps (`CompletedLaps` increment) while session is running.
- **Global race warm-up:** laps ≤1 rejected (`race-warmup` reason).
- **Pit involvement:** any lap touching pit lane rejected (`pit-lap`).
- **First lap after pit exit:** rejected (`pit-warmup`).
- **Incidents/off-track:** rejected with incident code when latched (`incident:<code>`).
- **Telemetry sanity:** fuel delta must be >0.05 L and ≤ max(10 L, 20% of effective tank) or rejected (`fuel<=0`, `fuelTooHigh`).
- **Profile bracket:** if profile baseline exists, require 0.5–1.5× of baseline or reject (`profileBracket`).
- **Window maintenance:** rolling dry/wet windows hold up to 5 accepted laps; seeds removed on overflow unless flagged as active seeds.
- **Max tracking:** session max burn updated only when 0.7–1.8× of baseline to avoid spikes.

## Stability selection
Evidence: `LalaLaunch.cs` — `UpdateStableFuelPerLap` and stable hold logic.【F:LalaLaunch.cs†L4180-L4254】

- Candidate sources: **Live** (latest window average) or **Profile** (track fuel average) when confidence ≤ threshold; fallback yields `StableSource="Fallback"`.
- Deadband: if candidate within `StableFuelPerLapDeadband` of previous stable, value held but source/confidence can advance.
- Confidence mirrors the chosen source: live uses `Confidence`, profile uses the readiness threshold (floored).
- Stable values are clamped to ≥0.1 L/lap to avoid near-zero persistence.
- Stable confidence drives `Fuel.Live.IsFuelReady` and pit window gating.

## Confidence behaviour
Evidence: `LalaLaunch.cs` — fuel confidence computation and usage.【F:LalaLaunch.cs†L1830-L1890】【F:LalaLaunch.cs†L468-L506】

- Fuel confidence grows with valid window size/quality; pace confidence derived separately.
- `OverallConfidence` = probabilistic product of fuel and pace confidences; exported for dashes.
- `Fuel.FuelReadyConfidenceThreshold` defines readiness; when stable confidence drops below it, pit window forces **NO DATA YET**.

## Projection, after-zero, and fallback precedence
Evidence: `LalaLaunch.cs` — `UpdateLiveFuelCalcs`, after-zero model, projection lap selection.【F:LalaLaunch.cs†L1895-L2347】【F:LalaLaunch.cs†L4306-L4391】

- **Projection lap selection:** prefer stint average when pace confidence ≥ `LapTimeConfidenceSwitchOn`; fall back to last-5; then profile average; else estimator fallback. Source logged and exported.
- **After-zero handling:** planner seconds (`Fuel.After0.PlannerSeconds`) always available; live estimate activates once timer zero is observed and session time advances beyond zero. Source switches between `planner` and `live` with log notice; `Fuel.After0.Source` reflects the current pick.
- **Projected laps:** computed via `FuelProjectionMath.ProjectLapsRemaining` using projection lap time, session time remaining, and after-zero allowance. If invalid, falls back to SimHub’s laps-remaining telemetry.
- **Fallback precedence:** fuel burn falls back to SimHub’s `DataCorePlugin.Computed.Fuel_LitersPerLap` only when no accepted laps exist (marks `_usingFallbackFuelProfile`). Stable burn may hold prior value if the candidate is invalid.

## Pit window and acceptance/rejection rules
Evidence: `LalaLaunch.cs` — pit window block in `UpdateLiveFuelCalcs`.【F:LalaLaunch.cs†L2145-L2335】

- Race-only and running-session gate; if not racing or no fuel stops required → **N/A**.
- Confidence gate: stable confidence below readiness threshold → **NO DATA YET** (state 5).
- Refuel off or request ≤0 → **SET FUEL!** (state 4).
- Unknown tank capacity → **TANK ERROR** (state 8).
- Otherwise evaluate tank space vs. required add for PUSH/STD/ECO (using push burn, stable burn, or save burn). Priority PUSH > STD > ECO.
- If any mode fits → `IsPitWindowOpen=true`, state 3/2/1 with label (**CLEAR PUSH**/**RACE PACE**/**FUEL SAVE**) and `PitWindowOpeningLap=current lap`.
- If none fit → state 7 (**TANK SPACE**); opening lap projected using ECO burn; closing lap derived from fuel-in-tank ÷ stable burn.
- `PitWindowClosingLap` computed from current fuel and stable burn; 0 if burn invalid.

## Reset triggers and state cleared
Evidence: `LalaLaunch.cs` — `ResetLiveFuelModelForNewSession`, `HandleSessionChangeForFuelModel`, session-change handler.【F:LalaLaunch.cs†L823-L1040】【F:LalaLaunch.cs†L3308-L3365】

- `HandleSessionChangeForFuelModel` called on session type changes detected via `DataUpdate`.
- Driving → Race transition captures seeds then resets model (applies seeds if car/track match).
- Non-race transitions: full reset without seeding.
- Reset clears rolling fuel windows, confidence, stable values, lap detector state, pit window state, pace windows, and live max fuel tracking. `FuelCalculator.ResetTrackConditionOverrideForSessionChange()` is invoked.
- Session token change (`SessionID:SubSessionID`) also clears rejoin, pit, pit-lite state, resets finish timing, smoothers, fuel instructions, and forces profile data reload.

## Stability selection vs. profile and sim fallbacks
Evidence: `LalaLaunch.cs` — fallback logic inside `UpdateLiveFuelCalcs` and `UpdateStableFuelPerLap`.【F:LalaLaunch.cs†L1895-L2143】【F:LalaLaunch.cs†L4180-L4254】

- **Live preferred:** when valid fuel window exists.  
- **Profile fallback:** used for stable burn when live confidence < readiness threshold or no live data.  
- **Sim fallback:** SimHub computed fuel used only to bootstrap live burn when zero accepted laps.  
- Stable burn holds previous value if the new candidate is invalid and prior stable existed; otherwise resets to 0 with `StableSource="Fallback"`.

## Confidence-driven outputs and readiness
Evidence: `LalaLaunch.cs` — readiness checks and pit window gating.【F:LalaLaunch.cs†L468-L506】【F:LalaLaunch.cs†L2145-L2335】

- `Fuel.Live.IsFuelReady` = stable confidence ≥ readiness threshold.
- Pit window state 5 (**NO DATA YET**) when readiness false.
- UI/strategy elements should check `Fuel.Live.IsFuelReady` before consuming lap/time projections.

## Cross-links to SimHub exports
- Full export names, cadence, and attachment points: see `Docs/SimHubParameterInventory.md`.
- Pit window, projection lap source, after-zero source, and confidence values are exported for dashboards and logging.

## TODO/VERIFY
- TODO/VERIFY: Confirm whether incident/off-track wiring is complete or still placeholder (hooks exist, but source for `_latchedIncidentReason` is not yet connected).【F:LalaLaunch.cs†L1080-L1145】
