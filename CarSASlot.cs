using System;

namespace LaunchPlugin
{
    public enum CarSAStatus
    {
        Unknown = 0,
        Normal = 1,
        InPits = 2
    }

    public enum CarSAStatusE
    {
        Unknown = 0,
        OutLap = 100,
        InPits = 110,
        CompromisedThisLap = 120,
        NotRelevant = 190,
        FasterClass = 200,
        SlowerClass = 210,
        Racing = 220,
        LappingYou = 230,
        BeingLapped = 240
    }

    public class CarSASlot
    {
        public int CarIdx { get; set; } = -1;
        public string Name { get; set; } = string.Empty;
        public string CarNumber { get; set; } = string.Empty;
        public string ClassColor { get; set; } = string.Empty;
        public bool IsOnTrack { get; set; }
        public bool IsOnPitRoad { get; set; }
        public bool IsValid { get; set; }
        public int LapDelta { get; set; }
        public double GapRealSec { get; set; } = double.NaN;
        public double ClosingRateSecPerSec { get; set; } = double.NaN;
        public int Status { get; set; } = (int)CarSAStatus.Unknown;
        public int StatusE { get; set; } = (int)CarSAStatusE.Unknown;
        public string StatusShort { get; set; } = "UNK";
        public string StatusLong { get; set; } = "Unknown";
        public int PaceFlagsRaw { get; set; } = -1;
        public int SessionFlagsRaw { get; set; } = -1;
        public int TrackSurfaceMaterialRaw { get; set; } = -1;
        public double ForwardDistPct { get; set; } = double.NaN;
        public double BackwardDistPct { get; set; } = double.NaN;
        public double RealGapRawSec { get; set; } = double.NaN;
        public double RealGapAdjSec { get; set; } = double.NaN;
        public double LastSeenCheckpointTimeSec { get; set; } = 0.0;
        public bool JustRebound { get; set; }

        internal double LastGapUpdateTimeSec { get; set; } = 0.0;
        internal double LastGapSec { get; set; } = double.NaN;
        internal bool HasGap { get; set; }
        internal double LastGapAbs { get; set; } = double.NaN;
        internal bool HasGapAbs { get; set; }
        internal bool HasRealGap { get; set; }
        internal double LastRealGapUpdateSessionTimeSec { get; set; } = 0.0;
        internal int LastStatusE { get; set; } = (int)CarSAStatusE.Unknown;
        internal bool StatusETextDirty { get; set; } = true;
        internal int TrackSurfaceRaw { get; set; } = int.MinValue;
        internal int CurrentLap { get; set; }
        internal int LastLap { get; set; } = int.MinValue;
        internal bool WasOnPitRoad { get; set; }
        internal bool OutLapActive { get; set; }
        internal int OutLapLap { get; set; } = int.MinValue;
        internal bool CompromisedThisLap { get; set; }
        internal int CompromisedLap { get; set; } = int.MinValue;

        public void Reset()
        {
            CarIdx = -1;
            Name = string.Empty;
            CarNumber = string.Empty;
            ClassColor = string.Empty;
            IsOnTrack = false;
            IsOnPitRoad = false;
            IsValid = false;
            LapDelta = 0;
            GapRealSec = double.NaN;
            ClosingRateSecPerSec = double.NaN;
            Status = (int)CarSAStatus.Unknown;
            StatusE = (int)CarSAStatusE.Unknown;
            StatusShort = "UNK";
            StatusLong = "Unknown";
            PaceFlagsRaw = -1;
            SessionFlagsRaw = -1;
            TrackSurfaceMaterialRaw = -1;
            ForwardDistPct = double.NaN;
            BackwardDistPct = double.NaN;
            RealGapRawSec = double.NaN;
            RealGapAdjSec = double.NaN;
            LastSeenCheckpointTimeSec = 0.0;
            JustRebound = false;
            LastGapUpdateTimeSec = 0.0;
            LastGapSec = double.NaN;
            HasGap = false;
            LastGapAbs = double.NaN;
            HasGapAbs = false;
            HasRealGap = false;
            LastRealGapUpdateSessionTimeSec = 0.0;
            LastStatusE = (int)CarSAStatusE.Unknown;
            StatusETextDirty = true;
            TrackSurfaceRaw = int.MinValue;
            CurrentLap = 0;
            LastLap = int.MinValue;
            WasOnPitRoad = false;
            OutLapActive = false;
            OutLapLap = int.MinValue;
            CompromisedThisLap = false;
            CompromisedLap = int.MinValue;
        }
    }

    public class CarSADebug
    {
        public int PlayerCarIdx { get; set; } = -1;
        public double PlayerLapPct { get; set; } = double.NaN;
        public int PlayerLap { get; set; }
        public int PlayerCheckpointIndexNow { get; set; } = -1;
        public int PlayerCheckpointIndexCrossed { get; set; } = -1;
        public bool PlayerCheckpointCrossed { get; set; }
        public double SessionTimeSec { get; set; }
        public bool SourceFastPathUsed { get; set; }

        public int Ahead01CarIdx { get; set; } = -1;
        public double Ahead01ForwardDistPct { get; set; } = double.NaN;
        public double Ahead01RealGapRawSec { get; set; } = double.NaN;
        public double Ahead01RealGapAdjSec { get; set; } = double.NaN;
        public double Ahead01LastSeenCheckpointTimeSec { get; set; }

        public int Behind01CarIdx { get; set; } = -1;
        public double Behind01BackwardDistPct { get; set; } = double.NaN;
        public double Behind01RealGapRawSec { get; set; } = double.NaN;
        public double Behind01RealGapAdjSec { get; set; } = double.NaN;
        public double Behind01LastSeenCheckpointTimeSec { get; set; }

        public int InvalidLapPctCount { get; set; }
        public int OnPitRoadCount { get; set; }
        public int OnTrackCount { get; set; }
        public int TimestampUpdatesThisTick { get; set; }
        public int FilteredHalfLapCountAhead { get; set; }
        public int FilteredHalfLapCountBehind { get; set; }
        public int TimestampUpdatesSinceLastPlayerCross { get; set; }

        public double LapTimeEstimateSec { get; set; }
        public int HysteresisReplacementsThisTick { get; set; }
        public int SlotCarIdxChangedThisTick { get; set; }
        public int RealGapClampsThisTick { get; set; }
        public bool HasCarIdxPaceFlags { get; set; }
        public bool HasCarIdxSessionFlags { get; set; }
        public bool HasCarIdxTrackSurfaceMaterial { get; set; }
        public int PlayerPaceFlagsRaw { get; set; } = -1;
        public int PlayerSessionFlagsRaw { get; set; } = -1;
        public int PlayerTrackSurfaceMaterialRaw { get; set; } = -1;
        public string RawTelemetryReadMode { get; set; } = string.Empty;
        public string RawTelemetryFailReason { get; set; } = string.Empty;

        public void Reset()
        {
            PlayerCarIdx = -1;
            PlayerLapPct = double.NaN;
            PlayerLap = 0;
            PlayerCheckpointIndexNow = -1;
            PlayerCheckpointIndexCrossed = -1;
            PlayerCheckpointCrossed = false;
            SessionTimeSec = 0.0;
            SourceFastPathUsed = false;
            Ahead01CarIdx = -1;
            Ahead01ForwardDistPct = double.NaN;
            Ahead01RealGapRawSec = double.NaN;
            Ahead01RealGapAdjSec = double.NaN;
            Ahead01LastSeenCheckpointTimeSec = 0.0;
            Behind01CarIdx = -1;
            Behind01BackwardDistPct = double.NaN;
            Behind01RealGapRawSec = double.NaN;
            Behind01RealGapAdjSec = double.NaN;
            Behind01LastSeenCheckpointTimeSec = 0.0;
            InvalidLapPctCount = 0;
            OnPitRoadCount = 0;
            OnTrackCount = 0;
            TimestampUpdatesThisTick = 0;
            FilteredHalfLapCountAhead = 0;
            FilteredHalfLapCountBehind = 0;
            TimestampUpdatesSinceLastPlayerCross = 0;
            LapTimeEstimateSec = 0.0;
            HysteresisReplacementsThisTick = 0;
            SlotCarIdxChangedThisTick = 0;
            RealGapClampsThisTick = 0;
            HasCarIdxPaceFlags = false;
            HasCarIdxSessionFlags = false;
            HasCarIdxTrackSurfaceMaterial = false;
            PlayerPaceFlagsRaw = -1;
            PlayerSessionFlagsRaw = -1;
            PlayerTrackSurfaceMaterialRaw = -1;
            RawTelemetryReadMode = string.Empty;
            RawTelemetryFailReason = string.Empty;
        }
    }

    public class CarSAOutputs
    {
        public CarSAOutputs(int slotsAhead, int slotsBehind)
        {
            Checkpoints = CarSAEngine.CheckpointCount;
            SlotsAhead = slotsAhead;
            SlotsBehind = slotsBehind;
            AheadSlots = new CarSASlot[slotsAhead];
            BehindSlots = new CarSASlot[slotsBehind];
            for (int i = 0; i < slotsAhead; i++)
            {
                AheadSlots[i] = new CarSASlot();
            }
            for (int i = 0; i < slotsBehind; i++)
            {
                BehindSlots[i] = new CarSASlot();
            }
            Debug = new CarSADebug();
        }

        public bool Valid { get; set; }
        public string Source { get; set; } = string.Empty;
        public int Checkpoints { get; }
        public int SlotsAhead { get; }
        public int SlotsBehind { get; }
        public CarSASlot[] AheadSlots { get; }
        public CarSASlot[] BehindSlots { get; }
        public CarSADebug Debug { get; }

        public void ResetSlots()
        {
            for (int i = 0; i < AheadSlots.Length; i++)
            {
                AheadSlots[i].Reset();
            }
            for (int i = 0; i < BehindSlots.Length; i++)
            {
                BehindSlots[i].Reset();
            }
        }

        public void ResetAll()
        {
            Valid = false;
            Source = string.Empty;
            ResetSlots();
            Debug.Reset();
        }
    }
}
