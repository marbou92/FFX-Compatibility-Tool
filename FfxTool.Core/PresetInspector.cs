using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FfxTool.Core
{
    /// <summary>
    /// One decoded keyframe of a preset parameter stream.
    ///
    /// Layout proven against FfxTool.Core.Tests/fixtures/sample_1.ffx (all
    /// three animated streams in the fixture decode with exact length match):
    ///   lhd3 (52 bytes, big-endian uint32s):
    ///     [0] 0x00D00BEE magic, [2] keyframe count, [4] record size (48)
    ///   ldat: count × record, and with record size 48:
    ///     +0  int32  time — raw preset ticks (AE stores these in the comp's
    ///                    own timebase; no public spec maps them to seconds,
    ///                    so the UI presents them as relative units)
    ///     +4  byte   interpolation into  this keyframe (1 linear 2 bezier 3 hold)
    ///     +5  byte   interpolation out of this keyframe
    ///     +8  double value
    ///     +16 double in-slope        +24 double in-influence
    ///     +32 double out-slope       +40 double out-influence
    ///   (tangent quadruple matches RESEARCH_NOTES.md's "bezier tangent
    ///   doubles for Graph Editor easing"; the fixture carries classic
    ///   Easy-Ease values like 1/3 and 1/6.)
    /// </summary>
    public class PresetKeyframe
    {
        public int Time;
        public double Value;
        public double InSlope;
        public double InInfluence;
        public double OutSlope;
        public double OutInfluence;
        public int InterpIn;
        public int InterpOut;

        /// <summary>Human label for this keyframe's easing, e.g. "Bezier".</summary>
        public string InterpLabel
        {
            get
            {
                string In(int v) => v == 1 ? "Linear" : v == 3 ? "Hold" : "Bezier";
                string a = In(InterpIn), b = In(InterpOut);
                return a == b ? a : a + " / " + b;
            }
        }
    }

    /// <summary>One parameter of one effect: static value or a keyframe stream.</summary>
    public class PresetParameter
    {
        public string Name;
        public string MatchName;
        public bool IsAnimated;
        public double? StaticValue;
        public double? Min;
        public double? Max;
        public readonly List<PresetKeyframe> Keyframes = new List<PresetKeyframe>();
    }

    /// <summary>Full parameter inspection of one effect inside a preset.</summary>
    public class PresetEffectDetails
    {
        public string MatchName;
        public string ShortName;
        public readonly List<PresetParameter> Parameters = new List<PresetParameter>();

        public int AnimatedCount
        {
            get { int n = 0; foreach (var p in Parameters) if (p.IsAnimated) n++; return n; }
        }
    }

    /// <summary>
    /// Read-only deep inspection of a .ffx preset: effect → parameters →
    /// keyframe streams. This NEVER modifies anything (the pipeline's rule
    /// about lhd3/ldat stays absolute — we only read); every parse step is
    /// guarded so an unusual or third-party stream degrades to "no data"
    /// instead of throwing at the caller.
    /// </summary>
    public static class PresetInspector
    {
        static readonly byte[] TDSP_FORM = RiffFile.Cid("tdsp");
        static readonly byte[] SSPC_FORM = RiffFile.Cid("sspc");
        static readonly byte[] TDMN = RiffFile.Cid("tdmn");
        static readonly byte[] TDSN = RiffFile.Cid("tdsn");
        static readonly byte[] FNAM = RiffFile.Cid("fnam");

        // Group/housekeeping markers that are not user-facing parameters.
        static readonly HashSet<string> SkipMatchNames = new HashSet<string>
        {
            "ADBE Effect Mask Parade",
            "ADBE Effect Mask Opacity",
            "ADBE Force CPU GPU",
            "ADBE Group End",
            "ADBE Effect Built In Params",
        };

        public static List<PresetEffectDetails> Inspect(byte[] data)
        {
            var tree = RiffFile.ParseFile(data);
            var besc = FindBesc(tree);

            // tdsp index entries and sspc parameter blocks pair BY POSITION —
            // the same rule Pipeline.RemoveEffectsByMatchName relies on.
            var tdsps = besc.Children.Where(c => c.IsContainer && Same(c.Form, TDSP_FORM)).ToList();
            var sspcs = besc.Children.Where(c => c.IsContainer && Same(c.Form, SSPC_FORM)).ToList();

            var result = new List<PresetEffectDetails>();
            int n = Math.Min(tdsps.Count, sspcs.Count);
            for (int i = 0; i < n; i++)
            {
                string matchName = TdmnEffectName(tdsps[i]);
                if (matchName == null) continue; // sentinel index entry

                var details = new PresetEffectDetails { MatchName = matchName };
                var fnam = sspcs[i].Children.FirstOrDefault(c => !c.IsContainer && Same(c.Cid, FNAM));
                if (fnam != null) details.ShortName = DecodeString(fnam.Content);

                foreach (var child in sspcs[i].Children)
                    if (child.IsContainer && Same(child.Form, RiffFile.Cid("tdgp")))
                        WalkGroup(child, details);

                result.Add(details);
            }
            return result;
        }

        static RiffNode FindBesc(RiffNode tree)
        {
            var besc = tree.Children[1];
            if (besc.Form == null || !Same(besc.Form, RiffFile.Cid("besc")))
                throw new InvalidOperationException("Expected the file's 2nd top-level chunk to be `LIST besc`.");
            return besc;
        }

        /// <summary>
        /// Walk a parameter group tree in document order. tdmn chunks carry
        /// the match name of whatever parameter record (LIST tdbs) follows
        /// them; nested tdgp groups are recursed into and their params are
        /// flattened with names kept, which matches how AE presents nested
        /// groups in the effect controls.
        /// </summary>
        static void WalkGroup(RiffNode group, PresetEffectDetails into)
        {
            string pendingMatch = null;
            foreach (var child in group.Children)
            {
                if (!child.IsContainer && Same(child.Cid, TDMN))
                {
                    pendingMatch = DecodeString(child.Content);
                }
                else if (child.IsContainer && Same(child.Form, RiffFile.Cid("tdgp")))
                {
                    WalkGroup(child, into);
                }
                else if (child.IsContainer && Same(child.Form, RiffFile.Cid("tdbs")))
                {
                    var p = ParseParameter(child, pendingMatch);
                    pendingMatch = null;
                    if (p != null && !SkipMatchNames.Contains(p.MatchName))
                        into.Parameters.Add(p);
                }
            }
        }

        static PresetParameter ParseParameter(RiffNode tdbs, string matchName)
        {
            var p = new PresetParameter { MatchName = matchName };
            foreach (var child in tdbs.Children)
            {
                if (!child.IsContainer && Same(child.Cid, TDSN))
                {
                    string name = DecodeString(child.Content);
                    if (!string.IsNullOrEmpty(name)) p.Name = name;
                }
                else if (!child.IsContainer && Same(child.Cid, RiffFile.Cid("cdat")))
                {
                    if (child.Content.Length >= 8) p.StaticValue = ReadBEDouble(child.Content, 0);
                }
                else if (!child.IsContainer && Same(child.Cid, RiffFile.Cid("tdum")))
                {
                    if (child.Content.Length >= 8) p.Min = ReadBEDouble(child.Content, 0);
                }
                else if (!child.IsContainer && Same(child.Cid, RiffFile.Cid("tduM")))
                {
                    if (child.Content.Length >= 8) p.Max = ReadBEDouble(child.Content, 0);
                }
                else if (child.IsContainer && Same(child.Form, RiffFile.Cid("list")))
                {
                    ParseKeyframeStream(child, p);
                }
            }
            p.Name = p.Name ?? matchName ?? "(unnamed parameter)";
            return p;
        }

        static void ParseKeyframeStream(RiffNode listNode, PresetParameter into)
        {
            var lhd3 = listNode.Children.FirstOrDefault(c => !c.IsContainer && Same(c.Cid, RiffFile.Cid("lhd3")));
            var ldat = listNode.Children.FirstOrDefault(c => !c.IsContainer && Same(c.Cid, RiffFile.Cid("ldat")));
            if (lhd3 == null || ldat == null || lhd3.Content.Length < 20 || ldat.Content.Length < 48)
                return;

            int count = (int)ReadBEUInt32(lhd3.Content, 8);
            int recSize = (int)ReadBEUInt32(lhd3.Content, 16);
            // Hard guards: anything outside the proven envelope (see class
            // comment) degrades to "no keyframes" instead of garbage rows.
            if (recSize < 48 || count < 1 || count > 100000) return;
            if (ldat.Content.Length != count * recSize) return;

            var bytes = ldat.Content;
            for (int r = 0; r < count; r++)
            {
                int o = r * recSize;
                var kf = new PresetKeyframe
                {
                    Time = (int)ReadBEUInt32(bytes, o),
                    InterpIn = o + 4 < bytes.Length ? bytes[o + 4] : 0,
                    InterpOut = o + 5 < bytes.Length ? bytes[o + 5] : 0,
                    Value = ReadBEDouble(bytes, o + 8),
                };
                if (recSize >= 48 && o + 48 <= bytes.Length)
                {
                    kf.InSlope = ReadBEDouble(bytes, o + 16);
                    kf.InInfluence = ReadBEDouble(bytes, o + 24);
                    kf.OutSlope = ReadBEDouble(bytes, o + 32);
                    kf.OutInfluence = ReadBEDouble(bytes, o + 40);
                }
                into.Keyframes.Add(kf);
            }
            into.IsAnimated = true;
        }

        /// <summary>
        /// Effect match name from a tdsp index entry: tdmn[0] is always the
        /// literal "ADBE Effect Parade" marker, tdmn[1] the real name. The
        /// tdmn chunks can sit inside nested sub-lists, hence the recursive
        /// search (same as Pipeline.TdmnEffectName).
        /// </summary>
        static string TdmnEffectName(RiffNode tdspNode)
        {
            var tdmns = RiffFile.FindAll(tdspNode, TDMN);
            if (tdmns.Count < 2) return null;
            return DecodeString(tdmns[1].Content);
        }

        /// <summary>
        /// Name chunks come in two encodings (RESEARCH_NOTES.md): CC files
        /// prefix with "Utf8" + big-endian length; native files are plain
        /// null-terminated. Decode accordingly, mirroring Pipeline.
        /// </summary>
        static string DecodeString(byte[] raw)
        {
            if (raw == null) return null;
            byte[] body = raw;
            if (raw.Length >= 8 && raw[0] == 'U' && raw[1] == 't' && raw[2] == 'f' && raw[3] == '8')
            {
                uint len = ReadBEUInt32(raw, 4);
                if (8 + len <= (uint)raw.Length)
                {
                    body = new byte[len];
                    Array.Copy(raw, 8, body, 0, len);
                    return Encoding.UTF8.GetString(body);
                }
            }
            int end = Array.IndexOf(raw, (byte)0);
            int n = end >= 0 ? end : raw.Length;
            return Encoding.GetEncoding("ISO-8859-1").GetString(raw, 0, n);
        }

        static uint ReadBEUInt32(byte[] b, int o) =>
            (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);

        static double ReadBEDouble(byte[] b, int o)
        {
            long v = ((long)b[o] << 56) | ((long)b[o + 1] << 48) | ((long)b[o + 2] << 40) |
                     ((long)b[o + 3] << 32) | ((long)b[o + 4] << 24) | ((long)b[o + 5] << 16) |
                     ((long)b[o + 6] << 8) | b[o + 7];
            return BitConverter.Int64BitsToDouble(v);
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
