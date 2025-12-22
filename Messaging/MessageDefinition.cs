using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace LaunchPlugin.Messaging
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum MessagePriority
    {
        Low = 10,
        Med = 50,
        High = 90
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum MessageActiveUntil
    {
        DelayThenClear,
        UntilStateChange,
        UntilValueIncrease,
        UntilValueDecrease,
        UntilSessionEnd,
        UntilManualMsgCx
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum MessageCancelAction
    {
        SilenceForDelay,
        UntilStateChange,
        UntilSessionEnd
    }

    public class MessageDefinition
    {
        public string MsgId { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public MessagePriority Priority { get; set; }
        public bool EnableOnLalaDash { get; set; }
        public bool EnableOnMsgDash { get; set; }
        public MessageActiveUntil ActiveUntil { get; set; }
        public int ActiveUntilDelayMs { get; set; }
        public MessageCancelAction MsgCxAction { get; set; }
        public int MsgCxDelayMs { get; set; }
        public int MinOnTimeMS { get; set; }
        public int MinCoolDownMS { get; set; }
        public string EvaluatorId { get; set; }
        public List<string> RequiredSignals { get; set; }
        public string TokenSpec { get; set; }
        public string TextTemplate { get; set; }
        public string Notes { get; set; }
        public string TextColor { get; set; } = string.Empty;   // "#AARRGGBB" or empty for defaults
        public string BgColor { get; set; } = string.Empty;     // "#AARRGGBB" or empty for defaults
        public string OutlineColor { get; set; } = string.Empty; // "#AARRGGBB" or empty for defaults
        public int FontSize { get; set; } = 24;                 // absolute size

        [JsonIgnore]
        public int PriorityValue => (int)Priority;

        public MessageDefinition Clone()
        {
            return new MessageDefinition
            {
                MsgId = MsgId,
                Name = Name,
                Category = Category,
                Priority = Priority,
                EnableOnLalaDash = EnableOnLalaDash,
                EnableOnMsgDash = EnableOnMsgDash,
                ActiveUntil = ActiveUntil,
                ActiveUntilDelayMs = ActiveUntilDelayMs,
                MsgCxAction = MsgCxAction,
                MsgCxDelayMs = MsgCxDelayMs,
                MinOnTimeMS = MinOnTimeMS,
                MinCoolDownMS = MinCoolDownMS,
                EvaluatorId = EvaluatorId,
                RequiredSignals = RequiredSignals == null ? new List<string>() : new List<string>(RequiredSignals),
                TokenSpec = TokenSpec,
                TextTemplate = TextTemplate,
                Notes = Notes,
                TextColor = TextColor,
                BgColor = BgColor,
                OutlineColor = OutlineColor,
                FontSize = FontSize
            };
        }

        public override string ToString()
        {
            return string.Format("{0} ({1}, {2})", MsgId ?? "unknown", Category ?? "", Priority);
        }
    }
}
