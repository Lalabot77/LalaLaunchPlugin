using System;
using System.Collections.Generic;
using System.IO;
using LaunchPlugin;
using Newtonsoft.Json;

namespace LaunchPlugin.Messaging
{
    public static class MessageDefinitionStore
    {
        [JsonObject(MemberSerialization.OptIn)]
        private class MessageDefinitionStoreRoot
        {
            [JsonProperty]
            public int SchemaVersion { get; set; } = 1;

            [JsonProperty]
            public List<MessageDefinition> Messages { get; set; } = new List<MessageDefinition>();
        }

        private const string NewFileName = "Messages.json";
        private const string LegacyFileName = "LalaLaunch.Messages.json";
        private static readonly HashSet<string> ExcludedMsgIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "rejoin.threat_high",
            "rejoin.threat_med"
        };

        public static string GetFolderPath()
        {
            return PluginStorage.GetPluginFolder();
        }

        public static string GetFilePath() => Path.Combine(GetFolderPath(), NewFileName);

        private static string GetLegacyFilePath() => PluginStorage.GetCommonFilePath(LegacyFileName);

        public static List<MessageDefinition> LoadOrCreateDefault()
        {
            try
            {
                var path = GetFilePath();
                var legacyPath = GetLegacyFilePath();
                PluginStorage.TryMigrate(legacyPath, path);

                if (!File.Exists(path))
                {
                    var defaults = BuildDefaultDefinitions();
                    SaveAll(defaults);
                    return defaults;
                }

                var json = File.ReadAllText(path);
                List<MessageDefinition> list = null;
                try
                {
                    var store = JsonConvert.DeserializeObject<MessageDefinitionStoreRoot>(json);
                    list = store?.Messages;
                }
                catch
                {
                    list = null;
                }

                if (list == null)
                {
                    list = JsonConvert.DeserializeObject<List<MessageDefinition>>(json);
                }

                list ??= new List<MessageDefinition>();
                foreach (var def in list)
                {
                    ApplyDefaults(def);
                }
                RemoveExcludedMessages(list);
                if (list.Count == 0)
                {
                    list = BuildDefaultDefinitions();
                    SaveAll(list);
                }

                return list;
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn($"[LalaPlugin:MSGV1] Failed to load message definitions, using defaults. {ex.Message}");
                var defaults = BuildDefaultDefinitions();
                SafeTry(() => SaveAll(defaults));
                return defaults;
            }
        }

        public static void SaveAll(List<MessageDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));

            var path = GetFilePath();

            var store = new MessageDefinitionStoreRoot
            {
                Messages = definitions
            };
            var json = JsonConvert.SerializeObject(store, Formatting.Indented);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path))
                File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
            else
                File.Move(tmp, path);
        }

        private static void SafeTry(Action action)
        {
            try { action(); } catch { /* ignore */ }
        }

        private static void ApplyDefaults(MessageDefinition def)
        {
            if (def == null) return;
            if (def.FontSize <= 0) def.FontSize = 24;
            def.TextColor = def.TextColor ?? string.Empty;
            def.BgColor = def.BgColor ?? string.Empty;
            def.OutlineColor = def.OutlineColor ?? string.Empty;
        }

        // Hardcoded defaults derived from Message Catalog v5 (CSV contract)
        private static List<MessageDefinition> BuildDefaultDefinitions()
        {
            var list = new List<MessageDefinition>
            {
                new MessageDefinition
                {
                    MsgId = "fuel.pit_required_Caution",
                    Name = "Pit Soon",
                    Category = "Fuel",
                    Priority = MessagePriority.Med,
                    EnableOnLalaDash = false,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.UntilStateChange,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 5000,
                    EvaluatorId = "Eval_PitRequiredCaution",
                    RequiredSignals = new List<string> { "FuelLapsRemaining" },
                    TokenSpec = "PIT_SOON",
                    TextTemplate = "BOX WITHIN 2 LAPS",
                    Notes = "Caution Yellow message active when only 2 laps fuel left"
                },
                new MessageDefinition
                {
                    MsgId = "fuel.pit_required_Warning",
                    Name = "Pit Now",
                    Category = "Fuel",
                    Priority = MessagePriority.High,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.UntilManualMsgCx,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 5000,
                    EvaluatorId = "Eval_PitRequiredWarning",
                    RequiredSignals = new List<string> { "FuelLapsRemaining" },
                    TokenSpec = "PIT_NOW",
                    TextTemplate = "BOX BOX BOX NOW",
                    Notes = "Warning Red message active when only 1 lap of fuel left"
                },
                new MessageDefinition
                {
                    MsgId = "flag.GreenStart",
                    Name = "Green Start Flag",
                    Category = "Race Control",
                    Priority = MessagePriority.Low,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = false,
                    ActiveUntil = MessageActiveUntil.DelayThenClear,
                    ActiveUntilDelayMs = 3000,
                    MsgCxAction = MessageCancelAction.UntilSessionEnd,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 180000,
                    EvaluatorId = "Eval_FlagGreenStart",
                    RequiredSignals = new List<string> { "FlagSessionFlags", "CompletedLaps" },
                    TokenSpec = null,
                    TextTemplate = "GO GO GO",
                    Notes = "Race start green flag only."
                },
                new MessageDefinition
                {
                    MsgId = "flag.GreenClear",
                    Name = "Green Clear Flag",
                    Category = "Race Control",
                    Priority = MessagePriority.Low,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = false,
                    ActiveUntil = MessageActiveUntil.DelayThenClear,
                    ActiveUntilDelayMs = 3000,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 2000,
                    MinCoolDownMS = 10000,
                    EvaluatorId = "Eval_FlagGreenClear",
                    RequiredSignals = new List<string> { "FlagSessionFlags" },
                    TokenSpec = null,
                    TextTemplate = "TRACK CLEAR"
                },
                new MessageDefinition
                {
                    MsgId = "flag.BlueFlag",
                    Name = "Blue Flag",
                    Category = "Race Control",
                    Priority = MessagePriority.Low,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = false,
                    ActiveUntil = MessageActiveUntil.UntilStateChange,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.SilenceForDelay,
                    MsgCxDelayMs = 10000,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 0,
                    EvaluatorId = "Eval_FlagBlue",
                    RequiredSignals = new List<string> { "FlagSessionFlags" },
                    TokenSpec = "BLUE",
                    TextTemplate = "Blue Flag"
                },
                new MessageDefinition
                {
                    MsgId = "flag.green",
                    Name = "Green flag",
                    Category = "Race Control",
                    Priority = MessagePriority.Low,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = false,
                    ActiveUntil = MessageActiveUntil.DelayThenClear,
                    ActiveUntilDelayMs = 3000,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 5000,
                    EvaluatorId = "Eval_FlagGreen",
                    RequiredSignals = new List<string> { "FlagSessionFlags" },
                    TokenSpec = null,
                    TextTemplate = "Green flag"
                },
                new MessageDefinition
                {
                    MsgId = "flag.yellow.local",
                    Name = "Yellow (local)",
                    Category = "Race Control",
                    Priority = MessagePriority.Low,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.UntilStateChange,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 10000,
                    EvaluatorId = "Eval_FlagYellowLocal",
                    RequiredSignals = new List<string> { "FlagSessionFlags" },
                    TokenSpec = null,
                    TextTemplate = "Yellow (local) {dist to incident ahead}"
                },
                new MessageDefinition
                {
                    MsgId = "flag.yellow.fcy",
                    Name = "Full Course Yellow",
                    Category = "Race Control",
                    Priority = MessagePriority.High,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.UntilStateChange,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 10000,
                    EvaluatorId = "Eval_FlagFCY",
                    RequiredSignals = new List<string> { "FlagSessionFlags" },
                    TokenSpec = null,
                    TextTemplate = "Full Course Yellow"
                },
                new MessageDefinition
                {
                    MsgId = "flag.safetycar",
                    Name = "Safety Car deployed",
                    Category = "Race Control",
                    Priority = MessagePriority.High,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.UntilStateChange,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 10000,
                    EvaluatorId = "Eval_SafetyCar",
                    RequiredSignals = new List<string> { "FlagSessionFlags", "PaceMode" },
                    TokenSpec = null,
                    TextTemplate = "Safety Car deployed"
                },
                new MessageDefinition
                {
                    MsgId = "flag.white",
                    Name = "White Flag",
                    Category = "Race Control",
                    Priority = MessagePriority.Low,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.UntilStateChange,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 10000,
                    EvaluatorId = "Eval_FlagWhite",
                    RequiredSignals = new List<string> { "FlagSessionFlags" },
                    TokenSpec = null,
                    TextTemplate = "White Flag"
                },
                new MessageDefinition
                {
                    MsgId = "flag.checkered",
                    Name = "Checkered flag",
                    Category = "Race Control",
                    Priority = MessagePriority.Low,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.UntilSessionEnd,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 10000,
                    EvaluatorId = "Eval_FlagCheckered",
                    RequiredSignals = new List<string> { "FlagSessionFlags" },
                    TokenSpec = null,
                    TextTemplate = "Checkered flag"
                },
                new MessageDefinition
                {
                    MsgId = "flag.black",
                    Name = "Black flag",
                    Category = "Race Control",
                    Priority = MessagePriority.High,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.UntilSessionEnd,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 10000,
                    EvaluatorId = "Eval_FlagBlack",
                    RequiredSignals = new List<string> { "FlagSessionFlags" },
                    TokenSpec = null,
                    TextTemplate = "Black flag"
                },
                new MessageDefinition
                {
                    MsgId = "flag.meatball",
                    Name = "Meatball (damage)",
                    Category = "Race Control",
                    Priority = MessagePriority.High,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.UntilSessionEnd,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 10000,
                    EvaluatorId = "Eval_FlagMeatball",
                    RequiredSignals = new List<string> { "FlagSessionFlags" },
                    TokenSpec = null,
                    TextTemplate = "Meatball"
                },
                new MessageDefinition
                {
                    MsgId = "pit.window_open",
                    Name = "Pit window open",
                    Category = "Pit",
                    Priority = MessagePriority.Med,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.DelayThenClear,
                    ActiveUntilDelayMs = 8000,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 5000,
                    EvaluatorId = "Eval_PitWindowOpen",
                    RequiredSignals = new List<string> { "PitWindowOpen" },
                    TokenSpec = null,
                    TextTemplate = "Pit window open"
                },
                new MessageDefinition
                {
                    MsgId = "pit.refuel_complete",
                    Name = "Refuel complete",
                    Category = "Pit",
                    Priority = MessagePriority.Low,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.DelayThenClear,
                    ActiveUntilDelayMs = 2000,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 5000,
                    EvaluatorId = "Eval_RefuelComplete",
                    RequiredSignals = new List<string> { "PitServiceFuelDone" },
                    TokenSpec = null,
                    TextTemplate = "Refuel complete"
                },
                new MessageDefinition
                {
                    MsgId = "trackmarkers.captured",
                    Name = "Pit markers captured",
                    Category = "Pit",
                    Priority = MessagePriority.Low,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.DelayThenClear,
                    ActiveUntilDelayMs = 4000,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1500,
                    MinCoolDownMS = 0,
                    EvaluatorId = "Eval_TrackMarkersCaptured",
                    RequiredSignals = new List<string> { "TrackMarkers.Pulse.Captured" },
                    TokenSpec = null,
                    TextTemplate = "Pit markers learned."
                },
                new MessageDefinition
                {
                    MsgId = "trackmarkers.length_delta",
                    Name = "Track length changed",
                    Category = "Pit",
                    Priority = MessagePriority.Low,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.DelayThenClear,
                    ActiveUntilDelayMs = 5000,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1500,
                    MinCoolDownMS = 0,
                    EvaluatorId = "Eval_TrackMarkersLengthDelta",
                    RequiredSignals = new List<string> { "TrackMarkers.Pulse.LengthDelta" },
                    TokenSpec = null,
                    TextTemplate = "Track length changed; pit marker distances may be off."
                },
                new MessageDefinition
                {
                    MsgId = "trackmarkers.lock_mismatch",
                    Name = "Locked marker mismatch",
                    Category = "Pit",
                    Priority = MessagePriority.Med,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.DelayThenClear,
                    ActiveUntilDelayMs = 5000,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1500,
                    MinCoolDownMS = 0,
                    EvaluatorId = "Eval_TrackMarkersLockedMismatch",
                    RequiredSignals = new List<string> { "TrackMarkers.Pulse.LockedMismatch" },
                    TokenSpec = null,
                    TextTemplate = "Locked pit markers differ from live detection."
                },
                new MessageDefinition
                {
                    MsgId = "fuel.save_required",
                    Name = "Fuel save required",
                    Category = "Fuel",
                    Priority = MessagePriority.Med,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.UntilStateChange,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.SilenceForDelay,
                    MsgCxDelayMs = 1800000,
                    MinOnTimeMS = 3000,
                    MinCoolDownMS = 5000,
                    EvaluatorId = "Eval_FuelSaveRequired",
                    RequiredSignals = new List<string> { "FuelDeltaLaps" },
                    TokenSpec = null,
                    TextTemplate = "Fuel save required"
                },
                new MessageDefinition
                {
                    MsgId = "fuel.push_ok",
                    Name = "Fuel OK to push",
                    Category = "Fuel",
                    Priority = MessagePriority.Low,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.DelayThenClear,
                    ActiveUntilDelayMs = 5000,
                    MsgCxAction = MessageCancelAction.UntilSessionEnd,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 5000,
                    EvaluatorId = "Eval_FuelCanPush",
                    RequiredSignals = new List<string> { "FuelCanPush", "PitStopsRequiredByFuel" },
                    TokenSpec = "PUSH_OK",
                    TextTemplate = "Fuel OK to push"
                },
                new MessageDefinition
                {
                    MsgId = "strategy.overtake_soon",
                    Name = "Overtake In XX Laps",
                    Category = "Strategy",
                    Priority = MessagePriority.Low,
                    EnableOnLalaDash = false,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.DelayThenClear,
                    ActiveUntilDelayMs = 3000,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 3000,
                    EvaluatorId = "Eval_CatchClassAhead",
                    RequiredSignals = new List<string> { "DriverAheadGapSeconds", "PlayerPaceLast5LapAvg" },
                    TokenSpec = "carId",
                    TextTemplate = "Overtake in {XX Laps}"
                },
                new MessageDefinition
                {
                    MsgId = "strategy.positionchange",
                    Name = "Position Change in Class",
                    Category = "Strategy",
                    Priority = MessagePriority.Low,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = false,
                    ActiveUntil = MessageActiveUntil.DelayThenClear,
                    ActiveUntilDelayMs = 2000,
                    MsgCxAction = MessageCancelAction.SilenceForDelay,
                    MsgCxDelayMs = 30000,
                    MinOnTimeMS = 2000,
                    MinCoolDownMS = 5000,
                    EvaluatorId = "Eval_DriverClassPosition",
                    RequiredSignals = new List<string> { "PlayerClassPosition" },
                    TokenSpec = null,
                    TextTemplate = "Now {PXX} in Class"
                },
                new MessageDefinition
                {
                    MsgId = "traffic.behind_close",
                    Name = "Car behind close",
                    Category = "Traffic",
                    Priority = MessagePriority.Low,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = false,
                    ActiveUntil = MessageActiveUntil.UntilValueIncrease,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 2000,
                    MinCoolDownMS = 2000,
                    EvaluatorId = "Eval_TrafficBehindClose",
                    RequiredSignals = new List<string> { "TrafficBehindGapSeconds" },
                    TokenSpec = "BEHIND_CLOSE",
                    TextTemplate = "Car behind close"
                },
                new MessageDefinition
                {
                    MsgId = "traffic.behind_attack",
                    Name = "Under attack (closing fast)",
                    Category = "Traffic",
                    Priority = MessagePriority.Med,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = false,
                    ActiveUntil = MessageActiveUntil.UntilValueIncrease,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 2000,
                    EvaluatorId = "Eval_TrafficBehindFast",
                    RequiredSignals = new List<string> { "TrafficBehindDistanceM" },
                    TokenSpec = "BEHIND_ATTACK",
                    TextTemplate = "Under attack"
                },
                new MessageDefinition
                {
                    MsgId = "traffic.fasterclass_behind",
                    Name = "Faster Class Approachng",
                    Category = "Traffic",
                    Priority = MessagePriority.High,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.UntilStateChange,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 1000,
                    EvaluatorId = "Eval_FasterClassBehind",
                    RequiredSignals = new List<string> { "FasterClassApproachLine" },
                    TokenSpec = "FASTER_CLASS",
                    TextTemplate = "Faster class approaching"
                },
                new MessageDefinition
                {
                    MsgId = "racecontrol.ahead_slow",
                    Name = "Incident ahead (slow car)",
                    Category = "Race Control",
                    Priority = MessagePriority.Med,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.DelayThenClear,
                    ActiveUntilDelayMs = 6000,
                    MsgCxAction = MessageCancelAction.SilenceForDelay,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 10000,
                    EvaluatorId = "Eval_IncidentAhead",
                    RequiredSignals = new List<string> { "IncidentAheadWarning" },
                    TokenSpec = null,
                    TextTemplate = "Incident ahead in {XXm}"
                },
                new MessageDefinition
                {
                    MsgId = "racecontrol.slowdown",
                    Name = "Penalty pending",
                    Category = "Race Control",
                    Priority = MessagePriority.High,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.UntilStateChange,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 20000,
                    MinOnTimeMS = 3000,
                    MinCoolDownMS = 10000,
                    EvaluatorId = "Eval_SlowDown",
                    RequiredSignals = new List<string> { "SlowDownTimeRemaining" },
                    TokenSpec = null,
                    TextTemplate = "Penalty pending"
                },
                new MessageDefinition
                {
                    MsgId = "racecontrol.inc_points",
                    Name = "Incident points warning",
                    Category = "Race Control",
                    Priority = MessagePriority.Med,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.DelayThenClear,
                    ActiveUntilDelayMs = 5000,
                    MsgCxAction = MessageCancelAction.SilenceForDelay,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 10000,
                    EvaluatorId = "Eval_IncPoints",
                    RequiredSignals = new List<string> { "IncidentCount" },
                    TokenSpec = null,
                    TextTemplate = "Incident points warning"
                }
            };

            foreach (var def in list)
            {
                ApplyDefaults(def);
            }

            RemoveExcludedMessages(list);
            return list;
        }

        private static void RemoveExcludedMessages(List<MessageDefinition> list)
        {
            if (list == null || list.Count == 0) return;
            list.RemoveAll(def => def != null && ExcludedMsgIds.Contains(def.MsgId ?? string.Empty));
        }
    }
}
