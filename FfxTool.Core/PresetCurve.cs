using System;
using System.Collections.Generic;

namespace FfxTool.Core
{
    /// <summary>
    /// Read-only evaluation of a decoded keyframe stream — the math behind
    /// the Lister's keyframe timing and the AE-style value/speed graphs.
    ///
    /// TIMEBASE: ldat records carry keyframe times as int32 "ticks" whose
    /// unit is 1/1024 of a 30 fps FRAME — i.e. 30 × 1024 = 30720 ticks per
    /// second (the tick AE's preset writer inherits from its internal
    /// ticks-per-frame quantization). Three independent proofs from a real
    /// AE-written preset pinned this after an earlier revision read the
    /// tick as 1/1024 SECOND (a 30× time stretch that squashed every speed
    /// graph to 1/30 of AE's values while leaving endpoint speeds — the
    /// stored slopes — untouched, the exact asymmetry the side-by-side
    /// screenshots showed):
    ///   1. The preset's `beso` (basic-settings) chunk carries 30720
    ///      outright, next to its 65536-based dimensions — the file states
    ///      its own ticks-per-second.
    ///   2. Integrating AE's own drawn speed graph column-by-column must
    ///      reproduce the stream's total value travel (∫dv/dt dt = Δv =
    ///      -365.88); AE's pixels give that integral only when the keyed
    ///      span 13312 ticks = 0.4333 s — 13312/30720 — while the old
    ///      reading (13.0 s) overshoots the travel by ~30×.
    ///   3. With 0.4333 s spans, the decoded tangents reproduce AE's speed
    ///      curve to pixel accuracy: the early notch bottoms at -1636/s
    ///      (AE: -1637) and the main valley at -3215/s (AE: -3226), and
    ///      the keyframe x-ratios 7006:6306 = 1.111 match AE's markers
    ///      (93.5 px : 84 px = 1.113).
    /// The same constant re-explains the older fixture: its times 1877 /
    /// 5631 / 11264 / 21504 are whole-or-half FRAMES in 1024-ticks-per-
    /// frame units (1.833 / 5.5 / 11 / 21 × 1024 — 11264 and 21504 are
    /// exactly 11 and 21 frames), which the earlier revision mislabeled
    /// as seconds. It remains a DISPLAY-time conversion only: ldat bytes
    /// are never read-modified-written by anything in this tool.
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
        /// <summary>The preset file's tick base: 1024 ticks per 30 fps
        /// frame = 30720 ticks per second (see the TIMEBASE note — an
        /// earlier 1024.0 here stretched every curve 30× and flattened
        /// every speed graph to 1/30 of AE's values).</summary>
        public const double TicksPerSecond = 30720.0;
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
        /// Build the segment list for a keyframe stream (dimension 0).
        /// Handles mirror AE's tangent geometry (slope x influence x
        /// segment length); influences read as percents when stored
        /// above 1.0, and an overlapping handle pair is shrunk AE-style
        /// - proportionally, keeping the easing ratio. A zero-length
        /// stream yields an empty list. Read-only on input.
        /// </summary>
        public static List<Segment> BuildSegments(List<PresetKeyframe> kfs)
        {
            return BuildSegments(kfs, 0);
        }

        /// <summary>
        /// Dimension overload. Dimension 0 is the classic scalar stream.
        /// Dimension 1 builds the SAME tangent geometry from a 2D
        /// stream's second-dimension value and tangent fields - AE's
        /// value graph draws one curve PER dimension (round-25 research:
        /// "when you animate Position, the value graph shows two
        /// separate lines, X and Y"), and the Y curve deserves the
        /// file's real easing, not a straight-line guess. Keyframes
        /// whose Y did not decode (NaN) are skipped; undecoded Y
        /// tangents (NaN) read as zero, which degenerates the bezier
        /// into exactly the line a 1D stream without tangents draws.
        /// </summary>
        public static List<Segment> BuildSegments(List<PresetKeyframe> kfs, int dim)
        {
            var segs = new List<Segment>();
            if (kfs == null || kfs.Count < 2) return segs;
            if (dim != 1)
            {
                for (int i = 0; i + 1 < kfs.Count; i++)
                    segs.Add(MakeSegment(kfs[i], kfs[i + 1], false));
                return segs;
            }
            var ys = new List<PresetKeyframe>();
            foreach (var k in kfs)
                if (!double.IsNaN(k.Value2) && !double.IsInfinity(k.Value2)) ys.Add(k);
            for (int i = 0; i + 1 < ys.Count; i++)
                segs.Add(MakeSegment(ys[i], ys[i + 1], true));
            return segs;
        }

        /// <summary>One keyframe-to-keyframe span with AE's tangent
        /// geometry, built from either dimension's numbers. The dim-0
        /// path is byte-for-byte the round-22 math (shape-verified
        /// since round 21); the dim-1 path feeds it the Y fields
        /// through the same influence normalization and overlap
        /// rescale.</summary>
        static Segment MakeSegment(PresetKeyframe a, PresetKeyframe b, bool dim1)
        {
            double v0 = dim1 ? a.Value2 : a.Value;
            double v1 = dim1 ? b.Value2 : b.Value;
            int aOut = dim1 ? (a.InterpOut2 < 0 ? a.InterpOut : a.InterpOut2) : a.InterpOut;
            double outSlope = Sanitize(dim1 ? a.OutSlope2 : a.OutSlope);
            double outInfl = Sanitize(dim1 ? a.OutInfluence2 : a.OutInfluence);
            double inSlope = Sanitize(dim1 ? b.InSlope2 : b.InSlope);
            double inInfl = Sanitize(dim1 ? b.InInfluence2 : b.InInfluence);

            double t0 = Seconds(a.Time), t1 = Seconds(b.Time);

            var seg = new Segment
            {
                T0 = t0, V0 = v0,
                T1 = t1, V1 = v1,
                Mode = aOut == InterpLinear || aOut == InterpHold
                    ? aOut : InterpBezier
            };

            // AE influence units: files written by AE store a fraction
            // (0.333...), but a value above 1.0 can only be the UI's
            // percent (33.33 = one third) - normalize before clamping
            double oi = Clamp01(NormInfluence(outInfl));
            double ii = Clamp01(NormInfluence(inInfl));
            // handles: time offset = influence x dt (seconds); value
            // offset = slope x time offset - slopes are value/second
            seg.C1T = t0 + oi * (t1 - t0);
            seg.C1V = v0 + outSlope * oi * (t1 - t0);
            seg.C2T = t1 - ii * (t1 - t0);
            seg.C2V = v1 - inSlope * ii * (t1 - t0);
            // AE's overlap rule: the two handles of one segment may
            // share at most the whole span. Scale both by the same
            // factor so they exactly fill it - the easing's handle
            // RATIO (what the eye reads as the shape) survives, where
            // the old midpoint pinch drew a symmetric bump. The value
            // offsets scale WITH the time offsets so each handle keeps
            // the slope the file stored (rescaling the times alone
            // silently steepened every resampled ease). After the
            // scale C1T <= C2T holds by construction; the final guard
            // only absorbs floating-point dust.
            double sum = oi + ii;
            if (sum > 1.0)
            {
                double k = 1.0 / sum;
                seg.C1T = t0 + oi * k * (t1 - t0);
                seg.C1V = v0 + outSlope * oi * k * (t1 - t0);
                seg.C2T = t1 - ii * k * (t1 - t0);
                seg.C2V = v1 - inSlope * ii * k * (t1 - t0);
            }
            if (seg.C1T > seg.C2T)
                seg.C1T = seg.C2T = 0.5 * (seg.C1T + seg.C2T);

            return seg;
        }

        /// <summary>NaN tangent fields (undecoded dimension-1 blocks)
        /// act as zero - the handle then sits on its keyframe and the
        /// bezier degenerates to the straight line the stream earns.</summary>
        static double Sanitize(double v) => double.IsNaN(v) ? 0 : v;

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
        /// SIGNED speed dv/dt of the stream at time t (seconds) — the
        /// derivative of the same cubic the value graph draws, sign
        /// included, exactly what AE's Graph Editor speed readout and
        /// speed graph show: an increasing value plots ABOVE zero, a
        /// decreasing one BELOW it (the user's AE screenshot pairs the
        /// descending value ease with a speed dip under the -500 line).
        /// Linear segments carry their constant slope, Hold segments 0,
        /// Bezier segments the exact tangent magnitude; outside the
        /// stream the value is held, so the speed is 0.
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
                    return (s.V1 - s.V0) / Math.Max(s.T1 - s.T0, 1e-9);
                double u = SolveBezierT(s, Math.Min(Math.Max(t, s.T0), s.T1));
                double m = 1 - u;
                // dP/du of the cubic at u, for value and time separately
                double dv = 3 * m * m * (s.C1V - s.V0) + 6 * m * u * (s.C2V - s.C1V) + 3 * u * u * (s.V1 - s.C2V);
                double dt = 3 * m * m * (s.C1T - s.T0) + 6 * m * u * (s.C2T - s.C1T) + 3 * u * u * (s.T1 - s.C2T);
                return Math.Abs(dt) < 1e-12 ? 0 : dv / dt;
            }
            return 0;
        }

        /// <summary>
        /// AE's speed graph for a MULTIDIMENSIONAL property: the
        /// magnitude of the velocity vector - sqrt(vx^2 + vy^2) -
        /// combining every dimension's own signed rate into the ONE
        /// curve AE draws (round-25 research: "the speed graph combines
        /// them into one single line representing the object's overall
        /// speed"). 1D callers keep SpeedAt's signed value. NaN when
        /// dimension 1 is missing, so callers can fall back to the 1D
        /// read.
        /// </summary>
        public static double SpeedMagnitudeAt(List<Segment> segs0, List<Segment> segs1, double t)
        {
            if (segs1 == null || segs1.Count == 0) return double.NaN;
            double vx = SpeedAt(segs0, t);
            double vy = SpeedAt(segs1, t);
            if (double.IsNaN(vx) || double.IsNaN(vy)) return double.NaN;
            return Math.Sqrt(vx * vx + vy * vy);
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
