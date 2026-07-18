using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FfxTool.Core
{
    /// <summary>
    /// The CS5.5-downgrade conversion pipeline — a 1:1 port of
    /// ffx_core/pipeline.py. Every step here was derived and verified
    /// against real .ffx sample files in the Python version first; see
    /// RESEARCH_NOTES.md for the full derivation and the three mistakes
    /// made along the way. Do not "simplify" any of these steps without
    /// re-testing against real files.
    ///
    /// Rules this class always follows (unchanged from the Python version):
    ///   - Keyframe data (lhd3/ldat) and third-party plugin blobs are NEVER
    ///     modified, under any circumstance.
    ///   - Every conversion ends with a verification pass (Verify()) before
    ///     the caller is allowed to treat the output as done.
    /// </summary>
    public static class Pipeline
    {
        // Confirmed version-byte values for the `head` chunk's 2nd uint32
        // field. Only CS5.5 has been confirmed against a real native
        // sample so far. To add a new target: get a same-preset pair (one
        // CC-saved, one saved natively in the target version), diff their
        // `head` chunks, and add the confirmed value here — do not guess.
        public static readonly Dictionary<string, uint> KnownVersions = new Dictionary<string, uint>
        {
            { "cs5.5", 78 },
        };

        public const int FnamFixedSize = 48; // CS5.5's fixed-width field size for `fnam` chunks

        static readonly byte[] TDMN = RiffFile.Cid("tdmn");
        static readonly byte[] TDIX = RiffFile.Cid("tdix");
        static readonly byte[] TDSN = RiffFile.Cid("tdsn");
        static readonly byte[] PDNM = RiffFile.Cid("pdnm");
        static readonly byte[] FNAM = RiffFile.Cid("fnam");
        static readonly byte[] LHD3 = RiffFile.Cid("lhd3");
        static readonly byte[] LDAT = RiffFile.Cid("ldat");
        static readonly byte[] HEAD = RiffFile.Cid("head");
        static readonly byte[] TDSP_FORM = RiffFile.Cid("tdsp");
        static readonly byte[] SSPC_FORM = RiffFile.Cid("sspc");
        static readonly byte[] UTF8_TAG = RiffFile.Cid("Utf8");

        public class ConversionResult
        {
            public byte[] Data;
            public List<string> RemovedEffects = new List<string>();
            public List<string> Warnings = new List<string>();
        }

        public class EffectInfo
        {
            public string MatchName; // null for the sentinel entry
            public bool IsSentinel;
        }

        static byte[] DecodeUtf8Prefixed(byte[] raw)
        {
            // Strip CC's "Utf8" + 4-byte-length prefix, returning the raw
            // string bytes. If `raw` isn't prefixed this way, return it
            // unchanged — some files may already be in the target format.
            if (raw.Length >= 8 && raw[0] == 'U' && raw[1] == 't' && raw[2] == 'f' && raw[3] == '8')
            {
                uint strlen = (uint)((raw[4] << 24) | (raw[5] << 16) | (raw[6] << 8) | raw[7]);
                if (8 + strlen <= raw.Length)
                {
                    var result = new byte[strlen];
                    Array.Copy(raw, 8, result, 0, strlen);
                    return result;
                }
            }
            return raw;
        }

        static string BytesToNullTerminatedString(byte[] raw)
        {
            int nullIdx = Array.IndexOf(raw, (byte)0);
            int len = nullIdx >= 0 ? nullIdx : raw.Length;
            return Encoding.GetEncoding("ISO-8859-1").GetString(raw, 0, len);
        }

        static string TdmnEffectName(RiffNode tdspNode)
        {
            var tdmns = RiffFile.FindAll(tdspNode, TDMN);
            if (tdmns.Count < 2) return null; // sentinel entry only has 1 tdmn
            return BytesToNullTerminatedString(tdmns[1].Content);
        }

        static string FnamEffectName(RiffNode sspcNode)
        {
            var fnam = sspcNode.Children.First(c => SequenceEqual(c.Cid, FNAM));
            var decoded = DecodeUtf8Prefixed(fnam.Content);
            return BytesToNullTerminatedString(decoded);
        }

        static bool SequenceEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static RiffNode FindBesc(RiffNode riffNode)
        {
            var besc = riffNode.Children[1];
            if (besc.Form == null || !SequenceEqual(besc.Form, RiffFile.Cid("besc")))
                throw new InvalidOperationException("Expected the file's 2nd top-level chunk to be `LIST besc`.");
            return besc;
        }

        /// <summary>
        /// Return every effect in a .ffx file: match-name and whether it's
        /// the harmless sentinel entry. Does not modify anything.
        /// </summary>
        public static List<EffectInfo> ListEffects(byte[] data)
        {
            var tree = RiffFile.ParseFile(data);
            var besc = FindBesc(tree);
            var result = new List<EffectInfo>();

            foreach (var c in besc.Children)
            {
                if (c.IsContainer && SequenceEqual(c.Cid, RiffFile.Cid("LIST")) && SequenceEqual(c.Form, TDSP_FORM))
                {
                    var name = TdmnEffectName(c);
                    result.Add(new EffectInfo { MatchName = name, IsSentinel = name == null });
                }
            }
            return result;
        }

        /// <summary>
        /// Preferred effect-removal function. Removes tdsp+tdsn index
        /// entries AND their corresponding sspc data blocks, matched by
        /// ORDER — which is how AE itself associates them via tdix. See
        /// RESEARCH_NOTES.md: matching by name alone is unreliable since
        /// short names like "Looks" aren't unique across vendors.
        /// </summary>
        public static List<string> RemoveEffectsByMatchName(RiffNode riffNode, HashSet<string> matchNamesToRemove)
        {
            var besc = FindBesc(riffNode);
            var children = besc.Children;

            var tdspOrder = children.Where(c => c.IsContainer && SequenceEqual(c.Form, TDSP_FORM)).ToList();
            var sspcOrder = children.Where(c => c.IsContainer && SequenceEqual(c.Form, SSPC_FORM)).ToList();
            var realTdsp = tdspOrder.Where(c => TdmnEffectName(c) != null).ToList();

            if (realTdsp.Count != sspcOrder.Count)
                throw new InvalidOperationException(
                    $"Effect index count ({realTdsp.Count}) doesn't match parameter block count " +
                    $"({sspcOrder.Count}) — file structure is not what this pipeline expects; aborting rather than guessing.");

            var sspcToRemove = new HashSet<RiffNode>();
            var removedNames = new List<string>();

            for (int i = 0; i < realTdsp.Count; i++)
            {
                var name = TdmnEffectName(realTdsp[i]);
                if (matchNamesToRemove.Contains(name))
                {
                    sspcToRemove.Add(sspcOrder[i]);
                    removedNames.Add(name);
                }
            }

            var newChildren = new List<RiffNode>();
            bool skipNextTdsn = false;

            foreach (var c in children)
            {
                if (skipNextTdsn && SequenceEqual(c.Cid, TDSN))
                {
                    skipNextTdsn = false;
                    continue;
                }
                if (c.IsContainer && SequenceEqual(c.Form, TDSP_FORM))
                {
                    var name = TdmnEffectName(c);
                    if (name != null && matchNamesToRemove.Contains(name))
                    {
                        skipNextTdsn = true;
                        continue;
                    }
                }
                if (c.IsContainer && SequenceEqual(c.Form, SSPC_FORM) && sspcToRemove.Contains(c))
                {
                    continue;
                }
                newChildren.Add(c);
            }

            besc.Children = newChildren;
            return removedNames;
        }

        /// <summary>
        /// Renumber every non-sentinel tdsp entry's tdix[1] to be
        /// contiguous 0..N-1, in order. Must be called after any effect
        /// removal — AE uses this index to associate an effect entry with
        /// its parameter block, and a gap causes wrong names/parameters to
        /// display (not always a crash — this was the "Utf1/Utf2" bug's
        /// real cause before the string-encoding issue was found too).
        /// </summary>
        public static int RenumberIndices(RiffNode riffNode)
        {
            var besc = FindBesc(riffNode);
            var tdsps = besc.Children.Where(c => c.IsContainer && SequenceEqual(c.Form, TDSP_FORM)).ToList();
            uint idx = 0;

            foreach (var n in tdsps)
            {
                if (TdmnEffectName(n) == null) continue; // sentinel — leave untouched
                var tdixs = RiffFile.FindAll(n, TDIX);
                tdixs[1].Content = new byte[]
                {
                    (byte)((idx >> 24) & 0xFF), (byte)((idx >> 16) & 0xFF),
                    (byte)((idx >> 8) & 0xFF), (byte)(idx & 0xFF)
                };
                idx++;
            }
            return (int)idx;
        }

        static void StripVariableUtf8(RiffNode node, byte[] cidTarget)
        {
            if (node.IsContainer)
            {
                foreach (var c in node.Children)
                    StripVariableUtf8(c, cidTarget);
            }
            else if (SequenceEqual(node.Cid, cidTarget))
            {
                var raw = node.Content;
                if (raw.Length >= 4 && raw[0] == 'U' && raw[1] == 't' && raw[2] == 'f' && raw[3] == '8')
                {
                    var decoded = DecodeUtf8Prefixed(raw);
                    var result = new byte[decoded.Length + 1];
                    Array.Copy(decoded, result, decoded.Length);
                    result[decoded.Length] = 0;
                    node.Content = result;
                }
            }
        }

        /// <summary>
        /// Convert tdsn/pdnm/fnam from CC's Utf8-prefixed encoding to the
        /// target's native plain-string format. Safe to call even if a
        /// file is already in the target format (no-op for chunks that
        /// aren't prefixed).
        /// </summary>
        public static void ConvertStringsToTargetFormat(RiffNode riffNode)
        {
            // tdsn and pdnm: strip prefix, keep as variable-length null-terminated
            StripVariableUtf8(riffNode, TDSN);
            StripVariableUtf8(riffNode, PDNM);

            // fnam: strip prefix AND pad/truncate to the fixed 48-byte
            // field CS5.5 expects. This is NOT the same treatment as
            // tdsn/pdnm — fnam sits at a fixed offset inside sspc and
            // leaving it variable-length shifts every field after it,
            // which crashes AE. This was Mistake #3 in the Python
            // derivation — do not "simplify" this back to a uniform strip.
            foreach (var sspcNode in RiffFile.FindAllLists(riffNode, SSPC_FORM))
            {
                var fnamNodes = sspcNode.Children.Where(c => SequenceEqual(c.Cid, FNAM)).ToList();
                if (fnamNodes.Count == 0) continue;
                var raw = fnamNodes[0].Content;
                if (raw.Length >= 4 && raw[0] == 'U' && raw[1] == 't' && raw[2] == 'f' && raw[3] == '8')
                {
                    var name = DecodeUtf8Prefixed(raw);
                    var padded = new byte[FnamFixedSize];
                    int copyLen = Math.Min(name.Length, FnamFixedSize - 1);
                    Array.Copy(name, padded, copyLen);
                    // remaining bytes are already 0 (C# array default)
                    fnamNodes[0].Content = padded;
                }
            }
        }

        /// <summary>Patch the head chunk's version field to a known target version.</summary>
        public static void PatchVersion(RiffNode riffNode, string target)
        {
            if (!KnownVersions.ContainsKey(target))
                throw new ArgumentException(
                    $"No confirmed version-byte value for target '{target}'. " +
                    $"Known targets: {string.Join(", ", KnownVersions.Keys.OrderBy(k => k))}. " +
                    $"See RESEARCH_NOTES.md for how to derive a new one — do not guess.");

            var headNode = riffNode.Children[0];
            if (!SequenceEqual(headNode.Cid, HEAD))
                throw new InvalidOperationException("Expected the file's 1st top-level chunk to be `head`.");

            var content = headNode.Content;
            uint version = KnownVersions[target];
            content[4] = (byte)((version >> 24) & 0xFF);
            content[5] = (byte)((version >> 16) & 0xFF);
            content[6] = (byte)((version >> 8) & 0xFF);
            content[7] = (byte)(version & 0xFF);
        }

        /// <summary>
        /// The mandatory post-conversion safety pass. Returns a list of
        /// problems found (empty list = all checks passed). Never skip this.
        /// </summary>
        public static List<string> Verify(byte[] originalData, byte[] convertedData)
        {
            var problems = new List<string>();
            var newTree = RiffFile.ParseFile(convertedData);

            // 1. No remaining Utf8-tagged chunks anywhere
            int utf8Remaining = CountUtf8(newTree);
            if (utf8Remaining > 0)
                problems.Add($"{utf8Remaining} chunk(s) still carry the Utf8 prefix.");

            // 2. tdix values are contiguous starting at 0
            var besc = FindBesc(newTree);
            var tdsps = besc.Children.Where(c => c.IsContainer && SequenceEqual(c.Form, TDSP_FORM)).ToList();
            var seenIndices = new List<uint>();
            foreach (var n in tdsps)
            {
                if (TdmnEffectName(n) == null) continue;
                var tdixs = RiffFile.FindAll(n, TDIX);
                var b = tdixs[1].Content;
                uint val = (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
                seenIndices.Add(val);
            }
            bool contiguous = true;
            for (int i = 0; i < seenIndices.Count; i++)
                if (seenIndices[i] != i) { contiguous = false; break; }
            if (!contiguous)
                problems.Add($"tdix values are not contiguous: {string.Join(",", seenIndices)}");

            // 3. Keyframe data (lhd3/ldat) unchanged wherever it still
            // exists (removed effects legitimately remove their own
            // keyframes — we only check surviving chunks are untouched).
            var origTree = RiffFile.ParseFile(originalData);
            var origLhd3 = new HashSet<string>(RiffFile.FindAll(origTree, LHD3).Select(c => System.Convert.ToBase64String(c.Content)));
            var newLhd3 = RiffFile.FindAll(newTree, LHD3).Select(c => System.Convert.ToBase64String(c.Content));
            if (newLhd3.Any(h => !origLhd3.Contains(h)))
                problems.Add("Keyframe header (lhd3) data changed unexpectedly.");

            var origLdat = new HashSet<string>(RiffFile.FindAll(origTree, LDAT).Select(c => System.Convert.ToBase64String(c.Content)));
            var newLdat = RiffFile.FindAll(newTree, LDAT).Select(c => System.Convert.ToBase64String(c.Content));
            if (newLdat.Any(h => !origLdat.Contains(h)))
                problems.Add("Keyframe data (ldat) changed unexpectedly.");

            return problems;
        }

        static int CountUtf8(RiffNode node)
        {
            int count = 0;
            if (node.IsContainer)
            {
                foreach (var c in node.Children) count += CountUtf8(c);
            }
            else if (node.Content.Length >= 4 && node.Content[0] == 'U' && node.Content[1] == 't' &&
                     node.Content[2] == 'f' && node.Content[3] == '8')
            {
                count++;
            }
            return count;
        }

        /// <summary>
        /// Run the full pipeline: optional effect removal, index
        /// renumbering, string-format conversion, version patch,
        /// re-serialize, verify.
        /// </summary>
        public static ConversionResult Convert(byte[] data, string target = "cs5.5", HashSet<string> removeMatchNames = null)
        {
            var tree = RiffFile.ParseFile(data);
            var result = new ConversionResult();

            if (removeMatchNames != null && removeMatchNames.Count > 0)
            {
                result.RemovedEffects = RemoveEffectsByMatchName(tree, removeMatchNames);
                var missing = removeMatchNames.Except(result.RemovedEffects).ToList();
                if (missing.Count > 0)
                    result.Warnings.Add($"Requested removal of effects not found in file: {string.Join(", ", missing)}");
            }

            RenumberIndices(tree);
            ConvertStringsToTargetFormat(tree);
            PatchVersion(tree, target);

            var outBytes = RiffFile.Serialize(tree);
            var problems = Verify(data, outBytes);
            if (problems.Count > 0)
                throw new InvalidOperationException(
                    "Conversion failed its verification pass:\n  - " + string.Join("\n  - ", problems));

            result.Data = outBytes;
            return result;
        }
    }
}
