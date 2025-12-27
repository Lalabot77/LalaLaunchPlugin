using System;

namespace LaunchPlugin.Messaging
{
    public class MessageInstance
    {
        public MessageDefinition Definition { get; set; }
        public string MsgId { get; set; }
        public int PriorityValue { get; set; }
        public string Category { get; set; }
        public string Text { get; set; }
        public string Token { get; set; }
        public string SourceEvaluatorId { get; set; }
        public DateTime ActivatedUtc { get; set; }
        public DateTime LastUpdateUtc { get; set; }
        public DateTime? CooldownUntilUtc { get; set; }
        public DateTime? IsSuppressedUntilUtc { get; set; }
        public bool SuppressedUntilTokenChange { get; set; }
        public string SuppressedToken { get; set; }
        public DateTime? MinVisibleUntilUtc { get; set; }
        public DateTime? NaturalClearUtc { get; set; }

        public bool IsSuppressed(DateTime now)
        {
            if (IsSuppressedUntilUtc.HasValue && now < IsSuppressedUntilUtc.Value)
                return true;

            if (SuppressedUntilTokenChange && !string.IsNullOrEmpty(SuppressedToken))
                return true;

            return false;
        }
    }
}
