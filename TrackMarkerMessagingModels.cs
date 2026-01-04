using System;

namespace LaunchPlugin
{
    internal class TrackMarkerCapturedMessage
    {
        public string TrackKey { get; set; } = "unknown";
        public double EntryPct { get; set; } = double.NaN;
        public double ExitPct { get; set; } = double.NaN;
        public bool Locked { get; set; }
    }

    internal class TrackMarkerLengthDeltaMessage
    {
        public string TrackKey { get; set; } = "unknown";
        public double StartM { get; set; } = double.NaN;
        public double NowM { get; set; } = double.NaN;
        public double DeltaM { get; set; } = double.NaN;
    }

    internal class TrackMarkerLockedMismatchMessage
    {
        public string TrackKey { get; set; } = "unknown";
        public double StoredEntryPct { get; set; } = double.NaN;
        public double CandidateEntryPct { get; set; } = double.NaN;
        public double StoredExitPct { get; set; } = double.NaN;
        public double CandidateExitPct { get; set; } = double.NaN;
        public double TolerancePct { get; set; } = double.NaN;
    }

    internal class TrackMarkerPulse<T> where T : class
    {
        private DateTime _timestampUtc = DateTime.MinValue;
        private bool _consumed;

        public T Data { get; private set; }

        public void Set(T data)
        {
            _timestampUtc = DateTime.UtcNow;
            _consumed = false;
            Data = data;
        }

        public bool TryConsume(double holdSeconds, out T data)
        {
            data = null;
            if (Data == null) return false;
            if (_timestampUtc == DateTime.MinValue) return false;
            if ((DateTime.UtcNow - _timestampUtc).TotalSeconds > holdSeconds) return false;
            if (_consumed) return false;

            data = Data;
            Data = null;
            _consumed = true;
            _timestampUtc = DateTime.MinValue;
            return true;
        }

        public void Reset()
        {
            Data = null;
            _timestampUtc = DateTime.MinValue;
            _consumed = false;
        }
    }
}
