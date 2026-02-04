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
        CompromisedOffTrack = 121,
        CompromisedPenalty = 122,
        HotLap = 130,
        CoolLap = 140,
        FasterClass = 200,
        SlowerClass = 210,
        Racing = 220,
        LappingYou = 230,
        BeingLapped = 240
    }

    // CarSASlot is a slot-centric container: state is bound to a slot position (Ahead/Behind index),
    // not to a specific car identity. Slot assignment swaps reset per-slot state and gaps.
    // SA-Core v2 intent: retain track-awareness and StatusE state; strip race-gap/telemetry
    // concepts that are NOT USED BY SA-CORE (see annotations below).
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
        // SA / track-awareness gap (used by StatusE relevance + closing rate).
        public double GapTrackSec { get; set; } = double.NaN;
        public double ClosingRateSecPerSec { get; set; } = double.NaN;
        public int Status { get; set; } = (int)CarSAStatus.Unknown;
        public int StatusE { get; set; } = (int)CarSAStatusE.Unknown;
        public string StatusShort { get; set; } = "UNK";
        public string StatusLong { get; set; } = "Unknown";
        public string StatusEReason { get; set; } = "unknown";
        public int SessionFlagsRaw { get; set; } = -1;
        public int TrackSurfaceMaterialRaw { get; set; } = -1;
        public int PositionInClass { get; set; }
        public string ClassName { get; set; } = string.Empty;
        public string ClassColorHex { get; set; } = string.Empty;
        public int IRating { get; set; }
        public string Licence { get; set; } = string.Empty;
        public double SafetyRating { get; set; } = double.NaN;
        public int LapsSincePit { get; set; } = -1;
        public double BestLapTimeSec { get; set; } = double.NaN;
        public double LastLapTimeSec { get; set; } = double.NaN;
        public string BestLap { get; set; } = string.Empty;
        public string LastLap { get; set; } = string.Empty;
        public double DeltaBestSec { get; set; } = double.NaN;
        public string DeltaBest { get; set; } = string.Empty;
        public double EstLapTimeSec { get; set; } = double.NaN;
        public string EstLapTime { get; set; } = string.Empty;
        public double HotScore { get; set; }
        public string HotVia { get; set; } = string.Empty;
        public double ForwardDistPct { get; set; } = double.NaN;
        public double BackwardDistPct { get; set; } = double.NaN;
        public bool JustRebound { get; set; }
        public double ReboundTimeSec { get; set; } = 0.0;

        internal double LastGapUpdateTimeSec { get; set; } = 0.0;
        internal double LastGapSec { get; set; } = double.NaN;
        internal bool HasGap { get; set; }
        internal double LastGapAbs { get; set; } = double.NaN;
        internal bool HasGapAbs { get; set; }
        internal int LastStatusE { get; set; } = (int)CarSAStatusE.Unknown;
        internal bool StatusETextDirty { get; set; } = true;
        internal int LastStatusELapDelta { get; set; } = int.MinValue;
        internal bool LastStatusEIsAhead { get; set; }
        internal int TrackSurfaceRaw { get; set; } = int.MinValue;
        internal int CurrentLap { get; set; }
        internal int LastLapNumber { get; set; } = int.MinValue;
        // Legacy latch fields retained for exporters; authoritative state lives in CarSAEngine._carStates.
        internal bool WasOnPitRoad { get; set; }
        internal bool WasInPitArea { get; set; }
        internal bool OutLapActive { get; set; }
        internal int OutLapLap { get; set; } = int.MinValue;
        internal bool CompromisedThisLap { get; set; }
        internal int CompromisedLap { get; set; } = int.MinValue;
        internal int CompromisedStatusE { get; set; } = (int)CarSAStatusE.Unknown;
        internal double LastCompEvidenceSessionTimeSec { get; set; } = -1.0;
        internal int CompEvidenceStreak { get; set; }
        internal bool SlotIsAhead { get; set; }
        internal double LastIdentityAttemptSessionTimeSec { get; set; } = -1.0;
        internal bool IdentityResolved { get; set; }
        // Deprecated alias (keep for legacy exports; internal state is OutLapActive only).
        internal bool OutLapLatched => OutLapActive;
        internal bool CompromisedThisLapLatched => CompromisedThisLap;
        internal int TrackSurfaceRawDebug => TrackSurfaceRaw;
        internal double ClosingRateSmoothed { get; set; } = double.NaN;
        internal bool ClosingRateHasSample { get; set; }

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
            GapTrackSec = double.NaN;
            ClosingRateSecPerSec = double.NaN;
            Status = (int)CarSAStatus.Unknown;
            StatusE = (int)CarSAStatusE.Unknown;
            StatusShort = "UNK";
            StatusLong = "Unknown";
            StatusEReason = "unknown";
            SessionFlagsRaw = -1;
            TrackSurfaceMaterialRaw = -1;
            PositionInClass = 0;
            ClassName = string.Empty;
            ClassColorHex = string.Empty;
            IRating = 0;
            Licence = string.Empty;
            SafetyRating = double.NaN;
            LapsSincePit = -1;
            BestLapTimeSec = double.NaN;
            LastLapTimeSec = double.NaN;
            BestLap = string.Empty;
            LastLap = string.Empty;
            DeltaBestSec = double.NaN;
            DeltaBest = "-";
            EstLapTimeSec = double.NaN;
            EstLapTime = "-";
            HotScore = 0.0;
            HotVia = string.Empty;
            ForwardDistPct = double.NaN;
            BackwardDistPct = double.NaN;
            JustRebound = false;
            ReboundTimeSec = 0.0;
            LastGapUpdateTimeSec = 0.0;
            LastGapSec = double.NaN;
            HasGap = false;
            LastGapAbs = double.NaN;
            HasGapAbs = false;
            LastStatusE = (int)CarSAStatusE.Unknown;
            StatusETextDirty = true;
            LastStatusELapDelta = int.MinValue;
            LastStatusEIsAhead = false;
            TrackSurfaceRaw = int.MinValue;
            CurrentLap = 0;
            LastLapNumber = int.MinValue;
            WasOnPitRoad = false;
            WasInPitArea = false;
            OutLapActive = false;
            OutLapLap = int.MinValue;
            CompromisedThisLap = false;
            CompromisedLap = int.MinValue;
            CompromisedStatusE = (int)CarSAStatusE.Unknown;
            LastCompEvidenceSessionTimeSec = -1.0;
            CompEvidenceStreak = 0;
            SlotIsAhead = false;
            LastIdentityAttemptSessionTimeSec = -1.0;
            IdentityResolved = false;
            ClosingRateSmoothed = double.NaN;
            ClosingRateHasSample = false;
        }
    }

    public class CarSADebug
    {
        public int PlayerCarIdx { get; set; } = -1;
        public double PlayerLapPct { get; set; } = double.NaN;
        public int PlayerLap { get; set; }
        public double SessionTimeSec { get; set; }
        public bool SourceFastPathUsed { get; set; }

        public int Ahead01CarIdx { get; set; } = -1;
        public double Ahead01ForwardDistPct { get; set; } = double.NaN;

        public int Behind01CarIdx { get; set; } = -1;
        public double Behind01BackwardDistPct { get; set; } = double.NaN;

        public int InvalidLapPctCount { get; set; }
        public int OnPitRoadCount { get; set; }
        public int OnTrackCount { get; set; }
        public int TimestampUpdatesThisTick { get; set; }
        public int FilteredHalfLapCountAhead { get; set; }
        public int FilteredHalfLapCountBehind { get; set; }

        public double LapTimeEstimateSec { get; set; }
        public int HysteresisReplacementsThisTick { get; set; }
        public int SlotCarIdxChangedThisTick { get; set; }
        public bool HasCarIdxPaceFlags { get; set; }
        public bool HasCarIdxSessionFlags { get; set; }
        public bool HasCarIdxTrackSurfaceMaterial { get; set; }
        public int PlayerPaceFlagsRaw { get; set; } = -1;
        public int PlayerSessionFlagsRaw { get; set; } = -1;
        public int PlayerTrackSurfaceMaterialRaw { get; set; } = -1;
        public int PlayerTrackSurfaceRaw { get; set; } = -1;
        public string RawTelemetryReadMode { get; set; } = string.Empty;
        public string RawTelemetryFailReason { get; set; } = string.Empty;

        public void Reset()
        {
            PlayerCarIdx = -1;
            PlayerLapPct = double.NaN;
            PlayerLap = 0;
            SessionTimeSec = 0.0;
            SourceFastPathUsed = false;
            Ahead01CarIdx = -1;
            Ahead01ForwardDistPct = double.NaN;
            Behind01CarIdx = -1;
            Behind01BackwardDistPct = double.NaN;
            InvalidLapPctCount = 0;
            OnPitRoadCount = 0;
            OnTrackCount = 0;
            TimestampUpdatesThisTick = 0;
            FilteredHalfLapCountAhead = 0;
            FilteredHalfLapCountBehind = 0;
            LapTimeEstimateSec = 0.0;
            HysteresisReplacementsThisTick = 0;
            SlotCarIdxChangedThisTick = 0;
            HasCarIdxPaceFlags = false;
            HasCarIdxSessionFlags = false;
            HasCarIdxTrackSurfaceMaterial = false;
            PlayerPaceFlagsRaw = -1;
            PlayerSessionFlagsRaw = -1;
            PlayerTrackSurfaceMaterialRaw = -1;
            PlayerTrackSurfaceRaw = -1;
            RawTelemetryReadMode = string.Empty;
            RawTelemetryFailReason = string.Empty;
        }
    }

    public class CarSAOutputs
    {
        public CarSAOutputs(int slotsAhead, int slotsBehind)
        {
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
