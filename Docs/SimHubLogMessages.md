# SimHub Log Info Messages

This file documents the Info-level messages emitted via `SimHub.Logging.Current.Info(...)` and what each value in the message represents.

## LalaLaunch.cs (fuel, laps, and projections)
- **`[LalaPlugin:Fuel Burn] Captured seed from session ... dry=X (n=a), wet=Y (n=b).`** — Saves rolling fuel figures to seed the next session, logging car, track key, dry/wet fuel-per-lap, and sample counts.【F:LalaLaunch.cs†L663-L691】
- **`[LalaPlugin:Fuel Burn] Seeded race model from previous session ...`** — Confirms dry/wet seed values were applied when starting a new session, including confidence.【F:LalaLaunch.cs†L807-L823】
- **`[LalaPlugin:Fuel Burn] Car/track change detected – clearing seeds and confidence`** — Indicates the fuel model was reset because car or track changed.【F:LalaLaunch.cs†L835-L849】
- **`[LalaPlugin:Lap Detector] ...` (low-speed ignore, pending armed/rejected, atypical crossing)** — Lap detection debug logs covering rejected laps (e.g., too slow), armed pending confirmations with track % and speed, rejected pendings, and atypical crossings that still advance the lap.【F:LalaLaunch.cs†L980-L1054】
- **`[LalaPlugin:PACE] Lap N: ...`**, **`[LalaPlugin:FUEL PER LAP] Lap N: ...`**, **`[LalaPlugin:FUEL DELTA] Lap N: ...`**, **`[LalaPlugin:RACE PROJECTION] Lap N: ...`** — Per-lap summaries at S/F with acceptance flags/reasons plus pace deltas, stint/last5, leader pace, fuel mode and window counts, delta-to-finish liters, stable laps remaining, projection lap source, after-zero handling, and projected laps/time remaining.【F:LalaLaunch.cs†L1119-L1148】
- **`[LalaPlugin:Leader Lap] clearing leader pace (feed dropped), lastAvg=...`** — Clears stored leader pace when the feed disappears after a lap cross.【F:LalaLaunch.cs†L1203-L1223】
- **`[LalaPlugin:Drive Time Projection] ...`** — Race-only per-lap projection log showing session time remaining, after-zero source/value, projected laps, reference lap time/source, and observed after-zero drive time.【F:LalaLaunch.cs†L2085-L2098】
## LalaLaunch.cs (profiles, pit cycle, launch/dash state)
- **`[LalaPlugin:Profiles] Changes to '<profile>' saved.`** — Confirmation when the active profile is saved from the UI command.【F:LalaLaunch.cs†L449-L461】
- **`[LalaPlugin:Profiles] Applied profile to live and refreshed Fuel.`** — Fired when a profile is applied via the Profiles view callback during plugin init.【F:LalaLaunch.cs†L2359-L2372】
- **`[LalaPlugin:Pit Cycle] Saved PitLaneLoss = Xs (src).`** — Records persistence of pit-lane loss (DTL or direct) after calculation and pushes it to the fuel tab snapshot.【F:LalaLaunch.cs†L2695-L2711】
- **`[LalaPlugin:Pit Cycle] Pit Lite Data used for DTL.`** — Notes that a finished out-lap from `PitCycleLite` was consumed to compute pit loss.【F:LalaLaunch.cs†L2713-L2728】
- **`[LalaPlugin:Launch Trace] ...`** — Lifecycle of launch trace CSVs: discards, aborts when off track/in pits, manual timeout, PB announcements, and session snapshots include car/track names and reasons.【F:LalaLaunch.cs†L3010-L4557】
- **`[LalaPlugin:Dash] ...`** — Dash auto-mode/on-track detection logs showing resets or mode/page switches.【F:LalaLaunch.cs†L3323-L3343】
- **`[LalaPlugin:Leader Lap] no valid leader lap time from any candidate – returning 0`** — Emitted when leader lap parsing yields no usable candidates.【F:LalaLaunch.cs†L4414-L4433】

## Lap detector timeout
- **`[LalaPlugin:Lap Detector] Pending lap confirmation expired ...`** — Warns that an armed lap increment was not confirmed before timeout, including pending lap, last observed track %, and speed context.【F:LalaLaunch.cs†L980-L1009】

## PitEngine.cs
- **`[LalaPlugin:Pit Cycle] Direct lane travel computed -> lane=Xs, stop=Ys, direct=Zs`** — Lane timer minus stop duration when a valid direct travel time is measured; shows lane time, stopped time, and derived direct seconds.【F:PitEngine.cs†L90-L236】
- **`[LalaPlugin:Pit Cycle] Pit exit detected – lane=Xs, stop=Ys, direct=Zs. Awaiting pit-lap completion.`** — Triggered on pit-lane exit with valid timers; arms pit-lap latch.【F:PitEngine.cs†L89-L175】
- **`[LalaPlugin:Pit Cycle] Pit-lap invalid – aborting pit-cycle evaluation.`** — Pit lap failed validation; cycle cleared.【F:PitEngine.cs†L189-L217】
- **`[LalaPlugin:Pit Cycle] Pit-lap captured = Xs – awaiting out-lap completion.`** — Valid pit lap latched with its duration logged.【F:PitEngine.cs†L192-L207】
- **`[LalaPlugin:Pit Cycle] Out-lap invalid – aborting pit-cycle evaluation.`** — Out-lap failed validation; cycle discarded.【F:PitEngine.cs†L210-L218】
- **`[LalaPlugin:Pit Cycle] DTL computed (formula): Total=Xs, NetMinusStop=Ys (avg=As, pitLap=Bs, outLap=Cs, stop=Ds)`** — Final pit delta computation with all inputs echoed.【F:PitEngine.cs†L223-L239】

## PitCycleLite.cs
- **`[LalaPlugin:Pit Lite] Entry detected. Arming cycle and clearing previous pit figures.`** — Pit entry edge; resets latched timers and state.【F:PitCycleLite.cs†L122-L147】
- **`[LalaPlugin:Pit Lite] Exit detected. Latching lane and box timers from PitEngine.`** and **`Exit latched. Lane=Xs, Box=Ys, Direct=Zs, Status=Status.`** — Pit exit edge and immediate latch of lane/box/direct timers with stop vs. drive-through status.【F:PitCycleLite.cs†L147-L163】
- **`[LalaPlugin:Pit Lite] Out-lap complete. Out=..., In=..., Lane=..., Box=..., Saved=... (source=...).`** — At S/F after out-lap completion, listing lap times, timers, chosen loss, and source (`dtl`/`direct`).【F:PitCycleLite.cs†L170-L208】
- **`[LalaPlugin:Pit Lite] Latched In-lap. In=...`** — Logs in-lap duration when it finishes.【F:PitCycleLite.cs†L183-L190】
- **`[LalaPlugin:Pit Lite] Publishing loss. Source=..., DTL=..., Direct=..., Avg=...`** — Publishes pit loss when baseline pace exists.【F:PitCycleLite.cs†L194-L208】
- **`[LalaPlugin:Pit Lite] Publishing direct loss (avg pace missing). Lane=..., Box=..., Direct=...`** — Fallback publication when baseline pace is missing.【F:PitCycleLite.cs†L207-L217】

## FuelCalcs.cs
- **`[LalaPlugin:Fuel Burn] Strategy reset – defaults applied.`** — Strategy inputs reset to defaults; throttled to once per second.【F:FuelCalcs.cs†L2037-L2057】
- **`[LalaPlugin:Leader Lap] ResetSnapshotDisplays: cleared live snapshot including leader delta.`** — Logs when live strategy snapshot rows are cleared after session end/reset.【F:FuelCalcs.cs†L2995-L3013】
- **`[LalaPlugin:Leader Lap] CalculateStrategy: estLap=..., leaderDelta=..., leaderLap=...`** — Strategy tab leader-pace computation with estimated lap, delta, and derived leader lap when values change meaningfully.【F:FuelCalcs.cs†L3842-L3869】

## ProfilesManagerViewModel.cs
- **`[LalaPlugin:Pace] PB Updated: car @ track -> lap`** — Personal-best update with car, track display, and lap time text.【F:ProfilesManagerViewModel.cs†L66-L78】
- **`[LalaPlugin:Profiles] Track resolved: key='...'`** — Logs resolved track key/display during profile operations.【F:ProfilesManagerViewModel.cs†L160-L182】
- **`[LalaPlugin:Profiles] Default Settings profile not found, creating baseline profile.`** — Indicates creation of the default profile when missing.【F:ProfilesManagerViewModel.cs†L551-L570】

## CarProfiles.cs
- **`[LalaPlugin:Profiles] ...` (ensure, add, save)** — Various profile-ensure/save operations log the profile name, track key, or lap stats when creating or updating entries.【F:CarProfiles.cs†L230-L754】

## LaunchAnalysisControl.xaml.cs
- **`[LaunchTrace] Deleted trace file: <path>`** — UI-driven deletion of a recorded launch trace file, logging the full path.【F:LaunchAnalysisControl.xaml.cs†L55-L70】

## RejoinAssistEngine.cs
- **`[LalaPlugin:Rejoin Assist] MsgCx override triggered.`** — Indicates message context override activation within rejoin assist logic.【F:RejoinAssistEngine.cs†L601-L622】
