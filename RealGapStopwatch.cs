using System;

namespace LaunchPlugin
{
    public class RealGapStopwatch
    {
        private readonly int _checkpointCount;
        private readonly int _maxCars;
        private readonly double[] _lastTimeSec;
        private readonly double[] _prevLapPct;
        private const double WrapThreshold = 0.5;
        private const int TrackSurfaceNotInWorld = -1;
        private const int TrackSurfaceOnTrack = 3;

        public RealGapStopwatch(int checkpointCount, int maxCars)
        {
            _checkpointCount = checkpointCount;
            _maxCars = maxCars;
            _lastTimeSec = new double[checkpointCount * maxCars];
            _prevLapPct = new double[maxCars];
            Reset();
        }

        public void Reset()
        {
            Array.Clear(_lastTimeSec, 0, _lastTimeSec.Length);
            for (int i = 0; i < _prevLapPct.Length; i++)
            {
                _prevLapPct[i] = double.NaN;
            }
        }

        public double GetLastCheckpointTimeSec(int checkpointIndex, int carIdx)
        {
            if (checkpointIndex < 0 || checkpointIndex >= _checkpointCount || carIdx < 0 || carIdx >= _maxCars)
            {
                return 0.0;
            }

            return _lastTimeSec[(checkpointIndex * _maxCars) + carIdx];
        }

        public void Update(
            double sessionTimeSec,
            float[] carIdxLapDistPct,
            int[] carIdxTrackSurface,
            int playerCarIdx,
            out bool playerCheckpointCrossed,
            out int playerCheckpointIndex,
            out int playerCheckpointIndexNow,
            out int timestampUpdates,
            out int invalidLapPctCount,
            out int onTrackCount)
        {
            playerCheckpointCrossed = false;
            playerCheckpointIndex = -1;
            playerCheckpointIndexNow = -1;
            timestampUpdates = 0;
            invalidLapPctCount = 0;
            onTrackCount = 0;

            if (carIdxLapDistPct == null)
            {
                return;
            }

            int carCount = Math.Min(_maxCars, carIdxLapDistPct.Length);
            for (int carIdx = 0; carIdx < carCount; carIdx++)
            {
                double lapPct = carIdxLapDistPct[carIdx];
                if (double.IsNaN(lapPct) || lapPct < 0.0 || lapPct >= 1.0)
                {
                    invalidLapPctCount++;
                    _prevLapPct[carIdx] = double.NaN;
                    if (carIdx == playerCarIdx)
                    {
                        playerCheckpointIndexNow = -1;
                    }
                    continue;
                }

                if (carIdx == playerCarIdx)
                {
                    int checkpointIndexNow = (int)Math.Floor(lapPct * _checkpointCount);
                    if (checkpointIndexNow < 0)
                    {
                        checkpointIndexNow = 0;
                    }
                    else if (checkpointIndexNow >= _checkpointCount)
                    {
                        checkpointIndexNow = _checkpointCount - 1;
                    }
                    playerCheckpointIndexNow = checkpointIndexNow;
                }

                bool onTrack = true;
                if (carIdxTrackSurface != null && carIdx < carIdxTrackSurface.Length)
                {
                    int surface = carIdxTrackSurface[carIdx];
                    if (surface == TrackSurfaceNotInWorld)
                    {
                        _prevLapPct[carIdx] = double.NaN;
                        continue;
                    }

                    onTrack = surface == TrackSurfaceOnTrack;
                }

                if (onTrack)
                {
                    onTrackCount++;
                }

                double prevLapPct = _prevLapPct[carIdx];
                if (double.IsNaN(prevLapPct))
                {
                    _prevLapPct[carIdx] = lapPct;
                    continue;
                }

                double startPct = prevLapPct;
                double endPct = lapPct;
                bool wrapped = false;
                if (endPct + 1e-6 < startPct && (startPct - endPct) > WrapThreshold)
                {
                    wrapped = true;
                    endPct += 1.0;
                }

                int startIndex = (int)Math.Floor(startPct * _checkpointCount);
                int endIndex = (int)Math.Floor(endPct * _checkpointCount);

                if (wrapped)
                {
                    if (startIndex < 0) startIndex = 0;
                }

                if (endIndex < startIndex)
                {
                    _prevLapPct[carIdx] = lapPct;
                    continue;
                }

                for (int checkpointIndex = startIndex + 1; checkpointIndex <= endIndex; checkpointIndex++)
                {
                    int normalizedIndex = checkpointIndex % _checkpointCount;
                    _lastTimeSec[(normalizedIndex * _maxCars) + carIdx] = sessionTimeSec;
                    timestampUpdates++;
                    if (carIdx == playerCarIdx)
                    {
                        playerCheckpointCrossed = true;
                        playerCheckpointIndex = normalizedIndex;
                    }
                }

                _prevLapPct[carIdx] = lapPct;
            }
        }
    }
}
