# SimHub Log Info Messages

This file documents the Info-level messages emitted via `SimHub.Logging.Current.Info(...)` and what each value in the message represents.

## LalaLaunch.cs
- **`[SimHubLogInfo][LapCrossing] | pace ... | fuel ...`** — Emitted on each accepted lap to summarize pace and fuel updates. Pace block includes `lap`, `time`, acceptance flag/reason, baseline/delta, stint/last5 averages, confidence, leader lap/average, and sample count; fuel block carries `used`, acceptance flag/reason, `mode` (wet/dry), live burn, wet/dry window counts, session max burn, fuel confidence, overall confidence, and whether a pit trip was active.【F:LalaLaunch.cs†L1060-L1095】
- **`[LiveFuel] Captured seed from session ... dry=X (n=a), wet=Y (n=b).`** — Written when saving rolling fuel figures to seed the next session; shows car, track key, dry/wet fuel-per-lap values, and sample counts used for each window.【F:LalaLaunch.cs†L640-L678】
- **`[LiveFuel] Car/track change detected – clearing seeds and confidence`** — Indicates the fuel model was reset because either car or track changed; no variable values beyond the fixed message.【F:LalaLaunch.cs†L822-L836】
- **`[LiveFuel] HandleSessionChangeForFuelModel error: ...`** and **`CaptureFuelSeedForNextSession error: ...`** — Exception catch logs for session-change fuel handling with the thrown message appended.【F:LalaLaunch.cs†L675-L877】
- **`[LapDetector] Pending lap confirmation expired ...`** — Warns that a lap increment was not confirmed before the timeout; includes pending lap number and the last observed track percentage when arming.【F:LalaLaunch.cs†L906-L918】

## PitEngine.cs
- **`[PitEngine] Direct lane travel computed -> lane=Xs, stop=Ys, direct=Zs`** — Live lane timer minus stop duration when a valid direct travel time is measured; shows total lane time, stopped time, and derived direct travel seconds.【F:PitEngine.cs†L100-L236】
- **`[PitEngine] Pit exit detected – lane=Xs, stop=Ys, direct=Zs. Awaiting pit-lap completion.`** — Fired when leaving pit lane with valid timers, carrying the same trio of values as above and signaling the pit-lap latch is armed.【F:PitEngine.cs†L164-L175】
- **`[PitEngine] Pit-lap invalid – aborting pit-cycle evaluation.`** — Indicates the pit lap failed validation (e.g., bad data) and the cycle is cleared.【F:PitEngine.cs†L189-L217】
- **`[PitEngine] Pit-lap captured = Xs – awaiting out-lap completion.`** — Confirms a valid pit lap was latched and reports its seconds before waiting for the out-lap.【F:PitEngine.cs†L189-L207】
- **`[PitEngine] Out-lap invalid – aborting pit-cycle evaluation.`** — Out-lap failed validation; pit-cycle metrics are discarded.【F:PitEngine.cs†L210-L218】
- **`[PitEngine] DTL computed (formula): Total=Xs, NetMinusStop=Ys (avg=As, pitLap=Bs, outLap=Cs, stop=Ds)`** — Final pit delta calculation showing total and net (minus stop) losses plus the inputs used: baseline average lap, pit lap, out lap, and stop duration.【F:PitEngine.cs†L225-L239】

## PitCycleLite.cs
- **`[PitLite] ENTRY edge detected – arming cycle and clearing previous pit figures.`** — Marks pit-entry detection, resets latched timers/values for the new cycle.【F:PitCycleLite.cs†L128-L146】
- **`[PitLite] EXIT edge detected – latching lane/box timers from PitEngine.`** and **`Exit latched: lane=Xs, box=Ys, direct=Zs, status=Status`** — Signals pit exit, copies timers from PitEngine, computes direct lane time, and records stop vs. drive-through status.【F:PitCycleLite.cs†L147-L163】
- **`[PitLite] Out-lap complete: Out=Xs, In=Ys, lane=As, box=Bs, chosen=Cs (source).`** — Logged at S/F when the out-lap completes; shows latched in/out laps, lane/box timers, chosen loss, and which source (`dtl`/`direct`) was used.【F:PitCycleLite.cs†L170-L208】
- **`[PitLite] Latched In-lap = Xs.`** — Captures the in-lap duration when it completes.【F:PitCycleLite.cs†L170-L190】
- **`[PitLite] Publishing loss (source=dtl): dtl=Xs, direct=Ys, avg=Zs.`** — Publishes pit loss when both laps and baseline pace exist; lists both DTL and direct computations plus the baseline used.【F:PitCycleLite.cs†L197-L208】
- **`[PitLite] Publishing direct loss (no avg pace): lane=Xs, box=Ys, direct=Zs.`** — Fallback publication when baseline pace is missing; reports the timers used.【F:PitCycleLite.cs†L207-L217】

## Launch trace and profile operations
- **`[LaunchTrace] ...` messages (open/close/append/discard)** — Lifecycle of launch trace CSV files: opening a new file, discarding unusable traces, writing summaries, or closing files. Each message includes the file path or timestamps involved.【F:LalaLaunch.cs†L4778-L4955】
- **`[Profiles] ...` messages** — Profile save/ensure logs indicating when car/track profiles are created, updated, rejected, or persisted; messages typically include the profile name, car, track key, or lap time used.【F:LalaLaunch.cs†L447-L2252】【F:ProfilesManagerViewModel.cs†L41-L633】
- **`[PB] ...` messages** — Personal-best capture or rejection outcomes showing lap milliseconds, car, and track key alongside whether the candidate was accepted.【F:LalaLaunch.cs†L3106-L3148】【F:ProfilesManagerViewModel.cs†L41-L112】

## Launch and pace monitoring
- **`[LalaLaunch] ...` messages** — High-level lifecycle events such as session snapshots, refuel start/end timing, launch state aborts, on-track detection, or auto-mode switching. Values vary by message (e.g., car/track names, timestamps, or mode/page labels).【F:LalaLaunch.cs†L2887-L3214】
- **`[Pace] ...` and `[FuelLeader] ...` messages** — Pace filtering decisions (e.g., rejecting outliers or logging fallback pace sources) and leader-lap parsing status; each message lists the lap times, deltas, or raw values considered.【F:LalaLaunch.cs†L1161-L4205】
