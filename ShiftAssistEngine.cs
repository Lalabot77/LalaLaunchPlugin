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

        public ShiftAssistEngine(Func<DateTime> utcNow = null)
        {
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public ShiftAssistState LastState { get; private set; } = ShiftAssistState.Off;
        public int LastTargetRpm { get; private set; }

        public bool Evaluate(int currentGear, int engineRpm, double throttle01, int targetRpm, int cooldownMs, int resetHysteresisRpm)
        {
            LastTargetRpm = targetRpm;

            if (currentGear != _lastGear)
            {
                _lastGear = currentGear;
                _wasAboveTarget = false;
            }

            if (currentGear < 1 || currentGear > 8 || targetRpm <= 0)
            {
                LastState = ShiftAssistState.NoData;
                return false;
            }

            if (throttle01 < 0.90)
            {
                LastState = ShiftAssistState.On;
                return false;
            }

            if (engineRpm < (targetRpm - resetHysteresisRpm))
            {
                _wasAboveTarget = false;
            }

            var nowUtc = _utcNow();
            bool cooldownPassed = _lastBeepAtUtc == DateTime.MinValue || (nowUtc - _lastBeepAtUtc).TotalMilliseconds >= cooldownMs;

            if (!_wasAboveTarget && engineRpm >= targetRpm)
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
            LastTargetRpm = 0;
            LastState = ShiftAssistState.Off;
        }
    }
}
