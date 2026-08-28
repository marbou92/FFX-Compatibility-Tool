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

        /// <summary>
        /// Human label for this keyframe's easing. Linear and Hold come
        /// straight from the stored interpolation codes; everything else
        /// is a Bezier, and the stored tangents tell AE's Easy Ease apart
        /// from a hand-drawn curve: Easy Ease is exactly "zero speed with
        /// the classic influence" (1/3 mid-stream, 1/6 on stream edges),
        /// which is how the shipped fixture's smooth streams decode.
        /// </summary>
        public string InterpLabel
        {
            get
            {
                string a = SideLabel(InterpIn, InSlope, InInfluence);
                string b = SideLabel(InterpOut, OutSlope, OutInfluence);
                return a == b ? a : a + " / " + b;
            }
        }

        static string SideLabel(int code, double slope, double influence)
        {
            if (code == 1) return "Linear";
            if (code == 3) return "Hold";
            return IsEasyEase(slope, influence) ? "Easy Ease" : "Bezier";
        }

        static bool IsEasyEase(double slope, double influence) =>
            Math.Abs(slope) < 1e-9 && influence > 1e-9 && influence <= 0.5;
    }

    /// <summary>
    /// Parameter control kinds, reverse-engineered from the `LIST parT`
    /// block every AE-saved preset carries inside its sspc (one
    /// tdmn → pard → pdnm triplet per parameter, in declaration order).
    /// The 148-byte pard descriptor states the control type as a
    /// big-endian uint32 at offset 12; the values were proven against
    /// sample_1.ffx, whose 223 parameters cover every kind the UI renders:
    ///   0 LAYER ("Host Layer", "Matte Layer"), 2 FIXED_SLIDER (min/max
    ///   ranges), 3 ANGLE ("Angle" = 90.0°), 4 CHECKBOX ("Invert Mocha"),
    ///   5 COLOR (RGB doubles, 0-255 scale in the fixture), 6 POINT
    ///   (X/Y doubles), 7 POPUP (cdat = 1-based index into the pdnm menu
    ///   "No|Tile|Reflect" → 3.0 = "Reflect"), 9/15 BUTTON ("Load Preset",
    ///   BCC's "Presets"), 11 ARBITRARY_DATA (mocha blobs), 12 PATH
    ///   ("Select Host Mask"), 13/14 GROUP_START/GROUP_END (named /
    ///   anonymous markers that flatten into display groups).
    /// </summary>
    public static class PresetParamKind
    {
        public const int Unknown = -1;
        public const int Layer = 0;
        public const int Slider = 1;
        public const int FixedSlider = 2;
        public const int Angle = 3;
        public const int Checkbox = 4;
        public const int Color = 5;
        public const int Point = 6;
        public const int Popup = 7;
        public const int FloatSlider = 9;     // BCC-style custom control row
        public const int ArbitraryData = 11;
        public const int Path = 12;
        public const int GroupStart = 13;
        public const int GroupEnd = 14;
        public const int Button = 15;
    }

    /// <summary>One parameter of one effect: static value or a keyframe stream.</summary>
    public class PresetParameter
    {
        public string Name;
        public string MatchName;
        /// <summary>
        /// Enclosing display group path ("Compositing Options", or deeper
        /// levels joined with '\u0001'), or null for top-level parameters —
        /// mirrors how AE nests parameters under tdgp disclosure groups.
        /// Group markers declared the plugin way (parT kinds 13/14) fold
        /// into the same path, so both AE nesting styles render alike.
        /// </summary>
        public string Group;
        /// <summary>Control kind from the parT pard descriptor (PresetParamKind).</summary>
        public int Kind = PresetParamKind.Unknown;
        /// <summary>Popup menu labels, in order — the '|' split of the parT pdnm chunk.</summary>
        public string[] MenuItems;
        public bool IsAnimated;
        public double? StaticValue;
        /// <summary>Extra cdat doubles: POINT's Y (offset 8), COLOR's G/B/A.</summary>
        public double? StaticValue2;
        public double? StaticValue3;
        public double? StaticValue4;
        public double? Min;
        public double? Max;
        public readonly List<PresetKeyframe> Keyframes = new List<PresetKeyframe>();
    }

    /// <summary>Full parameter inspection of one effect inside a preset.</summary>
    public class PresetEffectDetails
    {
        public string MatchName;
        public string ShortName;
        /// <summary>
        /// Non-null when this effect's parameter tree couldn't be decoded.
        /// The slot is still emitted (Parameters stays empty) so effect
        /// indexes stay aligned with the file's real effects — the UI
        /// surfaces the message instead of silently hiding the block.
        /// </summary>
        public string Error;
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

        /// <summary>parT metadata of one parameter: control kind, the pard's
        /// embedded display name (AE's fallback when the tdbs carries no
        /// tdsn), and the popup menu labels.</summary>
        private class ParamMeta
        {
            public int Kind = PresetParamKind.Unknown;
            public string PardName;
            public string[] Menu;
        }

        public static List<PresetEffectDetails> Inspect(byte[] data) => Inspect(data, null);

        /// <summary>
        /// Same inspection, but every decode problem is appended to
        /// <paramref name="errors"/> (human-readable, one line each)
        /// instead of being swallowed — the UI and the log can show the
        /// user exactly what could and couldn't be read.
        /// </summary>
        public static List<PresetEffectDetails> Inspect(byte[] data, List<string> errors)
        {
            var tree = RiffFile.ParseFile(data);
            var besc = FindBesc(tree);

            // tdsp index entries and sspc parameter blocks pair BY POSITION
            // among the REAL effects — the sentinel index entry has no sspc
            // of its own (the invariant Pipeline.RemoveEffectsByMatchName
            // enforces). The sentinel must therefore be excluded from the
            // tdsp list BEFORE pairing: pairing the raw list shifted every
            // effect one sspc down whenever the sentinel wasn't the last
            // entry — a single-effect preset decoded to nothing at all,
            // and multi-effect presets showed the wrong effect's parameters.
            var tdsps = besc.Children.Where(c => c.IsContainer && Same(c.Form, TDSP_FORM)).ToList();
            var sspcs = besc.Children.Where(c => c.IsContainer && Same(c.Form, SSPC_FORM)).ToList();

            var realTdsps = new List<RiffNode>();
            foreach (var t in tdsps)
                if (TdmnEffectName(t) != null) realTdsps.Add(t);

            if (realTdsps.Count != sspcs.Count)
                errors?.Add($"{realTdsps.Count} effect index entries but {sspcs.Count} parameter blocks — " +
                            "unexpected file structure; unpaired entries were skipped");

            var result = new List<PresetEffectDetails>(realTdsps.Count);
            int n = Math.Min(realTdsps.Count, sspcs.Count);
            for (int i = 0; i < n; i++)
            {
                string matchName = TdmnEffectName(realTdsps[i]);
                var details = new PresetEffectDetails { MatchName = matchName };
                try
                {
                    var fnam = sspcs[i].Children.FirstOrDefault(c => !c.IsContainer && Same(c.Cid, FNAM));
                    if (fnam != null) details.ShortName = DecodeString(fnam.Content);

                    // The effect's parT parameter tree: the only place the preset
                    // states WHAT CONTROL each parameter renders as (checkbox,
                    // popup + menu labels, slider, angle, color...). Parsing is
                    // additive and guarded — a preset without parT (or with an
                    // unexpected layout) still inspects, just with Unknown kinds.
                    var meta = ParseParamTree(sspcs[i]);

                    foreach (var child in sspcs[i].Children)
                        if (child.IsContainer && Same(child.Form, RiffFile.Cid("tdgp")))
                        {
                            // the effect's ROOT tdgp: its own tdsn is the effect
                            // display name, not a parameter group
                            WalkGroup(child, details, null, meta);
                            if (string.IsNullOrEmpty(details.ShortName))
                            {
                                var rootName = child.Children.FirstOrDefault(
                                    c => !c.IsContainer && Same(c.Cid, TDSN));
                                if (rootName != null) details.ShortName = DecodeString(rootName.Content);
                            }
                        }
                }
                catch (Exception ex)
                {
                    // one malformed effect must never sink the whole
                    // inspection — keep the slot (indexes stay aligned)
                    // and say what went wrong
                    details.Error = ex.GetType().Name + ": " + ex.Message;
                    errors?.Add($"effect #{i + 1} ({matchName ?? "?"}) couldn't be decoded — {details.Error}");
                }
                result.Add(details);
            }
            return result;
        }

        /// <summary>
        /// Reads one effect's LIST parT (direct sspc child): a flat run of
        /// tdmn (match name) → pard (148-byte descriptor) → pdnm (display
        /// name, or the popup's '|' menu list). The pard's uint32 at offset
        /// 12 is the control kind; its byte-16 name field is AE's own
        /// display-name fallback. Any surprise degrades to "no metadata".
        /// </summary>
        static Dictionary<string, ParamMeta> ParseParamTree(RiffNode sspc)
        {
            var map = new Dictionary<string, ParamMeta>();
            try
            {
                var parT = sspc.Children.FirstOrDefault(
                    c => c.IsContainer && Same(c.Form, RiffFile.Cid("parT")));
                if (parT == null) return map;

                string pending = null;   // tdmn awaiting its pard
                string lastMatch = null; // match name of the last pard seen (pdnm follows it)
                foreach (var child in parT.Children)
                {
                    if (!child.IsContainer && Same(child.Cid, TDMN))
                    {
                        pending = DecodeString(child.Content);
                        lastMatch = null; // a stray pdnm must never leak into the next entry
                    }
                    else if (!child.IsContainer && Same(child.Cid, RiffFile.Cid("pard")))
                    {
                        var meta = new ParamMeta();
                        var p = child.Content;
                        if (p.Length >= 16) meta.Kind = (int)ReadBEUInt32(p, 12);
                        // the pard's name field starts at byte 16, null-padded
                        int room = Math.Min(64, p.Length - 16);
                        if (room > 0)
                        {
                            int end = Array.IndexOf(p, (byte)0, 16, room);
                            int len = (end < 0 ? room : end) - 16;
                            if (len < 0) len = 0;
                            meta.PardName = Encoding.GetEncoding("ISO-8859-1").GetString(p, 16, len);
                        }
                        if (!string.IsNullOrEmpty(pending))
                        {
                            map[pending] = meta;
                            lastMatch = pending;
                        }
                        pending = null;
                    }
                    else if (!child.IsContainer && Same(child.Cid, RiffFile.Cid("pdnm")))
                    {
                        // pdnm follows its pard; the '|' split is the popup menu
                        if (lastMatch == null || !map.ContainsKey(lastMatch)) continue;
                        string text = DecodeString(child.Content);
                        if (text != null) map[lastMatch].Menu = text.Split('|');
                    }
                }
            }
            catch { return new Dictionary<string, ParamMeta>(); }
            return map;
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
        /// them; nested tdgp groups are recursed into as DISPLAY groups —
        /// their first tdsn leaf names them ("Compositing Options") and the
        /// path is stamped on every parameter found inside, which mirrors
        /// how AE nests groups in the effect controls. Plugins that declare
        /// groups the flat way (parT kinds 13 GROUP_START / 14 GROUP_END)
        /// fold into the same display-group paths, and their marker rows are
        /// never surfaced as parameters.
        /// </summary>
        static void WalkGroup(RiffNode group, PresetEffectDetails into, string groupPath,
                              Dictionary<string, ParamMeta> meta)
        {
            string pendingMatch = null;
            var declared = new List<string>(); // open GROUP_START display names
            foreach (var child in group.Children)
            {
                if (!child.IsContainer && Same(child.Cid, TDMN))
                {
                    pendingMatch = DecodeString(child.Content);
                }
                else if (child.IsContainer && Same(child.Form, RiffFile.Cid("tdgp")))
                {
                    // the tdgp's own tdsn names it ("Compositing Options");
                    // any open declared groups extend the path beneath it
                    string nested = ChildGroupPath(child, groupPath);
                    WalkGroup(child, into, JoinPaths(nested, declared), meta);
                }
                else if (child.IsContainer && Same(child.Form, RiffFile.Cid("tdbs")))
                {
                    var p = ParseParameter(child, pendingMatch, meta);
                    pendingMatch = null;
                    if (p.Kind == PresetParamKind.GroupStart)
                    {
                        declared.Add(string.IsNullOrEmpty(p.Name) ? p.MatchName : p.Name);
                        continue;
                    }
                    if (p.Kind == PresetParamKind.GroupEnd)
                    {
                        if (declared.Count > 0) declared.RemoveAt(declared.Count - 1);
                        continue;
                    }
                    // Hidden housekeeping rows: AE's per-effect root param and
                    // plugin group markers carry no display name at all (the
                    // old parser surfaced them as match-name junk rows).
                    if (string.IsNullOrEmpty(p.Name)) continue;
                    if (SkipMatchNames.Contains(p.MatchName)) continue;

                    p.Group = JoinPaths(groupPath, declared);
                    into.Parameters.Add(p);
                }
            }
        }

        /// <summary>groupPath plus the open declared-group names, '\u0001'-joined.</summary>
        static string JoinPaths(string basePath, List<string> declared)
        {
            if (declared == null || declared.Count == 0) return basePath;
            string tail = string.Join("\u0001", declared);
            return string.IsNullOrEmpty(basePath) ? tail : basePath + '\u0001' + tail;
        }

        /// <summary>Display-group path of a tdgp, appended to the parent path.</summary>
        static string ChildGroupPath(RiffNode node, string parentPath)
        {
            var tdsn = node.Children.FirstOrDefault(c => !c.IsContainer && Same(c.Cid, TDSN));
            string name = tdsn != null ? DecodeString(tdsn.Content) : null;
            if (string.IsNullOrEmpty(name)) return parentPath; // anonymous wrapper: keep parent depth
            return parentPath == null ? name : parentPath + '\u0001' + name;
        }

        static PresetParameter ParseParameter(RiffNode tdbs, string matchName,
                                              Dictionary<string, ParamMeta> meta)
        {
            var p = new PresetParameter { MatchName = matchName };
            // parT metadata: control kind + AE's display-name fallback
            if (matchName != null && meta != null && meta.TryGetValue(matchName, out var m))
            {
                p.Kind = m.Kind;
                p.MenuItems = m.Menu;
                if (!string.IsNullOrEmpty(m.PardName)) p.Name = m.PardName;
            }
            foreach (var child in tdbs.Children)
            {
                if (!child.IsContainer && Same(child.Cid, TDSN))
                {
                    string name = DecodeString(child.Content);
                    if (!string.IsNullOrEmpty(name)) p.Name = name;
                }
                else if (!child.IsContainer && Same(child.Cid, RiffFile.Cid("cdat")))
                {
                    // cdat carries the value as big-endian doubles; POINT
                    // stores X/Y (48 bytes) and COLOR stores R/G/B(/A)
                    // (96 bytes) — the extras drive the type-aware rendering
                    if (child.Content.Length >= 8) p.StaticValue = ReadBEDouble(child.Content, 0);
                    if (child.Content.Length >= 16) p.StaticValue2 = ReadBEDouble(child.Content, 8);
                    if (child.Content.Length >= 24) p.StaticValue3 = ReadBEDouble(child.Content, 16);
                    if (child.Content.Length >= 32) p.StaticValue4 = ReadBEDouble(child.Content, 24);
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
            // p.Name stays null when neither tdsn nor the pard name existed —
            // WalkGroup hides those rows (AE never shows them either)
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
