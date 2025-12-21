using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SimHub.Logging;

namespace LaunchPlugin.Messaging
{
    public static class MessageDefinitionStore
    {
        private const string FileName = "LalaLaunch.Messages.json";

        public static string GetFolderPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory?.TrimEnd('\\', '/');
            return Path.Combine(baseDir ?? string.Empty, "PluginsData", "Common");
        }

        public static string GetFilePath() => Path.Combine(GetFolderPath(), FileName);

        public static List<MessageDefinition> LoadOrCreateDefault()
        {
            try
            {
                var folder = GetFolderPath();
                var path = GetFilePath();

                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                if (!File.Exists(path))
                {
                    var defaults = BuildDefaultDefinitions();
                    SaveAll(defaults);
                    return defaults;
                }

                var json = File.ReadAllText(path);
                var list = JsonConvert.DeserializeObject<List<MessageDefinition>>(json) ?? new List<MessageDefinition>();
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

            var folder = GetFolderPath();
            var path = GetFilePath();
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var json = JsonConvert.SerializeObject(definitions, Formatting.Indented);
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

        // Hardcoded defaults derived from Message Catalog v5 (CSV contract)
        private static List<MessageDefinition> BuildDefaultDefinitions()
        {
            return new List<MessageDefinition>
            {
                new MessageDefinition
                {
                    MsgId = "fuel.pit_required_Caution",
                    Name = "Pit Soon",
                    Category = "Fuel",
                    Priority = MessagePriority.Med,
                    EnableOnLalaDash = false,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.DelayThenClear,
                    ActiveUntilDelayMs = 3000,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 5000,
                    EvaluatorId = "Eval_FuelPitRequired",
                    RequiredSignals = new List<string> { "FuelLapsRemaining" },
                    TokenSpec = "deficit-band",
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
                    EvaluatorId = "Eval_FuelPitRequired",
                    RequiredSignals = new List<string> { "FuelLapsRemaining" },
                    TokenSpec = "deficit-band",
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
                    TokenSpec = null,
                    TextTemplate = "Blue Flag {Time behind}"
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
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 5000,
                    EvaluatorId = "Eval_FuelCanPush",
                    RequiredSignals = new List<string> { "FuelCanPush" },
                    TokenSpec = null,
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
                    TokenSpec = "carId",
                    TextTemplate = "Car behind close {XXs}"
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
                    TokenSpec = "carId",
                    TextTemplate = "Under attack {XXm}"
                },
                new MessageDefinition
                {
                    MsgId = "traffic.fasterclass_behind",
                    Name = "Faster Class Approachng",
                    Category = "Traffic",
                    Priority = MessagePriority.Med,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.UntilStateChange,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 1000,
                    EvaluatorId = "Eval_FasterClassBehind",
                    RequiredSignals = new List<string> { "TrafficBehindGapSeconds", "TrafficBehindClass", "PlayerClassName" },
                    TokenSpec = "carId",
                    TextTemplate = "Faster Class {Class PXX} Approching {XXs}"
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
                    MsgId = "rejoin.threat_high",
                    Name = "Rejoin risk HIGH",
                    Category = "Rejoin",
                    Priority = MessagePriority.High,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.UntilStateChange,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 3000,
                    EvaluatorId = "Eval_RejoinThreatHigh",
                    RequiredSignals = new List<string> { "RejoinThreatLevel" },
                    TokenSpec = "reason-code",
                    TextTemplate = "Rejoin risk HIGH"
                },
                new MessageDefinition
                {
                    MsgId = "rejoin.threat_med",
                    Name = "Rejoin risk MED",
                    Category = "Rejoin",
                    Priority = MessagePriority.Med,
                    EnableOnLalaDash = true,
                    EnableOnMsgDash = true,
                    ActiveUntil = MessageActiveUntil.UntilStateChange,
                    ActiveUntilDelayMs = 0,
                    MsgCxAction = MessageCancelAction.UntilStateChange,
                    MsgCxDelayMs = 0,
                    MinOnTimeMS = 1000,
                    MinCoolDownMS = 3000,
                    EvaluatorId = "Eval_RejoinThreatMed",
                    RequiredSignals = new List<string> { "RejoinThreatLevel" },
                    TokenSpec = "reason-code",
                    TextTemplate = "Rejoin risk MED"
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
        }
    }
}
