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
    }

    public class ShiftAssistLearningEngine
    {
        private const int GearCount = 8;
        private const int BufferSize = 20;
        private const int MinSamplesForApply = 5;
        private const int StableGearArmMs = 100;
        private const int MinWindowMs = 500;
        private const int MaxWindowMs = 3000;
        private const int OutlierRpmDelta = 800;
        private const int MinSaneRpm = 1000;
        private const int MaxSaneRpm = 20000;
        private const double PeakFalloffRatio = 0.97;

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

        public ShiftAssistLearningTick LastTick => _lastTick;

        public ShiftAssistLearningTick Update(bool learningEnabled, string gearStackId, int effectiveGear, int rpm, double throttle01, double brake01, double sessionTimeSec, double lonAccelMps2)
        {
            var tick = new ShiftAssistLearningTick
            {
                State = learningEnabled ? ShiftAssistLearningState.Armed : ShiftAssistLearningState.Off,
                ActiveGear = effectiveGear,
                PeakAccelMps2 = _samplingPeakAccel,
                PeakRpm = _samplingPeakRpm,
                LastSampleRpm = _lastTick.LastSampleRpm
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

            if (!_samplingActive)
            {
                if (gateStrong && stableReady)
                {
                    _samplingActive = true;
                    _samplingGear = effectiveGear;
                    _samplingStartSec = sessionTimeSec;
                    _samplingPeakAccel = IsFinite(lonAccelMps2) ? lonAccelMps2 : double.MinValue;
                    _samplingPeakRpm = IsFinite(lonAccelMps2) ? rpm : 0;
                    _samplingCapturedRpm = 0;
                    _samplingLastObservedRpm = rpm;
                }
            }

            if (_samplingActive)
            {
                tick.State = ShiftAssistLearningState.Sampling;
                tick.ActiveGear = _samplingGear;

                _samplingLastObservedRpm = rpm;
                if (IsFinite(lonAccelMps2) && lonAccelMps2 > _samplingPeakAccel)
                {
                    _samplingPeakAccel = lonAccelMps2;
                    _samplingPeakRpm = rpm;
                    _samplingCapturedRpm = 0;
                }
                else if (_samplingCapturedRpm == 0 && IsFinite(_samplingPeakAccel) && _samplingPeakAccel > 0.0 && IsFinite(lonAccelMps2))
                {
                    double falloffThreshold = _samplingPeakAccel * PeakFalloffRatio;
                    if (lonAccelMps2 <= falloffThreshold)
                    {
                        _samplingCapturedRpm = rpm;
                    }
                }

                tick.PeakAccelMps2 = _samplingPeakAccel;
                tick.PeakRpm = _samplingPeakRpm;

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
                    FinalizeSample(stack, tick, windowMs);
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

        private void FinalizeSample(StackRuntime stack, ShiftAssistLearningTick tick, int windowMs)
        {
            bool validDuration = windowMs >= MinWindowMs;
            bool validPeak = IsFinite(_samplingPeakAccel) && _samplingPeakAccel > 0.0;
            int sampleRpm = _samplingCapturedRpm > 0 ? _samplingCapturedRpm : _samplingLastObservedRpm;
            bool validRpm = sampleRpm >= MinSaneRpm && sampleRpm <= MaxSaneRpm;

            var gearData = _samplingGear >= 1 && _samplingGear <= GearCount ? stack.Gears[_samplingGear - 1] : null;
            bool outlierRejected = false;
            if (gearData != null && gearData.Count >= MinSamplesForApply && gearData.LearnedRpm > 0)
            {
                outlierRejected = Math.Abs(sampleRpm - gearData.LearnedRpm) > OutlierRpmDelta;
            }

            bool accepted = validDuration && validPeak && validRpm && !outlierRejected && gearData != null;
            if (accepted)
            {
                gearData.AddSample(sampleRpm);
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
            }
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
