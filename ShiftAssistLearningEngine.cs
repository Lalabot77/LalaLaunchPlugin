using System;
using System.Collections.Generic;

namespace LaunchPlugin
{
    public enum ShiftAssistLearningState
    {
        Off,
        Armed,
        Sampling,
        Complete,
        Rejected
    }

    public class ShiftAssistLearningTick
    {
        public ShiftAssistLearningState State { get; set; }
        public int ActiveGear { get; set; }
        public int WindowMs { get; set; }
        public double PeakAccelMps2 { get; set; }
        public int PeakRpm { get; set; }
        public int LastSampleRpm { get; set; }
        public bool SampleAdded { get; set; }
        public int SamplesForGear { get; set; }
        public int LearnedRpmForGear { get; set; }
        public bool ShouldApplyLearnedRpm { get; set; }
        public int ApplyGear { get; set; }
        public int ApplyRpm { get; set; }
        public int LearnMinRpm { get; set; }
        public int LearnRedlineRpm { get; set; }
        public int LearnCaptureMinRpm { get; set; }
        public int LearnCapturedRpm { get; set; }
        public int LearnSampleRpmFinal { get; set; }
        public bool LearnSampleRpmWasClamped { get; set; }
        public string LearnEndReason { get; set; }
        public string LearnRejectedReason { get; set; }
        public bool LearnEndWasUpshift { get; set; }
        public bool LimiterHoldActive { get; set; }
        public int LimiterHoldMs { get; set; }
        public bool ArtifactResetDetected { get; set; }
        public string ArtifactReason { get; set; }
        public double CurrentGearRatioK { get; set; }
        public int CurrentGearRatioKValid { get; set; }
        public double NextGearRatioK { get; set; }
        public int NextGearRatioKValid { get; set; }
        public int CurrentBinIndex { get; set; }
        public int CurrentBinCount { get; set; }
        public double CurrentCurveAccelMps2 { get; set; }
        public int CrossoverCandidateRpm { get; set; }
        public int CrossoverComputedRpmForGear { get; set; }
        public int CrossoverInsufficientData { get; set; }
    }

    public class ShiftAssistLearningEngine
    {
        private const int GearCount = 8;
        private const int StableGearArmMs = 200;
        private const int MinWindowMs = 500;
        private const int MaxWindowMs = 3000;
        private const int MaxWindowUpshiftGraceMs = 400;
        private const int ReArmRpmDrop = 500;
        private const int AbsoluteMinLearnRpm = 2000;
        private const double LearnTrackMinRedlineRatio = 0.70;
        private const int RedlineHeadroomRpm = 300;
        private const double MinThrottleStrong = 0.95;
        private const double MinThrottleHardEnd = 0.90;
        private const int ThrottleDipGraceMs = 150;
        private const double BrakeNoiseThreshold = 0.01;
        private const int BrakeActiveMs = 100;
        private const double MinMovementMps = 5.0 / 3.6;
        private const double NearRedlineRatio = 0.99;
        private const int BinSizeRpm = 50;
        private const int MinBinSamples = 3;
        private const int MinBinsWithData = 8;
        private const int MinRatioSamples = 12;
        private const double MaxPlausibleAccelMps2 = 30.0;
        private const double CrossoverMarginMps2 = 0.10;
        private const int StableCrossoverToleranceRpm = 50;
        private const int StableCrossoverNeededTicks = 3;

        private readonly Dictionary<string, StackRuntime> _stacks = new Dictionary<string, StackRuntime>(StringComparer.OrdinalIgnoreCase);
        private readonly ShiftAssistLearningTick _lastTick = new ShiftAssistLearningTick { State = ShiftAssistLearningState.Off };

        private int _stableGear;
        private double _stableGearSinceSec = double.NaN;

        private bool _samplingActive;
        private int _samplingGear;
        private double _samplingStartSec;
        private int _samplingLearnMinRpm;
        private int _samplingCaptureMinRpm;
        private int _samplingLastObservedRpm;
        private int _samplingPreShiftRpm;
        private double _samplingPeakAccel;
        private int _samplingPeakRpm;
        private bool _samplingLimiterHoldActive;
        private double _samplingLimiterHoldStartedSec;
        private double _samplingThrottleDipStartedSec;
        private double _samplingBrakeActiveStartedSec;
        private bool _samplingPendingUpshiftGrace;
        private double _samplingUpshiftGraceUntilSec;
        private double _lastSessionTimeSec = double.NaN;
        private double _lastSpeedMps = double.NaN;
        private int _lastRpm = 0;
        private int _lastGear = 0;

        public ShiftAssistLearningTick LastTick => _lastTick;

        public ShiftAssistLearningTick Update(bool learningEnabled, string gearStackId, int effectiveGear, int rpm, double throttle01, double brake01, double speedMps, double sessionTimeSec, double lonAccelMps2, int redlineRpmForGear)
        {
            int learnMinRpm = ComputeLearnMinRpm(effectiveGear, redlineRpmForGear);
            int captureMinRpm = ComputeCaptureMinRpm(effectiveGear, redlineRpmForGear);
            var tick = new ShiftAssistLearningTick
            {
                State = learningEnabled ? ShiftAssistLearningState.Armed : ShiftAssistLearningState.Off,
                ActiveGear = effectiveGear,
                PeakAccelMps2 = _samplingPeakAccel,
                PeakRpm = _samplingPeakRpm,
                LastSampleRpm = _lastTick.LastSampleRpm,
                LearnMinRpm = learnMinRpm,
                LearnRedlineRpm = redlineRpmForGear,
                LearnCaptureMinRpm = captureMinRpm,
                LearnCapturedRpm = 0,
                LearnEndWasUpshift = false
            };

            string stackKey = string.IsNullOrWhiteSpace(gearStackId) ? "Default" : gearStackId.Trim();
            var stack = EnsureStack(stackKey);
            bool hasValidSessionTime = IsFinite(sessionTimeSec) && sessionTimeSec >= 0.0;

            bool artifactResetDetected = DetectArtifactReset(hasValidSessionTime, sessionTimeSec, effectiveGear, rpm, speedMps, out string artifactReason);
            tick.ArtifactResetDetected = artifactResetDetected;
            tick.ArtifactReason = artifactReason;

            if (!learningEnabled)
            {
                ResetSampling();
                _stableGear = 0;
                _stableGearSinceSec = double.NaN;
                PopulatePerGearStats(stack, effectiveGear, tick);
                PopulateDebugCurveFields(stack, effectiveGear, rpm, tick);
                CopyTick(tick);
                UpdateLastTelemetry(hasValidSessionTime, sessionTimeSec, speedMps, rpm, effectiveGear);
                return tick;
            }

            if (artifactResetDetected)
            {
                if (_samplingActive)
                {
                    tick.State = ShiftAssistLearningState.Rejected;
                    tick.LearnEndReason = "ArtifactReset";
                    tick.LearnRejectedReason = "ArtifactReset";
                }

                ResetSampling();
                PopulatePerGearStats(stack, effectiveGear, tick);
                PopulateDebugCurveFields(stack, effectiveGear, rpm, tick);
                CopyTick(tick);
                UpdateLastTelemetry(hasValidSessionTime, sessionTimeSec, speedMps, rpm, effectiveGear);
                return tick;
            }

            bool gateStrong = effectiveGear >= 1 && effectiveGear <= GearCount && throttle01 >= MinThrottleStrong && brake01 <= BrakeNoiseThreshold && speedMps >= MinMovementMps;

            if (effectiveGear != _stableGear)
            {
                _stableGear = effectiveGear;
                _stableGearSinceSec = hasValidSessionTime ? sessionTimeSec : double.NaN;
            }

            bool stableReady = effectiveGear >= 1 && effectiveGear <= GearCount && hasValidSessionTime && IsFinite(_stableGearSinceSec)
                && sessionTimeSec >= (_stableGearSinceSec + (StableGearArmMs / 1000.0));

            bool canArmForGear = true;
            if (effectiveGear >= 1 && effectiveGear <= GearCount)
            {
                var effectiveGearData = stack.Gears[effectiveGear - 1];
                effectiveGearData.TryClearReArmReset(rpm, ReArmRpmDrop);
                canArmForGear = !effectiveGearData.RequireRpmReset;
            }

            bool rpmMeetsLearnMin = rpm >= learnMinRpm;

            if (!_samplingActive)
            {
                if (gateStrong && stableReady && canArmForGear && rpmMeetsLearnMin)
                {
                    _samplingActive = true;
                    _samplingGear = effectiveGear;
                    _samplingStartSec = sessionTimeSec;
                    _samplingLearnMinRpm = learnMinRpm;
                    _samplingCaptureMinRpm = captureMinRpm;
                    _samplingLastObservedRpm = rpm;
                    _samplingPreShiftRpm = rpm;
                    _samplingPeakAccel = IsPlausibleAccel(lonAccelMps2) ? lonAccelMps2 : 0.0;
                    _samplingPeakRpm = IsPlausibleAccel(lonAccelMps2) ? rpm : 0;
                    _samplingLimiterHoldActive = false;
                    _samplingLimiterHoldStartedSec = double.NaN;
                    _samplingThrottleDipStartedSec = double.NaN;
                    _samplingBrakeActiveStartedSec = double.NaN;
                    _samplingPendingUpshiftGrace = false;
                    _samplingUpshiftGraceUntilSec = double.NaN;
                }
            }

            if (_samplingActive)
            {
                ProcessSampling(stack, tick, effectiveGear, rpm, throttle01, brake01, speedMps, hasValidSessionTime, sessionTimeSec, lonAccelMps2, redlineRpmForGear);
            }
            else
            {
                tick.WindowMs = 0;
                tick.PeakAccelMps2 = 0.0;
                tick.PeakRpm = 0;
            }

            PopulatePerGearStats(stack, effectiveGear, tick);
            PopulateDebugCurveFields(stack, effectiveGear, rpm, tick);
            CopyTick(tick);
            UpdateLastTelemetry(hasValidSessionTime, sessionTimeSec, speedMps, rpm, effectiveGear);
            return tick;
        }

        public int GetSampleCount(string gearStackId, int gear)
        {
            if (gear < 1 || gear > GearCount)
            {
                return 0;
            }

            return EnsureStack(gearStackId).Gears[gear - 1].AcceptedPullCount;
        }

        public int GetLearnedRpm(string gearStackId, int gear)
        {
            if (gear < 1 || gear > GearCount)
            {
                return 0;
            }

            return EnsureStack(gearStackId).Gears[gear - 1].LearnedRpm;
        }

        public void ResetSamplesForStack(string gearStackId)
        {
            EnsureStack(gearStackId).Reset();
        }

        private void ProcessSampling(StackRuntime stack, ShiftAssistLearningTick tick, int effectiveGear, int rpm, double throttle01, double brake01, double speedMps, bool hasValidSessionTime, double sessionTimeSec, double lonAccelMps2, int redlineRpmForGear)
        {
            tick.State = ShiftAssistLearningState.Sampling;
            tick.ActiveGear = _samplingGear;
            tick.LearnMinRpm = _samplingLearnMinRpm;
            tick.LearnCaptureMinRpm = _samplingCaptureMinRpm;

            int windowMs = 0;
            if (hasValidSessionTime && IsFinite(_samplingStartSec))
            {
                windowMs = (int)Math.Max(0.0, Math.Round((sessionTimeSec - _samplingStartSec) * 1000.0));
            }

            tick.WindowMs = windowMs;

            if (effectiveGear == _samplingGear && rpm > 0)
            {
                _samplingPreShiftRpm = rpm;
            }

            bool throttleStrongNow = throttle01 >= MinThrottleStrong;
            bool throttleHardDropNow = throttle01 < MinThrottleHardEnd;
            bool brakeAboveNoise = brake01 > BrakeNoiseThreshold;
            bool moving = speedMps >= MinMovementMps;
            bool nearRedline = redlineRpmForGear > 0 && rpm >= (int)Math.Round(redlineRpmForGear * NearRedlineRatio);

            if (throttleStrongNow)
            {
                _samplingThrottleDipStartedSec = double.NaN;
            }
            else if (hasValidSessionTime && !IsFinite(_samplingThrottleDipStartedSec))
            {
                _samplingThrottleDipStartedSec = sessionTimeSec;
            }

            bool throttleDipExpired = throttleHardDropNow;
            if (!throttleDipExpired && hasValidSessionTime && IsFinite(_samplingThrottleDipStartedSec))
            {
                throttleDipExpired = sessionTimeSec >= (_samplingThrottleDipStartedSec + (ThrottleDipGraceMs / 1000.0));
            }

            if (!brakeAboveNoise)
            {
                _samplingBrakeActiveStartedSec = double.NaN;
            }
            else if (hasValidSessionTime && !IsFinite(_samplingBrakeActiveStartedSec))
            {
                _samplingBrakeActiveStartedSec = sessionTimeSec;
            }

            bool brakeActiveLongEnough = false;
            if (hasValidSessionTime && IsFinite(_samplingBrakeActiveStartedSec))
            {
                brakeActiveLongEnough = sessionTimeSec >= (_samplingBrakeActiveStartedSec + (BrakeActiveMs / 1000.0));
            }

            bool limiterHoldNow = nearRedline && throttleStrongNow;
            if (limiterHoldNow)
            {
                if (!_samplingLimiterHoldActive)
                {
                    _samplingLimiterHoldActive = true;
                    _samplingLimiterHoldStartedSec = hasValidSessionTime ? sessionTimeSec : double.NaN;
                }
            }
            else
            {
                _samplingLimiterHoldActive = false;
                _samplingLimiterHoldStartedSec = double.NaN;
            }

            tick.LimiterHoldActive = _samplingLimiterHoldActive;
            tick.LimiterHoldMs = (hasValidSessionTime && IsFinite(_samplingLimiterHoldStartedSec))
                ? (int)Math.Max(0.0, Math.Round((sessionTimeSec - _samplingLimiterHoldStartedSec) * 1000.0))
                : 0;

            bool validForCurveSample = effectiveGear == _samplingGear && rpm >= _samplingLearnMinRpm && moving && throttleStrongNow && !brakeActiveLongEnough;
            if (validForCurveSample)
            {
                _samplingLastObservedRpm = rpm;
                if (IsPlausibleAccel(lonAccelMps2) && lonAccelMps2 > _samplingPeakAccel)
                {
                    _samplingPeakAccel = lonAccelMps2;
                    _samplingPeakRpm = rpm;
                }

                var gearData = stack.Gears[_samplingGear - 1];
                gearData.AddCurveSample(rpm, speedMps, lonAccelMps2);
            }

            tick.PeakAccelMps2 = _samplingPeakAccel;
            tick.PeakRpm = _samplingPeakRpm;

            bool gearChanged = effectiveGear != _samplingGear;
            bool endWasUpshift = gearChanged && effectiveGear == (_samplingGear + 1);

            if (_samplingPendingUpshiftGrace)
            {
                if (endWasUpshift)
                {
                    FinalizeSample(stack, tick, windowMs, "UpshiftAfterTimeoutGrace", true, false);
                    ResetSampling();
                    return;
                }

                bool graceExpired = !hasValidSessionTime || !IsFinite(_samplingUpshiftGraceUntilSec) || sessionTimeSec > _samplingUpshiftGraceUntilSec;
                if (graceExpired)
                {
                    FinalizeSample(stack, tick, windowMs, "LimiterHoldTimeout", false, true);
                    ResetSampling();
                }

                return;
            }

            bool maxDurationReached = windowMs >= MaxWindowMs;
            bool gateEnd = throttleDipExpired || brakeActiveLongEnough || !moving;

            if (gearChanged)
            {
                FinalizeSample(stack, tick, windowMs, endWasUpshift ? "GearChangedUpshift" : "GearChangedOther", endWasUpshift, false);
                ResetSampling();
                return;
            }

            if (!hasValidSessionTime)
            {
                FinalizeSample(stack, tick, windowMs, "InvalidSessionTime", false, false);
                ResetSampling();
                return;
            }

            if (maxDurationReached)
            {
                if (_samplingLimiterHoldActive && throttleStrongNow)
                {
                    return;
                }

                _samplingPendingUpshiftGrace = true;
                _samplingUpshiftGraceUntilSec = sessionTimeSec + (MaxWindowUpshiftGraceMs / 1000.0);
                return;
            }

            if (gateEnd)
            {
                if (_samplingLimiterHoldActive && throttleStrongNow)
                {
                    return;
                }

                FinalizeSample(stack, tick, windowMs, "GateEnd", false, false);
                ResetSampling();
            }
        }

        private void PopulatePerGearStats(StackRuntime stack, int effectiveGear, ShiftAssistLearningTick tick)
        {
            if (effectiveGear >= 1 && effectiveGear <= GearCount)
            {
                var g = stack.Gears[effectiveGear - 1];
                tick.SamplesForGear = g.AcceptedPullCount;
                tick.LearnedRpmForGear = g.LearnedRpm;
            }
            else
            {
                tick.SamplesForGear = 0;
                tick.LearnedRpmForGear = 0;
            }
        }

        private void PopulateDebugCurveFields(StackRuntime stack, int effectiveGear, int rpm, ShiftAssistLearningTick tick)
        {
            if (effectiveGear < 1 || effectiveGear > GearCount)
            {
                return;
            }

            var current = stack.Gears[effectiveGear - 1];
            tick.CurrentGearRatioK = current.RatioK;
            tick.CurrentGearRatioKValid = current.HasValidRatio ? 1 : 0;
            if (effectiveGear < GearCount)
            {
                var next = stack.Gears[effectiveGear];
                tick.NextGearRatioK = next.RatioK;
                tick.NextGearRatioKValid = next.HasValidRatio ? 1 : 0;
            }

            tick.CurrentBinIndex = current.GetBinIndex(rpm);
            tick.CurrentBinCount = current.GetBinCount(tick.CurrentBinIndex);
            tick.CurrentCurveAccelMps2 = current.GetCurveAccel(rpm);
            tick.CrossoverComputedRpmForGear = current.CrossoverRpm;
            tick.CrossoverInsufficientData = current.CrossoverInsufficientData ? 1 : 0;
        }

        private void FinalizeSample(StackRuntime stack, ShiftAssistLearningTick tick, int windowMs, string endReason, bool endWasUpshift, bool fromGraceTimeout)
        {
            tick.LearnEndReason = endReason;
            tick.LearnEndWasUpshift = endWasUpshift;

            var gearData = _samplingGear >= 1 && _samplingGear <= GearCount ? stack.Gears[_samplingGear - 1] : null;
            bool accepted = gearData != null && windowMs >= MinWindowMs && endWasUpshift;

            if (accepted)
            {
                int preShiftRpm = _samplingPreShiftRpm > 0 ? _samplingPreShiftRpm : _samplingLastObservedRpm;
                tick.SampleAdded = true;
                tick.LastSampleRpm = preShiftRpm;
                tick.LearnSampleRpmFinal = preShiftRpm;
                tick.LearnSampleRpmWasClamped = false;
                tick.State = ShiftAssistLearningState.Complete;

                gearData.AcceptedPullCount++;
                gearData.SetReArmRequired(_samplingPeakRpm);

                if (_samplingGear >= 1 && _samplingGear < GearCount)
                {
                    RecomputeCrossover(stack, _samplingGear, tick);
                }

                tick.SamplesForGear = gearData.AcceptedPullCount;
                tick.LearnedRpmForGear = gearData.LearnedRpm;

                if (tick.ApplyGear >= 1 && tick.ApplyGear <= GearCount && tick.ApplyRpm > 0)
                {
                    tick.ShouldApplyLearnedRpm = true;
                }
            }
            else
            {
                tick.State = ShiftAssistLearningState.Rejected;
                tick.LearnRejectedReason = fromGraceTimeout
                    ? "EndNotUpshift"
                    : (!endWasUpshift ? "EndNotUpshift" : (windowMs < MinWindowMs ? "WindowTooShort" : "Unknown"));
            }
        }

        private void RecomputeCrossover(StackRuntime stack, int sourceGear, ShiftAssistLearningTick tick)
        {
            if (sourceGear < 1 || sourceGear >= GearCount)
            {
                return;
            }

            var curr = stack.Gears[sourceGear - 1];
            var next = stack.Gears[sourceGear];
            curr.CrossoverInsufficientData = true;

            if (!curr.HasCoverage || !next.HasCoverage || !curr.HasValidRatio || !next.HasValidRatio)
            {
                return;
            }

            int minRpm = Math.Max(ComputeLearnMinRpm(sourceGear, tick.LearnRedlineRpm), AbsoluteMinLearnRpm);
            int maxRpm = tick.LearnRedlineRpm > 0 ? (tick.LearnRedlineRpm - RedlineHeadroomRpm) : (minRpm + 3000);
            if (maxRpm <= minRpm)
            {
                return;
            }

            int found = 0;
            for (int r = minRpm; r <= maxRpm; r += BinSizeRpm)
            {
                double aCurr = curr.GetCurveAccel(r);
                if (!IsFinite(aCurr))
                {
                    continue;
                }

                double r2 = r * (next.RatioK / curr.RatioK);
                double aNext = next.GetCurveAccel((int)Math.Round(r2));
                if (!IsFinite(aNext))
                {
                    continue;
                }

                if (aNext >= (aCurr + CrossoverMarginMps2))
                {
                    found = r;
                    break;
                }
            }

            tick.CrossoverCandidateRpm = found;

            if (found <= 0)
            {
                return;
            }

            curr.CrossoverInsufficientData = false;
            curr.CrossoverRpm = found;

            if (curr.StableCandidateRpm <= 0 || Math.Abs(curr.StableCandidateRpm - found) > StableCrossoverToleranceRpm)
            {
                curr.StableCandidateRpm = found;
                curr.StableCandidateTicks = 1;
                return;
            }

            curr.StableCandidateTicks++;
            if (curr.StableCandidateTicks >= StableCrossoverNeededTicks)
            {
                curr.LearnedRpm = found;
                tick.ApplyGear = sourceGear;
                tick.ApplyRpm = found;
            }
        }

        private bool DetectArtifactReset(bool hasValidSessionTime, double sessionTimeSec, int gear, int rpm, double speedMps, out string reason)
        {
            reason = string.Empty;

            if (hasValidSessionTime && IsFinite(_lastSessionTimeSec) && sessionTimeSec + 0.05 < _lastSessionTimeSec)
            {
                reason = "SessionTimeBackwards";
                return true;
            }

            if (hasValidSessionTime && IsFinite(_lastSessionTimeSec))
            {
                double dt = sessionTimeSec - _lastSessionTimeSec;
                if (dt > 0.0 && dt <= 0.30)
                {
                    if (IsFinite(speedMps) && IsFinite(_lastSpeedMps) && Math.Abs(speedMps - _lastSpeedMps) > 50.0)
                    {
                        reason = "SpeedDiscontinuity";
                        return true;
                    }

                    if (gear == _lastGear && _lastGear >= 1 && Math.Abs(rpm - _lastRpm) > 6000)
                    {
                        reason = "RpmDiscontinuity";
                        return true;
                    }
                }
            }

            return false;
        }

        private void UpdateLastTelemetry(bool hasValidSessionTime, double sessionTimeSec, double speedMps, int rpm, int gear)
        {
            if (hasValidSessionTime)
            {
                _lastSessionTimeSec = sessionTimeSec;
            }

            _lastSpeedMps = speedMps;
            _lastRpm = rpm;
            _lastGear = gear;
        }

        private int ComputeLearnMinRpm(int effectiveGear, int redlineRpmForGear)
        {
            return ComputeClampedMinRpm(effectiveGear, redlineRpmForGear, LearnTrackMinRedlineRatio);
        }

        private int ComputeCaptureMinRpm(int effectiveGear, int redlineRpmForGear)
        {
            return ComputeClampedMinRpm(effectiveGear, redlineRpmForGear, GetCaptureMinRedlineRatio(effectiveGear));
        }

        private static double GetCaptureMinRedlineRatio(int effectiveGear)
        {
            if (effectiveGear <= 2)
            {
                return 0.80;
            }

            if (effectiveGear <= 4)
            {
                return 0.75;
            }

            return 0.65;
        }

        private int ComputeClampedMinRpm(int effectiveGear, int redlineRpmForGear, double ratio)
        {
            if (effectiveGear < 1 || effectiveGear > GearCount)
            {
                return AbsoluteMinLearnRpm;
            }

            if (redlineRpmForGear <= 0)
            {
                return AbsoluteMinLearnRpm;
            }

            int scaled = (int)Math.Round(redlineRpmForGear * ratio);
            int maxAllowed = redlineRpmForGear - RedlineHeadroomRpm;
            if (maxAllowed < AbsoluteMinLearnRpm)
            {
                maxAllowed = AbsoluteMinLearnRpm;
            }

            if (scaled < AbsoluteMinLearnRpm)
            {
                return AbsoluteMinLearnRpm;
            }

            if (scaled > maxAllowed)
            {
                return maxAllowed;
            }

            return scaled;
        }

        private StackRuntime EnsureStack(string gearStackId)
        {
            string key = string.IsNullOrWhiteSpace(gearStackId) ? "Default" : gearStackId.Trim();
            if (!_stacks.TryGetValue(key, out StackRuntime runtime) || runtime == null)
            {
                runtime = new StackRuntime();
                _stacks[key] = runtime;
            }

            return runtime;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsPlausibleAccel(double accelMps2)
        {
            return IsFinite(accelMps2) && Math.Abs(accelMps2) <= MaxPlausibleAccelMps2;
        }

        private void ResetSampling()
        {
            _samplingActive = false;
            _samplingGear = 0;
            _samplingStartSec = double.NaN;
            _samplingLearnMinRpm = 0;
            _samplingCaptureMinRpm = 0;
            _samplingLastObservedRpm = 0;
            _samplingPreShiftRpm = 0;
            _samplingPeakAccel = 0.0;
            _samplingPeakRpm = 0;
            _samplingLimiterHoldActive = false;
            _samplingLimiterHoldStartedSec = double.NaN;
            _samplingThrottleDipStartedSec = double.NaN;
            _samplingBrakeActiveStartedSec = double.NaN;
            _samplingPendingUpshiftGrace = false;
            _samplingUpshiftGraceUntilSec = double.NaN;
        }

        private void CopyTick(ShiftAssistLearningTick tick)
        {
            _lastTick.State = tick.State;
            _lastTick.ActiveGear = tick.ActiveGear;
            _lastTick.WindowMs = tick.WindowMs;
            _lastTick.PeakAccelMps2 = tick.PeakAccelMps2;
            _lastTick.PeakRpm = tick.PeakRpm;
            _lastTick.LastSampleRpm = tick.LastSampleRpm;
            _lastTick.SampleAdded = tick.SampleAdded;
            _lastTick.SamplesForGear = tick.SamplesForGear;
            _lastTick.LearnedRpmForGear = tick.LearnedRpmForGear;
            _lastTick.ShouldApplyLearnedRpm = tick.ShouldApplyLearnedRpm;
            _lastTick.ApplyGear = tick.ApplyGear;
            _lastTick.ApplyRpm = tick.ApplyRpm;
            _lastTick.LearnMinRpm = tick.LearnMinRpm;
            _lastTick.LearnRedlineRpm = tick.LearnRedlineRpm;
            _lastTick.LearnCaptureMinRpm = tick.LearnCaptureMinRpm;
            _lastTick.LearnCapturedRpm = tick.LearnCapturedRpm;
            _lastTick.LearnSampleRpmFinal = tick.LearnSampleRpmFinal;
            _lastTick.LearnSampleRpmWasClamped = tick.LearnSampleRpmWasClamped;
            _lastTick.LearnEndReason = tick.LearnEndReason;
            _lastTick.LearnRejectedReason = tick.LearnRejectedReason;
            _lastTick.LearnEndWasUpshift = tick.LearnEndWasUpshift;
            _lastTick.LimiterHoldActive = tick.LimiterHoldActive;
            _lastTick.LimiterHoldMs = tick.LimiterHoldMs;
            _lastTick.ArtifactResetDetected = tick.ArtifactResetDetected;
            _lastTick.ArtifactReason = tick.ArtifactReason;
            _lastTick.CurrentGearRatioK = tick.CurrentGearRatioK;
            _lastTick.CurrentGearRatioKValid = tick.CurrentGearRatioKValid;
            _lastTick.NextGearRatioK = tick.NextGearRatioK;
            _lastTick.NextGearRatioKValid = tick.NextGearRatioKValid;
            _lastTick.CurrentBinIndex = tick.CurrentBinIndex;
            _lastTick.CurrentBinCount = tick.CurrentBinCount;
            _lastTick.CurrentCurveAccelMps2 = tick.CurrentCurveAccelMps2;
            _lastTick.CrossoverCandidateRpm = tick.CrossoverCandidateRpm;
            _lastTick.CrossoverComputedRpmForGear = tick.CrossoverComputedRpmForGear;
            _lastTick.CrossoverInsufficientData = tick.CrossoverInsufficientData;
        }

        private class StackRuntime
        {
            public readonly GearRuntime[] Gears =
            {
                new GearRuntime(), new GearRuntime(), new GearRuntime(), new GearRuntime(),
                new GearRuntime(), new GearRuntime(), new GearRuntime(), new GearRuntime()
            };

            public void Reset()
            {
                for (int i = 0; i < Gears.Length; i++)
                {
                    Gears[i].Reset();
                }
            }
        }

        private class GearRuntime
        {
            private readonly Dictionary<int, BinRuntime> _bins = new Dictionary<int, BinRuntime>();
            private readonly List<double> _ratioSamples = new List<double>();

            public int AcceptedPullCount { get; set; }
            public int LearnedRpm { get; set; }
            public int LastCapturedPeakRpm { get; private set; }
            public bool RequireRpmReset { get; private set; }
            public double RatioK { get; private set; }
            public bool HasValidRatio { get; private set; }
            public bool HasCoverage { get; private set; }
            public int CrossoverRpm { get; set; }
            public bool CrossoverInsufficientData { get; set; }
            public int StableCandidateRpm { get; set; }
            public int StableCandidateTicks { get; set; }

            public void AddCurveSample(int rpm, double speedMps, double accelMps2)
            {
                if (rpm <= 0 || !IsPlausibleAccel(accelMps2))
                {
                    return;
                }

                int idx = GetBinIndex(rpm);
                if (!_bins.TryGetValue(idx, out BinRuntime bin) || bin == null)
                {
                    bin = new BinRuntime();
                    _bins[idx] = bin;
                }

                bin.Add(accelMps2);

                if (speedMps >= MinMovementMps)
                {
                    double ratio = rpm / speedMps;
                    if (IsFinite(ratio) && ratio > 0.0)
                    {
                        _ratioSamples.Add(ratio);
                        if (_ratioSamples.Count > 400)
                        {
                            _ratioSamples.RemoveAt(0);
                        }
                    }
                }

                RefreshCoverage();
                RefreshRatio();
            }

            public int GetBinIndex(int rpm)
            {
                if (rpm <= 0)
                {
                    return 0;
                }

                return rpm / BinSizeRpm;
            }

            public int GetBinCount(int binIndex)
            {
                if (_bins.TryGetValue(binIndex, out BinRuntime bin) && bin != null)
                {
                    return bin.Count;
                }

                return 0;
            }

            public double GetCurveAccel(int rpm)
            {
                int center = GetBinIndex(rpm);
                double sum = 0.0;
                int n = 0;
                for (int i = center - 1; i <= center + 1; i++)
                {
                    if (_bins.TryGetValue(i, out BinRuntime bin) && bin != null && bin.Count >= MinBinSamples)
                    {
                        sum += bin.Median;
                        n++;
                    }
                }

                if (n <= 0)
                {
                    return double.NaN;
                }

                return sum / n;
            }

            public void Reset()
            {
                _bins.Clear();
                _ratioSamples.Clear();
                AcceptedPullCount = 0;
                LearnedRpm = 0;
                LastCapturedPeakRpm = 0;
                RequireRpmReset = false;
                RatioK = 0.0;
                HasValidRatio = false;
                HasCoverage = false;
                CrossoverRpm = 0;
                CrossoverInsufficientData = false;
                StableCandidateRpm = 0;
                StableCandidateTicks = 0;
            }

            public void SetReArmRequired(int peakRpm)
            {
                LastCapturedPeakRpm = peakRpm > 0 ? peakRpm : 0;
                RequireRpmReset = LastCapturedPeakRpm > 0;
            }

            public void TryClearReArmReset(int rpm, int reArmRpmDrop)
            {
                if (!RequireRpmReset || LastCapturedPeakRpm <= 0)
                {
                    return;
                }

                int reArmThreshold = LastCapturedPeakRpm - reArmRpmDrop;
                if (rpm <= reArmThreshold)
                {
                    RequireRpmReset = false;
                }
            }

            private void RefreshCoverage()
            {
                int withData = 0;
                foreach (var kv in _bins)
                {
                    if (kv.Value != null && kv.Value.Count >= MinBinSamples)
                    {
                        withData++;
                    }
                }

                HasCoverage = withData >= MinBinsWithData;
            }

            private void RefreshRatio()
            {
                if (_ratioSamples.Count < MinRatioSamples)
                {
                    HasValidRatio = false;
                    return;
                }

                double[] data = _ratioSamples.ToArray();
                Array.Sort(data);
                int mid = data.Length / 2;
                RatioK = (data.Length % 2) == 1
                    ? data[mid]
                    : (data[mid - 1] + data[mid]) / 2.0;
                HasValidRatio = IsFinite(RatioK) && RatioK > 0.0;
            }
        }

        private class BinRuntime
        {
            private readonly List<double> _accel = new List<double>();

            public int Count => _accel.Count;

            public double Median
            {
                get
                {
                    if (_accel.Count <= 0)
                    {
                        return double.NaN;
                    }

                    double[] data = _accel.ToArray();
                    Array.Sort(data);
                    int mid = data.Length / 2;
                    return (data.Length % 2) == 1
                        ? data[mid]
                        : (data[mid - 1] + data[mid]) / 2.0;
                }
            }

            public void Add(double accelMps2)
            {
                _accel.Add(accelMps2);
                if (_accel.Count > 60)
                {
                    _accel.RemoveAt(0);
                }
            }
        }
    }
}
