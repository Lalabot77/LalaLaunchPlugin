using System;
using System.Collections.Generic;
using System.Globalization;

namespace LaunchPlugin
{
    internal sealed class DecelCapture
    {
        // Master kill switch (runtime, no const-fold warning)
        private static readonly bool MASTER_ENABLED = true;

        // Tunables
        private const double SPEED_MAX_KPH = 200.0;
        private const double SPEED_MIN_KPH = 50.0;

        private const double BRAKE_MIN_PCT = 0.12;     // 12%
        private const double THROTTLE_MAX_PCT = 0.05;  // 5%

        private const double LATG_MAX = 0.25;          // straight-line filter
        private const int LOG_HZ = 20;
        private static readonly TimeSpan LOG_PERIOD = TimeSpan.FromSeconds(1.0 / LOG_HZ);

        private const double DT_MIN_S = 0.005;
        private const double DT_MAX_S = 0.200;

        // State
        private bool _wasCapturing;
        private DateTime _lastLogUtc = DateTime.MinValue;

        private double _prevSpeedMps = double.NaN;
        private DateTime _prevTickUtc = DateTime.MinValue;

        private double _prevSpeedKphAny = double.NaN;
        private DateTime _prevAnyUtc = DateTime.MinValue;

        private readonly List<double> _aSamples = new List<double>(4096); // dv/dt (m/s^2)
        private readonly List<double> _gSamples = new List<double>(4096); // lon (m/s^2)

        private int _sampleCount;
        private double _vStartKph;
        private double _vEndKph;
        private string _runTag = string.Empty;

        // Distance capture 200->50
        private bool _distArmed;
        private bool _distStarted;
        private double _dist200to50_m;

        public void Update(
            bool captureToggleOn,
            double speedKph,
            double brakePct01,
            double throttlePct01,
            double lonAccel_mps2, // <-- PASS M/S^2 NOW (no G conversion inside)
            double latG,
            string carNameOrClass,
            string trackName,
            string sessionToken
        )
        {
            if (!MASTER_ENABLED) return;

            var nowUtc = DateTime.UtcNow;

            // Track "any tick" prev so we can detect crossing 200/50 even if we aren't logging
            if (_prevAnyUtc == DateTime.MinValue)
            {
                _prevAnyUtc = nowUtc;
                _prevSpeedKphAny = speedKph;
            }

            // Toggle off -> end run
            if (!captureToggleOn)
            {
                if (_wasCapturing) EndRun(carNameOrClass, trackName, sessionToken);
                _wasCapturing = false;
                _prevAnyUtc = DateTime.MinValue;
                _prevSpeedKphAny = double.NaN;
                return;
            }

            // Toggle on -> start run
            if (!_wasCapturing)
            {
                StartRun(carNameOrClass, trackName, sessionToken, speedKph);
                _wasCapturing = true;
            }

            // Distance arming: wait until we were above 200, then we cross down into <=200
            if (!_distArmed)
            {
                if (!double.IsNaN(_prevSpeedKphAny) && _prevSpeedKphAny > SPEED_MAX_KPH)
                    _distArmed = true;
            }
            if (_distArmed && !_distStarted)
            {
                if (!double.IsNaN(_prevSpeedKphAny) && _prevSpeedKphAny > SPEED_MAX_KPH && speedKph <= SPEED_MAX_KPH)
                {
                    _distStarted = true;
                    _dist200to50_m = 0.0;
                }
            }

            // Hard gates (same as before)
            bool inBand = speedKph <= SPEED_MAX_KPH && speedKph >= SPEED_MIN_KPH;
            if (!inBand)
            {
                _prevAnyUtc = nowUtc;
                _prevSpeedKphAny = speedKph;
                return;
            }

            if (brakePct01 < BRAKE_MIN_PCT) { _prevAnyUtc = nowUtc; _prevSpeedKphAny = speedKph; return; }
            if (throttlePct01 > THROTTLE_MAX_PCT) { _prevAnyUtc = nowUtc; _prevSpeedKphAny = speedKph; return; }
            if (Math.Abs(latG) > LATG_MAX) { _prevAnyUtc = nowUtc; _prevSpeedKphAny = speedKph; return; }

            // Integrate distance between 200->50 using trapezoid rule (only once started)
            if (_distStarted && _prevAnyUtc != DateTime.MinValue)
            {
                double dtAny = (nowUtc - _prevAnyUtc).TotalSeconds;
                if (dtAny >= DT_MIN_S && dtAny <= DT_MAX_S)
                {
                    double vPrev = (_prevSpeedKphAny / 3.6);
                    double vNow = (speedKph / 3.6);
                    _dist200to50_m += 0.5 * (vPrev + vNow) * dtAny;
                }
            }

            // Log throttle
            if (_lastLogUtc != DateTime.MinValue && (nowUtc - _lastLogUtc) < LOG_PERIOD)
            {
                _prevAnyUtc = nowUtc;
                _prevSpeedKphAny = speedKph;
                return;
            }
            _lastLogUtc = nowUtc;

            // dv/dt decel
            double speedMps = speedKph / 3.6;
            double aMps2 = double.NaN;

            if (!double.IsNaN(_prevSpeedMps) && _prevTickUtc != DateTime.MinValue)
            {
                double dt = (nowUtc - _prevTickUtc).TotalSeconds;
                if (dt >= DT_MIN_S && dt <= DT_MAX_S)
                {
                    aMps2 = (_prevSpeedMps - speedMps) / dt;
                    if (aMps2 > 0.1 && aMps2 < 30.0) _aSamples.Add(aMps2);
                }
            }

            _prevSpeedMps = speedMps;
            _prevTickUtc = nowUtc;

            if (lonAccel_mps2 > 0.1 && lonAccel_mps2 < 30.0) _gSamples.Add(lonAccel_mps2);

            _sampleCount++;
            _vEndKph = speedKph;

            SimHub.Logging.Current.Info(string.Format(
                CultureInfo.InvariantCulture,
                "[LalaPlugin:DecelCap] RUN={0} v={1:0.0}kph brake={2:0.00} thr={3:0.00} latG={4:0.00} lonA={5:0.00} dvdtA={6:0.00} dist200to50={7:0.0}",
                _runTag,
                speedKph,
                brakePct01,
                throttlePct01,
                latG,
                lonAccel_mps2,
                double.IsNaN(aMps2) ? -1.0 : aMps2,
                _dist200to50_m
            ));

            // Auto-end at bottom speed
            if (speedKph <= SPEED_MIN_KPH + 0.5)
            {
                EndRun(carNameOrClass, trackName, sessionToken);
                _wasCapturing = false;
                _prevAnyUtc = DateTime.MinValue;
                _prevSpeedKphAny = double.NaN;
            }
            else
            {
                _prevAnyUtc = nowUtc;
                _prevSpeedKphAny = speedKph;
            }
        }

        private void StartRun(string car, string track, string token, double speedKph)
        {
            _aSamples.Clear();
            _gSamples.Clear();
            _sampleCount = 0;

            _prevSpeedMps = double.NaN;
            _prevTickUtc = DateTime.MinValue;
            _lastLogUtc = DateTime.MinValue;

            _prevAnyUtc = DateTime.MinValue;
            _prevSpeedKphAny = double.NaN;

            _vStartKph = speedKph;
            _vEndKph = speedKph;

            _distArmed = false;
            _distStarted = false;
            _dist200to50_m = 0.0;

            _runTag = $"{Short(token)}:{Short(track)}:{Short(car)}:{DateTime.UtcNow:HHmmss}";

            SimHub.Logging.Current.Info(string.Format(
                CultureInfo.InvariantCulture,
                "[LalaPlugin:DecelCap] START RUN={0} car={1} track={2} token={3} vStart={4:0.0}kph band=[{5:0.0},{6:0.0}]",
                _runTag, car, track, token, speedKph, SPEED_MIN_KPH, SPEED_MAX_KPH
            ));
        }

        private void EndRun(string car, string track, string token)
        {
            var dvdtMed = Median(_aSamples);
            var dvdtP75 = Percentile(_aSamples, 0.75);

            var lonMed = Median(_gSamples);
            var lonP75 = Percentile(_gSamples, 0.75);

            SimHub.Logging.Current.Info(string.Format(
                CultureInfo.InvariantCulture,
                "[LalaPlugin:DecelCap] END   RUN={0} car={1} track={2} token={3} samples={4} vStart={5:0.0} vEnd={6:0.0} " +
                "dvdtMed={7:0.00} dvdtP75={8:0.00} lonMed={9:0.00} lonP75={10:0.00} dist200to50_m={11:0.0}",
                _runTag, car, track, token, _sampleCount, _vStartKph, _vEndKph,
                dvdtMed, dvdtP75, lonMed, lonP75, _dist200to50_m
            ));

            _runTag = string.Empty;
        }

        private static string Short(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "na";
            s = s.Trim();
            return s.Length <= 12 ? s : s.Substring(0, 12);
        }

        private static double Median(List<double> values)
        {
            if (values == null || values.Count == 0) return double.NaN;
            values.Sort();
            int n = values.Count;
            if ((n & 1) == 1) return values[n / 2];
            return 0.5 * (values[(n / 2) - 1] + values[n / 2]);
        }

        private static double Percentile(List<double> values, double p)
        {
            if (values == null || values.Count == 0) return double.NaN;
            if (p <= 0) return Min(values);
            if (p >= 1) return Max(values);

            values.Sort();
            double idx = (values.Count - 1) * p;
            int lo = (int)Math.Floor(idx);
            int hi = (int)Math.Ceiling(idx);
            if (lo == hi) return values[lo];
            double frac = idx - lo;
            return values[lo] + (values[hi] - values[lo]) * frac;
        }

        private static double Min(List<double> values)
        {
            double m = double.PositiveInfinity;
            for (int i = 0; i < values.Count; i++) if (values[i] < m) m = values[i];
            return double.IsInfinity(m) ? double.NaN : m;
        }

        private static double Max(List<double> values)
        {
            double m = double.NegativeInfinity;
            for (int i = 0; i < values.Count; i++) if (values[i] > m) m = values[i];
            return double.IsInfinity(m) ? double.NaN : m;
        }
    }
}
