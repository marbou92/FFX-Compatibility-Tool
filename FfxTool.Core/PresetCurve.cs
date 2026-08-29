using System;
using System.Collections.Generic;

namespace FfxTool.Core
{
    /// <summary>
    /// Read-only evaluation of a decoded keyframe stream — the math behind
    /// the Lister's keyframe timing and the AE-style value/speed graphs.
    ///
    /// TIMEBASE: ldat records carry keyframe times as int32 "ticks". No
    /// public Adobe spec pins a ticks-per-second constant, so the value
    /// below was derived empirically from the shipped ground truth
    /// (FfxTool.Core.Tests/fixtures/sample_1.ffx). The raw times 1877 /
    /// 5631 / 11264 / 21504 divide by 1024 into 1.833 / 5.5 / 11 / 21
    /// seconds, and every one of those lands on an EXACT 30 fps frame
    /// boundary (frames 55 / 165 / 330 / 630) — which is how real
    /// keyframes get placed. Any other constant produces fractional
    /// frames for at least one of them, so 1024 is the best-evidence
    /// mapping. It is a DISPLAY-time conversion only: ldat bytes are
    /// never read-modified-written by anything in this tool.
    ///
    /// EASING: each segment interpolates as a cubic Bezier that mirrors
    /// AE's tangent model — the outgoing handle of keyframe A sits at
    /// time A.t + outInfluence·Δt with value A.v + outSlope·(outInfluence
    /// ·Δt), and keyframe B's incoming handle mirrors it. Slopes are
    /// VALUE-PER-SECOND — the same numbers AE's Graph Editor speed readout
    /// shows — so the handle's value offset is simply speed × its time
    /// offset in seconds. (An earlier revision divided by TicksPerSecond
    /// here, which collapsed every real-world handle onto its keyframe and
    /// flattened eased curves into straight lines — Easy Ease only looked
    /// right because its slopes are zero.) Linear segments are straight
    /// lines, Hold segments step at the right keyframe. The fixture's
    /// Easy-Ease streams (slope 0, influences 1/3 + 1/6) render as the
    /// familiar S-curve.
    ///
    /// TWO INFLUENCE SANITIZERS keep real-world files AE-shaped (AE never
    /// draws a folded or a full-span handle, so neither do we):
    ///   1. A stored influence above 1.0 is a PERCENT (AE's own UI unit,
    ///      33.33 = one third) — some writers store the UI number instead
    ///      of the fraction; it is divided by 100 before use.
    ///   2. When the two handles of one segment would overlap (out + in
    ///      influences sum over 1), AE shrinks BOTH handles so they fill
    ///      exactly the whole span and no further — the RATIO of the two
    ///      handle spans is what the easing reads as, and it survives.
    ///      (The earlier midpoint pinch erased that ratio and drew a
    ///      symmetric bump no AE easing ever shows.)
    ///
    /// EVALUATION: ValueAt/SpeedAt evaluate the TRUE cubic Bezier — the
    /// very curve WPF's BezierTo draws. (An earlier revision evaluated a
    /// degenerate 3-point form here, so the hover probe and the speed
    /// graph silently rode a different curve than the one on screen —
    /// the probe floated off the line and the speed graph disagreed
    /// with the value graph.) Control times are order-clamped so the
    /// time axis never folds back, keeping the x(u) inversion valid.
    /// </summary>
    public static class PresetCurve
    {
        public const double TicksPerSecond = 1024.0;
        public const int InterpLinear = 1;
        public const int InterpBezier = 2;
        public const int InterpHold = 3;

        public static double Seconds(int ticks) => ticks / TicksPerSecond;

        /// <summary>One keyframe-to-keyframe span, pre-converted to seconds.</summary>
        public struct Segment
        {
            public double T0, V0;          // left keyframe (seconds, value)
            public double T1, V1;          // right keyframe
            public int Mode;               // InterpOut of the left keyframe
            public double C1T, C1V;        // bezier control point 1 (out of A)
            public double C2T, C2V;        // bezier control point 2 (into B)
        }

        /// <summary>
        /// Build the segment list for a keyframe stream. Handles mirror
        /// AE's (slope × influence × segment length) tangent geometry;
        /// influences read as percents when stored above 1.0, and an
        /// overlapping handle pair is shrunk AE-style — proportionally,
        /// keeping the easing ratio. A zero-length stream yields an empty
        /// list. Read-only on input.
        /// </summary>
        public static List<Segment> BuildSegments(List<PresetKeyframe> kfs)
        {
            var segs = new List<Segment>();
            if (kfs == null || kfs.Count < 2) return segs;

            for (int i = 0; i + 1 < kfs.Count; i++)
            {
                var a = kfs[i];
                var b = kfs[i + 1];
                double t0 = Seconds(a.Time), t1 = Seconds(b.Time);
                double dt = Math.Max(t1 - t0, 1e-9);

                var seg = new Segment
                {
                    T0 = t0, V0 = a.Value,
                    T1 = t1, V1 = b.Value,
                    Mode = a.InterpOut == InterpLinear || a.InterpOut == InterpHold
                        ? a.InterpOut : InterpBezier
                };

                // AE influence units: files written by AE store a fraction
                // (0.333…), but a value above 1.0 can only be the UI's
                // percent (33.33 = one third) — normalize before clamping
                double oi = Clamp01(NormInfluence(a.OutInfluence));
                double ii = Clamp01(NormInfluence(b.InInfluence));
                // handles: time offset = influence × Δt (seconds); value
                // offset = slope × time offset — slopes are value/second
                seg.C1T = t0 + oi * (t1 - t0);
                seg.C1V = a.Value + a.OutSlope * oi * (t1 - t0);
                seg.C2T = t1 - ii * (t1 - t0);
                seg.C2V = b.Value - b.InSlope * ii * (t1 - t0);
                // AE's overlap rule: the two handles of one segment may
                // share at most the whole span. Scale both by the same
                // factor so they exactly fill it — the easing's handle
                // RATIO (what the eye reads as the shape) survives, where
                // the old midpoint pinch drew a symmetric bump. After the
                // scale C1T ≤ C2T holds by construction; the final guard
                // only absorbs floating-point dust.
                double sum = oi + ii;
                if (sum > 1.0)
                {
                    double k = 1.0 / sum;
                    seg.C1T = t0 + oi * k * (t1 - t0);
                    seg.C2T = t1 - ii * k * (t1 - t0);
                }
                if (seg.C1T > seg.C2T)
                    seg.C1T = seg.C2T = 0.5 * (seg.C1T + seg.C2T);

                segs.Add(seg);
            }
            return segs;
        }

        /// <summary>
        /// Value of the stream at time t (seconds). Outside the stream the
        /// edge keyframe values hold, exactly like a preset applied to a
        /// longer layer.
        /// </summary>
        public static double ValueAt(List<Segment> segs, double t)
        {
            if (segs == null || segs.Count == 0) return double.NaN;
            var first = segs[0];
            if (t <= first.T0) return first.V0;
            var last = segs[segs.Count - 1];
            if (t >= last.T1) return last.V1;

            foreach (var s in segs)
            {
                if (t < s.T0 || t > s.T1) continue;
                double u = (t - s.T0) / Math.Max(s.T1 - s.T0, 1e-9);
                if (s.Mode == InterpHold) return s.V0;
                if (s.Mode == InterpLinear) return s.V0 + (s.V1 - s.V0) * u;
                return Bezier(s, SolveBezierT(s, t));
            }
            return last.V1;
        }

        /// <summary>
        /// Analytic speed |dv/dt| of the stream at time t (seconds) — the
        /// derivative of the same cubic the value graph draws. Linear
        /// segments carry a constant slope, Hold segments 0, Bezier
        /// segments the exact tangent magnitude; outside the stream the
        /// value is held, so the speed is 0.
        /// </summary>
        public static double SpeedAt(List<Segment> segs, double t)
        {
            if (segs == null || segs.Count == 0) return double.NaN;
            var first = segs[0];
            if (t < first.T0) return 0;
            var last = segs[segs.Count - 1];
            if (t > last.T1) return 0;
            foreach (var s in segs)
            {
                if (t > s.T1) continue; // first segment whose span reaches t
                if (s.Mode == InterpHold) return 0;
                if (s.Mode == InterpLinear)
                    return Math.Abs((s.V1 - s.V0) / Math.Max(s.T1 - s.T0, 1e-9));
                double u = SolveBezierT(s, Math.Min(Math.Max(t, s.T0), s.T1));
                double m = 1 - u;
                // dP/du of the cubic at u, for value and time separately
                double dv = 3 * m * m * (s.C1V - s.V0) + 6 * m * u * (s.C2V - s.C1V) + 3 * u * u * (s.V1 - s.C2V);
                double dt = 3 * m * m * (s.C1T - s.T0) + 6 * m * u * (s.C2T - s.C1T) + 3 * u * u * (s.T1 - s.C2T);
                return Math.Abs(dt) < 1e-12 ? 0 : Math.Abs(dv / dt);
            }
            return 0;
        }

        /// <summary>
        /// Uniform value samples across the stream — the value graph's
        /// polyline and (by differencing) the speed graph's curve.
        /// </summary>
        public static void SampleValues(List<Segment> segs, int count,
            out double[] times, out double[] values)
        {
            times = new double[count];
            values = new double[count];
            if (segs == null || segs.Count == 0 || count < 2) return;
            double t0 = segs[0].T0, t1 = segs[segs.Count - 1].T1;
            for (int i = 0; i < count; i++)
            {
                double t = t0 + (t1 - t0) * i / (count - 1);
                times[i] = t;
                values[i] = ValueAt(segs, t);
            }
        }

        static double SolveBezierT(Segment s, double t)
        {
            // x(u) is monotonic (control times sit inside the span and are
            // order-clamped in BuildSegments); 22 bisection iterations put
            // the residual far below one frame — and because the drawn
            // Path evaluates this same cubic, the hover probe and the
            // speed graph ride exactly on the visible curve
            double lo = 0, hi = 1;
            for (int i = 0; i < 22; i++)
            {
                double mid = 0.5 * (lo + hi);
                double x = Cubic(s.T0, s.C1T, s.C2T, s.T1, mid);
                if (x < t) lo = mid; else hi = mid;
            }
            return 0.5 * (lo + hi);
        }

        /// <summary>The true cubic Bezier — the curve WPF's BezierTo draws.</summary>
        static double Bezier(Segment s, double u) =>
            Cubic(s.V0, s.C1V, s.C2V, s.V1, u);

        static double Cubic(double p0, double p1, double p2, double p3, double u)
        {
            double m = 1 - u;
            return m * m * m * p0 + 3 * m * m * u * p1 + 3 * m * u * u * p2 + u * u * u * p3;
        }

        static double Lerp(double a, double b, double u) => a + (b - a) * u;

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>A stored influence above 1.0 is AE's UI percent
        /// (33.33 means one third) — bring it back to a fraction.</summary>
        static double NormInfluence(double v) => v > 1.0 ? v / 100.0 : v;
    }
}
