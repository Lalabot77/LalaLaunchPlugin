using System;

namespace LaunchPlugin
{
    public class CarSAEngine
    {
        public const int CheckpointCount = 60;
        public const int SlotsAhead = 5;
        public const int SlotsBehind = 5;
        public const int MaxCars = 64;

        private const double DefaultLapTimeEstimateSec = 120.0;
        private const double HysteresisFactor = 0.90;
        private const double MaxRealGapSec = 600.0;
        private const double WrapAdjustThresholdFactor = 0.50;
        private const double ClosingRateClamp = 5.0;
        private const double HalfLapFilterMin = 0.40;
        private const double HalfLapFilterMax = 0.60;
        private const double RealGapGraceSec = 2.0;
        private const double WrapGuardEdgePct = 0.03;
        private const double LapDeltaWrapEdgePct = 0.15;
        private const int TrackSurfaceNotInWorld = -1;
        private const int TrackSurfaceOnTrack = 3;

        private readonly RealGapStopwatch _stopwatch;
        private readonly CarSAOutputs _outputs;
        private readonly int[] _aheadCandidateIdx;
        private readonly int[] _behindCandidateIdx;
        private readonly double[] _aheadCandidateDist;
        private readonly double[] _behindCandidateDist;
        private readonly bool _includePitRoad;
        private bool _loggedEnabled;
        private int _timestampUpdatesSinceLastPlayerCross;

        public CarSAEngine()
        {
            _stopwatch = new RealGapStopwatch(CheckpointCount, MaxCars);
            _outputs = new CarSAOutputs(SlotsAhead, SlotsBehind);
            _aheadCandidateIdx = new int[SlotsAhead];
            _behindCandidateIdx = new int[SlotsBehind];
            _aheadCandidateDist = new double[SlotsAhead];
            _behindCandidateDist = new double[SlotsBehind];
            _includePitRoad = false;
        }

        public CarSAOutputs Outputs => _outputs;

        public void Reset()
        {
            _loggedEnabled = false;
            _stopwatch.Reset();
            _outputs.ResetAll();
            _timestampUpdatesSinceLastPlayerCross = 0;
        }

        public void Update(
            double sessionTimeSec,
            int playerCarIdx,
            float[] carIdxLapDistPct,
            int[] carIdxLap,
            int[] carIdxTrackSurface,
            bool[] carIdxOnPitRoad,
            double lapTimeEstimateSec,
            bool debugEnabled)
        {
            _outputs.Source = "CarIdxTruth";
            _outputs.Debug.SessionTimeSec = sessionTimeSec;
            _outputs.Debug.SourceFastPathUsed = false;

            int onPitRoadCount = 0;
            int onTrackCount = 0;
            int invalidLapPctCount = 0;
            int timestampUpdates = 0;
            int filteredHalfLapAhead = 0;
            int filteredHalfLapBehind = 0;
            bool playerCheckpointCrossed;
            int playerCheckpointIndex;
            int playerCheckpointIndexNow;

            _stopwatch.Update(
                sessionTimeSec,
                carIdxLapDistPct,
                carIdxTrackSurface,
                playerCarIdx,
                out playerCheckpointCrossed,
                out playerCheckpointIndex,
                out playerCheckpointIndexNow,
                out timestampUpdates,
                out invalidLapPctCount,
                out onTrackCount);

            _timestampUpdatesSinceLastPlayerCross += timestampUpdates;

            int carCount = carIdxLapDistPct != null ? carIdxLapDistPct.Length : 0;
            if (carCount <= 0 || playerCarIdx < 0 || playerCarIdx >= carCount)
            {
                InvalidateOutputs(playerCarIdx, sessionTimeSec, invalidLapPctCount, onPitRoadCount, onTrackCount, timestampUpdates, debugEnabled, lapTimeEstimateSec);
                return;
            }

            double playerLapPct = carIdxLapDistPct[playerCarIdx];
            if (double.IsNaN(playerLapPct) || playerLapPct < 0.0 || playerLapPct >= 1.0)
            {
                invalidLapPctCount++;
                InvalidateOutputs(playerCarIdx, sessionTimeSec, invalidLapPctCount, onPitRoadCount, onTrackCount, timestampUpdates, debugEnabled, lapTimeEstimateSec);
                return;
            }

            int playerLap = 0;
            if (carIdxLap != null && playerCarIdx < carIdxLap.Length)
            {
                playerLap = carIdxLap[playerCarIdx];
            }

            double lapTimeUsed = lapTimeEstimateSec;
            if (!(lapTimeUsed > 0.0) || double.IsNaN(lapTimeUsed) || double.IsInfinity(lapTimeUsed))
            {
                lapTimeUsed = DefaultLapTimeEstimateSec;
            }

            int carLimit = Math.Min(MaxCars, carCount);
            ResetCandidates(_aheadCandidateIdx, _aheadCandidateDist);
            ResetCandidates(_behindCandidateIdx, _behindCandidateDist);

            for (int carIdx = 0; carIdx < carLimit; carIdx++)
            {
                if (carIdx == playerCarIdx)
                {
                    continue;
                }

                double lapPct = carIdxLapDistPct[carIdx];
                if (double.IsNaN(lapPct) || lapPct < 0.0 || lapPct >= 1.0)
                {
                    continue;
                }

                bool onTrack = true;
                if (carIdxTrackSurface != null && carIdx < carIdxTrackSurface.Length)
                {
                    int surface = carIdxTrackSurface[carIdx];
                    if (surface == TrackSurfaceNotInWorld)
                    {
                        onTrack = false;
                    }
                    else
                    {
                        onTrack = surface == TrackSurfaceOnTrack;
                    }
                }

                bool onPitRoad = false;
                if (carIdxOnPitRoad != null && carIdx < carIdxOnPitRoad.Length)
                {
                    onPitRoad = carIdxOnPitRoad[carIdx];
                }

                if (onPitRoad)
                {
                    onPitRoadCount++;
                }

                if (!onTrack)
                {
                    continue;
                }

                if (!_includePitRoad && onPitRoad)
                {
                    continue;
                }

                double forwardDist = lapPct - playerLapPct;
                if (forwardDist < 0.0) forwardDist += 1.0;

                double backwardDist = playerLapPct - lapPct;
                if (backwardDist < 0.0) backwardDist += 1.0;

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
            _outputs.Debug.PlayerCheckpointIndexNow = playerCheckpointIndexNow;
            _outputs.Debug.PlayerCheckpointIndexCrossed = playerCheckpointIndex;
            _outputs.Debug.PlayerCheckpointCrossed = playerCheckpointCrossed;
            _outputs.Debug.InvalidLapPctCount = invalidLapPctCount;
            _outputs.Debug.OnPitRoadCount = onPitRoadCount;
            _outputs.Debug.OnTrackCount = onTrackCount;
            _outputs.Debug.TimestampUpdatesThisTick = timestampUpdates;
            _outputs.Debug.FilteredHalfLapCountAhead = filteredHalfLapAhead;
            _outputs.Debug.FilteredHalfLapCountBehind = filteredHalfLapBehind;
            _outputs.Debug.TimestampUpdatesSinceLastPlayerCross = _timestampUpdatesSinceLastPlayerCross;
            if (playerCheckpointCrossed)
            {
                _timestampUpdatesSinceLastPlayerCross = 0;
            }

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

            ApplySlots(true, playerCarIdx, playerLapPct, playerLap, carIdxLapDistPct, carIdxLap, carIdxTrackSurface, carIdxOnPitRoad, _aheadCandidateIdx, _aheadCandidateDist, _outputs.AheadSlots, ref hysteresisReplacements, ref slotCarIdxChanged);
            ApplySlots(false, playerCarIdx, playerLapPct, playerLap, carIdxLapDistPct, carIdxLap, carIdxTrackSurface, carIdxOnPitRoad, _behindCandidateIdx, _behindCandidateDist, _outputs.BehindSlots, ref hysteresisReplacements, ref slotCarIdxChanged);

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

            int realGapClamps = 0;
            if (playerCheckpointIndexNow >= 0)
            {
                double playerCheckpointTimeSec = _stopwatch.GetLastCheckpointTimeSec(playerCheckpointIndexNow, playerCarIdx);
                UpdateRealGaps(true, sessionTimeSec, playerCheckpointTimeSec, playerCheckpointIndexNow, playerLap, playerLapPct, lapTimeUsed, carIdxLap, _outputs.AheadSlots, ref realGapClamps);
                UpdateRealGaps(false, sessionTimeSec, playerCheckpointTimeSec, playerCheckpointIndexNow, playerLap, playerLapPct, lapTimeUsed, carIdxLap, _outputs.BehindSlots, ref realGapClamps);
            }

            if (debugEnabled)
            {
                _outputs.Debug.RealGapClampsThisTick = realGapClamps;
            }
            else
            {
                _outputs.Debug.RealGapClampsThisTick = 0;
            }

            UpdateSlotDebug(_outputs.AheadSlots.Length > 0 ? _outputs.AheadSlots[0] : null, true);
            UpdateSlotDebug(_outputs.BehindSlots.Length > 0 ? _outputs.BehindSlots[0] : null, false);

            if (!_loggedEnabled)
            {
                SimHub.Logging.Current.Info("[LalaPlugin:CarSA] CarSA enabled (source=CarIdxTruth, checkpoints=60, slots=5/5)");
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
            _outputs.Debug.PlayerCheckpointIndexNow = -1;
            _outputs.Debug.PlayerCheckpointIndexCrossed = -1;
            _outputs.Debug.PlayerCheckpointCrossed = false;
            _outputs.Debug.SessionTimeSec = sessionTimeSec;
            _outputs.Debug.InvalidLapPctCount = invalidLapPctCount;
            _outputs.Debug.OnPitRoadCount = onPitRoadCount;
            _outputs.Debug.OnTrackCount = onTrackCount;
            _outputs.Debug.TimestampUpdatesThisTick = timestampUpdates;
            _outputs.Debug.FilteredHalfLapCountAhead = 0;
            _outputs.Debug.FilteredHalfLapCountBehind = 0;
            _outputs.Debug.TimestampUpdatesSinceLastPlayerCross = _timestampUpdatesSinceLastPlayerCross;
            _outputs.Debug.Ahead01CarIdx = -1;
            _outputs.Debug.Behind01CarIdx = -1;
            _outputs.Debug.SourceFastPathUsed = false;
            _outputs.Debug.HysteresisReplacementsThisTick = 0;
            _outputs.Debug.SlotCarIdxChangedThisTick = 0;
            _outputs.Debug.RealGapClampsThisTick = 0;
            _timestampUpdatesSinceLastPlayerCross = 0;
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

        private void ApplySlots(
            bool isAhead,
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
                        if (ApplySlotAssignment(slot, newIdx, newDist, isAhead))
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
                        if (ApplySlotAssignment(slot, newIdx, newDist, isAhead))
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

        private static bool ApplySlotAssignment(CarSASlot slot, int carIdx, double dist, bool isAhead)
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
                slot.GapRealSec = double.NaN;
                slot.RealGapRawSec = double.NaN;
                slot.RealGapAdjSec = double.NaN;
                slot.LastSeenCheckpointTimeSec = 0.0;
                slot.HasRealGap = false;
                slot.LastRealGapUpdateSessionTimeSec = 0.0;
                slot.JustRebound = true;
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
                slot.Status = (int)CarSAStatus.Unknown;
                slot.Name = string.Empty;
                slot.CarNumber = string.Empty;
                slot.ClassColor = string.Empty;
                return;
            }

            slot.IsValid = true;
            slot.IsOnTrack = true;
            slot.IsOnPitRoad = false;

            if (carIdxTrackSurface != null && slot.CarIdx < carIdxTrackSurface.Length)
            {
                int surface = carIdxTrackSurface[slot.CarIdx];
                if (surface == TrackSurfaceNotInWorld)
                {
                    slot.IsValid = false;
                    slot.IsOnTrack = false;
                    slot.IsOnPitRoad = false;
                    slot.LapDelta = 0;
                    slot.Status = (int)CarSAStatus.Unknown;
                    return;
                }

                slot.IsOnTrack = surface == TrackSurfaceOnTrack;
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

        private void UpdateRealGaps(
            bool isAhead,
            double sessionTimeSec,
            double playerCheckpointTimeSec,
            int checkpointIndex,
            int playerLap,
            double playerLapPct,
            double lapTimeEstimateSec,
            int[] carIdxLap,
            CarSASlot[] slots,
            ref int realGapClamps)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                CarSASlot slot = slots[i];
                if (!slot.IsValid || slot.CarIdx < 0)
                {
                    slot.GapRealSec = double.NaN;
                    slot.RealGapRawSec = double.NaN;
                    slot.RealGapAdjSec = double.NaN;
                    slot.LastSeenCheckpointTimeSec = 0.0;
                    slot.HasRealGap = false;
                    slot.LastRealGapUpdateSessionTimeSec = 0.0;
                    continue;
                }

                double lastSeen = _stopwatch.GetLastCheckpointTimeSec(checkpointIndex, slot.CarIdx);
                slot.LastSeenCheckpointTimeSec = lastSeen;
                if (lastSeen <= 0.0 || playerCheckpointTimeSec <= 0.0)
                {
                    if (slot.HasRealGap && (sessionTimeSec - slot.LastRealGapUpdateSessionTimeSec) <= RealGapGraceSec)
                    {
                        slot.ClosingRateSecPerSec = 0.0;
                        continue;
                    }

                    slot.GapRealSec = double.NaN;
                    slot.RealGapRawSec = double.NaN;
                    slot.RealGapAdjSec = double.NaN;
                    slot.ClosingRateSecPerSec = double.NaN;
                    slot.HasRealGap = false;
                    continue;
                }

                double rawGap = playerCheckpointTimeSec - lastSeen;
                double adjustedGap = rawGap;

                int lapDelta = slot.LapDelta;
                const double WrapStraddleClosePct = 0.10;
                double slotLapPct = double.NaN;
                double distPct = double.NaN;

                if (isAhead && !double.IsNaN(slot.ForwardDistPct))
                {
                    distPct = slot.ForwardDistPct;
                    slotLapPct = playerLapPct + slot.ForwardDistPct;
                    if (slotLapPct >= 1.0) slotLapPct -= 1.0;
                }
                else if (!isAhead && !double.IsNaN(slot.BackwardDistPct))
                {
                    distPct = slot.BackwardDistPct;
                    slotLapPct = playerLapPct - slot.BackwardDistPct;
                    if (slotLapPct < 0.0) slotLapPct += 1.0;
                }

                bool playerNearEdge = playerLapPct <= WrapGuardEdgePct || playerLapPct >= (1.0 - WrapGuardEdgePct);
                bool slotNearEdge = !double.IsNaN(slotLapPct) &&
                    (slotLapPct <= WrapGuardEdgePct || slotLapPct >= (1.0 - WrapGuardEdgePct));
                bool closeEnough = !double.IsNaN(distPct) && distPct <= WrapStraddleClosePct;
                bool suppressLapDeltaCorrection = playerNearEdge && slotNearEdge && closeEnough;
                bool allowLapDeltaAdjust = !slot.JustRebound && !suppressLapDeltaCorrection;
                bool allowBehindWrap = !slot.JustRebound;
                double behindWrapThreshold = suppressLapDeltaCorrection ? 0.90 : WrapAdjustThresholdFactor;

                if (lapDelta != 0)
                {
                    if (allowLapDeltaAdjust)
                    {
                        adjustedGap += lapDelta * lapTimeEstimateSec;
                    }
                }
                else if (!isAhead && rawGap > lapTimeEstimateSec * behindWrapThreshold)
                {
                    if (allowBehindWrap)
                    {
                        adjustedGap = rawGap - lapTimeEstimateSec;
                    }
                }

                if (adjustedGap > MaxRealGapSec)
                {
                    adjustedGap = MaxRealGapSec;
                    realGapClamps++;
                }
                else if (adjustedGap < -MaxRealGapSec)
                {
                    adjustedGap = -MaxRealGapSec;
                    realGapClamps++;
                }

                slot.RealGapRawSec = rawGap;
                slot.RealGapAdjSec = adjustedGap;
                slot.GapRealSec = isAhead ? Math.Abs(adjustedGap) : -Math.Abs(adjustedGap);
                slot.HasRealGap = true;
                slot.LastRealGapUpdateSessionTimeSec = sessionTimeSec;
                slot.JustRebound = false;

                UpdateClosingRate(slot, playerCheckpointTimeSec);
            }
        }

        private static void UpdateClosingRate(CarSASlot slot, double gapTimeSec)
        {
            if (!slot.IsValid || double.IsNaN(slot.GapRealSec))
            {
                return;
            }

            double gapAbs = Math.Abs(slot.GapRealSec);
            if (!slot.HasGapAbs)
            {
                slot.HasGap = true;
                slot.HasGapAbs = true;
                slot.LastGapSec = slot.GapRealSec;
                slot.LastGapAbs = gapAbs;
                slot.LastGapUpdateTimeSec = gapTimeSec;
                slot.ClosingRateSecPerSec = double.NaN;
                return;
            }

            double dt = gapTimeSec - slot.LastGapUpdateTimeSec;
            if (dt <= 0.05)
            {
                return;
            }

            double deltaAbs = gapAbs - slot.LastGapAbs;
            double rate = -(deltaAbs / dt);

            if (rate > ClosingRateClamp) rate = ClosingRateClamp;
            if (rate < -ClosingRateClamp) rate = -ClosingRateClamp;

            if (double.IsNaN(slot.ClosingRateSecPerSec))
            {
                slot.ClosingRateSecPerSec = rate;
            }
            else
            {
                slot.ClosingRateSecPerSec = (0.8 * slot.ClosingRateSecPerSec) + (0.2 * rate);
            }

            slot.LastGapSec = slot.GapRealSec;
            slot.LastGapAbs = gapAbs;
            slot.LastGapUpdateTimeSec = gapTimeSec;
        }

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
                _outputs.Debug.Ahead01RealGapRawSec = slot.RealGapRawSec;
                _outputs.Debug.Ahead01RealGapAdjSec = slot.RealGapAdjSec;
                _outputs.Debug.Ahead01LastSeenCheckpointTimeSec = slot.LastSeenCheckpointTimeSec;
            }
            else
            {
                _outputs.Debug.Behind01CarIdx = slot.CarIdx;
                _outputs.Debug.Behind01BackwardDistPct = slot.BackwardDistPct;
                _outputs.Debug.Behind01RealGapRawSec = slot.RealGapRawSec;
                _outputs.Debug.Behind01RealGapAdjSec = slot.RealGapAdjSec;
                _outputs.Debug.Behind01LastSeenCheckpointTimeSec = slot.LastSeenCheckpointTimeSec;
            }
        }
    }
}
