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
    ///   ldat: count × dims × record — lhd3 field [3] is the property's
    ///   DIMENSION COUNT (1 on every proven 1D stream; a 2D point stores
    ///   one record PER DIMENSION per keyframe, interleaved dim0, dim1,
    ///   dim0, dim1…). Reading a 2D stream as 1D plots X and Y as if
    ///   they were consecutive keyframes — the value graph zig-zags and
    ///   the speed graph spikes and decays, shapes AE never draws. With
    ///   record size 48 each per-dimension record is:
    ///     +0  int32  time — raw preset ticks: 1/1024 of a 30 fps frame,
    ///                    i.e. 30720 per second (proven against AE's own
    ///                    drawn speed graph; see PresetCurve's TIMEBASE
    ///                    note — an earlier revision read the tick as
    ///                    1/1024 second and stretched time 30×)
    ///     +4  byte   interpolation into  this keyframe (1 linear 2 bezier 3 hold)
    ///     +5  byte   interpolation out of this keyframe
    ///     +8  double value (of this dimension)
    ///     +16 double in-slope        +24 double in-influence
    ///     +32 double out-slope       +40 double out-influence
    ///   (tangent quadruple matches RESEARCH_NOTES.md's "bezier tangent
    ///   doubles for Graph Editor easing"; the fixture carries classic
    ///   Easy-Ease values like 1/3 and 1/6. The tangent block is proven
    ///   for record size 48 ONLY — other record shapes deliberately
    ///   decode time/interp/value and interpolate linear, because guessed
    ///   offsets would read a second value double as a tangent and bend
    ///   curves into shapes AE never draws. Influences are normalized to
    ///   0..1 at decode — stored percents (33.33) divide by 100.)
    ///   Dimension 1 of a 2D stream stores the SAME proven record layout
    ///   one record later, so its tangent block is decoded too: AE's
    ///   value graph draws one curve per dimension (round-25 research).
    /// </summary>
    public class PresetKeyframe
    {
        public int Time;
        public double Value;
        /// <summary>Second dimension's value when the stream is 2D
        /// (lhd3 dimension count 2) — dimension 0 stays in Value, so a
        /// POINT parameter can honestly say "X …, Y …". NaN when 1D.</summary>
        public double Value2 = double.NaN;
        /// <summary>Property dimension count from lhd3[3] (1 = scalar).</summary>
        public int DimCount = 1;
        public double InSlope;
        public double InInfluence;
        public double OutSlope;
        public double OutInfluence;
        public int InterpIn;
        public int InterpOut;
        // --- dimension 1 (2D streams) ---------------------------------
        // The second interleaved ldat record carries dimension 1's OWN
        // tangent block (the same proven 48-byte layout, one record
        // later). AE's value graph draws ONE CURVE PER DIMENSION ("when
        // you animate Position, the value graph shows two separate
        // lines, X and Y"), so the Y curve needs the file's real easing
        // instead of a linear guess. NaN means "not decoded" (non-48-byte
        // records) - PresetCurve then reads the field as zero, which is
        // exactly how a 1D stream without tangents behaves.
        public double InSlope2 = double.NaN;
        public double InInfluence2 = double.NaN;
        public double OutSlope2 = double.NaN;
        public double OutInfluence2 = double.NaN;
        /// <summary>Dimension 1's interpolation codes (-1 = mirror the
        /// dimension-0 codes; both records of one keyframe usually
        /// agree, but a third-party writer may disagree).</summary>
        public int InterpIn2 = -1;
        public int InterpOut2 = -1;

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
        public const int BoundedSlider = 10;  // AE's own bounded slider — the
                                              // kind under Exposure/Offset/Gamma,
                                              // the audio effects' rows (Reverb,
                                              // High-Low Pass), Deep Glow's sliders,
                                              // CSpice, MB Strength; always with
                                              // tdum/tduM bounds in the pack files
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
        /// <summary>The pard's param-flags word (big-endian uint32 at +4).
        /// Bits: the 0x200 hidden bit empirically marks rows AE never
        /// renders — BCC "placeholder" rows and vendor arb blobs.</summary>
        public uint ParamFlags;
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
        /// tdsn), the popup menu labels, and the pard's param-flags word —
        /// whose empirically-proven hidden bit is 0x200: the fixture's BCC
        /// "placeholder" rows carry 0x220 and Sapphire's opaque "mocha"
        /// blob 0x208, while every visibly rendered parameter — across all
        /// three vendors in the fixture — leaves bit 0x200 clear (visible
        /// rows carry 0x8/0x20/0x2 freely, so ONLY 0x200 may hide a row).
        /// BCC's own "Hidden" slider carries 0x8 like visible sliders,
        /// so it is recognized by name instead (see IsHiddenParam).</summary>
        private class ParamMeta
        {
            public int Kind = PresetParamKind.Unknown;
            public string PardName;
            public string[] Menu;
            public uint Flags;
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

            // A property/animator preset (AE's "Apply Animation Preset" onto
            // a PROPERTY selection — text animators, single-property value
            // snaps like a S_Shake Amplitude/Frequency panning curve) carries
            // target-path tdsp entries and their tdgp/tdbs data blocks but NO
            // sspc snapshot at all. Proven structure of such files (a pack of
            // 27 real-world presets, 6 of this class): besc children are
            // [beso, (LIST tdsp path, tdsn display-name) × N, LIST tdsp
            // sentinel-only, tdgp|tdbs data × N]. Each real tdsp is ONE
            // property group, so they inspect as one entry per group and the
            // effect list's N rows keep pairing by position.
            if (sspcs.Count == 0)
            {
                var groupNames = PropertyGroupNames(besc, realTdsps.Count);
                var dataBlocks = PropertyDataBlocks(besc);
                if (realTdsps.Count != dataBlocks.Count)
                    errors?.Add($"{realTdsps.Count} property-path entries but {dataBlocks.Count} data blocks — " +
                                "unexpected file structure; unpaired entries were skipped");
                var propResult = new List<PresetEffectDetails>(realTdsps.Count);
                int pn = Math.Min(realTdsps.Count, dataBlocks.Count);
                for (int i = 0; i < pn; i++)
                {
                    string matchName = TdmnEffectName(realTdsps[i]);
                    var details = new PresetEffectDetails { MatchName = matchName };
                    try
                    {
                        if (i < groupNames.Count && !string.IsNullOrEmpty(groupNames[i]))
                            details.ShortName = groupNames[i];
                        // no parT exists in these files — kinds stay Unknown,
                        // names come from each tdbs' own tdsn, groups from the
                        // nested tdgp structure itself
                        var emptyMeta = new Dictionary<string, ParamMeta>();
                        var seen = new Dictionary<string, int>();
                        var blk = dataBlocks[i];
                        if (Same(blk.Form, RiffFile.Cid("tdgp")))
                        {
                            WalkGroup(blk, details, null, seen, emptyMeta);
                        }
                        else if (Same(blk.Form, RiffFile.Cid("tdbs")))
                        {
                            var p = ParseParameter(blk, TdmnPathDeepest(realTdsps[i]), emptyMeta);
                            if (!string.IsNullOrEmpty(p.Name) &&
                                !SkipMatchNames.Contains(p.MatchName) &&
                                p.Kind != PresetParamKind.ArbitraryData &&
                                !IsHiddenParam(p))
                                details.Parameters.Add(p);
                        }
                    }
                    catch (Exception ex)
                    {
                        // one malformed group must never sink the rest — keep
                        // the slot (indexes stay aligned) and say what broke
                        details.Error = ex.GetType().Name + ": " + ex.Message;
                        errors?.Add($"property group #{i + 1} ({matchName ?? "?"}) couldn't be decoded — {details.Error}");
                    }
                    propResult.Add(details);
                }
                return propResult;
            }

            if (realTdsps.Count != sspcs.Count)
                errors?.Add($"{realTdsps.Count} effect index entries but {sspcs.Count} parameter blocks — " +
                            "unexpected file structure; unpaired entries were skipped");

            // AE writes the parT descriptor tree only on the FIRST sspc of an
            // effect that appears more than once in a preset (the pack's
            // multi-effect files: S_Sharpen ×2, MB LookSuite3 ×4, BCC Unsharp
            // Mask ×2, Wave Warp ×2, Drop Shadow ×2...). Every later copy
            // carries no parT — each of its rows degraded to Unknown kinds,
            // which un-grouped BCC's whole parameter tree (89 flat rows where
            // the first copy shows 61 grouped ones), leaked AE-hidden rows and
            // stripped popup menus. Cache by match name; an empty map (no
            // parT, or one with no usable entries) falls back to the first
            // copy's map. A later copy WITH its own parT keeps it.
            var metaCache = new Dictionary<string, Dictionary<string, ParamMeta>>(StringComparer.Ordinal);

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
                    if (meta.Count == 0 && matchName != null &&
                        metaCache.TryGetValue(matchName, out var cachedMeta))
                        meta = cachedMeta;
                    else if (meta.Count > 0 && matchName != null)
                        metaCache[matchName] = meta;

                    // Group-path disambiguation is PER EFFECT: the UI files rows
                    // by path within one effect, so same-named groups in two
                    // ROOT tdgp blocks of the same effect (legal, if rare) must
                    // not collide either — one counter covers every root walk.
                    var seen = new Dictionary<string, int>();

                    foreach (var child in sspcs[i].Children)
                        if (child.IsContainer && Same(child.Form, RiffFile.Cid("tdgp")))
                        {
                            // the effect's ROOT tdgp: its own tdsn is the effect
                            // display name, not a parameter group
                            WalkGroup(child, details, null, seen, meta);
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
                        if (p.Length >= 8) meta.Flags = ReadBEUInt32(p, 4);
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
        /// never surfaced as parameters. <paramref name="seen"/> is the
        /// PER-EFFECT repeat counter shared by every root walk (the UI files
        /// rows per effect, so paths must stay unique across all of them).
        /// </summary>
        static void WalkGroup(RiffNode group, PresetEffectDetails into, string groupPath,
                              Dictionary<string, int> seen, Dictionary<string, ParamMeta> meta)
        {
            string pendingMatch = null;
            var declared = new List<string>(); // open GROUP_START display names
            // Group display names repeat freely in real plugins (two tdgp
            // siblings both named "Compositing Options", a GROUP_START
            // reopened after it closed). The UI files rows by path, so two
            // identical paths would merge two DIFFERENT groups into one
            // node — rows show under the wrong (already-collapsed) header
            // and the second header vanishes entirely. A per-effect counter
            // appends an invisible \u0002<k> to the PATH of every repeat;
            // display names strip it back off, and each group instance —
            // with its own disclosure state — stays separate.
            foreach (var child in group.Children)
            {
                if (!child.IsContainer && Same(child.Cid, TDMN))
                {
                    pendingMatch = DecodeString(child.Content);
                }
                else if (child.IsContainer && Same(child.Form, RiffFile.Cid("tdgp")))
                {
                    // A tdmn directly BEFORE the tdgp names the GROUP — AE's
                    // own writer marks "Compositing Options" exactly that way
                    // (the fixture pairs 'ADBE Effect Built In Params' with
                    // the group six times). Consume it HERE: it becomes the
                    // group's name candidate and is cleared, so it can never
                    // pair with a later tdbs. The leaked match name used to
                    // mis-source the next parameter's parT kind/flags/menu
                    // whenever a writer followed the group with a parameter
                    // that carried no tdmn of its own — a visible row could
                    // inherit the group's 0x200 hidden bit and vanish, a
                    // GROUP marker misfire, a popup lose its menu — and an
                    // unnamed group hoisted every parameter inside it one
                    // level up, which reads as "sometimes the grouping just
                    // isn't recognized".
                    string groupMatch = pendingMatch;
                    pendingMatch = null;
                    // A nested tdgp sits INSIDE whatever is open right now —
                    // the declared GROUP_START stack is part of its base path
                    // exactly as it is for plain parameters. (The old code
                    // appended the open groups UNDER the tdgp's own name,
                    // inverting the tree for effects that mix both group
                    // styles: the tdgp hoisted out of its group and its
                    // parameters filed under the wrong header — "sometimes
                    // parameters don't belong to their group".) The tdgp's
                    // own tdsn names it (with the group's tdmn and the parT
                    // display names as fallbacks); anonymous wrappers (no
                    // name anywhere) keep the parent path — they ARE the
                    // parent visually.
                    string baseWithDeclared = JoinPaths(groupPath, declared);
                    string nested = ChildGroupPath(child, baseWithDeclared, meta, groupMatch);
                    if (nested != baseWithDeclared) nested = Disambiguate(nested, seen);
                    WalkGroup(child, into, nested, seen, meta);
                }
                else if (child.IsContainer && Same(child.Form, RiffFile.Cid("tdbs")))
                {
                    var p = ParseParameter(child, pendingMatch, meta);
                    pendingMatch = null;
                    if (p.Kind == PresetParamKind.GroupStart)
                    {
                        string dn = string.IsNullOrEmpty(p.Name) ? p.MatchName : p.Name;
                        // the candidate path this group will produce; the
                        // suffix (if any) always lands on the LAST segment,
                        // which is this name
                        declared.Add(dn);
                        string cand = JoinPaths(groupPath, declared);
                        string uniq = Disambiguate(cand, seen);
                        declared[declared.Count - 1] = dn +
                            (uniq.Length > cand.Length ? uniq.Substring(cand.Length) : "");
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
                    // AE-hidden rows the plugin itself flags or names: the
                    // pard's hidden bit 0x200 (BCC "placeholder" ×3,
                    // Sapphire's mocha blob), ARB_DATA shapes AE renders no
                    // UI for (Sapphire "mocha", BCC "Mocha Data0"), and
                    // BCC's internal "placeholder"/"Hidden" padding rows —
                    // "Hidden"'s own flag word (0x8) matches visible
                    // sliders, so only the name identifies it.
                    if (p.Kind == PresetParamKind.ArbitraryData) continue;
                    if (IsHiddenParam(p)) continue;

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

        /// <summary>
        /// Second and later uses of the same group path get an invisible
        /// \u0002&lt;k&gt; suffix (k = 2, 3, …) so every group INSTANCE has
        /// its own path — the UI files, collapses and expands them
        /// separately, and display names strip the suffix back off.
        /// </summary>
        static string Disambiguate(string path, Dictionary<string, int> seen)
        {
            if (!seen.TryGetValue(path, out int n))
            {
                seen[path] = 1;
                return path;
            }
            n++;
            seen[path] = n;
            return path + "\u0002" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// True when the plugin itself marks this parameter invisible:
        /// the pard's hidden bits — 0x200 (BCC "placeholder" ×3,
        /// Sapphire's mocha blob) and 0x8, the writer's "not in AE's
        /// Effect Controls" bit — or BCC's internal padding names.
        /// 0x8 is proven by BCC Directional Blur's superseded legacy
        /// PixelChooser block: 23 rows (Legacy PixelChooser, Apply
        /// PixelChooser, PC Intensity, Mask, Shape, Point 1/2 and the
        /// matte controls From…Invert Matte) ALL carry 0x8, and AE's own
        /// Effect Controls draws NONE of them — while every row AE does
        /// draw carries 0x0 (or 0x20 inside groups, which is not a
        /// visibility bit). Sapphire's 'mocha' (0x208) and 'Hidden'
        /// (0x8) fall under the same rule (round 23 hid them by name or
        /// by 0x200 and credited the wrong bit). Adobe's own effects
        /// never set 0x8, so the rule is free there.
        /// </summary>
        static bool IsHiddenParam(PresetParameter p)
        {
            if ((p.ParamFlags & 0x200) != 0) return true;
            if ((p.ParamFlags & 0x8) != 0) return true;
            return p.Name == "placeholder" || p.Name == "Hidden";
        }

        /// <summary>
        /// Display-group path of a tdgp, appended to the parent path. The
        /// name resolves in AE's own order: the tdsn inside the group,
        /// then the tdmn AE writes directly BEFORE it (the group's match
        /// name — how "Compositing Options" is named), then a tdmn inside
        /// the group through its parT descriptor (round 23's fallback).
        /// </summary>
        static string ChildGroupPath(RiffNode node, string parentPath,
                                     Dictionary<string, ParamMeta> meta,
                                     string outerMatch = null)
        {
            var tdsn = node.Children.FirstOrDefault(c => !c.IsContainer && Same(c.Cid, TDSN));
            string name = tdsn != null ? DecodeString(tdsn.Content) : null;
            if (string.IsNullOrEmpty(name))
            {
                // the group's own match name, declared in the tdmn right
                // before the tdgp (consumed by the caller) — without it an
                // unnamed tdgp would silently hoist every parameter inside
                // it one level up ("sometimes the grouping just isn't
                // recognized")
                name = PardNameOf(outerMatch, meta);
            }
            if (string.IsNullOrEmpty(name))
            {
                // Some writers name a nested tdgp only through its match
                // name — the tdmn inside the group resolving to the parT
                // descriptor's display name. Fall back to that before
                // giving up.
                foreach (var tdmn in node.Children.Where(c => !c.IsContainer && Same(c.Cid, TDMN)))
                {
                    string mn = DecodeString(tdmn.Content);
                    string viaMeta = PardNameOf(mn, meta);
                    if (viaMeta != null) { name = viaMeta; break; }
                }
            }
            if (string.IsNullOrEmpty(name)) return parentPath; // anonymous wrapper: keep parent depth
            return parentPath == null ? name : parentPath + '\u0001' + name;
        }

        /// <summary>The parT display name of a match name, or null when the
        /// descriptor is missing or nameless — the group-naming fuel.</summary>
        static string PardNameOf(string matchName, Dictionary<string, ParamMeta> meta)
        {
            if (string.IsNullOrEmpty(matchName) || meta == null) return null;
            return meta.TryGetValue(matchName, out var m) && !string.IsNullOrEmpty(m.PardName)
                ? m.PardName
                : null;
        }

        static PresetParameter ParseParameter(RiffNode tdbs, string matchName,
                                              Dictionary<string, ParamMeta> meta)
        {
            var p = new PresetParameter { MatchName = matchName };
            // parT metadata: control kind + AE's display-name fallback
            if (matchName != null && meta != null && meta.TryGetValue(matchName, out var m))
            {
                p.Kind = m.Kind;
                p.ParamFlags = m.Flags;
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

            // Multidimensional properties (a POINT's X/Y, a COLOR's R/G/B)
            // interleave one record PER DIMENSION per keyframe; lhd3[3]
            // states the dimension count (0 on files/tests that predate the
            // discovery — read as 1). The declaration must survive two
            // arithmetic checks before it is trusted: the record count must
            // tile the ldat exactly, and every dimension of one keyframe
            // must share its time. Anything else falls back to the 1D read
            // (and its exact-length guard) rather than guessing.
            int dims = (int)ReadBEUInt32(lhd3.Content, 12);
            if (dims < 1 || dims > 8) dims = 1;
            if ((long)count * dims * recSize != ldat.Content.Length) dims = 1;
            if (dims > 1) ValidateDimensionTimes(ldat.Content, count, dims, recSize, ref dims);
            if (ldat.Content.Length != count * dims * recSize) return;

            var bytes = ldat.Content;
            for (int r = 0; r < count; r++)
            {
                int o = r * dims * recSize;
                // third-party streams can carry garbage doubles; one
                // non-finite value must never poison the row text, the
                // stream summary or the graph geometry — the record is
                // skipped, non-finite tangents clamp to zero below
                double value = ReadBEDouble(bytes, o + 8);
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;

                var kf = new PresetKeyframe
                {
                    Time = (int)ReadBEUInt32(bytes, o),
                    InterpIn = o + 4 < bytes.Length ? bytes[o + 4] : 0,
                    InterpOut = o + 5 < bytes.Length ? bytes[o + 5] : 0,
                    Value = value,
                };
                kf.DimCount = dims;
                if (dims > 1 && recSize >= 16 && o + recSize + recSize <= bytes.Length)
                {
                    // second dimension's value (Y of a POINT) - and, on
                    // the proven 48-byte record, its OWN tangent block one
                    // record later. AE's value graph draws ONE CURVE PER
                    // DIMENSION ("when you animate Position, the value
                    // graph shows two separate lines, X and Y"), so the Y
                    // curve carries the file's real easing instead of a
                    // guessed straight line; the row and the graph tooltip
                    // still name the stream 2D.
                    int o2 = o + recSize;
                    double v2 = ReadBEDouble(bytes, o2 + 8);
                    if (!double.IsNaN(v2) && !double.IsInfinity(v2)) kf.Value2 = v2;
                    if (HasProvenTangentBlock(bytes, o2, recSize))
                    {
                        kf.InSlope2 = FiniteOrZero(ReadBEDouble(bytes, o2 + 16));
                        kf.InInfluence2 = Saturate(Influence(FiniteOrZero(ReadBEDouble(bytes, o2 + 24))));
                        kf.OutSlope2 = FiniteOrZero(ReadBEDouble(bytes, o2 + 32));
                        kf.OutInfluence2 = Saturate(Influence(FiniteOrZero(ReadBEDouble(bytes, o2 + 40))));
                        kf.InterpIn2 = bytes[o2 + 4];
                        kf.InterpOut2 = bytes[o2 + 5];
                    }
                }
                if (HasProvenTangentBlock(bytes, o, recSize))
                {
                    // tangent fields ride the proven 48-byte layout at the
                    // record's START (time, interp, value, in/out slope and
                    // influence). Padded records join them now — a writer
                    // that appends zero padding after byte 48 keeps every
                    // proven offset, and the all-zero tail PROVES the layout
                    // instead of guessing it. Before round 26 any non-48
                    // record silently drew clean straight lines where AE
                    // shows eased curves — the curve-math shapes that never
                    // matched. A record whose tail is NOT zero (an unknown
                    // layout whose +16 could be a second value double) still
                    // takes the honest linear read. Influences saturate to
                    // AE's 0..100% right here, so every readout stays honest
                    // even for wild third-party numbers.
                    kf.InSlope = FiniteOrZero(ReadBEDouble(bytes, o + 16));
                    kf.InInfluence = Saturate(Influence(FiniteOrZero(ReadBEDouble(bytes, o + 24))));
                    kf.OutSlope = FiniteOrZero(ReadBEDouble(bytes, o + 32));
                    kf.OutInfluence = Saturate(Influence(FiniteOrZero(ReadBEDouble(bytes, o + 40))));
                }
                into.Keyframes.Add(kf);
            }
            into.IsAnimated = into.Keyframes.Count > 0;
            // AE lists keyframes in time order; a third-party writer that
            // doesn't would draw backward segments and scribble the graph
            // (the "graphs sometimes show wrong on some effects" report).
            // Restore time order only when actually unsorted — a no-op for
            // honest files, and equal-time pairs keep their written order,
            // which instant steps (value 0 and 100 on the same frame) and
            // the row numbering depend on.
            for (int i = 1; i < into.Keyframes.Count; i++)
                if (into.Keyframes[i].Time < into.Keyframes[i - 1].Time)
                {
                    into.Keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));
                    break;
                }
            ClampTangents(into.Keyframes);
        }

        /// <summary>
        /// Every dimension of one keyframe must carry the same time — the
        /// one structural fingerprint that separates interleaved dimension
        /// records from a genuine flat 1D run. On the first mismatch the
        /// dims guess is revoked (the caller re-reads as 1D).
        /// </summary>
        static void ValidateDimensionTimes(byte[] bytes, int count, int dims, int recSize, ref int dimsInOut)
        {
            for (int r = 0; r < count; r++)
            {
                int baseOff = r * dims * recSize;
                int t0 = (int)ReadBEUInt32(bytes, baseOff);
                for (int d = 1; d < dims; d++)
                    if ((int)ReadBEUInt32(bytes, baseOff + d * recSize) != t0)
                    {
                        dimsInOut = 1;
                        return;
                    }
            }
        }

        /// <summary>Influence clamped to AE's 0..100% window.</summary>
        static double Saturate(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>
        /// AE-shaped tangent guard for real-world streams. The 48-byte
        /// tangent layout is proven, but a third-party writer can still
        /// store numbers that are not speeds (a value double reinterpreted,
        /// a raw byte offset) — one absurd handle then bends the whole
        /// curve into an arc AE never draws and blows the graph's scale
        /// into 1e+ readouts. The guard clamps GEOMETRY, not the raw
        /// slope: a handle may pull the curve at most a few multiples of
        /// the stream's own value travel away from its keyframe. Real
        /// easing passes untouched — an S-curve keeps near-zero handles
        /// at its keys, and even snappy high-influence eases, overshoots
        /// and bounces stay inside the envelope — while garbage lands
        /// orders of magnitude beyond it. (The previous slope-rate
        /// envelope distorted honest curves instead: a 90%-influence ease
        /// legitimately peaks near 10× its chord rate, far above a
        /// 4×-chord slope cap, and the clamp visibly flattened it.)
        /// </summary>
        static void ClampTangents(List<PresetKeyframe> kfs)
        {
            if (kfs == null || kfs.Count < 2) return;
            double vMin = kfs[0].Value, vMax = kfs[0].Value, maxAbs = 0;
            foreach (var k in kfs)
            {
                if (k.Value < vMin) vMin = k.Value;
                if (k.Value > vMax) vMax = k.Value;
                double a = Math.Abs(k.Value);
                if (a > maxAbs) maxAbs = a;
            }
            double range = vMax - vMin, maxSegDv = 0;
            for (int i = 1; i < kfs.Count; i++)
            {
                double dv = Math.Abs(kfs[i].Value - kfs[i - 1].Value);
                if (dv > maxSegDv) maxSegDv = dv;
            }
            // the excursion envelope: 3× the stream's own travel, with a
            // floor so flat streams still bound garbage (2% of the value
            // scale, or a tiny constant near zero)
            double envelope = Math.Max(Math.Max(3.0 * range, 3.0 * maxSegDv),
                                       Math.Max(0.02 * maxAbs, 1e-6));
            for (int i = 0; i < kfs.Count; i++)
            {
                var k = kfs[i];
                if (i + 1 < kfs.Count)
                    ClampHandle(k, true, (kfs[i + 1].Time - k.Time) / PresetCurve.TicksPerSecond, envelope);
                if (i > 0)
                    ClampHandle(k, false, (k.Time - kfs[i - 1].Time) / PresetCurve.TicksPerSecond, envelope);
            }

            // 2D streams: dimension 1's tangent block gets the same
            // geometry envelope over its own value travel - one garbage Y
            // slope must not bend the Y curve any more than an X one.
            if (kfs[0].DimCount > 1)
            {
                double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity, yMaxAbs = 0;
                bool anyY = false;
                foreach (var k in kfs)
                {
                    if (double.IsNaN(k.Value2) || double.IsInfinity(k.Value2)) continue;
                    anyY = true;
                    if (k.Value2 < yMin) yMin = k.Value2;
                    if (k.Value2 > yMax) yMax = k.Value2;
                    double a = Math.Abs(k.Value2);
                    if (a > yMaxAbs) yMaxAbs = a;
                }
                if (anyY)
                {
                    double yRange = yMax - yMin, yMaxSegDv = 0;
                    for (int i = 1; i < kfs.Count; i++)
                    {
                        if (double.IsNaN(kfs[i].Value2) || double.IsInfinity(kfs[i].Value2) ||
                            double.IsNaN(kfs[i - 1].Value2) || double.IsInfinity(kfs[i - 1].Value2)) continue;
                        double dv = Math.Abs(kfs[i].Value2 - kfs[i - 1].Value2);
                        if (dv > yMaxSegDv) yMaxSegDv = dv;
                    }
                    double yEnvelope = Math.Max(Math.Max(3.0 * yRange, 3.0 * yMaxSegDv),
                                                Math.Max(0.02 * yMaxAbs, 1e-6));
                    for (int i = 0; i < kfs.Count; i++)
                    {
                        var k = kfs[i];
                        if (i + 1 < kfs.Count)
                            ClampHandle2(k, true, (kfs[i + 1].Time - k.Time) / PresetCurve.TicksPerSecond, yEnvelope);
                        if (i > 0)
                            ClampHandle2(k, false, (k.Time - kfs[i - 1].Time) / PresetCurve.TicksPerSecond, yEnvelope);
                    }
                }
            }
        }

        /// <summary>Clamps ONE handle's value excursion (slope × influence ×
        /// span) to the envelope by rescaling the slope; sign and influence
        /// survive. A zero influence pulls nothing no matter the stored
        /// slope, so it is never touched.</summary>
        static void ClampHandle(PresetKeyframe k, bool outgoing, double dt, double envelope)
        {
            if (dt <= 1e-9) return;
            double infl = outgoing ? k.OutInfluence : k.InInfluence;
            if (infl <= 1e-9) return;
            double slope = outgoing ? k.OutSlope : k.InSlope;
            double excursion = slope * infl * dt;
            if (Math.Abs(excursion) <= envelope) return;
            double cappedSlope = envelope * (excursion < 0 ? -1 : 1) / (infl * dt);
            if (outgoing) k.OutSlope = cappedSlope; else k.InSlope = cappedSlope;
        }

        /// <summary>ClampHandle for dimension 1's tangent fields - NaN
        /// fields (undecoded blocks) are never touched.</summary>
        static void ClampHandle2(PresetKeyframe k, bool outgoing, double dt, double envelope)
        {
            if (dt <= 1e-9) return;
            double infl = outgoing ? k.OutInfluence2 : k.InInfluence2;
            if (double.IsNaN(infl) || infl <= 1e-9) return;
            double slope = outgoing ? k.OutSlope2 : k.InSlope2;
            if (double.IsNaN(slope)) return;
            double excursion = slope * infl * dt;
            if (Math.Abs(excursion) <= envelope) return;
            double capped = envelope * (excursion < 0 ? -1 : 1) / (infl * dt);
            if (outgoing) k.OutSlope2 = capped; else k.InSlope2 = capped;
        }

        /// <summary>Garbage tangent doubles read as 0 (no handle pull) —
        /// they must never reach the curve math as NaN.</summary>
        static double FiniteOrZero(double v) =>
            double.IsNaN(v) || double.IsInfinity(v) ? 0 : v;

        /// <summary>
        /// True when the record at <paramref name="o"/> provably carries the
        /// proven 48-byte tangent layout: the record is at least 48 bytes
        /// and every byte beyond them is zero padding. A non-zero tail means
        /// an unknown layout whose +16/+24/+32/+40 offsets could be a second
        /// value double — such records keep the honest linear read.
        /// </summary>
        static bool HasProvenTangentBlock(byte[] bytes, int o, int recSize)
        {
            if (recSize < 48 || o + 48 > bytes.Length) return false;
            for (int i = o + 48; i < o + recSize; i++)
                if (bytes[i] != 0) return false;
            return true;
        }

        /// <summary>
        /// Influence unit normalization, applied once at decode: AE stores
        /// a fraction (1/3 = 0.333…), but some writers store the UI's
        /// percent (33.33 = one third). A value above 1.0 can only be that
        /// percent, so it divides by 100 — every consumer downstream
        /// (curve math, Easy Ease recognition, tooltips) then sees one
        /// consistent 0..1 unit.
        /// </summary>
        static double Influence(double v) => v > 1.0 ? v / 100.0 : v;

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
        /// The LAST tdmn of a tdsp path — the deepest property the preset
        /// targets ('S_Shake-0050' in Effect Parade → S_Shake → S_Shake-0050).
        /// Only meaningful for property presets, where it becomes the single
        /// tdbs parameter's match name.
        /// </summary>
        static string TdmnPathDeepest(RiffNode tdspNode)
        {
            var tdmns = RiffFile.FindAll(tdspNode, TDMN);
            return tdmns.Count == 0 ? null : DecodeString(tdmns[tdmns.Count - 1].Content);
        }

        /// <summary>
        /// Display name of each property group of a property preset: the
        /// tdsn leaf that immediately follows the group's tdsp path at besc
        /// level ('Amplitude', 'Path Options', 'Animator 1' — AE shows these
        /// as the group titles after the preset lands). Collected in path
        /// order, one per real tdsp.
        /// </summary>
        static List<string> PropertyGroupNames(RiffNode besc, int expected)
        {
            var names = new List<string>(expected);
            bool wantName = false;
            foreach (var c in besc.Children)
            {
                if (c.IsContainer && Same(c.Form, TDSP_FORM))
                {
                    wantName = TdmnEffectName(c) != null; // the sentinel closes the index section
                    continue;
                }
                if (wantName && !c.IsContainer && Same(c.Cid, TDSN))
                {
                    names.Add(DecodeString(c.Content));
                    wantName = false;
                }
                else
                {
                    wantName = false; // the name must follow its path directly
                }
            }
            return names;
        }

        /// <summary>
        /// The data blocks of a property preset, in path order: every tdgp /
        /// tdbs container after the sentinel tdsp closes the index section
        /// ('Amplitude' preset = one bare tdbs; a text animator = one tdgp
        /// tree per property group).
        /// </summary>
        static List<RiffNode> PropertyDataBlocks(RiffNode besc)
        {
            var blocks = new List<RiffNode>();
            bool afterSentinel = false;
            foreach (var c in besc.Children)
            {
                if (c.IsContainer && Same(c.Form, TDSP_FORM))
                {
                    if (TdmnEffectName(c) == null) afterSentinel = true;
                    continue;
                }
                if (afterSentinel && c.IsContainer &&
                    (Same(c.Form, RiffFile.Cid("tdgp")) || Same(c.Form, RiffFile.Cid("tdbs"))))
                    blocks.Add(c);
            }
            return blocks;
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
