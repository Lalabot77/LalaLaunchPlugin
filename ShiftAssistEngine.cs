using System;

namespace LaunchPlugin
{
    internal enum ShiftAssistState
    {
        Off = 0,
        On = 1,
        NoData = 2,
        Cooldown = 3
    }

    internal sealed class ShiftAssistEngine
    {
        private const double MinRpmRateDtSec = 0.01;
        private const double MaxRpmRateDtSec = 0.20;
        private const int MaxRpmRatePerSec = 8000;
        private const int MaxPredictiveEarlyRpm = 800;
        private const int MinEffectiveTargetRpmFloor = 1000;
        private const int EffectiveTargetFloorOffset = 1500;
        private const int AudioOutputCompMs = 20;
        private const int UrgentMarginRpm = 200;
        private const double RpmRateSmoothingPrevWeight = 0.70;
        private const double RpmRateSmoothingCurrentWeight = 0.30;
        private const int MinUrgentAboveTargetRpm = 400;

        private readonly Func<DateTime> _utcNow;
        private int _lastGear;
        private bool _wasAboveTarget;
        private DateTime _lastBeepAtUtc = DateTime.MinValue;
        private int _lastRpm;
        private DateTime _lastSampleAtUtc = DateTime.MinValue;
        private bool _suppressUntilBelowReset;
        private bool _suppressAfterUpshiftUntilBelowReset;
        private double _smoothedRpmRate;
        private bool _hasSmoothedRpmRate;
        private bool _primaryBeepFired;
        private bool _urgentBeepFired;
        private bool _lastTickWasSpike;

        public ShiftAssistEngine(Func<DateTime> utcNow = null)
        {
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public ShiftAssistState LastState { get; private set; } = ShiftAssistState.Off;
        public int LastTargetRpm { get; private set; }
        public int LastEffectiveTargetRpm { get; private set; }
        public int LastRpmRate { get; private set; }
        public bool LastBeepWasUrgent { get; private set; }
        public bool IsSuppressingDownshift { get { return _suppressUntilBelowReset; } }
        public bool IsSuppressingUpshift { get { return _suppressAfterUpshiftUntilBelowReset; } }

        public bool Evaluate(int currentGear, int engineRpm, double throttle01, int targetRpm, int cooldownMs, int resetHysteresisRpm, int leadTimeMs, int redlineRpm)
        {
            LastTargetRpm = targetRpm;
            LastEffectiveTargetRpm = targetRpm;
            LastRpmRate = 0;
            LastBeepWasUrgent = false;
            int effectiveLeadMs = Math.Max(0, leadTimeMs - AudioOutputCompMs);
            if (effectiveLeadMs > 200)
            {
                effectiveLeadMs = 200;
            }

            var nowUtc = _utcNow();
            bool sameGearAsLastSample = currentGear >= 1 && currentGear == _lastGear;
            bool spikeDetected = false;

            if (_lastSampleAtUtc != DateTime.MinValue)
            {
                double dtSec = (nowUtc - _lastSampleAtUtc).TotalSeconds;
                int deltaRpm = engineRpm - _lastRpm;
                bool dtSecValid = dtSec >= MinRpmRateDtSec && dtSec <= MaxRpmRateDtSec;

                if (sameGearAsLastSample && deltaRpm > 0)
                {
                    if (dtSec > 0)
                    {
                        double maxExpectedDelta = (MaxRpmRatePerSec * dtSec) + 200.0;
                        if (deltaRpm > maxExpectedDelta)
                        {
                            spikeDetected = true;
                        }
                        else if (dtSecValid)
                        {
                            double rpmRate = deltaRpm / dtSec;
                            int clampedRate = (int)Math.Round(rpmRate);
                            if (clampedRate > MaxRpmRatePerSec)
                            {
                                clampedRate = MaxRpmRatePerSec;
                            }

                            if (clampedRate > 0)
                            {
                                if (!_hasSmoothedRpmRate)
                                {
                                    _smoothedRpmRate = clampedRate;
                                    _hasSmoothedRpmRate = true;
                                }
                                else
                                {
                                    _smoothedRpmRate = (_smoothedRpmRate * RpmRateSmoothingPrevWeight) + (clampedRate * RpmRateSmoothingCurrentWeight);
                                }

                                int smoothedRate = (int)Math.Round(_smoothedRpmRate);
                                if (smoothedRate < 0)
                                {
                                    smoothedRate = 0;
                                }

                                LastRpmRate = smoothedRate;
                                if (effectiveLeadMs > 0)
                                {
                                    double leadDeltaRpm = smoothedRate * (effectiveLeadMs / 1000.0);
                                    if (leadDeltaRpm > MaxPredictiveEarlyRpm)
                                    {
                                        leadDeltaRpm = MaxPredictiveEarlyRpm;
                                    }

                                    if (engineRpm < (targetRpm - MaxPredictiveEarlyRpm))
                                    {
                                        LastRpmRate = 0;
                                        LastEffectiveTargetRpm = targetRpm;
                                        leadDeltaRpm = 0;
                                    }

                                    int computedEffectiveTarget = targetRpm - (int)Math.Round(leadDeltaRpm);
                                    int effectiveFloor = Math.Max(MinEffectiveTargetRpmFloor, targetRpm - EffectiveTargetFloorOffset);
                                    if (computedEffectiveTarget < effectiveFloor)
                                    {
                                        LastRpmRate = 0;
                                        LastEffectiveTargetRpm = targetRpm;
                                    }
                                    else
                                    {
                                        LastEffectiveTargetRpm = computedEffectiveTarget;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            _lastRpm = engineRpm;
            _lastSampleAtUtc = nowUtc;

            int effectiveTargetRpm = LastEffectiveTargetRpm;

            if (currentGear != _lastGear)
            {
                _lastTickWasSpike = false;
                bool upshift = _lastGear >= 1 && currentGear > _lastGear;
                if (currentGear < _lastGear)
                {
                    _suppressUntilBelowReset = true;
                }

                if (upshift && engineRpm >= (effectiveTargetRpm - resetHysteresisRpm))
                {
                    _suppressAfterUpshiftUntilBelowReset = true;
                }

                _lastGear = currentGear;
                _wasAboveTarget = false;
                _primaryBeepFired = false;
                _urgentBeepFired = false;
                _smoothedRpmRate = 0;
                _hasSmoothedRpmRate = false;
            }

            if (currentGear < 1 || currentGear > 8 || targetRpm <= 0)
            {
                _suppressUntilBelowReset = false;
                _suppressAfterUpshiftUntilBelowReset = false;
                LastState = ShiftAssistState.NoData;
                return false;
            }

            if (throttle01 < 0.90)
            {
                LastState = ShiftAssistState.On;
                return false;
            }

            if (engineRpm < (effectiveTargetRpm - resetHysteresisRpm))
            {
                _wasAboveTarget = false;
                _suppressUntilBelowReset = false;
                _suppressAfterUpshiftUntilBelowReset = false;
                _primaryBeepFired = false;
                _urgentBeepFired = false;
            }

            if (_suppressUntilBelowReset || _suppressAfterUpshiftUntilBelowReset)
            {
                LastState = ShiftAssistState.On;
                return false;
            }

            bool cooldownPassed = _lastBeepAtUtc == DateTime.MinValue || (nowUtc - _lastBeepAtUtc).TotalMilliseconds >= cooldownMs;

            bool blockTriggerForSpike = spikeDetected || (_lastTickWasSpike && engineRpm >= effectiveTargetRpm);

            if (!_primaryBeepFired && !_wasAboveTarget && engineRpm >= effectiveTargetRpm)
            {
                if (blockTriggerForSpike)
                {
                    _lastTickWasSpike = spikeDetected;
                    LastState = ShiftAssistState.On;
                    return false;
                }

                if (!cooldownPassed)
                {
                    LastState = ShiftAssistState.Cooldown;
                    _wasAboveTarget = true;
                    return false;
                }

                _wasAboveTarget = true;
                _primaryBeepFired = true;
                _lastBeepAtUtc = nowUtc;
                _lastTickWasSpike = false;
                LastState = ShiftAssistState.On;
                return true;
            }

            bool hasUrgentHeadroom = redlineRpm >= (targetRpm + MinUrgentAboveTargetRpm);
            int urgentThresholdRpm = redlineRpm - UrgentMarginRpm;
            if (_primaryBeepFired && !_urgentBeepFired && cooldownPassed && !spikeDetected && hasUrgentHeadroom && engineRpm >= urgentThresholdRpm)
            {
                _urgentBeepFired = true;
                _lastBeepAtUtc = nowUtc;
                LastBeepWasUrgent = true;
                _lastTickWasSpike = false;
                LastState = ShiftAssistState.On;
                return true;
            }

            _lastTickWasSpike = spikeDetected;
            LastState = cooldownPassed ? ShiftAssistState.On : ShiftAssistState.Cooldown;
            return false;
        }

        public void Reset()
        {
            _lastGear = 0;
            _wasAboveTarget = false;
            _suppressUntilBelowReset = false;
            _suppressAfterUpshiftUntilBelowReset = false;
            _lastRpm = 0;
            _lastSampleAtUtc = DateTime.MinValue;
            _lastBeepAtUtc = DateTime.MinValue;
            _smoothedRpmRate = 0;
            _hasSmoothedRpmRate = false;
            _primaryBeepFired = false;
            _urgentBeepFired = false;
            _lastTickWasSpike = false;
            LastTargetRpm = 0;
            LastEffectiveTargetRpm = 0;
            LastRpmRate = 0;
            LastBeepWasUrgent = false;
            LastState = ShiftAssistState.Off;
        }
    }
}
