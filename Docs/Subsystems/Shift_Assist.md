# Shift Assist

Validated against commit: 7318ff6
Last updated: 2026-02-19
Branch: work

## Purpose
- Provide an audible upshift cue when RPM reaches a profile target for the current gear.
- Support predictive lead-time adjustment so the beep can trigger slightly before the raw target RPM.
- Capture runtime beep→upshift delay samples per gear for tuning and dashboard diagnostics.

## Inputs (source + cadence)
- Per-tick telemetry: gear, RPM, throttle.
- Active car profile shift targets, resolved by active gear stack id.
- Global Shift Assist settings: enable toggle, learning mode toggle, beep duration, lead-time ms, beep sound toggle/volume, custom WAV enable/path, debug CSV toggle/max Hz.
- Audio fallback assets: embedded default WAV extracted to plugin common storage.

## Internal state
- `ShiftAssistEngine`: last gear, threshold-crossing latch, cooldown timer, suppress-after-downshift latch, RPM rate and effective target tracking.
- `LalaLaunch`: beep latch timer, pending delay sample (gear + beep time), per-gear rolling delay stats (avg + sample count), enable-edge log latch, debug-audio-delay telemetry, optional debug CSV writer state.
- `ShiftAssistAudio`: resolved audio path, missing-custom warning latch, sound-choice log dedupe, `SoundPlayer` instance/cache.

## Calculation blocks (high level)
1) Resolve active gear stack and per-gear target RPM from profile.
2) Estimate RPM rate (bounded/guarded) and compute optional lead-time adjusted effective target.
3) Gate cueing by valid gear/target and high-throttle condition (>= ~90%).
4) Apply reset hysteresis and downshift suppression so repeated triggers are avoided until RPM drops sufficiently.
5) Enforce cooldown between beeps, then trigger cue and arm pending delay capture.
6) On subsequent upshift, compute beep→shift delay sample and update rolling per-gear averages.

## Outputs (exports + logs)
- Exports: `ShiftAssist.ActiveGearStackId`, `ShiftAssist.TargetRPM_CurrentGear`, `ShiftAssist.ShiftRPM_G1..G8`, `ShiftAssist.EffectiveTargetRPM_CurrentGear`, `ShiftAssist.RpmRate`, `ShiftAssist.Beep`, `ShiftAssist.ShiftLightEnabled`, `ShiftAssist.Learn.Enabled`, `ShiftAssist.Learn.State`, `ShiftAssist.Learn.ActiveGear`, `ShiftAssist.Learn.WindowMs`, `ShiftAssist.Learn.PeakAccelMps2`, `ShiftAssist.Learn.PeakRpm`, `ShiftAssist.Learn.LastSampleRpm`, `ShiftAssist.Learn.SavedPulse`, `ShiftAssist.Learn.Samples_G1..G8`, `ShiftAssist.Learn.LearnedRpm_G1..G8`, `ShiftAssist.Learn.Locked_G1..G8`, `ShiftAssist.State`, `ShiftAssist.Debug.AudioDelayMs`, `ShiftAssist.Debug.AudioDelayAgeMs`, `ShiftAssist.Debug.AudioIssued`, `ShiftAssist.Debug.AudioBackend`, `ShiftAssist.Debug.CsvEnabled`, `ShiftAssist.DelayAvg_G1..G8`, `ShiftAssist.DelayN_G1..G8`, `ShiftAssist.Delay.Pending`, `ShiftAssist.Delay.PendingGear`, `ShiftAssist.Delay.PendingAgeMs`, `ShiftAssist.Delay.PendingRpmAtCue`, `ShiftAssist.Delay.RpmAtBeep`, `ShiftAssist.Delay.CaptureState`.
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
