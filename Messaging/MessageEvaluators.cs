using System;
using System.Collections.Generic;
using SimHub.Logging;

namespace LaunchPlugin.Messaging
{
    public class MessageEvaluationResult
    {
        public bool ShouldActivate { get; set; }
        public string Token { get; set; }
        public string Text { get; set; }
    }

    public interface IMessageEvaluator
    {
        bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result);
    }

    internal interface IResettableEvaluator
    {
        void Reset();
    }

    internal abstract class BaseEvaluator : IMessageEvaluator
    {
        public abstract bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result);

        protected MessageEvaluationResult Build(string text, string token = null)
        {
            return new MessageEvaluationResult
            {
                ShouldActivate = true,
                Token = token,
                Text = text
            };
        }

        protected bool TryGet<T>(ISignalProvider signals, string id, out T value)
        {
            return signals.TryGet(id, out value);
        }
    }

    internal class FuelSaveEvaluator : BaseEvaluator
    {
        private const double SaveThreshold = -0.15;

        public override bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;
            if (!TryGet(signals, "FuelDeltaLaps", out double delta)) return false;
            if (double.IsNaN(delta)) return false;

            if (delta < SaveThreshold)
            {
                result = Build(definition.TextTemplate);
                return true;
            }

            return false;
        }
    }

    internal class FuelCanPushEvaluator : BaseEvaluator, IResettableEvaluator
    {
        private bool _latchedThisSession;

        public void Reset()
        {
            _latchedThisSession = false;
        }

        public override bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;

            if (!TryGet(signals, "SessionTypeName", out string sessionType) ||
                !string.Equals(sessionType, "Race", StringComparison.OrdinalIgnoreCase))
            {
                _latchedThisSession = false;
                return false;
            }

            if (_latchedThisSession) return false;

            if (!TryGet(signals, "PitStopsRequiredByFuel", out int stopsRequired)) return false;
            if (!TryGet(signals, "FuelIsReady", out bool fuelReady)) return false;
            if (!fuelReady) return false;
            if (stopsRequired != 0) return false;

            _latchedThisSession = true;
            result = Build(definition.TextTemplate, "PUSH_OK");
            return true;
        }
    }

    internal class PitWindowOpenEvaluator : BaseEvaluator
    {
        public override bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;
            if (!TryGet(signals, "PitWindowOpen", out bool open)) return false;
            if (!open) return false;
            result = Build(definition.TextTemplate);
            return true;
        }
    }

    internal class RefuelCompleteEvaluator : BaseEvaluator
    {
        public override bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;
            if (!TryGet(signals, "PitServiceFuelDone", out bool done)) return false;
            if (!done) return false;

            result = Build(definition.TextTemplate);
            return true;
        }
    }

    internal class SlowDownEvaluator : BaseEvaluator
    {
        public override bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;
            if (!TryGet(signals, "SlowDownTimeRemaining", out double remaining)) return false;
            if (!(remaining > 0)) return false;

            result = Build(definition.TextTemplate, "SLOWDOWN");
            return true;
        }
    }

    internal class IncidentPointsEvaluator : BaseEvaluator
    {
        private readonly int _threshold;
        public IncidentPointsEvaluator(int threshold = 14)
        {
            _threshold = threshold;
        }

        public override bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;
            if (!TryGet(signals, "IncidentCount", out int incidents)) return false;
            if (incidents >= _threshold)
            {
                result = Build(definition.TextTemplate, "INCIDENT_WARN");
                return true;
            }
            return false;
        }
    }

    internal class PitRequiredEvaluator : BaseEvaluator
    {
        private readonly bool _isWarning;

        public PitRequiredEvaluator(bool isWarning)
        {
            _isWarning = isWarning;
        }

        public override bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;

            if (!TryGet(signals, "SessionTypeName", out string sessionType) ||
                !string.Equals(sessionType, "Race", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!TryGet(signals, "FuelLapsRemaining", out double lapsRemaining)) return false;
            if (double.IsNaN(lapsRemaining)) return false;

            if (_isWarning)
            {
                if (lapsRemaining <= 0.0)
                {
                    result = Build(definition.TextTemplate, "PIT_NOW");
                    return true;
                }
            }
            else
            {
                if (lapsRemaining > 0.0 && lapsRemaining < 2.0)
                {
                    // Do not show if warning condition is already true
                    if (lapsRemaining <= 0.0) return false;
                    result = Build(definition.TextTemplate, "PIT_SOON");
                    return true;
                }
            }

            return false;
        }
    }

    internal class TrafficBehindCloseEvaluator : BaseEvaluator
    {
        private readonly double _threshold;

        public TrafficBehindCloseEvaluator(double thresholdSeconds = 2.0)
        {
            _threshold = thresholdSeconds;
        }

        public override bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;
            if (!TryGet(signals, "TrafficBehindGapSeconds", out double gap)) return false;
            if (!(gap > 0) || gap > _threshold) return false;

            result = Build(definition.TextTemplate, "BEHIND_CLOSE");
            return true;
        }
    }

    internal class TrafficBehindFastEvaluator : BaseEvaluator
    {
        private readonly double _distanceThreshold;

        public TrafficBehindFastEvaluator(double thresholdMeters = 12.0)
        {
            _distanceThreshold = thresholdMeters;
        }

        public override bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;
            if (!TryGet(signals, "TrafficBehindDistanceM", out double dist)) return false;
            if (!(dist > 0) || dist > _distanceThreshold) return false;
            result = Build(definition.TextTemplate, "BEHIND_ATTACK");
            return true;
        }
    }

    internal class FasterClassBehindEvaluator : BaseEvaluator
    {
        public override bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;

            if (!TryGet(signals, "SessionTypeName", out string sessionType) ||
                !string.Equals(sessionType, "Race", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!TryGet(signals, "FasterClassApproachLine", out string line)) return false;
            if (string.IsNullOrWhiteSpace(line)) return false;

            result = Build(definition.TextTemplate, "FASTER_CLASS");
            return true;
        }
    }

    internal class DriverPositionChangeEvaluator : BaseEvaluator
    {
        private int _lastPosition = -1;
        private DateTime _lastUpdateUtc = DateTime.MinValue;

        public override bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;
            if (!TryGet(signals, "PlayerClassPosition", out int pos)) return false;
            if (pos <= 0) return false;

            if (_lastPosition > 0 && pos != _lastPosition)
            {
                _lastPosition = pos;
                _lastUpdateUtc = utcNow;
                result = Build(definition.TextTemplate.Replace("{PXX}", $"P{pos}"), "POS_CHANGE");
                return true;
            }

            _lastPosition = pos;
            _lastUpdateUtc = utcNow;
            return false;
        }
    }

    internal class GreenFlagStartEvaluator : BaseEvaluator
    {
        public override bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;
            if (!TryGet(signals, "FlagSessionFlags", out int flags)) return false;
            if (!TryGet(signals, "CompletedLaps", out int laps)) laps = 0;

            if ((flags & SessionFlagBits.Green) != 0 && laps <= 1)
            {
                result = Build(definition.TextTemplate);
                return true;
            }

            return false;
        }
    }

    internal class GreenFlagClearEvaluator : BaseEvaluator
    {
        public override bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;
            if (!TryGet(signals, "FlagSessionFlags", out int flags)) return false;
            if ((flags & SessionFlagBits.Green) != 0)
            {
                result = Build(definition.TextTemplate, "GREEN");
                return true;
            }
            return false;
        }
    }

    internal class SimpleFlagEvaluator : BaseEvaluator
    {
        private readonly int _flagMask;
        public SimpleFlagEvaluator(int mask)
        {
            _flagMask = mask;
        }

        public override bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;
            if (!TryGet(signals, "FlagSessionFlags", out int flags)) return false;
            if ((flags & _flagMask) != 0)
            {
                var token = _flagMask == SessionFlagBits.Blue ? "BLUE" :
                    _flagMask == SessionFlagBits.White ? "WHITE" :
                    _flagMask == SessionFlagBits.Checkered ? "CHECKERED" :
                    _flagMask == SessionFlagBits.Black ? "BLACK" :
                    _flagMask == SessionFlagBits.Meatball ? "MEATBALL" : "FLAG";
                result = Build(definition.TextTemplate, token);
                return true;
            }
            return false;
        }
    }

    internal class SafetyCarEvaluator : BaseEvaluator
    {
        public override bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;
            if (!TryGet(signals, "FlagSessionFlags", out int flags)) return false;

            // Pacing modes > not_pacing indicate safety car phases in iRacing
            bool pacing = TryGet(signals, "PaceMode", out int paceMode) && paceMode != 4;

            if (pacing || (flags & SessionFlagBits.Caution) != 0 || (flags & SessionFlagBits.CautionWaving) != 0)
            {
                result = Build(definition.TextTemplate);
                return true;
            }

            return false;
        }
    }

    internal class FullCourseYellowEvaluator : BaseEvaluator
    {
        public override bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;
            if (!TryGet(signals, "FlagSessionFlags", out int flags)) return false;
            if ((flags & SessionFlagBits.Caution) != 0 || (flags & SessionFlagBits.CautionWaving) != 0)
            {
                result = Build(definition.TextTemplate);
                return true;
            }
            return false;
        }
    }

    internal class YellowLocalEvaluator : BaseEvaluator
    {
        public override bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;
            if (!TryGet(signals, "FlagSessionFlags", out int flags)) return false;
            if ((flags & SessionFlagBits.Yellow) != 0 || (flags & SessionFlagBits.YellowWaving) != 0)
            {
                result = Build(definition.TextTemplate);
                return true;
            }
            return false;
        }
    }

    internal static class SessionFlagBits
    {
        public const int Checkered = 0x0001;
        public const int White = 0x0002;
        public const int Green = 0x0004;
        public const int Yellow = 0x0008;
        public const int Red = 0x0010;
        public const int Blue = 0x0020;
        public const int Debris = 0x0040;
        public const int Crossed = 0x0080;
        public const int YellowWaving = 0x0100;
        public const int OneLapToGreen = 0x0200;
        public const int GreenHeld = 0x0400;
        public const int TenToGo = 0x0800;
        public const int FiveToGo = 0x1000;
        public const int RandomWaving = 0x2000;
        public const int Caution = 0x4000;
        public const int CautionWaving = 0x8000;
        public const int Black = 0x010000;
        public const int Disqualify = 0x020000;
        public const int Meatball = 0x100000; // using repair bit as meatball indicator
    }
}
