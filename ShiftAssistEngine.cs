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
        private const double MinRpmRateDtSec = 0.02;
        private const double MaxRpmRateDtSec = 0.20;
        private const int MaxRpmRatePerSec = 8000;
        private const int MaxPredictiveEarlyRpm = 800;
        private const int MinEffectiveTargetRpmFloor = 1000;
        private const int EffectiveTargetFloorOffset = 1500;

        private readonly Func<DateTime> _utcNow;
        private int _lastGear;
        private bool _wasAboveTarget;
        private DateTime _lastBeepAtUtc = DateTime.MinValue;
        private int _lastRpm;
        private DateTime _lastSampleAtUtc = DateTime.MinValue;
        private bool _suppressUntilBelowReset;
        private bool _suppressAfterUpshiftUntilBelowReset;

        public ShiftAssistEngine(Func<DateTime> utcNow = null)
        {
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public ShiftAssistState LastState { get; private set; } = ShiftAssistState.Off;
        public int LastTargetRpm { get; private set; }
        public int LastEffectiveTargetRpm { get; private set; }
        public int LastRpmRate { get; private set; }
        public bool IsSuppressingDownshift { get { return _suppressUntilBelowReset; } }
        public bool IsSuppressingUpshift { get { return _suppressAfterUpshiftUntilBelowReset; } }

        public bool Evaluate(int currentGear, int engineRpm, double throttle01, int targetRpm, int cooldownMs, int resetHysteresisRpm, int leadTimeMs)
        {
            LastTargetRpm = targetRpm;
            LastEffectiveTargetRpm = targetRpm;
            LastRpmRate = 0;

            var nowUtc = _utcNow();
            bool sameGearAsLastSample = currentGear >= 1 && currentGear == _lastGear;

            if (_lastSampleAtUtc != DateTime.MinValue)
            {
                double dtSec = (nowUtc - _lastSampleAtUtc).TotalSeconds;
                int deltaRpm = engineRpm - _lastRpm;
                if (sameGearAsLastSample && dtSec >= MinRpmRateDtSec && dtSec <= MaxRpmRateDtSec && deltaRpm > 0)
                {
                    double rpmRate = deltaRpm / dtSec;
                    int clampedRate = (int)Math.Round(rpmRate);
                    if (clampedRate > MaxRpmRatePerSec)
                    {
                        clampedRate = MaxRpmRatePerSec;
                    }

                    if (clampedRate > 0)
                    {
                        LastRpmRate = clampedRate;
                        if (leadTimeMs > 0)
                        {
                            double leadDeltaRpm = clampedRate * (leadTimeMs / 1000.0);
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

            _lastRpm = engineRpm;
            _lastSampleAtUtc = nowUtc;

            int effectiveTargetRpm = LastEffectiveTargetRpm;

            if (currentGear != _lastGear)
            {
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
            }

            if (_suppressUntilBelowReset || _suppressAfterUpshiftUntilBelowReset)
            {
                LastState = ShiftAssistState.On;
                return false;
            }

            bool cooldownPassed = _lastBeepAtUtc == DateTime.MinValue || (nowUtc - _lastBeepAtUtc).TotalMilliseconds >= cooldownMs;

            if (!_wasAboveTarget && engineRpm >= effectiveTargetRpm)
            {
                if (!cooldownPassed)
                {
                    LastState = ShiftAssistState.Cooldown;
                    _wasAboveTarget = true;
                    return false;
                }

                _wasAboveTarget = true;
                _lastBeepAtUtc = nowUtc;
                LastState = ShiftAssistState.On;
                return true;
            }

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
            LastTargetRpm = 0;
            LastEffectiveTargetRpm = 0;
            LastRpmRate = 0;
            LastState = ShiftAssistState.Off;
        }
    }
}
