# SimHub Log Messages (CANONICAL)

Validated against commit: 52bd57d7c618f4df094c68c4ea6f1e11cc5e328f  
Last updated: 2026-02-06  
Branch: work

Scope: Info-level logs emitted via `SimHub.Logging.Current.Info(...)`. Use the tag prefixes to filter in SimHub’s log view. Placeholder logs are noted; no deprecated messages are currently removed in code. Legacy/alternate copies of this list do not exist.

## How to read logs for debugging
- **Filter by tag prefix** (e.g., `[LalaPlugin:Fuel Burn]`, `[LalaPlugin:Pit Cycle]`, `[LalaPlugin:Finish]`) to isolate subsystems.
- **Lap-coupled logs** (PACE/FUEL/RACE PROJECTION) appear once per completed lap and include acceptance reasons; correlate them with lap numbers.
- **Pit-cycle logs** appear on pit entry/exit/out-lap completion. Use them with PitLite status exports.
- **Session/identity logs** (`[LalaPlugin:Session]`, `[LalaPlugin:Finish]`) mark resets and finish detection; pair with `Reset.*` SimHub exports.
- **Message system logs** (`MSGV1`) surface missing evaluators and active stack debug—check when adding new messages.

## Action, dash, and launch controls
- **`[LalaPlugin:Dash] PrimaryDashMode action fired (placeholder).`** — Action binding confirmed; no behaviour implemented yet.【F:LalaLaunch.cs†L10-L36】
- **`[LalaPlugin:Dash] SecondaryDashMode action fired (placeholder).`** — Same as above for secondary dash action.【F:LalaLaunch.cs†L10-L36】
- **`[LalaPlugin:Launch] LaunchMode pressed -> re-enabled launch mode.`** — User pressed Launch while feature was user-disabled; flag cleared.【F:LalaLaunch.cs†L17-L45】
- **`[LalaPlugin:Launch] LaunchMode blocked (inPits=..., seriousRejoin=...).`** — Launch button ignored due to pit/rejoin guard.【F:LalaLaunch.cs†L17-L45】
- **`[LalaPlugin:Launch] LaunchMode pressed -> ManualPrimed.`** — Launch primed manually after passing guards.【F:LalaLaunch.cs†L17-L45】
- **`[LalaPlugin:Launch] LaunchMode pressed -> aborting (state=...).`** — Launch button used as cancel; state reset and user-disabled latched.【F:LalaLaunch.cs†L17-L45】
- **`[LalaPlugin:PitScreen] Toggle pressed IN PITS -> dismissed=..., manual=...`** — Pit screen dismiss toggle used while on pit road.【F:LalaLaunch.cs†L47-L87】
- **`[LalaPlugin:PitScreen] Toggle pressed ON TRACK -> manual=...`** — Pit screen manual force toggle used on track.【F:LalaLaunch.cs†L47-L87】
- **`[LalaPlugin:MsgCx] MsgCx action fired (pressed latched + engines notified).`** — MsgCx action invoked; message engines receive cancel signal.【F:LalaLaunch.cs†L87-L118】
- **`[LalaPlugin:Launch] State change: <old> -> <new>.`** — Launch state machine transition (e.g., primed → logging).【F:LalaLaunch.cs†L2470-L2494】
- **`[LalaPlugin:Launch Trace] <reason> – cancelling to Idle.`** — Launch trace aborted to idle with the provided reason (debounced).【F:LalaLaunch.cs†L3048-L3074】
- **`[LalaPlugin:Launch] ManualPrimed timeout fired ...`** — Manual prime exceeded 30 s; launch cancelled and user-disabled latched.【F:LalaLaunch.cs†L4993-L5004】

## Fuel seeds, session change, and identity
- **`[LalaPlugin:Fuel Burn] Captured seed from session ... dry=X (n=a), wet=Y (n=b).`** — Saves rolling dry/wet fuel figures before session change for seeding Race.【F:LalaLaunch.cs†L790-L830】
- **`[LalaPlugin:Fuel Burn] Seeded race model from previous session ... conf=Z%.`** — Applies saved seeds on entering Race with matching car/track.【F:LalaLaunch.cs†L934-L956】
- **`[LalaPlugin:Fuel Burn] Car/track change detected – clearing seeds and confidence`** — Fuel model reset because car or track identity changed.【F:LalaLaunch.cs†L968-L983】
- **`[LalaPlugin:Session] token change old=... new=... type=...`** — Session identity changed (SessionID/SubSessionID); triggers subsystem resets and pit-save finalization.【F:LalaLaunch.cs†L3308-L3365】

## Lap detection and per-lap summaries
- **`[LalaPlugin:Lap Detector] Pending expired ...`** — Armed lap increment expired without confirmation; includes target lap and pct.【F:LalaLaunch.cs†L1058-L1068】
- **`[LalaPlugin:Lap Detector] Pending rejected ...`** — Armed lap rejected at expiry with pct/speed context.【F:LalaLaunch.cs†L1118-L1127】
- **`[LalaPlugin:Lap Detector] Ignored reason=low speed ...`** — Lap increment ignored because speed <8 km/h at crossing.【F:LalaLaunch.cs†L1134-L1149】
- **`[LalaPlugin:Lap Detector] Pending armed ...`** — Lap increment armed due to atypical pct; includes track pct and speed context.【F:LalaLaunch.cs†L1150-L1168】
- **`[LalaPlugin:Lap Detector] lap_crossed source=CompletedLaps ...`** — Atypical crossing still accepted; pct and speed included.【F:LalaLaunch.cs†L1169-L1185】
- **`[LalaPlugin:PACE] Lap N: ...`** — Per-lap pace summary with acceptance reason, lap time, stint/last5, leader lap/avg, sample count.【F:LalaLaunch.cs†L1238-L1259】
- **`[LalaPlugin:FUEL PER LAP] Lap N: ...`** — Fuel acceptance/rejection, mode, window counts, confidence, pit involvement.【F:LalaLaunch.cs†L1259-L1267】
- **`[LalaPlugin:FUEL DELTA] Lap N: ...`** — Current fuel, required liters, delta, stable burn/laps remaining.【F:LalaLaunch.cs†L1268-L1272】
- **`[LalaPlugin:RACE PROJECTION] Lap N: ...`** — After-zero source/value, timer0, session remain, projected laps, projection lap seconds, projected remaining seconds.【F:LalaLaunch.cs†L1273-L1281】
- **`[LalaPlugin:Leader Lap] clearing leader pace (feed dropped), lastAvg=...`** — Leader pace cache cleared when feed disappears.【F:LalaLaunch.cs†L1336-L1355】

## Projection, after-zero, and pit window
- **`[LalaPlugin:Drive Time Projection] After 0 Source Change from=... to=...`** — Switches between planner and live after-zero once timer zero is seen and live estimate is valid.【F:LalaLaunch.cs†L2005-L2019】
- **`[LalaPlugin:Pit Window] state=... label=... reqAdd=... tankSpace=... lap=... confStable=... reqStops=... closeLap=...`** — Pit window state transitions with context (requested add, tank space, confidence).【F:LalaLaunch.cs†L2145-L2335】
- **`[LalaPlugin:Drive Time Projection] tRemain=... after0Used=... lapsProj=... simLaps=... lapRef=... lapRefSrc=... after0Observed=...`** — Per-lap race-only projection snapshot showing after-zero, lap reference, and simulation comparison.【F:LalaLaunch.cs†L2337-L2347】
- **`[LalaPlugin:Pace] source=... lap=... stint=... last5=... profile=...`** — Projection lap source change (stint/last5/profile/fallback) with lap seconds.【F:LalaLaunch.cs†L4378-L4391】
- **`[LalaPlugin:Drive Time Projection] projection=drive_time ...`** — Logged when projected laps differ notably from sim laps; shows delta laps, lap ref, after-zero source, remaining seconds.【F:LalaLaunch.cs†L4498-L4516】
- **`[LalaPlugin:After0Result] driver=... leader=... pred=... lapsPred=...`** — After-zero outcome logged once when session ends or checkered seen.【F:LalaLaunch.cs†L4534-L4560】

## Pit, refuel, and PitLite
- **`[LalaPlugin:Pit Cycle] Saved PitLaneLoss = Xs (src).`** — Persisted pit lane loss from PitLite/DTL (debounced).【F:LalaLaunch.cs†L2950-L3004】
- **`[LalaPlugin:Pit Cycle] Pit Lite Data used for DTL.`** — Consumed PitLite out-lap candidate to save pit loss.【F:LalaLaunch.cs†L3004-L3035】
- **`[LalaPlugin:Refuel Rate] Learned refuel rate ... Cooldown until ...`** — Refuel EMA learning completed from detected fuel added/time. 【F:LalaLaunch.cs†L3488-L3507】
- **`[LalaPlugin:Pit Lite] ...`** — See PitCycleLite section below for entry/exit/out-lap/publish logs.
- **`[LalaPlugin:Pit Cycle] ...`** — See PitEngine section below for DTL/direct computations and pit-lap captures.

## Profiles, PBs, and fuel seeds
- **`[LalaPlugin:Profiles] Changes to '<profile>' saved.`** — Save action from Profiles tab command.【F:LalaLaunch.cs†L560-L580】
- **`[LalaPlugin:Profiles] Applied profile to live and refreshed Fuel.`** — Profile applied via view-model callback during init.【F:LalaLaunch.cs†L2585-L2633】
- **`[LalaPlugin:Profile] Session start snapshot: Car='...'  Track='...'`** — Live snapshot cleared and session identity pushed to Fuel tab on session change.【F:LalaLaunch.cs†L3308-L3365】
- **`[LalaPlugin:Profile] New live combo detected. Auto-selecting profile for Car='...' Track='...'`** — Auto-selection fired once per new car/track combo in DataUpdate.【F:LalaLaunch.cs†L3598-L3625】
- **`[LalaPlugin:Pace] PB Updated: car @ track -> lap`** — PB update accepted via ProfilesManagerViewModel.【F:ProfilesManagerViewModel.cs†L66-L88】
- **`[LalaPlugin:Profiles] Track resolved: key='...'`** — Track resolution during profile operations.【F:ProfilesManagerViewModel.cs†L160-L182】
- **`[LalaPlugin:Profiles] Default Settings profile not found, creating baseline profile.`** — Baseline profile auto-created on load miss.【F:ProfilesManagerViewModel.cs†L551-L570】
- **`[LalaPlugin:Profile/Pace] PB updated for track '...' (...) ...`** — CarProfiles PB change (live or manual).【F:CarProfiles.cs†L230-L251】
- **`[LalaPlugin:Profile/Pace] AvgDry updated ...`** — Dry average lap time edited for a track.【F:CarProfiles.cs†L526-L547】
- **`[LalaPlugin:Profile / Pace] AvgWet updated ...`** — Wet average lap time edited for a track.【F:CarProfiles.cs†L718-L740】

## Dashboard and pit screen automation
- **`[LalaPlugin:Dash] Ignition off detected – auto dash re-armed.`** — Auto dash will re-run on next ignition-on/engine-start.【F:LalaLaunch.cs†L3690-L3710】
- **`[LalaPlugin:Dash] Auto dash executed for session '...' – mode=auto, page='...'`** — Auto dash switched page on ignition-on/engine-start.【F:LalaLaunch.cs†L3711-L3724】
- **`[LalaPlugin:Dash] Auto dash timer expired – mode set to 'manual'.`** — Auto dash reverted to manual after delay.【F:LalaLaunch.cs†L3718-L3729】
- **`[LalaPlugin:PitScreen] Active -> <bool> (onPitRoad=..., dismissed=..., manual=...)`** — Pit screen visibility changed due to pit state or manual toggle.【F:LalaLaunch.cs†L3734-L3763】

## Finish timing and after-zero observation
- **`[LalaPlugin:Finish] checkered_flag trigger=flag ...`** — Finish detection driven by session flag data; includes leader/class validity and multiclass flag.【F:LalaLaunch.cs†L4566-L4715】
- **`[LalaPlugin:Finish] leader_finish trigger=derived source=...`** — Derived leader finish (class/overall) once timer zero seen and heuristics trip.【F:LalaLaunch.cs†L4716-L4740】
- **`[LalaPlugin:Finish] finish_latch trigger=driver_checkered ...`** — Driver checkered lap detected; logs timer0, leader/driver checkered times, after-zero measurements.【F:LalaLaunch.cs†L4729-L4780】

## Leader lap selection
- **`[LalaPlugin:Leader Lap] reject source=... reason=...`** — Candidate leader lap rejected (too small, below floor); may fall back to previous avg.【F:LalaLaunch.cs†L4845-L4862】
- **`[LalaPlugin:Leader Lap] using leader lap from <source> = Xs`** — Accepted leader lap source (telemetry fallback ordering).【F:LalaLaunch.cs†L4852-L4867】
- **`[LalaPlugin:Leader Lap] no valid leader lap time from any candidate – returning 0`** — All candidates invalid; leader lap cleared.【F:LalaLaunch.cs†L4869-L4872】
- **`[LalaPlugin:Leader Lap] ResetSnapshotDisplays: cleared live snapshot including leader delta.`** — Live strategy snapshot cleared on session end/reset from FuelCalcs.【F:FuelCalcs.cs†L2985-L3023】
- **`[LalaPlugin:Leader Lap] CalculateStrategy: estLap=..., leaderDelta=..., leaderLap=...`** — Strategy lap calculation when leader delta or estimate changes meaningfully.【F:FuelCalcs.cs†L3839-L3879】

## PitEngine (DTL/direct lane timing)
- **`[LalaPlugin:Pit Cycle] Direct lane travel computed -> lane=Xs, stop=Ys, direct=Zs`** — Valid direct lane time captured; includes lane and stop timing.【F:PitEngine.cs†L90-L175】
- **`[LalaPlugin:Pit Cycle] Pit exit detected – lane=Xs, stop=Ys, direct=Zs. Awaiting pit-lap completion.`** — Pit exit edge latched with timers; arms pit lap/out-lap tracking.【F:PitEngine.cs†L122-L175】
- **`[LalaPlugin:Pit Cycle] Pit-lap invalid – aborting pit-cycle evaluation.`** — Pit lap failed validation; cycle cleared.【F:PitEngine.cs†L175-L218】
- **`[LalaPlugin:Pit Cycle] Pit-lap captured = Xs – awaiting out-lap completion.`** — Valid pit lap latched; waiting for out-lap.【F:PitEngine.cs†L189-L207】
- **`[LalaPlugin:Pit Cycle] Out-lap invalid – aborting pit-cycle evaluation.`** — Out-lap rejected; clears state.【F:PitEngine.cs†L207-L218】
- **`[LalaPlugin:Pit Cycle] DTL computed (formula): Total=Xs, NetMinusStop=Ys (avg=As, pitLap=Bs, outLap=Cs, stop=Ds)`** — Final DTL computation with contributing terms.【F:PitEngine.cs†L218-L239】

## PitCycleLite (pit-lite surface)
- **`[LalaPlugin:Pit Lite] Entry detected. Arming cycle and clearing previous pit figures.`** — Pit entry edge; resets latched values.【F:PitCycleLite.cs†L122-L147】
- **`[LalaPlugin:Pit Lite] Exit detected. Latching lane and box timers from PitEngine.`** — Pit exit edge seen; pulls timers from PitEngine.【F:PitCycleLite.cs†L147-L163】
- **`[LalaPlugin:Pit Lite] Exit latched. Lane=..., Box=..., Direct=..., Status=Status.`** — Immediate exit latch with timers and status (drive-through vs stop).【F:PitCycleLite.cs†L147-L163】
- **`[LalaPlugin:Pit Lite] Out-lap complete. Out=..., In=..., Lane=..., Box=..., Saved=... (source=...).`** — Out-lap completed; publishes loss candidate and lap stats.【F:PitCycleLite.cs†L170-L208】
- **`[LalaPlugin:Pit Lite] In-lap latched. In=...`** — In-lap duration latched when validated.【F:PitCycleLite.cs†L183-L190】
- **`[LalaPlugin:Pit Lite] Publishing loss. Source=..., DTL=..., Direct=..., Avg=...`** — Publishes preferred loss with baseline pace.【F:PitCycleLite.cs†L194-L208】
- **`[LalaPlugin:Pit Lite] Publishing direct loss (avg pace missing). Lane=..., Box=..., Direct=...`** — Fallback publication when pace unavailable.【F:PitCycleLite.cs†L205-L213】

## FuelCalcs (planner and strategy)
- **`[LalaPlugin:Fuel Burn] Strategy reset – defaults applied.`** — Planner reset to defaults (throttled to 1 s).【F:FuelCalcs.cs†L2038-L2057】
- **`[LalaPlugin:Leader Lap] ResetSnapshotDisplays: cleared live snapshot including leader delta.`** — Live snapshot cleared after session end/reset (mirrors leader delta wipe).【F:FuelCalcs.cs†L2985-L3023】
- **`[LalaPlugin:Leader Lap] CalculateStrategy: estLap=..., leaderDelta=..., leaderLap=...`** — Strategy leader lap calculation log (only when values change meaningfully).【F:FuelCalcs.cs†L3839-L3879】

## Message system v1
- **`[LalaPlugin:MSGV1] <message>`** — General MSGV1 engine logs (e.g., stack outputs).【F:Messaging/MessageEngine.cs†L478-L560】
- **`[LalaPlugin:MSGV1] Registered placeholder evaluators: ...`** — Fired when message definitions reference missing evaluators; lists evaluator→message mapping.【F:Messaging/MessageEngine.cs†L499-L560】

## File and trace housekeeping
- **`[LaunchTrace] Deleted trace file: <path>`** — Launch trace file deletion via UI command.【F:LaunchAnalysisControl.xaml.cs†L55-L70】

## Pit Entry Assist
- **`[LalaPlugin:PitEntryAssist] ACTIVATE dToLine=... dReq=... margin=... spdΔ=... decel=... buffer=... cue=...`** — Edge-triggered when the assist arms (EnteringPits **or** limiter ON with overspeed >2 kph). Captures the resolved distance source, constant-decel requirement, margin, speed delta, profiled decel, buffer, and cue at arming time.【F:PitEngine.cs†L240-L363】
- **`[LalaPlugin:PitEntryAssist] LINE dToLine=... dReq=... margin=... spdΔ=... firstOK=... okBefore=... decel=... buffer=... cue=...`** — Edge-triggered on the pit-lane entry transition. Adds compliance markers: `firstOK` = distance to line where speed first dropped to pit limit during this activation; `okBefore` = metres compliant before the line (mirrors `firstOK` because compliance is recorded against distance-to-line). Used for tuning decel/buffer per track and verifying braking timing.【F:PitEngine.cs†L183-L216】
- **`[LalaPlugin:PitEntryAssist] END`** — Edge-triggered when the assist disarms (pit entry handled, invalid inputs, distance ≥500 m, or arming removed).【F:PitEngine.cs†L376-L398】

**Example pit entry lines:**
- `[LalaPlugin:PitEntryAssist] ACTIVATE dToLine=185.3m dReq=142.7m margin=42.6m spdΔ=35.2kph decel=14.0 buffer=15.0 cue=2`
- `[LalaPlugin:PitEntryAssist] LINE dToLine=3.2m dReq=0.0m margin=3.2m spdΔ=-2.1kph firstOK=58.4m okBefore=58.4m decel=14.0 buffer=15.0 cue=1`
- `[LalaPlugin:PitEntryAssist] END`

## Rejoin assist
- **`[LalaPlugin:Rejoin Assist] MsgCx override triggered.`** — Message context override fired inside rejoin assist engine.【F:RejoinAssistEngine.cs†L601-L622】
