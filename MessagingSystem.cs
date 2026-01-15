// MessagingSystem.cs
using FMOD;
using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace LaunchPlugin
{
    /// <summary>
    /// Shows a single line when a DIFFERENT-CLASS car behind is within WarnSeconds.
    /// Example (bind in Dash Studio as [LalaLaunch.MSG.OvertakeApproachLine]):
    ///   "P3 LMP2 3.4s"
    /// Empty string when nothing qualifies.
    /// </summary>
    public class MessagingSystem
    {
        public bool Enabled { get; set; } = false;           // LalaLaunch sets this each tick
        public double WarnSeconds { get; set; } = 5.0;       // Overwritten from per-car setting
        public int MaxScanBehind { get; set; } = 5;          // Fallback scans
        public string OvertakeApproachLine { get; private set; } = string.Empty;
        public double OtherClassBehindGap { get; private set; } = -1.0;

        // --- MsgCx placeholders (dash-controlled messaging lanes) ---

        /// <summary>
        /// Timed message lane (e.g., "BOX BOX"). When the driver presses MsgCx while this lane is active,
        /// we silence this lane only for <see cref="MsgCxTimeSilence"/>; other lanes remain eligible.
        /// </summary>
        public string MsgCxTimeMessage { get; private set; } = string.Empty;
        public TimeSpan MsgCxTimeSilence { get; set; } = TimeSpan.FromSeconds(8);
        public double MsgCxTimeSilenceRemainingSeconds
        {
            get
            {
                var remaining = _msgCxTimeSilencedUntilUtc - DateTime.UtcNow;
                return remaining > TimeSpan.Zero ? remaining.TotalSeconds : 0.0;
            }
        }
        public bool IsMsgCxTimeActive => !string.IsNullOrEmpty(MsgCxTimeMessage) && DateTime.UtcNow >= _msgCxTimeSilencedUntilUtc;

        /// <summary>
        /// State-change message lane (e.g., position changed). Pressing MsgCx hides the current state/token
        /// until the token changes; other lanes stay untouched.
        /// </summary>
        public string MsgCxStateMessage { get; private set; } = string.Empty;
        public string MsgCxStateToken { get; private set; } = string.Empty;
        public bool IsMsgCxStateActive =>
            !string.IsNullOrEmpty(MsgCxStateMessage) &&
            (!_msgCxStateCleared || !StringEqualsCI(_msgCxStateClearedToken, MsgCxStateToken));

        /// <summary>
        /// Action-trigger lane (e.g., "Refuel Not Selected" → jump to fuel dash). Pressing MsgCx raises
        /// <see cref="MsgCxActionRequested"/> and emits a short pulse.
        /// </summary>
        public string MsgCxActionMessage { get; private set; } = string.Empty;
        public bool MsgCxActionPulse { get; private set; }
        public event Action<string> MsgCxActionRequested;

        private DateTime _msgCxTimeSilencedUntilUtc = DateTime.MinValue;
        private DateTime _msgCxActionPulseUtc = DateTime.MinValue;
        private string _msgCxTimeCurrentKey = string.Empty;
        private string _msgCxStateClearedToken = string.Empty;
        private bool _msgCxStateCleared;
        private const double MsgCxActionPulseHoldSec = 0.5;

        // Very short hold to avoid flicker if the signal blips
        private DateTime _lastHitUtc = DateTime.MinValue;
        private const double HoldAfterMissSec = 0.35;

        public void Update(GameData data, PluginManager pm)
        {
            if (!Enabled || data?.NewData == null || pm == null || data.GameName != "IRacing")
            {
                OvertakeApproachLine = string.Empty;
                OtherClassBehindGap = -1.0;
                MaintainMsgCxTimers();
                return;
            }

            // 1) --- RSC FAST-PATH (RobRomain) -----------------------------------------
            // If these properties exist, use them. Otherwise skip to our own logic.
            string myClassRsc = GetString(pm, "IRacingExtraProperties.iRacing_Player_ClassName");
            double etaRsc = GetDouble(pm, "IRacingExtraProperties.iRacing_DriverBehind_00_RelativeGapToPlayer", double.NaN);
            string oppClassRsc = GetString(pm, "IRacingExtraProperties.iRacing_DriverBehind_00_ClassName");
            int oppPosClassRsc = GetInt(pm, "IRacingExtraProperties.iRacing_DriverBehind_00_PositionInClass", 0);

            // Guard the threshold locally too
            var gate = WarnSeconds;
            if (!(gate > 0)) gate = 5.0;     // fallback
            if (gate > 60) gate = 60;        // sanity cap

            if (!string.IsNullOrEmpty(myClassRsc) &&
                !string.IsNullOrEmpty(oppClassRsc) &&
                IsFinitePositive(etaRsc) &&
                etaRsc <= gate &&
                !StringEqualsCI(oppClassRsc, myClassRsc))
            {
                // Good to publish straight away
                OvertakeApproachLine = $"P{Math.Max(0, oppPosClassRsc)} {oppClassRsc} {etaRsc:0.0}s";
                OtherClassBehindGap = etaRsc;
                _lastHitUtc = DateTime.UtcNow;
                return;
            }

            // 2) --- FALLBACK A: SimHub OpponentsBehindOnTrack -------------------------
            // Use Opponents list (it’s already sorted by “behind”), minimal work.
            string myClass = data.NewData.CarClass ?? string.Empty;
            var behind = data.NewData.OpponentsBehindOnTrack;

            if (behind != null && behind.Count > 0)
            {
                double bestEta = double.MaxValue;
                string bestClass = null;
                int bestPos = 0;

                int scan = Math.Min(MaxScanBehind, behind.Count);
                for (int i = 0; i < scan; i++)
                {
                    var opp = behind[i];
                    if (opp == null) continue;

                    var oppClass = opp.CarClass ?? string.Empty;
                    if (StringEqualsCI(oppClass, myClass)) continue; // same class → skip

                    // Prefer RelativeGapToPlayer, fall back to GaptoPlayer (seconds)
                    double? eta = opp.RelativeGapToPlayer ?? opp.GaptoPlayer;
                    if (!eta.HasValue) continue;

                    double e = eta.Value;
                    if (!(e > 0) || e > gate) continue;

                    if (e < bestEta)
                    {
                        bestEta = e;
                        int posInClass = opp.PositionInClass > 0 ? opp.PositionInClass : opp.Position;
                        bestPos = Math.Max(0, posInClass);
                        bestClass = string.IsNullOrWhiteSpace(oppClass) ? "CLASS" : oppClass;
                    }
                }

                if (bestClass != null)
                {
                    OvertakeApproachLine = $"P{bestPos} {bestClass} {bestEta:0.0}s";
                    OtherClassBehindGap = bestEta;
                    _lastHitUtc = DateTime.UtcNow;
                    return;
                }
            }

            // 3) --- FALLBACK B: CarIdx arrays (no Opponents list available) ----------
            // Use CarIdxEstTime for ETA + DriverInfo for class & CarIdxClassPosition for pos.
            var estTimes = GetFloatArray(pm, "DataCorePlugin.GameRawData.Telemetry.CarIdxEstTime");
            if (estTimes != null && estTimes.Length > 0)
            {
                int playerIdx = GetInt(pm, "DataCorePlugin.GameRawData.Telemetry.PlayerCarIdx", 0);
                string myClassName = GetClassShortNameForCar(pm, playerIdx) ?? "";

                // Pit/surface filters
                var onPit = GetBoolArray(pm, "DataCorePlugin.GameRawData.Telemetry.CarIdxOnPitRoad");
                var surf = GetIntArray(pm, "DataCorePlugin.GameRawData.Telemetry.CarIdxTrackSurface");
                var classPos = GetIntArray(pm, "DataCorePlugin.GameRawData.Telemetry.CarIdxClassPosition");

                double bestEta = double.MaxValue;
                string bestClass = null;
                int bestPos = 0;

                // Pull up to MaxScanBehind smallest positive EstTime entries
                for (int idx = 0; idx < estTimes.Length; idx++)
                {
                    if (idx == playerIdx) continue;

                    double eta = estTimes[idx];
                    if (!(eta > 0) || eta > gate) continue;

                    if (onPit != null && idx < onPit.Length && onPit[idx]) continue;
                    if (surf != null && idx < surf.Length && surf[idx] <= 0) continue;

                    string oppClassShort = GetClassShortNameForCar(pm, idx) ?? "";
                    if (StringEqualsCI(oppClassShort, myClassName)) continue; // same class → skip

                    if (eta < bestEta)
                    {
                        bestEta = eta;
                        bestClass = string.IsNullOrWhiteSpace(oppClassShort) ? "CLASS" : oppClassShort;
                        bestPos = (classPos != null && idx < classPos.Length) ? Math.Max(0, classPos[idx]) : 0;
                    }
                }

                if (bestClass != null)
                {
                    OvertakeApproachLine = $"P{bestPos} {bestClass} {bestEta:0.0}s";
                    OtherClassBehindGap = bestEta;
                    _lastHitUtc = DateTime.UtcNow;
                    MaintainMsgCxTimers();
                    return;
                }
            }

            // Nothing qualified → clear with tiny hold
            ClearWithTinyHold();
            MaintainMsgCxTimers();
        }

        private void ClearWithTinyHold()
        {
            if ((DateTime.UtcNow - _lastHitUtc).TotalSeconds >= HoldAfterMissSec)
            {
                OvertakeApproachLine = string.Empty;
                OtherClassBehindGap = -1.0;
            }
        }

        // --- MsgCx helpers ----------------------------------------------------

        public void PublishTimedMessage(string message, TimeSpan? silence = null)
        {
            var key = message ?? string.Empty;

            // If the text changes, release any existing silence window immediately.
            if (!string.Equals(key, _msgCxTimeCurrentKey, StringComparison.Ordinal))
            {
                _msgCxTimeSilencedUntilUtc = DateTime.MinValue;
            }

            _msgCxTimeCurrentKey = key;
            MsgCxTimeMessage = key;
            if (silence.HasValue && silence.Value > TimeSpan.Zero)
            {
                MsgCxTimeSilence = silence.Value;
            }
        }

        public void PublishStateMessage(string message, string stateToken)
        {
            var token = stateToken ?? string.Empty;

            // When the token changes, the message should become visible again.
            if (!StringEqualsCI(token, MsgCxStateToken))
            {
                _msgCxStateCleared = false;
                _msgCxStateClearedToken = string.Empty;
            }

            MsgCxStateMessage = message ?? string.Empty;
            MsgCxStateToken = token;
        }

        public void PublishActionMessage(string message)
        {
            MsgCxActionMessage = message ?? string.Empty;
            if (string.IsNullOrEmpty(MsgCxActionMessage))
            {
                MsgCxActionPulse = false;
                _msgCxActionPulseUtc = DateTime.MinValue;
            }
        }

        public void TriggerMsgCx(bool includeAction = true)
        {
            // Priority order: time-cleared lane → state-cleared lane → action lane.
            // Only the active lane is affected so messages in other lanes remain eligible.
            if (IsMsgCxTimeActive)
            {
                TriggerTimedSilence();
                return;
            }

            if (IsMsgCxStateActive)
            {
                TriggerStateClear();
                return;
            }

            if (includeAction && !string.IsNullOrEmpty(MsgCxActionMessage))
            {
                TriggerAction();
            }
        }

        public void TriggerTimedSilence()
        {
            if (string.IsNullOrEmpty(MsgCxTimeMessage)) return;

            var duration = MsgCxTimeSilence > TimeSpan.Zero ? MsgCxTimeSilence : TimeSpan.FromSeconds(5);
            _msgCxTimeSilencedUntilUtc = DateTime.UtcNow + duration;
        }

        public void TriggerStateClear()
        {
            if (string.IsNullOrEmpty(MsgCxStateMessage)) return;

            _msgCxStateCleared = true;
            _msgCxStateClearedToken = MsgCxStateToken ?? string.Empty;
        }

        public void TriggerAction()
        {
            if (string.IsNullOrEmpty(MsgCxActionMessage)) return;

            MsgCxActionRequested?.Invoke(MsgCxActionMessage);
            _msgCxActionPulseUtc = DateTime.UtcNow;
            MsgCxActionPulse = true;
        }

        public void MaintainMsgCxTimers()
        {
            var now = DateTime.UtcNow;

            // Release time-silenced messages when the window expires.
            if (_msgCxTimeSilencedUntilUtc != DateTime.MinValue && now >= _msgCxTimeSilencedUntilUtc)
            {
                _msgCxTimeSilencedUntilUtc = DateTime.MinValue;
            }

            // Clear the action pulse after a brief hold so dashboards can latch it.
            if (MsgCxActionPulse && (now - _msgCxActionPulseUtc).TotalSeconds >= MsgCxActionPulseHoldSec)
            {
                MsgCxActionPulse = false;
            }

            // If the state token has changed while silenced, restore visibility immediately.
            if (_msgCxStateCleared && !StringEqualsCI(_msgCxStateClearedToken, MsgCxStateToken))
            {
                _msgCxStateCleared = false;
                _msgCxStateClearedToken = string.Empty;
            }
        }

        // ---------------- helpers (robust getters) ----------------

        private static bool StringEqualsCI(string a, string b) =>
            string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

        private static bool IsFinitePositive(double v) =>
            !double.IsNaN(v) && !double.IsInfinity(v) && v > 0;

        private static string GetString(PluginManager pm, string path)
        {
            try
            {
                var o = pm?.GetPropertyValue(path);
                return o?.ToString();
            }
            catch { return null; }
        }

        private static int GetInt(PluginManager pm, string path, int fallback = 0)
        {
            try
            {
                var o = pm?.GetPropertyValue(path);
                if (o == null) return fallback;
                if (o is int i) return i;
                if (o is long l) return (int)l;
                return int.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), out int parsed) ? parsed : fallback;
            }
            catch { return fallback; }
        }

        private static double GetDouble(PluginManager pm, string path, double fallback = double.NaN)
        {
            try
            {
                var o = pm?.GetPropertyValue(path);
                if (o == null) return fallback;
                if (o is double d) return d;
                if (o is float f) return f;
                if (o is int i) return i;
                if (o is long l) return l;
                return double.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture),
                                       NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                    ? parsed : fallback;
            }
            catch { return fallback; }
        }

        private static float[] GetFloatArray(PluginManager pm, string path)
        {
            try
            {
                var o = pm?.GetPropertyValue(path);
                if (o is float[] fa) return fa;
                if (o is double[] da) { var r = new float[da.Length]; for (int i = 0; i < da.Length; i++) r[i] = (float)da[i]; return r; }
                if (o is System.Collections.IEnumerable en)
                {
                    var list = new List<float>(64);
                    foreach (var x in en)
                    {
                        if (x is float f) list.Add(f);
                        else if (x is double d) list.Add((float)d);
                        else if (x is int i) list.Add(i);
                        else
                        {
                            if (float.TryParse(Convert.ToString(x, CultureInfo.InvariantCulture),
                                               NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                                list.Add(parsed);
                        }
                    }
                    return list.Count > 0 ? list.ToArray() : null;
                }
                return null;
            }
            catch { return null; }
        }

        private static int[] GetIntArray(PluginManager pm, string path)
        {
            try
            {
                var o = pm?.GetPropertyValue(path);
                if (o is int[] ia) return ia;
                if (o is long[] la) { var r = new int[la.Length]; for (int i = 0; i < la.Length; i++) r[i] = (int)la[i]; return r; }
                if (o is System.Collections.IEnumerable en)
                {
                    var list = new List<int>(64);
                    foreach (var x in en)
                    {
                        if (x is int i) list.Add(i);
                        else if (x is long l) list.Add((int)l);
                        else
                        {
                            if (int.TryParse(Convert.ToString(x, CultureInfo.InvariantCulture),
                                             NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                                list.Add(parsed);
                        }
                    }
                    return list.Count > 0 ? list.ToArray() : null;
                }
                return null;
            }
            catch { return null; }
        }

        // SessionData.DriverInfo lookup: map CarIdx → CarClassShortName
        private static string GetClassShortNameForCar(PluginManager pm, int carIdx)
        {
            if (pm == null) return null;
            for (int k = 0; k < 64; k++)
            {
                int idx = GetInt(pm, $"DataCorePlugin.GameRawData.SessionData.DriverInfo.CompetingDrivers[{k}].CarIdx", int.MinValue);
                if (idx == int.MinValue) break; // end
                if (idx == carIdx)
                {
                    var shortName = GetString(pm, $"DataCorePlugin.GameRawData.SessionData.DriverInfo.CompetingDrivers[{k}].CarClassShortName");
                    if (!string.IsNullOrWhiteSpace(shortName)) return shortName;
                    var longName = GetString(pm, $"DataCorePlugin.GameRawData.SessionData.DriverInfo.CompetingDrivers[{k}].CarClassName");
                    return longName;
                }
            }
            return null;
        }

        private static bool[] GetBoolArray(PluginManager pm, string path)
        {
            try
            {
                var o = pm?.GetPropertyValue(path);
                if (o == null) return null;

                if (o is bool[] ba) return ba;

                if (o is int[] ia)
                {
                    var r = new bool[ia.Length];
                    for (int i = 0; i < ia.Length; i++) r[i] = ia[i] != 0;
                    return r;
                }

                if (o is float[] fa)
                {
                    var r = new bool[fa.Length];
                    for (int i = 0; i < fa.Length; i++) r[i] = fa[i] != 0f;
                    return r;
                }

                // Generic enumerable fallback (object[], IList, etc.)
                if (o is System.Collections.IEnumerable en)
                {
                    var list = new List<bool>(64);
                    foreach (var x in en)
                    {
                        if (x is bool b) { list.Add(b); continue; }
                        if (x is int i) { list.Add(i != 0); continue; }
                        if (x is long l) { list.Add(l != 0L); continue; }
                        if (x is float f) { list.Add(f != 0f); continue; }
                        if (x is double d) { list.Add(d != 0.0); continue; }

                        // String/other: parse "true/false" or numeric "0/1"
                        var s = Convert.ToString(x, CultureInfo.InvariantCulture);
                        if (bool.TryParse(s, out bool bp)) { list.Add(bp); continue; }
                        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double dn))
                        {
                            list.Add(Math.Abs(dn) > double.Epsilon);
                            continue;
                        }
                        // If unparseable, treat as false
                        list.Add(false);
                    }
                    return list.Count > 0 ? list.ToArray() : null;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

    }
}
