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
        public bool PullAccepted { get; set; }
        public int SamplesForGear { get; set; }
        public int LearnedRpmForGear { get; set; }
        public bool ShouldApplyLearnedRpm { get; set; }
        public int ApplyGear { get; set; }
        public int ApplyRpm { get; set; }
        public int LearnMinRpm { get; set; }
        public int LearnRedlineRpm { get; set; }
        public int SamplingRedlineRpm { get; set; }
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
        public int ValidCurvePointsThisPull { get; set; }
        public int CrossoverCurrentCurveValid { get; set; }
        public int CrossoverNextCurveValid { get; set; }
        public int CrossoverCurrentKValid { get; set; }
        public int CrossoverNextKValid { get; set; }
        public int CrossoverScanMinRpm { get; set; }
        public int CrossoverScanMaxRpm { get; set; }
        public int CrossoverPredictedNextRpmInRange { get; set; }
        public string CrossoverSkipReason { get; set; }
    }

    public class ShiftAssistLearningEngine
    {
        private const int GearCount = 8;
        private const int StableGearArmMs = 200;
        private const int MinWindowMs = 250;
        private const int MaxWindowMs = 3000;
        private const int AbsoluteMinLearnRpm = 2000;
        private const double LearnTrackMinRedlineRatio = 0.70;
        private const int RedlineHeadroomRpm = 300;
        private const double MinThrottleStrong = 0.95;
        private const double BrakeNoiseEnter01 = 0.02;
        private const double BrakeNoiseExit01 = 0.01;
        private const int BrakeActiveMs = 100;
        private const double MinMovementMps = 5.0 / 3.6;
        private const double NearRedlineRatio = 0.99;
        private const int SpeedBinWidthKph = 2;
        private const int MinBinSamples = 3;
        private const int MinBinsWithData = 4;
        private const int MinCurveTotalSamples = 30;
        private const int MinRatioSamples = 12;
        private const double MinUsefulAccelMps2 = 0.25;
        private const double MaxPlausibleAccelMps2 = 30.0;
        private const int StableCrossoverToleranceRpm = 60;
        private const int StableCrossoverBufferSize = 5;
        private const int StableCrossoverMinSamples = 3;
        private const int MaxPlausibleEngineRpm = 22000;
        private const int RedlinePlausibilityPaddingRpm = 1500;
        private const int SafeLearnedRpmHeadroomRpm = 200;

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
        private double _samplingPeakAccel;
        private int _samplingPeakRpm;
        private int _samplingRedlineRpm;
        private int _samplingValidCurvePoints;
        private bool _samplingLimiterHoldActive;
        private double _samplingLimiterHoldStartedSec;
        private double _samplingBrakeActiveStartedSec;
        private bool _samplingBrakeActiveTiming;

        private double _lastSessionTimeSec = double.NaN;
        private double _lastSpeedMps = double.NaN;
        private int _lastRpm;
        private int _lastGear;

        public ShiftAssistLearningTick Update(bool learningEnabled, string gearStackId, int effectiveGear, int rpm, double throttle01, double brake01, double speedMps, double sessionTimeSec, double lonAccelMps2, int redlineRpmForGear)
        {
            int learnMinRpm = ComputeLearnMinRpm(effectiveGear, redlineRpmForGear);
            int captureMinRpm = ComputeCaptureMinRpm(effectiveGear, redlineRpmForGear);
            var tick = new ShiftAssistLearningTick
            {
                State = learningEnabled ? ShiftAssistLearningState.Armed : ShiftAssistLearningState.Off,
                ActiveGear = effectiveGear,
                LearnMinRpm = learnMinRpm,
                LearnRedlineRpm = redlineRpmForGear,
                LearnCaptureMinRpm = captureMinRpm,
                SamplingRedlineRpm = redlineRpmForGear,
                CrossoverSkipReason = string.Empty
            };

            var stack = EnsureStack(gearStackId);
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
                PopulateDebugCurveFields(stack, effectiveGear, speedMps, tick);
                CopyTick(tick);
                UpdateLastTelemetry(hasValidSessionTime, sessionTimeSec, speedMps, rpm, effectiveGear);
                return tick;
            }

            if (artifactResetDetected)
            {
                ResetSampling();
                PopulatePerGearStats(stack, effectiveGear, tick);
                PopulateDebugCurveFields(stack, effectiveGear, speedMps, tick);
                CopyTick(tick);
                UpdateLastTelemetry(hasValidSessionTime, sessionTimeSec, speedMps, rpm, effectiveGear);
                return tick;
            }

            bool gateStrong = effectiveGear >= 1 && effectiveGear <= GearCount && throttle01 >= MinThrottleStrong && brake01 <= BrakeNoiseExit01 && speedMps >= MinMovementMps;

            if (effectiveGear != _stableGear)
            {
                _stableGear = effectiveGear;
                _stableGearSinceSec = hasValidSessionTime ? sessionTimeSec : double.NaN;
            }

            bool stableReady = effectiveGear >= 1 && effectiveGear <= GearCount && hasValidSessionTime && IsFinite(_stableGearSinceSec)
                && sessionTimeSec >= (_stableGearSinceSec + (StableGearArmMs / 1000.0));

            bool limiterHoldNow = redlineRpmForGear > 0 && rpm >= (int)Math.Round(redlineRpmForGear * NearRedlineRatio) && throttle01 >= MinThrottleStrong;
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

            bool sampleAdded = false;
            if (stableReady && gateStrong && rpm >= learnMinRpm)
            {
                sampleAdded = TryAddPassiveSample(stack, effectiveGear, rpm, speedMps, lonAccelMps2, brake01, hasValidSessionTime, sessionTimeSec, redlineRpmForGear, tick);
            }

            UpdateSamplingWindow(tick, effectiveGear, rpm, gateStrong, stableReady, sampleAdded, hasValidSessionTime, sessionTimeSec, redlineRpmForGear);

            if (sampleAdded)
            {
                RecomputeCrossovers(stack, effectiveGear, tick);
            }
            else
            {
                RefreshSolveDiagnostics(stack, effectiveGear);
            }

            PopulatePerGearStats(stack, effectiveGear, tick);
            PopulateDebugCurveFields(stack, effectiveGear, speedMps, tick);
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

            return EnsureStack(gearStackId).Gears[gear - 1].UsefulSampleCount;
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
            ResetSampling();
        }

        private bool TryAddPassiveSample(StackRuntime stack, int gear, int rpm, double speedMps, double accelMps2, double brake01, bool hasValidSessionTime, double sessionTimeSec, int redlineRpmForGear, ShiftAssistLearningTick tick)
        {
            if (gear < 1 || gear > GearCount)
            {
                return false;
            }

            if (brake01 > BrakeNoiseEnter01)
            {
                if (!_samplingBrakeActiveTiming)
                {
                    _samplingBrakeActiveTiming = true;
                    _samplingBrakeActiveStartedSec = hasValidSessionTime ? sessionTimeSec : double.NaN;
                }
            }
            else if (brake01 <= BrakeNoiseExit01)
            {
                _samplingBrakeActiveTiming = false;
                _samplingBrakeActiveStartedSec = double.NaN;
            }

            bool brakeActiveLongEnough = _samplingBrakeActiveTiming
                && hasValidSessionTime
                && IsFinite(_samplingBrakeActiveStartedSec)
                && sessionTimeSec >= (_samplingBrakeActiveStartedSec + (BrakeActiveMs / 1000.0));

            if (brakeActiveLongEnough)
            {
                return false;
            }

            if (!IsPlausibleAccel(accelMps2) || accelMps2 < MinUsefulAccelMps2)
            {
                return false;
            }

            var gearData = stack.Gears[gear - 1];
            gearData.AddCurveSample(rpm, speedMps, accelMps2, redlineRpmForGear);

            tick.SampleAdded = true;
            tick.PullAccepted = true;
            tick.PeakAccelMps2 = accelMps2;
            tick.PeakRpm = rpm;
            tick.LastSampleRpm = rpm;
            tick.LearnCapturedRpm = rpm;
            tick.LearnSampleRpmFinal = rpm;
            tick.LearnSampleRpmWasClamped = false;
            return true;
        }

        private void UpdateSamplingWindow(ShiftAssistLearningTick tick, int effectiveGear, int rpm, bool gateStrong, bool stableReady, bool sampleAdded, bool hasValidSessionTime, double sessionTimeSec, int redlineRpmForGear)
        {
            if (!_samplingActive)
            {
                if (stableReady && gateStrong)
                {
                    _samplingActive = true;
                    _samplingGear = effectiveGear;
                    _samplingStartSec = sessionTimeSec;
                    _samplingLearnMinRpm = tick.LearnMinRpm;
                    _samplingCaptureMinRpm = tick.LearnCaptureMinRpm;
                    _samplingLastObservedRpm = rpm;
                    _samplingPeakAccel = 0.0;
                    _samplingPeakRpm = 0;
                    _samplingRedlineRpm = redlineRpmForGear;
                    _samplingValidCurvePoints = 0;
                }
            }

            if (!_samplingActive)
            {
                return;
            }

            tick.State = ShiftAssistLearningState.Sampling;
            tick.ActiveGear = _samplingGear;
            tick.SamplingRedlineRpm = _samplingRedlineRpm;

            if (sampleAdded)
            {
                _samplingValidCurvePoints++;
                _samplingLastObservedRpm = rpm;
                if (tick.PeakAccelMps2 > _samplingPeakAccel)
                {
                    _samplingPeakAccel = tick.PeakAccelMps2;
                    _samplingPeakRpm = rpm;
                }
            }

            if (IsPlausibleAccel(tick.PeakAccelMps2) && tick.PeakAccelMps2 > _samplingPeakAccel)
            {
                _samplingPeakAccel = tick.PeakAccelMps2;
                _samplingPeakRpm = rpm;
            }

            if (hasValidSessionTime && IsFinite(_samplingStartSec))
            {
                tick.WindowMs = (int)Math.Max(0.0, Math.Round((sessionTimeSec - _samplingStartSec) * 1000.0));
            }

            tick.ValidCurvePointsThisPull = _samplingValidCurvePoints;
            tick.PeakAccelMps2 = _samplingPeakAccel;
            tick.PeakRpm = _samplingPeakRpm;

            bool gearChanged = effectiveGear != _samplingGear;
            bool maxWindow = tick.WindowMs >= MaxWindowMs;
            bool gateLost = !gateStrong;

            if (gearChanged || maxWindow || gateLost)
            {
                bool endedAsUpshift = gearChanged && effectiveGear == (_samplingGear + 1);
                bool complete = endedAsUpshift && tick.WindowMs >= MinWindowMs && _samplingValidCurvePoints > 0;
                tick.State = complete ? ShiftAssistLearningState.Complete : ShiftAssistLearningState.Rejected;
                tick.LearnEndWasUpshift = endedAsUpshift;
                tick.LearnEndReason = gearChanged ? (endedAsUpshift ? "GearChangedUpshift" : "GearChangedOther") : (maxWindow ? "WindowElapsed" : "GateLost");
                if (!complete)
                {
                    tick.LearnRejectedReason = "Collecting";
                }

                ResetSampling();
            }
        }

        private void RefreshSolveDiagnostics(StackRuntime stack, int activeGear)
        {
            for (int gear = 1; gear < GearCount; gear++)
            {
                RecomputeCrossoverForGear(stack, gear, false, out int _, out bool _);
            }

            if (activeGear >= 1 && activeGear <= GearCount)
            {
                var g = stack.Gears[activeGear - 1];
                if (g.LastCrossoverSkipReason == null)
                {
                    g.LastCrossoverSkipReason = string.Empty;
                }
            }
        }

        private void PopulatePerGearStats(StackRuntime stack, int effectiveGear, ShiftAssistLearningTick tick)
        {
            if (effectiveGear >= 1 && effectiveGear <= GearCount)
            {
                var g = stack.Gears[effectiveGear - 1];
                tick.SamplesForGear = g.UsefulSampleCount;
                tick.LearnedRpmForGear = g.LearnedRpm;
            }
            else
            {
                tick.SamplesForGear = 0;
                tick.LearnedRpmForGear = 0;
            }
        }

        private void PopulateDebugCurveFields(StackRuntime stack, int effectiveGear, double speedMps, ShiftAssistLearningTick tick)
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

            int speedKph = ToSpeedKph(speedMps);
            tick.CurrentBinIndex = current.GetBinIndex(speedKph);
            tick.CurrentBinCount = current.GetBinCount(tick.CurrentBinIndex);
            tick.CurrentCurveAccelMps2 = current.GetCurveAccelAtSpeed(speedKph);
            tick.CrossoverCandidateRpm = current.LastCrossoverCandidateRpm;
            tick.CrossoverComputedRpmForGear = current.CrossoverRpm;
            tick.CrossoverInsufficientData = current.CrossoverInsufficientData ? 1 : 0;
            tick.CrossoverCurrentCurveValid = current.LastCurrentCurveValid ? 1 : 0;
            tick.CrossoverNextCurveValid = current.LastNextCurveValid ? 1 : 0;
            tick.CrossoverCurrentKValid = current.LastCurrentKValid ? 1 : 0;
            tick.CrossoverNextKValid = current.LastNextKValid ? 1 : 0;
            tick.CrossoverScanMinRpm = current.LastScanMinRpm;
            tick.CrossoverScanMaxRpm = current.LastScanMaxRpm;
            tick.CrossoverPredictedNextRpmInRange = current.LastPredictedNextRpmInRange ? 1 : 0;
            tick.CrossoverSkipReason = current.LastCrossoverSkipReason;
        }

        private void RecomputeCrossovers(StackRuntime stack, int triggerGear, ShiftAssistLearningTick tick)
        {
            int applyGear = 0;
            int applyRpm = 0;

            for (int sourceGear = 1; sourceGear < GearCount; sourceGear++)
            {
                bool stableSolveExists = RecomputeCrossoverForGear(stack, sourceGear, true, out int stableLearnedRpm, out bool _);
                if (!stableSolveExists || stableLearnedRpm <= 0)
                {
                    continue;
                }

                if (applyGear == 0 || sourceGear == triggerGear || sourceGear == (triggerGear - 1))
                {
                    applyGear = sourceGear;
                    applyRpm = stableLearnedRpm;
                }
            }

            if (applyGear > 0 && applyRpm > 0)
            {
                tick.ShouldApplyLearnedRpm = true;
                tick.ApplyGear = applyGear;
                tick.ApplyRpm = applyRpm;
            }
        }

        private bool RecomputeCrossoverForGear(StackRuntime stack, int sourceGear, bool publishLearned, out int stableLearnedRpm, out bool learnedRpmChanged)
        {
            stableLearnedRpm = 0;
            learnedRpmChanged = false;
            if (sourceGear < 1 || sourceGear >= GearCount)
            {
                return false;
            }

            if (sourceGear == 1)
            {
                var firstGear = stack.Gears[0];
                firstGear.CrossoverInsufficientData = true;
                firstGear.LastCrossoverCandidateRpm = 0;
                firstGear.LastCurrentCurveValid = firstGear.HasCoverage;
                firstGear.LastNextCurveValid = stack.Gears[1].HasCoverage;
                firstGear.LastCurrentKValid = firstGear.HasValidRatio;
                firstGear.LastNextKValid = stack.Gears[1].HasValidRatio;
                firstGear.LastScanMinRpm = 0;
                firstGear.LastScanMaxRpm = 0;
                firstGear.LastPredictedNextRpmInRange = false;
                firstGear.LastCrossoverSkipReason = "Gear1Excluded";
                return false;
            }

            var curr = stack.Gears[sourceGear - 1];
            var next = stack.Gears[sourceGear];

            curr.CrossoverInsufficientData = true;
            curr.LastCrossoverCandidateRpm = 0;
            curr.LastCurrentCurveValid = curr.HasCoverage;
            curr.LastNextCurveValid = next.HasCoverage;
            curr.LastCurrentKValid = curr.HasValidRatio;
            curr.LastNextKValid = next.HasValidRatio;
            curr.LastScanMinRpm = 0;
            curr.LastScanMaxRpm = 0;
            curr.LastPredictedNextRpmInRange = false;
            curr.LastCrossoverSkipReason = string.Empty;

            if (!curr.LastCurrentCurveValid || !curr.LastNextCurveValid || !curr.LastCurrentKValid || !curr.LastNextKValid)
            {
                curr.LastCrossoverSkipReason = "AwaitCurveOrRatio";
                return false;
            }

            if (!curr.TryGetUsableSpeedRange(out int minSpeedKph, out int maxSpeedKph)
                || !next.TryGetUsableSpeedRange(out int nextMinSpeedKph, out int nextMaxSpeedKph))
            {
                curr.LastCrossoverSkipReason = "UsableRangeUnavailable";
                return false;
            }

            int minScanSpeedKph = minSpeedKph > nextMinSpeedKph ? minSpeedKph : nextMinSpeedKph;
            int maxScanSpeedKph = maxSpeedKph < nextMaxSpeedKph ? maxSpeedKph : nextMaxSpeedKph;
            if (maxScanSpeedKph <= minScanSpeedKph)
            {
                curr.LastCrossoverSkipReason = "NoSpeedOverlap";
                return false;
            }

            int overlapSpanKph = maxScanSpeedKph - minScanSpeedKph;
            int scanStartSpeedKph = minScanSpeedKph + (int)Math.Ceiling(overlapSpanKph * 0.20);
            if (scanStartSpeedKph > maxScanSpeedKph)
            {
                curr.LastCrossoverSkipReason = "NoSpeedOverlap";
                return false;
            }

            int minScanRpm = ClampLearnedRpmToSafeCeiling(curr, (int)Math.Round(curr.RatioK * ToMps(scanStartSpeedKph)));
            int maxScanRpm = ClampLearnedRpmToSafeCeiling(curr, (int)Math.Round(curr.RatioK * ToMps(maxScanSpeedKph)));
            curr.LastScanMinRpm = minScanRpm;
            curr.LastScanMaxRpm = maxScanRpm;

            int foundSpeedKph = 0;
            for (int speedKph = scanStartSpeedKph; speedKph <= maxScanSpeedKph; speedKph += 1)
            {
                double aCurr = curr.GetCurveAccelAtSpeed(speedKph);
                double aNext = next.GetCurveAccelAtSpeed(speedKph);
                if (!IsFinite(aCurr) || !IsFinite(aNext))
                {
                    continue;
                }

                curr.LastPredictedNextRpmInRange = true;
                if (aNext >= aCurr)
                {
                    foundSpeedKph = speedKph;
                    break;
                }
            }

            if (foundSpeedKph <= 0)
            {
                curr.LastCrossoverSkipReason = curr.LastPredictedNextRpmInRange ? "NoCrossoverYet" : "MissingOverlappingBins";
                return false;
            }

            int candidateRpm = (int)Math.Round(curr.RatioK * ToMps(foundSpeedKph));
            candidateRpm = ClampLearnedRpmToSafeCeiling(curr, candidateRpm);
            if (candidateRpm <= 0)
            {
                curr.LastCrossoverSkipReason = "CandidateOutOfBounds";
                return false;
            }

            curr.LastCrossoverCandidateRpm = candidateRpm;
            curr.CrossoverInsufficientData = false;
            curr.CrossoverRpm = candidateRpm;
            curr.PushCrossoverCandidate(candidateRpm, StableCrossoverBufferSize);

            if (!curr.TryGetStableLearnedRpm(StableCrossoverToleranceRpm, StableCrossoverMinSamples, out int solvedRpm))
            {
                curr.LastCrossoverSkipReason = "AwaitingStability";
                return false;
            }

            solvedRpm = ClampLearnedRpmToSafeCeiling(curr, solvedRpm);
            if (solvedRpm <= 0)
            {
                curr.LastCrossoverSkipReason = "StableOutOfBounds";
                return false;
            }

            stableLearnedRpm = solvedRpm;
            curr.LastCrossoverSkipReason = "StableSolved";

            if (publishLearned)
            {
                learnedRpmChanged = curr.PublishLearnedRpm(solvedRpm);
            }

            return true;
        }

        private int ClampLearnedRpmToSafeCeiling(GearRuntime gearData, int rpm)
        {
            if (gearData == null || rpm <= 0)
            {
                return 0;
            }

            int safeMax = gearData.GetCrossoverUpperRpmCeiling(SafeLearnedRpmHeadroomRpm);
            if (safeMax > 0 && rpm > safeMax)
            {
                rpm = safeMax;
            }

            return rpm < AbsoluteMinLearnRpm ? 0 : rpm;
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

        private static int ToSpeedKph(double speedMps)
        {
            if (!IsFinite(speedMps) || speedMps < 0.0)
            {
                return 0;
            }

            return (int)Math.Round(speedMps * 3.6);
        }

        private static double ToMps(int speedKph)
        {
            return speedKph / 3.6;
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
            _samplingPeakAccel = 0.0;
            _samplingPeakRpm = 0;
            _samplingRedlineRpm = 0;
            _samplingValidCurvePoints = 0;
            _samplingLimiterHoldActive = false;
            _samplingLimiterHoldStartedSec = double.NaN;
            _samplingBrakeActiveStartedSec = double.NaN;
            _samplingBrakeActiveTiming = false;
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
            _lastTick.PullAccepted = tick.PullAccepted;
            _lastTick.SamplesForGear = tick.SamplesForGear;
            _lastTick.LearnedRpmForGear = tick.LearnedRpmForGear;
            _lastTick.ShouldApplyLearnedRpm = tick.ShouldApplyLearnedRpm;
            _lastTick.ApplyGear = tick.ApplyGear;
            _lastTick.ApplyRpm = tick.ApplyRpm;
            _lastTick.LearnMinRpm = tick.LearnMinRpm;
            _lastTick.LearnRedlineRpm = tick.LearnRedlineRpm;
            _lastTick.SamplingRedlineRpm = tick.SamplingRedlineRpm;
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
            _lastTick.ValidCurvePointsThisPull = tick.ValidCurvePointsThisPull;
            _lastTick.CrossoverCurrentCurveValid = tick.CrossoverCurrentCurveValid;
            _lastTick.CrossoverNextCurveValid = tick.CrossoverNextCurveValid;
            _lastTick.CrossoverCurrentKValid = tick.CrossoverCurrentKValid;
            _lastTick.CrossoverNextKValid = tick.CrossoverNextKValid;
            _lastTick.CrossoverScanMinRpm = tick.CrossoverScanMinRpm;
            _lastTick.CrossoverScanMaxRpm = tick.CrossoverScanMaxRpm;
            _lastTick.CrossoverPredictedNextRpmInRange = tick.CrossoverPredictedNextRpmInRange;
            _lastTick.CrossoverSkipReason = tick.CrossoverSkipReason;
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
            private readonly List<int> _recentCrossoverCandidates = new List<int>();

            public int UsefulSampleCount { get; private set; }
            public int LearnedRpm { get; private set; }
            public double RatioK { get; private set; }
            public bool HasValidRatio { get; private set; }
            public bool HasCoverage { get; private set; }
            public int SourceGearRedlineRpm { get; private set; }
            public int CrossoverRpm { get; set; }
            public bool CrossoverInsufficientData { get; set; }
            public int LastCrossoverCandidateRpm { get; set; }
            public bool LastCurrentCurveValid { get; set; }
            public bool LastNextCurveValid { get; set; }
            public bool LastCurrentKValid { get; set; }
            public bool LastNextKValid { get; set; }
            public int LastScanMinRpm { get; set; }
            public int LastScanMaxRpm { get; set; }
            public bool LastPredictedNextRpmInRange { get; set; }
            public string LastCrossoverSkipReason { get; set; }

            public void AddCurveSample(int rpm, double speedMps, double accelMps2, int redlineRpmForGear)
            {
                if (rpm <= 0 || rpm > MaxPlausibleEngineRpm || !IsPlausibleAccel(accelMps2) || accelMps2 < MinUsefulAccelMps2)
                {
                    return;
                }

                if (!IsFinite(speedMps) || speedMps < MinMovementMps)
                {
                    return;
                }

                if (redlineRpmForGear > 0)
                {
                    SourceGearRedlineRpm = redlineRpmForGear;
                }

                if (!IsRpmPlausibleForBounds(rpm, redlineRpmForGear))
                {
                    return;
                }

                int speedKph = ToSpeedKph(speedMps);
                int idx = GetBinIndex(speedKph);
                if (!_bins.TryGetValue(idx, out BinRuntime bin) || bin == null)
                {
                    bin = new BinRuntime();
                    _bins[idx] = bin;
                }

                bin.Add(accelMps2);
                UsefulSampleCount++;

                double ratio = rpm / speedMps;
                if (IsFinite(ratio) && ratio > 0.0)
                {
                    _ratioSamples.Add(ratio);
                    if (_ratioSamples.Count > 400)
                    {
                        _ratioSamples.RemoveAt(0);
                    }
                }

                RefreshCoverage();
                RefreshRatio();
            }

            public int GetBinIndex(int speedKph)
            {
                if (speedKph <= 0)
                {
                    return 0;
                }

                return speedKph / SpeedBinWidthKph;
            }

            public int GetBinCount(int binIndex)
            {
                if (_bins.TryGetValue(binIndex, out BinRuntime bin) && bin != null)
                {
                    return bin.Count;
                }

                return 0;
            }

            public double GetCurveAccelAtSpeed(int speedKph)
            {
                int center = GetBinIndex(speedKph);
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

            public int GetCrossoverUpperRpmCeiling(int headroomRpm)
            {
                if (SourceGearRedlineRpm <= 0)
                {
                    return 0;
                }

                int ceiling = SourceGearRedlineRpm - headroomRpm;
                return ceiling > AbsoluteMinLearnRpm ? ceiling : AbsoluteMinLearnRpm;
            }

            public bool TryGetUsableSpeedRange(out int minSpeedKph, out int maxSpeedKph)
            {
                minSpeedKph = 0;
                maxSpeedKph = 0;
                int minIndex = int.MaxValue;
                int maxIndex = int.MinValue;
                int binsWithData = 0;

                foreach (var kv in _bins)
                {
                    if (kv.Value == null || kv.Value.Count < MinBinSamples)
                    {
                        continue;
                    }

                    binsWithData++;
                    if (kv.Key < minIndex)
                    {
                        minIndex = kv.Key;
                    }

                    if (kv.Key > maxIndex)
                    {
                        maxIndex = kv.Key;
                    }
                }

                if (binsWithData < MinBinsWithData || UsefulSampleCount < MinCurveTotalSamples || minIndex == int.MaxValue)
                {
                    return false;
                }

                minSpeedKph = minIndex * SpeedBinWidthKph;
                maxSpeedKph = (maxIndex + 1) * SpeedBinWidthKph;
                return maxSpeedKph > minSpeedKph;
            }

            public void PushCrossoverCandidate(int rpm, int maxSamples)
            {
                if (rpm <= 0)
                {
                    return;
                }

                _recentCrossoverCandidates.Add(rpm);
                while (_recentCrossoverCandidates.Count > maxSamples)
                {
                    _recentCrossoverCandidates.RemoveAt(0);
                }
            }

            public bool TryGetStableLearnedRpm(int toleranceRpm, int minSamples, out int learnedRpm)
            {
                learnedRpm = 0;
                if (_recentCrossoverCandidates.Count < minSamples)
                {
                    return false;
                }

                int min = int.MaxValue;
                int max = int.MinValue;
                for (int i = 0; i < _recentCrossoverCandidates.Count; i++)
                {
                    int sample = _recentCrossoverCandidates[i];
                    if (sample < min)
                    {
                        min = sample;
                    }

                    if (sample > max)
                    {
                        max = sample;
                    }
                }

                if ((max - min) > toleranceRpm)
                {
                    return false;
                }

                int[] sorted = _recentCrossoverCandidates.ToArray();
                Array.Sort(sorted);
                int mid = sorted.Length / 2;
                learnedRpm = (sorted.Length % 2) == 1
                    ? sorted[mid]
                    : (int)Math.Round((sorted[mid - 1] + sorted[mid]) / 2.0);
                return learnedRpm > 0;
            }

            public bool PublishLearnedRpm(int rpm)
            {
                if (rpm <= 0)
                {
                    return false;
                }

                if (LearnedRpm == rpm)
                {
                    return false;
                }

                LearnedRpm = rpm;
                return true;
            }


            public void Reset()
            {
                _bins.Clear();
                _ratioSamples.Clear();
                _recentCrossoverCandidates.Clear();
                UsefulSampleCount = 0;
                LearnedRpm = 0;
                RatioK = 0.0;
                HasValidRatio = false;
                HasCoverage = false;
                SourceGearRedlineRpm = 0;
                CrossoverRpm = 0;
                CrossoverInsufficientData = false;
                LastCrossoverCandidateRpm = 0;
                LastCurrentCurveValid = false;
                LastNextCurveValid = false;
                LastCurrentKValid = false;
                LastNextKValid = false;
                LastScanMinRpm = 0;
                LastScanMaxRpm = 0;
                LastPredictedNextRpmInRange = false;
                LastCrossoverSkipReason = string.Empty;
            }

            private bool IsRpmPlausibleForBounds(int rpm, int redlineRpmForGear)
            {
                if (rpm <= 0 || rpm > MaxPlausibleEngineRpm)
                {
                    return false;
                }

                int referenceRedline = redlineRpmForGear > 0 ? redlineRpmForGear : SourceGearRedlineRpm;
                if (referenceRedline > 0 && rpm > (referenceRedline + RedlinePlausibilityPaddingRpm))
                {
                    return false;
                }

                return true;
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

                HasCoverage = withData >= MinBinsWithData && UsefulSampleCount >= MinCurveTotalSamples;
            }

            private void RefreshRatio()
            {
                if (_ratioSamples.Count < MinRatioSamples)
                {
                    HasValidRatio = false;
                    RatioK = 0.0;
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
                if (_accel.Count > 80)
                {
                    _accel.RemoveAt(0);
                }
            }
        }
    }
}
