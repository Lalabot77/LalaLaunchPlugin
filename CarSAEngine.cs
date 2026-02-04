using System;
using System.Collections.Generic;

namespace LaunchPlugin
{
    // CarSAEngine is slot-centric: Ahead/Behind slots carry state and are rebound as candidates change.
    // SA-Core v2 intent: keep track-awareness + StatusE logic; remove race-gap checkpoint logic
    // (RealGap*, GapRace/GapReal) which are NOT USED BY SA-CORE.
    public class CarSAEngine
    {
        public const int SlotsAhead = 5;
        public const int SlotsBehind = 5;
        public const int MaxCars = 64;

        private const double DefaultLapTimeEstimateSec = 120.0;
        private const double HysteresisFactor = 0.90;
        private const double ClosingRateClamp = 5.0;
        private const double HalfLapFilterMin = 0.40;
        private const double HalfLapFilterMax = 0.60;
        private const double LapDeltaWrapEdgePct = 0.05;
        private const int TrackSurfaceUnknown = int.MinValue;
        private const int TrackSurfaceNotInWorld = -1;
        private const int TrackSurfaceOffTrack = 0;
        private const int TrackSurfacePitStallOrTow = 1;
        private const int TrackSurfacePitLane = 2;
        private const int TrackSurfaceOnTrack = 3;
        private const string StatusShortUnknown = "UNK";
        private const string StatusShortOutLap = "OUT";
        private const string StatusShortInPits = "PIT";
        private const string StatusShortCompromisedOffTrack = "OFF";
        private const string StatusShortCompromisedPenalty = "PEN";
        private const string StatusShortFasterClass = "FCL";
        private const string StatusShortSlowerClass = "SCL";
        private const string StatusShortRacing = "RCE";
        private const string StatusShortHotLap = "HOT";
        private const string StatusShortCoolLap = "COL";
        private const string StatusLongUnknown = "";
        private const string StatusLongOutLap = "Out lap";
        private const string StatusLongInPits = "In pits";
        private const string StatusLongCompromisedOffTrack = "Lap Invalid";
        private const string StatusLongCompromisedPenalty = "Penalty";
        private const string StatusLongFasterClass = "Faster class";
        private const string StatusLongSlowerClass = "Slower class";
        private const string StatusLongRacing = "Racing";
        private const string StatusLongHotLap = "Hot lap";
        private const string StatusLongCoolLap = "Cool lap";
        private const string StatusEReasonPits = "pits";
        private const string StatusEReasonCompromisedOffTrack = "cmp_off";
        private const string StatusEReasonCompromisedPenalty = "cmp_pen";
        private const string StatusEReasonOutLap = "outlap";
        private const string StatusEReasonLapAhead = "lap_ahead";
        private const string StatusEReasonLapBehind = "lap_behind";
        private const string StatusEReasonRacing = "racing";
        private const string StatusEReasonOtherClass = "otherclass";
        private const string StatusEReasonOtherClassUnknownRank = "otherclass_unknownrank";
        private const string StatusEReasonUnknown = "unknown";
        private const int SessionFlagBlack = 0x00010000;
        private const int SessionFlagFurled = 0x00020000;
        private const int SessionFlagRepair = 0x00080000;
        private const int SessionFlagDisqualify = 0x00100000;
        private const int SessionFlagMaskCompromised = 0x00010000 | 0x00080000 | 0x00100000 | 0x00020000;

        private static int NormalizeTrackSurfaceRaw(int raw)
        {
            return raw == TrackSurfaceUnknown ? TrackSurfaceNotInWorld : raw;
        }

        private static bool IsPitAreaSurface(int raw)
        {
            return raw == TrackSurfacePitStallOrTow || raw == TrackSurfacePitLane;
        }

        private static bool IsOnTrackSurface(int raw)
        {
            return raw == TrackSurfaceOnTrack;
        }

        private static bool IsNotInWorldSurface(int raw)
        {
            return raw == TrackSurfaceNotInWorld;
        }

        private static bool IsPitStallOrTowSurface(int raw)
        {
            return raw == TrackSurfacePitStallOrTow;
        }

        private static bool IsPitLaneSurface(int raw)
        {
            return raw == TrackSurfacePitLane;
        }

        private readonly CarSAOutputs _outputs;
        private readonly int[] _aheadCandidateIdx;
        private readonly int[] _behindCandidateIdx;
        private readonly double[] _aheadCandidateDist;
        private readonly double[] _behindCandidateDist;
        private readonly bool _includePitRoad;
        // === Car-centric shadow state (authoritative StatusE cache) ============
        private readonly CarSA_CarState[] _carStates;
        private bool _loggedEnabled;
        private Dictionary<string, int> _classRankByColor;
        private int _lastSessionState = -1;
        private string _lastSessionTypeName = null;
        private double _sessionTypeStartTimeSec = double.NaN;
        private double _lastSessionTimeSec = double.NaN;
        private bool _allowStatusEThisTick = true;

        private sealed class CarSA_CarState
        {
            public int CarIdx { get; set; } = -1;
            public double LastSeenSessionTime { get; set; } = double.NaN;
            public int Lap { get; set; }
            public double LapDistPct { get; set; } = double.NaN;
            public double SignedDeltaPct { get; set; } = double.NaN;
            public double ForwardDistPct { get; set; } = double.NaN;
            public double BackwardDistPct { get; set; } = double.NaN;
            public double ClosingRateSecPerSec { get; set; } = double.NaN;
            public bool HasDeltaPct { get; set; }
            public double LastDeltaPct { get; set; } = double.NaN;
            public double LastGapPctAbs { get; set; } = double.NaN;
            public double LastDeltaUpdateTime { get; set; } = double.NaN;
            public double LastValidSessionTime { get; set; } = double.NaN;
            public bool IsOnTrack { get; set; }
            public bool IsOnPitRoad { get; set; }
            public int TrackSurfaceRaw { get; set; } = TrackSurfaceUnknown;
            public int SessionFlagsRaw { get; set; } = -1;
            public double LastInWorldSessionTimeSec { get; set; } = double.NaN;
            public int LastLapSeen { get; set; } = int.MinValue;
            public bool WasInPitArea { get; set; }
            public int CompromisedUntilLap { get; set; } = int.MinValue;
            public int OutLapUntilLap { get; set; } = int.MinValue;
            public bool CompromisedOffTrackActive { get; set; }
            public bool OutLapActive { get; set; }
            public bool CompromisedPenaltyActive { get; set; }
            public int OffTrackStreak { get; set; }
            public double OffTrackFirstSeenTimeSec { get; set; } = double.NaN;
            public int LapsSincePit { get; set; } = -1;

            public void Reset(int carIdx)
            {
                CarIdx = carIdx;
                LastSeenSessionTime = double.NaN;
                Lap = 0;
                LapDistPct = double.NaN;
                SignedDeltaPct = double.NaN;
                ForwardDistPct = double.NaN;
                BackwardDistPct = double.NaN;
                ClosingRateSecPerSec = double.NaN;
                HasDeltaPct = false;
                LastDeltaPct = double.NaN;
                LastGapPctAbs = double.NaN;
                LastDeltaUpdateTime = double.NaN;
                LastValidSessionTime = double.NaN;
                IsOnTrack = false;
                IsOnPitRoad = false;
                TrackSurfaceRaw = TrackSurfaceUnknown;
                SessionFlagsRaw = -1;
                LastInWorldSessionTimeSec = double.NaN;
                LastLapSeen = int.MinValue;
                WasInPitArea = false;
                CompromisedUntilLap = int.MinValue;
                OutLapUntilLap = int.MinValue;
                CompromisedOffTrackActive = false;
                OutLapActive = false;
                CompromisedPenaltyActive = false;
                OffTrackStreak = 0;
                OffTrackFirstSeenTimeSec = double.NaN;
                LapsSincePit = -1;
            }
        }

        public CarSAEngine()
        {
            _outputs = new CarSAOutputs(SlotsAhead, SlotsBehind);
            _aheadCandidateIdx = new int[SlotsAhead];
            _behindCandidateIdx = new int[SlotsBehind];
            _aheadCandidateDist = new double[SlotsAhead];
            _behindCandidateDist = new double[SlotsBehind];
            _includePitRoad = false;
            _carStates = new CarSA_CarState[MaxCars];
            for (int i = 0; i < _carStates.Length; i++)
            {
                _carStates[i] = new CarSA_CarState();
                _carStates[i].Reset(i);
            }
        }

        public CarSAOutputs Outputs => _outputs;

        public void SetClassRankMap(Dictionary<string, int> classRankByColor)
        {
            if (classRankByColor == null || classRankByColor.Count == 0)
            {
                _classRankByColor = null;
                return;
            }

            _classRankByColor = new Dictionary<string, int>(classRankByColor, StringComparer.OrdinalIgnoreCase);
        }

        // === StatusE logic ======================================================
        public void RefreshStatusE(double notRelevantGapSec, OpponentsEngine.OpponentOutputs opponentOutputs, string playerClassColor)
        {
            double sessionTimeSec = _outputs?.Debug?.SessionTimeSec ?? 0.0;
            if (!_allowStatusEThisTick)
            {
                bool isHardOff = IsHardOffSessionType(_lastSessionTypeName);
                bool isUnknownSession = IsUnknownSessionType(_lastSessionTypeName);
                if (isHardOff || isUnknownSession)
                {
                    string reason = isUnknownSession ? "sess_unknown" : "sess_off";
                    ApplyForcedStatusE(_outputs.AheadSlots, reason);
                    ApplyForcedStatusE(_outputs.BehindSlots, reason);
                }
                else
                {
                    ApplyGatedStatusE(_outputs.AheadSlots);
                    ApplyGatedStatusE(_outputs.BehindSlots);
                }
                return;
            }
            UpdateStatusE(_outputs.AheadSlots, notRelevantGapSec, true, opponentOutputs, playerClassColor, sessionTimeSec, _classRankByColor);
            UpdateStatusE(_outputs.BehindSlots, notRelevantGapSec, false, opponentOutputs, playerClassColor, sessionTimeSec, _classRankByColor);
            ApplySessionTypeStatusEPolicy(_outputs.AheadSlots);
            ApplySessionTypeStatusEPolicy(_outputs.BehindSlots);
        }

        public void Reset()
        {
            _loggedEnabled = false;
            _outputs.ResetAll();
            ResetCarStates();
            _lastSessionState = -1;
            _lastSessionTypeName = null;
            _sessionTypeStartTimeSec = double.NaN;
            _lastSessionTimeSec = double.NaN;
            _allowStatusEThisTick = true;
        }

        // === SA / track-awareness + slot assignment =============================
        public void Update(
            double sessionTimeSec,
            int sessionState,
            string sessionTypeName,
            int playerCarIdx,
            float[] carIdxLapDistPct,
            int[] carIdxLap,
            int[] carIdxTrackSurface,
            bool[] carIdxOnPitRoad,
            int[] carIdxSessionFlags,
            int[] carIdxPaceFlags,
            double lapTimeEstimateSec,
            double notRelevantGapSec,
            bool debugEnabled)
        {
            _ = notRelevantGapSec;
            _outputs.Source = "CarIdxTruth";
            _outputs.Debug.SessionTimeSec = sessionTimeSec;
            _outputs.Debug.SourceFastPathUsed = false;

            int carCount = carIdxLapDistPct != null ? carIdxLapDistPct.Length : 0;
            int onPitRoadCount = 0;
            int onTrackCount = 0;
            int invalidLapPctCount = 0;
            int timestampUpdates = 0;
            int filteredHalfLapAhead = 0;
            int filteredHalfLapBehind = 0;
            double lapTimeUsed = lapTimeEstimateSec;
            if (!(lapTimeUsed > 0.0) || double.IsNaN(lapTimeUsed) || double.IsInfinity(lapTimeUsed))
            {
                lapTimeUsed = DefaultLapTimeEstimateSec;
            }

            bool sessionTypeChanged = !string.Equals(sessionTypeName ?? string.Empty, _lastSessionTypeName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            if (sessionTypeChanged)
            {
                _sessionTypeStartTimeSec = sessionTimeSec;
            }
            if (!double.IsNaN(_lastSessionTimeSec) && (_lastSessionTimeSec - sessionTimeSec) > 1.0)
            {
                _sessionTypeStartTimeSec = sessionTimeSec;
            }
            _lastSessionTimeSec = sessionTimeSec;
            _lastSessionTypeName = sessionTypeName;

            bool isRace = IsRaceSessionType(sessionTypeName);
            bool isPracticeOrQual = IsPracticeOrQualSessionType(sessionTypeName);
            bool isHardOff = IsHardOffSessionType(sessionTypeName);
            bool allowStatusE = true;
            bool allowLatches = true;
            if (isHardOff)
            {
                allowStatusE = false;
                allowLatches = false;
            }
            if (!isHardOff && isRace && sessionState < 4)
            {
                allowStatusE = false;
                allowLatches = false;
            }
            if (isRace && _lastSessionState < 4 && sessionState >= 4)
            {
                ResetCarLatchesOnly();
            }
            if (!isHardOff && isPracticeOrQual)
            {
                if (double.IsNaN(_sessionTypeStartTimeSec))
                {
                    _sessionTypeStartTimeSec = sessionTimeSec;
                }
                double age = sessionTimeSec - _sessionTypeStartTimeSec;
                if (age < 5.0)
                {
                    allowStatusE = false;
                    allowLatches = false;
                }
            }
            _allowStatusEThisTick = allowStatusE;

            double playerLapPct = double.NaN;
            bool playerLapPctValid = false;
            if (carCount > 0 && playerCarIdx >= 0 && playerCarIdx < carCount)
            {
                playerLapPct = carIdxLapDistPct[playerCarIdx];
                playerLapPctValid = !double.IsNaN(playerLapPct) && playerLapPct >= 0.0 && playerLapPct < 1.0;
            }

            int playerTrackSurfaceRaw = -1;
            if (carIdxTrackSurface != null && playerCarIdx >= 0 && playerCarIdx < carIdxTrackSurface.Length)
            {
                int surface = carIdxTrackSurface[playerCarIdx];
                playerTrackSurfaceRaw = NormalizeTrackSurfaceRaw(surface);
            }
            _outputs.Debug.PlayerTrackSurfaceRaw = playerTrackSurfaceRaw;

            UpdateCarStates(sessionTimeSec, carIdxLapDistPct, carIdxLap, carIdxTrackSurface, carIdxOnPitRoad, carIdxSessionFlags, carIdxPaceFlags, playerLapPctValid ? playerLapPct : double.NaN, lapTimeUsed, allowLatches);

            if (carCount <= 0 || playerCarIdx < 0 || playerCarIdx >= carCount)
            {
                InvalidateOutputs(playerCarIdx, sessionTimeSec, invalidLapPctCount, onPitRoadCount, onTrackCount, timestampUpdates, debugEnabled, lapTimeEstimateSec);
                _lastSessionState = sessionState;
                return;
            }

            if (!playerLapPctValid)
            {
                invalidLapPctCount++;
                InvalidateOutputs(playerCarIdx, sessionTimeSec, invalidLapPctCount, onPitRoadCount, onTrackCount, timestampUpdates, debugEnabled, lapTimeEstimateSec);
                _lastSessionState = sessionState;
                return;
            }

            int playerLap = 0;
            if (carIdxLap != null && playerCarIdx < carIdxLap.Length)
            {
                playerLap = carIdxLap[playerCarIdx];
            }

            int carLimit = Math.Min(MaxCars, carCount);
            for (int carIdx = 0; carIdx < carLimit; carIdx++)
            {
                double lapPct = carIdxLapDistPct[carIdx];
                if (double.IsNaN(lapPct) || lapPct < 0.0 || lapPct >= 1.0)
                {
                    invalidLapPctCount++;
                }

                if (carIdxTrackSurface != null && carIdx < carIdxTrackSurface.Length)
                {
                    if (carIdxTrackSurface[carIdx] == TrackSurfaceOnTrack)
                    {
                        onTrackCount++;
                    }
                }

                if (carIdxOnPitRoad != null && carIdx < carIdxOnPitRoad.Length && carIdxOnPitRoad[carIdx])
                {
                    onPitRoadCount++;
                }
            }

            ResetCandidates(_aheadCandidateIdx, _aheadCandidateDist);
            ResetCandidates(_behindCandidateIdx, _behindCandidateDist);

            for (int carIdx = 0; carIdx < carLimit; carIdx++)
            {
                if (carIdx == playerCarIdx)
                {
                    continue;
                }

                var state = _carStates[carIdx];
                double forwardDist = state.ForwardDistPct;
                double backwardDist = state.BackwardDistPct;

                if (double.IsNaN(forwardDist) || double.IsNaN(backwardDist))
                {
                    continue;
                }

                bool onTrack = state.IsOnTrack;
                bool onPitRoad = state.IsOnPitRoad;

                if (!onTrack)
                {
                    continue;
                }

                if (!_includePitRoad && onPitRoad)
                {
                    continue;
                }

                if (forwardDist >= HalfLapFilterMin && forwardDist <= HalfLapFilterMax)
                {
                    filteredHalfLapAhead++;
                }
                else
                {
                    InsertCandidate(carIdx, forwardDist, _aheadCandidateIdx, _aheadCandidateDist);
                }

                if (backwardDist >= HalfLapFilterMin && backwardDist <= HalfLapFilterMax)
                {
                    filteredHalfLapBehind++;
                }
                else
                {
                    InsertCandidate(carIdx, backwardDist, _behindCandidateIdx, _behindCandidateDist);
                }
            }

            _outputs.Valid = true;
            _outputs.Debug.PlayerCarIdx = playerCarIdx;
            _outputs.Debug.PlayerLapPct = playerLapPct;
            _outputs.Debug.PlayerLap = playerLap;
            _outputs.Debug.InvalidLapPctCount = invalidLapPctCount;
            _outputs.Debug.OnPitRoadCount = onPitRoadCount;
            _outputs.Debug.OnTrackCount = onTrackCount;
            _outputs.Debug.TimestampUpdatesThisTick = timestampUpdates;
            _outputs.Debug.FilteredHalfLapCountAhead = filteredHalfLapAhead;
            _outputs.Debug.FilteredHalfLapCountBehind = filteredHalfLapBehind;

            if (debugEnabled)
            {
                _outputs.Debug.LapTimeEstimateSec = lapTimeUsed;
            }
            else
            {
                _outputs.Debug.LapTimeEstimateSec = 0.0;
            }

            int hysteresisReplacements = 0;
            int slotCarIdxChanged = 0;

            ApplySlots(true, sessionTimeSec, playerCarIdx, playerLapPct, playerLap, carIdxLapDistPct, carIdxLap, carIdxTrackSurface, carIdxOnPitRoad, _aheadCandidateIdx, _aheadCandidateDist, _outputs.AheadSlots, ref hysteresisReplacements, ref slotCarIdxChanged);
            ApplySlots(false, sessionTimeSec, playerCarIdx, playerLapPct, playerLap, carIdxLapDistPct, carIdxLap, carIdxTrackSurface, carIdxOnPitRoad, _behindCandidateIdx, _behindCandidateDist, _outputs.BehindSlots, ref hysteresisReplacements, ref slotCarIdxChanged);
            UpdateSlotGapsFromCarStates(_outputs.AheadSlots, lapTimeUsed, isAhead: true);
            UpdateSlotGapsFromCarStates(_outputs.BehindSlots, lapTimeUsed, isAhead: false);

            if (debugEnabled)
            {
                _outputs.Debug.HysteresisReplacementsThisTick = hysteresisReplacements;
                _outputs.Debug.SlotCarIdxChangedThisTick = slotCarIdxChanged;
            }
            else
            {
                _outputs.Debug.HysteresisReplacementsThisTick = 0;
                _outputs.Debug.SlotCarIdxChangedThisTick = 0;
            }

            UpdateSlotDebug(_outputs.AheadSlots.Length > 0 ? _outputs.AheadSlots[0] : null, true);
            UpdateSlotDebug(_outputs.BehindSlots.Length > 0 ? _outputs.BehindSlots[0] : null, false);

            _lastSessionState = sessionState;

            if (!_loggedEnabled)
            {
                SimHub.Logging.Current.Info("[LalaPlugin:CarSA] CarSA enabled (source=CarIdxTruth, slots=5/5)");
                _loggedEnabled = true;
            }
        }

        private void InvalidateOutputs(
            int playerCarIdx,
            double sessionTimeSec,
            int invalidLapPctCount,
            int onPitRoadCount,
            int onTrackCount,
            int timestampUpdates,
            bool debugEnabled,
            double lapTimeEstimateSec)
        {
            _outputs.Valid = false;
            _outputs.ResetSlots();
            _outputs.Debug.PlayerCarIdx = playerCarIdx;
            _outputs.Debug.PlayerLapPct = double.NaN;
            _outputs.Debug.PlayerLap = 0;
            _outputs.Debug.SessionTimeSec = sessionTimeSec;
            _outputs.Debug.InvalidLapPctCount = invalidLapPctCount;
            _outputs.Debug.OnPitRoadCount = onPitRoadCount;
            _outputs.Debug.OnTrackCount = onTrackCount;
            _outputs.Debug.TimestampUpdatesThisTick = timestampUpdates;
            _outputs.Debug.FilteredHalfLapCountAhead = 0;
            _outputs.Debug.FilteredHalfLapCountBehind = 0;
            _outputs.Debug.Ahead01CarIdx = -1;
            _outputs.Debug.Behind01CarIdx = -1;
            _outputs.Debug.SourceFastPathUsed = false;
            _outputs.Debug.HysteresisReplacementsThisTick = 0;
            _outputs.Debug.SlotCarIdxChangedThisTick = 0;
            _outputs.Debug.PlayerTrackSurfaceRaw = -1;
            if (debugEnabled)
            {
                _outputs.Debug.LapTimeEstimateSec = lapTimeEstimateSec;
            }
            else
            {
                _outputs.Debug.LapTimeEstimateSec = 0.0;
            }
        }

        private static void ResetCandidates(int[] idxs, double[] dists)
        {
            for (int i = 0; i < idxs.Length; i++)
            {
                idxs[i] = -1;
                dists[i] = double.MaxValue;
            }
        }

        // === Car-centric shadow state (unused by current logic) =================
        private void ResetCarStates()
        {
            for (int i = 0; i < _carStates.Length; i++)
            {
                _carStates[i].Reset(i);
            }
        }

        private void UpdateCarStates(
            double sessionTimeSec,
            float[] carIdxLapDistPct,
            int[] carIdxLap,
            int[] carIdxTrackSurface,
            bool[] carIdxOnPitRoad,
            int[] carIdxSessionFlags,
            int[] carIdxPaceFlags,
            double playerLapPct,
            double lapTimeEstimateSec,
            bool allowLatches)
        {
            _ = carIdxPaceFlags;
            const double graceWindowSec = 0.5;
            const double niwGraceSec = 3.0;
            for (int carIdx = 0; carIdx < _carStates.Length; carIdx++)
            {
                var state = _carStates[carIdx];
                int prevLap = state.LastLapSeen;
                bool prevWasPitArea = state.WasInPitArea;
                state.CarIdx = carIdx;
                state.LastSeenSessionTime = sessionTimeSec;
                state.Lap = (carIdxLap != null && carIdx < carIdxLap.Length) ? carIdxLap[carIdx] : 0;
                state.LapDistPct = (carIdxLapDistPct != null && carIdx < carIdxLapDistPct.Length)
                    ? carIdxLapDistPct[carIdx]
                    : double.NaN;
                state.IsOnPitRoad = carIdxOnPitRoad != null && carIdx < carIdxOnPitRoad.Length
                    && carIdxOnPitRoad[carIdx];
                if (carIdxTrackSurface != null && carIdx < carIdxTrackSurface.Length)
                {
                    int surface = NormalizeTrackSurfaceRaw(carIdxTrackSurface[carIdx]);
                    state.TrackSurfaceRaw = surface;
                    state.IsOnTrack = IsOnTrackSurface(surface);
                }
                else
                {
                    state.TrackSurfaceRaw = TrackSurfaceUnknown;
                    state.IsOnTrack = false;
                }

                bool hasLapPct = !double.IsNaN(state.LapDistPct) && state.LapDistPct >= 0.0 && state.LapDistPct < 1.0;
                bool hasPlayerPct = !double.IsNaN(playerLapPct) && playerLapPct >= 0.0 && playerLapPct < 1.0;

                bool inWorldNow = state.TrackSurfaceRaw != TrackSurfaceNotInWorld;
                bool pitAreaNow = state.IsOnPitRoad
                    || IsPitLaneSurface(state.TrackSurfaceRaw)
                    || IsPitStallOrTowSurface(state.TrackSurfaceRaw);
                bool onTrackNow = IsOnTrackSurface(state.TrackSurfaceRaw);
                bool offTrackNow = state.TrackSurfaceRaw == TrackSurfaceOffTrack;

                if (inWorldNow)
                {
                    state.LastInWorldSessionTimeSec = sessionTimeSec;
                    state.SessionFlagsRaw = (carIdxSessionFlags != null && carIdx < carIdxSessionFlags.Length)
                        ? carIdxSessionFlags[carIdx]
                        : -1;
                    state.CompromisedPenaltyActive = state.SessionFlagsRaw >= 0
                        && (unchecked((uint)state.SessionFlagsRaw) & (uint)SessionFlagMaskCompromised) != 0;
                }
                else
                {
                    if (double.IsNaN(state.LastInWorldSessionTimeSec))
                    {
                        state.LastInWorldSessionTimeSec = sessionTimeSec;
                    }

                    if ((sessionTimeSec - state.LastInWorldSessionTimeSec) > niwGraceSec)
                    {
                        state.CompromisedUntilLap = int.MinValue;
                        state.OutLapUntilLap = int.MinValue;
                        state.WasInPitArea = false;
                        state.CompromisedOffTrackActive = false;
                        state.OutLapActive = false;
                        state.SessionFlagsRaw = -1;
                        state.CompromisedPenaltyActive = false;
                        state.OffTrackStreak = 0;
                        state.OffTrackFirstSeenTimeSec = double.NaN;
                        state.LapsSincePit = -1;
                        continue;
                    }

                    continue;
                }

                bool lapAdvanced = prevLap != int.MinValue && state.Lap > prevLap;
                if (lapAdvanced)
                {
                    if (state.Lap >= state.CompromisedUntilLap)
                    {
                        state.CompromisedUntilLap = int.MinValue;
                    }
                    if (state.Lap >= state.OutLapUntilLap)
                    {
                        state.OutLapUntilLap = int.MinValue;
                    }
                    if (state.LapsSincePit >= 0)
                    {
                        state.LapsSincePit += 1;
                    }
                }

                if (allowLatches && offTrackNow && inWorldNow)
                {
                    if (state.OffTrackStreak == 0)
                    {
                        state.OffTrackFirstSeenTimeSec = sessionTimeSec;
                    }
                    state.OffTrackStreak++;
                    bool allowLatch = state.OffTrackStreak >= 3
                        || (!double.IsNaN(state.OffTrackFirstSeenTimeSec)
                            && (sessionTimeSec - state.OffTrackFirstSeenTimeSec) >= 0.20);
                    if (allowLatch)
                    {
                        int untilLap = state.Lap + 1;
                        if (state.CompromisedUntilLap < untilLap)
                        {
                            state.CompromisedUntilLap = untilLap;
                        }
                    }
                }
                else
                {
                    state.OffTrackStreak = 0;
                    state.OffTrackFirstSeenTimeSec = double.NaN;
                }

                bool pitExitToTrack = prevWasPitArea && !pitAreaNow && onTrackNow;
                if (pitExitToTrack && inWorldNow && allowLatches)
                {
                    int untilLap = state.Lap + 1;
                    if (state.OutLapUntilLap < untilLap)
                    {
                        state.OutLapUntilLap = untilLap;
                    }
                    state.LapsSincePit = 0;
                }

                state.WasInPitArea = pitAreaNow;
                state.LastLapSeen = state.Lap;
                state.CompromisedOffTrackActive = state.CompromisedUntilLap != int.MinValue
                    && state.Lap < state.CompromisedUntilLap;
                state.OutLapActive = state.OutLapUntilLap != int.MinValue
                    && state.Lap < state.OutLapUntilLap;
                if (hasLapPct && hasPlayerPct)
                {
                    double forwardDist = state.LapDistPct - playerLapPct;
                    if (forwardDist < 0.0) forwardDist += 1.0;
                    double backwardDist = playerLapPct - state.LapDistPct;
                    if (backwardDist < 0.0) backwardDist += 1.0;
                    state.ForwardDistPct = forwardDist;
                    state.BackwardDistPct = backwardDist;

                    double signedDelta = ComputeSignedDeltaPct(playerLapPct, state.LapDistPct);
                    state.SignedDeltaPct = signedDelta;
                    double gapPctAbs = Math.Abs(signedDelta);

                    if (state.HasDeltaPct)
                    {
                        double dt = sessionTimeSec - state.LastDeltaUpdateTime;
                        if (dt > 0.0)
                        {
                            double deltaAbs = gapPctAbs - state.LastGapPctAbs;
                            double rateSecPerSec = -(deltaAbs / dt) * lapTimeEstimateSec;
                            if (rateSecPerSec > ClosingRateClamp) rateSecPerSec = ClosingRateClamp;
                            if (rateSecPerSec < -ClosingRateClamp) rateSecPerSec = -ClosingRateClamp;
                            state.ClosingRateSecPerSec = rateSecPerSec;
                        }
                    }
                    else
                    {
                        state.ClosingRateSecPerSec = double.NaN;
                    }

                    state.HasDeltaPct = true;
                    state.LastDeltaPct = signedDelta;
                    state.LastGapPctAbs = gapPctAbs;
                    state.LastDeltaUpdateTime = sessionTimeSec;
                    state.LastValidSessionTime = sessionTimeSec;
                }
                else
                {
                    double lastValid = state.LastValidSessionTime;
                    if (!double.IsNaN(lastValid) && (sessionTimeSec - lastValid) <= graceWindowSec)
                    {
                        // Grace window: keep last delta/closing values.
                    }
                    else
                    {
                        state.SignedDeltaPct = double.NaN;
                        state.ForwardDistPct = double.NaN;
                        state.BackwardDistPct = double.NaN;
                        state.ClosingRateSecPerSec = double.NaN;
                        state.HasDeltaPct = false;
                        state.LastDeltaPct = double.NaN;
                        state.LastGapPctAbs = double.NaN;
                        state.LastDeltaUpdateTime = double.NaN;
                        state.LastValidSessionTime = double.NaN;
                    }
                }

            }
        }

        private static bool IsRaceSessionType(string name)
        {
            return string.Equals(name, "Race", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHardOffSessionType(string name)
        {
            return string.Equals(name, "Offline Testing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Lone Qualify", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPracticeOrQualSessionType(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (IsHardOffSessionType(name))
            {
                return false;
            }

            return name.IndexOf("Practice", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Qualify", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsUnknownSessionType(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            return !IsRaceSessionType(name)
                && !IsPracticeOrQualSessionType(name)
                && !IsHardOffSessionType(name);
        }

        private static double ComputeSignedDeltaPct(double playerLapPct, double carLapPct)
        {
            double delta = carLapPct - playerLapPct;
            if (delta > 0.5) delta -= 1.0;
            if (delta < -0.5) delta += 1.0;
            return delta;
        }

        private void UpdateSlotGapsFromCarStates(CarSASlot[] slots, double lapTimeEstimateSec, bool isAhead)
        {
            if (slots == null)
            {
                return;
            }

            foreach (var slot in slots)
            {
                if (slot == null || !slot.IsValid || slot.CarIdx < 0 || slot.CarIdx >= _carStates.Length)
                {
                    if (slot != null)
                    {
                        slot.GapTrackSec = double.NaN;
                        slot.ClosingRateSecPerSec = double.NaN;
                        slot.LapsSincePit = -1;
                    }
                    continue;
                }

                var state = _carStates[slot.CarIdx];
                double distPct = isAhead ? state.ForwardDistPct : -state.BackwardDistPct;
                if (double.IsNaN(distPct))
                {
                    slot.GapTrackSec = double.NaN;
                    slot.ClosingRateSecPerSec = double.NaN;
                }
                else
                {
                    double gapSec = distPct * lapTimeEstimateSec;
                    slot.GapTrackSec = gapSec;
                    slot.ClosingRateSecPerSec = state.ClosingRateSecPerSec;
                }

                slot.LapsSincePit = state.LapsSincePit;
            }
        }

        private static void InsertCandidate(int carIdx, double dist, int[] idxs, double[] dists)
        {
            int insertPos = -1;
            for (int i = 0; i < idxs.Length; i++)
            {
                if (dist < dists[i])
                {
                    insertPos = i;
                    break;
                }
            }

            if (insertPos < 0)
            {
                return;
            }

            for (int i = idxs.Length - 1; i > insertPos; i--)
            {
                idxs[i] = idxs[i - 1];
                dists[i] = dists[i - 1];
            }

            idxs[insertPos] = carIdx;
            dists[insertPos] = dist;
        }

        // === SA / track-awareness slot assignment ===============================
        private void ApplySlots(
            bool isAhead,
            double sessionTimeSec,
            int playerCarIdx,
            double playerLapPct,
            int playerLap,
            float[] carIdxLapDistPct,
            int[] carIdxLap,
            int[] carIdxTrackSurface,
            bool[] carIdxOnPitRoad,
            int[] candidateIdx,
            double[] candidateDist,
            CarSASlot[] slots,
            ref int hysteresisReplacements,
            ref int slotCarIdxChanged)
        {
            int slotCount = slots.Length;
            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                CarSASlot slot = slots[slotIndex];
                int newIdx = candidateIdx[slotIndex];
                double newDist = candidateDist[slotIndex];

                if (newIdx == slot.CarIdx && slot.CarIdx >= 0)
                {
                    if (isAhead)
                    {
                        slot.ForwardDistPct = newDist;
                    }
                    else
                    {
                        slot.BackwardDistPct = newDist;
                    }
                }
                else
                {
                    double currentDist = double.MaxValue;
                    bool currentValid = TryComputeDistance(playerCarIdx, playerLapPct, slot.CarIdx, carIdxLapDistPct, isAhead, out currentDist);

                    if (!currentValid)
                    {
                        if (ApplySlotAssignment(slot, newIdx, newDist, isAhead, sessionTimeSec))
                        {
                            slotCarIdxChanged++;
                        }
                        if (newIdx != -1)
                        {
                            hysteresisReplacements++;
                        }
                    }
                    else if (newIdx != -1 && newDist < currentDist * HysteresisFactor)
                    {
                        if (ApplySlotAssignment(slot, newIdx, newDist, isAhead, sessionTimeSec))
                        {
                            slotCarIdxChanged++;
                        }
                        hysteresisReplacements++;
                    }
                    else
                    {
                        if (isAhead)
                        {
                            slot.ForwardDistPct = currentDist;
                        }
                        else
                        {
                            slot.BackwardDistPct = currentDist;
                        }
                    }
                }

                UpdateSlotState(slot, playerLap, playerLapPct, isAhead, carIdxLap, carIdxTrackSurface, carIdxOnPitRoad);
            }
        }

        private static bool TryComputeDistance(int playerCarIdx, double playerLapPct, int carIdx, float[] carIdxLapDistPct, bool isAhead, out double dist)
        {
            dist = double.MaxValue;
            if (carIdx < 0 || carIdxLapDistPct == null || carIdx >= carIdxLapDistPct.Length || carIdx == playerCarIdx)
            {
                return false;
            }

            double lapPct = carIdxLapDistPct[carIdx];
            if (double.IsNaN(lapPct) || lapPct < 0.0 || lapPct >= 1.0)
            {
                return false;
            }

            if (isAhead)
            {
                dist = lapPct - playerLapPct;
                if (dist < 0.0) dist += 1.0;
            }
            else
            {
                dist = playerLapPct - lapPct;
                if (dist < 0.0) dist += 1.0;
            }

            return true;
        }

        private static bool ApplySlotAssignment(CarSASlot slot, int carIdx, double dist, bool isAhead, double sessionTimeSec)
        {
            bool carIdxChanged = slot.CarIdx != carIdx;
            if (carIdx < 0)
            {
                slot.Reset();
                return carIdxChanged;
            }

            if (carIdxChanged)
            {
                slot.HasGap = false;
                slot.LastGapSec = double.NaN;
                slot.LastGapUpdateTimeSec = 0.0;
                slot.HasGapAbs = false;
                slot.LastGapAbs = double.NaN;
                slot.ClosingRateSecPerSec = double.NaN;
                slot.LapsSincePit = -1;
                slot.JustRebound = true;
                slot.ReboundTimeSec = sessionTimeSec;
                slot.TrackSurfaceRaw = TrackSurfaceUnknown;
                slot.CurrentLap = 0;
                slot.LastLap = int.MinValue;
                slot.WasOnPitRoad = false;
                slot.WasInPitArea = false;
                slot.OutLapActive = false;
                slot.OutLapLap = int.MinValue;
                slot.CompromisedThisLap = false;
                slot.CompromisedLap = int.MinValue;
                slot.LastCompEvidenceSessionTimeSec = -1.0;
                slot.CompEvidenceStreak = 0;
                slot.PositionInClass = 0;
                slot.ClassName = string.Empty;
                slot.ClassColorHex = string.Empty;
                slot.IRating = 0;
                slot.Licence = string.Empty;
                slot.SafetyRating = double.NaN;
                slot.BestLapTimeSec = double.NaN;
                slot.LastLapTimeSec = double.NaN;
                slot.BestLap = string.Empty;
                slot.LastLap = string.Empty;
                slot.DeltaBestSec = double.NaN;
                slot.DeltaBest = string.Empty;
                slot.HotScore = 0.0;
                slot.HotVia = string.Empty;

                       // Phase 2: prevent stale StatusE labels carrying across car rebinds
                slot.StatusE = (int)CarSAStatusE.Unknown;
                slot.LastStatusE = int.MinValue;     // force UpdateStatusEText to run
                slot.StatusETextDirty = true;
                slot.StatusShort = "UNK";
                slot.StatusLong = "Unknown";
                slot.StatusEReason = "unknown";
            }

            slot.CarIdx = carIdx;
            if (isAhead)
            {
                slot.ForwardDistPct = dist;
                slot.BackwardDistPct = double.NaN;
            }
            else
            {
                slot.BackwardDistPct = dist;
                slot.ForwardDistPct = double.NaN;
            }

            return carIdxChanged;
        }

        // === SA / track-awareness per-slot state ================================
        private static void UpdateSlotState(
            CarSASlot slot,
            int playerLap,
            double playerLapPct,
            bool isAhead,
            int[] carIdxLap,
            int[] carIdxTrackSurface,
            bool[] carIdxOnPitRoad)
        {
            if (slot.CarIdx < 0)
            {
                slot.IsValid = false;
                slot.IsOnTrack = false;
                slot.IsOnPitRoad = false;
                slot.LapDelta = 0;
                slot.CurrentLap = 0;
                slot.TrackSurfaceRaw = TrackSurfaceUnknown;
                slot.Status = (int)CarSAStatus.Unknown;
                slot.Name = string.Empty;
                slot.CarNumber = string.Empty;
                slot.ClassColor = string.Empty;
                return;
            }

            slot.IsValid = true;
            slot.IsOnTrack = true;
            slot.IsOnPitRoad = false;

            int trackSurfaceRaw = TrackSurfaceUnknown;
            if (carIdxTrackSurface != null && slot.CarIdx < carIdxTrackSurface.Length)
            {
                int surface = NormalizeTrackSurfaceRaw(carIdxTrackSurface[slot.CarIdx]);
                trackSurfaceRaw = surface;
                if (IsNotInWorldSurface(surface))
                {
                    slot.IsValid = false;
                    slot.IsOnTrack = false;
                    slot.IsOnPitRoad = false;
                    slot.LapDelta = 0;
                    slot.CurrentLap = 0;
                    slot.TrackSurfaceRaw = trackSurfaceRaw;
                    slot.Status = (int)CarSAStatus.Unknown;
                    return;
                }

                slot.IsOnTrack = IsOnTrackSurface(surface);
            }

            if (carIdxOnPitRoad != null && slot.CarIdx < carIdxOnPitRoad.Length)
            {
                slot.IsOnPitRoad = carIdxOnPitRoad[slot.CarIdx];
            }

            int oppLap = 0;
            if (carIdxLap != null && slot.CarIdx < carIdxLap.Length)
            {
                oppLap = carIdxLap[slot.CarIdx];
            }
            slot.CurrentLap = oppLap;
            slot.TrackSurfaceRaw = trackSurfaceRaw;

            int baseLapDelta = oppLap - playerLap;
            if (baseLapDelta != 0 && !double.IsNaN(playerLapPct))
            {
                const double lapDeltaClosePct = 0.10;
                double oppLapPct = double.NaN;

                if (isAhead && !double.IsNaN(slot.ForwardDistPct))
                {
                    oppLapPct = playerLapPct + slot.ForwardDistPct;
                    if (oppLapPct >= 1.0) oppLapPct -= 1.0;
                }
                else if (!isAhead && !double.IsNaN(slot.BackwardDistPct))
                {
                    oppLapPct = playerLapPct - slot.BackwardDistPct;
                    if (oppLapPct < 0.0) oppLapPct += 1.0;
                }

                if (!double.IsNaN(oppLapPct))
                {
                    if (isAhead &&
                        baseLapDelta == 1 &&
                        slot.ForwardDistPct <= lapDeltaClosePct &&
                        playerLapPct >= (1.0 - LapDeltaWrapEdgePct) &&
                        oppLapPct <= LapDeltaWrapEdgePct)
                    {
                        baseLapDelta = 0;
                    }
                    else if (!isAhead &&
                        baseLapDelta == -1 &&
                        slot.BackwardDistPct <= lapDeltaClosePct &&
                        playerLapPct <= LapDeltaWrapEdgePct &&
                        oppLapPct >= (1.0 - LapDeltaWrapEdgePct))
                    {
                        baseLapDelta = 0;
                    }
                }
            }

            slot.LapDelta = baseLapDelta;

            if (slot.IsOnPitRoad)
            {
                slot.Status = (int)CarSAStatus.InPits;
            }
            else
            {
                slot.Status = (int)CarSAStatus.Normal;
            }
        }

        // === StatusE logic ======================================================
        private void UpdateStatusE(
            CarSASlot[] slots,
            double notRelevantGapSec,
            bool isAhead,
            OpponentsEngine.OpponentOutputs opponentOutputs,
            string playerClassColor,
            double sessionTimeSec,
            Dictionary<string, int> classRankByColor)
        {
            if (slots == null)
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                UpdateStatusE(slots[i], notRelevantGapSec, isAhead, opponentOutputs, playerClassColor, sessionTimeSec, classRankByColor);
            }
        }

        private void UpdateStatusE(
            CarSASlot slot,
            double notRelevantGapSec,
            bool isAhead,
            OpponentsEngine.OpponentOutputs opponentOutputs,
            string playerClassColor,
            double sessionTimeSec,
            Dictionary<string, int> classRankByColor)
        {
            if (slot == null)
            {
                return;
            }

            _ = notRelevantGapSec;
            _ = opponentOutputs;
            _ = sessionTimeSec;
            CarSA_CarState carState = null;
            if (slot.CarIdx >= 0 && slot.CarIdx < _carStates.Length)
            {
                carState = _carStates[slot.CarIdx];
            }

            // Phase 2: penalty/outlap/compromised latches are car-centric in _carStates; slot fields are mirrors for exporters/helpers.
            if (carState != null)
            {
                slot.SessionFlagsRaw = carState.SessionFlagsRaw;
            }
            else
            {
                slot.SessionFlagsRaw = -1;
            }

            slot.SlotIsAhead = isAhead;
            int statusE = (int)CarSAStatusE.Unknown;
            string statusEReason = StatusEReasonUnknown;
            int trackSurfaceRaw = slot.TrackSurfaceRaw == TrackSurfaceUnknown
                ? TrackSurfaceUnknown
                : NormalizeTrackSurfaceRaw(slot.TrackSurfaceRaw);
            if (slot.IsOnPitRoad || IsPitAreaSurface(trackSurfaceRaw))
            {
                statusE = (int)CarSAStatusE.InPits;
                statusEReason = StatusEReasonPits;
            }
            else if (carState != null && (carState.CompromisedPenaltyActive || carState.CompromisedOffTrackActive))
            {
                if (carState.CompromisedPenaltyActive)
                {
                    statusE = (int)CarSAStatusE.CompromisedPenalty;
                    statusEReason = StatusEReasonCompromisedPenalty;
                }
                else
                {
                    statusE = (int)CarSAStatusE.CompromisedOffTrack;
                    statusEReason = StatusEReasonCompromisedOffTrack;
                }
            }
            else if (!slot.IsValid || slot.TrackSurfaceRaw == TrackSurfaceNotInWorld)
            {
                statusE = (int)CarSAStatusE.Unknown;
                statusEReason = StatusEReasonUnknown;
            }
            else if (!slot.IsOnTrack)
            {
                statusE = (int)CarSAStatusE.Unknown;
                statusEReason = StatusEReasonUnknown;
            }
            // SA-Core v2: gap-based NotRelevant gating disabled (always relevant).
            else if (carState != null && carState.OutLapActive)
            {
                statusE = (int)CarSAStatusE.OutLap;
                statusEReason = StatusEReasonOutLap;
            }
            else if (slot.LapDelta > 0)
            {
                statusE = (int)CarSAStatusE.LappingYou;
                statusEReason = StatusEReasonLapAhead;
            }
            else if (slot.LapDelta < 0)
            {
                statusE = (int)CarSAStatusE.BeingLapped;
                statusEReason = StatusEReasonLapBehind;
            }
            else if (IsSameClass(slot, playerClassColor))
            {
                statusE = (int)CarSAStatusE.Racing;
                statusEReason = StatusEReasonRacing;
            }
            else if (IsOtherClass(slot, playerClassColor))
            {
                if (IsFasterClass(slot, playerClassColor, classRankByColor))
                {
                    statusE = (int)CarSAStatusE.FasterClass;
                    statusEReason = StatusEReasonOtherClass;
                }
                else if (IsSlowerClass(slot, playerClassColor, classRankByColor))
                {
                    statusE = (int)CarSAStatusE.SlowerClass;
                    statusEReason = StatusEReasonOtherClass;
                }
                else
                {
                    // Fallback to legacy heuristic when rank is unknown/missing.
                    statusE = isAhead
                        ? (int)CarSAStatusE.SlowerClass
                        : (int)CarSAStatusE.FasterClass;
                    statusEReason = StatusEReasonOtherClassUnknownRank;
                }
            }

            if (carState != null)
            {
                slot.OutLapActive = carState.OutLapActive;
                slot.CompromisedThisLap = carState.CompromisedPenaltyActive || carState.CompromisedOffTrackActive;
                slot.CompromisedStatusE = carState.CompromisedPenaltyActive
                    ? (int)CarSAStatusE.CompromisedPenalty
                    : (carState.CompromisedOffTrackActive ? (int)CarSAStatusE.CompromisedOffTrack : (int)CarSAStatusE.Unknown);
            }
            else
            {
                slot.OutLapActive = false;
                slot.CompromisedThisLap = false;
                slot.CompromisedStatusE = (int)CarSAStatusE.Unknown;
            }

            slot.StatusE = statusE;
            slot.StatusEReason = statusEReason;
            UpdateStatusEText(slot);
        }

        private void ApplySessionTypeStatusEPolicy(CarSASlot[] slots)
        {
            if (slots == null)
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                ApplySessionTypeStatusEPolicy(slots[i]);
            }
        }

        private void ApplySessionTypeStatusEPolicy(CarSASlot slot)
        {
            if (slot == null)
            {
                return;
            }

            string sessionTypeName = _lastSessionTypeName;
            bool isHardOff = IsHardOffSessionType(sessionTypeName);
            bool isUnknownSession = IsUnknownSessionType(sessionTypeName);
            if (isHardOff || isUnknownSession)
            {
                ForceStatusE(slot, (int)CarSAStatusE.Unknown, isUnknownSession ? "sess_unknown" : "sess_off");
                return;
            }

            if (IsPracticeOrQualSessionType(sessionTypeName))
            {
                if (slot.StatusE == (int)CarSAStatusE.Racing
                    || slot.StatusE == (int)CarSAStatusE.LappingYou
                    || slot.StatusE == (int)CarSAStatusE.BeingLapped)
                {
                    ForceStatusE(slot, (int)CarSAStatusE.Unknown, "sess_suppress");
                }
                return;
            }

            if (IsRaceSessionType(sessionTypeName))
            {
                if (slot.StatusE == (int)CarSAStatusE.HotLap
                    || slot.StatusE == (int)CarSAStatusE.CoolLap)
                {
                    ForceStatusE(slot, (int)CarSAStatusE.Unknown, "sess_suppress");
                }
            }
        }

        private static void ApplyGatedStatusE(CarSASlot[] slots)
        {
            if (slots == null)
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                CarSASlot slot = slots[i];
                if (slot == null)
                {
                    continue;
                }

                slot.StatusE = (int)CarSAStatusE.Unknown;
                slot.StatusEReason = "gated";
                slot.StatusETextDirty = true;
                UpdateStatusEText(slot);
            }
        }

        private static void ApplyForcedStatusE(CarSASlot[] slots, string reason)
        {
            if (slots == null)
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                ForceStatusE(slots[i], (int)CarSAStatusE.Unknown, reason);
            }
        }

        private static void ForceStatusE(CarSASlot slot, int statusE, string reason)
        {
            if (slot == null)
            {
                return;
            }

            if (slot.StatusE == statusE && string.Equals(slot.StatusEReason, reason, StringComparison.Ordinal))
            {
                if (slot.StatusETextDirty)
                {
                    UpdateStatusEText(slot);
                }
                return;
            }

            slot.StatusE = statusE;
            slot.StatusEReason = reason;
            slot.StatusETextDirty = true;
            UpdateStatusEText(slot);
        }

        private void ResetCarLatchesOnly()
        {
            for (int i = 0; i < _carStates.Length; i++)
            {
                var state = _carStates[i];
                state.WasInPitArea = false;
                state.CompromisedUntilLap = int.MinValue;
                state.OutLapUntilLap = int.MinValue;
                state.CompromisedOffTrackActive = false;
                state.OutLapActive = false;
                state.OffTrackStreak = 0;
                state.OffTrackFirstSeenTimeSec = double.NaN;
            }

            ClearSlotLatchStates(_outputs?.AheadSlots);
            ClearSlotLatchStates(_outputs?.BehindSlots);
        }

        private static void ClearSlotLatchStates(CarSASlot[] slots)
        {
            if (slots == null)
            {
                return;
            }

            foreach (var slot in slots)
            {
                if (slot == null)
                {
                    continue;
                }

                slot.OutLapActive = false;
                slot.CompromisedThisLap = false;
                slot.CompromisedStatusE = (int)CarSAStatusE.Unknown;
                slot.SessionFlagsRaw = -1;
                slot.StatusE = (int)CarSAStatusE.Unknown;
                slot.StatusEReason = "gated";
                slot.StatusETextDirty = true;
            }
        }

        internal static void GetCompromisedFlagBits(
            CarSASlot slot,
            out bool black,
            out bool furled,
            out bool repair,
            out bool disqualify)
        {
            black = false;
            furled = false;
            repair = false;
            disqualify = false;

            if (slot == null || !slot.IsValid || slot.TrackSurfaceRaw == TrackSurfaceNotInWorld)
            {
                return;
            }

            int rawFlags = slot.SessionFlagsRaw;
            if (rawFlags < 0)
            {
                return;
            }

            uint flags = unchecked((uint)rawFlags);
            black = (flags & (uint)SessionFlagBlack) != 0;
            furled = (flags & (uint)SessionFlagFurled) != 0;
            repair = (flags & (uint)SessionFlagRepair) != 0;
            disqualify = (flags & (uint)SessionFlagDisqualify) != 0;
        }

        private static bool IsRacingFromOpponents(CarSASlot slot, OpponentsEngine.OpponentOutputs opponentOutputs, bool isAhead)
        {
            if (slot == null || opponentOutputs == null)
            {
                return false;
            }

            return isAhead
                ? IsOpponentFight(slot, opponentOutputs.Ahead1) || IsOpponentFight(slot, opponentOutputs.Ahead2)
                : IsOpponentFight(slot, opponentOutputs.Behind1) || IsOpponentFight(slot, opponentOutputs.Behind2);
        }

        private static bool IsOpponentFight(CarSASlot slot, OpponentsEngine.OpponentTargetOutput target)
        {
            if (slot == null || target == null)
            {
                return false;
            }

            double lapsToFight = target.LapsToFight;
            if (double.IsNaN(lapsToFight) || double.IsInfinity(lapsToFight) || lapsToFight <= 0.0)
            {
                return false;
            }

            return IsIdentityMatch(slot, target);
        }

        private static bool IsIdentityMatch(CarSASlot slot, OpponentsEngine.OpponentTargetOutput target)
        {
            if (slot == null || target == null)
            {
                return false;
            }

            bool hasNumber = !string.IsNullOrWhiteSpace(slot.CarNumber) && !string.IsNullOrWhiteSpace(target.CarNumber);
            if (hasNumber && !string.Equals(slot.CarNumber, target.CarNumber, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(slot.ClassColor) &&
                !string.IsNullOrWhiteSpace(target.ClassColor) &&
                !string.Equals(slot.ClassColor, target.ClassColor, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (hasNumber)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(slot.Name) &&
                !string.IsNullOrWhiteSpace(target.Name) &&
                string.Equals(slot.Name, target.Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static bool IsOtherClass(CarSASlot slot, string playerClassColor)
        {
            if (slot == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(playerClassColor) || string.IsNullOrWhiteSpace(slot.ClassColor))
            {
                return false;
            }

            return !string.Equals(slot.ClassColor, playerClassColor, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameClass(CarSASlot slot, string playerClassColor)
        {
            if (slot == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(playerClassColor) || string.IsNullOrWhiteSpace(slot.ClassColor))
            {
                return false;
            }

            return string.Equals(slot.ClassColor, playerClassColor, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFasterClass(CarSASlot slot, string playerClassColor, Dictionary<string, int> classRankByColor)
        {
            if (!TryGetClassRanks(slot, playerClassColor, classRankByColor, out int slotRank, out int playerRank))
            {
                return false;
            }

            return slotRank < playerRank;
        }

        private static bool IsSlowerClass(CarSASlot slot, string playerClassColor, Dictionary<string, int> classRankByColor)
        {
            if (!TryGetClassRanks(slot, playerClassColor, classRankByColor, out int slotRank, out int playerRank))
            {
                return false;
            }

            return slotRank > playerRank;
        }

        private static bool TryGetClassRanks(
            CarSASlot slot,
            string playerClassColor,
            Dictionary<string, int> classRankByColor,
            out int slotRank,
            out int playerRank)
        {
            slotRank = 0;
            playerRank = 0;

            if (slot == null || string.IsNullOrWhiteSpace(playerClassColor) || classRankByColor == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(slot.ClassColor))
            {
                return false;
            }

            return classRankByColor.TryGetValue(slot.ClassColor, out slotRank)
                && classRankByColor.TryGetValue(playerClassColor, out playerRank);
        }

        private static void UpdateStatusEText(CarSASlot slot)
        {
            if (slot == null)
            {
                return;
            }

            bool needsUpdate = slot.StatusETextDirty || slot.LastStatusE != slot.StatusE;
            if (!needsUpdate && (slot.StatusE == (int)CarSAStatusE.LappingYou || slot.StatusE == (int)CarSAStatusE.BeingLapped))
            {
                if (slot.LastStatusELapDelta != slot.LapDelta || slot.LastStatusEIsAhead != slot.SlotIsAhead)
                {
                    needsUpdate = true;
                }
            }

            if (!needsUpdate)
            {
                return;
            }

            switch (slot.StatusE)
            {
                case (int)CarSAStatusE.OutLap:
                    slot.StatusShort = StatusShortOutLap;
                    slot.StatusLong = StatusLongOutLap;
                    break;
                case (int)CarSAStatusE.InPits:
                    slot.StatusShort = StatusShortInPits;
                    slot.StatusLong = StatusLongInPits;
                    break;
                case (int)CarSAStatusE.CompromisedOffTrack:
                    slot.StatusShort = StatusShortCompromisedOffTrack;
                    slot.StatusLong = StatusLongCompromisedOffTrack;
                    break;
                case (int)CarSAStatusE.CompromisedPenalty:
                    slot.StatusShort = StatusShortCompromisedPenalty;
                    slot.StatusLong = StatusLongCompromisedPenalty;
                    break;
                case (int)CarSAStatusE.FasterClass:
                    slot.StatusShort = StatusShortFasterClass;
                    slot.StatusLong = StatusLongFasterClass;
                    break;
                case (int)CarSAStatusE.SlowerClass:
                    slot.StatusShort = StatusShortSlowerClass;
                    slot.StatusLong = StatusLongSlowerClass;
                    break;
                case (int)CarSAStatusE.Racing:
                    slot.StatusShort = StatusShortRacing;
                    slot.StatusLong = StatusLongRacing;
                    break;
                case (int)CarSAStatusE.HotLap:
                    slot.StatusShort = StatusShortHotLap;
                    slot.StatusLong = StatusLongHotLap;
                    break;
                case (int)CarSAStatusE.CoolLap:
                    slot.StatusShort = StatusShortCoolLap;
                    slot.StatusLong = StatusLongCoolLap;
                    break;
                case (int)CarSAStatusE.LappingYou:
                case (int)CarSAStatusE.BeingLapped:
                    int lapDelta = slot.LapDelta;
                    int lapDeltaAbs = Math.Abs(lapDelta);
                    if (lapDeltaAbs > 9)
                    {
                        lapDeltaAbs = 9;
                    }

                    string lapSignShort = lapDelta >= 0 ? "+" : "-";
                    slot.StatusShort = $"{lapSignShort}{lapDeltaAbs}L";
                    string lapSign = lapDelta >= 0 ? "+" : "-";
                    string directionLabel = lapDelta >= 0 ? "Up" : "Down";
                    slot.StatusLong = $"{directionLabel} {lapSign}{Math.Abs(lapDelta)} Laps";
                    break;
                default:
                    slot.StatusShort = StatusShortUnknown;
                    slot.StatusLong = StatusLongUnknown;
                    break;
            }

            slot.LastStatusE = slot.StatusE;
            slot.StatusETextDirty = false;
            slot.LastStatusELapDelta = slot.LapDelta;
            slot.LastStatusEIsAhead = slot.SlotIsAhead;
        }

        // === CSV / debug instrumentation =======================================
        private void UpdateSlotDebug(CarSASlot slot, bool isAhead)
        {
            if (slot == null)
            {
                return;
            }

            if (isAhead)
            {
                _outputs.Debug.Ahead01CarIdx = slot.CarIdx;
                _outputs.Debug.Ahead01ForwardDistPct = slot.ForwardDistPct;
            }
            else
            {
                _outputs.Debug.Behind01CarIdx = slot.CarIdx;
                _outputs.Debug.Behind01BackwardDistPct = slot.BackwardDistPct;
            }
        }
    }
}
