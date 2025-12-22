using System;
using System.Collections.Generic;
using System.Linq;
using SimHub.Logging;
using SimHub.Plugins;
using LaunchPlugin;
using GameReaderCommon;

namespace LaunchPlugin.Messaging
{
    public class MessageEngine
    {
        private readonly Dictionary<string, MessageDefinition> _definitions;
        private readonly Dictionary<string, IMessageEvaluator> _evaluators;
        private readonly Dictionary<string, MessageInstance> _active = new Dictionary<string, MessageInstance>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _cooldowns = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (DateTime? untilUtc, bool untilTokenChange, string token)> _suppression = new Dictionary<string, (DateTime?, bool, string)>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _lastShownStack = new List<string>();
        private readonly TimeSpan _doubleTapWindow = TimeSpan.FromMilliseconds(350);
        private DateTime _lastMsgCxPressUtc = DateTime.MinValue;
        private DateTime _lastClearAllPulseUtc = DateTime.MinValue;

        private readonly MessageEngineOutputs _outputs = new MessageEngineOutputs();

        private readonly LaunchPlugin.LalaLaunch _plugin;
        private readonly PluginManager _pluginManager;
        private string _missingEvaluatorsCsv = string.Empty;

        public MessageEngine(PluginManager pluginManager, LaunchPlugin.LalaLaunch plugin)
        {
            _pluginManager = pluginManager;
            _plugin = plugin;
            _definitions = MessageDefinitionStore.LoadOrCreateDefault().ToDictionary(d => d.MsgId, d => d, StringComparer.OrdinalIgnoreCase);
            _evaluators = BuildEvaluators();
            RegisterMissingEvaluators();
            _outputs.MissingEvaluatorsCsv = _missingEvaluatorsCsv;
        }

        public MessageEngineOutputs Outputs => _outputs;

        public void ResetSession()
        {
            _active.Clear();
            _cooldowns.Clear();
            _suppression.Clear();
            _lastShownStack.Clear();
            _outputs.Clear();

            foreach (var evaluator in _evaluators.Values)
            {
                if (evaluator is IResettableEvaluator resettable)
                {
                    resettable.Reset();
                }
            }
        }

        public void Tick(GameReaderCommon.GameData data)
        {
            var now = DateTime.UtcNow;
            var signalProvider = new SignalProvider(_pluginManager, _plugin);

            // Evaluate all definitions
            foreach (var def in _definitions.Values)
            {
                if (!_evaluators.TryGetValue(def.EvaluatorId ?? string.Empty, out var evaluator))
                    continue;

                if (!evaluator.Evaluate(def, signalProvider, now, out var evalResult))
                {
                    // No trigger; handle state-change expiry for some messages
                    HandleNaturalExpiry(def, evalResult, now);
                    continue;
                }

                TryActivate(def, evalResult, now);
            }

            ProcessNaturalClears(now);
            SelectCurrentForDash(now);
            MaintainPulses(now);
        }

        private void HandleNaturalExpiry(MessageDefinition def, MessageEvaluationResult evalResult, DateTime now)
        {
            if (!_active.TryGetValue(def.MsgId, out var inst)) return;

            switch (def.ActiveUntil)
            {
                case MessageActiveUntil.UntilStateChange:
                case MessageActiveUntil.UntilValueIncrease:
                case MessageActiveUntil.UntilValueDecrease:
                    RemoveInstance(def.MsgId, "state-clear");
                    break;
                case MessageActiveUntil.DelayThenClear:
                    // do nothing; timer will clear it
                    break;
            }
        }

        private bool IsSuppressed(MessageDefinition def, string token, DateTime now)
        {
            if (_suppression.TryGetValue(def.MsgId, out var sup))
            {
                if (sup.untilUtc.HasValue && now < sup.untilUtc.Value)
                    return true;

                if (sup.untilTokenChange && !string.IsNullOrEmpty(sup.token) && string.Equals(sup.token, token, StringComparison.Ordinal))
                    return true;

                if (sup.untilTokenChange && !string.IsNullOrEmpty(sup.token) && !string.Equals(sup.token, token, StringComparison.Ordinal))
                {
                    // token changed -> clear suppression
                    _suppression.Remove(def.MsgId);
                    return false;
                }
            }
            return false;
        }

        private void TryActivate(MessageDefinition def, MessageEvaluationResult evalResult, DateTime now)
        {
            var token = evalResult?.Token ?? string.Empty;
            if (_cooldowns.TryGetValue(def.MsgId, out var cd) && now < cd)
                return;

            if (IsSuppressed(def, token, now))
                return;

            if (_active.TryGetValue(def.MsgId, out var existing))
            {
                existing.Token = token;
                existing.Text = ComposeText(def, evalResult);
                existing.LastUpdateUtc = now;
                if (!existing.NaturalClearUtc.HasValue)
                    existing.NaturalClearUtc = ComputeNaturalClear(def, now);
                if (!existing.MinVisibleUntilUtc.HasValue)
                    existing.MinVisibleUntilUtc = ComputeMinVisible(def, now);
                return;
            }

            var inst = new MessageInstance
            {
                Definition = def,
                MsgId = def.MsgId,
                PriorityValue = def.PriorityValue,
                Category = def.Category,
                Token = token,
                Text = ComposeText(def, evalResult),
                SourceEvaluatorId = def.EvaluatorId,
                ActivatedUtc = now,
                LastUpdateUtc = now,
                NaturalClearUtc = ComputeNaturalClear(def, now),
                MinVisibleUntilUtc = ComputeMinVisible(def, now)
            };

            _active[def.MsgId] = inst;
            LogInfo($"activated {def.MsgId} ({def.Category}/{def.Priority}) token='{token}' text='{inst.Text}'");
        }

        private static DateTime? ComputeNaturalClear(MessageDefinition def, DateTime now)
        {
            if (def.ActiveUntil == MessageActiveUntil.DelayThenClear && def.ActiveUntilDelayMs > 0)
                return now.AddMilliseconds(def.ActiveUntilDelayMs);
            return null;
        }

        private static DateTime? ComputeMinVisible(MessageDefinition def, DateTime now)
        {
            if (def.MinOnTimeMS > 0)
                return now.AddMilliseconds(def.MinOnTimeMS);
            return null;
        }

        private string ComposeText(MessageDefinition def, MessageEvaluationResult evalResult)
        {
            var token = evalResult?.Token;
            var template = evalResult?.Text ?? def.TextTemplate;

            if (string.IsNullOrWhiteSpace(template)) return def.MsgId;
            if (string.IsNullOrEmpty(token)) return template;

            return template
                .Replace("{token}", token)
                .Replace("{XXs}", token)
                .Replace("{XXm}", token)
                .Replace("{PXX}", token)
                .Replace("{XX Laps}", token);
        }

        private void ProcessNaturalClears(DateTime now)
        {
            var toRemove = new List<string>();
            foreach (var kv in _active)
            {
                var inst = kv.Value;
                var def = inst.Definition;

                if (def.ActiveUntil == MessageActiveUntil.DelayThenClear && inst.NaturalClearUtc.HasValue && now >= inst.NaturalClearUtc.Value)
                {
                    if (inst.MinVisibleUntilUtc.HasValue && now < inst.MinVisibleUntilUtc.Value)
                        continue;
                    toRemove.Add(kv.Key);
                    LogInfo($"auto-clear {kv.Key}");
                }
            }

            foreach (var id in toRemove)
            {
                RemoveInstance(id, "auto-clear");
            }
        }

        private void SelectCurrentForDash(DateTime now)
        {
            MessageInstance lala = null;
            MessageInstance msg = null;

            var ordered = _active.Values
                .OrderByDescending(m => m.PriorityValue)
                .ThenByDescending(m => m.LastUpdateUtc)
                .ToList();

            lala = ordered.FirstOrDefault(m => m.Definition.EnableOnLalaDash);
            msg = ordered.FirstOrDefault(m => m.Definition.EnableOnMsgDash);

            UpdateStackOrdering(lala, msg);

            _outputs.ActiveMsgIdLala = lala?.MsgId ?? string.Empty;
            _outputs.ActiveTextLala = lala?.Text ?? string.Empty;
            _outputs.ActivePriorityLala = lala?.Definition.Priority.ToString() ?? string.Empty;
            var lalaStyle = ResolveStyle(lala);
            _outputs.ActiveTextColorLala = lalaStyle.TextColor;
            _outputs.ActiveBgColorLala = lalaStyle.BgColor;
            _outputs.ActiveOutlineColorLala = lalaStyle.OutlineColor;
            _outputs.ActiveFontSizeLala = lalaStyle.FontSize;

            _outputs.ActiveMsgIdMsg = msg?.MsgId ?? string.Empty;
            _outputs.ActiveTextMsg = msg?.Text ?? string.Empty;
            _outputs.ActivePriorityMsg = msg?.Definition.Priority.ToString() ?? string.Empty;
            var msgStyle = ResolveStyle(msg);
            _outputs.ActiveTextColorMsg = msgStyle.TextColor;
            _outputs.ActiveBgColorMsg = msgStyle.BgColor;
            _outputs.ActiveOutlineColorMsg = msgStyle.OutlineColor;
            _outputs.ActiveFontSizeMsg = msgStyle.FontSize;

            _outputs.ActiveCount = _active.Count;
            _outputs.StackCsv = string.Join(";", ordered.Select(m => $"{m.MsgId}|{m.Definition.Priority}"));
        }

        private void UpdateStackOrdering(params MessageInstance[] instances)
        {
            foreach (var inst in instances)
            {
                if (inst == null) continue;
                _lastShownStack.RemoveAll(id => string.Equals(id, inst.MsgId, StringComparison.OrdinalIgnoreCase));
                _lastShownStack.Insert(0, inst.MsgId);
            }
        }

        public void OnMsgCxPressed()
        {
            var now = DateTime.UtcNow;
            if (_lastMsgCxPressUtc != DateTime.MinValue && (now - _lastMsgCxPressUtc) <= _doubleTapWindow)
            {
                _lastMsgCxPressUtc = DateTime.MinValue;
                ClearAll(now);
                return;
            }

            _lastMsgCxPressUtc = now;
            CancelLastShown(now);
        }

        private void CancelLastShown(DateTime now)
        {
            string targetId = null;
            if (_lastShownStack.Count > 0)
                targetId = _lastShownStack[0];
            else if (_active.Count > 0)
                targetId = _active.Values.OrderByDescending(m => m.LastUpdateUtc).First().MsgId;

            if (string.IsNullOrEmpty(targetId)) return;
            ApplyCancel(targetId, now);
        }

        private void ApplyCancel(string msgId, DateTime now)
        {
            if (!_definitions.TryGetValue(msgId, out var def)) return;
            if (!_active.TryGetValue(msgId, out var inst)) return;

            RemoveInstance(msgId, "cancel");
            _outputs.LastCancelMsgId = msgId;

            switch (def.MsgCxAction)
            {
                case MessageCancelAction.SilenceForDelay:
                    if (def.MsgCxDelayMs > 0)
                        _suppression[msgId] = (now.AddMilliseconds(def.MsgCxDelayMs), false, null);
                    break;
                case MessageCancelAction.UntilStateChange:
                    _suppression[msgId] = (null, true, inst.Token);
                    break;
                case MessageCancelAction.UntilSessionEnd:
                    _suppression[msgId] = (DateTime.MaxValue, false, null);
                    break;
            }

            LogInfo($"cancel {msgId} via MsgCx");
            SelectCurrentForDash(now);
        }

        private void RemoveInstance(string msgId, string reason)
        {
            if (_active.TryGetValue(msgId, out var inst))
            {
                _active.Remove(msgId);
                _lastShownStack.RemoveAll(id => string.Equals(id, msgId, StringComparison.OrdinalIgnoreCase));

                if (inst.Definition.MinCoolDownMS > 0)
                    _cooldowns[msgId] = DateTime.UtcNow.AddMilliseconds(inst.Definition.MinCoolDownMS);
            }
        }

        private void ClearAll(DateTime now)
        {
            foreach (var id in _active.Keys.ToList())
            {
                ApplyCancel(id, now);
            }

            _active.Clear();
            _lastShownStack.Clear();
            _outputs.ClearAllPulse = true;
            _lastClearAllPulseUtc = now;
            LogInfo("clear-all triggered");
            SelectCurrentForDash(now);
        }

        private void MaintainPulses(DateTime now)
        {
            if (_outputs.ClearAllPulse && (now - _lastClearAllPulseUtc).TotalMilliseconds > 250)
                _outputs.ClearAllPulse = false;
        }

        private (string TextColor, string BgColor, string OutlineColor, int FontSize) ResolveStyle(MessageInstance inst)
        {
            if (inst == null || inst.Definition == null)
                return (Defaults.TextLow, Defaults.BgLow, Defaults.OutlineLow, 24);

            var def = inst.Definition;
            bool isFlag = IsFlagMessage(def);
            var priorityDefaults = GetPriorityDefaults(def.Priority);

            string text = string.IsNullOrWhiteSpace(def.TextColor) ? priorityDefaults.Text : def.TextColor;
            string outline = string.IsNullOrWhiteSpace(def.OutlineColor) ? priorityDefaults.Outline : def.OutlineColor;

            string bg;
            if (isFlag && string.IsNullOrWhiteSpace(def.BgColor))
            {
                bg = GetFlagBgColor(def.MsgId);
                if (string.IsNullOrWhiteSpace(bg)) bg = priorityDefaults.Bg;
            }
            else
            {
                bg = string.IsNullOrWhiteSpace(def.BgColor) ? priorityDefaults.Bg : def.BgColor;
            }

            int fontSize = def.FontSize > 0 ? def.FontSize : 24;

            return (text, bg, outline, fontSize);
        }

        private static bool IsFlagMessage(MessageDefinition def)
        {
            if (def == null) return false;
            if (!string.IsNullOrEmpty(def.MsgId) && def.MsgId.StartsWith("flag.", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private static (string Text, string Bg, string Outline) GetPriorityDefaults(MessagePriority priority)
        {
            switch (priority)
            {
                case MessagePriority.High:
                    return (Defaults.TextHigh, Defaults.BgHigh, Defaults.OutlineHigh);
                case MessagePriority.Med:
                    return (Defaults.TextMed, Defaults.BgMed, Defaults.OutlineMed);
                default:
                    return (Defaults.TextLow, Defaults.BgLow, Defaults.OutlineLow);
            }
        }

        private static string GetFlagBgColor(string msgId)
        {
            if (string.IsNullOrWhiteSpace(msgId)) return Defaults.BgLow;
            var id = msgId.ToLowerInvariant();

            if (id.Contains("yellow"))
                return Colors.Yellow;
            if (id.Contains("blue"))
                return Colors.Blue;
            if (id.Contains("green"))
                return Colors.Green;
            if (id.Contains("white"))
                return Colors.White;
            if (id.Contains("red"))
                return Colors.Red;
            if (id.Contains("checkered") || id.Contains("chequered"))
                return Colors.Checkered;
            if (id.Contains("black"))
                return Colors.Black;
            if (id.Contains("meatball"))
                return Colors.Meatball;

            return Defaults.BgLow;
        }

        private static class Defaults
        {
            public const string TextHigh = "#FFFFFF00";     // yellow
            public const string BgHigh = "#FFFF0000";       // red
            public const string OutlineHigh = "#FFFFFF00";  // yellow

            public const string TextMed = "#FF0000FF";      // blue
            public const string BgMed = "#FFFFFF00";        // yellow
            public const string OutlineMed = "#FF0000FF";   // blue

            public const string TextLow = "#FFFFFFFF";      // white
            public const string BgLow = "#00000000";        // neutral transparent
            public const string OutlineLow = "#FFFFFFFF";   // white
        }

        private static class Colors
        {
            public const string Yellow = "#FFFFFF00";
            public const string Blue = "#FF0000FF";
            public const string Green = "#FF00FF00";
            public const string Red = "#FFFF0000";
            public const string White = "#FFFFFFFF";
            public const string Black = "#FF000000";
            public const string Meatball = "#FFFF8000";
            public const string Checkered = "#FF000000"; // rely on light text
        }

        private Dictionary<string, IMessageEvaluator> BuildEvaluators()
        {
            return new Dictionary<string, IMessageEvaluator>(StringComparer.OrdinalIgnoreCase)
            {
                { "Eval_PitRequiredCaution", new PitRequiredEvaluator(isWarning: false) },
                { "Eval_PitRequiredWarning", new PitRequiredEvaluator(isWarning: true) },
                { "Eval_FuelSaveRequired", new FuelSaveEvaluator() },
                { "Eval_FuelCanPush", new FuelCanPushEvaluator() },
                { "Eval_PitWindowOpen", new PitWindowOpenEvaluator() },
                { "Eval_RefuelComplete", new RefuelCompleteEvaluator() },
                { "Eval_SlowDown", new SlowDownEvaluator() },
                { "Eval_IncPoints", new IncidentPointsEvaluator() },
                { "Eval_TrafficBehindClose", new TrafficBehindCloseEvaluator() },
                { "Eval_TrafficBehindFast", new TrafficBehindFastEvaluator() },
                { "Eval_FasterClassBehind", new FasterClassBehindEvaluator() },
                { "Eval_DriverClassPosition", new DriverPositionChangeEvaluator() },
                { "Eval_FlagGreenStart", new GreenFlagStartEvaluator() },
                { "Eval_FlagGreenClear", new GreenFlagClearEvaluator() },
                { "Eval_FlagBlue", new SimpleFlagEvaluator(SessionFlagBits.Blue) },
                { "Eval_FlagGreen", new SimpleFlagEvaluator(SessionFlagBits.Green) },
                { "Eval_FlagYellowLocal", new YellowLocalEvaluator() },
                { "Eval_FlagFCY", new FullCourseYellowEvaluator() },
                { "Eval_SafetyCar", new SafetyCarEvaluator() },
                { "Eval_FlagWhite", new SimpleFlagEvaluator(SessionFlagBits.White) },
                { "Eval_FlagCheckered", new SimpleFlagEvaluator(SessionFlagBits.Checkered) },
                { "Eval_FlagBlack", new SimpleFlagEvaluator(SessionFlagBits.Black) },
                { "Eval_FlagMeatball", new SimpleFlagEvaluator(SessionFlagBits.Meatball) }
            };
        }

        private static void LogInfo(string message)
        {
            SimHub.Logging.Current.Info($"[LalaPlugin:MSGV1] {message}");
        }

        private void RegisterMissingEvaluators()
        {
            var referenced = _definitions.Values
                .Where(d => !string.IsNullOrWhiteSpace(d.EvaluatorId))
                .GroupBy(d => d.EvaluatorId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(d => d.MsgId).ToList(), StringComparer.OrdinalIgnoreCase);

            var missing = referenced.Keys.Where(k => !_evaluators.ContainsKey(k)).ToList();
            if (missing.Count == 0)
            {
                _missingEvaluatorsCsv = string.Empty;
                return;
            }

            foreach (var evalId in missing)
            {
                if (!referenced.TryGetValue(evalId, out var msgIds)) msgIds = new List<string>();
                _evaluators[evalId] = new MissingEvaluator(evalId, msgIds);
            }

            _missingEvaluatorsCsv = string.Join(";", missing.Select(id =>
            {
                var msgs = referenced.TryGetValue(id, out var list) ? string.Join(",", list) : "";
                return $"{id}|{msgs}";
            }));
        }
    }

    public class MessageEngineOutputs
    {
        public string ActiveTextLala { get; set; } = string.Empty;
        public string ActivePriorityLala { get; set; } = string.Empty;
        public string ActiveMsgIdLala { get; set; } = string.Empty;
        public string ActiveTextColorLala { get; set; } = Defaults.TextLow;
        public string ActiveBgColorLala { get; set; } = Defaults.BgLow;
        public string ActiveOutlineColorLala { get; set; } = Defaults.OutlineLow;
        public int ActiveFontSizeLala { get; set; } = 24;

        public string ActiveTextMsg { get; set; } = string.Empty;
        public string ActivePriorityMsg { get; set; } = string.Empty;
        public string ActiveMsgIdMsg { get; set; } = string.Empty;
        public string ActiveTextColorMsg { get; set; } = Defaults.TextLow;
        public string ActiveBgColorMsg { get; set; } = Defaults.BgLow;
        public string ActiveOutlineColorMsg { get; set; } = Defaults.OutlineLow;
        public int ActiveFontSizeMsg { get; set; } = 24;

        public int ActiveCount { get; set; }
        public string LastCancelMsgId { get; set; } = string.Empty;
        public bool ClearAllPulse { get; set; }
        public string StackCsv { get; set; } = string.Empty;
        public string MissingEvaluatorsCsv { get; set; } = string.Empty;

        public void Clear()
        {
            ActiveTextLala = string.Empty;
            ActivePriorityLala = string.Empty;
            ActiveMsgIdLala = string.Empty;
            ActiveTextColorLala = Defaults.TextLow;
            ActiveBgColorLala = Defaults.BgLow;
            ActiveOutlineColorLala = Defaults.OutlineLow;
            ActiveFontSizeLala = 24;
            ActiveTextMsg = string.Empty;
            ActivePriorityMsg = string.Empty;
            ActiveMsgIdMsg = string.Empty;
            ActiveTextColorMsg = Defaults.TextLow;
            ActiveBgColorMsg = Defaults.BgLow;
            ActiveOutlineColorMsg = Defaults.OutlineLow;
            ActiveFontSizeMsg = 24;
            ActiveCount = 0;
            LastCancelMsgId = string.Empty;
            ClearAllPulse = false;
            StackCsv = string.Empty;
            MissingEvaluatorsCsv = string.Empty;
        }
    }

    internal class MissingEvaluator : IMessageEvaluator
    {
        private readonly string _id;
        private readonly List<string> _msgIds;
        private bool _logged;

        public MissingEvaluator(string id, List<string> msgIds)
        {
            _id = id ?? "unknown";
            _msgIds = msgIds ?? new List<string>();
        }

        public bool Evaluate(MessageDefinition definition, ISignalProvider signals, DateTime utcNow, out MessageEvaluationResult result)
        {
            result = null;
            if (!_logged)
            {
                _logged = true;
                SimHub.Logging.Current.Warn($"[LalaPlugin:MSGV1] Missing evaluator '{_id}' used by: {string.Join(",", _msgIds)}");
            }
            return false;
        }
    }
}
