using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using FfxTool.Core;

namespace FfxTool.Core.Tests
{
    /// <summary>Port of tests/test_pipeline.py.</summary>
    public class PipelineTests
    {
        static string FixturesDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures");

        // --- synthetic file builder, mirrors _minimal_synthetic_file() in test_pipeline.py ---

        static byte[] UInt32BE(uint value) => new byte[]
        {
            (byte)((value >> 24) & 0xFF), (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF)
        };

        static byte[] Concat(params byte[][] parts)
        {
            int total = parts.Sum(p => p.Length);
            var result = new byte[total];
            int offset = 0;
            foreach (var p in parts) { Array.Copy(p, 0, result, offset, p.Length); offset += p.Length; }
            return result;
        }

        static byte[] Ascii(string s) => System.Text.Encoding.ASCII.GetBytes(s);

        static byte[] Utf8Prefixed(string s)
        {
            var b = Ascii(s);
            return Concat(Ascii("Utf8"), UInt32BE((uint)b.Length), b);
        }

        static byte[] MakeLeaf(string cid, byte[] content)
        {
            var chunk = Concat(Ascii(cid), UInt32BE((uint)content.Length), content);
            if (content.Length % 2 == 1) chunk = Concat(chunk, new byte[] { 0 });
            return chunk;
        }

        static byte[] MakeList(string form, byte[] children)
        {
            var body = Concat(Ascii(form), children);
            return Concat(Ascii("LIST"), UInt32BE((uint)body.Length), body);
        }

        static byte[] PadTo(string s, int len)
        {
            var b = Ascii(s);
            var result = new byte[len];
            Array.Copy(b, result, Math.Min(b.Length, len));
            return result;
        }

        static byte[] MakeTdsp(string matchName, uint index)
        {
            var tdmn0 = MakeLeaf("tdmn", PadTo("ADBE Effect Parade", 40));
            var tdmn1 = MakeLeaf("tdmn", PadTo(matchName, 40));
            var tdix0 = MakeLeaf("tdix", UInt32BE(0xFFFFFFFF));
            var tdix1 = MakeLeaf("tdix", UInt32BE(index));
            var tdsiA = MakeList("tdsi", Concat(tdmn0, tdix0));
            var tdsiB = MakeList("tdsi", Concat(tdmn1, tdix1));
            return MakeList("tdsp", Concat(tdsiA, tdsiB));
        }

        static byte[] SentinelTdsp()
        {
            var tdmnSentinel = MakeLeaf("tdmn", PadTo("ADBE End of path sentinel", 40));
            var tdixSentinel = MakeLeaf("tdix", UInt32BE(0xFFFFFFFF));
            return MakeList("tdsp", MakeList("tdsi", Concat(tdmnSentinel, tdixSentinel)));
        }

        static byte[] DoubleBE(double v)
        {
            var bytes = BitConverter.GetBytes(v); // little-endian on x86/x64
            Array.Reverse(bytes);
            return bytes;
        }

        /// <summary>One root tdgp holding a single named parameter with a
        /// static cdat value — enough for WalkGroup to surface one row.
        /// The tdmn sits BEFORE its tdbs as a sibling (that pending-match
        /// rule is how WalkGroup names a parameter), and the tdbs itself
        /// carries tdsn + cdat.</summary>
        static byte[] MakeParamTdgp(string paramName, double value)
        {
            var tdmn = MakeLeaf("tdmn", PadTo(paramName, 40));
            var tdsn = MakeLeaf("tdsn", Utf8Prefixed(paramName));
            var cdat = MakeLeaf("cdat", DoubleBE(value));
            var tdbs = MakeList("tdbs", Concat(tdsn, cdat));
            return MakeList("tdgp", Concat(tdmn, tdbs));
        }

        static byte[] MinimalSyntheticFile(string[] matchNames) => MinimalSyntheticFile(matchNames, sentinelLast: true);

        static byte[] MinimalSyntheticFile(string[] matchNames, bool sentinelLast)
        {
            var head = MakeLeaf("head", Concat(UInt32BE(3), UInt32BE(93), UInt32BE(0), UInt32BE(0x01000000)));
            var beso = MakeLeaf("beso", new byte[56]);

            var bescChildren = beso;
            if (!sentinelLast)
            {
                // AE writes the sentinel index entry FIRST in some saves
                bescChildren = Concat(bescChildren, SentinelTdsp());
            }

            for (uint i = 0; i < matchNames.Length; i++)
            {
                bescChildren = Concat(bescChildren, MakeTdsp(matchNames[i], i));
                bescChildren = Concat(bescChildren, MakeLeaf("tdsn", Utf8Prefixed(matchNames[i] + " display")));
            }

            if (sentinelLast)
                bescChildren = Concat(bescChildren, SentinelTdsp());

            foreach (var name in matchNames)
            {
                var fnam = MakeLeaf("fnam", Utf8Prefixed(name));
                bescChildren = Concat(bescChildren, MakeList("sspc", fnam));
            }

            var besc = MakeList("besc", bescChildren);
            var body = Concat(Ascii("FaFX"), head, besc);
            return Concat(Ascii("RIFX"), UInt32BE((uint)body.Length), body);
        }

        /// <summary>
        /// Two real effects (sentinel index entry FIRST — the layout that
        /// exposed the pairing bug), each sspc carrying one parameter whose
        /// static value is unique per effect, so a shifted pairing is
        /// detectable by value, not just by count.
        /// </summary>
        static byte[] TwoEffectFileSentinelFirst(double v1, double v2)
        {
            var head = MakeLeaf("head", Concat(UInt32BE(3), UInt32BE(93), UInt32BE(0), UInt32BE(0x01000000)));
            var beso = MakeLeaf("beso", new byte[56]);

            var tdsp1 = MakeTdsp("S_Sharpen", 0);
            var tdsn1 = MakeLeaf("tdsn", Utf8Prefixed("S_Sharpen display"));
            var tdsp2 = MakeTdsp("ADBE Exposure2", 1);
            var tdsn2 = MakeLeaf("tdsn", Utf8Prefixed("ADBE Exposure2 display"));

            var sspc1 = MakeList("sspc", Concat(
                MakeLeaf("fnam", Utf8Prefixed("S_Sharpen")),
                MakeParamTdgp("Sharpen Amount", v1)));
            var sspc2 = MakeList("sspc", Concat(
                MakeLeaf("fnam", Utf8Prefixed("ADBE Exposure2")),
                MakeParamTdgp("Exposure", v2)));

            var besc = MakeList("besc", Concat(beso, SentinelTdsp(), tdsp1, tdsn1, tdsp2, tdsn2, sspc1, sspc2));
            var body = Concat(Ascii("FaFX"), head, besc);
            return Concat(Ascii("RIFX"), UInt32BE((uint)body.Length), body);
        }

        // --- tests ---

        [Fact]
        public void PatchVersion_UnknownTarget_Throws()
        {
            var data = MinimalSyntheticFile(new[] { "S_Sharpen" });
            var tree = RiffFile.ParseFile(data);
            Assert.Throws<ArgumentException>(() => Pipeline.PatchVersion(tree, "totally-made-up-version"));
        }

        [Fact]
        public void PatchVersion_Cs55_SetsCorrectByte()
        {
            var data = MinimalSyntheticFile(new[] { "S_Sharpen" });
            var tree = RiffFile.ParseFile(data);
            Pipeline.PatchVersion(tree, "cs5.5");
            var head = tree.Children[0];
            uint version = (uint)((head.Content[4] << 24) | (head.Content[5] << 16) | (head.Content[6] << 8) | head.Content[7]);
            Assert.Equal((uint)78, version);
        }

        [Fact]
        public void StringConversion_RemovesUtf8Prefix()
        {
            var data = MinimalSyntheticFile(new[] { "S_Sharpen" });
            var tree = RiffFile.ParseFile(data);
            Pipeline.ConvertStringsToTargetFormat(tree);

            var tdsns = RiffFile.FindAll(tree, Ascii("tdsn"));
            Assert.Single(tdsns);
            Assert.Equal("S_Sharpen display\0", System.Text.Encoding.ASCII.GetString(tdsns[0].Content));

            var fnams = RiffFile.FindAll(tree, Ascii("fnam"));
            Assert.Single(fnams);
            Assert.Equal(Pipeline.FnamFixedSize, fnams[0].Content.Length);
            Assert.StartsWith("S_Sharpen\0", System.Text.Encoding.ASCII.GetString(fnams[0].Content));
        }

        [Fact]
        public void RemoveEffectsAndRenumber_WorksCorrectly()
        {
            var data = MinimalSyntheticFile(new[] { "MB LookSuite3", "S_Sharpen", "ADBE Exposure2" });
            var tree = RiffFile.ParseFile(data);

            var removed = Pipeline.RemoveEffectsByMatchName(tree, new HashSet<string> { "MB LookSuite3" });
            Assert.Single(removed);
            Assert.Equal("MB LookSuite3", removed[0]);

            var count = Pipeline.RenumberIndices(tree);
            Assert.Equal(2, count);
        }

        [Fact]
        public void Verify_FlagsLingeringUtf8()
        {
            var data = MinimalSyntheticFile(new[] { "S_Sharpen" });
            // a "converted" file that forgot to actually convert anything should fail verification
            var problems = Pipeline.Verify(data, data);
            Assert.Contains(problems, p => p.Contains("Utf8"));
        }

        [Fact]
        public void FullConvert_EndToEnd_Synthetic()
        {
            var data = MinimalSyntheticFile(new[] { "MB LookSuite3", "S_Sharpen" });
            var result = Pipeline.Convert(data, "cs5.5", new HashSet<string> { "MB LookSuite3" });
            Assert.Single(result.RemovedEffects);
            Assert.Equal("MB LookSuite3", result.RemovedEffects[0]);

            var problems = Pipeline.Verify(data, result.Data);
            Assert.Empty(problems);
        }

        [Fact]
        public void FullConvert_EndToEnd_RealFixture()
        {
            var path = Path.Combine(FixturesDir, "sample_1.ffx");
            var data = File.ReadAllBytes(path);
            var result = Pipeline.Convert(data, "cs5.5");
            var problems = Pipeline.Verify(data, result.Data);
            Assert.Empty(problems);
        }

        // --- PresetInspector pairing regression (the "empty Effect Controls" bug) ---
        // Inspect used to pair the RAW tdsp list (sentinel included) against
        // the sspc list. The sentinel has no sspc of its own, so whenever it
        // wasn't the last index entry every effect read the NEXT effect's
        // parameters and the last effect vanished — a single-effect preset
        // decoded to nothing at all ("it doesn't show anything").

        [Fact]
        public void Inspect_SentinelLast_PairsEveryEffect()
        {
            var data = MinimalSyntheticFile(new[] { "S_Sharpen", "ADBE Exposure2" });
            var details = PresetInspector.Inspect(data);
            Assert.Equal(2, details.Count);
            Assert.Equal("S_Sharpen", details[0].MatchName);
            Assert.Equal("ADBE Exposure2", details[1].MatchName);
        }

        [Fact]
        public void Inspect_SentinelFirst_StillPairsEveryEffect()
        {
            var data = MinimalSyntheticFile(new[] { "S_Sharpen", "ADBE Exposure2" }, sentinelLast: false);
            var details = PresetInspector.Inspect(data);
            Assert.Equal(2, details.Count);
            // the short name proves the sspc pairing didn't shift: each
            // effect must read its OWN sspc's fnam, not the neighbor's
            Assert.Equal("S_Sharpen", details[0].MatchName);
            Assert.Equal("S_Sharpen", details[0].ShortName);
            Assert.Equal("ADBE Exposure2", details[1].MatchName);
            Assert.Equal("ADBE Exposure2", details[1].ShortName);
        }

        [Fact]
        public void Inspect_SingleEffectSentinelFirst_ReturnsTheEffect()
        {
            // the reported "shows nothing" case: one effect, sentinel first
            var data = MinimalSyntheticFile(new[] { "S_Sharpen" }, sentinelLast: false);
            var details = PresetInspector.Inspect(data);
            Assert.Single(details);
            Assert.Equal("S_Sharpen", details[0].MatchName);
        }

        [Fact]
        public void Inspect_ParametersPairWithTheirOwnEffect_SentinelFirst()
        {
            var data = TwoEffectFileSentinelFirst(42.5, -7.25);
            var details = PresetInspector.Inspect(data);
            Assert.Equal(2, details.Count);

            Assert.Equal("S_Sharpen", details[0].MatchName);
            Assert.Single(details[0].Parameters);
            Assert.Equal("Sharpen Amount", details[0].Parameters[0].Name);
            Assert.Equal(42.5, details[0].Parameters[0].StaticValue ?? double.NaN, 9);

            Assert.Equal("ADBE Exposure2", details[1].MatchName);
            Assert.Single(details[1].Parameters);
            Assert.Equal("Exposure", details[1].Parameters[0].Name);
            Assert.Equal(-7.25, details[1].Parameters[0].StaticValue ?? double.NaN, 9);
        }
    }
}
