using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LaunchPlugin
{
    public class OpponentsEngine
    {
        private readonly EntityCache _entityCache = new EntityCache();
        private readonly NearbySlotsTracker _nearby;
        private readonly ClassLeaderboardTracker _leaderboard;
        private readonly PitExitPredictor _pitExitPredictor;

        private bool _gateActive;
        private bool _gateOpenedLogged;
        private string _playerIdentityKey = string.Empty;

        public OpponentOutputs Outputs { get; } = new OpponentOutputs();

        public OpponentsEngine()
        {
            _nearby = new NearbySlotsTracker(_entityCache);
            _leaderboard = new ClassLeaderboardTracker(_entityCache);
            _pitExitPredictor = new PitExitPredictor(_leaderboard, Outputs.PitExit);
        }

        public void Reset()
        {
            _gateActive = false;
            _gateOpenedLogged = false;
            _playerIdentityKey = string.Empty;
            Outputs.Reset();
            _entityCache.Clear();
            _nearby.Reset();
            _leaderboard.Reset();
            _pitExitPredictor.Reset();
        }

        public void Update(GameData data, PluginManager pluginManager, bool isRaceSession, int completedLaps, double myPaceSec, double pitLossSec)
        {
            var _ = data; // intentional discard to keep signature aligned with caller
            string playerClassColor = SafeReadString(pluginManager, "IRacingExtraProperties.iRacing_Player_ClassColor");
            string playerCarNumber = SafeReadString(pluginManager, "IRacingExtraProperties.iRacing_Player_CarNumber");
            _playerIdentityKey = MakeIdentityKey(playerClassColor, playerCarNumber);

            if (!isRaceSession)
            {
                if (_gateActive || _entityCache.Count > 0)
                {
                    Reset();
                }
                return;
            }

            bool gateNow = completedLaps >= 2;
            bool allowLogs = gateNow;

            _nearby.Update(pluginManager, allowLogs);
            _leaderboard.Update(pluginManager);
            _pitExitPredictor.Update(_playerIdentityKey, pitLossSec, allowLogs);

            if (!gateNow)
            {
                _gateActive = false;
                Outputs.Reset();
                return;
            }

            if (!_gateActive)
            {
                _gateActive = true;
                if (!_gateOpenedLogged)
                {
                    SimHub.Logging.Current.Info("[LalaPlugin:Opponents] Opponents subsystem active (Race session + lap gate met).");
                    _gateOpenedLogged = true;
                }
            }

            double validatedMyPace = SanitizePace(myPaceSec);

            _nearby.PopulateOutputs(Outputs, validatedMyPace);
            Outputs.LeaderBlendedPaceSec = _leaderboard.GetBlendedPaceForPosition(1);
            Outputs.P2BlendedPaceSec = _leaderboard.GetBlendedPaceForPosition(2);
            var summaries = BuildSummaries(Outputs);
            Outputs.SummaryAhead = summaries.Ahead;
            Outputs.SummaryBehind = summaries.Behind;
            Outputs.SummaryAhead1 = summaries.Ahead1;
            Outputs.SummaryAhead2 = summaries.Ahead2;
            Outputs.SummaryBehind1 = summaries.Behind1;
            Outputs.SummaryBehind2 = summaries.Behind2;
        }

        public static string MakeIdentityKey(string classColor, string carNumber)
        {
            if (string.IsNullOrWhiteSpace(classColor) && string.IsNullOrWhiteSpace(carNumber))
            {
                return string.Empty;
            }

            string color = string.IsNullOrWhiteSpace(classColor) ? "?" : classColor.Trim();
            string number = string.IsNullOrWhiteSpace(carNumber) ? "?" : carNumber.Trim();
            return $"{color}:{number}";
        }

        private static OpponentSummaries BuildSummaries(OpponentOutputs outputs)
        {
            string ahead1 = BuildTargetSummary("A1", outputs.Ahead1, true);
            string ahead2 = BuildTargetSummary("A2", outputs.Ahead2, true);
            string behind1 = BuildTargetSummary("B1", outputs.Behind1, false);
            string behind2 = BuildTargetSummary("B2", outputs.Behind2, false);

            return new OpponentSummaries
            {
                Ahead = BuildSideSummary("Ahead", ahead1, ahead2),
                Behind = BuildSideSummary("Behind", behind1, behind2),
                Ahead1 = ahead1,
                Ahead2 = ahead2,
                Behind1 = behind1,
                Behind2 = behind2
            };
        }

        private static string BuildSideSummary(string label, params string[] slots)
        {
            if (slots == null || slots.Length == 0)
            {
                return string.Empty;
            }

            var populated = slots.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (populated.Count == 0)
            {
                return $"{label}: —";
            }

            return $"{label}:  {string.Join(" | ", populated)}";
        }

        private static string BuildTargetSummary(string label, OpponentTargetOutput target, bool isAhead)
        {
            if (target == null)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(target.Name) && string.IsNullOrWhiteSpace(target.CarNumber))
            {
                return $"{label} —";
            }

            string ident = !string.IsNullOrWhiteSpace(target.CarNumber) ? $"#{target.CarNumber}" : target.Name;
            string gap = FormatGap(target.GapToPlayerSec, isAhead);
            string delta = FormatDelta(target.PaceDeltaSecPerLap);
            string lapsToFight = FormatLapsToFight(target.LapsToFight);

            return $"{label} {ident} {gap} {delta} LTF={lapsToFight}";
        }

        private static string FormatGap(double gapSec, bool isAhead)
        {
            if (double.IsNaN(gapSec) || double.IsInfinity(gapSec) || gapSec <= 0.0)
            {
                return "—";
            }

            string signed = (isAhead ? "+" : "-") + gapSec.ToString("0.0", CultureInfo.InvariantCulture);
            return $"{signed}s";
        }

        private static string FormatDelta(double delta)
        {
            if (double.IsNaN(delta) || double.IsInfinity(delta))
            {
                return "Δ—";
            }

            string signed = delta.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
            return $"Δ{signed}s/L";
        }

        private static string FormatLapsToFight(double lapsToFight)
        {
            if (double.IsNaN(lapsToFight) || double.IsInfinity(lapsToFight) || lapsToFight <= 0.0)
            {
                return "—";
            }

            return lapsToFight.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static string SafeReadString(PluginManager pluginManager, string propertyName)
        {
            try
            {
                var raw = pluginManager?.GetPropertyValue(propertyName);
                return Convert.ToString(raw, CultureInfo.InvariantCulture);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static double SafeReadDouble(PluginManager pluginManager, string propertyName)
        {
            try
            {
                var raw = pluginManager?.GetPropertyValue(propertyName);
                return ConvertRawToDouble(raw);
            }
            catch
            {
                return double.NaN;
            }
        }

        private static double ConvertRawToDouble(object raw)
        {
            if (raw == null)
            {
                return double.NaN;
            }

            try
            {
                double ApplyLapCap(double value)
                {
                    if (double.IsNaN(value))
                    {
                        return value;
                    }

                    return value > 600.0 ? double.NaN : value;
                }

                if (raw is TimeSpan ts)
                {
                    return ApplyLapCap(ts.TotalSeconds);
                }

                if (raw is string s)
                {
                    var trimmed = s.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                    {
                        return double.NaN;
                    }

                    if (trimmed.Count(c => c == ':') == 1)
                    {
                        var parts = trimmed.Split(':');
                        if (parts.Length == 2
                            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
                            && double.TryParse(parts[1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var seconds))
                        {
                            return ApplyLapCap((minutes * 60.0) + seconds);
                        }
                    }

                    if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out var parsedTs))
                    {
                        return ApplyLapCap(parsedTs.TotalSeconds);
                    }

                    if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsedDouble))
                    {
                        return ApplyLapCap(parsedDouble);
                    }

                    return double.NaN;
                }

                if (raw is IConvertible)
                {
                    return ApplyLapCap(Convert.ToDouble(raw, CultureInfo.InvariantCulture));
                }
            }
            catch
            {
                return double.NaN;
            }

            return double.NaN;
        }

        private static double SanitizePace(double paceSec)
        {
            if (paceSec <= 0.0 || double.IsNaN(paceSec) || double.IsInfinity(paceSec) || paceSec > 10000.0)
            {
                return double.NaN;
            }

            return paceSec;
        }

        private class NearbySlotsTracker
        {
            private readonly EntityCache _cache;
            private readonly Dictionary<string, SlotSample> _slots = new Dictionary<string, SlotSample>
            {
                { "Ahead1", new SlotSample() },
                { "Ahead2", new SlotSample() },
                { "Behind1", new SlotSample() },
                { "Behind2", new SlotSample() }
            };

            private readonly Dictionary<string, string> _lastIdentityBySlot = new Dictionary<string, string>
            {
                { "Ahead1", string.Empty },
                { "Ahead2", string.Empty },
                { "Behind1", string.Empty },
                { "Behind2", string.Empty }
            };

            public NearbySlotsTracker(EntityCache cache)
            {
                _cache = cache;
            }

            public void Reset()
            {
                foreach (var key in _slots.Keys.ToList())
                {
                    _slots[key] = new SlotSample();
                }

                foreach (var key in _lastIdentityBySlot.Keys.ToList())
                {
                    _lastIdentityBySlot[key] = string.Empty;
                }
            }

            public void Update(PluginManager pluginManager, bool allowLogs)
            {
                ReadSlot(pluginManager, "Ahead1", "iRacing_DriverAheadInClass_00", allowLogs);
                ReadSlot(pluginManager, "Ahead2", "iRacing_DriverAheadInClass_01", allowLogs);
                ReadSlot(pluginManager, "Behind1", "iRacing_DriverBehindInClass_00", allowLogs);
                ReadSlot(pluginManager, "Behind2", "iRacing_DriverBehindInClass_01", allowLogs);
            }

            public void PopulateOutputs(OpponentOutputs outputs, double myPaceSec)
            {
                PopulateTarget(outputs.Ahead1, _slots["Ahead1"], myPaceSec, true);
                PopulateTarget(outputs.Ahead2, _slots["Ahead2"], myPaceSec, true);
                PopulateTarget(outputs.Behind1, _slots["Behind1"], myPaceSec, false);
                PopulateTarget(outputs.Behind2, _slots["Behind2"], myPaceSec, false);
            }

            private void PopulateTarget(OpponentTargetOutput target, SlotSample sample, double myPaceSec, bool isAhead)
            {
                target.Name = sample.Name;
                target.CarNumber = sample.CarNumber;
                target.ClassColor = sample.ClassColor;
                target.GapToPlayerSec = Math.Abs(sample.GapToPlayerSec);

                if (string.IsNullOrWhiteSpace(sample.IdentityKey))
                {
                    target.BlendedPaceSec = double.NaN;
                    target.PaceDeltaSecPerLap = double.NaN;
                    target.LapsToFight = double.NaN;
                    return;
                }

                var entity = _cache.Get(sample.IdentityKey);
                if (entity != null)
                {
                    entity.UpdateMetadata(sample.Name, sample.CarNumber, sample.ClassColor);
                    entity.IngestLapTimes(sample.LastLapSec, sample.BestLapSec, sample.IsInPit);
                    target.BlendedPaceSec = entity.GetBlendedPaceSec();
                }
                else
                {
                    target.BlendedPaceSec = double.NaN;
                }

                if (double.IsNaN(myPaceSec) || double.IsNaN(target.BlendedPaceSec) || target.BlendedPaceSec <= 0.0)
                {
                    target.PaceDeltaSecPerLap = double.NaN;
                    target.LapsToFight = double.NaN;
                    return;
                }

                double closingRate = isAhead
                    ? target.BlendedPaceSec - myPaceSec
                    : myPaceSec - target.BlendedPaceSec;

                target.PaceDeltaSecPerLap = closingRate;

                double gap = target.GapToPlayerSec;
                if (gap > 0.0 && closingRate > 0.05)
                {
                    double lapsToFight = gap / closingRate;
                    target.LapsToFight = lapsToFight > 999.0 ? 999.0 : lapsToFight;
                }
                else
                {
                    target.LapsToFight = double.NaN;
                }
            }

            private void ReadSlot(PluginManager pluginManager, string slotKey, string baseName, bool allowLogs)
            {
                string name = SafeReadString(pluginManager, $"IRacingExtraProperties.{baseName}_Name");
                string carNumber = SafeReadString(pluginManager, $"IRacingExtraProperties.{baseName}_CarNumber");
                string classColor = SafeReadString(pluginManager, $"IRacingExtraProperties.{baseName}_ClassColor");

                double gapToPlayer = SafeReadDouble(pluginManager, $"IRacingExtraProperties.{baseName}_RelativeGapToPlayer");
                double lastLap = SafeReadDouble(pluginManager, $"IRacingExtraProperties.{baseName}_LastLapTime");
                double bestLap = SafeReadDouble(pluginManager, $"IRacingExtraProperties.{baseName}_BestLapTime");
                bool isInPit = SafeReadBool(pluginManager, $"IRacingExtraProperties.{baseName}_IsInPit");
                bool isConnected = SafeReadBool(pluginManager, $"IRacingExtraProperties.{baseName}_IsConnected");

                string identity = MakeIdentityKey(classColor, carNumber);
                var sample = new SlotSample
                {
                    IdentityKey = identity,
                    Name = name,
                    CarNumber = carNumber,
                    ClassColor = classColor,
                    GapToPlayerSec = gapToPlayer,
                    LastLapSec = lastLap,
                    BestLapSec = bestLap,
                    IsInPit = isInPit,
                    IsConnected = isConnected
                };

                _slots[slotKey] = sample;
                _cache.Touch(sample);

                string lastIdentity = _lastIdentityBySlot[slotKey];
                if (!string.Equals(identity, lastIdentity, StringComparison.Ordinal) && allowLogs)
                {
                    _lastIdentityBySlot[slotKey] = identity;
                    if (!string.IsNullOrWhiteSpace(identity))
                    {
                        SimHub.Logging.Current.Info($"[LalaPlugin:Opponents] Slot {slotKey} rebound -> {identity} ({name})");
                    }
                }
                else if (string.IsNullOrWhiteSpace(identity))
                {
                    _lastIdentityBySlot[slotKey] = identity;
                }
            }

            private static bool SafeReadBool(PluginManager pluginManager, string propertyName)
            {
                try
                {
                    var raw = pluginManager?.GetPropertyValue(propertyName);
                    return Convert.ToBoolean(raw);
                }
                catch
                {
                    return false;
                }
            }
        }

        private class ClassLeaderboardTracker
        {
            private readonly EntityCache _cache;
            private readonly List<LeaderboardRow> _rows = new List<LeaderboardRow>();

            public ClassLeaderboardTracker(EntityCache cache)
            {
                _cache = cache;
            }

            public IReadOnlyList<LeaderboardRow> Rows => _rows;

            public void Reset()
            {
                _rows.Clear();
            }

            public void Update(PluginManager pluginManager)
            {
                _rows.Clear();

                for (int i = 0; i < 64; i++)
                {
                    string suffix = i.ToString("00", CultureInfo.InvariantCulture);
                    string baseName = $"IRacingExtraProperties.iRacing_ClassLeaderboard_Driver_{suffix}";
                    string name = SafeReadString(pluginManager, $"{baseName}_Name");
                    string carNumber = SafeReadString(pluginManager, $"{baseName}_CarNumber");
                    string classColor = SafeReadString(pluginManager, $"{baseName}_ClassColor");

                    if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(carNumber) && string.IsNullOrWhiteSpace(classColor))
                    {
                        break;
                    }

                    var row = new LeaderboardRow
                    {
                        IdentityKey = MakeIdentityKey(classColor, carNumber),
                        Name = name,
                        CarNumber = carNumber,
                        ClassColor = classColor,
                        PositionInClass = SafeReadInt(pluginManager, $"{baseName}_PositionInClass"),
                        RelativeGapToLeader = SafeReadDouble(pluginManager, $"{baseName}_RelativeGapToLeader"),
                        IsInPit = SafeReadBool(pluginManager, $"{baseName}_IsInPit"),
                        IsConnected = SafeReadBool(pluginManager, $"{baseName}_IsConnected"),
                        LastLapSec = SafeReadDouble(pluginManager, $"{baseName}_LastLapTime"),
                        BestLapSec = SafeReadDouble(pluginManager, $"{baseName}_BestLapTime")
                    };

                    _rows.Add(row);
                    _cache.Touch(row.IdentityKey, row.Name, row.CarNumber, row.ClassColor);
                    _cache.Get(row.IdentityKey)?.IngestLapTimes(row.LastLapSec, row.BestLapSec, row.IsInPit);
                }
            }

            public double GetBlendedPaceForPosition(int positionInClass)
            {
                if (positionInClass <= 0) return double.NaN;

                var row = _rows.FirstOrDefault(r => r.PositionInClass == positionInClass);
                if (row == null || string.IsNullOrWhiteSpace(row.IdentityKey))
                {
                    return double.NaN;
                }

                var entity = _cache.Get(row.IdentityKey);
                return entity?.GetBlendedPaceSec() ?? double.NaN;
            }

            private static string SafeReadString(PluginManager pluginManager, string propertyName)
            {
                try
                {
                    var raw = pluginManager?.GetPropertyValue(propertyName);
                    return Convert.ToString(raw, CultureInfo.InvariantCulture);
                }
                catch
                {
                    return string.Empty;
                }
            }

            private static int SafeReadInt(PluginManager pluginManager, string propertyName)
            {
                try
                {
                    var raw = pluginManager?.GetPropertyValue(propertyName);
                    return Convert.ToInt32(raw);
                }
                catch
                {
                    return 0;
                }
            }

            private static bool SafeReadBool(PluginManager pluginManager, string propertyName)
            {
                try
                {
                    var raw = pluginManager?.GetPropertyValue(propertyName);
                    return Convert.ToBoolean(raw);
                }
                catch
                {
                    return false;
                }
            }
        }

        private class PitExitPredictor
        {
            private readonly ClassLeaderboardTracker _leaderboard;
            private readonly PitExitOutput _output;

            private bool _lastValid;
            private int _lastPredictedPos;

            public PitExitPredictor(ClassLeaderboardTracker leaderboard, PitExitOutput output)
            {
                _leaderboard = leaderboard;
                _output = output;
            }

            public void Reset()
            {
                _lastValid = false;
                _lastPredictedPos = 0;
                _output.Reset();
            }

            public void Update(string playerIdentityKey, double pitLossSec, bool allowLogs)
            {
                var rows = _leaderboard.Rows;
                if (rows == null || rows.Count == 0 || string.IsNullOrWhiteSpace(playerIdentityKey))
                {
                    SetInvalid(allowLogs);
                    return;
                }

                var playerRow = rows.FirstOrDefault(r => string.Equals(r.IdentityKey, playerIdentityKey, StringComparison.Ordinal));
                if (playerRow == null || string.IsNullOrWhiteSpace(playerRow.ClassColor))
                {
                    SetInvalid(allowLogs);
                    return;
                }

                double playerGapToLeader = playerRow.RelativeGapToLeader;
                if (double.IsNaN(playerGapToLeader) || double.IsInfinity(playerGapToLeader))
                {
                    SetInvalid(allowLogs);
                    return;
                }

                double pitLoss = (pitLossSec > 0.0 && !double.IsNaN(pitLossSec) && !double.IsInfinity(pitLossSec)) ? pitLossSec : 0.0;

                int carsAheadAfterPit = 0;

                foreach (var row in rows)
                {
                    if (row.IdentityKey == playerIdentityKey) continue;
                    if (!string.Equals(row.ClassColor, playerRow.ClassColor, StringComparison.Ordinal)) continue;
                    if (!row.IsConnected) continue;

                    double oppGapToPlayer = row.RelativeGapToLeader - playerGapToLeader;
                    double predictedGap = oppGapToPlayer - pitLoss;
                    if (predictedGap < 0.0)
                    {
                        carsAheadAfterPit++;
                    }
                }

                int predictedPos = 1 + carsAheadAfterPit;

                _output.Valid = true;
                _output.PredictedPositionInClass = predictedPos;
                _output.CarsAheadAfterPitCount = carsAheadAfterPit;
                _output.Summary = $"PitExit: P{predictedPos} after stop (ahead={carsAheadAfterPit}, loss={pitLoss:F1}s)";

                if (_output.Valid != _lastValid && allowLogs)
                {
                    SimHub.Logging.Current.Info($"[LalaPlugin:PitExit] Predictor valid -> true (pitLoss={pitLoss:F1}s)");
                }
                else if (_output.Valid && predictedPos != _lastPredictedPos && allowLogs)
                {
                    SimHub.Logging.Current.Info($"[LalaPlugin:PitExit] Predicted class position changed -> P{predictedPos} (ahead={carsAheadAfterPit})");
                }

                _lastValid = _output.Valid;
                _lastPredictedPos = predictedPos;
            }

            private void SetInvalid(bool allowLogs)
            {
                if (_lastValid && allowLogs)
                {
                    SimHub.Logging.Current.Info("[LalaPlugin:PitExit] Predictor valid -> false");
                }

                _output.Reset();
                _lastValid = false;
                _lastPredictedPos = 0;
            }
        }

        private class EntityCache
        {
            private readonly Dictionary<string, OpponentEntity> _entities = new Dictionary<string, OpponentEntity>();

            public int Count => _entities.Count;

            public void Clear()
            {
                _entities.Clear();
            }

            public OpponentEntity Get(string identityKey)
            {
                if (string.IsNullOrWhiteSpace(identityKey)) return null;
                _entities.TryGetValue(identityKey, out var entity);
                return entity;
            }

            public void Touch(SlotSample sample)
            {
                if (string.IsNullOrWhiteSpace(sample.IdentityKey))
                {
                    return;
                }

                Touch(sample.IdentityKey, sample.Name, sample.CarNumber, sample.ClassColor);
            }

            public OpponentEntity Touch(string identityKey, string name, string carNumber, string classColor)
            {
                if (string.IsNullOrWhiteSpace(identityKey))
                {
                    return null;
                }

                if (!_entities.TryGetValue(identityKey, out var entity))
                {
                    entity = new OpponentEntity(identityKey);
                    _entities[identityKey] = entity;
                }

                entity.UpdateMetadata(name, carNumber, classColor);
                return entity;
            }
        }

        private class OpponentEntity
        {
            private const int PaceWindowSize = 5;
            private const double InvalidLapThreshold = 10000.0;

            private readonly double[] _recentLaps = new double[PaceWindowSize];
            private int _lapCount;
            private int _lapIndex;

            public OpponentEntity(string identityKey)
            {
                IdentityKey = identityKey;
            }

            public string IdentityKey { get; }
            public string Name { get; private set; } = string.Empty;
            public string CarNumber { get; private set; } = string.Empty;
            public string ClassColor { get; private set; } = string.Empty;
            public double BestLapSec { get; private set; } = double.NaN;

            public void UpdateMetadata(string name, string carNumber, string classColor)
            {
                if (!string.IsNullOrWhiteSpace(name)) Name = name;
                if (!string.IsNullOrWhiteSpace(carNumber)) CarNumber = carNumber;
                if (!string.IsNullOrWhiteSpace(classColor)) ClassColor = classColor;
            }

            public void IngestLapTimes(double lastLapSec, double bestLapSec, bool isInPit)
            {
                if (bestLapSec > 0.0 && bestLapSec < InvalidLapThreshold)
                {
                    if (double.IsNaN(BestLapSec) || bestLapSec < BestLapSec)
                    {
                        BestLapSec = bestLapSec;
                    }
                }

                if (isInPit)
                {
                    return;
                }

                if (lastLapSec <= 0.0 || double.IsNaN(lastLapSec) || double.IsInfinity(lastLapSec) || lastLapSec > InvalidLapThreshold)
                {
                    return;
                }

                _recentLaps[_lapIndex] = lastLapSec;
                _lapIndex = (_lapIndex + 1) % PaceWindowSize;
                if (_lapCount < PaceWindowSize)
                {
                    _lapCount++;
                }
            }

            public double GetRecentAverage()
            {
                if (_lapCount == 0) return double.NaN;
                double sum = 0.0;
                for (int i = 0; i < _lapCount; i++)
                {
                    sum += _recentLaps[i];
                }
                return sum / _lapCount;
            }

            public double GetBlendedPaceSec()
            {
                double recent = GetRecentAverage();
                bool hasRecent = !double.IsNaN(recent) && recent > 0.0;

                bool hasBest = !double.IsNaN(BestLapSec) && BestLapSec > 0.0;
                double bestAdjusted = hasBest ? BestLapSec * 1.01 : double.NaN;

                if (hasRecent && hasBest)
                {
                    return (0.70 * recent) + (0.30 * bestAdjusted);
                }

                if (hasRecent)
                {
                    return recent;
                }

                if (hasBest)
                {
                    return bestAdjusted;
                }

                return double.NaN;
            }
        }

        public class OpponentOutputs
        {
            public OpponentOutputs()
            {
                PitExit = new PitExitOutput();
            }

            public OpponentTargetOutput Ahead1 { get; } = new OpponentTargetOutput();
            public OpponentTargetOutput Ahead2 { get; } = new OpponentTargetOutput();
            public OpponentTargetOutput Behind1 { get; } = new OpponentTargetOutput();
            public OpponentTargetOutput Behind2 { get; } = new OpponentTargetOutput();
            public OpponentTargetOutput Leader { get; } = new OpponentTargetOutput();
            public OpponentTargetOutput P2 { get; } = new OpponentTargetOutput();
            public PitExitOutput PitExit { get; }
            public string SummaryAhead { get; set; } = string.Empty;
            public string SummaryBehind { get; set; } = string.Empty;
            public string SummaryAhead1 { get; set; } = string.Empty;
            public string SummaryAhead2 { get; set; } = string.Empty;
            public string SummaryBehind1 { get; set; } = string.Empty;
            public string SummaryBehind2 { get; set; } = string.Empty;
            public double LeaderBlendedPaceSec { get; set; } = double.NaN;
            public double P2BlendedPaceSec { get; set; } = double.NaN;

            public void Reset()
            {
                Ahead1.Reset();
                Ahead2.Reset();
                Behind1.Reset();
                Behind2.Reset();
                Leader.Reset();
                P2.Reset();
                PitExit.Reset();
                SummaryAhead = string.Empty;
                SummaryBehind = string.Empty;
                SummaryAhead1 = string.Empty;
                SummaryAhead2 = string.Empty;
                SummaryBehind1 = string.Empty;
                SummaryBehind2 = string.Empty;
                LeaderBlendedPaceSec = double.NaN;
                P2BlendedPaceSec = double.NaN;
            }
        }

        public class OpponentSummaries
        {
            public string Ahead { get; set; } = string.Empty;
            public string Behind { get; set; } = string.Empty;
            public string Ahead1 { get; set; } = string.Empty;
            public string Ahead2 { get; set; } = string.Empty;
            public string Behind1 { get; set; } = string.Empty;
            public string Behind2 { get; set; } = string.Empty;
        }

        public class OpponentTargetOutput
        {
            public string Name { get; set; } = string.Empty;
            public string CarNumber { get; set; } = string.Empty;
            public string ClassColor { get; set; } = string.Empty;
            public double GapToPlayerSec { get; set; } = 0.0;
            public double BlendedPaceSec { get; set; } = double.NaN;
            public double PaceDeltaSecPerLap { get; set; } = double.NaN;
            public double LapsToFight { get; set; } = double.NaN;

            public void Reset()
            {
                Name = string.Empty;
                CarNumber = string.Empty;
                ClassColor = string.Empty;
                GapToPlayerSec = 0.0;
                BlendedPaceSec = double.NaN;
                PaceDeltaSecPerLap = double.NaN;
                LapsToFight = double.NaN;
            }
        }

        public class PitExitOutput
        {
            public bool Valid { get; set; }
            public int PredictedPositionInClass { get; set; }
            public int CarsAheadAfterPitCount { get; set; }
            public string Summary { get; set; } = string.Empty;

            public void Reset()
            {
                Valid = false;
                PredictedPositionInClass = 0;
                CarsAheadAfterPitCount = 0;
                Summary = string.Empty;
            }
        }

        private class SlotSample
        {
            public string IdentityKey { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string CarNumber { get; set; } = string.Empty;
            public string ClassColor { get; set; } = string.Empty;
            public double GapToPlayerSec { get; set; }
            public double LastLapSec { get; set; }
            public double BestLapSec { get; set; }
            public bool IsInPit { get; set; }
            public bool IsConnected { get; set; }
        }

        private class LeaderboardRow
        {
            public string IdentityKey { get; set; }
            public string Name { get; set; }
            public string CarNumber { get; set; }
            public string ClassColor { get; set; }
            public int PositionInClass { get; set; }
            public double RelativeGapToLeader { get; set; }
            public bool IsInPit { get; set; }
            public bool IsConnected { get; set; }
            public double LastLapSec { get; set; }
            public double BestLapSec { get; set; }
        }
    }
}
