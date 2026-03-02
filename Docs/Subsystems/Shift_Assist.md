# Shift Assist

Validated against commit: b115732
Last updated: 2026-03-01
Branch: work

## Purpose
- Provide an audible upshift cue when RPM reaches a profile target for the current gear.
- Support predictive lead-time adjustment so the beep can trigger slightly before the raw target RPM.
- Capture runtime beep→upshift delay samples per gear for tuning and dashboard diagnostics.

## Inputs (source + cadence)
- Per-tick telemetry: gear, RPM, throttle.
- Active car profile shift targets, resolved by active gear stack id.
- Global Shift Assist settings: enable toggle, learning mode toggle, beep duration, lead-time ms, beep sound toggle/volume, urgent sound toggle, custom WAV enable/path, debug CSV toggle/max Hz.
- Audio fallback assets: embedded default WAV extracted to plugin common storage.

## Internal state
- `ShiftAssistEngine`: last gear, threshold-crossing latch, cooldown timer, suppress-after-downshift latch, RPM rate and effective target tracking.
- `LalaLaunch`: primary/urgent/beep-export latch timers, pending delay sample (gear + beep time), per-gear rolling delay stats (avg + sample count), enable-edge log latch, debug-audio-delay telemetry, optional debug CSV writer state.
- `ShiftAssistAudio`: resolved audio path, missing-custom warning latch, sound-choice log dedupe, `SoundPlayer` instance/cache.

## Calculation blocks (high level)
1) Resolve active gear stack and per-gear target RPM from profile.
2) Estimate RPM rate (bounded/guarded) and compute optional lead-time adjusted effective target.
3) Gate cueing by valid gear/target and high-throttle condition (>= ~90%).
4) Apply reset hysteresis and downshift suppression so repeated triggers are avoided until RPM drops sufficiently.
5) Enforce cooldown between beeps, then trigger cue and arm pending delay capture.
6) On subsequent upshift, compute beep→shift delay sample and update rolling per-gear averages.


## Urgent Beep
- Optional secondary cue.
- Plays once per primary shift event.
- Delayed by 1000ms after the primary cue trigger, enforced inside `ShiftAssistEngine` before an urgent trigger can fire.
- Urgent trigger requests are not consumed early while waiting for the 1000ms delay; `_urgentBeepFired` is only set when urgent is actually emitted.
- Cue-dependent gating remains in `LalaLaunch`: urgent playback is only allowed while the cue condition (`ShiftAssist.State == On`) is active.
- Volume = 50% of main Beep volume slider.
- Uses same WAV selection and scaling pipeline.
- Does not affect learning, shift targets, delay capture, or Beep export latch.


## Shift Light Mode
- Per-profile selector with 3 routing modes: `Primary`, `Urgent`, `Both` (`ShiftAssistShiftLightMode` = `0/1/2`, default `2`).
- Controls only Shift Light latch/export routing (`ShiftAssist.ShiftLight`) and does **not** change primary/urgent audio behavior.
- `ShiftAssist.ShiftLight` is the canonical selected shift-light output:
  - `Primary` mode => primary cue latch only
  - `Urgent` mode => urgent cue latch only
  - `Both` mode => primary OR urgent latch
- Additional canonical exports `ShiftAssist.ShiftLightPrimary` and `ShiftAssist.ShiftLightUrgent` expose per-cue latch windows for advanced dashboards (`ShiftAssist.BeepPrimary/BeepUrgent` remain legacy aliases).
- If Shift Light is disabled, all three light exports are forced false.

## Audio pulse output
- `ShiftAssist.Beep` is reserved for audio observability and pulses only on ticks where audio issue succeeds (primary or urgent).
- Use this pulse with `ShiftAssist.Debug.AudioDelayMs` to validate playback timing/latency in dashboards.


## Learning model (physics/telemetry)
- Learning computes **optimal upshift RPM** from telemetry curves (not driver timing):
  - WOT acceptance gate uses throttle `>=95%` with up to `150ms` dip grace, brake noise ignored up to `1.0%`, and brake only considered active when above `1.0%` for `>=100ms`.
  - Samples are only recorded while speed is moving (`>=5 kph`).
  - Near-redline sampling uses limiter-hold behavior (`>=99%` of redline + valid throttle) so pulls are not prematurely ended at limiter.
  - If max window is hit, a `400ms` grace window allows a trailing upshift to still count as valid.
  - Reset/teleport artifacts (session-time rewind, extreme speed jump, impossible RPM discontinuity) cancel current sampling with artifact reasoning instead of normal rejection.
- Per-gear curves are built as RPM bins (50 RPM), with median accel per bin and light 3-bin smoothing for evaluation.
- Gear ratio proxy per gear is learned from telemetry as median `k_g = rpm/speed` using moving samples.
- Crossover rule for gear `g -> g+1`: choose the smallest RPM `r` where `a_next(r * k_{g+1}/k_g) >= a_curr(r) + margin`.
- Learned RPM auto-apply still respects per-gear lock state and requires short-term stability (same result within tolerance across consecutive evaluations) before profile write-back.
- Driver delay measurement remains independent: delay stays cue->upshift based on applied targets (manual or learned).

## Debug CSV — Urgent Columns
- `UrgentEnabled`, `BeepSoundEnabled`, `BeepVolumePct`, `UrgentVolumePctDerived`, `CueActive`, `BeepLatched` provide per-row urgent gating/settings context (with urgent volume derived as base slider / 2, clamped 0..100).
- `MsSincePrimaryAudioIssued`, `MsSincePrimaryCueTrigger`, `MsSinceUrgentPlayed`, `UrgentMinGapMsFixed` remain available as timing anchors for urgent diagnostics (`-1` means anchor unavailable yet); the 1000ms urgent delay decision now occurs in `ShiftAssistEngine`.
- `UrgentEligible`, `UrgentSuppressedReason`, `UrgentAttempted`, `UrgentPlayed`, `UrgentPlayError` provide per-tick urgent decision/outcome observability.
- `UrgentPlayError` is CSV-sanitized (quotes/newlines/commas).
- `RedlineRpm`, `OverRedline`, `Rpm`, `Gear`, `BeepType` provide lightweight runtime context for diagnosing missed urgent reminders around limiter/redline conditions.
- Learning debug columns now also include limiter-hold status/time, artifact reset flags/reason, current/next gear ratio estimates (`k` + validity), current bin diagnostics, and crossover candidate/final RPM with insufficient-data indicator.

## Outputs (exports + logs)
- Exports: `ShiftAssist.ActiveGearStackId`, `ShiftAssist.TargetRPM_CurrentGear`, `ShiftAssist.ShiftRPM_G1..G8`, `ShiftAssist.EffectiveTargetRPM_CurrentGear`, `ShiftAssist.RpmRate`, `ShiftAssist.Beep`, `ShiftAssist.ShiftLight`, `ShiftAssist.ShiftLightPrimary`, `ShiftAssist.ShiftLightUrgent`, `ShiftAssist.BeepLight`, `ShiftAssist.BeepPrimary`, `ShiftAssist.BeepUrgent`, `ShiftAssist.ShiftLightEnabled`, `ShiftAssist.Learn.Enabled`, `ShiftAssist.Learn.State`, `ShiftAssist.Learn.ActiveGear`, `ShiftAssist.Learn.WindowMs`, `ShiftAssist.Learn.PeakAccelMps2`, `ShiftAssist.Learn.PeakRpm`, `ShiftAssist.Learn.LastSampleRpm`, `ShiftAssist.Learn.SavedPulse`, `ShiftAssist.Learn.Samples_G1..G8`, `ShiftAssist.Learn.LearnedRpm_G1..G8`, `ShiftAssist.Learn.Locked_G1..G8`, `ShiftAssist.State`, `ShiftAssist.Debug.AudioDelayMs`, `ShiftAssist.Debug.AudioDelayAgeMs`, `ShiftAssist.Debug.AudioIssued`, `ShiftAssist.Debug.AudioBackend`, `ShiftAssist.Debug.CsvEnabled`, `ShiftAssist.DelayAvg_G1..G8`, `ShiftAssist.DelayN_G1..G8`, `ShiftAssist.Delay.Pending`, `ShiftAssist.Delay.PendingGear`, `ShiftAssist.Delay.PendingAgeMs`, `ShiftAssist.Delay.PendingRpmAtCue`, `ShiftAssist.Delay.RpmAtBeep`, `ShiftAssist.Delay.CaptureState`.
- Logs: enable/toggle/debug-csv transitions, learning reset, active-stack reset/lock/apply-learned action outcomes, beep trigger context (including urgent/primary type and suppression flags), test beep, delay sample capture/reset, optional audio-delay telemetry, custom/default sound choice, and audio warning/error paths.

## Dependencies / ordering assumptions
- Runs from `LalaLaunch.DataUpdate` once per tick after settings/profile resolution.
- Requires `ActiveProfile` to expose shift targets for the active stack and gear.
- Audio playback is optional for logic correctness; cue state/exports still update if playback fails.

## Reset rules
- Disabled state resets pending delay capture and keeps engine state off/no-data as applicable.
- Engine reset occurs on broader plugin/session resets through standard runtime reset paths.
- Delay stats reset only via explicit action; otherwise they persist for the current runtime.

## Failure modes
- Missing or invalid custom WAV -> one-time warning and fallback to embedded default sound.
- Embedded resource extraction/playback failure -> error/warn logs; logical cue still proceeds.
- No target RPM / invalid gear / low throttle -> state reports `NoData` or `On` without beep trigger.

## Test checklist
- Enable Shift Assist and confirm `ShiftAssist.State` transitions away from `Off`.
- Validate beep trigger at target RPM with throttle pinned and no duplicate cues within cooldown.
- Confirm predictive lead-time lowers `EffectiveTargetRPM_CurrentGear` when RPM rate is valid.
- Trigger test beep and verify latch export + log line (and no audio when Beep Sound is disabled).
- Run beep→upshift cycles and confirm `DelayAvg_G*` / `DelayN_G*` update per source gear.
- Test custom WAV valid/invalid paths and verify fallback logs.
- If debug CSV is enabled, verify file creation under `Logs/LalapluginData` and confirm rows are rate-limited by max Hz.
