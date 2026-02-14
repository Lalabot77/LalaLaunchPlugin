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

            if (_lastSampleAtUtc != DateTime.MinValue)
            {
                double dtMs = (nowUtc - _lastSampleAtUtc).TotalMilliseconds;
                if (dtMs >= 10.0 && dtMs <= 200.0)
                {
                    double dtSec = dtMs / 1000.0;
                    if (dtSec > 0.0)
                    {
                        double rpmRate = (engineRpm - _lastRpm) / dtSec;
                        int roundedRate = (int)Math.Round(rpmRate);
                        if (roundedRate > 0 && roundedRate >= 500 && roundedRate <= 50000)
                        {
                            LastRpmRate = roundedRate;
                            if (leadTimeMs > 0)
                            {
                                double rpmLead = rpmRate * (leadTimeMs / 1000.0);
                                LastEffectiveTargetRpm = Math.Max(1, (int)Math.Round(targetRpm - rpmLead));
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
                bool upshift = currentGear > _lastGear;
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
