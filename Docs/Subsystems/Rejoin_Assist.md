# Rejoin Assist

Validated against commit: c40b3a8fdd1df4c8c1251ff7f573812b915d726f  
Last updated: 2026-01-04  
Branch: work

## Purpose
- Detects and classify loss-of-control, off-track, pit-exit, and wrong-way situations, then surfaces warnings to the dash and message overlays. Alerts linger dynamically to keep the driver informed until both time and speed gates are satisfied.【F:RejoinAssistEngine.cs†L11-L170】【F:RejoinAssistEngine.cs†L521-L569】
- Provides threat-aware messaging for spins (e.g., “HOLD BRAKES”) by blending traffic proximity with vehicle speed.【F:RejoinAssistEngine.cs†L150-L165】【F:RejoinAssistEngine.cs†L269-L444】
- Exposes pit-phase passthroughs for HUD widgets and blocks Launch Mode when a serious incident is active to avoid conflicting driver aids.【F:LalaLaunch.cs†L2922-L2933】【F:LalaLaunch.cs†L3339-L3353】

## Inputs (source + cadence)
- Vehicle state each tick: speed, gear, yaw rate, pit-lane flag, on-track flag, lap distance %, and session type/flags (start-ready/set/go).【F:RejoinAssistEngine.cs†L452-L470】【F:RejoinAssistEngine.cs†L571-L599】
- Surface classification from `PlayerTrackSurfaceMaterial` to detect off-track conditions (>=15).【F:RejoinAssistEngine.cs†L461-L599】
- Pit phase from `PitEngine` (used to mark exiting pits without duplicating timers).【F:RejoinAssistEngine.cs†L466-L513】
- Traffic scan (iRacing only): `CarIdxLapDistPct`, `CarIdxEstTime`, `CarIdxTrackSurface`, `CarIdxOnPitRoad`, player index, and track length from session data, refreshed every update tick for threat scoring.【F:RejoinAssistEngine.cs†L269-L444】
- Profile knobs injected at construction time: linger time, minimum clear speed, and spin yaw threshold (yaw threshold scaled from `SpinYawRateThreshold / 10`).【F:LalaLaunch.cs†L3005-L3010】【F:CarProfiles.cs†L118-L125】
- Update cadence: `RejoinAssistEngine.Update` is called once per `DataUpdate` loop after pit processing.【F:LalaLaunch.cs†L3567-L3606】

## Internal State
- `_currentLogicReason` vs `_detectedReason` capture the active alert and the most recent detection result (can diverge during linger/delays).【F:RejoinAssistEngine.cs†L59-L170】【F:RejoinAssistEngine.cs†L521-L569】
- Timers: `_delayTimer` (arm delays per alert type), `_lingerTimer` (post-clear hold), `_spinHoldTimer` (3 s spin emphasis), `_msgCxTimer` (manual override hold).【F:RejoinAssistEngine.cs†L80-L115】【F:RejoinAssistEngine.cs†L475-L565】
- Recent lap distance `_previousLapDistPct` and last speed/yaw context for wrong-way and spin detection. `_rejoinSpeed` caches speed at linger start.【F:RejoinAssistEngine.cs†L61-L67】【F:RejoinAssistEngine.cs†L585-L599】【F:RejoinAssistEngine.cs†L515-L526】
- Threat assessor state: smoothed TTC, demotion hysteresis targets, last-good TTC timestamp, and debug string for diagnostics.【F:RejoinAssistEngine.cs†L235-L444】
- Public flags mirrored to dashboards: pit phase, exiting-pits boolean, threat level, time-to-threat, linger/override timer readouts, and serious-incident sentinel (spin/stopped/wrong-way).【F:RejoinAssistEngine.cs†L69-L116】【F:RejoinAssistEngine.cs†L91-L104】

## Calculation Blocks (high level)
1) **Reason detection (pre-priority):**  
   - Suppressed states: not in car, in pit lane, offline practice, race-start flag window, or launch mode active short-circuit the system.  
   - Incident detection: yaw rate over threshold ⇒ `Spin`; surface >=15 ⇒ `OffTrackLow/High` based on speed threshold; speed <20 kph on track ⇒ `StoppedOnTrack`; decreasing lap distance while moving and not crossing S/F ⇒ `WrongWay`.【F:RejoinAssistEngine.cs†L571-L600】【F:RejoinAssistEngine.cs†L487-L514】
2) **Priority resolver:**  
   - Manual override (`MsgCx`) wins; first second also hard-resets state.  
   - Suppressed states (including launch active) replace the current logic and clear timers.  
   - Active timers: spin hold (3 s) and pit-exit phase trump new detections.  
   - Linger: when an alert clears, timer runs with dynamic duration (threat-aware minimum 2 s, scaled by speed) and requires speed above the configured threshold to dismiss.  
   - New alerts respect per-type delays (1.5 s high-speed off-track, 2 s stopped, else 1 s) before latching; spin latches immediately and starts the spin hold timer.【F:RejoinAssistEngine.cs†L475-L555】【F:RejoinAssistEngine.cs†L201-L231】
3) **Threat assessment:**  
   - Scans opponents up to 60 % of a lap behind; derives distance and TTC from EstTime or a fallback formula, ignores NIW/pit cars, and applies guards for far-distance/short-spike noise.  
   - Smooths improving TTC (EMA) and applies sensitivity scaling when rejoining or slow, adjusting time/distance gates and hysteresis demotion holds.  
   - Classifies `CLEAR/CAUTION/WARNING/DANGER` and emits debug string for trace overlays.【F:RejoinAssistEngine.cs†L269-L444】
4) **Message selection:**  
   - Maps logic reason to text; suppresses lingering text once the detected reason is clear.  
   - Spin text escalates to “HOLD BRAKES” only when slow (≤50 kph) *and* threat under 6 s or WARNING/DANGER.【F:RejoinAssistEngine.cs†L128-L170】

## Rejoin reasons, triggers, and priorities
- **SettingDisabled (0):** Returned only by legacy callers; not actively produced by detection logic. Treated as suppressed (priority branch that clears timers and prevents alerting).【F:RejoinAssistEngine.cs†L11-L34】【F:RejoinAssistEngine.cs†L487-L505】
- **NotInCar (1):** Fires when `IsOnTrack` is false; hard-resets state and clears timers during the suppressed priority path (launch mode also suppressed).【F:RejoinAssistEngine.cs†L571-L599】【F:RejoinAssistEngine.cs†L487-L505】
- **InPit (2):** Detected via `IsInPitLane`; forces suppressed state so dash messages stay hidden while in lane. Pit-exit alerts are instead driven by `PitEngine` phase later in the priority ladder.【F:RejoinAssistEngine.cs†L452-L513】【F:RejoinAssistEngine.cs†L571-L600】
- **OfflinePractice (3):** Session type equals “Offline Testing”; suppresses alerts so test sessions stay quiet.【F:RejoinAssistEngine.cs†L571-L600】
- **RaceStart (4):** Session flags in start window (`SessionState` 1–3 or StartReady/Set/Go); suppresses alerts to avoid spurious warnings during grid/launch. Launch Mode active also enters the same suppressed branch.【F:RejoinAssistEngine.cs†L488-L505】【F:RejoinAssistEngine.cs†L571-L600】
- **LaunchModeActive (5):** Applied when the launch module is active; follows suppressed-state path to avoid overlapping driver aids.【F:RejoinAssistEngine.cs†L487-L505】【F:LalaLaunch.cs†L3339-L3353】
- **MsgCxPressed (6):** Manual cancel button. First second forces a full reset and then holds the override for up to 30 s (priority 1). During the hold, reason code stays `MsgCxPressed` and all alerts are suppressed. After 30 s, the override timer self-expires.【F:RejoinAssistEngine.cs†L475-L565】
- **None (50):** Neutral state. Any lingering timers must clear before returning here (time + speed gates).【F:RejoinAssistEngine.cs†L515-L526】
- **PitExit (60):** Mirrors `PitEngine` phase `ExitingPits`; outranks linger/new detections (priority 3). Dismisses when phase ends; no delay/linger timers applied.【F:RejoinAssistEngine.cs†L466-L513】
- **StoppedOnTrack (100):** Speed <20 kph on track surface; uses a 2.0 s arming delay before latching and then obeys linger/time-to-clear rules. Message: “STOPPED ON TRACK - HAZARD!”.【F:RejoinAssistEngine.cs†L520-L553】【F:RejoinAssistEngine.cs†L142-L170】【F:RejoinAssistEngine.cs†L571-L600】
- **OffTrackLowSpeed (110):** Off-track surface (>=15) and speed below threshold; uses 1.0 s delay before latching. Message: “OFF TRACK - REJOIN WHEN SAFE”.【F:RejoinAssistEngine.cs†L520-L553】【F:RejoinAssistEngine.cs†L144-L170】
- **OffTrackHighSpeed (120):** Off-track surface (>=15) and speed at/above threshold; uses 1.5 s delay before latching. Message: “OFF TRACK - CHECK TRAFFIC”.【F:RejoinAssistEngine.cs†L520-L553】【F:RejoinAssistEngine.cs†L145-L170】
- **Spin (130):** Yaw over threshold; latches immediately, starts 3 s spin-hold timer, and invokes spin-specific messaging (HOLD BRAKES gate + threat-aware linger). Message stays `Spin` during hold and linger unless MsgCx overrides.【F:RejoinAssistEngine.cs†L527-L565】【F:RejoinAssistEngine.cs†L150-L165】
- **WrongWay (140):** Lap distance percentage decreasing outside S/F crossing (with forward gear and speed >5 kph); latches immediately (no delay) and follows normal linger rules. Message: “WRONG WAY - TURN AROUND SAFELY!”.【F:RejoinAssistEngine.cs†L585-L599】【F:RejoinAssistEngine.cs†L167-L170】

### MsgCx (cancel) behaviour per reason
- Triggers priority 1 override: replaces the active logic reason with `MsgCxPressed`, suppresses all alert text, and zeros timers during the first second (full Reset).【F:RejoinAssistEngine.cs†L475-L505】
- While active (<30 s), the reason code published to SimHub remains `MsgCxPressed`, preventing downstream dashboards from showing rejoin warnings regardless of prior state. After expiry, detection/priorities resume as normal at the next `Update` tick.【F:RejoinAssistEngine.cs†L475-L565】

## Outputs (exports + logs)
- SimHub exports: `RejoinAlertReasonCode/Name/Message`, pit phase (`RejoinCurrentPitPhase(Name)`, `RejoinIsExitingPits`), and threat metrics (`RejoinThreatLevel/Name`, `RejoinTimeToThreat`).【F:LalaLaunch.cs†L2922-L2933】
- Serious-incident flag (`IsSeriousIncidentActive`) feeds Launch Mode blocker to cancel/deny launches when spin/stopped/wrong-way is detected.【F:LalaLaunch.cs†L3339-L3353】
- Dash visibility toggles (`LalaDashShowRejoinAssist`, `MsgDashShowRejoinAssist`) live alongside other dash controls so users can hide the overlay without disabling logic.【F:LalaLaunch.cs†L2935-L2953】
- Log: manual cancel trigger logs `[LalaPlugin:Rejoin Assist] MsgCx override triggered.` via `TriggerMsgCxOverride()`.【F:RejoinAssistEngine.cs†L602-L605】

## Dependencies / ordering assumptions
- Requires `PitEngine` instance to mirror pit phases; rejoin update runs after pit update so pit-exit state is current before evaluating priorities.【F:LalaLaunch.cs†L3044-L3053】【F:LalaLaunch.cs†L3567-L3606】
- Traffic/threat block is iRacing-only; non-iRacing titles remain at `CLEAR` with default TTC.【F:RejoinAssistEngine.cs†L284-L288】
- Profile inputs must stay within defensive bounds: linger time is clamped to 0.5–10 s when wiring `PitEngine`; spin yaw threshold is divided by 10 before passing to the engine.【F:LalaLaunch.cs†L3005-L3052】【F:CarProfiles.cs†L118-L125】

## Reset Rules
- Session-token change resets the engine, pit phase, lap-distance tracking, and threat state to defaults before continuing the new session.【F:LalaLaunch.cs†L3529-L3555】
- Manual cancel (`MsgCx`) resets state during the first second of the override; leaving the car (`NotInCar`) also clears timers and reasons.【F:RejoinAssistEngine.cs†L475-L505】【F:RejoinAssistEngine.cs†L571-L599】
- Spin hold and manual override timers self-expire at 3 s and 30 s respectively; linger clears once both time and speed gates are met.【F:RejoinAssistEngine.cs†L521-L565】

## Failure Modes / Safeguards
- Threat scan falls back to `CLEAR` if required iRacing arrays or lap distance data are missing; debug string records the guard cause for diagnostics.【F:RejoinAssistEngine.cs†L284-L298】【F:RejoinAssistEngine.cs†L442-L444】
- Wrong-way detection guards against start/finish crossings (lap pct wrap) to avoid false positives; still relies on forward gear check to reduce reverse-stint noise.【F:RejoinAssistEngine.cs†L585-L599】
- High-speed off-track and stopped-on-track alerts require a minimum delay before latching, reducing transient false alarms but delaying the first warning slightly.【F:RejoinAssistEngine.cs†L527-L555】
- Linger dismissal requires both time and speed gates; if the car is kept below the configured clear-speed threshold, the visible alert will persist longer by design.【F:RejoinAssistEngine.cs†L515-L526】

## Test Checklist
- Trigger each alert path (spin, wrong-way, stopped, off-track high/low, pit exit) and verify correct message text and delay/linger behaviour.  
- Confirm threat levels climb and decay with nearby traffic during a rejoin (including spin “HOLD BRAKES” gating).  
- Validate MsgCx override clears messages immediately and holds suppression for up to 30 s.  
- Verify session change resets state (reason code/time-to-threat clear) and Launch Mode refuses to arm during active serious incidents.  
- Check pit-exit flagging matches PitEngine phases on dash exports while on-track alerts remain suppressed in pit lane.  
- Adjust profile knobs (linger time, clear speed, yaw threshold) and ensure changes flow through without restart.【F:LalaLaunch.cs†L3005-L3010】【F:CarProfiles.cs†L118-L125】

## TODO / VERIFY
- TODO/VERIFY: Validate spin yaw-rate threshold scaling (`/10`) against telemetry units to ensure cross-sim correctness.【F:LalaLaunch.cs†L3005-L3010】
- TODO/VERIFY: Threat assessor currently ignores cars more than 60 % of a lap behind; confirm this window is sufficient on ultra-short tracks.【F:RejoinAssistEngine.cs†L321-L339】
