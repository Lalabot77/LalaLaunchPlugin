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
        public int LearnCaptureMinRpm { get; set; }
        public int LearnCapturedRpm { get; set; }
        public string LearnEndReason { get; set; }
        public string LearnRejectedReason { get; set; }
        public bool LearnEndWasUpshift { get; set; }
    }

    public class ShiftAssistLearningEngine
    {
        private const int GearCount = 8;
        private const int BufferSize = 20;
        private const int MinSamplesForApply = 3;
        private const int StableGearArmMs = 200;
        private const int MinWindowMs = 500;
        private const int MaxWindowMs = 3000;
        private const int OutlierRpmDelta = 800;
        private const int MinSaneRpm = 1000;
        private const int MaxSaneRpm = 20000;
        private const double PeakFalloffRatio = 0.97;
        private const int ReArmRpmDrop = 500;
        private const int AbsoluteMinLearnRpm = 2000;
        private const double LearnTrackMinRedlineRatio = 0.70;
        private const double LearnCaptureMinRedlineRatio = 0.80;
        private const int RedlineHeadroomRpm = 300;
        private const int MinTicksSincePeakForCapture = 4;
        private const int DelayedRiseRpmAbovePeak = 250;

        private readonly Dictionary<string, StackRuntime> _stacks = new Dictionary<string, StackRuntime>(StringComparer.OrdinalIgnoreCase);
        private readonly ShiftAssistLearningTick _lastTick = new ShiftAssistLearningTick { State = ShiftAssistLearningState.Off };

        private int _stableGear;
        private double _stableGearSinceSec = double.NaN;

        private bool _samplingActive;
        private int _samplingGear;
        private double _samplingStartSec;
        private double _samplingPeakAccel;
        private int _samplingPeakRpm;
        private int _samplingCapturedRpm;
        private int _samplingLastObservedRpm;
        private int _samplingPreShiftRpm;
        private int _samplingTicksSincePeak;
        private int _samplingFalloffConsecutive;
        private int _samplingLearnMinRpm;
        private int _samplingCaptureMinRpm;

        public ShiftAssistLearningTick LastTick => _lastTick;

        public ShiftAssistLearningTick Update(bool learningEnabled, string gearStackId, int effectiveGear, int rpm, double throttle01, double brake01, double sessionTimeSec, double lonAccelMps2, int redlineRpmForGear)
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
                LearnCaptureMinRpm = captureMinRpm,
                LearnCapturedRpm = _samplingCapturedRpm,
                LearnEndWasUpshift = false
            };

            string stackKey = string.IsNullOrWhiteSpace(gearStackId) ? "Default" : gearStackId.Trim();
            var stack = EnsureStack(stackKey);
            bool hasValidSessionTime = IsFinite(sessionTimeSec) && sessionTimeSec >= 0.0;

            if (!learningEnabled)
            {
                ResetSampling();
                _stableGear = 0;
                _stableGearSinceSec = double.NaN;
                PopulatePerGearStats(stack, effectiveGear, tick);
                CopyTick(tick);
                return tick;
            }

            bool gateStrong = effectiveGear >= 1 && effectiveGear <= GearCount && throttle01 >= 0.95 && brake01 <= 0.05;
            bool gateEnd = !gateStrong || throttle01 < 0.90 || brake01 > 0.05;

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
                    _samplingPeakAccel = IsFinite(lonAccelMps2) ? lonAccelMps2 : double.MinValue;
                    _samplingPeakRpm = IsFinite(lonAccelMps2) ? rpm : 0;
                    _samplingCapturedRpm = 0;
                    _samplingLastObservedRpm = rpm;
                    _samplingPreShiftRpm = rpm;
                    _samplingTicksSincePeak = 0;
                    _samplingFalloffConsecutive = 0;
                    _samplingLearnMinRpm = learnMinRpm;
                    _samplingCaptureMinRpm = captureMinRpm;
                }
            }

            if (_samplingActive)
            {
                tick.State = ShiftAssistLearningState.Sampling;
                tick.ActiveGear = _samplingGear;

                tick.LearnMinRpm = _samplingLearnMinRpm;
                tick.LearnCaptureMinRpm = _samplingCaptureMinRpm;
                bool samplingRpmReady = rpm >= _samplingLearnMinRpm;
                if (effectiveGear == _samplingGear && rpm > 0)
                {
                    _samplingPreShiftRpm = rpm;
                }

                if (samplingRpmReady)
                {
                    _samplingLastObservedRpm = rpm;
                    if (IsFinite(lonAccelMps2) && lonAccelMps2 > _samplingPeakAccel)
                    {
                        _samplingPeakAccel = lonAccelMps2;
                        _samplingPeakRpm = rpm;
                        _samplingCapturedRpm = 0;
                        _samplingTicksSincePeak = 0;
                        _samplingFalloffConsecutive = 0;
                    }
                    else
                    {
                        _samplingTicksSincePeak++;

                        if (rpm >= _samplingCaptureMinRpm && _samplingCapturedRpm == 0 && IsFinite(_samplingPeakAccel) && _samplingPeakAccel > 0.0 && IsFinite(lonAccelMps2))
                        {
                            double falloffThreshold = _samplingPeakAccel * PeakFalloffRatio;
                            bool meetsFalloff = lonAccelMps2 <= falloffThreshold;
                            if (meetsFalloff)
                            {
                                _samplingFalloffConsecutive++;
                                bool canCapture = _samplingTicksSincePeak >= MinTicksSincePeakForCapture;
                                bool twoConsecutiveFalloffTicks = _samplingFalloffConsecutive >= 2;
                                bool delayedRiseGuard = canCapture && rpm >= (_samplingPeakRpm + DelayedRiseRpmAbovePeak);
                                if (canCapture && (twoConsecutiveFalloffTicks || delayedRiseGuard))
                                {
                                    _samplingCapturedRpm = rpm;
                                }
                            }
                            else
                            {
                                _samplingFalloffConsecutive = 0;
                            }
                        }
                    }
                }

                tick.PeakAccelMps2 = _samplingPeakAccel;
                tick.PeakRpm = _samplingPeakRpm;
                tick.LearnCapturedRpm = _samplingCapturedRpm;

                int windowMs = 0;
                if (hasValidSessionTime && IsFinite(_samplingStartSec))
                {
                    windowMs = (int)Math.Max(0.0, Math.Round((sessionTimeSec - _samplingStartSec) * 1000.0));
                }

                tick.WindowMs = windowMs;

                bool maxDurationReached = windowMs >= MaxWindowMs;
                bool gearChanged = effectiveGear != _samplingGear;
                bool shouldEnd = !hasValidSessionTime || gateEnd || gearChanged || maxDurationReached;
                if (shouldEnd)
                {
                    bool endWasUpshift = gearChanged && effectiveGear == (_samplingGear + 1);
                    string endReason = !hasValidSessionTime ? "InvalidSessionTime" :
                        (gearChanged ? (endWasUpshift ? "GearChangedUpshift" : "GearChangedOther") :
                        (gateEnd ? "GateEnd" : "MaxWindow"));
                    FinalizeSample(stack, tick, windowMs, endReason, endWasUpshift);
                    ResetSampling();
                }
            }
            else
            {
                tick.WindowMs = 0;
                tick.PeakAccelMps2 = 0.0;
                tick.PeakRpm = 0;
            }

            PopulatePerGearStats(stack, effectiveGear, tick);
            CopyTick(tick);
            return tick;
        }

        public int GetSampleCount(string gearStackId, int gear)
        {
            if (gear < 1 || gear > GearCount)
            {
                return 0;
            }

            return EnsureStack(gearStackId).Gears[gear - 1].Count;
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

        private void PopulatePerGearStats(StackRuntime stack, int effectiveGear, ShiftAssistLearningTick tick)
        {
            if (effectiveGear >= 1 && effectiveGear <= GearCount)
            {
                var g = stack.Gears[effectiveGear - 1];
                tick.SamplesForGear = g.Count;
                tick.LearnedRpmForGear = g.LearnedRpm;
            }
            else
            {
                tick.SamplesForGear = 0;
                tick.LearnedRpmForGear = 0;
            }
        }

        private void FinalizeSample(StackRuntime stack, ShiftAssistLearningTick tick, int windowMs, string endReason, bool endWasUpshift)
        {
            bool validDuration = windowMs >= MinWindowMs;
            bool validPeak = IsFinite(_samplingPeakAccel) && _samplingPeakAccel > 0.0;
            bool hasCapturedInWindow = _samplingCapturedRpm > 0 && _samplingCapturedRpm >= _samplingCaptureMinRpm;
            int preShiftRpm = _samplingPreShiftRpm > 0 ? _samplingPreShiftRpm : _samplingLastObservedRpm;
            int sampleRpm = hasCapturedInWindow ? _samplingCapturedRpm : preShiftRpm;
            bool validRpm = sampleRpm >= MinSaneRpm && sampleRpm <= MaxSaneRpm;

            var gearData = _samplingGear >= 1 && _samplingGear <= GearCount ? stack.Gears[_samplingGear - 1] : null;
            bool outlierRejected = false;
            if (gearData != null && gearData.Count >= MinSamplesForApply && gearData.LearnedRpm > 0)
            {
                int delta = gearData.Count >= 10 ? 400 : OutlierRpmDelta;
                outlierRejected = Math.Abs(sampleRpm - gearData.LearnedRpm) > delta;
            }

            bool accepted = endWasUpshift && validDuration && validPeak && validRpm && !outlierRejected && gearData != null;
            tick.LearnCapturedRpm = _samplingCapturedRpm;
            tick.LearnEndReason = endReason;
            tick.LearnEndWasUpshift = endWasUpshift;
            if (accepted)
            {
                gearData.AddSample(sampleRpm);
                gearData.SetReArmRequired(_samplingPeakRpm);
                tick.SampleAdded = true;
                tick.LastSampleRpm = sampleRpm;
                tick.State = ShiftAssistLearningState.Complete;
                tick.SamplesForGear = gearData.Count;
                tick.LearnedRpmForGear = gearData.LearnedRpm;

                if (gearData.Count >= MinSamplesForApply && gearData.LearnedRpm > 0)
                {
                    tick.ShouldApplyLearnedRpm = true;
                    tick.ApplyGear = _samplingGear;
                    tick.ApplyRpm = gearData.LearnedRpm;
                }
            }
            else
            {
                tick.State = ShiftAssistLearningState.Rejected;
                tick.LearnRejectedReason = !validDuration ? "WindowTooShort" :
                    (!endWasUpshift ? "EndNotUpshift" :
                    (!validPeak ? "InvalidPeak" :
                    (!validRpm ? "InvalidRpm" :
                    (outlierRejected ? "Outlier" :
                    (gearData == null ? "InvalidGear" : "Unknown")))));
            }
        }

        private int ComputeLearnMinRpm(int effectiveGear, int redlineRpmForGear)
        {
            return ComputeClampedMinRpm(effectiveGear, redlineRpmForGear, LearnTrackMinRedlineRatio);
        }

        private int ComputeCaptureMinRpm(int effectiveGear, int redlineRpmForGear)
        {
            return ComputeClampedMinRpm(effectiveGear, redlineRpmForGear, LearnCaptureMinRedlineRatio);
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
            StackRuntime runtime;
            if (!_stacks.TryGetValue(key, out runtime) || runtime == null)
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

        private void ResetSampling()
        {
            _samplingActive = false;
            _samplingGear = 0;
            _samplingStartSec = double.NaN;
            _samplingPeakAccel = 0.0;
            _samplingPeakRpm = 0;
            _samplingCapturedRpm = 0;
            _samplingLastObservedRpm = 0;
            _samplingPreShiftRpm = 0;
            _samplingTicksSincePeak = 0;
            _samplingFalloffConsecutive = 0;
            _samplingLearnMinRpm = 0;
            _samplingCaptureMinRpm = 0;
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
            _lastTick.LearnCaptureMinRpm = tick.LearnCaptureMinRpm;
            _lastTick.LearnCapturedRpm = tick.LearnCapturedRpm;
            _lastTick.LearnEndReason = tick.LearnEndReason;
            _lastTick.LearnRejectedReason = tick.LearnRejectedReason;
            _lastTick.LearnEndWasUpshift = tick.LearnEndWasUpshift;
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
            private readonly int[] _samples = new int[BufferSize];
            private int _next;

            public int Count { get; private set; }
            public int LearnedRpm { get; private set; }
            public int LastCapturedPeakRpm { get; private set; }
            public bool RequireRpmReset { get; private set; }

            public void AddSample(int rpm)
            {
                _samples[_next] = rpm;
                _next = (_next + 1) % BufferSize;
                if (Count < BufferSize)
                {
                    Count++;
                }

                LearnedRpm = ComputeMedian();
            }

            public void Reset()
            {
                Array.Clear(_samples, 0, _samples.Length);
                _next = 0;
                Count = 0;
                LearnedRpm = 0;
                LastCapturedPeakRpm = 0;
                RequireRpmReset = false;
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

            private int ComputeMedian()
            {
                if (Count <= 0)
                {
                    return 0;
                }

                var data = new int[Count];
                int start = (Count == BufferSize) ? _next : 0;
                for (int i = 0; i < Count; i++)
                {
                    int idx = (start + i) % BufferSize;
                    data[i] = _samples[idx];
                }

                Array.Sort(data);
                int mid = Count / 2;
                if ((Count % 2) == 1)
                {
                    return data[mid];
                }

                return (int)Math.Round((data[mid - 1] + data[mid]) / 2.0);
            }
        }
    }
}
