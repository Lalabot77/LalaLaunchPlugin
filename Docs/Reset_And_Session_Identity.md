# Reset & Session Identity (CANONICAL)

Validated against commit: 88cfd6ba10558452391dd921d23ae69b94a0a44e  
Last updated: 2025-12-28  
Branch: work

## Definitions
- **Session identity/token:** `SessionID:SubSessionID` string derived from `WeekendInfo.SessionID/SubSessionID`, exported as `Reset.ThisSession`; prior value exported as `Reset.LastSession`.【F:LalaLaunch.cs†L3308-L3365】【F:LalaLaunch.cs†L2737-L2740】
- **Session type:** SimHub `SessionTypeName` (e.g., Practice, Qualifying, Race), exported as `Reset.ThisSessionType` and tracked separately from token for fuel-model handling.【F:LalaLaunch.cs†L3235-L3335】【F:LalaLaunch.cs†L2737-L2740】
- **New session (plugin perspective):** either the session token changes or the session type changes compared to `_lastFuelSessionType`.

## What constitutes a reset
- **Session token change** (identity) triggers: fuel/pit/rejoin resets, finish-timing reset, smoothing reset, profile snapshot clear, and a `[LalaPlugin:Session] token change ...` log entry.【F:LalaLaunch.cs†L3308-L3365】
- **Session type change** triggers fuel-model reset/seed logic and MSGV1 session reset even if the token is unchanged.【F:LalaLaunch.cs†L3649-L3676】
- **Car/track change** (while identity stable) triggers confidence/fuel model reset and clears stored seeds.【F:LalaLaunch.cs†L968-L1003】

## Reset matrix
| Subsystem | Reset triggers | State cleared | State preserved | Evidence logs |
| --- | --- | --- | --- | --- |
| Fuel model & pace | Session type change; session token change; car/track change; explicit call from `HandleSessionChangeForFuelModel`. | Fuel windows (dry/wet), confidence, stable burn/pace, lap detector, pit window state, live max fuel, pace windows, smoothing, finish timing readiness; seeds cleared on car/track change. | Seeds applied when entering Race with matching car/track; profile fuel averages updated only when valid dry laps exist. | `[LalaPlugin:Fuel Burn] Car/track change detected...`【F:LalaLaunch.cs†L968-L983】; session token log【F:LalaLaunch.cs†L3308-L3365】; per-lap logs restart after reset. |
| PitEngine & PitLite | Session token change; session type change indirectly via fuel reset; session token change finalizes pending PitLite candidate before reset. | Pit phase state, latched timers, pit-lite cycle, freeze flags. | Persisted pit-lane loss saved to profile before reset if available. | `[LalaPlugin:Pit Cycle] Saved PitLaneLoss ...`【F:LalaLaunch.cs†L2950-L3004】; token-change block consumes candidate and resets pit state.【F:LalaLaunch.cs†L3308-L3365】 |
| Finish timing / after-zero | Session token change; session type change; after session end when checkered processed. | Leader finished latches, timer-zero latch, observed extra-time state, cached class metadata. | Observed extra-time result logged once per session end. | `[LalaPlugin:Finish] ...` logs for flag/derived/driver checkered; after-zero result log.【F:LalaLaunch.cs†L4566-L4790】【F:LalaLaunch.cs†L4534-L4560】 |
| Messaging (MSGV1) | Session type change; session token change. | Message stack, active outputs, missing-evaluator state. | None (definitions stay loaded). | Implicit via `ResetSession()`; no dedicated log (use MSGV1 stack logs for evidence).【F:LalaLaunch.cs†L3308-L3365】【F:LalaLaunch.cs†L3649-L3676】 |
| Rejoin assist | Session token change. | Logic code, threat state, pit-phase detection. | None. | Reset invoked in session-token change block (no direct log).【F:LalaLaunch.cs†L3308-L3365】 |
| Dash/pit screen | Session token change resets snapshot car/track and pits dismissed flag; auto-dash re-armed on session-type change for ignition transitions. | Live snapshot car/track labels, pit screen dismissed flag, auto-dash arming tokens. | User visibility toggles (settings) persist. | `[LalaPlugin:Profile] Session start snapshot...`【F:LalaLaunch.cs†L3308-L3365】; auto-dash logs on ignition events.【F:LalaLaunch.cs†L3690-L3730】 |

## Dashboard-facing fuel/projection outputs
- Both session-token resets and session-type transitions now clear all fuel instruction exports (deltas, will-add, laps remaining, projections) alongside smoothed projections so dashboards never retain stale lap/fuel values after a reset. This clearing happens together with the existing fuel/pit resets described above.【F:LalaLaunch.cs†L3340-L3370】【F:LalaLaunch.cs†L3669-L3684】【F:LalaLaunch.cs†L4136-L4168】

## Timer-zero considerations
- **Contract:** Timed races should follow the leader-white-flag after timer zero; see `Docs/TimerZeroBehavior.md` for expected iRacing rules.
- **Observed behaviour in code:** `_timerZeroSeen` latches when `SessionTimeRemain` crosses to ≤0.5 s after at least one completed lap. Finish detection uses session flags plus heuristics comparing leader lap distance; after-zero projections log source switches (`planner` vs `live`) and final observed after-zero at session end.【F:LalaLaunch.cs†L1895-L1993】【F:LalaLaunch.cs†L4566-L4790】
- **Reset rules:** Finish-timing state resets on session token/type change and after `MaybeLogAfterZeroResult` runs.
- TODO/VERIFY: Confirm if lap-limited (non-timed) races require additional finish derivation beyond leader lap pct heuristic (currently heuristic only when timed).【F:LalaLaunch.cs†L4566-L4715】

## Cross-links
- Exported reset/session fields: `Reset.*`, `Race.*` flags in `Docs/SimHubParameterInventory.md`.
- Timer-zero modelling details: `Docs/TimerZeroBehavior.md`.
- Pit/fuel projection impacts: `Docs/FuelProperties_Spec.md`.
